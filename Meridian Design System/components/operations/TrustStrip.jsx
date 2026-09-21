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
  border:1px solid var(--state-muted-bd,#E4E3DE);border-left-width:3px;border-radius:var(--radius-chip,2px);
  background:var(--state-muted-bg,#EDEAE4);padding:5px 10px;}
.mds-trust__item--ready{border-color:var(--state-healthy-bd,rgba(58,122,86,.32));border-left-color:var(--state-healthy-fg,#2C5C40);background:var(--state-healthy-bg,rgba(58,122,86,.10));}
.mds-trust__item--review{border-color:var(--state-warn-bd,rgba(138,92,18,.38));border-left-color:var(--state-warn-fg,#68450E);background:var(--state-warn-bg,rgba(138,92,18,.11));}
.mds-trust__item--blocked{border-color:var(--state-danger-bd,rgba(168,68,60,.35));border-left-color:var(--state-danger-fg,#7E332D);background:var(--state-danger-bg,rgba(168,68,60,.10));}
.mds-trust__item--pending{border-color:var(--state-pending-bd,rgba(93,84,134,.34));border-left-color:var(--state-pending-fg,#463F64);background:var(--state-pending-bg,rgba(93,84,134,.10));}
.mds-trust__label{font-family:var(--font-data,monospace);font-size:9px;font-weight:700;text-transform:uppercase;
  letter-spacing:.05em;color:var(--text-muted,#5E666F);white-space:nowrap;}
.mds-trust__value{font-family:var(--font-body);font-size:12px;font-weight:600;color:var(--text-primary,#22252A);white-space:nowrap;}
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
