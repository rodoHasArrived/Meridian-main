// Meridian DurationContribution — what drives aggregate duration. Weight, instrument
// duration, and contribution are distinct quantities and are never used interchangeably:
// contribution is the ranked measure, weight and duration stay visible beside it. The zero
// datum is always on the face so offsetting contributions read correctly, contributions are
// tied back to the stated total, and missing weights or durations are exposed rather than
// treated as zero. Scenario values are labelled as scenarios, never as forecasts.
import React from "react";
import { conditionMeta } from "../operations/conditions";

const glyph=condition=>condition?conditionMeta(condition).glyph:null;
const finite=value=>typeof value==="number"&&Number.isFinite(value);
const unavailable=condition=>condition&&["restricted","missing","na"].includes(conditionMeta(condition).key);
const RANKS=[{id:"contribution",label:"Contribution"},{id:"difference",label:"Difference"},{id:"weight",label:"Weight"},{id:"label",label:"Name"}];

let injected=false;function inject(){if(injected||typeof document==="undefined")return;injected=true;const el=document.createElement("style");el.setAttribute("data-mds","duration-contribution");el.textContent=`
.mds-dc{font-family:var(--font-body);color:var(--text-secondary,#535C65)}
.mds-dc__head{display:flex;gap:12px;align-items:baseline;flex-wrap:wrap;margin-bottom:6px}
.mds-dc__def{font:10px var(--font-data,monospace);color:var(--text-primary,#20252B)}
.mds-dc__ranks{margin-left:auto;display:flex;border:1px solid var(--border,#D2CDC3);border-radius:2px;overflow:hidden}
.mds-dc__ranks button{appearance:none;border:0;border-left:1px solid var(--border,#D2CDC3);background:var(--surface-plate,#F8F7F3);font:9px var(--font-data,monospace);text-transform:uppercase;letter-spacing:.04em;color:var(--text-muted,#606870);padding:3px 8px;cursor:pointer}
.mds-dc__ranks button:first-child{border-left:0}
.mds-dc__ranks button[aria-pressed="true"]{background:var(--selected-surface,var(--bg-active,#E7EDF2));color:var(--text-primary,#20252B);font-weight:600}
.mds-dc__ranks button:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:-2px}
.mds-dc__unit{flex-basis:100%;font:9px var(--font-data,monospace);color:var(--text-muted,#606870)}
.mds-dc__colhead,.mds-dc__row{display:grid;grid-template-columns:minmax(118px,1.3fr) 64px 64px 1.5fr 76px 76px;gap:10px;align-items:center}
.mds-dc__colhead{font:600 9px var(--font-data,monospace);text-transform:uppercase;letter-spacing:.04em;color:var(--text-muted,#606870);padding-bottom:3px;border-bottom:1px solid var(--border-strong,#AAA49A)}
.mds-dc__colhead span:nth-child(n+2){text-align:right}
.mds-dc__colhead span:nth-child(4){text-align:left}
.mds-dc__row{min-height:32px;width:100%;border:0;border-bottom:1px solid var(--border-divider,#E2DED5);border-left:2px solid transparent;background:none;padding:0;text-align:left;font:inherit;color:inherit;cursor:pointer}
.mds-dc__row:hover{background:var(--bg-hover,#EDEFF0)}
.mds-dc__row:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:-2px}
.mds-dc__row[aria-selected="true"]{border-left-color:var(--selected,#496C8F);background:var(--selected-surface,var(--bg-active,#E7EDF2))}
.mds-dc__label{font-size:11px;font-weight:600;color:var(--text-primary,#20252B);overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
.mds-dc__label small{display:block;font:9px var(--font-data,monospace);font-weight:400;color:var(--text-muted,#606870)}
.mds-dc__num{text-align:right;font:11px var(--font-data,monospace);font-variant-numeric:tabular-nums;color:var(--text-primary,#20252B)}
.mds-dc__num[data-missing="true"]{color:var(--red-dim,#873832)}
.mds-dc__track{position:relative;height:16px;background:var(--surface-recessed,var(--bg-medium,#EBE8E1))}
.mds-dc__zero{position:absolute;top:-2px;bottom:-2px;border-left:1px solid var(--border-strong,#AAA49A)}
.mds-dc__bar{position:absolute;top:3px;height:6px;min-width:1px;background:var(--series-actual,var(--brand-axis,#29323A))}
.mds-dc__bar[data-negative="true"]{background:var(--series-prior,var(--brand-field,#5D7286))}
.mds-dc__cmp{position:absolute;bottom:2px;height:0;border-top:2px dashed var(--series-scenario,var(--scenario-signal,#C56343))}
.mds-dc__row[aria-selected="true"] .mds-dc__bar{background:var(--selected,#496C8F)}
.mds-dc__mark{display:inline-block;width:9px;text-align:center;margin-right:3px;color:var(--text-muted,#606870)}
.mds-dc__totals{display:grid;grid-template-columns:minmax(118px,1.3fr) 64px 64px 1.5fr 76px 76px;gap:10px;align-items:baseline;padding-top:5px;border-top:2px solid var(--border-strong,#AAA49A);font:600 11px var(--font-data,monospace);font-variant-numeric:tabular-nums;color:var(--text-primary,#20252B)}
.mds-dc__totals span:nth-child(n+2){text-align:right}
.mds-dc__tie{margin:5px 0 0;font:9px/14px var(--font-data,monospace);color:var(--text-muted,#606870)}
.mds-dc__tie[data-ok="false"]{color:var(--orange-dim,#725012)}
.mds-dc__table{width:100%;border-collapse:collapse;margin-top:10px}
.mds-dc__table caption{text-align:left;font:9px var(--font-data,monospace);text-transform:uppercase;letter-spacing:.05em;color:var(--text-muted,#606870);padding-bottom:3px}
.mds-dc__table th,.mds-dc__table td{border-bottom:1px solid var(--border-divider,#E2DED5);padding:4px 8px;font:10px var(--font-data,monospace);font-variant-numeric:tabular-nums;text-align:right}
.mds-dc__table th[scope="row"],.mds-dc__table thead th:first-child{text-align:left}
.mds-dc__table thead th{color:var(--text-muted,#606870);border-bottom:1px solid var(--border-strong,#AAA49A);font-weight:600;text-transform:uppercase;font-size:9px}
.mds-dc__table tr[data-total="true"] td,.mds-dc__table tr[data-total="true"] th{border-top:2px solid var(--border-strong,#AAA49A);font-weight:600;color:var(--text-primary,#20252B)}
.mds-dc--sr .mds-dc__table{position:absolute;width:1px;height:1px;overflow:hidden;clip:rect(0 0 0 0)}
@media (max-width:640px){.mds-dc__rows,.mds-dc__colhead,.mds-dc__totals{display:none}.mds-dc--sr .mds-dc__table{position:static;width:100%;height:auto;clip:auto}}
`;document.head.appendChild(el)}

const n2=v=>Number(v).toFixed(2);
const pct=v=>`${(Math.round(Number(v)*10)/10).toFixed(1)}%`;
const signed=(v,f=n2)=>`${v<0?"\u2212":"+"}${f(Math.abs(v))}`;
const has=v=>v!==null&&v!==undefined&&v!=="";

export function DurationContribution({
  items=[],
  durationDefinition="Effective duration",durationUnit="years",contributionUnit="years",
  comparisonLabel="Benchmark",showComparison=true,total,comparisonTotal,
  scenario,groupBy,unit,asOf,source,
  rank:rankProp,defaultRank="contribution",onRankChange,
  selectedId,onSelect,onOpenTrace,showTable=true,
  valueFormat=n2,description="Duration contribution",className="",...rest
}){
  inject();
  const[own,setOwn]=React.useState(defaultRank);
  const rank=rankProp||own;
  const setRank=r=>{if(rankProp===undefined)setOwn(r);if(onRankChange)onRankChange(r)};

  const rows=items.map(x=>{
    const contribution=has(x.contribution)?Number(x.contribution)
      :(has(x.weight)&&has(x.duration)?Number(x.weight)/100*Number(x.duration):null);
    const cmp=has(x.comparisonContribution)?Number(x.comparisonContribution):null;
    return{...x,contribution,cmp,
      diff:contribution!=null&&cmp!=null?contribution-cmp:null,
      incomplete:!has(x.weight)||!has(x.duration)};
  });
  const sorted=React.useMemo(()=>{
    const c=[...rows];
    if(rank==="contribution")c.sort((a,b)=>Math.abs(b.contribution??0)-Math.abs(a.contribution??0));
    else if(rank==="difference")c.sort((a,b)=>Math.abs(b.diff??0)-Math.abs(a.diff??0));
    else if(rank==="weight")c.sort((a,b)=>(Number(b.weight)||0)-(Number(a.weight)||0));
    else c.sort((a,b)=>String(a.label).localeCompare(String(b.label)));
    return c;
  },[rows,rank]);

  const sum=rows.reduce((n,r)=>n+(r.contribution||0),0);
  const sumCmp=rows.reduce((n,r)=>n+(r.cmp||0),0);
  const sumW=rows.reduce((n,r)=>n+(Number(r.weight)||0),0);
  const stated=has(total)?Number(total):null;
  const residual=stated!=null?stated-sum:0;
  const ties=stated==null||Math.abs(residual)<0.005;
  const incomplete=rows.filter(r=>r.incomplete);

  const lo=Math.min(0,...rows.map(r=>r.contribution??0));
  const hi=Math.max(0,...rows.map(r=>r.contribution??0));
  const span=Math.max(1e-9,hi-lo);
  const zero=(0-lo)/span*100;
  const barOf=v=>{const a=Math.min(0,v),b=Math.max(0,v);return{left:`${(a-lo)/span*100}%`,width:`${(b-a)/span*100}%`}};

  const onKey=(e,i)=>{
    const kids=e.currentTarget.parentElement?.children;if(!kids)return;
    let n=i;
    if(e.key==="ArrowDown")n=Math.min(kids.length-1,i+1);
    else if(e.key==="ArrowUp")n=Math.max(0,i-1);
    else if(e.key==="Home")n=0;
    else if(e.key==="End")n=kids.length-1;
    else if(e.key==="Enter"||e.key===" "){e.preventDefault();onSelect?.(sorted[i],i);return}
    else return;
    e.preventDefault();kids[n]?.focus();
  };

  const hasUnavailableInputs=items.some(item=>unavailable(item.condition)||(["weight","duration","contribution","comparisonContribution"].some(key=>item[key]!=null&&!finite(item[key])))||(!finite(item.contribution)&&!(finite(item.weight)&&finite(item.duration))));
  if(hasUnavailableInputs) {
    const withheld=items.some(item=>item.condition&&conditionMeta(item.condition).key==="restricted");
    return <div className="mds-exposure-unavailable" role="status" aria-label={description} style={{fontFamily:"var(--font-body)",color:"var(--text-primary)",padding:12,border:"1px solid var(--border)",background:"var(--surface-plate,var(--bg-light))"}}>
      <strong>{description}</strong>
      <p>{withheld?"Restricted inputs are withheld. Totals and comparisons are unavailable.":"Input data is unavailable or invalid. Totals and comparisons cannot be computed; missing values are not zero."}</p>
    </div>;
  }

  return <div className={`mds-dc${showTable?"":" mds-dc--sr"}${className?" "+className:""}`} {...rest}>
    <div className="mds-dc__head">
      <span className="mds-dc__def">{durationDefinition}{scenario?` · Scenario: ${scenario}`:""}</span>
      <div className="mds-dc__ranks" role="group" aria-label="Rank by">
        {RANKS.filter(r=>r.id!=="difference"||showComparison).map(r=>
          <button key={r.id} type="button" aria-pressed={rank===r.id} onClick={()=>setRank(r.id)}>{r.label}</button>)}
      </div>
      <span className="mds-dc__unit">
        Duration in {durationUnit} · contribution in {contributionUnit} · weight in percent of {unit||"market value"}
        {groupBy?` · grouped by ${groupBy}`:""}{asOf?` · as of ${asOf}`:""}
        {scenario?" · scenario values are scenarios, not forecasts":""}
      </span>
    </div>

    <div className="mds-dc__colhead">
      <span>{groupBy||"Entity"}</span><span>Weight</span><span>Duration</span>
      <span>Contribution · zero datum</span><span>Contribution</span><span>{showComparison?"vs "+comparisonLabel:"Share"}</span>
    </div>

    <div className="mds-dc__rows" role="listbox" aria-label={description}>{sorted.map((r,i)=>{
      const selected=String(selectedId)===String(r.id);
      const g=glyph(r.condition);
      const bar=barOf(r.contribution??0);
      return <button key={r.id||r.label||i} type="button" role="option" aria-selected={selected}
        tabIndex={selected?0:(selectedId==null&&i===0?0:-1)}
        className="mds-dc__row"
        onClick={()=>onSelect?.(r,i)}
        onDoubleClick={onOpenTrace?()=>onOpenTrace(r):undefined}
        onKeyDown={e=>onKey(e,i)}>
        <span className="mds-dc__label">{r.label}{r.detail?<small>{r.detail}</small>:r.incomplete?<small>Incomplete inputs</small>:null}</span>
        <span className="mds-dc__num" data-missing={!has(r.weight)?"true":undefined}>{has(r.weight)?pct(r.weight):"Missing"}</span>
        <span className="mds-dc__num" data-missing={!has(r.duration)?"true":undefined}>{has(r.duration)?valueFormat(r.duration):"Missing"}</span>
        <span className="mds-dc__track">
          <span className="mds-dc__zero" style={{left:`${zero}%`}}/>
          <span className="mds-dc__bar" data-negative={(r.contribution??0)<0?"true":undefined} style={bar}/>
          {showComparison&&r.cmp!=null?<span className="mds-dc__cmp" style={barOf(r.cmp)}/>:null}
        </span>
        <span className="mds-dc__num">{g?<span className="mds-dc__mark" aria-hidden="true">{g}</span>:null}{r.contribution==null?"—":valueFormat(r.contribution)}</span>
        <span className="mds-dc__num">{showComparison?(r.diff==null?"—":signed(r.diff,valueFormat)):(sum?pct((r.contribution||0)/sum*100):"—")}</span>
      </button>;
    })}</div>

    <div className="mds-dc__totals">
      <span>Total</span><span>{pct(sumW)}</span><span>—</span><span/>
      <span>{valueFormat(sum)}</span>
      <span>{showComparison?signed(sum-(has(comparisonTotal)?Number(comparisonTotal):sumCmp),valueFormat):(sum?pct(100):"—")}</span>
    </div>

    <p className="mds-dc__tie" data-ok={ties?"true":"false"}>
      {stated==null
        ? `Contributions sum to ${valueFormat(sum)} ${contributionUnit}; no aggregate total supplied to tie against.`
        : ties
          ? `Contributions tie to the stated portfolio duration of ${valueFormat(stated)} ${contributionUnit}.`
          : `Contributions sum to ${valueFormat(sum)} against a stated total of ${valueFormat(stated)} — residual ${signed(residual,valueFormat)} ${contributionUnit} is unexplained.`}
      {incomplete.length?` ${incomplete.length} row${incomplete.length===1?"":"s"} missing weight or duration; only explicitly supplied contributions remain available for those rows.`:""}
      {source?` Source: ${source}.`:""}
    </p>

    <table className="mds-dc__table">
      <caption>{description} · {durationDefinition} · {contributionUnit}{asOf?` · as of ${asOf}`:""}{scenario?` · scenario ${scenario}`:""}</caption>
      <thead><tr>
        <th scope="col">{groupBy||"Entity"}</th><th scope="col">Weight</th><th scope="col">Duration</th>
        <th scope="col">Contribution</th>{showComparison?<th scope="col">{comparisonLabel}</th>:null}
        {showComparison?<th scope="col">Difference</th>:null}<th scope="col">Condition</th>
      </tr></thead>
      <tbody>
        {sorted.map((r,i)=><tr key={r.id||i}>
          <th scope="row">{r.label}</th>
          <td>{has(r.weight)?pct(r.weight):"Missing"}</td>
          <td>{has(r.duration)?valueFormat(r.duration):"Missing"}</td>
          <td>{r.contribution==null?"—":valueFormat(r.contribution)}</td>
          {showComparison?<td>{r.cmp==null?"—":valueFormat(r.cmp)}</td>:null}
          {showComparison?<td>{r.diff==null?"—":signed(r.diff,valueFormat)}</td>:null}
          <td>{r.condition||"confirmed"}</td>
        </tr>)}
        <tr data-total="true">
          <th scope="row">Total</th><td>{pct(sumW)}</td><td>—</td><td>{valueFormat(sum)}</td>
          {showComparison?<td>{valueFormat(has(comparisonTotal)?Number(comparisonTotal):sumCmp)}</td>:null}
          {showComparison?<td>{signed(sum-(has(comparisonTotal)?Number(comparisonTotal):sumCmp),valueFormat)}</td>:null}
          <td>—</td>
        </tr>
      </tbody>
    </table>
  </div>;
}
