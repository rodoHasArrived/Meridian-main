// Meridian AnalyticalField — repeated data marks whose variation encodes population state.
import React from "react";

const GLYPH = { confirmed:"●", provisional:"◐", estimated:"○", stale:"◔", overridden:"◆", missing:"■", na:"—", pending:"◇", exception:"▲", reconciled:"✓", restricted:"■" };
let injected=false;
function inject(){if(injected||typeof document==="undefined")return;injected=true;const el=document.createElement("style");el.setAttribute("data-mds","analytical-field");el.textContent=`
.mds-af{font-family:var(--font-body);color:var(--text-secondary,#535C65)}.mds-af__grid{display:grid;grid-template-columns:repeat(var(--af-cols,10),minmax(18px,1fr));gap:5px;align-items:center}.mds-af__mark{aspect-ratio:1;min-width:18px;max-width:30px;border:1px solid var(--border,#D2CDC3);background:var(--surface-well,var(--card-surface-raised,#FCFBF8));color:var(--text-muted,#606870);display:flex;align-items:center;justify-content:center;font:9px var(--font-data,monospace);cursor:default;padding:0}.mds-af__mark[tabindex]{cursor:pointer}.mds-af__mark:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:2px}.mds-af__mark--confirmed,.mds-af__mark--reconciled{color:var(--green-dim,#23583F);border-color:var(--green,#2E7655)}.mds-af__mark--provisional,.mds-af__mark--pending,.mds-af__mark--stale{color:var(--pending,#7A6B52);border-color:var(--orange,#986717)}.mds-af__mark--estimated{color:var(--purple,#2F648A);border-style:dashed}.mds-af__mark--overridden{color:var(--selected,#496C8F);border-color:var(--selected,#496C8F)}.mds-af__mark--missing,.mds-af__mark--exception{color:var(--red-dim,#873832);border-color:var(--red,#B54B42);background:var(--red-surface,#F5E5E2)}.mds-af__mark--selected{box-shadow:0 0 0 2px var(--selected,#496C8F);border-color:var(--surface-well,#FCFBF8)}.mds-af__summary{margin-top:8px;font:11px var(--font-data,monospace);color:var(--text-muted,#606870)}.mds-af__legend{display:flex;gap:10px;flex-wrap:wrap;margin-top:8px;font-size:10px;color:var(--text-muted,#606870)}.mds-af__legend span{display:inline-flex;gap:4px;align-items:center}
`;document.head.appendChild(el)}
function normalize(item,index){if(typeof item==="string")return{id:String(index),label:item,condition:"confirmed"};return{id:String(item.id??index),condition:item.condition||item.state||"confirmed",...item}}
export function AnalyticalField({items=[],columns=10,selectedId,onSelect,showLegend=false,summary,className="",...rest}) {
  inject();
  const data=items.map(normalize);
  const columnCount=Number.isInteger(columns)&&columns>0?columns:10;
  const counts=data.reduce((a,item)=>(a[item.condition]=(a[item.condition]||0)+1,a),{});
  const text=summary||Object.entries(counts).map(([k,n])=>`${n} ${k}`).join(" · ");
  const keys=[...new Set(data.map(d=>d.condition))];
  const selectedIndex=data.findIndex(item=>String(selectedId)===String(item.id));
  const activeIndex=selectedIndex<0?0:selectedIndex;
  const describe=item=>`${item.label||item.id}, ${item.condition}${item.condition!=="restricted"&&item.value!=null?`, ${item.value}`:""}`;
  const handleKey=(event,index)=>{
    let next=index;
    if(event.key==="ArrowRight")next=Math.min(data.length-1,index+1);
    else if(event.key==="ArrowLeft")next=Math.max(0,index-1);
    else if(event.key==="ArrowDown")next=Math.min(data.length-1,index+columnCount);
    else if(event.key==="ArrowUp")next=Math.max(0,index-columnCount);
    else if(event.key==="Home")next=0;
    else if(event.key==="End")next=data.length-1;
    else return;
    event.preventDefault();
    event.currentTarget.closest('[role="grid"]')?.querySelectorAll("button")[next]?.focus();
  };
  const marks=data.map((item,i)=>{
    const Tag=onSelect?"button":"span";
    const mark=<Tag type={onSelect?"button":undefined} tabIndex={onSelect?(i===activeIndex?0:-1):undefined}
      className={`mds-af__mark mds-af__mark--${item.condition}${String(selectedId)===String(item.id)?" mds-af__mark--selected":""}`}
      title={describe(item)} aria-label={describe(item)} onClick={onSelect?()=>onSelect(item,i):undefined}
      onKeyDown={onSelect?(event)=>handleKey(event,i):undefined}>{GLYPH[item.condition]||"·"}</Tag>;
    return onSelect?<div key={item.id} role="gridcell" aria-selected={i===selectedIndex} style={{display:"contents"}}>{mark}</div>:<React.Fragment key={item.id}>{mark}</React.Fragment>;
  });
  const rows=[];
  for(let i=0;i<marks.length;i+=columnCount)rows.push(<div key={i} role="row" style={{display:"contents"}}>{marks.slice(i,i+columnCount)}</div>);
  return <div className={`mds-af${className?" "+className:""}`} style={{"--af-cols":columnCount}} {...rest}>
    <div className="mds-af__grid" role={onSelect?"grid":"img"} aria-label={typeof text==="string"?text:undefined}>{onSelect?rows:marks}</div>
    <div className="mds-af__summary">{text}</div>{showLegend?<div className="mds-af__legend">{keys.map(k=><span key={k}><b aria-hidden="true">{GLYPH[k]||"·"}</b>{k}</span>)}</div>:null}
  </div>;
}
