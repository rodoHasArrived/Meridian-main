import{r as e}from"./rolldown-runtime-XKyaHtl9.js";import{Ks as t,m as n}from"./button-BRyY4jSw.js";import{t as r}from"./inject-style-B4OC7SIt.js";var i=e(t(),1),a=n(),o=`
/* Resolve the workstation's active scope before the standalone package roles.
   The dashboard's --border is an HSL channel list, not a CSS color. */
.mds-metric{background:var(--ws-surface-raised,var(--card-surface-raised,#FCFBF8));
  border:1px solid var(--ws-border,var(--border-color,#D2CDC3));border-left-width:3px;
  border-radius:var(--radius-card,2px);padding:18px;}
.mds-metric--neutral{border-left-color:var(--border-strong,#AAA49A);}
.mds-metric--info{border-left-color:var(--semantic-info,#2F648A);}
.mds-metric--success{border-left-color:var(--severity-ready-fg,#225840);}
.mds-metric--warning{border-left-color:var(--severity-action-fg,#724D11);}
.mds-metric--danger{border-left-color:var(--severity-blocked-fg,#883832);}
.mds-metric__label{font-family:var(--font-body);font-size:10px;font-weight:600;
  font-variant:all-small-caps;letter-spacing:.03em;color:var(--text-muted,#606870);margin:0;}
.mds-metric__value{margin:8px 0 0;font-family:var(--font-data,monospace);font-size:24px;font-weight:600;
  line-height:1;font-variant-numeric:tabular-nums;color:var(--text-primary,#20252B);}
.mds-metric__delta{font-family:var(--font-data,monospace);font-size:11px;margin-top:6px;
  font-variant-numeric:tabular-nums;}
.mds-delta--up{color:var(--severity-ready-fg,#225840);}
.mds-delta--down{color:var(--severity-blocked-fg,#883832);}
.mds-delta--flat{color:var(--text-muted,#606870);}
.mds-metric--degraded .mds-metric__value{color:var(--text-muted,#606870);}
.mds-metric__trust{font-family:var(--font-body);font-size:12px;line-height:1.35;margin-top:6px;
  color:var(--text-muted,#606870);}
`,s={neutral:``,info:``,success:`positive`,warning:`attention`,danger:`critical`};function c({label:e,value:t,delta:n,tone:i=`neutral`,trend:c,ariaLabel:l,deltaAriaLabel:u,freshnessLabel:d,provenance:f,basis:p}){r(`metric`,o);let m=c??(n?.trim().startsWith(`-`)?`down`:n?.trim().startsWith(`+`)?`up`:`flat`),h=l??[typeof e==`string`?e:void 0,typeof t==`string`?t:void 0,n?`change ${n}`:void 0,s[i]?`status ${s[i]}`:void 0,d].filter(Boolean).join(`, `),g=[d,f].filter(Boolean).join(` · `);return(0,a.jsxs)(`div`,{className:`mds-metric mds-metric--${i}${d?` mds-metric--degraded`:``}`,role:`group`,"aria-label":h,children:[(0,a.jsx)(`p`,{className:`mds-metric__label`,children:e}),(0,a.jsx)(`p`,{className:`mds-metric__value`,children:t}),p?(0,a.jsx)(`p`,{className:`mt-2 text-xs text-muted-foreground`,children:p}):null,n&&(0,a.jsx)(`div`,{className:`mds-metric__delta mds-delta--${m}`,"aria-label":u,children:n}),g&&(0,a.jsx)(`p`,{className:`mds-metric__trust`,children:g})]})}var l=(0,i.memo)(c);export{l as t};