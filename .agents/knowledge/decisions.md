# Прийняті архітектурні рішення

Це короткий індекс ADR для рішень, що змінюють межі сервісів, авторизацію,
зберігання даних або release-процес. Звичайні баги й локальні UI-правки ADR
не потребують.

## ADR-001: бот і API в одному ASP.NET Core backend

- **Статус:** замінено ADR-007 від 2026-11-01; нижче збережено попереднє
  рішення для історії, воно більше не визначає архітектуру.
- **Контекст:** Mini App і Telegram-бот мають виконувати однакові сценарії
  та застосовувати однакові ownership/business rules.
- **Рішення:** webhook, bot handlers, HTTP API та прикладні сценарії живуть в
  одному backend; бот не викликає власний API через внутрішній HTTP.
- **Наслідки:** один source of truth для правил і транзакцій; handlers мають
  залишатися тонкими; масштабування worker-ів потребує окремого контролю.

## ADR-002: один публічний origin для Mini App і API

- **Статус:** прийнято.
- **Контекст:** потрібна безшовна cookie-сесія без передачі bearer token у URL.
- **Рішення:** Coolify маршрутизує `/api/*` до backend, решту UI до Next.js
  на тому самому HTTPS origin. Відповідно до ADR-007 webhook спрямовується
  до aiogram-сервісу; приватні bot endpoints через публічний proxy недоступні.
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

## ADR-006: AI-обробка повідомлень через OpenRouter на backend

- **Статус:** прийнято, 2026-11-01; адаптер реалізовано, live provider ще не перевірено.
- **Контекст:** користувач визначив OpenRouter provider для транскрипції
  голосових повідомлень і автоматичного вибору категорії витрати.
- **Варіанти:** прямі інтеграції з іншими AI providers або локальна
  транскрипція не обрані, оскільки виходять за поточну вимогу. Browser-side
  виклик із provider key непридатний через розкриття серверного секрету.
- **Рішення:** .NET backend виконує AI-обробку й валідує її результат.
  Продуктовий контракт описано в [product.md](../product.md); він є єдиним
  джерелом правил введення, дати й категорії.
- **Наслідки:** перед реалізацією треба вибрати й перевірити сумісні моделі
  та audio formats, обмежити витрати/таймаути, забезпечити приватність
  повідомлень і видалення тимчасового аудіо. Інтеграція не змінює identity,
  ownership або вимогу підтвердження фінансової операції.

## ADR-007: окремий Python/aiogram адаптер для Telegram

- **Статус:** прийнято, 2026-11-01; замінює ADR-001. Реалізовано й локально перевірено.
- **Контекст:** користувач прямо обрав aiogram і мінімум boilerplate, а
  основний backend залишається C# / ASP.NET Core.
- **Варіанти:** .NET Telegram-клієнт у тому самому процесі не відповідає
  вибору aiogram; перенесення бізнес-логіки в Python створило б другий
  backend і дублювання правил.
- **Рішення:** окремий aiogram 3.x сервіс для webhook, routers, кнопок та
  Telegram delivery. Він працює через приватний .NET API з окремим service
  credential. .NET залишається єдиним власником сценаріїв, БД та OpenRouter.
- **Наслідки:** у Compose потрібний bot container і Python lockfile;
  контракт, actor scope, retries та leases перетинають межу процесів.
  Спільна транзакція між сервісами відсутня, тому потрібні durable
  inbox/outbox та ідемпотентність. Redis або broker для цього не додаються
  без окремої потреби.
- **Чинні контракти:** [architecture.md](../architecture.md),
  [security.md](../security.md), [telegram.md](../telegram.md),
  [deployment.md](../deployment.md). Цей ADR фіксує причину вибору,
  а детальні протоколи підтримуються у відповідних контрактах.

## ADR-008: один Caddy gateway перед трьома application services

- **Статус:** прийнято та локально перевірено, 2026-11-01.
- **Контекст:** Coolify повинен відкривати один origin і тільки публічні
  маршрути API/бота; специфічні labels proxy залежать від інсталяції Coolify.
- **Варіанти:** окремі path routers у Coolify або один явний gateway.
- **Рішення:** у Compose публічний лише gateway. Caddy маршрутизує Next.js,
  `/api/*` і webhook; `/internal/*` повертає 404. TLS завершує Coolify.
  Короткі фінансові/catalogue мутації серіалізуються advisory lock власника;
  він не утримується під час AI/Telegram I/O.
- **Наслідки:** ще один невеликий контейнер і контрольований private IP;
  правила маршрутизації версіонуються в репозиторії й копіюються в gateway
  image через `deploy/gateway.Dockerfile`. Host bind mount Caddyfile не
  використовується: Coolify helper checkout і filesystem Docker host
  можуть відрізнятися. API upstream використовує alias
  `expense-tracker-api-private` лише в private network, щоб додаткова мережа
  Coolify не змінила source IP gateway і перевірку trusted proxy.
  Зміна routing потребує перебудови gateway. HTTPS і Telegram
  клієнти потребують перевірки після справжнього deployment.
- **Контракт запуску:** [deployment](../../docs/deployment.md).

## ADR-009: нормалізація голосу перед OpenRouter

- **Статус:** реалізовано, 2026-11-01; live audio model не перевірено.
- **Контекст:** Telegram надсилає OGG/Opus, браузер може надсилати WebM/MP4;
  підтримка форматів відрізняється між OpenRouter models/providers.
- **Рішення:** ffmpeg на backend нормалізує дозволені audio containers у
  mono WAV 16 kHz. Ліміти — 10 МБ і 120 с; тимчасові файли очищаються.
  Chat Completions отримує base64 `input_audio` і strict JSON schema для
  транскрипції. Далі транскрипт обробляє текстова модель за ADR-010.
  Model IDs версіонуються у серверному конфігу.
- **Наслідки:** frontend і бот не мають provider credentials або власних
  правил транскрипції. Потрібні ffmpeg у runtime image, явна згода
  користувача й перевірка реальної моделі до release.

## ADR-010: фіксовані AI-моделі та окремий STT перед текстовим розбором

- **Статус:** прийнято, 2026-11-01; уточнює ADR-009 за запитом користувача.
- **Контекст:** користувач обрав найновішу GPT Luna для тексту та якісну
  бюджетну модель STT для української й англійської; ID мають бути в конфігу.
- **Варіанти:** одна audio-модель для всього голосового сценарію не
  застосовувала б Luna до голосових витрат. Окремі provider SDK або прямий
  STT provider поза OpenRouter не потрібні.
- **Рішення:** `openai/gpt-6-luna` і `google/gemini-3.1-flash-lite` у
  [appsettings.json](../../src/backend/ExpenseTracker.Api/appsettings.json).
  STT отримує лише аудіо; Luna — текст/транскрипт та каталог категорій.
  Обидва виклики — на backend через OpenRouter, з strict JSON schema,
  `require_parameters` і `data_collection=deny`, під спільним timeout 75 с.
  Luna не отримує непідтримуваний `temperature`, reasoning — `none`;
  Flash-Lite використовує `minimal`. Немає автоматичного fallback/retry.
- **Доказ вибору:** [офіційна сторінка Luna](https://developers.openai.com/api/docs/models/gpt-6-luna),
  [каталог OpenRouter](https://openrouter.ai/api/v1/models),
  [Gemini 3.1 Flash-Lite](https://docs.cloud.google.com/vertex-ai/generative-ai/docs/models/gemini/3-1-flash-lite)
  та [аудіоформати OpenRouter](https://openrouter.ai/docs/guides/overview/multimodal/audio).
  Перевірено 2026-11-01; каталог підтверджує audio input і structured outputs,
  Google описує покращення ASR та minimal thinking. За каталогом audio input
  Flash-Lite коштує $0.50/1M tokens, output — $1.50/1M; це не фіксована
  ціна хвилини. Якість uk/en і доступ конкретного ключа ще не перевірено.
- **Наслідки:** голос має два послідовні provider-запити; STT failure зупиняє
  обробку до Luna. API, bot DTO, фінансові інваріанти й БД сумісні,
  міграція не потрібна. У env залишається лише секретний API key.
  Чинний продуктовий контракт — [product.md](../product.md).

## ADR-011: атомарне демонаповнення з окремим записом виконання

- **Статус:** `accepted`, 2026-11-01.
- **Контекст:** `/seed` має додати витрати за жовтень лише поточному actor,
  витримати повторну доставку/конкурентні виклики й не відновлювати записи,
  які користувач уже відредагував або видалив.
- **Варіанти:** пошук за описом ненадійний після редагування; перевикористання
  draft/inbox змішує різні сценарії й строки зберігання. Обрано малу таблицю
  `DemoSeeds` з ключем `(UserId, Period)`, кількістю й UTC часом виконання.
- **Рішення:** приватний BotActor endpoint викликає backend `DemoSeedService`.
  User advisory lock і одна PostgreSQL transaction охоплюють перевірку
  запису виконання, лімітів каталогів, рахунки/категорії та витрати.
  Використовується спільний `ExpenseService.Apply`, без дублювання грошових
  і ownership-правил у Python. Повтор повертає `createdCount=0`.
- **Наслідки:** потрібна additive migration `AddDemoSeeds` перед backend;
  rollback застосунку не вимагає видалення таблиці. Видалення запису виконання
  вручну зніме захист від повторного наповнення. Звичайні витрати доступні
  через обидва канали; bot DTO генеруються з OpenAPI. Профіль і персональні
  дані не переписуються, AI/outbox не викликаються. Наповнення лише за явною
  командою, без автоматичного seed у міграції або startup.
- **Контракти:** [product.md](../product.md), [telegram.md](../telegram.md).
- **Задача:** [демовитрати за жовтень](../tasks/seed-october-2026-11-01/README.md).

## ADR-012: console logs і persistent щоденні архіви через спільний runner

- **Статус:** прийнято, 2026-11-01; container/Coolify smoke ще не виконано.
- **Контекст:** потрібні всі серверні дії та помилки в Coolify і файли зі
  щоденною ротацією, що переживають перевипуск контейнерів.
- **Рішення:** Python stdlib runner у runtime images синхронно дублює
  stdout/stderr у Docker console й окремий service volume; UTC midnight
  rotation, gzip, retention за кількістю та календарним віком. Сервіси
  самі формують безпечні події; runner не читає Docker socket і не додає
  зовнішньої служби. .NET logging queue чекає замість відкидання подій.
- **Альтернативи:** різні file sinks у кожному runtime дублюють policy;
  Docker log rotation сама не забезпечує добові архіви між redeploy.
- **Наслідки:** Python додано у backend/frontend/gateway/PostgreSQL runtime
  images; bot уже використовує Python. PostgreSQL base/version і data volume
  збережені. Runner передає сигнали та exit code; Compose має init і grace
  period. Один writer на service volume вимагає stop-first release;
  для replicas потрібні окремі файли/volumes або collector. Файли не є
  транзакційним audit ledger чи backup і не гарантують захист від втрати диска.
- **Безпека:** без HTTP bodies, query, tokens, сум, нотаток і transcripts.
  Stack locations/types зберігаються без довільних exception messages/SQL;
  gateway filters і runner redaction додають захист. Retention початково
  14 днів, `LOG_RETENTION_DAYS=1..365`. Докладно: [logging](../../docs/logging.md).
