// Meridian CashFlowLadder — expected inflows against expected outflows, period by period.
// Mirrored ladder, not a stack: assets right of the zero datum, liabilities left, so the two
// stay directly comparable on one shared scale. Exact gross, net, and cumulative gap values
// are always on the face; the summary table is the mobile and nonvisual fallback.
import React from "react";
import { conditionMeta } from "../operations/conditions";
import { formatCompact } from "../core/format.js";

const glyph=condition=>condition?conditionMeta(condition).glyph:null;
const finite=value=>typeof value==="number"&&Number.isFinite(value);
const unavailable=condition=>condition&&["restricted","missing","na"].includes(conditionMeta(condition).key);

let injected=false;function inject(){if(injected||typeof document==="undefined")return;injected=true;const el=document.createElement("style");el.setAttribute("data-mds","cash-flow-ladder");el.textContent=`
.mds-cfl{font-family:var(--font-body);color:var(--text-secondary,#535C65)}
.mds-cfl__head{display:flex;gap:12px;align-items:baseline;flex-wrap:wrap;margin-bottom:6px}
.mds-cfl__legend{display:flex;gap:12px;font:10px var(--font-data,monospace);color:var(--text-muted,#606870)}
.mds-cfl__legend span{display:inline-flex;align-items:center;gap:5px}
.mds-cfl__legend i{width:12px;height:3px;display:inline-block;background:var(--series-actual,var(--brand-axis,#29323A))}
.mds-cfl__legend i[data-role="liability"]{background:var(--series-prior,var(--brand-field,#5D7286))}
.mds-cfl__legend i[data-role="scenario"]{background:var(--series-scenario,var(--scenario-signal,#C56343));height:0;border-top:2px dashed var(--series-scenario,var(--scenario-signal,#C56343))}
.mds-cfl__unit{margin-left:auto;font:9px var(--font-data,monospace);color:var(--text-muted,#606870)}
.mds-cfl__colhead,.mds-cfl__row{display:grid;grid-template-columns:minmax(78px,108px) 1fr 96px 96px;gap:10px;align-items:center}
.mds-cfl__colhead{font:600 9px var(--font-data,monospace);text-transform:uppercase;letter-spacing:.04em;color:var(--text-muted,#606870);padding-bottom:3px;border-bottom:1px solid var(--border-strong,#AAA49A)}
.mds-cfl__colhead span:nth-child(n+3){text-align:right}
.mds-cfl__row{min-height:34px;border-bottom:1px solid var(--border-divider,#E2DED5);width:100%;background:none;border-left:2px solid transparent;padding:0;text-align:left;font:inherit;color:inherit}
.mds-cfl__row--action{cursor:pointer}
.mds-cfl__row--action:hover{background:var(--bg-hover,#EDEFF0)}
.mds-cfl__row--action:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:-2px}
.mds-cfl__row[data-selected="true"]{border-left-color:var(--selected,#496C8F);background:var(--selected-surface,var(--bg-active,#E7EDF2))}
.mds-cfl__label{font-size:11px;font-weight:600;color:var(--text-primary,#20252B)}
.mds-cfl__label small{display:block;font:9px var(--font-data,monospace);font-weight:400;color:var(--text-muted,#606870)}
.mds-cfl__track{position:relative;height:22px;background:var(--surface-recessed,var(--bg-medium,#EBE8E1))}
.mds-cfl__zero{position:absolute;left:50%;top:-2px;bottom:-2px;border-left:1px solid var(--border-strong,#AAA49A)}
.mds-cfl__asset,.mds-cfl__liab{position:absolute;left:50%;height:7px;min-width:1px}
.mds-cfl__asset{top:3px;background:var(--series-actual,var(--brand-axis,#29323A))}
.mds-cfl__liab{bottom:3px;background:var(--series-prior,var(--brand-field,#5D7286));transform:translateX(-100%)}
.mds-cfl__scen{position:absolute;left:50%;height:0;border-top:2px dashed var(--series-scenario,var(--scenario-signal,#C56343))}
.mds-cfl__scen[data-side="liability"]{transform:translateX(-100%)}
.mds-cfl__scen[data-band="asset"]{top:2px}
.mds-cfl__scen[data-band="liability"]{bottom:2px}
.mds-cfl__value{text-align:right;font:11px var(--font-data,monospace);font-variant-numeric:tabular-nums;color:var(--text-primary,#20252B)}
.mds-cfl__value small{display:block;font-size:9px;color:var(--text-muted,#606870)}
.mds-cfl__value[data-negative="true"]{color:var(--red-dim,#873832)}
.mds-cfl__mark{display:inline-block;width:9px;text-align:center;margin-right:3px;color:var(--text-muted,#606870)}
.mds-cfl__note{margin:6px 0 0;font:9px/14px var(--font-data,monospace);color:var(--text-muted,#606870)}
.mds-cfl__table{width:100%;border-collapse:collapse;margin-top:10px}
.mds-cfl__table caption{text-align:left;font:9px var(--font-data,monospace);text-transform:uppercase;letter-spacing:.05em;color:var(--text-muted,#606870);padding-bottom:3px}
.mds-cfl__table th,.mds-cfl__table td{border-bottom:1px solid var(--border-divider,#E2DED5);padding:4px 8px;font:10px var(--font-data,monospace);font-variant-numeric:tabular-nums;text-align:right}
.mds-cfl__table th[scope="row"],.mds-cfl__table thead th:first-child{text-align:left}
.mds-cfl__table thead th{color:var(--text-muted,#606870);border-bottom:1px solid var(--border-strong,#AAA49A);font-weight:600;text-transform:uppercase;font-size:9px}
.mds-cfl__table tr[data-total="true"] td,.mds-cfl__table tr[data-total="true"] th{border-top:2px solid var(--border-strong,#AAA49A);font-weight:600;color:var(--text-primary,#20252B)}
.mds-cfl--sr .mds-cfl__table{position:absolute;width:1px;height:1px;overflow:hidden;clip:rect(0 0 0 0)}
@media (max-width:640px){.mds-cfl__rows,.mds-cfl__colhead{display:none}.mds-cfl--sr .mds-cfl__table{position:static;width:100%;height:auto;clip:auto}}
`;document.head.appendChild(el)}

const compact=v=>formatCompact(v,{decimals:1});
const signed=(v,f)=>`${v<0?"\u2212":"+"}${f(Math.abs(v))}`;

export function CashFlowLadder({
  items=[],valueFormat=compact,
  assetLabel="Asset inflows",liabilityLabel="Liability outflows",
  scenarioLabel="Scenario",showScenario=false,
  currency="USD",basis="Nominal",bucketDefinition,asOf,source,coverage,
  cumulative=true,showTable=true,selectedId,onSelect,
  description="Cash-flow ladder",className="",...rest
}){
  inject();
  const max=Math.max(1,...items.flatMap(x=>[Math.abs(Number(x.assets)||0),Math.abs(Number(x.liabilities)||0),Math.abs(Number(x.scenarioAssets)||0),Math.abs(Number(x.scenarioLiabilities)||0)]));
  let run=0;
  const rows=items.map(x=>{
    const a=Number(x.assets)||0,l=Number(x.liabilities)||0,n=x.net??a-l;
    run+=n;return{...x,a,l,n,cum:run};
  });
  const tot=rows.reduce((t,r)=>({a:t.a+r.a,l:t.l+r.l,n:t.n+r.n}),{a:0,l:0,n:0});
  const unscheduled=items.filter(x=>x.unscheduled);
  const Tag=onSelect?"button":"div";

  const hasUnavailableInputs=items.some(item=>unavailable(item.condition)||!finite(item.assets)||!finite(item.liabilities)||["net","scenarioAssets","scenarioLiabilities","unscheduled"].some(key=>item[key]!=null&&!finite(item[key])));
  if(hasUnavailableInputs) {
    const withheld=items.some(item=>item.condition&&conditionMeta(item.condition).key==="restricted");
    return <div className="mds-exposure-unavailable" role="status" aria-label={description} style={{fontFamily:"var(--font-body)",color:"var(--text-primary)",padding:12,border:"1px solid var(--border)",background:"var(--surface-plate,var(--bg-light))"}}>
      <strong>{description}</strong>
      <p>{withheld?"Restricted inputs are withheld. Totals and comparisons are unavailable.":"Input data is unavailable or invalid. Totals and comparisons cannot be computed; missing values are not zero."}</p>
    </div>;
  }

  return <div className={`mds-cfl${showTable?"":" mds-cfl--sr"}${className?" "+className:""}`} {...rest}>
    <div className="mds-cfl__head">
      <div className="mds-cfl__legend">
        <span><i/>{assetLabel}</span><span><i data-role="liability"/>{liabilityLabel}</span>
        {showScenario?<span><i data-role="scenario"/>{scenarioLabel}</span>:null}
      </div>
      <span className="mds-cfl__unit">{currency} · {basis}{bucketDefinition?` · ${bucketDefinition}`:""}{asOf?` · as of ${asOf}`:""}</span>
    </div>
    <div className="mds-cfl__colhead"><span>Period</span><span>{liabilityLabel} ← 0 → {assetLabel}</span><span>Net gap</span><span>{cumulative?"Cumulative":"Coverage"}</span></div>
    <div className="mds-cfl__rows">{rows.map((r,i)=>{
      const g=glyph(r.condition);
      return <Tag key={r.id||r.label||i} type={onSelect?"button":undefined} className={`mds-cfl__row${onSelect?" mds-cfl__row--action":""}`} data-selected={String(r.id||r.label)===String(selectedId)?"true":undefined} onClick={onSelect?()=>onSelect(r,i):undefined}>
        <span className="mds-cfl__label">{r.label}{r.detail?<small>{r.detail}</small>:null}</span>
        <span className="mds-cfl__track">
          <span className="mds-cfl__zero"/>
          <span className="mds-cfl__asset" style={{width:`${r.a/max*48}%`}}/>
          <span className="mds-cfl__liab" style={{width:`${r.l/max*48}%`}}/>
          {showScenario&&r.scenarioAssets!=null?<span className="mds-cfl__scen" data-band="asset" style={{width:`${Math.abs(r.scenarioAssets)/max*48}%`}}/>:null}
          {showScenario&&r.scenarioLiabilities!=null?<span className="mds-cfl__scen" data-band="liability" data-side="liability" style={{width:`${Math.abs(r.scenarioLiabilities)/max*48}%`}}/>:null}
        </span>
        <span className="mds-cfl__value" data-negative={r.n<0?"true":undefined}>{g?<span className="mds-cfl__mark" aria-hidden="true">{g}</span>:null}{signed(r.n,valueFormat)}<small>{valueFormat(r.a)} in · {valueFormat(r.l)} out</small></span>
        <span className="mds-cfl__value" data-negative={cumulative&&r.cum<0?"true":undefined}>{cumulative?signed(r.cum,valueFormat):(r.coverage??"—")}{r.unscheduled?<small>{valueFormat(r.unscheduled)} unscheduled</small>:null}</span>
      </Tag>;
    })}</div>
    {unscheduled.length||source||coverage?<p className="mds-cfl__note">
      {unscheduled.length?`${unscheduled.length} bucket${unscheduled.length===1?"":"s"} carry unscheduled or unknown flows — shown, never netted away. `:""}
      {coverage?`Coverage: ${coverage}. `:""}{source?`Source: ${source}.`:""}
    </p>:null}
    <table className="mds-cfl__table">
      <caption>{description} · {currency} · {basis}{asOf?` · as of ${asOf}`:""}</caption>
      <thead><tr><th scope="col">Period</th><th scope="col">{assetLabel}</th><th scope="col">{liabilityLabel}</th><th scope="col">Net</th>{cumulative?<th scope="col">Cumulative</th>:null}<th scope="col">Condition</th></tr></thead>
      <tbody>
        {rows.map((r,i)=><tr key={r.id||i}><th scope="row">{r.label}</th><td>{valueFormat(r.a)}</td><td>{valueFormat(r.l)}</td><td>{signed(r.n,valueFormat)}</td>{cumulative?<td>{signed(r.cum,valueFormat)}</td>:null}<td>{r.condition||"confirmed"}</td></tr>)}
        <tr data-total="true"><th scope="row">Total</th><td>{valueFormat(tot.a)}</td><td>{valueFormat(tot.l)}</td><td>{signed(tot.n,valueFormat)}</td>{cumulative?<td>{signed(tot.n,valueFormat)}</td>:null}<td>—</td></tr>
      </tbody>
    </table>
  </div>;
}
