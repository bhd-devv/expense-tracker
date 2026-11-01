# Caddyfile у gateway image замість host bind mount

Дата: 2026-11-01. Задача: не застосовується — завершене виправлення запуску gateway.

## Результат

Новий лог Coolify від користувача підтвердив проходження build і перехід
PostgreSQL, backend, frontend та bot у healthy. Запуск gateway завершився
помилкою OCI mount: host path `deploy/Caddyfile` не відповідав файлу
`/etc/caddy/Caddyfile`. Checkout у helper container був доступний для build,
але це не забезпечило наявності коректного файла на Docker host.

Додано [gateway.Dockerfile](../../deploy/gateway.Dockerfile) на зафіксованій
базі `caddy:2.10.2-alpine`, який копіює чинний Caddyfile в image. Compose
збирає `expense-tracker-gateway:${RELEASE_TAG:-local}` із кореня репозиторію.
Host bind mount конфігурації видалено. Routing, healthcheck, private IP,
порядок залежностей і persistent data volumes збережено.
Deployment contract та ADR-008 уточнено: routing конфігурація постачається
разом із gateway image, а зміна Caddyfile потребує його перебудови.

## Перевірки

| Команда або сценарій | Результат | Межі / доказ |
| --- | --- | --- |
| Аналіз наданого deployment log | passed | Build і healthy dependencies підтверджені логом користувача; поточний gateway у цьому deployment не стартував |
| `docker compose --project-directory . --env-file .env.example -f deploy/compose.yaml config --format json` | passed | `output/coolify-gateway-image/compose.json`, без реальних secrets і без Docker daemon |
| Перегляд resolved Compose та Dockerfile | passed | Усі Dockerfile існують; gateway не має host mounts, копіює Caddyfile через COPY; private IP та data volumes збережено |
| `python3 scripts/check_docs.py --write-index`, `--check`; `git diff --check` | passed | Індекс changelog, посилання й формат |
| Новий container build, health/routing smoke та application tests | skipped | Повну локальну перевірку користувач не запитував; виконано цільовий розбір і перевірку конфігурації для помилки |

Власний віддалений доступ до Coolify не використовувався. Новий gateway
image ще не збирався й не запускався. Telegram, OpenRouter, браузер,
операції PostgreSQL та міграції не перевірялися в цій задачі.

## Сумісність і rollout

Для rollout потрібні commit/push і повторний Deploy у Coolify з перебудовою
gateway. Параметри домену й порту 8080 не змінюються. Старий host path
`deploy/Caddyfile` більше не використовується; його видалення не потрібне
для нового запуску. Нових env-змінних, API/DTO змін або міграції БД немає.
Можна повернути попередній сумісний gateway image; повернення старої bind
конфігурації потребувало б коректного файла на Docker host.
Коміт, push, deployment та реєстрація webhook не виконувалися.
