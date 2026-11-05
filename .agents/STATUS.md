# Поточний стан

Оновлюй лише активну роботу, конкретні блокери й межі перевірки.
Історія завершених задач — у [changelog](changelog/README.md).

## Активна робота

[Видалення накопичувальних рахунків](tasks/delete-savings-2026-11-05/README.md)
завершено локально: кошик у списку й текстова дія у підсумку рахунка,
підтвердження для порожніх рахунків та рахунків з історією/залишком.
Version і tombstone захищають concurrent entry/delete та старі retries.
Release build, 3 PostgreSQL savings тести, frontend typecheck/lint/format/build,
OpenAPI drift і Chromium uk/en × light/dark × 320–1280 px пройшли.
`AddSavingsDeletion` застосовано лише до ізольованої PostgreSQL 14.
Перед release потрібне зупинення старого backend; rollback дозволений
лише на backend, що підтримує Deleted. Telegram, Docker/Coolify й production
PostgreSQL 17 у цій зміні не перевірялися.

[Доходи й окремі категорії](tasks/incomes-2026-11-05/README.md) завершено
локально: доходи замість звітів у навбарі, звіти кнопкою з огляду,
окремі browser API/таблиці й CRUD доходів/категорій. `AddIncomes`
застосовано лише до ізольованої PostgreSQL 14 для тестів. Release build,
цільовий тест доходів, frontend typecheck/lint/format/build, OpenAPI drift
і Chromium smoke (20 комбінацій viewport/мови/теми) пройшли. У повному
backend suite 76 passed / 3 failed: ті самі 401 у fixed-clock AI/task
тестах відтворено на незміненому HEAD. Production PostgreSQL 17, Telegram
WebView, Docker build та Coolify у цій задачі не перевірялися.

[Оновлення дизайну завдань](tasks/tasks-design-2026-11-05/README.md)
завершено локально: компактний workspace, тижнева дата, progress ring,
модальні create/details/delete/date й один scroll region для довгого списку.
Frontend typecheck/lint/format/build і browser smoke на synthetic API пройшли.
Матриця 320/360/390/430/1280 px × uk/en × light/dark, unknown-result retry,
focus/Escape та видимі дії короткого діалогу перевірені в Chromium.
Telegram WebView/keyboard, Safari, live backend/PostgreSQL і Coolify для цього
дизайну не перевірялися. Backend і bot у цій зміні не редагувалися.

[Бот лише з двома кнопками](tasks/launcher-only-2026-11-04/README.md)
реалізований локально: inline launch витрат `/` і завдань `/tasks`, без
продуктових команд, callbacks, діалогів, фінансових дій і сповіщень.
Private actor API та демонаповнення видалені; service key дає лише inbox.
Це замінює попередню вимогу parity функцій бота й Mini App.

## Блокери

Немає для локальної реалізації. Реліз і live registration не виконувались.

## Межі впевненості

Для попереднього спрощення бота runtime build/tests/lint/typecheck/browser/
PostgreSQL/Telegram/Coolify не запускалися. Поточні frontend перевірки дизайну
не підтверджують backend/bot runtime. Перед release потрібні окремо запитані
перевірки та OpenAPI export/drift із Development API. Додані regression
тести, оновлені consumers і перевірені документація/diff не замінюють runtime.

Нової міграції для спрощення бота немає. Попередні AddInputMode,
AddCategoryIcons, AddCategoryLimits, AddSavingsAccounts та AddRecurringTasks
підготовлені; тут не застосовувались. RemoveFinancialAccounts потребує
backup і узгодженого переходу; Down відхилено. Деталі —
[deployment](../docs/deployment.md).

Backend і бот слід оновити узгоджено без старого worker, потім явно
виконати register-webhook для очищення старого command menu. Наступне
приватне повідомлення прибере reply keyboard. Старі діалоги не продовжуються.

Виправлення private gateway alias і HTTPS forwarding у Coolify,
production cookie/CSRF, реальні Telegram-клієнти, live OpenRouter та права
persistent log volumes залишаються неперевіреними у deployment.
Цей документ не дає дозволу на міграцію, реєстрацію webhook чи деплой.
