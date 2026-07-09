import { DesignSystemBadge, type DesignSystemBadgeProps } from "@/design-system/badge";

/**
 * The Meridian operator status vocabulary collapses every readiness string onto five
 * canonical severities, mirroring `components/operations/status.js` in the vendored
 * design-system package.
 */
export const DESIGN_SYSTEM_SEVERITIES = ["ready", "review", "action", "blocked", "info"] as const;
export type DesignSystemSeverity = typeof DESIGN_SYSTEM_SEVERITIES[number];

const SEVERITY_ALIASES: Record<string, DesignSystemSeverity> = {
  ready: "ready", passed: "ready", healthy: "ready", complete: "ready", completed: "ready",
  cleared: "ready", certified: "ready", approved: "ready", posted: "ready", live: "ready",
  linked: "ready", verified: "ready", success: "ready", ok: "ready", matched: "ready",
  resolved: "ready", ontrack: "ready", signedoff: "ready",
  review: "review", reviewrequired: "review", inreview: "review", inprogress: "review",
  submitted: "review", awaitingoperatordecision: "review", readyforreview: "review",
  running: "review", queued: "review", awaitingapproval: "review", reopened: "review",
  action: "action", needsattention: "action", needsfix: "action", warning: "action",
  degraded: "action", attention: "action", stale: "action", drafted: "action",
  deferred: "action", breaksdetected: "action", atrisk: "action", partial: "action",
  blocked: "blocked", critical: "blocked", failed: "blocked", rejected: "blocked",
  error: "blocked", blocker: "blocked", breached: "blocked",
  info: "info", unknown: "info", notstarted: "info", missing: "info", pending: "info",
  draft: "info", neutral: "info", notrequired: "info", notready: "info", skipped: "info",
  paused: "info"
};

/** Collapse any Meridian status / severity string onto one of the five canonical severities. */
export function normalizeDesignSystemSeverity(status: string | null | undefined): DesignSystemSeverity {
  if (!status) {
    return "info";
  }

  const key = String(status).toLowerCase().replace(/[^a-z]/g, "");
  if (!key) {
    return "info";
  }

  // Unknown server-provided states should require operator attention rather than
  // silently looking informational. Explicit "unknown" still maps to info above.
  return SEVERITY_ALIASES[key] ?? "action";
}

const SEVERITY_BADGE_VARIANT: Record<DesignSystemSeverity, NonNullable<DesignSystemBadgeProps["variant"]>> = {
  ready: "success",
  review: "default",
  action: "warning",
  blocked: "danger",
  info: "outline"
};

const SEVERITY_DEFAULT_LABEL: Record<DesignSystemSeverity, string> = {
  ready: "Ready",
  review: "Review",
  action: "Action",
  blocked: "Blocked",
  info: "Info"
};

export interface DesignSystemStatusProps extends Omit<DesignSystemBadgeProps, "variant"> {
  /** A canonical severity, or any Meridian readiness string that will be normalized. */
  status: DesignSystemSeverity | string;
}

/**
 * Severity chip built on {@link DesignSystemBadge}, encoding the five canonical operator
 * severities (ready - review - action - blocked - info) with the design-system's semantic
 * state tokens. Any raw readiness string is normalized via
 * {@link normalizeDesignSystemSeverity}.
 *
 * @example
 * <DesignSystemStatus status="ReviewRequired" />       // -> "Review"
 * <DesignSystemStatus status="blocked" dot>Halted</DesignSystemStatus>
 */
export function DesignSystemStatus({ status, children, dot = true, ...props }: DesignSystemStatusProps) {
  const severity = normalizeDesignSystemSeverity(status);
  return (
    <DesignSystemBadge variant={SEVERITY_BADGE_VARIANT[severity]} dot={dot} {...props}>
      {children ?? SEVERITY_DEFAULT_LABEL[severity]}
    </DesignSystemBadge>
  );
}
