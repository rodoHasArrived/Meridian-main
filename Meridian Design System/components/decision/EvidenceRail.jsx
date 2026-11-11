// Meridian EvidenceRail — why the selected value or object is in the state it is in.
//
// A SLOT composition, not a schema: the screen supplies the section bodies it can actually prove,
// and the rail owns the order they are read in, the coverage denominator, and the continuity of the
// evidence itself. A section the screen cannot prove is absent — never a blank tab. The rail is
// driven by the committed selection (LinkedSelectionProvider), so selecting a cell, a chart
// observation or a dependency node changes what it explains.
import React from "react";

export const EVIDENCE_SECTIONS = [
  { id: "summary", label: "Summary", question: "What is the short answer?" },
  { id: "sources", label: "Sources", question: "What documents stand behind it?" },
  { id: "calculation", label: "Calculation", question: "How was it derived?" },
  { id: "lineage", label: "Lineage", question: "Where did the inputs come from?" },
  { id: "audit", label: "Audit", question: "Who touched it, and when?" },
];

const CONTINUITY = {
  live: { label: "Live", note: "" },
  retained: { label: "Retained", note: "Last known good — writes locked" },
  partial: { label: "Partial", note: "Coverage deficit stated below" },
  recovering: { label: "Recovering", note: "Reconciling against the live feed" },
  stale: { label: "Stale", note: "Exceeds the freshness threshold" },
};

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "evidence-rail");
  el.textContent = `
.mds-erail{display:flex;flex-direction:column;min-height:0;min-width:0;border:1px solid var(--border,#D2CDC3);background:var(--surface-plate,var(--bg-light,#F8F7F3));font-family:var(--font-body);color:var(--text-secondary,#535C65)}
.mds-erail__head{padding:8px 11px;border-bottom:1px solid var(--border-divider,#E2DED5);background:var(--bg-medium,#EBE8E1)}
.mds-erail__eyebrow{font:600 9px var(--font-data,monospace);letter-spacing:.07em;text-transform:uppercase;color:var(--text-muted,#606870)}
.mds-erail__title{margin:1px 0 0;font-size:13px;font-weight:600;color:var(--text-primary,#20252B)}
.mds-erail__obj{margin-top:2px;font:10px var(--font-data,monospace);font-variant-numeric:tabular-nums;color:var(--text-muted,#606870);word-break:break-word}
.mds-erail__state{display:flex;flex-wrap:wrap;gap:4px 10px;align-items:baseline;padding:6px 11px;border-bottom:1px solid var(--border-divider,#E2DED5)}
.mds-erail__cov{font:600 11px var(--font-data,monospace);font-variant-numeric:tabular-nums;color:var(--text-primary,#20252B)}
.mds-erail__covnote,.mds-erail__note{font:10px var(--font-data,monospace);color:var(--text-muted,#606870)}
.mds-erail__cov[data-short="true"]{color:var(--orange-dim,#725012)}
.mds-erail__chip{display:inline-flex;align-items:center;gap:5px;padding:1px 6px;border:1px solid var(--border,#D2CDC3);border-radius:var(--radius-chip,2px);font:600 9px var(--font-data,monospace);letter-spacing:.05em;text-transform:uppercase;color:var(--text-secondary,#535C65);background:var(--surface-well,var(--card-surface-raised,#FCFBF8))}
.mds-erail__chip[data-continuity="retained"],.mds-erail__chip[data-continuity="stale"]{border-color:var(--orange,#986717);color:var(--orange-dim,#725012);background:var(--orange-a10,rgba(152,103,23,.10))}
.mds-erail__chip[data-continuity="partial"]{border-color:var(--semantic-info,#2F648A);color:var(--semantic-info,#2F648A);background:var(--blue-a10,rgba(47,100,138,.10))}
.mds-erail__tabs{display:flex;flex-wrap:wrap;gap:0;border-bottom:1px solid var(--border-strong,#AAA49A);background:var(--surface-well,var(--card-surface-raised,#FCFBF8))}
.mds-erail__tab{appearance:none;min-height:var(--theme-control-height,30px);padding:0 10px;border:0;border-right:1px solid var(--border-divider,#E2DED5);border-bottom:2px solid transparent;background:transparent;font:600 11px var(--font-body);color:var(--text-muted,#606870);cursor:pointer;transition:color 120ms cubic-bezier(.2,0,0,1),border-color 120ms cubic-bezier(.2,0,0,1),background-color 120ms cubic-bezier(.2,0,0,1)}
.mds-erail__tab:hover{background:var(--bg-hover,#EDEFF0);color:var(--text-primary,#20252B)}
.mds-erail__tab:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:-2px}
.mds-erail__tab[aria-selected="true"]{color:var(--text-primary,#20252B);border-bottom-color:var(--interaction-selected,#496C8F);background:var(--interaction-selected-surface,var(--selected-surface,#E7EDF2))}
.mds-erail__count{margin-left:5px;font:10px var(--font-data,monospace);font-variant-numeric:tabular-nums;color:var(--text-muted,#606870)}
.mds-erail__q{padding:5px 11px 0;font:10px/15px var(--font-body);color:var(--text-muted,#606870)}
.mds-erail__body{flex:1;min-height:0;overflow:auto;padding:9px 11px}
.mds-erail__empty{padding:12px 0;font-size:11px;color:var(--text-muted,#606870)}
.mds-erail--dense .mds-erail__head,.mds-erail--dense .mds-erail__state,.mds-erail--dense .mds-erail__body{padding-left:9px;padding-right:9px}
.mds-erail--dense .mds-erail__tab{min-height:var(--table-row-height,26px);font-size:10px;padding:0 8px}
@media print{.mds-erail__tabs{display:none}.mds-erail__body{overflow:visible}}
`;
  document.head.appendChild(el);
}

export function EvidenceRail({
  object, title = "Evidence", sections = {}, counts = {},
  activeSection, defaultSection, onSectionChange,
  coverage, continuity, freshness, asOf,
  dense = false, showQuestion = true, className = "", ...rest
}) {
  inject();
  const uid = React.useId();
  const available = EVIDENCE_SECTIONS.filter((s) => sections[s.id] != null);
  const [internal, setInternal] = React.useState(defaultSection || (available[0] && available[0].id));
  const current = activeSection != null ? activeSection : internal;
  const active = available.find((s) => s.id === current) || available[0] || null;
  const tabRefs = React.useRef({});

  const select = (id) => {
    if (activeSection == null) setInternal(id);
    if (onSectionChange) onSectionChange(id);
  };
  const onKeyDown = (event) => {
    const i = available.findIndex((s) => s.id === (active && active.id));
    if (i < 0) return;
    const to = { ArrowRight: i + 1, ArrowLeft: i - 1, Home: 0, End: available.length - 1 }[event.key];
    if (to == null) return;
    const next = available[Math.min(Math.max(to, 0), available.length - 1)];
    if (!next) return;
    event.preventDefault();
    select(next.id);
    const node = tabRefs.current[next.id];
    if (node) node.focus();
  };

  const attached = coverage && coverage.attached != null ? Number(coverage.attached) : null;
  const required = coverage && coverage.required != null ? Number(coverage.required) : null;
  const short = attached != null && required != null && attached < required;
  const cont = continuity ? CONTINUITY[continuity] || { label: String(continuity), note: "" } : null;

  return <section className={`mds-erail${dense ? " mds-erail--dense" : ""}${className ? " " + className : ""}`} aria-labelledby={`${uid}-t`} {...rest}>
    <header className="mds-erail__head">
      <div className="mds-erail__eyebrow">Evidence</div>
      <h3 className="mds-erail__title" id={`${uid}-t`}>{title}</h3>
      {object ? <div className="mds-erail__obj">{[object.label, object.type, object.id, object.version != null ? `v${object.version}` : null].filter(Boolean).join(" · ")}</div> : null}
    </header>

    {(coverage || cont || freshness || asOf) ? <div className="mds-erail__state">
      {attached != null && required != null
        ? <span className="mds-erail__cov" data-short={short ? "true" : undefined}>{attached} of {required} {coverage.label || "sources"}</span>
        : null}
      {short ? <span className="mds-erail__covnote">{required - attached} required {required - attached === 1 ? "item" : "items"} outstanding</span> : null}
      {cont ? <span className="mds-erail__chip" data-continuity={continuity}>{cont.label}</span> : null}
      {cont && cont.note ? <span className="mds-erail__note">{cont.note}</span> : null}
      {freshness ? <span className="mds-erail__note">Freshness {freshness}</span> : null}
      {asOf ? <span className="mds-erail__note">As of {asOf}</span> : null}
    </div> : null}

    {available.length ? <>
      <div className="mds-erail__tabs" role="tablist" aria-label="Evidence sections" onKeyDown={onKeyDown}>
        {available.map((s) => {
          const selected = active && active.id === s.id;
          return <button key={s.id} type="button" className="mds-erail__tab" role="tab"
            id={`${uid}-tab-${s.id}`} aria-selected={selected ? "true" : "false"}
            aria-controls={`${uid}-panel-${s.id}`} tabIndex={selected ? 0 : -1}
            ref={(node) => { tabRefs.current[s.id] = node; }}
            onClick={() => select(s.id)}>
            {s.label}{counts[s.id] != null ? <span className="mds-erail__count">{counts[s.id]}</span> : null}
          </button>;
        })}
      </div>
      {showQuestion && active ? <p className="mds-erail__q">{active.question}</p> : null}
      {active ? <div className="mds-erail__body" role="tabpanel" id={`${uid}-panel-${active.id}`} aria-labelledby={`${uid}-tab-${active.id}`} tabIndex={0}>
        {sections[active.id]}
      </div> : null}
    </> : <div className="mds-erail__body"><p className="mds-erail__empty">No evidence sections declared for this object. A rail with nothing to prove is not rendered as empty tabs.</p></div>}
  </section>;
}
