// Meridian ActionGuard — one contract for "may this consequential action proceed, and if not, why".
// Guards are visible BEFORE the attempt: a user who cannot post sees the reason, the remediation and
// the authority required, rather than a dead disabled button or a hidden action. Blocking guards stay
// focusable so their presence and state remain discoverable to keyboard and screen-reader users.
import React from "react";

const STATE = {
  pending:   { label: "Awaiting input", glyph: "·", rank: -1 },
  satisfied: { label: "Satisfied", glyph: "✓", rank: 0 },
  warning:   { label: "Warning",   glyph: "!", rank: 1 },
  blocking:  { label: "Blocking",  glyph: "▲", rank: 2 },
};
const meta = s => STATE[s] || { label: s || "Unknown", glyph: "·", rank: 1 };
export function summarizeGuards(guards) {
  const g = guards || [];
  const blocking = g.filter(x => x.state === "blocking"), warning = g.filter(x => x.state === "warning");
  const pending = g.filter(x => x.state === "pending" || !Object.prototype.hasOwnProperty.call(STATE, x.state));
  return {
    total: g.length, satisfied: g.filter(x => x.state === "satisfied").length,
    warning: warning.length, blocking: blocking.length, pending: pending.length,
    blocked: blocking.length > 0, firstBlocking: blocking[0] || null,
    // A pending guard never blocks — the action is gated by its own validity — but it must not
    // be counted as satisfied either: "all clear" before anything was checked is a false receipt.
    sentence: blocking.length ? `${blocking.length} blocking ${blocking.length === 1 ? "guard" : "guards"}`
      : warning.length ? `${warning.length} ${warning.length === 1 ? "warning" : "warnings"}`
      : pending.length ? `${pending.length} ${pending.length === 1 ? "check" : "checks"} awaiting input`
      : g.length ? "All guards satisfied" : "No guards declared",
  };
}

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return; injected = true;
  const el = document.createElement("style"); el.setAttribute("data-mds", "action-guard");
  el.textContent = `
.mds-guards{font-family:var(--font-body);color:var(--text-primary,#20252B);display:grid;gap:8px}
.mds-guards__hd{display:flex;align-items:baseline;justify-content:space-between;gap:12px}
.mds-guards__lbl{font:600 9px var(--font-data,monospace);text-transform:uppercase;letter-spacing:.06em;color:var(--text-muted,#606870)}
.mds-guards__sum{font:11px var(--font-body);color:var(--text-secondary,#535C65)}
.mds-guards__list{display:grid;gap:0;margin:0;padding:0;list-style:none}
.mds-guard{display:grid;grid-template-columns:16px 1fr;gap:9px;padding:7px 9px 7px 7px;border-bottom:1px solid var(--border-divider,#E2DED5);min-height:var(--row-review,44px);align-content:center}
.mds-guard:last-child{border-bottom:0}
.mds-guard__g{font:11px/1.5 var(--font-data,monospace);text-align:center;color:var(--text-secondary,#535C65)}
.mds-guard__lbl{font:600 12px/1.45 var(--font-body)}
.mds-guard__why{display:block;font:11px/1.5 var(--font-body);color:var(--text-secondary,#535C65);max-width:60ch;text-wrap:pretty}
.mds-guard__meta{display:block;font:10px/1.5 var(--font-data,monospace);color:var(--text-muted,#606870)}
.mds-guard[data-state="blocking"]{background:var(--red-a10,rgba(181,75,66,.08));box-shadow:inset 2px 0 0 var(--red,#B54B42)}
.mds-guard[data-state="blocking"] .mds-guard__g{color:var(--red-dim,#873832)}
.mds-guard[data-state="warning"]{background:var(--orange-a10,rgba(152,103,23,.08));box-shadow:inset 2px 0 0 var(--orange,#986717)}
.mds-guard[data-state="warning"] .mds-guard__g{color:var(--orange-dim,#724D11)}
.mds-guard[data-state="satisfied"] .mds-guard__g{color:var(--green-dim,#23583F)}
.mds-guard[data-state="pending"] .mds-guard__g{color:var(--text-disabled,#A4ABB2)}
.mds-guard[data-state="pending"] .mds-guard__lbl{color:var(--text-secondary,#535C65)}
.mds-guard__fix{margin-top:5px;display:inline-flex;align-items:center;gap:6px;min-height:28px;padding:4px 10px;font:600 11px var(--font-body);cursor:pointer;background:var(--bg-light,#F8F7F3);border:1px solid var(--border-strong,#AAA49A);color:var(--text-primary,#20252B)}
.mds-guard__fix:hover{background:var(--bg-hover,#EDEFF0)}
.mds-guard__fix:focus-visible{outline:2px solid var(--interaction-focus,var(--focus,#2D6FB3));outline-offset:1px}
.mds-guard__role{font:10px var(--font-data,monospace);color:var(--text-muted,#606870)}
.mds-avail{font-family:var(--font-body);border:1px solid var(--border,#D2CDC3);background:var(--bg-light,#F8F7F3);padding:11px 12px;display:grid;gap:7px;max-width:44ch}
.mds-avail__act{font:600 13px/1.3 var(--font-body);color:var(--text-primary,#20252B)}
.mds-avail__st{font:600 9px var(--font-data,monospace);text-transform:uppercase;letter-spacing:.06em;color:var(--red-dim,#873832)}
.mds-avail__st[data-available="true"]{color:var(--green-dim,#23583F)}
.mds-avail dl{display:grid;grid-template-columns:auto 1fr;gap:2px 12px;margin:0;font:11px/1.55 var(--font-body)}
.mds-avail dt{font:600 9px var(--font-data,monospace);text-transform:uppercase;letter-spacing:.05em;color:var(--text-muted,#606870);padding-top:2px}
.mds-avail dd{margin:0;text-wrap:pretty}
`;
  document.head.appendChild(el);
}

export function ActionGuardList({ guards = [], label = "Guards", showSummary = true, onRemediate, sort = true, className = "", ...rest }) {
  inject();
  const list = sort ? [...guards].sort((a, b) => meta(b.state).rank - meta(a.state).rank) : guards;
  const s = summarizeGuards(guards);
  return <section className={`mds-guards${className ? " " + className : ""}`} aria-label={`${label} — ${s.sentence}`} {...rest}>
    <header className="mds-guards__hd"><span className="mds-guards__lbl">{label}</span>
      {showSummary ? <span className="mds-guards__sum">{s.sentence}</span> : null}</header>
    <ul className="mds-guards__list">{list.map(g => {
      const m = meta(g.state);
      return <li key={g.id} className="mds-guard" data-state={g.state} data-guard={g.id}>
        <span className="mds-guard__g" aria-hidden="true">{m.glyph}</span>
        <span><span className="mds-guard__lbl">{g.label}</span>
          <span className="mds-guard__meta">{m.label}{g.policyRef ? ` · ${g.policyRef}` : ""}{g.evidenceRefs && g.evidenceRefs.length ? ` · ${g.evidenceRefs.length} evidence ${g.evidenceRefs.length === 1 ? "item" : "items"}` : ""}</span>
          {g.reason ? <span className="mds-guard__why">{g.reason}</span> : null}
          {g.remediation ? <button type="button" className="mds-guard__fix" disabled={!onRemediate} title={onRemediate?undefined:"Remediation is unavailable in this view"}
            onClick={() => onRemediate && onRemediate(g.remediation, g)}>{g.remediation.label}
            {g.remediation.requiredRole ? <span className="mds-guard__role">{g.remediation.requiredRole}</span> : null}</button> : null}
        </span></li>;
    })}</ul>
  </section>;
}

// Namespace export: the guard contract as one reachable object.
export const ActionGuard = {
  states: STATE,
  summarize: summarizeGuards,
  List: props => <ActionGuardList {...props} />,
  Availability: props => <ActionAvailability {...props} />,
};

export function ActionAvailability({ action, guards = [], available, reason, requiredRole, onRemediate, className = "", ...rest }) {
  inject();
  const s = summarizeGuards(guards);
  const ok = available !== undefined ? available : !s.blocked;
  const blocker = s.firstBlocking;
  const why = reason || (blocker ? blocker.reason || blocker.label : null);
  const fix = blocker && blocker.remediation ? blocker.remediation : null;
  const role = requiredRole || (fix && fix.requiredRole) || null;
  return <div className={`mds-avail${className ? " " + className : ""}`} data-available={ok ? "true" : "false"} {...rest}>
    <span className="mds-avail__act">{action}</span>
    <span className="mds-avail__st" data-available={ok ? "true" : "false"}>{ok ? "Available" : "Unavailable"}</span>
    {!ok ? <dl>
      {why ? <><dt>Reason</dt><dd>{why}</dd></> : null}
      {blocker && blocker.policyRef ? <><dt>Policy</dt><dd>{blocker.policyRef}</dd></> : null}
      {fix ? <><dt>Resolution</dt><dd><button type="button" className="mds-guard__fix" style={{ marginTop: 0 }} disabled={!onRemediate} title={onRemediate?undefined:"Remediation is unavailable in this view"}
        onClick={() => onRemediate && onRemediate(fix, blocker)}>{fix.label}</button></dd></> : null}
      {role ? <><dt>Authority</dt><dd>{role}</dd></> : null}
    </dl> : null}
  </div>;
}
