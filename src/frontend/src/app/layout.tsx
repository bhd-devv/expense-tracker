import type { Metadata, Viewport } from "next";
import Script from "next/script";
import "./globals.css";

export const metadata: Metadata = {
  title: "Lime · Expense tracker",
  description: "Витрати під контролем. Your money, in focus.",
};
export const viewport: Viewport = {
  width: "device-width",
  initialScale: 1,
  viewportFit: "cover",
  themeColor: "#f6f7f3",
};

export default function Layout({
  children,
}: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="uk" suppressHydrationWarning>
      <body>
        <Script
          src="https://telegram.org/js/telegram-web-app.js"
          strategy="beforeInteractive"
        />
        <Script
          id="initial-theme"
          strategy="beforeInteractive"
        >{`try{var t=localStorage.getItem('lime-theme');document.documentElement.dataset.theme=t==='dark'||(t!=='light'&&(window.Telegram?.WebApp?.colorScheme==='dark'||(!window.Telegram?.WebApp?.initData&&matchMedia('(prefers-color-scheme: dark)').matches)))?'dark':'light'}catch{}`}</Script>
        {children}
      </body>
    </html>
  );
}
