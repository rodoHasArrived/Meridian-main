// Meridian DatePicker — single date field with a calendar popover. Functional only, no transitions.
// Dates are LOCAL calendar dates, never instants: an ISO "YYYY-MM-DD" in, the same string out, with
// no timezone round-trip. (The previous build used toISOString(), which shifts the date by a day for
// every user west of UTC — a settlement date entered as the 26th posted as the 25th.)
// The field is typeable; the calendar is a full ARIA grid with the standard keyboard model.
import React from "react";
import { calendarDate, toISO, fromISO, addDays, addMonths, sameDay, clampDate, parseLoose, dowLabels, longDate, monthTitle, weeksFor } from "./localDate.js";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-date-label{display:block;font-family:var(--font-body);font-size:10px;font-weight:600;
  font-variant:all-small-caps;letter-spacing:.03em;color:var(--text-muted,#606870);margin-bottom:5px;}
.mds-date-req{color:var(--red,#B54B42);margin-left:2px;}
.mds-date-wrap{position:relative;display:block;width:100%;}
.mds-date-field{display:flex;align-items:center;width:100%;box-sizing:border-box;height:32px;
  border:1px solid var(--border,#D2CDC3);background:var(--bg-light,#F8F7F3);}
.mds-date-field:hover{border-color:var(--border-hover,#BEB8AD);}
.mds-date-field:focus-within{border-color:var(--border-focus,#2D6FB3);outline:var(--focus-ring);outline-offset:var(--focus-ring-offset);}
.mds-date-field--error{border-color:var(--red,#B54B42);}
.mds-date-field--disabled{background:var(--bg-medium,#EBE8E1);opacity:.6;}
.mds-date-input{flex:1;min-width:0;height:100%;padding:7px 10px;border:none;background:transparent;
  color:var(--text-primary,#20252B);font-family:var(--font-data);font-size:13px;outline:none;
  font-variant-numeric:tabular-nums;}
.mds-date-input::placeholder{color:var(--text-muted,#606870);opacity:1;}
.mds-date-input:disabled{cursor:not-allowed;}
.mds-date-open{flex:0 0 auto;height:100%;padding:0 9px;border:0;background:none;margin:0;
  color:var(--text-muted,#606870);font-size:12px;cursor:pointer;display:inline-flex;align-items:center;}
.mds-date-open:hover{color:var(--text-primary,#20252B);}
.mds-date-open:focus-visible{outline:var(--focus-ring);outline-offset:-2px;}
.mds-date-open:disabled{cursor:not-allowed;color:var(--text-disabled,#A4ABB2);}
.mds-date-cal{position:absolute;top:calc(100% + 4px);left:0;z-index:var(--z-dropdown,100);
  background:var(--bg-light,#F8F7F3);border:1px solid var(--border,#D2CDC3);padding:10px;
  box-shadow:var(--shadow-menu,0 4px 12px rgba(17,23,29,.10));border-radius:var(--radius-overlay,6px);}
.mds-date-cal--above{top:auto;bottom:calc(100% + 4px);}
.mds-date-cal--right{left:auto;right:0;}
.mds-date-header{display:flex;justify-content:space-between;align-items:center;gap:6px;margin-bottom:10px;}
.mds-date-title{font-family:var(--font-body);font-size:12px;font-weight:600;color:var(--text-primary,#20252B);
  flex:1;text-align:center;white-space:nowrap;}
.mds-date-nav{flex:0 0 auto;width:24px;height:24px;display:inline-flex;align-items:center;justify-content:center;
  cursor:pointer;border:1px solid var(--border,#D2CDC3);background:var(--bg-light,#F8F7F3);
  color:var(--text-secondary,#535C65);font-size:11px;padding:0;}
.mds-date-nav:hover:not(:disabled){background:var(--bg-hover,#EDEFF0);color:var(--text-primary,#20252B);}
.mds-date-nav:focus-visible{outline:var(--focus-ring);outline-offset:1px;}
.mds-date-nav:disabled{opacity:.4;cursor:not-allowed;}
.mds-date-grid{border-collapse:separate;border-spacing:2px;}
.mds-date-dow{font-family:var(--font-body);font-size:10px;font-weight:600;text-align:center;
  color:var(--text-muted,#606870);padding:0 0 2px;width:30px;}
.mds-date-day{width:30px;height:28px;display:flex;align-items:center;justify-content:center;padding:0;
  border:1px solid transparent;background:transparent;cursor:pointer;font-family:var(--font-data);
  font-size:12px;font-variant-numeric:tabular-nums;color:var(--text-primary,#20252B);}
.mds-date-day:hover:not(:disabled){background:var(--bg-hover,#EDEFF0);}
.mds-date-day:focus-visible{outline:var(--focus-ring);outline-offset:-1px;}
.mds-date-day--other{color:var(--text-disabled,#A4ABB2);}
.mds-date-day--today{border-color:var(--interaction-selected,#496C8F);font-weight:600;}
.mds-date-day--selected{background:var(--interaction-selected,#496C8F);color:var(--text-on-accent,#0D1217);
  border-color:var(--interaction-selected,#496C8F);font-weight:600;}
.mds-date-day:disabled{color:var(--text-disabled,#A4ABB2);cursor:not-allowed;text-decoration:line-through;}
.mds-date-foot{display:flex;gap:6px;margin-top:8px;padding-top:8px;border-top:1px solid var(--border-divider,#E2DED5);}
.mds-date-foot button{flex:1;padding:4px 8px;border:1px solid var(--border,#D2CDC3);background:var(--bg-light,#F8F7F3);
  font-family:var(--font-body);font-size:11px;color:var(--text-secondary,#535C65);cursor:pointer;}
.mds-date-foot button:hover{background:var(--bg-hover,#EDEFF0);color:var(--text-primary,#20252B);}
.mds-date-foot button:focus-visible{outline:var(--focus-ring);outline-offset:1px;}
.mds-date-error{font-family:var(--font-body);font-size:11px;color:var(--red-dim,#883832);margin-top:5px;}
.mds-date-sr{position:absolute;width:1px;height:1px;overflow:hidden;clip:rect(0 0 0 0);white-space:nowrap;}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "datepicker");
  el.textContent = css;
  document.head.appendChild(el);
}

let uid = 0;
function useUid() {
  if (React.useId) return React.useId().replace(/:/g, "");
  return React.useMemo(() => "mdsdp" + ++uid, []);
}

export function DatePicker({
  label,
  value,
  onChange,
  min,
  max,
  disabledDate,
  disabled = false,
  required = false,
  error,
  clearable = true,
  showToday = true,
  weekStartsOn = 0,
  placeholder = "YYYY-MM-DD",
  id,
  name,
  "aria-label": ariaLabel,
  ...rest
}) {
  inject();
  const auto = useUid();
  const base = id || auto;
  const gridId = `${base}-grid`;
  const errorId = `${base}-error`;
  const {
    "aria-labelledby": labelledBy, "aria-describedby": describedBy,
    "aria-invalid": invalid, "aria-errormessage": errorMessage,
    "aria-required": ariaRequired, ...containerProps
  } = rest;
  const feedbackIds = [...new Set([...(describedBy || "").split(/\s+/).filter(Boolean), ...(error ? [errorId] : [])])].join(" ") || undefined;

  const selected = React.useMemo(() => fromISO(value), [value]);
  const minD = React.useMemo(() => fromISO(min), [min]);
  const maxD = React.useMemo(() => fromISO(max), [max]);

  const [open, setOpen] = React.useState(false);
  React.useEffect(() => { if (disabled) setOpen(false); }, [disabled]);
  const [draft, setDraft] = React.useState(value || "");
  const [cursor, setCursor] = React.useState(() => selected || new Date());
  const [placement, setPlacement] = React.useState("");
  const rootRef = React.useRef(null);
  const calRef = React.useRef(null);
  const inputRef = React.useRef(null);
  const dayRef = React.useRef(null);

  // Keep the field and the visible month in step when `value` changes from outside.
  React.useEffect(() => { setDraft(value || ""); }, [value]);
  React.useEffect(() => { if (selected) setCursor(selected); }, [value]);

  const isBlocked = React.useCallback((d) => {
    if (minD && d < minD) return true;
    if (maxD && d > maxD) return true;
    return !!disabledDate?.(toISO(d), d);
  }, [minD, maxD, disabledDate]);

  React.useEffect(() => {
    if (!open) return;
    const onDown = (e) => { if (rootRef.current && !rootRef.current.contains(e.target)) close(false); };
    document.addEventListener("mousedown", onDown);
    return () => document.removeEventListener("mousedown", onDown);
  }, [open]);

  // Flip the popover when it would run off the viewport.
  React.useLayoutEffect(() => {
    if (!open || !calRef.current) return;
    const r = calRef.current.getBoundingClientRect();
    let p = "";
    if (r.bottom > window.innerHeight && r.top - r.height > 0) p += " mds-date-cal--above";
    if (r.right > window.innerWidth) p += " mds-date-cal--right";
    setPlacement(p);
  }, [open]);

  React.useEffect(() => { if (open) dayRef.current?.focus(); }, [open, cursor]);

  const openCal = () => {
    if (disabled) return;
    setCursor(selected || clampDate(new Date(), minD, maxD));
    setOpen(true);
  };
  const close = (refocus = true) => {
    setOpen(false);
    setPlacement("");
    if (refocus) inputRef.current?.focus();
  };

  const pick = (d) => {
    if (disabled || isBlocked(d)) return;
    onChange?.(toISO(d));
    setDraft(toISO(d));
    close();
  };

  const commitTyped = () => {
    const raw = draft.trim();
    if (!raw) { onChange?.(""); return; }
    const d = parseLoose(raw);
    if (d && !isBlocked(d)) { onChange?.(toISO(d)); setDraft(toISO(d)); setCursor(d); }
    else setDraft(value || ""); // reject silently; the field snaps back to the last good value
  };

  const onFieldKey = (e) => {
    if (e.key === "Enter") { e.preventDefault(); open ? close() : commitTyped(); }
    else if (e.key === "ArrowDown" && !open) { e.preventDefault(); openCal(); }
    else if (e.key === "Escape" && open) { e.preventDefault(); e.stopPropagation(); close(); }
  };

  const onGridKey = (e) => {
    if (e.key === "Escape") { e.preventDefault(); e.stopPropagation(); close(); return; }
    if (!e.target.classList.contains("mds-date-day")) return;
    const k = e.key;
    let next = null;
    if (k === "ArrowLeft") next = addDays(cursor, -1);
    else if (k === "ArrowRight") next = addDays(cursor, 1);
    else if (k === "ArrowUp") next = addDays(cursor, -7);
    else if (k === "ArrowDown") next = addDays(cursor, 7);
    else if (k === "Home") next = addDays(cursor, -((cursor.getDay() - weekStartsOn + 7) % 7));
    else if (k === "End") next = addDays(cursor, 6 - ((cursor.getDay() - weekStartsOn + 7) % 7));
    else if (k === "PageUp") next = addMonths(cursor, e.shiftKey ? -12 : -1);
    else if (k === "PageDown") next = addMonths(cursor, e.shiftKey ? 12 : 1);
    else if (k === "Enter" || k === " ") return; // Native day button commits its own date.
    else if (k === "Escape") { e.preventDefault(); e.stopPropagation(); close(); return; }
    else if (k === "Tab") { close(); return; }
    if (next) { e.preventDefault(); setCursor(next); }
  };

  const weeks = React.useMemo(() => weeksFor(cursor, weekStartsOn), [cursor, weekStartsOn]);
  const dow = React.useMemo(() => dowLabels(weekStartsOn), [weekStartsOn]);
  const today = new Date();
  const prevBlocked = minD && calendarDate(cursor.getFullYear(), cursor.getMonth(), 0) < minD;
  const nextBlocked = maxD && calendarDate(cursor.getFullYear(), cursor.getMonth() + 1, 1) > maxD;

  return (
    <div ref={rootRef} {...containerProps}>
      {label && (
        <label className="mds-date-label" htmlFor={base}>
          {label}{required && <span className="mds-date-req" aria-hidden="true">*</span>}
        </label>
      )}
      <div className="mds-date-wrap">
        <div className={`mds-date-field${error ? " mds-date-field--error" : ""}${disabled ? " mds-date-field--disabled" : ""}`}>
          <input
            ref={inputRef}
            id={base}
            name={name}
            type="text"
            inputMode="numeric"
            autoComplete="off"
            className="mds-date-input"
            placeholder={placeholder}
            value={draft}
            disabled={disabled}
            aria-label={!label ? ariaLabel : undefined}
            aria-labelledby={labelledBy}
            aria-invalid={error ? true : invalid}
            aria-describedby={feedbackIds}
            aria-errormessage={errorMessage}
            aria-required={required ? true : ariaRequired}
            onChange={(e) => setDraft(e.target.value)}
            onBlur={commitTyped}
            onKeyDown={onFieldKey}
          />
          {clearable && value && !disabled && (
            <button type="button" className="mds-date-open" tabIndex={-1} aria-label="Clear date"
              onClick={() => { onChange?.(""); setDraft(""); inputRef.current?.focus(); }}>✕</button>
          )}
          <button
            type="button"
            className="mds-date-open"
            disabled={disabled}
            aria-haspopup="dialog"
            aria-expanded={open}
            aria-controls={open ? gridId : undefined}
            aria-label={open ? "Close calendar" : "Open calendar"}
            onClick={() => (open ? close() : openCal())}
          >▦</button>
        </div>
        {open && (
          <div ref={calRef} className={`mds-date-cal${placement}`} role="dialog" aria-modal="false"
            aria-label={label ? `${label} calendar` : "Choose date"} onKeyDown={onGridKey}>
            <div className="mds-date-header">
              <button type="button" className="mds-date-nav" disabled={prevBlocked} aria-label="Previous month"
                onClick={() => setCursor(addMonths(cursor, -1))}>‹</button>
              <span className="mds-date-title" aria-live="polite">{monthTitle(cursor)}</span>
              <button type="button" className="mds-date-nav" disabled={nextBlocked} aria-label="Next month"
                onClick={() => setCursor(addMonths(cursor, 1))}>›</button>
            </div>
            <table className="mds-date-grid" role="grid" id={gridId} aria-label={monthTitle(cursor)}>
              <thead>
                <tr>{dow.map((d) => <th key={d} className="mds-date-dow" scope="col" abbr={d}>{d}</th>)}</tr>
              </thead>
              <tbody>
                {weeks.map((week, wi) => (
                  <tr key={wi} role="row">
                    {week.map((d) => {
                      const other = d.getMonth() !== cursor.getMonth();
                      const isSel = sameDay(d, selected);
                      const isCursor = sameDay(d, cursor);
                      const blocked = isBlocked(d);
                      return (
                        <td key={toISO(d)} role="gridcell" aria-selected={isSel}>
                          <button
                            type="button"
                            ref={isCursor ? dayRef : undefined}
                            tabIndex={isCursor ? 0 : -1}
                            disabled={blocked}
                            aria-current={sameDay(d, today) ? "date" : undefined}
                            aria-label={longDate(d)}
                            className={`mds-date-day${other ? " mds-date-day--other" : ""}${isSel ? " mds-date-day--selected" : ""}${sameDay(d, today) && !isSel ? " mds-date-day--today" : ""}`}
                            onFocus={() => { if (!sameDay(d, cursor)) setCursor(d); }}
                            onClick={() => pick(d)}
                          >{d.getDate()}</button>
                        </td>
                      );
                    })}
                  </tr>
                ))}
              </tbody>
            </table>
            {(showToday || clearable) && (
              <div className="mds-date-foot">
                {showToday && <button type="button" disabled={isBlocked(today)} onClick={() => pick(today)}>Today</button>}
                {clearable && <button type="button" onClick={() => { onChange?.(""); setDraft(""); close(); }}>Clear</button>}
              </div>
            )}
          </div>
        )}
      </div>
      {error && <div className="mds-date-error" id={errorId}>{error}</div>}
      <span className="mds-date-sr" role="status" aria-live="polite">{open ? monthTitle(cursor) : ""}</span>
    </div>
  );
}

DatePicker.displayName = "DatePicker";
