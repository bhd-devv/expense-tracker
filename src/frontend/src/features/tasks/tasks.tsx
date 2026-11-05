"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import {
  Check,
  CalendarDays,
  ChevronLeft,
  ChevronRight,
  CircleCheck,
  Leaf,
  MoreHorizontal,
  Repeat2,
  Plus,
  RefreshCw,
  Trash2,
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
import { shiftDate, weekDates } from "./calendar";
import { TaskDialog } from "./task-dialog";

type Editor =
  | { kind: "create" }
  | { kind: "manage"; task: RecurringTask }
  | { kind: "delete"; task: RecurringTask }
  | { kind: "date" };

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
          <span className="tasks-brand-mark">
            <Leaf size={20} aria-hidden="true" />
          </span>{" "}
          Lime
        </span>
        <span className="tasks-app-name">{t.title}</span>
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
  const [calendarDate, setCalendarDate] = useState(profile.today);
  const nameInput = useRef<HTMLInputElement>(null);
  const dateInput = useRef<HTMLInputElement>(null);
  const deleteCancel = useRef<HTMLButtonElement>(null);
  const addButton = useRef<HTMLButtonElement>(null);
  const refreshButton = useRef<HTMLButtonElement>(null);
  const editorTrigger = useRef<HTMLButtonElement | null>(null);
  const restoreFocus = useRef(false);
  const busyRef = useRef(false);
  const loadSequence = useRef({ value: 0 });
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

  const load = useCallback(() => {
    if (busyRef.current) return Promise.resolve();
    const sequence = ++loadSequence.current.value;
    return readTasks(date)
      .then((next) => {
        if (sequence !== loadSequence.current.value) return;
        setData(next);
        setFault("");
      })
      .catch((error: unknown) => {
        if (sequence !== loadSequence.current.value) return;
        setFault(
          error instanceof ApiError && error.status === 401
            ? t.signIn
            : error instanceof ApiError && error.status === 429
              ? t.rateLimit
              : t.connection,
        );
      })
      .finally(() => {
        if (sequence === loadSequence.current.value) setLoading(false);
      });
  }, [date, t]);

  useEffect(() => {
    const sequence = loadSequence.current;
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
      ++sequence.value;
      window.clearInterval(timer);
      window.removeEventListener("focus", refresh);
      document.removeEventListener("visibilitychange", visible);
    };
  }, [load]);

  useEffect(() => {
    if (editor?.kind === "create" || editor?.kind === "manage")
      nameInput.current?.focus();
    if (editor?.kind === "date") dateInput.current?.focus();
    if (editor?.kind === "delete") deleteCancel.current?.focus();
  }, [editor]);

  useEffect(() => {
    if (restoreFocus.current && !editor && !loading && !busy) {
      restoreFocus.current = false;
      const trigger = editorTrigger.current;
      const target =
        trigger?.isConnected && !trigger.disabled
          ? trigger
          : pending
            ? refreshButton.current
            : addButton.current;
      target?.focus();
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
    ++loadSequence.current.value;
    setBusy(true);
    setPending(operation);
    setFault("");
    setNotice("");
    try {
      await submitTask(operation);
      sessionStorage.removeItem(storageKey);
      setPending(undefined);
      setNotice(t.updated);
      if (operation.kind !== "complete") closeEditor();
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
    setLoading(true);
    await load();
  }

  const today = data?.today ?? profile.today;
  const selectedDate = date || today;
  const due = data?.items.filter((task) => task.isDue) ?? [];
  const completed = due.filter((task) => task.completed).length;
  const items = view === "day" ? due : (data?.items ?? []);
  const locked = busy || !!pending || loading;
  const current = data?.date === selectedDate;
  function selectDate(value: string) {
    const next = value === today ? "" : value;
    if (next !== date) {
      setLoading(true);
      setDate(next);
    }
    setNotice("");
  }
  function refresh() {
    if (busyRef.current) return;
    setLoading(true);
    void load();
  }
  const locale = lang === "uk" ? "uk-UA" : "en-GB";
  const formatDate = (value: string, options: Intl.DateTimeFormatOptions) =>
    new Intl.DateTimeFormat(locale, { ...options, timeZone: "UTC" }).format(
      new Date(`${value}T12:00:00Z`),
    );
  const week = weekDates(selectedDate);
  const percentage = due.length
    ? Math.round((completed / due.length) * 100)
    : 0;
  const feedback = (
    <>
      {fault && (
        <p className="feedback error" role="alert">
          {fault}
        </p>
      )}
      {pending && !busy && (
        <div className="feedback error tasks-recovery" role="alert">
          <p>{t.unknown}</p>
          <div>
            <button
              className="quiet"
              disabled={busy || loading}
              onClick={refresh}
            >
              <RefreshCw size={18} aria-hidden="true" />
              {t.refresh}
            </button>
            <button
              className="button secondary"
              disabled={busy || loading}
              onClick={() => void mutate(pending, true)}
            >
              {t.retry}
            </button>
          </div>
        </div>
      )}
    </>
  );
  return (
    <div className="tasks-board">
      <section className="tasks-overview" aria-label={t.dayPlan}>
        <div className="tasks-day-heading">
          <h1>
            {selectedDate === today
              ? t.today
              : formatDate(selectedDate, { weekday: "long" })}
          </h1>
          <p>
            <time dateTime={selectedDate}>
              {formatDate(selectedDate, {
                day: "numeric",
                month: "long",
                year: "numeric",
              })}
            </time>
          </p>
          <span className="tasks-day-note">
            {data && current
              ? due.length && completed === due.length
                ? t.allDone
                : `${t.remaining} · ${due.length - completed}`
              : t.loading}
          </span>
        </div>
        <div
          className="tasks-progress"
          role="progressbar"
          aria-label={t.progress}
          aria-valuenow={current ? completed : 0}
          aria-valuemin={0}
          aria-valuemax={due.length || 1}
          aria-valuetext={`${current ? completed : 0} / ${due.length} ${t.progress}`}
        >
          <svg viewBox="0 0 80 80" aria-hidden="true">
            <circle className="tasks-progress-track" cx="40" cy="40" r="34" />
            <circle
              className="tasks-progress-fill"
              cx="40"
              cy="40"
              r="34"
              pathLength="100"
              strokeDasharray="100"
              strokeDashoffset={100 - (current ? percentage : 0)}
            />
          </svg>
          <span aria-hidden="true">
            <strong>
              {current ? completed : 0}
              <small>/{due.length}</small>
            </strong>
          </span>
        </div>
      </section>

      <section className="tasks-calendar" aria-label={t.date}>
        <div className="tasks-calendar-heading">
          <button
            className="quiet tasks-calendar-trigger"
            disabled={busy || loading}
            aria-label={t.chooseDate}
            onClick={(event) => {
              editorTrigger.current = event.currentTarget;
              setCalendarDate(selectedDate);
              setEditor({ kind: "date" });
            }}
          >
            <CalendarDays size={18} aria-hidden="true" />
            {formatDate(selectedDate, { month: "long", year: "numeric" })}
          </button>
          <div className="tasks-calendar-actions">
            {selectedDate !== today && (
              <button
                className="quiet"
                disabled={busy || loading}
                onClick={() => {
                  selectDate("");
                }}
              >
                {t.today}
              </button>
            )}
            <button
              className="icon-button"
              aria-label={t.previousWeek}
              disabled={busy || loading}
              onClick={() => {
                selectDate(shiftDate(selectedDate, -7));
              }}
            >
              <ChevronLeft size={20} aria-hidden="true" />
            </button>
            <button
              className="icon-button"
              aria-label={t.nextWeek}
              disabled={busy || loading || week[6] >= today}
              onClick={() => {
                selectDate(
                  shiftDate(selectedDate, 7) > today
                    ? ""
                    : shiftDate(selectedDate, 7),
                );
              }}
            >
              <ChevronRight size={20} aria-hidden="true" />
            </button>
          </div>
        </div>
        <div className="tasks-week">
          {week.map((day) => (
            <button
              key={day}
              className="tasks-day"
              aria-pressed={day === selectedDate}
              aria-current={day === today ? "date" : undefined}
              aria-label={formatDate(day, { dateStyle: "full" })}
              disabled={busy || loading || day > today}
              onClick={() => {
                selectDate(day);
              }}
            >
              <span>{formatDate(day, { weekday: "short" })}</span>
              <strong>{Number(day.slice(-2))}</strong>
              <i aria-hidden="true" />
            </button>
          ))}
        </div>
      </section>

      <div className="tasks-toolbar">
        <div className="tasks-views" role="group" aria-label={t.title}>
          <button aria-pressed={view === "day"} onClick={() => setView("day")}>
            {t.scheduled}
            {data && current && <span>{due.length}</span>}
          </button>
          <button aria-pressed={view === "all"} onClick={() => setView("all")}>
            {t.all}
            {data && <span>{data.items.length}</span>}
          </button>
        </div>
        <button
          ref={addButton}
          className="button primary tasks-add"
          aria-label={t.add}
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
          <span>{t.add}</span>
        </button>
      </div>

      <div
        className="tasks-scroll-region"
        tabIndex={0}
        role="region"
        aria-label={view === "day" ? t.scheduled : t.all}
        aria-busy={loading}
      >
        {!editor && feedback}
        {loading && (!data || !current) ? (
          <div className="tasks-skeleton" role="status">
            <span className="sr-only">{t.loading}</span>
            {[1, 2, 3, 4].map((row) => (
              <div key={row} aria-hidden="true">
                <i />
                <span />
              </div>
            ))}
          </div>
        ) : data && current ? (
          items.length ? (
            <ul className="tasks-list">
              {items.map((task) => (
                <li
                  key={task.id}
                  className={
                    task.completed && view === "day"
                      ? "tasks-row is-complete"
                      : "tasks-row"
                  }
                >
                  {view === "day" ? (
                    <button
                      className="tasks-check"
                      role="checkbox"
                      aria-checked={task.completed}
                      aria-label={`${task.completed ? t.unmark : t.mark}: ${task.name}`}
                      disabled={locked || !task.canComplete}
                      onClick={() => {
                        const input: TaskCompletion = {
                          date: data.date,
                          completed: !task.completed,
                          version: task.version,
                        };
                        void mutate({ kind: "complete", id: task.id, input });
                      }}
                    >
                      <span>
                        {task.completed && (
                          <Check
                            size={17}
                            strokeWidth={2.5}
                            aria-hidden="true"
                          />
                        )}
                      </span>
                    </button>
                  ) : (
                    <span className="tasks-repeat-icon">
                      <Repeat2 size={19} aria-hidden="true" />
                    </span>
                  )}
                  <div className="tasks-task-copy">
                    <span className="tasks-task-name">{task.name}</span>
                    <p>
                      {frequency(task.intervalDays, t)}
                      {view === "all" && (
                        <>
                          {" "}
                          · {t.starting}{" "}
                          <time dateTime={task.startDate}>
                            {formatDate(task.startDate, {
                              day: "numeric",
                              month: "short",
                            })}
                          </time>
                        </>
                      )}
                    </p>
                  </div>
                  <button
                    className="icon-button tasks-more"
                    aria-label={`${t.details}: ${task.name}`}
                    disabled={locked}
                    onClick={(event) => {
                      editorTrigger.current = event.currentTarget;
                      setName(task.name);
                      setEditor({ kind: "manage", task });
                    }}
                  >
                    <MoreHorizontal size={22} aria-hidden="true" />
                  </button>
                </li>
              ))}
            </ul>
          ) : (
            <div className="tasks-empty">
              <span>
                <CircleCheck size={30} strokeWidth={1.5} aria-hidden="true" />
              </span>
              <h2>{data.items.length ? t.restTitle : t.emptyTitle}</h2>
              <p>{data.items.length ? t.restBody : t.emptyBody}</p>
              <button
                className="quiet"
                disabled={locked}
                onClick={() => addButton.current?.click()}
              >
                <Plus size={18} aria-hidden="true" />
                {t.add}
              </button>
            </div>
          )
        ) : null}
      </div>
      <div className="tasks-board-footer">
        <span role="status" className="tasks-save-status">
          {busy ? t.loading : notice || (loading ? t.loading : "")}
        </span>
        <button className="quiet" disabled={busy || loading} onClick={refresh}>
          <RefreshCw size={15} aria-hidden="true" />
          {t.refresh}
        </button>
      </div>

      {editor && (
        <TaskDialog
          title={
            editor.kind === "create"
              ? t.newTask
              : editor.kind === "delete"
                ? t.deleteTitle
                : editor.kind === "date"
                  ? t.chooseDate
                  : t.details
          }
          closeLabel={t.cancel}
          busy={busy}
          onClose={closeEditor}
        >
          {editor.kind === "date" ? (
            <form
              onSubmit={(event) => {
                event.preventDefault();
                selectDate(calendarDate);
                closeEditor();
              }}
            >
              <div className="tasks-dialog-body">
                <label>
                  {t.date}
                  <input
                    data-dialog-focus
                    ref={dateInput}
                    type="date"
                    value={calendarDate}
                    max={today}
                    required
                    onChange={(event) => setCalendarDate(event.target.value)}
                  />
                </label>
              </div>
              <div className="tasks-dialog-footer">
                <button
                  ref={refreshButton}
                  type="button"
                  className="quiet"
                  onClick={() => {
                    selectDate("");
                    closeEditor();
                  }}
                >
                  {t.today}
                </button>
                <button type="submit" className="button primary">
                  {t.goToDate}
                </button>
              </div>
            </form>
          ) : editor.kind === "delete" ? (
            <div className="tasks-dialog-content">
              <div className="tasks-dialog-body">
                <p className="tasks-delete-name">{editor.task.name}</p>
                <p>{t.deleteConfirm}</p>
                {feedback}
              </div>
              <div className="tasks-dialog-footer">
                <button
                  ref={deleteCancel}
                  data-dialog-focus
                  type="button"
                  className="quiet"
                  disabled={busy}
                  onClick={() =>
                    setEditor({ kind: "manage", task: editor.task })
                  }
                >
                  {t.back}
                </button>
                <button
                  className="button tasks-delete-button"
                  disabled={locked}
                  onClick={() =>
                    void mutate({
                      kind: "delete",
                      id: editor.task.id,
                      version: editor.task.version,
                    })
                  }
                >
                  <Trash2 size={18} aria-hidden="true" />
                  {t.delete}
                </button>
              </div>
            </div>
          ) : (
            <form
              onSubmit={(event) => {
                event.preventDefault();
                if (editor.kind === "create") {
                  const input: CreateTask = {
                    requestId: crypto.randomUUID(),
                    name: name.trim(),
                    intervalDays,
                    startDate,
                  };
                  void mutate({ kind: "create", input });
                } else {
                  const input: RenameTask = {
                    name: name.trim(),
                    version: editor.task.version,
                  };
                  void mutate({ kind: "rename", id: editor.task.id, input });
                }
              }}
            >
              <div className="tasks-dialog-body tasks-form">
                <label>
                  {t.name}
                  <input
                    data-dialog-focus
                    ref={nameInput}
                    value={name}
                    required
                    maxLength={120}
                    disabled={locked}
                    placeholder={t.namePlaceholder}
                    onChange={(event) => setName(event.target.value)}
                  />
                </label>
                {editor.kind === "create" ? (
                  <>
                    <fieldset className="tasks-frequency" disabled={locked}>
                      <legend>{t.repeat}</legend>
                      {[1, 2, 3, 7].map((days) => (
                        <label key={days}>
                          <input
                            type="radio"
                            name="interval"
                            value={days}
                            checked={intervalDays === days}
                            onChange={() => setIntervalDays(days)}
                          />
                          <span>{frequency(days, t)}</span>
                        </label>
                      ))}
                    </fieldset>
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
                    <p className="tasks-form-hint">{t.scheduleHint}</p>
                  </>
                ) : (
                  <>
                    <div className="tasks-schedule-detail">
                      <Repeat2 size={19} aria-hidden="true" />
                      <div>
                        <strong>
                          {frequency(editor.task.intervalDays, t)}
                        </strong>
                        <p>
                          {t.starting}{" "}
                          {formatDate(editor.task.startDate, {
                            dateStyle: "long",
                          })}
                        </p>
                      </div>
                    </div>
                    <p className="tasks-form-hint">{t.renameHint}</p>
                  </>
                )}
                {feedback}
              </div>
              <div className="tasks-dialog-footer">
                {editor.kind === "manage" ? (
                  <button
                    type="button"
                    className="quiet danger"
                    disabled={locked}
                    onClick={() =>
                      setEditor({ kind: "delete", task: editor.task })
                    }
                  >
                    <Trash2 size={18} aria-hidden="true" />
                    {t.delete}
                  </button>
                ) : (
                  <button
                    type="button"
                    className="quiet"
                    disabled={busy}
                    onClick={closeEditor}
                  >
                    {t.cancel}
                  </button>
                )}
                <button
                  type="submit"
                  className="button primary"
                  disabled={locked || !name.trim()}
                >
                  <Check size={18} aria-hidden="true" />
                  {t.save}
                </button>
              </div>
            </form>
          )}
        </TaskDialog>
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
