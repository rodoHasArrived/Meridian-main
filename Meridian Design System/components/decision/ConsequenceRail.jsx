// Meridian ConsequenceRail — what changes if the proposed action proceeds.
//
// The counterpart to EvidenceRail: evidence looks backwards at why a value is what it is, this
// looks forwards at what a commit would do. Reversibility is stated FIRST, because it is the one
// fact that decides how much of the rest matters. Impacts are grouped by who feels them, and the
// guards that would block the attempt are listed with the consequence, not hidden behind a
// disabled button. Reversibility classes come from DecisionFlow — this rail declares no vocabulary
// of its own (guidelines/CONFORMANCE.md › C-15).
import React from "react";

export const CONSEQUENCE_KINDS = [
  { id: "financial", label: "Financial impact", noun: "value change" },
  { id: "accounting", label: "Accounting", noun: "posting" },
  { id: "controls", label: "Controls", noun: "control" },
  { id: "reports", label: "Reports", noun: "report" },
  { id: "approvals", label: "Approvals", noun: "approval" },
];

const REVERSIBILITY = {
  reversible: { label: "Reversible", note: "Can be undone without a compensating entry.", tone: "neutral" },
  compensatable: { label: "Compensatable", note: "Cannot be undone; requires a compensating entry.", tone: "info" },
  supersedable: { label: "Supersedable", note: "Stands until a later version replaces it.", tone: "warn" },
  irreversible: { label: "Irreversible", note: "Cannot be undone, compensated, or superseded.", tone: "danger" },
};

const GUARD_TONE = { blocking: "danger", warning: "warn", pending: "info", satisfied: "ok" };

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "consequence-rail");
  el.textContent = `
.mds-crail{display:flex;flex-direction:column;min-height:0;min-width:0;border:1px solid var(--border,#D2CDC3);background:var(--surface-plate,var(--bg-light,#F8F7F3));font-family:var(--font-body);color:var(--text-secondary,#535C65)}
.mds-crail__head{padding:8px 11px;border-bottom:1px solid var(--border-divider,#E2DED5);background:var(--bg-medium,#EBE8E1)}
.mds-crail__eyebrow{font:600 9px var(--font-data,monospace);letter-spacing:.07em;text-transform:uppercase;color:var(--text-muted,#606870)}
.mds-crail__title{margin:1px 0 0;font-size:13px;font-weight:600;color:var(--text-primary,#20252B)}
.mds-crail__prop{margin-top:2px;font:10px var(--font-data,monospace);font-variant-numeric:tabular-nums;color:var(--text-muted,#606870)}
.mds-crail__rev{display:grid;gap:1px;padding:7px 11px;border-bottom:1px solid var(--border-divider,#E2DED5);border-left:3px solid var(--border-strong,#AAA49A);background:var(--surface-well,var(--card-surface-raised,#FCFBF8))}
.mds-crail__rev[data-tone="info"]{border-left-color:var(--semantic-info,#2F648A)}
.mds-crail__rev[data-tone="warn"]{border-left-color:var(--orange,#986717)}
.mds-crail__rev[data-tone="danger"]{border-left-color:var(--red,#B54B42)}
.mds-crail__revlabel{font:600 11px var(--font-body);color:var(--text-primary,#20252B)}
.mds-crail__revnote{font:10px/15px var(--font-body);color:var(--text-muted,#606870)}
.mds-crail__body{flex:1;min-height:0;overflow:auto}
.mds-crail__group{border-bottom:1px solid var(--border-divider,#E2DED5)}
.mds-crail__glabel{display:flex;flex-wrap:wrap;align-items:baseline;justify-content:space-between;gap:2px 8px;padding:6px 11px 3px}
.mds-crail__gname{font:600 9px var(--font-data,monospace);letter-spacing:.06em;text-transform:uppercase;color:var(--text-muted,#606870)}
.mds-crail__gcount{font:10px var(--font-data,monospace);font-variant-numeric:tabular-nums;color:var(--text-muted,#606870)}
.mds-crail__rows{margin:0;padding:0;list-style:none}
.mds-crail__row{display:grid;grid-template-columns:1fr max-content;gap:8px;align-items:baseline;width:100%;min-height:var(--table-row-height,28px);padding:3px 11px;border:0;border-left:2px solid transparent;background:none;font:inherit;color:inherit;text-align:left}
.mds-crail__row--action{cursor:pointer;transition:background-color 120ms cubic-bezier(.2,0,0,1),border-color 120ms cubic-bezier(.2,0,0,1)}
.mds-crail__row--action:hover{background:var(--bg-hover,#EDEFF0)}
.mds-crail__row--action:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:-2px}
.mds-crail__row[data-selected="true"]{border-left-color:var(--interaction-selected,#496C8F);background:var(--interaction-selected-surface,#E7EDF2)}
.mds-crail__rlabel{font-size:11px;color:var(--text-primary,#20252B);min-width:0;overflow-wrap:anywhere}
.mds-crail__reffect{display:block;font:9px/13px var(--font-body);color:var(--text-muted,#606870)}
.mds-crail__rval{font:11px var(--font-data,monospace);font-variant-numeric:tabular-nums;text-align:right;color:var(--text-primary,#20252B);white-space:nowrap}
.mds-crail__rval[data-sign="up"]{color:var(--green-dim,#245C43)}
.mds-crail__rval[data-sign="down"]{color:var(--red-dim,#873832)}
.mds-crail__guards{padding:6px 11px;border-top:1px solid var(--border-strong,#AAA49A);background:var(--surface-well,var(--card-surface-raised,#FCFBF8))}
.mds-crail__glist{margin:3px 0 0;padding:0;list-style:none;display:grid;gap:2px}
.mds-crail__guard{display:grid;grid-template-columns:max-content 1fr;gap:6px;align-items:baseline;font-size:10px}
.mds-crail__gmark{font:600 10px var(--font-data,monospace);color:var(--text-muted,#606870)}
.mds-crail__guard[data-tone="danger"] .mds-crail__gmark{color:var(--red-dim,#873832)}
.mds-crail__guard[data-tone="warn"] .mds-crail__gmark{color:var(--orange-dim,#725012)}
.mds-crail__guard[data-tone="ok"] .mds-crail__gmark{color:var(--green-dim,#245C43)}
.mds-crail__empty{padding:12px 11px;font-size:11px;color:var(--text-muted,#606870)}
@media print{.mds-crail__body{overflow:visible}}
`;
  document.head.appendChild(el);
}

const sign = (n) => (typeof n === "number" ? (n > 0 ? "up" : n < 0 ? "down" : undefined) : undefined);

export function ConsequenceRail({
  object, proposal, consequences = {}, guards = [],
  title = "Consequence", onInspect, selectedId,
  dense = false, className = "", ...rest
}) {
  inject();
  const uid = React.useId();
  const rev = consequences.reversibility ? REVERSIBILITY[consequences.reversibility] || { label: String(consequences.reversibility), note: "", tone: "neutral" } : null;
  const groups = CONSEQUENCE_KINDS.map((k) => ({ kind: k, items: Array.isArray(consequences[k.id]) ? consequences[k.id] : [] })).filter((g) => g.items.length);
  const tally = guards.reduce((a, g) => { const s = g && g.status ? g.status : "satisfied"; a[s] = (a[s] || 0) + 1; return a; }, {});
  const blocking = tally.blocking || 0;
  // "Guards satisfied" while one is a warning and another is pending is the kind of reassuring
  // summary this system exists to refuse. Report the worst state, and count what is still open.
  const outstanding = blocking + (tally.warning || 0) + (tally.pending || 0);
  const guardLabel = blocking
    ? `${blocking} blocking ${blocking === 1 ? "guard" : "guards"}${outstanding > blocking ? ` · ${outstanding - blocking} more outstanding` : ""}`
    : outstanding
      ? `${outstanding} ${outstanding === 1 ? "guard" : "guards"} outstanding`
      : `All ${guards.length} guards satisfied`;

  return <section className={`mds-crail${dense ? " mds-crail--dense" : ""}${className ? " " + className : ""}`} aria-labelledby={`${uid}-t`} {...rest}>
    <header className="mds-crail__head">
      <div className="mds-crail__eyebrow">If this proceeds</div>
      <h3 className="mds-crail__title" id={`${uid}-t`}>{title}</h3>
      {proposal || object ? <div className="mds-crail__prop">{[
        proposal && proposal.id,
        proposal && proposal.changes ? `${proposal.changes.length} field ${proposal.changes.length === 1 ? "change" : "changes"}` : null,
        object && object.type, object && object.id,
        object && object.version != null ? `v${object.version}` : null,
      ].filter(Boolean).join(" · ")}</div> : null}
    </header>

    {rev ? <div className="mds-crail__rev" data-tone={rev.tone}>
      <span className="mds-crail__revlabel">{rev.label}</span>
      <span className="mds-crail__revnote">{rev.note}</span>
    </div> : null}

    <div className="mds-crail__body">
      {groups.length ? groups.map((g) => <div className="mds-crail__group" key={g.kind.id}>
        <div className="mds-crail__glabel">
          <span className="mds-crail__gname">{g.kind.label}</span>
          <span className="mds-crail__gcount">{g.items.length} {g.items.length === 1 ? g.kind.noun : `${g.kind.noun}s`}</span>
        </div>
        <ul className="mds-crail__rows">
          {g.items.map((item, i) => {
            const id = item.id || `${g.kind.id}-${i}`;
            const interactive = !!onInspect;
            const Tag = interactive ? "button" : "div";
            const amount = item.delta != null ? item.delta : item.amount;
            return <li key={id} aria-setsize={g.items.length} aria-posinset={i + 1}>
              <Tag className={`mds-crail__row${interactive ? " mds-crail__row--action" : ""}`}
                {...(interactive ? { type: "button", onClick: () => onInspect(g.kind.id, item), "aria-pressed": selectedId === id } : {})}
                data-selected={selectedId === id ? "true" : undefined}>
                <span className="mds-crail__rlabel">
                  {item.label || item.id}
                  {item.effect ? <span className="mds-crail__reffect">{item.effect}</span> : null}
                </span>
                {amount != null ? <span className="mds-crail__rval" data-sign={sign(amount)}>
                  {item.formatted != null ? item.formatted : amount}{item.currency ? ` ${item.currency}` : ""}
                </span> : null}
              </Tag>
            </li>;
          })}
        </ul>
      </div>) : <p className="mds-crail__empty">No consequence declared. An action with no stated consequence is not ready for review.</p>}
    </div>

    {guards.length ? <div className="mds-crail__guards">
      <span className="mds-crail__gname">{guardLabel}</span>
      <ul className="mds-crail__glist">
        {guards.map((g, i) => {
          const status = (g && g.status) || "satisfied";
          return <li className="mds-crail__guard" key={g.id || i} data-tone={GUARD_TONE[status] || "info"}>
            <span className="mds-crail__gmark" aria-hidden="true">{status === "blocking" ? "!" : status === "warning" ? "▲" : status === "pending" ? "◐" : "✓"}</span>
            <span>{g.label || g.id}{g.detail ? ` — ${g.detail}` : ""}<span className="mds-crail__reffect">{status}</span></span>
          </li>;
        })}
      </ul>
    </div> : null}
  </section>;
}
