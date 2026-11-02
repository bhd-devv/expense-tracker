"use client";

import { useEffect, useRef, useState } from "react";
import { itemSchema, request, type Item } from "@/lib/api";
import { categoryName, money, type Copy, type Language } from "@/lib/i18n";

export default function CategoryLimit({
  item,
  defaultCurrency,
  lang,
  t,
  busy,
  perform,
  onSaved,
}: {
  item: Item;
  defaultCurrency: string;
  lang: Language;
  t: Copy;
  busy: boolean;
  perform: (action: () => Promise<void>) => Promise<void>;
  onSaved: (updated: Item) => Promise<void>;
}) {
  const [open, setOpen] = useState(false);
  const [amount, setAmount] = useState("");
  const [version, setVersion] = useState(item.version);
  const input = useRef<HTMLInputElement>(null);
  const trigger = useRef<HTMLButtonElement>(null);
  const restoreFocus = useRef(false);
  useEffect(() => {
    if (open) {
      input.current?.focus();
      input.current?.select();
      restoreFocus.current = true;
    } else if (!busy && restoreFocus.current) {
      trigger.current?.focus();
      restoreFocus.current = false;
    }
  }, [open, busy]);
  const limit = item.limit;
  const percent = limit
    ? new Intl.NumberFormat(lang, { maximumFractionDigits: 1 }).format(
        limit.usedPercent,
      )
    : "";
  async function save(nextAmount: string | null, nextCurrency: string | null) {
    await perform(async () => {
      const updated = await request(
        `/categories/${item.id}/limit`,
        itemSchema,
        "PUT",
        {
          amount: nextAmount,
          currency: nextCurrency,
          version,
        },
      );
      setOpen(false);
      await onSaved(updated);
    });
  }
  return (
    <div className="category-limit">
      <div className="category-limit-heading">
        <span>
          {t.monthlyLimit}
          {limit && (
            <>
              {" "}
              ·{" "}
              {new Intl.DateTimeFormat(lang, {
                month: "short",
                year: "numeric",
              }).format(new Date(limit.from + "T12:00:00"))}
            </>
          )}
        </span>
        <button
          ref={trigger}
          type="button"
          className="quiet"
          disabled={busy || open}
          aria-label={`${limit ? t.editLimit : t.setLimit}: ${categoryName(item, t)}`}
          onClick={() => {
            setAmount(limit?.amount ?? "");
            setVersion(item.version);
            setOpen(true);
          }}
        >
          {limit ? t.editLimit : t.setLimit}
        </button>
      </div>
      {limit && (
        <div
          className={`category-limit-progress ${limit.exceeded ? "over-limit" : ""}`}
        >
          <div className="category-limit-values">
            <span>
              {money(limit.spent, limit.currency, lang)} /{" "}
              {money(limit.amount, limit.currency, lang)}
            </span>
            <strong>{percent}%</strong>
          </div>
          <progress
            max={100}
            value={Math.min(100, limit.usedPercent)}
            aria-label={`${t.monthlyLimit}: ${categoryName(item, t)}`}
            aria-valuetext={`${t.limitUsed} ${percent}%. ${money(limit.spent, limit.currency, lang)} / ${money(limit.amount, limit.currency, lang)}`}
          />
          {limit.exceeded && <p className="danger">{t.limitExceeded}</p>}
        </div>
      )}
      {open && (
        <form
          className="category-limit-form"
          onSubmit={(event) => {
            event.preventDefault();
            void save(amount.replace(",", "."), defaultCurrency);
          }}
          onKeyDown={(event) => {
            if (event.key === "Escape" && !busy) {
              event.preventDefault();
              setOpen(false);
            }
          }}
        >
          <div className="category-limit-fields">
            <label>
              {t.amount} · {defaultCurrency}
              <input
                ref={input}
                inputMode="decimal"
                required
                maxLength={24}
                value={amount}
                disabled={busy}
                onChange={(event) => setAmount(event.target.value)}
              />
            </label>
          </div>
          <div className="dialog-actions">
            <button className="button primary" disabled={busy}>
              {t.save}
            </button>
            <button
              type="button"
              className="button secondary"
              disabled={busy}
              onClick={() => setOpen(false)}
            >
              {t.cancel}
            </button>
            {limit && (
              <button
                type="button"
                className="quiet danger"
                disabled={busy}
                onClick={() => void save(null, null)}
              >
                {t.removeLimit}
              </button>
            )}
          </div>
        </form>
      )}
    </div>
  );
}
