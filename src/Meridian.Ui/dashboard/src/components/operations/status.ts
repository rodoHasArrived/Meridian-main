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

/** The five canonical operator severities the Concrete visual system encodes. */
export type SeverityKey = "ready" | "review" | "action" | "blocked" | "info";

export const severityKeys: readonly SeverityKey[] = [
  "ready",
  "review",
  "action",
  "blocked",
  "info",
];

export const severityLabels: Record<SeverityKey, string> = {
  ready: "Ready",
  review: "Review",
  action: "Action",
  blocked: "Blocked",
  info: "Info",
};

const SEVERITY_MAP: Record<string, SeverityKey> = {
  // ready / green
  ready: "ready", passed: "ready", healthy: "ready", complete: "ready", completed: "ready",
  cleared: "ready", certified: "ready", approved: "ready", posted: "ready", live: "ready",
  linked: "ready", livelinked: "ready", verified: "ready", success: "ready", ok: "ready",
  // review / steel-blue
  review: "review", reviewrequired: "review", inreview: "review", inprogress: "review",
  reviewerassigned: "review", submitted: "review", awaitingoperatordecision: "review",
  readyforreview: "review", running: "review", queued: "review", sourcebacked: "review",
  // action / ochre — needs operator attention but not hard-blocked
  action: "action", needsattention: "action", needsfix: "action", warning: "action",
  degraded: "action", attention: "action", exceptionsopen: "action", stale: "action",
  reviewrequiredsoon: "action", drafted: "action", deferred: "action",
  // blocked / brick-red
  blocked: "blocked", critical: "blocked", failed: "blocked", rejected: "blocked",
  error: "blocked", blocker: "blocked",
  // info / muted — unknown, not yet started, absent
  info: "info", unknown: "info", notstarted: "info", missing: "info", pending: "info",
  draft: "info", neutral: "info", notrequired: "info", default: "info", "": "info",
};

/** Collapse any Meridian status / severity string onto one of the 5 canonical severities. */
export function normalizeSeverity(status?: string | null): SeverityKey {
  if (!status) return "info";
  const key = String(status).toLowerCase().replace(/[^a-z]/g, "");
  return SEVERITY_MAP[key] ?? "info";
}

/** `"ReviewRequired"` → `"Review Required"`; `"NotStarted"` → `"Not Started"`. */
export function humanizeStatus(status?: string | null): string {
  if (!status) return "";
  return String(status)
    .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
    .replace(/[_-]+/g, " ")
    .replace(/^./, (c) => c.toUpperCase());
}
