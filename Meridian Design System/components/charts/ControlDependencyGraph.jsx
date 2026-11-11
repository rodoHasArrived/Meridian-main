// Meridian ControlDependencyGraph — how close tasks, validations, reconciliations,
// approvals, and attestations depend on one another. Layered and deterministic: layers come
// from longest-path depth, order within a layer from input order, so a node never moves
// because a status changed. Connectors are orthogonal, edges are explicitly typed, blocking
// propagates downstream visibly, and the numbered relationship outline is a first-class
// output — it is what export, screen readers, and a degraded session read from.
import React from "react";

const STATUS={
  "not-started":{label:"Not started",tone:"idle",glyph:"○"},
  ready:{label:"Ready",tone:"idle",glyph:"◇"},
  "in-progress":{label:"In progress",tone:"active",glyph:"◐"},
  waiting:{label:"Waiting",tone:"idle",glyph:"◇"},
  blocked:{label:"Blocked",tone:"blocked",glyph:"▲"},
  failed:{label:"Failed",tone:"blocked",glyph:"▲"},
  complete:{label:"Complete",tone:"done",glyph:"✓"},
  reopened:{label:"Reopened",tone:"review",glyph:"↺"},
  superseded:{label:"Superseded",tone:"idle",glyph:"—"},
};
const S_ALIAS={pending:"not-started",todo:"not-started",started:"in-progress",running:"in-progress",done:"complete",signed:"complete",review:"reopened",error:"failed"};
const st=v=>{const k=String(v||"not-started").trim().toLowerCase().replace(/[_\s]+/g,"-");const key=STATUS[k]?k:S_ALIAS[k]||"not-started";return{key,...STATUS[key]}};
const TYPES={input:"Data input",validation:"Validation",reconciliation:"Reconciliation",calculation:"Calculation",journal:"Journal",review:"Review",approval:"Approval",attestation:"Attestation",report:"Report",external:"External dependency"};
const EDGE={requires:"Requires",produces:"Produces",validates:"Validates",blocks:"Blocks",invalidates:"Invalidates",reopens:"Reopens",approves:"Approves"};
const BLOCKING=new Set(["blocked","failed"]);

let injected=false;function inject(){if(injected||typeof document==="undefined")return;injected=true;const el=document.createElement("style");el.setAttribute("data-mds","control-dependency-graph");el.textContent=`
.mds-cdg{font-family:var(--font-body);color:var(--text-secondary,#535C65)}
.mds-cdg__head{display:flex;gap:12px;align-items:baseline;flex-wrap:wrap;margin-bottom:6px;font:9px var(--font-data,monospace);color:var(--text-muted,#606870)}
.mds-cdg__head b{font-weight:600;color:var(--text-primary,#20252B);font-size:11px;font-family:var(--font-body)}
.mds-cdg__toggle{margin-left:auto;display:flex;border:1px solid var(--border,#D2CDC3);border-radius:2px;overflow:hidden}
.mds-cdg__toggle button{appearance:none;border:0;border-left:1px solid var(--border,#D2CDC3);background:var(--surface-plate,#F8F7F3);font:9px var(--font-data,monospace);text-transform:uppercase;letter-spacing:.04em;color:var(--text-muted,#606870);padding:3px 8px;cursor:pointer}
.mds-cdg__toggle button:first-child{border-left:0}
.mds-cdg__toggle button[aria-pressed="true"]{background:var(--selected-surface,var(--bg-active,#E7EDF2));color:var(--text-primary,#20252B);font-weight:600}
.mds-cdg__toggle button:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:-2px}
.mds-cdg svg{width:100%;height:auto;display:block;background:var(--surface-well,var(--card-surface-raised,#FCFBF8));border:1px solid var(--border,#D2CDC3)}
.mds-cdg__edge{fill:none;stroke:var(--border-strong,#AAA49A);stroke-width:1.25}
.mds-cdg__edge--critical{stroke:var(--brand-axis,#29323A);stroke-width:2}
.mds-cdg__edge--blocked{stroke:var(--red,#B54B42);stroke-dasharray:4 3}
.mds-cdg__edge--invalidates,.mds-cdg__edge--reopens{stroke:var(--orange,#986717);stroke-dasharray:2 3}
.mds-cdg__edgelabel{font:8px var(--font-data,monospace);fill:var(--text-muted,#606870)}
.mds-cdg__node rect{fill:var(--surface-plate,var(--bg-light,#F8F7F3));stroke:var(--border,#D2CDC3)}
.mds-cdg__node .label{font:600 10px var(--font-body);fill:var(--text-primary,#20252B)}
.mds-cdg__node .meta{font:8px var(--font-data,monospace);fill:var(--text-muted,#606870)}
.mds-cdg__node--done rect{stroke:var(--green,#2E7655)}
.mds-cdg__node--blocked rect{stroke:var(--red,#B54B42);fill:var(--red-surface,rgba(181,75,66,.08))}
.mds-cdg__node--review rect{stroke:var(--orange,#986717)}
.mds-cdg__node--active rect{stroke:var(--info,#2F648A)}
.mds-cdg__node--downstream rect{stroke-dasharray:3 2}
.mds-cdg__node--critical rect{stroke-width:1.75}
.mds-cdg__datum{fill:none;stroke:var(--selected,#496C8F);stroke-width:2}
.mds-cdg__hit{cursor:pointer}
.mds-cdg__hit:focus-visible{outline:none}
.mds-cdg__hit:focus-visible rect{stroke:var(--focus,#2D6FB3);stroke-width:2}
.mds-cdg__legend{display:flex;gap:12px;flex-wrap:wrap;margin-top:6px;font:9px var(--font-data,monospace);color:var(--text-muted,#606870)}
.mds-cdg__outline{margin-top:10px;border-top:1px solid var(--border-divider,#E2DED5);padding-top:8px}
.mds-cdg__outline summary{font:600 11px var(--font-body);cursor:pointer;color:var(--text-primary,#20252B)}
.mds-cdg__outline ol{margin:8px 0 0;padding-left:20px;font-size:11px;line-height:17px}
.mds-cdg__outline li{margin-bottom:4px}
.mds-cdg__outline small{display:block;font:9px/14px var(--font-data,monospace);color:var(--text-muted,#606870)}
.mds-cdg--outline-only svg,.mds-cdg--outline-only .mds-cdg__legend{display:none}
.mds-cdg--outline-only .mds-cdg__outline{border-top:0;padding-top:0}
@media (max-width:640px){.mds-cdg svg{display:none}.mds-cdg__outline{border-top:0}}
`;document.head.appendChild(el)}

// Deterministic layering: depth = longest requires-path from any root; ties keep input order.
function layered(nodes,edges){
  const byId=new Map(nodes.map(n=>[String(n.id),n]));
  const parents=new Map(nodes.map(n=>[String(n.id),[]]));
  edges.forEach(e=>{if(byId.has(String(e.to))&&byId.has(String(e.from)))parents.get(String(e.to)).push(String(e.from))});
  const depth=new Map();
  const walk=(id,seen)=>{
    if(depth.has(id))return depth.get(id);
    if(seen.has(id))return 0;
    seen.add(id);
    const d=parents.get(id).reduce((m,p)=>Math.max(m,walk(p,seen)+1),0);
    depth.set(id,d);return d;
  };
  nodes.forEach(n=>{const id=String(n.id);walk(id,new Set());if(n.layer!=null)depth.set(id,Number(n.layer))});
  return{depth,parents,byId};
}

export function ControlDependencyGraph({
  nodes=[],edges=[],width=760,nodeWidth=168,nodeHeight=48,layerGap=104,
  selectedId,onSelect,showEdgeLabels=true,criticalPath=true,
  view:viewProp,defaultView="graph",onViewChange,
  description="Control dependency graph",title,asOf,className="",...rest
}){
  inject();
  const[own,setOwn]=React.useState(defaultView);
  const view=viewProp||own;
  const setView=v=>{if(viewProp===undefined)setOwn(v);if(onViewChange)onViewChange(v)};

  const{depth,parents,byId}=React.useMemo(()=>layered(nodes,edges),[nodes,edges]);

  // Downstream blocking: anything reachable from a blocked or failed node is itself impeded.
  const impeded=React.useMemo(()=>{
    const out=new Map(nodes.map(n=>[String(n.id),[]]));
    edges.forEach(e=>{if(out.has(String(e.from)))out.get(String(e.from)).push(String(e.to))});
    const hit=new Set();
    const seed=nodes.filter(n=>BLOCKING.has(st(n.status).key)).map(n=>String(n.id));
    const stack=[...seed];
    while(stack.length){const id=stack.pop();(out.get(id)||[]).forEach(t=>{if(!hit.has(t)){hit.add(t);stack.push(t)}})}
    seed.forEach(id=>hit.delete(id));
    return hit;
  },[nodes,edges]);

  // Critical path: longest requires-chain ending at the deepest incomplete node.
  const critical=React.useMemo(()=>{
    if(!criticalPath||!nodes.length)return new Set();
    const open=nodes.filter(n=>st(n.status).key!=="complete");
    if(!open.length)return new Set();
    const target=open.reduce((a,b)=>(depth.get(String(b.id))??0)>(depth.get(String(a.id))??0)?b:a);
    const chain=new Set();
    const walk=id=>{
      if(chain.has(id))return;chain.add(id);
      const ps=parents.get(id)||[];
      if(!ps.length)return;
      walk(ps.reduce((a,b)=>(depth.get(b)??0)>(depth.get(a)??0)?b:a));
    };
    walk(String(target.id));
    return chain;
  },[nodes,parents,depth,criticalPath]);

  const layers=[...new Set(nodes.map(n=>depth.get(String(n.id))??0))].sort((a,b)=>a-b);
  const pos={};
  layers.forEach(l=>{
    const arr=nodes.filter(n=>(depth.get(String(n.id))??0)===l);
    arr.forEach((n,i)=>{
      const gap=width/(arr.length+1);
      pos[String(n.id)]={x:Math.round(gap*(i+1)-nodeWidth/2),y:20+layers.indexOf(l)*layerGap,w:nodeWidth,h:nodeHeight};
    });
  });
  const height=Math.max(160,layers.length*layerGap+30);

  // Orthogonal: down out of the source, across the channel between layers, down into the target.
  const path=(a,b)=>{
    const s=pos[String(a)],t=pos[String(b)];if(!s||!t)return"";
    const x1=s.x+s.w/2,y1=s.y+s.h,x2=t.x+t.w/2,y2=t.y,m=y1+(y2-y1)/2;
    return x1===x2?`M${x1} ${y1}V${y2}`:`M${x1} ${y1}V${m}H${x2}V${y2}`;
  };
  const mid=(a,b)=>{const s=pos[String(a)],t=pos[String(b)];if(!s||!t)return null;
    return{x:(t.x+t.w/2+s.x+s.w/2)/2,y:s.y+s.h+(t.y-s.y-s.h)/2-3}};

  const ordered=[...nodes].sort((a,b)=>(depth.get(String(a.id))??0)-(depth.get(String(b.id))??0));
  const blockedCount=nodes.filter(n=>BLOCKING.has(st(n.status).key)).length;

  return <div className={`mds-cdg${view==="outline"?" mds-cdg--outline-only":""}${className?" "+className:""}`} {...rest}>
    <div className="mds-cdg__head">
      <b>{title||description}</b>
      <span>{nodes.length} nodes · {edges.length} relationships{blockedCount?` · ${blockedCount} blocked`:""}{impeded.size?` · ${impeded.size} downstream impeded`:""}{asOf?` · as of ${asOf}`:""}</span>
      <div className="mds-cdg__toggle" role="group" aria-label="View">
        <button type="button" aria-pressed={view==="graph"} onClick={()=>setView("graph")}>Graph</button>
        <button type="button" aria-pressed={view==="outline"} onClick={()=>setView("outline")}>Outline</button>
      </div>
    </div>

    <svg viewBox={`0 0 ${width} ${height}`} role={onSelect ? "group" : "img"} aria-label={`${description}. ${nodes.length} nodes, ${edges.length} relationships. A numbered relationship outline follows.`}>
      {edges.map((e,i)=>{
        const type=String(e.type||"requires").toLowerCase();
        const blocked=e.blocked||BLOCKING.has(st(byId.get(String(e.from))?.status).key);
        const onPath=critical.has(String(e.from))&&critical.has(String(e.to));
        const p=mid(e.from,e.to);
        return <g key={e.id||i}>
          <path d={path(e.from,e.to)} className={`mds-cdg__edge${onPath?" mds-cdg__edge--critical":""}${blocked?" mds-cdg__edge--blocked":""} mds-cdg__edge--${type}`}/>
          {showEdgeLabels&&p?<text className="mds-cdg__edgelabel" x={p.x+4} y={p.y}>{EDGE[type]||type}</text>:null}
        </g>;
      })}
      {nodes.map(n=>{
        const p=pos[String(n.id)];if(!p)return null;
        const s=st(n.status);
        const sel=String(selectedId)===String(n.id);
        const down=impeded.has(String(n.id));
        const cls=`mds-cdg__node mds-cdg__node--${s.tone}${down?" mds-cdg__node--downstream":""}${critical.has(String(n.id))?" mds-cdg__node--critical":""}`;
        return <g key={n.id} transform={`translate(${p.x} ${p.y})`} className={cls}>
          <g className="mds-cdg__hit" tabIndex={onSelect?0:undefined} role={onSelect?"button":undefined}
            aria-label={`${s.glyph} ${String(n.label).slice(0,24)} ${TYPES[String(n.type||"").toLowerCase()]||n.type||"Task"} · ${s.label} ${[n.owner,n.due].filter(Boolean).join(" · ")||(down?"Impeded upstream":"—")}. ${n.label}${down?", impeded by an upstream block":""}`}
            onClick={()=>onSelect?.(n)}
            onKeyDown={e=>{if((e.key==="Enter"||e.key===" ")&&onSelect){e.preventDefault();onSelect(n)}}}>
            <rect width={p.w} height={p.h}/>
            {sel?<rect className="mds-cdg__datum" x="-3" y="-3" width={p.w+6} height={p.h+6}/>:null}
            <text className="label" x="9" y="18">{s.glyph} {String(n.label).slice(0,24)}</text>
            <text className="meta" x="9" y="31">{TYPES[String(n.type||"").toLowerCase()]||n.type||"Task"} · {s.label}</text>
            <text className="meta" x="9" y="42">{[n.owner,n.due].filter(Boolean).join(" · ")||(down?"Impeded upstream":"—")}</text>
          </g>
        </g>;
      })}
    </svg>

    <div className="mds-cdg__legend">
      {[...new Set(nodes.map(n=>st(n.status).key))].map(k=><span key={k}>{STATUS[k].glyph} {STATUS[k].label}</span>)}
      {critical.size?<span>Heavier stroke · critical path</span>:null}
      {impeded.size?<span>Dashed border · impeded by upstream block</span>:null}
    </div>

    <details className="mds-cdg__outline" open={view==="outline"}>
      <summary>Relationship outline</summary>
      <ol>{ordered.map(n=>{
        const s=st(n.status);
        const reqs=edges.filter(e=>String(e.to)===String(n.id));
        const down=impeded.has(String(n.id));
        return <li key={n.id}>
          <strong>{n.label}</strong> — {s.label}
          {n.owner?` · ${n.owner}`:""}{n.due?` · due ${n.due}`:""}
          {reqs.length
            ? <small>{reqs.map(e=>`${EDGE[String(e.type||"requires").toLowerCase()]||e.type}: ${byId.get(String(e.from))?.label||e.from}`).join(" · ")}</small>
            : <small>No upstream dependency</small>}
          {n.blocker?<small>Blocked: {n.blocker}</small>:down?<small>Impeded by an upstream block</small>:null}
          {critical.has(String(n.id))?<small>On the critical path to completion</small>:null}
        </li>;
      })}</ol>
    </details>
  </div>;
}
