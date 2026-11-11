import type { HTMLAttributes, ReactNode } from "react";
import { usesTrackedCaps } from "@/design-system/label-case";
import "@/styles/status-badge.css";

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
  paused: "info",
  // Aliases the operations SeverityBadge recognised before it became StatusBadge, so every
  // status that badge already knew keeps its tone.
  livelinked: "ready", reviewerassigned: "review", sourcebacked: "review",
  exceptionsopen: "action", reviewrequiredsoon: "action", danger: "blocked", default: "info"
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

const SEVERITY_DEFAULT_LABEL: Record<DesignSystemSeverity, string> = {
  ready: "Ready",
  review: "Review",
  action: "Action",
  blocked: "Blocked",
  info: "Info"
};

/**
 * Map the presentation tones screen view-models already use onto the five severities, so a
 * screen can hand its tone straight to {@link StatusBadge}. Unrecognized tones escalate to
 * action, as unrecognized statuses do.
 */
export function toneToSeverity(tone: string | null | undefined): DesignSystemSeverity {
  switch (tone) {
    case "success":
    case "positive":
    case "ready":
      return "ready";
    case "primary":
    case "default":
    case "review":
      return "review";
    case "warning":
    case "action":
      return "action";
    case "danger":
    case "destructive":
    case "blocked":
      return "blocked";
    case "muted":
    case "outline":
    case "neutral":
    case "info":
    case null:
    case undefined:
      return "info";
    default:
      return "action";
  }
}

export { statusLabelText, usesTrackedCaps } from "@/design-system/label-case";

export interface StatusBadgeProps extends Omit<HTMLAttributes<HTMLSpanElement>, "children"> {
  /** A canonical severity, or any Meridian readiness string; normalized onto the five tones. */
  status?: DesignSystemSeverity | string | null;
  /** Visible label; defaults to the canonical severity name. */
  label?: ReactNode;
  /** Same as `label`, for JSX children. `label` wins when both are given. */
  children?: ReactNode;
  /** Prepend a filled status dot. @default true */
  dot?: boolean;
}

/**
 * The one operator status component. Five tones (ready, review, action, blocked, info), shared
 * with the WPF desktop's `.severity-badge`; any readiness string is normalized by
 * {@link normalizeDesignSystemSeverity}, which escalates an unrecognized server state to action
 * rather than hiding it as info. Labels over two words render in normal case.
 *
 * @example
 * <StatusBadge status="ReviewRequired" />          // -> "Review"
 * <StatusBadge status="Blocked" label="3 breaks" /> // custom label, blocked tone
 */
export function StatusBadge({ status = "info", label, children, dot = true, className, ...props }: StatusBadgeProps) {
  const severity = normalizeDesignSystemSeverity(status);
  const content = label ?? children ?? SEVERITY_DEFAULT_LABEL[severity];
  const classes = [
    "mds-sev",
    `mds-sev--${severity}`,
    usesTrackedCaps(content) ? null : "mds-sev--sentence",
    className
  ].filter(Boolean).join(" ");
  return (
    <span className={classes} data-design-system-component="Status" data-severity={severity} {...props}>
      {dot ? <span className="mds-sev__dot" aria-hidden="true" /> : null}
      {content}
    </span>
  );
}

/** @deprecated Use {@link StatusBadge}; kept so existing call sites keep working. */
export type DesignSystemStatusProps = StatusBadgeProps;
/** @deprecated Use {@link StatusBadge}. */
export const DesignSystemStatus = StatusBadge;
