import{d as e,o as t}from"./button-BbEzzFtS.js";import{t as n}from"./inject-style-B4OC7SIt.js";import{n as r,t as i}from"./status-Dd02PRCS.js";var a=e(),o=`
.mds-rpanel{display:grid;gap:8px;min-width:0;border:1px solid var(--severity-info-bd,#E4E3DE);
  border-left:3px solid var(--severity-info-fg,#5E666F);border-radius:var(--radius-card,2px);
  background:var(--bg-panel,var(--bg-light,var(--ws-surface,#FBFAF8)));padding:12px 14px;}
.mds-rpanel--ready{border-color:var(--severity-ready-bd,rgba(58,122,86,.36));border-left-color:var(--severity-ready-fg,#2C5C40);}
.mds-rpanel--review{border-color:var(--severity-review-bd,rgba(168,84,54,.36));border-left-color:var(--severity-review-fg,#8C4429);}
.mds-rpanel--action{border-color:var(--severity-action-bd,rgba(138,92,18,.42));border-left-color:var(--severity-action-fg,#68450E);}
.mds-rpanel--blocked{border-color:var(--severity-blocked-bd,rgba(168,68,60,.40));border-left-color:var(--severity-blocked-fg,#7E332D);}
.mds-rpanel__head{display:flex;align-items:center;justify-content:space-between;gap:10px;min-width:0;}
.mds-rpanel__score{font-family:var(--font-data,monospace);font-size:11px;font-weight:700;
  color:var(--text-secondary,var(--ws-text-secondary,#4E5258));font-variant-numeric:tabular-nums;white-space:nowrap;}
.mds-rpanel__title{margin:0;color:var(--text-primary,var(--ws-text,#22252A));font-size:13px;font-weight:700;line-height:1.25;
  overflow:hidden;text-overflow:ellipsis;}
.mds-rpanel__detail{margin:0;color:var(--text-muted,var(--ws-text-muted,#5E666F));font-size:12px;line-height:1.45;}
.mds-rpanel__foot{display:flex;flex-wrap:wrap;gap:8px;align-items:center;justify-content:flex-end;
  border-top:1px solid var(--border,#E4E3DE);padding-top:10px;margin-top:2px;}
`;function s({state:e=`info`,statusLabel:i,title:s,detail:c,score:l,actions:u,children:d,detailId:f,role:p,ariaLabel:m,ariaDescribedBy:h,className:g}){n(`readiness-panel`,o);let _=r(e),v=h??f;return(0,a.jsxs)(`div`,{className:`mds-rpanel mds-rpanel--${_}${g?` `+g:``}`,role:p,"aria-label":m,"aria-describedby":v,children:[(0,a.jsxs)(`div`,{className:`mds-rpanel__head`,children:[(0,a.jsx)(t,{status:e,label:i}),l!=null&&(0,a.jsx)(`span`,{className:`mds-rpanel__score`,children:l})]}),s&&(0,a.jsx)(`p`,{className:`mds-rpanel__title`,children:s}),c&&(0,a.jsx)(`p`,{id:f,className:`mds-rpanel__detail`,children:c}),d,u&&(0,a.jsx)(`div`,{className:`mds-rpanel__foot`,children:u})]})}var c=`
.mds-gates{display:flex;list-style:none;margin:0;padding:0;gap:0;min-width:0;}
.mds-gate{position:relative;flex:1 1 0;display:flex;flex-direction:column;align-items:center;
  gap:6px;text-align:center;padding:0 4px;min-width:0;}
.mds-gate:not(:first-child)::before{content:"";position:absolute;top:13px;left:calc(-50% + 14px);
  width:calc(100% - 28px);height:2px;background:var(--mds-gate-line,var(--border,#E4E3DE));z-index:0;}
.mds-gate__node{position:relative;z-index:1;display:inline-flex;align-items:center;justify-content:center;
  width:28px;height:28px;border-radius:50%;border:1.5px solid var(--severity-info-bd,#E4E3DE);
  background:var(--bg-panel,var(--bg-light,#FBFAF8));color:var(--severity-info-fg,#5E666F);
  font-family:var(--font-data,monospace);font-size:11px;font-weight:700;}
.mds-gate--ready .mds-gate__node{border-color:var(--severity-ready-fg,#2C5C40);background:var(--severity-ready-bg,rgba(58,122,86,.10));color:var(--severity-ready-fg,#2C5C40);}
.mds-gate--review .mds-gate__node{border-color:var(--severity-review-fg,#8C4429);background:var(--severity-review-bg,rgba(168,84,54,.10));color:var(--severity-review-fg,#8C4429);}
.mds-gate--action .mds-gate__node{border-color:var(--severity-action-fg,#68450E);background:var(--severity-action-bg,rgba(138,92,18,.11));color:var(--severity-action-fg,#68450E);}
.mds-gate--blocked .mds-gate__node{border-color:var(--severity-blocked-fg,#7E332D);background:var(--severity-blocked-bg,rgba(168,68,60,.10));color:var(--severity-blocked-fg,#7E332D);}
.mds-gate__label{font-family:var(--font-body);font-size:11px;font-weight:600;color:var(--text-primary,#22252A);line-height:1.2;}
.mds-gate__status{font-family:var(--font-data,monospace);font-size:9px;font-weight:700;letter-spacing:.04em;
  text-transform:uppercase;color:var(--text-muted,#5E666F);}
`,l={ready:`✓`,blocked:`!`};function u({gates:e=[],className:t}){return n(`gate-rail`,c),(0,a.jsx)(`ol`,{className:`mds-gates${t?` `+t:``}`,children:e.map((t,n)=>{let o=r(t.status),s={"--mds-gate-line":n>0&&r(e[n-1].status)===`ready`?`var(--severity-ready-fg,#2C5C40)`:`var(--border,#E4E3DE)`};return(0,a.jsxs)(`li`,{className:`mds-gate mds-gate--${o}`,style:s,"aria-label":`${t.label}: ${i(t.status)}`,children:[(0,a.jsx)(`span`,{className:`mds-gate__node`,children:l[o]??n+1}),(0,a.jsx)(`span`,{className:`mds-gate__label`,children:t.label}),(0,a.jsx)(`span`,{className:`mds-gate__status`,children:t.statusLabel||i(t.status)})]},t.key||n)})})}var d=`
.mds-trust{display:flex;flex-wrap:wrap;align-items:stretch;gap:8px;min-width:0;}
.mds-trust__item{display:flex;flex-direction:column;gap:2px;min-width:0;
  border:1px solid var(--state-muted-bd,#E4E3DE);border-left-width:3px;border-radius:var(--radius-chip,2px);
  background:var(--state-muted-bg,#EDEAE4);padding:5px 10px;}
.mds-trust__item--ready{border-color:var(--state-healthy-bd,rgba(58,122,86,.32));border-left-color:var(--state-healthy-fg,#2C5C40);background:var(--state-healthy-bg,rgba(58,122,86,.10));}
.mds-trust__item--review{border-color:var(--state-warn-bd,rgba(138,92,18,.38));border-left-color:var(--state-warn-fg,#68450E);background:var(--state-warn-bg,rgba(138,92,18,.11));}
.mds-trust__item--blocked{border-color:var(--state-danger-bd,rgba(168,68,60,.35));border-left-color:var(--state-danger-fg,#7E332D);background:var(--state-danger-bg,rgba(168,68,60,.10));}
.mds-trust__item--pending{border-color:var(--state-pending-bd,rgba(93,84,134,.34));border-left-color:var(--state-pending-fg,#463F64);background:var(--state-pending-bg,rgba(93,84,134,.10));}
.mds-trust__label{font-family:var(--font-data,monospace);font-size:9px;font-weight:700;text-transform:uppercase;
  letter-spacing:.05em;color:var(--text-muted,#5E666F);white-space:nowrap;}
.mds-trust__value{font-family:var(--font-body);font-size:12px;font-weight:600;color:var(--text-primary,#22252A);white-space:nowrap;}
`,f={ready:`ready`,passed:`ready`,healthy:`ready`,live:`ready`,certified:`ready`,approved:`ready`,review:`review`,reviewrequired:`review`,inreview:`review`,warning:`review`,attention:`review`,needsattention:`review`,inprogress:`review`,blocked:`blocked`,critical:`blocked`,failed:`blocked`,degraded:`blocked`,pending:`pending`,submitted:`pending`,queued:`pending`,strategy:`pending`,paper:`pending`};function p(e){return e?f[String(e).toLowerCase().replace(/[^a-z]/g,``)]??`muted`:`muted`}function m({items:e=[],className:t}){return n(`trust-strip`,d),(0,a.jsx)(`div`,{className:`mds-trust${t?` `+t:``}`,children:e.map((e,t)=>(0,a.jsxs)(`div`,{className:`mds-trust__item mds-trust__item--${p(e.state)}`,children:[(0,a.jsx)(`span`,{className:`mds-trust__label`,children:e.label}),(0,a.jsx)(`span`,{className:`mds-trust__value`,children:e.value})]},t))})}var h=`
.mds-wsection{display:grid;gap:12px;scroll-margin-top:12px;min-width:0;}
.mds-wsection__head{position:sticky;top:0;z-index:15;display:flex;flex-wrap:wrap;align-items:center;
  justify-content:space-between;gap:10px;min-width:0;
  border:1px solid var(--border,#E4E3DE);border-radius:var(--radius-card,2px);background:var(--bg-medium,#EDEAE4);
  padding:9px 12px;}
.mds-wsection__heading{min-width:0;}
.mds-wsection__title{margin:0;color:var(--text-primary,#22252A);font-size:13px;font-weight:700;line-height:1.2;}
.mds-wsection__summary{margin:1px 0 0;color:var(--text-muted,#5E666F);font-size:12px;line-height:1.35;}
.mds-wsection__jump{display:inline-flex;align-items:center;gap:6px;min-height:30px;
  border:1px solid var(--accent,#A85436);border-radius:var(--radius-chip,2px);
  background:var(--blue-a10,rgba(168,84,54,.10));color:var(--accent-dim,#8C4429);
  padding:4px 10px;font-family:var(--font-body);font-size:11px;font-weight:700;cursor:pointer;text-decoration:none;
  white-space:nowrap;}
.mds-wsection__jump:hover{background:var(--bg-active,#F2E3DB);}
.mds-wsection__jump:focus-visible{outline:var(--focus-ring,2px solid var(--ws-accent,#A85436));outline-offset:2px;}
.mds-wsection__body{display:grid;gap:12px;min-width:0;}
`;function g({id:e,title:t,summary:r,jump:i,onJump:o,jumpHref:s,children:c,className:l}){return n(`workspace-section`,h),(0,a.jsxs)(`section`,{id:e,className:`mds-wsection${l?` `+l:``}`,children:[(0,a.jsxs)(`div`,{className:`mds-wsection__head`,children:[(0,a.jsxs)(`div`,{className:`mds-wsection__heading`,children:[t&&(0,a.jsx)(`h3`,{className:`mds-wsection__title`,children:t}),r&&(0,a.jsx)(`p`,{className:`mds-wsection__summary`,children:r})]}),i&&(s?(0,a.jsx)(`a`,{className:`mds-wsection__jump`,href:s,children:i}):(0,a.jsx)(`button`,{type:`button`,className:`mds-wsection__jump`,onClick:o,children:i}))]}),(0,a.jsx)(`div`,{className:`mds-wsection__body`,children:c})]})}export{s as i,m as n,u as r,g as t};