// Meridian trust strip — mirrors `.workstation-trust-strip`. The masthead readiness
// band: a row of label/value items tinted by state (ready · review · blocked ·
// pending), surfacing the operator's evidence posture at a glance. Note: per the app,
// "review" reads amber here (attention) rather than the blue used by SeverityBadge.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-trust{display:flex;flex-wrap:wrap;align-items:stretch;gap:8px;min-width:0;}
.mds-trust__item{display:flex;flex-direction:column;gap:2px;min-width:0;
  border:1px solid var(--state-muted-bd,#D7DCE2);border-left-width:3px;border-radius:var(--radius-chip,2px);
  background:var(--state-muted-bg,#F5F7FA);padding:5px 10px;}
.mds-trust__item--ready{border-color:var(--state-healthy-bd,rgba(22,136,95,.32));border-left-color:var(--state-healthy-fg,#16885F);background:var(--state-healthy-bg,rgba(22,136,95,.10));}
.mds-trust__item--review{border-color:var(--state-warn-bd,rgba(138,82,14,.38));border-left-color:var(--state-warn-fg,#8A520E);background:var(--state-warn-bg,rgba(138,82,14,.11));}
.mds-trust__item--blocked{border-color:var(--state-danger-bd,rgba(186,63,85,.35));border-left-color:var(--state-danger-fg,#BA3F55);background:var(--state-danger-bg,rgba(186,63,85,.10));}
.mds-trust__item--pending{border-color:var(--state-pending-bd,rgba(111,91,167,.34));border-left-color:var(--state-pending-fg,#6F5BA7);background:var(--state-pending-bg,rgba(111,91,167,.10));}
.mds-trust__label{font-family:var(--font-data,monospace);font-size:9px;font-weight:700;text-transform:uppercase;
  letter-spacing:.05em;color:var(--text-muted,#59636F);white-space:nowrap;}
.mds-trust__value{font-family:var(--font-body);font-size:12px;font-weight:600;color:var(--text-primary,#22272E);white-space:nowrap;}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "trust-strip");
  el.textContent = css;
  document.head.appendChild(el);
}

const STATE = {
  ready: "ready", passed: "ready", healthy: "ready", live: "ready", certified: "ready", approved: "ready",
  review: "review", reviewrequired: "review", inreview: "review", warning: "review", attention: "review",
  needsattention: "review", inprogress: "review",
  blocked: "blocked", critical: "blocked", failed: "blocked", degraded: "blocked",
  pending: "pending", submitted: "pending", queued: "pending", strategy: "pending", paper: "pending",
};
function trustState(s) {
  if (!s) return "muted";
  return STATE[String(s).toLowerCase().replace(/[^a-z]/g, "")] || "muted";
}

export function TrustStrip({ items = [], className = "" }) {
  inject();
  return (
    <div className={`mds-trust${className ? " " + className : ""}`}>
      {items.map((it, i) => (
        <div key={it.label || i} className={`mds-trust__item mds-trust__item--${trustState(it.state)}`}>
          <span className="mds-trust__label">{it.label}</span>
          <span className="mds-trust__value">{it.value}</span>
        </div>
      ))}
    </div>
  );
}
