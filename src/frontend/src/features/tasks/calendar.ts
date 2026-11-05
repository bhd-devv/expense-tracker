// Calendar navigation only; due dates and completion permissions come from the API.
export function shiftDate(value: string, days: number) {
  const date = new Date(`${value}T12:00:00Z`);
  date.setUTCDate(date.getUTCDate() + days);
  return date.toISOString().slice(0, 10);
}

export function weekDates(value: string) {
  const weekday = new Date(`${value}T12:00:00Z`).getUTCDay();
  const monday = shiftDate(value, -((weekday + 6) % 7));
  return Array.from({ length: 7 }, (_, index) => shiftDate(monday, index));
}
