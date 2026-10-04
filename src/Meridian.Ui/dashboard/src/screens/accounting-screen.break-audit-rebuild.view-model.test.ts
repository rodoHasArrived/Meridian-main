import { describe, expect, it } from "vitest";
import { buildBreakAuditRebuildViewModel } from "@/screens/accounting-screen.break-audit-rebuild.view-model";
import type { ReconciliationBreakQueueItem } from "@/types";

function item(overrides: Partial<ReconciliationBreakQueueItem> = {}): ReconciliationBreakQueueItem {
  return {
    breakId: "break-1",
    runId: "run-42",
    strategyName: "Cash sweep",
    category: "Cash",
    status: "Open",
    variance: -1250.5,
    reason: "Statement cash row has no book counterpart.",
    assignedTo: "j.rowe",
    detectedAt: "2026-08-24T09:00:00Z",
    lastUpdatedAt: "2026-08-25T11:00:00Z",
    reviewedBy: null,
    reviewedAt: null,
    resolvedBy: null,
    resolvedAt: null,
    resolutionNote: null,
    ...overrides
  } as ReconciliationBreakQueueItem;
}

describe("break audit rebuild comparison", () => {
  it("reports not-compared before a rebuild has run", () => {
    const view = buildBreakAuditRebuildViewModel(null, null);

    expect(view.compared).toBe(false);
    expect(view.matches).toBe(false);
    expect(view.verdict).toContain("Not compared");
    expect(view.comparedFieldCount).toBe(0);
  });

  it("does not claim a match when only one side is present", () => {
    expect(buildBreakAuditRebuildViewModel(item(), null).compared).toBe(false);
    expect(buildBreakAuditRebuildViewModel(null, item()).compared).toBe(false);
  });

  it("confirms agreement and says how many fields it compared", () => {
    const view = buildBreakAuditRebuildViewModel(item(), item());

    expect(view.matches).toBe(true);
    expect(view.differences).toEqual([]);
    expect(view.comparedFieldCount).toBeGreaterThan(0);
    expect(view.verdict).toContain(`${view.comparedFieldCount} compared field`);
  });

  it("lists each field the stored break and its audit trail disagree on", () => {
    const view = buildBreakAuditRebuildViewModel(
      item({ status: "Resolved", resolvedBy: "a.smith" }),
      item()
    );

    expect(view.matches).toBe(false);
    expect(view.differences).toEqual([
      { field: "resolvedBy", storedValue: "a.smith", rebuiltValue: "null" },
      { field: "status", storedValue: "Resolved", rebuiltValue: "Open" }
    ]);
    expect(view.verdict).toContain("differs from its audit trail in 2");
  });

  it("separates a field the trail cannot reconstruct from a disagreement", () => {
    const stored = item({ signoffStatus: "Pending" });
    const rebuilt = item();
    // The rebuilt snapshot simply omits what the trail never set.
    delete (rebuilt as unknown as Record<string, unknown>).signoffStatus;

    const view = buildBreakAuditRebuildViewModel(stored, rebuilt);

    expect(view.matches).toBe(true);
    expect(view.notReconstructed).toEqual(["signoffStatus"]);
    expect(view.notReconstructedNotice).toContain("could not be reconstructed");
  });

  it("skips collections, whose ordering is not a discrepancy", () => {
    const view = buildBreakAuditRebuildViewModel(
      item({ evidenceLinks: ["a", "b"] }),
      item({ evidenceLinks: ["b", "a"] })
    );

    expect(view.matches).toBe(true);
    expect(view.notReconstructed).not.toContain("evidenceLinks");
  });

  it("renders null and empty string distinguishably", () => {
    const view = buildBreakAuditRebuildViewModel(
      item({ resolutionNote: "" }),
      item({ resolutionNote: null })
    );

    expect(view.differences).toEqual([
      { field: "resolutionNote", storedValue: '""', rebuiltValue: "null" }
    ]);
  });
});
