# Правила агентної роботи для трекера витрат

Дата: 2026-10-31. Задача: правила роботи з `jordan-webapp-render`.

## Результат

Описано процес для нашого трекера витрат: один changelog на завершену
змістовну задачу, один task-файл для великої роботи, короткий `STATUS.md`,
ADR для меж системи, розділення чинних контрактів та історії, автоматична
перевірка документації й правила збереження доказів. Вимоги прив'язані до
впливу на API, Next.js Mini App, Telegram-бота та PostgreSQL.

Задіяні лише агентні документи, індекс людської документації, перевіряльний
скрипт і `.gitignore`. Backend, frontend, bot handlers та схема БД не змінювалися.

## Перевірки

| Команда або сценарій | Результат | Межі / доказ |
| --- | --- | --- |
| `python3 scripts/check_docs.py --write-index` | passed | Згенеровано `docs/changes.md` |
| `python3 scripts/check_docs.py --check` | passed | Перевірено локальні посилання, формат changelog та обов'язкові файли |
| `python3 -m py_compile scripts/check_docs.py` | passed | Перевірено лише Python-синтаксис перевіряльного скрипта |
| `git diff --check` | passed | Перевірено whitespace у diff |

Browser, реальний Telegram, PostgreSQL, міграції та Coolify не перевірялися,
бо ця задача змінює лише правила документації.

## Сумісність і rollout

Міграцій, API-змін, змін Mini App або Telegram UX немає. Під час майбутніх
backend-релізів task/changelog мають окремо описувати вже відкриті Mini App,
старі inline-кнопки, незавершені bot dialogs, порядок rollout і rollback.
Коміт, push та deployment не виконувалися.
