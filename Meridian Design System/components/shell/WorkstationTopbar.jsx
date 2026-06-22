// Meridian masthead — near-black brand bar (#171A1F) capping the light workstation.
// Brand mark + module breadcrumb · command search · UTC clock · environment mode badge.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.ws-masthead{display:grid;min-height:48px;
  grid-template-columns:max-content minmax(180px,1fr) max-content max-content;
  gap:.75rem;align-items:center;padding:0 1rem;
  background:var(--topbar-bg,#171A1F);border-bottom:1px solid var(--topbar-border,#262B31);
  font-family:var(--font-body);}
.ws-brand{display:inline-flex;align-items:center;gap:.5rem;min-width:0;}
.ws-brand img{width:22px;height:22px;flex:0 0 auto;}
.ws-brand__name{font-family:var(--font-display);font-size:14px;font-weight:600;color:var(--topbar-text,#F4F6F8);}
.ws-brand__sep{color:#3A4048;}
.ws-brand__mod{font-size:13px;color:#AEB7C0;}
.ws-search{display:flex;align-items:center;gap:.5rem;width:100%;max-width:560px;height:30px;
  padding:0 .625rem;border:1px solid #2C323A;border-radius:var(--radius-button,6px);
  background:#0F1216;color:#8A929B;text-align:left;cursor:text;
  font-family:var(--font-body);font-size:12px;transition:border-color .12s ease;}
.ws-search:hover{border-color:#3A424B;}
.ws-search__txt{flex:1;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;}
.ws-kbd{font-family:var(--font-data);font-size:10px;border:1px solid #2C323A;
  border-radius:3px;padding:1px 5px;color:#8A929B;}
.ws-status{display:inline-flex;align-items:center;gap:.5rem;font-family:var(--font-data);
  font-size:11px;color:#AEB7C0;font-variant-numeric:tabular-nums;}
.ws-env{display:inline-flex;align-items:center;gap:6px;padding:3px 9px;border:1px solid;
  border-radius:var(--radius-chip,4px);font-family:var(--font-body);font-size:11px;font-weight:600;
  font-variant:all-small-caps;letter-spacing:.03em;}
.ws-env__dot{width:6px;height:6px;border-radius:50%;background:currentColor;}
.ws-env--live{color:#E27087;border-color:rgba(186,63,85,.55);background:rgba(186,63,85,.18);}
.ws-env--paper{color:#7FB2CC;border-color:rgba(47,111,143,.55);background:rgba(47,111,143,.20);}
.ws-env--fixture{color:#D6A84A;border-color:rgba(183,121,31,.55);background:rgba(183,121,31,.20);}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "masthead");
  el.textContent = css;
  document.head.appendChild(el);
}

export function WorkstationTopbar({
  moduleLabel = "Workstation", environment = "PAPER", clock = "14:32:08 UTC",
  brandSrc = "assets/brand/meridian-mark-light.svg", onSearch
}) {
  inject();
  const env = String(environment).toLowerCase();
  return (
    <header className="ws-masthead">
      <div className="ws-brand">
        <img src={brandSrc} alt="Meridian" />
        <span className="ws-brand__name">Meridian</span>
        <span className="ws-brand__sep">/</span>
        <span className="ws-brand__mod">{moduleLabel}</span>
      </div>
      <button className="ws-search" onClick={onSearch} type="button">
        <span aria-hidden="true">⌕</span>
        <span className="ws-search__txt">Search symbols, runs, commands…</span>
        <span className="ws-kbd">Ctrl K</span>
      </button>
      <div className="ws-status">
        <span style={{ width: 6, height: 6, borderRadius: 999, background: "#2FA377", display: "inline-block" }} />
        <span>{clock}</span>
      </div>
      <span className={`ws-env ws-env--${env}`}>
        <span className="ws-env__dot" aria-hidden="true" />
        {String(environment)}
      </span>
    </header>
  );
}
