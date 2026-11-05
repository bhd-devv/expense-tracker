# Компактний дизайн трекера завдань із модальними вікнами

Дата: 2026-11-05. Задача: [tasks-design](../tasks/tasks-design-2026-11-05/README.md).

## Результат

Оновлено `/tasks`: компактний огляд із кільцевим прогресом, календар тижня,
segmented views із лічильниками, короткі рядки з однією кнопкою деталей.
Створення, перейменування/деталі, видалення й довільна дата — native modal
windows. Header/actions лишаються на місці; прокручується лише довгий список
або поля діалогу на короткому viewport. Форма на звичайних мобільних
розмірах вміщується без скролу. Збережено uk/en, теми, доступність і
поведінку невідомої відповіді/явного повтору. Backend, bot, API і БД без змін.

## Перевірки

| Команда / сценарій | Результат | Доказ і межі |
| --- | --- | --- |
| `npm run typecheck` | passed | `output/tasks-design/typecheck.log` |
| `npm run lint` | passed | `output/tasks-design/lint.log` |
| `npm run format:check` | passed | `output/tasks-design/format-check.log` |
| `npm run build` | passed | `output/tasks-design/build.log`; Next.js production build |
| Playwright UI flows | passed | `output/tasks-design/browser-flows.log`: create/cadence, completion, rename, delete/back, date/today, focus/Escape, exact explicit retry |
| Playwright layout matrix | passed | `output/tasks-design/browser-layout.log`: 320/360/390/430/1280 px × uk/en × light/dark; touch targets, page overflow, dialog fit |
| Playwright failure/keyboard states | passed | `output/tasks-design/browser-states.log`: known conflict/input, inert background, read failure, short viewport |
| Documentation index/check; `git diff --check` | passed | `output/tasks-design/docs-index.log`, `docs-check.log`, `diff-check.log` |

Browser smoke виконувався в Chromium із синтетичними profile/tasks API
відповідями через Playwright routing, без auth bypass у коді застосунку.
Реальні PostgreSQL, Telegram WebView/клавіатура, Safari, Coolify, міграції
й інтеграція з live backend тут не перевірялися. Окремого frontend unit-test
runner у проєкті немає; інтерактивні сценарії перевірено в браузері.
Скриншоти й тестові fixtures збережено лише в ігнорованому output/tasks-design.

## Сумісність і rollout

Потрібен лише rebuild/release frontend за окремим запитом. Нових залежностей,
міграцій чи secrets немає; browser session/CSRF і pending request format
сумісні. Уже відкритий Web App підхопить дизайн після повторного відкриття.
Коміт, push і deploy не виконувались. Чинний UX — [design](../design.md).
