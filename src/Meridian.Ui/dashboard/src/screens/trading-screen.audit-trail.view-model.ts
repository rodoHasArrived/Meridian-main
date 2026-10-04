/**
 * Presentation logic for the execution audit trail panel.
 *
 * The panel's job is to make a governed action trail readable *and* to make gaps
 * in it visible: the server chains each entry to the previous one, so a missing
 * link is a finding, not a rendering detail.
 */

import type { AuditTrailExplorerResult, AuditTrailTimelineEntry } from "@/types/execution-audit.types";

export type AuditTrailTone = "default" | "success" | "warning" | "danger";

export interface AuditTrailRowViewModel {
  auditId: string;
  occurredAt: string;
  objectLabel: string;
  actionLabel: string;
  outcome: string;
  outcomeTone: AuditTrailTone;
  actor: string;
  context: string;
  ledgerLabel: string;
  ledgerTone: AuditTrailTone;
  evidenceRoute: string | null;
  ariaLabel: string;
}

export interface AuditTrailPanelViewModel {
  rows: AuditTrailRowViewModel[];
  countLabel: string;
  truncated: boolean;
  truncationNotice: string | null;
  asOfLabel: string;
  emptyState: string;
}

/** Outcomes the execution audit trail records for a rejected or failed action. */
const FAILED_OUTCOMES = new Set(["failed", "rejected", "denied", "error", "blocked"]);
const WARNING_OUTCOMES = new Set(["partial", "warning", "retried", "cancelled", "canceled", "timedout"]);

export function buildAuditTrailPanelViewModel(
  result: AuditTrailExplorerResult | null,
  limit: number
): AuditTrailPanelViewModel {
  if (!result) {
    return {
      rows: [],
      countLabel: "—",
      truncated: false,
      truncationNotice: null,
      asOfLabel: "Not loaded",
      emptyState: "Audit trail has not loaded."
    };
  }

  const truncated = result.totalMatched > result.returned;
  return {
    rows: result.entries.map(buildAuditTrailRow),
    countLabel: `${result.returned} of ${result.totalMatched}`,
    truncated,
    truncationNotice: truncated
      ? `Showing the ${result.returned} most recent of ${result.totalMatched} matches. Narrow the filters or raise the limit above ${limit} to see the rest.`
      : null,
    asOfLabel: result.asOf,
    emptyState: "No audit entries match these filters."
  };
}

export function buildAuditTrailRow(entry: AuditTrailTimelineEntry): AuditTrailRowViewModel {
  const outcomeTone = auditOutcomeTone(entry.outcome);
  const context = [entry.symbol, entry.runId, entry.correlationId, entry.reason]
    .filter((value): value is string => Boolean(value && value.trim()))
    .join(" · ");

  return {
    auditId: entry.auditId,
    occurredAt: entry.occurredAt,
    objectLabel: `${entry.objectKind} ${entry.objectId}`,
    actionLabel: `${entry.category} · ${entry.action}`,
    outcome: entry.outcome,
    outcomeTone,
    actor: entry.actor ?? "System",
    context: context || entry.message || "—",
    ledgerLabel: auditLedgerLabel(entry),
    ledgerTone: auditLedgerTone(entry),
    evidenceRoute: entry.evidenceRoute ?? null,
    ariaLabel: `${entry.category} ${entry.action} on ${entry.objectKind} ${entry.objectId}, outcome ${entry.outcome}, by ${entry.actor ?? "system"}`
  };
}

export function auditOutcomeTone(outcome: string): AuditTrailTone {
  const normalized = outcome.trim().toLowerCase();
  if (FAILED_OUTCOMES.has(normalized)) {
    return "danger";
  }
  if (WARNING_OUTCOMES.has(normalized)) {
    return "warning";
  }

  return normalized ? "success" : "default";
}

/**
 * An entry that carries no ledger hash is reported as unchained rather than as
 * verified: silence about the chain would read as a clean chain.
 */
export function auditLedgerLabel(entry: AuditTrailTimelineEntry): string {
  if (!entry.currentActionHash) {
    return "Unchained";
  }

  const sequence = entry.actionLedgerSequence === null || entry.actionLedgerSequence === undefined
    ? "no sequence"
    : `#${entry.actionLedgerSequence}`;

  return `${entry.actionLedgerStatus ?? "Recorded"} ${sequence}`;
}

export function auditLedgerTone(entry: AuditTrailTimelineEntry): AuditTrailTone {
  if (!entry.currentActionHash) {
    return "warning";
  }

  const status = (entry.actionLedgerStatus ?? "").trim().toLowerCase();
  if (status === "broken" || status === "mismatch" || status === "tampered") {
    return "danger";
  }

  return "success";
}
