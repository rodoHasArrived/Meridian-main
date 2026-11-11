// Meridian BasisComparisonMatrix — one economic object across statutory, GAAP, tax,
// management, and other bases. The Plate principle: comparable objects get the same
// geometry, order, units, and hierarchy, so a reader compares by position, not by hunting.
// Difference is stated in words and numbers, never by color alone; the reference basis is
// named on the face; missing and not-applicable stay distinct; each cell can expose its
// exact value, method, source, condition, override state, evidence, and calculation trace.
import React from "react";
import { formatNumber, formatAccounting } from "../core/format.js";

const COND={confirmed:{label:"Confirmed",glyph:"●"},provisional:{label:"Provisional",glyph:"◐"},estimated:{label:"Estimated",glyph:"○"},stale:{label:"Stale",glyph:"◔"},overridden:{label:"Overridden",glyph:"◆"},missing:{label:"Missing",glyph:"■"},na:{label:"Not applicable",glyph:"—"},pending:{label:"Pending",glyph:"◇"},exception:{label:"Exception",glyph:"▲"},reconciled:{label:"Reconciled",glyph:"✓"}};
const cond=v=>COND[String(v||"").toLowerCase().replace(/[_\s-]+/g,"")]||null;
const MODES=[
  {id:"none",label:"Values"},
  {id:"absolute",label:"Absolute"},
  {id:"percent",label:"Percent"},
  {id:"bp",label:"Basis points"},
];

let injected=false;function inject(){if(injected||typeof document==="undefined")return;injected=true;const el=document.createElement("style");el.setAttribute("data-mds","basis-comparison-matrix");el.textContent=`
.mds-bcm{border:1px solid var(--border,#D2CDC3);background:var(--surface-well,var(--card-surface-raised,#FCFBF8));font-family:var(--font-body);color:var(--text-secondary,#535C65)}
.mds-bcm__head{display:flex;gap:10px;align-items:baseline;flex-wrap:wrap;padding:8px 10px;border-bottom:1px solid var(--border-divider,#E2DED5)}
.mds-bcm__head h3{margin:0;font-size:13px;color:var(--text-primary,#20252B)}
.mds-bcm__ref{font:9px var(--font-data,monospace);color:var(--text-muted,#606870)}
.mds-bcm__modes{margin-left:auto;display:flex;border:1px solid var(--border,#D2CDC3);border-radius:2px;overflow:hidden}
.mds-bcm__modes button{appearance:none;border:0;border-left:1px solid var(--border,#D2CDC3);background:var(--surface-plate,#F8F7F3);font:9px var(--font-data,monospace);text-transform:uppercase;letter-spacing:.04em;color:var(--text-muted,#606870);padding:3px 8px;cursor:pointer}
.mds-bcm__modes button:first-child{border-left:0}
.mds-bcm__modes button[aria-pressed="true"]{background:var(--interaction-selected-surface,#E7EDF2);color:var(--text-primary,#20252B);font-weight:600}
.mds-bcm__modes button:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:-2px}
.mds-bcm__scroll{overflow:auto}
.mds-bcm table{border-collapse:collapse;width:100%;min-width:560px}
.mds-bcm caption{text-align:left;padding:6px 10px;font:9px/14px var(--font-data,monospace);color:var(--text-muted,#606870)}
.mds-bcm th,.mds-bcm td{padding:0;border-right:1px solid var(--border-divider,#E2DED5);border-bottom:1px solid var(--border-divider,#E2DED5);vertical-align:top}
.mds-bcm th:last-child,.mds-bcm td:last-child{border-right:0}
.mds-bcm thead th{background:var(--surface-recessed,var(--bg-medium,#EBE8E1));padding:7px 10px;text-align:left;font:600 10px var(--font-data,monospace);color:var(--text-primary,#20252B);white-space:nowrap}
.mds-bcm thead th small{display:block;font-weight:400;font-size:9px;color:var(--text-muted,#606870)}
.mds-bcm thead th[data-reference="true"]{box-shadow:inset 0 -2px 0 var(--selected,#496C8F)}
.mds-bcm tbody th{padding:7px 10px;text-align:left;font-size:11px;font-weight:600;color:var(--text-primary,#20252B);background:var(--surface-plate,var(--bg-light,#F8F7F3));white-space:nowrap}
.mds-bcm tbody th small{display:block;font:9px var(--font-data,monospace);font-weight:400;color:var(--text-muted,#606870)}
.mds-bcm__cell{display:block;width:100%;border:0;border-left:2px solid transparent;background:none;padding:7px 10px;text-align:right;font:inherit;color:inherit}
.mds-bcm__cell--action{cursor:pointer}
.mds-bcm__cell--action:hover{background:var(--bg-hover,#EDEFF0)}
.mds-bcm__cell--action:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:-2px}
.mds-bcm__cell[data-selected="true"]{border-left-color:var(--selected,#496C8F);background:var(--interaction-selected-surface,#E7EDF2)}
.mds-bcm__val{display:block;font:11px var(--font-data,monospace);font-variant-numeric:tabular-nums;color:var(--text-primary,#20252B)}
.mds-bcm__val[data-text="true"]{font-family:var(--font-body);font-size:11px}
.mds-bcm__val[data-condition="missing"],.mds-bcm__val[data-condition="exception"]{color:var(--red-dim,#873832)}
.mds-bcm__val[data-condition="na"]{color:var(--text-muted,#606870)}
.mds-bcm__sub{display:block;margin-top:2px;font:9px/13px var(--font-data,monospace);color:var(--text-muted,#606870)}
.mds-bcm__diff{display:block;margin-top:3px;font:9px/13px var(--font-body);color:var(--text-primary,#20252B);border-top:1px dotted var(--border-divider,#E2DED5);padding-top:2px}
.mds-bcm__diff[data-same="true"]{color:var(--text-muted,#606870)}
.mds-bcm__mark{display:inline-block;width:9px;text-align:center;margin-right:3px;color:var(--text-muted,#606870)}
.mds-bcm__foot{display:flex;gap:14px;flex-wrap:wrap;padding:7px 10px;border-top:1px solid var(--border-divider,#E2DED5);font:9px/14px var(--font-data,monospace);color:var(--text-muted,#606870)}
`;document.head.appendChild(el)}

const norm=c=>c&&typeof c==="object"&&!React.isValidElement(c)?c:{value:c};
const num=v=>typeof v==="number"?v:(typeof v==="string"&&v.trim()!==""&&!isNaN(Number(v.replace(/[$,()\s]/g,"")))?Number(v.replace(/[,$\s]/g,"").replace(/^\((.*)\)$/,"-$1")):null);
const money=v=>formatNumber(Math.abs(v),{decimals:2});
const acct=v=>formatAccounting(v,{decimals:2});
const signed=(v,f)=>`${v<0?"\u2212":"+"}${f(Math.abs(v))}`;

function difference(mode,cell,ref,row,basisId){
  if(mode==="none"||!ref)return null;
  // Legacy call sites mark differing bases explicitly; honour that as an override.
  const flagged=(row.different||row.changed||[]).includes(basisId);
  const a=num(cell.value),b=num(ref.value);
  const sameText=String(cell.value)===String(ref.value);
  // Non-numeric rows compare as classification / method / timing differences, in words.
  if(a==null||b==null){
    if(cell.condition==="na"||ref.condition==="na")return{text:"Not comparable",same:true};
    if(sameText&&!flagged)return{text:"Same treatment",same:true};
    const kind=row.differenceKind||(row.method?"Method":"Classification");
    return{text:`${kind} differs · ${ref.value ?? "—"} → ${cell.value ?? "—"}`,same:false};
  }
  const d=a-b;
  if(Math.abs(d)<0.005&&mode!=="bp")return{text:flagged?"Differs from reference":"No difference",same:!flagged};
  if(mode==="percent")return{text:b?`${signed(d/Math.abs(b)*100,v=>v.toFixed(2)+"%")} vs reference`:"Percent not defined",same:false};
  if(mode==="bp")return{text:b?`${signed(d/Math.abs(b)*10000,v=>Math.round(v)+" bp")} vs reference`:"Basis points not defined",same:false};
  return{text:`${signed(d,x=>row.unit==="%"?x.toFixed(2)+"%":money(x))} vs reference`,same:false};
}

export function BasisComparisonMatrix({
  bases=[],rows=[],attributeLabel="Measure",referenceBasis,
  title="Basis comparison",object,asOf,period,notation="accounting",
  differenceMode:modeProp,defaultDifferenceMode="absolute",onDifferenceModeChange,
  selectedCell,onSelectCell,onOpenTrace,onOpenEvidence,
  description,className="",...rest
}){
  inject();
  const[own,setOwn]=React.useState(defaultDifferenceMode);
  const mode=modeProp||own;
  const setMode=m=>{if(modeProp===undefined)setOwn(m);if(onDifferenceModeChange)onDifferenceModeChange(m)};
  const refId=referenceBasis||bases[0]?.id;
  const refBasis=bases.find(b=>b.id===refId);
  const interactive=Boolean(onSelectCell||onOpenTrace||onOpenEvidence);

  const render=(cell,row)=>{
    if(cell.value===null||cell.value===undefined||cell.value==="")
      return cell.condition==="na"?"n/a":"—";
    const n=num(cell.value);
    if(n!=null&&typeof cell.value==="number")
      return row.unit==="%"?`${n.toFixed(2)}%`:(notation==="accounting"?acct(n):money(n));
    return cell.value;
  };

  return <section className={`mds-bcm${className?" "+className:""}`} {...rest}>
    <div className="mds-bcm__head">
      <div>
        <h3>{title}</h3>
        <div className="mds-bcm__ref">
          Reference basis: {refBasis?.label||"none"}{object?` · ${object}`:""}{period?` · ${period}`:""}{asOf?` · as of ${asOf}`:""}
        </div>
      </div>
      <div className="mds-bcm__modes" role="group" aria-label="Difference mode">
        {MODES.map(m=><button key={m.id} type="button" aria-pressed={mode===m.id} onClick={()=>setMode(m.id)}>{m.label}</button>)}
      </div>
    </div>

    <div className="mds-bcm__scroll">
      <table>
        <caption>{description||`${title} — every basis shown on identical rows and units; differences are stated against ${refBasis?.label||"the reference basis"} in words and numbers.`}</caption>
        <thead><tr>
          <th scope="col">{attributeLabel}</th>
          {bases.map(b=><th key={b.id} scope="col" data-reference={b.id===refId?"true":undefined}>
            {b.label}{b.id===refId?" · reference":""}
            {b.meta?<small>{b.meta}</small>:null}
          </th>)}
        </tr></thead>
        <tbody>{rows.map((row,ri)=>{
          const ref=norm(row.values?.[refId]);
          return <tr key={row.id||row.label||ri}>
            <th scope="row">{row.label}{row.unit||row.note?<small>{[row.unit,row.note].filter(Boolean).join(" · ")}</small>:null}</th>
            {bases.map(b=>{
              const cell=norm(row.values?.[b.id]);
              const c=cond(cell.condition);
              const isRef=b.id===refId;
              const diff=isRef?null:difference(mode,cell,ref,row,b.id);
              const key=`${row.id||row.label}::${b.id}`;
              const Tag=interactive?"button":"span";
              const numeric=num(cell.value)!=null&&typeof cell.value==="number";
              return <td key={b.id}>
                <Tag type={interactive?"button":undefined}
                  className={`mds-bcm__cell${interactive?" mds-bcm__cell--action":""}`}
                  data-selected={String(selectedCell)===key?"true":undefined}
                  onClick={interactive?()=>onSelectCell?.({row,basis:b,cell,key,reference:ref}):undefined}
                  onDoubleClick={onOpenTrace?()=>onOpenTrace({row,basis:b,cell}):undefined}>
                  <span className="mds-bcm__val" data-condition={c?.key||(cell.condition||"").toLowerCase()||undefined} data-text={numeric?undefined:"true"}>
                    {c?<span className="mds-bcm__mark" aria-hidden="true">{c.glyph}</span>:null}{render(cell,row)}
                  </span>
                  {cell.method||cell.source||cell.asOf?<span className="mds-bcm__sub">{[cell.method,cell.source,cell.asOf].filter(Boolean).join(" · ")}</span>:null}
                  {cell.override?<span className="mds-bcm__sub">Override: {cell.override}</span>:null}
                  {diff?<span className="mds-bcm__diff" data-same={diff.same?"true":undefined}>{diff.text}</span>:null}
                  {isRef&&mode!=="none"?<span className="mds-bcm__diff" data-same="true">Reference</span>:null}
                </Tag>
              </td>;
            })}
          </tr>;
        })}</tbody>
      </table>
    </div>

    <div className="mds-bcm__foot">
      <span>Reference: {refBasis?.label||"none"}</span>
      <span>Difference mode: {MODES.find(m=>m.id===mode)?.label}</span>
      <span>Notation: {notation}</span>
      <span>“—” missing · “n/a” not applicable — the two are never merged</span>
      {asOf?<span>As of {asOf}</span>:null}
    </div>
  </section>;
}
