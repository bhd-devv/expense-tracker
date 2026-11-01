# Агентні правила проєкту

У цій папці зберігаються правила реалізації, модель продукту й архітектурні
рішення для трекера витрат. Точка входу для агентів — [AGENTS.md](../AGENTS.md).

| Документ | Що визначає |
| --- | --- |
| [product.md](product.md) | Витрати, категорії, звіти та однакові можливості каналів |
| [design.md](design.md) | Стриманий UI, салатові акценти, `uk`/`en`, світла/темна теми й доступність |
| [architecture.md](architecture.md) | Стек, структура, спільні сценарії, дані й API |
| [security.md](security.md) | Telegram identity, Mini App sessions, CSRF і доступ до даних |
| [telegram.md](telegram.md) | Reply/inline keyboards, діалоги та надійна обробка updates |
| [deployment.md](deployment.md) | Docker Compose, Coolify, міграції та експлуатація |
| [development.md](development.md) | Стиль коду, залежності, перевірки й порядок змін |
| [guides/sdlc.md](guides/sdlc.md) | Як вести велику задачу від запиту до перевіреного результату |
| [guides/documentation.md](guides/documentation.md) | Правила changelog, задач, індексу та перевірки документації |
| [STATUS.md](STATUS.md) | Поточна активна робота й блокери |
| [knowledge/decisions.md](knowledge/decisions.md) | Прийняті архітектурні рішення (ADR) |
| [templates/decision.md](templates/decision.md) | Шаблон нового ADR |
| [changelog/README.md](changelog/README.md) | Формат історії завершених змін |
| [tasks/README.md](tasks/README.md) | Формат робочих записів великих задач |

Чинні продуктові й технічні контракти мають одне джерело правди: відповідну
сторінку з правилами або `knowledge/`. Changelog і task-файли посилаються на
них, але не копіюють їх і не перевизначають. Історичні записи, research та
архіви потрібні для контексту й не є інструкціями до виконання.

Вимоги користувача: .NET backend, TypeScript / Next.js frontend, Telegram-бот
на Python / aiogram із функціями frontend, безшовна безпечна авторизація й
деплой через Coolify. PostgreSQL і описаний у `product.md` склад MVP —
початкові архітектурні рішення. Змінюй їх за підтвердженою потребою, пояснюючи
наслідки. Не додавай інфраструктуру або функції лише тому, що вони можливі.
