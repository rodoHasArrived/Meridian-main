import { useCallback, useEffect, useRef, useState } from "react";
import {
  getBrokerageHouseholdPortfolio,
  getDataWorkspace,
  getGovernanceWorkspace,
  getAlpacaConnectionStatus,
  hasDevelopmentFixtureUsage,
  getPortfolioWorkspace,
  getReportingWorkspace,
  resetDevelopmentFixtureUsage,
  getSession,
  getStrategyWorkspace,
  getSystemStatus,
  getTradingWorkspace,
  getWorkflowLibrary,
  getWorkflowPresets
} from "@/lib/api";
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
  WorkflowPreset,
  WorkflowLibrary,
  WorkflowPresetLibrary,
  WorkspaceKey
} from "@/types";

type WorkspaceErrorMap = Partial<Record<WorkspaceKey, string>>;

interface WorkstationDataState {
  session: SessionInfo | null;
  overview: SystemOverviewResponse | null;
  research: ResearchWorkspaceResponse | null;
  trading: TradingWorkspaceResponse | null;
  portfolio: PortfolioWorkspaceResponse | null;
  dataOperations: DataOperationsWorkspaceResponse | null;
  governance: GovernanceWorkspaceResponse | null;
  reporting: GovernanceWorkspaceResponse | null;
  brokerageConnection: BrokerageConnectionStatus | null;
  brokeragePortfolio: BrokerageHouseholdPortfolio | null;
  workflowLibrary: WorkflowLibrary | null;
  workflowPresets: WorkflowPresetLibrary | null;
  workflowError: string | null;
  usingDevelopmentFixtures: boolean;
  loading: boolean;
  error: string | null;
  workspaceErrors: WorkspaceErrorMap;
}

const initialState: WorkstationDataState = {
  session: null,
  overview: null,
  research: null,
  trading: null,
  portfolio: null,
  dataOperations: null,
  governance: null,
  reporting: null,
  brokerageConnection: null,
  brokeragePortfolio: null,
  workflowLibrary: null,
  workflowPresets: null,
  workflowError: null,
  usingDevelopmentFixtures: false,
  loading: true,
  error: null,
  workspaceErrors: {}
};

export function useWorkstationData() {
  const [state, setState] = useState<WorkstationDataState>(initialState);
  const mountedRef = useRef(true);
  const refreshRevisionRef = useRef(0);
  const tradingRefreshRevisionRef = useRef(0);
  const refreshAbortRef = useRef<AbortController | null>(null);
  const tradingRefreshAbortRef = useRef<AbortController | null>(null);

  useEffect(() => {
    mountedRef.current = true;

    return () => {
      mountedRef.current = false;
      refreshRevisionRef.current += 1;
      tradingRefreshRevisionRef.current += 1;
      refreshAbortRef.current?.abort();
      tradingRefreshAbortRef.current?.abort();
    };
  }, []);

  const refresh = useCallback(async () => {
    if (!mountedRef.current) {
      return;
    }

    const revision = refreshRevisionRef.current + 1;
    refreshRevisionRef.current = revision;
    tradingRefreshRevisionRef.current += 1;
    refreshAbortRef.current?.abort();
    tradingRefreshAbortRef.current?.abort();
    const controller = new AbortController();
    refreshAbortRef.current = controller;
    const requestOptions = { signal: controller.signal };
    resetDevelopmentFixtureUsage();
    setState((current) => ({ ...current, loading: true, error: null, workflowError: null, workspaceErrors: {} }));

    const [
      session,
      overview,
      research,
      trading,
      portfolio,
      dataOperations,
      governance,
      reporting,
      brokerageConnection,
      brokeragePortfolio,
      workflowLibrary,
      workflowPresets
    ] = await Promise.allSettled([
      getSession(requestOptions),
      getSystemStatus(requestOptions),
      getStrategyWorkspace(requestOptions),
      getTradingWorkspace(requestOptions),
      getPortfolioWorkspace(requestOptions),
      getDataWorkspace(requestOptions),
      getGovernanceWorkspace(requestOptions),
      getReportingWorkspace(requestOptions),
      getAlpacaConnectionStatus(requestOptions),
      getBrokerageHouseholdPortfolio("alpaca", requestOptions),
      getWorkflowLibrary(requestOptions),
      getWorkflowPresets(requestOptions)
    ]);

    const workspaceErrors: WorkspaceErrorMap = {};
    const bootstrapErrors: string[] = [];
    const workflowErrors: string[] = [];
    const readWorkspace = <T,>(keys: WorkspaceKey[], result: PromiseSettledResult<T>): T | null => {
      if (result.status === "fulfilled") {
        return result.value;
      }

      const message = formatRequestError(result.reason, "Workspace request failed.");
      for (const key of keys) {
        appendWorkspaceError(workspaceErrors, key, message);
      }
      return null;
    };

    const readBootstrap = <T,>(result: PromiseSettledResult<T>): T | null => {
      if (result.status === "fulfilled") {
        return result.value;
      }

      bootstrapErrors.push(formatRequestError(result.reason, "Workstation bootstrap request failed."));
      return null;
    };

    const readWorkflow = <T,>(result: PromiseSettledResult<T>): T | null => {
      if (result.status === "fulfilled") {
        return result.value;
      }

      workflowErrors.push(formatRequestError(result.reason, "Workflow library request failed."));
      return null;
    };

    const nextState: WorkstationDataState = {
      session: readBootstrap(session),
      overview: readBootstrap(overview),
      research: readWorkspace(["strategy"], research),
      trading: readWorkspace(["trading"], trading),
      portfolio: readWorkspace(["portfolio"], portfolio),
      dataOperations: readWorkspace(["data"], dataOperations),
      governance: readWorkspace(["accounting"], governance),
      reporting: readWorkspace(["reporting"], reporting),
      brokerageConnection: readWorkspace(["portfolio"], brokerageConnection),
      brokeragePortfolio: readWorkspace(["portfolio"], brokeragePortfolio),
      workflowLibrary: readWorkflow(workflowLibrary),
      workflowPresets: readWorkflow(workflowPresets),
      workflowError: workflowErrors[0] ?? null,
      usingDevelopmentFixtures: hasDevelopmentFixtureUsage(),
      loading: false,
      error: Object.values(workspaceErrors)[0] ?? bootstrapErrors[0] ?? null,
      workspaceErrors
    };

    if (!mountedRef.current || refreshRevisionRef.current !== revision) {
      return;
    }

    if (refreshAbortRef.current === controller) {
      refreshAbortRef.current = null;
    }
    setState(nextState);
  }, []);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  // Keep the trading cockpit fresh without re-fetching every workspace.
  // Positions, orders, fills, and readiness status change as trading runs.
  const refreshingTrading = useRef(false);
  const refreshTrading = useCallback(async () => {
    if (refreshingTrading.current || !mountedRef.current) return;
    const revision = tradingRefreshRevisionRef.current + 1;
    tradingRefreshRevisionRef.current = revision;
    tradingRefreshAbortRef.current?.abort();
    const controller = new AbortController();
    tradingRefreshAbortRef.current = controller;
    refreshingTrading.current = true;
    try {
      const result = await getTradingWorkspace({ signal: controller.signal });
      if (!mountedRef.current || tradingRefreshRevisionRef.current !== revision) {
        return;
      }
      setState((current) => {
        const tradingError = current.workspaceErrors.trading;
        const workspaceErrors = withoutWorkspaceError(current.workspaceErrors, "trading");
        return {
          ...current,
          trading: result,
          error: current.error === tradingError ? firstWorkspaceError(workspaceErrors) ?? null : current.error,
          workspaceErrors,
          usingDevelopmentFixtures: current.usingDevelopmentFixtures || hasDevelopmentFixtureUsage()
        };
      });
    } catch (err) {
      if (!mountedRef.current || tradingRefreshRevisionRef.current !== revision) {
        return;
      }

      const message = formatRequestError(err, "Trading workspace refresh failed.");
      setState((current) => {
        const workspaceErrors = {
          ...current.workspaceErrors,
          trading: message
        };
        return {
          ...current,
          error: current.error ?? message,
          workspaceErrors
        };
      });
    } finally {
      if (tradingRefreshAbortRef.current === controller) {
        tradingRefreshAbortRef.current = null;
      }
      refreshingTrading.current = false;
    }
  }, []);

  // Keep portfolio positions in sync with strategy execution.
  const portfolioRefreshRevisionRef = useRef(0);
  const portfolioRefreshAbortRef = useRef<AbortController | null>(null);
  const refreshingPortfolio = useRef(false);
  const refreshPortfolio = useCallback(async () => {
    if (refreshingPortfolio.current || !mountedRef.current) return;
    const revision = portfolioRefreshRevisionRef.current + 1;
    portfolioRefreshRevisionRef.current = revision;
    portfolioRefreshAbortRef.current?.abort();
    const controller = new AbortController();
    portfolioRefreshAbortRef.current = controller;
    refreshingPortfolio.current = true;
    try {
      const [portfolio, brokeragePortfolio] = await Promise.allSettled([
        getPortfolioWorkspace({ signal: controller.signal }),
        getBrokerageHouseholdPortfolio("alpaca", { signal: controller.signal })
      ]);
      if (!mountedRef.current || portfolioRefreshRevisionRef.current !== revision) return;
      setState((current) => {
        let next = { ...current };
        const previousPortfolioError = current.workspaceErrors.portfolio;
        const refreshErrors: string[] = [];
        if (portfolio.status === "fulfilled") {
          next = { ...next, portfolio: portfolio.value };
        } else {
          refreshErrors.push(formatRequestError(portfolio.reason, "Portfolio workspace refresh failed"));
        }
        if (brokeragePortfolio.status === "fulfilled") {
          next = { ...next, brokeragePortfolio: brokeragePortfolio.value };
        } else {
          refreshErrors.push(formatRequestError(brokeragePortfolio.reason, "Brokerage household portfolio refresh failed"));
        }

        const refreshError = refreshErrors.length > 0 ? refreshErrors.join("; ") : null;
        const workspaceErrors = refreshError
          ? { ...current.workspaceErrors, portfolio: refreshError }
          : withoutWorkspaceError(current.workspaceErrors, "portfolio");
        const error = refreshError
          ? current.error === null || current.error === previousPortfolioError
            ? refreshError
            : current.error
          : current.error === previousPortfolioError
            ? firstWorkspaceError(workspaceErrors) ?? null
            : current.error;

        next = {
          ...next,
          workspaceErrors,
          error
        };
        return next;
      });
    } finally {
      if (portfolioRefreshAbortRef.current === controller) portfolioRefreshAbortRef.current = null;
      refreshingPortfolio.current = false;
    }
  }, []);

  const upsertWorkflowPreset = useCallback((preset: WorkflowPreset) => {
    if (!mountedRef.current) {
      return;
    }

    setState((current) => {
      const existingLibrary = current.workflowPresets ?? {
        generatedAt: preset.updatedAt,
        presets: []
      };
      const presets = [
        preset,
        ...existingLibrary.presets.filter(
          (item) => item.presetId.toLowerCase() !== preset.presetId.toLowerCase()
        )
      ].sort(compareWorkflowPresets);

      return {
        ...current,
        workflowPresets: {
          ...existingLibrary,
          generatedAt: preset.updatedAt,
          presets
        }
      };
    });
  }, []);

  useEffect(() => {
    const id = setInterval(() => { void refreshTrading(); }, 30_000);
    return () => clearInterval(id);
  }, [refreshTrading]);

  useEffect(() => {
    const id = setInterval(() => { void refreshPortfolio(); }, 60_000);
    return () => clearInterval(id);
  }, [refreshPortfolio]);

  return { ...state, refresh, refreshTrading, refreshPortfolio, upsertWorkflowPreset };
}

function formatRequestError(reason: unknown, fallback: string): string {
  return reason instanceof Error && reason.message.trim() ? reason.message : fallback;
}

function appendWorkspaceError(errors: WorkspaceErrorMap, key: WorkspaceKey, message: string) {
  const current = errors[key];
  if (!current) {
    errors[key] = message;
    return;
  }

  if (current.split("; ").includes(message)) {
    return;
  }

  errors[key] = `${current}; ${message}`;
}

function withoutWorkspaceError(errors: WorkspaceErrorMap, key: WorkspaceKey): WorkspaceErrorMap {
  if (!(key in errors)) {
    return errors;
  }

  const next = { ...errors };
  delete next[key];
  return next;
}

function firstWorkspaceError(errors: WorkspaceErrorMap): string | undefined {
  return Object.values(errors).find((value): value is string => Boolean(value));
}

function compareWorkflowPresets(left: WorkflowPreset, right: WorkflowPreset) {
  if (left.isPinned !== right.isPinned) {
    return left.isPinned ? -1 : 1;
  }

  const leftUsed = Date.parse(left.lastUsedAt ?? "") || 0;
  const rightUsed = Date.parse(right.lastUsedAt ?? "") || 0;
  if (leftUsed !== rightUsed) {
    return rightUsed - leftUsed;
  }

  const leftUpdated = Date.parse(left.updatedAt) || 0;
  const rightUpdated = Date.parse(right.updatedAt) || 0;
  if (leftUpdated !== rightUpdated) {
    return rightUpdated - leftUpdated;
  }

  return left.name.localeCompare(right.name);
}
