# Авторизація та безпека

## Єдина особа в Telegram і Mini App

Внутрішній User пов'язаний з унікальним числовим Telegram user ID.
Username, телефон, ім'я, `chat_id`, query-параметр `userId` або об'єкт
`initDataUnsafe` не доводять особу. Зміна username не створює нового акаунта.
В обох каналах backend знаходить того самого User за перевіреним Telegram ID.
Пароль і ручне зв'язування акаунтів для запуску з Telegram не потрібні.

## Вхід Mini App

1. Користувач відкриває HTTPS Mini App через `web_app` кнопку Telegram.
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

- Приймай updates через HTTPS webhook із перевіркою
  `X-Telegram-Bot-Api-Secret-Token` проти окремого випадкового секрету.
  Не використовуй bot token у URL webhook.
- Після перевірки webhook підтримуй фінансові дії лише в особистих чатах,
  із валідним `from.id` та відповідним типом update. Для callback identity —
  `callback_query.from.id`, а не ID автора повідомлення бота.
- Якщо update вказує на групу або не містить надійного контексту особи,
  не показуй фінансові дані; запропонуй особистий чат.
- Зв'язування з User відбувається на сервері. Webhook secret автентифікує
  доставку Telegram, а серверна ownership-перевірка — конкретну операцію.

## Захист API та даних

- Публічні Next.js і `/api/*` розміщуй на одному origin. Bot webhook —
  окремий endpoint backend, автентифікований webhook secret, без browser cookie.
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
- Обмежуй розмір запитів, частоту auth спроб і команд на користувача.
  Webhook ingress також потребує загального ліміту й захисту від перевантаження.
- Bot token, DB credentials, webhook secret і Data Protection keys доступні
  лише серверу. Заборонено `NEXT_PUBLIC_*` для секретів.
- Не логуй `initData`, cookie, auth headers, tokens або довільні Telegram
  payloads. Фінансові нотатки й суми не додавай у технічні logs за замовчуванням.
- Довіряй forwarded headers лише від налаштованого proxy/network Coolify.
  HTTPS termination не повинна вимикати `Secure` cookie або створювати open redirects.

Початковий сценарій входу — запуск усередині Telegram. Поза Telegram показуй
кнопку відкриття бота/Mini App. Самостійний browser login через Telegram —
окрема функція; не підмінюй його передачею Telegram ID або reusable token у URL.

Під час реалізації звіряй протокол з офіційними джерелами:
[Mini Apps](https://core.telegram.org/bots/webapps#validating-data-received-via-the-mini-app),
[webhooks](https://core.telegram.org/bots/api#setwebhook),
[ASP.NET Core security](https://learn.microsoft.com/aspnet/core/security/).
