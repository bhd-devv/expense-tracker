# Детальні логи в Coolify та щоденні gzip-архіви

Дата: 2026-11-01. Задача: [логування](../tasks/detailed-logging-2026-11-01/README.md).

## Результат

API логує HTTP start/result, status/duration, correlation IDs, auth/CSRF
відмови, збережені мутації й idempotent replay. Додані AI/audio/Telegram
identity події, maintenance counts, inbox/outbox acceptance/claim/result.
Помилки мають типи, stack locations, inner causes та безпечні коди без
довільного exception text, SQL або приватних payloads.

Бот логує webhook, menu/callback/dialog actions, backend/Telegram requests,
delivery attempts/errors/exhaustion, shutdown. JSON events мають context
update/delivery IDs; response log містить backend request ID.
Gateway тепер веде access logs із видаленими URI/headers.

Спільний stdlib runner у всіх шести Compose services дублює stdout/stderr
у persistent `/logs`, стискає щоденні UTC архіви в gzip й обмежує retention
за кількістю та календарним віком. Початково 14 днів; налаштування
`LOG_RETENTION_DAYS=1..365`. Docker console history: 5 × 50 MiB, blocking.
Runner передає сигнали й exit codes; Compose має init і grace period.
Секрети з env додатково маскуються до console/file запису.

PostgreSQL тепер збирається з тієї самої бази 17.6 із runner; data volume
незмінний. Логування не змінює схему чи фінансові правила. Frontend UI не
змінено; server output архівується, browser analytics не додається.
API додає `X-Request-Id` і ProblemDetails `requestId` без зміни DTO клієнтів.

Описані policy та release-обмеження в [logging](../../docs/logging.md) і
[ADR-012](../knowledge/decisions.md#adr-012-console-logs-і-persistent-щоденні-архіви-через-спільний-runner).
У bot test transport виправлено відображення reply keyboard: повернений
Telegram Message може містити лише inline markup, інакше падали seed tests.

## Перевірки

| Команда / сценарій | Результат | Межі / доказ |
| --- | --- | --- |
| Release `dotnet test tests/ExpenseTracker.Tests -c Release --no-restore` із синтетичними `ConnectionStrings__Database`, `ASPNETCORE_ENVIRONMENT=Development`, `App__Origin=https://localhost` | 43 passed, 7 skipped | `output/detailed-logging/backend-tests.txt`; PostgreSQL не підключалась; два xUnit2031 warnings у паралельно доданих PostgreSQL tests; request ID перевірено для success/handled failure |
| Bot Ruff check, форматування змінених файлів, mypy | Passed | Без зміни стороннього `copy.py`, повна format-перевірка показала окреме форматування seed copy |
| Цільові bot/webhook/privacy tests | 6 passed | `output/detailed-logging/bot-tests.txt`; synthetic HTTP/Telegram |
| Повний bot suite після виправлення test transport | 14 passed, 1 skipped | `output/detailed-logging/bot-suite.txt`; real-backend інтеграція пропущена без URL |
| `python3 -m unittest discover -s tests -p test_log_runner.py -v` | 3 passed | `output/detailed-logging/runner-tests.txt`; stdout/stderr, redaction, gzip, retention, restart, SIGTERM/exit |
| Compose config із синтетичними env | Passed | `output/detailed-logging/compose.txt`; daemon не потрібний |
| .NET whitespace formatter для змінених файлів | Passed | Без unrelated refactoring |
| `python3 scripts/check_docs.py --write-index`, `--check`; `git diff --check` | Passed | Індекс, локальні посилання та формат diff |
| Container build / smoke, Caddy runtime validation, Coolify | Не виконано | Локальний Docker daemon недоступний |

Початковий backend run без синтетичних startup env падав на configuration
validation нових host-based auth tests; повтор з потрібною конфігурацією
пройшов. Початковий bot run виявив некоректну test keyboard fixture, виправлено.
Документацію та diff перевірено окремо. Browser, live Telegram/OpenRouter,
реальна PostgreSQL, міграції й Coolify не запускалися; UI поведінка не змінена.

## Сумісність і rollout

Потрібно перебудувати всі images й перевипустити контейнери зі сталими
named volumes. Один writer на service log volume: stop-first release,
без одночасної ротації replicas. One-shot register-webhook використовує
окремий `LOG_SERVICE=bot-register`. Для replicas потрібні окремі log files
або collector. Логи не є backup чи гарантією проти disk/host failure.
Rollback — попередні images/Compose; log volumes можна зберегти.
Ця задача не потребує міграції, не змінює callbacks, sessions, drafts або
API ownership. Паралельні зміни seed/auth у workspace збережено.
Коміт, push, deployment та реєстрація webhook не виконувалися.
