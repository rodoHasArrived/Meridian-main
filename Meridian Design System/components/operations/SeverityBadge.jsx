// Meridian severity badge — mirrors `.severity-badge` in the app stylesheet.
// The operator status chip: mono, 9px, uppercase, alpha-fill + solid semantic
// border. Accepts the app's readiness/severity strings (Ready, ReviewRequired,
// Blocked, Critical, Stale, …) and collapses them onto 5 canonical severities.
import React from "react";
import { normalizeSeverity, severityLabels } from "./status.js";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-sev{display:inline-flex;align-items:center;gap:5px;width:fit-content;max-width:100%;
  min-height:20px;border:1px solid var(--severity-info-bd,#D2CDC3);border-radius:var(--radius-chip,2px);
  background:var(--severity-info-bg,#EBE8E1);color:var(--severity-info-fg,#606870);
  font-family:var(--font-data,"IBM Plex Mono",monospace);font-size:9px;font-weight:700;line-height:1;
  letter-spacing:.04em;padding:0 7px;text-transform:uppercase;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;}
.mds-sev.mds-sev{color:var(--text-primary,#20252B);}
.mds-sev--ready .mds-sev__dot{background:var(--severity-ready-fg,#225840);}
.mds-sev--review .mds-sev__dot{background:var(--severity-review-fg,#944A32);}
.mds-sev--action .mds-sev__dot{background:var(--severity-action-fg,#724D11);}
.mds-sev--blocked .mds-sev__dot{background:var(--severity-blocked-fg,#883832);}
.mds-sev__dot{height:5px;width:5px;border-radius:50%;background:currentColor;flex:0 0 auto;}
.mds-sev--ready{border-color:var(--severity-ready-bd,rgba(46,118,85,.36));background:var(--severity-ready-bg,rgba(46,118,85,.10));color:var(--severity-ready-fg,#225840);}
.mds-sev--review{border-color:var(--severity-review-bd,rgba(197,99,67,.36));background:var(--severity-review-bg,rgba(197,99,67,.10));color:var(--severity-review-fg,#944A32);}
.mds-sev--action{border-color:var(--severity-action-bd,rgba(152,103,23,.42));background:var(--severity-action-bg,rgba(152,103,23,.11));color:var(--severity-action-fg,#724D11);}
.mds-sev--blocked{border-color:var(--severity-blocked-bd,rgba(181,75,66,.40));background:var(--severity-blocked-bg,rgba(181,75,66,.10));color:var(--severity-blocked-fg,#883832);}
.mds-sev--info{border-color:var(--severity-info-bd,#D2CDC3);background:var(--severity-info-bg,#EBE8E1);color:var(--severity-info-fg,#606870);}
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
