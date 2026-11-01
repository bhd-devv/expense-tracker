# Шляхи Compose від кореня checkout для Coolify

Дата: 2026-11-01. Задача: не застосовується — завершене виправлення помилки деплою.

## Результат

Наданий користувачем лог Coolify показав запуск Compose із
`--project-directory /artifacts/<checkout>` та `-f <checkout>/deploy/compose.yaml`.
Попередній `context: ..` у цьому режимі виходив за межі checkout; Docker
шукав `src` у `/artifacts/src`, повідомляв про відсутні Dockerfile і
завершував deployment до збірки images.

У [Compose](../../deploy/compose.yaml) усі build contexts змінено на `.`;
Dockerfile paths лишилися відносними до кореня репозиторію. Bind mount
gateway виправлено з `./Caddyfile` на `./deploy/Caddyfile`, щоб він також
працював із тією самою базою paths. У development/deployment документації
зафіксовано явний `--project-directory .` для локальних команд і параметри
Coolify: Base Directory `/`, Compose Location `/deploy/compose.yaml`.

## Перевірки

| Команда або сценарій | Результат | Межі / доказ |
| --- | --- | --- |
| `docker compose --project-directory . --env-file .env.example -f deploy/compose.yaml config --format json` | passed | `output/coolify-compose-paths/compose.json`, лише placeholders, без Docker daemon |
| Перегляд resolved paths у Compose JSON | passed | `migrate`, `backend`, `frontend`, `bot`: context дорівнює кореню репозиторію й Dockerfile існує; gateway має наявний read-only `deploy/Caddyfile` |
| `python3 scripts/check_docs.py --write-index`, `--check`; `git diff --check` | passed | Індекс, локальні посилання й whitespace |
| Container build, application tests і health/routing smoke | skipped | Виконано лише цільову перевірку шляхів для наданої помилки; повну перевірку користувач не запитував |

Живий Coolify deployment, Telegram, OpenRouter, PostgreSQL, міграції та
браузер не перевірялися. Успішне вирішення шляхів не доводить готовності
всього production deployment.

## Сумісність і rollout

Application images, API/DTO, Mini App, бот і схема БД не змінені. Для нового
деплою потрібні commit/push цього виправлення і повторний запуск Coolify з
коренем checkout як Base Directory. Локальний Compose надалі запускається
з кореня репозиторію з явним `--project-directory .`. Коміт, push, deployment
та реєстрація webhook не виконувалися.
