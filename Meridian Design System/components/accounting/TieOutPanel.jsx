// Meridian TieOutPanel — a compact proof that two independently derived values reconcile.
// Separates gross difference, explained reconciling items, and unexplained residual, so a
// tie-out never reads as successful merely because the total difference is small.
import React from "react";
import { VarianceCell } from "./VarianceCell";
import { Button } from "../core/Button";
import { useFormat } from "../core/FormatProvider";
import { conditionMeta } from "../operations/conditions.js";

const TYPES={"one-to-one":"One-to-one","many-to-one":"Many-to-one","one-to-many":"One-to-many",rollforward:"Rollforward","quantity-and-value":"Quantity and value","count-and-amount":"Count and amount","multi-basis":"Multi-basis"};
const CONDITION_GLYPH={confirmed:"●",provisional:"◐",estimated:"○",stale:"◔",overridden:"◆",missing:"■",na:"—",pending:"◇",exception:"▲",reconciled:"✓"};

let injected=false;function inject(){if(injected||typeof document==="undefined")return;injected=true;const el=document.createElement("style");el.setAttribute("data-mds","tie-out-panel");el.textContent=`
.mds-tie{border:1px solid var(--border,#D2CDC3);background:var(--surface-plate,var(--bg-light,#F8F7F3));font-family:var(--font-body);color:var(--text-secondary,#535C65)}
.mds-tie__head{display:flex;align-items:flex-start;gap:10px;padding:10px 12px;border-bottom:1px solid var(--border-divider,#E2DED5)}
.mds-tie__head h3{margin:0;font-size:14px;color:var(--text-primary,#20252B)}
.mds-tie__head p{margin:2px 0 0;font:10px var(--font-data,monospace);color:var(--text-muted,#606870)}
.mds-tie__tags{display:flex;flex-direction:column;align-items:flex-end;gap:3px;margin-left:auto}
.mds-tie__type{font:9px var(--font-data,monospace);text-transform:uppercase;letter-spacing:.04em;color:var(--text-muted,#606870)}
.mds-tie__status{display:inline-flex;align-items:center;gap:4px;font:600 10px var(--font-data,monospace);text-transform:uppercase;letter-spacing:.03em}
.mds-tie--tied .mds-tie__status{color:var(--green-dim,#23583F)}
.mds-tie--tolerance .mds-tie__status{color:var(--orange-dim,#725012)}
.mds-tie--exception .mds-tie__status{color:var(--red-dim,#873832)}
.mds-tie--indeterminate .mds-tie__status{color:var(--red-dim,#873832)}
.mds-tie__block{padding:8px 12px;border-top:1px solid var(--border-divider,#E2DED5);background:var(--red-a10,rgba(181,75,66,.10));font:11px/1.5 var(--font-body);color:var(--text-primary,#20252B);max-width:none}
.mds-tie__block b{font-weight:600}
.mds-tie__rows{padding:6px 12px}
.mds-tie__row{display:grid;grid-template-columns:1fr auto;gap:10px;align-items:baseline;min-height:28px;border-bottom:1px solid var(--border-divider,#E2DED5);font-size:11px}
.mds-tie__row:last-child{border-bottom:0}
.mds-tie__row--rule{border-bottom-color:var(--border-strong,#AAA49A)}
.mds-tie__row--sub{padding-left:14px;color:var(--text-muted,#606870);min-height:25px;font-size:10px}
.mds-tie__row--total{font-weight:600;color:var(--text-primary,#20252B)}
.mds-tie__srclabel{display:flex;align-items:baseline;gap:6px}
.mds-tie__cond{font:9px var(--font-data,monospace);color:var(--text-muted,#606870)}
.mds-tie__value{font:11px var(--font-data,monospace);font-variant-numeric:tabular-nums;color:var(--text-primary,#20252B)}
.mds-tie__toggle{display:inline-flex;align-items:center;gap:5px;background:none;border:0;padding:0;font:inherit;color:var(--border-focus,#2D6FB3);cursor:pointer}
.mds-tie__toggle:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:2px}
.mds-tie__items{border-bottom:1px solid var(--border-divider,#E2DED5)}
.mds-tie__item{display:grid;grid-template-columns:1fr auto 88px 118px;gap:8px;align-items:baseline;min-height:26px;padding-left:14px;border-bottom:1px solid var(--border-divider,#E2DED5);font-size:10px}
.mds-tie__item:last-child{border-bottom:0}
.mds-tie__item--head{color:var(--text-muted,#606870);font:9px var(--font-data,monospace);text-transform:uppercase;letter-spacing:.04em;min-height:22px}
.mds-tie__item--unexplained{background:var(--red-a10,rgba(181,75,66,.10))}
.mds-tie__proof{display:grid;grid-template-columns:1fr auto;gap:12px;align-items:center;padding:10px 12px;border-top:1px solid var(--border-strong,#AAA49A);background:var(--surface-well,var(--card-surface-raised,#FCFBF8))}
.mds-tie__proof strong{display:block;font-size:12px;color:var(--text-primary,#20252B)}
.mds-tie__proof em{display:block;margin-top:2px;font:10px var(--font-body);font-style:normal;color:var(--text-muted,#606870)}
.mds-tie__secondary{display:grid;grid-template-columns:1fr auto;gap:10px;align-items:center;padding:8px 12px;border-top:1px solid var(--border-divider,#E2DED5);font-size:10px}
.mds-tie__reason{display:grid;gap:6px;padding:9px 12px;border-top:1px solid var(--border-divider,#E2DED5);background:var(--orange-surface,#F2EAD8)}
.mds-tie__reason label{font:600 9px var(--font-data,monospace);text-transform:uppercase;color:var(--text-muted,#606870)}
.mds-tie__reason textarea{box-sizing:border-box;width:100%;min-height:48px;border:1px solid var(--border,#D2CDC3);background:var(--surface-well,var(--card-surface-raised,#FCFBF8));color:var(--text-primary,#20252B);font:11px var(--font-body);padding:6px}
.mds-tie__reason textarea:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:1px}
.mds-tie__reason .row{display:flex;gap:6px;justify-content:flex-end}
.mds-tie__actions{display:flex;flex-wrap:wrap;align-items:center;gap:6px;padding:9px 12px;border-top:1px solid var(--border-strong,#AAA49A);background:var(--surface-recessed,var(--bg-medium,#EBE8E1))}
.mds-tie__actions .spacer{flex:1}
.mds-tie__foot{padding:7px 12px;border-top:1px solid var(--border-divider,#E2DED5);font:10px var(--font-data,monospace);color:var(--text-muted,#606870)}
@media(max-width:560px){.mds-tie__item{grid-template-columns:1fr auto;row-gap:2px}.mds-tie__item span:nth-child(3),.mds-tie__item span:nth-child(4){grid-column:1/-1;text-align:left}.mds-tie__actions .spacer{display:none}.mds-tie__actions .mds-btn{flex:1 1 120px}}
`;document.head.appendChild(el)}

const EPS=0.005;
// Conditions under which a stated value is not a usable input. A tie-out reading one of these as a
// number is the defect: `missing` coerced to 0 makes the gross difference vanish.
const UNUSABLE=["missing","pending","stale","na","exception","restricted"];
const condition=s=>s?.condition?conditionMeta(s.condition).key:null;
const unavailableValue=s=>["missing","na","restricted"].includes(condition(s));
const num=v=>{if(v==null||(typeof v==="string"&&v.trim()===""))return null;const n=Number(v);return Number.isFinite(n)?n:null};
/** Every reason this tie-out cannot be computed, in operator words. Empty = computable. */
function inputGaps(sourceA,sourceB,list){
  const out=[];
  const check=(s,fallback)=>{
    const label=s?.label||fallback;
    const c=condition(s);
    if(c==="restricted"){out.push(`${label} is restricted`);return}
    if(!s||num(s.value)===null){out.push(`${label} has no value`);return}
    if(UNUSABLE.includes(c))out.push(`${label} is ${c==="na"||c==="n/a"?"not applicable":c}`);
  };
  check(sourceA,"Source A");check(sourceB,"Source B");
  for(const x of (list||[])){
    if(!x)continue;
    const label=condition(x)==="restricted"?"Restricted reconciling item":`Reconciling item "${x.label||"unnamed"}"`;
    if(condition(x)==="restricted")out.push(`${label} is restricted`);
    else if(x.explained!==false){
      if(num(x.value)===null)out.push(`${label} has no amount`);
      else if(UNUSABLE.includes(condition(x)))out.push(`${label} is ${condition(x)}`);
    }
  }
  return out;
}

export function TieOutPanel({
  title="Tie-out proof",context,type="one-to-one",
  sourceA,sourceB,items,adjustments=[],secondary,
  tolerance=0,currency="USD",notation="currency",unit,digits,
  defaultExpanded=false,expanded:expandedProp,onToggleDetail,
  reviewer,reviewedAt,evidence,evidenceLabel,
  requireReason=true,acceptLabel="Accept within tolerance",
  onInvestigate,onAddItem,onLinkEvidence,onAccept,onEscalate,onReopen,onResolve,onExport,
  status:statusProp,className="",...rest
}){
  const uid = React.useId();
  inject();
  const list=(items&&items.length?items:adjustments)||[];
  // A missing input is not a zero. Coercing one made `gross` 0, which made the panel read "Tied
  // out" — the single reading a tie-out exists to prevent.
  const gaps=inputGaps(sourceA,sourceB,list);
  const indeterminate=gaps.length>0;
  const av=unavailableValue(sourceA)?null:num(sourceA?.value),bv=unavailableValue(sourceB)?null:num(sourceB?.value);
  const a=av??0,b=bv??0;
  const explainedItems=list.filter(x=>x.explained!==false);
  const explainedUsable=explainedItems.every(x=>num(x.value)!==null&&!UNUSABLE.includes(condition(x)));
  const explained=explainedItems.reduce((n,x)=>n+(unavailableValue(x)?0:Number(x.value)||0),0);
  const gross=a-b;
  const adjusted=a+explained;
  const unexplained=adjusted-b;
  const fullyExplained=Math.abs(unexplained)<EPS;
  const withinTolerance=Math.abs(unexplained)<=Math.abs(tolerance);
  // A declared status cannot outrank a missing input: `status="tied"` over an absent source would
  // reinstate the defect from the caller's side.
  const key=indeterminate?"indeterminate":statusProp||(fullyExplained?"tied":withinTolerance?"tolerance":"exception");
  const statusText={tied:"Tied out",tolerance:"Within tolerance",exception:"Exception",indeterminate:"Not tied — input missing"}[key]||key;
  const statusMark={tied:"✓",tolerance:"◇",exception:"▲",indeterminate:"■"}[key]||"—";
  const proofNote={tied:"Difference fully explained by the reconciling items above.",tolerance:"Residual is below tolerance but is not explained. Classify it or accept it with a reason.",exception:"Residual exceeds tolerance and is unexplained.",indeterminate:"No residual can be computed while an input is unavailable. An unavailable input is not a zero."}[key];

  const [openState,setOpen]=React.useState(defaultExpanded);
  const open=expandedProp!==undefined?expandedProp:openState;
  const toggle=()=>{if(expandedProp===undefined)setOpen(o=>!o);onToggleDetail?.(!open)};
  const [reasonOpen,setReasonOpen]=React.useState(false);
  const [reason,setReason]=React.useState("");
  const fx=useFormat({currency});
  const fmt=v=>notation==="currency"?fx.money(Number(v)||0,{decimals:digits??2,parens:false}):fx.number(Number(v)||0,{decimals:digits??2});
  const signed=v=>`${v<0?"\u2212":"+"}${fmt(Math.abs(v))}`;
  const cond=c=>c?<span className="mds-tie__cond">{CONDITION_GLYPH[String(c).toLowerCase()]||"·"} {String(c).charAt(0).toUpperCase()+String(c).slice(1)}</span>:null;

  // Nothing consequential is actionable while an input is missing — the residual that acceptance or
  // resolution would dispose of has not been computed. Closing the gap is the only forward move.
  const primary=indeterminate
    ?(onInvestigate?{fn:onInvestigate,label:"Investigate missing input"}:onLinkEvidence?{fn:onLinkEvidence,label:"Link the missing source"}:null)
    :onResolve?{fn:onResolve,label:"Resolve"}:key==="exception"&&onInvestigate?{fn:onInvestigate,label:"Investigate residual"}:key==="tolerance"&&onAccept?{fn:()=>requireReason?setReasonOpen(true):onAccept(""),label:acceptLabel}:onExport?{fn:onExport,label:"Export tie-out proof"}:null;

  return <section className={`mds-tie mds-tie--${key}${className?" "+className:""}`} {...rest}>
    <header className="mds-tie__head">
      <div><h3>{title}</h3>{context?<p>{context}</p>:null}</div>
      <div className="mds-tie__tags">
        <span className="mds-tie__status"><span aria-hidden="true">{statusMark}</span>{statusText}</span>
        <span className="mds-tie__type">{TYPES[type]||type}</span>
      </div>
    </header>
    <div className="mds-tie__rows">
      <div className="mds-tie__row"><span className="mds-tie__srclabel">{sourceA?.label||"Source A"}{cond(sourceA?.condition)}{sourceA?.count!=null&&condition(sourceA)!=="restricted"?<span className="mds-tie__cond">{fx.number(sourceA.count, { decimals: 0 })} records</span>:null}</span><span className="mds-tie__value">{av===null?"—":fmt(a)}</span></div>
      <div className="mds-tie__row mds-tie__row--rule"><span className="mds-tie__srclabel">{sourceB?.label||"Source B"}{cond(sourceB?.condition)}{sourceB?.count!=null&&condition(sourceB)!=="restricted"?<span className="mds-tie__cond">{fx.number(sourceB.count, { decimals: 0 })} records</span>:null}</span><span className="mds-tie__value">{bv===null?"—":fmt(b)}</span></div>
      <div className="mds-tie__row"><span>Gross difference</span><span className="mds-tie__value">{indeterminate?"—":signed(gross)}</span></div>
      <div className="mds-tie__row mds-tie__row--rule"><span>{list.length?<button type="button" className="mds-tie__toggle" onClick={toggle} aria-expanded={open}><span aria-hidden="true">{open?"▾":"▸"}</span>Explained · {explainedItems.length} reconciling item{explainedItems.length===1?"":"s"}</button>:"Explained"}</span><span className="mds-tie__value">{explainedUsable?signed(explained):"—"}</span></div>
    </div>
    {open&&list.length?<div className="mds-tie__items">
      <div className="mds-tie__item mds-tie__item--head"><span>Item</span><span>Amount</span><span>Condition</span><span>Evidence</span></div>
      {list.map((x,i)=><div className={`mds-tie__item${x.explained===false?" mds-tie__item--unexplained":""}`} key={x.id||x.label||i}><span>{condition(x)==="restricted"?"Restricted reconciling item":x.label}</span><span className="mds-tie__value">{condition(x)==="restricted"?"Restricted":num(x.value)===null||unavailableValue(x)?"—":signed(Number(x.value))}</span><span>{String(x.condition||(x.explained===false?"exception":"confirmed")).replace(/^./,s=>s.toUpperCase())}</span><span>{condition(x)==="restricted"?"Restricted":x.evidence||"No evidence"}</span></div>)}
    </div>:null}
    <div className="mds-tie__proof">
      <div><strong>Unexplained residual</strong><em>{proofNote}</em></div>
      {indeterminate?<span className="mds-tie__value" aria-label="Residual not computable — an input is missing">—</span>:<VarianceCell actual={unexplained} expected={0} tolerance={tolerance} notation={notation==="currency"?"accounting":notation} currency={currency} unit={unit} digits={digits} showDelta={false} showRelative={false}/>}
    </div>
    {indeterminate?<div className="mds-tie__block"><b>Cannot tie out.</b> {gaps.join(" · ")}. Supply the input, or record it as a reconciling item with its own evidence. Acceptance and resolution stay unavailable until then.</div>:null}
    {secondary?<div className="mds-tie__secondary"><span>{secondary.label}</span><VarianceCell layout="inline" actual={secondary.actual} expected={secondary.expected} tolerance={secondary.tolerance||0} notation={secondary.notation||"quantity"} unit={secondary.unit} digits={secondary.digits} comparisonLabel={secondary.comparisonLabel} showRelative={false}/></div>:null}
    {reasonOpen?<div className="mds-tie__reason">
      <label htmlFor={`${uid}-reason`}>Reason for accepting an unexplained residual</label>
      <textarea id={`${uid}-reason`} value={reason} onChange={e=>setReason(e.target.value)} placeholder="Name the policy or judgment that permits acceptance."/>
      <div className="row"><Button size="sm" variant="ghost" onClick={()=>{setReasonOpen(false);setReason("")}}>Cancel</Button><Button size="sm" disabled={!reason.trim()} onClick={()=>{onAccept?.(reason.trim());setReasonOpen(false);setReason("")}}>Confirm acceptance</Button></div>
    </div>:null}
    {onInvestigate||onAddItem||onLinkEvidence||onAccept||onEscalate||onReopen||onResolve||onExport?<footer className="mds-tie__actions">
      <span className="spacer"/>
      {onAddItem?<Button size="sm" variant="ghost" onClick={onAddItem}>Add reconciling item</Button>:null}
      {onLinkEvidence&&primary?.fn!==onLinkEvidence?<Button size="sm" variant="ghost" onClick={onLinkEvidence}>Link evidence</Button>:null}
      {onExport&&primary?.fn!==onExport?<Button size="sm" variant="ghost" onClick={onExport}>Export proof</Button>:null}
      {onReopen?<Button size="sm" variant="ghost" onClick={onReopen}>Reopen</Button>:null}
      {onEscalate?<Button size="sm" variant="ghost" onClick={onEscalate}>Escalate</Button>:null}
      {onInvestigate&&primary?.fn!==onInvestigate?<Button size="sm" variant="ghost" onClick={onInvestigate}>Investigate residual</Button>:null}
      {onResolve&&indeterminate?<Button size="sm" variant="ghost" disabled title="Unavailable — the residual has not been computed">Resolve</Button>:null}
      {onAccept&&primary?.label!==acceptLabel?<Button size="sm" variant="ghost" disabled={indeterminate} title={indeterminate?"Unavailable — an input is missing, so there is no residual to accept":undefined} onClick={()=>requireReason?setReasonOpen(true):onAccept("")}>{acceptLabel}</Button>:null}
      {primary?<Button size="sm" onClick={primary.fn}>{primary.label}</Button>:null}
    </footer>:null}
    {reviewer||reviewedAt||evidence||evidenceLabel?<footer className="mds-tie__foot">{[reviewer&&`Reviewed by ${reviewer}`,reviewedAt,evidenceLabel,typeof evidence==="string"?evidence:null].filter(Boolean).join(" · ")}{typeof evidence==="string"?null:evidence}</footer>:null}
  </section>;
}
