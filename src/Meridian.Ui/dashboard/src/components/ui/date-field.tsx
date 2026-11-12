import { type AriaAttributes, type KeyboardEvent, type ReactNode, useEffect, useId, useRef, useState } from "react";
import { cn } from "@/lib/utils";
import { usesTrackedCaps } from "@/design-system/label-case";

// Civil dates use UTC only as an internal carrier, never as an instant to localize.
export function calendarDate(year: number, month: number, day: number): Date {
  const date = new Date(0);
  date.setUTCFullYear(year, month, day);
  date.setUTCHours(12, 0, 0, 0);
  return date;
}

export function parseDateOnly(value: string | null | undefined): Date | null {
  const match = typeof value === "string" && value.match(/^(\d{4})-(\d{2})-(\d{2})$/);
  if (!match) return null;
  const year = Number(match[1]); const month = Number(match[2]); const day = Number(match[3]);
  const date = calendarDate(year, month - 1, day);
  return date.getUTCFullYear() === year && date.getUTCMonth() === month - 1 && date.getUTCDate() === day ? date : null;
}

export function dateOnly(date: Date): string {
  return `${String(date.getUTCFullYear()).padStart(4, "0")}-${String(date.getUTCMonth() + 1).padStart(2, "0")}-${String(date.getUTCDate()).padStart(2, "0")}`;
}

function today(): Date {
  const date = new Date();
  return calendarDate(date.getFullYear(), date.getMonth(), date.getDate());
}
const monthNumber = (date: Date) => date.getUTCFullYear() * 12 + date.getUTCMonth();
const monthStart = (date: Date) => calendarDate(date.getUTCFullYear(), date.getUTCMonth(), 1);
const readable = (date: Date, options: Intl.DateTimeFormatOptions) => date.toLocaleDateString("en-US", { ...options, timeZone: "UTC" });

function addMonths(date: Date, delta: number): Date {
  const first = calendarDate(date.getUTCFullYear(), date.getUTCMonth() + delta, 1);
  const last = calendarDate(first.getUTCFullYear(), first.getUTCMonth() + 1, 0).getUTCDate();
  return calendarDate(first.getUTCFullYear(), first.getUTCMonth(), Math.min(last, date.getUTCDate()));
}

function keyDate(date: Date, event: KeyboardEvent<HTMLButtonElement>): Date | null {
  const deltas: Record<string, number> = { ArrowLeft: -1, ArrowRight: 1, ArrowUp: -7, ArrowDown: 7, Home: -date.getUTCDay(), End: 6 - date.getUTCDay() };
  if (Object.hasOwn(deltas, event.key)) return calendarDate(date.getUTCFullYear(), date.getUTCMonth(), date.getUTCDate() + deltas[event.key]!);
  if (event.key === "PageUp" || event.key === "PageDown") return addMonths(date, (event.key === "PageUp" ? -1 : 1) * (event.shiftKey ? 12 : 1));
  return null;
}

interface CalendarRange { start: string | null; end: string | null }
interface CommonProps extends AriaAttributes { label?: ReactNode; id?: string; disabled?: boolean; className?: string; placeholder?: string }
type CalendarFieldProps = CommonProps & (
  { range?: false; value?: string | null; onChange?: (value: string) => void } |
  { range: true; start?: string | null; end?: string | null; onChange?: (range: CalendarRange) => void }
);

interface MonthProps {
  date: Date; titleId: string; focusDate: string; selected: string[];
  rangeStart?: string | null; rangeEnd?: string | null;
  onPick: (value: string) => void; onMove: (value: Date) => void;
  previous?: ReactNode; next?: ReactNode;
}

function CalendarMonth({ date, titleId, focusDate, selected, rangeStart, rangeEnd, onPick, onMove, previous, next }: MonthProps) {
  const first = monthStart(date);
  const gridStart = calendarDate(first.getUTCFullYear(), first.getUTCMonth(), 1 - first.getUTCDay());
  const weekdays = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];
  const rows = Array.from({ length: 6 }, (_, row) => Array.from({ length: 7 }, (_, column) => calendarDate(gridStart.getUTCFullYear(), gridStart.getUTCMonth(), gridStart.getUTCDate() + row * 7 + column)));
  return <div className="w-[16rem]">
    <div className="mb-3 flex items-center justify-between text-xs font-semibold text-foreground">
      {previous ?? <span />}<span id={titleId} aria-live="polite">{readable(date, { month: "long", year: "numeric" })}</span>{next ?? <span />}
    </div>
    <div className="grid grid-cols-7 gap-0.5" role="grid" aria-labelledby={titleId}>
      <div className="contents" role="row">{weekdays.map((day) => <div key={day} role="columnheader" aria-label={day} className="text-center font-mono text-xs font-semibold text-muted-foreground">{day.slice(0, 2)}</div>)}</div>
      {rows.map((days, index) => <div key={index} className="contents" role="row">{days.map((day) => {
        const iso = dateOnly(day); const other = day.getUTCMonth() !== date.getUTCMonth(); const chosen = selected.includes(iso);
        const current = iso === dateOnly(today()); const inRange = rangeStart && rangeEnd && rangeStart <= iso && iso <= rangeEnd;
        return <div key={iso} role="gridcell" aria-selected={chosen}>
          <button type="button" data-date={iso} data-current-month={!other}
            className={cn("flex h-8 w-8 items-center justify-center rounded-[var(--radius-button)] border border-transparent font-mono text-xs text-foreground transition-colors hover:bg-[var(--ws-row-hover)] focus-visible:outline-hidden focus-visible:ring-2 focus-visible:ring-[var(--interaction-focus)] [outline-offset:-2px]", other && "text-muted-foreground", inRange && "bg-[var(--interaction-selected-surface)]", current && !chosen && "border-[var(--interaction-selected)]", chosen && "border-[var(--interaction-selected)] bg-[var(--interaction-selected)] text-[var(--text-on-fill)] hover:bg-[var(--interaction-selected)]")}
            tabIndex={!other && iso === focusDate ? 0 : -1}
            aria-label={readable(day, { weekday: "long", year: "numeric", month: "long", day: "numeric" })} aria-current={current ? "date" : undefined}
            onClick={() => onPick(iso)} onKeyDown={(event) => { const nextDate = keyDate(day, event); if (nextDate) { event.preventDefault(); onMove(nextDate); } }}>
            {day.getUTCDate()}
          </button>
        </div>;
      })}</div>)}
    </div>
  </div>;
}

export function CalendarField(props: CalendarFieldProps) {
  const { label, id, disabled = false, className, placeholder = "Select date…" } = props;
  const ariaProps = Object.fromEntries(Object.entries(props).filter(([name]) => name.startsWith("aria-"))) as AriaAttributes;
  const range = props.range === true;
  const value = props.range === true ? undefined : props.value;
  const start = props.range === true ? props.start : undefined;
  const end = props.range === true ? props.end : undefined;
  const generated = useId(); const fieldId = id ?? `date-${generated}`; const panelId = `${fieldId}-calendar`;
  const labelId = `${fieldId}-label`; const valueId = `${fieldId}-value`;
  const initial = parseDateOnly(range ? start : value) ?? today();
  const [open, setOpen] = useState(false);
  const [month, setMonth] = useState(() => monthStart(initial));
  const [focusDate, setFocusDate] = useState(() => dateOnly(initial));
  const root = useRef<HTMLDivElement>(null); const trigger = useRef<HTMLButtonElement>(null); const panel = useRef<HTMLDivElement>(null);
  const close = (restore = false) => { setOpen(false); if (restore) trigger.current?.focus(); };
  const move = (next: Date) => {
    setFocusDate(dateOnly(next));
    if (monthNumber(next) < monthNumber(month) || monthNumber(next) > monthNumber(month) + (range ? 1 : 0)) setMonth(monthStart(next));
  };
  const navigate = (delta: number) => {
    setMonth((current) => addMonths(monthStart(current), delta));
    setFocusDate(dateOnly(addMonths(parseDateOnly(focusDate) ?? initial, delta)));
  };
  const focusDay = () => panel.current?.querySelector<HTMLButtonElement>(`button[data-date="${focusDate}"][data-current-month="true"]`)?.focus();
  useEffect(() => {
    if (!open) return undefined;
    const outside = (event: MouseEvent) => { if (event.target instanceof Node && !root.current?.contains(event.target)) setOpen(false); };
    document.addEventListener("mousedown", outside); return () => document.removeEventListener("mousedown", outside);
  }, [open]);
  useEffect(() => { if (disabled) setOpen(false); }, [disabled]);
  useEffect(() => {
    if (!open || !panel.current) return;
    const box = panel.current.getBoundingClientRect();
    const overflows = box.right > window.innerWidth - 12;
    panel.current.style.left = overflows ? "auto" : "0"; panel.current.style.right = overflows ? "0" : "auto";
    if (!document.activeElement?.hasAttribute("data-calendar-nav")) panel.current.querySelector<HTMLButtonElement>(`button[data-date="${focusDate}"][data-current-month="true"]`)?.focus();
  }, [open, focusDate, month]);
  const toggle = () => {
    if (open) close();
    else { const next = parseDateOnly(range ? start : value) ?? today(); setMonth(monthStart(next)); setFocusDate(dateOnly(next)); setOpen(true); }
  };
  const pick = (iso: string) => {
    if (props.range !== true) { props.onChange?.(iso); close(true); return; }
    if (!parseDateOnly(start) || parseDateOnly(end)) { props.onChange?.({ start: iso, end: null }); move(parseDateOnly(iso)!); }
    else { props.onChange?.(iso < start! ? { start: iso, end: start! } : { start: start!, end: iso }); close(true); }
  };
  const display = (iso: string | null | undefined, fallback: string, year?: boolean) => iso ? parseDateOnly(iso) ? readable(parseDateOnly(iso)!, { year: year ? "numeric" : undefined, month: "short", day: "numeric" }) : "Invalid date" : fallback;
  const displayValue = range ? `${display(start, "Start")} — ${display(end, "End")}` : display(value, placeholder, true);
  const describedLabel = typeof label === "string" ? label : range ? "Date range" : "Date";
  const labelledBy = props["aria-labelledby"] ? `${props["aria-labelledby"]} ${valueId}` : label ? `${labelId} ${valueId}` : undefined;
  const externalLabel = id && !labelledBy && !props["aria-label"];
  const description = [props["aria-describedby"], externalLabel || props["aria-label"] ? valueId : null].filter(Boolean).join(" ") || undefined;
  const nav = (delta: number) => <button type="button" data-calendar-nav aria-label={delta < 0 ? "Previous month" : "Next month"} onClick={() => navigate(delta)}
    className="min-h-7 min-w-7 rounded-[var(--radius-button)] border border-border bg-card px-2 py-1 text-foreground transition-colors hover:bg-[var(--ws-row-hover)] focus-visible:outline-hidden focus-visible:ring-2 focus-visible:ring-[var(--interaction-focus)]">{delta < 0 ? "←" : "→"}</button>;
  return <div ref={root} className={cn("relative w-full", className)} data-date-field-open={open ? "true" : undefined}
    onBlur={(event) => { if (!(event.relatedTarget instanceof Node) || !event.currentTarget.contains(event.relatedTarget)) setOpen(false); }}
    onKeyDown={(event) => { if (event.key === "Escape" && open) { event.preventDefault(); event.stopPropagation(); close(true); } }}>
    {label ? <label id={labelId} htmlFor={fieldId} className={cn("mb-1.5 block text-xs font-semibold text-muted-foreground", usesTrackedCaps(label) && "font-mono uppercase tracking-[0.14em]")}>{label}</label> : null}
    <button {...ariaProps} ref={trigger} id={fieldId} type="button" disabled={disabled}
      aria-haspopup="dialog" aria-expanded={open} aria-controls={open ? panelId : undefined}
      aria-labelledby={props["aria-label"] ? props["aria-labelledby"] : labelledBy} aria-describedby={description}
      aria-label={props["aria-label"] ?? (!labelledBy && !id ? `${describedLabel}: ${displayValue}` : undefined)}
      onClick={toggle} onKeyDown={(event) => { if (event.key === "ArrowDown") { event.preventDefault(); if (!open) toggle(); else focusDay(); } }}
      className={cn("h-9 w-full cursor-pointer rounded-[var(--radius-button)] border border-border bg-[var(--ws-surface-raised)] px-2.5 text-left font-mono text-sm text-foreground", "hover:border-[var(--ws-border-hover)] focus-visible:border-[var(--interaction-focus)] focus-visible:outline-hidden focus-visible:ring-2 focus-visible:ring-[var(--interaction-focus)]", disabled && "cursor-not-allowed opacity-55")}>
      <span id={valueId}>{displayValue}</span>
    </button>
    {open ? <div ref={panel} id={panelId} role="dialog" aria-modal="false" aria-label={`${describedLabel} calendar`}
      className={cn("absolute left-0 top-[calc(100%+4px)] z-[100] max-w-[calc(100vw-24px)] overflow-auto rounded-[var(--radius-overlay)] border border-border bg-popover p-3 shadow-float", range && "flex gap-4")}>
      {Array.from({ length: range ? 2 : 1 }, (_, index) => <CalendarMonth key={index} date={calendarDate(month.getUTCFullYear(), month.getUTCMonth() + index, 1)} titleId={`${panelId}-month-${index}`} focusDate={focusDate} selected={(range ? [start, end] : [value]).filter((entry): entry is string => Boolean(entry))} rangeStart={range ? start : undefined} rangeEnd={range ? end : undefined} onPick={pick} onMove={move} previous={index === 0 ? nav(-1) : undefined} next={index === (range ? 1 : 0) ? nav(1) : undefined} />)}
    </div> : null}
  </div>;
}
