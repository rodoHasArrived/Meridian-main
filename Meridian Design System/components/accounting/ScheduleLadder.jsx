// Meridian ScheduleLadder — a security's terms make a falsifiable prediction; this is where it
// gets checked. Two named lanes (expected, modelled from terms · actual, received from a source)
// on one continuous period ladder. Past periods carry a variance; future periods carry expected
// only and are marked undelivered — absence never renders as agreement. Applicability is decided
// by the security's PROPERTIES, never by an asset-class label.
import React from "react";
import { VarianceCell } from "./VarianceCell";
import { formatMoney, formatNumber } from "../core/format.js";

const NOTATION={factor:{variance:"number",digits:8},rate:{variance:"percent",digits:4},percent:{variance:"percent",digits:3},bp:{variance:"bp",digits:0},currency:{variance:"accounting",digits:2},accounting:{variance:"accounting",digits:2},quantity:{variance:"quantity",digits:0},number:{variance:"number",digits:2}};
const num=v=>{const n=Number(v);return Number.isFinite(n)?n:null};
const has=v=>v!==null&&v!==undefined&&v!==""&&num(v)!==null;
const stamp=v=>{if(!v)return null;const t=Date.parse(v);return Number.isFinite(t)?t:null};

function laneFormat({notation="factor",digits,currency,unit}){
  const n=NOTATION[notation]||NOTATION.number,d=digits??n.digits;
  const cur=n.variance==="accounting"?(currency||"USD"):null;
  const suffix=n.variance==="percent"?"%":n.variance==="bp"?" bp":(unit?` ${unit}`:"");
  return v=>{if(v==null)return"—";const body=cur?formatMoney(Math.abs(v),{currency:cur,decimals:d,parens:false}):formatNumber(Math.abs(v),{decimals:d,suffix});return v<0?(cur?`(${body})`:`\u2212${body}`):body};
}

// Applicability. `requires` is a property key, a list of keys (all must hold),
// or {any:[…]} / {all:[…]}; nested arrays inside a list read as any-of.
export function scheduleApplies(properties,requires){
  if(!requires)return true;
  const p=properties||{};
  const truthy=k=>{const v=p[k];return!(v===undefined||v===null||v===false||v===""||v===0||v==="false"||v==="no")};
  const all=list=>list.every(r=>scheduleApplies(p,r));
  if(typeof requires==="string")return truthy(requires);
  if(typeof requires==="function")return!!requires(p);
  if(Array.isArray(requires))return all(requires);
  if(requires.any)return requires.any.some(r=>scheduleApplies(p,r));
  if(requires.all)return all(requires.all);
  return true;
}

let injected=false;function inject(){if(injected||typeof document==="undefined")return;injected=true;const el=document.createElement("style");el.setAttribute("data-mds","schedule-ladder");el.textContent=`
.mds-sl{border:1px solid var(--border,#D2CDC3);background:var(--surface-well,var(--bg-light,#F8F7F3));font-family:var(--font-body);color:var(--text-secondary,#535C65)}
.mds-sl__head{display:flex;flex-wrap:wrap;gap:4px 12px;align-items:baseline;padding:9px 11px;border-bottom:1px solid var(--border-divider,#E2DED5)}
.mds-sl__title{margin:0;font-size:13px;font-weight:600;color:var(--text-primary,#20252B)}
.mds-sl__object,.mds-sl__meta{font:10px/14px var(--font-data,monospace);color:var(--text-muted,#606870)}
.mds-sl__meta{margin-left:auto;text-align:right}
.mds-sl__next{display:grid;grid-template-columns:auto 1fr;gap:2px 14px;padding:9px 11px;border-bottom:1px solid var(--border-divider,#E2DED5);background:var(--bg,#F2F0EB)}
.mds-sl__nextlabel{font:600 9px/14px var(--font-data,monospace);letter-spacing:.05em;text-transform:uppercase;color:var(--text-muted,#606870)}
.mds-sl__nextline{font-size:11px;line-height:16px;color:var(--text-primary,#20252B);font-family:var(--font-data,monospace)}
.mds-sl__tally{grid-column:2;font:10px/14px var(--font-body);color:var(--text-muted,#606870)}
.mds-sl__scroll{overflow:auto}
.mds-sl table{width:100%;border-collapse:collapse}
.mds-sl th,.mds-sl td{padding:6px 11px;text-align:left;vertical-align:top;border-bottom:1px solid var(--border-divider,#E2DED5)}
.mds-sl thead th{font:600 9px/13px var(--font-data,monospace);letter-spacing:.05em;text-transform:uppercase;color:var(--text-muted,#606870);white-space:nowrap}
.mds-sl thead th span{display:block;font:400 9px/13px var(--font-body);letter-spacing:0;text-transform:none}
.mds-sl th[data-num],.mds-sl td[data-num]{text-align:right}
.mds-sl__period{font:600 11px/16px var(--font-data,monospace);color:var(--text-primary,#20252B);white-space:nowrap}
.mds-sl__date{font:10px/16px var(--font-data,monospace);color:var(--text-muted,#606870);white-space:nowrap}
.mds-sl__exp{font:600 12px/16px var(--font-data,monospace);font-variant-numeric:tabular-nums;color:var(--text-primary,#20252B)}
.mds-sl__sub{display:block;font:400 10px/14px var(--font-body);color:var(--text-muted,#606870)}
.mds-sl__wait{display:inline-grid;justify-items:end;font-family:var(--font-data,monospace)}
.mds-sl__wait b{font:600 11px/16px var(--font-data,monospace);color:var(--pending,#7A6B52)}
.mds-sl__wait span{font:10px/14px var(--font-body);color:var(--text-muted,#606870)}
.mds-sl__mark{display:inline-block;width:8px;margin-right:3px;text-align:center}
.mds-sl tbody tr[data-projected="true"] .mds-sl__period,.mds-sl tbody tr[data-projected="true"] .mds-sl__exp{font-weight:400}
.mds-sl tbody tr[data-current="true"]{background:var(--bg-active,#E7EDF2)}
.mds-sl__rule td{padding:5px 11px;background:var(--bg,#F2F0EB);font:10px/14px var(--font-data,monospace);color:var(--text-muted,#606870);border-top:1px solid var(--border,#D2CDC3)}
.mds-sl__trace{background:none;border:0;padding:0;font:10px/14px var(--font-body);color:var(--link,#2A5F96);cursor:pointer;text-align:left}
.mds-sl__trace:hover{text-decoration:underline}
.mds-sl__trace:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:1px}
.mds-sl__foot{display:grid;gap:2px;padding:9px 11px;border-top:1px solid var(--border-divider,#E2DED5);font:10px/15px var(--font-body);color:var(--text-muted,#606870)}
.mds-sl--na{padding:9px 11px;font:10px/15px var(--font-body);color:var(--text-muted,#606870)}
`;document.head.appendChild(el)}

function resolve(row,tolAbs,nearFactor){
  if(row.state)return row.state;
  if(row.projected)return"undelivered";
  const a=num(row.actual),e=num(row.expected);
  if(row.notComparable||row.condition==="na")return"not-comparable";
  if(!has(row.actual))return e!=null?"undelivered":"missing-reference";
  if(!has(row.expected))return"missing-reference";
  const abs=Math.abs(a-e);
  return abs<=tolAbs?(tolAbs>0&&abs>=tolAbs*nearFactor?"near":"within"):"outside";
}

export function ScheduleLadder({
  title="Schedule",kind,object,asOf,properties,requires,notApplicableReason,hideWhenNotApplicable=true,
  expectedLabel="Expected",expectedSource,basis,actualLabel="Actual",actualSource,
  rows=[],notation="factor",digits,currency,unit,
  tolerance,tolerancePct,nearFactor=0.8,
  periodLabel="Period",dateLabel="Payable",
  showNext=true,showTally=true,note,pendingText,
  selectedId,onSelectRow,onOpenTrace,className="",...rest
}){
  inject();
  const applicable=scheduleApplies(properties,requires);
  if(!applicable&&hideWhenNotApplicable)return null;
  if(!applicable)return <section className={`mds-sl mds-sl--na${className?" "+className:""}`} {...rest}>
    <strong style={{color:"var(--text-secondary,#535C65)"}}>{title}</strong> — not applicable.{" "}
    {notApplicableReason||`${object||"This security"} carries no such term.`}
  </section>;

  const n=NOTATION[notation]||NOTATION.number;
  const fmt=laneFormat({notation,digits,currency,unit});
  const cut=stamp(asOf);

  const prepared=rows.map((r,i)=>{
    const rowCut=stamp(r.date);
    const projected=r.projected!==undefined?!!r.projected
      :(cut!=null&&rowCut!=null?rowCut>cut:!has(r.actual));
    const tolAbs=r.tolerance!=null?Math.abs(r.tolerance)
      :r.tolerancePct!=null&&has(r.expected)?Math.abs(num(r.expected))*r.tolerancePct/100
      :tolerance!=null?Math.abs(tolerance)
      :tolerancePct!=null&&has(r.expected)?Math.abs(num(r.expected))*tolerancePct/100:0;
    const row={...r,projected,tolAbs,id:r.id??r.period??String(i)};
    return{...row,resolved:resolve(row,tolAbs,r.nearFactor??nearFactor)};
  });

  const delivered=prepared.filter(r=>!r.projected&&(r.resolved==="within"||r.resolved==="near"||r.resolved==="outside"));
  const tally={within:0,near:0,outside:0};
  delivered.forEach(r=>{tally[r.resolved]=(tally[r.resolved]||0)+1});
  const firstProjected=prepared.findIndex(r=>r.projected||r.resolved==="undelivered");
  const next=firstProjected>=0?prepared[firstProjected]:null;
  const ruleAfter=firstProjected>0?prepared[firstProjected-1].id:null;

  const tolLine=tolerance!=null?`${fmt(Math.abs(tolerance))} tolerance`:tolerancePct!=null?`${tolerancePct}% relative tolerance`:null;
  const waiting=pendingText||"Not yet delivered";

  return <section className={`mds-sl${className?" "+className:""}`} {...rest}>
    <header className="mds-sl__head">
      <h3 className="mds-sl__title">{title}</h3>
      {kind?<span className="mds-sl__object">{kind}</span>:null}
      {object?<span className="mds-sl__object">{object}</span>:null}
      <span className="mds-sl__meta">
        {asOf?<>As of {asOf}<br/></>:null}
        {tolLine||null}
      </span>
    </header>

    {showNext&&next?<div className="mds-sl__next">
      <span className="mds-sl__nextlabel">Next</span>
      <span className="mds-sl__nextline">
        {next.period}{next.date?` · ${next.date}`:""} · {expectedLabel.toLowerCase()} {fmt(num(next.expected))}
        {actualSource?` · awaiting ${actualSource}`:""}
      </span>
      {showTally?<span className="mds-sl__tally">
        {delivered.length} of {prepared.length} periods delivered · {tally.within} agreed
        {tally.near?` · ${tally.near} to review`:""}
        {tally.outside?` · ${tally.outside} outside tolerance`:""}
        {!tally.near&&!tally.outside&&delivered.length?" · no exceptions":""}
      </span>:null}
    </div>:null}

    <div className="mds-sl__scroll">
      <table>
        <caption className="mds-sl__object" style={{captionSide:"bottom",padding:"6px 11px",textAlign:"left"}}>
          {title}{object?` · ${object}`:""} — {expectedLabel} ({expectedSource||basis||"modelled from terms"}) versus {actualLabel}{actualSource?` (${actualSource})`:""}.
        </caption>
        <thead><tr>
          <th scope="col">{periodLabel}</th>
          <th scope="col">{dateLabel}</th>
          <th scope="col" data-num="true">{expectedLabel}<span>{expectedSource||basis||"Modelled from terms"}</span></th>
          <th scope="col" data-num="true">{actualLabel}<span>{actualSource||"Received"}</span></th>
        </tr></thead>
        <tbody>{prepared.map(r=>{
          const cells=[
            <td key="p" className="mds-sl__period">{r.period}{r.label?<span className="mds-sl__sub">{r.label}</span>:null}</td>,
            <td key="d" className="mds-sl__date">{r.date||"—"}{r.dateNote?<span className="mds-sl__sub">{r.dateNote}</span>:null}</td>,
            <td key="e" data-num="true"><span className="mds-sl__exp">{fmt(num(r.expected))}</span>{r.expectedNote?<span className="mds-sl__sub">{r.expectedNote}</span>:null}</td>,
            <td key="a" data-num="true">{r.resolved==="undelivered"
              ? <span className="mds-sl__wait"><b><span className="mds-sl__mark" aria-hidden="true">◇</span>{waiting}</b><span>{r.actualDue?`Due ${r.actualDue}`:actualSource?`From ${actualSource}`:""}</span></span>
              : <VarianceCell actual={r.actual} expected={r.expected} tolerance={r.tolAbs}
                  nearFactor={r.nearFactor??nearFactor} notation={n.variance} digits={digits??n.digits}
                  currency={currency} unit={unit} condition={r.condition} conditionDetail={r.conditionDetail}
                  comparisonLabel={r.comparisonLabel||"terms"} explanation={r.note} state={r.state}
                  notComparable={r.notComparable} showRelative={false}
                  selected={selectedId!=null&&selectedId===r.id}
                  onSelect={onSelectRow?()=>onSelectRow(r):undefined}/>}
              {onOpenTrace&&r.resolved!=="undelivered"?<div><button type="button" className="mds-sl__trace" onClick={()=>onOpenTrace(r)}>Trace</button></div>:null}
            </td>,
          ];
          const tr=<tr key={r.id} data-projected={r.projected?"true":undefined} data-current={r.current?"true":undefined} data-state={r.resolved}>{cells}</tr>;
          if(ruleAfter!=null&&r.id===ruleAfter)return[tr,
            <tr key={`${r.id}-rule`} className="mds-sl__rule"><td colSpan={4}>
              {asOf?`As of ${asOf} — `:""}rows below are projected from terms; no actual has been received.
            </td></tr>];
          return tr;
        })}</tbody>
      </table>
    </div>

    <div className="mds-sl__foot">
      {basis?<span>Expected basis: {basis}</span>:null}
      {actualSource?<span>Actual source: {actualSource}</span>:null}
      {note?<span>{note}</span>:null}
    </div>
  </section>;
}

// Renders every schedule whose `requires` the security's properties satisfy, in declared order.
export function ScheduleLadderSet({properties,schedules=[],emptyText,showNotApplicable=false,className="",...rest}){
  inject();
  const applicable=schedules.filter(s=>scheduleApplies(properties,s.requires));
  const shown=showNotApplicable?schedules:applicable;
  if(!shown.length)return <section className={`mds-sl mds-sl--na${className?" "+className:""}`} {...rest}>
    {emptyText||"No term-bearing schedules on this security — nothing to check."}
  </section>;
  return <div className={className||undefined} style={{display:"grid",gap:14}} {...rest}>
    {shown.map((s,i)=><ScheduleLadder key={s.id||s.title||i} {...s} properties={properties} hideWhenNotApplicable={!showNotApplicable}/>)}
  </div>;
}
