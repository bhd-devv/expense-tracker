# Перший локальний MVP трекера витрат

Дата: 2026-11-01. Задача: [реалізація MVP](../tasks/implement-mvp-2026-11-01/README.md).

## Результат

Замість набору правил підготовлено робочі API, Mini App і кнопковий aiogram-бот.
.NET backend володіє фінансовими правилами, авторизацією, AI та PostgreSQL;
обидва інтерфейси використовують спільні сценарії.

- **API:** Telegram HMAC/replay validation, server cookie sessions, CSRF,
  ownership, текстові/голосові AI-чернетки, підтвердження, CRUD витрат,
  фільтри/пагінація, звіти за валютами, каталоги й налаштування. Точні decimal,
  optimistic versions, rate limits і транзакційна ідемпотентність.
- **Mini App:** Next.js/strict TypeScript, адаптивний салатовий UI, uk/en,
  light/dark/auto, текст і запис голосу, редагування, звіти й налаштування.
  Є серверна сесія, обробка невідомого результату та відновлення чернетки.
- **Telegram:** aiogram 3, головна reply keyboard, контекстні inline buttons,
  відповідні функції без введення команд, приватні чати й actor API,
  durable webhook inbox та worker/outbox. Bot не звертається до БД/AI.
- **БД:** початкова EF Core міграція для users/catalogues/expenses/drafts,
  sessions/proofs/conversations/deliveries, ownership FKs і constraints.
  AI-дані чернетки очищаються після завершення/expiry, callback version
  не перевикористовується після завершення діалогу.

Контракти генеруються з OpenAPI у TS/Pydantic. Додано Dockerfiles, Compose
із migration gate, Caddy gateway, CI та [інструкцію запуску](../../README.md).
Архітектурні рішення — [ADR](../knowledge/decisions.md).

## Перевірки

| Команда або сценарій | Результат | Межі / доказ |
| --- | --- | --- |
| .NET Release / format / tests | passed, 21/21 | Ізольована справжня PostgreSQL, EF migration; `output/mvp/backend-tests-final.log` |
| Frontend format/typecheck/lint/build | passed | `output/mvp/frontend-build-final.log` |
| Ruff / mypy / pytest | passed, 6/6 | Локальний API + mock Telegram transport, без живих повідомлень |
| Генерація контрактів двічі | passed, однакові hashes | `output/mvp/contracts-final.log` |
| Playwright desktop/mobile | passed | Login, draft/save/edit/report/category, uk/light, en/dark; `output/mvp/*png` |
| Docker build та Compose smoke | passed | Три images; migration exits 0, services healthy; gateway 200/401/404/403 |
| npm runtime audit | passed, 0 vulnerabilities | Dev advisory/peer обмеження: [dependencies](../../docs/dependencies.md) |
| docs checker / git diff --check | passed | Локальна перевірка документації |

Браузер і справжня PostgreSQL перевірені. Live OpenRouter, справжні Telegram
клієнти/voice recording/HTTPS cookie сумісність та Coolify не перевірені.
AI-тести використовують mock провайдера; ключі/model IDs не вигадувались.
GitHub CI лише налаштовано. Retention idempotency metadata та завершених
queues потребує окремої політики перед тривалою production експлуатацією.

## Сумісність і rollout

Це початкова schema, не зміна наявного production. Порядок наступного релізу:
backup → build images → одноразовий migrate → backend/bot/frontend/gateway →
HTTPS smoke обох каналів → окрема явна реєстрація webhook. Повна інструкція —
[deployment](../../docs/deployment.md). Жодного віддаленого деплою не виконано.

Уже відкритий Mini App оновлює дані й налаштування; stale versions дають
conflict, сесію перевіряє сервер. Старі inline callbacks перевіряють версію
діалогу/сутності та не повторюють фінансову операцію. Чернетки живуть 30 хв;
повторне підтвердження має один фінансовий ефект. Доставка повідомлення
після невідомої мережевої відповіді може дублювати notification.

Надалі контракти для старого клієнта змінювати адитивно й перевіряти окремо;
початкова версія не доводить сумісність із майбутнім релізом. Rollback —
повернути сумісні images і перевірити readiness/канали. Initial migration Down
видаляє дані й не є штатним rollback; відновлення БД — лише з перевіреного backup.
Коміт, push, live webhook і deployment не виконувались.
