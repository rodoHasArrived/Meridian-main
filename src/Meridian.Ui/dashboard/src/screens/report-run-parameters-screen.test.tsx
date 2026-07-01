import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import * as api from "@/lib/api";
import { ReportRunParametersScreen } from "@/screens/report-run-parameters-screen";
import { renderWithRouter, waitForAsyncEffects } from "@/test/render";
import type { AccountingWorkspaceResponse, ManualJournalEntryWorkbench, ReportingRunResult } from "@/types";

vi.mock("@/lib/api", async () => {
  const actual = await vi.importActual<typeof import("@/lib/api")>("@/lib/api");
  return {
    ...actual,
    getManualJournalEntryWorkbench: vi.fn(),
    runReportingNow: vi.fn()
  };
});

const reportingData: AccountingWorkspaceResponse = {
  metrics: [],
  reconciliationQueue: [],
  breakQueue: [],
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
    summary: "No reporting profiles.",
    templates: [
      { templateId: "trial-balance-pack", family: "Financial Statements", name: "Trial Balance Pack", version: "1.0", sections: ["summary"] }
    ],
    recentRuns: []
  }
};

const accountingData: AccountingWorkspaceResponse = {
  ...reportingData,
  reconciliationQueue: [
    {
      runId: "run-1",
      strategyName: "Alpha",
      mode: "paper",
      status: "Running",
      lastUpdated: "3m ago",
      breakCount: 1,
      openBreakCount: 1,
      reconciliationStatus: "BreaksOpen"
    }
  ]
};

const emptyWorkbench: ManualJournalEntryWorkbench = {
  fundProfileId: "fund-alpha",
  loadedAtUtc: "2026-06-30T00:00:00Z",
  ledgerBooks: [],
  chartOfAccounts: [],
  drafts: [],
  auditTrail: []
};

async function renderScreen(initialEntry: string) {
  const result = renderWithRouter(
    <ReportRunParametersScreen data={reportingData} accounting={accountingData} />,
    { initialEntries: [initialEntry] }
  );
  await waitForAsyncEffects();
  return result;
}

describe("ReportRunParametersScreen", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(api.getManualJournalEntryWorkbench).mockResolvedValue(emptyWorkbench);
  });

  it("prompts to open from the Report Library when no templateId is provided", async () => {
    await renderScreen("/reporting/run");

    expect(screen.getByText(/Open this page from the Report Library/)).toBeInTheDocument();
  });

  it("renders a template-not-found state for an unknown templateId", async () => {
    await renderScreen("/reporting/run?templateId=unknown-template%3A1.0");

    expect(await screen.findByText("Template not found")).toBeInTheDocument();
  });

  it("renders the readiness gate with open breaks and the exports runner for a known template", async () => {
    await renderScreen("/reporting/run?templateId=trial-balance-pack%3A1.0");

    expect(await screen.findByText("Warnings present")).toBeInTheDocument();
    expect(screen.getByText("Open reconciliation breaks")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Review breaks" })).toHaveAttribute("href", "/accounting/reconciliation");
    expect(screen.getByRole("region", { name: "Exports report runner" })).toHaveTextContent("Trial Balance Pack");
  });

  it("submits the run and shows the result via the shared status view", async () => {
    const runResult: ReportingRunResult = {
      run: {
        runId: "run-42",
        status: "Queued",
        trigger: "Manual",
        templateId: "trial-balance-pack",
        family: "Financial Statements",
        attemptCount: 1,
        sectionCount: 1,
        lineageLinkedSections: 0,
        artifacts: [],
        auditActions: [],
        failureReason: null
      }
    };
    vi.mocked(api.runReportingNow).mockResolvedValueOnce(runResult);
    const user = userEvent.setup();

    await renderScreen("/reporting/run?templateId=trial-balance-pack%3A1.0");

    await user.click(await screen.findByRole("button", { name: "Run Trial Balance Pack from Exports" }));

    expect(await screen.findByText("Trial Balance Pack run created.")).toBeInTheDocument();
    expect(api.runReportingNow).toHaveBeenCalledWith(
      expect.objectContaining({ templateId: "trial-balance-pack" })
    );
  });
});
