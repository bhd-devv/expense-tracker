"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import {
  Archive,
  ArchiveRestore,
  CarFront,
  ChartPie,
  HeartPulse,
  House,
  PencilLine,
  ReceiptText,
  ShoppingBag,
  Ticket,
  UtensilsCrossed,
  ArrowUpRight,
  Check,
  ChevronLeft,
  ChevronRight,
  Leaf,
  LoaderCircle,
  LogOut,
  Mic,
  Plus,
  RefreshCw,
  Settings2,
  Square,
  Tags,
  WalletCards,
  SlidersHorizontal,
  X,
  type LucideIcon,
} from "lucide-react";
import { z } from "zod";
import {
  ApiError,
  authenticate,
  catalogSchema,
  draftSchema,
  expenseSchema,
  itemSchema,
  nothing,
  pageSchema,
  profileSchema,
  reportSchema,
  request,
  type Catalog,
  type Draft,
  type Expense,
  type ExpenseInput,
  type Item,
  type Profile,
} from "@/lib/api";
import {
  categoryName,
  dictionaries,
  language,
  money,
  records,
  type Copy,
} from "@/lib/i18n";
import { telegram } from "@/lib/telegram";

type Tab =
  | "overview"
  | "expenses"
  | "reports"
  | "accounts"
  | "categories"
  | "settings";
type Editor =
  | { kind: "draft"; value: Draft }
  | { kind: "expense"; value: Expense };
type Data = {
  catalog: Catalog;
  page: z.infer<typeof pageSchema>;
  report: z.infer<typeof reportSchema>;
};
const currencies = ["UAH", "USD", "EUR", "GBP", "PLN", "JPY"];
const nav = [
  { key: "overview", Icon: House },
  { key: "expenses", Icon: ReceiptText },
  { key: "reports", Icon: ChartPie },
  { key: "accounts", Icon: WalletCards },
  { key: "categories", Icon: Tags },
  { key: "settings", Icon: Settings2 },
] as const;
const categoryIcons: Record<string, LucideIcon> = {
  food: UtensilsCrossed,
  transport: CarFront,
  shopping: ShoppingBag,
  home: House,
  health: HeartPulse,
  leisure: Ticket,
  other: ReceiptText,
};
function CategoryIcon({ code }: { code?: string | null }) {
  const Icon =
    code && Object.hasOwn(categoryIcons, code) ? categoryIcons[code] : Tags;
  return <Icon size={22} strokeWidth={1.8} aria-hidden="true" />;
}
function errorText(error: unknown, t: Copy) {
  if (!(error instanceof ApiError)) return t.connection;
  if (error.code === "unknown") return t.unknown;
  if (error.status === 401) return t.signInBody;
  if (error.code === "draft_expired") return t.expired;
  if (error.status === 409) return t.conflict;
  if (error.code === "ai_consent_required") return t.consentNeeded;
  return error.status >= 500 ? t.connection : t.invalid;
}
function applyTheme(theme: string) {
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

export default function Workspace() {
  const [profile, setProfile] = useState<Profile>();
  const [data, setData] = useState<Data>();
  const [booting, setBooting] = useState(true);
  const [botName, setBotName] = useState("");
  const [tab, setTab] = useState<Tab>("overview");
  const [notice, setNotice] = useState("");
  const [fault, setFault] = useState("");
  const [busy, setBusy] = useState(false);
  const [text, setText] = useState("");
  const [editor, setEditor] = useState<Editor>();
  const [pending, setPending] = useState<{ requestId: string; text: string }>();
  const [recoverId, setRecoverId] = useState("");
  const [filters, setFilters] = useState({
    from: "",
    to: "",
    categoryId: "",
    accountId: "",
    page: 0,
  });
  const [recording, setRecording] = useState(false);
  const recorder = useRef<MediaRecorder | null>(null);
  const voiceTimeout = useRef<ReturnType<typeof setTimeout> | undefined>(
    undefined,
  );
  const lang = language(profile?.language ?? "uk");
  const t = dictionaries[lang];
  const query = new URLSearchParams(
    Object.entries(filters)
      .filter(([, v]) => v !== "")
      .map(([k, v]) => [k, String(v)]),
  ).toString();
  const month = profile?.today?.slice(0, 7);
  const loadVersion = useRef(0);
  const load = useCallback(async () => {
    const version = ++loadVersion.current;
    const [catalog, page, report] = await Promise.all([
      request("/catalog", catalogSchema),
      request(`/expenses?${tab === "overview" ? "page=0" : query}`, pageSchema),
      request(
        `/reports?${tab === "overview" && month ? `from=${month}-01` : query}`,
        reportSchema,
      ),
    ]);
    if (version === loadVersion.current) setData({ catalog, page, report });
  }, [query, tab, month]);
  useEffect(() => {
    authenticate()
      .then((value) => {
        setProfile(value);
        setRecoverId(sessionStorage.getItem("lime-draft") ?? "");
      })
      .catch((error) => setFault(errorText(error, dictionaries.uk)))
      .finally(() => setBooting(false));
    request("/config", z.object({ botUsername: z.string() }))
      .then((v) => setBotName(v.botUsername))
      .catch(() => {});
  }, []);
  useEffect(() => {
    if (!profile) return;
    applyTheme(profile.theme ?? "auto");
    document.documentElement.lang = lang;
    const update = () => applyTheme(profile.theme ?? "auto");
    const media = matchMedia("(prefers-color-scheme: dark)");
    media.addEventListener("change", update);
    telegram()?.onEvent("themeChanged", update);
    return () => {
      media.removeEventListener("change", update);
      telegram()?.offEvent("themeChanged", update);
    };
  }, [profile, lang, tab]);
  useEffect(() => {
    if (!profile) return;
    let active = true;
    const refresh = () =>
      load().catch((error) => {
        if (active) setFault(errorText(error, t));
      });
    const focus = () => {
      void refresh();
      request("/profile", profileSchema)
        .then((value) => {
          if (active) setProfile(value);
        })
        .catch((error) => {
          if (active) setFault(errorText(error, t));
        });
    };
    void refresh();
    window.addEventListener("focus", focus);
    return () => {
      active = false;
      window.removeEventListener("focus", focus);
    };
  }, [profile, load, t]);
  useEffect(
    () => () => {
      clearTimeout(voiceTimeout.current);
      if (recorder.current?.state === "recording") recorder.current.stop();
      recorder.current?.stream.getTracks().forEach((track) => track.stop());
    },
    [],
  );
  useEffect(() => {
    const app = telegram();
    const back =
      app?.initData && app.isVersionAtLeast("6.1") ? app.BackButton : undefined;
    if (!back) return;
    const goBack = () => setTab("overview");
    if (tab !== "overview" && !editor) {
      back.show();
      back.onClick(goBack);
    } else back.hide();
    return () => {
      back.offClick(goBack);
      back.hide();
    };
  }, [tab, editor]);
  async function action(fn: () => Promise<void>) {
    setFault("");
    setNotice("");
    setBusy(true);
    try {
      await fn();
    } catch (error) {
      setFault(errorText(error, t));
    } finally {
      setBusy(false);
    }
  }
  function showDraft(draft: Draft) {
    if (draft.status === "saved") {
      setNotice(t.saved);
      sessionStorage.removeItem("lime-draft");
      setRecoverId("");
      return;
    }
    if (draft.status === "cancelled") {
      sessionStorage.removeItem("lime-draft");
      setRecoverId("");
      return;
    }
    sessionStorage.setItem("lime-draft", draft.id);
    setRecoverId(draft.id);
    setEditor({ kind: "draft", value: draft });
  }
  async function createDraft() {
    const input = pending ?? { requestId: crypto.randomUUID(), text };
    setPending(input);
    await action(async () => {
      showDraft(await request("/drafts", draftSchema, "POST", input));
      setPending(undefined);
      setText("");
    });
  }
  async function record() {
    if (recording) {
      recorder.current?.stop();
      return;
    }
    if (!profile?.aiConsent) {
      setFault(t.consentNeeded);
      return;
    }
    try {
      const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
      const media = new MediaRecorder(stream);
      recorder.current = media;
      const chunks: BlobPart[] = [];
      media.ondataavailable = (event) => chunks.push(event.data);
      media.onstop = () => {
        clearTimeout(voiceTimeout.current);
        stream.getTracks().forEach((track) => track.stop());
        setRecording(false);
        const form = new FormData();
        form.set("requestId", crypto.randomUUID());
        form.set(
          "audio",
          new Blob(chunks, { type: media.mimeType }),
          "voice.webm",
        );
        void action(async () =>
          showDraft(await request("/drafts/audio", draftSchema, "POST", form)),
        );
      };
      media.start();
      setRecording(true);
      voiceTimeout.current = setTimeout(() => media.stop(), 120000);
    } catch {
      setFault(t.micError);
    }
  }
  async function saveEditor(input: ExpenseInput) {
    if (!editor) return;
    await action(async () => {
      await request(
        editor.kind === "draft"
          ? `/drafts/${editor.value.id}/confirm`
          : `/expenses/${editor.value.id}`,
        expenseSchema,
        editor.kind === "draft" ? "POST" : "PUT",
        input,
      );
      if (editor.kind === "draft") {
        sessionStorage.removeItem("lime-draft");
        setRecoverId("");
      }
      setEditor(undefined);
      setNotice(t.saved);
      await load();
    });
  }
  async function cancelEditor() {
    if (!editor) return;
    if (editor.kind === "expense") {
      setEditor(undefined);
      return;
    }
    await action(async () => {
      await request(`/drafts/${editor.value.id}/cancel`, draftSchema, "POST", {
        version: editor.value.version,
      });
      sessionStorage.removeItem("lime-draft");
      setRecoverId("");
      setEditor(undefined);
    });
  }
  if (!profile)
    return (
      <main className="auth-page">
        <div className="auth-orbit">
          <Leaf size={48} />
        </div>
        <h1>{booting ? t.loading : t.signInTitle}</h1>
        <p>{t.signInBody}</p>
        {!booting && (
          <>
            <a
              className="button primary"
              href={
                botName
                  ? `https://t.me/${encodeURIComponent(botName)}`
                  : "https://t.me/"
              }
            >
              {t.openTelegram}
              <ArrowUpRight size={18} />
            </a>
            <button className="quiet" onClick={() => location.reload()}>
              {t.retry}
            </button>
          </>
        )}
        {fault && !booting && (
          <p role="alert" className="error">
            {fault}
          </p>
        )}
      </main>
    );
  const catalog = data?.catalog;
  return (
    <div className="app-shell">
      <aside className="sidebar">
        <a
          className="brand"
          href="#"
          aria-label="Lime"
          onClick={() => setTab("overview")}
        >
          <span className="brand-mark">
            <Leaf size={23} />
          </span>
          lime<span className="brand-dot">.</span>
        </a>
        <nav aria-label={t.brand}>
          {nav.map(({ key, Icon }) => (
            <button
              key={key}
              aria-label={t[key]}
              aria-current={tab === key ? "page" : undefined}
              className={tab === key ? "selected" : ""}
              onClick={() => {
                setFault("");
                setTab(key);
              }}
            >
              <Icon size={23} strokeWidth={1.8} />
              <span>{key === "settings" ? t.settingsNav : t[key]}</span>
            </button>
          ))}
        </nav>
      </aside>
      <main className="main-content">
        <header className="topbar">
          <span className="mobile-brand">
            <Leaf size={23} strokeWidth={1.8} /> lime
          </span>
          <button
            className="icon-button"
            aria-label={t.refresh}
            onClick={() => void action(load)}
          >
            <RefreshCw size={21} strokeWidth={1.8} />
          </button>
        </header>
        {tab === "overview" ? (
          <h1 className="sr-only">{t.overview}</h1>
        ) : (
          <section className="page-heading">
            <h1>{t[tab]}</h1>
            {tab !== "settings" && (
              <button
                className="button primary"
                onClick={() => {
                  if (tab === "accounts" || tab === "categories") {
                    document.getElementById(`${tab}-name`)?.focus();
                    return;
                  }
                  setTab("overview");
                  requestAnimationFrame(() =>
                    document.getElementById("quick-add")?.focus(),
                  );
                }}
              >
                <Plus size={20} />
                {t.add}
              </button>
            )}
          </section>
        )}
        {(fault || notice) && (
          <div
            className={fault ? "feedback error" : "feedback success"}
            role={fault ? "alert" : "status"}
          >
            {fault || notice}
            <button
              className="icon-button"
              aria-label={t.close}
              onClick={() => {
                setFault("");
                setNotice("");
              }}
            >
              <X size={16} />
            </button>
          </div>
        )}
        {!data ? (
          <div className="loading">
            <LoaderCircle className="spin" />
            {t.loading}
          </div>
        ) : (
          <>
            {tab === "overview" && (
              <>
                <div className="overview-grid">
                  <section className="balance-card">
                    <span className="card-kicker">{t.monthSpending}</span>
                    {data.report.totals.length ? (
                      data.report.totals.map((total) => (
                        <div key={total.currency} className="balance-value">
                          {money(total.amount, total.currency, lang)}
                        </div>
                      ))
                    ) : (
                      <div className="balance-value">
                        {money("0", profile.currency ?? "UAH", lang)}
                      </div>
                    )}
                  </section>
                  <section className="quick-card">
                    <h2>{t.quickAdd}</h2>
                    <form
                      onSubmit={(event) => {
                        event.preventDefault();
                        void createDraft();
                      }}
                    >
                      <label className="sr-only" htmlFor="quick-add">
                        {t.description}
                      </label>
                      <textarea
                        id="quick-add"
                        maxLength={2000}
                        placeholder={t.placeholder}
                        value={text}
                        disabled={busy || !!pending}
                        onChange={(event) => setText(event.target.value)}
                      />
                      <div className="quick-actions">
                        <button
                          type="button"
                          className={`button secondary ${recording ? "recording" : ""}`}
                          disabled={busy}
                          aria-label={recording ? t.stop : t.voice}
                          onClick={() => void record()}
                        >
                          {recording ? (
                            <Square size={22} />
                          ) : (
                            <Mic size={22} strokeWidth={1.8} />
                          )}
                          <span>{recording ? t.stop : t.voiceShort}</span>
                        </button>
                        <button
                          className="button primary"
                          aria-label={pending ? t.retry : t.parse}
                          disabled={busy || (!text.trim() && !pending)}
                        >
                          <span>{pending ? t.retry : t.parse}</span>
                          {busy ? (
                            <LoaderCircle size={17} className="spin" />
                          ) : (
                            <ChevronRight size={20} />
                          )}
                        </button>
                      </div>
                    </form>
                    {(recording || busy) && (
                      <p className="hint" role="status">
                        {recording ? t.recording : t.processing}
                      </p>
                    )}
                    {recoverId && (
                      <button
                        className="recover"
                        onClick={() =>
                          void action(async () =>
                            showDraft(
                              await request(
                                `/drafts/${recoverId}`,
                                draftSchema,
                              ),
                            ),
                          )
                        }
                      >
                        {t.draftStored} · {t.recover}
                      </button>
                    )}
                  </section>
                </div>
                <div className="section-heading">
                  <div>
                    <h2>{t.recent}</h2>
                  </div>
                  <button className="quiet" onClick={() => setTab("expenses")}>
                    {t.seeAll}
                    <ChevronRight size={20} />
                  </button>
                </div>
              </>
            )}
            {(tab === "expenses" || tab === "reports") && (
              <details className="filter-panel">
                <summary>
                  <SlidersHorizontal size={20} strokeWidth={1.8} />
                  {t.filters}
                  {[
                    filters.from,
                    filters.to,
                    filters.categoryId,
                    filters.accountId,
                  ].filter(Boolean).length > 0 && (
                    <span className="filter-count">
                      {
                        [
                          filters.from,
                          filters.to,
                          filters.categoryId,
                          filters.accountId,
                        ].filter(Boolean).length
                      }
                    </span>
                  )}
                </summary>
                <div className="filters" aria-label={t.filters}>
                  <label>
                    {t.from}
                    <input
                      type="date"
                      value={filters.from}
                      onChange={(e) =>
                        setFilters({
                          ...filters,
                          from: e.target.value,
                          page: 0,
                        })
                      }
                    />
                  </label>
                  <label>
                    {t.to}
                    <input
                      type="date"
                      value={filters.to}
                      onChange={(e) =>
                        setFilters({ ...filters, to: e.target.value, page: 0 })
                      }
                    />
                  </label>
                  {(["categoryId", "accountId"] as const).map((key) => (
                    <label key={key}>
                      {key === "categoryId" ? t.category : t.account}
                      <select
                        value={filters[key]}
                        onChange={(e) =>
                          setFilters({
                            ...filters,
                            [key]: e.target.value,
                            page: 0,
                          })
                        }
                      >
                        <option value="">{t.all}</option>
                        {(key === "categoryId"
                          ? data.catalog.categories
                          : data.catalog.accounts
                        ).map((item) => (
                          <option key={item.id} value={item.id}>
                            {categoryName(item, t)}
                          </option>
                        ))}
                      </select>
                    </label>
                  ))}
                </div>
              </details>
            )}
            {(tab === "overview" || tab === "expenses") && (
              <section className="expense-list">
                {data.page.items.length ? (
                  <>
                    <div className="list-head">
                      <span>{t.description}</span>
                      <span>{t.category}</span>
                      <span>{t.date}</span>
                      <span>{t.amount}</span>
                      <span />
                    </div>
                    {data.page.items
                      .slice(0, tab === "overview" ? 5 : 20)
                      .map((expense) => {
                        const cat = data.catalog.categories.find(
                          (c) => c.id === expense.categoryId,
                        );
                        return (
                          <div className="expense-row" key={expense.id}>
                            <div className="expense-description">
                              <span className="category-icon">
                                <CategoryIcon code={cat?.code} />
                              </span>
                              <div>
                                <strong>{expense.description}</strong>
                                <small className="expense-meta">
                                  {cat ? categoryName(cat, t) : "—"}
                                  {" · "}
                                  {new Intl.DateTimeFormat(lang, {
                                    day: "numeric",
                                    month: "short",
                                  }).format(
                                    new Date(expense.date + "T12:00:00"),
                                  )}
                                </small>
                              </div>
                            </div>
                            <span className="category-tag">
                              {cat ? categoryName(cat, t) : "—"}
                            </span>
                            <time dateTime={expense.date}>
                              {new Intl.DateTimeFormat(lang, {
                                day: "numeric",
                                month: "short",
                              }).format(new Date(expense.date + "T12:00:00"))}
                            </time>
                            <strong className="expense-amount">
                              {money(expense.amount, expense.currency, lang)}
                            </strong>
                            <button
                              className="icon-button"
                              aria-label={`${t.edit}: ${expense.description}`}
                              onClick={() =>
                                setEditor({ kind: "expense", value: expense })
                              }
                            >
                              <PencilLine size={20} strokeWidth={1.8} />
                            </button>
                          </div>
                        );
                      })}
                  </>
                ) : (
                  <Empty title={t.empty} body={t.emptyBody} />
                )}
              </section>
            )}
            {tab === "expenses" && (
              <div className="pagination">
                <button
                  className="button secondary"
                  disabled={!filters.page}
                  onClick={() =>
                    setFilters({ ...filters, page: filters.page - 1 })
                  }
                >
                  <ChevronLeft size={17} />
                  {t.previous}
                </button>
                <span>
                  {t.page} {filters.page + 1}
                </span>
                <button
                  className="button secondary"
                  disabled={!data.page.hasMore}
                  onClick={() =>
                    setFilters({ ...filters, page: filters.page + 1 })
                  }
                >
                  {t.next}
                  <ChevronRight size={17} />
                </button>
              </div>
            )}
            {tab === "reports" && (
              <section className="report-grid">
                {!data.report.totals.length && (
                  <Empty title={t.noReport} body={t.emptyBody} />
                )}
                {data.report.totals.map((total) => (
                  <article className="panel report-card" key={total.currency}>
                    <span className="eyebrow">{t.total}</span>
                    <h2 className="report-total">
                      {money(total.amount, total.currency, lang)}
                    </h2>
                    <p>{records(total.count, lang)}</p>
                    {data.report.categories
                      .filter((r) => r.currency === total.currency)
                      .map((row) => {
                        const category = data.catalog.categories.find(
                          (c) => c.id === row.categoryId,
                        );
                        const scaled = (v: string) =>
                          BigInt(
                            v
                              .replace(".", "")
                              .padEnd(v.split(".")[0].length + 4, "0"),
                          );
                        const percent =
                          Number(
                            (scaled(row.amount) * 10000n) /
                              (scaled(total.amount) || 1n),
                          ) / 100;
                        return (
                          <div className="report-row" key={row.categoryId}>
                            <div>
                              <span>
                                {category ? categoryName(category, t) : "—"}
                              </span>
                              <strong>
                                {money(row.amount, row.currency, lang)}
                              </strong>
                            </div>
                            <div className="bar-track">
                              <div
                                className="bar-fill"
                                style={{ width: `${percent}%` }}
                              />
                            </div>
                          </div>
                        );
                      })}
                  </article>
                ))}
              </section>
            )}
            {(tab === "accounts" || tab === "categories") && (
              <CatalogEditor
                key={tab}
                kind={tab}
                items={
                  tab === "accounts"
                    ? data.catalog.accounts
                    : data.catalog.categories
                }
                t={t}
                busy={busy}
                perform={action}
                reload={async () => {
                  await load();
                  setProfile(await request("/profile", profileSchema));
                }}
              />
            )}
            {tab === "settings" && (
              <Preferences
                key={profile.version}
                profile={profile}
                catalog={data.catalog}
                t={t}
                busy={busy}
                onSave={(value) =>
                  action(async () => {
                    setProfile(
                      await request("/profile", profileSchema, "PUT", value),
                    );
                    setNotice(dictionaries[language(value.language)].updated);
                  })
                }
                logout={() =>
                  void action(async () => {
                    await request("/auth/logout", nothing, "POST");
                    sessionStorage.removeItem("lime-draft");
                    setProfile(undefined);
                    setFault(t.signedOut);
                  })
                }
              />
            )}
          </>
        )}
      </main>
      {editor && catalog && (
        <ExpenseEditor
          editor={editor}
          catalog={catalog}
          t={t}
          busy={busy}
          error={fault}
          onSave={saveEditor}
          onCancel={cancelEditor}
          onDelete={() => {
            if (editor.kind === "expense" && window.confirm(t.confirmDelete))
              void action(async () => {
                await request(
                  `/expenses/${editor.value.id}?version=${editor.value.version}`,
                  nothing,
                  "DELETE",
                );
                setEditor(undefined);
                setNotice(t.deleted);
                await load();
              });
          }}
        />
      )}
    </div>
  );
}
function Empty({ title, body }: { title: string; body: string }) {
  return (
    <div className="empty">
      <span className="empty-icon">
        <ReceiptText size={32} strokeWidth={1.8} />
      </span>
      <h2>{title}</h2>
      <p>{body}</p>
    </div>
  );
}
function ExpenseEditor({
  editor,
  catalog,
  t,
  busy,
  error,
  onSave,
  onCancel,
  onDelete,
}: {
  editor: Editor;
  catalog: Catalog;
  t: Copy;
  busy: boolean;
  error: string;
  onSave: (input: ExpenseInput) => Promise<void>;
  onCancel: () => Promise<void>;
  onDelete: () => void;
}) {
  const ref = useRef<HTMLDialogElement>(null);
  const [value, setValue] = useState({
    amount: editor.value.amount ?? "",
    description: editor.value.description,
    date: editor.value.date,
    accountId: editor.value.accountId ?? "",
    categoryId: editor.value.categoryId ?? "",
    version: editor.value.version,
  });
  useEffect(() => {
    ref.current?.showModal();
  }, []);
  return (
    <dialog
      ref={ref}
      className="editor-dialog"
      aria-labelledby="expense-editor-title"
      onCancel={(e) => {
        e.preventDefault();
        if (!busy) void onCancel();
      }}
    >
      <form
        onSubmit={(e) => {
          e.preventDefault();
          void onSave(value);
        }}
      >
        <div className="dialog-top">
          <ReceiptText size={24} strokeWidth={1.8} aria-hidden="true" />
          <button
            type="button"
            className="icon-button"
            aria-label={t.close}
            disabled={busy}
            onClick={() => void onCancel()}
          >
            <X size={21} />
          </button>
        </div>
        <h2 id="expense-editor-title">
          {editor.kind === "draft" ? t.draft : t.edit}
        </h2>
        {editor.kind === "draft" && editor.value.errorCode && (
          <p className="hint">
            {editor.value.errorCode === "ai_consent_required"
              ? t.consentNeeded
              : editor.value.status === "failed"
                ? t.aiError
                : t.clarify}
          </p>
        )}
        {error && (
          <p className="error" role="alert">
            {error}
          </p>
        )}
        <label>
          {t.amount}
          <input
            autoFocus
            required
            inputMode="decimal"
            pattern="[0-9]+([.,][0-9]{1,4})?"
            value={value.amount}
            onChange={(e) =>
              setValue({ ...value, amount: e.target.value.replace(",", ".") })
            }
          />
        </label>
        <label>
          {t.description}
          <input
            required
            maxLength={500}
            value={value.description}
            onChange={(e) =>
              setValue({ ...value, description: e.target.value })
            }
          />
        </label>
        <div className="form-grid">
          {(["accountId", "categoryId"] as const).map((key) => (
            <label key={key}>
              {key === "accountId" ? t.account : t.category}
              <select
                required
                value={value[key]}
                onChange={(e) => setValue({ ...value, [key]: e.target.value })}
              >
                <option value="">{t.choose}</option>
                {(key === "accountId" ? catalog.accounts : catalog.categories)
                  .filter((item) => !item.archived || item.id === value[key])
                  .map((item) => (
                    <option key={item.id} value={item.id}>
                      {categoryName(item, t)}
                      {item.currency ? ` · ${item.currency}` : ""}
                    </option>
                  ))}
              </select>
            </label>
          ))}
        </div>
        <label>
          {t.date}
          <input
            required
            type="date"
            value={value.date}
            onChange={(e) => setValue({ ...value, date: e.target.value })}
          />
        </label>
        {editor.kind === "draft" && editor.value.transcript && (
          <details>
            <summary>{t.transcript}</summary>
            <p>{editor.value.transcript}</p>
          </details>
        )}
        <div className="dialog-actions">
          {editor.kind === "expense" && (
            <button
              type="button"
              disabled={busy}
              className="quiet danger"
              onClick={onDelete}
            >
              {t.remove}
            </button>
          )}
          <button
            type="button"
            disabled={busy}
            className="button secondary"
            onClick={() => void onCancel()}
          >
            {t.cancel}
          </button>
          <button disabled={busy} className="button primary">
            {busy ? (
              <LoaderCircle className="spin" size={17} />
            ) : (
              <Check size={17} />
            )}
            {editor.kind === "draft" ? t.save : t.update}
          </button>
        </div>
      </form>
    </dialog>
  );
}
function CatalogEditor({
  kind,
  items,
  t,
  busy,
  perform,
  reload,
}: {
  kind: "accounts" | "categories";
  items: Item[];
  t: Copy;
  busy: boolean;
  perform: (fn: () => Promise<void>) => Promise<void>;
  reload: () => Promise<void>;
}) {
  const [editing, setEditing] = useState<Item>();
  const [name, setName] = useState("");
  const [currency, setCurrency] = useState("UAH");
  async function submit() {
    await perform(async () => {
      await request(
        `/${kind}${editing ? `/${editing.id}` : ""}`,
        itemSchema,
        editing ? "PUT" : "POST",
        {
          name,
          currency: kind === "accounts" ? currency : null,
          archived: editing?.archived ?? false,
          version: editing?.version ?? 0,
        },
      );
      setName("");
      setEditing(undefined);
      await reload();
    });
  }
  return (
    <div className="catalog-layout">
      <section className="panel">
        {items.map((item) => (
          <div className="catalog-row" key={item.id}>
            <div className="catalog-name">
              <span className="category-icon">
                {kind === "accounts" ? (
                  <WalletCards size={22} strokeWidth={1.8} />
                ) : (
                  <CategoryIcon code={item.code} />
                )}
              </span>
              <div>
                <strong>{categoryName(item, t)}</strong>
                <small>{item.archived ? t.archived : item.currency}</small>
              </div>
            </div>
            <button
              className="icon-button"
              aria-label={`${t.rename}: ${categoryName(item, t)}`}
              title={t.rename}
              disabled={busy}
              onClick={() => {
                setEditing(item);
                setName(categoryName(item, t));
                setCurrency(item.currency ?? "UAH");
              }}
            >
              <PencilLine size={20} strokeWidth={1.8} />
            </button>
            <button
              className="icon-button"
              aria-label={`${item.archived ? t.restore : t.archive}: ${categoryName(item, t)}`}
              title={item.archived ? t.restore : t.archive}
              disabled={busy}
              onClick={() =>
                void perform(async () => {
                  await request(`/${kind}/${item.id}`, itemSchema, "PUT", {
                    ...item,
                    archived: !item.archived,
                  });
                  await reload();
                })
              }
            >
              {item.archived ? (
                <ArchiveRestore size={20} strokeWidth={1.8} />
              ) : (
                <Archive size={20} strokeWidth={1.8} />
              )}
            </button>
          </div>
        ))}
      </section>
      <form
        className="panel catalog-form"
        onSubmit={(e) => {
          e.preventDefault();
          void submit();
        }}
      >
        <h2>{editing ? t.edit : t.create}</h2>
        <label>
          {t.name}
          <input
            id={`${kind}-name`}
            required
            maxLength={60}
            value={name}
            onChange={(e) => setName(e.target.value)}
          />
        </label>
        {kind === "accounts" && (
          <label>
            {t.currency}
            <select
              value={currency}
              onChange={(e) => setCurrency(e.target.value)}
            >
              {currencies.map((c) => (
                <option key={c}>{c}</option>
              ))}
            </select>
          </label>
        )}
        <div className="dialog-actions">
          {editing && (
            <button
              className="quiet"
              type="button"
              onClick={() => {
                setEditing(undefined);
                setName("");
              }}
            >
              {t.cancel}
            </button>
          )}
          <button disabled={busy} className="button primary">
            {editing ? <Check size={20} /> : <Plus size={20} />}
            {editing ? t.update : t.create}
          </button>
        </div>
      </form>
    </div>
  );
}
function Preferences({
  profile,
  catalog,
  t,
  busy,
  onSave,
  logout,
}: {
  profile: Profile;
  catalog: Catalog;
  t: Copy;
  busy: boolean;
  onSave: (value: Profile) => Promise<void>;
  logout: () => void;
}) {
  const [value, setValue] = useState(profile);
  return (
    <form
      className="panel preferences"
      onSubmit={(e) => {
        e.preventDefault();
        void onSave(value);
      }}
    >
      <div className="form-grid">
        <label>
          {t.language}
          <select
            value={value.language}
            onChange={(e) => setValue({ ...value, language: e.target.value })}
          >
            <option value="uk">Українська</option>
            <option value="en">English</option>
          </select>
        </label>
        <label>
          {t.theme}
          <select
            value={value.theme}
            onChange={(e) => {
              setValue({ ...value, theme: e.target.value });
              applyTheme(e.target.value);
            }}
          >
            {["auto", "light", "dark"].map((v) => (
              <option key={v} value={v}>
                {v === "auto" ? t.auto : v === "light" ? t.light : t.dark}
              </option>
            ))}
          </select>
        </label>
        <label>
          {t.timezone}
          <input
            required
            maxLength={100}
            value={value.timeZone}
            list="timezones"
            onChange={(e) => setValue({ ...value, timeZone: e.target.value })}
          />
          <datalist id="timezones">
            {[
              "Europe/Kyiv",
              "Europe/London",
              "Europe/Warsaw",
              "America/New_York",
              "UTC",
            ].map((v) => (
              <option key={v}>{v}</option>
            ))}
          </datalist>
        </label>
        <label>
          {t.currency}
          <select
            value={value.currency}
            onChange={(e) => setValue({ ...value, currency: e.target.value })}
          >
            {currencies.map((c) => (
              <option key={c}>{c}</option>
            ))}
          </select>
        </label>
        <label>
          {t.defaultAccount}
          <select
            value={value.defaultAccountId ?? ""}
            onChange={(e) =>
              setValue({ ...value, defaultAccountId: e.target.value || null })
            }
          >
            <option value="">{t.noDefault}</option>
            {catalog.accounts
              .filter((a) => !a.archived)
              .map((a) => (
                <option key={a.id} value={a.id}>
                  {a.name} · {a.currency}
                </option>
              ))}
          </select>
        </label>
      </div>
      <div className="consent">
        <label>
          <input
            type="checkbox"
            checked={value.aiConsent}
            onChange={(e) =>
              setValue({ ...value, aiConsent: e.target.checked })
            }
          />
          {t.consent}
        </label>
        <p>{t.consentBody}</p>
      </div>
      <div className="settings-actions">
        <button
          type="button"
          className="quiet danger"
          disabled={busy}
          onClick={logout}
        >
          <LogOut size={17} />
          {t.logout}
        </button>
        <button className="button primary" disabled={busy}>
          <Check size={17} />
          {t.update}
        </button>
      </div>
    </form>
  );
}
