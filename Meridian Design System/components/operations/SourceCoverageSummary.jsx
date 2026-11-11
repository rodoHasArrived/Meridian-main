// Meridian SourceCoverageSummary — is the evidence set behind this value, population, or
// attestation complete? Always exposes the denominator, keeps "not applicable" separate from
// "missing", states the freshness threshold and the weighting basis, and never collapses
// coverage into an unexplained score. Every category is a filter on the population.
import React from "react";
import { AnalyticalField } from "../charts/AnalyticalField";

const CATS=[
  {key:"confirmed",label:"Confirmed",glyph:"●",note:"Current, valid source available"},
  {key:"provisional",label:"Provisional",glyph:"◐",note:"Temporary source awaiting confirmation"},
  {key:"stale",label:"Stale",glyph:"◔",note:"Source exceeds the freshness threshold"},
  {key:"missing",label:"Missing",glyph:"■",note:"Required source unavailable"},
  {key:"na",label:"Not applicable",glyph:"—",note:"Source not required for this population"},
];
const MAP={confirmed:"confirmed",reconciled:"confirmed",complete:"confirmed",verified:"confirmed",ok:"confirmed",matched:"confirmed",certified:"confirmed",provisional:"provisional",preliminary:"provisional",unaudited:"provisional",estimated:"provisional",pending:"provisional",stale:"stale",delayed:"stale",aged:"stale",missing:"missing",absent:"missing",unavailable:"missing",exception:"missing",failed:"missing",na:"na","not applicable":"na","n/a":"na",none:"na"};
const bucket=v=>MAP[String(v||"").trim().toLowerCase().replace(/[_-]+/g," ")]||"missing";

let injected=false;function inject(){if(injected||typeof document==="undefined")return;injected=true;const el=document.createElement("style");el.setAttribute("data-mds","source-coverage-summary");el.textContent=`
.mds-scs{border:1px solid var(--border,#D2CDC3);background:var(--surface-plate,var(--bg-light,#F8F7F3));font-family:var(--font-body);color:var(--text-secondary,#535C65)}
.mds-scs--compact{display:flex;align-items:baseline;gap:9px;flex-wrap:wrap;padding:6px 9px;border:0;background:transparent}
.mds-scs__count{font:600 12px var(--font-data,monospace);font-variant-numeric:tabular-nums;color:var(--text-primary,#20252B)}
.mds-scs__text{font:10px var(--font-data,monospace);color:var(--text-muted,#606870)}
.mds-scs__head{padding:9px 11px;border-bottom:1px solid var(--border-divider,#E2DED5)}
.mds-scs__head h3{margin:0;font-size:13px;color:var(--text-primary,#20252B)}
.mds-scs__denom{margin-top:2px;font:10px var(--font-data,monospace);font-variant-numeric:tabular-nums;color:var(--text-primary,#20252B)}
.mds-scs__body{padding:10px 11px}
.mds-scs__table{width:100%;border-collapse:collapse;margin-top:9px}
.mds-scs__table th{text-align:left;font:9px var(--font-data,monospace);text-transform:uppercase;letter-spacing:.05em;color:var(--text-muted,#606870);font-weight:600;padding:0 8px 4px 0;border-bottom:1px solid var(--border-strong,#AAA49A)}
.mds-scs__table td{padding:0;border-bottom:1px solid var(--border-divider,#E2DED5)}
.mds-scs__table th:nth-child(2),.mds-scs__table th:nth-child(3){text-align:right}
.mds-scs__row{display:grid;grid-template-columns:1fr 52px 52px;gap:8px;align-items:baseline;width:100%;min-height:26px;padding:4px 0;background:none;border:0;border-left:2px solid transparent;font:inherit;color:inherit;text-align:left;cursor:default}
.mds-scs__row--action{cursor:pointer}
.mds-scs__row--action:hover{background:var(--bg-hover,#EDEFF0)}
.mds-scs__row--action:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:-2px}
.mds-scs__row[data-selected="true"]{border-left-color:var(--selected,#496C8F);background:var(--selected-surface,var(--bg-active,#E7EDF2))}
.mds-scs__name{font-size:11px;color:var(--text-primary,#20252B)}
.mds-scs__note{display:block;font:9px/13px var(--font-body);color:var(--text-muted,#606870)}
.mds-scs__glyph{display:inline-block;width:11px;text-align:center;margin-right:5px;color:var(--text-muted,#606870)}
.mds-scs__n,.mds-scs__pct{font:11px var(--font-data,monospace);font-variant-numeric:tabular-nums;text-align:right;color:var(--text-primary,#20252B)}
.mds-scs__pct{color:var(--text-muted,#606870);font-size:10px}
.mds-scs__row[data-cat="missing"][data-nonzero="true"] .mds-scs__n{color:var(--red-dim,#873832);font-weight:600}
.mds-scs__row[data-cat="stale"][data-nonzero="true"] .mds-scs__n{color:var(--orange-dim,#725012);font-weight:600}
.mds-scs__foot{display:flex;gap:12px;flex-wrap:wrap;padding:7px 11px;border-top:1px solid var(--border-divider,#E2DED5);font:9px/14px var(--font-data,monospace);color:var(--text-muted,#606870)}
`;document.head.appendChild(el)}

export function SourceCoverageSummary({
  sources=[],counts,total,compact=true,title="Source coverage",
  onSelect,onFilter,selectedCategory,
  freshnessThreshold,weighting="record",weightLabel,unit,asOf,
  showField=true,showNote=true,className="",...rest
}){
  inject();
  const tally=CATS.reduce((a,c)=>{a[c.key]=0;return a},{});
  if(counts)CATS.forEach(c=>{tally[c.key]=Number(counts[c.key])||0});
  else sources.forEach(s=>{tally[bucket(s.condition||s.state)]+=1});
  const population=total!=null?Number(total):CATS.reduce((n,c)=>n+tally[c.key],0);
  const required=Math.max(0,population-tally.na);
  const pct=n=>required?`${Math.round(n/required*1000)/10}%`:"—";
  const weightNoun=weightLabel||(weighting==="value"?`value-weighted${unit?` (${unit})`:""}`:"record-weighted");
  const summary=`${tally.confirmed} of ${required} confirmed`+
    (tally.provisional?` · ${tally.provisional} provisional`:"")+
    (tally.stale?` · ${tally.stale} stale`:"")+
    ` · ${tally.missing} missing`;

  if(compact)return <div className={`mds-scs mds-scs--compact${className?" "+className:""}`} role="status" {...rest}>
    <span className="mds-scs__count">{tally.confirmed}/{required}</span>
    <span className="mds-scs__text">{summary}{tally.na?` · ${tally.na} n/a`:""}</span>
    {freshnessThreshold?<span className="mds-scs__text">Freshness {freshnessThreshold}</span>:null}
  </div>;

  const rowProps=cat=>onFilter?{as:"button",type:"button",onClick:()=>onFilter(cat.key,{count:tally[cat.key],required,population})}:{};
  return <section className={`mds-scs${className?" "+className:""}`} {...rest}>
    <header className="mds-scs__head">
      <h3>{title}</h3>
      <div className="mds-scs__denom">{required} required sources · {summary}{tally.na?` · ${tally.na} not applicable`:""}</div>
    </header>
    <div className="mds-scs__body">
      {showField&&sources.length&&sources.length<=240?<AnalyticalField items={sources} columns={Math.min(12,Math.max(6,Math.ceil(Math.sqrt(sources.length))))} onSelect={onSelect} summary={summary}/>:null}
      <table className="mds-scs__table">
        <thead><tr><th scope="col">Condition</th><th scope="col">Count</th><th scope="col">Share</th></tr></thead>
        <tbody>{CATS.map(c=>{const n=tally[c.key];const{as,...handlers}=rowProps(c);const Tag=as||"div";
          return <tr key={c.key}><td colSpan={3}>
            <Tag className={`mds-scs__row${onFilter?" mds-scs__row--action":""}`} data-cat={c.key} data-nonzero={n>0?"true":undefined} data-selected={selectedCategory===c.key?"true":undefined} aria-pressed={onFilter?selectedCategory===c.key:undefined} {...handlers}>
              <span className="mds-scs__name"><span className="mds-scs__glyph" aria-hidden="true">{c.glyph}</span>{c.label}{showNote?<span className="mds-scs__note">{c.note}</span>:null}</span>
              <span className="mds-scs__n">{n}</span>
              <span className="mds-scs__pct">{c.key==="na"?"—":pct(n)}</span>
            </Tag>
          </td></tr>;})}
        </tbody>
      </table>
    </div>
    <footer className="mds-scs__foot">
      <span>Denominator: {required} required of {population} in population</span>
      <span>Basis: {weightNoun}</span>
      {freshnessThreshold?<span>Freshness threshold: {freshnessThreshold}</span>:null}
      {asOf?<span>As of {asOf}</span>:null}
    </footer>
  </section>;
}
