# Доходи з окремими категоріями

Дата: 2026-11-05. Задача: [доходи](../tasks/incomes-2026-11-05/README.md).

## Результат

У mobile/desktop навігації «Доходи» замінили «Звіти»; звіти відкриваються
кнопкою з огляду. Додано ручне створення, редагування, підтверджене видалення,
фільтри, пагінацію й підсумок доходів. Категорії доходів мають окрему сторінку
назви/іконки/створення/редагування/видалення, доступну також із категорій.
Витрати, їхні категорії/ліміти/звіти та накопичення лишаються незалежними.

.NET browser API `/api/incomes` та `/api/income-categories` використовують
session/CSRF, owner locks, version, точні decimal суми й composite FK.
Request ID/hash і tombstones захищають створення від дублів/відновлення
старим запитом. Невідома відповідь зберігає точний запит у scoped
sessionStorage, блокує нові мутації та дозволяє явний повтор. UI підтримує
uk/en, обидві теми, native modal, focus/Escape та короткі viewport.
Generated OpenAPI/TypeScript синхронізовано з поточним API; Python inbox
моделі без змін. Продукт, архітектуру, дизайн та ADR-021 оновлено.

## Перевірки

| Команда або сценарій | Результат | Межі / доказ |
| --- | --- | --- |
| Locked restore, Release build, format змінених C# | passed | `output/incomes/restore.log`, `backend-build.log`, `backend-format.log` |
| Цільовий PostgreSQL тест доходів | passed | `output/incomes/integration.log`; additive міграція застосована до окремої локальної PostgreSQL 14 |
| Повний .NET suite | failed | `output/incomes/backend-tests.log`: 76 passed / 3 failed; ті самі 401 відтворено на HEAD у `baseline-tests.log` |
| Frontend typecheck/lint/format/build | passed | `output/incomes/`; фінальна збірка |
| Генерація контрактів і повторна генерація | passed | `output/incomes/contract-drift.log`, drift none |
| Chromium synthetic API flow + layout matrix | passed | `output/incomes/browser-matrix.log`: 20 комбінацій, 60 layout checks; `browser-flow.log` і `browser-reload-recovery.log` |
| Docs index/check, `git diff --check` | passed | `output/incomes/docs-index.log`, `docs-check.log`, `diff-check.log` |

Реальні Telegram-клієнти/Safari, Docker images, production PostgreSQL 17
і Coolify не перевірялися. Браузерний smoke використовував synthetic API;
серверні інваріанти перевірено окремо на PostgreSQL.

## Сумісність і rollout

`20261105150655_AddIncomes` — additive, перед новим backend і frontend
через штатний migrate job після перевірки backup. Нових secrets/env/сервісів
немає; бот не змінюється. Старі Mini App витрат сумісні; для нової навігації
потрібне оновлення frontend. Image rollback лишає фінансові таблиці;
Down відхиляє видалення історії. Production міграція, коміт, push і деплой
не виконувалися. Докладний порядок — у [deployment](../../docs/deployment.md).
