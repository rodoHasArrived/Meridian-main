// Meridian VarianceCell — the standard answer to "does this value agree with its reference?".
// Carries actual, reference, absolute + relative variance, tolerance, result state and data
// condition. Result is never communicated by color alone: every state carries a mark and a word.
import React from "react";
import { formatMoney, formatNumber } from "../core/format.js";

const CONDITION={confirmed:{label:"Confirmed",glyph:"●"},provisional:{label:"Provisional",glyph:"◐"},estimated:{label:"Estimated",glyph:"○"},stale:{label:"Stale",glyph:"◔"},overridden:{label:"Overridden",glyph:"◆"},missing:{label:"Missing",glyph:"■"},na:{label:"Not applicable",glyph:"—"},pending:{label:"Pending",glyph:"◇"},exception:{label:"Exception",glyph:"▲"},reconciled:{label:"Reconciled",glyph:"✓"}};
const ALIAS={complete:"confirmed",verified:"confirmed",ok:"confirmed",live:"confirmed",matched:"reconciled",certified:"reconciled",preliminary:"provisional",unaudited:"provisional",modeled:"estimated",forecast:"estimated",projected:"estimated",delayed:"stale",aged:"stale",override:"overridden",manual:"overridden",absent:"missing",unavailable:"missing","not applicable":"na","n/a":"na",none:"na",awaiting:"pending",queued:"pending",scheduled:"pending",unmatched:"exception",break:"exception",failed:"exception"};
function meta(v){if(!v)return null;const k=String(v).trim().toLowerCase().replace(/[_-]+/g," ");const key=CONDITION[k]?k:ALIAS[k]||ALIAS[k.replace(/\s+/g,"")];return key?{key,...CONDITION[key]}:null}

const MODES={currency:{digits:2},accounting:{digits:2,parens:true},percent:{digits:2,suffix:"%",relative:false},bp:{digits:0,suffix:" bp",relative:false},quantity:{digits:0},count:{digits:0,suffix:" records"},duration:{digits:0,suffix:" days",dir:["late","early"],relative:false},number:{digits:2},analytical:{digits:2}};
const num=v=>{const n=Number(v);return Number.isFinite(n)?n:null};
const has=v=>v!==null&&v!==undefined&&v!==""&&num(v)!==null;

function formatter({notation="currency",unit,currency,digits,format}){
  const m=MODES[notation]||MODES.number,d=digits??m.digits;
  const cur=(notation==="currency"||notation==="accounting")?(currency||(unit&&unit.length===3?unit:null)||"USD"):null;
  const suffix=m.suffix!==undefined?m.suffix:(unit?` ${unit}`:"");
  const abs=v=>{if(format)return format(Math.abs(v));return cur?formatMoney(Math.abs(v),{currency:cur,decimals:d,parens:false}):formatNumber(Math.abs(v),{decimals:d,suffix})};
  return{
    abs,
    value:v=>m.parens&&v<0?`(${abs(v)})`:`${v<0?"\u2212":""}${abs(v)}`,
    delta:v=>{if(m.dir)return v===0?`On time`:`${abs(v)} ${v>0?m.dir[0]:m.dir[1]}`;if(m.parens)return v<0?`(${abs(v)})`:abs(v);return`${v<0?"\u2212":v>0?"+":""}${abs(v)}`},
    relative:m.relative!==false,
  };
}
function conditioned(text,c){if(!c)return text;if(c.key==="estimated")return`${text} Est.`;if(c.key==="overridden")return`${text}\u2009*`;return text}

let injected=false;function inject(){if(injected||typeof document==="undefined")return;injected=true;const el=document.createElement("style");el.setAttribute("data-mds","variance-cell");el.textContent=`
.mds-var{display:inline-grid;gap:0;justify-items:end;min-width:118px;padding:1px 0 1px 7px;border-left:2px solid transparent;text-align:right;font-family:var(--font-data,monospace);font-variant-numeric:tabular-nums}
.mds-var__actual{font-size:12px;font-weight:600;line-height:16px;color:var(--text-primary,#20252B)}
.mds-var__delta,.mds-var__result,.mds-var__cond{font-size:10px;line-height:14px;color:var(--text-muted,#606870)}
.mds-var__result{font-weight:600}
.mds-var__note{max-width:30ch;font:10px/14px var(--font-body);color:var(--text-muted,#606870);text-align:right}
.mds-var__mark{display:inline-block;width:8px;margin-right:3px;text-align:center}
.mds-var__ref{color:var(--text-muted,#606870);font-weight:400}
.mds-var--pass .mds-var__delta,.mds-var--pass .mds-var__result{color:var(--green-dim,#23583F)}
.mds-var--review .mds-var__delta,.mds-var--review .mds-var__result{color:var(--orange-dim,#724D11)}
.mds-var--exception .mds-var__delta,.mds-var--exception .mds-var__result{color:var(--red-dim,#873832)}
.mds-var--pending .mds-var__delta,.mds-var--pending .mds-var__result{color:var(--pending,#7A6B52)}
.mds-var--neutral .mds-var__actual{color:var(--text-muted,#606870);font-weight:400}
.mds-var--action{background:none;border:0;border-left:2px solid transparent;font:inherit;color:inherit;cursor:pointer;padding:1px 3px 1px 7px}
.mds-var--action:hover{background:var(--bg-hover,#EDEFF0)}
.mds-var--action:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:1px}
.mds-var[data-selected="true"]{border-left-color:var(--selected,#496C8F);background:var(--interaction-selected-surface,var(--bg-active,#E7EDF2))}
.mds-var--inline{grid-auto-flow:column;align-items:baseline;gap:8px;justify-items:start;text-align:left;min-width:0}
.mds-var--inline .mds-var__note{display:none}
`;document.head.appendChild(el)}

export function VarianceCell({
  actual,expected,reference,tolerance,tolerancePct,nearFactor=0.8,
  notation,mode,unit,currency,digits,format,
  comparisonLabel,condition,conditionDetail,explanation,reason,
  state:stateProp,notComparable=false,showRelative,showReference=false,showDelta=true,
  layout="stacked",selected=false,onSelect,className="",...rest
}){
  inject();
  const legacyMode=mode==="accounting"?"accounting":mode==="analytical"?"number":null;
  const f=formatter({notation:notation||legacyMode||"currency",unit,currency,digits,format});
  const ref=expected!==undefined?expected:reference;
  const c=meta(condition);
  const a=num(actual),e=num(ref);
  const tolAbs=tolerance!=null?Math.abs(tolerance):(tolerancePct!=null&&e!=null?Math.abs(e)*tolerancePct/100:0);
  const variance=a!=null&&e!=null?a-e:null;
  const relative=variance!=null&&e!==0&&e!=null?variance/Math.abs(e)*100:null;

  let key=stateProp;
  if(!key){
    if(notComparable||c?.key==="na")key="not-comparable";
    else if(c?.key==="pending")key="pending";
    else if(!has(actual))key="missing-actual";
    else if(!has(ref))key="missing-reference";
    else{const abs=Math.abs(variance);key=abs<=tolAbs?(tolAbs>0&&abs>=tolAbs*nearFactor?"near":"within"):"outside"}
  }
  const tone={within:"pass",near:"review",outside:"exception",pending:"pending","not-comparable":"neutral","missing-actual":"neutral","missing-reference":"neutral"}[key]||"neutral";
  const mark={within:"✓",near:"◇",outside:"▲",pending:"◇","not-comparable":"—","missing-actual":"■","missing-reference":"■"}[key]||"—";
  const tolText=tolAbs>0?`${f.abs(tolAbs)} tolerance`:"tolerance";
  const result={
    within:tolAbs>0?`Within ${tolText}`:variance===0?"Agrees exactly":"Within tolerance",
    near:`Near ${tolText}`,
    outside:`Outside ${tolText}`,
    pending:"Pending calculation",
    "not-comparable":"Not comparable",
    "missing-actual":"Missing actual",
    "missing-reference":"No reference value",
  }[key]||"Not comparable";
  const word={within:"Agreed",near:"Review",outside:"Exception",pending:"Pending","not-comparable":"Not applicable","missing-actual":"Missing","missing-reference":"Missing"}[key]||"—";

  const actualText=a!=null?conditioned(f.value(a),c):"—";
  const wantRelative=showRelative!==undefined?showRelative:f.relative;
  const deltaText=variance!=null&&showDelta!==false?[f.delta(variance),wantRelative&&relative!=null?`${relative<0?"\u2212":relative>0?"+":""}${Math.abs(relative).toFixed(2)}%`:null].filter(Boolean).join(" · "):null;
  const versus=comparisonLabel?` versus ${comparisonLabel}`:"";
  const condText=c?[word,conditionDetail?`${c.label} ${conditionDetail}`:`${c.label}`].join(" · "):null;
  const note=explanation||reason;
  const inline=layout==="inline";
  const label=[actualText,deltaText&&`${deltaText}${versus}`,result,condText].filter(Boolean).join(". ");

  const Tag=onSelect?"button":"span";
  const props=onSelect?{type:"button",onClick:()=>onSelect({actual:a,expected:e,variance,relative,state:key,tolerance:tolAbs}),"aria-pressed":selected||undefined}:{};
  return <Tag className={`mds-var mds-var--${tone}${inline?" mds-var--inline":""}${onSelect?" mds-var--action":""}${className?" "+className:""}`} data-state={key} data-condition={c?.key||undefined} data-selected={selected?"true":undefined} aria-label={label} title={label} {...props} {...rest}>
    <span className="mds-var__actual">{actualText}</span>
    {deltaText?<span className="mds-var__delta"><span className="mds-var__mark" aria-hidden="true">{mark}</span>{deltaText}{versus}</span>:null}
    <span className="mds-var__result">{!deltaText?<span className="mds-var__mark" aria-hidden="true">{mark}</span>:null}{result}</span>
    {showReference&&e!=null&&!inline?<span className="mds-var__cond mds-var__ref">Reference {f.value(e)}</span>:null}
    {condText&&!inline?<span className="mds-var__cond">{condText}</span>:null}
    {note&&!inline?<span className="mds-var__note">{note}</span>:null}
  </Tag>;
}
