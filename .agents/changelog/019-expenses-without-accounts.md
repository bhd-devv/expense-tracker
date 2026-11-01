# Витрати без фінансових рахунків

Дата: 2026-11-02. Задача: [спрощення моделі](../tasks/expenses-without-accounts-2026-11-02/README.md).

## Результат

За запитом користувача прибрано фінансові рахунки: EF entity/table,
посилання у витраті/чернетці/профілі, каталог і endpoints, фільтри,
сторінку Mini App, bot menu/handlers та переклади. Лишилися акаунт
користувача, витрати з категоріями та звіти; надходжень і балансів немає.

Валюта належить самій витраті/чернетці. Нові чернетки беруть валюту профілю,
AI може визначити явно вказану підтримувану валюту; редагування доступне в
обох каналах. Зміна профілю не конвертує історію. Seed додає 70 витрат за
жовтень 2026 з UAH/USD/EUR та потрібні категорії, без демокарток/гаманців.
Один DemoSeeds receipt на користувача/період продовжує захищати від дублів.

Міграція `RemoveFinancialAccounts` переносить валюту старих чернеток,
прибирає каталог і зв'язки, нормалізує idempotency fingerprints і скидає
стару bot navigation. Витрати, профілі, категорії та seed receipts зберігаються.
Старі кнопки прибраного каталогу відхиляються; стара reply keyboard оновлюється
без помилкового створення AI-чернетки. OpenAPI оновлено відповідно до DTO,
TypeScript/Pydantic згенеровано з цього контракту. Чинне рішення — ADR-013.

## Перевірки

| Команда або сценарій | Результат | Межі / доказ |
| --- | --- | --- |
| Аналіз коду, пошук залежностей, перегляд diff | Виконано | Прибрано active financial account model, збережено тільки legacy migration/compatibility |
| `npm run generate` | Виконано | Frontend types із checked-in OpenAPI; live API export не перевірено |
| `uv run --frozen --offline datamodel-codegen ...` | Виконано | Bot DTO з того самого OpenAPI |
| Prettier `--write`, Ruff `format` | Виконано | Змінені frontend/bot файли відформатовано |
| Backend/PostgreSQL та Fake Telegram regressions | Оновлено/додано, не запущено | Міграція в ізольованій схемі `_test`, валюти, seed, ownership та старі bot дії |
| Build/tests/lint/typecheck/browser/контейнери | Не запускалися | Збережено правило ручних перевірок лише за запитом |
| `python3 scripts/check_docs.py --write-index`, `--check` | passed | `output/expenses-without-accounts/docs-index.log`, `docs-check.log` |
| `git diff --check` | passed | `output/expenses-without-accounts/diff-check.log` |

Браузер, Telegram-клієнт, справжня PostgreSQL, застосування міграції та
Coolify після зміни не перевірялися. Реальних Telegram/AI запитів не було.

## Сумісність і rollout

Потрібні backup та stop-first release: зупинити старі backend/bot,
застосувати міграції, запустити нові backend/bot/frontend. Старий контракт
несумісний із видаленими полями та новим required `currency` для mutations;
відкритий Mini App треба повторно відкрити. `/start` оновлює reply menu,
незавершені bot steps скидаються, самі чернетки зберігаються з новою версією.
Зворотна міграція явно відхиляється: колишні назви/зв'язки рахунків можна
повернути лише з backup разом зі старими images. Image rollback недостатній.
Порядок — у [deployment](../../docs/deployment.md). Коміт, push, deploy,
production migration та webhook registration не виконувалися.
