import { screen, within } from "@testing-library/react";
import type { ReactElement } from "react";
import * as api from "@/lib/api";
import * as ledgerReportsApi from "@/lib/ledger-reports-api";
import {
  AccountDetailScreen,
  ApprovalInboxScreen,
  CloseCalendarScreen,
  LedgerExplorerScreen,
  ReconciliationMatchWorkbenchScreen,
  ReportPreviewValidationScreen,
  ReportRunDetailScreen
} from "@/screens/finance-standard-pages-screen";
import { requireFirst } from "@/test/fixtures";
import { renderWithRouter, waitForAsyncEffects } from "@/test/render";
import type { AccountingWorkspaceResponse } from "@/types";

vi.mock("@/lib/api", async () => {
  const actual = await vi.importActual<typeof import("@/lib/api")>("@/lib/api");
  return {
    ...actual,
    getOperationsCloseCalendar: vi.fn(),
    getRunLedgerJournal: vi.fn(),
    getRunTrialBalance: vi.fn()
  };
});

vi.mock("@/lib/ledger-reports-api", () => ({
  getLedgerBooks: vi.fn(),
  getLedgerPeriods: vi.fn(),
  getLedgerPeriodTrialBalance: vi.fn(),
  getLedgerPeriodPnlSummary: vi.fn(),
  getLedgerPeriodJournalEntries: vi.fn()
}));

const LEDGER_BOOK_ID = "00000000-0000-0000-0000-0000000000aa";
const LEDGER_PERIOD_ID = "11111111-1111-1111-1111-111111111111";

function mockPostedBook() {
  vi.mocked(ledgerReportsApi.getLedgerBooks).mockResolvedValue([{
    ledgerBookId: LEDGER_BOOK_ID,
    fundProfileId: "fund-alpha",
    fundStructureNodeId: "00000000-0000-0000-0000-0000000000bb",
    fundStructureNodeKind: "Fund",
    displayName: "Master Fund",
    baseCurrency: "USD",
    createdAt: "2026-01-01T00:00:00Z",
    updatedAt: "2026-01-01T00:00:00Z",
    accountingBasis: "Primary",
    accountingPolicyId: "legacy-v1",
    accountingPolicyVersion: "legacy-v1"
  }] as never);
  vi.mocked(ledgerReportsApi.getLedgerPeriods).mockResolvedValue([{
    periodId: LEDGER_PERIOD_ID,
    ledgerBookId: LEDGER_BOOK_ID,
    fiscalYear: 2026,
    periodNo: 7,
    label: "July 2026",
    startDate: "2026-07-01",
    endDate: "2026-07-31",
    status: "HardClosed",
    openedAt: "2026-07-01T00:00:00Z",
    closedAt: "2026-08-02T00:00:00Z",
    version: 1
  }] as never);
  vi.mocked(ledgerReportsApi.getLedgerPeriodTrialBalance).mockResolvedValue([] as never);
  vi.mocked(ledgerReportsApi.getLedgerPeriodPnlSummary).mockRejectedValue(new Error("not configured"));
  vi.mocked(ledgerReportsApi.getLedgerPeriodJournalEntries).mockResolvedValue([] as never);
}

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
  });

  it("renders the report preview and validation checkpoint tabs", async () => {
    await renderPage(<ReportPreviewValidationScreen data={data.reporting} />, "/reporting/preview");

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
        recentRuns: [{ ...requireFirst(data.reporting.recentRuns, "reporting.recentRuns"), validationWarnings: [] }]
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
        templates: [{ ...requireFirst(data.reporting.templates, "reporting.templates"), lifecycleStatus: "Draft" }],
        recentRuns: [{ ...requireFirst(data.reporting.recentRuns, "reporting.recentRuns"), validationWarnings: [] }]
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
          { ...requireFirst(data.reporting.recentRuns, "reporting.recentRuns"), runId: "run-first", reportName: "First recent report", validationWarnings: [] },
          { ...requireFirst(data.reporting.recentRuns, "reporting.recentRuns"), runId: "run-selected", reportName: "Requested report", validationWarnings: ["Requested-run warning"] }
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
          ...requireFirst(data.reporting.recentRuns, "reporting.recentRuns"),
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
    expect(screen.getByRole("link", { name: "Open run ledger activity" })).toHaveAttribute("href", "/strategy/run-ledger?runId=run-42");
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

  it("reads Accounting's ledger from the posted journal, not from a strategy run", async () => {
    // This is the surface the workspace links to as "Validate the ledger". It used to load
    // getRunLedgerJournal for whichever run headed the reconciliation queue -- a simulation
    // artifact under the name an operator reads as the book of record.
    mockPostedBook();
    vi.mocked(ledgerReportsApi.getLedgerPeriodJournalEntries).mockResolvedValue([
      {
        journalEntryId: "je-cash-1",
        periodId: LEDGER_PERIOD_ID,
        ledgerBookId: LEDGER_BOOK_ID,
        timestamp: "2026-07-31T00:00:00Z",
        description: "Cash sweep",
        totalDebits: 500,
        totalCredits: 500,
        isBalanced: true,
        lines: []
      }
    ] as never);

    await renderPage(<LedgerExplorerScreen data={data} />, "/accounting/ledger");

    expect(screen.getByRole("heading", { name: "Ledger Explorer" })).toBeInTheDocument();
    expect(api.getRunLedgerJournal).not.toHaveBeenCalled();
    expect(ledgerReportsApi.getLedgerPeriodJournalEntries).toHaveBeenCalledWith(LEDGER_PERIOD_ID);

    // Scoped by ledger book and period, the posted book's own coordinates -- not by run.
    expect(screen.getByLabelText("Ledger book")).toBeInTheDocument();
    expect(screen.getByLabelText("Ledger period")).toBeInTheDocument();
    expect(screen.queryByLabelText("Run / period")).not.toBeInTheDocument();

    // The drill-through carries the period, which is how the detail screen resolves a posted
    // entry. It used to carry a runId, which no posted entry has.
    expect(await screen.findByRole("table", { name: "Ledger Explorer results" })).toHaveTextContent("Open journal detail");
    expect(screen.getByRole("link", { name: "Open journal detail" })).toHaveAttribute(
      "href",
      `/accounting/journal-entries/detail?journalEntryId=je-cash-1&periodId=${LEDGER_PERIOD_ID}`
    );
  });

  it("offers no ledger filter it does not apply", async () => {
    // "Unposted", "Reversals", "Manual JEs" and "System Generated" changed a label and nothing
    // else, and the results header reported the unfiltered count as that view's. On a governed
    // book of record a control that appears to filter and does not is a misstatement.
    mockPostedBook();

    await renderPage(<LedgerExplorerScreen data={data} />, "/accounting/ledger");

    expect(screen.queryByRole("button", { name: "Manual JEs" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Unposted" })).not.toBeInTheDocument();
    // Search does filter, so it stays.
    expect(screen.getByLabelText("Search by account, amount, journal ID, source, security, entity")).toHaveAttribute(
      "placeholder",
      "Cash, $120,500, AAPL, cash sweep"
    );
  });

  it("switches the ledger explorer to the trial balance tab via the view search param", async () => {
    mockPostedBook();
    vi.mocked(api.getRunLedgerJournal).mockResolvedValue([]);
    vi.mocked(api.getRunTrialBalance).mockResolvedValue([]);

    const tabData = {
      ...data,
      reconciliationQueue: [
        {
          runId: "run-42",
          strategyName: "Paper Index Mean Reversion",
          mode: "paper",
          status: "Running",
          lastUpdated: "3m ago",
          breakCount: 1,
          openBreakCount: 1,
          reconciliationStatus: "BreaksOpen"
        }
      ]
    } as unknown as AccountingWorkspaceResponse;

    await renderPage(<LedgerExplorerScreen data={tabData} />, "/accounting/ledger?view=trial-balance&runId=run-42");

    expect(screen.getByRole("tab", { name: "Trial balance" })).toHaveAttribute("aria-selected", "true");
    expect(screen.getByRole("tab", { name: "Ledger" })).toHaveAttribute("aria-selected", "false");
    expect(screen.queryByLabelText("Search by account, amount, journal ID, source, security, entity")).not.toBeInTheDocument();
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

});
