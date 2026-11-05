# Компактний преміальний трекер завдань

Стан: `completed`. Дата: 2026-11-05.

## Результат і критерії

Доросліший дизайн `/tasks` у стилі Lime, мінімум скролу, створення/деталі/
видалення/дата в модальних вікнах. Основні controls доступні без прокрутки;
довгі списки й низькі viewport залишають доступ до всього контенту.
Повні uk/en і light/dark, touch targets 44×44, focus/Escape та стани помилок.

## Scope / non-goals

Лише frontend task feature, ізольовані tasks CSS і UX документація.
Без нових залежностей, зміни API, даних, auth, розкладів або бота.

## Виконання

- [x] Прочитати чинні правила, task UI і Next.js client boundary guide.
- [x] Оновити board, календар, рядки й native dialogs.
- [x] Зберегти серверні permissions, pending request і exact explicit retry.
- [x] Перевірити typecheck/lint/format/build та цільові browser flows.
- [x] Оновити design, changelog та STATUS.

## Рішення й ризики

Чинний контракт — [design](../../design.md). Native dialog дає inert фон
і browser-managed modal behavior; явний focus потрібний при зміні кроку.
Прокрутка лише в області довгого списку/полів, без приховування даних.
Для UI smoke використано synthetic API fixtures; це не перевірка backend,
Telegram auth чи клавіатури справжнього телефона.

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

## Результат

Локальну реалізацію завершено. [Changelog](../../changelog/031-tasks-premium-design.md).
Нової схеми чи ADR немає: межі сервісів, auth і persistence не змінювались.
Реальні Telegram/Safari/backend/PostgreSQL/Coolify не перевірені;
коміт, push і deployment не виконувались.
