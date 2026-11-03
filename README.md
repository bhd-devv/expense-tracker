# Lime — трекер витрат

Особисті витрати через Telegram Mini App та кнопковий Telegram-бот.
Режим у налаштуваннях: ручна форма або текст/голос → AI-чернетка.
Обидва сценарії зберігають витрату після підтвердження користувача.
Дата за замовчуванням — сьогодні за часовим поясом профілю; явна дата
у формі чи AI-описі має пріоритет, зокрема «учора» / «yesterday».
Ручне введення не потребує AI-згоди або OpenRouter key.

- ASP.NET Core / .NET 10, EF Core, PostgreSQL.
- Next.js 16 App Router, React, strict TypeScript; `uk`/`en`, light/dark/auto.
- Python / aiogram 3; reply-меню, inline-дії, durable inbox/outbox.
- OpenRouter на backend: текстовий розбір і транскрипція WAV-аудіо.
- Редагування/видалення витрат, фільтри, звіти у валюті профілю,
  створення/архівування категорій, налаштування в обох каналах.

## Почати локально

Потрібні SDK з [global.json](global.json), Node 22.14+, uv 0.12.5,
Python 3.14, PostgreSQL і ffmpeg. Версії бібліотек зафіксовано lock-файлами.

```bash
dotnet restore src/backend/ExpenseTracker.slnx --locked-mode
npm --prefix src/frontend ci
(cd src/bot && uv sync --frozen)
```

Створіть окрему порожню локальну PostgreSQL БД. Наприклад:

```bash
docker run -d --name lime-dev-db \
  -e POSTGRES_USER=expenses -e POSTGRES_PASSWORD=local-dev-only \
  -e POSTGRES_DB=expenses_dev -p 127.0.0.1:5432:5432 postgres:17.6-bookworm
export ConnectionStrings__Database='Host=127.0.0.1;Database=expenses_dev;Username=expenses;Password=local-dev-only'
export App__Origin=http://localhost:3000
export Telegram__BotToken='<окремий тестовий bot token>'
export Telegram__ServiceKey='<випадковий service key, щонайменше 32 символи>'
# Для AI додайте OpenRouter__ApiKey; моделі вже задані в appsettings.json.
dotnet run --project src/backend/ExpenseTracker.Api -- --environment Development --migrate
dotnet run --project src/backend/ExpenseTracker.Api -- --environment Development --urls http://127.0.0.1:5085
```

В іншому терміналі:

```bash
(cd src/frontend && API_URL=http://127.0.0.1:5085 npm run dev)
```

Вхід поза Telegram показує стартовий екран. Обхідної dev-авторизації немає.
Username бота backend отримує через Telegram `getMe` за bot token і кешує
до перезапуску. Для кнопки переходу до бота не потрібна окрема env-змінна.
Для живого Mini App потрібен HTTPS origin із доступом до API, окремий
тестовий бот та запуск його Web App у Telegram. Cookie сесії залишаються
на backend; frontend не зберігає Telegram proof або session token.

Bot environment: `BOT_TOKEN`, `BOT_SERVICE_KEY`, `BOT_WEBHOOK_SECRET`,
`BOT_APP_URL` (HTTPS), `BOT_BACKEND_URL` (приватний URL із `/internal/bot/`).

```bash
(cd src/bot && uv run --frozen python -m expense_tracker_bot.main)
```

Сервіс сам не реєструє webhook. Деплой і його явна реєстрація описані в
[інструкції Coolify](docs/deployment.md).

Логи сервісів доступні в Coolify та persistent `/logs`: щоденні gzip-архіви,
початково 14 днів зберігання (`LOG_RETENTION_DAYS`). Шляхи й команди — у
[інструкції логування](docs/logging.md).

## Демодані для перевірки

Після деплою надішліть `/seed` у приватному чаті бота або виберіть
«Налаштування → Додати демодані · жовтень 2026». Це додасть вашому
користувачу 70 витрат за **1–31 жовтня 2026**
із позначенням валюти профілю й використає 7 стандартних категорій. Наявні витрати та
налаштування не змінюються, AI не викликається.

Для перегляду в боті або Mini App виберіть період `2026-10-01` — `2026-10-31`
у витратах/звітах. Демозаписи можна редагувати й видаляти звичайними діями.
Повторний `/seed` не додає дублів і не відновлює видалені записи.
Потрібні місця для відсутніх активних стандартних категорій у межах ліміту
50 категорій; інакше нічого не додається.
Усі міграції включно з `AddRecurringTasks` мають виконатися перед запуском оновленого backend
(штатний сервіс `migrate` у Compose). Автоматичного наповнення при деплої немає.
Продукт містить витрати з категоріями та окремі накопичувальні рахунки.
З головної Mini App відкрийте «Накопичувальні рахунки»; у боті — однойменний
пункт меню. Створіть рахунок із назвою, потім додавайте поповнення та списання
із датою й нотаткою. Залишок і журнал спільні для обох каналів. Списання
понад залишок відхиляється. Позначення валюти береться з налаштувань профілю;
звичайні витрати/звіти й категорії залишаються окремими від накопичень.
Редагування, видалення журналу, перекази й конвертація не підтримуються.
Порядок оновлення БД — у [deployment](docs/deployment.md).

## Перевірки

Перевірки запускаються локально за явним запитом користувача.
GitHub Actions у репозиторії відсутні; push і pull request не запускають
цей набір перевірок. Тести та команди нижче збережені для ручного запуску.

```bash
# Окрема тестова БД; ім’я обов’язково завершується на _test.
export EXPENSE_TEST_DATABASE='Host=127.0.0.1;Database=expenses_test;Username=expenses;Password=local-dev-only'
dotnet test tests/ExpenseTracker.Tests -c Release
dotnet format src/backend/ExpenseTracker.slnx --verify-no-changes --no-restore
(cd src/frontend && npm run typecheck && npm run lint && npm run format:check && npm run build)
(cd src/bot && uv run --frozen ruff check src tests && uv run --frozen ruff format --check src tests && uv run --frozen mypy src)
(cd src/bot && uv run --frozen pytest -q)
python3 scripts/check_docs.py --check
```

PostgreSQL-тести пропускаються без `EXPENSE_TEST_DATABASE`. Інтеграційний
тест handlers потребує запущеного API **на ізольованій тестовій БД** з
синтетичними credentials, узгодженими з
[інтеграційним тестом](src/bot/tests/test_handlers_integration.py), та
`EXPENSE_BOT_TEST_URL=http://127.0.0.1:5085/internal/bot/`.
Він використовує підмінний Telegram transport і не надсилає повідомлень людям.

Контракти генеруються з Development API:

```bash
python3 scripts/generate_contracts.py
```

Це оновлює [OpenAPI](contracts/openapi.json), TypeScript DTO та Python-моделі.
Під час локальної перевірки контрактів повторіть генерацію та перевірте
відсутність різниці в цих трьох файлах.

## Межі першої версії

AI за замовчуванням вимкнений до згоди користувача. Без конфігурації
OpenRouter текстову чернетку можна заповнити вручну; голос потребує згоди
та придатної моделі. У [серверному конфігу](src/backend/ExpenseTracker.Api/appsettings.json)
зафіксовано `openai/gpt-6-luna` для розбору тексту та
`google/gemini-3.1-flash-lite` для транскрипції українського й англійського
голосу. Голос спочатку транскрибується, потім текст розбирає Luna.
У env потрібен лише ключ OpenRouter; ID моделей туди не додаються.
Обидва запити використовують strict JSON schema і `data_collection=deny`.
Каталог і параметри моделей перевірено; якість STT та доступність конкретного
ключа потребують живої перевірки перед релізом. Установіть бюджет ключа в OpenRouter.

Локальні перевірки не замінюють Telegram Android/iOS/Desktop/Web та HTTPS
smoke у Coolify. Реальні credentials не були надані; live AI, Telegram
клієнти й production deployment ще не перевірені.

Чинні правила — [AGENTS.md](AGENTS.md), архітектура —
[.agents/architecture.md](.agents/architecture.md), історія —
[docs/changes.md](docs/changes.md). Початковий `main.py` не використовується.

## Регулярні завдання

У боті натисніть «✅ Завдання», потім inline-кнопку відкриття. Окремий
вебзастосунок `/tasks` дозволяє створювати завдання щодня, кожні 2 чи 3 дні
або щотижня з обраної дати. Відмічайте виконання за сьогодні чи попередній
запланований день, переглядайте «Усі завдання», перейменовуйте й видаляйте.
Для іншого розкладу створіть нове завдання; пропуски не зсувають повторення.

Керування завданнями в чаті відсутнє: бот лише відкриває Web App.
Використовуються ті самі Telegram-вхід, мова/тема та часовий пояс профілю.
Після оновлення бота `/start` додає нову кнопку до reply-меню.
Нових env-змінних немає; перед запуском потрібна `AddRecurringTasks`.
Ця міграція та runtime/browser/Telegram перевірки ще не виконувались.
