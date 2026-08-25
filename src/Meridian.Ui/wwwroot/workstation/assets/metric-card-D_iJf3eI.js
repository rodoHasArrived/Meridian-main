import{C as e,T as t,o as n}from"./button-CHUXGgnN.js";import{S as r}from"./index-DEEdahaO.js";var i=t(e(),1),a=n(),o=`
.mds-metric{background:var(--card-surface-raised,#F3F6F9);
  border:1px solid var(--border,#CBD3DC);border-left-width:3px;
  border-radius:var(--radius-card,2px);padding:18px;}
.mds-metric--neutral{border-left-color:var(--border-strong,#99A5B2);}
.mds-metric--info{border-left-color:var(--accent,#2F6F8F);}
.mds-metric--success{border-left-color:var(--severity-ready-fg,#16885F);}
.mds-metric--warning{border-left-color:var(--severity-action-fg,#8A520E);}
.mds-metric--danger{border-left-color:var(--severity-blocked-fg,#BA3F55);}
.mds-metric__label{font-family:var(--font-body);font-size:10px;font-weight:600;
  font-variant:all-small-caps;letter-spacing:.03em;color:var(--text-muted,#59636F);margin:0;}
.mds-metric__value{margin:8px 0 0;font-family:var(--font-data,monospace);font-size:24px;font-weight:600;
  line-height:1;font-variant-numeric:tabular-nums;color:var(--text-primary,#22272E);}
.mds-metric__delta{font-family:var(--font-data,monospace);font-size:11px;margin-top:6px;
  font-variant-numeric:tabular-nums;}
.mds-delta--up{color:var(--severity-ready-fg,#16885F);}
.mds-delta--down{color:var(--severity-blocked-fg,#BA3F55);}
.mds-delta--flat{color:var(--text-muted,#59636F);}
`,s={neutral:``,info:``,success:`positive`,warning:`attention`,danger:`critical`};function c({label:e,value:t,delta:n,tone:i=`neutral`,trend:c,ariaLabel:l,deltaAriaLabel:u}){r(`metric`,o);let d=c??(n?.trim().startsWith(`-`)?`down`:n?.trim().startsWith(`+`)?`up`:`flat`),f=l??[typeof e==`string`?e:void 0,typeof t==`string`?t:void 0,n?`change ${n}`:void 0,s[i]?`status ${s[i]}`:void 0].filter(Boolean).join(`, `);return(0,a.jsxs)(`div`,{className:`mds-metric mds-metric--${i}`,role:`group`,"aria-label":f,children:[(0,a.jsx)(`p`,{className:`mds-metric__label`,children:e}),(0,a.jsx)(`p`,{className:`mds-metric__value`,children:t}),n&&(0,a.jsx)(`div`,{className:`mds-metric__delta mds-delta--${d}`,"aria-label":u,children:n})]})}var l=(0,i.memo)(c);export{l as t};