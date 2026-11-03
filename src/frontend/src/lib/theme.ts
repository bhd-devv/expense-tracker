import { telegram } from "./telegram";

export function applyTheme(theme: string) {
  const app = telegram();
  const device = matchMedia("(prefers-color-scheme: dark)").matches
    ? "dark"
    : "light";
  const host = app?.initData ? app.colorScheme : device;
  document.documentElement.dataset.theme =
    theme === "dark" || (theme === "auto" && host === "dark")
      ? "dark"
      : "light";
  localStorage.setItem("lime-theme", theme);
}
