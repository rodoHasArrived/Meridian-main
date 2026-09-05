import{o as e}from"./button-CHUXGgnN.js";import{w as t}from"./index-B5BuJeb0.js";import{n,r,t as i}from"./status-ngYnknmL.js";var a=e(),o=`
.mds-sev{display:inline-flex;align-items:center;gap:5px;width:fit-content;max-width:100%;
  min-height:20px;border:1px solid var(--severity-info-bd,#D7DCE2);border-radius:var(--radius-chip,2px);
  background:var(--severity-info-bg,#F5F7FA);color:var(--severity-info-fg,#6E7781);
  font-family:var(--font-data,"Cascadia Mono","JetBrains Mono",monospace);font-size:9px;font-weight:700;line-height:1;
  letter-spacing:.04em;padding:0 7px;text-transform:uppercase;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;}
.mds-sev__dot{height:5px;width:5px;border-radius:50%;background:currentColor;flex:0 0 auto;}
.mds-sev--ready{border-color:var(--severity-ready-bd,rgba(22,136,95,.36));background:var(--severity-ready-bg,rgba(22,136,95,.10));color:var(--severity-ready-fg,#16885F);}
.mds-sev--review{border-color:var(--severity-review-bd,rgba(47,111,143,.36));background:var(--severity-review-bg,rgba(47,111,143,.10));color:var(--severity-review-fg,#2F6F8F);}
.mds-sev--action{border-color:var(--severity-action-bd,rgba(138,82,14,.42));background:var(--severity-action-bg,rgba(138,82,14,.11));color:var(--severity-action-fg,#8A520E);}
.mds-sev--blocked{border-color:var(--severity-blocked-bd,rgba(186,63,85,.40));background:var(--severity-blocked-bg,rgba(186,63,85,.10));color:var(--severity-blocked-fg,#BA3F55);}
.mds-sev--info{border-color:var(--severity-info-bd,#D7DCE2);background:var(--severity-info-bg,#F5F7FA);color:var(--severity-info-fg,#6E7781);}
`;function s({status:e=`info`,label:i,dot:s=!0,className:c,...l}){t(`severity-badge`,o);let u=n(e);return(0,a.jsxs)(`span`,{className:`mds-sev mds-sev--${u}${c?` `+c:``}`,...l,children:[s&&(0,a.jsx)(`span`,{className:`mds-sev__dot`,"aria-hidden":`true`}),i??r[u]]})}var c=`
.mds-rpanel{display:grid;gap:8px;min-width:0;border:1px solid var(--severity-info-bd,#D7DCE2);
  border-left:3px solid var(--severity-info-fg,#6E7781);border-radius:var(--radius-card,2px);
  background:var(--bg-panel,var(--bg-light,var(--ws-surface,#fff)));padding:12px 14px;}
.mds-rpanel--ready{border-color:var(--severity-ready-bd,rgba(22,136,95,.36));border-left-color:var(--severity-ready-fg,#16885F);}
.mds-rpanel--review{border-color:var(--severity-review-bd,rgba(47,111,143,.36));border-left-color:var(--severity-review-fg,#2F6F8F);}
.mds-rpanel--action{border-color:var(--severity-action-bd,rgba(138,82,14,.42));border-left-color:var(--severity-action-fg,#8A520E);}
.mds-rpanel--blocked{border-color:var(--severity-blocked-bd,rgba(186,63,85,.40));border-left-color:var(--severity-blocked-fg,#BA3F55);}
.mds-rpanel__head{display:flex;align-items:center;justify-content:space-between;gap:10px;min-width:0;}
.mds-rpanel__score{font-family:var(--font-data,monospace);font-size:11px;font-weight:700;
  color:var(--text-secondary,var(--ws-text-secondary,#4D5967));font-variant-numeric:tabular-nums;white-space:nowrap;}
.mds-rpanel__title{margin:0;color:var(--text-primary,var(--ws-text,#22272E));font-size:13px;font-weight:700;line-height:1.25;
  overflow:hidden;text-overflow:ellipsis;}
.mds-rpanel__detail{margin:0;color:var(--text-muted,var(--ws-text-muted,#59636F));font-size:12px;line-height:1.45;}
.mds-rpanel__foot{display:flex;flex-wrap:wrap;gap:8px;align-items:center;justify-content:flex-end;
  border-top:1px solid var(--border,#D7DCE2);padding-top:10px;margin-top:2px;}
`;function l({state:e=`info`,statusLabel:r,title:i,detail:o,score:l,actions:u,children:d,detailId:f,role:p,ariaLabel:m,ariaDescribedBy:h,className:g}){t(`readiness-panel`,c);let _=n(e),v=h??f;return(0,a.jsxs)(`div`,{className:`mds-rpanel mds-rpanel--${_}${g?` `+g:``}`,role:p,"aria-label":m,"aria-describedby":v,children:[(0,a.jsxs)(`div`,{className:`mds-rpanel__head`,children:[(0,a.jsx)(s,{status:e,label:r}),l!=null&&(0,a.jsx)(`span`,{className:`mds-rpanel__score`,children:l})]}),i&&(0,a.jsx)(`p`,{className:`mds-rpanel__title`,children:i}),o&&(0,a.jsx)(`p`,{id:f,className:`mds-rpanel__detail`,children:o}),d,u&&(0,a.jsx)(`div`,{className:`mds-rpanel__foot`,children:u})]})}var u=`
.mds-gates{display:flex;list-style:none;margin:0;padding:0;gap:0;min-width:0;}
.mds-gate{position:relative;flex:1 1 0;display:flex;flex-direction:column;align-items:center;
  gap:6px;text-align:center;padding:0 4px;min-width:0;}
.mds-gate:not(:first-child)::before{content:"";position:absolute;top:13px;left:calc(-50% + 14px);
  width:calc(100% - 28px);height:2px;background:var(--mds-gate-line,var(--border,#D7DCE2));z-index:0;}
.mds-gate__node{position:relative;z-index:1;display:inline-flex;align-items:center;justify-content:center;
  width:28px;height:28px;border-radius:50%;border:1.5px solid var(--severity-info-bd,#D7DCE2);
  background:var(--bg-panel,var(--bg-light,#fff));color:var(--severity-info-fg,#6E7781);
  font-family:var(--font-data,monospace);font-size:11px;font-weight:700;}
.mds-gate--ready .mds-gate__node{border-color:var(--severity-ready-fg,#16885F);background:var(--severity-ready-bg,rgba(22,136,95,.10));color:var(--severity-ready-fg,#16885F);}
.mds-gate--review .mds-gate__node{border-color:var(--severity-review-fg,#2F6F8F);background:var(--severity-review-bg,rgba(47,111,143,.10));color:var(--severity-review-fg,#2F6F8F);}
.mds-gate--action .mds-gate__node{border-color:var(--severity-action-fg,#8A520E);background:var(--severity-action-bg,rgba(138,82,14,.11));color:var(--severity-action-fg,#8A520E);}
.mds-gate--blocked .mds-gate__node{border-color:var(--severity-blocked-fg,#BA3F55);background:var(--severity-blocked-bg,rgba(186,63,85,.10));color:var(--severity-blocked-fg,#BA3F55);}
.mds-gate__label{font-family:var(--font-body);font-size:11px;font-weight:600;color:var(--text-primary,#22272E);line-height:1.2;}
.mds-gate__status{font-family:var(--font-data,monospace);font-size:9px;font-weight:700;letter-spacing:.04em;
  text-transform:uppercase;color:var(--text-muted,#59636F);}
`,d={ready:`✓`,blocked:`!`};function f({gates:e=[],className:r}){return t(`gate-rail`,u),(0,a.jsx)(`ol`,{className:`mds-gates${r?` `+r:``}`,children:e.map((t,r)=>{let o=n(t.status),s={"--mds-gate-line":r>0&&n(e[r-1].status)===`ready`?`var(--severity-ready-fg,#16885F)`:`var(--border,#D7DCE2)`};return(0,a.jsxs)(`li`,{className:`mds-gate mds-gate--${o}`,style:s,"aria-label":`${t.label}: ${i(t.status)}`,children:[(0,a.jsx)(`span`,{className:`mds-gate__node`,children:d[o]??r+1}),(0,a.jsx)(`span`,{className:`mds-gate__label`,children:t.label}),(0,a.jsx)(`span`,{className:`mds-gate__status`,children:t.statusLabel||i(t.status)})]},t.key||r)})})}var p=`
.mds-trust{display:flex;flex-wrap:wrap;align-items:stretch;gap:8px;min-width:0;}
.mds-trust__item{display:flex;flex-direction:column;gap:2px;min-width:0;
  border:1px solid var(--state-muted-bd,#D7DCE2);border-left-width:3px;border-radius:var(--radius-chip,2px);
  background:var(--state-muted-bg,#F5F7FA);padding:5px 10px;}
.mds-trust__item--ready{border-color:var(--state-healthy-bd,rgba(22,136,95,.32));border-left-color:var(--state-healthy-fg,#16885F);background:var(--state-healthy-bg,rgba(22,136,95,.10));}
.mds-trust__item--review{border-color:var(--state-warn-bd,rgba(138,82,14,.38));border-left-color:var(--state-warn-fg,#8A520E);background:var(--state-warn-bg,rgba(138,82,14,.11));}
.mds-trust__item--blocked{border-color:var(--state-danger-bd,rgba(186,63,85,.35));border-left-color:var(--state-danger-fg,#BA3F55);background:var(--state-danger-bg,rgba(186,63,85,.10));}
.mds-trust__item--pending{border-color:var(--state-pending-bd,rgba(111,91,167,.34));border-left-color:var(--state-pending-fg,#6F5BA7);background:var(--state-pending-bg,rgba(111,91,167,.10));}
.mds-trust__label{font-family:var(--font-data,monospace);font-size:9px;font-weight:700;text-transform:uppercase;
  letter-spacing:.05em;color:var(--text-muted,#59636F);white-space:nowrap;}
.mds-trust__value{font-family:var(--font-body);font-size:12px;font-weight:600;color:var(--text-primary,#22272E);white-space:nowrap;}
`,m={ready:`ready`,passed:`ready`,healthy:`ready`,live:`ready`,certified:`ready`,approved:`ready`,review:`review`,reviewrequired:`review`,inreview:`review`,warning:`review`,attention:`review`,needsattention:`review`,inprogress:`review`,blocked:`blocked`,critical:`blocked`,failed:`blocked`,degraded:`blocked`,pending:`pending`,submitted:`pending`,queued:`pending`,strategy:`pending`,paper:`pending`};function h(e){return e?m[String(e).toLowerCase().replace(/[^a-z]/g,``)]??`muted`:`muted`}function g({items:e=[],className:n}){return t(`trust-strip`,p),(0,a.jsx)(`div`,{className:`mds-trust${n?` `+n:``}`,children:e.map((e,t)=>(0,a.jsxs)(`div`,{className:`mds-trust__item mds-trust__item--${h(e.state)}`,children:[(0,a.jsx)(`span`,{className:`mds-trust__label`,children:e.label}),(0,a.jsx)(`span`,{className:`mds-trust__value`,children:e.value})]},t))})}var _=`
.mds-wsection{display:grid;gap:12px;scroll-margin-top:12px;min-width:0;}
.mds-wsection__head{position:sticky;top:0;z-index:15;display:flex;flex-wrap:wrap;align-items:center;
  justify-content:space-between;gap:10px;min-width:0;
  border:1px solid var(--border,#D7DCE2);border-radius:var(--radius-card,2px);background:var(--bg-medium,#F5F7FA);
  padding:9px 12px;}
.mds-wsection__heading{min-width:0;}
.mds-wsection__title{margin:0;color:var(--text-primary,#22272E);font-size:13px;font-weight:700;line-height:1.2;}
.mds-wsection__summary{margin:1px 0 0;color:var(--text-muted,#59636F);font-size:12px;line-height:1.35;}
.mds-wsection__jump{display:inline-flex;align-items:center;gap:6px;min-height:30px;
  border:1px solid var(--accent,#2F6F8F);border-radius:var(--radius-chip,2px);
  background:var(--blue-a10,rgba(47,111,143,.10));color:var(--accent,#2F6F8F);
  padding:4px 10px;font-family:var(--font-body);font-size:11px;font-weight:700;cursor:pointer;text-decoration:none;
  white-space:nowrap;}
.mds-wsection__jump:hover{background:var(--bg-active,#E6EEF5);}
.mds-wsection__jump:focus-visible{outline:var(--focus-ring,2px solid #2F6F8F);outline-offset:2px;}
.mds-wsection__body{display:grid;gap:12px;min-width:0;}
`;function v({id:e,title:n,summary:r,jump:i,onJump:o,jumpHref:s,children:c,className:l}){return t(`workspace-section`,_),(0,a.jsxs)(`section`,{id:e,className:`mds-wsection${l?` `+l:``}`,children:[(0,a.jsxs)(`div`,{className:`mds-wsection__head`,children:[(0,a.jsxs)(`div`,{className:`mds-wsection__heading`,children:[n&&(0,a.jsx)(`h3`,{className:`mds-wsection__title`,children:n}),r&&(0,a.jsx)(`p`,{className:`mds-wsection__summary`,children:r})]}),i&&(s?(0,a.jsx)(`a`,{className:`mds-wsection__jump`,href:s,children:i}):(0,a.jsx)(`button`,{type:`button`,className:`mds-wsection__jump`,onClick:o,children:i}))]}),(0,a.jsx)(`div`,{className:`mds-wsection__body`,children:c})]})}export{s as a,l as i,g as n,f as r,v as t};