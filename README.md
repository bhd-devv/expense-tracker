# Lime — трекер витрат

Особисті витрати через Telegram Mini App та кнопковий Telegram-бот.
Текст або голос → AI-чернетка → перевірка користувачем → збережена витрата.
Сьогоднішню дату визначає backend за часовим поясом профілю.

- ASP.NET Core / .NET 10, EF Core, PostgreSQL.
- Next.js 16 App Router, React, strict TypeScript; `uk`/`en`, light/dark/auto.
- Python / aiogram 3; reply-меню, inline-дії, durable inbox/outbox.
- OpenRouter на backend: текстовий розбір і транскрипція WAV-аудіо.
- Редагування/видалення витрат, фільтри, звіти окремо за валютами,
  створення/архівування рахунків і категорій, налаштування в обох каналах.

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
