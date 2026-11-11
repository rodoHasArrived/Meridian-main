// Meridian ChangeSetReview — edits become a staged, inspectable transaction: stage → validate →
// review impact → commit. ChangeSetProvider owns original/draft state, ChangeSetDiff shows
// field-level before/after with reason and validation, ChangeReviewBar is the persistent status.
import React from "react";
import { formatNumber } from "../core/format.js";
import { Button } from "../core/Button";

const Context=React.createContext(null);
function equal(a,b){return JSON.stringify(a)===JSON.stringify(b)}
const show=v=>v===null||v===undefined||v===""?"—":typeof v==="boolean"?(v?"Yes":"No"):String(v);

const STATUS={
  draft:{label:"Draft",tone:"neutral"},validating:{label:"Validating",tone:"neutral"},
  invalid:{label:"Validation failed",tone:"exception"},"validation-failed":{label:"Validation failed",tone:"exception"},
  warning:{label:"Warning present",tone:"review"},ready:{label:"Ready for review",tone:"pass"},
  submitted:{label:"Submitted",tone:"info"},approved:{label:"Approved",tone:"pass"},
  rejected:{label:"Rejected",tone:"exception"},committed:{label:"Committed",tone:"pass"},
  partial:{label:"Partially committed",tone:"review"},superseded:{label:"Superseded",tone:"neutral"},
  conflicted:{label:"Conflicted",tone:"exception"},clean:{label:"No staged changes",tone:"neutral"},
};

export function ChangeSetProvider({original={},value,onChange,version,id,fields={},validate,children}){
  const controlled=value!==undefined;
  const[draft,setDraft]=React.useState(()=>({...original}));
  const[reasons,setReasons]=React.useState({});
  React.useEffect(()=>{if(!controlled)setDraft({...original})},[original]);
  const current=controlled?value:draft;
  const set=next=>{if(!controlled)setDraft(next);onChange?.(next)};
  const setField=(key,v)=>set({...current,[key]:v});
  const setReason=(key,v)=>setReasons(r=>({...r,[key]:v}));
  const discard=()=>{set({...original});setReasons({})};
  const changes=Object.keys({...original,...current}).filter(k=>!equal(original[k],current[k])).map(k=>({key:k,before:original[k],after:current[k],group:fields[k]?.group,label:fields[k]?.label,impact:fields[k]?.impact,reason:reasons[k]}));
  const issues=(validate?validate(current,changes):[])||[];
  const errors=issues.filter(i=>i.severity==="error");
  const status=!changes.length?"clean":errors.length?"invalid":issues.length?"warning":"ready";
  const api={id,version,original,draft:current,setDraft:set,setField,discard,changes,issues,errors,reasons,setReason,status,dirty:changes.length>0};
  return <Context.Provider value={api}>{children}</Context.Provider>;
}
export function useChangeSet(){const v=React.useContext(Context);if(!v)throw new Error("useChangeSet must be used inside ChangeSetProvider");return v}
// Capitalized carrier so the hook is reachable from the compiled bundle namespace
// (matching TableHooks / FormHooks). Also available as ChangeSetProvider.use.
export const ChangeSetHooks={useChangeSet};
ChangeSetProvider.use=useChangeSet;

let injected=false;function inject(){if(injected||typeof document==="undefined")return;injected=true;const el=document.createElement("style");el.setAttribute("data-mds","change-set");el.textContent=`
.mds-cs{border:1px solid var(--border,#D2CDC3);background:var(--surface-plate,var(--bg-light,#F8F7F3));font-family:var(--font-body);color:var(--text-secondary,#535C65)}
.mds-cs__head{display:flex;gap:10px;align-items:flex-start;padding:10px 12px;border-bottom:1px solid var(--border-divider,#E2DED5)}
.mds-cs__head h3{margin:0;font-size:14px;color:var(--text-primary,#20252B)}
.mds-cs__context{margin-top:2px;font:10px var(--font-data,monospace);color:var(--text-muted,#606870)}
.mds-cs__tags{display:flex;flex-direction:column;align-items:flex-end;gap:3px;margin-left:auto;text-align:right}
.mds-cs__count{font:600 10px var(--font-data,monospace);color:var(--selected,#496C8F)}
.mds-cs__status{display:inline-flex;align-items:center;gap:4px;font:600 9px var(--font-data,monospace);text-transform:uppercase;letter-spacing:.04em;padding:2px 6px;border:1px solid var(--border,#D2CDC3);background:var(--surface-well,var(--card-surface-raised,#FCFBF8));color:var(--text-muted,#606870)}
.mds-cs__status[data-tone="pass"]{color:var(--green-dim,#23583F);border-color:var(--green,#2E7655);background:var(--green-a10,rgba(46,118,85,.10))}
.mds-cs__status[data-tone="review"]{color:var(--orange-dim,#725012);border-color:var(--orange,#986717);background:var(--orange-a10,rgba(152,103,23,.10))}
.mds-cs__status[data-tone="exception"]{color:var(--red-dim,#873832);border-color:var(--red,#B54B42);background:var(--red-a10,rgba(181,75,66,.10))}
.mds-cs__status[data-tone="info"]{color:var(--info,#2F648A);border-color:var(--info,#2F648A);background:var(--info-a10,rgba(47,100,138,.10))}
.mds-cs__scope{display:flex;flex-wrap:wrap;gap:14px;padding:7px 12px;border-bottom:1px solid var(--border-divider,#E2DED5);background:var(--surface-well,var(--card-surface-raised,#FCFBF8))}
.mds-cs__scope div{display:grid;gap:1px}
.mds-cs__scope dt{font:9px var(--font-data,monospace);text-transform:uppercase;letter-spacing:.04em;color:var(--text-muted,#606870)}
.mds-cs__scope dd{margin:0;font:11px var(--font-data,monospace);color:var(--text-primary,#20252B)}
.mds-cs__diff{padding:0 12px}
.mds-cs__group{padding:7px 0 2px;font:600 9px var(--font-data,monospace);text-transform:uppercase;letter-spacing:.04em;color:var(--text-muted,#606870)}
.mds-cs__row{display:grid;grid-template-columns:minmax(120px,1fr) 1fr 20px 1fr;gap:8px;min-height:32px;align-items:center;border-bottom:1px solid var(--border-divider,#E2DED5);font-size:11px}
.mds-cs__label{font-weight:600;color:var(--text-secondary,#535C65)}
.mds-cs__before{font-family:var(--font-data,monospace);color:var(--text-muted,#606870);text-decoration:line-through}
.mds-cs__after{font-family:var(--font-data,monospace);color:var(--text-primary,#20252B);font-weight:600}
.mds-cs__meta{grid-column:1/-1;display:grid;gap:3px;padding:0 0 7px}
.mds-cs__impact{font:10px var(--font-body);color:var(--text-muted,#606870)}
.mds-cs__issue{display:flex;gap:5px;font:10px var(--font-data,monospace)}
.mds-cs__issue[data-sev="error"]{color:var(--red-dim,#873832)}
.mds-cs__issue[data-sev="warning"]{color:var(--orange-dim,#725012)}
.mds-cs__issue[data-sev="info"]{color:var(--info,#2F648A)}
.mds-cs__reason{display:grid;gap:3px}
.mds-cs__reason label{font:9px var(--font-data,monospace);text-transform:uppercase;color:var(--text-muted,#606870)}
.mds-cs__reason input{box-sizing:border-box;width:100%;height:26px;border:1px solid var(--border,#D2CDC3);background:var(--surface-well,var(--card-surface-raised,#FCFBF8));color:var(--text-primary,#20252B);font:11px var(--font-body);padding:0 7px}
.mds-cs__reason input:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:1px}
.mds-cs__empty{padding:12px;font-size:11px;color:var(--text-muted,#606870)}
.mds-cs__warning{margin:7px 12px;padding:7px 9px;border-left:2px solid var(--orange,#986717);background:var(--orange-surface,#F2EAD8);font-size:10px;color:var(--text-secondary,#535C65)}
.mds-cs__section{padding:8px 12px;border-top:1px solid var(--border-divider,#E2DED5)}
.mds-cs__section h4{margin:0 0 5px;font:600 9px var(--font-data,monospace);text-transform:uppercase;letter-spacing:.04em;color:var(--text-muted,#606870)}
.mds-cs__impacts{display:grid;gap:0}
.mds-cs__impactrow{display:grid;grid-template-columns:16px 1fr auto;gap:7px;align-items:baseline;min-height:24px;border-bottom:1px solid var(--border-divider,#E2DED5);font-size:10px}
.mds-cs__impactrow:last-child{border-bottom:0}
.mds-cs__impactrow b{font:11px var(--font-data,monospace);font-weight:600;color:var(--text-primary,#20252B)}
.mds-cs__impactrow span[data-sev="warning"]{color:var(--orange,#986717)}
.mds-cs__impactrow span[data-sev="error"]{color:var(--red,#B54B42)}
.mds-cs__confirm{display:grid;gap:7px;padding:9px 12px;border-top:1px solid var(--border-strong,#AAA49A);background:var(--red-surface,#F5E5E2)}
.mds-cs__confirm p{margin:0;font-size:11px;color:var(--text-primary,#20252B)}
.mds-cs__confirm ul{margin:0;padding-left:16px;font:10px var(--font-data,monospace);color:var(--text-secondary,#535C65)}
.mds-cs__confirm .row{display:flex;gap:6px;justify-content:flex-end}
.mds-cs__bar{display:flex;align-items:center;gap:6px;padding:9px 12px;border-top:1px solid var(--border-strong,#AAA49A);background:var(--surface-recessed,var(--bg-medium,#EBE8E1))}
.mds-cs__bar span{font:10px var(--font-data,monospace);color:var(--text-muted,#606870)}
.mds-cs__bar .spacer{flex:1}
@media(max-width:560px){.mds-cs__bar{flex-wrap:wrap}.mds-cs__bar>span:first-child{flex:1 0 100%}.mds-cs__bar .spacer{display:none}.mds-cs__bar .mds-btn{flex:1 1 105px}.mds-cs__row{grid-template-columns:1fr 18px 1fr}.mds-cs__label{grid-column:1/-1;padding-top:5px}}
`;document.head.appendChild(el)}

export function ChangeSetDiff({changes=[],labels={},fields={},issues=[],reasons={},onReason,requireReason=false,grouped=true}){
  const uid=React.useId();
  inject();
  if(!changes.length)return <div className="mds-cs__diff"><div className="mds-cs__empty">No staged changes. Edits are held here for review before they touch the record.</div></div>;
  const nameOf=c=>c.label||fields[c.key]?.label||labels[c.key]||c.key;
  const groups=grouped?changes.reduce((m,c)=>{const g=c.group||fields[c.key]?.group||"Changes";(m[g]=m[g]||[]).push(c);return m},{}):{"":changes};
  return <div className="mds-cs__diff">{Object.entries(groups).map(([g,rows])=><React.Fragment key={g||"all"}>
    {g?<div className="mds-cs__group">{g}</div>:null}
    {rows.map(c=>{const rowIssues=issues.filter(i=>i.key===c.key);const impact=c.impact||fields[c.key]?.impact;const reason=reasons[c.key]??c.reason;return <div className="mds-cs__row" key={c.key}>
      <span className="mds-cs__label">{nameOf(c)}</span>
      <span className="mds-cs__before">{show(c.before)}</span>
      <span aria-hidden="true">→</span>
      <span className="mds-cs__after">{show(c.after)}</span>
      {impact||rowIssues.length||requireReason?<div className="mds-cs__meta">
        {impact?<div className="mds-cs__impact">{impact}</div>:null}
        {rowIssues.map((i,n)=><div className="mds-cs__issue" data-sev={i.severity||"warning"} key={n}><span aria-hidden="true">{i.severity==="error"?"▲":i.severity==="info"?"·":"◇"}</span>{i.message}</div>)}
        {requireReason?<div className="mds-cs__reason"><label htmlFor={`${uid}-reason-${c.key}`}>Reason for change</label><input id={`${uid}-reason-${c.key}`} value={reason||""} readOnly={!onReason} onChange={e=>onReason?.(c.key,e.target.value)} placeholder="Why this value changes"/></div>:null}
      </div>:null}
    </div>})}
  </React.Fragment>)}</div>;
}

export function ChangeReviewBar({changes=[],warnings=[],errors=[],status,onDiscard,onReviewImpact,onSave,onSubmit,saveLabel="Save changes",submitLabel="Submit for review",disabled}){
  inject();
  const n=changes.length;
  const parts=[`${n} staged change${n===1?"":"s"}`];
  if(errors.length)parts.push(`${errors.length} error${errors.length===1?"":"s"}`);
  if(warnings.length)parts.push(`${warnings.length} warning${warnings.length===1?"":"s"}`);
  if(status&&STATUS[status])parts.push(STATUS[status].label);
  return <div className="mds-cs__bar">
    <span>{parts.join(" · ")}</span><span className="spacer"/>
    {onDiscard?<Button size="sm" variant="ghost" onClick={onDiscard} disabled={!n}>Discard</Button>:null}
    {onReviewImpact?<Button size="sm" variant="ghost" onClick={onReviewImpact} disabled={!n}>Review impact</Button>:null}
    {onSubmit?<Button size="sm" variant="ghost" onClick={onSubmit} disabled={disabled||!n||!!errors.length}>{submitLabel}</Button>:null}
    {onSave?<Button size="sm" onClick={onSave} disabled={disabled||!n||!!errors.length}>{saveLabel}</Button>:null}
  </div>;
}

function normalizeImpact(impact){
  if(!impact)return[];
  if(Array.isArray(impact))return impact;
  const map=[["records","Affected records"],["recalculations","Recalculations required"],["reports","Reports that may change"],["controls","Controls that must rerun"],["lockedPeriod","Affects a locked period","warning"],["invalidatesApprovals","Prior approvals become invalid","warning"],["auditEvent","Audit event created"]];
  return map.filter(([k])=>impact[k]!==undefined&&impact[k]!==null&&impact[k]!==false).map(([k,label,sev])=>({label,value:impact[k]===true?"Yes":impact[k],severity:impact[k]===true?sev||"warning":undefined}));
}

export function ChangeSetReview({
  title="Review changes",context,changeSetId,scope,affectedRecords,initiatedBy,basedOnVersion,version,
  labels={},fields={},grouped=true,requireReason=false,
  warnings=[],issues,impact,status:statusProp,
  confirmDiscard=true,onDiscard,onReviewImpact,onSave,onSubmit,saveLabel,submitLabel,
  className="",...rest
}){
  inject();
  const cs=useChangeSet();
  const all=issues||cs.issues||[];
  const errors=all.filter(i=>i.severity==="error");
  const warns=[...warnings,...all.filter(i=>i.severity==="warning"&&!i.key).map(i=>i.message)];
  const status=statusProp||cs.status;
  const unavailable=["validating","committed","superseded","conflicted"].includes(status);
  const missingReasons=requireReason&&cs.changes.some(change=>!(cs.reasons[change.key]||change.reason||"").trim());
  const tone=STATUS[status]?.tone||"neutral";
  const impacts=normalizeImpact(impact);
  const[confirming,setConfirming]=React.useState(false);
  const ver=version??cs.version??basedOnVersion;
  const scopeItems=[changeSetId||cs.id?{k:"Change set",v:changeSetId||cs.id}:null,scope?{k:"Scope",v:scope}:null,affectedRecords!=null?{k:"Affected records",v:formatNumber(Number(affectedRecords), { decimals: 0 })}:null,initiatedBy?{k:"Initiated by",v:initiatedBy}:null,ver!=null?{k:"Based on version",v:ver}:null].filter(Boolean);

  return <section className={`mds-cs${className?" "+className:""}`} {...rest}>
    <header className="mds-cs__head">
      <div><h3>{title}</h3>{context?<div className="mds-cs__context">{context}</div>:null}</div>
      <div className="mds-cs__tags">
        <span className="mds-cs__count">{cs.changes.length} changed</span>
        <span className="mds-cs__status" data-tone={tone}>{STATUS[status]?.label||status}</span>
      </div>
    </header>
    {scopeItems.length?<dl className="mds-cs__scope">{scopeItems.map(i=><div key={i.k}><dt>{i.k}</dt><dd>{i.v}</dd></div>)}</dl>:null}
    <ChangeSetDiff changes={cs.changes} labels={labels} fields={fields} issues={all} reasons={cs.reasons} onReason={cs.setReason} requireReason={requireReason} grouped={grouped}/>
    {warns.map((w,i)=><div className="mds-cs__warning" key={i}>{w}</div>)}
    {impacts.length?<div className="mds-cs__section"><h4>Impact before commitment</h4><div className="mds-cs__impacts">{impacts.map((x,i)=><div className="mds-cs__impactrow" key={i}><span aria-hidden="true" data-sev={x.severity}>{x.severity==="error"?"▲":x.severity==="warning"?"◇":"·"}</span><span>{x.label}</span><b>{x.value}</b></div>)}</div></div>:null}
    {confirming?<div className="mds-cs__confirm">
      <p>Discard {cs.changes.length} staged change{cs.changes.length===1?"":"s"}? The record is untouched, but this staged work is not recoverable.</p>
      <ul>{cs.changes.map(c=><li key={c.key}>{c.label||fields[c.key]?.label||labels[c.key]||c.key}: {show(c.before)} → {show(c.after)}</li>)}</ul>
      <div className="row"><Button size="sm" variant="ghost" onClick={()=>setConfirming(false)}>Keep editing</Button><Button size="sm" variant="danger" onClick={()=>{setConfirming(false);(onDiscard||cs.discard)()}}>Discard staged changes</Button></div>
    </div>:null}
    <ChangeReviewBar changes={cs.changes} warnings={warns} errors={errors} status={status}
      onDiscard={confirmDiscard?()=>setConfirming(true):(onDiscard||cs.discard)}
      onReviewImpact={onReviewImpact} onSubmit={onSubmit?()=>onSubmit(cs.draft,cs.changes):undefined}
      onSave={onSave?()=>onSave(cs.draft,cs.changes):undefined} disabled={unavailable||missingReasons} saveLabel={saveLabel} submitLabel={submitLabel}/>
  </section>;
}
