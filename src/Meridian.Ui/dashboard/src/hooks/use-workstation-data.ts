import { useCallback, useEffect, useRef, useState } from "react";
import { useRequestLifecycle, type RequestLifecycleStatus } from "@/hooks/use-request-lifecycle";
import {
  getBrokerageHouseholdPortfolio,
  getDataWorkspace,
  getAccountingWorkspace,
  getAlpacaConnectionStatus,
  getRobinhoodConnectionStatus,
  getLedgerMappingWorkbench,
  getOperationsApprovalPolicyMatrix,
  getOperationsCloseCalendar,
  getProviderConnections,
  getProviderReadiness,
  getProviderRoutingBindings,
  getProviderRoutingConnections,
  getProviderRoutingTrustSnapshots,
  hasDevelopmentFixtureUsage,
  getPortfolioMultiAssetCoverage,
  getPortfolioWorkspace,
  getReportingWorkspace,
  resetDevelopmentFixtureUsage,
  getSession,
  getStrategyWorkspace,
  getSystemStatus,
  getTradingWorkspace,
  getWorkstationWorkflowSummary,
  getWorkflowLibrary,
  getWorkflowPresets,
  getFeatureCapabilities,
  getRolePermissionCatalog,
  getSecurityAssetProfiles,
  setFeatureCapability
} from "@/lib/api";
import { describeApiError } from "@/lib/api-errors";
import { normalizeFundAccountGuid } from "@/lib/fund-account-scope";
import type {
  BrokerageConnectionStatus,
  BrokerageHouseholdPortfolio,
  DataWorkspaceResponse,
  FeatureCapabilitySettingsResponse,
  AccountingWorkspaceResponse,
  ReportingWorkspaceResponse,
  LedgerMappingWorkbench,
  OperationsApprovalPolicyMatrix,
  OperationsCloseCalendar,
  OperatorWorkflowHomeSummary,
  MultiAssetCoverageSummary,
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
  RolePermissionCatalog,
  SecurityAssetProfileDefinition,
  WorkflowPreset,
  WorkflowLibrary,
  WorkflowPresetLibrary,
  WorkspaceKey
} from "@/types";

type WorkspaceErrorMap = Partial<Record<WorkspaceKey, string>>;
type RefreshCategory = "bootstrap" | "workspace" | "workflow";
type RefreshDataKey =
  | "session"
  | "overview"
  | "strategy"
  | "trading"
  | "portfolio"
  | "portfolioMultiAssetCoverage"
  | "data"
  | "accounting"
  | "reporting"
  | "brokerageConnection"
  | "robinhoodConnection"
  | "providerConnections"
  | "providerReadiness"
  | "providerRoutingConnections"
  | "providerRoutingBindings"
  | "providerRoutingTrustSnapshots"
  | "rolePermissionCatalog"
  | "securityAssetProfiles"
  | "ledgerMappingWorkbench"
  | "operationsApprovalPolicyMatrix"
  | "operationsCloseCalendar"
  | "brokeragePortfolio"
  | "workflowSummary"
  | "workflowLibrary"
  | "workflowPresets"
  | "featureCapabilities";

interface RefreshEntry {
  key: RefreshDataKey;
  category: RefreshCategory;
  workspaceKeys?: WorkspaceKey[];
  start: (requestOptions: { signal: AbortSignal }) => Promise<unknown>;
}

interface SettledRefreshEntry {
  entry: RefreshEntry;
  result: PromiseSettledResult<unknown>;
}

export interface UseWorkstationDataOptions {
  activeWorkspace?: WorkspaceKey;
  workflowSummaryScope?: {
    hasOperatingContext?: boolean;
    operatingContext?: string | null;
    fundProfileId?: string | null;
    fundAccountId?: string | null;
    fundDisplayName?: string | null;
  };
}

interface WorkstationDataState {
  session: SessionInfo | null;
  overview: SystemOverviewResponse | null;
  strategy: StrategyWorkspaceResponse | null;
  trading: TradingWorkspaceResponse | null;
  portfolio: PortfolioWorkspaceResponse | null;
  portfolioMultiAssetCoverage: MultiAssetCoverageSummary | null;
  data: DataWorkspaceResponse | null;
  accounting: AccountingWorkspaceResponse | null;
  reporting: ReportingWorkspaceResponse | null;
  brokerageConnection: BrokerageConnectionStatus | null;
  robinhoodConnection: BrokerageConnectionStatus | null;
  providerConnections: ProviderConnectionRow[] | null;
  providerReadiness: ProviderReadinessSummary | null;
  providerRoutingConnections: ProviderRoutingConnection[] | null;
  providerRoutingBindings: ProviderRoutingBinding[] | null;
  providerRoutingTrustSnapshots: ProviderRoutingTrustSnapshot[] | null;
  providerRoutingRefreshing: boolean;
  rolePermissionCatalog: RolePermissionCatalog | null;
  securityAssetProfiles: SecurityAssetProfileDefinition[] | null;
  ledgerMappingWorkbench: LedgerMappingWorkbench | null;
  operationsApprovalPolicyMatrix: OperationsApprovalPolicyMatrix | null;
  operationsCloseCalendar: OperationsCloseCalendar | null;
  brokeragePortfolio: BrokerageHouseholdPortfolio | null;
  workflowLibrary: WorkflowLibrary | null;
  workflowPresets: WorkflowPresetLibrary | null;
  workflowSummary: OperatorWorkflowHomeSummary | null;
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
  loading: true,
  error: null,
  workspaceErrors: {},
  refreshStatus: null as unknown as RequestLifecycleStatus,
  tradingRefreshStatus: null as unknown as RequestLifecycleStatus,
  providerRoutingRefreshStatus: null as unknown as RequestLifecycleStatus,
  portfolioRefreshStatus: null as unknown as RequestLifecycleStatus
};

const WORKSPACE_REFRESH_STALE_MS = 30_000;

export function useWorkstationData(options: UseWorkstationDataOptions = {}) {
  const activeWorkspace = options.activeWorkspace ?? "trading";
  const workflowSummaryHasOperatingContext = options.workflowSummaryScope?.hasOperatingContext ?? false;
  const workflowSummaryOperatingContext = options.workflowSummaryScope?.operatingContext ?? null;
  const workflowSummaryFundProfileId = options.workflowSummaryScope?.fundProfileId ?? null;
  const workflowSummaryFundAccountId = options.workflowSummaryScope?.fundAccountId ?? null;
  const workflowSummaryFundDisplayName = options.workflowSummaryScope?.fundDisplayName ?? null;
  const activeWorkspaceRef = useRef<WorkspaceKey>(activeWorkspace);
  const hasPublishedBootstrapRef = useRef(false);
  const documentVisible = useDocumentVisible();
  const fullRefreshLifecycle = useRequestLifecycle({
    operation: "workstation overview refresh",
    runningMessage: "Refreshing workstation overview, workspaces, and shared evidence.",
    successMessage: "Workstation data refreshed.",
    failureMessage: "Workstation refresh failed.",
    staleMessage: "Older workstation refresh response discarded.",
    maxRetries: 2
  });
  const refreshTradingRef = useRef<(options?: { attempt?: number }) => Promise<void>>(async () => {});
  const tradingRefreshLifecycle = useRequestLifecycle({
    operation: "trading workspace refresh",
    runningMessage: "Refreshing trading workspace evidence.",
    successMessage: "Trading workspace refreshed.",
    failureMessage: "Trading workspace refresh failed.",
    staleMessage: "Older trading refresh response discarded.",
    maxRetries: 2,
    onRetry: ({ attempt }) => {
      void refreshTradingRef.current({ attempt });
    }
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
    runningMessage: "Refreshing portfolio positions, brokerage household, and reporting live views.",
    successMessage: "Portfolio and reporting evidence refreshed.",
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
  const stateRef = useRef<WorkstationDataState>(state);
  const lastLoadedAtRef = useRef<Partial<Record<RefreshDataKey, number>>>({});

  useEffect(() => {
    stateRef.current = state;
  }, [state]);

  useEffect(() => {
    activeWorkspaceRef.current = activeWorkspace;
  }, [activeWorkspace]);

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

  const refresh = useCallback(async (
    options: { includeDeferred?: boolean } = {}
  ) => {
    const activeWorkspace = activeWorkspaceRef.current;
    const shouldFetchDeferred = options.includeDeferred === true || !hasPublishedBootstrapRef.current;
    tradingRefreshLifecycle.invalidate();
    providerRoutingRefreshLifecycle.invalidate();
    portfolioRefreshLifecycle.invalidate();
    const token = fullRefreshLifecycle.start();
    if (!token) {
      return;
    }

    resetDevelopmentFixtureUsage();
    setState((current) => ({
      ...current,
      loading: !hasPublishedBootstrapRef.current,
      error: null,
      workflowError: null,
      workspaceErrors: {},
      providerRoutingRefreshing: false
    }));

    const entries = createRefreshEntries({
      hasOperatingContext: workflowSummaryHasOperatingContext,
      operatingContext: workflowSummaryOperatingContext,
      fundProfileId: workflowSummaryFundProfileId,
      fundAccountId: workflowSummaryFundAccountId,
      fundDisplayName: workflowSummaryFundDisplayName
    });
    const primaryEntries = selectPrimaryRefreshEntries(entries, activeWorkspace);
    const deferredEntries = entries.filter((entry) => !primaryEntries.includes(entry));

    const primaryResults = await settleRefreshEntries(primaryEntries, { signal: token.signal });
    if (!token.isCurrent()) {
      fullRefreshLifecycle.markStale(token.version);
      return;
    }

    const primaryPublished = token.safeSetState(setState, (current) => mergeRefreshSettlements(current, primaryResults, {
      loading: false,
      replaceErrorsFor: getWorkspaceKeysForEntries(primaryEntries)
    }));
    if (primaryPublished) {
      hasPublishedBootstrapRef.current = true;
      markRefreshEntriesLoaded(primaryResults, lastLoadedAtRef.current);
    }

    if (!shouldFetchDeferred || deferredEntries.length === 0) {
      fullRefreshLifecycle.succeed(token);
      return;
    }

    const deferredResults = await settleRefreshEntries(deferredEntries, { signal: token.signal });
    if (!token.isCurrent()) {
      fullRefreshLifecycle.markStale(token.version);
      return;
    }

    const deferredPublished = token.safeSetState(setState, (current) => mergeRefreshSettlements(current, deferredResults, {
      loading: false,
      replaceErrorsFor: getWorkspaceKeysForEntries(deferredEntries)
    }));
    if (deferredPublished) {
      hasPublishedBootstrapRef.current = true;
      markRefreshEntriesLoaded(deferredResults, lastLoadedAtRef.current);
    }
    fullRefreshLifecycle.succeed(token);
  }, [
    fullRefreshLifecycle.markStale,
    fullRefreshLifecycle.start,
    fullRefreshLifecycle.succeed,
    portfolioRefreshLifecycle.invalidate,
    providerRoutingRefreshLifecycle.invalidate,
    tradingRefreshLifecycle.invalidate,
    workflowSummaryFundAccountId,
    workflowSummaryFundDisplayName,
    workflowSummaryFundProfileId,
    workflowSummaryHasOperatingContext,
    workflowSummaryOperatingContext
  ]);

  const refreshWorkspace = useCallback(async (
    workspace: WorkspaceKey = activeWorkspaceRef.current,
    options: { staleOnly?: boolean } = {}
  ) => {
    const token = fullRefreshLifecycle.start({
      busyMode: "drop",
      runningMessage: "Refreshing active workspace evidence."
    });
    if (!token) {
      return;
    }

    resetDevelopmentFixtureUsage();
    const entries = selectPrimaryRefreshEntries(createRefreshEntries({
      hasOperatingContext: workflowSummaryHasOperatingContext,
      operatingContext: workflowSummaryOperatingContext,
      fundProfileId: workflowSummaryFundProfileId,
      fundAccountId: workflowSummaryFundAccountId,
      fundDisplayName: workflowSummaryFundDisplayName
    }), workspace);
    const scopedEntries = options.staleOnly
      ? entries.filter((entry) => shouldRefreshEntry(entry, stateRef.current, lastLoadedAtRef.current))
      : entries;

    if (scopedEntries.length === 0) {
      fullRefreshLifecycle.succeed(token, { message: "Active workspace evidence is current." });
      return;
    }

    const settlements = await settleRefreshEntries(scopedEntries, { signal: token.signal });
    if (!token.isCurrent()) {
      fullRefreshLifecycle.markStale(token.version);
      return;
    }

    const published = token.safeSetState(setState, (current) => mergeRefreshSettlements(current, settlements, {
      loading: false,
      replaceErrorsFor: getWorkspaceKeysForEntries(scopedEntries)
    }));
    if (published) {
      markRefreshEntriesLoaded(settlements, lastLoadedAtRef.current);
    }
    fullRefreshLifecycle.succeed(token);
  }, [
    fullRefreshLifecycle.markStale,
    fullRefreshLifecycle.start,
    fullRefreshLifecycle.succeed,
    workflowSummaryFundAccountId,
    workflowSummaryFundDisplayName,
    workflowSummaryFundProfileId,
    workflowSummaryHasOperatingContext,
    workflowSummaryOperatingContext
  ]);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  useEffect(() => {
    if (!hasPublishedBootstrapRef.current) {
      return;
    }

    void refreshWorkspace(activeWorkspace, { staleOnly: true });
  }, [activeWorkspace, refreshWorkspace]);

  // Keep the trading cockpit fresh without re-fetching every workspace.
  // Positions, orders, fills, and readiness status change as trading runs.
  const refreshTrading = useCallback(async (options: { attempt?: number } = {}) => {
    const token = tradingRefreshLifecycle.start({ busyMode: "drop", attempt: options.attempt });
    if (!token) return;
    try {
      const result = await getTradingWorkspace({
        signal: token.signal,
        fundAccountId: normalizeFundAccountGuid(workflowSummaryFundAccountId)
      });
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
    tradingRefreshLifecycle.succeed,
    workflowSummaryFundAccountId
  ]);

  useEffect(() => {
    refreshTradingRef.current = refreshTrading;
  }, [refreshTrading]);

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
      const [providerConnections, providerReadiness, routingConnections, routingBindings, trustSnapshots] = await Promise.allSettled([
        getProviderConnections({ signal: token.signal }),
        getProviderReadiness({ signal: token.signal }),
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

        if (providerReadiness.status === "fulfilled") {
          next = { ...next, providerReadiness: providerReadiness.value };
        } else {
          refreshErrors.push(formatRequestError(providerReadiness.reason, "Provider readiness refresh failed."));
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

  // Keep portfolio and reporting live-view projections in sync with strategy execution.
  const refreshPortfolio = useCallback(async () => {
    if (refreshingPortfolio.current) return;
    const token = portfolioRefreshLifecycle.start({ busyMode: "drop" });
    if (!token) return;
    refreshingPortfolio.current = true;
    try {
      const [portfolio, portfolioMultiAssetCoverage, brokeragePortfolio, reporting] = await Promise.allSettled([
        getPortfolioWorkspace({ signal: token.signal }),
        getPortfolioMultiAssetCoverage({ signal: token.signal }),
        getBrokerageHouseholdPortfolio("alpaca", { signal: token.signal }),
        getReportingWorkspace({ signal: token.signal })
      ]);
      if (!token.isCurrent()) {
        portfolioRefreshLifecycle.markStale(token.version);
        return;
      }
      token.safeSetState(setState, (current) => {
        let next = { ...current };
        const previousPortfolioError = current.workspaceErrors.portfolio;
        const previousReportingError = current.workspaceErrors.reporting;
        const refreshErrors: string[] = [];
        const reportingRefreshErrors: string[] = [];
        if (portfolio.status === "fulfilled") {
          next = { ...next, portfolio: portfolio.value };
        } else {
          refreshErrors.push(formatRequestError(portfolio.reason, "Portfolio workspace refresh failed"));
        }
        if (portfolioMultiAssetCoverage.status === "fulfilled") {
          next = { ...next, portfolioMultiAssetCoverage: portfolioMultiAssetCoverage.value };
        } else {
          refreshErrors.push(formatRequestError(portfolioMultiAssetCoverage.reason, "Multi-asset coverage refresh failed"));
        }
        if (brokeragePortfolio.status === "fulfilled") {
          next = { ...next, brokeragePortfolio: brokeragePortfolio.value };
        } else {
          refreshErrors.push(formatRequestError(brokeragePortfolio.reason, "Brokerage household portfolio refresh failed"));
        }
        if (reporting.status === "fulfilled") {
          next = { ...next, reporting: reporting.value };
        } else {
          reportingRefreshErrors.push(formatRequestError(reporting.reason, "Reporting live portfolio refresh failed"));
        }

        const refreshError = refreshErrors.length > 0 ? refreshErrors.join("; ") : null;
        const reportingRefreshError = reportingRefreshErrors.length > 0 ? reportingRefreshErrors.join("; ") : null;
        let workspaceErrors = refreshError
          ? { ...current.workspaceErrors, portfolio: refreshError }
          : withoutWorkspaceError(current.workspaceErrors, "portfolio");
        workspaceErrors = reportingRefreshError
          ? { ...workspaceErrors, reporting: reportingRefreshError }
          : withoutWorkspaceError(workspaceErrors, "reporting");
        const primaryRefreshError = refreshError ?? reportingRefreshError;
        const previousPrimaryError = refreshError ? previousPortfolioError : previousReportingError;
        const error = primaryRefreshError
          ? current.error === null || current.error === previousPrimaryError
            ? primaryRefreshError
            : current.error
          : current.error === previousPortfolioError || current.error === previousReportingError
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

  usePollingInterval(refreshTrading, resolveTradingPollingInterval(activeWorkspace, documentVisible));
  usePollingInterval(refreshProviderRouting, resolveProviderRoutingPollingInterval(activeWorkspace, documentVisible));
  usePollingInterval(refreshPortfolio, resolvePortfolioPollingInterval(activeWorkspace, documentVisible));

  return { ...state, refresh, refreshWorkspace, refreshTrading, refreshPortfolio, refreshProviderRouting, updateFeatureCapability, upsertWorkflowPreset };
}

function createRefreshEntries(
  workflowSummaryScope: {
    hasOperatingContext?: boolean;
    operatingContext?: string | null;
    fundProfileId?: string | null;
    fundAccountId?: string | null;
    fundDisplayName?: string | null;
  } = {}
): RefreshEntry[] {
  const tradingFundAccountId = normalizeFundAccountGuid(workflowSummaryScope.fundAccountId);
  return [
    { key: "session", category: "bootstrap", start: (requestOptions) => getSession(requestOptions) },
    { key: "overview", category: "bootstrap", start: (requestOptions) => getSystemStatus(requestOptions) },
    { key: "strategy", category: "workspace", workspaceKeys: ["strategy"], start: (requestOptions) => getStrategyWorkspace(requestOptions) },
    {
      key: "trading",
      category: "workspace",
      workspaceKeys: ["trading"],
      start: (requestOptions) => getTradingWorkspace({ ...requestOptions, fundAccountId: tradingFundAccountId })
    },
    { key: "portfolio", category: "workspace", workspaceKeys: ["portfolio"], start: (requestOptions) => getPortfolioWorkspace(requestOptions) },
    {
      key: "portfolioMultiAssetCoverage",
      category: "workspace",
      workspaceKeys: ["portfolio", "accounting"],
      start: (requestOptions) => getPortfolioMultiAssetCoverage(requestOptions)
    },
    { key: "data", category: "workspace", workspaceKeys: ["data"], start: (requestOptions) => getDataWorkspace(requestOptions) },
    { key: "accounting", category: "workspace", workspaceKeys: ["accounting"], start: (requestOptions) => getAccountingWorkspace(requestOptions) },
    { key: "reporting", category: "workspace", workspaceKeys: ["reporting"], start: (requestOptions) => getReportingWorkspace(requestOptions) },
    { key: "brokerageConnection", category: "workspace", workspaceKeys: ["portfolio"], start: (requestOptions) => getAlpacaConnectionStatus(requestOptions) },
    {
      key: "robinhoodConnection",
      category: "workspace",
      workspaceKeys: ["settings", "portfolio"],
      start: (requestOptions) => getRobinhoodConnectionStatus(requestOptions)
    },
    { key: "providerConnections", category: "workspace", workspaceKeys: ["settings", "data"], start: (requestOptions) => getProviderConnections(requestOptions) },
    { key: "providerReadiness", category: "workspace", workspaceKeys: ["settings", "data"], start: (requestOptions) => getProviderReadiness(requestOptions) },
    { key: "providerRoutingConnections", category: "workspace", workspaceKeys: ["settings"], start: (requestOptions) => getProviderRoutingConnections(requestOptions) },
    { key: "providerRoutingBindings", category: "workspace", workspaceKeys: ["settings"], start: (requestOptions) => getProviderRoutingBindings(requestOptions) },
    {
      key: "providerRoutingTrustSnapshots",
      category: "workspace",
      workspaceKeys: ["settings"],
      start: (requestOptions) => getProviderRoutingTrustSnapshots(requestOptions)
    },
    { key: "rolePermissionCatalog", category: "workspace", workspaceKeys: ["settings"], start: (requestOptions) => getRolePermissionCatalog(requestOptions) },
    { key: "securityAssetProfiles", category: "workspace", workspaceKeys: ["settings", "data"], start: (requestOptions) => getSecurityAssetProfiles(requestOptions) },
    { key: "ledgerMappingWorkbench", category: "workspace", workspaceKeys: ["settings"], start: (requestOptions) => getLedgerMappingWorkbench(requestOptions) },
    { key: "operationsApprovalPolicyMatrix", category: "workspace", workspaceKeys: ["settings"], start: (requestOptions) => getOperationsApprovalPolicyMatrix(requestOptions) },
    { key: "operationsCloseCalendar", category: "workspace", workspaceKeys: ["settings"], start: (requestOptions) => getOperationsCloseCalendar({}, requestOptions) },
    { key: "brokeragePortfolio", category: "workspace", workspaceKeys: ["portfolio"], start: (requestOptions) => getBrokerageHouseholdPortfolio("alpaca", requestOptions) },
    {
      key: "workflowSummary",
      category: "workflow",
      start: (requestOptions) => getWorkstationWorkflowSummary({
        hasOperatingContext: workflowSummaryScope.hasOperatingContext,
        operatingContext: workflowSummaryScope.operatingContext ?? undefined,
        fundProfileId: workflowSummaryScope.fundProfileId ?? undefined,
        fundAccountId: workflowSummaryScope.fundAccountId ?? undefined,
        fundDisplayName: workflowSummaryScope.fundDisplayName ?? undefined,
        signal: requestOptions.signal
      })
    },
    { key: "workflowLibrary", category: "workflow", start: (requestOptions) => getWorkflowLibrary(requestOptions) },
    { key: "workflowPresets", category: "workflow", start: (requestOptions) => getWorkflowPresets(requestOptions) },
    { key: "featureCapabilities", category: "workspace", workspaceKeys: ["settings"], start: (requestOptions) => getFeatureCapabilities(requestOptions) }
  ];
}

function selectPrimaryRefreshEntries(entries: RefreshEntry[], activeWorkspace: WorkspaceKey): RefreshEntry[] {
  const primaryKeys = new Set<RefreshDataKey>(["session", "overview", "workflowSummary", "workflowLibrary", "workflowPresets"]);
  for (const key of getActiveWorkspaceRefreshKeys(activeWorkspace)) {
    primaryKeys.add(key);
  }

  return entries.filter((entry) => primaryKeys.has(entry.key));
}

function getActiveWorkspaceRefreshKeys(activeWorkspace: WorkspaceKey): RefreshDataKey[] {
  switch (activeWorkspace) {
    case "portfolio":
      return ["portfolio", "portfolioMultiAssetCoverage", "brokerageConnection", "brokeragePortfolio"];
    case "accounting":
      return ["accounting", "portfolioMultiAssetCoverage"];
    case "reporting":
      return ["reporting"];
    case "strategy":
      return ["strategy"];
    case "data":
      return ["data", "providerConnections", "providerReadiness", "securityAssetProfiles"];
    case "settings":
      return [
        "providerConnections",
        "providerReadiness",
        "providerRoutingConnections",
        "providerRoutingBindings",
        "providerRoutingTrustSnapshots",
        "rolePermissionCatalog",
        "securityAssetProfiles",
        "ledgerMappingWorkbench",
        "operationsApprovalPolicyMatrix",
        "operationsCloseCalendar",
        "featureCapabilities"
      ];
    case "trading":
    default:
      return ["trading"];
  }
}

async function settleRefreshEntries(
  entries: RefreshEntry[],
  requestOptions: { signal: AbortSignal }
): Promise<SettledRefreshEntry[]> {
  const results = await Promise.allSettled(entries.map((entry) => entry.start(requestOptions)));
  return entries.map((entry, index) => ({
    entry,
    result: results[index]
  }));
}

function mergeRefreshSettlements(
  current: WorkstationDataState,
  settlements: SettledRefreshEntry[],
  options: { loading: boolean; replaceErrorsFor?: WorkspaceKey[] }
): WorkstationDataState {
  const next: WorkstationDataState = {
    ...current,
    loading: options.loading,
    providerRoutingRefreshing: false,
    usingDevelopmentFixtures: hasDevelopmentFixtureUsage()
  };
  const workspaceErrors: WorkspaceErrorMap = { ...current.workspaceErrors };
  for (const workspaceKey of options.replaceErrorsFor ?? []) {
    delete workspaceErrors[workspaceKey];
  }
  const bootstrapErrors: string[] = [];
  const hasBootstrapSettlements = settlements.some((settlement) => settlement.entry.category === "bootstrap");
  const hasWorkflowSettlements = settlements.some((settlement) => settlement.entry.category === "workflow");
  const workflowErrors: string[] = hasWorkflowSettlements || !current.workflowError ? [] : [current.workflowError];
  const currentErrorIsWorkspaceError = Object.values(current.workspaceErrors).includes(current.error ?? "");

  for (const { entry, result } of settlements) {
    if (result.status === "fulfilled") {
      assignRefreshValue(next, entry.key, result.value);
      continue;
    }

    const message = formatRequestError(result.reason, `${getRefreshEntryLabel(entry)} request failed.`);
    if (entry.category === "workspace") {
      for (const workspaceKey of entry.workspaceKeys ?? []) {
        appendWorkspaceError(workspaceErrors, workspaceKey, message);
      }
    } else if (entry.category === "workflow") {
      if (!workflowErrors.includes(message)) {
        workflowErrors.push(message);
      }
    } else {
      bootstrapErrors.push(message);
    }
  }

  next.workspaceErrors = workspaceErrors;
  next.workflowError = workflowErrors[0] ?? null;
  const retainedGlobalError = !hasBootstrapSettlements && current.error && !currentErrorIsWorkspaceError
    ? current.error
    : null;
  next.error = firstWorkspaceError(workspaceErrors) ?? bootstrapErrors[0] ?? retainedGlobalError ?? null;
  return next;
}

function getWorkspaceKeysForEntries(entries: RefreshEntry[]): WorkspaceKey[] {
  return Array.from(new Set(entries.flatMap((entry) => entry.workspaceKeys ?? [])));
}

function markRefreshEntriesLoaded(
  settlements: SettledRefreshEntry[],
  lastLoadedAt: Partial<Record<RefreshDataKey, number>>,
  loadedAt = Date.now()
) {
  for (const { entry, result } of settlements) {
    if (result.status === "fulfilled") {
      lastLoadedAt[entry.key] = loadedAt;
    }
  }
}

function shouldRefreshEntry(
  entry: RefreshEntry,
  current: WorkstationDataState,
  lastLoadedAt: Partial<Record<RefreshDataKey, number>>,
  now = Date.now()
) {
  const currentValue = current[entry.key as keyof WorkstationDataState];
  const loadedAt = lastLoadedAt[entry.key];
  return currentValue == null || loadedAt === undefined || now - loadedAt > WORKSPACE_REFRESH_STALE_MS;
}

function assignRefreshValue(state: WorkstationDataState, key: RefreshDataKey, value: unknown) {
  switch (key) {
    case "session":
      state.session = value as SessionInfo;
      break;
    case "overview":
      state.overview = value as SystemOverviewResponse;
      break;
    case "strategy":
      state.strategy = value as StrategyWorkspaceResponse;
      break;
    case "trading":
      state.trading = value as TradingWorkspaceResponse;
      break;
    case "portfolio":
      state.portfolio = value as PortfolioWorkspaceResponse;
      break;
    case "portfolioMultiAssetCoverage":
      state.portfolioMultiAssetCoverage = value as MultiAssetCoverageSummary;
      break;
    case "data":
      state.data = value as DataWorkspaceResponse;
      break;
    case "accounting":
      state.accounting = value as AccountingWorkspaceResponse;
      break;
    case "reporting":
      state.reporting = value as ReportingWorkspaceResponse;
      break;
    case "brokerageConnection":
      state.brokerageConnection = value as BrokerageConnectionStatus;
      break;
    case "robinhoodConnection":
      state.robinhoodConnection = value as BrokerageConnectionStatus;
      break;
    case "providerConnections":
      state.providerConnections = value as ProviderConnectionRow[];
      break;
    case "providerReadiness":
      state.providerReadiness = value as ProviderReadinessSummary;
      break;
    case "providerRoutingConnections":
      state.providerRoutingConnections = value as ProviderRoutingConnection[];
      break;
    case "providerRoutingBindings":
      state.providerRoutingBindings = value as ProviderRoutingBinding[];
      break;
    case "providerRoutingTrustSnapshots":
      state.providerRoutingTrustSnapshots = value as ProviderRoutingTrustSnapshot[];
      break;
    case "rolePermissionCatalog":
      state.rolePermissionCatalog = value as RolePermissionCatalog;
      break;
    case "securityAssetProfiles":
      state.securityAssetProfiles = value as SecurityAssetProfileDefinition[];
      break;
    case "ledgerMappingWorkbench":
      state.ledgerMappingWorkbench = value as LedgerMappingWorkbench;
      break;
    case "operationsApprovalPolicyMatrix":
      state.operationsApprovalPolicyMatrix = value as OperationsApprovalPolicyMatrix;
      break;
    case "operationsCloseCalendar":
      state.operationsCloseCalendar = value as OperationsCloseCalendar;
      break;
    case "brokeragePortfolio":
      state.brokeragePortfolio = value as BrokerageHouseholdPortfolio;
      break;
    case "workflowLibrary":
      state.workflowLibrary = value as WorkflowLibrary;
      break;
    case "workflowPresets":
      state.workflowPresets = value as WorkflowPresetLibrary;
      break;
    case "workflowSummary":
      state.workflowSummary = value as OperatorWorkflowHomeSummary;
      break;
    case "featureCapabilities":
      state.featureCapabilities = value as FeatureCapabilitySettingsResponse;
      break;
  }
}

function getRefreshEntryLabel(entry: RefreshEntry): string {
  if (entry.category === "bootstrap") {
    return "Workstation bootstrap";
  }

  if (entry.category === "workflow") {
    return "Workflow library";
  }

  return "Workspace";
}

function usePollingInterval(callback: () => void | Promise<void>, intervalMs: number | null) {
  const callbackRef = useRef(callback);

  useEffect(() => {
    callbackRef.current = callback;
  }, [callback]);

  useEffect(() => {
    if (intervalMs === null) {
      return undefined;
    }

    const id = window.setInterval(() => {
      void callbackRef.current();
    }, intervalMs);
    return () => window.clearInterval(id);
  }, [intervalMs]);
}

function useDocumentVisible() {
  const [documentVisible, setDocumentVisible] = useState(readDocumentVisible);

  useEffect(() => {
    const handleVisibilityChange = () => setDocumentVisible(readDocumentVisible());
    document.addEventListener("visibilitychange", handleVisibilityChange);
    return () => document.removeEventListener("visibilitychange", handleVisibilityChange);
  }, []);

  return documentVisible;
}

function readDocumentVisible() {
  return typeof document === "undefined" || document.visibilityState !== "hidden";
}

function resolveTradingPollingInterval(activeWorkspace: WorkspaceKey, documentVisible: boolean) {
  return documentVisible && activeWorkspace === "trading" ? 30_000 : null;
}

function resolveProviderRoutingPollingInterval(activeWorkspace: WorkspaceKey, documentVisible: boolean) {
  return documentVisible && (activeWorkspace === "data" || activeWorkspace === "settings") ? 30_000 : null;
}

function resolvePortfolioPollingInterval(activeWorkspace: WorkspaceKey, documentVisible: boolean) {
  if (!documentVisible) {
    return null;
  }

  if (activeWorkspace === "portfolio") {
    return 5_000;
  }

  return activeWorkspace === "trading" || activeWorkspace === "accounting" || activeWorkspace === "reporting"
    ? 30_000
    : null;
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
