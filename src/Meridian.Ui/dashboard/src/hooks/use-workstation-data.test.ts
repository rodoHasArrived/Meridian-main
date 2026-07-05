import { act, renderHook, waitFor } from "@testing-library/react";
import { createElement, StrictMode, type ReactNode } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { useWorkstationData } from "@/hooks/use-workstation-data";
import * as api from "@/lib/api";
import { createApiErrorFromResponseBody } from "@/lib/api-errors";
import type {
  BrokerageConnectionStatus,
  BrokerageHouseholdPortfolio,
  DataWorkspaceResponse,
  AccountingWorkspaceResponse,
  MultiAssetCoverageSummary,
  OperatorWorkflowHomeSummary,
  ReportingWorkspaceResponse,
  PortfolioWorkspaceResponse,
  ProviderConnectionRow,
  ProviderReadinessSummary,
  ProviderRoutingBinding,
  ProviderRoutingConnection,
  ProviderRoutingTrustSnapshot,
  StrategyWorkspaceResponse,
  SessionInfo,
  SystemOverviewResponse,
  TradingWorkspaceResponse,
  WorkspaceKey,
  WorkflowLibrary,
  WorkflowPreset,
  WorkflowPresetLibrary
} from "@/types";

vi.mock("@/lib/api", () => ({
  getBrokerageHouseholdPortfolio: vi.fn(),
  getDataWorkspace: vi.fn(),
  getAccountingWorkspace: vi.fn(),
  getAlpacaConnectionStatus: vi.fn(),
  getRobinhoodConnectionStatus: vi.fn(),
  getProviderConnections: vi.fn(),
  getProviderReadiness: vi.fn(),
  getProviderRoutingBindings: vi.fn(),
  getProviderRoutingConnections: vi.fn(),
  getProviderRoutingTrustSnapshots: vi.fn(),
  getRolePermissionCatalog: vi.fn(),
  getSecurityAssetProfiles: vi.fn(),
  getLedgerMappingWorkbench: vi.fn(),
  getOperationsApprovalPolicyMatrix: vi.fn(),
  getOperationsCloseCalendar: vi.fn(),
  hasDevelopmentFixtureUsage: vi.fn(() => false),
  getPortfolioMultiAssetCoverage: vi.fn(),
  getPortfolioWorkspace: vi.fn(),
  getReportingWorkspace: vi.fn(),
  resetDevelopmentFixtureUsage: vi.fn(),
  getSession: vi.fn(),
  getStrategyWorkspace: vi.fn(),
  getSystemStatus: vi.fn(),
  getTradingWorkspace: vi.fn(),
  getWorkstationWorkflowSummary: vi.fn(),
  getWorkflowLibrary: vi.fn(),
  getWorkflowPresets: vi.fn(),
  getFeatureCapabilities: vi.fn(),
  setFeatureCapability: vi.fn()
}));

type Deferred<T> = {
  promise: Promise<T>;
  resolve: (value: T) => void;
  reject: (reason?: unknown) => void;
  settled: boolean;
};

const requests: Record<string, Deferred<unknown>[]> = {
  brokerageConnection: [],
  robinhoodConnection: [],
  brokeragePortfolio: [],
  data: [],
  accounting: [],
  overview: [],
  portfolio: [],
  portfolioMultiAssetCoverage: [],
  providerConnections: [],
  providerReadiness: [],
  providerRoutingBindings: [],
  providerRoutingConnections: [],
  providerRoutingTrustSnapshots: [],
  rolePermissionCatalog: [],
  securityAssetProfiles: [],
  ledgerMappingWorkbench: [],
  operationsApprovalPolicyMatrix: [],
  operationsCloseCalendar: [],
  reporting: [],
  strategy: [],
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
    vi.mocked(api.getStrategyWorkspace).mockImplementation(() => track<StrategyWorkspaceResponse>("strategy"));
    vi.mocked(api.getTradingWorkspace).mockImplementation(() => track<TradingWorkspaceResponse>("trading"));
    vi.mocked(api.getPortfolioWorkspace).mockImplementation(() => track<PortfolioWorkspaceResponse>("portfolio"));
    vi.mocked(api.getPortfolioMultiAssetCoverage).mockImplementation(() => track<MultiAssetCoverageSummary>("portfolioMultiAssetCoverage"));
    vi.mocked(api.getDataWorkspace).mockImplementation(() => track<DataWorkspaceResponse>("data"));
    vi.mocked(api.getAccountingWorkspace).mockImplementation(() => track<AccountingWorkspaceResponse>("accounting"));
    vi.mocked(api.getReportingWorkspace).mockImplementation(() => track<ReportingWorkspaceResponse>("reporting"));
    vi.mocked(api.getAlpacaConnectionStatus).mockImplementation(() => track<BrokerageConnectionStatus>("brokerageConnection"));
    vi.mocked(api.getRobinhoodConnectionStatus).mockImplementation(() => track<BrokerageConnectionStatus>("robinhoodConnection"));
    vi.mocked(api.getProviderConnections).mockImplementation(() => track<ProviderConnectionRow[]>("providerConnections"));
    vi.mocked(api.getProviderReadiness).mockImplementation(() => track<ProviderReadinessSummary>("providerReadiness"));
    vi.mocked(api.getProviderRoutingConnections).mockImplementation(() => track<ProviderRoutingConnection[]>("providerRoutingConnections"));
    vi.mocked(api.getProviderRoutingBindings).mockImplementation(() => track<ProviderRoutingBinding[]>("providerRoutingBindings"));
    vi.mocked(api.getProviderRoutingTrustSnapshots).mockImplementation(() => track<ProviderRoutingTrustSnapshot[]>("providerRoutingTrustSnapshots"));
    vi.mocked(api.getRolePermissionCatalog).mockResolvedValue({ generatedAt: "2026-01-01T00:00:00Z", roles: [] } as never);
    vi.mocked(api.getSecurityAssetProfiles).mockResolvedValue([]);
    vi.mocked(api.getLedgerMappingWorkbench).mockResolvedValue({ generatedAt: "2026-01-01T00:00:00Z" } as never);
    vi.mocked(api.getOperationsApprovalPolicyMatrix).mockResolvedValue({ generatedAt: "2026-01-01T00:00:00Z" } as never);
    vi.mocked(api.getOperationsCloseCalendar).mockResolvedValue({ generatedAt: "2026-01-01T00:00:00Z" } as never);
    vi.mocked(api.getBrokerageHouseholdPortfolio).mockImplementation(() => track<BrokerageHouseholdPortfolio>("brokeragePortfolio"));
    vi.mocked(api.getWorkstationWorkflowSummary).mockResolvedValue(buildWorkflowSummary("default"));
    vi.mocked(api.getWorkflowLibrary).mockImplementation(() => track<WorkflowLibrary>("workflowLibrary"));
    vi.mocked(api.getWorkflowPresets).mockImplementation(() => track<WorkflowPresetLibrary>("workflowPresets"));
    vi.mocked(api.getFeatureCapabilities).mockResolvedValue({
      generatedAt: "2026-01-01T00:00:00Z",
      capabilities: []
    } as never);
    vi.mocked(api.hasDevelopmentFixtureUsage).mockReturnValue(false);
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
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
      await resolveRefreshBatch(1, "newer");
      await newerRefresh;
    });

    expect(result.current.session?.displayName).toBe("newer session");
    expect(result.current.loading).toBe(false);

    await act(async () => {
      await resolveRefreshBatch(0, "older");
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
      await resolveRefreshBatch(1, "newer");
      await flushAsync();
    });
  });

  it("does not publish a full refresh after the hook unmounts", async () => {
    const { result, unmount } = renderHook(() => useWorkstationData());

    await waitFor(() => expect(api.getSession).toHaveBeenCalledTimes(1));

    unmount();
    await act(async () => {
      await resolveRefreshBatch(0, "unmounted");
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
      resolveRequestIfPresent<StrategyWorkspaceResponse>("strategy", 0, { marker: "strategy" } as unknown as StrategyWorkspaceResponse);
      resolveRequest<TradingWorkspaceResponse>("trading", 0, { marker: "trading" } as unknown as TradingWorkspaceResponse);
      resolveRequestIfPresent<PortfolioWorkspaceResponse>("portfolio", 0, { marker: "portfolio" } as unknown as PortfolioWorkspaceResponse);
      resolveRequestIfPresent<MultiAssetCoverageSummary>(
        "portfolioMultiAssetCoverage",
        0,
        { marker: "portfolio coverage" } as unknown as MultiAssetCoverageSummary
      );
      resolveRequestIfPresent<DataWorkspaceResponse>("data", 0, { marker: "data" } as unknown as DataWorkspaceResponse);
      resolveRequestIfPresent<AccountingWorkspaceResponse>("accounting", 0, { marker: "accounting" } as unknown as AccountingWorkspaceResponse);
      resolveRequestIfPresent<ReportingWorkspaceResponse>("reporting", 0, { marker: "reporting" } as unknown as ReportingWorkspaceResponse);
      resolveRequestIfPresent<BrokerageConnectionStatus>("brokerageConnection", 0, { marker: "brokerage" } as unknown as BrokerageConnectionStatus);
      resolveRequestIfPresent<ProviderConnectionRow[]>("providerConnections", 0, []);
      resolveRequestIfPresent<ProviderReadinessSummary>("providerReadiness", 0, buildProviderReadiness("session-expired"));
      resolveRequestIfPresent<ProviderRoutingConnection[]>("providerRoutingConnections", 0, []);
      resolveRequestIfPresent<ProviderRoutingBinding[]>("providerRoutingBindings", 0, []);
      resolveRequestIfPresent<ProviderRoutingTrustSnapshot[]>("providerRoutingTrustSnapshots", 0, []);
      resolveRequestIfPresent<BrokerageHouseholdPortfolio>("brokeragePortfolio", 0, { marker: "brokerage portfolio" } as unknown as BrokerageHouseholdPortfolio);
      resolveRequest<WorkflowLibrary>("workflowLibrary", 0, { marker: "workflows" } as unknown as WorkflowLibrary);
      resolveRequest<WorkflowPresetLibrary>("workflowPresets", 0, { generatedAt: "2026-01-01T00:00:00Z", presets: [] });
      await flushAsync();
    });

    expect(result.current.error).toBe("Session expired or Meridian sign-in is required.");
    expect(result.current.session).toBeNull();
    expect(result.current.overview).toEqual({ marker: "overview" });
  });

  it("translates raw HTML 404 workspace failures before publishing shell errors", async () => {
    const { result } = renderHook(() => useWorkstationData({ activeWorkspace: "reporting" }));

    await waitFor(() => expect(api.getSession).toHaveBeenCalledTimes(1));

    await act(async () => {
      resolveRequest<SessionInfo>("session", 0, {
        activeWorkspace: "reporting",
        commandCount: 1,
        displayName: "Ops session",
        environment: "paper",
        role: "Operator"
      });
      resolveRequest<SystemOverviewResponse>("overview", 0, { marker: "overview" } as unknown as SystemOverviewResponse);
      resolveRequestIfPresent<StrategyWorkspaceResponse>("strategy", 0, { marker: "strategy" } as unknown as StrategyWorkspaceResponse);
      resolveRequestIfPresent<TradingWorkspaceResponse>("trading", 0, { marker: "trading" } as unknown as TradingWorkspaceResponse);
      resolveRequestIfPresent<PortfolioWorkspaceResponse>("portfolio", 0, { marker: "portfolio" } as unknown as PortfolioWorkspaceResponse);
      resolveRequestIfPresent<MultiAssetCoverageSummary>(
        "portfolioMultiAssetCoverage",
        0,
        { marker: "portfolio coverage" } as unknown as MultiAssetCoverageSummary
      );
      resolveRequestIfPresent<DataWorkspaceResponse>("data", 0, { marker: "data" } as unknown as DataWorkspaceResponse);
      resolveRequestIfPresent<AccountingWorkspaceResponse>("accounting", 0, { marker: "accounting" } as unknown as AccountingWorkspaceResponse);
      rejectRequest("reporting", 0, createApiErrorFromResponseBody(
        "/api/workstation/reporting",
        404,
        "<!DOCTYPE HTML><html><body><h1>404</h1><p>File not found</p></body></html>"
      ));
      resolveRequestIfPresent<BrokerageConnectionStatus>("brokerageConnection", 0, { marker: "brokerage" } as unknown as BrokerageConnectionStatus);
      resolveRequestIfPresent<BrokerageConnectionStatus>("robinhoodConnection", 0, { marker: "robinhood" } as unknown as BrokerageConnectionStatus);
      resolveRequestIfPresent<ProviderConnectionRow[]>("providerConnections", 0, []);
      resolveRequestIfPresent<ProviderReadinessSummary>("providerReadiness", 0, buildProviderReadiness("reporting-html-404"));
      resolveRequestIfPresent<ProviderRoutingConnection[]>("providerRoutingConnections", 0, []);
      resolveRequestIfPresent<ProviderRoutingBinding[]>("providerRoutingBindings", 0, []);
      resolveRequestIfPresent<ProviderRoutingTrustSnapshot[]>("providerRoutingTrustSnapshots", 0, []);
      resolveRequestIfPresent<BrokerageHouseholdPortfolio>("brokeragePortfolio", 0, { marker: "brokerage portfolio" } as unknown as BrokerageHouseholdPortfolio);
      resolveRequest<WorkflowLibrary>("workflowLibrary", 0, { marker: "workflows" } as unknown as WorkflowLibrary);
      resolveRequest<WorkflowPresetLibrary>("workflowPresets", 0, { generatedAt: "2026-01-01T00:00:00Z", presets: [] });
      await flushAsync();
    });

    expect(result.current.workspaceErrors.reporting).toBe("The requested Meridian data is unavailable.");
    expect(result.current.error).toBe("The requested Meridian data is unavailable.");
    expect(result.current.error).not.toContain("<!DOCTYPE");
    expect(result.current.error).not.toContain("File not found");
  });

  it("keeps the active StrictMode refresh live after the dev remount cycle", async () => {
    const { result } = renderHook(() => useWorkstationData(), { wrapper: StrictModeWrapper });

    await waitFor(() => expect(api.getSession).toHaveBeenCalledTimes(2));

    await act(async () => {
      await resolveRefreshBatch(1, "strict-active");
      await flushAsync();
    });

    await waitFor(() => expect(result.current.session?.displayName).toBe("strict-active session"));

    await act(async () => {
      await resolveRefreshBatch(0, "strict-stale");
      await flushAsync();
    });

    expect(result.current.session?.displayName).toBe("strict-active session");
  });

  it("publishes the active workspace before slower secondary workspaces settle", async () => {
    const { result } = renderHook(() => useWorkstationData({ activeWorkspace: "accounting" }));

    await waitFor(() => expect(api.getAccountingWorkspace).toHaveBeenCalledTimes(1));
    expect(api.getStrategyWorkspace).not.toHaveBeenCalled();
    expect(api.getTradingWorkspace).not.toHaveBeenCalled();
    expect(api.getPortfolioWorkspace).not.toHaveBeenCalled();

    await act(async () => {
      resolveRequest<SessionInfo>("session", 0, {
        activeWorkspace: "accounting",
        commandCount: 1,
        displayName: "active session",
        environment: "paper",
        role: "Operator"
      });
      resolveRequest<SystemOverviewResponse>("overview", 0, { marker: "active overview" } as unknown as SystemOverviewResponse);
      resolveRequest<AccountingWorkspaceResponse>("accounting", 0, { marker: "active accounting" } as unknown as AccountingWorkspaceResponse);
      resolveRequest<MultiAssetCoverageSummary>(
        "portfolioMultiAssetCoverage",
        0,
        { marker: "active portfolio coverage" } as unknown as MultiAssetCoverageSummary
      );
      resolveRequest<WorkflowLibrary>("workflowLibrary", 0, { marker: "active workflows" } as unknown as WorkflowLibrary);
      resolveRequest<WorkflowPresetLibrary>("workflowPresets", 0, {
        generatedAt: "2026-01-01T00:00:00Z",
        presets: []
      });
      await flushAsync();
    });

    expect(result.current.loading).toBe(false);
    expect(result.current.accounting).toEqual({ marker: "active accounting" });
    expect(result.current.strategy).toBeNull();
    expect(result.current.refreshStatus.inFlight).toBe(true);

    await act(async () => {
      resolveSecondaryRefreshBatch(0, "secondary");
      await flushAsync();
    });

    expect(result.current.strategy).toEqual({ marker: "secondary strategy" });
    expect(result.current.trading).toEqual({ marker: "secondary trading" });
    expect(result.current.refreshStatus.inFlight).toBe(false);
  });

  it("runs later manual refreshes against only the active workspace scope", async () => {
    const { result } = renderHook(() => useWorkstationData({ activeWorkspace: "data" }));

    await waitFor(() => expect(api.getSession).toHaveBeenCalledTimes(1));
    await act(async () => {
      await resolveRefreshBatch(0, "initial");
      await flushAsync();
    });
    await waitFor(() => expect(result.current.refreshStatus.inFlight).toBe(false));

    let scopedRefresh!: Promise<void>;
    act(() => {
      scopedRefresh = result.current.refresh();
    });
    await waitFor(() => expect(api.getSession).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(api.getDataWorkspace).toHaveBeenCalledTimes(2));

    await act(async () => {
      resolveRequest<SessionInfo>("session", 1, {
        activeWorkspace: "data",
        commandCount: 2,
        displayName: "scoped session",
        environment: "paper",
        role: "Operator"
      });
      resolveRequest<SystemOverviewResponse>("overview", 1, { marker: "scoped overview" } as unknown as SystemOverviewResponse);
      resolveRequest<DataWorkspaceResponse>("data", 1, { marker: "scoped data" } as unknown as DataWorkspaceResponse);
      resolveRequest<ProviderConnectionRow[]>("providerConnections", 1, []);
      resolveRequest<ProviderReadinessSummary>("providerReadiness", 1, buildProviderReadiness("scoped"));
      resolveRequest<WorkflowLibrary>("workflowLibrary", 1, { marker: "scoped workflows" } as unknown as WorkflowLibrary);
      resolveRequest<WorkflowPresetLibrary>("workflowPresets", 1, { generatedAt: "2026-01-01T00:01:00Z", presets: [] });
      await scopedRefresh;
    });

    expect(result.current.data).toEqual({ marker: "scoped data" });
    expect(api.getAccountingWorkspace).toHaveBeenCalledTimes(1);
    expect(api.getPortfolioWorkspace).toHaveBeenCalledTimes(1);
    expect(api.getTradingWorkspace).toHaveBeenCalledTimes(1);
  });

  it("refetches stale active workspace slices when the active workspace changes", async () => {
    let now = 1_000;
    vi.spyOn(Date, "now").mockImplementation(() => now);
    const { result, rerender } = renderHook(
      ({ workspace }) => useWorkstationData({ activeWorkspace: workspace }),
      { initialProps: { workspace: "trading" as WorkspaceKey } }
    );

    await waitFor(() => expect(api.getSession).toHaveBeenCalledTimes(1));
    await act(async () => {
      await resolveRefreshBatch(0, "initial");
      await flushAsync();
    });
    await waitFor(() => expect(result.current.refreshStatus.inFlight).toBe(false));

    now = 32_000;
    rerender({ workspace: "data" });
    await waitFor(() => expect(api.getDataWorkspace).toHaveBeenCalledTimes(2));

    await act(async () => {
      resolveRequest<SessionInfo>("session", 1, {
        activeWorkspace: "data",
        commandCount: 2,
        displayName: "navigation session",
        environment: "paper",
        role: "Operator"
      });
      resolveRequest<SystemOverviewResponse>("overview", 1, { marker: "navigation overview" } as unknown as SystemOverviewResponse);
      resolveRequest<DataWorkspaceResponse>("data", 1, { marker: "navigation data" } as unknown as DataWorkspaceResponse);
      resolveRequest<ProviderConnectionRow[]>("providerConnections", 1, []);
      resolveRequest<ProviderReadinessSummary>("providerReadiness", 1, buildProviderReadiness("navigation"));
      resolveRequest<WorkflowLibrary>("workflowLibrary", 1, { marker: "navigation workflows" } as unknown as WorkflowLibrary);
      resolveRequest<WorkflowPresetLibrary>("workflowPresets", 1, { generatedAt: "2026-01-01T00:02:00Z", presets: [] });
      await flushAsync();
    });

    expect(result.current.data).toEqual({ marker: "navigation data" });
  });

  it("preserves unrelated workspace errors during scoped refresh replacement", async () => {
    const { result } = renderHook(() => useWorkstationData({ activeWorkspace: "trading" }));

    await waitFor(() => expect(api.getSession).toHaveBeenCalledTimes(1));
    await act(async () => {
      await resolveRefreshBatch(0, "initial");
      await flushAsync();
    });
    await waitFor(() => expect(result.current.refreshStatus.inFlight).toBe(false));

    let tradingRefresh!: Promise<void>;
    act(() => {
      tradingRefresh = result.current.refreshTrading();
    });
    await waitFor(() => expect(api.getTradingWorkspace).toHaveBeenCalledTimes(2));
    await act(async () => {
      rejectRequest("trading", 1, new Error("Trading scoped failure."));
      await tradingRefresh;
    });

    let dataRefresh!: Promise<void>;
    act(() => {
      dataRefresh = result.current.refreshWorkspace("data");
    });
    await waitFor(() => expect(api.getDataWorkspace).toHaveBeenCalledTimes(2));
    await act(async () => {
      resolveRequest<SessionInfo>("session", 1, {
        activeWorkspace: "data",
        commandCount: 2,
        displayName: "data session",
        environment: "paper",
        role: "Operator"
      });
      resolveRequest<SystemOverviewResponse>("overview", 1, { marker: "data overview" } as unknown as SystemOverviewResponse);
      rejectRequest("data", 1, new Error("Data scoped failure."));
      resolveRequest<ProviderConnectionRow[]>("providerConnections", 1, []);
      resolveRequest<ProviderReadinessSummary>("providerReadiness", 1, buildProviderReadiness("data"));
      resolveRequest<WorkflowLibrary>("workflowLibrary", 1, { marker: "data workflows" } as unknown as WorkflowLibrary);
      resolveRequest<WorkflowPresetLibrary>("workflowPresets", 1, { generatedAt: "2026-01-01T00:03:00Z", presets: [] });
      await dataRefresh;
    });

    expect(result.current.workspaceErrors.trading).toBe("Trading scoped failure.");
    expect(result.current.workspaceErrors.data).toBe("Data scoped failure.");
  });

  it("passes account scope into the shared workflow summary request", async () => {
    const scopedSummary = buildWorkflowSummary("scoped");
    vi.mocked(api.getWorkstationWorkflowSummary).mockResolvedValue(scopedSummary);
    const { result } = renderHook(() => useWorkstationData({
      activeWorkspace: "accounting",
      workflowSummaryScope: {
        hasOperatingContext: true,
        fundAccountId: "53bf0251-17f6-4fb7-8dbe-6fb4966e2749"
      }
    }));

    await waitFor(() => expect(api.getWorkstationWorkflowSummary).toHaveBeenCalledTimes(1));

    await act(async () => {
      await resolveRefreshBatch(0, "scoped");
      await flushAsync();
    });

    expect(api.getWorkstationWorkflowSummary).toHaveBeenCalledWith(expect.objectContaining({
      hasOperatingContext: true,
      fundAccountId: "53bf0251-17f6-4fb7-8dbe-6fb4966e2749",
      signal: expect.any(AbortSignal)
    }));
    expect(result.current.workflowSummary).toBe(scopedSummary);
  });

  it("passes GUID account scope into trading workspace requests", async () => {
    const fundAccountId = "53bf0251-17f6-4fb7-8dbe-6fb4966e2749";
    const { result } = renderHook(() => useWorkstationData({
      activeWorkspace: "trading",
      workflowSummaryScope: {
        hasOperatingContext: true,
        fundAccountId
      }
    }));

    await waitFor(() => expect(api.getTradingWorkspace).toHaveBeenCalledTimes(1));
    expect(api.getTradingWorkspace).toHaveBeenLastCalledWith(expect.objectContaining({
      fundAccountId,
      signal: expect.any(AbortSignal)
    }));

    await act(async () => {
      await resolveRefreshBatch(0, "scoped-trading");
      await flushAsync();
    });

    let tradingRefresh!: Promise<void>;
    act(() => {
      tradingRefresh = result.current.refreshTrading();
    });
    await waitFor(() => expect(api.getTradingWorkspace).toHaveBeenCalledTimes(2));
    expect(api.getTradingWorkspace).toHaveBeenLastCalledWith(expect.objectContaining({
      fundAccountId,
      signal: expect.any(AbortSignal)
    }));

    await act(async () => {
      resolveRequest<TradingWorkspaceResponse>("trading", 1, { marker: "scoped trading refresh" } as unknown as TradingWorkspaceResponse);
      await tradingRefresh;
    });
  });

  it("omits non-GUID operating account labels from trading workspace requests", async () => {
    renderHook(() => useWorkstationData({
      activeWorkspace: "trading",
      workflowSummaryScope: {
        hasOperatingContext: true,
        fundAccountId: "brokerage-account-label"
      }
    }));

    await waitFor(() => expect(api.getTradingWorkspace).toHaveBeenCalledTimes(1));
    expect(api.getTradingWorkspace).toHaveBeenLastCalledWith(expect.objectContaining({
      signal: expect.any(AbortSignal)
    }));
    expect(vi.mocked(api.getTradingWorkspace).mock.calls[0]?.[0]?.fundAccountId).toBeUndefined();

    await act(async () => {
      await resolveRefreshBatch(0, "label-scope");
      await flushAsync();
    });
  });

  it("polls only the visible route-relevant refresh lanes", async () => {
    const intervals: Array<{ handler: TimerHandler; delay?: number }> = [];
    vi.spyOn(window, "setInterval").mockImplementation((handler: TimerHandler, delay?: number) => {
      intervals.push({ handler, delay });
      return intervals.length as unknown as ReturnType<typeof setInterval>;
    });
    vi.spyOn(window, "clearInterval").mockImplementation(() => undefined);
    const { unmount } = renderHook(() => useWorkstationData({ activeWorkspace: "data" }));

    await waitFor(() => expect(api.getSession).toHaveBeenCalledTimes(1));
    await act(async () => {
      await resolveRefreshBatch(0, "initial");
      await flushAsync();
    });

    const workstationIntervals = intervals.filter((interval) => interval.delay === 30_000 || interval.delay === 5_000);
    expect(workstationIntervals).toHaveLength(1);
    expect(workstationIntervals[0].delay).toBe(30_000);
    act(() => {
      const handler = workstationIntervals[0].handler;
      if (typeof handler === "function") {
        handler();
      }
    });
    await waitFor(() => expect(api.getProviderConnections).toHaveBeenCalledTimes(2));

    expect(api.getTradingWorkspace).toHaveBeenCalledTimes(1);
    expect(api.getPortfolioWorkspace).toHaveBeenCalledTimes(1);
    unmount();
  });

  it("pauses automatic polling while the document is hidden", async () => {
    const setIntervalSpy = vi.spyOn(window, "setInterval");
    vi.spyOn(document, "visibilityState", "get").mockReturnValue("hidden");
    const { unmount } = renderHook(() => useWorkstationData({ activeWorkspace: "portfolio" }));

    await waitFor(() => expect(api.getSession).toHaveBeenCalledTimes(1));
    await act(async () => {
      await resolveRefreshBatch(0, "initial");
      await flushAsync();
    });

    const workstationIntervalCalls = setIntervalSpy.mock.calls.filter(([, delay]) => delay === 30_000 || delay === 5_000);
    expect(workstationIntervalCalls).toHaveLength(0);
    expect(api.getTradingWorkspace).toHaveBeenCalledTimes(1);
    expect(api.getProviderConnections).toHaveBeenCalledTimes(1);
    expect(api.getPortfolioWorkspace).toHaveBeenCalledTimes(1);
    unmount();
  });

  it("surfaces development fixture usage after bootstrap", async () => {
    const { result } = renderHook(() => useWorkstationData());

    await waitFor(() => expect(api.getSession).toHaveBeenCalledTimes(1));
    vi.mocked(api.hasDevelopmentFixtureUsage).mockReturnValue(true);

    await act(async () => {
      await resolveRefreshBatch(0, "fixture");
      await flushAsync();
    });

    expect(api.resetDevelopmentFixtureUsage).toHaveBeenCalled();
    expect(result.current.usingDevelopmentFixtures).toBe(true);
  });

  it("merges workflow preset mutation results into the shell catalog", async () => {
    const { result } = renderHook(() => useWorkstationData());

    await waitFor(() => expect(api.getSession).toHaveBeenCalledTimes(1));

    await act(async () => {
      await resolveRefreshBatch(0, "workflow");
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
      await resolveRefreshBatch(0, "initial");
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
      resolveRequest<ProviderReadinessSummary>("providerReadiness", 1, buildProviderReadiness("routing-refresh"));
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
      await resolveRefreshBatch(0, "initial");
      await flushAsync();
    });

    let successfulRefresh!: Promise<void>;
    act(() => {
      successfulRefresh = result.current.refreshProviderRouting();
    });
    await waitFor(() => expect(api.getProviderRoutingConnections).toHaveBeenCalledTimes(2));
    await act(async () => {
      resolveRequest<ProviderConnectionRow[]>("providerConnections", 1, []);
      resolveRequest<ProviderReadinessSummary>("providerReadiness", 1, buildProviderReadiness("routing-success"));
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
      rejectRequest("providerReadiness", 2, new Error("Provider readiness timed out."));
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
      await resolveRefreshBatch(0, "initial");
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
      await resolveRefreshBatchWithIndexes({
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
      await resolveRefreshBatch(0, "initial");
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
    const { result } = renderHook(() => useWorkstationData({ activeWorkspace: "portfolio" }));

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
      resolveRequestIfPresent<StrategyWorkspaceResponse>("strategy", 0, { marker: "strategy" } as unknown as StrategyWorkspaceResponse);
      resolveRequestIfPresent<TradingWorkspaceResponse>("trading", 0, { marker: "trading" } as unknown as TradingWorkspaceResponse);
      rejectRequest("portfolio", 0, new Error("Portfolio workspace timed out."));
      resolveRequest<MultiAssetCoverageSummary>(
        "portfolioMultiAssetCoverage",
        0,
        { marker: "portfolio coverage" } as unknown as MultiAssetCoverageSummary
      );
      resolveRequestIfPresent<DataWorkspaceResponse>("data", 0, { marker: "data" } as unknown as DataWorkspaceResponse);
      resolveRequestIfPresent<AccountingWorkspaceResponse>("accounting", 0, { marker: "accounting" } as unknown as AccountingWorkspaceResponse);
      resolveRequestIfPresent<ReportingWorkspaceResponse>("reporting", 0, { marker: "reporting" } as unknown as ReportingWorkspaceResponse);
      rejectRequest("brokerageConnection", 0, new Error("Alpaca connection status failed."));
      resolveRequestIfPresent<BrokerageConnectionStatus>("robinhoodConnection", 0, { marker: "robinhood" } as unknown as BrokerageConnectionStatus);
      resolveRequestIfPresent<ProviderConnectionRow[]>("providerConnections", 0, []);
      resolveRequestIfPresent<ProviderReadinessSummary>("providerReadiness", 0, buildProviderReadiness("partial-failure"));
      resolveRequestIfPresent<ProviderRoutingConnection[]>("providerRoutingConnections", 0, []);
      resolveRequestIfPresent<ProviderRoutingBinding[]>("providerRoutingBindings", 0, []);
      resolveRequestIfPresent<ProviderRoutingTrustSnapshot[]>("providerRoutingTrustSnapshots", 0, []);
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
      await resolveRefreshBatch(0, "initial");
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
      await resolveRefreshBatch(0, "initial");
      await flushAsync();
    });

    let portfolioRefresh!: Promise<void>;
    act(() => {
      portfolioRefresh = result.current.refreshPortfolio();
    });
    await waitFor(() => expect(api.getPortfolioWorkspace).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(api.getPortfolioMultiAssetCoverage).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(api.getBrokerageHouseholdPortfolio).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(api.getReportingWorkspace).toHaveBeenCalledTimes(2));

    await act(async () => {
      rejectRequest("portfolio", 1, new Error("Portfolio refresh failed."));
      resolveRequest<MultiAssetCoverageSummary>(
        "portfolioMultiAssetCoverage",
        1,
        { marker: "refreshed portfolio coverage" } as unknown as MultiAssetCoverageSummary
      );
      rejectRequest("brokeragePortfolio", 1, new Error("Brokerage portfolio refresh failed."));
      resolveRequest<ReportingWorkspaceResponse>("reporting", 1, { marker: "refreshed reporting" } as unknown as ReportingWorkspaceResponse);
      await portfolioRefresh;
    });

    expect(result.current.portfolio).toEqual({ marker: "initial portfolio" });
    expect(result.current.brokeragePortfolio).toEqual({ marker: "initial brokerage" });
    expect(result.current.reporting).toEqual({ marker: "refreshed reporting" });
    expect(result.current.workspaceErrors.portfolio).toBe(
      "Portfolio refresh failed.; Brokerage portfolio refresh failed."
    );
    expect(result.current.error).toBe(result.current.workspaceErrors.portfolio);
  });

  it("clears the portfolio-only refresh failure after a later portfolio refresh succeeds", async () => {
    const { result } = renderHook(() => useWorkstationData());

    await waitFor(() => expect(api.getSession).toHaveBeenCalledTimes(1));

    await act(async () => {
      await resolveRefreshBatch(0, "initial");
      await flushAsync();
    });

    let failedRefresh!: Promise<void>;
    act(() => {
      failedRefresh = result.current.refreshPortfolio();
    });
    await waitFor(() => expect(api.getPortfolioWorkspace).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(api.getPortfolioMultiAssetCoverage).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(api.getBrokerageHouseholdPortfolio).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(api.getReportingWorkspace).toHaveBeenCalledTimes(2));
    await act(async () => {
      rejectRequest("portfolio", 1, new Error("Portfolio refresh failed."));
      resolveRequest<MultiAssetCoverageSummary>(
        "portfolioMultiAssetCoverage",
        1,
        { marker: "failed-refresh portfolio coverage" } as unknown as MultiAssetCoverageSummary
      );
      rejectRequest("brokeragePortfolio", 1, new Error("Brokerage portfolio refresh failed."));
      rejectRequest("reporting", 1, new Error("Reporting refresh failed."));
      await failedRefresh;
    });

    let recoveryRefresh!: Promise<void>;
    act(() => {
      recoveryRefresh = result.current.refreshPortfolio();
    });
    await waitFor(() => expect(api.getPortfolioWorkspace).toHaveBeenCalledTimes(3));
    await waitFor(() => expect(api.getPortfolioMultiAssetCoverage).toHaveBeenCalledTimes(3));
    await waitFor(() => expect(api.getBrokerageHouseholdPortfolio).toHaveBeenCalledTimes(3));
    await waitFor(() => expect(api.getReportingWorkspace).toHaveBeenCalledTimes(3));
    await act(async () => {
      resolveRequest<PortfolioWorkspaceResponse>("portfolio", 2, { marker: "recovered portfolio" } as unknown as PortfolioWorkspaceResponse);
      resolveRequest<MultiAssetCoverageSummary>(
        "portfolioMultiAssetCoverage",
        2,
        { marker: "recovered portfolio coverage" } as unknown as MultiAssetCoverageSummary
      );
      resolveRequest<BrokerageHouseholdPortfolio>("brokeragePortfolio", 2, { marker: "recovered brokerage" } as unknown as BrokerageHouseholdPortfolio);
      resolveRequest<ReportingWorkspaceResponse>("reporting", 2, { marker: "recovered reporting" } as unknown as ReportingWorkspaceResponse);
      await recoveryRefresh;
    });

    expect(result.current.portfolio).toEqual({ marker: "recovered portfolio" });
    expect(result.current.brokeragePortfolio).toEqual({ marker: "recovered brokerage" });
    expect(result.current.reporting).toEqual({ marker: "recovered reporting" });
    expect(result.current.workspaceErrors.portfolio).toBeUndefined();
    expect(result.current.workspaceErrors.reporting).toBeUndefined();
    expect(result.current.error).toBeNull();
  });

  it("refreshes reporting live-view payloads with the portfolio refresh lane", async () => {
    const { result } = renderHook(() => useWorkstationData({ activeWorkspace: "reporting" }));

    await waitFor(() => expect(api.getSession).toHaveBeenCalledTimes(1));

    await act(async () => {
      await resolveRefreshBatch(0, "initial");
      await flushAsync();
    });

    let portfolioRefresh!: Promise<void>;
    act(() => {
      portfolioRefresh = result.current.refreshPortfolio();
    });
    await waitFor(() => expect(api.getPortfolioWorkspace).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(api.getReportingWorkspace).toHaveBeenCalledTimes(2));

    await act(async () => {
      resolveRequest<PortfolioWorkspaceResponse>("portfolio", 1, { marker: "live portfolio" } as unknown as PortfolioWorkspaceResponse);
      resolveRequest<MultiAssetCoverageSummary>(
        "portfolioMultiAssetCoverage",
        1,
        { marker: "live portfolio coverage" } as unknown as MultiAssetCoverageSummary
      );
      resolveRequest<BrokerageHouseholdPortfolio>("brokeragePortfolio", 1, { marker: "live brokerage" } as unknown as BrokerageHouseholdPortfolio);
      resolveRequest<ReportingWorkspaceResponse>("reporting", 1, { marker: "live reporting views" } as unknown as ReportingWorkspaceResponse);
      await portfolioRefresh;
    });

    expect(result.current.portfolio).toEqual({ marker: "live portfolio" });
    expect(result.current.reporting).toEqual({ marker: "live reporting views" });
    expect(result.current.workspaceErrors.reporting).toBeUndefined();
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
  let settled = false;
  const promise = new Promise<T>((promiseResolve, promiseReject) => {
    resolve = (value) => {
      settled = true;
      promiseResolve(value);
    };
    reject = (reason) => {
      settled = true;
      promiseReject(reason);
    };
  });

  return {
    promise,
    resolve,
    reject,
    get settled() {
      return settled;
    }
  };
}

function buildWorkflowSummary(marker: string): OperatorWorkflowHomeSummary {
  return {
    generatedAt: "2026-01-01T00:00:00Z",
    hasOperatingContext: true,
    operatingContextLabel: `${marker} context`,
    fundDisplayName: `${marker} fund`,
    workspaces: [
      {
        workspaceId: "accounting",
        workspaceTitle: "Accounting",
        statusLabel: "Financial operations exceptions require review",
        statusDetail: `${marker} financial operations detail`,
        statusTone: "Warning",
        nextAction: {
          label: "Resolve Exceptions",
          detail: "Open reconciliation casework.",
          targetPageTag: "FundReconciliation",
          tone: "Primary"
        },
        primaryBlocker: {
          code: "financial-operations-exceptions",
          label: "1 unresolved exception",
          detail: "Resolve the retained exception before close.",
          tone: "Warning",
          isBlocking: true
        },
        evidence: [
          { label: "Core flow", value: "Resolve Exceptions", tone: "Warning" },
          { label: "Breaks", value: "1", tone: "Warning" },
          { label: "Approval", value: "Pending", tone: "Warning" },
          { label: "Evidence", value: "1", tone: "Success" }
        ]
      }
    ]
  };
}

function resolveRefreshBatch(index: number, marker: string) {
  return resolveRefreshBatchWithIndexes({ marker, defaultIndex: index, tradingIndex: index });
}

function resolveSecondaryRefreshBatch(index: number, marker: string) {
  resolveRequest<StrategyWorkspaceResponse>("strategy", index, { marker: `${marker} strategy` } as unknown as StrategyWorkspaceResponse);
  resolveRequest<TradingWorkspaceResponse>("trading", index, { marker: `${marker} trading` } as unknown as TradingWorkspaceResponse);
  resolveRequest<PortfolioWorkspaceResponse>("portfolio", index, { marker: `${marker} portfolio` } as unknown as PortfolioWorkspaceResponse);
  resolveRequest<DataWorkspaceResponse>("data", index, { marker: `${marker} data` } as unknown as DataWorkspaceResponse);
  resolveRequest<ReportingWorkspaceResponse>("reporting", index, { marker: `${marker} reporting` } as unknown as ReportingWorkspaceResponse);
  resolveRequest<BrokerageConnectionStatus>("brokerageConnection", index, { marker: `${marker} connection` } as unknown as BrokerageConnectionStatus);
  resolveRequest<BrokerageConnectionStatus>("robinhoodConnection", index, { marker: `${marker} robinhood` } as unknown as BrokerageConnectionStatus);
  resolveRequest<ProviderConnectionRow[]>("providerConnections", index, []);
  resolveRequest<ProviderReadinessSummary>("providerReadiness", index, buildProviderReadiness(marker));
  resolveRequest<ProviderRoutingConnection[]>("providerRoutingConnections", index, []);
  resolveRequest<ProviderRoutingBinding[]>("providerRoutingBindings", index, []);
  resolveRequest<ProviderRoutingTrustSnapshot[]>("providerRoutingTrustSnapshots", index, []);
  resolveRequest<BrokerageHouseholdPortfolio>("brokeragePortfolio", index, { marker: `${marker} brokerage` } as unknown as BrokerageHouseholdPortfolio);
}

async function resolveRefreshBatchWithIndexes({
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
  resolveRequestIfPresent<StrategyWorkspaceResponse>("strategy", defaultIndex, { marker: `${marker} strategy` } as unknown as StrategyWorkspaceResponse);
  resolveRequestIfPresent<TradingWorkspaceResponse>("trading", tradingIndex, { marker: `${marker} trading` } as unknown as TradingWorkspaceResponse);
  resolveRequestIfPresent<PortfolioWorkspaceResponse>("portfolio", defaultIndex, { marker: `${marker} portfolio` } as unknown as PortfolioWorkspaceResponse);
  resolveRequestIfPresent<MultiAssetCoverageSummary>(
    "portfolioMultiAssetCoverage",
    defaultIndex,
    { marker: `${marker} portfolio coverage` } as unknown as MultiAssetCoverageSummary
  );
  resolveRequestIfPresent<DataWorkspaceResponse>("data", defaultIndex, { marker: `${marker} data` } as unknown as DataWorkspaceResponse);
  resolveRequestIfPresent<AccountingWorkspaceResponse>("accounting", defaultIndex, { marker: `${marker} accounting` } as unknown as AccountingWorkspaceResponse);
  resolveRequestIfPresent<ReportingWorkspaceResponse>("reporting", defaultIndex, { marker: `${marker} reporting` } as unknown as ReportingWorkspaceResponse);
  resolveRequestIfPresent<BrokerageConnectionStatus>("brokerageConnection", defaultIndex, { marker: `${marker} connection` } as unknown as BrokerageConnectionStatus);
  resolveRequestIfPresent<BrokerageConnectionStatus>("robinhoodConnection", defaultIndex, { marker: `${marker} robinhood` } as unknown as BrokerageConnectionStatus);
  resolveRequestIfPresent<ProviderConnectionRow[]>("providerConnections", defaultIndex, []);
  resolveRequestIfPresent<ProviderReadinessSummary>("providerReadiness", defaultIndex, buildProviderReadiness(marker));
  resolveRequestIfPresent<ProviderRoutingConnection[]>("providerRoutingConnections", defaultIndex, []);
  resolveRequestIfPresent<ProviderRoutingBinding[]>("providerRoutingBindings", defaultIndex, []);
  resolveRequestIfPresent<ProviderRoutingTrustSnapshot[]>("providerRoutingTrustSnapshots", defaultIndex, []);
  resolveRequestIfPresent<BrokerageHouseholdPortfolio>("brokeragePortfolio", defaultIndex, { marker: `${marker} brokerage` } as unknown as BrokerageHouseholdPortfolio);
  resolveRequest<WorkflowLibrary>("workflowLibrary", defaultIndex, { marker: `${marker} workflows` } as unknown as WorkflowLibrary);
  resolveRequest<WorkflowPresetLibrary>("workflowPresets", defaultIndex, {
    generatedAt: "2026-01-01T00:00:00Z",
    presets: []
  });
  await resolveDeferredRefreshBatch(marker);
}

async function resolveDeferredRefreshBatch(marker: string) {
  await flushTimers();
  resolveFirstPendingRequestIfPresent<StrategyWorkspaceResponse>("strategy", { marker: `${marker} strategy` } as unknown as StrategyWorkspaceResponse);
  resolveFirstPendingRequestIfPresent<TradingWorkspaceResponse>("trading", { marker: `${marker} trading` } as unknown as TradingWorkspaceResponse);
  resolveFirstPendingRequestIfPresent<PortfolioWorkspaceResponse>("portfolio", { marker: `${marker} portfolio` } as unknown as PortfolioWorkspaceResponse);
  resolveFirstPendingRequestIfPresent<MultiAssetCoverageSummary>(
    "portfolioMultiAssetCoverage",
    { marker: `${marker} portfolio coverage` } as unknown as MultiAssetCoverageSummary
  );
  resolveFirstPendingRequestIfPresent<DataWorkspaceResponse>("data", { marker: `${marker} data` } as unknown as DataWorkspaceResponse);
  resolveFirstPendingRequestIfPresent<AccountingWorkspaceResponse>("accounting", { marker: `${marker} accounting` } as unknown as AccountingWorkspaceResponse);
  resolveFirstPendingRequestIfPresent<ReportingWorkspaceResponse>("reporting", { marker: `${marker} reporting` } as unknown as ReportingWorkspaceResponse);
  resolveFirstPendingRequestIfPresent<BrokerageConnectionStatus>("brokerageConnection", { marker: `${marker} connection` } as unknown as BrokerageConnectionStatus);
  resolveFirstPendingRequestIfPresent<BrokerageConnectionStatus>("robinhoodConnection", { marker: `${marker} robinhood` } as unknown as BrokerageConnectionStatus);
  resolveFirstPendingRequestIfPresent<ProviderConnectionRow[]>("providerConnections", []);
  resolveFirstPendingRequestIfPresent<ProviderReadinessSummary>("providerReadiness", buildProviderReadiness(marker));
  resolveFirstPendingRequestIfPresent<ProviderRoutingConnection[]>("providerRoutingConnections", []);
  resolveFirstPendingRequestIfPresent<ProviderRoutingBinding[]>("providerRoutingBindings", []);
  resolveFirstPendingRequestIfPresent<ProviderRoutingTrustSnapshot[]>("providerRoutingTrustSnapshots", []);
  resolveFirstPendingRequestIfPresent<BrokerageHouseholdPortfolio>("brokeragePortfolio", { marker: `${marker} brokerage` } as unknown as BrokerageHouseholdPortfolio);
}

function flushTimers() {
  return new Promise<void>((resolve) => setTimeout(resolve, 0));
}

function resolveRequest<T>(key: keyof typeof requests, index: number, value: T) {
  const request = requests[key][index] as Deferred<T> | undefined;
  if (!request) {
    throw new Error(`Missing ${String(key)} request ${index}`);
  }

  request.resolve(value);
}

function resolveRequestIfPresent<T>(key: keyof typeof requests, index: number, value: T) {
  const request = requests[key][index] as Deferred<T> | undefined;
  request?.resolve(value);
}

function resolveFirstPendingRequestIfPresent<T>(key: keyof typeof requests, value: T) {
  const request = requests[key].find((entry) => !entry.settled) as Deferred<T> | undefined;
  request?.resolve(value);
}

function rejectRequest(key: keyof typeof requests, index: number, reason: unknown) {
  const request = requests[key][index];
  if (!request) {
    throw new Error(`Missing ${String(key)} request ${index}`);
  }

  request.reject(reason);
}

function buildProviderReadiness(marker: string): ProviderReadinessSummary {
  return {
    asOf: "2026-01-01T00:00:00Z",
    status: "Ready",
    totalProviders: 0,
    readyProviders: 0,
    reviewProviders: 0,
    degradedProviders: 0,
    blockedProviders: 0,
    summary: `${marker} provider readiness`,
    recommendedAction: "No provider action required.",
    providers: []
  };
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
