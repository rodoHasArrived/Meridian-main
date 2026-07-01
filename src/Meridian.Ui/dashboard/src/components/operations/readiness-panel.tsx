// Meridian readiness panel (Concrete) — mirrors the desktop `.panel-state`.
//
// A gate / readiness summary block: a SeverityBadge state label + optional score, a
// title, a detail line, optional body content, and a right-aligned action footer. The
// left rail and border tint track `state`. The workhorse for "this lane is
// ReviewRequired because…" panels across Operations, Accounting, and Data.
import type { ReactNode } from "react";
import { injectStyle } from "./inject-style";
import { SeverityBadge } from "./severity-badge";
import { normalizeSeverity } from "./status";

const CSS = `
.mds-rpanel{display:grid;gap:8px;min-width:0;border:1px solid var(--severity-info-bd,#D7DCE2);
  border-left:3px solid var(--severity-info-fg,#6E7781);border-radius:var(--radius-card,2px);
  background:var(--bg-panel,var(--bg-light,#fff));padding:12px 14px;}
.mds-rpanel--ready{border-color:var(--severity-ready-bd,rgba(22,136,95,.36));border-left-color:var(--severity-ready-fg,#16885F);}
.mds-rpanel--review{border-color:var(--severity-review-bd,rgba(47,111,143,.36));border-left-color:var(--severity-review-fg,#2F6F8F);}
.mds-rpanel--action{border-color:var(--severity-action-bd,rgba(138,82,14,.42));border-left-color:var(--severity-action-fg,#8A520E);}
.mds-rpanel--blocked{border-color:var(--severity-blocked-bd,rgba(186,63,85,.40));border-left-color:var(--severity-blocked-fg,#BA3F55);}
.mds-rpanel__head{display:flex;align-items:center;justify-content:space-between;gap:10px;min-width:0;}
.mds-rpanel__score{font-family:var(--font-data,monospace);font-size:11px;font-weight:700;
  color:var(--text-secondary,#4D5967);font-variant-numeric:tabular-nums;white-space:nowrap;}
.mds-rpanel__title{margin:0;color:var(--text-primary,#22272E);font-size:13px;font-weight:700;line-height:1.25;
  overflow:hidden;text-overflow:ellipsis;}
.mds-rpanel__detail{margin:0;color:var(--text-muted,#59636F);font-size:12px;line-height:1.45;}
.mds-rpanel__foot{display:flex;flex-wrap:wrap;gap:8px;align-items:center;justify-content:flex-end;
  border-top:1px solid var(--border,#D7DCE2);padding-top:10px;margin-top:2px;}
`;

export interface ReadinessPanelProps {
  /** Readiness status string (Ready, ReviewRequired, Blocked, …). @default "info" */
  state?: string;
  /** Override the badge label (defaults to the canonical severity name). */
  statusLabel?: ReactNode;
  /** Bold panel title. */
  title?: ReactNode;
  /** Muted supporting detail line. */
  detail?: ReactNode;
  /** Optional mono score / metric shown top-right (e.g. "86 / 100"). */
  score?: ReactNode;
  /** Right-aligned action footer (buttons). */
  actions?: ReactNode;
  /** Extra body content between the detail line and the footer. */
  children?: ReactNode;
}

/**
 * Readiness panel — a gate / readiness summary block led by a {@link SeverityBadge}.
 *
 * @example
 * <ReadinessPanel
 *   state="ReviewRequired"
 *   score="86 / 100"
 *   title="Reconciliation"
 *   detail="3 open exceptions above tolerance in the custody cash lane."
 *   actions={<><Button variant="ghost">Investigate</Button><Button variant="primary">Approve</Button></>}
 * />
 */
export function ReadinessPanel({
  state = "info",
  statusLabel,
  title,
  detail,
  score,
  actions,
  children,
}: ReadinessPanelProps) {
  injectStyle("readiness-panel", CSS);
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
