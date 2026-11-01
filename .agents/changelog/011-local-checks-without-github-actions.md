# Локальні перевірки за запитом замість GitHub Actions

Дата: 2026-11-01. Задача: не застосовується — завершена зміна процесу перевірок.

## Результат

За запитом користувача workflow `.github/workflows/checks.yml` прибрано
з робочого tracked дерева. Локальну копію збережено як
`output/local-checks/workflows/checks.yml` із короткою інструкцією поруч.
Правило `output/` уже є в `.gitignore`; workflow не потрапить у наступний
коміт за звичайного додавання файлів.

Build, тести, lint, typecheck, browser smoke та контейнерні/інтеграційні
перевірки надалі виконуються локально лише за явним запитом користувача.
Цю перевагу зафіксовано в development contract і SDLC; README описує
ручний запуск без обіцянок автоматичного CI. Тести, scripts і lockfiles
збережені. Копія YAML — довідка, а не новий локальний runner чи hook.

## Перевірки

| Команда або сценарій | Результат | Межі / доказ |
| --- | --- | --- |
| Перегляд diff та локальної копії workflow | passed | Копія зберігає вихідний YAML; у tracked workflow paths немає наявних файлів |
| `git check-ignore output/local-checks/workflows/checks.yml` | passed | Чинне правило `output/` |
| `python3 scripts/check_docs.py --write-index`, `--check`; `git diff --check` | passed | Індекс changelog, локальні посилання та формат документації |
| Build, lint, typecheck, тести, браузер і контейнерні перевірки | skipped | Користувач просив прибрати автоматичні Actions, не запускав задачу перевірки застосунку |

Telegram, OpenRouter, PostgreSQL, міграції та Coolify не перевірялися.

## Сумісність і rollout

Застосунок, API/DTO, Mini App, бот, БД і Compose не змінені. Після коміту
та push видалення workflow нові push/PR не запускатимуть цей workflow.
Наявні GitHub runs та їхня історія не змінювалися. Локальні зміни
підготовлені; коміт, push, deployment і зміни GitHub settings не виконувалися.
