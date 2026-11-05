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
Витрати з їхніми сумами/валютами, категорії, профілі й історичні демодані
зберігаються. Версії чернеток збільшуються; legacy conversation скидається
міграцією, але чинний бот діалоги більше не читає. Після release повторно
відкрийте Mini App. Старі callback-кнопки не виконують дій.

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

Команда використовує окремий webhook secret, реєструє лише `message` та
зберігає pending updates. Також видаляє command menus у default/all-private
scopes для default/uk/en і скидає додатковий menu Web App до Telegram default.
Довільні персональні scopes, створені поза застосунком, не змінюються. Не запускайте
її для production бота з локального середовища. Один бот має один webhook.
Окрема назва `bot-register` дає one-shot команді власний log file у bot volume.

Для входу відкривайте Web Apps однією з двох кнопок **під повідомленням**:
«💸 Витрати» або «✅ Завдання». Reply `web_app` не передає підписаний initData.
Будь-яке приватне повідомлення прибере стару reply keyboard й покаже дві
inline-кнопки без команд чи проміжного меню.

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
- У Web App текст і голос → чернетка з правильною локальною датою →
  підтвердження; повтор не додає запис. Перевірте другу browser session.
- Бот на текст/голос/стару команду повертає тільки дві inline кнопки;
  callbacks не виконують дій. Обидві кнопки відкривають signed Telegram auth.
- Паралельне редагування повертає conflict; чернетки живуть 30 хвилин.
  Обидві мови й теми, уже відкриті Web Apps після оновлення.

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

Inbox: lease 3 хвилини, до 8 спроб, затримка після помилки 30 секунд.
Повторне надсилання Telegram може дублювати **повідомлення**, фінансові
операції мають окрему ідемпотентність. Слідкуйте за health, delivery errors,
кількістю незавершених `Deliveries` та рядками `Attempts >= 8` у приватній БД.
За 24 години необроблені payloads очищаються; фінансові дані зберігаються.
Completed deliveries і idempotency records потребують окремої retention
політики перед довготривалою production експлуатацією. Автоматичної повторної
публікації failed jobs немає; спочатку встановіть результат операції.

`RELEASE_TAG` позначає локальні images. Rollback — зупинити нові workers,
повернути сумісні images, перевірити readiness й обидва Web Apps. Image rollback
не скасовує міграцію. Початкова міграція створює всі таблиці; її Down видаляє
дані, тому не застосовуйте його як штатний rollback. Перед schema changes
потрібна перевірена резервна копія; надалі використовуйте expand/contract.

## Перехід до бота лише з двома кнопками

Нової схеми БД чи env для цього переходу немає. Зупиніть старий bot worker,
узгоджено оновіть backend і bot images: actor/workspace/conversation/seed
routes видалені, outbox worker відсутній. Старий бот несумісний із новим API.
Frontend фінансів і завдань продовжує працювати через `/api/*` та ту саму
cookie/CSRF модель. Service key бота лишається для inbox без actor privileges.

Після release виконайте явний register-webhook вище, щоб Telegram прибрав
старі command menus. Наступне private message користувача прибере стару
reply keyboard. Старі queued messages отримають тільки меню; callbacks
будуть завершені без дій, outbox не доставляється. Legacy Conversation,
DemoSeed і старі outbox записи зберігаються без активного використання.
Image rollback потребує сумісної пари backend/бота та перегляду queued outbox
перед поверненням старого worker, який може надіслати старі сповіщення.

## Міграції функцій Web Apps

Перед новим backend застосуйте штатним migrate job усі pending migrations
на БД із придатним backup. Цей опис не означає, що міграції вже виконані.
Якщо RemoveFinancialAccounts ще не застосований, чинний stop-first порядок
і обмеження rollback вище залишаються обов'язковими.

| Міграція | Зміни й сумісність |
| --- | --- |
| AddInputMode | Users.inputMode із початковим ai; згода на AI і витрати незмінні. Down видаляє лише вибір режиму. |
| AddCategoryIcons | Nullable Categories.Icon; JSON-каталог вбудований у backend. Down видаляє лише вибір іконок. |
| AddCategoryLimits | MonthlyLimit numeric(20,4) і legacy LimitCurrency з pair/positive check. Down видаляє налаштування, не витрати. |
| AddSavingsAccounts | Окремі SavingsAccounts/SavingsEntries без зв'язків зі звичайними витратами. Image rollback лишає таблиці; Down відхиляє видалення фінансового журналу. |
| AddRecurringTasks | RecurringTasks/TaskCompletions без змін фінансів. Image rollback лишає таблиці; Down відхиляє видалення історії. |

Валюта профілю визначає позначення витрат, чернеток, звітів, лімітів і
накопичень без перерахунку чисел. Legacy currency поля зберігаються для
сумісності, а не для перевизначення профілю. Старі витрати різних валют
підсумовуються разом як введені числа. Повернення старого backend поверне
старі правила групування, тому перевірте їх до rollback.

DELETE категорії захищає пов'язані витрати й активні чернетки через
category_in_use, ownership/version і Restrict FK. Image rollback не
відновлює фізично видалені категорії чи витрати. Ліміт категорії змінюється
окремим versioned запитом; rename/icon його зберігають. Прогрес — поточний
місяць/timezone профілю, без поділу за legacy валютами.

Накопичення: створення, append-only поповнення/списання, стабільний request
ID, конкурентний захист залишку. Невідома відповідь потребує явного повтору
збереженого запиту. Бот фінансові сценарії не обслуговує.

`/tasks` працює в чинному Next.js image через frontend catch-all;
`/api/tasks*` — через чинний backend route. Друга inline launch-кнопка
вказує на BOT_APP_URL + `/tasks`. Нових origin, tokens, secrets чи сервісів
немає. Розклади 1/2/3/7, timezone/DST, відмітки окремих дат, конкурентні
версії та tombstone/create replay перевіряються на backend.

Перед release за окремим запитом потрібні OpenAPI export/drift, Release
build/tests, frontend typecheck/lint/build, bot lint/typing/tests, реальна
PostgreSQL із міграцією, ownership/CSRF, точність грошей, timezone й невідомі
відповіді. Browser: 320/360/390/430 px і desktop, uk/en × light/dark.
Окремо — Telegram inline auth, cookie/CSRF через HTTPS і Coolify routing.
Ці runtime перевірки та міграції в задачі спрощення бота не виконувались.

## Доходи та категорії доходів

Міграція `20261105150655_AddIncomes` додає `IncomeCategories` і `Incomes`
без зміни наявних фінансових таблиць. Застосовуйте її окремим migrate job
після перевірки backup, перед новим backend і frontend. Нових env-змінних
немає. Бот продовжує відкривати `/` і `/tasks`; доходи доступні всередині
Mini App. Старі клієнти витрат сумісні. При rollback images залиште нові
таблиці й дані; Down навмисно відхилено для збереження фінансової історії.
Локальна перевірка на PostgreSQL 14 не підтверджує production PostgreSQL 17
або release у Coolify.

## Видалення накопичувальних рахунків

`20261105152920_AddSavingsDeletion` додає `Deleted=false` і `Version=1` до
наявних `SavingsAccounts`. Перед release перевірте backup й застосуйте
міграцію штатним migrate job. Зупиніть попередній backend до запуску нового:
старий код не інкрементує version і не приховує Deleted. Потім оновіть
frontend. Старий frontend може читати нові DTO з додатковим полем version.
Нових env-змінних і змін бота немає.

Після першого видалення rollback дозволено лише на backend, що підтримує
Deleted: старі images знову покажуть видалені рахунки та дозволять операції
з ними. Down навмисно відхилено. Локальні тести на PostgreSQL 14 не
підтверджують production PostgreSQL 17 або release у Coolify.
