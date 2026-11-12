// Shared private date-field behavior. ISO values represent civil dates, not
// instants; UTC is only an internal carrier so timezones cannot shift a day.
import React from "react";

export function calendarDate(year, month, day) {
  const date = new Date(0);
  date.setUTCFullYear(year, month, day);
  date.setUTCHours(12, 0, 0, 0);
  return date;
}

export function parseDateOnly(value) {
  const match = typeof value === "string" && value.match(/^(\d{4})-(\d{2})-(\d{2})$/);
  if (!match) return null;
  const [, year, month, day] = match.map(Number);
  const date = calendarDate(year, month - 1, day);
  return date.getUTCFullYear() === year && date.getUTCMonth() === month - 1 && date.getUTCDate() === day ? date : null;
}

export function dateOnly(date) {
  return `${String(date.getUTCFullYear()).padStart(4, "0")}-${String(date.getUTCMonth() + 1).padStart(2, "0")}-${String(date.getUTCDate()).padStart(2, "0")}`;
}

function today() {
  const date = new Date();
  return calendarDate(date.getFullYear(), date.getMonth(), date.getDate());
}
const monthNumber = (date) => date.getUTCFullYear() * 12 + date.getUTCMonth();
const monthStart = (date) => calendarDate(date.getUTCFullYear(), date.getUTCMonth(), 1);
const readable = (date, options) => date.toLocaleDateString("en-US", { ...options, timeZone: "UTC" });

function addMonths(date, delta) {
  const first = calendarDate(date.getUTCFullYear(), date.getUTCMonth() + delta, 1);
  const last = calendarDate(first.getUTCFullYear(), first.getUTCMonth() + 1, 0).getUTCDate();
  return calendarDate(first.getUTCFullYear(), first.getUTCMonth(), Math.min(last, date.getUTCDate()));
}

function keyDate(date, event) {
  const deltas = { ArrowLeft: -1, ArrowRight: 1, ArrowUp: -7, ArrowDown: 7, Home: -date.getUTCDay(), End: 6 - date.getUTCDay() };
  if (Object.hasOwn(deltas, event.key)) return calendarDate(date.getUTCFullYear(), date.getUTCMonth(), date.getUTCDate() + deltas[event.key]);
  if (event.key === "PageUp" || event.key === "PageDown") return addMonths(date, (event.key === "PageUp" ? -1 : 1) * (event.shiftKey ? 12 : 1));
  return null;
}

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-date-label,.mds-drange-label{display:block;font-family:var(--font-body);font-size:10px;font-weight:600;font-variant:all-small-caps;letter-spacing:.03em;color:var(--text-muted,#5E666F);margin-bottom:5px;}
.mds-date-wrap,.mds-drange-wrap{position:relative;display:inline-block;width:100%;}
.mds-date-input,.mds-drange-input{box-sizing:border-box;width:100%;height:32px;text-align:left;padding:7px 10px;border:1px solid var(--border,#E4E3DE);border-radius:var(--radius-button,2px);background:var(--bg-light,#FBFAF8);color:var(--text-primary,#22252A);font-family:var(--font-data);font-size:13px;cursor:pointer;}
.mds-date-input:disabled,.mds-drange-input:disabled{opacity:.6;cursor:not-allowed;}
.mds-date-cal,.mds-drange-cal{position:absolute;top:calc(100% + 4px);left:0;z-index:100;box-sizing:border-box;max-width:calc(100vw - 24px);overflow:auto;background:var(--bg-light,#FBFAF8);border:1px solid var(--border,#E4E3DE);padding:12px;}
.mds-drange-cal{display:grid;grid-template-columns:repeat(2,238px);gap:16px;}
.mds-date-header,.mds-drange-header{display:flex;justify-content:space-between;align-items:center;gap:8px;margin-bottom:12px;font-family:var(--font-body);font-size:12px;font-weight:600;color:var(--text-primary,#22252A);}
.mds-date-nav{min-width:28px;min-height:28px;cursor:pointer;padding:4px 8px;border:1px solid var(--border,#E4E3DE);border-radius:var(--radius-button,2px);background:var(--bg-light,#FBFAF8);color:var(--text-primary,#22252A);}
.mds-date-nav:hover{background:var(--bg-hover,#F0EEE9);}
.mds-date-grid{display:grid;grid-template-columns:repeat(7,32px);gap:2px;}
.mds-date-row{display:contents;}
.mds-date-dow{font-family:var(--font-body);font-size:10px;font-weight:600;text-align:center;color:var(--text-muted,#5E666F);}
.mds-date-day{width:32px;height:32px;display:flex;align-items:center;justify-content:center;border:1px solid var(--border,#E4E3DE);border-radius:var(--radius-button,2px);background:var(--bg-light,#FBFAF8);color:var(--text-primary,#22252A);cursor:pointer;font-size:12px;}
.mds-date-day:hover{background:var(--bg-hover,#F0EEE9);}
.mds-date-day--other{color:var(--text-muted,#5E666F);}
.mds-date-day--inrange{background:var(--blue-a10,rgba(168,84,54,.10));}
.mds-date-day--selected{background:var(--accent,#A85436);color:var(--text-on-accent,#fff);border-color:var(--accent,#A85436);}
.mds-date-day--today{border:2px solid var(--accent,#A85436);}
.mds-date-input:focus-visible,.mds-drange-input:focus-visible,.mds-date-nav:focus-visible,.mds-date-day:focus-visible{outline:var(--focus-ring);outline-offset:var(--focus-ring-offset);}
`;
  const style = document.createElement("style");
  style.setAttribute("data-mds", "date-fields");
  style.textContent = css;
  document.head.appendChild(style);
}

function calendarMonth({ date, titleId, focusDate, selected, rangeStart, rangeEnd, onPick, onMove, monthControls }) {
  const first = monthStart(date);
  const gridStart = calendarDate(first.getUTCFullYear(), first.getUTCMonth(), 1 - first.getUTCDay());
  const rows = Array.from({ length: 6 }, (_, row) => Array.from({ length: 7 }, (_, column) => calendarDate(gridStart.getUTCFullYear(), gridStart.getUTCMonth(), gridStart.getUTCDate() + row * 7 + column)));
  const weekdays = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];
  return <div>
    <div className="mds-date-header">{monthControls?.previous}<span id={titleId} aria-live="polite">{readable(date, { month: "long", year: "numeric" })}</span>{monthControls?.next}</div>
    <div className="mds-date-grid" role="grid" aria-labelledby={titleId}>
      <div className="mds-date-row" role="row">{weekdays.map((day) => <div key={day} className="mds-date-dow" role="columnheader" aria-label={day}>{day.slice(0, 2)}</div>)}</div>
      {rows.map((days, index) => <div key={index} className="mds-date-row" role="row">{days.map((day) => {
        const iso = dateOnly(day);
        const other = day.getUTCMonth() !== date.getUTCMonth();
        const chosen = selected.includes(iso);
        const current = iso === dateOnly(today());
        const inRange = rangeStart && rangeEnd && rangeStart <= iso && iso <= rangeEnd;
        return <div key={iso} role="gridcell" aria-selected={chosen}>
          <button type="button" data-date={iso} data-current-month={!other}
            className={`mds-date-day${other ? " mds-date-day--other" : ""}${inRange ? " mds-date-day--inrange" : ""}${chosen ? " mds-date-day--selected" : ""}${current && !chosen ? " mds-date-day--today" : ""}`}
            tabIndex={!other && iso === focusDate ? 0 : -1} aria-label={readable(day, { weekday: "long", year: "numeric", month: "long", day: "numeric" })}
            aria-current={current ? "date" : undefined}
            onClick={() => onPick(iso)} onKeyDown={(event) => { const next = keyDate(day, event); if (next) { event.preventDefault(); onMove(next); } }}>
            {day.getUTCDate()}
          </button>
        </div>;
      })}</div>)}
    </div>
  </div>;
}

function CalendarField({ range = false, label, value, start, end, onChange, id, disabled = false, ...rest }) {
  inject();
  const generated = React.useId();
  const fieldId = id ?? `mds-date-${generated}`;
  const panelId = `${fieldId}-calendar`;
  const labelId = `${fieldId}-label`;
  const valueId = `${fieldId}-value`;
  const initial = parseDateOnly(range ? start : value) ?? today();
  const [open, setOpen] = React.useState(false);
  const [month, setMonth] = React.useState(() => monthStart(initial));
  const [focusDate, setFocusDate] = React.useState(() => dateOnly(initial));
  const root = React.useRef(null);
  const trigger = React.useRef(null);
  const panel = React.useRef(null);
  const prefix = range ? "mds-drange" : "mds-date";
  const close = (restore = false) => { setOpen(false); if (restore) trigger.current?.focus(); };
  const move = (next) => {
    setFocusDate(dateOnly(next));
    if (monthNumber(next) < monthNumber(month) || monthNumber(next) > monthNumber(month) + (range ? 1 : 0)) setMonth(monthStart(next));
  };
  const navigate = (delta) => {
    setMonth((current) => addMonths(monthStart(current), delta));
    setFocusDate(dateOnly(addMonths(parseDateOnly(focusDate) ?? initial, delta)));
  };
  React.useEffect(() => {
    if (!open) return undefined;
    const outside = (event) => { if (!root.current?.contains(event.target)) setOpen(false); };
    document.addEventListener("mousedown", outside);
    return () => document.removeEventListener("mousedown", outside);
  }, [open]);
  React.useEffect(() => { if (disabled) setOpen(false); }, [disabled]);
  React.useEffect(() => {
    if (!open || !panel.current) return;
    const box = panel.current.getBoundingClientRect();
    panel.current.style.left = box.right > window.innerWidth - 12 ? "auto" : "0";
    panel.current.style.right = box.right > window.innerWidth - 12 ? "0" : "auto";
    if (!document.activeElement?.hasAttribute("data-calendar-nav")) panel.current.querySelector(`button[data-date="${focusDate}"][data-current-month="true"]`)?.focus();
  }, [open, focusDate, month, range]);
  const toggle = () => {
    if (open) close();
    else { const next = parseDateOnly(range ? start : value) ?? today(); setMonth(monthStart(next)); setFocusDate(dateOnly(next)); setOpen(true); }
  };
  const pick = (iso) => {
    if (!range) { onChange?.(iso); close(true); return; }
    if (!parseDateOnly(start) || parseDateOnly(end)) { onChange?.({ start: iso, end: null }); move(parseDateOnly(iso)); }
    else { onChange?.(iso < start ? { start: iso, end: start } : { start, end: iso }); close(true); }
  };
  const display = (iso, fallback, year) => iso ? parseDateOnly(iso) ? readable(parseDateOnly(iso), { year: year ? "numeric" : undefined, month: "short", day: "numeric" }) : "Invalid date" : fallback;
  const displayValue = range ? `${display(start, "Start")} — ${display(end, "End")}` : display(value, "Select date…", true);
  const describedLabel = typeof label === "string" ? label : range ? "Date range" : "Date";
  const labelledBy = rest["aria-labelledby"] ? `${rest["aria-labelledby"]} ${valueId}` : label ? `${labelId} ${valueId}` : undefined;
  const nav = (delta) => <button type="button" className="mds-date-nav" data-calendar-nav aria-label={delta < 0 ? "Previous month" : "Next month"} onClick={() => navigate(delta)}>{delta < 0 ? "←" : "→"}</button>;
  const CalendarMonth = calendarMonth;
  const externallyLabelled = id && !labelledBy && !rest["aria-label"];
  const description = [rest["aria-describedby"], externallyLabelled ? valueId : null].filter(Boolean).join(" ") || undefined;
  return <div ref={root} onBlur={(event) => { if (!event.currentTarget.contains(event.relatedTarget)) setOpen(false); }} onKeyDown={(event) => { if (event.key === "Escape" && open) { event.preventDefault(); event.stopPropagation(); close(true); } }}>
    {label && <label id={labelId} htmlFor={fieldId} className={`${prefix}-label`}>{label}</label>}
    <div className={`${prefix}-wrap`}>
      <button {...rest} ref={trigger} id={fieldId} type="button" className={`${prefix}-input${rest.className ? ` ${rest.className}` : ""}`} disabled={disabled}
        aria-haspopup="dialog" aria-expanded={open} aria-controls={open ? panelId : undefined}
        aria-labelledby={rest["aria-label"] ? rest["aria-labelledby"] : labelledBy}
        aria-describedby={description}
        aria-label={rest["aria-label"] ?? (!labelledBy && !id ? `${describedLabel}: ${displayValue}` : undefined)}
        onClick={(event) => { rest.onClick?.(event); if (!event.defaultPrevented) toggle(); }} onKeyDown={(event) => { rest.onKeyDown?.(event); if (!event.defaultPrevented && event.key === "ArrowDown") { event.preventDefault(); if (!open) toggle(); } }}>
        <span id={valueId}>{displayValue}</span>
      </button>
      {open && <div ref={panel} id={panelId} className={`${prefix}-cal`} role="dialog" aria-modal="false" aria-label={`${describedLabel} calendar`}>
        {Array.from({ length: range ? 2 : 1 }, (_, index) => <CalendarMonth key={index} date={calendarDate(month.getUTCFullYear(), month.getUTCMonth() + index, 1)} titleId={`${panelId}-month-${index}`}
          focusDate={focusDate} selected={(range ? [start, end] : [value]).filter(Boolean)} rangeStart={range && start} rangeEnd={range && end} onPick={pick} onMove={move}
          monthControls={{ previous: index === 0 ? nav(-1) : null, next: index === (range ? 1 : 0) ? nav(1) : null }} />)}
      </div>}
    </div>
  </div>;
}

export { CalendarField as calendarField };
