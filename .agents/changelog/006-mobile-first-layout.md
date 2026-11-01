# Mobile-first верстка Mini App

Дата: 2026-11-01. Задача: не застосовується — завершена UI-правка.

## Результат

Базові стилі Mini App тепер орієнтовані на телефон від 320 CSS px;
планшет/desktop додаються через `min-width`. Нижня навігація враховує safe
areas. Збільшено touch targets до 44×44 px, поля мають 16 px, форми й
редактор одноколонкові. Кнопка створення чернетки має текст, дії додавання
доступні також поза оглядом. Дата й категорія видимі у мобільному списку;
суми та довгі назви переносяться без горизонтального скролу. Теми й обидві
мови збережено; українська нижня навігація має короткий підпис «Опції» з
повним accessible name «Налаштування».

API, Telegram-бот і БД не змінювались; міграція не потрібна. Мобільні правила
закріплено в [design.md](../design.md).

## Перевірки

| Команда або сценарій | Результат | Межі / доказ |
| --- | --- | --- |
| Prettier, typecheck, lint, production build | passed | `output/playwright/mobile-build.log` |
| Playwright overview: 320/360/390/430/768/1440 px | passed | Немає overflow, замалих кнопок, обрізаних nav labels |
| Усі 6 розділів: 320/390 px, uk/light та en/dark | passed | document width = viewport, кнопки ≥44×44, поля ≥16 px |
| Редактор: 320/390/430 px | passed | Немає внутрішнього overflow, потрібна вертикальна прокрутка доступна |
| Довгі значення у редакторі без збереження | passed | Локальна браузерна перевірка |
| Docs checker, git diff --check | passed | Локальна перевірка |

Скриншоти: `output/playwright/mobile-overview-uk-light.png`,
`mobile-overview-uk-dark.png`, `mobile-overview-en-dark.png`,
`mobile-editor-uk-light.png`, `mobile-expenses-uk-light.png`.
Використано справжній локальний API/PostgreSQL зі синтетичними даними;
схема не змінювалась. Це browser viewport smoke, не реальний телефон:
Telegram iOS/Android, software keyboard і фізичні safe areas не перевірені.
Coolify не запускали; нові контейнери для CSS-only зміни не збирали.

## Сумісність і rollout

Зміна лише представлення. Уже відкритий Mini App отримає нові стилі після
оновлення; API-контракти, сесії, чернетки, старі inline-кнопки й bot dialogs
залишаються сумісними. Наступний реліз потребує звичайного frontend build
і окремо авторизованого deployment. Rollback — попередній frontend image;
міграцій чи перетворення даних немає. Коміт/push/deploy не виконувались.
