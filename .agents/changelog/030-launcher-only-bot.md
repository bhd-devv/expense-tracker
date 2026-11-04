# Бот лише відкриває витрати та завдання

Дата: 2026-11-04. Задача: [launcher-only](../tasks/launcher-only-2026-11-04/README.md).

## Результат

Замість команд і фінансових діалогів бот повертає рівно дві inline Web App
кнопки. Видалено chat handlers, callbacks, завантаження голосу, seed service,
actor/workspace/conversation API та outbox notifications. Service credential
працює лише з inbox; вхід у Web Apps залишається через verified initData,
cookie й CSRF. Оновлено OpenAPI, generated DTO, regression тести й правила.
Схема БД без змін; legacy Conversation/DemoSeed та наявні дані зберігаються.

## Перевірки

| Команда або сценарій | Результат | Межі / доказ |
| --- | --- | --- |
| Генерація TS/Python DTO з локальної OpenAPI-схеми | виконано | Не live export; `output/launcher-only/bot-openapi.json` |
| Ruff format змінених Python-файлів | виконано | Форматування, без lint/tests |
| `python3 scripts/check_docs.py --write-index` | passed | `output/launcher-only/docs-index.log` |
| `python3 scripts/check_docs.py --check` | passed | `output/launcher-only/docs-check.log` |
| `git diff --check` | passed | `output/launcher-only/diff-check.log` |
| Build/tests/lint/typecheck/browser/Telegram/PostgreSQL/Coolify | skipped | Не запитані; за правилами development не запускались |

Нова міграція відсутня; реальні клієнти Telegram, браузер, PostgreSQL,
Coolify та live OpenAPI export не перевірялися.

## Сумісність і rollout

Stop-first оновлення backend/бота, потім явний register-webhook очищує
старі command menus і додаткову Web App menu button. Повідомлення користувача
прибирає стару reply keyboard. Старі callbacks не працюють; існуючі inline
launch та browser sessions зберігаються. Pending updates не видаляються.
Rollback — сумісні images та перегляд старого outbox перед поверненням
старого worker. Коміт, push, міграції, registration і deploy не виконувались.
Рішення — [ADR-020](../knowledge/decisions.md).
