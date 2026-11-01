# Логи в Coolify та щоденні архіви

Усі шість сервісів Compose (`backend`, `bot`, `frontend`, `gateway`,
`postgres`, `migrate`) пишуть stdout/stderr у Coolify Logs та одночасно у
власний persistent volume. Перебудова контейнера не видаляє ці volumes.
Frontend тут означає Next.js server; browser console та локальні дії без
запиту на сервер не є серверним журналом.

## Що записується

- API: початок/кінець кожного HTTP-запиту, route template, метод, status,
  тривалість, `requestId`, `traceId`, внутрішній user ID та GUID ресурсу.
  Відмови auth, CSRF, rate limiting і validation також мають status.
- Підтверджені зміни: витрати, чернетки, рахунки, категорії, налаштування,
  login/logout. Мутації логуються після commit; idempotent replay має
  окрему подію. Читання списків/звітів видно як HTTP-запити.
- AI: операція, model ID, HTTP status, тривалість, невдалий розбір;
  аудіоконвертація — exit code й розмір результату.
- Telegram: приймання/ігнорування update, durable acceptance, duplicate,
  delivery ID, update ID, номер спроби, результат та вичерпання спроб;
  кнопкові дії й dialog input без самого введеного значення.
  Вихідні Telegram methods та backend HTTP calls мають початок/результат.
- Lifecycle, міграції, maintenance counts, PostgreSQL connections/errors,
  access/error logs gateway та вивід Next.js server.

API повертає `X-Request-Id`; ProblemDetails додатково містить `requestId`.
Bot response log містить той самий ID для переходу до backend log.
Помилки зберігають тип, stack locations та внутрішні причини; .NET також
записує API code / SQLSTATE та HRESULT. Довільні exception messages і SQL
text не записуються: вони можуть містити фінансові описи або credentials.
Bootstrap failures мають `stage`, наприклад `configuration.database`.

Тіла запитів/відповідей, query strings, Telegram payloads, cookie, headers,
токени, суми, нотатки й транскрипти не логуються. Gateway видаляє URI та
headers із JSON access/error events; endpoint видно детальніше в API.
Runner додатково маскує точні значення configured secrets зі свого env.
Не вмикайте EF sensitive logging, SQL command logging або HTTP body logging.

## Файли й строки зберігання

| Сервіс | Volume | Поточний файл |
| --- | --- | --- |
| backend | `backend-logs` | `/logs/backend.log` |
| bot | `bot-logs` | `/logs/bot.log` |
| frontend | `frontend-logs` | `/logs/frontend.log` |
| gateway | `gateway-logs` | `/logs/gateway.log` |
| postgres | `postgres-logs` | `/logs/postgres.log` |
| migrate | `migrate-logs` | `/logs/migrate.log` |

О 00:00 **UTC** попередній файл стискається в
`/logs/<service>.log.YYYY-MM-DD.gz`, починається новий поточний файл.
Ротація виконується навіть без нових подій, поки runner працює; після
зупинки наступний запуск також перевіряє прострочену ротацію. `migrate`
працює лише під час release, тому перевіряє її при наступному запуску.
Початково зберігаються 14 щоденних архівів плюс поточний файл.
У Coolify environment можна задати `LOG_RETENTION_DAYS` від 1 до 365;
після зміни перевипустіть контейнери. Старіші архіви видаляються під час
ротації та на старті: діє і ліміт кількості, і календарний вік архівів.
Файли відкриваються з обмеженими правами через `umask 077`.

Окремо Docker console history обмежена `json-file`: 5 файлів по 50 MiB на
контейнер. Coolify показує цей поточний Docker history; довшу історію читайте
у `/logs`. Щоденні файлові логи мають часову ротацію, без обмеження розміру
в межах однієї доби. Плануйте диск під фактичну інтенсивність: health checks
та порожні queue claims також логуються.

## Перегляд і копіювання

У Coolify відкрийте **Logs** потрібного сервісу. У його Terminal:

```bash
ls -lh /logs
tail -n 100 /logs/backend.log
gzip -cd /logs/backend.log.2026-11-01.gz
```

Назву сервісу/дату підставте з `ls`; порожній поточний файл після ротації —
нормальний стан. Для локального Compose з налаштованим `.env`:

```bash
docker compose --project-directory . --env-file .env -f deploy/compose.yaml logs --since 1h backend bot gateway
docker compose --project-directory . --env-file .env -f deploy/compose.yaml cp backend:/logs output/backend-logs
```

У stdout/file немає lossy background queue: повільний диск/console створює
backpressure. Runner передає SIGTERM/SIGINT групі процесів, дочитує pipe й
повертає exit code сервісу. Compose використовує init і 30 с на shutdown.
`log_archive_write_failed` / `log_archive_rotation_failed` у Coolify означає,
що файловий запис/ротація потребує уваги; console logging продовжується.

## Release та межі надійності

Перебудуйте **всі** images: runner включено в Dockerfiles; PostgreSQL image
тепер також збирається локально, з незмінної бази `postgres:17.6-bookworm`.
Дані БД залишаються в `postgres-data`, зміна логування не потребує міграції.
Назви persistent volumes мають залишатися сталими; `down -v` їх видаляє.

Один writer на service volume: перед запуском нового контейнера з тим самим
`/logs` зупиніть старий. Для replicas потрібні окремі log volumes/файли або
зовнішній log collector; спільний файл не підтримує конкурентну ротацію.
Не запускайте паралельні migrate jobs.

Щоденна ротація не є backup. SIGKILL, disk full, втрата filesystem/хоста або
видалення volume можуть втратити дані; абсолютної гарантії журналу немає.
Для історії понад retention налаштуйте backup log volumes поза хостом і
перевірте відновлення. Docker socket або зовнішній log service не потрібні.

Перевірка без Docker: `python3 -m unittest discover -s tests -p test_log_runner.py -v`.
Вона перевіряє stdout/stderr mirroring, redaction, exit code, UTC rotation,
gzip, retention, restart та graceful shutdown. Container build, права
volumes і фактичні Logs/Terminal у Coolify потребують deployment smoke.
