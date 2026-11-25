import { act, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { StrictMode } from "react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { App } from "@/app";
import { captureLayoutState, upsertUserLayout } from "@/lib/saved-layouts";
import { useWorkstationData } from "@/hooks/use-workstation-data";
import { apiGetJson } from "@/lib/api";
import { getOperatorPreferences, getOperatorScopePreference } from "@/lib/api/operator-workstation.api";
import { getDisplayTimeZone, reloadDisplayTimeZone } from "@/lib/display-time-zone";
import { OPERATOR_TRIAGE_ENDPOINT, type OperatorTriageInbox } from "@/lib/operator-inbox-triage";
import { OPERATING_CONTEXT_STORAGE_KEY } from "@/lib/operating-scope/persistence";
import { WORKSTATION_API_ENDPOINTS } from "@/lib/workstation-endpoints";
import { appendRecordWorkspaceContext, buildRecordWorkspaceContext } from "@/lib/record-workspace-context";
import { readStoredOperatingScope, writeStoredOperatingScope } from "@/lib/operating-scope/persistence";
import { renderWithRouter } from "@/test/render";
import { useLocation, useNavigate } from "react-router-dom";
import type { FirstRunStatus } from "@/features/first-run/types";
import type {
  BrokerageHouseholdPortfolio,
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

vi.mock("@/lib/api/operator-workstation.api", () => ({
  getCrossScreenCounts: vi.fn().mockResolvedValue(null),
  getOperatorScopePreference: vi.fn().mockResolvedValue({ defaultScope: null, updatedAt: null }),
  saveOperatorScopePreference: vi.fn().mockResolvedValue({ defaultScope: null, updatedAt: null }),
  // Presentation preferences stay browser-only unless a test loads them from the host.
  getOperatorPreferences: vi.fn().mockRejectedValue(new Error("offline")),
  saveOperatorPreferences: vi.fn().mockRejectedValue(new Error("offline")),
  recordOperatorSurfaceVisit: vi.fn().mockRejectedValue(new Error("offline")),
  getOperatorSurfaceVisit: vi.fn().mockRejectedValue(new Error("offline"))
}));

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
    crossScreenCounts: null,
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
    figureSourceLoadedAt: {},
    figureSourcesStale: [],
    figureSourceScope: {},
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

const loadedOperatorInbox: OperatorTriageInbox = {
  asOf: "2026-05-14T21:30:00Z",
  actorId: "operator-1",
  persistenceAvailable: true,
  unavailableReason: null,
  items: [{
    workItemId: "promotion-review",
    label: "Promotion checklist incomplete",
    detail: "Portfolio and ledger continuity review must be finished before paper-operation readiness is accepted.",
    workspace: "Trading",
    targetRoute: "/trading/readiness",
    kind: "PromotionReview",
    tone: "Warning",
    createdAt: "2026-05-14T20:30:00Z",
    ownerId: null,
    dueAt: null,
    version: 1,
    sourceVersion: null,
    acknowledgedAt: null,
    snoozedUntil: null,
    capabilities: { canAssign: true, canAcknowledge: true, canSnooze: true, canUnsnooze: false, disabledReason: null },
    activity: []
  }]
};

const EMPTY_SCOPE_BAR_NAME = "Operating scope: All authorized scopes";

/** The permanent top-bar scope bar; its accessible name lists every labelled dimension. */
function scopeBar() {
  return screen.getByRole("button", { name: /^Operating scope: / });
}

async function openScopePicker(user: ReturnType<typeof userEvent.setup>) {
  await user.click(scopeBar());
  return screen.findByRole("dialog", { name: "Operating scope" });
}

/** Workflow context lives in the scope picker, collapsed under the scope controls. */
async function openShellContext(user: ReturnType<typeof userEvent.setup>) {
  const dialog = await openScopePicker(user);
  const summary = within(dialog).getByText("Workflow context").closest("summary")!;
  if (!summary.closest("details")?.open) await user.click(summary);
}

describe("App", () => {
  beforeEach(() => {
    document.title = "Meridian";
    window.localStorage.clear();
    window.sessionStorage.clear();
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

  it("keeps Views open when a pending Portfolio search flushes on blur", async () => {
    const user = userEvent.setup();
    function PendingSearch() {
      const navigate = useNavigate();
      return <input aria-label="Pending table search" onBlur={() => navigate("/portfolio?view=table-search", { replace: true })} />;
    }
    renderWithRouter(<><App /><PendingSearch /></>, { initialEntries: ["/portfolio"] });
    await user.click(screen.getByRole("textbox", { name: "Pending table search" }));
    await user.type(screen.getByRole("textbox", { name: "Pending table search" }), "AA");
    const views = screen.getByText("Views", { selector: "summary" });
    await user.click(views);
    await waitFor(() => expect(screen.getByRole("region", { name: "View controls" })).toBeVisible());
    expect(views.closest("details")).toHaveAttribute("open");
    expect(views).toHaveFocus();
  });

  it("shows the originating record in related work and keeps a quote symbol local to the destination", async () => {
    const user = userEvent.setup();
    mockWorkstationData({
      loading: false,
      session: { displayName: "Ops Desk", role: "Operator", environment: "paper", activeWorkspace: "data", commandCount: 7 }
    });
    writeStoredOperatingScope({ symbol: "MSFT", fundAccountId: "current-account" });
    const context = buildRecordWorkspaceContext({
      kind: "instrument", id: "security-a", name: "Example Equity", assetClass: "Equity", status: "Active",
      asOf: "2026-10-28", trust: "Identity on file", scope: { securityId: "security-a", symbol: "DEMO" }
    }, {
      pathname: "/portfolio/asset-detail", search: "?securityId=security-a&provider=Alpaca", hash: ""
    }, {})!;
    const route = appendRecordWorkspaceContext("/data/quotes?symbol=DEMO", context)!;
    renderWithRouter(<App />, { initialEntries: [route] });

    const origin = within(await screen.findByRole("region", { name: "Originating record" }));
    expect(origin.getByText("Instrument from Portfolio")).toBeVisible();
    expect(origin.getByText("Example Equity")).toBeVisible();
    origin.getAllByText("security-a").forEach((element) => expect(element).not.toBeVisible());
    const technicalIdentity = origin.getByText("Technical identity");
    await user.click(technicalIdentity);
    expect(within(technicalIdentity.closest("details")!).getByText("security-a")).toBeVisible();
    expect(origin.getByText("Identity on file")).toBeVisible();
    expect(origin.getByText("2026-10-28")).toBeVisible();
    expect(origin.getByRole("link", { name: "Return to Portfolio" })).toHaveAttribute("href", context.origin);
    expect(screen.getByRole("region", { name: "Operating scope bar" })).not.toHaveTextContent("current-account");
    await user.click(origin.getByText("Origin scope"));
    expect(origin.getByText("current-account")).toBeVisible();
    expect(readStoredOperatingScope().symbol).toBe("MSFT");

    await user.click(origin.getByRole("link", { name: "Dismiss originating record" }));
    expect(screen.queryByRole("region", { name: "Originating record" })).not.toBeInTheDocument();
    expect(readStoredOperatingScope().symbol).toBe("MSFT");
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
    expect(screen.queryByRole("button", { name: "Go to route, action, evidence... Ctrl K" }))
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

    expect(await screen.findByRole("button", { name: "Go to route, action, evidence... Ctrl K" }))
      .toBeInTheDocument();
    expect(firstRunRequests).toBe(2);
    expect(mockedUseWorkstationData).toHaveBeenCalled();
  });

  it("keeps a failed provenance probe as UNKNOWN with a retry control, never a SIMULATED brand", async () => {
    let allowDemoMode = false;
    let demoModeRequests = 0;
    vi.mocked(apiGetJson).mockImplementation((path) => {
      if (path === WORKSTATION_API_ENDPOINTS.firstRunStatus) {
        return resolveSynchronously(completedFirstRunStatus) as ReturnType<typeof apiGetJson>;
      }

      if (path === WORKSTATION_API_ENDPOINTS.demoMode) {
        demoModeRequests += 1;
        return allowDemoMode
          ? Promise.resolve({ enabled: false, provenance: "real" }) as ReturnType<typeof apiGetJson>
          : Promise.reject(new Error("Demo-mode endpoint unavailable."));
      }

      return Promise.reject(new Error(`No test response configured for ${path}`));
    });
    mockWorkstationData({ loading: false, usingDevelopmentFixtures: false });

    renderWithRouter(<App />, { initialEntries: ["/trading"] });

    expect(await screen.findByTestId("data-provenance-unknown")).toBeVisible();
    expect(screen.queryByTestId("data-provenance-simulated")).toBeNull();
    const trust = screen.getByRole("region", { name: "Workstation build, environment, provenance, and provider health" });
    await userEvent.click(trust.querySelector("summary") as HTMLElement);
    const retry = await within(trust).findByRole("button", {
      name: "Retry live Meridian workspace data"
    });
    allowDemoMode = true;
    await userEvent.click(retry);

    await waitFor(() => {
      expect(screen.queryByTestId("data-provenance-unknown")).not.toBeInTheDocument();
    });
    expect(demoModeRequests).toBeGreaterThanOrEqual(2);
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
      name: "Workstation build, environment, provenance, and provider health"
    });
    expect(strip).toHaveAttribute("aria-live", "polite");
    await userEvent.click(strip.querySelector("summary") as HTMLElement);
    const trustDetails = within(strip).getByRole("group", {
      name: "Environment, provenance, and provider details"
    });
    expect(within(trustDetails).getByRole("link", { name: /Data provenance SEEDED/ }))
      .toHaveTextContent("ProvenanceSEEDED");
    // W9-TRUTH-001: the non-dismissable provenance chip stays visible in the single status control.
    expect(within(strip).getByTestId("data-provenance-seeded")).toBeVisible();
    expect(within(strip).queryByRole("button", { name: /dismiss/i })).not.toBeInTheDocument();
  });

  it("applies the operator's default scope to an unscoped session under StrictMode (C1)", async () => {
    window.localStorage.removeItem(OPERATING_CONTEXT_STORAGE_KEY);
    // Like fetch, the read rejects once its signal aborts.
    vi.mocked(getOperatorScopePreference).mockImplementation((options = {}) => new Promise((resolve, reject) => {
      const timer = setTimeout(() => resolve({ defaultScope: { symbol: "MSFT" }, updatedAt: "2026-11-06T09:00:00Z" }), 0);
      options.signal?.addEventListener("abort", () => {
        clearTimeout(timer);
        reject(new DOMException("The read was aborted.", "AbortError"));
      });
    }));
    renderWithRouter(<StrictMode><App /></StrictMode>, { initialEntries: ["/trading"] });
    // StrictMode replays the effect: the first read is aborted, and the replay must still load it.
    await waitFor(() => expect(window.localStorage.getItem(OPERATING_CONTEXT_STORAGE_KEY) ?? "").toContain("MSFT"));
    expect(vi.mocked(getOperatorScopePreference).mock.calls.length).toBeGreaterThanOrEqual(2);
    vi.mocked(getOperatorScopePreference).mockReset().mockResolvedValue({ defaultScope: null, updatedAt: null });
    window.localStorage.removeItem(OPERATING_CONTEXT_STORAGE_KEY);
  });

  it("keeps the operator's default scope out of a record search link", async () => {
    window.localStorage.removeItem(OPERATING_CONTEXT_STORAGE_KEY);
    vi.mocked(getOperatorScopePreference).mockResolvedValue({
      defaultScope: { symbol: "MSFT" },
      updatedAt: "2026-11-06T09:00:00Z"
    });

    await act(async () => {
      renderWithRouter(<App />, { initialEntries: ["/portfolio?recordScope=1"] });
    });

    expect(getOperatorScopePreference).toHaveBeenCalled();
    expect(readStoredOperatingScope()).toEqual({});
  });

  it("loads the signed-in operator's host preferences and applies them to this browser", async () => {
    window.localStorage.removeItem("meridian.workstation.display-time-zone.v1");
    reloadDisplayTimeZone();
    vi.mocked(getOperatorPreferences).mockResolvedValue({
      preferences: { appearance: "dark", density: "compact", displayTimeZone: "Europe/London", pageDescriptionsHidden: false, keyBindings: null },
      updatedAt: "2026-11-17T09:00:00Z"
    });
    mockWorkstationData({
      loading: false,
      usingDevelopmentFixtures: false,
      session: { displayName: "Ops Desk", role: "Operator", environment: "paper", activeWorkspace: "trading", commandCount: 7 }
    });

    renderWithRouter(<App />, { initialEntries: ["/trading"] });

    await waitFor(() => expect(getDisplayTimeZone()).toBe("Europe/London"));
    expect(document.documentElement.getAttribute("data-theme")).toBe("dark");
    expect(document.body.getAttribute("data-theme-density")).toBe("compact");

    vi.mocked(getOperatorPreferences).mockReset().mockRejectedValue(new Error("offline"));
    window.localStorage.removeItem("meridian.workstation.display-time-zone.v1");
    window.localStorage.removeItem("meridian.workstation.appearance.v1");
    window.localStorage.removeItem("meridian.workstation.density.v1");
    reloadDisplayTimeZone();
    document.documentElement.removeAttribute("data-theme");
    document.body.removeAttribute("data-theme-density");
  });

  it("does not read host preferences for a fixture session", async () => {
    vi.mocked(getOperatorPreferences).mockClear();
    mockWorkstationData({
      loading: false,
      usingDevelopmentFixtures: true,
      session: { displayName: "Ops Desk", role: "Operator", environment: "paper", activeWorkspace: "trading", commandCount: 7 }
    });

    renderWithRouter(<App />, { initialEntries: ["/trading"] });
    await screen.findByRole("main");

    expect(getOperatorPreferences).not.toHaveBeenCalled();
  });

  it("keeps an operator's explicit empty scope when the default loads late (C1)", async () => {
    window.localStorage.removeItem(OPERATING_CONTEXT_STORAGE_KEY);
    let release!: () => void;
    vi.mocked(getOperatorScopePreference).mockImplementation(() => new Promise((resolve) => {
      release = () => resolve({ defaultScope: { symbol: "MSFT" }, updatedAt: "2026-11-06T09:00:00Z" });
    }));
    const user = userEvent.setup();
    renderWithRouter(<App />, { initialEntries: ["/trading"] });

    // The operator applies an empty scope while the default is still loading.
    await openScopePicker(user);
    await user.click(screen.getByRole("button", { name: "Clear scope" }));
    await user.click(screen.getByRole("button", { name: "Apply scope" }));
    await act(async () => release());

    expect(window.localStorage.getItem(OPERATING_CONTEXT_STORAGE_KEY) ?? "").not.toContain("MSFT");
    expect(scopeBar()).toHaveAccessibleName(EMPTY_SCOPE_BAR_NAME);
    vi.mocked(getOperatorScopePreference).mockReset().mockResolvedValue({ defaultScope: null, updatedAt: null });
  });

  it("offers saved views in the command palette (C4)", async () => {
    upsertUserLayout({
      id: "month-end-breaks",
      name: "Month-end breaks",
      description: null,
      source: "user",
      state: captureLayoutState({
        route: "/accounting/reconciliation?reconciliationStatus=Open&reconciliationColumns=owner",
        operatingScope: { fundAccountId: "fund-a" },
        density: "compact",
        panes: []
      })
    });
    const user = userEvent.setup();
    renderWithRouter(<App />, { initialEntries: ["/trading"] });

    await user.keyboard("{Control>}k{/Control}");
    const dialog = screen.getByRole("dialog", { name: "Open workstation command" });
    expect(await within(dialog).findByText("Open saved view Month-end breaks")).toBeInTheDocument();
    window.localStorage.removeItem("meridian.workstation.layouts.v1");
  });

  it("opens and closes the command palette with Control+K", async () => {
    const user = userEvent.setup();
    renderWithRouter(<App />, { initialEntries: ["/trading"] });
    const trigger = screen.getByRole("button", { name: "Go to route, action, evidence... Ctrl K" });
    expect(trigger).toHaveAccessibleDescription("Open workstation command palette (Ctrl K). Keyboard shortcut: Ctrl K.");

    expect(document.querySelector('[data-design-system-component="Masthead"]')).toHaveClass("mds-masthead");
    expect(trigger).toHaveClass("mds-masthead__search");
    // The hazard stripe is scoped to a LIVE session with non-real data; the masthead says which
    // environment it is in so the stripe stays off for paper and demo sessions.
    expect(document.querySelector('[data-design-system-component="Masthead"]')).toHaveAttribute("data-environment");
    expect(document.querySelector('[data-design-system-component="Masthead"]')).not.toHaveAttribute("data-environment", "live");
    expect(screen.queryByRole("dialog", { name: "Open workstation command" })).not.toBeInTheDocument();
    expect(trigger).toHaveAttribute("aria-controls", "command-palette-dialog");
    expect(trigger).toHaveAttribute("aria-expanded", "false");
    expect(trigger).toHaveAttribute("aria-haspopup", "dialog");

    await user.keyboard("{Control>}k{/Control}");
    const dialog = screen.getByRole("dialog", { name: "Open workstation command" });
    expect(dialog).toHaveAttribute("id", "command-palette-dialog");
    expect(trigger).toHaveAttribute("aria-expanded", "true");
    expect(trigger).toHaveAccessibleName("Go to route, action, evidence... Ctrl K");
    expect(trigger).toHaveAccessibleDescription("Close workstation command palette (Ctrl K). Keyboard shortcut: Ctrl K.");

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

  it.each(["/", "/today"])("renders the shared Today queue at %s", async (route) => {
    const user = userEvent.setup();
    mockDailyControlTowerData();
    // Load the shared operator inbox too, so both sources render in Today's one queue grid.
    const defaultApiGetJson = vi.mocked(apiGetJson).getMockImplementation()!;
    vi.mocked(apiGetJson).mockImplementation((path, ...rest) => path === OPERATOR_TRIAGE_ENDPOINT
      ? Promise.resolve(loadedOperatorInbox) as ReturnType<typeof apiGetJson>
      : defaultApiGetJson(path, ...rest));

    const { container } = renderWithRouter(<App />, { initialEntries: [route] });

    expect(await screen.findByRole(
      "heading",
      { name: "What needs an operator decision now" },
      { timeout: 10_000 }
    )).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Today", level: 1 })).toBeInTheDocument();
    expect(screen.getAllByRole("heading", { level: 1 })).toHaveLength(1);
    // The header and Today word an unscoped view the same way: everything authorized.
    expect(scopeBar()).toHaveAccessibleName(EMPTY_SCOPE_BAR_NAME);
    const confidence = screen.getByRole("region", { name: "Daily control tower confidence" });
    // Scope is stated once, in the header bar; Today no longer repeats it.
    expect(within(confidence).queryByText("Scope")).not.toBeInTheDocument();
    // The selected item's evidence time is in its detail rail; the queue says why it is ranked there.
    expect(within(confidence).queryByText("Selected evidence")).not.toBeInTheDocument();
    expect(screen.getByTestId("today-rank-reason")).toHaveTextContent(/^Why ranked here: Ranked \d+ of \d+\. Sorted by severity/);
    expect(within(screen.getByRole("region", { name: "Priority decision" })).getByRole("region", { name: "Workstation status" })).toHaveAttribute("data-status-tone", "blocked");
    expect(screen.getByTestId("today-attention-count")).toHaveTextContent(/^\d+ needs? attention · blocked \+ action, excluding snoozed/);
    const trust = screen.getByRole("region", { name: "Workstation build, environment, provenance, and provider health" });
    await user.click(trust.querySelector("summary") as HTMLElement);
    expect(within(trust).getByRole("link", { name: "Blocked Brokerage sync failed" }))
      .toHaveAttribute("href", "/");
    expect(within(trust).getByRole("link", { name: "Blocked Brokerage sync failed" }))
      .toHaveAccessibleDescription(expect.stringContaining("Open Today"));
    await user.click(trust.querySelector("summary") as HTMLElement);
    const status = within(screen.getByRole("region", { name: "Priority decision" })).getByRole("region", { name: "Workstation status" });
    expect(within(status).getByTestId("status-row-issues")).toHaveTextContent("Decisions:Brokerage sync failed");
    ["Decisions", "Providers", "Provenance", "Workspace data"].forEach((label) => {
      expect(within(status.querySelector("dl") as HTMLElement).getByText(label)).toBeInTheDocument();
    });
    // The masthead names the issue; the footer holds connectivity and freshness only (P1).
    expect(within(screen.getByRole("contentinfo", { name: "Workstation status" })).queryByText(/Brokerage sync failed/))
      .not.toBeInTheDocument();

    expect(screen.queryByRole("region", { name: "Daily control tower decision drivers" })).not.toBeInTheDocument();
    expect(screen.queryByRole("region", { name: "Choose Control Tower scope" })).not.toBeInTheDocument();
    expect(confidence).not.toHaveTextContent("All authorized scopes");
    const todayQueue = await screen.findByRole("treegrid", { name: "Today queue" });
    expect(todayQueue).toBeInTheDocument();
    // One grid holds both sources, and one count covers it: the sidebar Today badge, the count
    // beside the Queue heading and the rows on screen are the same 4 workspace signals + 1
    // shared-inbox item.
    expect(within(screen.getByRole("region", { name: "Queue" })).getByRole("treegrid", { name: "Today queue" }))
      .toBe(todayQueue);
    expect(screen.getAllByRole("treegrid")).toHaveLength(1);
    expect(todayQueue.querySelectorAll('tbody tr[data-dense-row-id^="workspace:"]')).toHaveLength(4);
    const todayLinks = await screen.findAllByRole("link", { name: /^Today, Daily Control Tower, current route, 5 items need attention/ });
    expect(todayLinks.length).toBeGreaterThan(0);
    for (const link of todayLinks) {
      expect(link.parentElement?.querySelector(".operator-nav-count")).toHaveTextContent("5");
      // The badge lets the pointer through to the link, so the link's tooltip names the unit.
      expect(link).toHaveAttribute("title", "Today: Daily Control Tower · 5 items in Today's finance queue");
    }
    expect(screen.getByTestId("today-attention-count")).toHaveTextContent("5 need attention · blocked + action, excluding snoozed");
    expect(within(screen.getByRole("tablist", { name: "Queue source" })).getByRole("tab", { name: /^All/ })).toHaveTextContent("5");
    expect(todayQueue.querySelectorAll("tbody tr[data-dense-row-id]")).toHaveLength(5);
    const inboxRow = within(todayQueue).getByRole("row", { name: "Promotion checklist incomplete" });
    expect(inboxRow).toHaveTextContent("Shared inbox");
    expect(todayQueue.querySelectorAll('tbody tr[data-dense-row-id^="inbox:"]')).toHaveLength(1);
    // With both sources in one grid, each row name stays unique on the page.
    expect(screen.getAllByRole("row", { name: "Brokerage sync failed" })).toHaveLength(1);
    expect(screen.getAllByRole("link", {
      name: "Reporting: Report pack approval waiting. Monthly board pack still needs an operator sign-off. Open report packs."
    }).some((link) => link.getAttribute("href") === "/reporting/report-packs")).toBe(true);

    // The evidence pane opens on the leading (worst) decision; any other row is one click away.
    expect(screen.getByRole("region", { name: /Brokerage sync failed evidence summary/i })).toBeInTheDocument();
    await user.click(screen.getByRole("row", { name: "Report pack approval waiting" }));
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
    await waitFor(() => expect(document.title).toBe("Today - Meridian"));
  });

  it("redirects the legacy overview route to the daily control tower", async () => {
    mockDailyControlTowerData();

    renderWithRouter(<App />, { initialEntries: ["/overview"] });

    await waitFor(() => expect(screen.getByRole("heading", { name: "What needs an operator decision now" })).toBeInTheDocument());
    await waitFor(() => expect(document.title).toBe("Today - Meridian"));
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
    expect(container.querySelector(".workflow-continuity-operator-flow")).not.toBeInTheDocument();
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
    expect(screen.queryByText("Data", { selector: ".sub" })).not.toBeInTheDocument();
    expect(screen.getByRole("heading", { level: 1 })).toHaveTextContent("Providers");
    const strip = screen.getByRole("region", { name: "Workstation build, environment, provenance, and provider health" });
    await userEvent.click(strip.querySelector("summary") as HTMLElement);
    const trustDetails = within(strip).getByRole("group", {
      name: "Environment, provenance, and provider details"
    });
    expect(within(trustDetails).getByLabelText("Build 0.1.0. Current Meridian web release."))
      .toHaveTextContent("Buildv0.1.0");
    expect(within(trustDetails).getByLabelText(/^Environment Demo\./)).toHaveTextContent("EnvironmentDemo");
    expect(within(trustDetails).getByRole("link", { name: /Data provenance SEEDED/ }))
      .toHaveTextContent("ProvenanceSEEDED");
    expect(within(trustDetails).getByRole("link", {
      name: "Providers 1 degraded. 1 provider degraded; open Data providers before trading decisions. Open provider health."
    })).toHaveAttribute("href", "/data/providers");
  });

  it("groups current session context for assistive technology", async () => {
    renderWithRouter(<App />, { initialEntries: ["/trading"] });

    const sessionContext = screen.getByRole("group", {
      name: "Current session: Ops Desk, Operator"
    });
    expect(within(sessionContext).getByText("Operator")).toBeInTheDocument();
    expect(within(sessionContext).getByText("Ops Desk")).toBeInTheDocument();
    expect(within(sessionContext).getByText("Operator")).toBeInTheDocument();
    // The environment badge moved into the trust pill so the masthead fits one row; it
    // leads that pill and is still announced through the session group's name above.
    const strip = screen.getByRole("region", { name: "Workstation build, environment, provenance, and provider health" });
    expect(await within(strip).findByText("PAPER")).toHaveAttribute("data-environment", "paper");
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

    expect(screen.getByRole("region", { name: "Operating scope bar" })).toBeInTheDocument();
    expect(scopeBar()).toHaveAccessibleName("Operating scope: Subject MSFT");
    expect(screen.queryByText("/data/quotes?symbol=MSFT")).not.toBeInTheDocument();
    // Workflow context lives in the scope chip's popover, not in the header row.
    expect(screen.queryByText("Workflow context")).not.toBeInTheDocument();
    expect(screen.queryByText("Import → Validate → Reconcile → Investigate → Approve → Report")).not.toBeInTheDocument();
    expect(screen.queryByRole("navigation", { name: "Market Data To Paper workflow steps" })).not.toBeInTheDocument();

    await openShellContext(user);

    expect(screen.getByText("Current workflow")).toBeInTheDocument();
    expect(screen.getByText("Import → Validate → Reconcile → Investigate → Approve → Report")).toBeInTheDocument();
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
    expect(screen.getByRole("dialog", { name: "Operating scope" })).toBeInTheDocument();
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

    const operatingScope = scopeBar();
    expect(within(operatingScope).getByText("Subject")).toBeInTheDocument();
    expect(within(operatingScope).getByText("MSFT")).toBeInTheDocument();
    expect(within(operatingScope).getByText("Account")).toBeInTheDocument();
    expect(within(operatingScope).getByText("fund-1")).toBeInTheDocument();
    expect(within(operatingScope).getByText("Run")).toBeInTheDocument();
    expect(within(operatingScope).getByText("Selected run")).toBeInTheDocument();
    expect(within(operatingScope).queryByText("run-9")).not.toBeInTheDocument();
    expect(within(operatingScope).getByText("Alpaca").closest("[title]")).toHaveAttribute("title", expect.stringMatching(/^Provider: /));
    expect(within(operatingScope).getByText("2026-05-01 to 2026-05-15").closest("[title]")).toHaveAttribute("title", expect.stringMatching(/^Period: /));
    expect(window.localStorage.getItem("meridian.workstation.operatingContext.v1")).toContain("\"fundAccountId\":\"fund-1\"");

    await user.keyboard("{Control>}k{/Control}");
    expect(screen.getAllByText("Subject: MSFT / Account: fund-1 / Run: Selected run / Provider: Alpaca / Window: 2026-05-01 to 2026-05-15").length).toBeGreaterThan(0);
    expect(screen.getByRole("link", { name: "Open Trading workspace" })).toHaveAttribute(
      "href",
      "/trading?symbol=MSFT&fundAccountId=fund-1&runId=run-9&provider=Alpaca&from=2026-05-01&to=2026-05-15"
    );

    await user.keyboard("{Escape}");
    await openScopePicker(user);
    await user.click(screen.getByRole("button", { name: "Clear scope" }));
    expect(window.localStorage.getItem("meridian.workstation.operatingContext.v1")).toContain("fund-1");
    await user.click(screen.getByRole("button", { name: "Apply scope" }));

    expect(window.localStorage.getItem("meridian.workstation.operatingContext.v1")).toBeNull();
    expect(scopeBar()).toHaveAccessibleName(EMPTY_SCOPE_BAR_NAME);
  });

  it("removes a cleared scope dimension from the URL and storage without restoring it from the old route", async () => {
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
      portfolio,
      loading: false
    });
    function LocationProbe() {
      const location = useLocation();
      return <output aria-label="Current route">{location.pathname}{location.search}</output>;
    }
    renderWithRouter(<><App /><LocationProbe /></>, {
      initialEntries: ["/portfolio?symbol=MSFT&fundAccountId=fund-1&provider=Alpaca"]
    });

    const storageKey = "meridian.workstation.operatingContext.v1";
    expect(JSON.parse(window.localStorage.getItem(storageKey) ?? "{}")).toMatchObject({
      symbol: "MSFT", fundAccountId: "fund-1", provider: "Alpaca"
    });
    await openScopePicker(user);
    await user.clear(screen.getByLabelText("Fund account"));
    await user.click(screen.getByRole("button", { name: "Apply scope" }));

    await waitFor(() => {
      const route = screen.getByLabelText("Current route").textContent ?? "";
      const params = new URLSearchParams(route.split("?")[1]);
      expect(params.has("fundAccountId")).toBe(false);
      expect(params.get("symbol")).toBe("MSFT");
      expect(params.get("provider")).toBe("Alpaca");
      const stored = JSON.parse(window.localStorage.getItem(storageKey) ?? "{}");
      expect(stored).toMatchObject({ symbol: "MSFT", provider: "Alpaca" });
      expect(stored).not.toHaveProperty("fundAccountId");
    });
    expect(scopeBar()).not.toHaveTextContent("fund-1");
    expect(scopeBar()).not.toHaveTextContent("Account");
  });

  it("picks the fund account in the scope bar on Orders and carries it in the URL and on links (P4)", async () => {
    const user = userEvent.setup();
    const fundAccountId = "11111111-2222-3333-4444-555555555555";
    mockWorkstationData({
      session: { displayName: "Ops Desk", role: "Operator", environment: "paper", activeWorkspace: "trading", commandCount: 7 },
      overview,
      brokeragePortfolio: { accounts: [{ fundAccountId, displayName: "Main fund" }] } as BrokerageHouseholdPortfolio,
      trading: {
        metrics: [{ id: "m1", label: "Open Orders", value: "4", delta: "", tone: "default" }],
        positions: [],
        openOrders: [],
        fills: [],
        risk: { state: "Observe", summary: "Guardrails are active.", netExposure: "$0", grossExposure: "$0", var95: "$0", maxDrawdown: "0%", buyingPowerUsed: "0%", activeGuardrails: [] },
        brokerage: { provider: "Interactive Brokers", account: "DU1009034", environment: "paper", connection: "Connected", lastHeartbeat: "2s ago", orderIngress: "healthy", fillFeed: "healthy", notes: "" }
      } as TradingWorkspaceResponse,
      loading: false
    });
    function LocationProbe() {
      const location = useLocation();
      return <output aria-label="Current route">{location.pathname}{location.search}</output>;
    }
    const view = renderWithRouter(<><App /><LocationProbe /></>, { initialEntries: ["/trading/orders?symbol=MSFT"] });

    const recovery = await screen.findByRole("region", { name: "Brokerage recovery" }, { timeout: 10_000 });
    expect(within(recovery).getByRole("status", { name: "Brokerage recovery readiness" })).toHaveTextContent("No fund account in scope");
    const blotter = screen.getByRole("region", { name: "Order blotter and selected detail" });
    const journal = screen.getByRole("region", { name: "Operational trade journal" });
    expect(recovery.compareDocumentPosition(blotter) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(blotter.compareDocumentPosition(journal) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(journal).toHaveTextContent("Select an account to see its journal");
    // The page carries no picker of its own: the account step is inside the header scope bar.
    expect(within(recovery).queryByLabelText("Fund account")).not.toBeInTheDocument();
    expect(recovery).toHaveTextContent("Choose the account in the scope bar above.");
    const scopeRegion = screen.getByRole("region", { name: "Operating scope bar" });
    await user.type(within(scopeRegion).getByRole("combobox", { name: "Account for this workspace" }), "Main fund");
    await user.keyboard("{Enter}");

    await waitFor(() => {
      const params = new URLSearchParams((screen.getByLabelText("Current route").textContent ?? "").split("?")[1]);
      expect(params.get("fundAccountId")).toBe(fundAccountId);
      // Only the account changed; the rest of the scope is kept.
      expect(params.get("symbol")).toBe("MSFT");
    });
    expect(scopeBar()).toHaveAccessibleName(`Operating scope: Account ${fundAccountId}, Subject MSFT`);
    expect(screen.queryByLabelText("Fund account")).not.toBeInTheDocument();
    // Links built from the scope carry it, so a followed or copied link opens in the same scope.
    const portfolioLink = within(screen.getAllByRole("navigation").find((nav) => within(nav).queryByRole("link", { name: /Portfolio/ }))!)
      .getAllByRole("link", { name: /Portfolio/ })[0]!;
    expect(new URLSearchParams(portfolioLink.getAttribute("href")!.split("?")[1]).get("fundAccountId")).toBe(fundAccountId);
    expect(JSON.parse(window.localStorage.getItem(OPERATING_CONTEXT_STORAGE_KEY)!)).toMatchObject({ fundAccountId, symbol: "MSFT" });
    view.unmount();
    renderWithRouter(<App />, { initialEntries: ["/trading/orders"] });
    await waitFor(() => expect(scopeBar()).toHaveAccessibleName(new RegExp(`Account ${fundAccountId}`)));
    expect(screen.queryByRole("combobox", { name: "Fund account" })).not.toBeInTheDocument();
  });

  it("carries the tab's fund scope into a route opened without it, and keeps tabs independent (Phase 1)", async () => {
    mockWorkstationData({
      session: { displayName: "Ops Desk", role: "Operator", environment: "paper", activeWorkspace: "portfolio", commandCount: 7 },
      overview,
      loading: false
    });
    function LocationProbe() {
      const location = useLocation();
      return <output aria-label="Current route">{location.pathname}{location.search}</output>;
    }
    // Another tab last used fund-b; this tab chose fund-a.
    window.localStorage.setItem(OPERATING_CONTEXT_STORAGE_KEY, JSON.stringify({ fundId: "fund-b" }));
    window.sessionStorage.setItem(OPERATING_CONTEXT_STORAGE_KEY, JSON.stringify({ entityId: "entity-1", fundId: "fund-a" }));
    const view = renderWithRouter(<><App /><LocationProbe /></>, { initialEntries: ["/portfolio?view=household"] });
    await waitFor(() => {
      const params = new URLSearchParams((screen.getByLabelText("Current route").textContent ?? "").split("?")[1]);
      expect(params.get("fundId")).toBe("fund-a");
      expect(params.get("entityId")).toBe("entity-1");
      expect(params.get("view")).toBe("household");
    });
    view.unmount();

    // A new tab starts from the last scope used in any tab.
    window.sessionStorage.clear();
    renderWithRouter(<><App /><LocationProbe /></>, { initialEntries: ["/portfolio"] });
    await waitFor(() => {
      const params = new URLSearchParams((screen.getByLabelText("Current route").textContent ?? "").split("?")[1]);
      expect(params.get("fundId")).toBe("fund-b");
    });
  });

  it("prints the scope on a figures strip inside the shell without per-screen wiring (P4)", async () => {
    mockWorkstationData({
      session: { displayName: "Ops Desk", role: "Operator", environment: "paper", activeWorkspace: "data", commandCount: 7 },
      overview,
      data: {
        metrics: [{ id: "providers", label: "Providers", value: "3", delta: "", tone: "default" }],
        providers: [],
        backfills: [],
        exports: []
      } as unknown as DataWorkspaceResponse,
      loading: false
    });

    renderWithRouter(<App />, { initialEntries: ["/data/providers?fundAccountId=fund-1"] });

    const footnote = (await screen.findAllByTestId("stat-strip-provenance", {}, { timeout: 10_000 }))[0]!;
    // Data figures cover loaded accounts and must not borrow the selected account scope.
    expect(footnote).toHaveTextContent("Scope: All loaded accounts");
    expect(footnote).not.toHaveTextContent("fund-1");
    expect(scopeBar()).toHaveTextContent("fund-1");
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
    }).map((link) => link.getAttribute("href"))).toContain("/settings?fundAccountId=fund-1&recordScope=1#alpaca-provider-setup");
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

    expect(scopeBar()).toHaveTextContent("MSFT");
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
    await openScopePicker(user);
    await user.click(screen.getByRole("button", { name: "Clear scope" }));
    await user.click(screen.getByRole("button", { name: "Apply scope" }));

    expect(scopeBar()).toHaveAccessibleName(EMPTY_SCOPE_BAR_NAME);
    expect(window.localStorage.getItem("meridian.workstation.operatingContext.v1")).toBeNull();
  });

  it("sets the workstation document title on first direct route load without moving focus", async () => {
    renderWithRouter(<App />, { initialEntries: ["/settings"] });

    await waitFor(() => expect(document.title).toBe("Settings - Meridian"));
    expect(screen.getByRole("main")).not.toHaveFocus();
    expect(screen.queryByText("Settings loaded.")).not.toBeInTheDocument();
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

    await waitFor(() => expect(document.title).toBe("Accounting - Meridian"));
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

    await waitFor(() => expect(document.title).toBe("Strategy - Meridian"));
    expect(screen.getByLabelText("Strategy workspace, active section, Available product maturity")).toBeInTheDocument();

    document.title = "Meridian";
    await user.click(screen.getByRole("button", { name: "Open legacy governance" }));

    await waitFor(() => expect(document.title).toBe("Accounting - Meridian"));
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

    expect(await screen.findByText("Portfolio loaded.")).toBeInTheDocument();
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

    expect(await screen.findByText("Settings loaded. Jumping to alpaca provider setup.")).toBeInTheDocument();
    await waitFor(() => expect(document.getElementById("alpaca-provider-setup")).not.toBeNull(), { timeout: 15000 });
    const alpacaSetup = document.getElementById("alpaca-provider-setup");
    await waitFor(() => expect(alpacaSetup).toHaveFocus(), { timeout: 5000 });
    await openShellContext(user);
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

    // jsdom 31+ applies @media rules at its fixed 1024px viewport, where the desktop rail
    // replaces this toggle (display: none above 981px), so role queries see no accessible
    // name. Reach the narrow-layout control by its label; the drawer behaviour is under test.
    const toggle = screen.getByLabelText("Open workspace navigation", { selector: "button" });
    expect(toggle).toHaveAttribute("aria-haspopup", "dialog");
    await user.click(toggle);
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
