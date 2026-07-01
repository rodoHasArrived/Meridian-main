import { describe, expect, it } from "vitest";
import { buildReportRunReadinessGateViewState } from "@/screens/report-run-parameters-screen.view-model";
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
