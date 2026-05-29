import { useCallback, useEffect, useRef, useState } from "react";
import { useRequestLifecycle, type RequestLifecycleStatus } from "@/hooks/use-request-lifecycle";
import {
  getBrokerageHouseholdPortfolio,
  getDataWorkspace,
  getGovernanceWorkspace,
  getAlpacaConnectionStatus,
  getLedgerMappingWorkbench,
  getOwnershipReview,
  getOperationsApprovalPolicyMatrix,
  getOperationsCloseCalendar,
  getProviderConnections,
  getProviderRoutingBindings,
  getProviderRoutingConnections,
  getProviderRoutingTrustSnapshots,
  hasDevelopmentFixtureUsage,
  getPortfolioWorkspace,
  getReportingWorkspace,
  resetDevelopmentFixtureUsage,
  getSession,
  getStrategyWorkspace,
  getSystemStatus,
  getTradingWorkspace,
  getWorkflowLibrary,
  getWorkflowPresets,
  getFeatureCapabilities,
  getRolePermissionCatalog,
  getSecurityAssetProfiles,
  setFeatureCapability
} from "@/lib/api";
import { describeApiError } from "@/lib/api-errors";
import type {
  BrokerageConnectionStatus,
  BrokerageHouseholdPortfolio,
  DataOperationsWorkspaceResponse,
  FeatureCapabilitySettingsResponse,
  GovernanceWorkspaceResponse,
  LedgerMappingWorkbench,
  OperationsApprovalPolicyMatrix,
  OperationsCloseCalendar,
  OwnershipReviewModel,
  PortfolioWorkspaceResponse,
  ProviderConnectionRow,
  ProviderRoutingBinding,
  ProviderRoutingConnection,
  ProviderRoutingTrustSnapshot,
  ResearchWorkspaceResponse,
  SessionInfo,
  SystemOverviewResponse,
  TradingWorkspaceResponse,
  RolePermissionCatalog,
  SecurityAssetProfileDefinition,
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
  providerConnections: ProviderConnectionRow[] | null;
  providerRoutingConnections: ProviderRoutingConnection[] | null;
  providerRoutingBindings: ProviderRoutingBinding[] | null;
  providerRoutingTrustSnapshots: ProviderRoutingTrustSnapshot[] | null;
  providerRoutingRefreshing: boolean;
  rolePermissionCatalog: RolePermissionCatalog | null;
  securityAssetProfiles: SecurityAssetProfileDefinition[] | null;
  ledgerMappingWorkbench: LedgerMappingWorkbench | null;
  operationsApprovalPolicyMatrix: OperationsApprovalPolicyMatrix | null;
  operationsCloseCalendar: OperationsCloseCalendar | null;
  ownershipReview: OwnershipReviewModel | null;
  brokeragePortfolio: BrokerageHouseholdPortfolio | null;
  workflowLibrary: WorkflowLibrary | null;
  workflowPresets: WorkflowPresetLibrary | null;
  featureCapabilities: FeatureCapabilitySettingsResponse | null;
  workflowError: string | null;
  usingDevelopmentFixtures: boolean;
  loading: boolean;
  error: string | null;
  workspaceErrors: WorkspaceErrorMap;
  refreshStatus: RequestLifecycleStatus;
  tradingRefreshStatus: RequestLifecycleStatus;
  providerRoutingRefreshStatus: RequestLifecycleStatus;
  portfolioRefreshStatus: RequestLifecycleStatus;
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
  ownershipReview: null,
  brokeragePortfolio: null,
  workflowLibrary: null,
  workflowPresets: null,
  featureCapabilities: null,
  workflowError: null,
  usingDevelopmentFixtures: false,
  loading: true,
  error: null,
  workspaceErrors: {},
  refreshStatus: null as unknown as RequestLifecycleStatus,
  tradingRefreshStatus: null as unknown as RequestLifecycleStatus,
  providerRoutingRefreshStatus: null as unknown as RequestLifecycleStatus,
  portfolioRefreshStatus: null as unknown as RequestLifecycleStatus
};

export function useWorkstationData() {
  const fullRefreshLifecycle = useRequestLifecycle({
    operation: "workstation overview refresh",
    runningMessage: "Refreshing workstation overview, workspaces, and shared evidence.",
    successMessage: "Workstation data refreshed.",
    failureMessage: "Workstation refresh failed.",
    staleMessage: "Older workstation refresh response discarded.",
    maxRetries: 2
  });
  const tradingRefreshLifecycle = useRequestLifecycle({
    operation: "trading workspace refresh",
    runningMessage: "Refreshing trading workspace evidence.",
    successMessage: "Trading workspace refreshed.",
    failureMessage: "Trading workspace refresh failed.",
    staleMessage: "Older trading refresh response discarded.",
    maxRetries: 2
  });
  const providerRoutingRefreshLifecycle = useRequestLifecycle({
    operation: "provider routing refresh",
    runningMessage: "Refreshing provider-routing evidence.",
    successMessage: "Provider-routing evidence refreshed.",
    failureMessage: "Provider-routing refresh failed.",
    staleMessage: "Older provider-routing response discarded.",
    maxRetries: 2
  });
  const portfolioRefreshLifecycle = useRequestLifecycle({
    operation: "portfolio refresh",
    runningMessage: "Refreshing portfolio positions and brokerage household.",
    successMessage: "Portfolio evidence refreshed.",
    failureMessage: "Portfolio refresh failed.",
    staleMessage: "Older portfolio response discarded.",
    maxRetries: 2
  });
  const [state, setState] = useState<WorkstationDataState>(() => ({
    ...initialState,
    refreshStatus: fullRefreshLifecycle.status,
    tradingRefreshStatus: tradingRefreshLifecycle.status,
    providerRoutingRefreshStatus: providerRoutingRefreshLifecycle.status,
    portfolioRefreshStatus: portfolioRefreshLifecycle.status
  }));
  const refreshingPortfolio = useRef(false);

  useEffect(() => {
    setState((current) => ({
      ...current,
      refreshStatus: fullRefreshLifecycle.status,
      tradingRefreshStatus: tradingRefreshLifecycle.status,
      providerRoutingRefreshStatus: providerRoutingRefreshLifecycle.status,
      portfolioRefreshStatus: portfolioRefreshLifecycle.status
    }));
  }, [
    fullRefreshLifecycle.status,
    portfolioRefreshLifecycle.status,
    providerRoutingRefreshLifecycle.status,
    tradingRefreshLifecycle.status
  ]);

  const refresh = useCallback(async () => {
    tradingRefreshLifecycle.invalidate();
    providerRoutingRefreshLifecycle.invalidate();
    portfolioRefreshLifecycle.invalidate();
    const token = fullRefreshLifecycle.start();
    if (!token) {
      return;
    }

    const requestOptions = { signal: token.signal };
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
      providerConnections,
      providerRoutingConnections,
      providerRoutingBindings,
      providerRoutingTrustSnapshots,
      rolePermissionCatalog,
      securityAssetProfiles,
      ledgerMappingWorkbench,
      operationsApprovalPolicyMatrix,
      operationsCloseCalendar,
      ownershipReview,
      brokeragePortfolio,
      workflowLibrary,
      workflowPresets,
      featureCapabilities
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
      getProviderConnections(requestOptions),
      getProviderRoutingConnections(requestOptions),
      getProviderRoutingBindings(requestOptions),
      getProviderRoutingTrustSnapshots(requestOptions),
      getRolePermissionCatalog(requestOptions),
      getSecurityAssetProfiles(requestOptions),
      getLedgerMappingWorkbench(requestOptions),
      getOperationsApprovalPolicyMatrix(requestOptions),
      getOperationsCloseCalendar({}, requestOptions),
      getOwnershipReview(requestOptions),
      getBrokerageHouseholdPortfolio("alpaca", requestOptions),
      getWorkflowLibrary(requestOptions),
      getWorkflowPresets(requestOptions),
      getFeatureCapabilities(requestOptions)
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

    const nextState = {
      session: readBootstrap(session),
      overview: readBootstrap(overview),
      research: readWorkspace(["strategy"], research),
      trading: readWorkspace(["trading"], trading),
      portfolio: readWorkspace(["portfolio"], portfolio),
      dataOperations: readWorkspace(["data"], dataOperations),
      governance: readWorkspace(["accounting"], governance),
      reporting: readWorkspace(["reporting"], reporting),
      brokerageConnection: readWorkspace(["portfolio"], brokerageConnection),
      providerConnections: readWorkspace(["settings", "data"], providerConnections),
      providerRoutingConnections: readWorkspace(["settings"], providerRoutingConnections),
      providerRoutingBindings: readWorkspace(["settings"], providerRoutingBindings),
      providerRoutingTrustSnapshots: readWorkspace(["settings"], providerRoutingTrustSnapshots),
      providerRoutingRefreshing: false,
      rolePermissionCatalog: readWorkspace(["settings"], rolePermissionCatalog),
      securityAssetProfiles: readWorkspace(["settings", "data"], securityAssetProfiles),
      ledgerMappingWorkbench: readWorkspace(["settings"], ledgerMappingWorkbench),
      operationsApprovalPolicyMatrix: readWorkspace(["settings"], operationsApprovalPolicyMatrix),
      operationsCloseCalendar: readWorkspace(["settings"], operationsCloseCalendar),
      ownershipReview: readWorkspace(["settings"], ownershipReview),
      brokeragePortfolio: readWorkspace(["portfolio"], brokeragePortfolio),
      workflowLibrary: readWorkflow(workflowLibrary),
      workflowPresets: readWorkflow(workflowPresets),
      featureCapabilities: readWorkspace(["settings"], featureCapabilities),
      workflowError: workflowErrors[0] ?? null,
      usingDevelopmentFixtures: hasDevelopmentFixtureUsage(),
      loading: false,
      error: Object.values(workspaceErrors)[0] ?? bootstrapErrors[0] ?? null,
      workspaceErrors
    };

    if (!token.isCurrent()) {
      fullRefreshLifecycle.markStale(token.version);
      return;
    }

    token.safeSetState(setState, (current) => ({
      ...nextState,
      refreshStatus: current.refreshStatus,
      tradingRefreshStatus: current.tradingRefreshStatus,
      providerRoutingRefreshStatus: current.providerRoutingRefreshStatus,
      portfolioRefreshStatus: current.portfolioRefreshStatus
    }));
    fullRefreshLifecycle.succeed(token);
  }, [
    fullRefreshLifecycle.markStale,
    fullRefreshLifecycle.start,
    fullRefreshLifecycle.succeed,
    portfolioRefreshLifecycle.invalidate,
    providerRoutingRefreshLifecycle.invalidate,
    tradingRefreshLifecycle.invalidate
  ]);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  // Keep the trading cockpit fresh without re-fetching every workspace.
  // Positions, orders, fills, and readiness status change as trading runs.
  const refreshTrading = useCallback(async () => {
    const token = tradingRefreshLifecycle.start({ busyMode: "drop" });
    if (!token) return;
    try {
      const result = await getTradingWorkspace({ signal: token.signal });
      if (!token.isCurrent()) {
        tradingRefreshLifecycle.markStale(token.version);
        return;
      }
      token.safeSetState(setState, (current) => {
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
      tradingRefreshLifecycle.succeed(token);
    } catch (err) {
      if (!token.isCurrent()) {
        tradingRefreshLifecycle.markStale(token.version);
        return;
      }

      const message = formatRequestError(err, "Trading workspace refresh failed.");
      token.safeSetState(setState, (current) => {
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
      tradingRefreshLifecycle.fail(token, err, { fallback: "Trading workspace refresh failed." });
    } finally {
      tradingRefreshLifecycle.finish(token);
    }
  }, [
    tradingRefreshLifecycle.fail,
    tradingRefreshLifecycle.finish,
    tradingRefreshLifecycle.markStale,
    tradingRefreshLifecycle.start,
    tradingRefreshLifecycle.succeed
  ]);

  const updateFeatureCapability = useCallback(async (capabilityKey: string, isEnabled: boolean) => {
    const result = await setFeatureCapability(capabilityKey, isEnabled);
    setState((current) => ({
      ...current,
      featureCapabilities: result,
      workspaceErrors: withoutWorkspaceError(current.workspaceErrors, "settings"),
      error: current.workspaceErrors.settings === current.error
        ? firstWorkspaceError(withoutWorkspaceError(current.workspaceErrors, "settings")) ?? null
        : current.error
    }));
  }, []);

  // Keep provider-routing evidence current without reloading the full workstation.
  const refreshProviderRouting = useCallback(async () => {
    const token = providerRoutingRefreshLifecycle.start({ busyMode: "drop" });
    if (!token) return;
    token.safeSetState(setState, (current) => ({ ...current, providerRoutingRefreshing: true }));

    try {
      const [providerConnections, routingConnections, routingBindings, trustSnapshots] = await Promise.allSettled([
        getProviderConnections({ signal: token.signal }),
        getProviderRoutingConnections({ signal: token.signal }),
        getProviderRoutingBindings({ signal: token.signal }),
        getProviderRoutingTrustSnapshots({ signal: token.signal })
      ]);

      if (!token.isCurrent()) {
        providerRoutingRefreshLifecycle.markStale(token.version);
        return;
      }

      token.safeSetState(setState, (current) => {
        let next = { ...current };
        const previousSettingsError = current.workspaceErrors.settings;
        const refreshErrors: string[] = [];

        if (providerConnections.status === "fulfilled") {
          next = { ...next, providerConnections: providerConnections.value };
        } else {
          refreshErrors.push(formatRequestError(providerConnections.reason, "Provider connection refresh failed."));
        }

        if (routingConnections.status === "fulfilled") {
          next = { ...next, providerRoutingConnections: routingConnections.value };
        } else {
          refreshErrors.push(formatRequestError(routingConnections.reason, "Provider-routing connection refresh failed."));
        }

        if (routingBindings.status === "fulfilled") {
          next = { ...next, providerRoutingBindings: routingBindings.value };
        } else {
          refreshErrors.push(formatRequestError(routingBindings.reason, "Provider-routing binding refresh failed."));
        }

        if (trustSnapshots.status === "fulfilled") {
          next = { ...next, providerRoutingTrustSnapshots: trustSnapshots.value };
        } else {
          refreshErrors.push(formatRequestError(trustSnapshots.reason, "Provider-routing trust refresh failed."));
        }

        const refreshError = refreshErrors.length > 0 ? refreshErrors.join("; ") : null;
        const workspaceErrors = refreshError
          ? { ...current.workspaceErrors, settings: refreshError }
          : withoutWorkspaceError(current.workspaceErrors, "settings");
        const error = refreshError
          ? current.error === null || current.error === previousSettingsError
            ? refreshError
            : current.error
          : current.error === previousSettingsError
            ? firstWorkspaceError(workspaceErrors) ?? null
            : current.error;

        return {
          ...next,
          workspaceErrors,
          error,
          providerRoutingRefreshing: false,
          usingDevelopmentFixtures: current.usingDevelopmentFixtures || hasDevelopmentFixtureUsage()
        };
      });
      providerRoutingRefreshLifecycle.succeed(token);
    } finally {
      if (token.isCurrent()) {
        token.safeSetState(setState, (current) => ({ ...current, providerRoutingRefreshing: false }));
      }
      providerRoutingRefreshLifecycle.finish(token);
    }
  }, [
    providerRoutingRefreshLifecycle.finish,
    providerRoutingRefreshLifecycle.markStale,
    providerRoutingRefreshLifecycle.start,
    providerRoutingRefreshLifecycle.succeed
  ]);

  // Keep portfolio positions in sync with strategy execution.
  const refreshPortfolio = useCallback(async () => {
    if (refreshingPortfolio.current) return;
    const token = portfolioRefreshLifecycle.start({ busyMode: "drop" });
    if (!token) return;
    refreshingPortfolio.current = true;
    try {
      const [portfolio, brokeragePortfolio] = await Promise.allSettled([
        getPortfolioWorkspace({ signal: token.signal }),
        getBrokerageHouseholdPortfolio("alpaca", { signal: token.signal })
      ]);
      if (!token.isCurrent()) {
        portfolioRefreshLifecycle.markStale(token.version);
        return;
      }
      token.safeSetState(setState, (current) => {
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
      portfolioRefreshLifecycle.succeed(token);
    } finally {
      portfolioRefreshLifecycle.finish(token);
      refreshingPortfolio.current = false;
    }
  }, [
    portfolioRefreshLifecycle.finish,
    portfolioRefreshLifecycle.markStale,
    portfolioRefreshLifecycle.start,
    portfolioRefreshLifecycle.succeed
  ]);

  const upsertWorkflowPreset = useCallback((preset: WorkflowPreset) => {
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
    const id = setInterval(() => { void refreshProviderRouting(); }, 30_000);
    return () => clearInterval(id);
  }, [refreshProviderRouting]);

  useEffect(() => {
    const id = setInterval(() => { void refreshPortfolio(); }, 5_000);
    return () => clearInterval(id);
  }, [refreshPortfolio]);

  return { ...state, refresh, refreshTrading, refreshPortfolio, refreshProviderRouting, updateFeatureCapability, upsertWorkflowPreset };
}

function formatRequestError(reason: unknown, fallback: string): string {
  return describeApiError(reason, fallback).summary;
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
