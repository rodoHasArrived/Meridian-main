// Meridian MaturityLadder — how exposure is distributed across maturity buckets. Where
// CashFlowLadder plots scheduled flows, this plots the profile of the underlying exposure.
// Bucket definitions are declared, stable, and always fully rendered: a bucket with no
// exposure shows as zero rather than disappearing, and "Unknown maturity" is never dropped.
// Amount and percent are separate modes, never mixed in one reading; the difference column
// is directly labelled so the comparison never depends on bar length alone.
import React from "react";
import { formatCompact } from "../core/format.js";

const COND={confirmed:"●",provisional:"◐",estimated:"○",stale:"◔",overridden:"◆",missing:"■",na:"—",pending:"◇",exception:"▲",reconciled:"✓"};
const glyph=c=>COND[String(c||"").toLowerCase()]||null;
export const MATURITY_BUCKETS=["0–1 year","1–3 years","3–5 years","5–7 years","7–10 years","10–20 years","20+ years","Unknown"];
const MODES=[{id:"amount",label:"Amount"},{id:"percent",label:"Percent"}];
const UNKNOWN=/^unknown|^unscheduled|^perpetual|^no maturity/i;

let injected=false;function inject(){if(injected||typeof document==="undefined")return;injected=true;const el=document.createElement("style");el.setAttribute("data-mds","maturity-ladder");el.textContent=`
.mds-ml{font-family:var(--font-body);color:var(--text-secondary,#535C65)}
.mds-ml__head{display:flex;gap:12px;align-items:baseline;flex-wrap:wrap;margin-bottom:6px}
.mds-ml__key{display:flex;gap:12px;font:10px var(--font-data,monospace);color:var(--text-muted,#606870)}
.mds-ml__key span{display:inline-flex;align-items:center;gap:5px}
.mds-ml__key i{width:12px;height:3px;display:inline-block;background:var(--series-actual,var(--brand-axis,#29323A))}
.mds-ml__key i[data-role="comparison"]{background:var(--series-prior,#7890A4)}
.mds-ml__modes{margin-left:auto;display:flex;border:1px solid var(--border,#D2CDC3);border-radius:2px;overflow:hidden}
.mds-ml__modes button{appearance:none;border:0;border-left:1px solid var(--border,#D2CDC3);background:var(--surface-plate,#F8F7F3);font:9px var(--font-data,monospace);text-transform:uppercase;letter-spacing:.04em;color:var(--text-muted,#606870);padding:3px 8px;cursor:pointer}
.mds-ml__modes button:first-child{border-left:0}
.mds-ml__modes button[aria-pressed="true"]{background:var(--interaction-selected-surface,#E7EDF2);color:var(--text-primary,#20252B);font-weight:600}
.mds-ml__modes button:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:-2px}
.mds-ml__unit{font:9px var(--font-data,monospace);color:var(--text-muted,#606870);flex-basis:100%}
.mds-ml__colhead,.mds-ml__row{display:grid;grid-template-columns:minmax(78px,104px) 1fr 92px 92px 96px;gap:10px;align-items:center}
.mds-ml__colhead{font:600 9px var(--font-data,monospace);text-transform:uppercase;letter-spacing:.04em;color:var(--text-muted,#606870);padding-bottom:3px;border-bottom:1px solid var(--border-strong,#AAA49A)}
.mds-ml__colhead span:nth-child(n+3){text-align:right}
.mds-ml__row{min-height:34px;width:100%;border:0;border-bottom:1px solid var(--border-divider,#E2DED5);border-left:2px solid transparent;background:none;padding:0;text-align:left;font:inherit;color:inherit}
.mds-ml__row--action{cursor:pointer}
.mds-ml__row--action:hover{background:var(--bg-hover,#EDEFF0)}
.mds-ml__row--action:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:-2px}
.mds-ml__row[data-selected="true"]{border-left-color:var(--selected,#496C8F);background:var(--interaction-selected-surface,#E7EDF2)}
.mds-ml__row[data-unknown="true"] .mds-ml__label{font-style:italic}
.mds-ml__label{font-size:11px;font-weight:600;color:var(--text-primary,#20252B)}
.mds-ml__label small{display:block;font:9px var(--font-data,monospace);font-weight:400;color:var(--text-muted,#606870)}
.mds-ml__track{position:relative;height:22px;background:var(--surface-recessed,var(--bg-medium,#EBE8E1));border-left:1px solid var(--border-strong,#AAA49A)}
.mds-ml__bar{position:absolute;left:0;height:7px;min-width:1px}
.mds-ml__bar--primary{top:3px;background:var(--series-actual,var(--brand-axis,#29323A))}
.mds-ml__bar--comparison{bottom:3px;background:var(--series-prior,#7890A4)}
.mds-ml__parity{position:absolute;top:0;bottom:0;border-left:1px dashed var(--border-strong,#AAA49A)}
.mds-ml__value{text-align:right;font:11px var(--font-data,monospace);font-variant-numeric:tabular-nums;color:var(--text-primary,#20252B)}
.mds-ml__value small{display:block;font-size:9px;color:var(--text-muted,#606870)}
.mds-ml__diff{text-align:right;font:11px var(--font-data,monospace);font-variant-numeric:tabular-nums;color:var(--text-primary,#20252B)}
.mds-ml__diff small{display:block;font:9px var(--font-body);color:var(--text-muted,#606870)}
.mds-ml__mark{display:inline-block;width:9px;text-align:center;margin-right:3px;color:var(--text-muted,#606870)}
.mds-ml__totals{display:grid;grid-template-columns:minmax(78px,104px) 1fr 92px 92px 96px;gap:10px;align-items:baseline;padding-top:5px;border-top:2px solid var(--border-strong,#AAA49A);font:600 11px var(--font-data,monospace);font-variant-numeric:tabular-nums;color:var(--text-primary,#20252B)}
.mds-ml__totals span:nth-child(n+3){text-align:right}
.mds-ml__note{margin:6px 0 0;font:9px/14px var(--font-data,monospace);color:var(--text-muted,#606870)}
.mds-ml__table{width:100%;border-collapse:collapse;margin-top:10px}
.mds-ml__table caption{text-align:left;font:9px var(--font-data,monospace);text-transform:uppercase;letter-spacing:.05em;color:var(--text-muted,#606870);padding-bottom:3px}
.mds-ml__table th,.mds-ml__table td{border-bottom:1px solid var(--border-divider,#E2DED5);padding:4px 8px;font:10px var(--font-data,monospace);font-variant-numeric:tabular-nums;text-align:right}
.mds-ml__table th[scope="row"],.mds-ml__table thead th:first-child{text-align:left}
.mds-ml__table thead th{color:var(--text-muted,#606870);border-bottom:1px solid var(--border-strong,#AAA49A);font-weight:600;text-transform:uppercase;font-size:9px}
.mds-ml__table tr[data-total="true"] td,.mds-ml__table tr[data-total="true"] th{border-top:2px solid var(--border-strong,#AAA49A);font-weight:600;color:var(--text-primary,#20252B)}
.mds-ml--sr .mds-ml__table{position:absolute;width:1px;height:1px;overflow:hidden;clip:rect(0 0 0 0)}
@media (max-width:640px){.mds-ml__rows,.mds-ml__colhead,.mds-ml__totals{display:none}.mds-ml--sr .mds-ml__table{position:static;width:100%;height:auto;clip:auto}}
`;document.head.appendChild(el)}

const compact=v=>formatCompact(v,{decimals:1});
const signed=(v,f)=>`${v<0?"\u2212":"+"}${f(Math.abs(v))}`;
const pct=v=>`${(Math.round(v*10)/10).toFixed(1)}%`;

export function MaturityLadder({
  items=[],buckets,
  primaryLabel="Portfolio",comparisonLabel="Benchmark",comparisonMode,
  mode:modeProp,defaultMode="amount",onModeChange,
  valueFormat=compact,currency="USD",measure="Market value",
  asOf,source,coverage,bucketDefinition,
  selectedId,onSelect,showTable=true,showParity=true,
  description="Maturity ladder",className="",...rest
}){
  inject();
  const[own,setOwn]=React.useState(defaultMode);
  const mode=modeProp||own;
  const setMode=m=>{if(modeProp===undefined)setOwn(m);if(onModeChange)onModeChange(m)};

  // Declared buckets are authoritative: render every one, in order, even at zero.
  const declared=buckets||(items.length?items.map(x=>x.label):MATURITY_BUCKETS);
  const byLabel=new Map(items.map(x=>[String(x.label),x]));
  const rows=declared.map(label=>{
    const x=byLabel.get(String(label))||{label,primary:0,comparison:0,condition:"na",empty:true};
    return{...x,label,p:Number(x.primary)||0,c:Number(x.comparison)||0,unknown:x.unknown||UNKNOWN.test(String(label))};
  });
  // Any item outside the declared set is appended rather than silently dropped.
  items.forEach(x=>{if(!declared.some(l=>String(l)===String(x.label)))rows.push({...x,p:Number(x.primary)||0,c:Number(x.comparison)||0,unknown:true,undeclared:true})});

  const totP=rows.reduce((n,r)=>n+r.p,0);
  const totC=rows.reduce((n,r)=>n+r.c,0);
  const share=(v,t)=>t?v/t*100:0;
  const view=r=>mode==="percent"?{p:share(r.p,totP),c:share(r.c,totC)}:{p:r.p,c:r.c};
  const fmt=v=>mode==="percent"?pct(v):valueFormat(v);
  const diffUnit=mode==="percent"?"pp":currency;
  const max=Math.max(1,...rows.flatMap(r=>{const v=view(r);return[v.p,v.c]}));
  const Tag=onSelect?"button":"div";
  const unknownRow=rows.find(r=>r.unknown&&(r.p||r.c));

  return <div className={`mds-ml${showTable?"":" mds-ml--sr"}${className?" "+className:""}`} {...rest}>
    <div className="mds-ml__head">
      <div className="mds-ml__key">
        <span><i/>{primaryLabel}</span><span><i data-role="comparison"/>{comparisonLabel}</span>
      </div>
      <div className="mds-ml__modes" role="group" aria-label="Value mode">
        {MODES.map(m=><button key={m.id} type="button" aria-pressed={mode===m.id} onClick={()=>setMode(m.id)}>{m.label}</button>)}
      </div>
      <span className="mds-ml__unit">
        {comparisonMode?`${comparisonMode} · `:""}{measure} · {mode==="percent"?"percent of total":currency}
        {bucketDefinition?` · ${bucketDefinition}`:""}{asOf?` · as of ${asOf}`:""}
      </span>
    </div>

    <div className="mds-ml__colhead">
      <span>Bucket</span><span>Shared scale</span><span>{primaryLabel}</span><span>{comparisonLabel}</span><span>Difference</span>
    </div>

    <div className="mds-ml__rows">{rows.map((r,i)=>{
      const v=view(r);const d=v.p-v.c;const g=glyph(r.condition);
      return <Tag key={r.id||r.label||i} type={onSelect?"button":undefined}
        className={`mds-ml__row${onSelect?" mds-ml__row--action":""}`}
        data-selected={String(r.id||r.label)===String(selectedId)?"true":undefined}
        data-unknown={r.unknown?"true":undefined}
        onClick={onSelect?()=>onSelect(r,i):undefined}>
        <span className="mds-ml__label">{r.label}
          {r.undeclared?<small>Outside declared buckets</small>:r.detail?<small>{r.detail}</small>:r.empty?<small>No exposure</small>:null}
        </span>
        <span className="mds-ml__track">
          {showParity?<span className="mds-ml__parity" style={{left:`${Math.min(100,v.c/max*100)}%`}}/>:null}
          <span className="mds-ml__bar mds-ml__bar--primary" style={{width:`${Math.min(100,v.p/max*100)}%`}}/>
          <span className="mds-ml__bar mds-ml__bar--comparison" style={{width:`${Math.min(100,v.c/max*100)}%`}}/>
        </span>
        <span className="mds-ml__value">{g?<span className="mds-ml__mark" aria-hidden="true">{g}</span>:null}{fmt(v.p)}</span>
        <span className="mds-ml__value">{fmt(v.c)}</span>
        <span className="mds-ml__diff">{signed(d,x=>mode==="percent"?pct(x):valueFormat(x))}<small>{mode==="percent"?"pp vs":"vs"} {comparisonLabel}</small></span>
      </Tag>;
    })}</div>

    <div className="mds-ml__totals">
      <span>Total</span><span/>
      <span>{mode==="percent"?pct(100):valueFormat(totP)}</span>
      <span>{mode==="percent"?pct(100):valueFormat(totC)}</span>
      <span>{mode==="percent"?"—":signed(totP-totC,valueFormat)}</span>
    </div>

    <p className="mds-ml__note">
      Buckets are fixed and fully enumerated; empty buckets are shown at zero.
      {unknownRow?` Unknown maturity carries ${mode==="percent"?pct(share(unknownRow.p,totP)):valueFormat(unknownRow.p)} and is never redistributed.`:" No exposure carries unknown maturity."}
      {coverage?` Coverage: ${coverage}.`:""}{source?` Source: ${source}.`:""}
      {` Difference stated in ${diffUnit}; amount and percent are not mixed.`}
    </p>

    <table className="mds-ml__table">
      <caption>{description} · {measure} · {currency}{asOf?` · as of ${asOf}`:""}{comparisonMode?` · ${comparisonMode}`:""}</caption>
      <thead><tr><th scope="col">Bucket</th><th scope="col">{primaryLabel}</th><th scope="col">Share</th><th scope="col">{comparisonLabel}</th><th scope="col">Share</th><th scope="col">Difference</th><th scope="col">Condition</th></tr></thead>
      <tbody>
        {rows.map((r,i)=><tr key={r.id||i}>
          <th scope="row">{r.label}</th>
          <td>{valueFormat(r.p)}</td><td>{pct(share(r.p,totP))}</td>
          <td>{valueFormat(r.c)}</td><td>{pct(share(r.c,totC))}</td>
          <td>{signed(r.p-r.c,valueFormat)}</td>
          <td>{r.condition||(r.empty?"na":"confirmed")}</td>
        </tr>)}
        <tr data-total="true"><th scope="row">Total</th><td>{valueFormat(totP)}</td><td>{pct(100)}</td><td>{valueFormat(totC)}</td><td>{pct(100)}</td><td>{signed(totP-totC,valueFormat)}</td><td>—</td></tr>
      </tbody>
    </table>
  </div>;
}
