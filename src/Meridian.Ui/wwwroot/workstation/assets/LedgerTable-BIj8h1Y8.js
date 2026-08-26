import{C as e,T as t,o as n}from"./button-CHUXGgnN.js";import{S as r,b as i}from"./accounting-screen.basis-bridge.view-model-BWznLolM.js";var a=t(e(),1),o=n(),s=!1;function c(){if(s||typeof document>`u`)return;s=!0;let e=document.createElement(`style`);e.setAttribute(`data-mds`,`ledger`),e.textContent=`
.ldg-wrap{overflow-x:auto;border:1px solid var(--border,#CBD3DC);
  border-radius:var(--radius-chip,2px);background:var(--bg-light,#FFFFFF);}
.ldg{width:100%;min-width:100%;border-collapse:separate;border-spacing:0;
  font-family:var(--font-data,monospace);font-size:12px;}
.ldg thead th{padding:9px 12px;text-align:left;white-space:nowrap;position:sticky;top:0;
  background:var(--bg-medium,#EBEFF4);z-index:1;
  font-family:var(--font-body,inherit);font-size:10px;font-weight:600;font-variant:all-small-caps;
  letter-spacing:.03em;color:var(--text-muted,#59636F);
  border-bottom:1px solid var(--border-strong,#99A5B2);border-right:1px solid var(--border-divider,#CBD3DC);}
.ldg thead th:last-child{border-right:none;}
.ldg th.ldg--r{text-align:right;}
.ldg td{padding:11px 12px;white-space:nowrap;color:var(--text-primary,#22272E);
  border-top:1px solid var(--border,#CBD3DC);border-right:1px solid var(--border-divider,#CBD3DC);
  vertical-align:baseline;height:40px;}
.ldg td:last-child{border-right:none;}
.ldg tbody tr:first-child td{border-top:none;}
.ldg td.ldg--r{text-align:right;}
.ldg tbody tr:hover{background:var(--bg-hover,#F3F6F9);}
.ldg__date{color:var(--text-secondary,#4D5967);}
.ldg__ref{color:var(--accent,#2F6F8F);}
.ldg__memo{font-family:var(--font-body,inherit);color:var(--text-secondary,#4D5967);
  white-space:normal;min-width:160px;}
.ldg__acct{color:var(--text-primary,#22272E);}
.ldg__open td{background:var(--bg-medium,#EBEFF4);color:var(--text-muted,#59636F);}
.ldg__open .ldg__memo{font-style:italic;}
.ldg tfoot td{padding:9px 12px;background:var(--bg-medium,#EBEFF4);font-weight:600;
  border-top:2px solid var(--border-strong,#99A5B2);color:var(--text-primary,#22272E);}
.ldg tfoot td.ldg--r{text-align:right;}
.ldg__foot-label{font-family:var(--font-body,inherit);font-variant:all-small-caps;letter-spacing:.03em;
  font-size:11px;color:var(--text-secondary,#4D5967);}
.ldg__sort{cursor:pointer;user-select:none;}
.ldg__sort:focus-visible{outline:2px solid var(--accent,#2F6F8F);outline-offset:-2px;}
`,document.head.appendChild(e)}function l({rows:e,currency:t=`USD`,opening:n,showAccount:s=!1,normalSide:l=`debit`,caption:u,onSort:d}){c();let[f,p]=(0,a.useState)(null),[m,h]=(0,a.useState)(1),g=e=>{if(!d)return;let t=f===e&&m===1?-1:1;p(e),h(t),d(e,t)},_=e=>t=>{(t.key===`Enter`||t.key===` `)&&(t.preventDefault(),g(e))},v=r(n),y=Number.isFinite(v),b=y?v:0,x=y,S=e.map(e=>{let t=r(e.debit)||0,n=r(e.credit)||0,i=l===`credit`?n-t:t-n,a=r(e.balance);return e.balance!=null&&Number.isFinite(a)?(b=a,x=!0):x&&(b+=i),{...e,_bal:b,_hasBal:x}}),C=e.reduce((e,t)=>e+(r(t.debit)||0),0),w=e.reduce((e,t)=>e+(r(t.credit)||0),0),T=C-w,E=e=>d&&f===e?m===1?` ↑`:` ↓`:``,D=e=>d?{className:`ldg__sort`,onClick:()=>g(e),onKeyDown:_(e),tabIndex:0,"aria-sort":f===e?m===1?`ascending`:`descending`:`none`,scope:`col`}:{scope:`col`};return(0,o.jsx)(`div`,{className:`ldg-wrap`,role:`region`,"aria-label":u||`General ledger`,children:(0,o.jsxs)(`table`,{className:`ldg`,children:[(0,o.jsx)(`thead`,{children:(0,o.jsxs)(`tr`,{children:[(0,o.jsxs)(`th`,{...D(`date`),children:[`Date`,E(`date`)]}),(0,o.jsxs)(`th`,{...D(`ref`),children:[`Ref`,E(`ref`)]}),s&&(0,o.jsxs)(`th`,{...D(`account`),children:[`Account`,E(`account`)]}),(0,o.jsxs)(`th`,{...D(`memo`),children:[`Description`,E(`memo`)]}),(0,o.jsxs)(`th`,{...D(`debit`),className:`${d?`ldg__sort `:``}ldg--r`.trim(),children:[`Debit`,E(`debit`)]}),(0,o.jsxs)(`th`,{...D(`credit`),className:`${d?`ldg__sort `:``}ldg--r`.trim(),children:[`Credit`,E(`credit`)]}),(0,o.jsxs)(`th`,{...D(`balance`),className:`${d?`ldg__sort `:``}ldg--r`.trim(),children:[`Balance`,E(`balance`)]})]})}),(0,o.jsxs)(`tbody`,{children:[y&&(0,o.jsxs)(`tr`,{className:`ldg__open`,children:[(0,o.jsx)(`td`,{className:`ldg__date`}),(0,o.jsx)(`td`,{}),s&&(0,o.jsx)(`td`,{}),(0,o.jsx)(`td`,{className:`ldg__memo`,children:`Opening balance`}),(0,o.jsx)(`td`,{className:`ldg--r`}),(0,o.jsx)(`td`,{className:`ldg--r`}),(0,o.jsx)(`td`,{className:`ldg--r`,children:(0,o.jsx)(i,{value:v,currency:t,mode:`muted`})})]}),S.map((e,n)=>(0,o.jsxs)(`tr`,{children:[(0,o.jsx)(`td`,{className:`ldg__date`,children:e.date}),(0,o.jsx)(`td`,{className:`ldg__ref`,children:e.ref}),s&&(0,o.jsx)(`td`,{className:`ldg__acct`,children:e.account}),(0,o.jsx)(`td`,{className:`ldg__memo`,children:e.memo}),(0,o.jsx)(`td`,{className:`ldg--r`,children:(0,o.jsx)(i,{value:e.debit??``,currency:t,zeroDash:!0})}),(0,o.jsx)(`td`,{className:`ldg--r`,children:(0,o.jsx)(i,{value:e.credit??``,currency:t,zeroDash:!0})}),(0,o.jsx)(`td`,{className:`ldg--r`,children:e._hasBal?(0,o.jsx)(i,{value:e._bal,currency:t,parens:!0}):(0,o.jsx)(`span`,{style:{color:`var(--text-disabled, #889099)`},children:`—`})})]},n))]}),(0,o.jsx)(`tfoot`,{children:(0,o.jsxs)(`tr`,{children:[(0,o.jsx)(`td`,{className:`ldg__foot-label`,colSpan:s?4:3,children:`Totals`}),(0,o.jsx)(`td`,{className:`ldg--r`,children:(0,o.jsx)(i,{value:C,currency:t,strong:!0})}),(0,o.jsx)(`td`,{className:`ldg--r`,children:(0,o.jsx)(i,{value:w,currency:t,strong:!0})}),(0,o.jsx)(`td`,{className:`ldg--r`,children:(0,o.jsx)(i,{value:T,currency:t,mode:Math.abs(T)<.005?`muted`:`pnl`,parens:!0,strong:!0,"aria-label":Math.abs(T)<.005?`Ledger balanced`:`Ledger imbalance`})})]})})]})})}l.displayName=`LedgerTable`;export{l as t};