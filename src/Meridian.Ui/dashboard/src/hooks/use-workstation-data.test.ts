import { act, renderHook, waitFor } from "@testing-library/react";
import { createElement, StrictMode, type ReactNode } from "react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { useWorkstationData } from "@/hooks/use-workstation-data";
import * as api from "@/lib/api";
import { createApiErrorFromResponseBody } from "@/lib/api-errors";
import type {
  BrokerageConnectionStatus,
  BrokerageHouseholdPortfolio,
  DataOperationsWorkspaceResponse,
  GovernanceWorkspaceResponse,
  PortfolioWorkspaceResponse,
  ProviderConnectionRow,
  ProviderRoutingBinding,
  ProviderRoutingConnection,
  ProviderRoutingTrustSnapshot,
  ResearchWorkspaceResponse,
  SessionInfo,
  SystemOverviewResponse,
  TradingWorkspaceResponse,
  WorkflowLibrary,
  WorkflowPreset,
  WorkflowPresetLibrary
} from "@/types";

vi.mock("@/lib/api", () => ({
  getBrokerageHouseholdPortfolio: vi.fn(),
  getDataWorkspace: vi.fn(),
  getGovernanceWorkspace: vi.fn(),
  getAlpacaConnectionStatus: vi.fn(),
  getProviderConnections: vi.fn(),
  getProviderRoutingBindings: vi.fn(),
  getProviderRoutingConnections: vi.fn(),
  getProviderRoutingTrustSnapshots: vi.fn(),
  hasDevelopmentFixtureUsage: vi.fn(() => false),
  getPortfolioWorkspace: vi.fn(),
  getReportingWorkspace: vi.fn(),
  resetDevelopmentFixtureUsage: vi.fn(),
  getSession: vi.fn(),
  getStrategyWorkspace: vi.fn(),
  getSystemStatus: vi.fn(),
  getTradingWorkspace: vi.fn(),
  getWorkflowLibrary: vi.fn(),
  getWorkflowPresets: vi.fn(),
  getFeatureCapabilities: vi.fn(),
  setFeatureCapability: vi.fn()
}));

type Deferred<T> = {
  promise: Promise<T>;
  resolve: (value: T) => void;
  reject: (reason?: unknown) => void;
};

const requests: Record<string, Deferred<unknown>[]> = {
  brokerageConnection: [],
  brokeragePortfolio: [],
  dataOperations: [],
  governance: [],
  overview: [],
  portfolio: [],
  providerConnections: [],
  providerRoutingBindings: [],
  providerRoutingConnections: [],
  providerRoutingTrustSnapshots: [],
  reporting: [],
  research: [],
  session: [],
  trading: [],
  workflowLibrary: [],
  workflowPresets: []
};

describe("useWorkstationData", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    for (const queue of Object.values(requests)) {
      queue.length = 0;
    }

    vi.mocked(api.getSession).mockImplementation(() => track<SessionInfo>("session"));
    vi.mocked(api.getSystemStatus).mockImplementation(() => track<SystemOverviewResponse>("overview"));
    vi.mocked(api.getStrategyWorkspace).mockImplementation(() => track<ResearchWorkspaceResponse>("research"));
    vi.mocked(api.getTradingWorkspace).mockImplementation(() => track<TradingWorkspaceResponse>("trading"));
    vi.mocked(api.getPortfolioWorkspace).mockImplementation(() => track<PortfolioWorkspaceResponse>("portfolio"));
    vi.mocked(api.getDataWorkspace).mockImplementation(() => track<DataOperationsWorkspaceResponse>("dataOperations"));
    vi.mocked(api.getGovernanceWorkspace).mockImplementation(() => track<GovernanceWorkspaceResponse>("governance"));
    vi.mocked(api.getReportingWorkspace).mockImplementation(() => track<GovernanceWorkspaceResponse>("reporting"));
    vi.mocked(api.getAlpacaConnectionStatus).mockImplementation(() => track<BrokerageConnectionStatus>("brokerageConnection"));
    vi.mocked(api.getProviderConnections).mockImplementation(() => track<ProviderConnectionRow[]>("providerConnections"));
    vi.mocked(api.getProviderRoutingConnections).mockImplementation(() => track<ProviderRoutingConnection[]>("providerRoutingConnections"));
    vi.mocked(api.getProviderRoutingBindings).mockImplementation(() => track<ProviderRoutingBinding[]>("providerRoutingBindings"));
    vi.mocked(api.getProviderRoutingTrustSnapshots).mockImplementation(() => track<ProviderRoutingTrustSnapshot[]>("providerRoutingTrustSnapshots"));
    vi.mocked(api.getBrokerageHouseholdPortfolio).mockImplementation(() => track<BrokerageHouseholdPortfolio>("brokeragePortfolio"));
    vi.mocked(api.getWorkflowLibrary).mockImplementation(() => track<WorkflowLibrary>("workflowLibrary"));
    vi.mocked(api.getWorkflowPresets).mockImplementation(() => track<WorkflowPresetLibrary>("workflowPresets"));
    vi.mocked(api.getFeatureCapabilities).mockResolvedValue({
      generatedAt: "2026-01-01T00:00:00Z",
      capabilities: []
    } as never);
    vi.mocked(api.hasDevelopmentFixtureUsage).mockReturnValue(false);
  });

  it("ignores an older full refresh that resolves after a newer refresh", async () => {
    const { result } = renderHook(() => useWorkstationData());

    await waitFor(() => expect(api.getSession).toHaveBeenCalledTimes(1));

    let newerRefresh!: Promise<void>;
    act(() => {
      newerRefresh = result.current.refresh();
    });
    await waitFor(() => expect(api.getSession).toHaveBeenCalledTimes(2));

    await act(async () => {
      resolveRefreshBatch(1, "newer");
      await newerRefresh;
    });

    expect(result.current.session?.displayName).toBe("newer session");
    expect(result.current.loading).toBe(false);

    await act(async () => {
      resolveRefreshBatch(0, "older");
      await flushAsync();
    });

    expect(result.current.session?.displayName).toBe("newer session");
    expect(result.current.overview).toEqual({ marker: "newer overview" });
  });

  it("aborts superseded full refresh requests before starting the next batch", async () => {
    const { result } = renderHook(() => useWorkstationData());

    await waitFor(() => expect(api.getSession).toHaveBeenCalledTimes(1));
    const olderSignal = vi.mocked(api.getSession).mock.calls[0]?.[0]?.signal;
    expect(olderSignal?.aborted).toBe(false);

    act(() => {
      void result.current.refresh();
    });
    await waitFor(() => expect(api.getSession).toHaveBeenCalledTimes(2));

    const newerSignal = vi.mocked(api.getSession).mock.calls[1]?.[0]?.signal;
    expect(olderSignal?.aborted).toBe(true);
    expect(newerSignal?.aborted).toBe(false);

    await act(async () => {
      resolveRefreshBatch(1, "newer");
      await flushAsync();
    });
  });

  it("does not publish a full refresh after the hook unmounts", async () => {
    const { result, unmount } = renderHook(() => useWorkstationData());

    await waitFor(() => expect(api.getSession).toHaveBeenCalledTimes(1));

    unmount();
    await act(async () => {
      resolveRefreshBatch(0, "unmounted");
      await flushAsync();
    });

    expect(result.current.session).toBeNull();
  });

  it("surfaces an expired session as a session recovery state", async () => {
    const { result } = renderHook(() => useWorkstationData());

    await waitFor(() => expect(api.getSession).toHaveBeenCalledTimes(1));

    await act(async () => {
      rejectRequest("session", 0, createApiErrorFromResponseBody(
        "/api/workstation/session",
        401,
        JSON.stringify({
          title: "Unauthorized",
          detail: "The workstation session token expired."
        })
      ));
      resolveRequest<SystemOverviewResponse>("overview", 0, { marker: "overview" } as unknown as SystemOverviewResponse);
      resolveRequest<ResearchWorkspaceResponse>("research", 0, { marker: "research" } as unknown as ResearchWorkspaceResponse);
      resolveRequest<TradingWorkspaceResponse>("trading", 0, { marker: "trading" } as unknown as TradingWorkspaceResponse);
      resolveRequest<PortfolioWorkspaceResponse>("portfolio", 0, { marker: "portfolio" } as unknown as PortfolioWorkspaceResponse);
      resolveRequest<DataOperationsWorkspaceResponse>("dataOperations", 0, { marker: "data" } as unknown as DataOperationsWorkspaceResponse);
      resolveRequest<GovernanceWorkspaceResponse>("governance", 0, { marker: "accounting" } as unknown as GovernanceWorkspaceResponse);
      resolveRequest<GovernanceWorkspaceResponse>("reporting", 0, { marker: "reporting" } as unknown as GovernanceWorkspaceResponse);
      resolveRequest<BrokerageConnectionStatus>("brokerageConnection", 0, { marker: "brokerage" } as unknown as BrokerageConnectionStatus);
      resolveRequest<ProviderConnectionRow[]>("providerConnections", 0, []);
      resolveRequest<ProviderRoutingConnection[]>("providerRoutingConnections", 0, []);
      resolveRequest<ProviderRoutingBinding[]>("providerRoutingBindings", 0, []);
      resolveRequest<ProviderRoutingTrustSnapshot[]>("providerRoutingTrustSnapshots", 0, []);
      resolveRequest<BrokerageHouseholdPortfolio>("brokeragePortfolio", 0, { marker: "brokerage portfolio" } as unknown as BrokerageHouseholdPortfolio);
      resolveRequest<WorkflowLibrary>("workflowLibrary", 0, { marker: "workflows" } as unknown as WorkflowLibrary);
      resolveRequest<WorkflowPresetLibrary>("workflowPresets", 0, { generatedAt: "2026-01-01T00:00:00Z", presets: [] });
      await flushAsync();
    });

    expect(result.current.error).toBe("Session expired or Meridian sign-in is required.");
    expect(result.current.session).toBeNull();
    expect(result.current.overview).toEqual({ marker: "overview" });
  });

  it("keeps the active StrictMode refresh live after the dev remount cycle", async () => {
    const { result } = renderHook(() => useWorkstationData(), { wrapper: StrictModeWrapper });

    await waitFor(() => expect(api.getSession).toHaveBeenCalledTimes(2));

    await act(async () => {
      resolveRefreshBatch(1, "strict-active");
      await flushAsync();
    });

    await waitFor(() => expect(result.current.session?.displayName).toBe("strict-active session"));

    await act(async () => {
      resolveRefreshBatch(0, "strict-stale");
      await flushAsync();
    });

    expect(result.current.session?.displayName).toBe("strict-active session");
  });

  it("surfaces development fixture usage after bootstrap", async () => {
    const { result } = renderHook(() => useWorkstationData());

    await waitFor(() => expect(api.getSession).toHaveBeenCalledTimes(1));
    vi.mocked(api.hasDevelopmentFixtureUsage).mockReturnValue(true);

    await act(async () => {
      resolveRefreshBatch(0, "fixture");
      await flushAsync();
    });

    expect(api.resetDevelopmentFixtureUsage).toHaveBeenCalled();
    expect(result.current.usingDevelopmentFixtures).toBe(true);
  });

  it("merges workflow preset mutation results into the shell catalog", async () => {
    const { result } = renderHook(() => useWorkstationData());

    await waitFor(() => expect(api.getSession).toHaveBeenCalledTimes(1));

    await act(async () => {
      resolveRefreshBatch(0, "workflow");
      await flushAsync();
    });

    act(() => {
      result.current.upsertWorkflowPreset(buildPreset("daily-open", "Daily open", false, "2026-01-01T00:00:00Z"));
      result.current.upsertWorkflowPreset(buildPreset("risk-review", "Risk review", true, "2026-01-02T00:00:00Z"));
      result.current.upsertWorkflowPreset(buildPreset("daily-open", "Daily open", true, "2026-01-03T00:00:00Z"));
    });

    expect(result.current.workflowPresets?.presets.map((preset) => preset.presetId)).toEqual([
      "daily-open",
      "risk-review"
    ]);
    expect(result.current.workflowPresets?.presets[0]).toMatchObject({
      presetId: "daily-open",
      isPinned: true,
      lastUsedAt: "2026-01-03T00:00:00Z"
    });
  });

  it("refreshes provider-routing evidence without reloading every workspace", async () => {
    const { result } = renderHook(() => useWorkstationData());

    await waitFor(() => expect(api.getSession).toHaveBeenCalledTimes(1));

    await act(async () => {
      resolveRefreshBatch(0, "initial");
      await flushAsync();
    });

    let routingRefresh!: Promise<void>;
    act(() => {
      routingRefresh = result.current.refreshProviderRouting();
    });
    await waitFor(() => expect(api.getProviderRoutingConnections).toHaveBeenCalledTimes(2));

    expect(api.getSession).toHaveBeenCalledTimes(1);
    expect(result.current.providerRoutingRefreshing).toBe(true);

    await act(async () => {
      resolveRequest<ProviderConnectionRow[]>("providerConnections", 1, []);
      resolveRequest<ProviderRoutingConnection[]>("providerRoutingConnections", 1, [
        {
          connectionId: "provider-alpaca-paper",
          providerFamilyId: "alpaca",
          displayName: "Alpaca paper",
          connectionType: "DataVendor",
          connectionMode: "ReadOnly",
          enabled: true,
          credentialReference: "vault:alpaca/paper",
          institutionId: null,
          externalAccountId: null,
          scope: null,
          tags: ["streaming"],
          description: null,
          productionReady: false
        }
      ]);
      resolveRequest<ProviderRoutingBinding[]>("providerRoutingBindings", 1, [
        {
          bindingId: "provider-alpaca-paper-RealtimeMarketData",
          capability: "RealtimeMarketData",
          connectionId: "provider-alpaca-paper",
          target: null,
          priority: 100,
          enabled: true,
          failoverConnectionIds: [],
          safetyModeOverride: null,
          notes: null
        }
      ]);
      resolveRequest<ProviderRoutingTrustSnapshot[]>("providerRoutingTrustSnapshots", 1, [
        {
          connectionId: "provider-alpaca-paper",
          providerFamilyId: "alpaca",
          score: 82,
          isHealthy: true,
          healthStatus: "Healthy",
          isProductionReady: false,
          isCertificationFresh: false,
          signals: []
        }
      ]);
      await routingRefresh;
    });

    expect(result.current.providerRoutingConnections?.[0].connectionId).toBe("provider-alpaca-paper");
    expect(result.current.providerRoutingBindings).toHaveLength(1);
    expect(result.current.providerRoutingTrustSnapshots?.[0].score).toBe(82);
    expect(result.current.providerRoutingRefreshing).toBe(false);
    expect(result.current.workspaceErrors.settings).toBeUndefined();
  });

  it("keeps stale provider-routing evidence when a later routing refresh fails", async () => {
    const { result } = renderHook(() => useWorkstationData());

    await waitFor(() => expect(api.getSession).toHaveBeenCalledTimes(1));

    await act(async () => {
      resolveRefreshBatch(0, "initial");
      await flushAsync();
    });

    let successfulRefresh!: Promise<void>;
    act(() => {
      successfulRefresh = result.current.refreshProviderRouting();
    });
    await waitFor(() => expect(api.getProviderRoutingConnections).toHaveBeenCalledTimes(2));
    await act(async () => {
      resolveRequest<ProviderConnectionRow[]>("providerConnections", 1, []);
      resolveRequest<ProviderRoutingConnection[]>("providerRoutingConnections", 1, [
        {
          connectionId: "provider-polygon",
          providerFamilyId: "polygon",
          displayName: "Polygon.io",
          connectionType: "DataVendor",
          connectionMode: "ReadOnly",
          enabled: true,
          credentialReference: "vault:polygon/default",
          institutionId: null,
          externalAccountId: null,
          scope: null,
          tags: ["backfill"],
          description: null,
          productionReady: true
        }
      ]);
      resolveRequest<ProviderRoutingBinding[]>("providerRoutingBindings", 1, []);
      resolveRequest<ProviderRoutingTrustSnapshot[]>("providerRoutingTrustSnapshots", 1, []);
      await successfulRefresh;
    });

    let failedRefresh!: Promise<void>;
    act(() => {
      failedRefresh = result.current.refreshProviderRouting();
    });
    await waitFor(() => expect(api.getProviderRoutingConnections).toHaveBeenCalledTimes(3));
    await act(async () => {
      rejectRequest("providerConnections", 2, new Error("Provider connections timed out."));
      rejectRequest("providerRoutingConnections", 2, new Error("Routing connections timed out."));
      rejectRequest("providerRoutingBindings", 2, new Error("Routing bindings timed out."));
      rejectRequest("providerRoutingTrustSnapshots", 2, new Error("Trust snapshots timed out."));
      await failedRefresh;
    });

    expect(result.current.providerRoutingConnections?.[0].connectionId).toBe("provider-polygon");
    expect(result.current.workspaceErrors.settings).toContain("Routing connections timed out.");
    expect(result.current.providerRoutingRefreshing).toBe(false);
  });

  it("does not let an older trading-only refresh overwrite a newer full refresh", async () => {
    const { result } = renderHook(() => useWorkstationData());

    await waitFor(() => expect(api.getSession).toHaveBeenCalledTimes(1));

    await act(async () => {
      resolveRefreshBatch(0, "initial");
      await flushAsync();
    });

    let staleTradingRefresh!: Promise<void>;
    act(() => {
      staleTradingRefresh = result.current.refreshTrading();
    });
    await waitFor(() => expect(api.getTradingWorkspace).toHaveBeenCalledTimes(2));

    let fullRefresh!: Promise<void>;
    act(() => {
      fullRefresh = result.current.refresh();
    });
    await waitFor(() => expect(api.getSession).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(api.getTradingWorkspace).toHaveBeenCalledTimes(3));

    await act(async () => {
      resolveRefreshBatchWithIndexes({
        marker: "fresh",
        defaultIndex: 1,
        tradingIndex: 2
      });
      await fullRefresh;
    });

    expect(result.current.trading).toEqual({ marker: "fresh trading" });

    await act(async () => {
      resolveRequest<TradingWorkspaceResponse>("trading", 1, { marker: "stale trading" } as unknown as TradingWorkspaceResponse);
      await staleTradingRefresh;
    });

    expect(result.current.trading).toEqual({ marker: "fresh trading" });
  });

  it("surfaces trading-only refresh failures without discarding stale trading data", async () => {
    const { result } = renderHook(() => useWorkstationData());

    await waitFor(() => expect(api.getSession).toHaveBeenCalledTimes(1));

    await act(async () => {
      resolveRefreshBatch(0, "initial");
      await flushAsync();
    });

    let tradingRefresh!: Promise<void>;
    act(() => {
      tradingRefresh = result.current.refreshTrading();
    });
    await waitFor(() => expect(api.getTradingWorkspace).toHaveBeenCalledTimes(2));

    await act(async () => {
      rejectRequest("trading", 1, new Error("Trading endpoint timed out."));
      await tradingRefresh;
    });

    expect(result.current.trading).toEqual({ marker: "initial trading" });
    expect(result.current.workspaceErrors).toMatchObject({
      trading: "Trading endpoint timed out."
    });
    expect(result.current.error).toBe("Trading endpoint timed out.");
  });

  it("preserves multiple request failures for a shared workspace slice", async () => {
    const { result } = renderHook(() => useWorkstationData());

    await waitFor(() => expect(api.getSession).toHaveBeenCalledTimes(1));

    await act(async () => {
      resolveRequest<SessionInfo>("session", 0, {
        activeWorkspace: "portfolio",
        commandCount: 1,
        displayName: "Ops session",
        environment: "paper",
        role: "Operator"
      });
      resolveRequest<SystemOverviewResponse>("overview", 0, { marker: "overview" } as unknown as SystemOverviewResponse);
      resolveRequest<ResearchWorkspaceResponse>("research", 0, { marker: "research" } as unknown as ResearchWorkspaceResponse);
      resolveRequest<TradingWorkspaceResponse>("trading", 0, { marker: "trading" } as unknown as TradingWorkspaceResponse);
      rejectRequest("portfolio", 0, new Error("Portfolio workspace timed out."));
      resolveRequest<DataOperationsWorkspaceResponse>("dataOperations", 0, { marker: "data" } as unknown as DataOperationsWorkspaceResponse);
      resolveRequest<GovernanceWorkspaceResponse>("governance", 0, { marker: "accounting" } as unknown as GovernanceWorkspaceResponse);
      resolveRequest<GovernanceWorkspaceResponse>("reporting", 0, { marker: "reporting" } as unknown as GovernanceWorkspaceResponse);
      rejectRequest("brokerageConnection", 0, new Error("Alpaca connection status failed."));
      resolveRequest<ProviderConnectionRow[]>("providerConnections", 0, []);
      resolveRequest<ProviderRoutingConnection[]>("providerRoutingConnections", 0, []);
      resolveRequest<ProviderRoutingBinding[]>("providerRoutingBindings", 0, []);
      resolveRequest<ProviderRoutingTrustSnapshot[]>("providerRoutingTrustSnapshots", 0, []);
      rejectRequest("brokeragePortfolio", 0, new Error("Brokerage household sync failed."));
      resolveRequest<WorkflowLibrary>("workflowLibrary", 0, { marker: "workflows" } as unknown as WorkflowLibrary);
      resolveRequest<WorkflowPresetLibrary>("workflowPresets", 0, {
        generatedAt: "2026-01-01T00:00:00Z",
        presets: []
      });
      await flushAsync();
    });

    expect(result.current.workspaceErrors.portfolio).toBe(
      "Portfolio workspace timed out.; Alpaca connection status failed.; Brokerage household sync failed."
    );
    expect(result.current.error).toBe(result.current.workspaceErrors.portfolio);
  });

  it("clears the trading-only refresh failure after a later trading refresh succeeds", async () => {
    const { result } = renderHook(() => useWorkstationData());

    await waitFor(() => expect(api.getSession).toHaveBeenCalledTimes(1));

    await act(async () => {
      resolveRefreshBatch(0, "initial");
      await flushAsync();
    });

    let failedRefresh!: Promise<void>;
    act(() => {
      failedRefresh = result.current.refreshTrading();
    });
    await waitFor(() => expect(api.getTradingWorkspace).toHaveBeenCalledTimes(2));
    await act(async () => {
      rejectRequest("trading", 1, new Error("Trading endpoint timed out."));
      await failedRefresh;
    });

    let recoveryRefresh!: Promise<void>;
    act(() => {
      recoveryRefresh = result.current.refreshTrading();
    });
    await waitFor(() => expect(api.getTradingWorkspace).toHaveBeenCalledTimes(3));
    await act(async () => {
      resolveRequest<TradingWorkspaceResponse>("trading", 2, { marker: "recovered trading" } as unknown as TradingWorkspaceResponse);
      await recoveryRefresh;
    });

    expect(result.current.trading).toEqual({ marker: "recovered trading" });
    expect(result.current.workspaceErrors.trading).toBeUndefined();
    expect(result.current.error).toBeNull();
  });

  it("surfaces portfolio-only refresh failures without discarding stale portfolio data", async () => {
    const { result } = renderHook(() => useWorkstationData());

    await waitFor(() => expect(api.getSession).toHaveBeenCalledTimes(1));

    await act(async () => {
      resolveRefreshBatch(0, "initial");
      await flushAsync();
    });

    let portfolioRefresh!: Promise<void>;
    act(() => {
      portfolioRefresh = result.current.refreshPortfolio();
    });
    await waitFor(() => expect(api.getPortfolioWorkspace).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(api.getBrokerageHouseholdPortfolio).toHaveBeenCalledTimes(2));

    await act(async () => {
      rejectRequest("portfolio", 1, new Error("Portfolio refresh failed."));
      rejectRequest("brokeragePortfolio", 1, new Error("Brokerage portfolio refresh failed."));
      await portfolioRefresh;
    });

    expect(result.current.portfolio).toEqual({ marker: "initial portfolio" });
    expect(result.current.brokeragePortfolio).toEqual({ marker: "initial brokerage" });
    expect(result.current.workspaceErrors.portfolio).toBe(
      "Portfolio refresh failed.; Brokerage portfolio refresh failed."
    );
    expect(result.current.error).toBe(result.current.workspaceErrors.portfolio);
  });

  it("clears the portfolio-only refresh failure after a later portfolio refresh succeeds", async () => {
    const { result } = renderHook(() => useWorkstationData());

    await waitFor(() => expect(api.getSession).toHaveBeenCalledTimes(1));

    await act(async () => {
      resolveRefreshBatch(0, "initial");
      await flushAsync();
    });

    let failedRefresh!: Promise<void>;
    act(() => {
      failedRefresh = result.current.refreshPortfolio();
    });
    await waitFor(() => expect(api.getPortfolioWorkspace).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(api.getBrokerageHouseholdPortfolio).toHaveBeenCalledTimes(2));
    await act(async () => {
      rejectRequest("portfolio", 1, new Error("Portfolio refresh failed."));
      rejectRequest("brokeragePortfolio", 1, new Error("Brokerage portfolio refresh failed."));
      await failedRefresh;
    });

    let recoveryRefresh!: Promise<void>;
    act(() => {
      recoveryRefresh = result.current.refreshPortfolio();
    });
    await waitFor(() => expect(api.getPortfolioWorkspace).toHaveBeenCalledTimes(3));
    await waitFor(() => expect(api.getBrokerageHouseholdPortfolio).toHaveBeenCalledTimes(3));
    await act(async () => {
      resolveRequest<PortfolioWorkspaceResponse>("portfolio", 2, { marker: "recovered portfolio" } as unknown as PortfolioWorkspaceResponse);
      resolveRequest<BrokerageHouseholdPortfolio>("brokeragePortfolio", 2, { marker: "recovered brokerage" } as unknown as BrokerageHouseholdPortfolio);
      await recoveryRefresh;
    });

    expect(result.current.portfolio).toEqual({ marker: "recovered portfolio" });
    expect(result.current.brokeragePortfolio).toEqual({ marker: "recovered brokerage" });
    expect(result.current.workspaceErrors.portfolio).toBeUndefined();
    expect(result.current.error).toBeNull();
  });
});

function StrictModeWrapper({ children }: { children: ReactNode }) {
  return createElement(StrictMode, null, children);
}

function track<T>(key: keyof typeof requests): Promise<T> {
  const deferred = createDeferred<T>();
  requests[key].push(deferred as Deferred<unknown>);
  return deferred.promise;
}

function createDeferred<T>(): Deferred<T> {
  let resolve!: (value: T) => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((promiseResolve, promiseReject) => {
    resolve = promiseResolve;
    reject = promiseReject;
  });

  return { promise, resolve, reject };
}

function resolveRefreshBatch(index: number, marker: string) {
  resolveRefreshBatchWithIndexes({ marker, defaultIndex: index, tradingIndex: index });
}

function resolveRefreshBatchWithIndexes({
  marker,
  defaultIndex,
  tradingIndex
}: {
  marker: string;
  defaultIndex: number;
  tradingIndex: number;
}) {
  resolveRequest<SessionInfo>("session", defaultIndex, {
    activeWorkspace: "trading",
    commandCount: 1,
    displayName: `${marker} session`,
    environment: "paper",
    role: "Operator"
  });
  resolveRequest<SystemOverviewResponse>("overview", defaultIndex, { marker: `${marker} overview` } as unknown as SystemOverviewResponse);
  resolveRequest<ResearchWorkspaceResponse>("research", defaultIndex, { marker: `${marker} research` } as unknown as ResearchWorkspaceResponse);
  resolveRequest<TradingWorkspaceResponse>("trading", tradingIndex, { marker: `${marker} trading` } as unknown as TradingWorkspaceResponse);
  resolveRequest<PortfolioWorkspaceResponse>("portfolio", defaultIndex, { marker: `${marker} portfolio` } as unknown as PortfolioWorkspaceResponse);
  resolveRequest<DataOperationsWorkspaceResponse>("dataOperations", defaultIndex, { marker: `${marker} data` } as unknown as DataOperationsWorkspaceResponse);
  resolveRequest<GovernanceWorkspaceResponse>("governance", defaultIndex, { marker: `${marker} accounting` } as unknown as GovernanceWorkspaceResponse);
  resolveRequest<GovernanceWorkspaceResponse>("reporting", defaultIndex, { marker: `${marker} reporting` } as unknown as GovernanceWorkspaceResponse);
  resolveRequest<BrokerageConnectionStatus>("brokerageConnection", defaultIndex, { marker: `${marker} connection` } as unknown as BrokerageConnectionStatus);
  resolveRequest<ProviderConnectionRow[]>("providerConnections", defaultIndex, []);
  resolveRequest<ProviderRoutingConnection[]>("providerRoutingConnections", defaultIndex, []);
  resolveRequest<ProviderRoutingBinding[]>("providerRoutingBindings", defaultIndex, []);
  resolveRequest<ProviderRoutingTrustSnapshot[]>("providerRoutingTrustSnapshots", defaultIndex, []);
  resolveRequest<BrokerageHouseholdPortfolio>("brokeragePortfolio", defaultIndex, { marker: `${marker} brokerage` } as unknown as BrokerageHouseholdPortfolio);
  resolveRequest<WorkflowLibrary>("workflowLibrary", defaultIndex, { marker: `${marker} workflows` } as unknown as WorkflowLibrary);
  resolveRequest<WorkflowPresetLibrary>("workflowPresets", defaultIndex, {
    generatedAt: "2026-01-01T00:00:00Z",
    presets: []
  });
}

function resolveRequest<T>(key: keyof typeof requests, index: number, value: T) {
  const request = requests[key][index] as Deferred<T> | undefined;
  if (!request) {
    throw new Error(`Missing ${String(key)} request ${index}`);
  }

  request.resolve(value);
}

function rejectRequest(key: keyof typeof requests, index: number, reason: unknown) {
  const request = requests[key][index];
  if (!request) {
    throw new Error(`Missing ${String(key)} request ${index}`);
  }

  request.reject(reason);
}

async function flushAsync() {
  await Promise.resolve();
  await Promise.resolve();
}

function buildPreset(presetId: string, name: string, isPinned: boolean, lastUsedAt: string): WorkflowPreset {
  return {
    presetId,
    name,
    description: null,
    workflowId: "paper-trading-readiness",
    workflowTitle: "Paper Trading Readiness",
    actionId: "workflow.trading.review-paper-candidate",
    actionLabel: "Review Candidate for Paper",
    workspaceId: "trading",
    workspaceTitle: "Trading",
    targetPageTag: "TradingReadiness",
    tags: [],
    isPinned,
    createdAt: "2026-01-01T00:00:00Z",
    updatedAt: lastUsedAt,
    lastUsedAt
  };
}
