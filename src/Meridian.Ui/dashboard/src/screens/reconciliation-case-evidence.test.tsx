import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { describe, expect, it } from "vitest";
import type { ReconciliationBreakLineage, ReconciliationBreakQueueItem } from "@/types";
import { ReconciliationCaseEvidence } from "./reconciliation-case-evidence";

const lineage: ReconciliationBreakLineage = {
  lineageId: "cash-lineage", comparisonScopeId: "comparison-v1", occurrenceId: "first-cash-break", occurrenceNumber: 1,
  observationState: "Aging", firstObservedAt: "2026-11-17T00:00:00Z", occurrenceFirstObservedAt: "2026-11-17T00:00:00Z",
  lastObservedAt: "2026-11-20T00:00:00Z", lastObservedRunId: "import-20"
};

const item: ReconciliationBreakQueueItem = {
  breakId: "cash-break-20", runId: "run-20", sourceType: "statement", sourceImportId: "import-20", sourceReference: "statement-line-19",
  strategyName: "Cash review", category: "CashMismatch", status: "Open", variance: 0.00000001, reason: "Cash variance exceeds tolerance.",
  assignedTo: "ops-2", detectedAt: "2026-11-20T00:00:00Z", lastUpdatedAt: "2026-11-20T00:00:00Z", severity: "High",
  reviewedBy: null, reviewedAt: null, resolvedBy: null, resolvedAt: null, resolutionNote: null, version: 4, lineage,
  tenantId: "tenant-1", companyId: "company-1", fundAccountId: "account-1", ledgerBookId: "book-1", accountingPeriodId: "period-1",
  toleranceProfileId: "cash-tolerance", asOfDate: "2026-11-20", measures: [
    { kind: "Value", unit: "EUR", expected: 150, actual: 150.00000001, variance: 0.00000001, tolerance: 0 },
    { kind: "Quantity", unit: "units", expected: 100, actual: null, variance: 0, tolerance: null, unavailableReason: "Source quantity unavailable." }
  ]
};

describe("retained reconciliation case evidence", () => {
  it("shows both operands and each server residual in its supplied unit and with its sign", () => {
    render(<MemoryRouter><ReconciliationCaseEvidence item={item} /></MemoryRouter>);
    const comparison = within(screen.getByRole("table", { name: "Retained residual components" }));
    expect(comparison.getByRole("columnheader", { name: "Statement" })).toBeInTheDocument();
    expect(comparison.getByRole("columnheader", { name: "Ledger" })).toBeInTheDocument();
    const [value, quantity] = comparison.getAllByRole("row").slice(1);
    expect(within(value).getByRole("rowheader")).toHaveTextContent("EUR");
    expect(within(value).getByText("+0.00000001")).toBeInTheDocument();
    expect(within(quantity).getAllByText("Unavailable")).toHaveLength(2);
    expect(screen.getByText("cash-tolerance · Version unavailable")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Review retained evidence" }).getAttribute("href")).toContain("subjectId=cash-break-20");
  });

  it("compares explicit earlier evidence only across matching lineage and comparison scopes", async () => {
    const user = userEvent.setup();
    const prior = { ...item, breakId: "cash-break-18", sourceImportId: "import-18", detectedAt: "2026-11-18T00:00:00Z", lineage: { ...lineage, lastObservedRunId: "import-18" },
      measures: [{ kind: "Value" as const, unit: "EUR", expected: 150, actual: 149.99, variance: -0.01, tolerance: 0 }] };
    const otherPolicy = { ...prior, breakId: "old-policy", lineage: { ...prior.lineage, comparisonScopeId: "comparison-v0" } };
    const otherLineage = { ...prior, breakId: "other-lineage", lineage: { ...prior.lineage, lineageId: "different" } };
    render(<MemoryRouter><ReconciliationCaseEvidence item={item} relatedRows={[prior, otherPolicy, otherLineage]} /></MemoryRouter>);
    const select = screen.getByRole("combobox", { name: "Compare with an earlier retained case" });
    expect(within(select).queryByRole("option", { name: /other-lineage/ })).toBeNull();
    await user.selectOptions(select, "cash-break-18");
    expect(screen.getByText("−0.01")).toBeInTheDocument();
    expect(screen.getByText("Residual changed")).toBeInTheDocument();
    await user.selectOptions(select, "old-policy");
    expect(screen.getByText("Comparison scope changed")).toBeInTheDocument();
    expect(screen.queryByRole("columnheader", { name: "Prior residual" })).not.toBeInTheDocument();
  });

  it("keeps source clearing, reopening reason, and the case status independently visible", () => {
    render(<MemoryRouter><ReconciliationCaseEvidence item={{ ...item, lineage: { ...lineage, observationState: "Cleared", clearedByRunId: "import-21" },
      lifecycleState: "Reopened", reopenedAt: "2026-11-20T12:00:00Z", reopenReason: "The source restated the closing balance." }} /></MemoryRouter>);
    expect(screen.getByText("Source cleared")).toBeInTheDocument();
    expect(screen.getByText("import-21")).toBeInTheDocument();
    expect(screen.getByText("Reopened")).toBeInTheDocument();
    expect(screen.getByText(/restated the closing balance/)).toBeInTheDocument();
  });
});
