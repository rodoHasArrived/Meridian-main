// Meridian PostingImpactPreview — the financial, accounting, reporting, and control
// consequences of a journal before it is committed. Results resolve to three classes:
// informational, warning (explicit acknowledgment), blocking (action unavailable).
import React from "react";
import { Button } from "../core/Button";
import { formatNumber, formatAccounting } from "../core/format.js";
import { knownAmount, sumKnown, optionalSide } from "./knownAmounts.js";

const CLASSES={blocking:{label:"Blocking",tone:"stop",glyph:"▲"},warning:{label:"Warning",tone:"warn",glyph:"◆"},info:{label:"Informational",tone:"info",glyph:"●"}};
const OPEN=["open","adjustment","reopened"];

let injected=false;function inject(){if(injected||typeof document==="undefined")return;injected=true;const el=document.createElement("style");el.setAttribute("data-mds","posting-impact-preview");el.textContent=`
.mds-post{border:1px solid var(--border,#D2CDC3);background:var(--surface-plate,var(--bg-light,#F8F7F3));font-family:var(--font-body);color:var(--text-secondary,#535C65)}
.mds-post__head{display:flex;gap:10px;align-items:flex-start;padding:10px 12px;border-bottom:1px solid var(--border-divider,#E2DED5)}
.mds-post__head h3{margin:0;font-size:14px;color:var(--text-primary,#20252B)}
.mds-post__sub{margin-top:2px;font:9px var(--font-data,monospace);color:var(--text-muted,#606870)}
.mds-post__period{margin-left:auto;text-align:right;font:10px/15px var(--font-data,monospace);color:var(--text-muted,#606870);flex:none}
.mds-post__period b{display:block;color:var(--text-primary,#20252B);font-weight:600}
.mds-post__checks{display:grid;border-bottom:1px solid var(--border-divider,#E2DED5)}
.mds-post__check{display:grid;grid-template-columns:16px 1fr auto;gap:8px;align-items:baseline;padding:6px 12px;border-bottom:1px solid var(--border-divider,#E2DED5);font-size:11px}
.mds-post__check:last-child{border-bottom:0}
.mds-post__check[data-tone="stop"]{background:var(--red-surface,rgba(245,229,226,.1));color:var(--red-dim,#873832)}
.mds-post__check[data-tone="warn"]{background:var(--orange-surface,rgba(242,234,216,.1));color:var(--orange-dim,#724D11)}
.mds-post__check small{display:block;font:9px/13px var(--font-data,monospace);color:var(--text-muted,#606870)}
.mds-post__class{font:9px var(--font-data,monospace);text-transform:uppercase;letter-spacing:.04em}
.mds-post table{width:100%;border-collapse:collapse}
.mds-post th,.mds-post td{padding:6px 10px;border-bottom:1px solid var(--border-divider,#E2DED5);font-size:10px;text-align:left}
.mds-post th{background:var(--surface-recessed,var(--bg-medium,#EBE8E1));color:var(--text-muted,#606870);font:600 9px var(--font-data,monospace);text-transform:uppercase;letter-spacing:.04em}
.mds-post td:nth-last-child(-n+2),.mds-post th:nth-last-child(-n+2){text-align:right;font-family:var(--font-data,monospace);font-variant-numeric:tabular-nums}
.mds-post tr[data-total="true"] td{border-top:2px solid var(--border-strong,#AAA49A);font-weight:600;color:var(--text-primary,#20252B)}
.mds-post__section{padding:8px 12px 4px;font:9px var(--font-data,monospace);text-transform:uppercase;letter-spacing:.05em;color:var(--text-muted,#606870);border-top:1px solid var(--border-divider,#E2DED5)}
.mds-post__effect{display:grid;grid-template-columns:1fr 108px 14px 108px;gap:10px;align-items:baseline;margin:0 12px;min-height:26px;padding:3px 0;border-bottom:1px solid var(--border-divider,#E2DED5);font-size:10px}
.mds-post__effect span:not(:first-child){font-family:var(--font-data,monospace);font-variant-numeric:tabular-nums;text-align:right;color:var(--text-primary,#20252B)}
.mds-post__effect small{display:block;font:9px var(--font-data,monospace);color:var(--text-muted,#606870);text-align:left}
.mds-post__list{margin:0;padding:2px 12px 8px;list-style:none;display:grid;gap:2px}
.mds-post__list li{font:10px/15px var(--font-data,monospace);color:var(--text-secondary,#535C65)}
.mds-post__ack{display:flex;gap:8px;align-items:flex-start;margin:0;padding:9px 12px;border-top:1px solid var(--orange,#986717);background:var(--orange-surface,rgba(242,234,216,.1));font-size:11px;color:var(--orange-dim,#724D11)}
.mds-post__ack input{margin-top:2px}
.mds-post__foot{display:flex;align-items:center;gap:10px;padding:9px 12px;border-top:2px solid var(--border-strong,#AAA49A);font:10px var(--font-data,monospace);color:var(--text-muted,#606870)}
.mds-post__actions{margin-left:auto;display:flex;gap:6px}
`;document.head.appendChild(el)}

const m=value=>knownAmount(value)===null?"Missing":formatNumber(value,{decimals:2});
const acct=v=>formatAccounting(Number(v)||0,{decimals:2});

export function PostingImpactPreview({
  title="Posting impact",subtitle,period,periodState="open",postingPeriod,asOf,
  lines=[],effects=[],statements=[],positions=[],downstream=[],controls=[],checks=[],
  reversal,requireAcknowledgement,authority,
  onPost,onCancel,postLabel="Post journal",className="",...rest
}){
  inject();
  const sides=lines.map(line=>({debit:optionalSide(line.debit),credit:optionalSide(line.credit)}));
  const debit=sumKnown(sides.map(side=>side.debit));
  const credit=sumKnown(sides.map(side=>side.credit));
  const inputsKnown=debit!==null&&credit!==null;
  const invalidSides=sides.some(side=>side.debit===null||side.credit===null||side.debit<0||side.credit<0||(side.debit>0&&side.credit>0));
  const hasActivity=sides.some(side=>side.debit>0||side.credit>0);
  const balanced=inputsKnown&&Math.abs(debit-credit)<.005;
  const periodOpen=OPEN.includes(String(periodState).toLowerCase().replace(/[_\s]+/g,"-"))||String(periodState).toLowerCase().includes("soft");
  const derived=[
    !inputsKnown&&{id:"inputs",severity:"blocking",label:"Posting amounts are unavailable",detail:"Missing amounts are not zero; supply valid journal amounts before posting"},
    invalidSides&&{id:"sides",severity:"blocking",label:"Journal lines contain invalid debit or credit amounts",detail:"Use nonnegative amounts on a single side of each line"},
    !hasActivity&&{id:"activity",severity:"blocking",label:"Journal has no nonzero postings",detail:"An empty or all-zero preview cannot be posted"},
    inputsKnown&&!balanced&&{id:"balance",severity:"blocking",label:"Debits do not equal credits",detail:`Out by ${m(Math.abs(debit-credit))}`},
    !periodOpen&&{id:"period",severity:"blocking",label:`Period is ${periodState}`,detail:"Reopen authorization required before posting"},
    periodOpen&&String(periodState).toLowerCase().includes("soft")&&{id:"soft",severity:"warning",label:"Soft-closed period",detail:"Late entry requires controller approval"},
    authority===false&&{id:"authority",severity:"blocking",label:"User lacks posting authority",detail:"Request a signatory with posting rights"},
  ].filter(Boolean);
  const all=[...derived,...checks];
  const blockers=all.filter(c=>c.severity==="blocking");
  const warnings=all.filter(c=>c.severity==="warning");
  const needsAck=(requireAcknowledgement??warnings.length>0)&&warnings.length>0;
  let draftKey;
  try { draftKey=JSON.stringify({period,periodState,postingPeriod,asOf,lines,effects,statements,positions,downstream,controls,checks,reversal,requireAcknowledgement,authority},(key,value)=>key==="_owner"||typeof value==="function"?undefined:typeof value==="bigint"?String(value):value); } catch { draftKey=null; }
  const[ackKey,setAckKey]=React.useState(null);
  const ack=draftKey!==null&&ackKey===draftKey;
  const blocked=blockers.length>0||(needsAck&&!ack);
  const sideText=value=>value===undefined||(typeof value==="string"&&value.trim()==="")||knownAmount(value)===0?"—":m(value);

  return <section className={`mds-post${className?" "+className:""}`} {...rest}>
    <header className="mds-post__head">
      <div><h3>{title}</h3>{subtitle?<div className="mds-post__sub">{subtitle}</div>:null}</div>
      <div className="mds-post__period">
        <b>{period||"No reporting period"}</b>
        {postingPeriod?<span>Posting {postingPeriod}<br/></span>:null}
        <span>{periodState}</span>{asOf?<span> · as of {asOf}</span>:null}
      </div>
    </header>

    {all.length?<div className="mds-post__checks">{all.map((c,i)=>{const k=CLASSES[c.severity==="blocking"?"blocking":c.severity==="warning"?"warning":"info"];
      return <div className="mds-post__check" key={c.id||i} data-tone={k.tone}>
        <span aria-hidden="true">{k.glyph}</span>
        <span>{c.label}{c.detail?<small>{c.detail}</small>:null}</span>
        <span className="mds-post__class">{k.label}</span>
      </div>})}</div>:null}

    <table>
      <caption className="mds-post__section" style={{captionSide:"top",textAlign:"left"}}>Journal lines</caption>
      <thead><tr><th scope="col">Account</th><th scope="col">Description</th><th scope="col">Debit</th><th scope="col">Credit</th></tr></thead>
      <tbody>
        {lines.map((x,i)=><tr key={x.id||i}><td>{x.account}</td><td>{x.description||"—"}</td><td>{sideText(x.debit)}</td><td>{sideText(x.credit)}</td></tr>)}
        <tr data-total="true"><td colSpan="2">{!inputsKnown?"Inputs unavailable":balanced?"Balanced":"Out of balance"}</td><td>{m(debit)}</td><td>{m(credit)}</td></tr>
      </tbody>
    </table>

    {effects.length?<><div className="mds-post__section">Reporting-basis impact</div>
      {effects.map((x,i)=><div className="mds-post__effect" key={x.id||x.label||i}>
        <span>{x.label}{x.basis?<small>{x.basis}</small>:null}</span>
        <span>{x.before==null?"—":typeof x.before==="number"?acct(x.before):x.before}</span>
        <span aria-hidden="true">→</span>
        <span>{x.after==null?"—":typeof x.after==="number"?acct(x.after):x.after}</span>
      </div>)}</>:null}

    {statements.length?<><div className="mds-post__section">Statement impact</div><ul className="mds-post__list">{statements.map((s,i)=><li key={i}>{typeof s==="string"?s:`${s.label} · ${s.value}`}</li>)}</ul></>:null}
    {positions.length?<><div className="mds-post__section">Position and lot impact</div><ul className="mds-post__list">{positions.map((s,i)=><li key={i}>{typeof s==="string"?s:`${s.label} · ${s.value}`}</li>)}</ul></>:null}
    {controls.length||downstream.length?<><div className="mds-post__section">Downstream reports and controls</div><ul className="mds-post__list">{[...controls,...downstream].map((s,i)=><li key={i}>{typeof s==="string"?s:`${s.label}${s.value?` · ${s.value}`:""}`}</li>)}</ul></>:null}
    {reversal?<><div className="mds-post__section">Reversal behavior</div><ul className="mds-post__list"><li>{reversal}</li></ul></>:null}

    {needsAck?<label className="mds-post__ack"><input type="checkbox" checked={ack} onChange={e=>setAckKey(e.target.checked?draftKey:null)}/><span>I acknowledge {warnings.length} warning{warnings.length===1?"":"s"} on this posting.</span></label>:null}

    <footer className="mds-post__foot">
      <span>{blockers.length?`${blockers.length} blocking condition${blockers.length===1?"":"s"}`:balanced?"Ready to post":"Not ready"}{warnings.length?` · ${warnings.length} warning${warnings.length===1?"":"s"}`:""}</span>
      <div className="mds-post__actions">
        {onCancel?<Button size="sm" variant="ghost" onClick={onCancel}>Cancel</Button>:null}
        {onPost?<Button size="sm" disabled={blocked} onClick={onPost} title={blockers.length?blockers[0].label:needsAck&&!ack?"Acknowledge the warnings first":undefined}>{postLabel}</Button>:null}
      </div>
    </footer>
  </section>;
}
