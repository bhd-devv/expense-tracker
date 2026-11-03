const en = {
  title: "Tasks",
  today: "Today",
  scheduled: "For the day",
  all: "All tasks",
  add: "Add task",
  name: "Task name",
  namePlaceholder: "For example, read for 20 minutes",
  repeat: "Repeat",
  start: "Start date",
  daily: "Every day",
  twoDays: "Every 2 days",
  threeDays: "Every 3 days",
  weekly: "Every week",
  save: "Save",
  cancel: "Cancel",
  edit: "Rename task",
  delete: "Delete task",
  deleteConfirm:
    "Delete this task? It will also disappear from past days. This cannot be undone.",
  done: "Completed",
  notDone: "Not completed",
  mark: "Mark as completed",
  unmark: "Mark as not completed",
  emptyTitle: "Your first task",
  emptyBody: "Add a task and choose how often to repeat it.",
  restTitle: "No tasks scheduled",
  restBody: "Choose another date or add a task.",
  progress: "completed",
  date: "Date",
  refresh: "Refresh",
  retry: "Retry the same change",
  updated: "Saved",
  loading: "Loading tasks…",
  signIn:
    "Open this app from the Tasks button in your Telegram bot to sign in.",
  connection: "Could not load tasks. Try refreshing.",
  invalid: "Check the task details and try again.",
  conflict: "This task changed. Refresh the list and reopen the task.",
  rateLimit: "Too many requests. Please wait and try again.",
  limit: "You can have up to 100 tasks. Delete an unused task first.",
  notDue:
    "This task is not scheduled for that date, or the date is in the future. Refresh the list.",
  unknown:
    "The result is unknown. Refresh to check the list or retry the same change to establish its result.",
  storage: "Allow session storage in this browser before saving changes.",
  scheduleHint:
    "The schedule starts on this date. Skipped days do not shift it.",
  renameHint: "To change the schedule, create a new task.",
};

type Copy = { [Key in keyof typeof en]: string };
const uk: Copy = {
  title: "Завдання",
  today: "Сьогодні",
  scheduled: "На день",
  all: "Усі завдання",
  add: "Додати завдання",
  name: "Назва завдання",
  namePlaceholder: "Наприклад, читати 20 хвилин",
  repeat: "Повторення",
  start: "Дата початку",
  daily: "Щодня",
  twoDays: "Кожні 2 дні",
  threeDays: "Кожні 3 дні",
  weekly: "Щотижня",
  save: "Зберегти",
  cancel: "Скасувати",
  edit: "Перейменувати завдання",
  delete: "Видалити завдання",
  deleteConfirm:
    "Видалити завдання? Воно також зникне з попередніх днів. Цю дію не можна скасувати.",
  done: "Виконано",
  notDone: "Не виконано",
  mark: "Позначити виконаним",
  unmark: "Прибрати відмітку виконання",
  emptyTitle: "Ваше перше завдання",
  emptyBody: "Додайте завдання й виберіть, як часто його повторювати.",
  restTitle: "Немає завдань за розкладом",
  restBody: "Виберіть іншу дату або додайте завдання.",
  progress: "виконано",
  date: "Дата",
  refresh: "Оновити",
  retry: "Повторити ту саму зміну",
  updated: "Збережено",
  loading: "Завантаження завдань…",
  signIn:
    "Для входу відкрийте цей застосунок кнопкою «Завдання» у своєму Telegram-боті.",
  connection: "Не вдалося завантажити завдання. Спробуйте оновити.",
  invalid: "Перевірте дані завдання та спробуйте ще раз.",
  conflict: "Завдання змінилося. Оновіть список і відкрийте завдання знову.",
  rateLimit: "Забагато запитів. Зачекайте та спробуйте ще раз.",
  limit: "Можна мати до 100 завдань. Спочатку видаліть непотрібне.",
  notDue:
    "На цю дату завдання не заплановане або дата ще не настала. Оновіть список.",
  unknown:
    "Результат невідомий. Оновіть список для перевірки або повторіть ту саму зміну, щоб встановити результат.",
  storage: "Дозвольте сховище сесії в браузері, щоб зберігати зміни.",
  scheduleHint:
    "Розклад починається з цієї дати. Пропущені дні його не зсувають.",
  renameHint: "Щоб змінити розклад, створіть нове завдання.",
};

export const taskCopy = { uk, en };
export function frequency(interval: number, t: Copy) {
  switch (interval) {
    case 2:
      return t.twoDays;
    case 3:
      return t.threeDays;
    case 7:
      return t.weekly;
    default:
      return t.daily;
  }
}
