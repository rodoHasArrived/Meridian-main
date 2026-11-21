import type { MetricCardTone } from "@/components/data/metric-card";
import { normalizeDesignSystemSeverity, type DesignSystemSeverity } from "@/design-system/status";

/**
 * The one mapping from a status to its look (UX consolidation P2). Every status, tone or badge
 * variant a view model produces goes through {@link toSeverity} onto the five canonical
 * severities, and each severity has exactly one panel, text, badge and metric style. Screens do
 * not keep their own tone-to-class maps.
 *
 * Accepted input: a canonical severity, a Meridian status string ("ReviewRequired", "Posted"),
 * or a legacy presentation tone ("success", "warning", "danger", "muted", "default"). Unknown
 * server states escalate to "action" as {@link normalizeDesignSystemSeverity} does.
 */
export type StatusInput = DesignSystemSeverity | string | null | undefined;

// Presentation tones the status vocabulary does not name. "warning" and "danger" are already
// aliases there (action, blocked); these are the neutral and positive tones that must not escalate.
const PRESENTATION_TONE_SEVERITY: Record<string, DesignSystemSeverity> = {
  muted: "info",
  outline: "info",
  primary: "info",
  secondary: "info",
  positive: "ready",
  trusted: "ready",
  destructive: "blocked",
  negative: "blocked"
};

/** Any status, tone or badge variant onto one of the five canonical severities. */
export function toSeverity(status: StatusInput): DesignSystemSeverity {
  const key = String(status ?? "").toLowerCase().replace(/[^a-z]/g, "");
  return PRESENTATION_TONE_SEVERITY[key] ?? normalizeDesignSystemSeverity(status);
}

export type StatusBadgeVariant = "outline" | "success" | "warning" | "danger";

const BADGE_VARIANT: Record<DesignSystemSeverity, StatusBadgeVariant> = {
  ready: "success",
  review: "warning",
  action: "warning",
  blocked: "danger",
  info: "outline"
};

const PANEL_CLASS: Record<DesignSystemSeverity, string> = {
  ready: "border-[var(--severity-ready-bd)] bg-[var(--severity-ready-bg)]",
  review: "border-[var(--severity-review-bd)] bg-[var(--severity-review-bg)]",
  action: "border-[var(--severity-action-bd)] bg-[var(--severity-action-bg)]",
  blocked: "border-[var(--severity-blocked-bd)] bg-[var(--severity-blocked-bg)]",
  info: "border-border/70 bg-secondary/20"
};

// Informational text is not tinted: it reads in the surrounding text colour.
const TEXT_CLASS: Record<DesignSystemSeverity, string> = {
  ready: "text-[var(--severity-ready-fg)]",
  review: "text-[var(--severity-review-fg)]",
  action: "text-[var(--severity-action-fg)]",
  blocked: "text-[var(--severity-blocked-fg)]",
  info: ""
};

const METRIC_TONE: Record<DesignSystemSeverity, MetricCardTone> = {
  ready: "success",
  review: "warning",
  action: "warning",
  blocked: "danger",
  info: "neutral"
};

/** The `Badge` variant for a status. Prefer `StatusBadge` for new status UI. */
export function statusBadgeVariant(status: StatusInput): StatusBadgeVariant {
  return BADGE_VARIANT[toSeverity(status)];
}

/** Border and background classes for a panel, card or callout carrying a status. */
export function statusPanelClass(status: StatusInput): string {
  return PANEL_CLASS[toSeverity(status)];
}

/** Text colour for a status value; empty for informational, which inherits. */
export function statusTextClass(status: StatusInput): string {
  return TEXT_CLASS[toSeverity(status)];
}

/** `MetricCard` tone for a status. */
export function statusMetricTone(status: StatusInput): MetricCardTone {
  return METRIC_TONE[toSeverity(status)];
}
