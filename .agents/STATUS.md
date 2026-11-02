# Поточний стан

Оновлюй цей файл лише для активної роботи, конкретного блокера та наступної
дії. Він не є журналом змін, health check production або списком дозволених
задач. Старі записи не означають, що роботу потрібно продовжувати.

## Активна робота

Локальні зміни [ручного/AI-введення](tasks/manual-ai-input-2026-11-02/README.md)
завершені. Підготовлено additive migration `AddInputMode`; build, тести,
browser та реальні Telegram/OpenRouter перевірки очікують окремого запиту.

Локальні зміни [витрат без фінансових рахунків](tasks/expenses-without-accounts-2026-11-02/README.md)
завершені. Runtime перевірки очікують окремого запиту користувача;
перед release потрібні PostgreSQL migration test і узгоджене оновлення сервісів.

## Блокери

Немає для завершеної локальної задачі.

## Межі впевненості

Міграція `RemoveFinancialAccounts` підготовлена, але не застосовувалася до
реальної БД у цій задачі. Backend build, tests, lint/typecheck, browser і
Coolify для спрощеної моделі не запускалися. Зворотна міграція відхиляється;
перед production release потрібен backup. Докладно — [deployment](../docs/deployment.md).

Наданий Docker inspect підтвердив дві спільні мережі gateway/backend;
production logs показали HTTP у `Antiforgery.CheckSSLConfig`.
[Приватний API alias виправлено локально](changelog/018-gateway-private-api-routing.md).
Наступна зовнішня дія — redeploy оновленого Compose й gateway image,
потім перевірка private DNS, 200 `/api/auth/csrf` та входу з Telegram.
Після правки цей сценарій у Coolify ще не перевірено.

Живі OpenRouter/Telegram, HTTPS у Telegram-клієнтах і Coolify ще не перевірені.
Логування та щоденні архіви [реалізовано локально](../docs/logging.md);
image build, Caddy runtime і права log volumes не перевірені, оскільки
локальний Docker daemon недоступний. Перед release потрібний container smoke.
Для наступної інтеграційної задачі потрібні тестові credentials, model IDs і
домен. Документ не дає дозволу на деплой або реєстрацію webhook.
