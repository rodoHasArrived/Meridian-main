// Bulk break triage (Phase 3, C2): assign to me, route to an owner, or resolve with a reason.
//
// Every bulk action uses the governed casework path. It dry-runs the whole selection first, then
// executes only the breaks the dry run passed, all or nothing. The server stamps the actor, writes
// an audit entry for each case, and decides eligibility. This module only builds the requests and
// reads the results.
import type { ReconciliationBreakQueueItem, ReconciliationBulkCaseworkRequest, ReconciliationBulkCaseworkResult } from "@/types";

export type BulkTriageKind = "assign" | "route" | "resolve";

/** The casework endpoint's default and maximum case count per bulk request. */
export const BULK_TRIAGE_MAX_BREAKS = 100;
export const BULK_TRIAGE_SOURCE = "workstation-reconciliation-bulk-casework";

export interface BulkTriageDraft {
  kind: BulkTriageKind;
  /** Owner for "route". */
  assignee: string;
  /** Required for "resolve"; an optional handoff note for "route". */
  reason: string;
  rootCauseCode: string;
  resolutionCode: string;
  /** Optional retained evidence reference, e.g. `journal:je-1`. */
  evidenceLink: string;
}

export function emptyBulkTriageDraft(kind: BulkTriageKind): BulkTriageDraft {
  return { kind, assignee: "", reason: "", rootCauseCode: "", resolutionCode: "", evidenceLink: "" };
}

export const BULK_TRIAGE_LABELS: Record<BulkTriageKind, { action: string; verb: string; done: string }> = {
  assign: { action: "Assign to me", verb: "Assign", done: "assigned" },
  route: { action: "Route to owner", verb: "Route", done: "routed" },
  resolve: { action: "Resolve with reason", verb: "Resolve", done: "resolved" }
};

/** Why the draft cannot be checked yet, or null when it can. */
export function bulkTriageDraftProblem(draft: BulkTriageDraft, selectedCount: number): string | null {
  if (selectedCount === 0) return "Select at least one break.";
  if (selectedCount > BULK_TRIAGE_MAX_BREAKS) return `Select at most ${BULK_TRIAGE_MAX_BREAKS} breaks per bulk action.`;
  if (draft.kind === "route" && !draft.assignee.trim()) return "Enter the owner to route these breaks to.";
  if (draft.kind === "resolve") {
    if (!draft.reason.trim()) return "Enter the reason these breaks are resolved.";
    if (!draft.rootCauseCode) return "Choose a root cause.";
    if (!draft.resolutionCode) return "Choose a resolution.";
  }
  return null;
}

export function buildBulkCaseworkRequest(
  draft: BulkTriageDraft,
  breakIds: readonly string[],
  options: { operationId: string; dryRun: boolean; operator: string | null }
): ReconciliationBulkCaseworkRequest {
  const commandId = `${options.operationId}:${options.dryRun ? "dry-run" : "execute"}`;
  const reason = draft.reason.trim() || null;
  const evidenceLink = draft.evidenceLink.trim();
  const base: ReconciliationBulkCaseworkRequest = {
    breakIds: [...breakIds],
    action: draft.kind === "resolve" ? "Resolve" : "Assign",
    // The server replaces the actor and source with the authenticated session.
    actor: options.operator ?? "workstation-operator",
    commandId,
    correlationId: options.operationId,
    source: BULK_TRIAGE_SOURCE,
    idempotencyKey: commandId,
    dryRun: options.dryRun,
    // The dry run reports every case; the execute applies the passed set atomically.
    allowPartialSuccess: options.dryRun,
    maxCaseCount: BULK_TRIAGE_MAX_BREAKS
  };
  switch (draft.kind) {
    case "assign":
      return { ...base, assignToCurrentOperator: true };
    case "route":
      return { ...base, assignee: draft.assignee.trim(), note: reason };
    case "resolve":
      return {
        ...base,
        reason,
        note: reason,
        rootCauseCode: draft.rootCauseCode,
        resolutionCode: draft.resolutionCode,
        evidenceLinks: evidenceLink ? [evidenceLink] : null
      };
  }
}

export interface BulkTriagePreview {
  passedIds: string[];
  blocked: Array<{ breakId: string; error: string }>;
  summary: string;
}

export function summarizeBulkDryRun(kind: BulkTriageKind, result: ReconciliationBulkCaseworkResult): BulkTriagePreview {
  const passedIds = result.results.filter((entry) => entry.wouldSucceed).map((entry) => entry.breakId);
  const blocked = result.results
    .filter((entry) => !entry.wouldSucceed)
    .map((entry) => ({ breakId: entry.breakId, error: entry.error?.trim() || "The case failed validation." }));
  const done = BULK_TRIAGE_LABELS[kind].done;
  const total = result.results.length;
  const summary = passedIds.length === total
    ? `${total === 1 ? "The break" : `All ${total} breaks`} can be ${done}.`
    : passedIds.length === 0
      ? `${total === 1 ? "The selected break" : `None of the ${total} selected breaks`} can be ${done}.`
      : `${passedIds.length} of ${total} breaks can be ${done}. The others stay as they are.`;
  return { passedIds, blocked, summary };
}

export interface BulkTriageOutcome {
  updatedItems: ReconciliationBreakQueueItem[];
  failed: Array<{ breakId: string; error: string }>;
  summary: string;
}

export function summarizeBulkExecute(kind: BulkTriageKind, result: ReconciliationBulkCaseworkResult): BulkTriageOutcome {
  const updatedItems = result.results
    .filter((entry) => entry.succeeded && entry.item)
    .map((entry) => entry.item as ReconciliationBreakQueueItem);
  const failed = result.results
    .filter((entry) => !entry.succeeded)
    .map((entry) => ({ breakId: entry.breakId, error: entry.error?.trim() || "The case was not changed." }));
  const count = updatedItems.length;
  const summary = count === 0
    ? "No breaks were changed."
    : `${count === 1 ? "1 break" : `${count} breaks`} ${BULK_TRIAGE_LABELS[kind].done}. Each break keeps its own audit entry.`;
  return { updatedItems, failed, summary };
}

export function newBulkOperationId(): string {
  const random = typeof crypto !== "undefined" && typeof crypto.randomUUID === "function"
    ? crypto.randomUUID()
    : `${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 10)}`;
  return `bulk-${random}`;
}
