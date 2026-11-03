"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import {
  Check,
  CircleCheck,
  Leaf,
  PencilLine,
  Plus,
  RefreshCw,
  Trash2,
  X,
} from "lucide-react";
import {
  ApiError,
  authenticate,
  nothing,
  request,
  type Profile,
} from "@/lib/api";
import { language } from "@/lib/i18n";
import { applyTheme } from "@/lib/theme";
import { telegram } from "@/lib/telegram";
import {
  taskListSchema,
  pendingTaskSchema,
  type PendingTask,
  taskSchema,
  type CreateTask,
  type RecurringTask,
  type RenameTask,
  type TaskCompletion,
} from "./api";
import { frequency, taskCopy } from "./copy";

type Editor = { kind: "create" } | { kind: "rename"; task: RecurringTask };

export default function Tasks() {
  const [profile, setProfile] = useState<Profile>();
  const [failed, setFailed] = useState<"signIn" | "connection">();
  const [attempt, setAttempt] = useState(0);
  useEffect(() => {
    let active = true;
    void authenticate()
      .then((value) => {
        if (active) setProfile(value);
      })
      .catch((error: unknown) => {
        if (active)
          setFailed(
            error instanceof ApiError &&
              (error.status === 401 || error.status === 403)
              ? "signIn"
              : "connection",
          );
      });
    return () => {
      active = false;
    };
  }, [attempt]);
  const lang = language(profile?.language ?? "uk");
  const t = taskCopy[lang];
  useEffect(() => {
    if (!profile) return;
    document.documentElement.lang = lang;
    const update = () => applyTheme(profile.theme);
    update();
    const media = matchMedia("(prefers-color-scheme: dark)");
    media.addEventListener("change", update);
    telegram()?.onEvent("themeChanged", update);
    return () => {
      media.removeEventListener("change", update);
      telegram()?.offEvent("themeChanged", update);
    };
  }, [profile, lang]);
  return (
    <main className="tasks-shell">
      <header className="tasks-header">
        <span className="tasks-brand">
          <Leaf size={24} aria-hidden="true" /> Lime
        </span>
        <h1>{t.title}</h1>
      </header>
      {profile ? (
        <TaskBoard key={profile.id} profile={profile} />
      ) : failed ? (
        <div className="panel tasks-auth">
          <p role="alert">{t[failed]}</p>
          <button
            className="quiet"
            onClick={() => {
              setFailed(undefined);
              setAttempt((value) => value + 1);
            }}
          >
            <RefreshCw size={20} aria-hidden="true" />
            {t.refresh}
          </button>
        </div>
      ) : (
        <p className="tasks-loading" role="status">
          {t.loading}
        </p>
      )}
    </main>
  );
}

function TaskBoard({ profile }: { profile: Profile }) {
  const lang = language(profile.language);
  const t = taskCopy[lang];
  const [data, setData] = useState<Awaited<ReturnType<typeof readTasks>>>();
  const [date, setDate] = useState("");
  const [view, setView] = useState<"day" | "all">("day");
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [fault, setFault] = useState("");
  const [notice, setNotice] = useState("");
  const [editor, setEditor] = useState<Editor>();
  const [name, setName] = useState("");
  const [intervalDays, setIntervalDays] = useState(1);
  const [startDate, setStartDate] = useState(profile.today);
  const nameInput = useRef<HTMLInputElement>(null);
  const addButton = useRef<HTMLButtonElement>(null);
  const editorTrigger = useRef<HTMLButtonElement | null>(null);
  const restoreFocus = useRef(false);
  const busyRef = useRef(false);
  const loadSequence = useRef(0);
  const storageKey = `lime-tasks-pending-${profile.id}`;
  const [pending, setPending] = useState<PendingTask | undefined>(() => {
    try {
      const stored = sessionStorage.getItem(storageKey);
      const parsed = pendingTaskSchema.safeParse(
        stored ? JSON.parse(stored) : null,
      );
      return parsed.success ? parsed.data : undefined;
    } catch {
      return undefined;
    }
  });

  function errorText(error: unknown) {
    if (!(error instanceof ApiError)) return t.connection;
    if (error.status === 401) return t.signIn;
    if (error.status === 429) return t.rateLimit;
    if (error.status === 409) return t.conflict;
    if (error.code === "task_limit") return t.limit;
    if (error.code === "task_not_due") return t.notDue;
    return error.status >= 500 || error.status === 0 ? t.connection : t.invalid;
  }

  const load = useCallback(async () => {
    if (busyRef.current) return;
    const sequence = ++loadSequence.current;
    setLoading(true);
    try {
      const next = await readTasks(date);
      if (sequence !== loadSequence.current) return;
      setData(next);
      setFault("");
    } catch (error) {
      if (sequence === loadSequence.current)
        setFault(
          error instanceof ApiError && error.status === 401
            ? t.signIn
            : error instanceof ApiError && error.status === 429
              ? t.rateLimit
              : t.connection,
        );
    } finally {
      if (sequence === loadSequence.current) setLoading(false);
    }
  }, [date, t]);

  useEffect(() => {
    void load();
    const refresh = () => {
      void load();
    };
    const visible = () => {
      if (document.visibilityState === "visible") refresh();
    };
    window.addEventListener("focus", refresh);
    document.addEventListener("visibilitychange", visible);
    // Refresh local "today" even when the app stays open across midnight.
    const timer = window.setInterval(refresh, 60_000);
    return () => {
      ++loadSequence.current;
      window.clearInterval(timer);
      window.removeEventListener("focus", refresh);
      document.removeEventListener("visibilitychange", visible);
    };
  }, [load]);

  useEffect(() => {
    if (editor) nameInput.current?.focus();
  }, [editor]);

  useEffect(() => {
    if (restoreFocus.current && !editor && !loading && !busy && !pending) {
      restoreFocus.current = false;
      (editorTrigger.current ?? addButton.current)?.focus();
    }
  }, [editor, loading, busy, pending]);

  function closeEditor() {
    restoreFocus.current = true;
    setEditor(undefined);
  }

  async function mutate(operation: PendingTask, retry = false) {
    if (busyRef.current || loading || (pending && !retry)) return;
    // If storage is unavailable, do not send a request that could be lost on reload.
    try {
      sessionStorage.setItem(storageKey, JSON.stringify(operation));
    } catch {
      setFault(t.storage);
      return;
    }
    busyRef.current = true;
    ++loadSequence.current;
    setBusy(true);
    setPending(operation);
    setFault("");
    setNotice("");
    try {
      await submitTask(operation);
      sessionStorage.removeItem(storageKey);
      setPending(undefined);
      setNotice(t.updated);
      if (operation.kind === "create" || operation.kind === "rename")
        closeEditor();
    } catch (error) {
      const rejected =
        error instanceof ApiError && error.status >= 400 && error.status < 500;
      if (rejected) {
        sessionStorage.removeItem(storageKey);
        setPending(undefined);
      }
      setFault(rejected ? errorText(error) : "");
      return;
    } finally {
      busyRef.current = false;
      setBusy(false);
    }
    await load();
  }

  const today = data?.today ?? profile.today;
  const selectedDate = date || today;
  const due = data?.items.filter((task) => task.isDue) ?? [];
  const completed = due.filter((task) => task.completed).length;
  const items = view === "day" ? due : (data?.items ?? []);
  const locked = busy || !!pending || loading;
  const current = data?.date === selectedDate;
  return (
    <div className="tasks-board">
      <div className="tasks-toolbar">
        <div className="tasks-views" role="group" aria-label={t.title}>
          <button
            className="quiet"
            aria-pressed={view === "day"}
            onClick={() => setView("day")}
          >
            {t.scheduled}
          </button>
          <button
            className="quiet"
            aria-pressed={view === "all"}
            onClick={() => setView("all")}
          >
            {t.all}
          </button>
        </div>
        <button
          className="icon-button"
          aria-label={t.refresh}
          disabled={busy || loading}
          onClick={() => void load()}
        >
          <RefreshCw size={20} aria-hidden="true" />
        </button>
      </div>

      <div className="tasks-date-row">
        <label>
          {t.date}
          <input
            type="date"
            value={selectedDate}
            max={today}
            required
            disabled={busy || loading}
            onChange={(event) => {
              if (event.target.value) {
                setDate(event.target.value);
                setNotice("");
              }
            }}
          />
        </label>
        <button
          className="quiet"
          disabled={busy || loading}
          onClick={() => setDate("")}
        >
          {t.today}
        </button>
      </div>
      {fault && (
        <p className="feedback error" role="alert">
          {fault}
        </p>
      )}
      {pending && !busy && (
        <p className="feedback error" role="alert">
          {t.unknown}
          <button
            className="quiet"
            disabled={busy || loading}
            onClick={() => void mutate(pending, true)}
          >
            {t.retry}
          </button>
        </p>
      )}
      {notice && (
        <p className="feedback success" role="status">
          {notice}
        </p>
      )}
      {loading && (
        <p className="tasks-loading" role="status">
          {t.loading}
        </p>
      )}

      {!editor && (
        <button
          ref={addButton}
          className="button primary tasks-add"
          disabled={locked}
          onClick={() => {
            editorTrigger.current = addButton.current;
            setName("");
            setIntervalDays(1);
            setStartDate(today);
            setEditor({ kind: "create" });
          }}
        >
          <Plus size={20} aria-hidden="true" />
          {t.add}
        </button>
      )}

      {editor && (
        <form
          className="panel tasks-form"
          aria-label={editor.kind === "create" ? t.add : t.edit}
          onKeyDown={(event) => {
            if (event.key === "Escape" && !busy) closeEditor();
          }}
          onSubmit={(event) => {
            event.preventDefault();
            if (editor.kind === "create") {
              const input: CreateTask = {
                requestId: crypto.randomUUID(),
                name,
                intervalDays,
                startDate,
              };
              void mutate({ kind: "create", input });
            } else {
              const input: RenameTask = { name, version: editor.task.version };
              void mutate({ kind: "rename", id: editor.task.id, input });
            }
          }}
        >
          <div className="section-heading">
            <h2>{editor.kind === "create" ? t.add : t.edit}</h2>
            <button
              className="icon-button"
              type="button"
              aria-label={t.cancel}
              disabled={busy}
              onClick={closeEditor}
            >
              <X size={20} aria-hidden="true" />
            </button>
          </div>
          <label>
            {t.name}
            <input
              ref={nameInput}
              value={name}
              onChange={(event) => setName(event.target.value)}
              placeholder={t.namePlaceholder}
              required
              maxLength={120}
              disabled={locked}
            />
          </label>
          {editor.kind === "create" ? (
            <>
              <label>
                {t.repeat}
                <select
                  value={intervalDays}
                  disabled={locked}
                  onChange={(event) =>
                    setIntervalDays(Number(event.target.value))
                  }
                >
                  <option value={1}>{t.daily}</option>
                  <option value={2}>{t.twoDays}</option>
                  <option value={3}>{t.threeDays}</option>
                  <option value={7}>{t.weekly}</option>
                </select>
              </label>
              <label>
                {t.start}
                <input
                  type="date"
                  value={startDate}
                  required
                  disabled={locked}
                  onChange={(event) => setStartDate(event.target.value)}
                />
              </label>
              <p>{t.scheduleHint}</p>
            </>
          ) : (
            <p>{t.renameHint}</p>
          )}
          <div className="tasks-form-actions">
            <button type="submit" className="button primary" disabled={locked}>
              {t.save}
            </button>
            <button
              type="button"
              className="quiet"
              disabled={busy}
              onClick={closeEditor}
            >
              {t.cancel}
            </button>
          </div>
        </form>
      )}

      {data && current && (
        <section
          className="panel tasks-list"
          aria-label={view === "day" ? t.scheduled : t.all}
          aria-busy={loading}
        >
          {view === "day" && due.length > 0 && (
            <div className="tasks-progress">
              <span>
                {completed} / {due.length} {t.progress}
              </span>
              <progress
                value={completed}
                max={due.length}
                aria-label={t.progress}
              />
            </div>
          )}
          {items.length ? (
            <ul>
              {items.map((task) => (
                <li key={task.id}>
                  {view === "day" ? (
                    <button
                      className="tasks-check"
                      role="checkbox"
                      aria-checked={task.completed}
                      aria-label={`${task.completed ? t.unmark : t.mark}: ${task.name}`}
                      disabled={locked || !!editor || !task.canComplete}
                      onClick={() => {
                        const input: TaskCompletion = {
                          date: data.date,
                          completed: !task.completed,
                          version: task.version,
                        };
                        void mutate({ kind: "complete", id: task.id, input });
                      }}
                    >
                      {task.completed && <Check size={22} aria-hidden="true" />}
                    </button>
                  ) : (
                    <CircleCheck size={22} aria-hidden="true" />
                  )}
                  <div className="tasks-task-copy">
                    <span
                      className={
                        view === "day" && task.completed
                          ? "tasks-done"
                          : undefined
                      }
                    >
                      {task.name}
                    </span>
                    <p>
                      {frequency(task.intervalDays, t)} ·{" "}
                      <time dateTime={task.startDate}>
                        {new Intl.DateTimeFormat(
                          lang === "uk" ? "uk-UA" : "en-GB",
                          { dateStyle: "medium", timeZone: "UTC" },
                        ).format(new Date(`${task.startDate}T12:00:00Z`))}
                      </time>
                    </p>
                    {view === "day" && (
                      <small>{task.completed ? t.done : t.notDone}</small>
                    )}
                  </div>
                  <div className="tasks-row-actions">
                    <button
                      className="icon-button"
                      aria-label={`${t.edit}: ${task.name}`}
                      disabled={locked || !!editor}
                      onClick={(event) => {
                        editorTrigger.current = event.currentTarget;
                        setName(task.name);
                        setEditor({ kind: "rename", task });
                      }}
                    >
                      <PencilLine size={20} aria-hidden="true" />
                    </button>
                    <button
                      className="icon-button danger"
                      aria-label={`${t.delete}: ${task.name}`}
                      disabled={locked || !!editor}
                      onClick={() => {
                        if (window.confirm(t.deleteConfirm))
                          void mutate({
                            kind: "delete",
                            id: task.id,
                            version: task.version,
                          });
                      }}
                    >
                      <Trash2 size={20} aria-hidden="true" />
                    </button>
                  </div>
                </li>
              ))}
            </ul>
          ) : (
            <div className="empty">
              <CircleCheck
                className="empty-icon"
                size={32}
                aria-hidden="true"
              />
              <h2>{data.items.length ? t.restTitle : t.emptyTitle}</h2>
              <p>{data.items.length ? t.restBody : t.emptyBody}</p>
            </div>
          )}
        </section>
      )}
    </div>
  );
}

function readTasks(date: string) {
  return request(
    `/tasks${date ? `?date=${encodeURIComponent(date)}` : ""}`,
    taskListSchema,
  );
}

async function submitTask(operation: PendingTask) {
  switch (operation.kind) {
    case "create":
      return request("/tasks", taskSchema, "POST", operation.input);
    case "rename":
      return request(`/tasks/${operation.id}`, nothing, "PUT", operation.input);
    case "complete":
      return request(
        `/tasks/${operation.id}/completion`,
        nothing,
        "PUT",
        operation.input,
      );
    case "delete":
      return request(
        `/tasks/${operation.id}?version=${operation.version}`,
        nothing,
        "DELETE",
      );
  }
}
