# Telegram-бот із двома кнопками

Стан: `completed`. Дата: 2026-11-04.

## Результат і критерії приймання

Будь-яке приватне повідомлення лише показує inline «Витрати» та «Завдання».
Немає command/callback/dialog/finance/seed/audio/notification сценаріїв у
боті чи private actor API. Усі дані й дії доступні у відповідних Web Apps.

## Scope / non-goals

Bot handlers/transport/registration, backend service authorization й inbox,
видалення actor/seed/outbox логіки, consumers, тести та чинні правила.
Без переписування Web Apps, нової схеми БД, видалення старих даних чи deploy.

## План

- [x] Прочитати правила й перевірити стан репозиторію.
- [x] Прибрати сценарії бота й private actor API, лишити два launch links.
- [x] Оновити generated contracts і regression тести.
- [x] Оновити ADR-020, чинні правила, changelog та STATUS.
- [x] Перевірити зміст документації й diff; runtime перевірки не запитані.

## Рішення й ризики

[ADR-020](../../knowledge/decisions.md) визначає нову межу service auth,
інертне compatibility storage та stop-first release. Inline запуск потрібен
для signed initData. Reply keyboard зникає після наступної взаємодії;
попередні command menus — після явної реєстрації. Збереження inbox лишає
durable dedupe/lease; невідома Telegram відповідь може дублювати лише меню.

## Виконання й перевірки

| Команда або сценарій | Результат | Межі / доказ |
| --- | --- | --- |
| Генерація TS/Python DTO з локальної OpenAPI-схеми | виконано | Не live export; `output/launcher-only/bot-openapi.json` |
| Ruff format змінених Python-файлів | виконано | Форматування, без lint/tests |
| `python3 scripts/check_docs.py --write-index` | passed | `output/launcher-only/docs-index.log` |
| `python3 scripts/check_docs.py --check` | passed | `output/launcher-only/docs-check.log` |
| `git diff --check` | passed | `output/launcher-only/diff-check.log` |
| Build/tests/lint/typecheck/browser/Telegram/PostgreSQL/Coolify | skipped | Не запитані; за правилами development не запускались |

## Результат

Локальну реалізацію завершено. [Changelog](../../changelog/030-launcher-only-bot.md).
Build, тести, browser, справжні Telegram/PostgreSQL, live contract export і
Coolify не перевірені. Віддалені зміни, коміт і деплой не виконувались.
