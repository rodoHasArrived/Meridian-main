// Meridian CloseCalendar — the accounting close as an operational control surface: time,
// responsibility, dependency, evidence, and sign-off. Three views over one task set —
// calendar (temporal orientation), dependency (sequence and blockers), owner (workload) —
// plus an agenda fallback on narrow screens. Not a heatmap.
import React from "react";
import { Datum } from "../core/Datum";

const STATUS={
  "not-started":{label:"Not started",tone:"idle",glyph:"○"},
  "in-progress":{label:"In progress",tone:"active",glyph:"◐"},
  waiting:{label:"Waiting on dependency",tone:"idle",glyph:"◇"},
  "at-risk":{label:"At risk",tone:"review",glyph:"◔"},
  blocked:{label:"Blocked",tone:"blocked",glyph:"▲"},
  review:{label:"Ready for review",tone:"review",glyph:"◆"},
  complete:{label:"Complete",tone:"done",glyph:"✓"},
  reopened:{label:"Reopened",tone:"review",glyph:"↺"},
  waived:{label:"Waived",tone:"idle",glyph:"—"},
};
const ALIAS={pending:"not-started",todo:"not-started",started:"in-progress",running:"in-progress",late:"at-risk",overdue:"at-risk",done:"complete",signed:"complete",ready:"review"};
const st=v=>{const k=String(v||"not-started").trim().toLowerCase().replace(/[_\s]+/g,"-");const key=STATUS[k]?k:ALIAS[k]||"not-started";return{key,...STATUS[key]}};
const VIEWS=[{id:"calendar",label:"Calendar"},{id:"dependency",label:"Dependency"},{id:"owner",label:"Owner"}];

let injected=false;function inject(){if(injected||typeof document==="undefined")return;injected=true;const el=document.createElement("style");el.setAttribute("data-mds","close-calendar");el.textContent=`
.mds-close{border:1px solid var(--border,#D2CDC3);background:var(--surface-plate,var(--bg-light,#F8F7F3));font-family:var(--font-body);position:relative}
.mds-close__toolbar{display:flex;gap:10px;align-items:center;padding:7px 9px;border-bottom:1px solid var(--border-divider,#E2DED5)}
.mds-close__title{font:600 12px var(--font-body);color:var(--text-primary,#20252B)}
.mds-close__stats{font:9px var(--font-data,monospace);color:var(--text-muted,#606870)}
.mds-close__views{margin-left:auto;display:flex;border:1px solid var(--border,#D2CDC3);border-radius:2px;overflow:hidden}
.mds-close__views button{appearance:none;border:0;border-left:1px solid var(--border,#D2CDC3);background:var(--surface-well,#FCFBF8);font:9px var(--font-data,monospace);text-transform:uppercase;letter-spacing:.04em;color:var(--text-muted,#606870);padding:4px 9px;cursor:pointer}
.mds-close__views button:first-child{border-left:0}
.mds-close__views button[aria-pressed="true"]{background:var(--selected-surface,#E7EDF2);color:var(--text-primary,#20252B);font-weight:600}
.mds-close__views button:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:-2px}
.mds-close__grid{display:grid;grid-template-columns:repeat(var(--close-cols,7),minmax(132px,1fr));overflow:auto}
.mds-close__day{min-height:var(--calendar-cell-min-height,170px);border-right:1px solid var(--border-divider,#E2DED5);position:relative}
.mds-close__day:last-child{border-right:0}
.mds-close__day[data-past="true"] .mds-close__date{color:var(--text-muted,#606870)}
.mds-close__date{display:flex;justify-content:space-between;gap:6px;padding:7px 8px;border-bottom:1px solid var(--border-divider,#E2DED5);font:600 10px var(--font-data,monospace);color:var(--text-muted,#606870);background:var(--surface-recessed,var(--bg-medium,#EBE8E1))}
.mds-close__tasks{padding:5px;display:grid;gap:4px}
.mds-close__task{display:grid;gap:2px;width:100%;border:0;border-left:2px solid var(--neutral,#6D757D);background:var(--surface-well,#FCFBF8);padding:6px;text-align:left;cursor:pointer;color:var(--text-secondary,#535C65);font-family:var(--font-body)}
.mds-close__task:hover{background:var(--bg-hover,#EDEFF0)}
.mds-close__task:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:1px}
.mds-close__task[data-selected="true"]{background:var(--selected-surface,#E7EDF2)}
.mds-close__task[data-tone="done"]{border-left-color:var(--green,#2E7655)}
.mds-close__task[data-tone="review"]{border-left-color:var(--orange,#986717)}
.mds-close__task[data-tone="active"]{border-left-color:var(--purple,#2F648A)}
.mds-close__task[data-tone="blocked"]{border-left-color:var(--red,#B54B42);background:var(--red-surface,rgba(245,229,226,.1))}
.mds-close__name{font-size:10px;font-weight:600;color:var(--text-primary,#20252B)}
.mds-close__meta{font:9px/13px var(--font-data,monospace);color:var(--text-muted,#606870)}
.mds-close__mark{display:inline-block;width:10px;text-align:center;margin-right:3px}
.mds-close__rows{padding:2px 10px 8px}
.mds-close__row{display:grid;grid-template-columns:20px minmax(160px,1.6fr) 1fr 108px 96px 78px;gap:10px;align-items:center;width:100%;min-height:36px;border:0;border-bottom:1px solid var(--border-divider,#E2DED5);background:transparent;text-align:left;font-size:11px;color:var(--text-secondary,#535C65);cursor:pointer;font-family:var(--font-body)}
.mds-close__row:hover{background:var(--bg-hover,#EDEFF0)}
.mds-close__row:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:-2px}
.mds-close__row[data-selected="true"]{background:var(--selected-surface,#E7EDF2)}
.mds-close__row strong{color:var(--text-primary,#20252B);font-weight:600}
.mds-close__row small{display:block;font:9px/13px var(--font-data,monospace);color:var(--text-muted,#606870)}
.mds-close__row[data-tone="blocked"] .mds-close__status{color:var(--red-dim,#873832);font-weight:600}
.mds-close__row[data-tone="review"] .mds-close__status{color:var(--orange-dim,#724D11)}
.mds-close__row[data-tone="done"] .mds-close__status{color:var(--green-dim,#23583F)}
.mds-close__status,.mds-close__due{font:10px var(--font-data,monospace);font-variant-numeric:tabular-nums}
.mds-close__depth{padding-left:calc(var(--depth,0) * 14px)}
.mds-close__group{display:flex;justify-content:space-between;gap:10px;padding:8px 10px 3px;border-bottom:1px solid var(--border-strong,#AAA49A);font:9px var(--font-data,monospace);text-transform:uppercase;letter-spacing:.05em;color:var(--text-muted,#606870);margin-top:8px}
.mds-close__legend{display:flex;gap:12px;flex-wrap:wrap;padding:7px 10px;border-top:1px solid var(--border-divider,#E2DED5);font:9px var(--font-data,monospace);color:var(--text-muted,#606870)}
@media (max-width:720px){.mds-close__grid{grid-template-columns:1fr}.mds-close__day{min-height:0}}
`;document.head.appendChild(el)}

function TaskRow({t,depth=0,selectedId,onSelect,tasks}){
  const s=st(t.status);
  const requires=(t.dependsOn||[]).map(id=>tasks.find(x=>String(x.id)===String(id))?.title||id);
  return <button type="button" className="mds-close__row mds-close__depth" style={{"--depth":depth}} data-tone={s.tone} data-selected={String(t.id)===String(selectedId)?"true":undefined} onClick={()=>onSelect?.(t)}>
    <span className="mds-close__mark" aria-hidden="true">{s.glyph}</span>
    <span><strong>{t.title}</strong>{requires.length?<small>Requires: {requires.join(", ")}</small>:null}{t.blocker?<small>Blocked: {t.blocker}</small>:null}</span>
    <span>{t.evidence?<small>Evidence: {t.evidence}</small>:null}{t.signoff?<small>Sign-off: {t.signoff}</small>:null}</span>
    <span className="mds-close__due">{t.due||"No due time"}</span>
    <span>{t.owner||"Unassigned"}</span>
    <span className="mds-close__status">{s.label}</span>
  </button>;
}

export function CloseCalendar({
  days=[],tasks=[],currentDate,currentTime,title="Close calendar",period,
  selectedId,onSelect,view:viewProp,defaultView="calendar",onViewChange,
  className="",...rest
}){
  inject();
  const[own,setOwn]=React.useState(defaultView);
  const view=viewProp||own;
  const setView=v=>{if(viewProp===undefined)setOwn(v);if(onViewChange)onViewChange(v)};
  const counts=tasks.reduce((a,t)=>{const k=st(t.status).key;a[k]=(a[k]||0)+1;return a},{});
  const blocked=(counts.blocked||0)+(counts["at-risk"]||0);
  const dayIndex=days.findIndex(d=>d.date===currentDate);

  const ordered=React.useMemo(()=>{
    const byId=new Map(tasks.map(t=>[String(t.id),t]));
    const depth=new Map();
    const walk=(t,seen=new Set())=>{
      const id=String(t.id);
      if(depth.has(id))return depth.get(id);
      if(seen.has(id))return 0;
      seen.add(id);
      const d=(t.dependsOn||[]).reduce((m,p)=>{const parent=byId.get(String(p));return parent?Math.max(m,walk(parent,seen)+1):m},0);
      depth.set(id,d);return d;
    };
    return tasks.map(t=>({t,depth:walk(t)})).sort((a,b)=>a.depth-b.depth||String(a.t.due||"").localeCompare(String(b.t.due||"")));
  },[tasks]);

  const owners=React.useMemo(()=>{
    const m=new Map();
    tasks.forEach(t=>{const k=t.team||t.owner||"Unassigned";if(!m.has(k))m.set(k,[]);m.get(k).push(t)});
    return [...m.entries()];
  },[tasks]);

  return <section className={`mds-close${className?" "+className:""}`} {...rest}>
    <div className="mds-close__toolbar">
      <span className="mds-close__title">{title}</span>
      <span className="mds-close__stats">{period?`${period} · `:""}{tasks.length} tasks · {counts.complete||0} complete{blocked?` · ${blocked} blocked or at risk`:""}{currentTime?` · now ${currentTime}`:""}</span>
      <div className="mds-close__views" role="group" aria-label="Close view">{VIEWS.map(v=><button key={v.id} type="button" aria-pressed={view===v.id} onClick={()=>setView(v.id)}>{v.label}</button>)}</div>
    </div>

    {view==="calendar"?<div className="mds-close__grid" style={{"--close-cols":days.length||1}}>
      {days.map((day,i)=><div className="mds-close__day" key={day.date} data-past={dayIndex>=0&&i<dayIndex?"true":undefined}>
        {day.date===currentDate?<Datum role="current-time" orientation="vertical" position={0} label={currentTime?`Now · ${currentTime}`:"Current period"}/>:null}
        <div className="mds-close__date"><span>{day.label||day.date}</span><span>{tasks.filter(t=>t.date===day.date).length||""}</span></div>
        <div className="mds-close__tasks">{tasks.filter(t=>t.date===day.date).map(t=>{const s=st(t.status);return <button type="button" className="mds-close__task" data-tone={s.tone} data-status={s.key} data-selected={String(t.id)===String(selectedId)?"true":undefined} key={t.id} onClick={()=>onSelect?.(t)}>
          <span className="mds-close__name"><span className="mds-close__mark" aria-hidden="true">{s.glyph}</span>{t.title}</span>
          <span className="mds-close__meta">{t.due||"No due time"} · {t.owner||"Unassigned"}</span>
          <span className="mds-close__meta">{s.label}{t.evidence?` · ${t.evidence}`:""}</span>
          {t.blocker?<span className="mds-close__meta">Blocked: {t.blocker}</span>:null}
          {t.completedAt?<span className="mds-close__meta">Completed {t.completedAt}</span>:null}
        </button>})}</div>
      </div>)}
    </div>:null}

    {view==="dependency"?<div className="mds-close__rows">
      {ordered.map(({t,depth})=><TaskRow key={t.id} t={t} depth={depth} tasks={tasks} selectedId={selectedId} onSelect={onSelect}/>)}
    </div>:null}

    {view==="owner"?<div className="mds-close__rows">
      {owners.map(([owner,list])=><div key={owner}>
        <div className="mds-close__group"><span>{owner}</span><span>{list.filter(t=>st(t.status).key==="complete").length} of {list.length} complete</span></div>
        {list.map(t=><TaskRow key={t.id} t={t} tasks={tasks} selectedId={selectedId} onSelect={onSelect}/>)}
      </div>)}
    </div>:null}

    <div className="mds-close__legend">{Object.entries(STATUS).map(([k,v])=><span key={k}>{v.glyph} {v.label}{counts[k]?` ${counts[k]}`:""}</span>)}</div>
  </section>;
}
