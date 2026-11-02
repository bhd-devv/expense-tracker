# Архітектура

## Компоненти

```text
Telegram Mini App (Next.js) ── HTTPS /api/* ──> ASP.NET Core ──> PostgreSQL
                                                  ^
Telegram Bot API ── HTTPS webhook ──> Python / aiogram
                                     приватний API
Telegram Bot API <── aiogram <── outbox через приватний API
```

Backend: підтримувана LTS .NET / ASP.NET Core, nullable reference types,
EF Core із PostgreSQL provider, офіційні механізми DI, Options validation,
logging, ProblemDetails, Authentication/Authorization і Antiforgery.
Під час створення scaffold зафіксуй актуальні сумісні SDK і версії пакетів.

Frontend: Next.js App Router, React, strict TypeScript. Server Components
за замовчуванням; Telegram SDK та інтерактивність — у вузьких client boundaries.
Mini App передає автентифікаційні дані backend і працює з API через той самий
origin. Не перенось бізнес-операції або доступ до БД у Next.js Server Actions.

Telegram-бот — окремий Python-сервіс на підтримуваній aiogram 3.x.
Використовуй штатні Dispatcher, Router, filters, middleware, CallbackData
та keyboard builders за потребою. Для webhook використовуй інтеграцію
aiogram з aiohttp; не додавай другий web framework лише для цього endpoint.
Бот приймає updates, керує кнопковим UX і викликає приватний .NET API
через один async HTTP client із таймаутами та connection pooling.
Голосовий файл завантажуй штатним aiogram client із перевіркою лімітів і
передавай backend як аудіодані; не передавай token-bearing Telegram URL.

Усі бізнес-правила, транскрипція/категоризація через OpenRouter, фінансові
транзакції та доступ до PostgreSQL належать .NET backend. Python не має
прямого доступу до таблиць, не повторює ownership/грошові правила й не
стає другим backend. Межа довіри описана в [security.md](security.md).

Текстові й голосові описи проходять спільний сценарій створення AI-чернетки
на backend із викликами OpenRouter для транскрипції та категоризації.
Детальний контракт — у [product.md](product.md). Виклики AI мають таймаути,
ліміти аудіо/запитів і виконуються поза транзакціями БД. Результат належить
конкретній версії чернетки: запізніла відповідь не відновлює скасовану
чернетку й не перезаписує виправлення користувача. Перед збереженням витрати
backend повторно перевіряє всі інваріанти.

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
        Categories/
        Reports/
        BotIntegration/
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
  bot/
    pyproject.toml
    uv.lock
    src/
      expense_tracker_bot/
        handlers/
        keyboards/
        api.py
        main.py
    tests/
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

- Endpoints і aiogram handlers тонкі: вхідні дані, auth context, виклик
  backend-сценарію, перетворення результату у HTTP або Telegram-повідомлення.
- Один прикладний сценарій створення/редагування витрати обслуговує обидва
  канали. Перевірки суми, власника, валюти й категорії живуть тут і в БД.
- Власника визначає перевірений серверний контекст; `userId` із клієнтського
  JSON, callback або URL не є джерелом повноважень.
- Публічний контракт API — OpenAPI з backend. Генеруй TypeScript DTO/client
  із нього, перевіряй актуальність генерації. Валідуй зовнішній JSON на межі;
  згенеровані типи самі по собі не перевіряють runtime-дані.
- Приватні bot endpoints теж мають OpenAPI-контракт. Не підтримуй дві
  ручні копії DTO в C# і Python: генеруй потрібні Python-моделі з backend
  схеми та валідуй відповіді. Не додавай ще один універсальний RPC/SDK шар.
- API повертає стабільні коди помилок через ProblemDetails. Канали локалізують
  повідомлення для користувача, не аналізуючи текст exception.
- Пагінація, ліміти та підтримувані фільтри явні; не завантажуй усю історію
  заради одного екрану чи Telegram-повідомлення.

## Узгодженість і повторна доставка

PostgreSQL — єдине джерело стану: записи, сесії, bot conversations,
отримані updates, використані auth proofs та outbox. Міграції EF Core
версіонуються разом із кодом; власник схеми й усіх записів — .NET backend.
Бот приймає/claim-ить inbox і outbox через приватний API; механіка
доставки описана в [telegram.md](telegram.md). Унікальний constraint на Telegram user ID
захищає від створення двох користувачів під час одночасного першого входу.

Зміна фінансових даних і запис вихідної події/повідомлення виконуються в
короткій транзакції. Публікуй ефекти після commit. Aiogram worker доставляє
підготовлені backend події через Telegram Bot API. Він має явні
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

## Окремий журнал накопичень

Feature `Savings` на .NET обслуговує `/api/savings` і приватні
`/internal/bot/actor/savings` через ті самі use cases. GET списку, POST
створення та GET/POST `/{id}/entries`; усі запити actor-scoped.
`SavingsAccounts` зберігає власника, назву, legacy валюту, decimal-підсумки
Added/Spent (`numeric(28,4)`). `SavingsEntries` — незмінні операції із
`numeric(20,4)` сумою та composite FK `(UserId, AccountId)` до рахунка.
Account/entry ID є request ID; дані незмінні, тому окремий hash не потрібен.
Owner advisory lock захищає залишок, totals+entry зберігаються одним commit.
DB checks захищають додатну суму, kind і невід'ємний залишок. Зв'язків із
Expense/Category немає. Деталі — [product](product.md).

Валюта savings DTO завжди з профілю; legacy account/request Currency не
перевизначає позначення. Це узгоджено зі спільними налаштуваннями валюти.
