// Meridian severity badge — mirrors `.severity-badge` in the app stylesheet.
// The operator status chip: mono, 9px, uppercase, alpha-fill + solid semantic
// border. Accepts the app's readiness/severity strings (Ready, ReviewRequired,
// Blocked, Critical, Stale, …) and collapses them onto 5 canonical severities.
import React from "react";
import { normalizeSeverity, severityLabels } from "./status";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-sev{display:inline-flex;align-items:center;gap:5px;width:fit-content;max-width:100%;
  min-height:20px;border:1px solid var(--severity-info-bd,#E4E3DE);border-radius:var(--radius-chip,2px);
  background:var(--severity-info-bg,#EDEAE4);color:var(--severity-info-fg,#5E666F);
  font-family:var(--font-data,"Cascadia Mono",monospace);font-size:9px;font-weight:700;line-height:1;
  letter-spacing:.04em;padding:0 7px;text-transform:uppercase;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;}
.mds-sev__dot{height:5px;width:5px;border-radius:50%;background:currentColor;flex:0 0 auto;}
.mds-sev--ready{border-color:var(--severity-ready-bd,rgba(58,122,86,.36));background:var(--severity-ready-bg,rgba(58,122,86,.10));color:var(--severity-ready-fg,#2C5C40);}
.mds-sev--review{border-color:var(--severity-review-bd,rgba(168,84,54,.36));background:var(--severity-review-bg,rgba(168,84,54,.10));color:var(--severity-review-fg,#8C4429);}
.mds-sev--action{border-color:var(--severity-action-bd,rgba(138,92,18,.42));background:var(--severity-action-bg,rgba(138,92,18,.11));color:var(--severity-action-fg,#68450E);}
.mds-sev--blocked{border-color:var(--severity-blocked-bd,rgba(168,68,60,.40));background:var(--severity-blocked-bg,rgba(168,68,60,.10));color:var(--severity-blocked-fg,#7E332D);}
.mds-sev--info{border-color:var(--severity-info-bd,#E4E3DE);background:var(--severity-info-bg,#EDEAE4);color:var(--severity-info-fg,#5E666F);}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "severity-badge");
  el.textContent = css;
  document.head.appendChild(el);
}

export function SeverityBadge({ status = "info", label, dot = true, className = "", ...rest }) {
  inject();
  const sev = normalizeSeverity(status);
  return (
    <span className={`mds-sev mds-sev--${sev}${className ? " " + className : ""}`} {...rest}>
      {dot && <span className="mds-sev__dot" aria-hidden="true" />}
      {label != null ? label : severityLabels[sev]}
    </span>
  );
}
