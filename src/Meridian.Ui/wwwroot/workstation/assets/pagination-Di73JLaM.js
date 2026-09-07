import{C as e,T as t,o as n}from"./button-CHUXGgnN.js";import{w as r}from"./index-799NA-Or.js";var i=t(e(),1),a=n(),o=`
.pgn-wrap{display:flex;align-items:center;justify-content:space-between;gap:12px;flex-wrap:wrap;
  padding:12px;background:var(--bg-medium,#F3F6F9);border-radius:var(--radius-chip,2px);
  border:1px solid var(--border,#CBD3DC);}
.pgn-info{font-family:var(--font-data,monospace);font-size:12px;color:var(--text-muted,#59636F);
  font-variant-numeric:tabular-nums;}
.pgn-controls{display:flex;align-items:center;gap:4px;}
.pgn-btn{appearance:none;border:1px solid var(--border,#CBD3DC);background:var(--bg-panel,var(--bg-light,#fff));
  color:var(--text-primary,#22272E);min-width:28px;height:28px;padding:0 6px;border-radius:var(--radius-button,2px);
  cursor:pointer;font-family:var(--font-data,monospace);font-size:12px;font-weight:500;
  display:flex;align-items:center;justify-content:center;
  transition:background .1s ease,border-color .1s ease;}
.pgn-btn:hover:not(:disabled){background:var(--bg-active,#E6EEF5);border-color:var(--accent,#2F6F8F);}
.pgn-btn:focus-visible{outline:var(--focus-ring,2px solid #2F6F8F);outline-offset:1px;}
.pgn-btn:disabled{opacity:.4;cursor:not-allowed;}
.pgn-btn--active{background:var(--accent,#2F6F8F);color:#fff;border-color:var(--accent,#2F6F8F);}
.pgn-ellipsis{padding:0 4px;color:var(--text-muted,#59636F);}
.pgn-jump{height:28px;padding:4px 8px;width:52px;text-align:center;
  border:1px solid var(--border,#CBD3DC);border-radius:var(--radius-button,2px);
  background:var(--bg-panel,var(--bg-light,#fff));color:var(--text-primary,#22272E);
  font-family:var(--font-data,monospace);font-size:12px;transition:border-color .1s ease;}
.pgn-jump:focus{outline:none;border-color:var(--accent,#2F6F8F);}
`;function s({currentPage:e=1,totalPages:t=1,onPageChange:n=()=>{},totalItems:s=0,itemsPerPage:c=50,siblingCount:l=1}){r(`pagination`,o);let[u,d]=(0,i.useState)(String(e));(0,i.useEffect)(()=>{d(String(e))},[e]);let f=e=>{n(Math.max(1,Math.min(t,e)))},p=()=>{let e=Math.max(1,Math.min(t,parseInt(u,10)||1));d(String(e)),n(e)},m=(()=>{let n=[],r=Math.max(2,e-l),i=Math.min(t-1,e+l);n.push(1),r>2&&n.push(`...`);for(let e=r;e<=i;e++)n.push(e);return i<t-1&&n.push(`...`),t>1&&n.push(t),n})(),h=(e-1)*c+1,g=Math.min(e*c,s||0);return(0,a.jsxs)(`div`,{className:`pgn-wrap`,children:[(0,a.jsx)(`div`,{className:`pgn-info`,children:s>0?`${h}–${g} of ${s}`:`Page ${e} of ${t}`}),(0,a.jsxs)(`div`,{className:`pgn-controls`,children:[(0,a.jsx)(`button`,{type:`button`,className:`pgn-btn`,onClick:()=>f(e-1),disabled:e===1,"aria-label":`Previous page`,children:`‹`}),m.map((t,n)=>t===`...`?(0,a.jsx)(`span`,{className:`pgn-ellipsis`,children:`…`},`e${n}`):(0,a.jsx)(`button`,{type:`button`,className:`pgn-btn${t===e?` pgn-btn--active`:``}`,onClick:()=>f(t),"aria-label":`Page ${t}`,"aria-current":t===e?`page`:void 0,children:t},t)),(0,a.jsx)(`button`,{type:`button`,className:`pgn-btn`,onClick:()=>f(e+1),disabled:e===t,"aria-label":`Next page`,children:`›`}),(0,a.jsx)(`input`,{type:`number`,className:`pgn-jump`,value:u,onChange:e=>d(e.target.value),onKeyDown:e=>e.key===`Enter`&&p(),onBlur:p,min:1,max:t,"aria-label":`Jump to page`})]})]})}export{s as t};