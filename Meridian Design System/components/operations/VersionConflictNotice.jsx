// Meridian VersionConflictNotice — optimistic-concurrency conflict stated in three-way form
// (base · yours · latest). Never defaults to overwrite, never auto-merges a conflicting financial
// value, and requires a reason before a newer saved value is intentionally replaced.
import React from "react";
import { Button } from "../core/Button";

let injected=false;function inject(){if(injected||typeof document==="undefined")return;injected=true;const el=document.createElement("style");el.setAttribute("data-mds","version-conflict");el.textContent=`
.mds-vcn{border:1px solid var(--orange,#986717);background:var(--orange-surface,#F2EAD8);font-family:var(--font-body);color:var(--text-secondary,#535C65)}
.mds-vcn__head{display:flex;gap:9px;padding:10px 12px;border-bottom:1px solid color-mix(in srgb,var(--orange,#986717) 30%,transparent)}
.mds-vcn__glyph{color:var(--orange,#986717);font-weight:700}
.mds-vcn h3{margin:0;font-size:13px;color:var(--text-primary,#20252B)}
.mds-vcn h3:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:2px}
.mds-vcn p{margin:2px 0 0;font-size:10px}
.mds-vcn__lineage{margin-top:4px;font:10px var(--font-data,monospace);color:var(--text-muted,#606870)}
.mds-vcn__table{border-bottom:1px solid color-mix(in srgb,var(--orange,#986717) 30%,transparent)}
.mds-vcn__r{display:grid;grid-template-columns:minmax(110px,1fr) 1fr 1fr 1fr;gap:8px;align-items:start;padding:7px 12px;border-bottom:1px solid color-mix(in srgb,var(--orange,#986717) 22%,transparent)}
.mds-vcn__r:last-child{border-bottom:0}
.mds-vcn__r--head{padding-top:8px;padding-bottom:5px;font:600 9px var(--font-data,monospace);text-transform:uppercase;letter-spacing:.04em;color:var(--text-muted,#606870)}
.mds-vcn__r--clean{color:var(--text-muted,#606870)}
.mds-vcn__field{display:flex;align-items:baseline;gap:5px;font-size:11px;font-weight:600;color:var(--text-primary,#20252B)}
.mds-vcn__flag{font:9px var(--font-data,monospace);text-transform:uppercase;letter-spacing:.03em;color:var(--red-dim,#873832)}
.mds-vcn__r--clean .mds-vcn__flag{color:var(--green-dim,#23583F)}
.mds-vcn__cell{display:grid;gap:3px;font:11px var(--font-data,monospace);color:var(--text-primary,#20252B)}
.mds-vcn__cell small{font:9px var(--font-data,monospace);color:var(--text-muted,#606870)}
.mds-vcn__pick{display:inline-flex;align-items:center;gap:5px;font:9px var(--font-data,monospace);text-transform:uppercase;letter-spacing:.03em;color:var(--text-muted,#606870);cursor:pointer}
.mds-vcn__pick input{accent-color:var(--border-focus,#2D6FB3);margin:0}
.mds-vcn__pick input:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:2px}
.mds-vcn__clean{padding:7px 12px;border-bottom:1px solid color-mix(in srgb,var(--orange,#986717) 30%,transparent);font:10px var(--font-data,monospace);color:var(--text-muted,#606870)}
.mds-vcn__reason{display:grid;gap:5px;padding:9px 12px;border-bottom:1px solid color-mix(in srgb,var(--orange,#986717) 30%,transparent)}
.mds-vcn__reason label{font:600 9px var(--font-data,monospace);text-transform:uppercase;letter-spacing:.04em;color:var(--text-muted,#606870)}
.mds-vcn__reason textarea{box-sizing:border-box;width:100%;min-height:46px;border:1px solid var(--border,#D2CDC3);background:var(--surface-well,var(--card-surface-raised,#FCFBF8));color:var(--text-primary,#20252B);font:11px var(--font-body);padding:6px}
.mds-vcn__reason textarea:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:1px}
.mds-vcn__note{padding:7px 12px;font:10px var(--font-body);color:var(--text-secondary,#535C65);border-bottom:1px solid color-mix(in srgb,var(--orange,#986717) 30%,transparent)}
.mds-vcn__actions{display:flex;flex-wrap:wrap;justify-content:flex-end;gap:6px;padding:8px 12px}
@media(max-width:640px){.mds-vcn__r{grid-template-columns:1fr 1fr;row-gap:5px}.mds-vcn__r--head{display:none}.mds-vcn__field{grid-column:1/-1}.mds-vcn__actions .mds-btn{flex:1 1 132px}}
`;document.head.appendChild(el)}

const show=v=>v===null||v===undefined||v===""?"—":typeof v==="boolean"?(v?"Yes":"No"):String(v);

export function VersionConflictNotice({
  title="Record changed while you were editing",record,
  fields,field,proposed,latest,base,
  baseVersion,originalVersion,latestVersion,latestAuthor,latestAt,
  choices,onFieldChoice,requireReason=true,
  onReload,onCompare,onMergeNonConflicting,onReapply,onSaveDraft,onDiscard,
  autoFocus=true,className="",...rest
}){
  const uid = React.useId();
  inject();
  const headingRef=React.useRef(null);
  React.useEffect(()=>{if(autoFocus)headingRef.current?.focus()},[autoFocus]);
  const baseVer=baseVersion??originalVersion;
  const rows=(fields&&fields.length?fields:[{key:field||"field",label:field||"This field",base,proposed,latest}]).map(r=>({
    ...r,key:r.key||r.label,
    conflict:r.conflict!==undefined?r.conflict:(String(show(r.proposed))!==String(show(r.base))&&String(show(r.latest))!==String(show(r.base))&&String(show(r.proposed))!==String(show(r.latest))),
  }));
  const conflicts=rows.filter(r=>r.conflict),clean=rows.filter(r=>!r.conflict);
  const[localChoices,setLocalChoices]=React.useState({});
  const picked=choices||localChoices;
  const pick=(key,side)=>{if(!choices)setLocalChoices(c=>({...c,[key]:side}));onFieldChoice?.(key,side)};
  const[reason,setReason]=React.useState("");
  const allPicked=conflicts.every(r=>picked[r.key]);
  const keepsMine=conflicts.some(r=>picked[r.key]==="mine");
  const canReapply=(!requireReason||reason.trim().length>0)&&(conflicts.length===0||allPicked);

  return <section className={`mds-vcn${className?" "+className:""}`} role="alert" aria-live="assertive" {...rest}>
    <header className="mds-vcn__head">
      <span className="mds-vcn__glyph" aria-hidden="true">◇</span>
      <div>
        <h3 ref={headingRef} tabIndex={-1}>{title}</h3>
        <p>{record?`${record} · `:""}{conflicts.length?`${conflicts.length} field${conflicts.length===1?"":"s"} conflict`:"No field conflicts"}{clean.length?` · ${clean.length} field${clean.length===1?"":"s"} merge cleanly`:""}. Nothing has been saved.</p>
        <div className="mds-vcn__lineage">Your edit began from version {show(baseVer)} · version {show(latestVersion)} was saved by {latestAuthor||"another operator"}{latestAt?` at ${latestAt}`:""}</div>
      </div>
    </header>
    <div className="mds-vcn__table">
      <div className="mds-vcn__r mds-vcn__r--head"><span>Field</span><span>Base · v{show(baseVer)}</span><span>Your proposed value</span><span>Latest saved · v{show(latestVersion)}</span></div>
      {conflicts.map(r=><div className="mds-vcn__r" key={r.key}>
        <span className="mds-vcn__field">{r.label||r.key}<span className="mds-vcn__flag">Conflict</span></span>
        <span className="mds-vcn__cell">{show(r.base)}<small>Both edits diverge from this</small></span>
        <span className="mds-vcn__cell">{show(r.proposed)}{onFieldChoice||!choices?<label className="mds-vcn__pick"><input type="radio" name={`mds-vcn-${r.key}`} checked={picked[r.key]==="mine"} onChange={()=>pick(r.key,"mine")}/>Keep mine</label>:null}</span>
        <span className="mds-vcn__cell">{show(r.latest)}{onFieldChoice||!choices?<label className="mds-vcn__pick"><input type="radio" name={`mds-vcn-${r.key}`} checked={picked[r.key]==="theirs"} onChange={()=>pick(r.key,"theirs")}/>Keep latest</label>:null}</span>
      </div>)}
      {clean.map(r=><div className="mds-vcn__r mds-vcn__r--clean" key={r.key}>
        <span className="mds-vcn__field">{r.label||r.key}<span className="mds-vcn__flag">Merges</span></span>
        <span className="mds-vcn__cell">{show(r.base)}</span>
        <span className="mds-vcn__cell">{show(r.proposed)}</span>
        <span className="mds-vcn__cell">{show(r.latest)}</span>
      </div>)}
    </div>
    {keepsMine?<div className="mds-vcn__note">Keeping your value replaces a newer saved value. Both versions stay in the audit record, validation re-runs, and any impact preview taken against version {show(baseVer)} is invalidated.</div>:null}
    {requireReason?<div className="mds-vcn__reason">
      <label htmlFor={`${uid}-reason`}>Reason for replacing the newer record</label>
      <textarea id={`${uid}-reason`} value={reason} onChange={e=>setReason(e.target.value)} placeholder="Name the authority or evidence that makes your value correct."/>
    </div>:null}
    <footer className="mds-vcn__actions">
      {onDiscard?<Button size="sm" variant="danger" onClick={onDiscard}>Discard my draft</Button>:null}
      {onSaveDraft?<Button size="sm" variant="ghost" onClick={()=>onSaveDraft(reason)}>Save as new draft</Button>:null}
      {onCompare?<Button size="sm" variant="ghost" onClick={onCompare}>Compare all changes</Button>:null}
      {onMergeNonConflicting?<Button size="sm" variant="ghost" onClick={onMergeNonConflicting} disabled={!clean.length}>Merge {clean.length} nonconflicting</Button>:null}
      {onReapply?<Button size="sm" variant="ghost" disabled={!canReapply} onClick={()=>onReapply({reason:reason.trim(),choices:picked})}>Reapply with rationale</Button>:null}
      {onReload?<Button size="sm" onClick={onReload}>Reload latest record</Button>:null}
    </footer>
  </section>;
}
