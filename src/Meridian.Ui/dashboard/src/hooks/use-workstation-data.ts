import { useCallback, useEffect, useRef, useState } from "react";
import {
  getBrokerageHouseholdPortfolio,
  getDataWorkspace,
  getGovernanceWorkspace,
  getAlpacaConnectionStatus,
  getPortfolioWorkspace,
  getReportingWorkspace,
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
  loading: true,
  error: null,
  workspaceErrors: {}
};

export function useWorkstationData() {
  const [state, setState] = useState<WorkstationDataState>(initialState);

  const refresh = useCallback(async () => {
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
      getSession(),
      getSystemStatus(),
      getStrategyWorkspace(),
      getTradingWorkspace(),
      getPortfolioWorkspace(),
      getDataWorkspace(),
      getGovernanceWorkspace(),
      getReportingWorkspace(),
      getAlpacaConnectionStatus(),
      getBrokerageHouseholdPortfolio("alpaca"),
      getWorkflowLibrary(),
      getWorkflowPresets()
    ]);

    const workspaceErrors: WorkspaceErrorMap = {};
    const bootstrapErrors: string[] = [];
    const workflowErrors: string[] = [];
    const readWorkspace = <T,>(keys: WorkspaceKey[], result: PromiseSettledResult<T>): T | null => {
      if (result.status === "fulfilled") {
        return result.value;
      }

      const message = result.reason instanceof Error ? result.reason.message : "Workspace request failed.";
      for (const key of keys) {
        workspaceErrors[key] = message;
      }
      return null;
    };

    const readBootstrap = <T,>(result: PromiseSettledResult<T>): T | null => {
      if (result.status === "fulfilled") {
        return result.value;
      }

      bootstrapErrors.push(result.reason instanceof Error ? result.reason.message : "Workstation bootstrap request failed.");
      return null;
    };

    const readWorkflow = <T,>(result: PromiseSettledResult<T>): T | null => {
      if (result.status === "fulfilled") {
        return result.value;
      }

      workflowErrors.push(result.reason instanceof Error ? result.reason.message : "Workflow library request failed.");
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
      loading: false,
      error: Object.values(workspaceErrors)[0] ?? bootstrapErrors[0] ?? null,
      workspaceErrors
    };

    setState(nextState);
  }, []);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  // Keep the trading cockpit fresh without re-fetching every workspace.
  // Positions, orders, fills, and readiness status change as trading runs.
  const refreshingTrading = useRef(false);
  const refreshTrading = useCallback(async () => {
    if (refreshingTrading.current) return;
    refreshingTrading.current = true;
    try {
      const result = await getTradingWorkspace();
      setState((current) => ({ ...current, trading: result }));
    } catch {
      // keep stale data; full refresh() is always available
    } finally {
      refreshingTrading.current = false;
    }
  }, []);

  useEffect(() => {
    const id = setInterval(() => { void refreshTrading(); }, 30_000);
    return () => clearInterval(id);
  }, [refreshTrading]);

  return { ...state, refresh, refreshTrading };
}
