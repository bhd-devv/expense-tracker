# Aiogram-сервіс і правила мінімуму boilerplate

Дата: 2026-11-01. Задача: оновити лише агентні правила за уточненням користувача.

## Результат

Telegram-бот визначено як окремий Python/aiogram 3.x адаптер у `src/bot/`.
Прибрано вимогу `Telegram.Bot` і запуску handlers усередині .NET-процесу.
ASP.NET Core залишається єдиним власником бізнес-сценаріїв, PostgreSQL і
OpenRouter; aiogram відповідає за webhook, routers, кнопки та delivery.

Узгоджено [архітектуру](../architecture.md), [безпеку](../security.md),
[Telegram-контракт](../telegram.md) і [Coolify deployment](../deployment.md):
приватний bot API, окремий service credential, actor scope, durable inbox/outbox,
ідемпотентність і bot container. ADR-007 замінює ADR-001; попередній запис
позначено як історичний. Додано правила чистого коду, використання штатних
засобів і бібліотек та послідовної роботи з релевантними перевірками.

API, Mini App, bot service та БД отримали уточнені вимоги. Runtime-код,
scaffold, пакети, міграції й Docker-конфігурацію не створювалися.

## Перевірки

- Звірено чинні правила на відсутність вимоги бота в .NET; старе рішення
  лишається лише в ADR-001 із явним статусом заміни та в історії.
- `python3 scripts/check_docs.py --write-index` — оновлено індекс.
- `python3 scripts/check_docs.py --check` — перевірено документи й посилання.
- `git diff --check` — перевірено форматування diff.

Aiogram, backend/frontend build, browser, Telegram, PostgreSQL і Coolify
не запускалися: користувач уточнив scope як оновлення лише правил.

## Сумісність і rollout

Змін runtime і БД немає; rollback застосунку не потрібний. Під час реалізації
потрібно узгоджувати приватний API між .NET та Python, зберігати actor scope,
і працювати з повторними/застарілими updates та callback-кнопками за
Telegram-контрактом. Коміт, push і deployment не виконувалися.
