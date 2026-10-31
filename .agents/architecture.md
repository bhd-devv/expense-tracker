# Архітектура

## Компоненти

```text
Telegram Mini App (Next.js) ── HTTPS /api/* ──┐
                                           v
Telegram Bot API ── HTTPS webhook ──> ASP.NET Core ──> PostgreSQL
                                           |
                                   спільні сценарії
                                           |
                                   outbox worker ──> Telegram Bot API
```

Backend: підтримувана LTS .NET / ASP.NET Core, nullable reference types,
EF Core із PostgreSQL provider, офіційні механізми DI, Options validation,
logging, ProblemDetails, Authentication/Authorization і Antiforgery.
Для Telegram використовуй підтримувану бібліотеку `Telegram.Bot`.
Під час створення scaffold зафіксуй актуальні сумісні SDK і версії пакетів.

Frontend: Next.js App Router, React, strict TypeScript. Server Components
за замовчуванням; Telegram SDK та інтерактивність — у вузьких client boundaries.
Mini App передає автентифікаційні дані backend і працює з API через той самий
origin. Не перенось бізнес-операції або доступ до БД у Next.js Server Actions.

Для MVP бот є feature того самого ASP.NET Core застосунку. Webhook endpoint
перетворює update на команду, а bot handlers викликають прикладні сценарії
напряму з перевіреним контекстом користувача. Не роби внутрішній HTTP-запит
до власного API й не додавай окремий сервіс без практичної потреби.

## Цільова структура

```text
AGENTS.md
.agents/
src/
  backend/
    ExpenseTracker.Api/
      Features/
        Auth/
        Users/
        Expenses/
        Accounts/
        Categories/
        Reports/
        Telegram/
      Infrastructure/
        Persistence/
      Program.cs
    ExpenseTracker.slnx
  frontend/
    src/
      app/
      features/
      components/
      lib/
tests/
  ExpenseTracker.Tests/
  ExpenseTracker.IntegrationTests/
deploy/
  compose.yaml
.env.example
```

Це орієнтир для майбутньої реалізації. Не створюй порожні папки, assemblies,
інтерфейси або загальні CRUD-абстракції лише заради структури. Організовуй
сценарій поруч із його DTO, валідацією й endpoint; розділяй за відповідальністю,
коли це допомагає читати код. Не додавай generic repository поверх DbContext,
MediatR, Redis або message broker без підтвердженої потреби.

## Межі й контракти

- Endpoints і bot handlers тонкі: вхідні дані, auth context, виклик сценарію,
  перетворення результату у HTTP або Telegram-повідомлення.
- Один прикладний сценарій створення/редагування витрати обслуговує обидва
  канали. Перевірки суми, власника, валюти й категорії живуть тут і в БД.
- Власника визначає перевірений серверний контекст; `userId` із клієнтського
  JSON, callback або URL не є джерелом повноважень.
- Публічний контракт API — OpenAPI з backend. Генеруй TypeScript DTO/client
  із нього, перевіряй актуальність генерації. Валідуй зовнішній JSON на межі;
  згенеровані типи самі по собі не перевіряють runtime-дані.
- API повертає стабільні коди помилок через ProblemDetails. Канали локалізують
  повідомлення для користувача, не аналізуючи текст exception.
- Пагінація, ліміти та підтримувані фільтри явні; не завантажуй усю історію
  заради одного екрану чи Telegram-повідомлення.

## Узгодженість і повторна доставка

PostgreSQL — єдине джерело стану: записи, сесії, bot conversations,
отримані updates, використані auth proofs та outbox. Міграції EF Core
версіонуються разом із кодом. Унікальний constraint на Telegram user ID
захищає від створення двох користувачів під час одночасного першого входу.

Зміна фінансових даних і запис вихідної події/повідомлення виконуються в
короткій транзакції. Публікуй ефекти після commit. Outbox worker має явні
таймаути, обмежені retries, backoff і видимий стан невдалих повідомлень.
Доставка Telegram може дати дубль після невідомої мережевої відповіді;
не обіцяй exactly-once delivery і не повторюй фінансову операцію через це.

Для створення витрати Mini App генерує idempotency key на одну підтверджену
операцію та зберігає його до встановлення результату. Backend зберігає key,
власника, тип операції, hash нормалізованих вхідних даних і результат атомарно
зі зміною. Повтор із тим самим key та іншими даними повертає конфлікт.
У боті одноразовий ID підтвердження чернетки виконує ту саму роль; новий
`update_id` повторного натискання не повинен створювати ще одну витрату.

Idempotency key не є дозволом на прихований retry. Після невідомої відповіді
спочатку встанови результат або запропонуй явну повторну дію з тим самим key.
Не тримай транзакції відкритими під час виклику Telegram або іншого HTTP API.
