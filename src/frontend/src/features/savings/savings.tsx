"use client";

import { useCallback, useEffect, useState } from "react";
import {
  ArrowDownLeft,
  ArrowUpRight,
  ChevronLeft,
  ChevronRight,
  PiggyBank,
  Plus,
  RefreshCw,
} from "lucide-react";
import { z } from "zod";
import {
  ApiError,
  request,
  savingsAccountSchema,
  savingsAccountsSchema,
  savingsEntrySchema,
  savingsPageSchema,
  type Profile,
} from "@/lib/api";
import { money, type Copy, type Language } from "@/lib/i18n";

const pendingSchema = z.discriminatedUnion("kind", [
  z.object({
    kind: z.literal("account"),
    body: z.object({
      requestId: z.string().uuid(),
      name: z.string(),
      currency: z.string(),
    }),
  }),
  z.object({
    kind: z.literal("entry"),
    accountId: z.string().uuid(),
    body: z.object({
      requestId: z.string().uuid(),
      kind: z.enum(["deposit", "withdrawal"]),
      amount: z.string(),
      date: z.string(),
      description: z.string(),
    }),
  }),
]);
type Pending = z.infer<typeof pendingSchema>;

export default function Savings({
  profile,
  lang,
  t,
  onHome,
}: {
  profile: Profile;
  lang: Language;
  t: Copy;
  onHome: () => void;
}) {
  const storageKey = `lime-savings-${profile.id}`;
  const [accounts, setAccounts] =
    useState<z.infer<typeof savingsAccountsSchema>>();
  const [selected, setSelected] = useState("");
  const [page, setPage] = useState(0);
  const [history, setHistory] = useState<z.infer<typeof savingsPageSchema>>();
  const [loading, setLoading] = useState(true);
  const [revision, setRevision] = useState(0);
  const [busy, setBusy] = useState(false);
  const [fault, setFault] = useState("");
  const [notice, setNotice] = useState("");
  const [creating, setCreating] = useState(false);
  const [name, setName] = useState("");
  const [kind, setKind] = useState<"deposit" | "withdrawal">("deposit");
  const [amount, setAmount] = useState("");
  const [date, setDate] = useState(profile.today);
  const [description, setDescription] = useState("");
  const [pending, setPending] = useState<Pending | undefined>(() => {
    // This component mounts only after browser authentication.
    try {
      const stored = sessionStorage.getItem(storageKey);
      const parsed = pendingSchema.safeParse(
        stored ? JSON.parse(stored) : null,
      );
      return parsed.success ? parsed.data : undefined;
    } catch {
      return undefined;
    }
  });
  function errorText(error: unknown) {
    if (!(error instanceof ApiError)) return t.connection;
    if (error.code === "insufficient_savings") return t.insufficientSavings;
    if (error.code === "savings_limit") return t.savingsLimit;
    if (error.code === "unknown") return t.savingsUnknown;
    if (error.status === 429) return t.rateLimit;
    if (error.status === 409) return t.conflict;
    return error.status >= 500 || error.status === 0 ? t.connection : t.invalid;
  }
  const load = useCallback(async () => {
    const list = await request("/savings", savingsAccountsSchema);
    const entries = selected
      ? await request(
          `/savings/${selected}/entries?page=${page}`,
          savingsPageSchema,
        )
      : undefined;
    return { list, entries };
  }, [selected, page]);
  useEffect(() => {
    let active = true;
    const refresh = () => {
      setLoading(true);
      void load()
        .then(({ list, entries }) => {
          if (!active) return;
          setAccounts(list);
          setHistory(entries);
        })
        .catch((error: unknown) => {
          if (active)
            setFault(
              error instanceof ApiError && error.status === 429
                ? t.rateLimit
                : t.connection,
            );
        })
        .finally(() => {
          if (active) setLoading(false);
        });
    };
    refresh();
    window.addEventListener("focus", refresh);
    return () => {
      active = false;
      window.removeEventListener("focus", refresh);
    };
  }, [load, t, revision]);
  async function refresh() {
    setLoading(true);
    setFault("");
    try {
      const { list, entries } = await load();
      setAccounts(list);
      setHistory(entries);
    } catch (error) {
      setFault(errorText(error));
    } finally {
      setLoading(false);
    }
  }
  async function save(operation: Pending) {
    if (busy) return;
    setBusy(true);
    setFault("");
    setNotice("");
    try {
      // Persist the exact request before sending it, including across page reloads.
      sessionStorage.setItem(storageKey, JSON.stringify(operation));
      setPending(operation);
      if (operation.kind === "account") {
        const account = await request(
          "/savings",
          savingsAccountSchema,
          "POST",
          operation.body,
        );
        setSelected(account.id);
        setHistory(undefined);
        setPage(0);
        setCreating(false);
        setName("");
      } else {
        await request(
          `/savings/${operation.accountId}/entries`,
          savingsEntrySchema,
          "POST",
          operation.body,
        );
        setSelected(operation.accountId);
        setPage(0);
        setAmount("");
        setDescription("");
      }
      sessionStorage.removeItem(storageKey);
      setPending(undefined);
      setNotice(operation.kind === "account" ? t.updated : t.savingsSaved);
      // A refresh failure must never resubmit the operation.
      setHistory(undefined);
      setRevision((value) => value + 1);
    } catch (error) {
      const knownRejection =
        error instanceof ApiError && error.status >= 400 && error.status < 500;
      if (knownRejection) {
        sessionStorage.removeItem(storageKey);
        setPending(undefined);
      }
      setFault(knownRejection ? errorText(error) : t.savingsUnknown);
    } finally {
      setBusy(false);
    }
  }
  const account =
    history?.account.id === selected
      ? history.account
      : accounts?.items.find((item) => item.id === selected);
  const locked = busy || !!pending;
  return (
    <section className="savings-page" aria-label={t.savings}>
      <div className="section-heading">
        <button
          className="quiet"
          disabled={busy}
          onClick={() => {
            if (selected) {
              setSelected("");
              setPage(0);
              setHistory(undefined);
              setNotice("");
            } else onHome();
          }}
        >
          <ChevronLeft size={20} aria-hidden="true" />
          {selected ? t.savingsBack : t.savingsHome}
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
      {fault && (
        <p className="feedback error" role="alert">
          {fault}
        </p>
      )}
      {notice && (
        <p className="feedback success" role="status">
          {notice}
        </p>
      )}
      {pending && (
        <div className="savings-pending">
          <p>{t.savingsPending}</p>
          <button
            className="button secondary"
            disabled={busy}
            onClick={() => void save(pending)}
          >
            {t.retry}
          </button>
        </div>
      )}
      {loading && <p role="status">{t.loading}</p>}
      {!loading && fault && (
        <button
          className="button secondary"
          disabled={busy}
          onClick={() => void refresh()}
        >
          {t.refresh}
        </button>
      )}
      {!selected ? (
        <>
          <div className="savings-list">
            {accounts?.items.map((item) => (
              <button
                className="savings-account"
                key={item.id}
                disabled={busy}
                onClick={() => {
                  setSelected(item.id);
                  setPage(0);
                  setHistory(undefined);
                  setFault("");
                  setNotice("");
                }}
              >
                <PiggyBank size={24} aria-hidden="true" />
                <span>
                  <strong>{item.name}</strong>
                  <small>{t.savingsBalance}</small>
                </span>
                <strong className="money">
                  {money(item.balance, item.currency, lang)}
                </strong>
                <ChevronRight size={20} aria-hidden="true" />
              </button>
            ))}
            {accounts && !accounts.items.length && (
              <p className="empty">{t.emptySavings}</p>
            )}
          </div>
          {creating ? (
            <form
              className="panel savings-form"
              onSubmit={(event) => {
                event.preventDefault();
                void save({
                  kind: "account",
                  body: {
                    requestId: crypto.randomUUID(),
                    name,
                    currency: profile.currency,
                  },
                });
              }}
            >
              <h2>{t.newSavings}</h2>
              <fieldset disabled={locked}>
                <label>
                  {t.name}
                  <input
                    autoFocus
                    maxLength={60}
                    required
                    value={name}
                    onChange={(event) => setName(event.target.value)}
                  />
                </label>
              </fieldset>
              <div className="savings-actions">
                <button className="button primary" disabled={locked}>
                  {t.create}
                </button>
                <button
                  type="button"
                  className="button secondary"
                  disabled={locked}
                  onClick={() => setCreating(false)}
                >
                  {t.cancel}
                </button>
              </div>
            </form>
          ) : (
            <button
              className="button primary"
              disabled={locked}
              onClick={() => setCreating(true)}
            >
              <Plus size={20} aria-hidden="true" />
              {t.newSavings}
            </button>
          )}
        </>
      ) : (
        account && (
          <>
            <div className="panel savings-summary">
              <h2>{account.name}</h2>
              <p>{t.savingsBalance}</p>
              <strong className="report-total">
                {money(account.balance, account.currency, lang)}
              </strong>
              <dl>
                <div>
                  <dt>{t.savingsAdded}</dt>
                  <dd>{money(account.added, account.currency, lang)}</dd>
                </div>
                <div>
                  <dt>{t.savingsSpent}</dt>
                  <dd>{money(account.spent, account.currency, lang)}</dd>
                </div>
              </dl>
            </div>
            <form
              className="panel savings-form"
              onSubmit={(event) => {
                event.preventDefault();
                void save({
                  kind: "entry",
                  accountId: selected,
                  body: {
                    requestId: crypto.randomUUID(),
                    kind,
                    amount: amount.replace(",", "."),
                    date,
                    description,
                  },
                });
              }}
            >
              <fieldset disabled={locked}>
                <legend className="sr-only">{t.savingsHistory}</legend>
                <div className="savings-kind">
                  <label>
                    <input
                      type="radio"
                      name="savings-kind"
                      checked={kind === "deposit"}
                      onChange={() => setKind("deposit")}
                    />
                    {t.deposit}
                  </label>
                  <label>
                    <input
                      type="radio"
                      name="savings-kind"
                      checked={kind === "withdrawal"}
                      onChange={() => setKind("withdrawal")}
                    />
                    {t.withdrawal}
                  </label>
                </div>
                <label>
                  {t.amount} · {account.currency}
                  <input
                    inputMode="decimal"
                    required
                    maxLength={22}
                    value={amount}
                    onChange={(event) => setAmount(event.target.value)}
                  />
                </label>
                <label>
                  {t.date}
                  <input
                    type="date"
                    required
                    value={date}
                    onChange={(event) => setDate(event.target.value)}
                  />
                </label>
                <label>
                  {t.savingsNote}
                  <input
                    maxLength={500}
                    value={description}
                    onChange={(event) => setDescription(event.target.value)}
                  />
                </label>
              </fieldset>
              <button className="button primary" disabled={locked}>
                {busy ? t.loading : t.save}
              </button>
            </form>
            <section className="savings-history">
              <h2>{t.savingsHistory}</h2>
              {history?.account.id === selected && history.page === page && (
                <>
                  {history.items.length ? (
                    history.items.map((entry) => (
                      <div className="savings-entry" key={entry.id}>
                        {entry.kind === "deposit" ? (
                          <ArrowDownLeft size={22} aria-hidden="true" />
                        ) : (
                          <ArrowUpRight size={22} aria-hidden="true" />
                        )}
                        <div>
                          <strong>
                            {entry.kind === "deposit"
                              ? t.depositRecord
                              : t.withdrawalRecord}
                          </strong>
                          {entry.description && <p>{entry.description}</p>}
                          <time dateTime={entry.date}>
                            {new Intl.DateTimeFormat(lang).format(
                              new Date(entry.date + "T12:00:00"),
                            )}
                          </time>
                        </div>
                        <strong className="money">
                          {entry.kind === "deposit" ? "+" : "−"}
                          {money(entry.amount, account.currency, lang)}
                        </strong>
                      </div>
                    ))
                  ) : (
                    <p className="empty">{t.emptySavingsHistory}</p>
                  )}
                  <div className="pagination">
                    <button
                      className="button secondary"
                      disabled={busy || loading || !page}
                      onClick={() => setPage(page - 1)}
                    >
                      {t.previous}
                    </button>
                    <span>
                      {t.page} {page + 1}
                    </span>
                    <button
                      className="button secondary"
                      disabled={busy || loading || !history.hasMore}
                      onClick={() => setPage(page + 1)}
                    >
                      {t.next}
                    </button>
                  </div>
                </>
              )}
            </section>
          </>
        )
      )}
    </section>
  );
}
