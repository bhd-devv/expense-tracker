# Місячні ліміти категорій і смужки використання

Дата: 2026-11-02. Задача: [ліміти категорій](../tasks/category-limits-2026-11-02/README.md).

## Результат

Mini App дозволяє встановити/змінити/прибрати ліміт суми та валюти
безпосередньо під відповідною категорією. Показує поточний місяць,
витрачено/ліміт, відсоток і native progress із accessible value text.
Перевищення має текст і колір danger; смужка максимум 100%, підпис може
бути понад 100%. Без ліміту немає фіктивного 0%. Стан inline rename та
створення категорії збережений; оновлення ліміту синхронізує версію rename.

Бот пропонує валюту кнопками, введення суми та прибирання ліміту, показує
відсоток і текстову смужку в каталозі/деталях. Durable діалоги й callbacks
перевіряють версії; введення суми не потрапляє в AI-створення витрати.

Backend володіє правилами: поточний календарний місяць/timezone профілю,
лише збережені витрати відповідної категорії/валюти. Decimal суми, money
strings в API, decimal відсоток до одного дробового знака й точний exceeded.
Чернетки та інші валюти не враховуються; перевищення не блокує витрати.
Прогрес перераховується при читанні каталогу після змін витрат і місяця.
Окремий PUT limit та nullable колонки з constraint не дозволяють старим
запитам назви/іконки скидати ліміт. Синхронізовано OpenAPI і generated DTO.

## Перевірки

| Команда або сценарій | Результат | Межі / доказ |
| --- | --- | --- |
| `npm --prefix src/frontend run generate` | passed | Types із checked-in OpenAPI; live export/drift check не запускався |
| `.venv/bin/python -m datamodel_code_generator …` у `src/bot` | passed | Pydantic v2 DTO; повні параметри в task |
| Prettier `--write`, Ruff import formatting/format | passed | Форматування, не повний lint/typecheck |
| Перегляд коду/контракту/міграції | passed | Лише зміст; `output/category-limits/content-review.log` |
| `python3 scripts/check_docs.py --write-index` | passed | `output/category-limits/docs-index.log` |
| `python3 scripts/check_docs.py --check` | passed | `output/category-limits/docs-check.log` |
| `git diff --check` | passed | `output/category-limits/diff-check.log` |
| Build, tests, lint/typecheck, browser, PostgreSQL/Telegram/Coolify | skipped | За правилами репозиторію лише за явним запитом |

Додано, але не запущено PostgreSQL-регресію валюти/меж місяця за timezone,
draft exclusion, перевищення, редагування/видалення витрат, ownership/version,
невалідних сум, старих клієнтів, зміни профілю й нового місяця. Bot fake
transport регресія uk/en покриває вибір валюти, суму з комою, 125%, прибирання
й застарілі callbacks. Міграція не застосовувалася; реальні дані не змінені.

## Сумісність і rollout

Після backup штатний migration job має застосувати `AddCategoryLimits`
перед backend, потім оновити frontend/bot. Старий Mini App може ігнорувати
limit; для нових дій його потрібно перевідкрити. Старі запити назви/іконки
зберігають ліміт, старі callbacks перевіряють спільну category version.
Перед rollback бота завершити/скинути `category_limit_currency` та
`category_limit` діалоги. Колонки можна залишити при image rollback;
Down прибирає лише налаштування лімітів. Коміт/push/deployment не виконано.

Контракти: [product](../product.md), [design](../design.md),
[Telegram](../telegram.md), [ADR-017](../knowledge/decisions.md),
[deployment](../../docs/deployment.md).
