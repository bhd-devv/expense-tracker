# Авторизація та безпека

## Єдина особа в Telegram і Mini App

Внутрішній User пов'язаний з унікальним числовим Telegram user ID.
Username, телефон, ім'я, `chat_id`, query-параметр `userId` або об'єкт
`initDataUnsafe` не доводять особу. Зміна username не створює нового акаунта.
В обох каналах backend знаходить того самого User за перевіреним Telegram ID.
Пароль і ручне зв'язування акаунтів для запуску з Telegram не потрібні.

## Вхід Mini App

1. Користувач відкриває HTTPS Mini App через inline `web_app` кнопку Telegram
   або menu button бота. Запуск через reply `KeyboardButton.web_app` не
   передає `initData` і не використовується для входу.
   Client boundary отримує сирий `Telegram.WebApp.initData` із Telegram SDK.
2. Передай його один раз у тілі `POST /api/auth/telegram` через TLS.
   Не передавай через URL і не записуй у logs, analytics або сховище браузера.
3. Backend перевіряє підпис за чинною офіційною схемою Telegram Mini Apps
   для свого bot token, включно з побудовою data-check-string і
   constant-time comparison. Використовуй перевірений validator або
   стандартні криптографічні примітиви з офіційними test vectors;
   не винаходь власну схему. Перед реалізацією звір актуальну специфікацію.
4. Лише після перевірки підпису обробляй `user` і `auth_date`. Вимагай
   Telegram user ID, валідний формат і свіжість: початковий TTL — 5 хвилин,
   допустиме відхилення годинника в майбутнє — до 30 секунд. Значення мають
   бути конфігурованими й перевіреними при старті.
5. Атомарно зареєструй fingerprint перевіреного proof до завершення TTL,
   знайди/створи User і створи нову сесію. Повторне використання proof
   не створює нову сесію. Повтор від уже прив'язаної чинної сесії може
   повернути її поточний стан без нового входу.
6. Видай opaque session cookie. Решта API працює з цією сесією, а не
   приймає `initData` як довготривалий bearer token.

Сесію створюй штатним ASP.NET Core cookie authentication із серверним
ticket store у PostgreSQL. Cookie — `HttpOnly`, `Secure`, `Path=/`, без
`Domain`, із префіксом `__Host-`. Початково використовуй `SameSite=Lax`;
реальну поведінку перевір на Android, iOS, Telegram Desktop та Telegram Web.
Якщо конкретний підтримуваний embedded-контекст потребує `SameSite=None`,
змінюй лише після перевірки з `Secure` і повним CSRF-захистом. Блокування
third-party cookies не обходь перенесенням session token до localStorage.

Browser session містить `user_id` для ownership та стандартний
`ClaimTypes.NameIdentifier` із тим самим внутрішнім User ID для ASP.NET Core
Antiforgery. Одного власного `user_id` недостатньо для генерації CSRF-токена
автентифікованого користувача. Захищені старі tickets без стандартного claim
доповнюються при cookie validation та перевидаються; прострочений ticket,
невалідний User ID або різні значення цих claims відхиляються.

Початкові ліміти сесії: idle 24 години, абсолютний строк 7 діб, серверне
відкликання на виході. Не подовжуй абсолютний строк автоматично.
Зберігай Data Protection keys у постійному захищеному сховищі, спільному
для backend replicas. Не пиши власну криптографію session tokens.

На старті Mini App спочатку перевіряє `/api/auth/me`. Чинна сесія дозволяє
продовжити без повторного використання proof. При завершенні сесії запускай
новий вхід зі свіжим `initData`. Якщо proof застарів або відповідь входу
загубилась разом із cookie, запропонуй повторно відкрити Mini App із Telegram.
Дедуплікуй паралельні запити входу, зокрема подвійні effects у React dev mode.

## Вхід через бота

- Aiogram-сервіс приймає updates через HTTPS webhook із перевіркою
  `X-Telegram-Bot-Api-Secret-Token` проти окремого випадкового секрету.
  Не використовуй bot token у URL webhook.
- Після перевірки webhook підтримуй фінансові дії лише в особистих чатах,
  із валідним `from.id` та відповідним типом update. Для callback identity —
  `callback_query.from.id`, а не ID автора повідомлення бота.
- Якщо update вказує на групу або не містить надійного контексту особи,
  не показуй фінансові дані; запропонуй особистий чат.
- Зв'язування з User відбувається на сервері. Webhook secret автентифікує
  доставку Telegram, а серверна ownership-перевірка — конкретну операцію.

## Межа довіри aiogram → .NET

- Бот є довіреним серверним адаптером із правом передати перевірений
  Telegram actor context. Для нього використовуй окремий service credential,
  відмінний від bot token, webhook secret і browser session cookie.
- Початковий механізм — випадковий API key у заголовку Authorization для
  окремої ASP.NET Core authentication scheme/policy. Використовуй стандартний
  генератор випадкових байтів і constant-time comparison; не пиши власні
  HMAC/JWT-схеми. Ключ зберігається у secrets обох сервісів і має ротацію.
- Приватні `/internal/bot/*` endpoints доступні лише в Docker network;
  proxy не публікує їх і backend port не відкритий напряму назовні.
  Service credential обов'язковий навіть у приватній мережі. Якщо сервіси
  розміщено на різних хостах, передавай запити через захищений TLS-канал.
- `from.id` потрапляє до цих endpoints лише після перевірки Telegram update
  aiogram-сервісом. Service key доводить походження від бота, а не право на
  довільну фінансову дію: backend встановлює User за actor context і перевіряє
  ownership записів, категорій, рахунків, діалогів і callbacks.
- Browser endpoints не приймають service headers або довільний Telegram ID
  як спосіб входу. Bot endpoints не створюють browser sessions і не видають
  боту права адміністратора або доступ до користувацьких даних без actor scope.
- Aiogram не доводить особу Mini App. Перевірка підпису `initData`, replay
  protection і browser session лишаються в .NET. Bot token потрібний aiogram
  для Telegram API та backend для перевірки Mini App; webhook secret потрібний
  лише aiogram. DB credentials і Data Protection keys потрібні лише backend;
  OpenRouter key також залишається лише в .NET.

## Захист API та даних

- Публічні Next.js і `/api/*` розміщуй на одному origin. Bot webhook —
  окремий endpoint aiogram, автентифікований webhook secret, без browser cookie.
- Усі browser-запити, що змінюють стан, захищай ASP.NET Core Antiforgery
  із request token у заголовку. Login endpoint також захищай від login CSRF:
  встанови анонімний antiforgery context перед обміном `initData`.
  Після входу отримай новий request token для автентифікованого контексту.
- Перевіряй origin для browser mutations. Не використовуй GET для зміни даних.
  Не дозволяй довільні credentialed CORS origins.
- Вибирай сутності із фільтром власника й перевіряй ownership зв'язаних ID.
  Захист має працювати однаково для HTTP, bot handlers і callback buttons.
- `callback_data`, `start_param` і deep links — недовірені дані; вони не
  надають права доступу. Службові одноразові токени мають TTL і прив'язку
  до власника, конкретної дії та чернетки/версії сутності.
- Обмежуй розмір запитів, частоту auth спроб і дій на користувача.
  Webhook ingress також потребує загального ліміту й захисту від перевантаження.
- Bot token, DB credentials, webhook secret і Data Protection keys доступні
  лише серверу. Заборонено `NEXT_PUBLIC_*` для секретів.
- Не логуй `initData`, cookie, auth headers, tokens або довільні Telegram
  payloads. Фінансові нотатки й суми не додавай у технічні logs за замовчуванням.
- Довіряй forwarded headers лише від налаштованого private IP gateway
  (`172.30.19.2` у Compose). Gateway звертається до API через network-specific
  alias `expense-tracker-api-private`; додаткова application network Coolify
  не є довіреним proxy-периметром.
  HTTPS termination не повинна вимикати `Secure` cookie або створювати open redirects.

Початковий сценарій входу — запуск усередині Telegram. Поза Telegram показуй
кнопку відкриття бота/Mini App. Самостійний browser login через Telegram —
окрема функція; не підмінюй його передачею Telegram ID або reusable token у URL.

Під час реалізації звіряй протокол з офіційними джерелами:
[Mini Apps](https://core.telegram.org/bots/webapps#validating-data-received-via-the-mini-app),
[webhooks](https://core.telegram.org/bots/api#setwebhook),
[ASP.NET Core security](https://learn.microsoft.com/aspnet/core/security/).
