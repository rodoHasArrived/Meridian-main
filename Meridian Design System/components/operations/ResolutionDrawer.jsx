// Meridian ResolutionDrawer — the canonical exception-resolution surface: evidence, cause,
// correction, impact, ownership and rationale in a contextual side region that keeps the
// originating row or figure on screen. One primary action at a time.
import React from "react";
import { Button } from "../core/Button";

const SEVERITY={blocked:{label:"Blocked",tone:"exception",mark:"▲"},action:{label:"Action",tone:"exception",mark:"▲"},exception:{label:"Exception",tone:"exception",mark:"▲"},review:{label:"Review",tone:"review",mark:"◇"},ready:{label:"Ready",tone:"pass",mark:"✓"},info:{label:"Info",tone:"info",mark:"·"}};

let injected=false;function inject(){if(injected||typeof document==="undefined")return;injected=true;const el=document.createElement("style");el.setAttribute("data-mds","resolution-drawer");el.textContent=`
.mds-res{display:flex;flex-direction:column;min-height:420px;background:var(--surface-overlay,var(--surface-overlay-base,#fff));border-left:1px solid var(--border-strong,#AAA49A);font-family:var(--font-body);color:var(--text-secondary,#535C65)}
.mds-res--inline{border:1px solid var(--border,#D2CDC3)}
.mds-res__back{display:flex;align-items:center;gap:6px;min-height:32px;padding:7px 12px;border:0;border-bottom:1px solid var(--border-divider,#E2DED5);background:var(--surface-recessed,var(--bg-medium,#EBE8E1));font:10px var(--font-data,monospace);color:var(--interaction-link,#2A5F96);cursor:pointer;width:100%;text-align:left}.mds-res__back:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:-2px}
.mds-res__head{padding:11px 12px;border-bottom:1px solid var(--border-divider,#E2DED5)}
.mds-res__titlerow{display:flex;align-items:flex-start;gap:8px}
.mds-res__head h3{margin:0;font-size:14px;color:var(--text-primary,#20252B)}
.mds-res__sev{margin-left:auto;display:inline-flex;align-items:center;gap:4px;padding:2px 6px;border:1px solid var(--border,#D2CDC3);font:600 9px var(--font-data,monospace);text-transform:uppercase;letter-spacing:.04em;color:var(--text-muted,#606870);white-space:nowrap}
.mds-res__sev[data-tone="exception"]{color:var(--red-dim,#873832);border-color:var(--red,#B54B42);background:var(--red-a10,rgba(181,75,66,.10))}
.mds-res__sev[data-tone="review"]{color:var(--orange-dim,#724D11);border-color:var(--orange,#986717);background:var(--orange-a10,rgba(152,103,23,.10))}
.mds-res__sev[data-tone="pass"]{color:var(--green-dim,#23583F);border-color:var(--green,#2E7655);background:var(--green-a10,rgba(46,118,85,.10))}
.mds-res__sev[data-tone="info"]{color:var(--purple-dim,#234B68);border-color:var(--color-info,#2F648A);background:var(--purple-a10,rgba(47,100,138,.10))}
.mds-res__meta{margin-top:3px;font:10px var(--font-data,monospace);color:var(--text-muted,#606870)}
.mds-res__statement{padding:9px 12px;border-bottom:1px solid var(--border-divider,#E2DED5);font-size:11px;line-height:16px;color:var(--text-primary,#20252B)}
.mds-res__body{flex:1;overflow:auto}
.mds-res__section{padding:9px 12px;border-bottom:1px solid var(--border-divider,#E2DED5)}
.mds-res__section h4{margin:0 0 6px;font:600 10px var(--font-data,monospace);text-transform:uppercase;letter-spacing:.04em;color:var(--text-muted,#606870)}
.mds-res__field{display:grid;grid-template-columns:1fr auto;gap:10px;min-height:25px;align-items:baseline;border-bottom:1px solid var(--border-divider,#E2DED5);font-size:10px}
.mds-res__field:last-child{border-bottom:0}
.mds-res__field span:last-child{font-family:var(--font-data,monospace);font-variant-numeric:tabular-nums;color:var(--text-primary,#20252B);text-align:right}
.mds-res__field[data-flag="true"] span:last-child{color:var(--orange-dim,#724D11)}
.mds-res__evidence{display:grid;gap:0;margin:0;padding:0;list-style:none}
.mds-res__evidence li{display:grid;grid-template-columns:12px 1fr auto;gap:7px;align-items:baseline;min-height:25px;border-bottom:1px solid var(--border-divider,#E2DED5);font-size:10px}
.mds-res__evidence li:last-child{border-bottom:0}
.mds-res__evidence a{color:var(--border-focus,#2D6FB3)}
.mds-res__evidence small{font:9px var(--font-data,monospace);color:var(--text-muted,#606870)}
.mds-res__select{display:grid;gap:4px}
.mds-res__select select,.mds-res__select textarea{box-sizing:border-box;width:100%;border:1px solid var(--border,#D2CDC3);background:var(--surface-well,var(--card-surface-raised,#FCFBF8));color:var(--text-primary,#20252B);font:11px var(--font-body);padding:6px 7px}
.mds-res__select select{height:30px;padding:0 7px}
.mds-res__select textarea{min-height:44px}
.mds-res__select select:focus-visible,.mds-res__select textarea:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:1px}
.mds-res__select label{font:9px var(--font-data,monospace);text-transform:uppercase;letter-spacing:.04em;color:var(--text-muted,#606870)}
.mds-res__prose{font-size:11px;line-height:16px}
.mds-res__foot{display:flex;flex-wrap:wrap;align-items:center;gap:6px;padding:9px 12px;border-top:1px solid var(--border-strong,#AAA49A);background:var(--surface-recessed,var(--bg-medium,#EBE8E1))}
.mds-res__ownership{font:9px var(--font-data,monospace);color:var(--text-muted,#606870)}
.mds-res__foot .spacer{flex:1}
@media(max-width:640px){
.mds-res{border-left:0;border-top:1px solid var(--border-strong,#AAA49A);min-height:auto}
.mds-res__back{display:flex;align-items:center;gap:6px;padding:7px 12px;border-bottom:1px solid var(--border-divider,#E2DED5);background:var(--surface-recessed,var(--bg-medium,#EBE8E1));font:10px var(--font-data,monospace);color:var(--border-focus,#2D6FB3);background:none;border-width:0 0 1px;cursor:pointer;width:100%;text-align:left}
.mds-res__foot .spacer{display:none}
.mds-res__ownership{flex:1 0 100%}
.mds-res__foot .mds-btn{flex:1 1 118px}
}
`;document.head.appendChild(el)}

function Section({title,children}){return children?<section className="mds-res__section"><h4>{title}</h4>{children}</section>:null}

export function ResolutionDrawer({
  inline=false,title="Resolve exception",caseId,version,severity="exception",condition,object,statement,
  fields=[],evidence,evidenceItems=[],
  rootCause,rootCauses,onRootCauseChange,rootCauseNote,onRootCauseNoteChange,rootCauseNoteLabel="Additional detail",
  correction,correctionFacts=[],impact,impactItems=[],
  ownership,assignee,sla,audit,
  requireReason=false,reason,onReasonChange,reasonLabel="Resolution rationale",
  primaryAction="resolve",onBack,backLabel="Back to queue",
  onResolve,onEscalate,onDefer,onReopen,onAssign,onRequestEvidence,
  className="",...rest
}){
  const uid = React.useId();
  inject();
  const sev=SEVERITY[String(severity).toLowerCase()]||SEVERITY.exception;
  const reasonScope=JSON.stringify([caseId??null,version??null]);
  const[localReason,setLocalReason]=React.useState(()=>({scope:reasonScope,value:""}));
  const reasonValue=reason!==undefined?reason:localReason.scope===reasonScope?localReason.value:"";
  React.useEffect(()=>{setLocalReason(previous=>previous.scope===reasonScope?previous:{scope:reasonScope,value:""})},[reasonScope]);
  const setReason=v=>{if(reason===undefined)setLocalReason({scope:reasonScope,value:v});onReasonChange?.(v)};
  const own=ownership||(assignee||sla?{assignee,due:sla}:null);
  const ownRows=own?[["Assignee",own.assignee],["Team",own.team],["Due",own.due],["Age",own.age],["Escalation",own.escalation],["Prior handoffs",own.handoffs]].filter(([,v])=>v!==undefined&&v!==null&&v!==""):[];
  const canResolve=!requireReason||reasonValue.trim().length>0;
  const actions={resolve:{fn:onResolve,label:"Resolve exception",disabled:!canResolve},escalate:{fn:onEscalate,label:"Escalate"},defer:{fn:onDefer,label:"Defer"},reopen:{fn:onReopen,label:"Reopen"}};
  const primaryKey=actions[primaryAction]?.fn?primaryAction:Object.keys(actions).find(k=>actions[k].fn);
  const secondaries=["defer","escalate","reopen","resolve"].filter(k=>k!==primaryKey&&actions[k].fn);

  return <aside className={`mds-res${inline?" mds-res--inline":""}${className?" "+className:""}`} aria-label={title} {...rest}>
    {onBack?<button type="button" className="mds-res__back" onClick={onBack}><span aria-hidden="true">←</span>{backLabel}</button>:null}
    <header className="mds-res__head">
      <div className="mds-res__titlerow">
        <h3>{title}</h3>
        <span className="mds-res__sev" data-tone={sev.tone}><span aria-hidden="true">{sev.mark}</span>{sev.label}</span>
      </div>
      <div className="mds-res__meta">{[caseId,version!=null&&`Version ${version}`,object,condition&&String(condition).replace(/^./,s=>s.toUpperCase())].filter(Boolean).join(" · ")}</div>
    </header>
    {statement?<div className="mds-res__statement">{statement}</div>:null}
    <div className="mds-res__body">
      <Section title="Current evidence">{fields.length||evidenceItems.length||evidence?<>
        {fields.map((f,i)=><div className="mds-res__field" data-flag={f.flag?"true":undefined} key={f.id||f.label||i}><span>{f.label}</span><span>{f.value??"—"}</span></div>)}
        {evidenceItems.length?<ul className="mds-res__evidence">{evidenceItems.map((e,i)=><li key={e.id||e.label||i}><span aria-hidden="true">{e.missing?"■":"●"}</span><span>{e.href?<a href={e.href}>{e.label}</a>:e.label}</span><small>{e.detail||(e.missing?"Missing":"Attached")}</small></li>)}</ul>:null}
        {evidence}
      </>:null}</Section>
      <Section title="Root cause">{rootCauses?.length?<div className="mds-res__select">
        <label htmlFor={`${uid}-rootcause`}>Classification</label>
        <select disabled={!onRootCauseChange} id={`${uid}-rootcause`} value={typeof rootCause==="string"?rootCause:""} onChange={e=>onRootCauseChange?.(e.target.value)}>
          <option value="">Not classified</option>
          {rootCauses.map(o=>{const v=typeof o==="string"?o:o.value;const l=typeof o==="string"?o:o.label;return <option value={v} key={v}>{l}</option>})}
        </select>
        {onRootCauseNoteChange!==undefined||rootCauseNote!==undefined?<><label htmlFor={`${uid}-rootnote`}>{rootCauseNoteLabel}</label><textarea readOnly={!onRootCauseNoteChange} id={`${uid}-rootnote`} value={rootCauseNote||""} onChange={e=>onRootCauseNoteChange?.(e.target.value)} placeholder="Optional detail beyond the classification."/></>:null}
      </div>:rootCause?<div className="mds-res__prose">{rootCause}</div>:null}</Section>
      <Section title="Proposed correction">{correction||correctionFacts.length?<>
        {correction?<div className="mds-res__prose">{correction}</div>:null}
        {correctionFacts.map((f,i)=><div className="mds-res__field" data-flag={f.flag?"true":undefined} key={f.label||i}><span>{f.label}</span><span>{f.value}</span></div>)}
      </>:null}</Section>
      <Section title="Impact">{impact||impactItems.length?<>
        {impact?<div className="mds-res__prose">{impact}</div>:null}
        {impactItems.map((f,i)=><div className="mds-res__field" data-flag={f.flag?"true":undefined} key={f.label||i}><span>{f.label}</span><span>{f.value}</span></div>)}
      </>:null}</Section>
      <Section title="Ownership and SLA">{ownRows.length?ownRows.map(([k,v])=><div className="mds-res__field" key={k}><span>{k}</span><span>{v}</span></div>):null}</Section>
      {requireReason?<section className="mds-res__section"><h4>{reasonLabel}</h4><div className="mds-res__select"><textarea readOnly={reason!==undefined&&!onReasonChange} aria-label={reasonLabel} value={reasonValue} onChange={e=>setReason(e.target.value)} placeholder="Required before this exception can be resolved."/></div></section>:null}
      <Section title="Audit details">{audit}</Section>
    </div>
    <footer className="mds-res__foot">
      <span className="mds-res__ownership">{[own?.assignee&&`Assignee ${own.assignee}`,own?.due&&`Due ${own.due}`,sla&&!own?.due?sla:null].filter(Boolean).join(" · ")}</span>
      <span className="spacer"/>
      {onRequestEvidence?<Button size="sm" variant="ghost" onClick={onRequestEvidence}>Request evidence</Button>:null}
      {onAssign?<Button size="sm" variant="ghost" onClick={onAssign}>Assign</Button>:null}
      {secondaries.map(k=><Button size="sm" variant="ghost" key={k} onClick={()=>actions[k].fn(reasonValue)} disabled={actions[k].disabled}>{actions[k].label}</Button>)}
      {primaryKey?<Button size="sm" onClick={()=>actions[primaryKey].fn(reasonValue)} disabled={actions[primaryKey].disabled}>{actions[primaryKey].label}</Button>:null}
    </footer>
  </aside>;
}
