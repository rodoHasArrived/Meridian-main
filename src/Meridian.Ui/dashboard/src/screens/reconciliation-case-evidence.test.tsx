import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { describe, expect, it, vi } from "vitest";
import type { ReconciliationBreakLineage, ReconciliationBreakQueueItem, RetainedStatementCaseEvidence } from "@/types";
import { ReconciliationCaseEvidence, ReconciliationMatchCandidate } from "./reconciliation-case-evidence";

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

const record = { instrument: "ABC", recordDate: "2026-11-20", settlementDate: null, quantity: "6", currency: "USD", transactionType: "trade" };
const retainedMatch: RetainedStatementCaseEvidence = {
  runId: "import-20", matchArtifactSha256: "a".repeat(64),
  tolerancePolicy: { profileId: "cash-policy", version: 7, effectiveDate: "2026-11-20", comparisonPolicyFingerprint: "b".repeat(64) },
  match: {
    tier: "Candidate", explanation: "The same external ID has different execution prices.", confidence: "0.50", amountMeasure: "Net amount",
    ruleIds: ["transaction-external-id"],
    sourceAllocations: [{ ...record, recordId: "source", evidenceReference: "import-20:12", amount: "-9007199254740992.123456", unitPrice: "15.31" }],
    ledgerAllocations: [{ ...record, recordId: "trade-a", evidenceReference: "journal:a", amount: "-9007199254740992.123450", unitPrice: "15.32" }],
    residuals: [{ measure: "Net amount", unit: "USD", sourceTotal: "-9007199254740992.123456", ledgerTotal: "-9007199254740992.123450", residual: "-0.000006", tolerance: "0.000001" }]
  }
};

describe("retained reconciliation case evidence", () => {
  it("keeps source identifiers inspectable and groups missing comparison context", async () => {
    const user = userEvent.setup();
    render(<MemoryRouter><ReconciliationCaseEvidence item={{ ...item, lineage: undefined,
      sourceImportId: undefined, runId: "run-42", evidenceLinks: ["journal:je-cash-1"],
      fundAccountId: undefined, ledgerBookId: undefined, accountingPeriodId: undefined,
      sourceSystem: undefined, asOfDate: undefined }} /></MemoryRouter>);

    expect(screen.getByText("1 linked record")).toBeVisible();
    expect(screen.getByText("Comparison context unavailable: fund account, ledger book, accounting period, as-of date, source.")).toBeVisible();
    expect(screen.getByText("run-42")).not.toBeVisible();
    expect(screen.getByText("journal:je-cash-1")).not.toBeVisible();
    await user.click(screen.getByText("Evidence identifiers"));
    expect(screen.getByText("run-42")).toBeVisible();
    expect(screen.getByText("journal:je-cash-1")).toBeVisible();
  });

  it("renders candidate price, basis and signed decimal evidence at exactly the retained precision", async () => {
    const user = userEvent.setup();
    const candidate = { ...item, statementEvidence: retainedMatch };
    render(<MemoryRouter><ReconciliationCaseEvidence item={candidate} /><ReconciliationMatchCandidate item={candidate} /></MemoryRouter>);
    const comparison = within(screen.getByRole("table", { name: "Exact residual components" }));
    expect(comparison.getByText("−9,007,199,254,740,992.123456")).toBeInTheDocument();
    expect(comparison.getByText("−0.000006")).toBeInTheDocument();
    await user.click(comparison.getByRole("button", { name: /Inspect Net amount residual/ }));
    const passport = await screen.findByRole("dialog", { name: "Amount passport" });
    expect(within(passport).getByText("−0.000006")).toBeInTheDocument();
    expect(within(passport).getByText("Statement minus ledger")).toBeInTheDocument();
    await user.click(within(passport).getByRole("button", { name: "Close drawer" }));
    expect(within(screen.getByRole("region", { name: "Ledger records" })).getByText("15.32 USD")).toBeInTheDocument();
    const suggestion = within(screen.getByRole("region", { name: "Match candidate" }));
    expect(suggestion.getByText(/Review required/)).toBeInTheDocument();
    await user.click(suggestion.getByText("Candidate rules"));
    expect(suggestion.getByText("transaction-external-id")).toBeVisible();
    expect(screen.getByText("cash-policy · Version 7")).toBeInTheDocument();
  });

  it("renders a confirmed merge separately and opens the prior cases it actually joined", async () => {
    const user = userEvent.setup();
    const openCase = vi.fn();
    const merged: ReconciliationBreakQueueItem = { ...item, statementEvidence: retainedMatch, lineage: { ...lineage,
      observationState: "Cleared", clearedByRunId: "import-21", regrouping: {
        kind: "Merged", priorRunId: "import-20", priorBreakIds: [item.breakId, "other-break"], group: {
          matchGroupId: "verified-group", sourceSubjects: [], evidence: { ...retainedMatch, runId: "import-21", match: {
            ...retainedMatch.match, tier: "Exact", explanation: "Two source movements cleared against one ledger posting.",
            sourceAllocations: [...retainedMatch.match.sourceAllocations, { ...record, recordId: "source-2", evidenceReference: "import-21:22", amount: "-20" }]
          } }
        }
      }
    } };
    render(<MemoryRouter><ReconciliationCaseEvidence item={merged} onOpenCase={openCase} /></MemoryRouter>);
    expect(screen.getByText("Merged into one ledger record")).toBeInTheDocument();
    const group = within(screen.getByRole("region", { name: "Confirmed run match group" }));
    expect(group.getByRole("table", { name: "Confirmed group residual components" })).toBeInTheDocument();
    expect(group.getByText("import-21:22")).toBeInTheDocument();
    await user.click(group.getByRole("button", { name: "Open case other-break" }));
    expect(openCase).toHaveBeenCalledWith("other-break");
  });
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
