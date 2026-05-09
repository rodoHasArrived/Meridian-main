import { act, renderHook, waitFor } from "@testing-library/react";
import { createElement, StrictMode, type ReactNode } from "react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { useWorkstationData } from "@/hooks/use-workstation-data";
import * as api from "@/lib/api";
import type {
  BrokerageConnectionStatus,
  BrokerageHouseholdPortfolio,
  DataOperationsWorkspaceResponse,
  GovernanceWorkspaceResponse,
  PortfolioWorkspaceResponse,
  ResearchWorkspaceResponse,
  SessionInfo,
  SystemOverviewResponse,
  TradingWorkspaceResponse,
  WorkflowLibrary,
  WorkflowPresetLibrary
} from "@/types";

vi.mock("@/lib/api", () => ({
  getBrokerageHouseholdPortfolio: vi.fn(),
  getDataWorkspace: vi.fn(),
  getGovernanceWorkspace: vi.fn(),
  getAlpacaConnectionStatus: vi.fn(),
  hasDevelopmentFixtureUsage: vi.fn(() => false),
  getPortfolioWorkspace: vi.fn(),
  getReportingWorkspace: vi.fn(),
  resetDevelopmentFixtureUsage: vi.fn(),
  getSession: vi.fn(),
  getStrategyWorkspace: vi.fn(),
  getSystemStatus: vi.fn(),
  getTradingWorkspace: vi.fn(),
  getWorkflowLibrary: vi.fn(),
  getWorkflowPresets: vi.fn()
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
    vi.mocked(api.getBrokerageHouseholdPortfolio).mockImplementation(() => track<BrokerageHouseholdPortfolio>("brokeragePortfolio"));
    vi.mocked(api.getWorkflowLibrary).mockImplementation(() => track<WorkflowLibrary>("workflowLibrary"));
    vi.mocked(api.getWorkflowPresets).mockImplementation(() => track<WorkflowPresetLibrary>("workflowPresets"));
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
  resolveRequest<SessionInfo>("session", index, {
    activeWorkspace: "trading",
    commandCount: 1,
    displayName: `${marker} session`,
    environment: "paper",
    role: "Operator"
  });
  resolveRequest<SystemOverviewResponse>("overview", index, { marker: `${marker} overview` } as unknown as SystemOverviewResponse);
  resolveRequest<ResearchWorkspaceResponse>("research", index, { marker: `${marker} research` } as unknown as ResearchWorkspaceResponse);
  resolveRequest<TradingWorkspaceResponse>("trading", index, { marker: `${marker} trading` } as unknown as TradingWorkspaceResponse);
  resolveRequest<PortfolioWorkspaceResponse>("portfolio", index, { marker: `${marker} portfolio` } as unknown as PortfolioWorkspaceResponse);
  resolveRequest<DataOperationsWorkspaceResponse>("dataOperations", index, { marker: `${marker} data` } as unknown as DataOperationsWorkspaceResponse);
  resolveRequest<GovernanceWorkspaceResponse>("governance", index, { marker: `${marker} accounting` } as unknown as GovernanceWorkspaceResponse);
  resolveRequest<GovernanceWorkspaceResponse>("reporting", index, { marker: `${marker} reporting` } as unknown as GovernanceWorkspaceResponse);
  resolveRequest<BrokerageConnectionStatus>("brokerageConnection", index, { marker: `${marker} connection` } as unknown as BrokerageConnectionStatus);
  resolveRequest<BrokerageHouseholdPortfolio>("brokeragePortfolio", index, { marker: `${marker} brokerage` } as unknown as BrokerageHouseholdPortfolio);
  resolveRequest<WorkflowLibrary>("workflowLibrary", index, { marker: `${marker} workflows` } as unknown as WorkflowLibrary);
  resolveRequest<WorkflowPresetLibrary>("workflowPresets", index, { marker: `${marker} presets` } as unknown as WorkflowPresetLibrary);
}

function resolveRequest<T>(key: keyof typeof requests, index: number, value: T) {
  const request = requests[key][index] as Deferred<T> | undefined;
  if (!request) {
    throw new Error(`Missing ${String(key)} request ${index}`);
  }

  request.resolve(value);
}

async function flushAsync() {
  await Promise.resolve();
  await Promise.resolve();
}
