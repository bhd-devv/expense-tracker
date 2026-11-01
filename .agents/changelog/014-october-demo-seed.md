# Демовитрати за жовтень через /seed

Дата: 2026-11-01. Задача: [демонаповнення](../tasks/seed-october-2026-11-01/README.md).

## Результат

Приватна команда `/seed` і кнопка налаштувань бота додають поточному
користувачу 70 витрат за всі дні жовтня 2026, 4 деморахунки UAH/USD/EUR
і використовують 7 стандартних категорій. Дані видно у звичайних списках
і звітах Mini App та бота; бот нагадує вибрати жовтневий період.

Backend виконує наповнення атомарно під user advisory lock, застосовує
спільну expense validation і ліміти каталогів. Запис `DemoSeeds` не дозволяє
дублів при повторних/конкурентних викликах навіть після редагування або
видалення демовитрат. AI не викликається, особисті витрати й профіль
не змінюються. Додано migration та згенеровано OpenAPI/TypeScript/Python DTO.
Persistence-рішення — [ADR-011](../knowledge/decisions.md#adr-011-атомарне-демонаповнення-з-окремим-записом-виконання).

## Перевірки

| Команда або сценарій | Результат | Межі / доказ |
| --- | --- | --- |
| `dotnet-ef migrations add AddDemoSeeds` (10.0.12) | Згенеровано | Компіляція для авторингу; створення лише `DemoSeeds`, до БД не застосовано |
| `python3 scripts/generate_contracts.py --url http://127.0.0.1:5099/openapi/v1.json` | Згенеровано | Локальний Development API, синтетичні налаштування; server URL нормалізовано до стандартного 5085 |
| PostgreSQL і fake Telegram тести | Додано, не запускалися | Concurrent seed, actor isolation, канали/звіти, ліміти/архівні категорії, ingress/uk/en і невідома відповідь без retries |
| Application tests/build/lint/typecheck | Не запускалися як перевірки | Збережено вимогу ручного запуску лише за запитом |
| `python3 scripts/check_docs.py --write-index`, `--check` | passed | `output/seed-october/docs-index.log`, `docs-check.log` |
| `git diff --check` | passed | `output/seed-october/diff-check.log`; зміст seed diff переглянуто |

Реальні Telegram-клієнти, PostgreSQL, застосування міграції, браузер і Coolify
не перевірялися. Генерація контрактів не є повною перевіркою застосунку.

## Сумісність і rollout

Штатний Compose `migrate` має застосувати `AddDemoSeeds` перед новим backend,
потім потрібен оновлений бот. Env-змінних не додано. Поточні Mini App,
drafts і старі inline-кнопки сумісні; демокоманда не змінює активний діалог.
Наповнення виконується лише вручну `/seed`; коміт, push і деплой не виконувалися.
