// Meridian readiness panel — mirrors `.panel-state`. A gate / readiness summary
// block: a SeverityBadge state label + optional score, a title, a detail line,
// optional body content, and an action footer. The workhorse for "this lane is
// ReviewRequired because…" panels across Operations, Accounting, and Data.
import React from "react";
import { SeverityBadge } from "./SeverityBadge";
import { normalizeSeverity } from "./status.js";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-rpanel{display:grid;gap:8px;min-width:0;border:1px solid var(--severity-info-bd,#D2CDC3);
  border-left:3px solid var(--severity-info-fg,#6B737B);border-radius:var(--radius-card,2px);
  background:var(--bg-light,#F8F7F3);padding:12px 14px;box-shadow:var(--shadow-soft,0 1px 1px rgba(0,0,0,.06));}
.mds-rpanel--ready{border-color:var(--severity-ready-bd,rgba(74,124,94,.36));border-left-color:var(--severity-ready-fg,#2E7655);}
.mds-rpanel--review{border-color:var(--severity-review-bd,rgba(47,111,143,.36));border-left-color:var(--severity-review-fg,#C56343);}
.mds-rpanel--action{border-color:var(--severity-action-bd,rgba(154,113,19,.42));border-left-color:var(--severity-action-fg,#986717);}
.mds-rpanel--blocked{border-color:var(--severity-blocked-bd,rgba(166,67,63,.40));border-left-color:var(--severity-blocked-fg,#B54B42);}
.mds-rpanel__head{display:flex;align-items:center;justify-content:space-between;gap:10px;min-width:0;}
.mds-rpanel__score{font-family:var(--font-data,monospace);font-size:11px;font-weight:700;
  color:var(--text-secondary,#535C65);font-variant-numeric:tabular-nums;white-space:nowrap;}
.mds-rpanel__title{margin:0;color:var(--text-primary,#20252B);font-size:13px;font-weight:700;line-height:1.25;
  overflow:hidden;text-overflow:ellipsis;}
.mds-rpanel__detail{margin:0;color:var(--text-muted,#6B737B);font-size:12px;line-height:1.45;}
.mds-rpanel__foot{display:flex;flex-wrap:wrap;gap:8px;align-items:center;justify-content:flex-end;
  border-top:1px solid var(--border,#D2CDC3);padding-top:10px;margin-top:2px;}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "readiness-panel");
  el.textContent = css;
  document.head.appendChild(el);
}

export function ReadinessPanel({ state = "info", statusLabel, title, detail, score, actions, children }) {
  inject();
  const sev = normalizeSeverity(state);
  return (
    <div className={`mds-rpanel mds-rpanel--${sev}`}>
      <div className="mds-rpanel__head">
        <SeverityBadge status={state} label={statusLabel} />
        {score != null && <span className="mds-rpanel__score">{score}</span>}
      </div>
      {title && <p className="mds-rpanel__title">{title}</p>}
      {detail && <p className="mds-rpanel__detail">{detail}</p>}
      {children}
      {actions && <div className="mds-rpanel__foot">{actions}</div>}
    </div>
  );
}
