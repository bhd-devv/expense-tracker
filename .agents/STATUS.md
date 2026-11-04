# Поточний стан

Оновлюй лише активну роботу, конкретні блокери й межі перевірки.
Історія завершених задач — у [changelog](changelog/README.md).

## Активна робота

[Бот лише з двома кнопками](tasks/launcher-only-2026-11-04/README.md)
реалізований локально: inline launch витрат `/` і завдань `/tasks`, без
продуктових команд, callbacks, діалогів, фінансових дій і сповіщень.
Private actor API та демонаповнення видалені; service key дає лише inbox.
Це замінює попередню вимогу parity функцій бота й Mini App.

## Блокери

Немає для локальної реалізації. Реліз і live registration не виконувались.

## Межі впевненості

Runtime build/tests/lint/typecheck/browser/PostgreSQL/Telegram/Coolify не
запускалися за чинними правилами. Перед release потрібні окремо запитані
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
