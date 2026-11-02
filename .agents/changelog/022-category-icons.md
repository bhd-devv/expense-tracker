# Каталог і зручна зміна іконок категорій

Дата: 2026-11-02. Задача: [каталог іконок](../tasks/category-icons-2026-11-02/README.md).

## Результат

Додано 100 іконок у 10 тематичних групах із назвами uk/en. Mini App
відкриває вибір натисканням іконки категорії: пошук, фільтр груп, поточний
вибір і збереження. Іконку також можна обрати у формі створення/редагування;
вона відображається в категоріях, витратах і звітах. Використано наявну
`lucide-react@1.66.0` (ISC, підтримує React 19), без нової залежності;
компоненти імпортуються явно.

Telegram пропонує групи й іконки кнопками з emoji, назвами та поточним
вибором; після створення категорії теж відкриває вибір. Backend володіє
спільним JSON-каталогом і дозволеними ID, перевіряє ownership/version.
Nullable `Category.Icon` зберігається незалежно від назви; старі клієнти
не скидають вибір. Синхронізовано OpenAPI і generated frontend/bot DTO.
Додано регресії persistence/ownership/version і bot callback flow.

## Перевірки

| Команда або сценарій | Результат | Межі / доказ |
| --- | --- | --- |
| `npm --prefix src/frontend run generate` | passed | DTO з checked-in OpenAPI; live API export не запускався |
| `.venv/bin/python -m datamodel_code_generator …` у `src/bot` | passed | Pydantic v2 DTO; повні параметри в task |
| Prettier `--write`, Ruff import formatting/format | passed | Форматування змінених файлів; не lint/typecheck |
| Перегляд JSON, явних імпортів Lucide та API paths | passed | 100 ID, 10 груп, без дублів і розбіжностей render map; `output/category-icons/content-review.log` |
| `python3 scripts/check_docs.py --write-index` | passed | `output/category-icons/docs-index.log` |
| `python3 scripts/check_docs.py --check` | passed | `output/category-icons/docs-check.log` |
| `git diff --check` | passed | `output/category-icons/diff-check.log` |
| Build, tests, lint, typecheck, browser, реальний Telegram/PostgreSQL/Coolify | skipped | За правилами репозиторію лише на явний запит |

## Сумісність і rollout

Additive migration `AddCategoryIcons` не застосовувалася. Перед backend
потрібен штатний migration job після backup, потім оновлення bot/frontend.
Відкритий старий Mini App може пропускати icon без втрати вибору; для нової
функції його слід перевідкрити. Старі callbacks захищені version checks.
При rollback бота новий `category_icon` діалог слід завершити або скинути.
Колонку можна залишити при image rollback; Down видаляє лише вибрані іконки.
Коміт, push, deployment і зміна реальної БД не виконувалися.

Контракти: [product](../product.md), [design](../design.md),
[Telegram](../telegram.md), [ADR-015](../knowledge/decisions.md),
[deployment](../../docs/deployment.md).
