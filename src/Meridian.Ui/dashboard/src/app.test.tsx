import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { App } from "@/app";
import { useWorkstationData } from "@/hooks/use-workstation-data";
import { apiGetJson } from "@/lib/api";
import { WORKSTATION_API_ENDPOINTS } from "@/lib/workstation-endpoints";
import { renderWithRouter } from "@/test/render";
import { useNavigate } from "react-router-dom";
import type { FirstRunStatus } from "@/features/first-run/types";
import type {
  DataWorkspaceResponse,
  PortfolioWorkspaceResponse,
  SystemOverviewResponse,
  TradingWorkspaceResponse
} from "@/types";

vi.mock("@/hooks/use-workstation-data", () => ({
  useWorkstationData: vi.fn()
}));

vi.mock("@/lib/api", async () => {
  const actual = await vi.importActual<typeof import("@/lib/api")>("@/lib/api");
  return {
    ...actual,
    apiGetJson: vi.fn(),
    markWorkflowPresetUsed: vi.fn().mockResolvedValue(undefined)
  };
});

const mockedUseWorkstationData = vi.mocked(useWorkstationData);
type WorkstationDataSnapshot = ReturnType<typeof useWorkstationData>;

function resolveSynchronously<T>(value: T): Promise<T> {
  return {
    then: (onFulfilled: (resolved: T) => unknown) => Promise.resolve(onFulfilled(value))
  } as unknown as Promise<T>;
}

function idleRequestStatus(operation: string) {
  return {
    operation,
    phase: "idle" as const,
    inFlight: false,
    version: 0,
    message: "Ready.",
    error: null,
    startedAt: null,
    settledAt: null,
    lastSucceededAt: null,
    staleDiscardCount: 0,
    backoff: { attempt: 0, retryCount: 0, nextRetryDelayMs: null, maxRetries: 0 }
  };
}


function mockWorkstationData(overrides: Partial<WorkstationDataSnapshot>) {
  mockedUseWorkstationData.mockReturnValue({
    session: null,
    overview: null,
    strategy: null,
    trading: null,
    portfolio: null,
    portfolioMultiAssetCoverage: null,
    data: null,
    accounting: null,
    reporting: null,
    brokerageConnection: null,
    robinhoodConnection: null,
    providerConnections: null,
    providerReadiness: null,
    providerRoutingConnections: null,
    providerRoutingBindings: null,
    providerRoutingTrustSnapshots: null,
    providerRoutingRefreshing: false,
    rolePermissionCatalog: null,
    securityAssetProfiles: null,
    ledgerMappingWorkbench: null,
    operationsApprovalPolicyMatrix: null,
    operationsCloseCalendar: null,
    brokeragePortfolio: null,
    workflowLibrary: null,
    workflowPresets: null,
    workflowSummary: null,
    featureCapabilities: null,
    workflowError: null,
    usingDevelopmentFixtures: false,
    loading: false,
    error: null,
    workspaceErrors: {},
    refreshStatus: idleRequestStatus("workstation overview refresh"),
    tradingRefreshStatus: idleRequestStatus("trading workspace refresh"),
    providerRoutingRefreshStatus: idleRequestStatus("provider routing refresh"),
    portfolioRefreshStatus: idleRequestStatus("portfolio refresh"),
    refresh: vi.fn(),
    refreshWorkspace: vi.fn(),
    refreshTrading: vi.fn(),
    refreshPortfolio: vi.fn(),
    refreshProviderRouting: vi.fn(),
    updateFeatureCapability: vi.fn(),
    upsertWorkflowPreset: vi.fn(),
    ...overrides
  });
}

const portfolio: PortfolioWorkspaceResponse = {
  metrics: [],
  positions: [
    {
      symbol: "NVDA",
      side: "Long",
      quantity: "12",
      averagePrice: "840.00",
      markPrice: "850.00",
      dayPnl: "+$120",
      unrealizedPnl: "+$120",
      exposure: "$10,200"
    }
  ],
  risk: {
    state: "Healthy",
    summary: "Portfolio endpoint risk posture.",
    netExposure: "$10,200",
    grossExposure: "$10,200",
    var95: "$500",
    maxDrawdown: "0%",
    buyingPowerUsed: "22%",
    activeGuardrails: []
  },
  brokerage: {
    provider: "Alpaca",
    account: "PF-ENDPOINT",
    environment: "paper",
    connection: "Connected",
    lastHeartbeat: "1s ago",
    orderIngress: "healthy",
    fillFeed: "healthy",
    notes: ""
  },
  runs: [],
  cashFlow: null
};

const overview: SystemOverviewResponse = {
  systemStatus: "Degraded",
  providersOnline: 2,
  providersTotal: 3,
  activeRuns: 1,
  openPositions: 4,
  activeBackfills: 0,
  symbolsMonitored: 24,
  storageHealth: "Warning",
  lastHeartbeatUtc: "2026-05-15T14:00:00Z",
  metrics: [],
  recentEvents: []
};

const completedFirstRunStatus: FirstRunStatus = {
  isComplete: true,
  goal: "operate-fund",
  starterKitId: "fund-operations",
  dataChoice: "sample",
  workspace: {
    id: "ops-workspace",
    name: "Operations workspace",
    isSample: false,
    badge: "PAPER",
    safetyMessage: "Paper operation only.",
    samplePackVersion: ""
  },
  starterKits: [],
  outcomes: [],
  recommendedActions: [],
  sampleWorkspace: null
};

function mockDailyControlTowerData() {
  mockWorkstationData({
    session: {
      displayName: "Ops Desk",
      role: "Operator",
      environment: "paper",
      activeWorkspace: "trading",
      commandCount: 7
    },
    overview,
    trading: {
      readiness: {
        asOf: "2026-05-14T21:30:00Z",
        overallStatus: "Blocked",
        readyForPaperOperation: false,
        acceptanceGates: [
          {
            gateId: "replay-gate",
            label: "Replay audit",
            status: "Blocked",
            detail: "Replay evidence is stale for the active paper session."
          }
        ],
        activeSession: null,
        sessions: [],
        replay: null,
        controls: {
          circuitBreakerOpen: false
        },
        promotion: null,
        trustGate: null,
        brokerageSync: null,
        workItems: [
          {
            workItemId: "brokerage-sync",
            kind: "BrokerageSync",
            label: "Brokerage sync failed",
            detail: "Account sync failed after the last provider heartbeat.",
            tone: "Critical",
            createdAt: "2026-05-14T20:00:00Z",
            runId: null,
            fundAccountId: "fund-1",
            auditReference: "audit-1",
            workspace: "portfolio",
            targetRoute: "/portfolio/brokerage-sync",
            targetPageTag: "BrokerageSync"
          },
          {
            workItemId: "report-pack",
            kind: "ReportPackApproval",
            label: "Report pack approval waiting",
            detail: "Monthly board pack still needs an operator sign-off.",
            tone: "Warning",
            createdAt: "2026-05-14T21:00:00Z",
            runId: "run-1",
            fundAccountId: null,
            auditReference: "audit-2",
            workspace: "reporting",
            targetRoute: "/reporting/report-packs",
            targetPageTag: "ReportPackApproval"
          }
        ],
        warnings: []
      }
    } as unknown as TradingWorkspaceResponse,
    data: {
      providers: [
        {
          provider: "Alpaca",
          status: "Warning",
          capability: "paper",
          latency: "120ms",
          note: "Paper endpoint returned intermittent quote gaps.",
          recommendedAction: "Review paper provider posture."
        }
      ],
      backfills: [],
      exports: []
    } as unknown as DataWorkspaceResponse,
    loading: false,
    error: null,
    workspaceErrors: {}
  });
}

describe("App", () => {
  beforeEach(() => {
    document.title = "Meridian";
    window.localStorage.clear();
    mockedUseWorkstationData.mockClear();
    vi.mocked(apiGetJson).mockReset();
    vi.mocked(apiGetJson).mockImplementation((path) => {
      if (path === WORKSTATION_API_ENDPOINTS.firstRunStatus) {
        return resolveSynchronously(completedFirstRunStatus) as ReturnType<typeof apiGetJson>;
      }

      if (path === WORKSTATION_API_ENDPOINTS.demoMode) {
        return Promise.resolve({ enabled: false, provenance: "real" }) as ReturnType<typeof apiGetJson>;
      }

      return Promise.reject(new Error(`No test response configured for ${path}`));
    });
    mockWorkstationData({
      session: {
        displayName: "Ops Desk",
        role: "Operator",
        environment: "paper",
        activeWorkspace: "trading",
        commandCount: 7
      },
      overview: null,
      strategy: null,
      trading: null,
      portfolio: null,
      data: null,
      accounting: null,
      reporting: null,
      brokerageConnection: null,
      providerConnections: null,
      brokeragePortfolio: null,
      workflowLibrary: null,
      workflowPresets: null,
      workflowSummary: null,
      workflowError: null,
      usingDevelopmentFixtures: false,
      loading: true,
      error: null,
      workspaceErrors: {},
      refresh: vi.fn(),
      refreshTrading: vi.fn(),
      refreshPortfolio: vi.fn(),
      upsertWorkflowPreset: vi.fn()
    });
  });

  it("keeps the workstation closed while activation status is loading", () => {
    vi.mocked(apiGetJson).mockImplementation((path) => {
      if (path === WORKSTATION_API_ENDPOINTS.firstRunStatus) {
        return new Promise(() => {}) as ReturnType<typeof apiGetJson>;
      }

      if (path === WORKSTATION_API_ENDPOINTS.demoMode) {
        return Promise.resolve({ enabled: false, provenance: "real" }) as ReturnType<typeof apiGetJson>;
      }

      return Promise.reject(new Error(`No test response configured for ${path}`));
    });

    renderWithRouter(<App />, { initialEntries: ["/trading"] });

    expect(screen.getByRole("status", { name: "Activation status check" })).toHaveTextContent(
      "Checking activation status"
    );
    expect(screen.queryByRole("button", { name: "Open workstation command palette (Ctrl K)" }))
      .not.toBeInTheDocument();
    expect(mockedUseWorkstationData).not.toHaveBeenCalled();
  });

  it("fails closed on an unknown activation state and recovers after retry", async () => {
    let firstRunRequests = 0;
    vi.mocked(apiGetJson).mockImplementation((path) => {
      if (path === WORKSTATION_API_ENDPOINTS.firstRunStatus) {
        firstRunRequests += 1;
        return firstRunRequests === 1
          ? Promise.reject(new Error("Activation endpoint unavailable."))
          : Promise.resolve(completedFirstRunStatus) as ReturnType<typeof apiGetJson>;
      }

      if (path === WORKSTATION_API_ENDPOINTS.demoMode) {
        return Promise.resolve({ enabled: false, provenance: "real" }) as ReturnType<typeof apiGetJson>;
      }

      return Promise.reject(new Error(`No test response configured for ${path}`));
    });

    renderWithRouter(<App />, { initialEntries: ["/trading"] });

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("Activation status unavailable");
    expect(alert).toHaveTextContent("Activation state is unknown");
    expect(mockedUseWorkstationData).not.toHaveBeenCalled();

    await userEvent.click(screen.getByRole("button", { name: "Retry activation check" }));

    expect(await screen.findByRole("button", { name: "Open workstation command palette (Ctrl K)" }))
      .toBeInTheDocument();
    expect(firstRunRequests).toBe(2);
    expect(mockedUseWorkstationData).toHaveBeenCalled();
  });

  it("surfaces successful seeded demo JSON even without a development-fixture response header", async () => {
    vi.mocked(apiGetJson).mockImplementation((path) => {
      if (path === WORKSTATION_API_ENDPOINTS.firstRunStatus) {
        return resolveSynchronously(completedFirstRunStatus) as ReturnType<typeof apiGetJson>;
      }

      if (path === WORKSTATION_API_ENDPOINTS.demoMode) {
        return Promise.resolve({ enabled: true, provenance: "seeded" }) as ReturnType<typeof apiGetJson>;
      }

      return Promise.reject(new Error(`No test response configured for ${path}`));
    });
    mockWorkstationData({ loading: false, usingDevelopmentFixtures: false });

    renderWithRouter(<App />, { initialEntries: ["/trading"] });

    const strip = await screen.findByRole("region", {
      name: "Workstation build, environment, provenance, and provider posture"
    });
    expect(strip).toHaveAttribute("aria-live", "polite");
    await userEvent.click(within(strip).getByText("Trust"));
    const trustDetails = within(strip).getByRole("group", {
      name: "Environment, provenance, and provider details"
    });
    expect(within(trustDetails).getByRole("link", { name: /Data provenance SEEDED/ }))
      .toHaveTextContent("ProvenanceSEEDED");
    expect(screen.queryByRole("region", { name: "Data provenance" })).not.toBeInTheDocument();
  });

  it("opens and closes the command palette with Control+K", async () => {
    const user = userEvent.setup();
    renderWithRouter(<App />, { initialEntries: ["/trading"] });
    const trigger = screen.getByRole("button", { name: "Open workstation command palette (Ctrl K)" });

    expect(document.querySelector('[data-design-system-component="Masthead"]')).toHaveClass("mds-masthead");
    expect(trigger).toHaveClass("mds-masthead__search");
    expect(screen.queryByRole("dialog", { name: "Open workstation command" })).not.toBeInTheDocument();
    expect(trigger).toHaveAttribute("aria-controls", "command-palette-dialog");
    expect(trigger).toHaveAttribute("aria-expanded", "false");
    expect(trigger).toHaveAttribute("aria-haspopup", "dialog");

    await user.keyboard("{Control>}k{/Control}");
    const dialog = screen.getByRole("dialog", { name: "Open workstation command" });
    expect(dialog).toHaveAttribute("id", "command-palette-dialog");
    expect(trigger).toHaveAttribute("aria-expanded", "true");
    expect(trigger).toHaveAccessibleName("Close workstation command palette (Ctrl K)");

    await user.keyboard("{Control>}k{/Control}");
    expect(screen.queryByRole("dialog", { name: "Open workstation command" })).not.toBeInTheDocument();
    expect(trigger).toHaveAttribute("aria-expanded", "false");
  });

  it("provides a skip link into the workbench content", () => {
    mockWorkstationData({ loading: false });

    renderWithRouter(<App />, { initialEntries: ["/trading"] });

    expect(screen.getByRole("link", { name: "Skip to workbench" })).toHaveAttribute("href", "#workbench-content");
    const main = screen.getByRole("main", { name: "Trading workbench" });
    expect(main).toHaveAttribute("id", "workbench-content");
    expect(main).toHaveAttribute("aria-busy", "false");
  });

  it("marks the named workbench landmark busy during bootstrap", () => {
    mockWorkstationData({ loading: true });

    renderWithRouter(<App />, { initialEntries: ["/reporting/report-packs"] });

    expect(screen.getByRole("main", { name: "Reporting workbench" })).toHaveAttribute("aria-busy", "true");
  });

  it("renders the daily control tower on the root route", async () => {
    const user = userEvent.setup();
    mockDailyControlTowerData();

    const { container } = renderWithRouter(<App />, { initialEntries: ["/"] });

    expect(await screen.findByRole(
      "heading",
      { name: "What needs an operator decision now" },
      { timeout: 10_000 }
    )).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Daily Control Tower Workstation" })).toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Daily Control Tower continuity" })).toBeInTheDocument();
    const confidence = screen.getByRole("region", { name: "Daily control tower confidence" });
    ["Connectivity", "Scope", "Freshness", "Completeness", "Blocker"].forEach((label) => {
      expect(within(confidence).getByText(label)).toBeInTheDocument();
    });
    expect(confidence).toHaveTextContent("4 ranked items");
    expect(screen.queryByRole("region", { name: "Daily control tower decision drivers" })).not.toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Choose Control Tower scope" })).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Review all scopes" }));
    expect(screen.getByRole("treegrid", { name: "Daily control tower finance queue" })).toBeInTheDocument();
    expect(screen.getAllByRole("link", {
      name: "Reporting: Report pack approval waiting. Monthly board pack still needs an operator sign-off. Open report packs."
    }).some((link) => link.getAttribute("href") === "/reporting/report-packs")).toBe(true);

    const evidenceSummary = screen.getByRole("region", { name: /Report pack approval waiting evidence summary/i });
    const moreEvidence = within(evidenceSummary).getByText("More evidence").closest("details");
    expect(moreEvidence).not.toHaveAttribute("open");
    await user.click(within(evidenceSummary).getByText("More evidence"));
    expect(moreEvidence).toHaveAttribute("open");
    [
      "Source",
      "Freshness",
      "Reconciliation",
      "Approvals",
      "Report Usage",
      "Blockers",
      "Evidence Packet",
      "Audit Trail"
    ].forEach((label) => {
      expect(within(evidenceSummary).getByText(label)).toBeInTheDocument();
    });
    expect(within(evidenceSummary).getByText("Reporting package or evidence")).toBeInTheDocument();
    const accessibilityResults = await axe(container);
    expect(accessibilityResults.violations).toHaveLength(0);
    await waitFor(() => expect(document.title).toBe("Daily Control Tower - Meridian"));
  });

  it("redirects the legacy overview route to the daily control tower", async () => {
    mockDailyControlTowerData();

    renderWithRouter(<App />, { initialEntries: ["/overview"] });

    await waitFor(() => expect(screen.getByRole("heading", { name: "What needs an operator decision now" })).toBeInTheDocument());
    await waitFor(() => expect(document.title).toBe("Daily Control Tower - Meridian"));
  });

  it("hides workflow continuity on an unrecognized route", async () => {
    mockDailyControlTowerData();

    const { container } = renderWithRouter(<App />, { initialEntries: ["/not-a-workstation-route"] });

    expect(await screen.findByRole(
      "alert",
      { name: "Workbench route not found" },
      { timeout: 10_000 }
    )).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Open Daily Control Tower" })).toHaveAttribute("href", "/");
    expect(container.querySelector(".workflow-continuity-dock")).not.toBeInTheDocument();
  });

  it.each([
    "/data/not-a-real-workstream",
    "/settings/not-a-real-task"
  ])("rejects unknown workspace child route %s instead of rendering a root fallback", async (route) => {
    mockDailyControlTowerData();

    renderWithRouter(<App />, { initialEntries: [route] });

    expect(await screen.findByRole(
      "alert",
      { name: "Workbench route not found" },
      { timeout: 10_000 }
    )).toBeInTheDocument();
  });

  it("renders build, environment, data-source, and provider trust in the masthead", async () => {
    mockWorkstationData({
      session: {
        displayName: "Ops Desk",
        role: "Operator",
        environment: "paper",
        activeWorkspace: "data",
        commandCount: 7
      },
      data: {
        metrics: [],
        providers: [
          {
            provider: "Alpaca",
            status: "Degraded",
            capability: "paper",
            latency: "timeout",
            note: "Credential check failed"
          }
        ],
        backfills: [],
        exports: []
      },
      usingDevelopmentFixtures: true,
      loading: false
    });

    renderWithRouter(<App />, { initialEntries: ["/data/providers"] });

    expect(document.querySelector('[data-design-system-component="Masthead"]')).toHaveClass("mds-masthead");
    expect(screen.getByText("Data", { selector: ".sub" })).toBeInTheDocument();
    const strip = screen.getByRole("region", { name: "Workstation build, environment, provenance, and provider posture" });
    await userEvent.click(within(strip).getByText("Trust"));
    const trustDetails = within(strip).getByRole("group", {
      name: "Environment, provenance, and provider details"
    });
    expect(within(trustDetails).getByLabelText("Build 0.1.0. Current Meridian web release."))
      .toHaveTextContent("Buildv0.1.0");
    expect(within(trustDetails).getByLabelText(/^Environment Paper\./)).toHaveTextContent("EnvironmentPaper");
    expect(within(trustDetails).getByRole("link", { name: /Data provenance SEEDED/ }))
      .toHaveTextContent("ProvenanceSEEDED");
    expect(within(trustDetails).getByRole("link", {
      name: "Providers 1 degraded. 1 provider degraded; open Data provider posture before trading decisions. Open provider posture."
    })).toHaveAttribute("href", "/data/providers");
  });

  it("groups current session context for assistive technology", () => {
    renderWithRouter(<App />, { initialEntries: ["/trading"] });

    const sessionContext = screen.getByRole("group", {
      name: "Current session: paper, Ops Desk, Operator"
    });
    expect(within(sessionContext).getByText("paper")).toBeInTheDocument();
    expect(within(sessionContext).getByText("Ops Desk")).toBeInTheDocument();
    expect(within(sessionContext).getByText("Operator")).toBeInTheDocument();
  });

  it("renders an informative startup status while workstation bootstrap is loading", () => {
    renderWithRouter(<App />, { initialEntries: ["/trading"] });

    const status = screen.getByRole("status", { name: "Preparing workspace" });
    expect(status).toHaveAttribute("aria-busy", "true");
    expect(within(status).getByLabelText("Session state: resolving operator context and environment guardrails")).toBeInTheDocument();
    expect(within(status).getByLabelText("Workspace data: loading Trading, Portfolio, Accounting, Reporting, Strategy, Data, and Settings")).toBeInTheDocument();
    expect(within(status).getByLabelText("Evidence slices: preparing readiness, reconciliation, provider, and report-pack evidence")).toBeInTheDocument();
    expect(within(status).getByLabelText("Workspace data loading status")).toBeInTheDocument();
  });

  it("has no basic accessibility violations in the trading shell", async () => {
    const { container } = renderWithRouter(<App />, { initialEntries: ["/trading"] });

    const results = await axe(container);
    expect(results.violations).toHaveLength(0);
  });

  it("renders route-aware workflow continuity without relying on memorized navigation", async () => {
    const user = userEvent.setup();
    mockWorkstationData({
      session: {
        displayName: "Ops Desk",
        role: "Operator",
        environment: "paper",
        activeWorkspace: "data",
        commandCount: 7
      },
      overview,
      strategy: null,
      trading: null,
      portfolio: null,
      data: null,
      accounting: null,
      reporting: null,
      brokerageConnection: null,
      providerConnections: null,
      brokeragePortfolio: null,
      workflowLibrary: null,
      workflowPresets: null,
      workflowSummary: null,
      workflowError: null,
      usingDevelopmentFixtures: false,
      loading: false,
      error: null,
      workspaceErrors: {},
      refresh: vi.fn(),
      refreshTrading: vi.fn(),
      refreshPortfolio: vi.fn(),
      upsertWorkflowPreset: vi.fn()
    });

    renderWithRouter(<App />, { initialEntries: ["/data/quotes?symbol=MSFT"] });

    expect(screen.getByRole("region", { name: "Market Data To Paper continuity" })).toBeInTheDocument();
    expect(screen.getByText("Data / MSFT")).toBeInTheDocument();
    expect(screen.getByLabelText("Current route /data/quotes")).toHaveTextContent("/data/quotes");
    expect(screen.queryByText("/data/quotes?symbol=MSFT")).not.toBeInTheDocument();
    expect(screen.queryByText("Import -> Validate -> Reconcile -> Investigate -> Approve -> Report")).not.toBeInTheDocument();
    expect(screen.queryByRole("navigation", { name: "Market Data To Paper workflow steps" })).not.toBeInTheDocument();

    await user.click(screen.getByText("Flow details"));

    expect(screen.getByText("/data/quotes?symbol=MSFT")).toBeInTheDocument();
    expect(screen.getByText("Import -> Validate -> Reconcile -> Investigate -> Approve -> Report")).toBeInTheDocument();
    expect(screen.getByRole("navigation", { name: "Market Data To Paper workflow steps" })).toBeInTheDocument();
    expect(screen.getByRole("navigation", { name: "Primary operator workflow steps" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Market data, current workflow step, Waiting" })).toHaveAttribute(
      "href",
      "/data/quotes?symbol=MSFT"
    );
    expect(screen.getByRole("link", { name: "Validate, current primary operator workflow step, Current" })).toHaveAttribute(
      "href",
      "/data/operations?symbol=MSFT"
    );
    expect(screen.getByRole("link", { name: "Readiness, next workflow step, Waiting" })).toHaveAttribute(
      "href",
      "/trading/readiness?symbol=MSFT"
    );
    expect(screen.getByRole("link", { name: "Continue workflow to Readiness" })).toHaveAttribute(
      "href",
      "/trading/readiness?symbol=MSFT"
    );
    expect(screen.getByRole("button", { name: "Clear MSFT operating context" })).toBeInTheDocument();
  });

  it("renders and clears the global operating scope across the workstation shell", async () => {
    const user = userEvent.setup();
    mockWorkstationData({
      session: {
        displayName: "Ops Desk",
        role: "Operator",
        environment: "paper",
        activeWorkspace: "portfolio",
        commandCount: 7
      },
      overview,
      strategy: null,
      trading: null,
      portfolio,
      data: null,
      accounting: null,
      reporting: null,
      brokerageConnection: null,
      providerConnections: null,
      brokeragePortfolio: null,
      workflowLibrary: null,
      workflowPresets: null,
      workflowSummary: null,
      workflowError: null,
      usingDevelopmentFixtures: false,
      loading: false,
      error: null,
      workspaceErrors: {},
      refresh: vi.fn(),
      refreshTrading: vi.fn(),
      refreshPortfolio: vi.fn(),
      upsertWorkflowPreset: vi.fn()
    });

    renderWithRouter(<App />, {
      initialEntries: ["/portfolio?symbol=MSFT&fundAccountId=fund-1&runId=run-9&provider=Alpaca&from=2026-05-01&to=2026-05-15"]
    });

    const operatingScope = screen.getByLabelText("Operating scope");
    expect(within(operatingScope).getByText("Subject")).toBeInTheDocument();
    expect(within(operatingScope).getByText("MSFT")).toBeInTheDocument();
    expect(within(operatingScope).getByText("Account")).toBeInTheDocument();
    expect(within(operatingScope).getByText("fund-1")).toBeInTheDocument();
    expect(within(operatingScope).getByText("Run")).toBeInTheDocument();
    expect(within(operatingScope).getByText("Selected run")).toBeInTheDocument();
    expect(within(operatingScope).queryByText("run-9")).not.toBeInTheDocument();
    expect(within(operatingScope).getByText("Provider")).toBeInTheDocument();
    expect(within(operatingScope).getByText("Alpaca")).toBeInTheDocument();
    expect(within(operatingScope).getByText("Window")).toBeInTheDocument();
    expect(within(operatingScope).getByText("2026-05-01 to 2026-05-15")).toBeInTheDocument();
    expect(window.localStorage.getItem("meridian.workstation.operatingContext.v1")).toContain("\"fundAccountId\":\"fund-1\"");

    await user.keyboard("{Control>}k{/Control}");
    expect(screen.getAllByText("Subject: MSFT / Account: fund-1 / Run: Selected run / Provider: Alpaca / Window: 2026-05-01 to 2026-05-15").length).toBeGreaterThan(0);
    expect(screen.getByRole("link", { name: "Open Trading workspace" })).toHaveAttribute(
      "href",
      "/trading?symbol=MSFT&fundAccountId=fund-1&runId=run-9&provider=Alpaca&from=2026-05-01&to=2026-05-15"
    );

    await user.keyboard("{Escape}");
    await user.click(screen.getByRole("button", {
      name: "Clear operating scope: Subject MSFT, Account fund-1, Run Selected run, Provider Alpaca, Window 2026-05-01 to 2026-05-15"
    }));

    expect(window.localStorage.getItem("meridian.workstation.operatingContext.v1")).toBeNull();
    expect(screen.queryByLabelText("Operating scope")).not.toBeInTheDocument();
  });

  it("surfaces ranked operator focus and evidence while preserving command palette actions", async () => {
    const user = userEvent.setup();
    mockWorkstationData({
      session: {
        displayName: "Ops Desk",
        role: "Operator",
        environment: "paper",
        activeWorkspace: "data",
        commandCount: 7
      },
      overview: null,
      strategy: null,
      trading: {
        readiness: {
          acceptanceGates: [
            {
              gateId: "replay-gate",
              label: "Replay audit",
              status: "Blocked",
              detail: "Replay evidence is stale for the active paper session."
            }
          ],
          workItems: [
            {
              workItemId: "brokerage-sync",
              kind: "BrokerageSync",
              label: "Brokerage sync failed",
              detail: "Account sync failed after the last provider heartbeat.",
              tone: "Critical",
              createdAt: "2026-05-14T20:00:00Z",
              runId: null,
              fundAccountId: "fund-1",
              auditReference: "audit-1",
              workspace: "portfolio",
              targetRoute: "/portfolio/brokerage-sync",
              targetPageTag: "BrokerageSync"
            },
            {
              workItemId: "report-pack",
              kind: "ReportPackApproval",
              label: "Report pack approval waiting",
              detail: "Monthly board pack still needs an operator sign-off.",
              tone: "Warning",
              createdAt: "2026-05-14T21:00:00Z",
              runId: "run-1",
              fundAccountId: null,
              auditReference: "audit-2",
              workspace: "reporting",
              targetRoute: "/reporting/report-packs",
              targetPageTag: "ReportPackApproval"
            }
          ],
          controls: {
            circuitBreakerOpen: false
          },
          replay: null,
          brokerageSync: null
        }
      } as unknown as TradingWorkspaceResponse,
      portfolio: null,
      data: {
        providers: [
          {
            provider: "Alpaca",
            status: "Warning",
            capability: "paper",
            latency: "120ms",
            note: "Paper endpoint returned intermittent quote gaps.",
            recommendedAction: "Review paper provider posture."
          }
        ],
        backfills: [],
        exports: []
      } as unknown as DataWorkspaceResponse,
      accounting: null,
      reporting: null,
      brokerageConnection: null,
      providerConnections: null,
      brokeragePortfolio: null,
      workflowLibrary: null,
      workflowPresets: null,
      workflowSummary: null,
      workflowError: null,
      usingDevelopmentFixtures: false,
      loading: false,
      error: null,
      workspaceErrors: {},
      refresh: vi.fn(),
      refreshTrading: vi.fn(),
      refreshPortfolio: vi.fn(),
      upsertWorkflowPreset: vi.fn()
    });

    renderWithRouter(<App />, { initialEntries: ["/data/quotes?symbol=MSFT"] });

    await user.keyboard("{Control>}k{/Control}");
    expect(screen.getByRole("dialog", { name: "Open workstation command" })).toBeInTheDocument();
    expect(screen.getByLabelText("Focus actions: 4 focus actions")).toBeInTheDocument();
    expect(screen.getByRole("navigation", { name: /\d+ workstation commands/ })).toBeInTheDocument();
    expect(screen.getAllByRole("link", {
      name: "Settings: Brokerage sync failed. Account sync failed after the last provider heartbeat. Fix provider setup."
    }).some((link) => link.getAttribute("href") === "/settings#alpaca-provider-setup")).toBe(true);
    expect(screen.getByRole("link", {
      name: "Data: Alpaca provider warning. Review paper provider posture. Open provider trust."
    })).toHaveAttribute("href", "/data/providers?symbol=MSFT");
    expect(screen.getAllByRole("link", {
      name: "Trading: Replay audit. Replay evidence is stale for the active paper session. Open readiness."
    }).some((link) => link.getAttribute("href") === "/trading/readiness?symbol=MSFT")).toBe(true);
  });

  it("keeps a stored operating symbol available in the shell and command palette", async () => {
    const user = userEvent.setup();
    window.localStorage.setItem("meridian.workstation.operatingContext.v1", JSON.stringify({ symbol: "msft" }));
    mockWorkstationData({
      session: {
        displayName: "Ops Desk",
        role: "Operator",
        environment: "paper",
        activeWorkspace: "portfolio",
        commandCount: 7
      },
      overview,
      strategy: null,
      trading: null,
      portfolio: null,
      data: null,
      accounting: null,
      reporting: null,
      brokerageConnection: null,
      providerConnections: null,
      brokeragePortfolio: null,
      workflowLibrary: null,
      workflowPresets: null,
      workflowSummary: null,
      workflowError: null,
      usingDevelopmentFixtures: false,
      loading: false,
      error: null,
      workspaceErrors: {},
      refresh: vi.fn(),
      refreshTrading: vi.fn(),
      refreshPortfolio: vi.fn(),
      upsertWorkflowPreset: vi.fn()
    });

    renderWithRouter(<App />, { initialEntries: ["/portfolio"] });

    expect(screen.getByText("Portfolio / MSFT")).toBeInTheDocument();
    await user.keyboard("{Control>}k{/Control}");

    expect(screen.getAllByText("Subject: MSFT").length).toBeGreaterThan(0);
    expect(screen.getByRole("link", { name: "Open Live quotes route" })).toHaveAttribute(
      "href",
      "/data/quotes?symbol=MSFT"
    );
    expect(screen.getByRole("link", { name: "Open Price alerts route" })).toHaveAttribute(
      "href",
      "/data/quotes?view=alerts&symbol=MSFT"
    );

    await user.keyboard("{Escape}");
    await user.click(screen.getByRole("button", { name: "Clear MSFT operating context" }));

    expect(screen.queryByText("Portfolio / MSFT")).not.toBeInTheDocument();
    expect(window.localStorage.getItem("meridian.workstation.operatingContext.v1")).toBeNull();
  });

  it("sets the workstation document title on first direct route load without moving focus", async () => {
    renderWithRouter(<App />, { initialEntries: ["/settings"] });

    await waitFor(() => expect(document.title).toBe("Settings Workstation - Meridian"));
    expect(screen.getByRole("main")).not.toHaveFocus();
    expect(screen.queryByText("Settings Workstation loaded.")).not.toBeInTheDocument();
  });

  it("redirects the legacy Data Security Master route into Accounting", async () => {
    mockWorkstationData({
      session: {
        displayName: "Ops Desk",
        role: "Operator",
        environment: "paper",
        activeWorkspace: "trading",
        commandCount: 7
      },
      overview: null,
      strategy: null,
      trading: null,
      portfolio: null,
      data: null,
      accounting: null,
      reporting: null,
      brokerageConnection: null,
      providerConnections: null,
      brokeragePortfolio: null,
      workflowLibrary: null,
      workflowPresets: null,
      workflowSummary: null,
      workflowError: null,
      usingDevelopmentFixtures: false,
      loading: false,
      error: null,
      workspaceErrors: {},
      refresh: vi.fn(),
      refreshTrading: vi.fn(),
      refreshPortfolio: vi.fn(),
      upsertWorkflowPreset: vi.fn()
    });

    renderWithRouter(<App />, { initialEntries: ["/data/security-master"] });

    await waitFor(() => expect(document.title).toBe("Accounting Workstation - Meridian"));
    expect(screen.getByLabelText("Accounting workspace, active section, Available product maturity")).toBeInTheDocument();
  });

  it("redirects legacy Research and Governance wildcard routes to canonical workspaces", async () => {
    const user = userEvent.setup();
    mockWorkstationData({ loading: false });

    function Harness() {
      const navigate = useNavigate();
      return (
        <>
          <button type="button" onClick={() => navigate("/governance/reconciliation")}>Open legacy governance</button>
          <App />
        </>
      );
    }

    renderWithRouter(<Harness />, { initialEntries: ["/research/run-library"] });

    await waitFor(() => expect(document.title).toBe("Strategy Workstation - Meridian"));
    expect(screen.getByLabelText("Strategy workspace, active section, Available product maturity")).toBeInTheDocument();

    document.title = "Meridian";
    await user.click(screen.getByRole("button", { name: "Open legacy governance" }));

    await waitFor(() => expect(document.title).toBe("Accounting Workstation - Meridian"));
    expect(screen.getByLabelText("Accounting workspace, active section, Available product maturity")).toBeInTheDocument();
  });

  it("announces route changes and moves focus to the workbench", async () => {
    const user = userEvent.setup();

    function Harness() {
      const navigate = useNavigate();
      return (
        <>
          <button type="button" onClick={() => navigate("/portfolio")}>Route to portfolio</button>
          <App />
        </>
      );
    }

    renderWithRouter(<Harness />, { initialEntries: ["/trading"] });

    await user.click(screen.getByRole("button", { name: "Route to portfolio" }));

    expect(await screen.findByText("Portfolio Workstation loaded.")).toBeInTheDocument();
    await waitFor(() => expect(screen.getByRole("main")).toHaveFocus());
  });

  it("announces and focuses hash-targeted workflow links", async () => {
    const user = userEvent.setup();

    mockWorkstationData({
      session: {
        displayName: "Ops Desk",
        role: "Operator",
        environment: "paper",
        activeWorkspace: "settings",
        commandCount: 7
      },
      overview,
      strategy: null,
      trading: null,
      portfolio: null,
      data: null,
      accounting: null,
      reporting: null,
      brokerageConnection: null,
      providerConnections: null,
      brokeragePortfolio: null,
      workflowLibrary: null,
      workflowPresets: null,
      workflowSummary: null,
      workflowError: null,
      usingDevelopmentFixtures: true,
      loading: false,
      error: null,
      workspaceErrors: {},
      refresh: vi.fn(),
      refreshTrading: vi.fn(),
      refreshPortfolio: vi.fn(),
      upsertWorkflowPreset: vi.fn()
    });

    renderWithRouter(<App />, { initialEntries: ["/settings#alpaca-provider-setup"] });

    expect(await screen.findByText("Settings Workstation loaded. Jumping to alpaca provider setup.")).toBeInTheDocument();
    await waitFor(() => expect(document.getElementById("alpaca-provider-setup")).not.toBeNull(), { timeout: 15000 });
    const alpacaSetup = document.getElementById("alpaca-provider-setup");
    await waitFor(() => expect(alpacaSetup).toHaveFocus(), { timeout: 5000 });
    await user.click(screen.getByText("Flow details"));
    expect(screen.getByRole("link", { name: /Provider setup, current workflow step/ })).toHaveAttribute(
      "aria-current",
      "step"
    );
  });

  it("does not open the command palette shortcut while typing in an input", async () => {
    const user = userEvent.setup();
    renderWithRouter(
      <>
        <App />
        <label htmlFor="scratch-input">Scratch</label>
        <input id="scratch-input" />
      </>,
      { initialEntries: ["/trading"] }
    );

    await user.click(screen.getByLabelText("Scratch"));
    await user.keyboard("{Control>}k{/Control}");

    expect(screen.queryByRole("dialog", { name: "Open workstation command" })).not.toBeInTheDocument();
  });

  it("opens and closes the responsive workspace navigation drawer", async () => {
    const user = userEvent.setup();
    renderWithRouter(<App />, { initialEntries: ["/trading"] });

    expect(screen.queryByRole("dialog", { name: "Workspace navigation" })).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Open workspace navigation" }));
    const navigationDialog = screen.getByRole("dialog", { name: "Workspace navigation" });
    expect(navigationDialog).toBeInTheDocument();
    expect(within(navigationDialog).getByLabelText("Meridian navigation")).toHaveAttribute("data-design-system-component", "NavRail");
    expect(within(navigationDialog).getByLabelText("Trading workspace, current route, Available product maturity")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Close workspace navigation" }));
    expect(screen.queryByRole("dialog", { name: "Workspace navigation" })).not.toBeInTheDocument();
  });

  it("routes degraded bootstrap recovery to Settings capability diagnostics", async () => {
    const user = userEvent.setup();
    const refresh = vi.fn();

    mockWorkstationData({
      session: {
        displayName: "Ops Desk",
        role: "Operator",
        environment: "paper",
        activeWorkspace: "trading",
        commandCount: 7
      },
      overview: null,
      strategy: null,
      trading: null,
      portfolio: null,
      data: null,
      accounting: null,
      reporting: null,
      brokerageConnection: null,
      providerConnections: null,
      brokeragePortfolio: null,
      workflowLibrary: null,
      workflowPresets: null,
      workflowSummary: null,
      workflowError: null,
      usingDevelopmentFixtures: false,
      loading: false,
      error: "Data workspace unavailable",
      workspaceErrors: {
        data: "Backfill summary timed out."
      },
      refresh,
      refreshTrading: vi.fn(),
      refreshPortfolio: vi.fn(),
      upsertWorkflowPreset: vi.fn()
    });

    renderWithRouter(<App />, { initialEntries: ["/trading"] });

    expect(screen.getByRole("status", { name: "Some workspace data is unavailable" })).toBeInTheDocument();
    const diagnosticsLink = screen.getByRole("link", {
      name: "Review Settings diagnostics for failed workspace areas"
    });
    expect(diagnosticsLink).toHaveAttribute("href", "/settings#backend-capability-coverage");

    await user.click(diagnosticsLink);

    await waitFor(() => expect(document.getElementById("backend-capability-coverage")).not.toBeNull(), { timeout: 5000 });
    const capabilityCoverage = document.getElementById("backend-capability-coverage");
    expect(capabilityCoverage).not.toBeNull();
    await waitFor(() => expect(capabilityCoverage).toHaveFocus());
  });

  it("renders the Portfolio route from the fetched portfolio workspace payload", async () => {
    mockWorkstationData({
      session: {
        displayName: "Ops Desk",
        role: "Operator",
        environment: "paper",
        activeWorkspace: "portfolio",
        commandCount: 7
      },
      overview: null,
      strategy: null,
      trading: null,
      portfolio,
      data: null,
      accounting: null,
      reporting: null,
      brokerageConnection: null,
      providerConnections: null,
      brokeragePortfolio: null,
      workflowLibrary: null,
      workflowPresets: null,
      workflowSummary: null,
      workflowError: null,
      usingDevelopmentFixtures: false,
      loading: false,
      error: null,
      workspaceErrors: {},
      refresh: vi.fn(),
      refreshTrading: vi.fn(),
      refreshPortfolio: vi.fn(),
      upsertWorkflowPreset: vi.fn()
    });

    renderWithRouter(<App />, { initialEntries: ["/portfolio"] });

    const positionsTable = await screen.findByRole("treegrid", { name: /open positions/i }, { timeout: 5000 });
    expect(within(positionsTable).getByText("NVDA")).toBeInTheDocument();
    expect(screen.getAllByText("Portfolio workspace").length).toBeGreaterThan(0);
  });
});
