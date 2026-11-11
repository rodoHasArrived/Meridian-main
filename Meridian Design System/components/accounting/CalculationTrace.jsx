// Meridian CalculationTrace — how inputs became the output. Distinct from provenance:
// provenance says where data came from, this says how it was derived. Three levels —
// summary (formula + result), expanded (all material inputs and intermediates), audit
// (source ids, as-of dates, overrides, calculation version, execution detail).
import React from "react";

import { conditionMeta } from "../operations/conditions.js";
const meta=value=>value?conditionMeta(value):null;
const LEVELS=[{id:"summary",label:"Summary"},{id:"expanded",label:"Expanded"},{id:"audit",label:"Audit"}];

let injected=false;function inject(){if(injected||typeof document==="undefined")return;injected=true;const el=document.createElement("style");el.setAttribute("data-mds","calculation-trace");el.textContent=`
.mds-calc{border:1px solid var(--border,#D2CDC3);background:var(--surface-well,var(--card-surface-raised,#FCFBF8));font-family:var(--font-body);color:var(--text-secondary,#535C65)}
.mds-calc__head{display:flex;gap:12px;align-items:flex-start;padding:10px 12px;border-bottom:1px solid var(--border-divider,#E2DED5)}
.mds-calc__head h3{margin:0;font-size:14px;color:var(--text-primary,#20252B)}
.mds-calc__formula{margin-top:3px;font:11px/16px var(--font-data,monospace);color:var(--text-muted,#606870)}
.mds-calc__levels{margin-left:auto;display:flex;border:1px solid var(--border,#D2CDC3);border-radius:2px;overflow:hidden;flex:none}
.mds-calc__levels button{appearance:none;border:0;border-left:1px solid var(--border,#D2CDC3);background:var(--surface-plate,#F8F7F3);font:9px var(--font-data,monospace);text-transform:uppercase;letter-spacing:.04em;color:var(--text-muted,#606870);padding:3px 8px;cursor:pointer}
.mds-calc__levels button:first-child{border-left:0}
.mds-calc__levels button[aria-pressed="true"]{background:var(--selected-surface,#E7EDF2);color:var(--text-primary,#20252B);font-weight:600}
.mds-calc__levels button:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:-2px}
.mds-calc__group{padding:5px 12px 0;font:9px var(--font-data,monospace);text-transform:uppercase;letter-spacing:.05em;color:var(--text-muted,#606870)}
.mds-calc__rows{padding:4px 12px 6px}
.mds-calc__row{display:grid;grid-template-columns:18px 1fr auto;gap:8px;align-items:baseline;min-height:27px;padding:3px 0;border-bottom:1px solid var(--border-divider,#E2DED5)}
.mds-calc__row:last-child{border-bottom:0}
.mds-calc__row--intermediate{background:var(--surface-plate,#F8F7F3);margin:0 -12px;padding:3px 12px}
.mds-calc__op{font:600 12px var(--font-data,monospace);color:var(--text-muted,#606870)}
.mds-calc__label{font-size:11px;color:var(--text-primary,#20252B)}
.mds-calc__value{font:11px var(--font-data,monospace);font-variant-numeric:tabular-nums;color:var(--text-primary,#20252B);text-align:right}
.mds-calc__source,.mds-calc__detail{display:block;font:9px/14px var(--font-data,monospace);color:var(--text-muted,#606870);font-weight:400}
.mds-calc__mark{display:inline-block;width:9px;text-align:center;margin-right:3px}
.mds-calc__row[data-condition="overridden"]{border-left:2px solid var(--orange,#986717);margin-left:-12px;padding-left:10px}
.mds-calc__row[data-condition="missing"],.mds-calc__row[data-condition="exception"]{border-left:2px solid var(--red,#B54B42);margin-left:-12px;padding-left:10px}
.mds-calc__result{display:grid;grid-template-columns:1fr auto;gap:12px;align-items:baseline;padding:10px 12px;border-top:2px solid var(--border-strong,#AAA49A);background:var(--surface-plate,var(--bg-light,#F8F7F3))}
.mds-calc__result strong{font-size:12px;color:var(--text-primary,#20252B)}
.mds-calc__result span{font:600 14px var(--font-data,monospace);font-variant-numeric:tabular-nums;color:var(--text-primary,#20252B)}
.mds-calc__foot{display:flex;gap:14px;flex-wrap:wrap;padding:7px 12px;border-top:1px solid var(--border-divider,#E2DED5);font:9px var(--font-data,monospace);color:var(--text-muted,#606870)}
.mds-calc__audit{padding:9px 12px;border-top:1px solid var(--border-divider,#E2DED5);background:var(--surface-plate,#F8F7F3)}
.mds-calc__audit h4{margin:0 0 5px;font:9px var(--font-data,monospace);text-transform:uppercase;letter-spacing:.05em;color:var(--text-muted,#606870)}
.mds-calc__kv{display:grid;grid-template-columns:repeat(auto-fill,minmax(168px,1fr));gap:2px 14px}
.mds-calc__kv div{display:flex;gap:6px;justify-content:space-between;font:9px/16px var(--font-data,monospace);border-bottom:1px dotted var(--border-divider,#E2DED5)}
.mds-calc__kv b{font-weight:600;color:var(--text-primary,#20252B)}
.mds-calc__tech{margin-top:7px;font:10px/16px var(--font-data,monospace);color:var(--text-secondary,#535C65);word-break:break-all}
.mds-calc__repro{margin:6px 0 0;font:10px/15px var(--font-body);color:var(--text-muted,#606870)}
`;document.head.appendChild(el)}

const val=x=>x.value===null||x.value===undefined||x.value===""?"—":x.value;
function conditioned(text,c){if(c?.key==="restricted")return "Restricted";if(c?.key==="missing"||c?.key==="na")return "—";if(!c||text==="—")return text;if(c.key==="estimated")return`${text} Est.`;if(c.key==="overridden")return`${text}\u2009*`;return text}

function Row({step,level}){
  const c=meta(step.condition);
  const detail=level==="audit"?[step.sourceId&&`id ${step.sourceId}`,step.asOf&&`as of ${step.asOf}`,step.changedBy&&`changed by ${step.changedBy}`,step.changedAt&&step.changedAt].filter(Boolean).join(" · "):null;
  return <div className={`mds-calc__row${step.kind==="intermediate"?" mds-calc__row--intermediate":""}`} data-condition={c?.key||undefined}>
    <span className="mds-calc__op" aria-hidden="true">{step.operator||""}</span>
    <span className="mds-calc__label">{step.label}
      {level!=="summary"&&(step.source||step.detail)?<small className="mds-calc__source">{step.source?`Source: ${step.source}`:step.detail}</small>:null}
      {detail?<small className="mds-calc__detail">{detail}</small>:null}
    </span>
    <span className="mds-calc__value">{c&&level!=="summary"?<span className="mds-calc__mark" aria-hidden="true">{c.glyph}</span>:null}{conditioned(val(step),c)}{step.unit?` ${step.unit}`:""}</span>
  </div>;
}

export function CalculationTrace({
  title,formula,technicalFormula,steps=[],result,
  rounding,basis,override,overrides=[],condition="confirmed",asOf,
  level:levelProp,defaultLevel="expanded",onLevelChange,availableLevels,
  calculationVersion,calculatedAt,executedBy,engine,reproducibility,
  className="",...rest
}){
  inject();
  const[own,setOwn]=React.useState(defaultLevel);
  const level=levelProp||own;
  const setLevel=l=>{if(levelProp===undefined)setOwn(l);if(onLevelChange)onLevelChange(l)};
  const shown=LEVELS.filter(l=>!availableLevels||availableLevels.includes(l.id));
  const c=meta(condition);
  const material=steps.filter(s=>s.material!==false);
  const rows=level==="summary"?material.filter(s=>s.kind==="intermediate"||s.summary):material;
  const overrideList=overrides.length?overrides:(override?[{label:"Override",value:override}]:[]);
  return <section className={`mds-calc${className?" "+className:""}`} {...rest}>
    <header className="mds-calc__head">
      <div><h3>{title}</h3>{formula?<div className="mds-calc__formula">{formula}</div>:null}</div>
      {shown.length>1?<div className="mds-calc__levels" role="group" aria-label="Trace level">{shown.map(l=><button key={l.id} type="button" aria-pressed={level===l.id} onClick={()=>setLevel(l.id)}>{l.label}</button>)}</div>:null}
    </header>
    {level!=="summary"&&rows.some(s=>s.kind==="intermediate")?<div className="mds-calc__group">Inputs and intermediates</div>:null}
    <div className="mds-calc__rows">{rows.map((s,i)=><Row key={s.id||s.label||i} step={s} level={level}/>)}</div>
    <div className="mds-calc__result">
      <strong>{result?.label||"Calculated result"}</strong>
      <span>{conditioned(result?.value??"—",c)}{result?.unit?` ${result.unit}`:""}</span>
    </div>
    <footer className="mds-calc__foot">
      <span>{c?`${c.glyph} ${c.label}`:`Condition: ${condition}`}</span>
      {asOf?<span>As of {asOf}</span>:null}
      {rounding?<span>Rounding: {rounding}</span>:null}
      {basis?<span>Basis: {basis}</span>:null}
      {overrideList.length?<span>Overrides: {overrideList.length}</span>:null}
    </footer>
    {level==="audit"?<div className="mds-calc__audit">
      <h4>Audit detail</h4>
      <div className="mds-calc__kv">
        {calculationVersion?<div><span>Calculation version</span><b>{calculationVersion}</b></div>:null}
        {calculatedAt?<div><span>Executed</span><b>{calculatedAt}</b></div>:null}
        {executedBy?<div><span>Executed by</span><b>{executedBy}</b></div>:null}
        {engine?<div><span>Engine</span><b>{engine}</b></div>:null}
        {rounding?<div><span>Rounding policy</span><b>{rounding}</b></div>:null}
        {basis?<div><span>Accounting basis</span><b>{basis}</b></div>:null}
        {c?.key!=="restricted"&&overrideList.map((o,i)=><div key={i}><span>{o.label||"Override"}</span><b>{o.value}{o.by?` · ${o.by}`:""}</b></div>)}
      </div>
      {technicalFormula&&c?.key!=="restricted"?<div className="mds-calc__tech">{technicalFormula}</div>:null}
      {reproducibility?<p className="mds-calc__repro">{reproducibility}</p>:null}
    </div>:null}
  </section>;
}
