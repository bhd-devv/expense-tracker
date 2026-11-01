# Username бота з Telegram замість окремої конфігурації

Дата: 2026-11-01. Задача: не застосовується — завершена зміна конфігурації.

## Результат

Backend визначає username бота через Telegram `getMe` за bot token під час
першого `/api/config`. Успішна відповідь кешується до перезапуску; паралельні
запити використовують один lookup. Timeout — 10 секунд, відповідь обмежена
64 КіБ. Невалідна відповідь, HTTP/network failure або timeout повертає 503
`telegram_unavailable`; наступний запит може виконати lookup знову.
Скасування HTTP-запиту передається далі. HTTP logging для Telegram client
вимкнено, transport exceptions не розкривають token-bearing URL.

Прибрано `TELEGRAM_BOT_USERNAME` із Compose, `.env.example` і локального
ігнорованого `.env`, а `Telegram:BotUsername` — з appsettings. Інші локальні
env-значення збережені. Оновлено README та deployment contract. Нових
залежностей немає; використано штатний `IHttpClientFactory`.

Форма відповіді `/api/config` не змінилася. Mini App отримує ту саму адресу
бота; код aiogram, бізнес-правила, сесії та схема БД не змінені. Запуск
backend і міграції не викликають `getMe`.

## Перевірки

Команди .NET запускалися з `DOTNET_CLI_HOME=/private/tmp/expense-tracker-dotnet`,
`NUGET_PACKAGES=/private/tmp/expense-tracker-nuget` і вимкненою CLI telemetry.
Логи — `output/telegram-bot-identity/`.

| Команда або сценарій | Результат | Межі / доказ |
| --- | --- | --- |
| `dotnet restore src/backend/ExpenseTracker.slnx --locked-mode` | passed | `restore.log`, lockfiles не змінені |
| `dotnet build src/backend/ExpenseTracker.slnx -c Release --no-restore` | passed | `build.log`, 0 warnings/errors |
| `dotnet test tests/ExpenseTracker.Tests -c Release --no-restore` | passed | `tests.log`: 26 passed, 5 PostgreSQL tests skipped без тестової БД |
| Нові Telegram identity тести | passed | Підмінний HTTP handler: спільний lookup, кеш, невалідні відповіді, network failure, відсутність token у помилці, cancellation і відновлення |
| `dotnet format src/backend/ExpenseTracker.slnx --verify-no-changes --no-restore` | passed | `format.log` |
| `docker compose --env-file .env.example -f deploy/compose.yaml config --quiet` | passed | Без реальних secrets і без daemon |
| `docker compose --env-file .env.example -f deploy/compose.yaml build backend` | blocked | `container-build.log`: Docker daemon не запущений |
| `python3 scripts/check_docs.py --write-index`, `--check`; `git diff --check` | passed | Generated index, локальні посилання й whitespace |

Браузер, живий Telegram, PostgreSQL, міграції та Coolify не перевірялися.
Frontend і bot перевірки не повторювалися: їхня реалізація й DTO не змінені.

## Сумісність і rollout

Окремий username у Coolify більше не потрібний. Після зміни username в
BotFather перезапустіть backend, щоб оновити кеш. При недоступності Telegram
перший lookup не зможе повернути посилання до успішного наступного запиту.
Старі callbacks, відкриті Mini App і bot dialogs сумісні. Для rollback на
попередній backend поверніть його змінну username. Міграція БД не потрібна.
Коміт, push, deploy і реєстрація webhook не виконувалися.
