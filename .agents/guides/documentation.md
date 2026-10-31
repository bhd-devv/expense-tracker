# Перевірка документації та доказів

Запускай з кореня:

```bash
python3 scripts/check_docs.py --check
git diff --check
```

Скрипт перевіряє:

- наявність обов'язкових агентних документів;
- локальні Markdown-посилання, що не виходять за межі репозиторію;
- формат і унікальність числових changelog-префіксів;
- актуальність `docs/changes.md` щодо записів у `.agents/changelog/`;
- відсутність secrets-подібних файлів у tracked documentation paths.

Після додавання changelog спочатку виконай
`python3 scripts/check_docs.py --write-index`, потім `--check`.
`docs/changes.md` — generated index, його не редагуй вручну.

## Докази

Логи, скриншоти, API dumps і результати ручних сценаріїв зберігай у
`output/<назва-задачі>/`. Ця папка ігнорується Git. У task/changelog зазначай
команду або сценарій, дату, результат і межі доказу. Не зберігай там токени,
cookie, повні Telegram payloads, production credentials чи приватні нотатки.

Для UI явно вкажи, чи це browser smoke чи реальний Telegram Android/iOS/Desktop/Web.
Для даних вкажи, чи використовувалася справжня PostgreSQL, чи застосовувалась
міграція. Для deployment вкажи, чи перевірявся Coolify; успішний local build
не означає успішний deploy.
