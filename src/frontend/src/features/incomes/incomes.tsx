"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import {
  ChevronLeft,
  ChevronRight,
  PencilLine,
  Plus,
  RefreshCw,
  Tags,
  Trash2,
  X,
} from "lucide-react";
import {
  ApiError,
  catalogSchema,
  itemSchema,
  nothing,
  request,
  type Catalog,
  type Item,
  type Profile,
} from "@/lib/api";
import { money, type Copy, type Language } from "@/lib/i18n";
import { CategoryIcon } from "@/features/categories/category-icon";
import IconPicker from "@/features/categories/icon-picker";
import {
  incomePageSchema,
  incomeSchema,
  pendingSchema,
  type Income,
  type Pending,
} from "./api";
import type { z } from "zod";

function failure(error: unknown, t: Copy) {
  if (!(error instanceof ApiError)) return t.connection;
  if (error.code === "income_category_in_use") return t.incomeCategoryInUse;
  if (error.status === 409) return t.conflict;
  if (error.status === 429) return t.rateLimit;
  if (error.status === 401) return t.signInBody;
  return error.status >= 500 || error.status === 0 ? t.connection : t.invalid;
}

export default function Incomes({
  profile,
  lang,
  t,
  categoriesView,
  refreshVersion,
  onCategories,
  onIncomes,
}: {
  profile: Profile;
  lang: Language;
  t: Copy;
  categoriesView: boolean;
  refreshVersion: number;
  onCategories: () => void;
  onIncomes: () => void;
}) {
  const [catalog, setCatalog] = useState<Catalog>();
  const [page, setPage] = useState<z.infer<typeof incomePageSchema>>();
  const [filters, setFilters] = useState({
    from: "",
    to: "",
    categoryId: "",
    page: 0,
  });
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [pending, setPending] = useState<Pending>();
  const [fault, setFault] = useState("");
  const [notice, setNotice] = useState("");
  const [editor, setEditor] = useState<Income | "new">();
  const [categoryEditor, setCategoryEditor] = useState<Item>();
  const [categoryName, setCategoryName] = useState("");
  const [icon, setIcon] = useState("tags");
  const [picker, setPicker] = useState(false);
  const storageKey = `lime-income-pending-${profile.id}`;
  const sequence = useRef(0);
  const activeMutation = useRef(false);
  const query = new URLSearchParams(
    Object.entries(filters)
      .filter(([, v]) => v !== "")
      .map(([k, v]) => [k, String(v)]),
  ).toString();
  const load = useCallback(async () => {
    const version = ++sequence.current;
    const [nextCatalog, nextPage] = await Promise.all([
      request("/income-categories", catalogSchema),
      request(`/incomes?${query}`, incomePageSchema),
    ]);
    if (version === sequence.current) {
      setCatalog(nextCatalog);
      setPage(nextPage);
    }
  }, [query]);
  const refresh = useCallback(async () => {
    setLoading(true);
    try {
      await load();
    } catch (error) {
      setFault(failure(error, t));
    } finally {
      setLoading(false);
    }
  }, [load, t]);
  useEffect(() => {
    let active = true;
    void Promise.resolve().then(() => {
      if (!active) return;
      const saved = sessionStorage.getItem(storageKey);
      if (saved) {
        try {
          const result = pendingSchema.safeParse(JSON.parse(saved));
          if (result.success) setPending(result.data);
        } catch {
          sessionStorage.removeItem(storageKey);
        }
      }
    });
    return () => {
      active = false;
    };
  }, [storageKey]);
  useEffect(() => {
    let active = true;
    void load()
      .catch((error) => {
        if (active) setFault(failure(error, t));
      })
      .finally(() => {
        if (active) setLoading(false);
      });
    const focus = () => {
      void refresh();
    };
    window.addEventListener("focus", focus);
    return () => {
      active = false;
      window.removeEventListener("focus", focus);
    };
  }, [load, refresh, refreshVersion, t]);
  async function mutate(operation: Pending) {
    if (activeMutation.current) return;
    activeMutation.current = true;
    setBusy(true);
    setFault("");
    setNotice("");
    setPending(operation);
    sessionStorage.setItem(storageKey, JSON.stringify(operation));
    let committed = false;
    try {
      const path = `/${operation.resource}${operation.id ? `/${operation.id}` : ""}${operation.method === "DELETE" ? `?version=${operation.version}` : ""}`;
      if (operation.method === "DELETE")
        await request(path, nothing, operation.method);
      else if (operation.resource === "incomes")
        await request(path, incomeSchema, operation.method, operation.body);
      else await request(path, itemSchema, operation.method, operation.body);
      committed = true;
      sessionStorage.removeItem(storageKey);
      setPending(undefined);
      setEditor(undefined);
      setCategoryEditor(undefined);
      setCategoryName("");
      setIcon("tags");
      setNotice(
        operation.resource === "incomes"
          ? operation.method === "DELETE"
            ? t.incomeDeleted
            : t.incomeSaved
          : t.updated,
      );
      await load();
    } catch (error) {
      const known =
        error instanceof ApiError && error.status >= 400 && error.status < 500;
      if (committed || known) {
        sessionStorage.removeItem(storageKey);
        setPending(undefined);
        setFault(failure(error, t));
      } else setFault(t.incomeUnknown);
    } finally {
      activeMutation.current = false;
      setBusy(false);
    }
  }
  const blocked = busy || !!pending;
  const items = catalog?.categories ?? [];
  return (
    <div className="income-workspace">
      <div className="income-toolbar">
        <button
          className="button secondary"
          onClick={categoriesView ? onIncomes : onCategories}
        >
          {categoriesView ? (
            <ChevronLeft size={20} aria-hidden="true" />
          ) : (
            <Tags size={20} aria-hidden="true" />
          )}
          {categoriesView ? t.incomes : t.incomeCategories}
        </button>
        <button
          className="icon-button"
          aria-label={t.refresh}
          disabled={busy || loading}
          onClick={() => void refresh()}
        >
          <RefreshCw size={20} aria-hidden="true" />
        </button>
      </div>
      {(fault || notice) && (
        <p
          className={fault ? "feedback error" : "feedback success"}
          role={fault ? "alert" : "status"}
        >
          {fault || notice}
        </p>
      )}
      {pending && (
        <div className="panel" role="alert">
          <p>{t.incomeUnknown}</p>
          <button
            className="button secondary"
            disabled={busy}
            onClick={() => void mutate(pending)}
          >
            {t.retry}
          </button>
        </div>
      )}
      {loading && <p role="status">{t.loading}</p>}
      {!catalog || !page ? (
        !loading && (
          <button className="button secondary" onClick={() => void refresh()}>
            {t.retry}
          </button>
        )
      ) : categoriesView ? (
        <div className="catalog-layout">
          <section className="panel">
            {!items.length && <p className="empty">{t.emptyCategories}</p>}
            {items.map((item) => (
              <div className="catalog-item" key={item.id}>
                <div className="catalog-row">
                  <div className="catalog-name">
                    <span className="category-icon">
                      <CategoryIcon icon={item.icon} />
                    </span>
                    <strong>{item.name}</strong>
                  </div>
                  <div className="record-actions">
                    <button
                      className="icon-button"
                      aria-label={`${t.edit}: ${item.name}`}
                      disabled={blocked}
                      onClick={() => {
                        setCategoryEditor(item);
                        setCategoryName(item.name);
                        setIcon(item.icon ?? "tags");
                        requestAnimationFrame(() =>
                          document
                            .getElementById("income-category-name")
                            ?.focus(),
                        );
                      }}
                    >
                      <PencilLine size={20} aria-hidden="true" />
                    </button>
                    <button
                      className="icon-button danger"
                      aria-label={`${t.remove}: ${item.name}`}
                      disabled={blocked}
                      onClick={() => {
                        if (
                          window.confirm(
                            `${t.confirmDeleteCategory}\n${item.name}`,
                          )
                        )
                          void mutate({
                            resource: "income-categories",
                            method: "DELETE",
                            id: item.id,
                            version: item.version,
                          });
                      }}
                    >
                      <Trash2 size={20} aria-hidden="true" />
                    </button>
                  </div>
                </div>
              </div>
            ))}
          </section>
          <form
            className="panel catalog-form"
            onSubmit={(event) => {
              event.preventDefault();
              if (blocked) return;
              void mutate({
                resource: "income-categories",
                method: categoryEditor ? "PUT" : "POST",
                id: categoryEditor?.id,
                body: {
                  requestId: categoryEditor?.id ?? crypto.randomUUID(),
                  name: categoryName,
                  icon,
                  version: categoryEditor?.version ?? 0,
                },
              });
            }}
          >
            <h2>{categoryEditor ? t.edit : t.create}</h2>
            <button
              type="button"
              className="button secondary category-icon-trigger"
              disabled={blocked}
              onClick={() => setPicker(true)}
            >
              <CategoryIcon icon={icon} />
              {t.chooseIcon}
            </button>
            <label>
              {t.name}
              <input
                id="income-category-name"
                required
                maxLength={60}
                value={categoryName}
                disabled={blocked}
                onChange={(event) => setCategoryName(event.target.value)}
              />
            </label>
            <div className="dialog-actions">
              {categoryEditor && (
                <button
                  type="button"
                  className="button secondary"
                  disabled={blocked}
                  onClick={() => {
                    setCategoryEditor(undefined);
                    setCategoryName("");
                    setIcon("tags");
                  }}
                >
                  {t.cancel}
                </button>
              )}
              <button className="button primary" disabled={blocked}>
                {categoryEditor ? t.save : t.create}
              </button>
            </div>
          </form>
        </div>
      ) : (
        <>
          <section className="panel income-summary">
            <span>{t.incomeTotal}</span>
            <strong>{money(page.total, page.currency, lang)}</strong>
          </section>
          <div className="section-heading">
            <h2>{t.incomes}</h2>
            <button
              className="button primary"
              disabled={blocked || !items.length}
              onClick={() => setEditor("new")}
            >
              <Plus size={20} aria-hidden="true" />
              {t.addIncome}
            </button>
          </div>
          {!items.length && (
            <div className="panel">
              <p>{t.incomeCategoryHint}</p>
              <button className="button secondary" onClick={onCategories}>
                {t.incomeCategories}
                <ChevronRight size={20} aria-hidden="true" />
              </button>
            </div>
          )}
          <details className="filter-panel">
            <summary>{t.filters}</summary>
            <div className="filters">
              <label>
                {t.from}
                <input
                  type="date"
                  value={filters.from}
                  onChange={(event) =>
                    setFilters({
                      ...filters,
                      from: event.target.value,
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
                  onChange={(event) =>
                    setFilters({ ...filters, to: event.target.value, page: 0 })
                  }
                />
              </label>
              <label>
                {t.category}
                <select
                  value={filters.categoryId}
                  onChange={(event) =>
                    setFilters({
                      ...filters,
                      categoryId: event.target.value,
                      page: 0,
                    })
                  }
                >
                  <option value="">{t.all}</option>
                  {items.map((item) => (
                    <option key={item.id} value={item.id}>
                      {item.name}
                    </option>
                  ))}
                </select>
              </label>
            </div>
          </details>
          <section className="expense-list">
            {!page.items.length && (
              <div className="empty">
                <h2>{t.emptyIncomes}</h2>
                <p>{t.incomeHint}</p>
              </div>
            )}
            {page.items.map((income) => {
              const category = items.find(
                (item) => item.id === income.categoryId,
              );
              return (
                <div className="income-row" key={income.id}>
                  <div className="expense-description">
                    <span className="category-icon">
                      <CategoryIcon icon={category?.icon} />
                    </span>
                    <div>
                      <strong>{income.description}</strong>
                      <small className="expense-meta">
                        {category?.name ?? "—"} ·{" "}
                        <time dateTime={income.date}>
                          {new Intl.DateTimeFormat(lang, {
                            dateStyle: "medium",
                          }).format(new Date(`${income.date}T12:00:00`))}
                        </time>
                      </small>
                    </div>
                  </div>
                  <strong className="income-amount">
                    +{money(income.amount, income.currency, lang)}
                  </strong>
                  <div className="record-actions">
                    <button
                      className="icon-button"
                      aria-label={`${t.edit}: ${income.description}`}
                      disabled={blocked}
                      onClick={() => setEditor(income)}
                    >
                      <PencilLine size={20} aria-hidden="true" />
                    </button>
                    <button
                      className="icon-button danger"
                      aria-label={`${t.remove}: ${income.description}`}
                      disabled={blocked}
                      onClick={() => {
                        if (
                          window.confirm(
                            `${t.confirmDeleteIncome}\n${income.description}`,
                          )
                        )
                          void mutate({
                            resource: "incomes",
                            id: income.id,
                            method: "DELETE",
                            version: income.version,
                          });
                      }}
                    >
                      <Trash2 size={20} aria-hidden="true" />
                    </button>
                  </div>
                </div>
              );
            })}
          </section>
          <div className="pagination">
            <button
              className="button secondary"
              disabled={loading || !filters.page}
              onClick={() => setFilters({ ...filters, page: filters.page - 1 })}
            >
              {t.previous}
            </button>
            <span>
              {t.page} {filters.page + 1}
            </span>
            <button
              className="button secondary"
              disabled={loading || !page.hasMore}
              onClick={() => setFilters({ ...filters, page: filters.page + 1 })}
            >
              {t.next}
            </button>
          </div>
        </>
      )}
      {picker && (
        <IconPicker
          value={icon}
          title={t.incomeCategories}
          lang={lang}
          t={t}
          onClose={() => setPicker(false)}
          onSelect={(value) => {
            setIcon(value);
            setPicker(false);
          }}
        />
      )}
      {editor && (
        <IncomeEditor
          key={typeof editor === "string" ? editor : editor.id}
          income={editor === "new" ? undefined : editor}
          profile={profile}
          categories={items}
          t={t}
          busy={blocked}
          error={pending ? t.incomeUnknown : fault}
          onClose={() => setEditor(undefined)}
          onRetry={pending ? () => void mutate(pending) : undefined}
          onSave={(body) =>
            void mutate({
              resource: "incomes",
              method: editor === "new" ? "POST" : "PUT",
              id: editor === "new" ? undefined : editor.id,
              body,
            })
          }
        />
      )}
    </div>
  );
}

function IncomeEditor({
  income,
  profile,
  categories,
  t,
  busy,
  error,
  onClose,
  onSave,
  onRetry,
}: {
  income?: Income;
  profile: Profile;
  categories: Item[];
  t: Copy;
  busy: boolean;
  error: string;
  onClose: () => void;
  onSave: (body: NonNullable<Pending["body"]>) => void;
  onRetry?: () => void;
}) {
  const ref = useRef<HTMLDialogElement>(null);
  const [amount, setAmount] = useState(income?.amount ?? "");
  const [description, setDescription] = useState(income?.description ?? "");
  const [categoryId, setCategoryId] = useState(income?.categoryId ?? "");
  const [date, setDate] = useState(income?.date ?? profile.today);
  useEffect(() => {
    const trigger = document.activeElement;
    ref.current?.showModal();
    ref.current?.querySelector("input")?.focus();
    return () => {
      if (trigger instanceof HTMLElement && trigger.isConnected)
        trigger.focus();
    };
  }, []);
  return (
    <dialog
      ref={ref}
      className="editor-dialog"
      aria-labelledby="income-editor-title"
      onCancel={(event) => {
        event.preventDefault();
        if (!busy) onClose();
      }}
    >
      <form
        onSubmit={(event) => {
          event.preventDefault();
          if (!busy)
            onSave({
              requestId: income?.id ?? crypto.randomUUID(),
              amount,
              description,
              categoryId,
              date,
              version: income?.version ?? 0,
            });
        }}
      >
        <div className="dialog-top">
          <h2 id="income-editor-title">
            {income ? t.editIncome : t.addIncome}
          </h2>
          <button
            type="button"
            className="icon-button"
            disabled={busy}
            aria-label={t.close}
            onClick={onClose}
          >
            <X size={20} aria-hidden="true" />
          </button>
        </div>
        <div className="editor-fields">
          <label>
            {t.amount} · {profile.currency}
            <input
              autoFocus
              inputMode="decimal"
              required
              value={amount}
              disabled={busy}
              onChange={(event) =>
                setAmount(event.target.value.replace(",", "."))
              }
            />
          </label>
          <label>
            {t.category}
            <select
              required
              value={categoryId}
              disabled={busy}
              onChange={(event) => setCategoryId(event.target.value)}
            >
              <option value="">{t.choose}</option>
              {categories.map((category) => (
                <option key={category.id} value={category.id}>
                  {category.name}
                </option>
              ))}
            </select>
          </label>
          <label>
            {t.description}
            <input
              required
              maxLength={500}
              value={description}
              disabled={busy}
              onChange={(event) => setDescription(event.target.value)}
            />
          </label>
          <label>
            {t.date}
            <input
              required
              type="date"
              value={date}
              disabled={busy}
              onChange={(event) => setDate(event.target.value)}
            />
          </label>
          {error && (
            <p role="alert" className="error">
              {error}
            </p>
          )}
        </div>
        <div className="dialog-actions">
          <button
            type="button"
            className="button secondary"
            disabled={busy}
            onClick={onClose}
          >
            {t.cancel}
          </button>
          {onRetry ? (
            <button type="button" className="button primary" onClick={onRetry}>
              {t.retry}
            </button>
          ) : (
            <button className="button primary" disabled={busy}>
              {t.save}
            </button>
          )}
        </div>
      </form>
    </dialog>
  );
}
