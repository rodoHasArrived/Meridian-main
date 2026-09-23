// Meridian SLA chip — case-clock chip mirroring the reconciliation case SLA model
// (`ReconciliationCaseSummaryDto`: SlaState OnTrack | Warning | Breached, SlaDueAtUtc,
// AgeBand, BusinessAgeHours). Mono, severity-tinted, deterministic: pass `now` to pin
// the countdown (it never self-ticks — re-render to advance it).
import React from "react";
import { normalizeSeverity } from "./status";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-sla{display:inline-flex;align-items:center;gap:6px;width:fit-content;max-width:100%;
  min-height:20px;border:1px solid var(--severity-info-bd,#E4E3DE);border-radius:var(--radius-chip,2px);
  background:var(--severity-info-bg,#EDEAE4);color:var(--severity-info-fg,#5E666F);
  font-family:var(--font-data,"Cascadia Mono",monospace);font-size:9px;font-weight:700;line-height:1;
  letter-spacing:.04em;padding:0 7px;text-transform:uppercase;white-space:nowrap;}
.mds-sla__dot{height:5px;width:5px;border-radius:50%;background:currentColor;flex:0 0 auto;}
.mds-sla__sep{opacity:.45;}
.mds-sla__val{font-weight:600;}
.mds-sla--ready{border-color:var(--severity-ready-bd,rgba(58,122,86,.36));background:var(--severity-ready-bg,rgba(58,122,86,.10));color:var(--severity-ready-fg,#2C5C40);}
.mds-sla--action{border-color:var(--severity-action-bd,rgba(138,92,18,.42));background:var(--severity-action-bg,rgba(138,92,18,.11));color:var(--severity-action-fg,#68450E);}
.mds-sla--blocked{border-color:var(--severity-blocked-bd,rgba(168,68,60,.40));background:var(--severity-blocked-bg,rgba(168,68,60,.10));color:var(--severity-blocked-fg,#7E332D);}
.mds-sla--review{border-color:var(--severity-review-bd,rgba(168,84,54,.36));background:var(--severity-review-bg,rgba(168,84,54,.10));color:var(--severity-review-fg,#8C4429);}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "sla-chip");
  el.textContent = css;
  document.head.appendChild(el);
}

/** 8_040_000 → "2h 14m"; 86_400_000 → "1d 0h"; 540_000 → "9m". */
export function formatSlaDuration(ms) {
  const abs = Math.max(0, Math.round(Math.abs(ms) / 60000)); // minutes
  const d = Math.floor(abs / 1440);
  const h = Math.floor((abs % 1440) / 60);
  const m = abs % 60;
  if (d > 0) return `${d}d ${h}h`;
  if (h > 0) return `${h}h ${m}m`;
  return `${m}m`;
}

const STATE_LABELS = { ready: "on track", action: "at risk", blocked: "breached", review: "sla", info: "sla" };

export function SlaChip({
  state = "OnTrack",
  dueAtUtc,
  ageBand,
  businessAgeHours,
  now,
  label = "SLA",
  className = "",
  ...rest
}) {
  inject();
  const sev = normalizeSeverity(state);
  const nowMs = now == null ? Date.now() : (typeof now === "number" ? now : Date.parse(now));

  let value = null;
  const dueMs = dueAtUtc ? Date.parse(dueAtUtc) : NaN;
  if (!Number.isNaN(dueMs)) {
    const diff = dueMs - nowMs;
    value = diff >= 0 ? `due ${formatSlaDuration(diff)}` : `over by ${formatSlaDuration(diff)}`;
  } else if (ageBand) {
    value = String(ageBand);
  } else if (businessAgeHours != null) {
    value = `${Number(businessAgeHours).toFixed(1)}h open`;
  }

  const stateLabel = STATE_LABELS[sev] || "sla";
  return (
    <span className={`mds-sla mds-sla--${sev}${className ? " " + className : ""}`} {...rest}>
      <span className="mds-sla__dot" aria-hidden="true" />
      {label ? <span>{label}</span> : null}
      {label ? <span className="mds-sla__sep">·</span> : null}
      <span>{stateLabel}</span>
      {value ? <span className="mds-sla__sep">·</span> : null}
      {value ? <span className="mds-sla__val">{value}</span> : null}
    </span>
  );
}
