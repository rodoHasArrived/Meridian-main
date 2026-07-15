import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import * as api from "@/lib/api";
import { ReportRunParametersScreen } from "@/screens/report-run-parameters-screen";
import { renderWithRouter, TestMemoryRouter, waitForAsyncEffects } from "@/test/render";
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

  it("offers a template picker when no templateId is provided", async () => {
    await renderScreen("/reporting/run");

    expect(screen.getByRole("combobox", { name: "Choose a report template to run" })).toBeInTheDocument();
    expect(screen.getAllByText(/Trial Balance Pack/).length).toBeGreaterThanOrEqual(2);
    expect(screen.getByRole("region", { name: "Report run setup scan band" })).toHaveTextContent("Templates");
    expect(screen.getByRole("region", { name: "Recommended report templates" })).toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Saved report run views" })).toHaveTextContent("Monthly close package");
    expect(screen.getByRole("button", { name: "Configure" })).toBeInTheDocument();
    expect(screen.getByRole("complementary", { name: "Report run setup context" })).toBeInTheDocument();
    const nextActions = screen.getByRole("list", { name: "Report run next actions" });
    expect(nextActions).toHaveTextContent("Review breaks");
    expect(nextActions).toHaveTextContent("1 open reconciliation break(s)");
    const readinessCues = screen.getByRole("list", { name: "Report run readiness cues" });
    expect(readinessCues).toHaveTextContent("Open reconciliation breaks");
    expect(readinessCues).toHaveTextContent("1");
    expect(screen.getByText("No recent report runs are loaded. Select a template to prepare the first run.")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Or browse the Report Library" })).toHaveAttribute("href", "/reporting/library");
  });

  it("presents recent run names and review states without raw slugs", async () => {
    const reviewData: AccountingWorkspaceResponse = {
      ...reportingData,
      reporting: {
        ...reportingData.reporting,
        recentRuns: [{
          runId: "run-tb-review",
          templateId: "trial-balance-pack",
          family: "Financial Statements",
          status: "AwaitingApproval",
          trigger: "Manual",
          asOfDate: null,
          attemptCount: 1,
          sectionCount: 1,
          lineageLinkedSections: 1,
          artifacts: [],
          auditActions: [],
          failureReason: null,
          drilldownLinks: [],
          nextActions: []
        }]
      }
    };

    renderWithRouter(
      <ReportRunParametersScreen data={reviewData} accounting={accountingData} />,
      { initialEntries: ["/reporting/run"] }
    );
    await waitForAsyncEffects();

    const recentRuns = screen.getByRole("list", { name: "Recent report runs" });
    expect(recentRuns).toHaveTextContent("Trial Balance Pack");
    expect(recentRuns).toHaveTextContent("Awaiting approval · No as-of date retained");
    expect(recentRuns).not.toHaveTextContent("trial-balance-pack");
  });

  it("has no basic accessibility violations in the initial run setup state", async () => {
    const { container } = await renderScreen("/reporting/run");

    expect(screen.getByRole("complementary", { name: "Report run setup context" })).toBeInTheDocument();
    const results = await axe(container);
    expect(results.violations).toHaveLength(0);
  });

  it("keeps run setup focus order on template selection, library browse, and configure", async () => {
    const user = userEvent.setup();

    await renderScreen("/reporting/run");

    await user.tab();
    expect(screen.getByRole("combobox", { name: "Choose a report template to run" })).toHaveFocus();

    await user.tab();
    expect(screen.getByRole("link", { name: "Or browse the Report Library" })).toHaveFocus();

    await user.tab();
    expect(screen.getByRole("button", { name: "Configure" })).toHaveFocus();
  });

  it("navigates to the selected template's parameters when picked from the picker", async () => {
    const user = userEvent.setup();

    await renderScreen("/reporting/run");

    await user.selectOptions(
      screen.getByRole("combobox", { name: "Choose a report template to run" }),
      "trial-balance-pack:1.0"
    );

    expect(await screen.findByRole("region", { name: "Report run form" })).toHaveTextContent("Trial Balance Pack");
  });

  it("runs the template selected inside the active parameters form", async () => {
    const user = userEvent.setup();
    const selectableData: AccountingWorkspaceResponse = {
      ...reportingData,
      reporting: {
        ...reportingData.reporting,
        templates: [
          { ...reportingData.reporting.templates![0], lifecycleStatus: "Approved" },
          {
            templateId: "holdings-pack",
            family: "Holdings",
            name: "Holdings Pack",
            version: "2.0",
            sections: ["holdings"],
            lifecycleStatus: "Approved"
          }
        ]
      }
    };
    vi.mocked(api.runReportingNow).mockResolvedValue({
      run: {
        runId: "run-holdings-1",
        templateId: "holdings-pack",
        family: "Holdings",
        status: "Queued",
        trigger: "Manual",
        asOfDate: "2026-06-30",
        attemptCount: 1,
        sectionCount: 1,
        lineageLinkedSections: 0,
        artifacts: [],
        auditActions: [],
        failureReason: null,
        drilldownLinks: [],
        nextActions: []
      }
    });

    renderWithRouter(
      <ReportRunParametersScreen data={selectableData} accounting={accountingData} />,
      { initialEntries: ["/reporting/run?templateId=trial-balance-pack%3A1.0"] }
    );
    await waitForAsyncEffects();

    await user.selectOptions(screen.getByLabelText("Report run template"), "holdings-pack:2.0");
    expect(screen.getByLabelText("Report run template")).toHaveValue("holdings-pack:2.0");
    await user.click(screen.getByRole("button", { name: "Run Holdings Pack" }));

    await waitFor(() => expect(api.runReportingNow).toHaveBeenCalledWith(
      expect.objectContaining({ templateId: "holdings-pack" })
    ));
  });

  it("renders a template-not-found state for an unknown templateId", async () => {
    await renderScreen("/reporting/run?templateId=unknown-template%3A1.0");

    expect(await screen.findByText("Template not found")).toBeInTheDocument();
  });

  it("hydrates cloned run context into the governed run form", async () => {
    const cloneData: AccountingWorkspaceResponse = {
      ...reportingData,
      reporting: {
        ...reportingData.reporting,
        recentRuns: [{
          runId: "run-tb-released",
          templateId: "trial-balance-pack",
          family: "Financial Statements",
          status: "Released",
          trigger: "Manual",
          asOfDate: "2026-05-31",
          attemptCount: 1,
          sectionCount: 1,
          lineageLinkedSections: 1,
          artifacts: [],
          auditActions: [],
          failureReason: null,
          drilldownLinks: [],
          nextActions: []
        }]
      }
    };

    const view = renderWithRouter(
      <ReportRunParametersScreen data={null} accounting={accountingData} />,
      { initialEntries: ["/reporting/run?cloneRunId=run-tb-released"] }
    );
    expect(screen.getByText("Loading report parameters")).toBeInTheDocument();

    view.rerender(
      <TestMemoryRouter initialEntries={["/reporting/run?cloneRunId=run-tb-released"]}>
        <ReportRunParametersScreen data={cloneData} accounting={accountingData} />
      </TestMemoryRouter>
    );
    await waitForAsyncEffects();

    expect(screen.getByRole("region", { name: "Report run form" })).toHaveTextContent("Trial Balance Pack");
    expect(screen.getByLabelText("Report run template")).toHaveValue("trial-balance-pack:1.0");
    expect(screen.getByLabelText("Report run as-of date")).toHaveValue("2026-05-31");
  });

  it("renders the readiness gate with open breaks and the governed run form for a known template", async () => {
    await renderScreen("/reporting/run?templateId=trial-balance-pack%3A1.0");

    expect(screen.getByRole("heading", { name: "Report Parameters" })).toBeInTheDocument();
    expect(screen.getByLabelText("Entity / fund / portfolio")).toHaveValue("All entities");
    expect(screen.getByLabelText("Ledger book")).toHaveValue("Primary GL");
    expect(screen.getByLabelText("Accounting basis")).toHaveValue("GAAP");
    expect(screen.getByLabelText("Output format")).toHaveValue("PDF");
    expect(screen.getByLabelText("Include supporting schedules")).toBeChecked();
    expect(screen.getByLabelText("Include evidence appendix")).toBeChecked();
    expect(screen.getByRole("heading", { name: "Can this report run?" })).toBeInTheDocument();
    expect(await screen.findByText("Warnings present")).toBeInTheDocument();
    expect(screen.getByText("Open reconciliation breaks")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Review breaks" })).toHaveAttribute("href", "/accounting/reconciliation");
    expect(screen.getByRole("region", { name: "Report run form" })).toHaveTextContent("Trial Balance Pack");
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

    await user.click(await screen.findByRole("button", { name: "Run Trial Balance Pack" }));

    expect(await screen.findByText("Trial Balance Pack run created.")).toBeInTheDocument();
    const status = screen.getByRole("status", { name: "Report run status" });
    expect(status).toHaveTextContent("Run retained for audit review");
    const runReference = within(status).getByText("Run reference").closest("details");
    expect(runReference).not.toBeNull();
    expect(runReference).not.toHaveAttribute("open");
    expect(within(runReference as HTMLElement).getByText("run-42")).toBeInTheDocument();
    expect(api.runReportingNow).toHaveBeenCalledWith(
      expect.objectContaining({ templateId: "trial-balance-pack" })
    );
  });
});
