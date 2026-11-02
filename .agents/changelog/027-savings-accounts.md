# Накопичувальні рахунки з поповненнями й списаннями

Дата: 2026-11-02. Задача: [накопичення](../tasks/savings-accounts-2026-11-02/README.md).

## Результат

З головної Mini App доступні створення накопичувальних рахунків, залишки,
поповнення, списання, дата/нотатка та історія з пагінацією. Повний сценарій
є в reply/inline UX бота. .NET володіє правилами доступу, точними сумами,
конкурентним залишком та ідемпотентністю. Журнал окремий від категорій,
звичайних витрат і звітів; валюта-позначення береться з профілю.
Оновлено OpenAPI та згенеровані TypeScript/Pydantic consumers.

## Перевірки

| Команда або сценарій | Результат | Межі / доказ |
| --- | --- | --- |
| openapi-typescript; datamodel-codegen | passed | Generated consumers зі збереженої схеми; export із живого backend не запускався |
| Prettier; Ruff format/import ordering | passed | Форматування змінених файлів |
| `python3 scripts/check_docs.py --check` | passed | `output/savings-accounts/documentation.log` |
| `git diff --check` | passed | `output/savings-accounts/diff-check.log` |
| Build/typecheck/lint/tests/browser | skipped | За правилами лише за явним запитом |

Додано PostgreSQL-тест конкурентних списань, ownership, decimal-точності,
ідемпотентності, пагінації та спільного bot/browser API. Bot-тест перевіряє
втрату відповіді й повтор ключа; тести не запускалися. Реальні Telegram,
PostgreSQL, застосування міграції й Coolify не перевірялися.

## Сумісність і rollout

Additive migration `AddSavingsAccounts` перед backend, далі обидва канали.
Витрати не мігрують у журнал. Старі клієнти можуть працювати без нового
розділу. Prefix `sv` окремий від старих account callbacks. Перед rollback
бота завершити savings conversations; таблиці не видаляти. Down відхиляє
втрату журналу; image rollback не потребує зворотної міграції.
Коміт, push, deploy та реєстрація webhook не виконувалися.
