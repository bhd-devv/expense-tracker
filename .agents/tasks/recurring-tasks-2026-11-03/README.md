# Окремий вебзастосунок регулярних завдань

Стан: `completed locally`. Дата: 2026-11-03.

## Результат і критерії приймання

Кнопка «✅ Завдання» бота відкриває окремий `/tasks`. Створення завдання
з інтервалом 1/2/3/7 і датою початку, список на день/усі, відмітки сьогодні
та в минулі заплановані дні, progress, перейменування й видалення.
У боті лише launch: без списків, task callbacks, нагадувань або керування.
Мова/тема/Telegram identity спільні; доступ і розклад визначає .NET.

## Scope / non-goals

Browser-only .NET feature, PostgreSQL entities/міграція, окремий Next.js
route, bot launch, OpenAPI та generated DTO. Фінансові дані не зачеплені.
Немає окремого domain/service, notification worker, зміни cadence після
створення, тестового auth bypass, коміту, деплою чи запуску міграції.

## Виконано

- [x] Прочитано чинні правила й код, початковий git status чистий.
- [x] Додано Tasks use cases/endpoints, ownership, version, owner locking,
  composite FK/PK, tombstone та additive migration/model snapshot.
- [x] Додано окремий UI з локалізацією/темами, станами й точним pending
  запитом для явного повтору після невідомої відповіді.
- [x] Додано launch бота й тести старих мовних підписів без task API в Python.
- [x] Оновлено чинні контракти, ADR-019, документацію й changelog.
- [ ] Runtime перевірки за окремим явним запитом користувача.

## Рішення й ризики

Продуктова семантика — [product](../../product.md), persistence/межа
web-only — [ADR-019](../../knowledge/decisions.md). Частота від початку,
пропуск не зсуває графік. Видалення приховує також попередні дні, але
історію лишає в БД. Нові browser endpoints поза `MapWorkspace` бота.
OpenAPI доповнено без запуску backend; generated DTO оновлені з цього
контракту. Порівняння з реальним Development API залишається неперевіреним.

## Виконання й перевірки

| Команда або сценарій | Результат | Обмеження |
| --- | --- | --- |
| `git status --short`, читання коду/diff | reviewed | Без runtime доказу |
| `npm run generate` у frontend | passed | TypeScript DTO зі збереженої схеми |
| `datamodel-codegen --input ../../contracts/openapi.json ...` у bot | passed | Python DTO, без bot endpoints для завдань |
| Prettier `--write`, Ruff `format` змінених файлів | completed | Форматування, не typecheck/lint |
| `python3 scripts/check_docs.py --write-index` і `--check` | passed | `output/recurring-tasks/` |
| `git diff --check` | passed | `output/recurring-tasks/` |
| Build/tests/typecheck/lint/browser | skipped | Не запитано; правила development.md |
| Реальні Telegram/PostgreSQL/міграція/Coolify | skipped | Не запускалися |

Додані тести: календарні межі/високосний день, timezone, isolation,
конкурентні відмітки, заборона незапланованих/майбутніх дат, незалежність
днів, незмінність звітів, tombstone/replay та launch uk/en/старою мовою.
Вони не запускалися. Докази перевірок документації: `output/recurring-tasks/`.

## Результат

Локальна реалізація завершена; [changelog](../../changelog/029-recurring-tasks-web-app.md).
Наступна release-дія після окремого запиту: перевірки, backup і migration
job, backend, frontend/bot; оновлення reply-меню через `/start`.
Цей файл не надає дозволу на віддалені операції.
