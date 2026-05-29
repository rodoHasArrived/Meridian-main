import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { App } from "@/app";
import { useWorkstationData } from "@/hooks/use-workstation-data";
import { renderWithRouter } from "@/test/render";
import { useNavigate } from "react-router-dom";
import type {
  DataOperationsWorkspaceResponse,
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
    markWorkflowPresetUsed: vi.fn().mockResolvedValue(undefined)
  };
});

const mockedUseWorkstationData = vi.mocked(useWorkstationData);
type WorkstationDataSnapshot = ReturnType<typeof useWorkstationData>;

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
    staleDiscardCount: 0,
    backoff: { attempt: 0, retryCount: 0, nextRetryDelayMs: null, maxRetries: 0 }
  };
}


function mockWorkstationData(overrides: Partial<WorkstationDataSnapshot>) {
  mockedUseWorkstationData.mockReturnValue({
    session: null,
    overview: null,
    research: null,
    trading: null,
    portfolio: null,
    dataOperations: null,
    governance: null,
    reporting: null,
    brokerageConnection: null,
    providerConnections: null,
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

describe("App", () => {
  beforeEach(() => {
    document.title = "Meridian";
    window.localStorage.clear();
    mockWorkstationData({
      session: {
        displayName: "Ops Desk",
        role: "Operator",
        environment: "paper",
        activeWorkspace: "trading",
        commandCount: 7
      },
      overview: null,
      research: null,
      trading: null,
      portfolio: null,
      dataOperations: null,
      governance: null,
      reporting: null,
      brokerageConnection: null,
      providerConnections: null,
      brokeragePortfolio: null,
      workflowLibrary: null,
      workflowPresets: null,
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

  it("opens and closes the command palette with Control+K", async () => {
    const user = userEvent.setup();
    renderWithRouter(<App />, { initialEntries: ["/trading"] });
    const trigger = screen.getByRole("button", { name: "Open workstation command palette (Ctrl K)" });

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
    renderWithRouter(<App />, { initialEntries: ["/trading"] });

    expect(screen.getByRole("link", { name: "Skip to workbench" })).toHaveAttribute("href", "#workbench-content");
    expect(screen.getByRole("main")).toHaveAttribute("id", "workbench-content");
  });

  it("redirects the root route to trading", async () => {
    renderWithRouter(<App />, { initialEntries: ["/"] });
    await waitFor(() => expect(screen.getByRole("heading", { name: "Trading Workstation" })).toBeInTheDocument());
  });

  it("renders build, environment, data-source, and provider trust in the masthead", () => {
    mockWorkstationData({
      session: {
        displayName: "Ops Desk",
        role: "Operator",
        environment: "paper",
        activeWorkspace: "data",
        commandCount: 7
      },
      dataOperations: {
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

    const strip = screen.getByRole("region", { name: "Workstation build, mode, data source, and provider posture" });
    expect(within(strip).getByLabelText("Build 0.1.0. Browser workstation bundle version.")).toHaveTextContent("Buildv0.1.0");
    expect(within(strip).getByLabelText("Mode Paper. Session Ops Desk is operating in paper mode.")).toHaveTextContent("ModePaper");
    expect(within(strip).getByLabelText("Data source Demo fixtures. No-host fixture payloads are visible; do not treat this as live operational readiness.")).toHaveTextContent("SourceDemo fixtures");
    expect(within(strip).getByRole("link", {
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

    const status = screen.getByRole("status", { name: "Booting workstation shell" });
    expect(status).toHaveAttribute("aria-busy", "true");
    expect(within(status).getByLabelText("Session state: resolving operator context and environment guardrails")).toBeInTheDocument();
    expect(within(status).getByLabelText("Workspace payloads: loading Trading, Portfolio, Accounting, Reporting, Strategy, Data, and Settings")).toBeInTheDocument();
    expect(within(status).getByLabelText("Evidence slices: preparing readiness, reconciliation, provider, and report-pack evidence")).toBeInTheDocument();
    expect(within(status).getByLabelText("Workspace bootstrap status")).toBeInTheDocument();
  });

  it("has no basic accessibility violations in the trading shell", async () => {
    const { container } = renderWithRouter(<App />, { initialEntries: ["/trading"] });

    const results = await axe(container);
    expect(results.violations).toHaveLength(0);
  });

  it("renders route-aware workflow continuity without relying on memorized navigation", () => {
    mockWorkstationData({
      session: {
        displayName: "Ops Desk",
        role: "Operator",
        environment: "paper",
        activeWorkspace: "data",
        commandCount: 7
      },
      overview,
      research: null,
      trading: null,
      portfolio: null,
      dataOperations: null,
      governance: null,
      reporting: null,
      brokerageConnection: null,
      providerConnections: null,
      brokeragePortfolio: null,
      workflowLibrary: null,
      workflowPresets: null,
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
    expect(screen.getByText("/data/quotes?symbol=MSFT")).toBeInTheDocument();
    expect(screen.getByRole("navigation", { name: "Market Data To Paper workflow steps" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Live quotes, current workflow step, Waiting" })).toHaveAttribute(
      "href",
      "/data/quotes?symbol=MSFT"
    );
    expect(screen.getByRole("link", { name: "Price alerts, next workflow step, Waiting" })).toHaveAttribute(
      "href",
      "/data/alerts?symbol=MSFT"
    );
    expect(screen.getByRole("link", { name: "Continue workflow to Price alerts" })).toHaveAttribute(
      "href",
      "/data/alerts?symbol=MSFT"
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
      research: null,
      trading: null,
      portfolio,
      dataOperations: null,
      governance: null,
      reporting: null,
      brokerageConnection: null,
      providerConnections: null,
      brokeragePortfolio: null,
      workflowLibrary: null,
      workflowPresets: null,
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
    expect(within(operatingScope).getByText("run-9")).toBeInTheDocument();
    expect(within(operatingScope).getByText("Provider")).toBeInTheDocument();
    expect(within(operatingScope).getByText("Alpaca")).toBeInTheDocument();
    expect(within(operatingScope).getByText("Window")).toBeInTheDocument();
    expect(within(operatingScope).getByText("2026-05-01 to 2026-05-15")).toBeInTheDocument();
    expect(window.localStorage.getItem("meridian.workstation.operatingContext.v1")).toContain("\"fundAccountId\":\"fund-1\"");

    await user.keyboard("{Control>}k{/Control}");
    expect(screen.getAllByText("Subject: MSFT / Account: fund-1 / Run: run-9 / Provider: Alpaca / Window: 2026-05-01 to 2026-05-15").length).toBeGreaterThan(0);
    expect(screen.getByRole("link", { name: "Open Trading workspace" })).toHaveAttribute(
      "href",
      "/trading?symbol=MSFT&fundAccountId=fund-1&runId=run-9&provider=Alpaca&from=2026-05-01&to=2026-05-15"
    );

    await user.keyboard("{Escape}");
    await user.click(screen.getByRole("button", {
      name: "Clear operating scope: Subject MSFT, Account fund-1, Run run-9, Provider Alpaca, Window 2026-05-01 to 2026-05-15"
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
      research: null,
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
      dataOperations: {
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
      } as unknown as DataOperationsWorkspaceResponse,
      governance: null,
      reporting: null,
      brokerageConnection: null,
      providerConnections: null,
      brokeragePortfolio: null,
      workflowLibrary: null,
      workflowPresets: null,
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
    expect(screen.getByRole("navigation", { name: "26 workstation commands" })).toBeInTheDocument();
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
      research: null,
      trading: null,
      portfolio: null,
      dataOperations: null,
      governance: null,
      reporting: null,
      brokerageConnection: null,
      providerConnections: null,
      brokeragePortfolio: null,
      workflowLibrary: null,
      workflowPresets: null,
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
      "/data/alerts?symbol=MSFT"
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
      research: null,
      trading: null,
      portfolio: null,
      dataOperations: null,
      governance: null,
      reporting: null,
      brokerageConnection: null,
      providerConnections: null,
      brokeragePortfolio: null,
      workflowLibrary: null,
      workflowPresets: null,
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
    expect(screen.getByLabelText("Accounting workspace, current route, Review")).toHaveAttribute("aria-current", "page");
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
    mockWorkstationData({
      session: {
        displayName: "Ops Desk",
        role: "Operator",
        environment: "paper",
        activeWorkspace: "settings",
        commandCount: 7
      },
      overview,
      research: null,
      trading: null,
      portfolio: null,
      dataOperations: null,
      governance: null,
      reporting: null,
      brokerageConnection: null,
      providerConnections: null,
      brokeragePortfolio: null,
      workflowLibrary: null,
      workflowPresets: null,
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
    expect(screen.getByRole("link", { name: /Provider setup, current workflow step/ })).toHaveAttribute(
      "aria-current",
      "step"
    );
    await waitFor(() => expect(alpacaSetup).toHaveFocus(), { timeout: 5000 });
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

  it("opens and closes the mobile workspace navigation drawer", async () => {
    const user = userEvent.setup();
    renderWithRouter(<App />, { initialEntries: ["/trading"] });

    expect(screen.queryByRole("dialog", { name: "Workspace navigation" })).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Open workspace navigation" }));
    const navigationDialog = screen.getByRole("dialog", { name: "Workspace navigation" });
    expect(navigationDialog).toBeInTheDocument();
    expect(within(navigationDialog).getByLabelText("Trading workspace, current route, Review")).toBeInTheDocument();

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
      research: null,
      trading: null,
      portfolio: null,
      dataOperations: null,
      governance: null,
      reporting: null,
      brokerageConnection: null,
      providerConnections: null,
      brokeragePortfolio: null,
      workflowLibrary: null,
      workflowPresets: null,
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

    expect(screen.getByRole("status", { name: "Workstation bootstrap is partially degraded" })).toBeInTheDocument();
    const diagnosticsLink = screen.getByRole("link", {
      name: "Review Settings capability coverage for failed workstation slices"
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
      research: null,
      trading: null,
      portfolio,
      dataOperations: null,
      governance: null,
      reporting: null,
      brokerageConnection: null,
      providerConnections: null,
      brokeragePortfolio: null,
      workflowLibrary: null,
      workflowPresets: null,
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

    const positionsTable = await screen.findByRole("table", { name: /open positions/i }, { timeout: 5000 });
    expect(within(positionsTable).getByText("NVDA")).toBeInTheDocument();
    expect(screen.getAllByText("Portfolio workspace").length).toBeGreaterThan(0);
  });
});
