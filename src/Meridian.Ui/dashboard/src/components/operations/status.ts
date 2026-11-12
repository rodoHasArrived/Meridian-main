// Meridian operator status vocabulary (Concrete design system).
//
// The platform classifies nearly everything as a readiness state. Across the type
// model the recurring status strings are: Ready · ReviewRequired · Blocked · Unknown ·
// Missing · Stale · NotStarted · InProgress · Passed, plus validation severities
// Info · Warning · Critical. This helper collapses all of them onto the five canonical
// operator severities the visual system encodes: ready · review · action · blocked ·
// info — mirroring the `.severity-badge` classes in the desktop stylesheet.
//
// Re-implemented natively in TypeScript for the browser workstation; sibling U1 owns the
// `--severity-*` / `--state-*` design tokens, so every consumer here reads tokens with a
// hex fallback that matches the handoff output.

import {
  DESIGN_SYSTEM_SEVERITIES,
  normalizeDesignSystemSeverity,
  type DesignSystemSeverity,
} from "@/design-system/status";

/** The five canonical operator severities the Concrete visual system encodes. */
export type SeverityKey = DesignSystemSeverity;

export const severityKeys: readonly SeverityKey[] = DESIGN_SYSTEM_SEVERITIES;

export const severityLabels: Record<SeverityKey, string> = {
  ready: "Ready",
  review: "Review",
  action: "Action",
  blocked: "Blocked",
  info: "Info",
};

/**
 * Collapse any Meridian status / severity string onto one of the 5 canonical severities.
 *
 * Delegates to the single design-system normaliser so the operations components and
 * `StatusBadge` share one alias vocabulary: an empty status stays `info`, and a status string
 * nobody recognises escalates to `action` rather than looking informational.
 */
export function normalizeSeverity(status?: string | null): SeverityKey {
  return normalizeDesignSystemSeverity(status);
}

/** `"ReviewRequired"` → `"Review Required"`; `"NotStarted"` → `"Not Started"`. */
export function humanizeStatus(status?: string | null): string {
  if (!status) return "";
  return String(status)
    .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
    .replace(/[_-]+/g, " ")
    .replace(/^./, (c) => c.toUpperCase());
}
