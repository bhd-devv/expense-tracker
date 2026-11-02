export type Language = "uk" | "en";

const en = {
  brand: "EXPENSE TRACKER",
  overview: "Overview",
  expenses: "Expenses",
  reports: "Reports",
  categories: "Categories",
  savings: "Savings accounts",
  newSavings: "Create account",
  emptySavings: "No savings accounts yet",
  savingsBalance: "Balance",
  savingsAdded: "Added",
  savingsSpent: "Spent",
  deposit: "Add money",
  withdrawal: "Spend money",
  depositRecord: "Deposit",
  withdrawalRecord: "Withdrawal",
  savingsHistory: "Transaction history",
  emptySavingsHistory: "No transactions yet",
  savingsNote: "Note (optional)",
  savingsBack: "All accounts",
  savingsHome: "Back to overview",
  savingsSaved: "Transaction saved",
  insufficientSavings:
    "Not enough money in this account. Refresh the balance or enter a smaller amount.",
  savingsUnknown:
    "The result is unknown. Retry this same operation to establish the result before adding another.",
  savingsLimit: "The savings account limit has been reached.",
  savingsPending:
    "An operation is awaiting confirmation. Retry it to establish the result.",

  emptyCategories: "No categories yet",
  monthlyLimit: "Monthly limit",
  setLimit: "Set limit",
  editLimit: "Edit limit",
  removeLimit: "Remove limit",
  limitUsed: "Used",
  limitExceeded: "Limit exceeded",
  limitCurrencyHint: "All category expenses count toward the limit.",
  icon: "Icon",
  chooseIcon: "Choose icon",
  searchIcons: "Search icons…",
  iconGroup: "Icon group",
  allIcons: "All icons",
  noIcons: "No icons found. Try another search.",
  settings: "Settings",
  add: "Add",
  monthSpending: "This month",
  recent: "Recent expenses",
  seeAll: "All",
  empty: "No expenses yet",
  emptyBody: "Add one by text or voice.",
  quickAdd: "Add expense",
  inputMode: "Expense entry",
  manualMode: "Manual",
  aiMode: "AI · text or voice",
  useAi: "Use AI for expense entry",
  manualEntry: "Enter amount, category and details",
  manualHint: "Fill in the fields. You can change today’s date.",
  clarifyDate:
    "Choose the expense date: the date in the description was unclear.",
  placeholder: "Coffee 85 UAH…",
  parse: "Continue",
  processing: "Processing…",
  voice: "Record a voice note",
  voiceShort: "Voice",
  stop: "Stop recording",
  recording: "Recording · up to 2 minutes",
  micError: "Microphone unavailable. You can still type your expense.",
  saved: "Expense saved",
  updated: "Saved",
  deleted: "Expense deleted",
  cancel: "Cancel",
  save: "Save",
  update: "Save",
  edit: "Edit",
  remove: "Delete",
  confirmDelete: "Delete this expense? This action cannot be undone.",
  confirmDeleteCategory: "Delete this category? This action cannot be undone.",
  categoryInUse:
    "This category is used by expenses or active drafts. Change their category first.",
  draft: "New expense",
  amount: "Amount",
  description: "Description",
  category: "Category",
  date: "Date",
  choose: "Choose…",
  transcript: "Recognised text",
  filters: "Filters",
  from: "From",
  to: "To",
  all: "All",
  previous: "Previous",
  next: "Next",
  page: "Page",
  noReport: "Nothing to report yet",
  name: "Name",
  currency: "Currency",
  currencyHint:
    "Applies everywhere. Changing the currency keeps all amounts unchanged.",
  archived: "Archived",
  create: "Create",
  rename: "Rename",
  language: "Language",
  theme: "Appearance",
  light: "Light",
  dark: "Dark",
  auto: "Match device",
  timezone: "Time zone",
  consent: "AI processing",
  consentBody:
    "Text, audio and categories are sent to OpenRouter. Temporary audio is deleted after processing.",
  consentNeeded:
    "Enable AI processing in Settings, or choose Manual entry there.",
  logout: "Sign out",
  signedOut:
    "You have signed out. Reopen the app in Telegram to sign in again.",
  openTelegram: "Open in Telegram",
  signInTitle: "Your expenses",
  signInBody: "Open in Telegram to sign in securely.",
  telegramLaunch:
    "Return to the bot and open the app with the button below its message or the Lime menu button next to the message field.",
  retry: "Try again",
  loading: "Loading…",
  connection: "Unable to connect. Check your connection and try again.",
  rateLimit: "Too many requests. Wait a minute and try again.",
  conflict:
    "This record has changed. Refresh and check it before saving again.",
  invalid: "Check the entered values and try again.",
  aiError: "AI is unavailable. You can fill in this draft manually.",
  clarify: "Please check the amount and category.",
  expired: "This draft expired. Start a new expense.",
  unknown:
    "The result is not yet known. Refresh to check before creating another expense.",
  food: "Food & drinks",
  transport: "Transport",
  shopping: "Shopping",
  home: "Home",
  health: "Health",
  leisure: "Leisure",
  other: "Other",
  close: "Close",
  total: "Total",
  refresh: "Refresh",
  draftStored: "Your draft is still here",
  recover: "Open draft",
};

const uk: typeof en = {
  brand: "ТРЕКЕР ВИТРАТ",
  overview: "Огляд",
  expenses: "Витрати",
  reports: "Звіти",
  categories: "Категорії",
  savings: "Накопичувальні рахунки",
  newSavings: "Створити рахунок",
  emptySavings: "Поки немає накопичувальних рахунків",
  savingsBalance: "Залишок",
  savingsAdded: "Додано",
  savingsSpent: "Витрачено",
  deposit: "Поповнити",
  withdrawal: "Витратити",
  depositRecord: "Поповнення",
  withdrawalRecord: "Списання",
  savingsHistory: "Історія операцій",
  emptySavingsHistory: "Поки немає операцій",
  savingsNote: "Нотатка (необов’язково)",
  savingsBack: "Усі рахунки",
  savingsHome: "На головну",
  savingsSaved: "Операцію збережено",
  insufficientSavings:
    "На рахунку недостатньо коштів. Оновіть залишок або вкажіть меншу суму.",
  savingsUnknown:
    "Результат невідомий. Повторіть цю саму операцію, щоб встановити результат перед додаванням іншої.",
  savingsLimit: "Досягнуто ліміту накопичувальних рахунків.",
  savingsPending:
    "Є операція з невідомим результатом. Повторіть її, щоб встановити результат.",

  emptyCategories: "Категорій ще немає",
  monthlyLimit: "Місячний ліміт",
  setLimit: "Встановити ліміт",
  editLimit: "Змінити ліміт",
  removeLimit: "Прибрати ліміт",
  limitUsed: "Використано",
  limitExceeded: "Ліміт перевищено",
  limitCurrencyHint: "До ліміту входять усі витрати категорії.",
  icon: "Іконка",
  chooseIcon: "Вибрати іконку",
  searchIcons: "Пошук іконок…",
  iconGroup: "Група іконок",
  allIcons: "Усі іконки",
  noIcons: "Іконок не знайдено. Спробуйте інший запит.",
  settings: "Налаштування",
  add: "Додати",
  monthSpending: "Цього місяця",
  recent: "Останні витрати",
  seeAll: "Усі",
  empty: "Витрат ще немає",
  emptyBody: "Додайте текстом або голосом.",
  quickAdd: "Додати витрату",
  inputMode: "Введення витрат",
  manualMode: "Вручну",
  aiMode: "AI · текст або голос",
  useAi: "Використовувати AI для введення витрат",
  manualEntry: "Вказати суму, категорію та деталі",
  manualHint: "Заповніть поля. Сьогоднішню дату можна змінити.",
  clarifyDate: "Виберіть дату витрати: дата в описі неоднозначна.",
  placeholder: "Кава 85 грн…",
  parse: "Далі",
  processing: "Обробляємо…",
  voice: "Записати голосове повідомлення",
  voiceShort: "Голос",
  stop: "Завершити запис",
  recording: "Записуємо · до 2 хвилин",
  micError: "Мікрофон недоступний. Можна ввести витрату текстом.",
  saved: "Витрату збережено",
  updated: "Збережено",
  deleted: "Витрату видалено",
  cancel: "Скасувати",
  save: "Зберегти",
  update: "Зберегти",
  edit: "Редагувати",
  remove: "Видалити",
  confirmDelete: "Видалити цю витрату? Цю дію не можна скасувати.",
  confirmDeleteCategory: "Видалити цю категорію? Цю дію не можна скасувати.",
  categoryInUse:
    "Категорія використовується у витратах або активних чернетках. Спочатку змініть їхню категорію.",
  draft: "Нова витрата",
  amount: "Сума",
  description: "Опис",
  category: "Категорія",
  date: "Дата",
  choose: "Оберіть…",
  transcript: "Розпізнаний текст",
  filters: "Фільтри",
  from: "Від",
  to: "До",
  all: "Усі",
  previous: "Назад",
  next: "Далі",
  page: "Сторінка",
  noReport: "Поки немає даних для звіту",
  name: "Назва",
  currency: "Валюта",
  currencyHint:
    "Спільна для всіх сум. Зміна валюти змінює лише позначення, без перерахунку.",
  archived: "В архіві",
  create: "Створити",
  rename: "Перейменувати",
  language: "Мова",
  theme: "Оформлення",
  light: "Світле",
  dark: "Темне",
  auto: "Як на пристрої",
  timezone: "Часовий пояс",
  consent: "AI-обробка",
  consentBody:
    "Текст, аудіо й категорії надсилаються до OpenRouter. Тимчасове аудіо видаляється після обробки.",
  consentNeeded:
    "Увімкніть AI-обробку в налаштуваннях або виберіть там ручне введення.",
  logout: "Вийти",
  signedOut:
    "Ви вийшли. Відкрийте застосунок знову в Telegram для нового входу.",
  openTelegram: "Відкрити в Telegram",
  signInTitle: "Ваші витрати",
  signInBody: "Відкрийте в Telegram для безпечного входу.",
  telegramLaunch:
    "Поверніться до бота й відкрийте застосунок кнопкою під його повідомленням або кнопкою меню Lime біля поля введення.",
  retry: "Спробувати ще",
  loading: "Завантаження…",
  connection:
    "Не вдалося підключитися. Перевірте з’єднання та спробуйте ще раз.",
  rateLimit: "Забагато запитів. Зачекайте хвилину й спробуйте ще раз.",
  conflict:
    "Запис уже змінився. Оновіть дані та перевірте їх перед збереженням.",
  invalid: "Перевірте введені значення та спробуйте ще раз.",
  aiError: "AI зараз недоступний. Заповніть цю чернетку вручну.",
  clarify: "Перевірте суму та категорію.",
  expired: "Строк дії чернетки минув. Створіть нову витрату.",
  unknown:
    "Результат поки невідомий. Оновіть дані перед створенням ще однієї витрати.",
  food: "Їжа та напої",
  transport: "Транспорт",
  shopping: "Покупки",
  home: "Дім",
  health: "Здоров’я",
  leisure: "Дозвілля",
  other: "Інше",
  close: "Закрити",
  total: "Разом",
  refresh: "Оновити",
  draftStored: "Ваша чернетка збережена",
  recover: "Відкрити чернетку",
};

export const dictionaries = { uk, en };
export type Copy = typeof en;
export function language(value: string): Language {
  return value === "en" ? "en" : "uk";
}
export function categoryName(
  item: { name: string; code?: string | null },
  t: Copy,
) {
  const code = item.code;
  if (
    code === "food" ||
    code === "transport" ||
    code === "shopping" ||
    code === "home" ||
    code === "health" ||
    code === "leisure" ||
    code === "other"
  )
    return t[code];
  return item.name;
}

export function currencySymbol(currency: string) {
  const symbols: Record<string, string> = {
    UAH: "₴",
    USD: "$",
    EUR: "€",
    GBP: "£",
    PLN: "zł",
    JPY: "¥",
  };
  return symbols[currency] ?? currency;
}

export function money(value: string, currency: string, lang: Language) {
  const [whole, fraction = ""] = value.split(".");
  const grouped = new Intl.NumberFormat(
    lang === "uk" ? "uk-UA" : "en-GB",
  ).format(BigInt(whole));
  const decimals = fraction
    ? fraction.padEnd(2, "0")
    : currency === "JPY"
      ? ""
      : "00";
  return `${grouped}${decimals ? `${lang === "uk" ? "," : "."}${decimals}` : ""} ${currencySymbol(currency)}`;
}

export function records(count: number, lang: Language) {
  const plural = new Intl.PluralRules(lang).select(count);
  const word =
    lang === "en"
      ? plural === "one"
        ? "record"
        : "records"
      : plural === "one"
        ? "запис"
        : plural === "few"
          ? "записи"
          : "записів";
  return `${count} ${word}`;
}
