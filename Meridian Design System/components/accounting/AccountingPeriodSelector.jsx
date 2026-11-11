// Meridian AccountingPeriodSelector — period semantics a date picker cannot express:
// fiscal year, reporting vs posting period, period state, lock time, late-entry policy,
// and the distinct date roles (transaction, trade, settlement, accounting, posting, as-of).
// A locked period stays visible and inspectable; the control explains why, never just disables.
import React from "react";

const STATES={
  future:{label:"Future",tone:"idle",note:"Not yet open for posting"},
  open:{label:"Open",tone:"open",note:"Posting permitted"},
  "soft-closed":{label:"Soft closed",tone:"review",note:"Late entry permitted with approval"},
  "hard-closed":{label:"Hard closed",tone:"stop",note:"Closed to entries; adjustment period may apply"},
  locked:{label:"Locked",tone:"stop",note:"Read-only; reopen authorization required"},
  adjustment:{label:"Adjustment",tone:"review",note:"Open for adjusting entries only"},
  reopened:{label:"Reopened",tone:"review",note:"Reopened after close; prior approvals may be invalid"},
  "prior-period":{label:"Prior-period adjustment",tone:"review",note:"Entries restate a closed period"},
  archive:{label:"Read-only archive",tone:"idle",note:"Retained for inspection only"},
};
const ALIAS={closed:"hard-closed","soft close":"soft-closed","hard close":"hard-closed",softclosed:"soft-closed",hardclosed:"hard-closed","prior period adjustment":"prior-period",ppa:"prior-period",readonly:"archive","read only archive":"archive",current:"open"};
const norm=v=>{const k=String(v||"open").trim().toLowerCase().replace(/[_\s]+/g,"-");return STATES[k]?k:(ALIAS[k]||ALIAS[k.replace(/-/g," ")]||"open")};
const DATE_LABELS={transaction:"Transaction date",trade:"Trade date",settlement:"Settlement date",accounting:"Accounting date",posting:"Posting date",reporting:"Reporting period",asOf:"As-of date"};

let injected=false;function inject(){if(injected||typeof document==="undefined")return;injected=true;const el=document.createElement("style");el.setAttribute("data-mds","accounting-period-selector");el.textContent=`
.mds-aps{display:grid;gap:4px;font-family:var(--font-body);min-width:248px}
.mds-aps__label{font:600 10px var(--font-data,monospace);text-transform:uppercase;letter-spacing:.04em;color:var(--text-muted,#606870)}
.mds-aps__control{display:grid;grid-template-columns:1fr auto;align-items:center;height:36px;border:1px solid var(--border,#D2CDC3);background:var(--surface-well,var(--card-surface-raised,#FCFBF8));position:relative}
.mds-aps select{appearance:none;border:0;background:transparent;width:100%;height:100%;padding:0 118px 0 9px;color:var(--text-primary,#20252B);font:12px var(--font-body)}
.mds-aps select:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:1px}
.mds-aps__state{position:absolute;right:8px;pointer-events:none;font:600 9px var(--font-data,monospace);text-transform:uppercase;letter-spacing:.04em;padding:1px 5px;border:1px solid var(--border,#D2CDC3);border-radius:2px;background:var(--surface-plate,#F8F7F3);color:var(--text-muted,#606870)}
.mds-aps__state[data-tone="open"]{color:var(--green-dim,#23583F);border-color:var(--green,#2E7655)}
.mds-aps__state[data-tone="review"]{color:var(--orange-dim,#725012);border-color:var(--orange,#986717)}
.mds-aps__state[data-tone="stop"]{color:var(--red-dim,#873832);border-color:var(--red,#B54B42)}
.mds-aps__meta{display:grid;gap:1px;padding:6px 8px;border:1px solid var(--border-divider,#E2DED5);border-top:0;background:var(--surface-plate,var(--bg-light,#F8F7F3))}
.mds-aps__kv{display:flex;justify-content:space-between;gap:10px;font:9px/15px var(--font-data,monospace);color:var(--text-muted,#606870)}
.mds-aps__kv b{font-weight:600;color:var(--text-primary,#20252B)}
.mds-aps__note{font:10px/15px var(--font-body);color:var(--text-secondary,#535C65);padding:5px 8px;border:1px solid var(--border-divider,#E2DED5);border-top:0;background:var(--surface-well,#FCFBF8)}
.mds-aps__note[data-tone="stop"]{border-color:var(--red,#B54B42);background:var(--red-surface,rgba(181,75,66,.1));color:var(--red-dim,#873832)}
.mds-aps__note[data-tone="review"]{border-color:var(--orange,#986717);background:var(--orange-surface,rgba(152,103,23,.1));color:var(--orange-dim,#725012)}
.mds-aps__dates{display:grid;gap:1px;padding:6px 8px;border:1px solid var(--border-divider,#E2DED5);border-top:0;background:var(--surface-well,#FCFBF8)}
.mds-aps__dates h4{margin:0 0 2px;font:9px var(--font-data,monospace);text-transform:uppercase;letter-spacing:.05em;color:var(--text-muted,#606870)}
`;document.head.appendChild(el)}

export function AccountingPeriodSelector({
  periods=[],value,onChange,label="Accounting period",disabled,
  showDetail=true,dates,className="",...rest
}){
  inject();
  const current=periods.find(p=>String(p.value)===String(value))||periods[0]||{};
  const key=norm(current.state);
  const s=STATES[key];
  const explanation=current.explanation||(key==="locked"||key==="hard-closed"?`${s.label} · ${current.lockedAfter?`locked ${current.lockedAfter}`:s.note}. Editing and posting are unavailable; the period remains inspectable.`:s.note);
  const dateRoles=dates?Object.entries(dates).filter(([,v])=>v):[];
  return <div className={`mds-aps${className?" "+className:""}`} {...rest}>
    <label className="mds-aps__label" htmlFor={undefined}>{label}</label>
    <span className="mds-aps__control">
      <select aria-label={label} value={value??current.value??""} onChange={e=>onChange?.(e.target.value,periods.find(p=>String(p.value)===e.target.value))} disabled={disabled}>
        {periods.map(p=><option key={p.value} value={p.value}>{p.label}{p.state&&norm(p.state)!=="open"?` — ${STATES[norm(p.state)].label}`:""}</option>)}
      </select>
      <span className="mds-aps__state" data-tone={s.tone} data-state={key}>{s.label}</span>
    </span>
    {showDetail?<div className="mds-aps__meta">
      {current.fiscalYear?<span className="mds-aps__kv"><span>Fiscal year</span><b>{current.fiscalYear}</b></span>:null}
      <span className="mds-aps__kv"><span>Reporting period</span><b>{current.reportingPeriod||current.label||"—"}</b></span>
      <span className="mds-aps__kv"><span>Posting period</span><b>{current.postingPeriod||"—"}</b></span>
      {current.asOf?<span className="mds-aps__kv"><span>As-of date</span><b>{current.asOf}</b></span>:null}
      {current.lockedAfter?<span className="mds-aps__kv"><span>Locked after</span><b>{current.lockedAfter}</b></span>:null}
      {current.lateEntryPolicy?<span className="mds-aps__kv"><span>Late-entry policy</span><b>{current.lateEntryPolicy}</b></span>:null}
      {current.permission?<span className="mds-aps__kv"><span>Your permission</span><b>{current.permission}</b></span>:null}
      {current.adjustmentPeriod?<span className="mds-aps__kv"><span>Adjustment period</span><b>{current.adjustmentPeriod}</b></span>:null}
    </div>:null}
    {showDetail&&(explanation||current.description)?<p className="mds-aps__note" data-tone={s.tone==="open"||s.tone==="idle"?undefined:s.tone}>{current.description||explanation}</p>:null}
    {dateRoles.length?<div className="mds-aps__dates"><h4>Dates in effect</h4>{dateRoles.map(([k,v])=><span className="mds-aps__kv" key={k}><span>{DATE_LABELS[k]||k}</span><b>{v}</b></span>)}</div>:null}
  </div>;
}
