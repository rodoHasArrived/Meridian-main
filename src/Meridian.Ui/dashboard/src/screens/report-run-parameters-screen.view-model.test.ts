import { describe, expect, it } from "vitest";
import {
  buildAuthoritativeReadinessGateViewState,
  buildDefaultReportRunParameterDraft,
  buildReportRunReadinessGateViewState,
  validateAndBuildReportingRunParameters
} from "@/screens/report-run-parameters-screen.view-model";
import type { AccountingWorkspaceResponse, ManualJournalEntryDraft } from "@/types";

function buildDraft(overrides: Partial<ManualJournalEntryDraft>): ManualJournalEntryDraft {
  return {
    journalEntryId: "je-1",
    status: "Draft",
    fundProfileId: "fund-alpha",
    accountingBasis: "Primary",
    accountingDate: "2026-06-30",
    currency: "USD",
    memo: "",
    preparedBy: "browser-user",
    createdAtUtc: "2026-06-30T00:00:00Z",
    updatedAtUtc: "2026-06-30T00:00:00Z",
    version: 1,
    lines: [],
    evidenceLinks: [],
    validationIssues: [],
    totalDebits: 0,
    totalCredits: 0,
    imbalance: 0,
    entryType: "General",
    ...overrides
  };
}

const reconciliationQueue: AccountingWorkspaceResponse["reconciliationQueue"] = [
  {
    runId: "run-1",
    strategyName: "Alpha",
    mode: "paper",
    status: "Running",
    lastUpdated: "3m ago",
    breakCount: 2,
    openBreakCount: 1,
    reconciliationStatus: "BreaksOpen"
  },
  {
    runId: "run-2",
    strategyName: "Beta",
    mode: "paper",
    status: "Paused",
    lastUpdated: "7m ago",
    breakCount: 0,
    openBreakCount: 0,
    reconciliationStatus: "Resolved"
  }
];

describe("buildReportRunReadinessGateViewState", () => {
  it("is clear to run when there are no open breaks or unposted journals", () => {
    const view = buildReportRunReadinessGateViewState({
      reconciliationQueue: [{ ...reconciliationQueue[0], openBreakCount: 0 }],
      manualDrafts: [buildDraft({ status: "Posted" })]
    });

    expect(view.isClear).toBe(true);
    expect(view.items.every((item) => item.tone === "success")).toBe(true);
    expect(view.items.every((item) => item.href === null)).toBe(true);
  });

  it("sums open breaks across the reconciliation queue and links to the reconciliation screen", () => {
    const view = buildReportRunReadinessGateViewState({ reconciliationQueue, manualDrafts: [] });

    const breaksItem = view.items.find((item) => item.id === "open-breaks");
    expect(breaksItem?.count).toBe(1);
    expect(breaksItem?.tone).toBe("warning");
    expect(breaksItem?.href).toBe("/accounting/reconciliation");
    expect(view.isClear).toBe(false);
  });

  it("counts manual drafts that are not Posted, Rejected, or Reversed as unposted", () => {
    const view = buildReportRunReadinessGateViewState({
      reconciliationQueue: [],
      manualDrafts: [
        buildDraft({ journalEntryId: "je-draft", status: "Draft" }),
        buildDraft({ journalEntryId: "je-submitted", status: "Submitted" }),
        buildDraft({ journalEntryId: "je-posted", status: "Posted" }),
        buildDraft({ journalEntryId: "je-rejected", status: "Rejected" }),
        buildDraft({ journalEntryId: "je-reversed", status: "Reversed" })
      ]
    });

    const journalsItem = view.items.find((item) => item.id === "unposted-journals");
    expect(journalsItem?.count).toBe(2);
    expect(journalsItem?.tone).toBe("warning");
    expect(journalsItem?.href).toBe("/accounting/journal-entries");
  });

  it("always includes the advisory disclaimer", () => {
    const view = buildReportRunReadinessGateViewState({ reconciliationQueue: [], manualDrafts: [] });

    expect(view.disclaimer).toMatch(/Advisory only/);
  });
});

describe("reporting P0 parameter and readiness view models", () => {
  it("builds every typed run parameter from an operator-authored portfolio scope", () => {
    const draft = {
      ...buildDefaultReportRunParameterDraft({ fundProfileId: "fund-alpha", asOfDate: "2026-06-30" }),
      entityScopeKind: "Portfolio" as const,
      portfolioId: "portfolio-credit",
      periodId: "2026-Q2",
      ledgerBookId: "11111111-1111-1111-1111-111111111111",
      ledgerBookCode: "STAT-GL",
      accountingBasis: "Statutory" as const,
      presentationCurrency: "eur",
      consolidationLevel: "Portfolio" as const,
      outputFormat: "Xlsx" as const,
      finality: "Final" as const,
      includeSupportingSchedules: false,
      includeEvidenceAppendix: true,
      templateParametersJson: JSON.stringify({ reportingRegion: "EU" })
    };

    const result = validateAndBuildReportingRunParameters(draft, "2026-06-30");

    expect(result.issues).toEqual([]);
    expect(result.parameters).toEqual({
      scope: {
        fundProfileId: "fund-alpha",
        entityScopeKind: "Portfolio",
        entityId: null,
        portfolioId: "portfolio-credit",
        investorId: null,
        dimensions: null
      },
      periodId: "2026-Q2",
      asOfDate: "2026-06-30",
      ledgerBook: {
        ledgerBookId: "11111111-1111-1111-1111-111111111111",
        ledgerBookCode: "STAT-GL"
      },
      accountingBasis: "Statutory",
      presentationCurrency: "EUR",
      consolidationLevel: "Portfolio",
      outputFormat: "Xlsx",
      finality: "Final",
      includeSupportingSchedules: false,
      includeEvidenceAppendix: true,
      templateParameters: { reportingRegion: "EU" }
    });
  });

  it("fails closed when required scope or template-parameter JSON is incomplete", () => {
    const draft = {
      ...buildDefaultReportRunParameterDraft({ fundProfileId: null, asOfDate: "2026-06-30" }),
      entityScopeKind: "Investor" as const,
      ledgerBookCode: "",
      templateParametersJson: "[not-an-object]"
    };

    const result = validateAndBuildReportingRunParameters(draft, "2026-06-30");

    expect(result.parameters).toBeNull();
    expect(result.issues).toEqual(expect.arrayContaining([
      "Select or enter a fund profile.",
      "Enter a ledger book ID or code.",
      "Enter the scoped investor ID.",
      "Template parameters must contain valid JSON."
    ]));
  });

  it("uses the requested finality when a readiness result permits drafts but blocks final output", () => {
    const readiness = {
      evaluationId: "evaluation-1",
      evaluatedAtUtc: "2026-06-30T20:00:00Z",
      resolvedTemplate: { name: "trial-balance-pack", version: 1 },
      resolvedParameters: validateAndBuildReportingRunParameters(
        buildDefaultReportRunParameterDraft({ fundProfileId: "fund-alpha", asOfDate: "2026-06-30" }),
        "2026-06-30"
      ).parameters!,
      status: "Blocked" as const,
      canGenerateDraft: true,
      canGenerateFinal: false,
      checks: [{
        checkId: "evidence",
        label: "Release evidence",
        status: "Blocked" as const,
        summary: "Evidence is required for final output.",
        issueCount: 1,
        blocksDraft: false,
        blocksFinal: true,
        route: null,
        evidenceReferences: []
      }],
      blockingReasons: ["Evidence is required for final output."],
      evidenceHash: "a".repeat(64)
    };

    expect(buildAuthoritativeReadinessGateViewState(readiness, "Draft").canRun).toBe(true);
    expect(buildAuthoritativeReadinessGateViewState(readiness, "Final")).toEqual(expect.objectContaining({
      canRun: false,
      statusLabel: "Final blocked",
      blockingReasons: ["Evidence is required for final output."]
    }));
  });
});
