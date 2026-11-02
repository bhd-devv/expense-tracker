# Візуальне оновлення й плавні анімації Mini App

Стан: `completed`. Дата: 2026-11-02.

## Результат і критерії приймання

Оновлено поверхні, типографіку сум, контролі та іконки Mini App у двох
темах. Додано узгоджений рух розділів, списків, діалогів, inline-форм,
смужок і перемикача ручного/AI-введення. Reduced motion вимикає рух;
семантика форм, локалізація, native dialog/progress і keyboard focus
збережені в коді. Візуальна прийнятність у браузері ще не перевірена.

## Scope / non-goals

Frontend CSS, keyed wrapper поточної вкладки й чинний design contract.
API, фінансові правила, дані, Telegram-бот і залежності не змінювалися.
Нова міграція не потрібна. Попередні функціональні зміни збережено.

## План

- [x] Прочитати чинні правила, UI й локальний Next.js guide.
- [x] Оновити спільні visual/motion tokens і компонування.
- [x] Переглянути зміст змін і відформатувати frontend.
- [x] Оновити design contract, changelog, STATUS й індекс документації.
- [ ] Browser flow у двох темах, uk/en, 320/360/390/430 px і desktop —
  відкладено до явного запиту на runtime-перевірку за правилами репозиторію.

## Рішення й ризики

CSS keyframes/transitions без нової animation-бібліотеки. Ключ wrapper
залежить лише від вкладки, тому reload даних не перемонтовує форми.
Підняття на hover обмежено hover-capable fine pointer. Прогрес лишається
native, грошові значення й серверні відсотки не змінюються. Псевдоелементи
native progress можуть анімуватися по-різному в різних WebView; це потребує
браузерного й Telegram QA. Міграції попередніх задач не застосовувалися.

## Виконання й перевірки

| Команда або сценарій | Результат | Доказ / обмеження |
| --- | --- | --- |
| Локальний Prettier `--write` для CSS і TSX | passed | `output/interface-polish/format.log`; форматування, не typecheck |
| Перегляд CSS/JSX | passed | `output/interface-polish/content-review.log`; без візуального QA |
| `python3 scripts/check_docs.py --write-index` | passed | `output/interface-polish/docs-index.log` |
| `python3 scripts/check_docs.py --check` | passed | `output/interface-polish/docs-check.log` |
| `git diff --check` | passed | `output/interface-polish/diff-check.log` |
| Build, lint, typecheck, tests, browser, Telegram, PostgreSQL, Coolify | skipped | Не запускалися за відсутності явного запиту на перевірку |

## Продовження / результат

Локальна реалізація завершена: [changelog](../../changelog/026-interface-polish.md).
Коміт, push і deployment не виконувалися. Наступна перевірка за запитом —
frontend build/typecheck та browser flow з reduced motion, клавіатурою,
двома темами й вузькими viewport; окремо — справжні Telegram-клієнти.
