# Місячні ліміти категорій і прогрес

Стан: `completed` (локальна реалізація; runtime не перевірено). Дата: 2026-11-02.

## Мета й критерії

Для кожної категорії можна задати, змінити й прибрати місячний ліміт із
валютою. Mini App показує смужку, суму витрат/ліміт і відсоток у рядку;
редактор відкривається поруч із категорією. Бот має ті самі можливості
кнопками й показує текстову смужку. Перевищення видно понад 100%, але воно
не блокує створення витрат. Без ліміту немає вигаданого 0%.

## Рішення й scope

Один необов'язковий ліміт на категорію, одна явна підтримувана валюта.
Період — поточний календарний місяць за timezone профілю. Backend рахує
лише збережені витрати цієї категорії в цій валюті, без FX/чернеток.
Суми — decimal/numeric, API money strings; відсоток рахує .NET.
Окремий PUT limit захищений ownership/version; старі rename/icon запити
не змінюють ліміт. Additive migration; без реальних міграцій/деплою/коміту.
Наявні незакомічені зміни іконок, видалення й inline rename зберігаються.

## Кроки

- [x] Прочитати контекст, правила й код.
- [x] Backend, міграція, OpenAPI/generated DTO.
- [x] Inline UI та aiogram UX, uk/en, progress/accessibility.
- [x] Регресії, документація, docs/diff checks.

## Перевірки

Build/tests/lint/typecheck/browser/PostgreSQL/Coolify лише за явним запитом
згідно з development.md. Генерація DTO, форматування, content review та
обов'язкові docs/diff checks виконуються у цій задачі. Результати — тут і
в одному changelog, докази лише `output/category-limits/`.

Генерація frontend: `npm --prefix src/frontend run generate` — passed.
Bot у `src/bot`: `.venv/bin/python -m datamodel_code_generator --input
../../contracts/openapi.json --input-file-type openapi --output
src/expense_tracker_bot/models.py --output-model-type pydantic_v2.BaseModel
--target-python-version 3.13 --use-standard-collections --use-union-operator
--disable-timestamp --use-annotated --field-constraints --formatters ruff-format`
— passed; наявна `.venv`, без встановлення залежностей. Prettier `--write`
для зміненого frontend, Ruff `check --select I --fix` лише імпортів generated
models і `format` для models/handlers/copy/bot integration tests — passed.

Додано PostgreSQL і uk/en bot fake transport регресії; вони не запускалися.
`AddCategoryLimits` і snapshot підготовлено вручну за чинним EF-шаблоном;
DB migration та build не запускалися. Live OpenAPI export не виконувався.
Результати docs/diff і межі: [changelog](../../changelog/025-category-monthly-limits.md).

Перегляд OpenAPI і category model у snapshot/designer — passed; precision
та constraint узгоджені. `python3 scripts/check_docs.py --write-index`,
`--check` та `git diff --check` — passed, логи в `output/category-limits/`.

## Результат

Ліміти доступні в обох каналах, редактор і смужка біля відповідної категорії.
Наступні дії лише за окремим запитом: runtime/DB/browser/Telegram перевірки
та застосування міграції; commit/push/deployment не виконані.
