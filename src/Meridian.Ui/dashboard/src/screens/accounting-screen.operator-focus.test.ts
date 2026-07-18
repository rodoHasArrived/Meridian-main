import { describe, expect, it } from "vitest";
import { buildAccountingOperatorFocusItems } from "@/screens/accounting-screen.operator-focus";
import type { AccountingWorkspaceResponse } from "@/types";

describe("buildAccountingOperatorFocusItems", () => {
  it("uses a finance-facing break label while retaining the raw category in the case record", () => {
    const accounting = {
      breakQueue: [{
        breakId: "break-cash-1",
        runId: "run-42",
        strategyName: "Fund Alpha",
        category: "CASH_AMOUNT_MISMATCH",
        status: "Open",
        variance: 125,
        reason: "Bank cash differs from the ledger.",
        assignedTo: null,
        detectedAt: "2026-06-30T00:00:00Z",
        lastUpdatedAt: "2026-06-30T01:00:00Z",
        reviewedBy: null,
        reviewedAt: null,
        resolvedBy: null,
        resolvedAt: null,
        resolutionNote: null
      }],
      reconciliationQueue: [],
      cashFlow: null
    } as unknown as AccountingWorkspaceResponse;

    const items = buildAccountingOperatorFocusItems({ accounting, workflowSummary: null });

    expect(items).toHaveLength(1);
    expect(items[0]).toMatchObject({
      id: "break:break-cash-1",
      label: "Cash variance needs review",
      detail: "Bank cash differs from the ledger."
    });
    expect(items[0]?.label).not.toContain("CASH_AMOUNT_MISMATCH");
  });
});
