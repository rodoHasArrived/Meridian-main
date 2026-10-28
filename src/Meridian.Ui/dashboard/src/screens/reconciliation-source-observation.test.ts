import { describe, expect, it } from "vitest";
import type { ReconciliationBreakLineage } from "@/types";
import { sourceObservationAge, sourceObservationLabel } from "./reconciliation-source-observation";
import { areReconciliationBreakQueuesEquivalent } from "./accounting-screen.reconciliation-queue-utils";
import type { ReconciliationBreakQueueItem } from "@/types";
const lineage: ReconciliationBreakLineage = {
  lineageId: "l", comparisonScopeId: "s", occurrenceId: "o", occurrenceNumber: 1, observationState: "Aging",
  firstObservedAt: "2026-09-01T00:00:00Z", occurrenceFirstObservedAt: "2026-09-01T00:00:00Z",
  lastObservedAt: "2026-09-02T12:00:00Z", lastObservedRunId: "run"
};
describe("retained source observation", () => {
  it("does not manufacture age for legacy rows", () => {
    expect(sourceObservationLabel(null)).toBe("Identity not established");
    expect(sourceObservationAge(null)).toBe("Age not tracked");
  });
  it("shows retained age and distinguishes source clearing from case closure", () => {
    expect(sourceObservationAge(lineage)).toBe("36.0h observed");
    expect(sourceObservationLabel({ ...lineage, observationState: "Cleared" })).toBe("Source cleared");
    expect(sourceObservationAge({ ...lineage, clearedAt: "2026-09-03T00:00:00Z" })).toBe("48.0h observed");
  });
  it("resets observed age for a recurrence without changing original first observation", () => {
    expect(sourceObservationAge({ ...lineage, occurrenceNumber: 2, occurrenceFirstObservedAt: lineage.lastObservedAt })).toBe("0.0h observed");
  });
  it("refreshes when only retained lineage changes", () => {
    const row = { breakId: "b", lineage } as ReconciliationBreakQueueItem;
    expect(areReconciliationBreakQueuesEquivalent([row], [{ ...row, lineage: { ...lineage, observationState: "Cleared" } }])).toBe(false);
    expect(areReconciliationBreakQueuesEquivalent([row], [{ ...row, lineage: { ...lineage } }])).toBe(true);
  });
});
