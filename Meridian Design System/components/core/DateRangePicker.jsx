// Meridian DateRangePicker — start/end selection over two months. Functional only, no transitions.
// Local calendar dates throughout (see ./localDate) — the previous build ran dates through
// toISOString(), shifting every range back a day for users west of UTC.
// Preset ranges (MTD/QTD/YTD…) sit beside the calendar because that is how a desk actually picks.
import React from "react";
import { calendarDate, toISO, fromISO, addDays, addMonths, sameDay, parseLoose, dowLabels, longDate, monthTitle, weeksFor, rangePresets } from "./localDate.js";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-dr-label{display:block;font-family:var(--font-body);font-size:10px;font-weight:600;
  font-variant:all-small-caps;letter-spacing:.03em;color:var(--text-muted,#606870);margin-bottom:5px;}
.mds-dr-wrap{position:relative;display:block;width:100%;}
.mds-dr-field{display:flex;align-items:center;width:100%;box-sizing:border-box;height:32px;
  border:1px solid var(--border,#D2CDC3);background:var(--bg-light,#F8F7F3);}
.mds-dr-field:hover{border-color:var(--border-hover,#BEB8AD);}
.mds-dr-field:focus-within{border-color:var(--border-focus,#2D6FB3);outline:var(--focus-ring);outline-offset:var(--focus-ring-offset);}
.mds-dr-field--error{border-color:var(--red,#B54B42);}
.mds-dr-field--disabled{background:var(--bg-medium,#EBE8E1);opacity:.6;}
.mds-dr-input{flex:1;min-width:0;height:100%;padding:7px 10px;border:none;background:transparent;
  color:var(--text-primary,#20252B);font-family:var(--font-data);font-size:13px;outline:none;
  font-variant-numeric:tabular-nums;text-align:left;cursor:pointer;display:flex;align-items:center;}
.mds-dr-input--empty{color:var(--text-muted,#606870);}
.mds-dr-btn{flex:0 0 auto;height:100%;padding:0 9px;border:0;background:none;margin:0;
  color:var(--text-muted,#606870);font-size:12px;cursor:pointer;display:inline-flex;align-items:center;}
.mds-dr-btn:hover{color:var(--text-primary,#20252B);}
.mds-dr-btn:focus-visible{outline:var(--focus-ring);outline-offset:-2px;}
.mds-dr-cal{position:absolute;top:calc(100% + 4px);left:0;z-index:var(--z-dropdown,100);
  background:var(--bg-light,#F8F7F3);border:1px solid var(--border,#D2CDC3);display:flex;
  box-shadow:var(--shadow-menu,0 4px 12px rgba(17,23,29,.10));border-radius:var(--radius-overlay,6px);}
.mds-dr-cal--above{top:auto;bottom:calc(100% + 4px);}
.mds-dr-cal--right{left:auto;right:0;}
.mds-dr-presets{flex:0 0 auto;display:flex;flex-direction:column;gap:2px;padding:10px;
  border-right:1px solid var(--border-divider,#E2DED5);min-width:96px;}
.mds-dr-preset{text-align:left;padding:4px 8px;border:1px solid transparent;background:none;cursor:pointer;
  font-family:var(--font-body);font-size:11px;color:var(--text-secondary,#535C65);}
.mds-dr-preset:hover{background:var(--bg-hover,#EDEFF0);color:var(--text-primary,#20252B);}
.mds-dr-preset:focus-visible{outline:var(--focus-ring);outline-offset:-1px;}
.mds-dr-preset--on{background:var(--bg-active,#E7EDF2);color:var(--interaction-selected,#496C8F);font-weight:600;}
.mds-dr-months{display:flex;gap:16px;padding:10px;}
.mds-dr-month{flex:0 0 auto;}
.mds-dr-header{display:flex;justify-content:space-between;align-items:center;gap:6px;margin-bottom:8px;height:24px;}
.mds-dr-title{font-family:var(--font-body);font-size:12px;font-weight:600;color:var(--text-primary,#20252B);
  flex:1;text-align:center;white-space:nowrap;}
.mds-dr-nav{flex:0 0 auto;width:24px;height:24px;display:inline-flex;align-items:center;justify-content:center;
  cursor:pointer;border:1px solid var(--border,#D2CDC3);background:var(--bg-light,#F8F7F3);
  color:var(--text-secondary,#535C65);font-size:11px;padding:0;}
.mds-dr-nav:hover:not(:disabled){background:var(--bg-hover,#EDEFF0);color:var(--text-primary,#20252B);}
.mds-dr-nav:focus-visible{outline:var(--focus-ring);outline-offset:1px;}
.mds-dr-nav:disabled{opacity:.4;cursor:not-allowed;}
.mds-dr-nav--ghost{visibility:hidden;}
.mds-dr-grid{border-collapse:collapse;}
.mds-dr-dow{font-family:var(--font-body);font-size:10px;font-weight:600;text-align:center;
  color:var(--text-muted,#606870);padding:0 0 4px;width:30px;}
.mds-dr-cell{padding:0;}
.mds-dr-day{width:30px;height:28px;display:flex;align-items:center;justify-content:center;padding:0;
  border:1px solid transparent;background:transparent;cursor:pointer;font-family:var(--font-data);
  font-size:12px;font-variant-numeric:tabular-nums;color:var(--text-primary,#20252B);}
.mds-dr-day:hover:not(:disabled){background:var(--bg-hover,#EDEFF0);}
.mds-dr-day:focus-visible{outline:var(--focus-ring);outline-offset:-1px;}
.mds-dr-day--other{color:var(--text-disabled,#A4ABB2);}
.mds-dr-day--today{border-color:var(--interaction-selected,#496C8F);font-weight:600;}
.mds-dr-cell--inrange{background:var(--blue-a10,rgba(47,100,138,.10));}
.mds-dr-cell--preview{background:var(--bg-hover,#EDEFF0);}
.mds-dr-day--edge{background:var(--interaction-selected,#496C8F);color:var(--text-on-accent,#0D1217);
  border-color:var(--interaction-selected,#496C8F);font-weight:600;}
.mds-dr-day:disabled{color:var(--text-disabled,#A4ABB2);cursor:not-allowed;text-decoration:line-through;}
.mds-dr-foot{display:flex;align-items:center;justify-content:space-between;gap:8px;padding:8px 10px;
  border-top:1px solid var(--border-divider,#E2DED5);}
.mds-dr-hint{font-family:var(--font-data);font-size:10px;color:var(--text-muted,#606870);}
.mds-dr-foot button{padding:4px 10px;border:1px solid var(--border,#D2CDC3);background:var(--bg-light,#F8F7F3);
  font-family:var(--font-body);font-size:11px;color:var(--text-secondary,#535C65);cursor:pointer;}
.mds-dr-foot button:hover{background:var(--bg-hover,#EDEFF0);color:var(--text-primary,#20252B);}
.mds-dr-error{font-family:var(--font-body);font-size:11px;color:var(--red-dim,#883832);margin-top:5px;}
.mds-dr-sr{position:absolute;width:1px;height:1px;overflow:hidden;clip:rect(0 0 0 0);white-space:nowrap;}
@media (max-width:720px){.mds-dr-months .mds-dr-month:last-child{display:none;}}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "daterangepicker");
  el.textContent = css;
  document.head.appendChild(el);
}

let uid = 0;
function useUid() {
  if (React.useId) return React.useId().replace(/:/g, "");
  return React.useMemo(() => "mdsdr" + ++uid, []);
}

const short = (iso) => {
  const d = fromISO(iso);
  return d ? d.toLocaleDateString(undefined, { month: "short", day: "numeric", year: "numeric" }) : null;
};

export function DateRangePicker({
  label,
  start,
  end,
  onChange,
  min,
  max,
  disabledDate,
  presets,
  showPresets = true,
  months = 2,
  weekStartsOn = 0,
  disabled = false,
  error,
  clearable = true,
  id,
  "aria-label": ariaLabel,
  ...rest
}) {
  inject();
  const auto = useUid();
  const base = id || auto;
  const errorId = `${base}-error`;
  const {
    "aria-labelledby": labelledBy, "aria-describedby": describedBy,
    "aria-invalid": invalid, "aria-errormessage": errorMessage,
    "aria-required": ariaRequired, ...containerProps
  } = rest;
  const feedbackIds = [...new Set([...(describedBy || "").split(/\s+/).filter(Boolean), ...(error ? [errorId] : [])])].join(" ") || undefined;

  const startD = React.useMemo(() => fromISO(start), [start]);
  const endD = React.useMemo(() => fromISO(end), [end]);
  const minD = React.useMemo(() => fromISO(min), [min]);
  const maxD = React.useMemo(() => fromISO(max), [max]);

  const [open, setOpen] = React.useState(false);
  const [cursor, setCursor] = React.useState(() => startD || new Date());
  const [anchor, setAnchor] = React.useState(null); // first click of an in-progress range
  const [hover, setHover] = React.useState(null);
  React.useEffect(() => { if (disabled) { setOpen(false); setAnchor(null); setHover(null); } }, [disabled]);
  const [placement, setPlacement] = React.useState("");
  const rootRef = React.useRef(null);
  const calRef = React.useRef(null);
  const triggerRef = React.useRef(null);
  const dayRef = React.useRef(null);
  const [focusDay, setFocusDay] = React.useState(() => startD || new Date());

  const presetList = React.useMemo(() => presets || rangePresets(), [presets]);

  React.useEffect(() => { if (startD) { setCursor(startD); setFocusDay(startD); } }, [start]);

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

  React.useLayoutEffect(() => {
    if (!open || !calRef.current) return;
    const r = calRef.current.getBoundingClientRect();
    let p = "";
    if (r.bottom > window.innerHeight && r.top - r.height > 0) p += " mds-dr-cal--above";
    if (r.right > window.innerWidth) p += " mds-dr-cal--right";
    setPlacement(p);
  }, [open]);

  React.useEffect(() => { if (open) dayRef.current?.focus(); }, [open, focusDay]);

  const close = (refocus = true) => {
    setOpen(false);
    setAnchor(null);
    setHover(null);
    setPlacement("");
    if (refocus) triggerRef.current?.focus();
  };

  // Two clicks make a range; the first click sets the anchor, the second closes it out. Clicking
  // a completed range starts a new one rather than silently extending the old.
  const pick = (d) => {
    if (disabled || isBlocked(d)) return;
    const iso = toISO(d);
    if (anchor == null) { setAnchor(iso); onChange?.({ start: iso, end: null }); return; }
    if (iso < anchor) onChange?.({ start: iso, end: anchor });
    else onChange?.({ start: anchor, end: iso });
    setAnchor(null);
    setHover(null);
    close();
  };

  const presetBlocked = (p) => { const a = fromISO(p.start), b = fromISO(p.end); return disabled || !a || !b || isBlocked(a) || isBlocked(b); };
  const applyPreset = (p) => { if (presetBlocked(p)) return; onChange?.({ start: p.start, end: p.end }); setAnchor(null); close(); };
  const activePreset = presetList.find((p) => p.start === start && p.end === end);

  const onGridKey = (e) => {
    const k = e.key;
    if (k === "Escape") { e.preventDefault(); e.stopPropagation(); close(); return; }
    // Presets and month-navigation buttons keep their native Enter/Space behavior.
    if (!e.target.classList.contains("mds-dr-day")) return;
    let next = null;
    if (k === "ArrowLeft") next = addDays(focusDay, -1);
    else if (k === "ArrowRight") next = addDays(focusDay, 1);
    else if (k === "ArrowUp") next = addDays(focusDay, -7);
    else if (k === "ArrowDown") next = addDays(focusDay, 7);
    else if (k === "Home") next = addDays(focusDay, -((focusDay.getDay() - weekStartsOn + 7) % 7));
    else if (k === "End") next = addDays(focusDay, 6 - ((focusDay.getDay() - weekStartsOn + 7) % 7));
    else if (k === "PageUp") next = addMonths(focusDay, e.shiftKey ? -12 : -1);
    else if (k === "PageDown") next = addMonths(focusDay, e.shiftKey ? 12 : 1);
    else if (k === "Enter" || k === " ") return; // Native day button commits its own date.
    else if (k === "Escape") { e.preventDefault(); e.stopPropagation(); close(); return; }
    else if (k === "Tab") { close(false); return; }
    if (next) {
      e.preventDefault();
      setFocusDay(next);
      setHover(toISO(next));
      const nextMonth = calendarDate(next.getFullYear(), next.getMonth(), 1);
      const firstMonth = calendarDate(cursor.getFullYear(), cursor.getMonth(), 1);
      const lastMonth = addMonths(firstMonth, months - 1);
      if (nextMonth < firstMonth) setCursor(nextMonth);
      else if (nextMonth > lastMonth) setCursor(addMonths(nextMonth, -(months - 1)));
    }
  };

  // While a range is in progress, shade from the anchor to whatever the pointer/keyboard is on.
  const lo = anchor && hover ? (hover < anchor ? hover : anchor) : start;
  const hi = anchor && hover ? (hover < anchor ? anchor : hover) : end;
  const previewing = !!(anchor && hover);

  const dow = React.useMemo(() => dowLabels(weekStartsOn), [weekStartsOn]);
  const today = new Date();
  const prevBlocked = minD && calendarDate(cursor.getFullYear(), cursor.getMonth(), 0) < minD;
  const nextBlocked = maxD && calendarDate(cursor.getFullYear(), cursor.getMonth() + months, 1) > maxD;
  const summary = start ? `${short(start)} — ${end ? short(end) : "…"}` : null;

  return (
    <div ref={rootRef} {...containerProps}>
      {label && <span className="mds-dr-label" id={`${base}-label`}>{label}</span>}
      <div className="mds-dr-wrap">
        <div className={`mds-dr-field${error ? " mds-dr-field--error" : ""}${disabled ? " mds-dr-field--disabled" : ""}`}>
          <button
            ref={triggerRef}
            type="button"
            id={base}
            className={`mds-dr-input${summary ? "" : " mds-dr-input--empty"}`}
            disabled={disabled}
            aria-haspopup="dialog"
            aria-expanded={open}
            aria-labelledby={labelledBy || (label ? `${base}-label ${base}` : undefined)}
            aria-label={!label && !labelledBy ? (ariaLabel || (id ? undefined : "Date range")) : undefined}
            aria-invalid={error ? true : invalid}
            aria-describedby={feedbackIds}
            aria-errormessage={errorMessage}
            aria-required={ariaRequired}
            onClick={() => (open ? close() : setOpen(true))}
          >{summary || "Select range…"}</button>
          {clearable && start && !disabled && (
            <button type="button" className="mds-dr-btn" tabIndex={-1} aria-label="Clear range"
              onClick={() => onChange?.({ start: null, end: null })}>✕</button>
          )}
          <button type="button" className="mds-dr-btn" disabled={disabled} tabIndex={-1}
            aria-hidden="true" onClick={() => (open ? close() : setOpen(true))}>▦</button>
        </div>
        {open && (
          <div ref={calRef} className={`mds-dr-cal${placement}`} role="dialog" aria-modal="false"
            aria-label={label ? `${label} range` : "Choose date range"} onKeyDown={onGridKey}>
            {showPresets && presetList.length > 0 && (
              <div className="mds-dr-presets">
                {presetList.map((p) => (
                  <button key={p.id} type="button"
                    disabled={presetBlocked(p)}
                    className={`mds-dr-preset${activePreset?.id === p.id ? " mds-dr-preset--on" : ""}`}
                    aria-pressed={activePreset?.id === p.id}
                    onClick={() => applyPreset(p)}>{p.label}</button>
                ))}
              </div>
            )}
            <div>
              <div className="mds-dr-months" onMouseLeave={() => !anchor && setHover(null)}>
                {Array.from({ length: months }, (_, mi) => {
                  const mCursor = addMonths(cursor, mi);
                  const weeks = weeksFor(mCursor, weekStartsOn);
                  return (
                    <div key={mi} className="mds-dr-month">
                      <div className="mds-dr-header">
                        <button type="button" disabled={prevBlocked}
                          className={`mds-dr-nav${mi === 0 ? "" : " mds-dr-nav--ghost"}`}
                          aria-label="Previous month" aria-hidden={mi !== 0}
                          tabIndex={mi === 0 ? 0 : -1}
                          onClick={() => setCursor(addMonths(cursor, -1))}>‹</button>
                        <span className="mds-dr-title">{monthTitle(mCursor)}</span>
                        <button type="button" disabled={nextBlocked}
                          className={`mds-dr-nav${mi === months - 1 ? "" : " mds-dr-nav--ghost"}`}
                          aria-label="Next month" aria-hidden={mi !== months - 1}
                          tabIndex={mi === months - 1 ? 0 : -1}
                          onClick={() => setCursor(addMonths(cursor, 1))}>›</button>
                      </div>
                      <table className="mds-dr-grid" role="grid" aria-label={monthTitle(mCursor)}>
                        <thead>
                          <tr>{dow.map((d) => <th key={d} className="mds-dr-dow" scope="col">{d}</th>)}</tr>
                        </thead>
                        <tbody>
                          {weeks.map((week, wi) => (
                            <tr key={wi} role="row">
                              {week.map((d) => {
                                const iso = toISO(d);
                                const other = d.getMonth() !== mCursor.getMonth();
                                const isEdge = iso === lo || iso === hi;
                                const inRange = lo && hi && iso >= lo && iso <= hi;
                                const blocked = isBlocked(d);
                                const isFocus = sameDay(d, focusDay);
                                return (
                                  <td key={iso} role="gridcell" aria-selected={isEdge || inRange}
                                    className={`mds-dr-cell${inRange && !isEdge ? (previewing ? " mds-dr-cell--preview" : " mds-dr-cell--inrange") : ""}`}>
                                    <button
                                      type="button"
                                      ref={isFocus ? dayRef : undefined}
                                      tabIndex={isFocus ? 0 : -1}
                                      disabled={blocked}
                                      aria-current={sameDay(d, today) ? "date" : undefined}
                                      aria-label={longDate(d)}
                                      className={`mds-dr-day${other ? " mds-dr-day--other" : ""}${isEdge ? " mds-dr-day--edge" : ""}${sameDay(d, today) && !isEdge ? " mds-dr-day--today" : ""}`}
                                      onMouseEnter={() => setHover(iso)}
                                      onFocus={() => setFocusDay(d)}
                                      onClick={() => pick(d)}
                                    >{d.getDate()}</button>
                                  </td>
                                );
                              })}
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                  );
                })}
              </div>
              <div className="mds-dr-foot">
                <span className="mds-dr-hint">
                  {anchor ? "Pick the end date" : summary || "Pick the start date"}
                </span>
                {clearable && (
                  <button type="button" onClick={() => { onChange?.({ start: null, end: null }); setAnchor(null); close(); }}>Clear</button>
                )}
              </div>
            </div>
          </div>
        )}
      </div>
      {error && <div className="mds-dr-error" id={errorId}>{error}</div>}
      <span className="mds-dr-sr" role="status" aria-live="polite">
        {open ? (anchor ? `Start ${short(anchor)}. Pick the end date.` : summary || "") : ""}
      </span>
    </div>
  );
}

DateRangePicker.displayName = "DateRangePicker";
