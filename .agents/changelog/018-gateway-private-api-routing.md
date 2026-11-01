# Приватний маршрут gateway до API для HTTPS та CSRF

Дата: 2026-11-01. Задача: не застосовується.

## Результат

Production logs показали 500 на `GET /api/auth/csrf` зі стеком
`DefaultAntiforgery.CheckSSLConfig`: API бачив HTTP перед видачею Secure
cookie. Наданий користувачем Docker inspect показав дві спільні мережі:
gateway мав `10.0.10.7` та `172.30.19.2`, backend — `10.0.10.5` та
`172.30.19.131`. Ім'я `backend` існувало в обох мережах, але trusted proxy
API — лише `172.30.19.2`. Фактичний TCP маршрут окремо не трасувався.

Backend отримав alias `expense-tracker-api-private` лише на private network,
Caddy використовує його для `/api/*`. Це прибирає неоднозначність DNS та
закріплює source IP gateway відповідно до наявного trusted proxy.
API-контракти, бот, frontend і схема БД не змінюються; Secure cookies та CSRF
залишаються обов'язковими. Уточнено [deployment](../../docs/deployment.md)
та ADR-008.

## Перевірки

| Команда або сценарій | Результат | Межі / доказ |
| --- | --- | --- |
| Аналіз наданих logs та Docker network inspect | Виконано | Встановлено HTTP у Antiforgery та неоднозначність DNS між двома мережами |
| Перегляд Compose/Caddy diff | Виконано | Private alias лише backend; gateway довірена адреса не змінена |
| `python3 scripts/check_docs.py --write-index`, `--check` | passed | `output/gateway-private-api-routing/docs-index.log`, `docs-check.log` |
| `git diff --check` | passed | `output/gateway-private-api-routing/diff-check.log` |
| Build, тести, Compose/container smoke | Не запускалися | Локальні перевірки застосунку лише за явним запитом користувача |

Браузер, Telegram-клієнт, PostgreSQL та Coolify після правки не перевірені.
Міграція не потрібна. Нових тестів для конфігураційної правки не додано;
операційна інструкція містить DNS та HTTPS/CSRF smoke для реальної топології.

## Сумісність і rollout

Потрібний redeploy з оновленим Compose backend та перебудованим gateway
image: restart не додає alias і не оновлює Caddyfile всередині image.
Сесії, відкриті Mini App, bot dialogs та дані сумісні; після redeploy
повторно відкрити Mini App і перевірити 200 CSRF та Telegram login.
Rollback на старий upstream повертає ризик неоднозначного DNS.
Коміт, push, deploy та webhook registration не виконувалися.
