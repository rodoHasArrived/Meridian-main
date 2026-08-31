/**
 * Operator status chip — mirrors `.severity-badge`. The single most-used status
 * encoding in Meridian: it collapses every readiness / gate / validation status
 * string in the data model onto five canonical severities, each with its own
 * mono, uppercase, alpha-filled treatment.
 *
 * Severity mapping (via `normalizeSeverity`):
 * - **ready** (green) — Ready, Passed, Healthy, Complete, Approved, Certified, Live
 * - **review** (blue) — ReviewRequired, InReview, InProgress, Submitted, Running
 * - **action** (amber) — NeedsAttention, NeedsFix, Warning, Degraded, ExceptionsOpen, Stale
 * - **blocked** (red) — Blocked, Critical, Failed, Rejected
 * - **info** (muted) — Unknown, NotStarted, Missing, Pending, Draft
 *
 * @example
 * <SeverityBadge status="ReviewRequired" />          // → "Review" (blue)
 * <SeverityBadge status="Blocked" label="3 breaks" /> // custom label, red
 */
export interface SeverityBadgeProps extends React.HTMLAttributes<HTMLSpanElement> {
  /**
   * Any Meridian readiness / severity string. Normalized to ready · review ·
   * action · blocked · info. @default "info"
   */
  status?: string;
  /** Override the auto label (defaults to the canonical severity name). */
  label?: React.ReactNode;
  /** Prepend a filled status dot. @default true */
  dot?: boolean;
}
export declare function SeverityBadge(props: SeverityBadgeProps): JSX.Element;
