# Coolify deployment

Використовуйте Docker Compose application із файлом `deploy/compose.yaml`.
Build context — корінь репозиторію. Конфігурація має окремі images для API,
Mini App, aiogram і Caddy gateway, PostgreSQL та одноразовий migrate.

У Coolify задайте **Base Directory `/`**, **Docker Compose Location
`/deploy/compose.yaml`**. Coolify передає корінь checkout як
`--project-directory`, тому `build.context: .` рахується від кореня
репозиторію. Dockerfile paths
рахуються від build context. Локальні Compose-команди нижче запускайте з
кореня з `--project-directory .`, щоб отримати той самий результат.
`context: ..` у цьому режимі виходить за межі checkout і спричиняє
`lstat /artifacts/src: no such file or directory`.

Gateway збирається через [gateway.Dockerfile](../deploy/gateway.Dockerfile):
[Caddyfile](../deploy/Caddyfile) копіюється в image під час збірки.
Bind mount цього файла з deployment-хоста не потрібний: checkout у helper
container не гарантує наявності файла за відповідним шляхом на Docker host.
Після зміни routing перебудуйте й перевипустіть gateway image. Якщо раніше
Coolify створив помилковий каталог `deploy/Caddyfile`, новий Compose його
більше не монтує; прибирати його для цього виправлення не потрібно.

## Налаштування

1. Задайте змінні з [.env.example](../.env.example) у Coolify secrets.
   `APP_ORIGIN` — точний HTTPS origin без кінцевого `/`, шляху або query.
   Пароль PostgreSQL має бути випадковим; у цьому Compose він включається
   до Npgsql connection string, тому використовуйте hex/base64url без `;`.
   Username бота не задається: backend отримує його через Telegram `getMe`
   за `TELEGRAM_BOT_TOKEN` під час першого запиту `/api/config` і кешує до
   перезапуску. Після зміни username у BotFather перезапустіть backend.
   Якщо Telegram недоступний або token невалідний, цей endpoint повертає
   503; запуск сервісів і міграції не потребують виклику `getMe`.
   ID AI-моделей уже задані в
   [appsettings.json](../src/backend/ExpenseTracker.Api/appsettings.json):
   `openai/gpt-6-luna` для тексту, `google/gemini-3.1-flash-lite` для STT.
   У Coolify задайте лише `OPENROUTER_API_KEY`; старі model env-змінні
   видаліть, включно з прямими `OpenRouter__TextModel`/`OpenRouter__AudioModel`,
   щоб вони не перевизначали серверний конфіг.
2. Призначте домен **тільки gateway**, порт `8080`, HTTPS через Coolify proxy.
   Не призначайте domains backend/bot/frontend/postgres і не відкривайте їх
   порти на хості. Caddy маршрутизує `/api/*` і `/telegram/webhook`, повертає
   404 для `/internal/*`, решту передає Next.js.
3. Приватна мережа має subnet `172.30.19.0/24`, gateway — `172.30.19.2`.
   Автоматичні адреси виділяються з `172.30.19.128/25`, окремо від gateway.
   За конфлікту змініть subnet, `ipv4_address` і `App__KnownProxies__0`
   узгоджено. Backend довіряє forwarded headers лише цій адресі.
   Caddy звертається до API через `expense-tracker-api-private:8080` — alias
   backend, заданий **лише** в мережі `private`. Не замінюйте його на
   `backend:8080`: Coolify додає також application network, де ім'я
   `backend` може резолвитися в іншу адресу. Тоді gateway з'єднується з API
   зі своєї іншої, недовіреної адреси, і backend ігнорує HTTPS-заголовок.
   Довірчий периметр — Coolify proxy + gateway; gateway не має бути
   доступним напряму з інтернету. TLS завершується перед gateway.
4. Налаштуйте backup PostgreSQL поза хостом і захищений persistent volume
   `data-protection`. Доступ до цього volume дає доступ до ключів сесій;
   обмежте його права й шифруйте backups/disk засобами інфраструктури.
5. Перевірте конфігурацію та зберіть images:

   ```bash
   docker compose --project-directory . --env-file .env -f deploy/compose.yaml config --quiet
   docker compose --project-directory . --env-file .env -f deploy/compose.yaml build
   ```

`migrate` чекає health PostgreSQL, застосовує початкову міграцію й завершується.
Backend стартує лише після `service_completed_successfully`. Перед наступним
релізом переконайтеся, що Coolify **повторно запускає migrate**, а не використовує
його старий завершений контейнер. Якщо deployment engine не підтримує gate,
виконайте `docker compose ... run --rm migrate` окремим release job до backend.
Не запускайте міграції конкурентно на replicas. Перший запуск: одна backend
і одна bot replica; inbox worker обробляє updates послідовно.

## Перехід до витрат без фінансових рахунків

Release з `RemoveFinancialAccounts` потребує узгодженого оновлення backend,
бота й frontend. Перед ним зробіть backup PostgreSQL та зупиніть старі
backend/bot, щоб вони не працювали зі схемою під час її зміни. Застосуйте
міграції через `migrate`, потім запустіть нові сервіси. Не використовуйте
rolling release старої й нової версій разом для цієї зміни.

Міграція переносить валюту чернетки з колишнього рахунку, а за його відсутності
— з профілю, та видаляє каталог фінансових рахунків і пов'язані поля.
Витрати з їхніми сумами/валютами, категорії, профілі й записи виконаного
`/seed` зберігаються. Повторний seed після оновлення не дублює дані.
Версії чернеток збільшуються; bot conversation повертається до головного
меню. Після release повторно відкрийте Mini App і надішліть `/start` у боті.
Старі кнопки прибраного каталогу повертають повідомлення про застарілу дію.

Старий API-контракт несумісний: поля `accountId`, `defaultAccountId`,
маршрути рахунків і фільтр прибрані; зміна/підтвердження витрати передає
`currency`. Зворотна міграція явно відхиляється, бо назви й зв'язки видаленого
каталогу неможливо відновити. Для повного rollback потрібен backup перед
release разом із відповідними старими images; image rollback окремо недостатній.

## Telegram

У BotFather налаштуйте HTTPS Mini App для тестового бота. Після перевірки
маршрутів зареєструйте webhook **окремою явною дією**:

```bash
docker compose --project-directory . --env-file .env -f deploy/compose.yaml run --rm -e LOG_SERVICE=bot-register bot \
  python -m expense_tracker_bot.main register-webhook
```

Команда використовує окремий webhook secret, реєструє лише message і
callback_query, задає menu Web App та зберігає pending updates. Не запускайте
її для production бота з локального середовища. Один бот має один webhook.
Окрема назва `bot-register` дає one-shot команді власний log file у bot volume.

Для входу відкривайте Mini App кнопкою **під повідомленням** бота або
menu button **Lime** біля поля введення. Reply `web_app` запуск не передає
підписаного `initData`. Після оновлення бота надішліть `/start`, щоб він
замінив стару reply keyboard й показав правильну inline-кнопку.
Пункт «Відкрити застосунок» у новій reply keyboard спершу надсилає таку кнопку.

Якщо в логах є лише `401 /api/auth/me` і `200 /api/config`, але немає
`/api/auth/csrf` та `POST /api/auth/telegram`, login ще не був надісланий:
перевірте спосіб відкриття Mini App. `204` у `/queues/*/claim` означає
порожню чергу, а не помилку бота. Не копіюйте `initData` в URL або logs.

## Smoke після release

- `/api/health/ready` → 200; `/internal/bot/actor/profile` через public origin → 404.
- `GET /api/auth/csrf` через public HTTPS origin → 200, CSRF cookie має
  `Secure`. `500` із `DefaultAntiforgery.CheckSSLConfig` означає, що API
  бачить HTTP: перевірте private alias та адресу gateway у trusted proxies.
- `/telegram/webhook` без secret → 403; непідтримувані updates не розкривають даних.
- Mini App: Telegram login, Secure/HttpOnly cookie, CSRF, повторне відкриття,
  logout і відмова доступу після нього. Перевірте реальні Telegram клієнти.
- Текст і голос → чернетка з правильною локальною датою → підтвердження;
  повторне натискання не додає запис. Та сама витрата видима в іншому каналі.
- Старі callbacks і паралельне редагування повертають conflict; чернетки
  живуть 30 хвилин. Обидві мови й теми, уже відкритий Mini App після оновлення.

### Якщо CSRF падає через HTTPS після Coolify proxy

Перевірте мережі актуальних gateway/backend контейнерів, не виводячи env:

```bash
sudo docker ps --format '{{.Names}}'
sudo docker inspect --format '{{.Name}} {{json .NetworkSettings.Networks}}' GATEWAY_CONTAINER BACKEND_CONTAINER
sudo docker exec GATEWAY_CONTAINER nslookup expense-tracker-api-private
```

У private network gateway повинен мати `172.30.19.2`, backend — alias
`expense-tracker-api-private`; DNS-відповідь із gateway повинна містити
тільки private IP backend. Додаткова application network від Coolify допустима;
її адреси не треба додавати до `App__KnownProxies__0`.
Для цієї правки потрібні оновлена Compose-конфігурація backend із alias та
перебудований gateway image з новим Caddyfile. Простого restart недостатньо.
Після redeploy відкрийте Mini App знову та перевірте CSRF → Telegram login.
Не вимикайте Secure cookies або CSRF і не дозволяйте forwarded headers
від усіх адрес для обходу цієї помилки.

## Експлуатація та rollback

Логи всіх сервісів надходять у Coolify Logs і persistent volumes із щоденною
UTC gzip-ротацією. Налаштування retention, шляхи, копіювання та межі
надійності описані в [інструкції логування](logging.md).

AI: 2000 символів тексту/транскрипту, до 10 МБ / 120 секунд аудіо,
до 10 запитів/хвилину на користувача, загальний timeout AI-обробки 75 с,
до 1000 output tokens на provider-запит. Голос використовує два послідовні
запити: STT → текстовий розбір. Luna працює без reasoning і без несумісного
`temperature`; Flash-Lite — із minimal reasoning. Вимкніть logging payloads.
Ліміт витрат ключа задається також у OpenRouter; це межа загального бюджету.
Каталог першої версії — до 50 категорій на користувача з архівом.
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

## Оновлення ручного та AI-введення

Міграція `AddInputMode` додає до `Users` поле режиму з початковим `ai`.
Вона не змінює витрат, категорій, згоди на AI чи наявних чернеток.
Після backup застосуйте штатним migration job усі міграції, включно з
цією, перед новим backend; оновіть frontend і bot images. Перевідкрийте
Mini App. Попередній перехід `RemoveFinancialAccounts` усе ще потребує
описаного вище узгодженого stop-first release, якщо він ще не застосований.

Поля профілю/запитів additive; пропущений `inputMode` під час збереження
налаштувань зберігає поточний режим. Пропущений `mode` створення чернетки
використовує режим профілю. Новий Mini App зберігає режим разом із request ID
для явного повтору невідомого результату. Старі чернетки й bot callbacks
не перерозбираються та проходять ті самі version/ownership/TTL перевірки.

Перед release перевірте ручне створення без AI-згоди/ключа в обох каналах,
зміну режиму між каналами, AI текст/голос із датою та без, «учора» у часовому
поясі профілю, неоднозначну дату й повтор після переходу через північ.
Build/DB/browser/live Telegram/OpenRouter/Coolify для цієї зміни ще не виконані.
Rollback до сумісних images може залишити additive колонку; Down видалить
лише вибір режиму. Зміни тут не є дозволом на deployment чи міграцію.

## Оновлення іконок категорій

`AddCategoryIcons` додає nullable text `Categories.Icon`; наявні назви,
витрати й категорії зберігаються. Після backup застосуйте штатним migration
job усі міграції включно з цією перед новим backend, потім оновіть bot і
frontend images та перевідкрийте Mini App. JSON-каталог іконок вбудований
у backend image, зовнішнього сервісу чи volume для нього немає.

API додає `icon` і `/category-icons` для обох каналів. Старі клієнти можуть
пропускати `icon`: backend зберігає вибір; без збереженого значення повертає
системну іконку. Старі inline-кнопки захищені версіями. Перед rollback bot
image завершіть або скиньте нові діалоги `category_icon` через головне меню.
Колонку можна залишити при image rollback; Down видалить лише вибрані іконки.

Перед release потрібні перевірка міграції на PostgreSQL, створення/зміна
іконки в обох каналах, перейменування, архівація, старі callbacks і запити
без icon. Build, тести, browser, реальний Telegram і Coolify для цієї зміни
не запускалися; міграція не застосовувалася.

## Видалення в рядках списків

Оновлення додає `DELETE /api/categories/{id}?version=…` та відповідний
приватний bot endpoint, без нової схеми БД. Спочатку оновіть backend, потім
frontend/bot і перевідкрийте Mini App. Видалення доступне поруч із записом;
категорії без дій архівації/відновлення. Старі archive callbacks новий бот
відхиляє; старий API з полем `archived` зберігається для сумісності.

Пов'язані витрати й активні чернетки блокують видалення категорії з
`category_in_use`; каскаду немає. Потрібні ownership/version/CSRF,
PostgreSQL і UI перевірки перед release; у цій задачі вони не запускалися.
Image rollback не відновлює фізично видалені категорії/витрати. Ці зміни
не є дозволом на deployment або видалення реальних даних.

## Місячні ліміти категорій

`AddCategoryLimits` додає nullable `Categories.MonthlyLimit` (`numeric(20,4)`)
та `LimitCurrency` із check constraint: обидва null або додатний ліміт із
валютою. Наявні категорії починають без ліміту, витрати не переписуються.
Після backup застосуйте штатним migration job усі міграції включно з цією
перед новим backend, потім оновіть frontend/bot і перевідкрийте Mini App.

API додає `CatalogItemDto.limit` і `PUT /categories/{id}/limit` для обох
каналів. Ліміт змінюється лише окремим versioned запитом; старі запити
назви/іконки його зберігають. Null amount/currency прибирають ліміт.
Прогрес рахується backend за поточним місяцем/timezone профілю, категорією
та точною валютою; старі клієнти можуть ігнорувати additive поле.

Перед release перевірте міграцію на PostgreSQL, timezone на межі місяця,
точність сум, інші валюти/чернетки, зміни й видалення витрат, ownership та
конфлікти обох каналів. Runtime/DB/browser/реальний Telegram/Coolify в цій
задачі не запускалися, міграція не застосовувалася. Перед rollback старого
бота завершіть або скиньте `category_limit_currency`/`category_limit` діалоги.
Колонки можна залишити при image rollback; Down видаляє лише налаштування
лімітів, не витрати. Документ не надає дозволу на deployment чи міграції.
