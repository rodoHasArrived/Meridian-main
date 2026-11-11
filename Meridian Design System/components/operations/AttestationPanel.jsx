// Meridian AttestationPanel — sign-off bound to a specific object, version, period and evidence
// set. Not a generic approval button: readiness gates, evidence completeness, dual-control roles,
// certification text and rationale are all part of the record the signature attaches to.
import React from "react";
import { Button } from "../core/Button";

const STATE={
  "not-ready":{label:"Not ready",tone:"neutral"},blocked:{label:"Blocked",tone:"exception"},
  ready:{label:"Ready for attestation",tone:"pass"},awaiting:{label:"Awaiting signatory",tone:"review"},
  approved:{label:"Approved",tone:"pass"},rejected:{label:"Rejected",tone:"exception"},
  "changes-requested":{label:"Changes requested",tone:"review"},reopened:{label:"Reopened",tone:"review"},
  superseded:{label:"Superseded",tone:"neutral"},expired:{label:"Expired — underlying change",tone:"exception"},
};

let injected=false;function inject(){if(injected||typeof document==="undefined")return;injected=true;const el=document.createElement("style");el.setAttribute("data-mds","attestation-panel");el.textContent=`
.mds-att{border:1px solid var(--border,#D2CDC3);background:var(--surface-plate,var(--bg-light,#F8F7F3));font-family:var(--font-body);color:var(--text-secondary,#535C65)}
.mds-att__head{display:flex;gap:10px;align-items:flex-start;padding:10px 12px;border-bottom:1px solid var(--border-divider,#E2DED5)}
.mds-att__head h3{margin:0;font-size:14px;color:var(--text-primary,#20252B)}
.mds-att__scope{margin-top:2px;font:10px var(--font-data,monospace);color:var(--text-muted,#606870)}
.mds-att__tags{display:flex;flex-direction:column;align-items:flex-end;gap:3px;margin-left:auto;text-align:right}
.mds-att__version{font:10px var(--font-data,monospace);color:var(--text-muted,#606870)}
.mds-att__state{display:inline-flex;align-items:center;gap:4px;padding:2px 6px;border:1px solid var(--border,#D2CDC3);font:600 9px var(--font-data,monospace);text-transform:uppercase;letter-spacing:.04em;color:var(--text-muted,#606870);white-space:nowrap}
.mds-att__state[data-tone="pass"]{color:var(--green-dim,#23583F);border-color:var(--green,#2E7655);background:var(--green-a10,rgba(46,118,85,.10))}
.mds-att__state[data-tone="review"]{color:var(--orange-dim,#725012);border-color:var(--orange,#986717);background:var(--orange-a10,rgba(152,103,23,.10))}
.mds-att__state[data-tone="exception"]{color:var(--red-dim,#873832);border-color:var(--red,#B54B42);background:var(--red-a10,rgba(181,75,66,.10))}
.mds-att__banner{padding:8px 12px;border-bottom:1px solid var(--border-divider,#E2DED5);background:var(--orange-surface,#F2EAD8);font-size:11px;color:var(--text-primary,#20252B)}
.mds-att__banner--void{background:var(--red-surface,#F5E5E2)}
.mds-att__sub{padding:6px 12px 2px;font:600 9px var(--font-data,monospace);text-transform:uppercase;letter-spacing:.04em;color:var(--text-muted,#606870)}
.mds-att__gates{padding:0 12px 4px}
.mds-att__gate{display:grid;grid-template-columns:16px 1fr auto;gap:7px;align-items:center;min-height:29px;border-bottom:1px solid var(--border-divider,#E2DED5);font-size:10px}
.mds-att__gate:last-child{border-bottom:0}
.mds-att__mark{font-family:var(--font-data,monospace);color:var(--text-muted,#606870)}
.mds-att__gate[data-status="complete"] .mds-att__mark,.mds-att__gate[data-status="reconciled"] .mds-att__mark{color:var(--green,#2E7655)}
.mds-att__gate[data-status="blocked"] .mds-att__mark{color:var(--red,#B54B42)}
.mds-att__gate[data-status="review"] .mds-att__mark,.mds-att__gate[data-status="pending"] .mds-att__mark{color:var(--orange,#986717)}
.mds-att__gate[data-blocking="true"] span:nth-child(2){color:var(--text-primary,#20252B);font-weight:600}
.mds-att__detail{font:9px var(--font-data,monospace);color:var(--text-muted,#606870)}
.mds-att__evidence{display:grid;grid-template-columns:1fr auto;gap:8px;align-items:baseline;padding:7px 12px;border-top:1px solid var(--border-divider,#E2DED5);font-size:10px}
.mds-att__evidence b{font:11px var(--font-data,monospace);color:var(--text-primary,#20252B)}
.mds-att__evidence[data-complete="false"] b{color:var(--orange-dim,#725012)}
.mds-att__roles{display:grid;gap:0;padding:0 12px;border-top:1px solid var(--border-divider,#E2DED5)}
.mds-att__role-row{display:grid;grid-template-columns:96px 1fr auto;gap:8px;align-items:baseline;min-height:26px;border-bottom:1px solid var(--border-divider,#E2DED5);font-size:10px}
.mds-att__role-row:last-child{border-bottom:0}
.mds-att__role-row dt{font:9px var(--font-data,monospace);text-transform:uppercase;letter-spacing:.04em;color:var(--text-muted,#606870)}
.mds-att__role-row dd{margin:0;font:11px var(--font-data,monospace);color:var(--text-primary,#20252B)}
.mds-att__role-row small{font:9px var(--font-data,monospace);color:var(--text-muted,#606870)}
.mds-att__bind{display:flex;flex-wrap:wrap;gap:12px;padding:7px 12px;border-top:1px solid var(--border-divider,#E2DED5);background:var(--surface-well,var(--card-surface-raised,#FCFBF8))}
.mds-att__bind div{display:grid;gap:1px}
.mds-att__bind dt{font:9px var(--font-data,monospace);text-transform:uppercase;letter-spacing:.04em;color:var(--text-muted,#606870)}
.mds-att__bind dd{margin:0;font:10px var(--font-data,monospace);color:var(--text-primary,#20252B)}
.mds-att__cert{padding:9px 12px;border-top:1px solid var(--border-divider,#E2DED5);font-size:10px;line-height:15px}
.mds-att__cert blockquote{margin:0;padding-left:9px;border-left:2px solid var(--border-strong,#AAA49A);color:var(--text-primary,#20252B)}
.mds-att textarea{box-sizing:border-box;width:100%;min-height:58px;margin-top:7px;border:1px solid var(--border,#D2CDC3);background:var(--surface-well,var(--card-surface-raised,#FCFBF8));color:var(--text-primary,#20252B);font:11px var(--font-body);padding:7px}
.mds-att textarea:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:1px}
.mds-att__history{padding:0 12px;border-top:1px solid var(--border-divider,#E2DED5)}
.mds-att__event{display:grid;grid-template-columns:auto 1fr;gap:8px;align-items:baseline;min-height:24px;border-bottom:1px solid var(--border-divider,#E2DED5);font:10px var(--font-data,monospace);color:var(--text-muted,#606870)}
.mds-att__event:last-child{border-bottom:0}
.mds-att__event b{color:var(--text-primary,#20252B);font-weight:600}
.mds-att__block{padding:7px 12px;border-top:1px solid var(--border-divider,#E2DED5);font:10px var(--font-body);color:var(--red-dim,#873832)}
.mds-att__foot{display:flex;flex-wrap:wrap;align-items:center;gap:6px;padding:9px 12px;border-top:1px solid var(--border-strong,#AAA49A);background:var(--surface-recessed,var(--bg-medium,#EBE8E1))}
.mds-att__role{font:9px var(--font-data,monospace);color:var(--text-muted,#606870)}
.mds-att__foot .spacer{flex:1}
@media(max-width:560px){.mds-att__foot{flex-wrap:wrap}.mds-att__role{flex:1 0 100%}.mds-att__foot .spacer{display:none}.mds-att__foot .mds-btn{flex:1 1 110px}.mds-att__role-row{grid-template-columns:1fr auto}.mds-att__role-row dt{grid-column:1/-1}}
`;document.head.appendChild(el)}

const glyph=s=>s==="complete"||s==="reconciled"?"✓":s==="blocked"?"!":s==="review"?"◇":s==="pending"?"◇":"○";
const DONE=["complete","reconciled","passed","ok"];

export function AttestationPanel({
  title,scope,period,objectId,snapshot,version,role,signatoryRole,
  gates=[],evidence,history=[],signatories=[],currentUser,segregation=true,
  certification,requireComment=false,comment:commentProp,onCommentChange,
  state:stateProp,supersededBy,expiredReason,snapshotFindings=[],
  onApprove,onReject,onRequestChanges,onReopen,className="",...rest
}){
  inject();
  const[local,setLocal]=React.useState("");
  const comment=commentProp!==undefined?commentProp:local;
  const setComment=v=>{if(commentProp===undefined)setLocal(v);onCommentChange?.(v)};

  const blockedGates=gates.filter(g=>g.blocking||g.status==="blocked");
  const openGates=gates.filter(g=>!DONE.includes(g.status));
  const evidenceDeclared=!!evidence&&evidence.required!=null&&evidence.attached!=null;
  const evidenceComplete=!evidenceDeclared||evidence.attached>=evidence.required;
  // Readiness is asserted from what IS declared, never from what is absent. An empty panel used to
  // derive "Ready for attestation" — no gates to fail, no evidence to be short of, nothing bound.
  const declarationGaps=[
    !gates.length&&"no readiness gates declared",
    !evidenceDeclared&&"evidence set not declared",
    !snapshot&&"not bound to a reviewed snapshot",
    (version==null||version==="")&&"no object version bound",
    !signatories.length&&"no signatory roles declared",
  ].filter(Boolean);
  const findings=(snapshotFindings||[]).filter(Boolean);
  const voidFindings=findings.filter(f=>f.effect==="void");
  const reviewFindings=findings.filter(f=>f.effect==="review");
  const sayFinding=f=>`${f.field} ${f.was===undefined?"":String(f.was)} → ${f.now===undefined?"":String(f.now)}`.replace(/\s+/g," ").trim();
  const derived=supersededBy?"superseded":expiredReason||voidFindings.length?"expired":blockedGates.length?"blocked":openGates.length||!evidenceComplete||declarationGaps.length?"not-ready":"ready";
  // A caller may declare any state EXCEPT a readiness the declaration does not support.
  const state=stateProp==="ready"&&(declarationGaps.length||voidFindings.length)?derived:stateProp||derived;
  const meta=STATE[state]||{label:state,tone:"neutral"};
  const terminal=["approved","rejected","superseded","expired"].includes(state);

  const conflict=segregation&&currentUser?signatories.find(s=>s.name&&currentUser&&s.name===currentUser&&/prepare|review/i.test(s.role||"")):null;
  const reasons=[
    voidFindings.length&&`The reviewed snapshot changed — ${voidFindings.map(sayFinding).join(" · ")}`,
    declarationGaps.length&&`Attestation under-declared — ${declarationGaps.join(" · ")}`,
    blockedGates.length&&`${blockedGates.length} blocking condition${blockedGates.length===1?"":"s"} unresolved`,
    openGates.length&&`${openGates.length} readiness check${openGates.length===1?"":"s"} incomplete`,
    !evidenceComplete&&evidenceDeclared&&`Evidence incomplete — ${evidence.attached} of ${evidence.required} items attached`,
    conflict&&`Segregation of duties — ${currentUser} already signed as ${conflict.role}`,
    requireComment&&!comment.trim()&&"Rationale required",
    terminal&&`Attestation is ${meta.label.toLowerCase()}`,
  ].filter(Boolean);
  const ready=reasons.length===0;

  const bind=[["Object",objectId],["Version",version],["Period",period],["Snapshot",snapshot],["Signatory role",signatoryRole||role]].filter(([,v])=>v!==undefined&&v!==null&&v!=="");

  return <section className={`mds-att${className?" "+className:""}`} {...rest}>
    <header className="mds-att__head">
      <div><h3>{title}</h3>{scope||period?<div className="mds-att__scope">{[scope,period].filter(Boolean).join(" · ")}</div>:null}</div>
      <div className="mds-att__tags">
        <span className="mds-att__version">Version {version??"—"}</span>
        <span className="mds-att__state" data-tone={meta.tone}>{meta.label}</span>
      </div>
    </header>
    {supersededBy?<div className="mds-att__banner mds-att__banner--void">Superseded by {supersededBy}. This attestation no longer certifies the current record.</div>:null}
    {expiredReason?<div className="mds-att__banner mds-att__banner--void">{expiredReason}</div>:null}
    {voidFindings.length?<div className="mds-att__banner mds-att__banner--void">The reviewed snapshot changed after review — {voidFindings.map(sayFinding).join(" · ")}. The pending signature is void: re-read the record and capture a new snapshot before signing.</div>:null}
    {!voidFindings.length&&reviewFindings.length?<div className="mds-att__banner">Worth re-reading before signing — {reviewFindings.map(sayFinding).join(" · ")}.</div>:null}
    {gates.length?<><div className="mds-att__sub">Readiness</div><div className="mds-att__gates">{gates.map((g,i)=><div className="mds-att__gate" data-status={g.status} data-blocking={g.blocking||g.status==="blocked"?"true":undefined} key={g.id||g.label||i}><span className="mds-att__mark" aria-hidden="true">{glyph(g.status)}</span><span>{g.label}</span><span className="mds-att__detail">{g.detail||g.status}</span></div>)}</div></>:null}
    <div className="mds-att__evidence" data-complete={evidenceDeclared&&evidenceComplete?"true":"false"}><span>Evidence completeness{evidence&&evidence.label?` · ${evidence.label}`:""}</span><b>{evidenceDeclared?`${evidence.attached} of ${evidence.required} attached`:"Not declared"}</b></div>
    {signatories.length?<dl className="mds-att__roles">{signatories.map((s,i)=><div className="mds-att__role-row" key={s.role||i}><dt>{s.role}</dt><dd>{s.name||"Unassigned"}</dd><dd><small>{s.at||s.status||"Pending"}</small></dd></div>)}</dl>:null}
    {bind.length?<dl className="mds-att__bind">{bind.map(([k,v])=><div key={k}><dt>{k}</dt><dd>{v}</dd></div>)}</dl>:null}
    {certification||requireComment?<div className="mds-att__cert">
      {certification?<blockquote>{certification}</blockquote>:null}
      {requireComment?<textarea value={comment} onChange={e=>setComment(e.target.value)} aria-label="Attestation rationale" placeholder="Required rationale or certification note"/>:null}
    </div>:null}
    {!ready&&(!terminal||voidFindings.length)?<div className="mds-att__block">Cannot attest — {reasons.join(" · ")}.</div>:null}
    {history.length?<><div className="mds-att__sub">Approval history</div><div className="mds-att__history">{history.map((h,i)=><div className="mds-att__event" key={h.id||i}><span>{h.at}</span><span><b>{h.actor}</b> {h.action}{h.note?` — ${h.note}`:""}</span></div>)}</div></>:null}
    <footer className="mds-att__foot">
      <span className="mds-att__role">Required role: {signatoryRole||role||"Authorized reviewer"}</span>
      <span className="spacer"/>
      {onReopen?<Button size="sm" variant="ghost" onClick={onReopen}>Reopen</Button>:null}
      {onReject?<Button size="sm" variant="danger" onClick={()=>onReject(comment)} disabled={terminal}>Reject</Button>:null}
      {onRequestChanges?<Button size="sm" variant="ghost" onClick={()=>onRequestChanges(comment)} disabled={terminal}>Request changes</Button>:null}
      {onApprove?<Button size="sm" disabled={!ready} onClick={()=>onApprove(comment)}>Approve and attest</Button>:null}
    </footer>
  </section>;
}
