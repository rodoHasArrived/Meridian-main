import{r as e}from"./rolldown-runtime-XKyaHtl9.js";import{Ks as t,m as n}from"./button-CT5J9mL7.js";import{S as r}from"./format-BR49U7Fw.js";import{g as i,n as a}from"./metric-trust-BjI2AKNX.js";import{t as o}from"./inject-style-B4OC7SIt.js";var s=e(t(),1),c=n(),l=`
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
.mds-metric__label{font-family:var(--font-body);font-size:12px;font-weight:600;
  font-variant:all-small-caps;letter-spacing:.03em;color:var(--text-muted,#606870);margin:0;}
.mds-metric__value{margin:8px 0 0;font-family:var(--font-data,monospace);font-size:24px;font-weight:600;
  line-height:1;font-variant-numeric:tabular-nums;color:var(--text-primary,#20252B);}
.mds-metric__delta{font-family:var(--font-data,monospace);font-size:12px;margin-top:6px;
  font-variant-numeric:tabular-nums;}
.mds-delta--up{color:var(--severity-ready-fg,#225840);}
.mds-delta--down{color:var(--severity-blocked-fg,#883832);}
.mds-delta--flat{color:var(--text-muted,#606870);}
.mds-metric--degraded .mds-metric__value{color:var(--text-muted,#606870);}
.mds-metric__trust{font-family:var(--font-body);font-size:12px;line-height:1.35;margin-top:6px;
  color:var(--text-muted,#606870);}
`,u={neutral:``,info:``,success:`positive`,warning:`attention`,danger:`critical`};function d({label:e,value:t,delta:n,tone:r=`neutral`,trend:i,ariaLabel:a,deltaAriaLabel:s,freshnessLabel:d,provenance:f,basis:p}){o(`metric`,l);let m=i??(n?.trim().startsWith(`-`)?`down`:n?.trim().startsWith(`+`)?`up`:`flat`),h=a??[typeof e==`string`?e:void 0,typeof t==`string`?t:void 0,n?`change ${n}`:void 0,u[r]?`status ${u[r]}`:void 0,d].filter(Boolean).join(`, `),g=[d,f].filter(Boolean).join(` · `);return(0,c.jsxs)(`div`,{className:`mds-metric mds-metric--${r}${d?` mds-metric--degraded`:``}`,role:`group`,"aria-label":h,children:[(0,c.jsx)(`p`,{className:`mds-metric__label`,children:e}),(0,c.jsx)(`p`,{className:`mds-metric__value`,children:t}),p?(0,c.jsx)(`p`,{className:`mt-2 text-xs text-muted-foreground`,children:p}):null,n&&(0,c.jsx)(`div`,{className:`mds-metric__delta mds-delta--${m}`,"aria-label":s,children:n}),g&&(0,c.jsx)(`p`,{className:`mds-metric__trust`,children:g})]})}var f=(0,s.memo)(d),p={default:`text-foreground`,success:`text-success`,warning:`text-warning`,danger:`text-danger`},m={default:`neutral`,success:`positive`,warning:`attention`,danger:`critical`};function h(e){let t=g(e.id||e.label),n=a(e),o=r(e.label.trim()),s=e.value.trim(),c=e.comparisonPeriod?i(e):n.delta?.text??null,l=c?`Change ${_(c)}`:null,u=[`${o} metric`,s,l,`Status ${m[e.tone]}`,n.freshnessDetail?.replace(/\.$/,``)??null,n.provenanceText].filter(Boolean).join(`. `);return{id:t,label:o,value:s,delta:c,toneClass:p[e.tone],ariaLabel:u,deltaAriaLabel:l,labelId:`${t}-label`,valueId:`${t}-value`,deltaId:c?`${t}-delta`:null,freshnessLabel:n.freshnessLabel,provenanceText:n.provenanceText}}function g(e){let t=e.trim().toLowerCase().replace(/[^a-z0-9]+/g,`-`).replace(/^-+|-+$/g,``);return t?`metric-${t}`:`metric-card`}function _(e){return e.replace(/[▲△]/g,`up`).replace(/[▼▽]/g,`down`).replace(/−/g,`-`).replace(/\s+/g,` `).trim()}export{f as n,h as t};