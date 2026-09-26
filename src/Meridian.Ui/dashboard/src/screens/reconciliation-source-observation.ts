import type { ReconciliationBreakLineage } from "@/types";

export function sourceObservationLabel(lineage?: ReconciliationBreakLineage | null): string {
  if (!lineage) return "Identity not established";
  return lineage.observationState === "Cleared" ? "Source cleared" : lineage.observationState;
}

export function sourceObservationAge(lineage?: ReconciliationBreakLineage | null): string {
  if (!lineage) return "Age not tracked";
  const start = Date.parse(lineage.occurrenceFirstObservedAt);
  const end = Date.parse(lineage.clearedAt ?? lineage.lastObservedAt);
  if (!Number.isFinite(start) || !Number.isFinite(end) || end < start) return "Age unavailable";
  const hours = (end - start) / 3_600_000;
  return `${hours.toFixed(1)}h observed`;
}
