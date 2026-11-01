# Детальне логування та щоденні архіви

Стан: `completed`. Дата: 2026-11-01.

## Результат і критерії приймання

Логи запитів, помилок, AI та Telegram delivery доступні в Coolify stdout.
Кожен сервіс має persistent log volume, UTC daily gzip rotation і retention.
Секрети, тіла запитів, нотатки, суми та транскрипти не логуються.

## Scope / non-goals

API, bot, gateway, frontend server, PostgreSQL та migrate; без зміни API/БД,
без browser analytics, deployment, реєстрації webhook або коміту.

## План

- [x] Прочитати правила й реалізацію.
- [x] Додати безпечні структуровані події й request correlation.
- [x] Додати спільний stdout/file runner із daily rotation та Compose volumes.
- [x] Перевірити зміни й оновити документацію.

## Рішення й ризики

Один runner на Python stdlib дублює stdout/stderr у файли без Docker socket
і без зовнішнього log service. Runtime images отримують Python; app business
logic залишається у відповідних сервісах. SIGTERM передається процесу.
Неможливо гарантувати збереження при втраті диска/хоста; потрібні backups.

## Виконання й перевірки

| Команда / сценарій | Результат | Доказ / обмеження |
| --- | --- | --- |
| Release backend build/tests із synthetic startup env | 43 passed, 7 skipped | `output/detailed-logging/backend-tests.txt`; без PostgreSQL |
| Bot Ruff check, mypy, форматування змінених файлів | Passed | Повний format check має окрему невідформатовану seed copy, не змінену тут |
| Bot suite | 14 passed, 1 skipped | `output/detailed-logging/bot-suite.txt`; виправлено некоректну keyboard fixture; real-backend test не запускався |
| Log runner stdlib suite | 3 passed | `output/detailed-logging/runner-tests.txt` |
| Compose config з synthetic env | Passed | `output/detailed-logging/compose.txt` |
| Docs index/check та diff check | Passed | Локальні посилання й формат |
| Images/Caddy runtime/Coolify | Не перевірено | Docker daemon недоступний |

## Результат

Завершено локально, без deployment чи коміту. Деталі й policy:
[changelog](../../changelog/016-detailed-logging-daily-archives.md),
[logging](../../../docs/logging.md). Перед release потрібні image build і
smoke named-volume прав, Caddy та фактичних Coolify logs. Один writer на
volume, stop-first release; не використовувати спільний файл для replicas.
