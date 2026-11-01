# Вхід Mini App через inline-кнопку замість reply web_app

Дата: 2026-11-01. Задача: [Telegram launch](../tasks/telegram-inline-launch-2026-11-01/README.md).

## Результат

Кнопка `KeyboardButton.web_app` у головній reply keyboard відкривала Mini App
без `initData`: Telegram не надає його для цього launch mode. Нові production
логи містили тільки 401 `/api/auth/me`, 200 config і порожні черги бота;
запит login не надсилався. Це окремий дефект від попередньої правки CSRF subject.

Головне меню залишається reply keyboard. Його дія «Відкрити застосунок»
надсилає inline web_app-кнопку, яка передає Telegram proof. `/start`
також надсилає цю кнопку та замінює стару клавіатуру. Mini App recovery
веде до бота з `?start=app`; відсутність initData пояснюється конкретною
uk/en підказкою. Menu button Lime продовжує працювати.

Авторизація на backend обов'язкова; API/БД/env не змінюються.
Чинні правила: [telegram.md](../telegram.md), [security.md](../security.md).
Підтвердження Telegram поведінки:
[WebAppInitData](https://core.telegram.org/bots/webapps#webappinitdata),
[inline launch](https://core.telegram.org/bots/webapps#inline-button-mini-apps),
[menu launch](https://core.telegram.org/bots/webapps#launching-mini-apps-from-the-menu-button).

## Перевірки

| Команда або сценарій | Результат | Межі / доказ |
| --- | --- | --- |
| Аналіз двох наданих JSON logs | Діагноз встановлено | `output/telegram-inline-launch/log-summary.json`; лише безпечні лічильники/маршрути, без raw payloads |
| Перегляд коду/diff і Telegram документації | Виконано | Тип reply web_app не надає initData; правильні inline та menu launch |
| Fake Telegram regressions uk/en, menu/start | Додано, не запущено | Немає справжніх Telegram викликів |
| Build/tests/lint/typecheck/browser | Не запускалися | Збережено вимогу локальних перевірок лише за запитом |
| `python3 scripts/check_docs.py --write-index`, `--check` | passed | `output/telegram-inline-launch/docs-index.log`, `docs-check.log` |
| `git diff --check` | passed | `output/telegram-inline-launch/diff-check.log` |

## Сумісність і rollout

Потрібен deploy bot/frontend; міграція не потрібна. Після оновлення
надішліть `/start` та відкрийте inline-кнопку під відповіддю. Старі reply
web_app-кнопки не змінюються автоматично. До оновлення можна відкрити
вже налаштованим menu button Lime. Cookies, drafts та старі фінансові
inline callbacks сумісні. Коміт, push, deploy або setWebhook не виконувалися.
Живий Telegram і Coolify після правки не перевірено.
