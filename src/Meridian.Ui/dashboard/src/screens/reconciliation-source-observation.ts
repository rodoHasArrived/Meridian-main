import { formatDecimal } from "@/lib/format";
import type { ReconciliationBreakLineage, ReconciliationBreakQueueAuditEvent, ReconciliationBreakQueueItem } from "@/types";

export function sourceObservationLabel(lineage?: ReconciliationBreakLineage | null): string {
  if (!lineage) return "Identity not established";
  if (lineage.regrouping?.kind === "Split") return "Source split";
  if (lineage.regrouping?.kind === "Merged") return "Source merged";
  return lineage.observationState === "Cleared" ? "Source cleared" : lineage.observationState;
}

export function sourceRunChangeLabel(lineage?: ReconciliationBreakLineage | null): string {
  if (!lineage) return "Comparison unavailable";
  if (lineage.regrouping?.kind === "Split") return "Split across ledger records";
  if (lineage.regrouping?.kind === "Merged") return "Merged into one ledger record";
  switch (lineage.observationState) {
    case "New": return "New observation";
    case "Aging": return "Persisted";
    case "Recurring": return "Recurred after clearing";
    case "Cleared": return "Source cleared";
    default: return "Comparison unavailable";
  }
}

/** Later observations advance the queue's comparison head without replacing the original operands. */
export function originalComparisonScope(
  item: ReconciliationBreakQueueItem,
  history: readonly ReconciliationBreakQueueAuditEvent[] = []
): string | null {
  const sourceRun = item.sourceImportId ?? item.runId;
  if (item.lineage?.lastObservedRunId === sourceRun) return item.lineage.comparisonScopeId || null;

  const scopes = new Set<string>();
  for (const event of history) {
    if (event.breakId !== item.breakId || event.eventType !== "SourceBreakObserved"
      || event.correlationId !== sourceRun || !event.afterPayload) continue;
    try {
      const snapshot = JSON.parse(event.afterPayload) as Partial<ReconciliationBreakQueueItem> | null;
      if (snapshot?.breakId === item.breakId && (snapshot.sourceImportId ?? snapshot.runId) === sourceRun
        && snapshot.sourceFingerprint === item.sourceFingerprint && snapshot.lineage?.lineageId === item.lineage?.lineageId
        && snapshot.lineage?.lastObservedRunId === sourceRun && typeof snapshot.lineage.comparisonScopeId === "string"
        && snapshot.lineage.comparisonScopeId) {
        scopes.add(snapshot.lineage.comparisonScopeId);
      }
    } catch { /* Malformed snapshots cannot establish an original comparison scope. */ }
  }
  return scopes.size === 1 ? [...scopes][0] : null;
}

export function sourceObservationAge(lineage?: ReconciliationBreakLineage | null): string {
  if (!lineage) return "Age not tracked";
  const start = Date.parse(lineage.occurrenceFirstObservedAt);
  const end = Date.parse(lineage.clearedAt ?? lineage.lastObservedAt);
  if (!Number.isFinite(start) || !Number.isFinite(end) || end < start) return "Age unavailable";
  const hours = (end - start) / 3_600_000;
  return `${formatDecimal(hours, 1)}h observed`;
}
