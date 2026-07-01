// Meridian severity badge (Concrete) — mirrors the desktop `.severity-badge`.
//
// The operator status chip: mono, 9px, uppercase, alpha-10 wash + solid semantic border
// (never a solid fill). Accepts any of the platform's readiness / severity strings
// (Ready, ReviewRequired, Blocked, Critical, Stale, …) and collapses them onto the five
// canonical severities via `normalizeSeverity`.
import type { HTMLAttributes, ReactNode } from "react";
import { injectStyle } from "./inject-style";
import { normalizeSeverity, severityLabels } from "./status";

const CSS = `
.mds-sev{display:inline-flex;align-items:center;gap:5px;width:fit-content;max-width:100%;
  min-height:20px;border:1px solid var(--severity-info-bd,#D7DCE2);border-radius:var(--radius-chip,2px);
  background:var(--severity-info-bg,#F5F7FA);color:var(--severity-info-fg,#6E7781);
  font-family:var(--font-data,"Cascadia Mono","JetBrains Mono",monospace);font-size:9px;font-weight:700;line-height:1;
  letter-spacing:.04em;padding:0 7px;text-transform:uppercase;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;}
.mds-sev__dot{height:5px;width:5px;border-radius:50%;background:currentColor;flex:0 0 auto;}
.mds-sev--ready{border-color:var(--severity-ready-bd,rgba(22,136,95,.36));background:var(--severity-ready-bg,rgba(22,136,95,.10));color:var(--severity-ready-fg,#16885F);}
.mds-sev--review{border-color:var(--severity-review-bd,rgba(47,111,143,.36));background:var(--severity-review-bg,rgba(47,111,143,.10));color:var(--severity-review-fg,#2F6F8F);}
.mds-sev--action{border-color:var(--severity-action-bd,rgba(138,82,14,.42));background:var(--severity-action-bg,rgba(138,82,14,.11));color:var(--severity-action-fg,#8A520E);}
.mds-sev--blocked{border-color:var(--severity-blocked-bd,rgba(186,63,85,.40));background:var(--severity-blocked-bg,rgba(186,63,85,.10));color:var(--severity-blocked-fg,#BA3F55);}
.mds-sev--info{border-color:var(--severity-info-bd,#D7DCE2);background:var(--severity-info-bg,#F5F7FA);color:var(--severity-info-fg,#6E7781);}
`;

export interface SeverityBadgeProps extends HTMLAttributes<HTMLSpanElement> {
  /**
   * Any Meridian readiness / severity string. Normalized to
   * ready · review · action · blocked · info.
   * @default "info"
   */
  status?: string;
  /** Override the auto label (defaults to the canonical severity name). */
  label?: ReactNode;
  /** Prepend a filled status dot. @default true */
  dot?: boolean;
}

/**
 * Operator status chip — the single most-used status encoding in Meridian.
 *
 * @example
 * <SeverityBadge status="ReviewRequired" />          // → "Review" (steel-blue)
 * <SeverityBadge status="Blocked" label="3 breaks" /> // custom label, brick-red
 */
export function SeverityBadge({
  status = "info",
  label,
  dot = true,
  className,
  ...rest
}: SeverityBadgeProps) {
  injectStyle("severity-badge", CSS);
  const sev = normalizeSeverity(status);
  return (
    <span className={`mds-sev mds-sev--${sev}${className ? " " + className : ""}`} {...rest}>
      {dot && <span className="mds-sev__dot" aria-hidden="true" />}
      {label != null ? label : severityLabels[sev]}
    </span>
  );
}
