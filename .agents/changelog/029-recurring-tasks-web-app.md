# Окремий вебзастосунок регулярних завдань

Дата: 2026-11-03. Задача: [вебзастосунок завдань](../tasks/recurring-tasks-2026-11-03/README.md).

## Результат

Бот має reply-кнопку «✅ Завдання» з inline launch окремого `/tasks`.
Керування/відмітки доступні лише у вебзастосунку: створення, розклади
1/2/3/7 днів від старту, список на день/усі, минулі дати, progress,
перейменування й підтверджене видалення. Є uk/en, light/dark та спільна
Telegram session/CSRF. Витрати/категорії/накопичення/звіти не змінюються.

.NET перевіряє власника, версію, timezone й допустимість дня; owner lock,
PK/FK й interval constraint захищають стан у PostgreSQL. Невідома відповідь
залишає точний pending запит і блокує нові мутації до відомого результату
або відхилення явного повтору. Додано міграцію, OpenAPI/DTO та тести.
Правила web-only винятку зафіксовано в [ADR-019](../knowledge/decisions.md).

## Перевірки

| Команда або сценарій | Результат | Межі / доказ |
| --- | --- | --- |
| Перегляд коду й diff | reviewed | Без runtime перевірки |
| `npm run generate` у frontend | passed | Генерація зі збереженого OpenAPI |
| `datamodel-codegen --input ../../contracts/openapi.json ...` | passed | Python DTO; import order відформатовано |
| Prettier `--write`, Ruff `format` змінених файлів | completed | Не typecheck/lint |
| `python3 scripts/check_docs.py --write-index` і `--check` | passed | `output/recurring-tasks/docs-check.txt` |
| `git diff --check` | passed | `output/recurring-tasks/diff-check.txt` |
| Backend/frontend/bot build, tests, typecheck, lint | skipped | Потрібен явний запит за development.md |
| Браузер 320/360/390/430/desktop, uk/en × light/dark | skipped | Не запускався |
| Telegram, реальна PostgreSQL, міграція, Coolify | skipped | Не запускалися |

OpenAPI доповнено без запуску API; експорт і drift-check із Development
endpoint не виконано. TypeScript/Python DTO згенеровано зі збереженої схеми.
Додані тести залишаються неперевіреними.

## Сумісність і rollout

Additive `20261103000000_AddRecurringTasks` перед новим backend, потім
frontend і bot. Нових env/secrets/services немає. `/start` оновлює reply-меню;
старі фінансові inline launch лишаються чинними. Існуючі діалоги не змінені.
Task endpoints є тільки в browser API, не в private bot API.
Image rollback лишає таблиці й історію; Down навмисно відхилено.
Міграція, коміт, push, deployment і Telegram registration не виконувались.
