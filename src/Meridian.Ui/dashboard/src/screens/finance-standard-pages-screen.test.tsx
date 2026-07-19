import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { ReactElement } from "react";
import * as api from "@/lib/api";
import {
  AccountDetailScreen,
  ApprovalInboxScreen,
  CloseCalendarScreen,
  EvidenceDetailScreen,
  LedgerExplorerScreen,
  ReconciliationMatchWorkbenchScreen,
  ReportPreviewValidationScreen,
  ReportRunDetailScreen
} from "@/screens/finance-standard-pages-screen";
import { renderWithRouter, waitForAsyncEffects } from "@/test/render";
import type { AccountingWorkspaceResponse, FinancialRecordExplorerDto } from "@/types";

vi.mock("@/lib/api", async () => {
  const actual = await vi.importActual<typeof import("@/lib/api")>("@/lib/api");
  return {
    ...actual,
    getFinancialRecordExplorer: vi.fn(),
    getOperationsCloseCalendar: vi.fn(),
    getRunLedgerJournal: vi.fn(),
    getRunTrialBalance: vi.fn(),
    saveFinancialRecordExplorerView: vi.fn()
  };
});

const data = {
  metrics: [],
  reconciliationQueue: [
    {
      runId: "run-42",
      strategyName: "Paper Index Mean Reversion",
      trialBalance: [
        {
          accountId: "acct-cash",
          accountCode: "1000",
          accountName: "Cash",
          accountType: "Asset",
          balance: 120500
        }
      ]
    }
  ],
  breakQueue: [
    {
      breakId: "break-cash-1",
      runId: "run-42",
      strategyName: "Paper Index Mean Reversion",
      category: "CashAmountVariance",
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
    }
  ],
  closePlans: [
    {
      period: "2026-06",
      tasks: [
        { label: "Run trial balance", status: "Pending" },
        { label: "Controller approval", status: "Blocked" }
      ],
      approvals: [
        { approvalId: "approval-je-1", label: "Journal entry approval", status: "Pending" }
      ],
      closeCalendar: [
        { label: "Lock period", dueDate: "2026-07-05" }
      ]
    }
  ],
  cashFlow: {
    totalCash: 0,
    totalLedgerCash: 0,
    netVariance: 0,
    totalFinancing: 0,
    runsWithCashSignals: 0,
    runsWithCashVariance: 0,
    tone: "success",
    summary: "No cash-flow signals."
  },
  reporting: {
    profileCount: 0,
    recommendedProfiles: [],
    profiles: [],
    templates: [
      { templateId: "trial-balance-pack", family: "Financial Statements", name: "Trial Balance Pack", version: "1.0", sections: ["summary"] }
    ],
    recentRuns: [
      {
        runId: "run-tb-1",
        templateId: "trial-balance-pack",
        reportName: "Trial Balance Pack",
        status: "Draft",
        actor: "controller",
        startedAtUtc: "2026-06-30T12:00:00Z",
        inputDatasets: ["ledger", "evidence"],
        parameters: {
          entityScope: "Fund Alpha",
          accountingBasis: "GAAP",
          outputFormat: "PDF"
        },
        validationWarnings: ["Open reconciliation break"],
        generatedFiles: ["trial-balance.pdf"],
        distributionRecipients: ["Controller"]
      }
    ],
    summary: "Reporting fixtures."
  }
} as unknown as AccountingWorkspaceResponse;

async function renderPage(node: ReactElement, route: string) {
  const result = renderWithRouter(node, { initialEntries: [route] });
  await waitForAsyncEffects();
  return result;
}

describe("finance standard pages", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    window.history.replaceState({}, "", "/");
    vi.mocked(api.getFinancialRecordExplorer).mockResolvedValue(createLedgerFinancialRecordExplorer());
    vi.mocked(api.saveFinancialRecordExplorerView).mockResolvedValue(
      createLedgerFinancialRecordExplorer().savedViews[0]
    );
  });

  it("renders the report preview and validation checkpoint tabs", async () => {
    await renderPage(<ReportPreviewValidationScreen data={data} />, "/reporting/preview");

    expect(screen.getByRole("heading", { name: "Report Preview & Validation" })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: "Preview" })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: "Validation Issues" })).toBeInTheDocument();
    expect(screen.getAllByText("Open reconciliation break")).toHaveLength(2);
    expect(screen.getByRole("button", { name: "Run report" })).toBeDisabled();
  });

  it("places the run action next to a ready report preview", async () => {
    const readyData = {
      ...data,
      reporting: {
        ...data.reporting,
        recentRuns: [{ ...data.reporting.recentRuns[0], validationWarnings: [] }]
      }
    } as unknown as AccountingWorkspaceResponse;

    await renderPage(<ReportPreviewValidationScreen data={readyData} />, "/reporting/preview");

    expect(screen.getByText("Ready to run")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Run report" })).toHaveAttribute(
      "href",
      "/reporting/run?templateId=trial-balance-pack%3A1.0"
    );
  });

  it("routes an unapproved preview to governance instead of advertising it as runnable", async () => {
    const draftTemplateData = {
      ...data,
      reporting: {
        ...data.reporting,
        templates: [{ ...data.reporting.templates[0], lifecycleStatus: "Draft" }],
        recentRuns: [{ ...data.reporting.recentRuns[0], validationWarnings: [] }]
      }
    } as unknown as AccountingWorkspaceResponse;

    await renderPage(<ReportPreviewValidationScreen data={draftTemplateData} />, "/reporting/preview?runId=run-tb-1");

    expect(screen.queryByRole("link", { name: "Run report" })).not.toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Review template" })).toHaveAttribute(
      "href",
      "/reporting/governance?templateId=trial-balance-pack%3A1.0"
    );
  });

  it("keeps a run-scoped preview on the requested run instead of the first recent run", async () => {
    const scopedData = {
      ...data,
      reporting: {
        ...data.reporting,
        recentRuns: [
          { ...data.reporting.recentRuns[0], runId: "run-first", reportName: "First recent report", validationWarnings: [] },
          { ...data.reporting.recentRuns[0], runId: "run-selected", reportName: "Requested report", validationWarnings: ["Requested-run warning"] }
        ]
      }
    } as unknown as AccountingWorkspaceResponse;

    await renderPage(<ReportPreviewValidationScreen data={scopedData} />, "/reporting/preview?runId=run-selected");

    expect(screen.getAllByText("Requested report").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Requested-run warning").length).toBeGreaterThan(0);
    expect(screen.queryByText("First recent report")).not.toBeInTheDocument();
  });

  it("routes an approval-pending preview to review and renders retained output evidence", async () => {
    const approvalData = {
      ...data,
      reporting: {
        ...data.reporting,
        recentRuns: [{
          ...data.reporting.recentRuns[0],
          status: "AwaitingApproval",
          asOfDate: null,
          sectionCount: 1,
          lineageLinkedSections: 1,
          validationWarnings: [],
          generatedFiles: [],
          artifacts: [
            "/api/fund-structure/report-packs/report-1/evidence-bundle",
            "publication-manifest:manifest-1"
          ]
        }]
      }
    } as unknown as AccountingWorkspaceResponse;

    await renderPage(<ReportPreviewValidationScreen data={approvalData} />, "/reporting/preview?runId=run-tb-1");

    expect(screen.getByRole("link", { name: "Review approval" })).toHaveAttribute(
      "href",
      "/reporting/run-status?runId=run-tb-1"
    );
    expect(screen.queryByRole("link", { name: "Review template" })).not.toBeInTheDocument();
    expect(screen.getByText("Template sections: Summary.")).toBeInTheDocument();
    expect(screen.getByText("1 of 1 sections carry retained lineage into approval review.")).toBeInTheDocument();
    expect(screen.getAllByText("Evidence bundle retained for approval review.").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Publication manifest retained for release control.").length).toBeGreaterThan(0);

    const confidence = screen.getByRole("region", { name: "Report preview confidence" });
    const freshness = within(confidence).getByText("Freshness").parentElement;
    expect(freshness).toHaveTextContent("Needs review");
    expect(freshness).toHaveTextContent("No as-of date retained");
  });

  it("does not claim run readiness when no report is selected", async () => {
    const emptyReportingData = {
      ...data,
      reporting: {
        ...data.reporting,
        recentRuns: [],
        templates: []
      }
    } as unknown as AccountingWorkspaceResponse;

    await renderPage(<ReportPreviewValidationScreen data={emptyReportingData} />, "/reporting/preview");

    expect(screen.getAllByText("No report selected").length).toBeGreaterThan(0);
    expect(screen.queryByRole("link", { name: "Run report" })).not.toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Choose report" })).toHaveAttribute("href", "/reporting/library");
  });

  it("does not substitute reporting-workspace fixtures when run detail has no runId", async () => {
    await renderPage(<ReportRunDetailScreen data={data} />, "/reporting/runs/detail");

    expect(screen.getByRole("alert")).toHaveTextContent("Select a governed report run");
    expect(screen.queryByText("Entity Scope: Fund Alpha")).not.toBeInTheDocument();
  });

  it("renders account detail with the standard trial-balance drill path fields", async () => {
    vi.mocked(api.getRunTrialBalance).mockResolvedValueOnce([{
      accountName: "Cash",
      accountType: "Asset",
      symbol: null,
      financialAccountId: "acct-cash",
      balance: 120500,
      entryCount: 3,
      security: null,
      entityScopeDisplayName: "Fund Alpha",
      sourceJournalEntryId: "je-cash-1",
      approvalIds: ["approval-je-1"]
    }]);

    await renderPage(<AccountDetailScreen data={data} />, "/accounting/accounts/detail?accountId=acct-cash");

    expect(screen.getByRole("heading", { name: "Account Detail" })).toBeInTheDocument();
    expect(await screen.findByText("$120,500.00")).toBeInTheDocument();
    const trustSummary = screen.getByRole("region", { name: "Data confidence" });
    expect(trustSummary).toHaveTextContent("Trial balance");
    expect(trustSummary).toHaveTextContent("Selected ledger run");
    expect(trustSummary).toHaveTextContent("Accounting basis missing");
    expect(trustSummary).toHaveTextContent("Needs review");
    expect(screen.getByRole("link", { name: "Open ledger activity" })).toHaveAttribute("href", "/accounting/ledger?runId=run-42");
    expect(screen.getByRole("link", { name: "Open source journal entry" })).toHaveAttribute("href", "/accounting/journal-entries/detail?journalEntryId=je-cash-1&runId=run-42");
    expect(screen.getByRole("link", { name: "Review approvals" })).toHaveAttribute("href", "/accounting/approvals?approvalId=approval-je-1");
  });

  it("does not present account freshness as current when no ledger run is available", async () => {
    const withoutRuns = {
      ...data,
      reconciliationQueue: []
    } as unknown as AccountingWorkspaceResponse;

    await renderPage(<AccountDetailScreen data={withoutRuns} />, "/accounting/accounts/detail?accountId=acct-cash");

    const trustSummary = screen.getByRole("region", { name: "Data confidence" });
    expect(trustSummary).toHaveTextContent("No run selected");
    expect(trustSummary).toHaveTextContent("No response loaded");
    expect(trustSummary).not.toHaveTextContent("Current response");
    expect(api.getRunTrialBalance).not.toHaveBeenCalled();
  });

  it("renders one shared ledger explorer with subordinate run-scoped journal links", async () => {
    const user = userEvent.setup();
    vi.mocked(api.getRunLedgerJournal).mockResolvedValueOnce([
      {
        journalEntryId: "je-cash-1",
        timestamp: "2026-06-30T00:00:00Z",
        description: "Cash sweep",
        totalDebits: 500,
        totalCredits: 500,
        lineCount: 2,
        accountScopeDisplayName: "Cash",
        entityScopeDisplayName: "Fund Alpha"
      }
    ]);

    await renderPage(<LedgerExplorerScreen data={data} />, "/accounting/ledger?runId=run-42");

    expect(screen.getByRole("heading", { name: "Ledger Explorer" })).toBeInTheDocument();
    expect(api.getFinancialRecordExplorer).toHaveBeenCalledWith("ledger");
    expect(await screen.findByRole("cell", { name: "Retained cash control" })).toBeInTheDocument();
    expect(screen.getByLabelText("Search Ledger Explorer")).toBeInTheDocument();
    expect(screen.queryByLabelText("Search by account, amount, journal ID, source, security, entity")).not.toBeInTheDocument();
    expect(screen.queryByRole("group", { name: "Saved ledger views" })).not.toBeInTheDocument();
    expect(screen.getByLabelText("Journal run context")).toBeInTheDocument();
    expect(await screen.findByRole("table", { name: "Run-scoped ledger journal results" })).toHaveTextContent("Open journal detail");
    expect(screen.getByRole("link", { name: "Open journal detail" })).toHaveAttribute(
      "href",
      "/accounting/journal-entries/detail?journalEntryId=je-cash-1&runId=run-42"
    );

    await user.clear(screen.getByLabelText("Search Ledger Explorer"));
    await user.type(screen.getByLabelText("Search Ledger Explorer"), "missing record");
    expect(screen.getByRole("status")).toHaveTextContent("No source-backed records are available for this explorer.");
    expect(screen.getByRole("table", { name: "Run-scoped ledger journal results" })).toHaveTextContent("Open journal detail");
  });

  it("persists a named ledger explorer view and refetches the shared explorer", async () => {
    const user = userEvent.setup();
    const initialExplorer = createLedgerFinancialRecordExplorer();
    const refreshedExplorer = createLedgerFinancialRecordExplorer({
      savedViews: [
        ...initialExplorer.savedViews,
        {
          viewId: "cash-review",
          label: "Cash review",
          description: "Search: cash",
          isSystem: false,
          isActive: true,
          filters: [],
          searchText: "cash"
        }
      ]
    });
    vi.mocked(api.getFinancialRecordExplorer)
      .mockResolvedValueOnce(initialExplorer)
      .mockResolvedValueOnce(refreshedExplorer);
    vi.mocked(api.saveFinancialRecordExplorerView).mockResolvedValueOnce(refreshedExplorer.savedViews[1]);
    vi.mocked(api.getRunLedgerJournal).mockResolvedValueOnce([]);

    await renderPage(<LedgerExplorerScreen data={data} />, "/accounting/ledger?runId=run-42");

    await user.type(await screen.findByLabelText("Search Ledger Explorer"), "cash");
    await user.type(screen.getByLabelText("Saved view name"), "Cash review");
    await user.click(screen.getByRole("button", { name: "Save view" }));

    await waitFor(() => {
      expect(api.saveFinancialRecordExplorerView).toHaveBeenCalledWith(
        "ledger",
        expect.objectContaining({ label: "Cash review", searchText: "cash" })
      );
      expect(api.getFinancialRecordExplorer).toHaveBeenCalledTimes(2);
    });
    expect(await screen.findByRole("button", { name: "Cash review" })).toBeInTheDocument();
  });

  it("preserves a query-selected run that is absent from the reconciliation queue", async () => {
    const runScopedData = {
      ...data,
      reconciliationQueue: []
    } as unknown as AccountingWorkspaceResponse;
    vi.mocked(api.getRunLedgerJournal).mockResolvedValueOnce([{
      journalEntryId: "je-retained-run",
      timestamp: "2026-06-30T00:00:00Z",
      description: "Retained deep-link journal",
      totalDebits: 25,
      totalCredits: 25,
      lineCount: 2,
      accountScopeDisplayName: "Cash",
      entityScopeDisplayName: "Fund Alpha"
    }]);
    window.history.replaceState({}, "", "/accounting/ledger?runId=retained-run");

    await renderPage(<LedgerExplorerScreen data={runScopedData} />, "/accounting/ledger?runId=retained-run");

    expect(screen.getByLabelText("Journal run context")).toHaveValue("retained-run");
    expect(api.getRunLedgerJournal).toHaveBeenCalledWith("retained-run");
    expect(await screen.findByRole("link", { name: "Open journal detail" })).toHaveAttribute(
      "href",
      "/accounting/journal-entries/detail?journalEntryId=je-retained-run&runId=retained-run"
    );
    expect(new URLSearchParams(window.location.search).get("runId")).toBe("retained-run");
  });

  it("keeps the shared ledger explorer useful when no reconciliation runs exist", async () => {
    const emptyQueueData = {
      ...data,
      reconciliationQueue: []
    } as unknown as AccountingWorkspaceResponse;

    await renderPage(<LedgerExplorerScreen data={emptyQueueData} />, "/accounting/ledger");

    expect(api.getFinancialRecordExplorer).toHaveBeenCalledWith("ledger");
    expect(api.getRunLedgerJournal).not.toHaveBeenCalled();
    expect(await screen.findByRole("cell", { name: "Retained cash control" })).toBeInTheDocument();
    expect(screen.getByLabelText("Journal run context")).toHaveValue("");
    expect(screen.getByRole("option", { name: "No run available" })).toBeInTheDocument();
    expect(screen.getByRole("table", { name: "Run-scoped ledger journal results" })).toHaveTextContent(
      "No journal evidence rows are available for the selected run."
    );
  });

  it("renders reconciliation match workbench as a focused clearing queue", async () => {
    await renderPage(<ReconciliationMatchWorkbenchScreen data={data} />, "/accounting/reconciliation/match");

    expect(screen.getByRole("heading", { name: "Reconciliation Match Workbench" })).toBeInTheDocument();
    expect(screen.getAllByText("Cash variance needs review").length).toBeGreaterThan(0);
    expect(screen.getByRole("link", { name: "Open reconciliation casework" })).toHaveAttribute("href", "/accounting/reconciliation");
    expect(screen.getByRole("button", { name: "Record match decision" })).toBeDisabled();
  });

  it("renders close calendar tasks", async () => {
    vi.mocked(api.getOperationsCloseCalendar).mockResolvedValueOnce({
      generatedAtUtc: "2026-06-30T02:00:00Z",
      items: [{
        workflowId: "workflow-close-1",
        fundAccountId: "fund-alpha",
        periodId: "2026-06",
        status: "LedgerPostingDraft",
        version: 1,
        nextDueDate: "2026-07-03",
        nextDueTaskId: "trial-balance",
        nextDueLabel: "Run trial balance",
        nextDueOwner: "Controller",
        readinessSeverity: "Warning",
        readinessScore: 68,
        isReadyToClose: false,
        blockerCount: 1,
        openChecklistCount: 2,
        requiredApprovalCount: 2,
        completedApprovalCount: 1,
        route: "/accounting/operations-continuity"
      }]
    });

    await renderPage(<CloseCalendarScreen data={data} />, "/accounting/close-calendar");

    expect(screen.getByRole("heading", { name: "Close Calendar" })).toBeInTheDocument();
    expect(await screen.findByText(/June 2026: Run trial balance · Ledger Posting Draft · owner Controller · due Jul 3, 2026/)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Open close workflow for June 2026" })).toHaveAttribute("href", "/accounting/operations-continuity");
    expect(screen.getByRole("link", { name: "Open Operations Continuity" })).toHaveAttribute("href", "/accounting/operations-continuity");
  });

  it("renders approval inbox rows with per-approval decision links", async () => {
    await renderPage(<ApprovalInboxScreen data={data} />, "/accounting/approvals/inbox");

    expect(screen.getByRole("heading", { name: "Approval Inbox" })).toBeInTheDocument();
    expect(screen.getByText("Journal entry approval")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Review and decide Journal entry approval" })).toHaveAttribute(
      "href",
      "/accounting/approvals?approvalId=approval-je-1"
    );
    expect(screen.getByText("What evidence supports it?")).toBeInTheDocument();
  });

  it("renders evidence detail with the non-approval rule visible", async () => {
    await renderPage(<EvidenceDetailScreen />, "/accounting/evidence/detail?evidenceId=bank-statement");

    expect(screen.getByRole("heading", { name: "Evidence Detail" })).toBeInTheDocument();
    expect(screen.getByText("bank-statement")).toBeInTheDocument();
    expect(screen.getByText(/does not approve, post, or release work/i)).toBeInTheDocument();
    expect(screen.getByText("Evidence reference ready for inspection")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Inspect selected evidence" })).toHaveAttribute("href", "/reporting/evidence?subjectKind=evidence&subjectId=bank-statement");
  });
});

function createLedgerFinancialRecordExplorer(
  overrides: Partial<FinancialRecordExplorerDto> = {}
): FinancialRecordExplorerDto {
  const detail = {
    recordId: "ledger:run-42:cash",
    recordType: "Ledger account",
    title: "Retained cash control",
    subtitle: "Assets · run-42",
    description: "Source-backed cash balance with retained journal and evidence links.",
    tone: "Success" as const,
    fields: [
      { label: "Balance", value: "$120,500.00", detail: "Retained trial-balance value.", tone: "Success" as const }
    ],
    proofActions: [
      {
        actionId: "open-journal",
        label: "Open journal evidence",
        description: "Open retained run journal evidence.",
        href: "/api/workstation/runs/run-42/ledger/journal",
        isEnabled: true,
        disabledReason: "",
        tone: "Info" as const
      }
    ],
    usedIn: [],
    impacts: [],
    fullRecordHref: "/api/workstation/runs/run-42/ledger/trial-balance"
  };

  return {
    explorerId: "ledger",
    title: "Ledger Explorer",
    description: "Explore retained ledger records and their proof links.",
    sourceState: "Source-backed ledger projection from retained accounting records.",
    isBlocked: false,
    blockedReason: "",
    scopeItems: [
      { label: "Record set", value: "Ledger trial balance", tone: "Info" }
    ],
    savedViews: [
      {
        viewId: "system-ledger-default",
        label: "Controller review",
        description: "Default retained-ledger review.",
        isSystem: true,
        isActive: true,
        filters: [],
        searchText: ""
      }
    ],
    summaryItems: [
      { label: "Retained records", value: "1", detail: "One source-backed ledger record.", tone: "Success" }
    ],
    filters: [],
    columns: [
      { columnId: "account", header: "Account", cellKind: "text", width: 220, isRightAligned: false },
      { columnId: "balance", header: "Balance", cellKind: "currency", width: 120, isRightAligned: true }
    ],
    rows: [
      {
        recordId: detail.recordId,
        recordType: "ledger",
        label: detail.title,
        source: "Trial balance",
        status: "Posted",
        tone: "Success",
        cells: [
          { columnId: "account", displayValue: detail.title, rawValue: "Cash", tone: "Success", linkHref: "" },
          { columnId: "balance", displayValue: "$120,500.00", rawValue: "120500", tone: "Success", linkHref: "" }
        ],
        detail
      }
    ],
    selectedRecord: detail,
    proofActions: [],
    recordGraph: { nodes: [], edges: [] },
    ...overrides
  };
}
