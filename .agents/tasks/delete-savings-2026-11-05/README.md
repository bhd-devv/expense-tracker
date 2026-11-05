# Видиме видалення накопичувальних рахунків

Стан: `completed`. Дата: 2026-11-05.

## Результат і критерії приймання

Видимий кошик поруч із рахунком у списку та текстова дія всередині рахунка.
Видалення з підтвердженням доступне також для рахунків із ненульовим
залишком/історією. Після успіху рахунок і журнал недоступні у Web App.

## Scope / non-goals

Savings API/БД, frontend uk/en і теми, generated contract, тести й документи.
Інші фінансові операції/бот/deploy не змінюються.

## План

- [x] Прочитати наявний Savings сценарій і правила.
- [x] Додати version/tombstone, DELETE і migration.
- [x] Додати видимі кнопки/підтвердження/unknown retry.
- [x] Перевірити PostgreSQL та UI й оновити контракти/changelog.

## Рішення й ризики

Owner lock і version не дозволяють видалити рахунок із незазначеною
користувачем конкурентною операцією. Tombstone зберігає ID від старого
create retry; записи журналу недоступні через API. Перед release потрібна
міграція; старий backend не повинен працювати одночасно з новим.

## Виконання й перевірки

Дата перевірок: 2026-11-05. Докази — `output/delete-savings/` (ignored).

| Команда або сценарій | Результат | Доказ / обмеження |
| --- | --- | --- |
| `dotnet ef migrations add AddSavingsDeletion --project src/backend/ExpenseTracker.Api` | passed | `migration-generation.log`; EF tools 10.0.8/runtime 10.0.12 warning |
| `dotnet format` лише змінених C# | passed | `backend-format.log` |
| `dotnet build src/backend/ExpenseTracker.slnx -c Release --no-restore` | passed | `backend-build.log`; 4 наявні xUnit2031 warnings в інших тестах |
| `dotnet test tests/ExpenseTracker.Tests -c Release --no-build --no-restore --filter FullyQualifiedName~Savings` | passed | `integration.log`: 3/3; реальна ізольована PostgreSQL 14, міграція застосована до наявної тестової БД |
| Генерація контрактів + повторна генерація/порівняння hashes | passed | `contracts.log`, `contract-drift.log`: drift none |
| Frontend typecheck/lint/format/build | passed | відповідні `.log` |
| Chromium list/detail 320/360/390/430/1280 × uk/en × light/dark | passed | `browser-matrix.log`: 40 layout checks, видимі кнопки ≥44 px, без overflow |
| Скасування, видалення з історією, unknown response/reload/exact retry, кошик порожнього рахунка | passed | `browser-recovery.log`, `browser-list-delete.log`; synthetic API; screenshot-файли |
| Docs index/check і `git diff --check` | passed | `docs-index.log`, `docs-check.log`, `diff-check.log` |

## Продовження / результат

Реалізовано API/UI/persistence. [Changelog](../../changelog/033-delete-savings.md),
[ADR-022](../../knowledge/decisions.md). Попередні зміни доходів і сторонні
зміни дизайну завдань збережено.

Production БД/міграція, Telegram WebView, Docker images і Coolify не перевірялися.
Повний backend suite тут не повторювався: попередньо відомі 3 fixed-clock 401
відтворювалися також на HEAD; scope цієї перевірки — Savings. Перед release
потрібні backup, міграція, stop старого backend і новий backend/frontend.
Down відхилено; older backend, що ігнорує Deleted, поверне приховані рахунки.
Коміт/push/deploy не виконувалися.
