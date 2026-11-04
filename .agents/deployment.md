# Деплой через Coolify

## Початкова топологія

Проєкт має розгортатися як Docker Compose application у Coolify.
Під час реалізації додай `deploy/compose.yaml` і окремі multi-stage Dockerfile
для backend, frontend та окремий production Dockerfile для Python/aiogram
бота. Gateway має окремий `deploy/gateway.Dockerfile`, який копіює Caddyfile
в image; host bind mount для конфігурації не використовується. База
відносних Compose paths — корінь репозиторію через
`--project-directory`: `build.context: .`.
Dockerfile paths рахуються від build context. Локальні команди запускай із
кореня з `--project-directory .`, як у Coolify. Base Directory — `/`,
Compose Location — `/deploy/compose.yaml`. Перевір реальний build у Coolify.

| Сервіс | Призначення | Доступ |
| --- | --- | --- |
| `frontend` | Next.js production server, standalone output | Публічний HTTPS через proxy |
| `backend` | ASP.NET Core API, бізнес-сценарії, persistence та приватний bot API | Лише `/api/*` через proxy; `/internal/bot/*` приватно |
| `bot` | Python / aiogram, webhook і один inbox worker для двох launch-кнопок | Лише webhook через proxy; backend доступний у Docker network |
| `postgres` | Дані, сесії, inbox | Лише приватна Docker network |
| `migrate` | Одноразові EF Core migrations | Deployment job, без публічного порту |

Один публічний origin, наприклад `https://expenses.example.com`:

- `/api/*` → backend; цей маршрут має вищий пріоритет за frontend catch-all.
- `/telegram/webhook` → aiogram-сервіс із перевіркою webhook secret.
- решта маршрутів → frontend.

Домен у прикладі є placeholder. Налаштуй точні domain/path rules відповідно
до поточної версії Coolify/proxy й перевір, що API ніколи не потрапляє до
Next.js catch-all. Зовнішній TLS завершує Coolify proxy. Не публікуй БД,
`/internal/bot/*` або весь bot port. Webhook маршрут має вищий пріоритет
за frontend catch-all; приватні API paths блокуються на proxy.

Gateway звертається до backend через alias `expense-tracker-api-private`,
заданий лише на Compose network `private`. Coolify може під'єднати обидва
контейнери також до application network: звичайне ім'я `backend` тоді
не гарантує private маршрут. HTTPS forwarded headers backend приймає від
статичного private IP gateway `172.30.19.2`; цей периметр не розширюй на
всю додаткову мережу. Зміна alias вимагає застосування Compose й rebuild gateway.

## Конфігурація

Додай `.env.example` лише з безпечними placeholders та описом обов'язкових
змінних. Секрети задаються в Coolify environment/secrets, не комітяться й
не вбудовуються в image layers або frontend bundle.

Початкові групи конфігурації:

- ASP.NET Core environment, PostgreSQL connection string.
- Telegram bot token, окремий webhook secret, публічний Mini App URL.
  Username backend отримує через Telegram `getMe` за токеном і кешує до
  перезапуску; окрема env-змінна не потрібна.
- Приватний backend URL для бота та окремий service API key. Розподіл
  секретів між сервісами визначено в [security.md](security.md).
- Серверний OpenRouter API key; model IDs для транскрипції та текстового
  розбору версіонуються в `appsettings.json`, а не env. Поточний вибір —
  у [product.md](product.md). Таймаути, ліміти вартості/обсягу й підтримувані audio formats.
- Публічний origin, довірені proxy, строки auth proof/session/draft.
- Захищене сховище ASP.NET Core Data Protection keys.
- PostgreSQL credentials; моніторинг і рівень logging без персональних payloads.

Версії runtime, SDK, NuGet/npm пакетів і container base images фіксуй;
не використовуй `latest`. Node.js має бути сумісним із вибраною Next.js;
Python — із вибраною aiogram 3.x. Lockfiles обов'язкові: frontend build
використовує frozen install / `npm ci`, бот — `uv sync --frozen --no-dev`.

## Запуск і міграції

1. Перевір Compose config, build images, потрібні secrets та доступність БД.
2. Перед зміною схеми переконайся, що є придатна резервна копія.
3. Один deployment job застосовує міграції до готової БД; лише успішне
   завершення дозволяє запустити нову версію backend.
4. Запусти backend, aiogram-бота й frontend. Перевір readiness backend,
   приватне з'єднання бота та публічні маршрути, включно з відмовою доступу
   до `/internal/bot/*` через публічний origin.
5. Окремою явною deployment-дією зареєструй Telegram webhook і Mini App URL.
   Не викликай `setWebhook` на старті кожної replica. Реєстрація не повинна
   неявно очищати pending updates.
   Реєстрація також прибирає старі command menus і додатковий menu Web App.
6. Перевір обидві inline launch-кнопки, Telegram-вхід і сценарії Web Apps.

Не виконуй `Database.Migrate()` конкурентно при старті всіх replicas.
Не покладайся лише на Compose `depends_on` як на перевірку готовності БД.
Якщо вибраний режим Coolify не підтримує deployment gate для `migrate`,
реалізуй окремий контрольований крок release, перш ніж запускати застосунок.

## Надійність та експлуатація

- Health endpoints backend: `/api/health/live` без залежності від БД,
  `/api/health/ready` із перевіркою необхідних залежностей. Відповіді не
  розкривають connection strings, версії схеми або інші внутрішні дані.
- Docker healthchecks для сервісів, restart policy та graceful shutdown.
  Передавай CancellationToken; зупинка worker не має втрачати inbox.
- Контейнери запускаються без root, де це підтримано runtime. Фінансові
  дані не зберігаються у файловій системі ephemeral containers.
- Persistent volumes для PostgreSQL і Data Protection keys; автоматичні
  резервні копії зберігаються поза хостом. Перевіряй відновлення.
- Початково одна backend та одна bot replica. Масштабування вимагає спільних keys,
  коректного locking/claiming inbox. Actor API й діалогів немає.
- Міграції мають зберігати сумісність під час оновлення. Rollback image
  не відкочує БД; плануй expand/contract для несумісних змін.
- Спостерігай за readiness, помилками auth, затримкою та backlog inbox,
  failed jobs і помилками Telegram. Використовуй correlation IDs без секретів.
- Кожен сервіс дублює stdout/stderr у persistent log volume через спільний
  runner; UTC daily gzip rotation, retention через `LOG_RETENTION_DAYS`.
  Один writer на volume, без одночасної ротації replicas. Формат, захист
  даних, перегляд та release-обмеження — у [logging](../docs/logging.md).
- Локальне середовище й production використовують різні БД, bot tokens
  і webhook secrets. Telegram дозволяє один webhook на бота; локальний
  tunnel або polling не має перехоплювати production updates.

Деплой не вважається перевіреним лише тому, що Docker images зібралися:
потрібні HTTPS, правильний routing, cookie/CSRF і реальний Telegram запуск.

Реалізовані Compose, gateway, production environment і порядок запуску описані
в [операційній інструкції](../docs/deployment.md). Вона уточнює цю топологію;
локальний smoke не є підтвердженням deployment у Coolify.
