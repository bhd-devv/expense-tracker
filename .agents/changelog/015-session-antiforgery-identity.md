# Стандартна ідентичність сесії для Antiforgery

Дата: 2026-11-01. Задача: повідомлена помилка входу Mini App, окремий task не потрібен.

## Результат

У browser principal після перевіреного Telegram login було лише власне
поле `user_id`. ASP.NET Core Antiforgery не використовує його автоматично
як унікальний subject, тому отримання CSRF-токена після login могло
завершитися `InvalidOperationException` і зірвати bootstrap Mini App.

Нові сесії мають також `ClaimTypes.NameIdentifier` із внутрішнім User ID.
Cookie validation доповнює захищені старі сесії та перевидає ticket,
відхиляє невалідні/суперечливі ідентифікатори й зберігає абсолютний TTL.
Контракт уточнено в [security.md](../security.md). Зміна не потребує нового ADR.

Додано регресійні тести отримання справжнього Antiforgery-токена для
нового й старого principal та відхилення прострочених/невалідних сесій.
Підключення до БД для цих тестових сценаріїв не потрібне.

## Перевірки

| Команда або сценарій | Результат | Межі / доказ |
| --- | --- | --- |
| Перегляд auth/CSRF flow та diff | Виявлено дефект claim | Логи користувача містять лише тип exception: конкретний production stack не встановлено |
| `python3 scripts/check_docs.py --write-index`, `--check` | passed | `output/session-antiforgery/docs-index.log`, `docs-check.log` |
| `git diff --check` | passed | `output/session-antiforgery/diff-check.log` |
| Application build і тести | Не запускалися | Збережено вимогу запускати перевірки лише за окремим запитом |

Реальні Telegram-клієнти, браузер, PostgreSQL і Coolify не перевірялися.
Успішний startup backend із повідомленням `Now listening` не доводить
успішність окремих API-запитів. Без stack trace не можна виключити іншу
причину production `InvalidOperationException`, зокрема неправильне
розпізнавання HTTPS за proxy під час створення Secure CSRF cookie.

## Сумісність і rollout

Потрібне оновлення backend; нових env, API DTO або міграцій немає.
Чинні захищені сесії оновлюються при наступному запиті без нового login.
Ownership лишається за перевіреним `user_id`; Telegram validation і CSRF
залишаються обов'язковими. Коміт, push і деплой не виконувалися.
Паралельні зміни logging/deployment та попередній `/seed` збережено.
