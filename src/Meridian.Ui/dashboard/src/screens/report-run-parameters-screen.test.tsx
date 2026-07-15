import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { useLocation } from "react-router-dom";
import * as api from "@/lib/api";
import { ReportRunParametersScreen } from "@/screens/report-run-parameters-screen";
import { renderWithRouter, TestMemoryRouter, waitForAsyncEffects } from "@/test/render";
import type {
  AccountingWorkspaceResponse,
  ManualJournalEntryWorkbench,
  ReportingRunReadiness,
  ReportingRunResult
} from "@/types";

vi.mock("@/lib/api", async () => {
  const actual = await vi.importActual<typeof import("@/lib/api")>("@/lib/api");
  return {
    ...actual,
    assessReportingRunReadiness: vi.fn(),
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
    fundProfileId: "fund-alpha",
    selectedFundProfileId: "fund-alpha",
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

const readyReadiness: ReportingRunReadiness = {
  evaluationId: "report-readiness-ready",
  evaluatedAtUtc: "2026-06-30T20:00:00Z",
  resolvedTemplate: { name: "trial-balance-pack", version: 1 },
  resolvedParameters: {
    scope: {
      fundProfileId: "fund-alpha",
      entityScopeKind: "AllEntities",
      entityId: null,
      portfolioId: null,
      investorId: null,
      dimensions: null
    },
    periodId: "2026-06",
    asOfDate: "2026-06-30",
    ledgerBook: { ledgerBookId: null, ledgerBookCode: "Primary GL" },
    accountingBasis: "Gaap",
    presentationCurrency: "USD",
    consolidationLevel: "Fund",
    outputFormat: "Pdf",
    finality: "Draft",
    includeSupportingSchedules: true,
    includeEvidenceAppendix: true,
    templateParameters: {}
  },
  status: "Ready",
  canGenerateDraft: true,
  canGenerateFinal: true,
  checks: [{
    checkId: "run-scope",
    label: "Fund, entity, period, and ledger scope",
    status: "Ready",
    summary: "The requested report scope is complete and internally consistent.",
    issueCount: 0,
    blocksDraft: false,
    blocksFinal: false,
    route: null,
    evidenceReferences: ["fund:fund-alpha", "period:2026-06"]
  }],
  blockingReasons: [],
  evidenceHash: "a".repeat(64)
};

async function renderScreen(initialEntry: string) {
  const result = renderWithRouter(
    <>
      <ReportRunParametersScreen data={reportingData} accounting={accountingData} />
      <LocationProbe />
    </>,
    { initialEntries: [initialEntry] }
  );
  await waitForAsyncEffects();
  return result;
}

function LocationProbe() {
  const location = useLocation();
  return <output data-testid="current-location" className="sr-only">{location.pathname}{location.search}</output>;
}

describe("ReportRunParametersScreen", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(api.assessReportingRunReadiness).mockResolvedValue(readyReadiness);
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
    await waitFor(() => expect(screen.getByRole("button", { name: "Run Holdings Pack" })).toBeEnabled());
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
    expect(screen.getByLabelText("Fund profile")).toHaveValue("fund-alpha");
    expect(screen.getByLabelText("Entity / fund / portfolio")).toHaveValue("AllEntities");
    expect(screen.getByLabelText("Ledger book code")).toHaveValue("Primary GL");
    expect(screen.getByLabelText("Accounting basis")).toHaveValue("Gaap");
    expect(screen.getByLabelText("Output format")).toHaveValue("Pdf");
    expect(screen.getByLabelText("Include supporting schedules")).toBeChecked();
    expect(screen.getByLabelText("Include evidence appendix")).toBeChecked();
    expect(screen.getByRole("heading", { name: "Can this report run?" })).toBeInTheDocument();
    expect(await screen.findByText("Draft ready")).toBeInTheDocument();
    expect(screen.getByText("Fund, entity, period, and ledger scope")).toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Report run form" })).toHaveTextContent("Trial Balance Pack");
  });

  it("submits the exact normalized run and navigates to its governed detail", async () => {
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

    await waitFor(() => expect(screen.getByRole("button", { name: "Run Trial Balance Pack" })).toBeEnabled());
    await user.click(await screen.findByRole("button", { name: "Run Trial Balance Pack" }));

    await waitFor(() => expect(screen.getByTestId("current-location")).toHaveTextContent(
      "/reporting/runs/detail?runId=run-42"
    ));
    expect(api.runReportingNow).toHaveBeenCalledWith(
      expect.objectContaining({
        templateId: "trial-balance-pack",
        template: { name: "trial-balance-pack", version: 1 },
        parameters: expect.objectContaining({ scope: expect.objectContaining({ fundProfileId: "fund-alpha" }) })
      })
    );
  });

  it("round-trips the operator's exact scope, book, output, evidence, and template version through preflight and run", async () => {
    const user = userEvent.setup();
    vi.mocked(api.assessReportingRunReadiness).mockImplementation(async (request) => ({
      ...readyReadiness,
      resolvedTemplate: request.template!,
      resolvedParameters: {
        ...request.parameters!,
        scope: {
          ...request.parameters!.scope,
          dimensions: {
            ...(request.parameters!.scope.dimensions ?? {}),
            organizationId: "org-server-normalized"
          }
        }
      }
    }));
    vi.mocked(api.runReportingNow).mockResolvedValue({
      run: {
        runId: "run-portfolio-42",
        templateId: "trial-balance-pack",
        family: "Financial Statements",
        status: "Queued",
        trigger: "Manual",
        asOfDate: "2026-06-30",
        attemptCount: 1,
        sectionCount: 1,
        lineageLinkedSections: 0,
        artifacts: [],
        auditActions: [],
        failureReason: null
      }
    });

    await renderScreen("/reporting/run?templateId=trial-balance-pack%3A1.0");

    await user.clear(screen.getByLabelText("Fund profile"));
    await user.type(screen.getByLabelText("Fund profile"), "fund-institutional");
    await user.selectOptions(screen.getByLabelText("Entity / fund / portfolio"), "Portfolio");
    await user.type(screen.getByLabelText("Portfolio ID"), "portfolio-credit");
    await user.clear(screen.getByLabelText("Accounting period ID"));
    await user.type(screen.getByLabelText("Accounting period ID"), "2026-Q2");
    await user.clear(screen.getByLabelText("Ledger book code"));
    await user.type(screen.getByLabelText("Ledger book code"), "STAT-GL");
    await user.type(screen.getByLabelText("Ledger book ID (optional)"), "11111111-1111-1111-1111-111111111111");
    await user.selectOptions(screen.getByLabelText("Accounting basis"), "Statutory");
    await user.clear(screen.getByLabelText("Presentation currency"));
    await user.type(screen.getByLabelText("Presentation currency"), "eur");
    await user.selectOptions(screen.getByLabelText("Consolidation level"), "Portfolio");
    await user.selectOptions(screen.getByLabelText("Output format"), "Xlsx");
    await user.click(screen.getByLabelText("Include supporting schedules"));
    await user.click(screen.getByLabelText("Include evidence appendix"));
    fireEvent.change(screen.getByLabelText("Ledger dimensions (JSON)"), {
      target: {
        value: JSON.stringify({
          strategyId: "private-credit",
          positionId: "22222222-2222-2222-2222-222222222222",
          externalGlDimensions: { Department: "Private Credit", Class: "Senior" }
        })
      }
    });
    fireEvent.change(screen.getByLabelText("Template parameters (JSON)"), {
      target: { value: JSON.stringify({ reportingRegion: "EU" }) }
    });
    await user.clear(screen.getByLabelText("Report run as-of date"));
    await user.type(screen.getByLabelText("Report run as-of date"), "2026-06-30");

    const runButton = screen.getByRole("button", { name: "Run Trial Balance Pack" });
    await waitFor(() => expect(runButton).toBeEnabled());

    const latestPreflightRequest = vi.mocked(api.assessReportingRunReadiness).mock.calls.at(-1)?.[0]!;
    expect(latestPreflightRequest).toEqual(expect.objectContaining({
      templateId: "trial-balance-pack",
      template: { name: "trial-balance-pack", version: 1 },
      asOfDate: "2026-06-30",
      parameters: {
        scope: {
          fundProfileId: "fund-institutional",
          entityScopeKind: "Portfolio",
          entityId: null,
          portfolioId: "portfolio-credit",
          investorId: null,
          dimensions: {
            strategyId: "private-credit",
            positionId: "22222222-2222-2222-2222-222222222222",
            externalGlDimensions: { Department: "Private Credit", Class: "Senior" }
          }
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
        finality: "Draft",
        includeSupportingSchedules: false,
        includeEvidenceAppendix: false,
        templateParameters: { reportingRegion: "EU" }
      }
    }));

    await user.click(runButton);
    expect(api.runReportingNow).toHaveBeenCalledWith({
      ...latestPreflightRequest,
      parameters: {
        ...latestPreflightRequest.parameters!,
        scope: {
          ...latestPreflightRequest.parameters!.scope,
          dimensions: {
            ...(latestPreflightRequest.parameters!.scope.dimensions ?? {}),
            organizationId: "org-server-normalized"
          }
        }
      }
    });
  });

  it("blocks a final run when the matching server preflight denies final generation", async () => {
    const user = userEvent.setup();
    vi.mocked(api.assessReportingRunReadiness).mockResolvedValue({
      ...readyReadiness,
      status: "Blocked",
      canGenerateDraft: true,
      canGenerateFinal: false,
      checks: [{
        checkId: "evidence-appendix",
        label: "Release evidence appendix",
        status: "Blocked",
        summary: "Final reporting output must include the supporting evidence appendix.",
        issueCount: 1,
        blocksDraft: false,
        blocksFinal: true,
        route: "/workstation/reporting/evidence",
        evidenceReferences: []
      }],
      blockingReasons: ["Final reporting output must include the supporting evidence appendix."]
    });

    await renderScreen("/reporting/run?templateId=trial-balance-pack%3A1.0");
    await user.selectOptions(screen.getByLabelText("Draft vs final"), "Final");

    expect(await screen.findByText("Final blocked")).toBeInTheDocument();
    expect(screen.getAllByText("Final reporting output must include the supporting evidence appendix.").length)
      .toBeGreaterThanOrEqual(1);
    expect(screen.getByRole("button", { name: "Run Trial Balance Pack" })).toBeDisabled();
    expect(api.runReportingNow).not.toHaveBeenCalled();
  });
});
