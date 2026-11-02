# Видалення категорій і витрат поряд із записами

Дата: 2026-11-02. Задача: [видалення у списках](../tasks/inline-delete-2026-11-02/README.md).

## Результат

Mini App показує сусідні кнопки редагування й видалення в кожному рядку
категорії та витрати, також на огляді. Кнопку видалення прибрано знизу
редактора витрати; archive/restore дії прибрано з категорій. На телефоні
сума під описом звільняє місце для двох зон 44×44 px; desktop зберігає колонки.
Видалення має підтвердження з назвою запису, loading/error handling і
оновлює список; видалення відкритої для редагування категорії скидає форму.

У боті кнопка видалення стоїть поряд із записом, відкриває підтвердження
й оновлює список після успіху. Категорії мають пагінацію по 20, щоб клавіатура
вкладалась у Telegram-ліміт; старі archive callbacks відхиляються.

Додано спільний backend DELETE категорії для Mini App і бота. Перевірки
ownership, version, пов'язаних витрат і активних чернеток виконуються під
user lock. `category_in_use` має окреме uk/en повідомлення; витрати каскадом
не видаляються. OpenAPI та frontend types оновлено; Python DTO не змінюються,
оскільки нових schemas немає. Нової міграції немає. Попередні локальні зміни
іконок збережено.

## Перевірки

| Команда або сценарій | Результат | Межі / доказ |
| --- | --- | --- |
| `npm --prefix src/frontend run generate` | passed | Types із checked-in OpenAPI; live export/drift check не запускався |
| Prettier `--write` змінених frontend файлів | passed | Форматування, не lint/typecheck |
| Ruff `format` handlers/copy/integration tests | passed | Форматування, не lint/typecheck |
| Перегляд коду, OpenAPI, callback layouts | passed | `output/inline-delete/content-review.log`; не runtime доказ |
| `python3 scripts/check_docs.py --write-index` | passed | `output/inline-delete/docs-index.log` |
| `python3 scripts/check_docs.py --check` | passed | `output/inline-delete/docs-check.log` |
| `git diff --check` | passed | `output/inline-delete/diff-check.log` |
| Build, tests, lint/typecheck, browser, реальний Telegram/PostgreSQL/Coolify | skipped | За правилами репозиторію лише за явним запитом |

Додано, але не запущено PostgreSQL-регресію ownership/version, active draft
та expense blockers, збереження звітів, успішне видалення через bot API й
повторний DELETE. Bot fake transport покриває uk/en списки, підтвердження,
скасування, повторні callbacks, `category_in_use` та пагінацію 50 категорій.
Міграції й видалення реальних даних не виконувалися.

## Сумісність і rollout

Спочатку backend з новим DELETE, потім frontend/bot; Mini App перевідкрити.
Схема незмінна від попередньої задачі іконок, її міграція все ще потрібна.
Legacy `archived` API/дані залишаються сумісними; новий bot відхиляє старі
archive callbacks. Старі delete callbacks витрат проходять ті самі перевірки
версій; після видалення повтор повертає not_found/stale. Незавершені чернетки
не переписуються; посилання активної чернетки блокує видалення категорії.
Image rollback не повертає видалені записи. Коміт/push/deployment не виконано.

Контракти: [product](../product.md), [design](../design.md),
[Telegram](../telegram.md), [ADR-016](../knowledge/decisions.md),
[deployment](../../docs/deployment.md).
