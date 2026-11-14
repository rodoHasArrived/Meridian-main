/**
 * Status — carrier object for the operator status vocabulary (components/operations/status.js).
 *
 * Meridian collapses every domain status string onto FIVE canonical severities; all
 * operations components encode exactly this mapping. Consume the same functions so your
 * severity logic (sorting, worst-of roll-ups, filters) never disagrees with the chips:
 *
 *   const { normalizeSeverity, severityKeys } = window.MeridianDesignSystem_205d17.Status;
 *   rows.sort((a, b) => severityKeys.indexOf(normalizeSeverity(a.status)) -
 *                       severityKeys.indexOf(normalizeSeverity(b.status)));
 *
 * The full domain-word → severity table is the wire contract in
 * guidelines/ENTITY_SCHEMAS.md › Status vocabulary. Pass raw domain words
 * ("BreaksDetected", "AwaitingApproval") — never pre-map to the canonical five.
 */
export interface StatusHelpers {
  /** The five canonical severities, ordered best → informational: ["ready", "review", "action", "blocked", "info"]. */
  severityKeys: ["ready", "review", "action", "blocked", "info"];
  /** Display labels for the canonical severities: { ready: "Ready", … }. */
  severityLabels: Record<"ready" | "review" | "action" | "blocked" | "info", string>;
  /**
   * Collapse any Meridian status / severity string onto one of the five canonical
   * severities. Case- and punctuation-insensitive ("ReviewRequired" ≡ "review required").
   * Unknown or empty input → "info" — a status the system doesn't recognize must never
   * look like a state it can vouch for.
   */
  normalizeSeverity(status: string | null | undefined): "ready" | "review" | "action" | "blocked" | "info";
  /** "ReviewRequired" → "Review Required"; "breaks_detected" → "Breaks detected". */
  humanizeStatus(status: string | null | undefined): string;
}
export declare const Status: StatusHelpers;
