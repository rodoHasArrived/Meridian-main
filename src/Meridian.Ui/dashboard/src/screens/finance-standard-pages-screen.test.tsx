import { screen } from "@testing-library/react";
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
import type { AccountingWorkspaceResponse } from "@/types";

vi.mock("@/lib/api", async () => {
  const actual = await vi.importActual<typeof import("@/lib/api")>("@/lib/api");
  return {
    ...actual,
    getRunLedgerJournal: vi.fn()
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
      label: "Cash variance",
      status: "Open"
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
        reportName: "Trial Balance Pack",
        status: "Draft",
        actor: "controller",
        startedAtUtc: "2026-06-30T12:00:00Z",
        inputDatasets: ["ledger", "evidence"],
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
    await renderPage(<ReportPreviewValidationScreen data={data} />, "/reporting/preview");

    expect(screen.getByRole("heading", { name: "Report Preview & Validation" })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: "Preview" })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: "Validation Issues" })).toBeInTheDocument();
    expect(screen.getAllByText("Open reconciliation break")).toHaveLength(2);
  });

  it("renders report run detail with clone and preview actions", async () => {
    await renderPage(<ReportRunDetailScreen data={data} />, "/reporting/runs/detail?runId=run-tb-1");

    expect(screen.getByRole("heading", { name: "Report Run Detail" })).toBeInTheDocument();
    expect(screen.getByText("run-tb-1")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Clone parameters" })).toHaveAttribute(
      "href",
      "/reporting/run?cloneRunId=run-tb-1"
    );
    expect(screen.getByRole("link", { name: "Open preview" })).toHaveAttribute("href", "/reporting/preview");
  });

  it("renders account detail with the standard trial-balance drill path fields", async () => {
    await renderPage(<AccountDetailScreen data={data} />, "/accounting/accounts/detail?accountId=acct-cash");

    expect(screen.getByRole("heading", { name: "Account Detail" })).toBeInTheDocument();
    expect(screen.getByText("Cash")).toBeInTheDocument();
    expect(screen.getByText("Related journal entries")).toBeInTheDocument();
    expect(screen.getByText("Report lines using this account")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Open ledger activity" })).toHaveAttribute("href", "/accounting/ledger");
    expect(screen.getByRole("link", { name: "Review evidence detail" })).toHaveAttribute("href", "/accounting/evidence/detail");
  });

  it("renders ledger explorer search, saved views, and journal drill links", async () => {
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
    expect(screen.getByLabelText("Search by account, amount, journal ID, source, security, entity")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Manual JEs" })).toBeInTheDocument();
    expect(await screen.findByRole("table", { name: "Ledger Explorer results" })).toHaveTextContent("je-cash-1");
    expect(screen.getByRole("link", { name: "je-cash-1" })).toHaveAttribute(
      "href",
      "/accounting/journal-entries/detail?journalEntryId=je-cash-1&runId=run-42"
    );
  });

  it("renders reconciliation match workbench as a focused clearing queue", async () => {
    await renderPage(<ReconciliationMatchWorkbenchScreen data={data} />, "/accounting/reconciliation/match");

    expect(screen.getByRole("heading", { name: "Reconciliation Match Workbench" })).toBeInTheDocument();
    expect(screen.getByText("Cash variance")).toBeInTheDocument();
    expect(screen.getByText("Source statement / provider records")).toBeInTheDocument();
    expect(screen.getByText("Suggested matches")).toBeInTheDocument();
    expect(screen.getByText("Ledger records")).toBeInTheDocument();
  });

  it("renders close calendar tasks", async () => {
    await renderPage(<CloseCalendarScreen data={data} />, "/accounting/close-calendar");

    expect(screen.getByRole("heading", { name: "Close Calendar" })).toBeInTheDocument();
    expect(screen.getByText("Run trial balance - Pending - owner Controller - due TBD")).toBeInTheDocument();
    expect(screen.getByText("Controller approval - Blocked - owner Controller - due TBD")).toBeInTheDocument();
    expect(screen.getByText("Required evidence and sign-off state")).toBeInTheDocument();
  });

  it("renders approval inbox review prompts", async () => {
    await renderPage(<ApprovalInboxScreen data={data} />, "/accounting/approvals/inbox");

    expect(screen.getByRole("heading", { name: "Approval Inbox" })).toBeInTheDocument();
    expect(screen.getByText("Journal entry approval: Pending")).toBeInTheDocument();
    expect(screen.getByText("What evidence supports it?")).toBeInTheDocument();
  });

  it("renders evidence detail with the non-approval rule visible", async () => {
    await renderPage(<EvidenceDetailScreen />, "/accounting/evidence/detail?evidenceId=bank-statement");

    expect(screen.getByRole("heading", { name: "Evidence Detail" })).toBeInTheDocument();
    expect(screen.getByText("bank-statement")).toBeInTheDocument();
    expect(screen.getByText(/does not approve, post, or release work/i)).toBeInTheDocument();
  });
});
