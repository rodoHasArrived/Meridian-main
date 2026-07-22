// Meridian masthead — near-black brand bar (#171A1F) capping the light workstation.
// Brand mark + module breadcrumb · command search · UTC clock · environment mode badge.
import React from "react";
import { Badge } from "../core/Badge";

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
.ws-brand__sep{color:var(--topbar-sep,#3A4048);}
.ws-brand__mod{font-size:13px;color:var(--topbar-text-muted,#AEB7C0);}
.ws-search{display:flex;align-items:center;gap:.5rem;width:100%;max-width:560px;height:30px;
  padding:0 .625rem;border:1px solid var(--topbar-field-border,#2C323A);border-radius:var(--radius-button,2px);
  background:var(--topbar-field-bg,#0F1216);color:var(--topbar-text-faint,#8A929B);text-align:left;cursor:text;
  font-family:var(--font-body);font-size:12px;transition:border-color .12s ease;}
.ws-search:hover{border-color:var(--topbar-field-border-hover,#3A424B);}
.ws-search__txt{flex:1;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;}
.ws-kbd{font-family:var(--font-data);font-size:10px;border:1px solid var(--topbar-field-border,#2C323A);
  border-radius:var(--radius-chip,2px);padding:1px 5px;color:var(--topbar-text-faint,#8A929B);}
.ws-status{display:inline-flex;align-items:center;gap:.5rem;font-family:var(--font-data);
  font-size:11px;color:var(--topbar-text-muted,#AEB7C0);font-variant-numeric:tabular-nums;}
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
    // Live mode paints a hard 2px red inset hairline across the masthead — operators feel "real money".
    <header className="ws-masthead" style={env === "live" ? { boxShadow: "inset 0 2px 0 0 var(--mode-live)" } : undefined}>
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
        <span style={{ width: 6, height: 6, borderRadius: 999, background: "var(--chrome-ok, #2FA377)", display: "inline-block" }} />
        <span>{clock}</span>
      </div>
      {/* Environment chip — reuse Badge's solid live/paper/fixture variants instead of a hand-rolled twin */}
      <Badge variant={["live", "paper", "fixture"].includes(env) ? env : "neutral"} dot>
        {String(environment)}
      </Badge>
    </header>
  );
}
