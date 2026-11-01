# Реалізація першого MVP

Стан: `completed_local`. Дата: 2026-11-01.

## Результат і критерії приймання

Реалізовано .NET 10 API, PostgreSQL, Next.js Mini App та Python/aiogram-бот.
Обидва канали використовують спільні AI-чернетки, підтвердження, редагування,
видалення, історію, звіти, рахунки, категорії та налаштування.
Доступні `uk`/`en`, light/dark/auto. Telegram login, серверні cookie-сесії,
CSRF, ownership, ідемпотентність, версії сутностей, durable inbox/outbox.
Підготовлено Compose для Coolify та GitHub Actions. Запуск — у
[README](../../../README.md), deployment — у [гайді](../../../docs/deployment.md).

## План

- [x] Зафіксувати залежності та створити backend із persistence/auth.
- [x] Реалізувати витрати, AI-чернетки, каталоги, звіти й приватний bot API.
- [x] Реалізувати aiogram routers, inbox/outbox і кнопкові сценарії.
- [x] Реалізувати Mini App і перевірити в браузері.
- [x] Додати Docker Compose, CI, документацію та релевантні тести.
- [x] Виконати локальні перевірки й зафіксувати межі інтеграцій.

## Рішення

Бізнес-операції й AI виконуються лише в backend; Python має транспортні
handlers і згенеровані Pydantic DTO. OpenAPI є джерелом контрактів для обох
клієнтів. Caddy — єдиний public gateway; приватний bot API не публікується.
Аудіо нормалізується ffmpeg до WAV. Дата фіксується до AI за часовим поясом.
Короткі мутації серіалізуються advisory lock власника, зовнішній I/O поза ним.
ADR — у [decisions](../../knowledge/decisions.md).

## Фактичні перевірки

- `dotnet format ... --verify-no-changes --no-restore`, Release build і
  `dotnet test tests/ExpenseTracker.Tests -c Release`: 21 passed, 0 skipped.
  Справжня ізольована PostgreSQL, початкова EF-міграція, ownership, CSRF,
  HMAC/replay, конкурентне підтвердження, версії та lease fencing.
- Frontend: `npm run format:check`, `typecheck`, `lint`, `build` — passed.
- Bot: Ruff check/format, strict mypy — passed; `pytest -q` — 6 passed.
  Інтеграційні handlers/webhook використовують реальний локальний API та
  підмінений Telegram transport. Повідомлень людям не надсилали.
- `scripts/generate_contracts.py` двічі: однакові SHA-256 OpenAPI/TS/Python.
- Playwright у браузері зі синтетичним підписаним Telegram proof: login,
  текстова чернетка без live AI, ручне доповнення й save 85 → edit 90 UAH,
  історія/місячний звіт, створення категорії, uk/light і en/dark,
  mobile 390px/desktop, повторне відкриття — passed.
- Docker: зібрано три images; локальний Compose із PostgreSQL, явною
  міграцією та healthchecks. Gateway: головна/readiness 200, auth/me 401,
  приватний маршрут 404, webhook без secret 403. Production antiforgery
  cookie має Secure/HttpOnly/__Host-. Це локальний smoke, не Coolify.
- `npm audit --omit=dev`: 0 vulnerabilities; dev advisory/peer warnings
  записано у [dependencies](../../../docs/dependencies.md).
- Перевірки документації та `git diff --check` — passed.

Докази локально в ігнорованій `output/mvp/`: `backend-tests-final.log`,
`frontend-build-final.log`, `contracts-final.log`, `container-build-final.log`,
`desktop-uk-light.png`, `mobile-uk-light.png` та dark screenshots.

## Обмеження й наступна дія

OpenRouter/Telegram credentials і production domain не надані. Live AI,
справжній мікрофон Telegram, клієнти Android/iOS/Desktop/Web, HTTPS cookie
поведінка в них та Coolify deployment не перевірені. Аудіотести перевіряють
ffmpeg і контракт провайдера з mock, не якість живої транскрипції.
CI налаштовано, але на GitHub не запускався. Retention завершених delivery
та idempotency metadata потребує політики перед тривалою експлуатацією.

Останній завершений крок: локальна реалізація та перевірки всіх компонентів.
Наступна окрема задача: налаштувати тестового бота, OpenRouter models/key
і HTTPS середовище, виконати live smoke за deployment guide. Це не дозвіл
на публікацію, реєстрацію webhook або production міграцію.
Попередні незакомічені документи й початковий `main.py` збережено.
