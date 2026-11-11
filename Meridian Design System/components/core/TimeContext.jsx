// Meridian time context — the temporal contract. Meridian works with twelve kinds of time, and a
// figure is only interpretable when the surface says which ones it is stated in. This module owns
// the role ledger, the provider that declares them once per screen, the readouts that render them,
// and the coherence rules that catch a temporally impossible record.
import React from "react";

/** The role ledger. `kind` answers "whose clock is this?": an event in the world, the book's
 *  recognition of it, or an observation of a figure. `precision` drives formatting. */
export const TIME_ROLES = [
  { id: "trade", label: "Trade date", kind: "event", precision: "date", authority: "Order management", definition: "When the transaction was agreed." },
  { id: "settlement", label: "Settlement date", kind: "event", precision: "date", authority: "Custodian", definition: "When cash and security actually change hands." },
  { id: "effective", label: "Effective date", kind: "event", precision: "date", authority: "Contract", definition: "When the term, rate, or amendment starts to apply." },
  { id: "received", label: "Received at", kind: "event", precision: "instant", authority: "Ingestion", definition: "When this system first held the evidence." },
  { id: "posting", label: "Posting date", kind: "book", precision: "date", authority: "Ledger", definition: "The date the entry is recognized in the books." },
  { id: "valuation", label: "Valuation date", kind: "book", precision: "date", authority: "Pricing", definition: "The date the prices and rates are drawn from." },
  { id: "reportingPeriod", label: "Reporting period", kind: "book", precision: "period", authority: "Ledger", definition: "The period the figure is reported in." },
  { id: "fiscalPeriod", label: "Fiscal period", kind: "book", precision: "period", authority: "Entity calendar", definition: "The entity's own year and period, which need not match the calendar." },
  { id: "closeDeadline", label: "Close deadline", kind: "book", precision: "instant", authority: "Close calendar", definition: "When the period stops accepting entries." },
  { id: "asOf", label: "As of", kind: "observation", precision: "instant", authority: "Reporting", definition: "The instant the stated position is true for." },
  { id: "evidenceAt", label: "Evidence timestamp", kind: "observation", precision: "instant", authority: "Source system", definition: "When the source document or extract was produced." },
  { id: "calculatedAt", label: "Calculation timestamp", kind: "observation", precision: "instant", authority: "Calculation engine", definition: "When this figure was last computed." },
];
const BY_ID = TIME_ROLES.reduce((map, role) => { map[role.id] = role; return map; }, {});
const KINDS = [
  { id: "event", label: "In the world" },
  { id: "book", label: "In the books" },
  { id: "observation", label: "In this figure" },
];

function toDate(value) {
  if (value == null || value === "") return null;
  const d = value instanceof Date ? value : new Date(value);
  return isNaN(d.getTime()) ? null : d;
}
function pad(n) { return String(n).padStart(2, "0"); }
function fmtDate(d) { return `${d.getUTCFullYear()}-${pad(d.getUTCMonth() + 1)}-${pad(d.getUTCDate())}`; }
function fmtInstant(d) { return `${fmtDate(d)} ${pad(d.getUTCHours())}:${pad(d.getUTCMinutes())}:${pad(d.getUTCSeconds())}Z`; }
function format(value, precision) {
  if (value == null || value === "") return "—";
  if (precision === "period") return String(value);
  const d = toDate(value);
  if (!d) return String(value);
  return precision === "date" ? fmtDate(d) : fmtInstant(d);
}
function day(value) { const d = toDate(value); return d ? Date.UTC(d.getUTCFullYear(), d.getUTCMonth(), d.getUTCDate()) : null; }
function ms(value) { const d = toDate(value); return d ? d.getTime() : null; }

/** The coherence rules. Each returns `{ id, severity, message, roles }`. Severity is
 *  `error` (the record is temporally impossible), `warning` (legal but consequential), or
 *  `info` (a difference the reader must not assume away). */
export function checkTimeCoherence(time = {}, options = {}) {
  const now = options.now == null ? Date.now() : ms(options.now) ?? Date.now();
  const out = [];
  const add = (id, severity, message, roles) => out.push({ id, severity, message, roles });
  const trade = day(time.trade), settlement = day(time.settlement), effective = day(time.effective);
  const posting = day(time.posting), valuation = day(time.valuation);
  const asOf = ms(time.asOf), received = ms(time.received);
  const evidenceAt = ms(time.evidenceAt), calculatedAt = ms(time.calculatedAt);
  const deadline = ms(time.closeDeadline);

  if (trade != null && settlement != null && settlement < trade)
    add("settle-before-trade", "error", "Settlement date precedes the trade date.", ["trade", "settlement"]);
  if (calculatedAt != null && evidenceAt != null && calculatedAt < evidenceAt)
    add("calc-before-evidence", "error", "The figure was calculated before its evidence was produced — it does not reflect the current source.", ["calculatedAt", "evidenceAt"]);
  if (posting != null && time.reportingPeriod && String(time.reportingPeriod).match(/^\d{4}-\d{2}/)) {
    const p = String(time.reportingPeriod).slice(0, 7);
    const postingPeriod = `${new Date(posting).getUTCFullYear()}-${pad(new Date(posting).getUTCMonth() + 1)}`;
    if (postingPeriod !== p)
      add("posting-outside-period", "error", `Posting date falls in ${postingPeriod}, outside reporting period ${p}.`, ["posting", "reportingPeriod"]);
  }
  if (deadline != null && received != null && received > deadline)
    add("evidence-after-deadline", "warning", "Evidence arrived after the close deadline — the period was measured without it.", ["received", "closeDeadline"]);
  if (deadline != null && settlement != null && settlement > deadline)
    add("settles-after-deadline", "warning", "The trade settles after the close deadline — the period carries an unsettled position.", ["settlement", "closeDeadline"]);
  if (deadline != null && now > deadline && !options.closed)
    add("deadline-passed", "warning", "The close deadline has passed and the period is still open.", ["closeDeadline"]);
  if (asOf != null && trade != null && asOf < trade)
    add("asof-before-trade", "warning", "The as-of instant precedes the trade it reports.", ["asOf", "trade"]);
  if (valuation != null && asOf != null && valuation !== day(time.asOf))
    add("valuation-differs", "info", "Prices are drawn from a different date than the as-of instant.", ["valuation", "asOf"]);
  if (effective != null && posting != null && effective > posting)
    add("effective-after-posting", "info", "The term becomes effective after the entry is posted — expect a restatement.", ["effective", "posting"]);
  return out;
}

const Ctx = React.createContext(null);

/** Declares the screen's time roles once. Children read them by role id instead of each
 *  surface inventing its own date field. */
export function TimeContextProvider({ value, defaultValue, onChange, children }) {
  const controlled = value !== undefined;
  const [internal, setInternal] = React.useState(defaultValue || {});
  const time = controlled ? (value || {}) : internal;
  const update = React.useCallback((patch) => {
    const next = { ...time, ...(typeof patch === "function" ? patch(time) : patch) };
    if (!controlled) setInternal(next);
    onChange?.(next);
  }, [time, controlled, onChange]);
  const api = React.useMemo(() => ({
    time, update,
    reset: () => update(defaultValue || {}),
    get: (id) => time[id],
    coherence: checkTimeCoherence(time),
  }), [time, update, defaultValue]);
  return <Ctx.Provider value={api}>{children}</Ctx.Provider>;
}
export function useTimeContext() {
  const value = React.useContext(Ctx);
  if (!value) throw new Error("useTimeContext must be used inside TimeContextProvider");
  return value;
}
/** Bundle carrier — the compiler only exposes capitalized exports, so consumers of
 *  `_ds_bundle.js` reach these through this object:
 *  `const { TimeHooks } = window.<Namespace>; const { useTimeContext } = TimeHooks;` */
export const TimeHooks = { useTimeContext, checkTimeCoherence, formatTimeRole: format, TIME_ROLES };

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "time-context");
  el.textContent = `
.mds-time{display:grid;gap:10px;font-family:var(--font-body);color:var(--text-secondary,#535C65)}
.mds-time__group{display:grid;gap:3px}
.mds-time__kind{font:600 11px var(--font-body);color:var(--text-muted,#606870);margin:0 0 2px}
.mds-time__row{display:grid;grid-template-columns:minmax(96px,auto) 1fr;gap:4px 14px;align-items:baseline;padding:3px 0;border-bottom:1px solid var(--border-divider,#E2DED5)}
.mds-time__row:last-child{border-bottom:0}
.mds-time__row[data-flagged="on"]{background:var(--orange-a10,rgba(152,103,23,.1));box-shadow:inset 2px 0 0 var(--orange,#986717);padding-left:6px}
.mds-time__label{font:11px var(--font-body);color:var(--text-muted,#606870)}
.mds-time__value{font:12px var(--font-data,monospace);color:var(--text-primary,#20252B);font-variant-numeric:tabular-nums;font-weight:600;justify-self:end;text-align:right;white-space:nowrap}
.mds-time__value[data-missing="on"]{color:var(--text-disabled,#A4ABB2);font-weight:400}
.mds-time__meta{grid-column:1/-1;font:10px var(--font-data,monospace);color:var(--text-muted,#606870)}
.mds-time__co{display:grid;gap:4px;margin:0;padding:0;list-style:none}
.mds-time__issue{display:grid;grid-template-columns:auto 1fr;gap:8px;align-items:baseline;padding:6px 8px;border:1px solid var(--border,#D2CDC3);font:12px/17px var(--font-body);text-wrap:pretty}
.mds-time__issue[data-severity="error"]{border-color:var(--red,#B54B42);background:var(--red-a10,rgba(181,75,66,.1))}
.mds-time__issue[data-severity="warning"]{border-color:var(--orange,#986717);background:var(--orange-a10,rgba(152,103,23,.1))}
.mds-time__issue[data-severity="info"]{background:var(--card-surface,#F8F7F3)}
.mds-time__sev{font:600 10px var(--font-data,monospace);letter-spacing:.05em;text-transform:uppercase;color:var(--text-muted,#606870)}
.mds-time__issue[data-severity="error"] .mds-time__sev{color:var(--red-dim,#883832)}
.mds-time__issue[data-severity="warning"] .mds-time__sev{color:var(--orange,#986717)}
.mds-time__ok{font:12px var(--font-body);color:var(--text-muted,#606870);margin:0}
.mds-timerole{display:inline-grid;gap:1px}
.mds-timerole__l{font:11px var(--font-body);color:var(--text-muted,#606870)}
.mds-timerole__v{font:12px var(--font-data,monospace);font-weight:600;color:var(--text-primary,#20252B);font-variant-numeric:tabular-nums;white-space:nowrap}
`;
  document.head.appendChild(el);
}

/** One role, rendered with its name. Reads the provider when `value` is omitted. */
export function TimeRole({ role, value, label, showLabel = true, className = "", ...rest }) {
  inject();
  let provided = null;
  try { provided = React.useContext(Ctx); } catch { /* used without a provider */ }
  const meta = BY_ID[role] || { label: role, precision: "instant" };
  const v = value !== undefined ? value : provided?.time?.[role];
  const text = format(v, meta.precision);
  return <span className={`mds-timerole${className ? " " + className : ""}`} data-time-role={role} {...rest}>
    {showLabel ? <span className="mds-timerole__l">{label ?? meta.label}</span> : null}
    <span className="mds-timerole__v" title={meta.definition}>{text}</span>
  </span>;
}

/** The screen's temporal declaration: the roles that apply, grouped by whose clock they are,
 *  with the coherence rules stated underneath. Omit `roles` to render every role the context
 *  actually carries. */
export function TimeContextPanel({
  value, roles, coherence = true, showMissing = false, sources, title, className = "", ...rest
}) {
  inject();
  let provided = null;
  try { provided = React.useContext(Ctx); } catch { /* used without a provider */ }
  const time = { ...(provided?.time || {}), ...(value || {}) };
  const wanted = roles && roles.length ? roles : TIME_ROLES.map((r) => r.id).filter((id) => time[id] != null);
  const issues = coherence ? checkTimeCoherence(time) : [];
  const flagged = new Set(issues.filter((i) => i.severity !== "info").flatMap((i) => i.roles));
  const groups = KINDS.map((kind) => ({
    ...kind,
    rows: wanted.map((id) => BY_ID[id]).filter((r) => r && r.kind === kind.id && (showMissing || time[r.id] != null)),
  })).filter((g) => g.rows.length);

  return <div className={`mds-time${className ? " " + className : ""}`} {...rest}>
    {title ? <h3 className="mds-time__kind" style={{ fontSize: 12, color: "var(--text-primary,#20252B)" }}>{title}</h3> : null}
    {groups.map((group) => <div key={group.id} className="mds-time__group">
      <h4 className="mds-time__kind">{group.label}</h4>
      {group.rows.map((role) => {
        const raw = time[role.id];
        return <div key={role.id} className="mds-time__row" data-time-role={role.id} data-flagged={flagged.has(role.id) ? "on" : "off"}>
          <span className="mds-time__label" title={role.definition}>{role.label}</span>
          <span className="mds-time__value" data-missing={raw == null ? "on" : "off"}>{format(raw, role.precision)}</span>
          {sources && sources[role.id] ? <span className="mds-time__meta">{sources[role.id]}</span> : null}
        </div>;
      })}
    </div>)}
    {coherence ? (issues.length
      ? <ul className="mds-time__co">
          {issues.map((issue) => <li key={issue.id} className="mds-time__issue" data-severity={issue.severity}>
            <span className="mds-time__sev">{issue.severity}</span>
            <span>{issue.message}</span>
          </li>)}
        </ul>
      : <p className="mds-time__ok">Dates are coherent — no ordering, period, or staleness conflict.</p>) : null}
  </div>;
}

/** Module carrier — the whole contract in one reference, for consumers that would rather destructure
 *  once: `const { TimeContext } = window.<Namespace>; <TimeContext.Provider value={time}>`. */
export const TimeContext = {
  Provider: TimeContextProvider,
  Panel: TimeContextPanel,
  Role: TimeRole,
  ROLES: TIME_ROLES,
  check: checkTimeCoherence,
  use: useTimeContext,
  format,
};
