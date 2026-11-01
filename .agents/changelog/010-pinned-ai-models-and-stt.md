# Фіксовані GPT Luna та бюджетна STT-модель у серверному конфігу

Дата: 2026-11-01. Задача: не застосовується — завершена зміна AI-конфігурації.

## Результат

За запитом користувача вибрано актуальну `openai/gpt-6-luna` для текстового
розбору та стабільну `google/gemini-3.1-flash-lite` для STT українського й
англійського голосу. ID версіонуються у
[appsettings.json](../../src/backend/ExpenseTracker.Api/appsettings.json).
Їх прибрано з Compose, `.env.example` та локального ігнорованого `.env`;
інші локальні значення збережено. У env залишається OpenRouter API key.

Голос тепер має два послідовні запити через OpenRouter: Flash-Lite отримує
лише WAV-аудіо й повертає транскрипт, потім Luna розбирає цей текст із
каталогом категорій. Текстове введення одразу використовує Luna. Розбір не
переписує вихідний текст або STT-транскрипт; невалідний STT зупиняє pipeline.
Обидва запити мають strict JSON schema, `require_parameters` та
`data_collection=deny`, спільний timeout 75 секунд і максимум 1000 output
tokens кожен. Luna використовує reasoning `none` без непідтримуваного
`temperature`, Flash-Lite — `minimal`. Автоматичних retries/fallback немає.

Вибір перевірено за офіційною сторінкою OpenAI, публічним каталогом і
endpoints OpenRouter та документацією Google щодо ASR, audio formats і
minimal thinking. Джерела й причину вибору зафіксовано в
[ADR-010](../knowledge/decisions.md#adr-010-фіксовані-ai-моделі-та-окремий-stt-перед-текстовим-розбором).
Низька ціна Flash-Lite і документоване покращення ASR є підставою вибору;
виміряної live-якості uk/en поки немає.

API/DTO, Mini App, aiogram-код, фінансові інваріанти й схема БД не змінені;
обидва канали використовують оновлений backend. Нових залежностей немає.
Оновлено README, продуктовий і deployment contracts та ADR-009/010.

## Перевірки

Логи й публічні metadata snapshots — `output/ai-model-config/`.
Команди .NET запускалися з `DOTNET_CLI_HOME=/private/tmp/expense-tracker-dotnet`,
`NUGET_PACKAGES=/private/tmp/expense-tracker-nuget` і вимкненою CLI telemetry.

| Команда або сценарій | Результат | Межі / доказ |
| --- | --- | --- |
| Читання `/api/v1/models` і `/models/{id}/endpoints` OpenRouter | passed | ID, structured outputs, audio input, supported parameters і ціни; без ключа чи платних inference-запитів |
| `dotnet restore src/backend/ExpenseTracker.slnx --locked-mode` | passed | `restore.log`, lockfiles не змінені |
| `dotnet format src/backend/ExpenseTracker.slnx --verify-no-changes --no-restore` | passed | `format.log` |
| `dotnet build src/backend/ExpenseTracker.slnx -c Release --no-restore` | passed | `build.log`, 0 warnings/errors |
| `dotnet test tests/ExpenseTracker.Tests -c Release --no-build --no-restore` | passed | `tests.log`: 34 passed, 5 skipped без тестової PostgreSQL |
| Audio → STT → text tests для uk/en/mixed fixtures | passed | Підмінний HTTP handler; порядок/model routing, WAV, schema, privacy, відсутність категорій у STT, збереження транскрипту й зупинка після STT failure. Це не оцінка якості розпізнавання |
| `docker compose --env-file .env.example -f deploy/compose.yaml config --quiet` | passed | Без реальних secrets і без daemon |
| `docker compose --env-file .env.example -f deploy/compose.yaml build backend` | blocked | `container-build.log`: локальний Docker daemon не запущений |
| `python3 scripts/check_docs.py --write-index`, `--check`; `git diff --check` | passed | Generated index, посилання та whitespace |

Живий OpenRouter inference, Telegram, браузер, PostgreSQL, міграції й Coolify
не перевірялися. Frontend/bot build і тести не повторювалися: їхні DTO й
реалізація не змінені. Перевірка metadata не доводить якості uk/en або
доступності privacy-сумісного маршруту для конкретного production ключа.

## Сумісність і rollout

У Coolify достатньо OpenRouter API key; видаліть старі model env-змінні,
особливо прямі `OpenRouter__TextModel`/`OpenRouter__AudioModel`, щоб вони не
перевизначали конфіг. Обрані ID надалі змінюються в репозиторії разом із
перевіркою можливостей. Перед release потрібен live smoke українського й
англійського тексту/голосу з бюджетом ключа та чинною AI-згодою.
Голос має два provider-запити замість одного під спільним deadline.
Відкриті Mini App, старі callbacks і bot dialogs сумісні; міграція БД не
потрібна. Rollback старого backend потребує повернення його model env.
Коміт, push, deployment і реєстрація webhook не виконувалися.
