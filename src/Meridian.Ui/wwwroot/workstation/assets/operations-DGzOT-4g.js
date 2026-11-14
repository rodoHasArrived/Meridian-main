import{n as e,t}from"./status-LOtJ7oS3.js";import{m as n,s as r}from"./button-CivEqPI0.js";import{t as i}from"./inject-style-B4OC7SIt.js";import"./index-xx1HWgij.js";var a=n(),o=`
.mds-rpanel{display:grid;gap:8px;min-width:0;border:1px solid var(--severity-info-bd,#D2CDC3);
  border-left:3px solid var(--severity-info-fg,#606870);border-radius:var(--radius-card,2px);
  background:var(--bg-panel,var(--bg-light,var(--ws-surface,#F8F7F3)));padding:12px 14px;}
.mds-rpanel--ready{border-color:var(--severity-ready-bd,rgba(46,118,85,.36));border-left-color:var(--severity-ready-fg,#225840);}
.mds-rpanel--review{border-color:var(--severity-review-bd,rgba(197,99,67,.36));border-left-color:var(--severity-review-fg,#944A32);}
.mds-rpanel--action{border-color:var(--severity-action-bd,rgba(152,103,23,.42));border-left-color:var(--severity-action-fg,#724D11);}
.mds-rpanel--blocked{border-color:var(--severity-blocked-bd,rgba(181,75,66,.40));border-left-color:var(--severity-blocked-fg,#883832);}
.mds-rpanel__head{display:flex;align-items:center;justify-content:space-between;gap:10px;min-width:0;}
.mds-rpanel__score{font-family:var(--font-data,monospace);font-size:11px;font-weight:700;
  color:var(--text-secondary,var(--ws-text-secondary,#535C65));font-variant-numeric:tabular-nums;white-space:nowrap;}
.mds-rpanel__title{margin:0;color:var(--text-primary,var(--ws-text,#20252B));font-size:13px;font-weight:700;line-height:1.25;
  overflow:hidden;text-overflow:ellipsis;}
.mds-rpanel__detail{margin:0;color:var(--text-muted,var(--ws-text-muted,#606870));font-size:12px;line-height:1.45;}
.mds-rpanel__foot{display:flex;flex-wrap:wrap;gap:8px;align-items:center;justify-content:flex-end;
  border-top:1px solid var(--border-color,#D2CDC3);padding-top:10px;margin-top:2px;}
`;function s({state:t=`info`,statusLabel:n,title:s,detail:c,score:l,actions:u,children:d,detailId:f,role:p,ariaLabel:m,ariaDescribedBy:h,className:g}){i(`readiness-panel`,o);let _=e(t),v=h??f;return(0,a.jsxs)(`div`,{className:`mds-rpanel mds-rpanel--${_}${g?` `+g:``}`,role:p,"aria-label":m,"aria-describedby":v,children:[(0,a.jsxs)(`div`,{className:`mds-rpanel__head`,children:[(0,a.jsx)(r,{status:t,label:n}),l!=null&&(0,a.jsx)(`span`,{className:`mds-rpanel__score`,children:l})]}),s&&(0,a.jsx)(`p`,{className:`mds-rpanel__title`,children:s}),c&&(0,a.jsx)(`p`,{id:f,className:`mds-rpanel__detail`,children:c}),d,u&&(0,a.jsx)(`div`,{className:`mds-rpanel__foot`,children:u})]})}var c=`
.mds-gates{display:flex;list-style:none;margin:0;padding:0;gap:0;min-width:0;}
.mds-gate{position:relative;flex:1 1 0;display:flex;flex-direction:column;align-items:center;
  gap:6px;text-align:center;padding:0 4px;min-width:0;}
.mds-gate:not(:first-child)::before{content:"";position:absolute;top:13px;left:calc(-50% + 14px);
  width:calc(100% - 28px);height:2px;background:var(--mds-gate-line,var(--border-color,#D2CDC3));z-index:0;}
.mds-gate__node{position:relative;z-index:1;display:inline-flex;align-items:center;justify-content:center;
  width:28px;height:28px;border-radius:50%;border:1.5px solid var(--severity-info-bd,#D2CDC3);
  background:var(--bg-panel,var(--bg-light,#F8F7F3));color:var(--severity-info-fg,#606870);
  font-family:var(--font-data,monospace);font-size:11px;font-weight:700;}
.mds-gate--ready .mds-gate__node{border-color:var(--severity-ready-fg,#225840);background:var(--severity-ready-bg,rgba(46,118,85,.10));color:var(--severity-ready-fg,#225840);}
.mds-gate--review .mds-gate__node{border-color:var(--severity-review-fg,#944A32);background:var(--severity-review-bg,rgba(197,99,67,.10));color:var(--severity-review-fg,#944A32);}
.mds-gate--action .mds-gate__node{border-color:var(--severity-action-fg,#724D11);background:var(--severity-action-bg,rgba(152,103,23,.11));color:var(--severity-action-fg,#724D11);}
.mds-gate--blocked .mds-gate__node{border-color:var(--severity-blocked-fg,#883832);background:var(--severity-blocked-bg,rgba(181,75,66,.10));color:var(--severity-blocked-fg,#883832);}
.mds-gate__label{font-family:var(--font-body);font-size:11px;font-weight:600;color:var(--text-primary,#20252B);line-height:1.2;}
.mds-gate__status{font-family:var(--font-data,monospace);font-size:9px;font-weight:700;letter-spacing:.04em;
  text-transform:uppercase;color:var(--text-muted,#606870);}
`,l={ready:`✓`,blocked:`!`};function u({gates:n=[],className:r}){return i(`gate-rail`,c),(0,a.jsx)(`ol`,{className:`mds-gates${r?` `+r:``}`,children:n.map((r,i)=>{let o=e(r.status),s={"--mds-gate-line":i>0&&e(n[i-1].status)===`ready`?`var(--severity-ready-fg,#225840)`:`var(--border-color,#D2CDC3)`};return(0,a.jsxs)(`li`,{className:`mds-gate mds-gate--${o}`,style:s,"aria-label":`${r.label}: ${t(r.status)}`,children:[(0,a.jsx)(`span`,{className:`mds-gate__node`,children:l[o]??i+1}),(0,a.jsx)(`span`,{className:`mds-gate__label`,children:r.label}),(0,a.jsx)(`span`,{className:`mds-gate__status`,children:r.statusLabel||t(r.status)})]},r.key||i)})})}var d=`
.mds-trust{display:flex;flex-wrap:wrap;align-items:stretch;gap:8px;min-width:0;}
.mds-trust__item{display:flex;flex-direction:column;gap:2px;min-width:0;
  border:1px solid var(--state-muted-bd,#D2CDC3);border-left-width:3px;border-radius:var(--radius-chip,2px);
  background:var(--state-muted-bg,#EBE8E1);padding:5px 10px;}
.mds-trust__item--ready{border-color:var(--state-healthy-bd,rgba(46,118,85,.32));border-left-color:var(--state-healthy-fg,#225840);background:var(--state-healthy-bg,rgba(46,118,85,.10));}
.mds-trust__item--review{border-color:var(--state-warn-bd,rgba(152,103,23,.38));border-left-color:var(--state-warn-fg,#724D11);background:var(--state-warn-bg,rgba(152,103,23,.11));}
.mds-trust__item--blocked{border-color:var(--state-danger-bd,rgba(181,75,66,.35));border-left-color:var(--state-danger-fg,#883832);background:var(--state-danger-bg,rgba(181,75,66,.10));}
.mds-trust__item--pending{border-color:var(--state-pending-bd,rgba(122,107,82,.34));border-left-color:var(--state-pending-fg,#5C503E);background:var(--state-pending-bg,rgba(122,107,82,.10));}
.mds-trust__label{font-family:var(--font-data,monospace);font-size:9px;font-weight:700;text-transform:uppercase;
  letter-spacing:.05em;color:var(--text-muted,#606870);white-space:nowrap;}
.mds-trust__value{font-family:var(--font-body);font-size:12px;font-weight:600;color:var(--text-primary,#20252B);white-space:nowrap;}
`,f={ready:`ready`,passed:`ready`,healthy:`ready`,live:`ready`,certified:`ready`,approved:`ready`,review:`review`,reviewrequired:`review`,inreview:`review`,warning:`review`,attention:`review`,needsattention:`review`,inprogress:`review`,blocked:`blocked`,critical:`blocked`,failed:`blocked`,degraded:`blocked`,pending:`pending`,submitted:`pending`,queued:`pending`,strategy:`pending`,paper:`pending`};function p(e){return e?f[String(e).toLowerCase().replace(/[^a-z]/g,``)]??`muted`:`muted`}function m({items:e=[],className:t}){return i(`trust-strip`,d),(0,a.jsx)(`div`,{className:`mds-trust${t?` `+t:``}`,children:e.map((e,t)=>(0,a.jsxs)(`div`,{className:`mds-trust__item mds-trust__item--${p(e.state)}`,children:[(0,a.jsx)(`span`,{className:`mds-trust__label`,children:e.label}),(0,a.jsx)(`span`,{className:`mds-trust__value`,children:e.value})]},t))})}export{u as n,s as r,m as t};