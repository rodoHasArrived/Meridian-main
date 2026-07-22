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
  min-height:20px;border:1px solid var(--severity-info-bd,#D7DCE2);border-radius:var(--radius-chip,2px);
  background:var(--severity-info-bg,#F5F7FA);color:var(--severity-info-fg,#6E7781);
  font-family:var(--font-data,"Cascadia Mono",monospace);font-size:9px;font-weight:700;line-height:1;
  letter-spacing:.04em;padding:0 7px;text-transform:uppercase;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;}
.mds-sev__dot{height:5px;width:5px;border-radius:50%;background:currentColor;flex:0 0 auto;}
.mds-sev--ready{border-color:var(--severity-ready-bd,rgba(22,136,95,.36));background:var(--severity-ready-bg,rgba(22,136,95,.10));color:var(--severity-ready-fg,#16885F);}
.mds-sev--review{border-color:var(--severity-review-bd,rgba(47,111,143,.36));background:var(--severity-review-bg,rgba(47,111,143,.10));color:var(--severity-review-fg,#2F6F8F);}
.mds-sev--action{border-color:var(--severity-action-bd,rgba(138,82,14,.42));background:var(--severity-action-bg,rgba(138,82,14,.11));color:var(--severity-action-fg,#8A520E);}
.mds-sev--blocked{border-color:var(--severity-blocked-bd,rgba(186,63,85,.40));background:var(--severity-blocked-bg,rgba(186,63,85,.10));color:var(--severity-blocked-fg,#BA3F55);}
.mds-sev--info{border-color:var(--severity-info-bd,#D7DCE2);background:var(--severity-info-bg,#F5F7FA);color:var(--severity-info-fg,#6E7781);}
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
