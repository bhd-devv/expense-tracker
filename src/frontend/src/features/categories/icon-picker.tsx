"use client";

import { useEffect, useRef, useState } from "react";
import { Check, Search, X } from "lucide-react";
import { iconLibrarySchema, request } from "@/lib/api";
import type { components } from "@/lib/api-schema";
import type { Copy, Language } from "@/lib/i18n";
import { CategoryIcon } from "./category-icon";

export default function IconPicker({
  value,
  title,
  lang,
  t,
  onSelect,
  onClose,
}: {
  value: string;
  title: string;
  lang: Language;
  t: Copy;
  onSelect: (icon: string) => void;
  onClose: () => void;
}) {
  const ref = useRef<HTMLDialogElement>(null);
  const [library, setLibrary] =
    useState<components["schemas"]["CategoryIconLibraryDto"]>();
  const [failed, setFailed] = useState(false);
  const [attempt, setAttempt] = useState(0);
  const [search, setSearch] = useState("");
  const [group, setGroup] = useState("");
  useEffect(() => {
    const dialog = ref.current;
    const trigger = document.activeElement;
    dialog?.showModal();
    return () => {
      dialog?.close();
      if (trigger instanceof HTMLElement && trigger.isConnected)
        trigger.focus();
    };
  }, []);
  useEffect(() => {
    let active = true;
    request("/category-icons", iconLibrarySchema)
      .then((result) => {
        if (active) setLibrary(result);
      })
      .catch(() => {
        if (active) setFailed(true);
      });
    return () => {
      active = false;
    };
  }, [attempt]);
  const query = search.trim().toLocaleLowerCase(lang);
  const groups = library?.groups
    .filter((item) => !group || item.id === group)
    .map((item) => ({
      ...item,
      icons: item.icons.filter((icon) =>
        `${icon.uk} ${icon.en} ${icon.id} ${item.uk} ${item.en}`
          .toLocaleLowerCase(lang)
          .includes(query),
      ),
    }))
    .filter((item) => item.icons.length);
  return (
    <dialog
      ref={ref}
      className="icon-picker"
      aria-labelledby="icon-picker-title"
      onCancel={(event) => {
        event.preventDefault();
        onClose();
      }}
    >
      <div className="icon-picker-heading">
        <h2 id="icon-picker-title">
          {t.chooseIcon}: {title}
        </h2>
        <button
          type="button"
          className="icon-button"
          aria-label={t.close}
          onClick={onClose}
        >
          <X size={22} aria-hidden="true" />
        </button>
      </div>
      <label className="icon-search">
        <Search size={20} aria-hidden="true" />
        <input
          autoFocus
          type="search"
          aria-label={t.searchIcons}
          placeholder={t.searchIcons}
          value={search}
          onChange={(event) => setSearch(event.target.value)}
        />
      </label>
      {library && (
        <label className="icon-group-filter">
          {t.iconGroup}
          <select
            value={group}
            onChange={(event) => setGroup(event.target.value)}
          >
            <option value="">{t.allIcons}</option>
            {library.groups.map((item) => (
              <option key={item.id} value={item.id}>
                {item[lang]}
              </option>
            ))}
          </select>
        </label>
      )}
      {failed ? (
        <div className="empty" role="alert">
          <p>{t.connection}</p>
          <button
            type="button"
            className="button secondary"
            onClick={() => {
              setFailed(false);
              setAttempt(attempt + 1);
            }}
          >
            {t.retry}
          </button>
        </div>
      ) : !library ? (
        <p role="status">{t.loading}</p>
      ) : (
        <>
          <p className="sr-only" role="status">
            {groups?.reduce((count, item) => count + item.icons.length, 0)} ·{" "}
            {t.icon}
          </p>
          <div className="icon-picker-results">
            {groups?.length ? (
              groups.map((item) => (
                <section key={item.id} aria-label={item[lang]}>
                  <h3>{item[lang]}</h3>
                  <div className="icon-grid">
                    {item.icons.map((icon) => (
                      <button
                        type="button"
                        key={icon.id}
                        className={`icon-option ${value === icon.id ? "selected" : ""}`}
                        aria-pressed={value === icon.id}
                        title={icon[lang]}
                        onClick={() => onSelect(icon.id)}
                      >
                        <CategoryIcon icon={icon.id} />
                        <span>{icon[lang]}</span>
                        {value === icon.id && (
                          <Check
                            className="icon-option-check"
                            size={14}
                            aria-hidden="true"
                          />
                        )}
                      </button>
                    ))}
                  </div>
                </section>
              ))
            ) : (
              <p className="empty">{t.noIcons}</p>
            )}
          </div>
        </>
      )}
    </dialog>
  );
}
