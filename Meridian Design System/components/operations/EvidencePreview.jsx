// Meridian EvidencePreview — the source material behind a value, exception, calculation
// input, or attestation, kept beside the analytical context. Every visual excerpt carries a
// text equivalent; permissions, redaction, and export restrictions are stated, not implied.
import React from "react";
import { Button } from "../core/Button";
import { conditionMeta } from "./conditions";

const meta=value=>value?conditionMeta(value):null;
const TYPES={statement:"Statement",confirmation:"Trade confirmation",extract:"GL extract",custodian:"Custodian file",pricing:"Pricing report",model:"Model output",policy:"Accounting policy",approval:"Approval record",correspondence:"Correspondence",snapshot:"Source snapshot",document:"Document"};

let injected=false;function inject(){if(injected||typeof document==="undefined")return;injected=true;const el=document.createElement("style");el.setAttribute("data-mds","evidence-preview");el.textContent=`
.mds-evp{border:1px solid var(--border,#D2CDC3);background:var(--surface-plate,var(--bg-light,#F8F7F3));font-family:var(--font-body);color:var(--text-secondary,#535C65)}
.mds-evp__head{display:flex;gap:10px;align-items:flex-start;padding:10px 12px;border-bottom:1px solid var(--border-divider,#E2DED5)}
.mds-evp__head h3{margin:0;font-size:14px;color:var(--text-primary,#20252B)}
.mds-evp__source{margin-top:2px;font:9px var(--font-data,monospace);color:var(--text-muted,#606870)}
.mds-evp__type{display:inline-block;margin-right:6px;padding:0 4px;border:1px solid var(--border,#D2CDC3);border-radius:2px;font:9px var(--font-data,monospace);text-transform:uppercase;letter-spacing:.04em;color:var(--text-secondary,#535C65);background:var(--surface-well,#FCFBF8)}
.mds-evp__loc{margin-left:auto;text-align:right;font:9px/14px var(--font-data,monospace);color:var(--text-muted,#606870);flex:none}
.mds-evp__flags{display:flex;gap:8px;flex-wrap:wrap;padding:6px 12px;border-bottom:1px solid var(--border-divider,#E2DED5);font:9px var(--font-data,monospace);color:var(--text-muted,#606870)}
.mds-evp__flag{display:inline-flex;gap:4px;align-items:center;padding:1px 5px;border-radius:2px;background:var(--surface-well,#FCFBF8);border:1px solid var(--border-divider,#E2DED5)}
.mds-evp__flag--warn{background:var(--orange-surface,rgba(152,103,23,.1));border-color:var(--orange,#986717);color:var(--orange-dim,#725012)}
.mds-evp__flag--stop{background:var(--red-surface,rgba(181,75,66,.1));border-color:var(--red,#B54B42);color:var(--red-dim,#873832)}
.mds-evp__doc{margin:12px;background:var(--surface-overlay,#fff);border:1px solid var(--border,#D2CDC3);padding:14px;min-height:150px;font:11px/18px var(--font-data,monospace);white-space:pre-wrap;color:var(--text-secondary,#535C65)}
.mds-evp__doc--image{padding:0;display:block;min-height:0}
.mds-evp__doc--image img{display:block;width:100%;height:auto}
.mds-evp mark{background:var(--orange-surface,#F2EAD8);color:var(--text-primary,#20252B);padding:2px 3px;border-bottom:2px solid var(--orange,#986717)}
.mds-evp__equiv{margin:0 12px 12px;padding:8px 10px;border:1px solid var(--border-divider,#E2DED5);background:var(--surface-well,#FCFBF8)}
.mds-evp__equiv h4{margin:0 0 3px;font:9px var(--font-data,monospace);text-transform:uppercase;letter-spacing:.05em;color:var(--text-muted,#606870)}
.mds-evp__equiv p{margin:0;font:11px/17px var(--font-body);color:var(--text-primary,#20252B)}
.mds-evp__meta{display:grid;grid-template-columns:repeat(auto-fit,minmax(140px,1fr));border-top:1px solid var(--border-divider,#E2DED5)}
.mds-evp__meta div{padding:7px 10px;border-right:1px solid var(--border-divider,#E2DED5);border-bottom:1px solid var(--border-divider,#E2DED5)}
.mds-evp__meta span{display:block;font:9px var(--font-data,monospace);color:var(--text-muted,#606870)}
.mds-evp__meta strong{display:block;margin-top:2px;font:10px var(--font-data,monospace);color:var(--text-primary,#20252B);font-weight:600}
.mds-evp__cite{display:flex;gap:8px;align-items:center;padding:7px 12px;border-top:1px solid var(--border-divider,#E2DED5);font:9px/14px var(--font-data,monospace);color:var(--text-muted,#606870)}
.mds-evp__foot{display:flex;gap:6px;align-items:center;flex-wrap:wrap;padding:8px 10px}
.mds-evp__nav{font:9px var(--font-data,monospace);color:var(--text-muted,#606870);margin-right:auto}
.mds-evp--superseded{opacity:.86}
.mds-evp--superseded .mds-evp__head h3{text-decoration:line-through;text-decoration-color:var(--text-muted,#606870)}
`;document.head.appendChild(el)}

export function EvidencePreview({
  title,source,sourceSystem,documentId,type,page,section,
  before="",highlight="",after="",image,imageAlt,textEquivalent,
  asOf,statementDate,receivedAt,extraction,extractionStatus,completeness,confidence,
  condition,superseded,supersededBy,redacted,restriction,permissions={},
  citation,metadata=[],index,total,
  onPrevious,onNext,onOpenOriginal,onCopyCitation,onCompare,onLink,onMarkSuperseded,
  className="",...rest
}){
  inject();
  const c=meta(condition);
  const typeLabel=type?(TYPES[String(type).toLowerCase()]||type):null;
  const withheld=c?.key==="restricted";
  const canOpen=permissions.canOpen!==false&&!withheld;
  const canExport=permissions.canExport!==false;
  const extractText=extraction||extractionStatus;
  const equivalent=textEquivalent||(image?imageAlt:null);
  const flags=[
    superseded&&{tone:"warn",text:supersededBy?`Superseded by ${supersededBy}`:"Superseded"},
    redacted&&{tone:"warn",text:typeof redacted==="string"?`Redacted · ${redacted}`:"Contains redactions"},
    restriction&&{tone:"stop",text:restriction},
    !canExport&&{tone:"stop",text:"Export restricted"},
  ].filter(Boolean);
  return <section className={`mds-evp${superseded?" mds-evp--superseded":""}${className?" "+className:""}`} data-condition={c?.key||undefined} {...rest}>
    <header className="mds-evp__head">
      <div>
        <h3>{title}</h3>
        <div className="mds-evp__source">{typeLabel?<span className="mds-evp__type">{typeLabel}</span>:null}{[source,sourceSystem,documentId].filter(Boolean).join(" · ")}</div>
      </div>
      <div className="mds-evp__loc">{page?`Page ${page}`:null}{page&&section?<br/>:null}{section}</div>
    </header>
    {flags.length?<div className="mds-evp__flags">{flags.map((f,i)=><span key={i} className={`mds-evp__flag mds-evp__flag--${f.tone}`}>{f.text}</span>)}</div>:null}
    {withheld ? <div className="mds-evp__doc">Restricted</div> : image
      ? <div className="mds-evp__doc mds-evp__doc--image"><img src={image} alt={imageAlt||`Evidence excerpt from ${title}`}/></div>
      : <div className="mds-evp__doc">{before}{highlight?<mark>{highlight}</mark>:null}{after}</div>}
    {equivalent&&!withheld?<div className="mds-evp__equiv"><h4>Text equivalent</h4><p>{equivalent}</p></div>:null}
    <div className="mds-evp__meta">
      <div><span>{statementDate?"Statement date":"As of"}</span><strong>{statementDate||asOf||"—"}</strong></div>
      <div><span>Received</span><strong>{receivedAt||"—"}</strong></div>
      <div><span>Extraction</span><strong>{withheld?"Restricted":extractText||"—"}</strong></div>
      <div><span>Condition</span><strong>{c?`${c.glyph} ${c.label}`:"—"}</strong></div>
      {completeness!=null&&!withheld?<div><span>Completeness</span><strong>{completeness}</strong></div>:null}
      {confidence!=null&&!withheld?<div><span>Match confidence</span><strong>{confidence}%</strong></div>:null}
      {(!withheld?metadata:[]).map((m,i)=><div key={i}><span>{m.label}</span><strong>{m.value}</strong></div>)}
    </div>
    {citation?<div className="mds-evp__cite"><span>{citation}</span></div>:null}
    <footer className="mds-evp__foot">
      {total?<span className="mds-evp__nav">Evidence {(index??0)+1} of {total}</span>:<span className="mds-evp__nav"/>}
      {onPrevious?<Button size="sm" variant="ghost" onClick={onPrevious}>Previous</Button>:null}
      {onNext?<Button size="sm" variant="ghost" onClick={onNext}>Next</Button>:null}
      {onCopyCitation?<Button size="sm" variant="ghost" onClick={onCopyCitation}>Copy citation</Button>:null}
      {onCompare?<Button size="sm" variant="ghost" onClick={onCompare} disabled={withheld} title={withheld?"Restricted evidence cannot be compared":undefined}>Compare</Button>:null}
      {onLink?<Button size="sm" variant="ghost" onClick={onLink}>Link to exception</Button>:null}
      {onMarkSuperseded&&!superseded?<Button size="sm" variant="ghost" onClick={onMarkSuperseded}>Mark superseded</Button>:null}
      {onOpenOriginal?<Button size="sm" onClick={onOpenOriginal} disabled={!canOpen} title={canOpen?undefined:withheld?"Evidence is restricted":"Document permissions do not allow opening the original"}>Open original</Button>:null}
    </footer>
  </section>;
}
