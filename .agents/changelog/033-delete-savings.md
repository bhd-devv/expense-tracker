# Видиме видалення накопичувальних рахунків

Дата: 2026-11-05. Задача: [видалення рахунків](../tasks/delete-savings-2026-11-05/README.md).

## Результат

У списку накопичувальних рахунків поруч із кнопкою відкриття з’явився
окремий кошик 44×44 з локалізованою назвою. У підсумку відкритого рахунка
видима текстова дія «Видалити рахунок». Обидві дії потребують підтвердження
з назвою й поясненням про всю історію. Видалення дозволено для порожніх
рахунків і рахунків із залишком/історією; після успіху — список і status.

DELETE `/api/savings/{id}?version=...` використовує чинні browser auth/CSRF,
ownership і owner lock. Version змінюється при новому entry; конкурентна
зміна відхиляє stale delete. Точний повтор успішного DELETE повертає 204.
Tombstone приховує рахунок/журнал і блокує нові операції та create retry,
зберігаючи службову історію й зарезервований ID. Unknown delete зберігається
в scoped sessionStorage й повторюється лише явно, включно після reload.
Інші витрати/доходи/категорії не змінюються. Бот не редагувався.

## Перевірки

| Команда або сценарій | Результат | Межі / доказ |
| --- | --- | --- |
| Release build і format змінених C# | passed | `output/delete-savings/backend-build.log`; 4 наявні analyzer warnings в інших тестах |
| `dotnet test ... --filter FullyQualifiedName~Savings` | passed | `output/delete-savings/integration.log`: 3/3; PostgreSQL 14, міграція застосована лише до ізольованої тестової БД |
| Frontend typecheck/lint/format/build | passed | `output/delete-savings/` logs |
| Генерація контрактів і повторна генерація | passed | `output/delete-savings/contract-drift.log`, drift none |
| Chromium list/detail 20 комбінацій мов/тем/viewport | passed | `output/delete-savings/browser-matrix.log`: 40 layout checks |
| Confirm cancel, видалення з detail/list, unknown response + reload + точний retry | passed | `output/delete-savings/browser-recovery.log`, `browser-list-delete.log` |
| Docs index/check, `git diff --check` | passed | `output/delete-savings/docs-index.log`, `docs-check.log`, `diff-check.log` |

Браузерний smoke — synthetic API; серверна поведінка окремо перевірена
на PostgreSQL. Реальні Telegram-клієнти, Docker images, production
PostgreSQL 17 та Coolify не перевірялися. Повний backend suite у цій зміні
не повторювався; відомі fixed-clock auth failures описані в задачі доходів.

## Сумісність і rollout

Additive `20261105152920_AddSavingsDeletion` додає `Deleted=false`/`Version=1`
до старих рахунків. Перед новим API/frontend потрібні backup, штатний
migrate job і зупинення попереднього backend. Старий backend не збільшує
version та ігнорує Deleted; його не можна запускати одночасно з новим.
Після першого видалення rollback — лише на deletion-aware API; інакше
видалені рахунки знову стануть доступними. Down відхилено. Нових env/сервісів
немає, старий frontend сумісний із новим DTO. Коміт, push і deployment
не виконувалися. Деталі — [deployment](../../docs/deployment.md).
