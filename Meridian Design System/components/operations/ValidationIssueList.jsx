// Meridian validation issue list — the recurring `{ code, severity, message }`
// validation pattern (ExtensibilityValidationIssue, EvidenceValidationIssue,
// AccountingConfigurationValidationIssue, ProviderIntegrationValidationIssue, …).
// Renders issues as compact rows: a severity dot, mono code, message, optional gate.
// Validation severities Info / Warning / Critical collapse onto info / action / blocked.
import React from "react";
import { normalizeSeverity } from "./status";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-issues{display:flex;flex-direction:column;border:1px solid var(--border,#D7DCE2);
  border-radius:var(--radius-card,2px);overflow:hidden;background:var(--bg-light,#fff);}
.mds-issues__empty{padding:14px;text-align:center;color:var(--text-muted,#59636F);
  font-family:var(--font-body);font-size:12px;}
.mds-issue{display:grid;grid-template-columns:auto auto 1fr auto;gap:10px;align-items:baseline;
  padding:9px 12px;border-top:1px solid var(--border,#D7DCE2);}
.mds-issue:first-child{border-top:0;}
.mds-issue--dense{padding:6px 10px;}
.mds-issue__dot{align-self:center;width:7px;height:7px;border-radius:50%;flex:0 0 auto;
  background:var(--severity-info-fg,#6E7781);}
.mds-issue--blocked .mds-issue__dot{background:var(--severity-blocked-fg,#BA3F55);}
.mds-issue--action .mds-issue__dot{background:var(--severity-action-fg,#8A520E);}
.mds-issue--review .mds-issue__dot{background:var(--severity-review-fg,#2F6F8F);}
.mds-issue--ready .mds-issue__dot{background:var(--severity-ready-fg,#16885F);}
.mds-issue__code{font-family:var(--font-data,monospace);font-size:11px;font-weight:700;
  color:var(--text-secondary,#4D5967);white-space:nowrap;}
.mds-issue__msg{font-family:var(--font-body);font-size:12px;color:var(--text-primary,#22272E);line-height:1.4;min-width:0;}
.mds-issue__gate{font-family:var(--font-data,monospace);font-size:9px;font-weight:700;text-transform:uppercase;
  letter-spacing:.04em;color:var(--text-muted,#59636F);white-space:nowrap;}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "validation-issues");
  el.textContent = css;
  document.head.appendChild(el);
}

export function ValidationIssueList({ issues = [], dense = false, emptyLabel = "No issues", className = "" }) {
  inject();
  if (!issues.length) {
    return (
      <div className={`mds-issues${className ? " " + className : ""}`}>
        <div className="mds-issues__empty">{emptyLabel}</div>
      </div>
    );
  }
  return (
    <div className={`mds-issues${className ? " " + className : ""}`}>
      {issues.map((it, i) => {
        const sev = normalizeSeverity(it.severity);
        return (
          <div key={it.code || i} className={`mds-issue mds-issue--${sev}${dense ? " mds-issue--dense" : ""}`}>
            <span className="mds-issue__dot" aria-hidden="true" />
            {it.code && <span className="mds-issue__code">{it.code}</span>}
            <span className="mds-issue__msg">{it.message}</span>
            {it.gate && <span className="mds-issue__gate">{it.gate}</span>}
          </div>
        );
      })}
    </div>
  );
}
