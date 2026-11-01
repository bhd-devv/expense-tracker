export type Language = "uk" | "en";

const en = {
  brand: "EXPENSE TRACKER",
  overview: "Overview",
  expenses: "Expenses",
  reports: "Reports",
  accounts: "Accounts",
  categories: "Categories",
  settings: "Settings",
  more: "More",
  add: "Add",
  monthSpending: "This month",
  recent: "Recent expenses",
  seeAll: "All",
  empty: "No expenses yet",
  emptyBody: "Add one by text or voice.",
  quickAdd: "Add expense",
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
  draft: "New expense",
  amount: "Amount",
  description: "Description",
  category: "Category",
  account: "Account",
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
  archived: "Archived",
  archive: "Archive",
  restore: "Restore",
  create: "Create",
  rename: "Rename",
  language: "Language",
  theme: "Appearance",
  light: "Light",
  dark: "Dark",
  auto: "Match device",
  timezone: "Time zone",
  defaultAccount: "Default account",
  noDefault: "Ask each time",
  consent: "AI processing",
  consentBody:
    "Text, audio and categories are sent to OpenRouter. Temporary audio is deleted after processing.",
  consentNeeded:
    "Enable AI processing in Settings, or fill in the draft manually.",
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
  clarify: "Please check the amount, category and account.",
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
  accounts: "Рахунки",
  categories: "Категорії",
  settings: "Налаштування",
  more: "Ще",
  add: "Додати",
  monthSpending: "Цього місяця",
  recent: "Останні витрати",
  seeAll: "Усі",
  empty: "Витрат ще немає",
  emptyBody: "Додайте текстом або голосом.",
  quickAdd: "Додати витрату",
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
  draft: "Нова витрата",
  amount: "Сума",
  description: "Опис",
  category: "Категорія",
  account: "Рахунок",
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
  archived: "В архіві",
  archive: "Архівувати",
  restore: "Відновити",
  create: "Створити",
  rename: "Перейменувати",
  language: "Мова",
  theme: "Оформлення",
  light: "Світле",
  dark: "Темне",
  auto: "Як на пристрої",
  timezone: "Часовий пояс",
  defaultAccount: "Типовий рахунок",
  noDefault: "Запитувати щоразу",
  consent: "AI-обробка",
  consentBody:
    "Текст, аудіо й категорії надсилаються до OpenRouter. Тимчасове аудіо видаляється після обробки.",
  consentNeeded:
    "Увімкніть AI-обробку в налаштуваннях або заповніть чернетку вручну.",
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
  clarify: "Перевірте суму, категорію та рахунок.",
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

export function money(value: string, currency: string, lang: Language) {
  const [whole, fraction = ""] = value.split(".");
  const grouped = new Intl.NumberFormat(
    lang === "uk" ? "uk-UA" : "en-GB",
  ).format(BigInt(whole));
  const decimals = currency === "JPY" ? "" : (fraction + "00").slice(0, 2);
  return `${grouped}${decimals ? `${lang === "uk" ? "," : "."}${decimals}` : ""} ${currency}`;
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
