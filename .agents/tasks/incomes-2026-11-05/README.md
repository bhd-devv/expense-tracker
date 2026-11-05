# Доходи й окремі категорії

Стан: `completed`. Дата: 2026-11-05.

## Результат і критерії приймання

Навігація містить доходи замість звітів. Звіти відкриваються з огляду.
Доходи створюються, редагуються й видаляються у Web App; їхні категорії
мають окрему сторінку створення/редагування/видалення. Дані ізольовані за
власником, доходи не потрапляють до витрат, їхніх звітів і накопичень.

## Scope / non-goals

.NET API, PostgreSQL entities і additive migration, generated OpenAPI/TS,
Mini App uk/en та чинні теми. AI для доходів, зміни бота та деплой не входять.

## План

- [x] Прочитати правила й код, зберегти сторонні зміни дизайну завдань.
- [x] Реалізувати backend, міграцію й контракт.
- [x] Реалізувати навігацію, доходи та категорії доходів.
- [x] Виконати відповідні перевірки й оновити документацію.

## Рішення й ризики

Окремі IncomeCategories/Incomes, composite ownership FK, decimal суми,
owner lock і version. Створення має request ID; видалення залишає
службовий tombstone, щоб стара повторна спроба не відновила запис.
Міграція готується в коді; production БД і Coolify не змінюються.

## Виконання й перевірки

Дата перевірок: 2026-11-05. Докази — `output/incomes/` (ignored).

| Команда або сценарій | Результат | Доказ / обмеження |
| --- | --- | --- |
| `dotnet restore src/backend/ExpenseTracker.slnx --locked-mode` | passed | `restore.log` |
| `dotnet ef migrations add AddIncomes --project src/backend/ExpenseTracker.Api` | passed | `migration-generation.log`; EF tools 10.0.8 попереджає про runtime 10.0.12 |
| `dotnet build src/backend/ExpenseTracker.slnx -c Release --no-restore` | passed | `backend-build.log`, 0 warnings/errors |
| `dotnet format` лише змінених C# файлів | passed | `backend-format.log` |
| `dotnet test tests/ExpenseTracker.Tests -c Release --filter FullyQualifiedName~IncomeIsExactScopedIdempotentAndSeparateFromExpenses` | passed | `integration.log`; справжня ізольована PostgreSQL 14, нова міграція застосована |
| Повний `dotnet test ... --no-build --no-restore` | failed | `backend-tests.log`: 76 passed, 3 failed (401 у fixed-clock AI/task tests) |
| Ті самі 3 тести на незміненому HEAD | failed | `baseline-tests.log`: ті самі 401; окрема локальна БД |
| `python3 scripts/generate_contracts.py`, повтор і порівняння hashes | passed | `contracts.log`, `contract-drift.log`: drift none |
| `npm run typecheck`, `npm run lint`, Prettier check, `npm run build` | passed | відповідні `.log`; фінальна збірка |
| Chromium: створення/редагування доходу й категорії, delete confirmation, reports з огляду | passed | `browser-flow.log`, `browser-recovery.log`; synthetic API, фінальний стан перевірено окремим snapshot |
| 320/360/390/430/1280 × uk/en × light/dark: список/редактор/категорії | passed | `browser-matrix.log`: 60 layout checks; стабільні стани з reduced motion, overflow відсутній, buttons ≥44 px |
| Focus/Escape, 390×560, unknown response + exact retry після reload без дубля | passed | `browser-reload-recovery.log`; dialog top 10/bottom 550, поля прокручуються, footer видимий |
| `python3 scripts/check_docs.py --write-index`, `--check`, `git diff --check` | passed | `docs-index.log`, `docs-check.log`, `diff-check.log` |

## Продовження / результат

Реалізовано повний browser → .NET → PostgreSQL сценарій. Контракти:
[product](../../product.md), [architecture](../../architecture.md),
[ADR-021](../../knowledge/decisions.md). Історія:
[032-incomes](../../changelog/032-incomes.md).

Міграція не застосована до production. Docker images, production PostgreSQL 17,
реальні Telegram-клієнти, Safari й Coolify не перевірялися. Перед release
потрібен штатний backup/migrate/backend/frontend порядок. Нових env-змінних
і змін бота немає. Чужі незакомічені зміни дизайну завдань збережено.
