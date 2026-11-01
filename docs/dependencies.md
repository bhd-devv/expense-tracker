# Залежності

Runtime та прямі бібліотеки зафіксовані в `global.json`, `.csproj`,
`package.json`, `pyproject.toml`; транзитивні — у трьох lock-файлах.
Next.js / React / TypeScript / Lucide / Zod / aiogram / httpx / EF Core / Npgsql
використовують permissive MIT/Apache/BSD ліцензії. Перевіряйте NOTICE й
ліцензії транзитивних залежностей при оновленні. ffmpeg установлюється
пакетним менеджером runtime image й має окремі LGPL/GPL умови збірки.

Перевірка 2026-11-01: `npm audit --omit=dev` — 0 вразливостей.
Повний audit показує moderate advisory minimatch (<11.0.1) у dev tooling,
що обробляє glob-шаблони; транзитивно зачіпає lint та генератор OpenAPI.
Автоматичний `audit fix --force` пропонує несумісні downgrade й не застосований.
Не запускайте tooling над недовіреними glob-шаблонами.

ESLint 10.14.0 проходить поточний lint із eslint-config-next 16.4.3.
Транзитивні React/import/a11y plugins декларують peer ESLint до 9; npm видає
peer warnings. ESLint 9 уже позначений unsupported, тому його не зафіксовано
як довготривале рішення. При оновленні config/plugins звірте peer ranges,
повторіть lint і audit; не примушуйте minimatch 11 поверх несумісних API.
Dev tooling не копіюється до frontend runtime standalone image.
