# Каталог іконок категорій

Стан: `completed` (локальна реалізація; runtime перевірки не запускалися). Дата: 2026-11-02.

## Результат і критерії приймання

100 іконок Lucide у 10 тематичних групах із назвами uk/en. Mini App:
натискання іконки категорії → пошук/групи → вибір і збереження; вибір іконки
під час створення категорії. Бот: вибір групи та іконки кнопками.
Іконка зберігається незалежно від назви/локалізації, відображається в
категоріях та витратах. Ownership і version перевіряє backend.

## Scope / non-goals

Існуюча `lucide-react@1.66.0` (ISC), каталог backend, additive migration,
OpenAPI/generated DTO, frontend picker, aiogram UX, регресії й документація.
Без довільних SVG/URL, нової залежності, коміту, deployment чи застосування БД.

## План

- [x] Прочитати правила, чинний код і залежності.
- [x] Додати каталог та persistence з серверною валідацією.
- [x] Реалізувати пошук/групи та редагування в обох каналах.
- [x] Синхронізувати контракти, додати регресії й документацію.
- [x] Переглянути diff, виконати обов'язкові docs/diff перевірки.

## Рішення й ризики

Наявна бібліотека Lucide достатня; явний набір імпортів не включає всю
бібліотеку. Backend володіє дозволеними ID та спільними назвами/групами.
Іконка — окреме nullable поле; відсутня означає системну за code або Tags.
Пропуск icon старими клієнтами зберігає чинне значення. Немає uploads,
зовнішніх зображень чи виконуваного користувацького SVG.

## Виконання й перевірки

Build/tests/lint/typecheck/browser/DB за локальними правилами — лише за
окремим запитом. Генерація DTO, форматування, content/diff/docs перевірки —
у межах цієї зміни. Виконано:

- `npm --prefix src/frontend run generate` — passed; типи з checked-in
  OpenAPI, live export/drift перевірка не запускалася.
- У `src/bot`: `.venv/bin/python -m datamodel_code_generator --input
  ../../contracts/openapi.json --input-file-type openapi --output
  src/expense_tracker_bot/models.py --output-model-type pydantic_v2.BaseModel
  --target-python-version 3.13 --use-standard-collections --use-union-operator
  --disable-timestamp --use-annotated --field-constraints --formatters ruff-format`
  — passed; `uv` недоступний, використано наявну `.venv`.
- Prettier `--write` змінених frontend файлів, Ruff `check --select I --fix`
  для імпортів generated models/handlers і Ruff `format` — passed; це
  форматування, не повний lint чи strict typing.
- Перегляд JSON/клієнтської мапи/API paths — 100 іконок, 10 груп, без
  дублів чи розбіжностей; `output/category-icons/content-review.log`.
- Обов'язкові docs/diff перевірки — результат у changelog та
  `output/category-icons/`.

Тести CategoryIconTests, PostgreSQL ownership/version/preservation і
параметризований uk/en bot flow додано, але не запускалися. Міграція
`AddCategoryIcons` не застосовувалася; browser/live Telegram/Coolify не
перевірялися. Немає висновку про production-готовність.

## Продовження / результат

Локальну реалізацію завершено. [Результат і rollout](../../changelog/022-category-icons.md),
[ADR-015](../../knowledge/decisions.md). Runtime перевірки — наступна дія
лише за окремим запитом; deployment не виконувався.
