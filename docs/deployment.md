# Coolify deployment

Використовуйте Docker Compose application із файлом `deploy/compose.yaml`.
Build context — корінь репозиторію. Конфігурація має окремі images для API,
Mini App і aiogram, PostgreSQL, одноразовий migrate та Caddy gateway.

## Налаштування

1. Задайте змінні з [.env.example](../.env.example) у Coolify secrets.
   `APP_ORIGIN` — точний HTTPS origin без кінцевого `/`, шляху або query.
   Пароль PostgreSQL має бути випадковим; у цьому Compose він включається
   до Npgsql connection string, тому використовуйте hex/base64url без `;`.
2. Призначте домен **тільки gateway**, порт `8080`, HTTPS через Coolify proxy.
   Не призначайте domains backend/bot/frontend/postgres і не відкривайте їх
   порти на хості. Caddy маршрутизує `/api/*` і `/telegram/webhook`, повертає
   404 для `/internal/*`, решту передає Next.js.
3. Приватна мережа має subnet `172.30.19.0/24`, gateway — `172.30.19.2`.
   Автоматичні адреси виділяються з `172.30.19.128/25`, окремо від gateway.
   За конфлікту змініть subnet, `ipv4_address` і `App__KnownProxies__0`
   узгоджено. Backend довіряє forwarded headers лише цій адресі.
   Довірчий периметр — Coolify proxy + gateway; gateway не має бути
   доступним напряму з інтернету. TLS завершується перед gateway.
4. Налаштуйте backup PostgreSQL поза хостом і захищений persistent volume
   `data-protection`. Доступ до цього volume дає доступ до ключів сесій;
   обмежте його права й шифруйте backups/disk засобами інфраструктури.
5. Перевірте конфігурацію та зберіть images:

   ```bash
   docker compose --env-file .env -f deploy/compose.yaml config --quiet
   docker compose --env-file .env -f deploy/compose.yaml build
   ```

`migrate` чекає health PostgreSQL, застосовує початкову міграцію й завершується.
Backend стартує лише після `service_completed_successfully`. Перед наступним
релізом переконайтеся, що Coolify **повторно запускає migrate**, а не використовує
його старий завершений контейнер. Якщо deployment engine не підтримує gate,
виконайте `docker compose ... run --rm migrate` окремим release job до backend.
Не запускайте міграції конкурентно на replicas. Перший запуск: одна backend
і одна bot replica; inbox worker обробляє updates послідовно.

## Telegram

У BotFather налаштуйте HTTPS Mini App для тестового бота. Після перевірки
маршрутів зареєструйте webhook **окремою явною дією**:

```bash
docker compose --env-file .env -f deploy/compose.yaml run --rm bot \
  python -m expense_tracker_bot.main register-webhook
```

Команда використовує окремий webhook secret, реєструє лише message і
callback_query, задає menu Web App та зберігає pending updates. Не запускайте
її для production бота з локального середовища. Один бот має один webhook.

## Smoke після release

- `/api/health/ready` → 200; `/internal/bot/actor/profile` через public origin → 404.
- `/telegram/webhook` без secret → 403; непідтримувані updates не розкривають даних.
- Mini App: Telegram login, Secure/HttpOnly cookie, CSRF, повторне відкриття,
  logout і відмова доступу після нього. Перевірте реальні Telegram клієнти.
- Текст і голос → чернетка з правильною локальною датою → підтвердження;
  повторне натискання не додає запис. Та сама витрата видима в іншому каналі.
- Старі callbacks і паралельне редагування повертають conflict; чернетки
  живуть 30 хвилин. Обидві мови й теми, уже відкритий Mini App після оновлення.

## Експлуатація та rollback

AI: 2000 символів тексту, до 10 МБ / 120 секунд аудіо, до 10 запитів/хвилину
на користувача, timeout 75 с, 1000 output tokens. Вимкніть logging payloads.
Ліміт витрат ключа задається також у OpenRouter; це межа загального бюджету.
Каталог першої версії — до 50 рахунків і 50 категорій на користувача з архівом.
Криптографічний proof: `Telegram__ProofMaxAgeSeconds=300` (60..600),
`Telegram__ProofFutureSkewSeconds=30` (0..60), перевірка значень при старті.
Cookie: idle 24 години, абсолютний строк 7 днів. Не змінюйте ці строки
без узгодженого оновлення security-контракту й тестів.

Inbox/outbox: lease 3 хвилини, до 8 спроб, затримка після помилки 30 секунд.
Повторне надсилання Telegram може дублювати **повідомлення**, фінансові
операції мають окрему ідемпотентність. Слідкуйте за health, delivery errors,
кількістю незавершених `Deliveries` та рядками `Attempts >= 8` у приватній БД.
За 24 години необроблені payloads очищаються; фінансові дані зберігаються.
Completed deliveries і idempotency records потребують окремої retention
політики перед довготривалою production експлуатацією. Автоматичної повторної
публікації failed jobs немає; спочатку встановіть результат операції.

`RELEASE_TAG` позначає локальні images. Rollback — зупинити нові workers,
повернути сумісні images, перевірити readiness й обидва канали. Image rollback
не скасовує міграцію. Початкова міграція створює всі таблиці; її Down видаляє
дані, тому не застосовуйте його як штатний rollback. Перед schema changes
потрібна перевірена резервна копія; надалі використовуйте expand/contract.
