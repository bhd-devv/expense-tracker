# Прийняті архітектурні рішення

Це короткий індекс ADR для рішень, що змінюють межі сервісів, авторизацію,
зберігання даних або release-процес. Звичайні баги й локальні UI-правки ADR
не потребують.

## ADR-001: бот і API в одному ASP.NET Core backend

- **Статус:** прийнято.
- **Контекст:** Mini App і Telegram-бот мають виконувати однакові сценарії
  та застосовувати однакові ownership/business rules.
- **Рішення:** webhook, bot handlers, HTTP API та прикладні сценарії живуть в
  одному backend; бот не викликає власний API через внутрішній HTTP.
- **Наслідки:** один source of truth для правил і транзакцій; handlers мають
  залишатися тонкими; масштабування worker-ів потребує окремого контролю.

## ADR-002: один публічний origin для Mini App і API

- **Статус:** прийнято.
- **Контекст:** потрібна безшовна cookie-сесія без передачі bearer token у URL.
- **Рішення:** Coolify маршрутизує `/api/*` і webhook до backend, решту до
  Next.js на тому самому HTTPS origin.
- **Наслідки:** простіші cookies/CSRF та frontend requests; routing треба
  перевіряти під час deployment smoke, а API не має потрапляти в catch-all.

## ADR-003: cookie-сесія після перевірки Telegram Mini App proof

- **Статус:** прийнято.
- **Контекст:** `initData` доводить запуск із Telegram, але не має бути
  довготривалим browser bearer token.
- **Рішення:** backend перевіряє `initData`, створює серверну сесію у
  `HttpOnly` cookie та захищає state-changing requests Antiforgery.
- **Наслідки:** потрібні Data Protection keys, session persistence і реальна
  перевірка Telegram-клієнтів; localStorage для auth не використовується.

## ADR-004: PostgreSQL authoritative для inbox/outbox і діалогів

- **Статус:** прийнято.
- **Контекст:** Telegram повторює updates, бот має чернетки й side effects,
  а restart контейнера не повинен втрачати стан.
- **Рішення:** PostgreSQL зберігає updates, conversation drafts, sessions,
  idempotency keys та outbox; Redis/пам'ять не є джерелом бізнес-стану.
- **Наслідки:** потрібні constraints, короткі транзакції, claim/locking,
  worker retry policy та міграції. Telegram delivery не є exactly-once.

## ADR-005: історія не перевизначає чинні контракти

- **Статус:** прийнято.
- **Контекст:** task, research і changelog накопичують корисний контекст, але
  можуть описувати старий код або вже скасоване рішення.
- **Рішення:** чинним джерелом є правила/knowledge, а історичні документи
  лише посилаються на них і не дають дозволу виконувати операції.
- **Наслідки:** перед роботою треба звірити документ із кодом і актуальним
  користувацьким запитом; суперечність виправляється в поточному контракті.
