# Вхід Mini App через правильний Telegram launch mode

Стан: `completed`. Дата: 2026-11-01.

## Запит і діагноз

Користувач надав backend/bot logs після попередньої правки сесій.
У цих двох уривках по 100 подій: немає Error або 500, БД readiness — 200,
bot queues — 204. Mini App двічі отримує 401 `/api/auth/me` та 200 `/api/config`,
але не надсилає csrf/login. Доказ: `output/telegram-inline-launch/log-summary.json`.

У коді головна reply keyboard відкривала застосунок через
`KeyboardButton.web_app`. За [офіційним Telegram контрактом](https://core.telegram.org/bots/webapps#webappinitdata)
таке відкриття має порожній initData. Inline web_app та menu button
підходять для поточного серверного login. Старий CSRF bug був окремим;
нові логи не доводять, що користувач уже пройшов крок після login.

## Реалізація й критерії

- [x] Зберегти головне reply-меню: текстова дія застосунку показує inline web_app.
- [x] `/start` і recovery deep link `?start=app` показують inline-кнопку та нове reply-меню.
- [x] Відсутність initData має конкретну uk/en підказку в Mini App.
- [x] Додати fake Telegram регресії menu/start і правильного типу клавіатури.
- [x] Оновити telegram/security/deployment contracts і один changelog.

## Межі й перевірки

Не змінено backend validation, cookies, API DTO, env або БД. Немає auth
обходу через довільний user ID, launch params або initDataUnsafe.
Build/tests/lint/typecheck/browser не запускалися: чинна вимога користувача
дозволяє їх лише за окремим запитом. Прочитано Next.js 16 локальний Script
guide, переглянуто diff. Документаційні перевірки — у
[changelog](../../changelog/017-telegram-inline-launch.md).

Коміт, push, deploy, реальні Telegram-повідомлення та webhook registration
не виконувалися. Уже зареєстрований menu button Lime можна використати
до оновлення; після deploy bot/frontend слід надіслати `/start`.
