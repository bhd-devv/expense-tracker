from .models import CatalogItemDto

COPY = {
    "en": {
        "add": "➕ Add expense",
        "expenses": "📋 Expenses",
        "reports": "📊 Reports",
        "categories": "🗂 Categories",
        "accounts": "💳 Accounts",
        "settings": "⚙️ Settings",
        "app": "📱 Open app",
        "welcome": (
            "A little clarity. Every day.\nSend a description or a voice note to add an expense."
        ),
        "prompt": (
            "Send an expense as text or a voice note. We will set today’s date and "
            "suggest a category."
        ),
        "save": "Save",
        "edit": "Edit",
        "cancel": "Cancel",
        "back": "Back",
        "delete": "Delete",
        "confirm": "Confirm deletion",
        "empty": "Nothing here yet.",
        "saved": "Expense saved ✓",
        "deleted": "Expense deleted",
        "draft": "Check your expense",
        "processing": "Understanding your expense…",
        "manual": "AI could not complete this draft. Use the buttons to fill it in manually.",
        "input": "Send the new value",
        "error": "Please check the value and try again.",
        "stale": "This action expired or the record changed. Reopen it from the menu.",
        "unavailable": "Service unavailable. Please try again shortly.",
        "amount": "Amount",
        "description": "Description",
        "date": "Date (YYYY-MM-DD)",
        "accountId": "Account",
        "categoryId": "Category",
        "name": "Name",
        "currency": "Currency",
        "create": "Create",
        "archive": "Archive",
        "restore": "Restore",
        "language": "Language",
        "theme": "Appearance",
        "timeZone": "Time zone",
        "defaultAccountId": "Default account",
        "aiConsent": "AI processing",
        "consent": (
            "Your text or audio and category list will be sent to OpenRouter. Temporary "
            "audio is removed after processing. Enable only if you agree."
        ),
        "enable": "Enable",
        "disable": "Disable",
        "none": "Ask each time",
        "auto": "Match device",
        "light": "Light",
        "dark": "Dark",
        "all": "All time",
        "month": "This month",
        "week": "Last 7 days",
        "custom": "Custom period",
        "periodPrompt": "Send dates: YYYY-MM-DD YYYY-MM-DD",
        "choose": "Choose",
        "previous": "Previous",
        "next": "Next",
        "consentNeeded": (
            "Enable AI processing in Settings before sending voice notes. Text drafts "
            "can be completed manually."
        ),
        "limit": "Voice note must be at most 2 minutes and 10 MB.",
        "food": "Food & drinks",
        "transport": "Transport",
        "shopping": "Shopping",
        "home": "Home",
        "health": "Health",
        "leisure": "Leisure",
        "other": "Other",
        "overview": "Overview",
        "refresh": "Refresh",
        "seed": "Add demo data · October 2026",
        "seedAdded": (
            "Added {count} demo expenses for October 2026: "
            "4 demo accounts, 7 categories, UAH/USD/EUR."
        ),
        "seedExists": (
            "Demo data for October 2026 has already been added. No new records were created."
        ),
        "seedPeriod": "To view them, select 2026-10-01 — 2026-10-31 in Expenses or Reports.",
        "seedLimit": (
            "Demo data needs 4 free account slots and up to 7 category slots "
            "(50 total each). Nothing was added."
        ),
        "seedUnknown": (
            "Could not confirm the result. Send /seed again: it will not add duplicate demo data."
        ),
    },
    "uk": {
        "add": "➕ Додати витрату",
        "expenses": "📋 Витрати",
        "reports": "📊 Звіти",
        "categories": "🗂 Категорії",
        "accounts": "💳 Рахунки",
        "settings": "⚙️ Налаштування",
        "app": "📱 Відкрити застосунок",
        "welcome": "Трохи ясності. Щодня.\nНадішліть опис або голосове, щоб додати витрату.",
        "prompt": (
            "Надішліть витрату текстом або голосом. Підставимо сьогоднішню дату й "
            "запропонуємо категорію."
        ),
        "save": "Зберегти",
        "edit": "Змінити",
        "cancel": "Скасувати",
        "back": "Назад",
        "delete": "Видалити",
        "confirm": "Підтвердити видалення",
        "empty": "Тут поки порожньо.",
        "saved": "Витрату збережено ✓",
        "deleted": "Витрату видалено",
        "draft": "Перевірте витрату",
        "processing": "Розбираємо витрату…",
        "manual": "AI не зміг заповнити чернетку. Внесіть значення вручну кнопками.",
        "input": "Надішліть нове значення",
        "error": "Перевірте значення та спробуйте ще раз.",
        "stale": "Дія застаріла або запис змінився. Відкрийте його з меню знову.",
        "unavailable": "Сервіс недоступний. Спробуйте трохи пізніше.",
        "amount": "Сума",
        "description": "Опис",
        "date": "Дата (РРРР-ММ-ДД)",
        "accountId": "Рахунок",
        "categoryId": "Категорія",
        "name": "Назва",
        "currency": "Валюта",
        "create": "Створити",
        "archive": "Архівувати",
        "restore": "Відновити",
        "language": "Мова",
        "theme": "Оформлення",
        "timeZone": "Часовий пояс",
        "defaultAccountId": "Типовий рахунок",
        "aiConsent": "AI-обробка",
        "consent": (
            "Текст або аудіо та категорії буде надіслано в OpenRouter. Тимчасове аудіо "
            "видаляється після обробки. Увімкніть лише за вашою згодою."
        ),
        "enable": "Увімкнути",
        "disable": "Вимкнути",
        "none": "Запитувати щоразу",
        "auto": "Як на пристрої",
        "light": "Світле",
        "dark": "Темне",
        "all": "За весь час",
        "month": "Цей місяць",
        "week": "Останні 7 днів",
        "custom": "Інший період",
        "periodPrompt": "Надішліть дати: РРРР-ММ-ДД РРРР-ММ-ДД",
        "choose": "Оберіть",
        "previous": "Назад",
        "next": "Далі",
        "consentNeeded": (
            "Увімкніть AI-обробку в налаштуваннях для голосових. Текстову чернетку можна"
            " заповнити вручну."
        ),
        "limit": "Голосове має бути до 2 хвилин і 10 МБ.",
        "food": "Їжа та напої",
        "transport": "Транспорт",
        "shopping": "Покупки",
        "home": "Дім",
        "health": "Здоров’я",
        "leisure": "Дозвілля",
        "other": "Інше",
        "overview": "Огляд",
        "refresh": "Оновити",
        "seed": "Додати демодані · жовтень 2026",
        "seedAdded": (
            "Додано {count} демовитрат за жовтень 2026: "
            "4 деморахунки, 7 категорій, UAH/USD/EUR."
        ),
        "seedExists": "Демодані за жовтень 2026 уже додано. Нові записи не створено.",
        "seedPeriod": "Для перегляду виберіть 2026-10-01 — 2026-10-31 у витратах або звітах.",
        "seedLimit": (
            "Потрібні 4 вільні місця для рахунків і до 7 для категорій "
            "(ліміт — по 50). Нічого не додано."
        ),
        "seedUnknown": (
            "Не вдалося підтвердити результат. Повторіть /seed: дублікати демоданих не з'являться."
        ),
    },
}
CURRENCIES = ("UAH", "USD", "EUR", "GBP", "PLN", "JPY")


def category(item: CatalogItemDto, t: dict[str, str]) -> str:
    return t.get(item.code or "", item.name)
