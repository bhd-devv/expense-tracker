# Авторизація та безпека

## Особа у вебзастосунках

Внутрішній User пов'язаний з унікальним числовим Telegram user ID.
Username, телефон, ім'я, `chat_id`, query-параметр `userId` або об'єкт
`initDataUnsafe` не доводять особу. Зміна username не створює нового акаунта.
В обох Web Apps backend знаходить того самого User за перевіреним Telegram ID.
Повідомлення в боті не створює User і не дає доступу до його даних.
Пароль і ручне зв'язування акаунтів для запуску з Telegram не потрібні.

## Вхід Mini App

1. Користувач відкриває HTTPS Mini App через inline `web_app` кнопку Telegram
   бота. Запуск через reply `KeyboardButton.web_app` не
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

## Webhook бота й межа aiogram → .NET

- Aiogram перевіряє `X-Telegram-Bot-Api-Secret-Token` проти окремого
  випадкового секрету. Bot token не використовується в URL webhook.
- Приватне повідомлення людини використовується лише для відповіді двома
  кнопками. Групи й callbacks ігноруються. До inbox не потрапляють тексти,
  аудіо, імена чи callback data; ID потрібні лише для доставки меню.
- Бот має окремий випадковий service API key у Authorization, відмінний
  від bot token, webhook secret і browser cookie. Authentication scheme
  перевіряє його constant-time і видає лише `service=bot`, без `user_id`.
  `X-Telegram-User-Id` не створює actor і не використовується для входу.
- Приватні `/internal/bot/*` endpoints — лише inbox accept, inbox claim і
  lease result. Actor/workspace/conversation/seed routes видалені; outbox
  claim повертає 404. Lease result також обмежений inbox delivery.
- Приватний API доступний лише в Docker network і завжди вимагає ключ;
  proxy блокує `/internal/*`, backend port не публікується. Між хостами
  потрібен TLS. Ключ зберігається в secrets backend/бота й має ротацію.
- Service key не автентифікує browser requests і не дає доступу до фінансів
  або завдань. Aiogram не доводить особу Mini App: `initData`, replay
  protection, cookie та CSRF перевіряє .NET.
- Bot token потрібний aiogram для Telegram API та backend для перевірки
  Mini App; webhook secret — лише aiogram. DB credentials, Data Protection
  keys і OpenRouter key доступні лише backend.

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
  Захист застосовується до всіх browser endpoints; бот їх не викликає.
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
