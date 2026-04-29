import { useCallback, useEffect, useState } from "react";
import {
  getDataOperationsWorkspace,
  getGovernanceWorkspace,
  getResearchWorkspace,
  getSession,
  getSystemStatus,
  getTradingWorkspace
} from "@/lib/api";
import type {
  DataOperationsWorkspaceResponse,
  GovernanceWorkspaceResponse,
  ResearchWorkspaceResponse,
  SessionInfo,
  SystemOverviewResponse,
  TradingWorkspaceResponse,
  WorkspaceKey
} from "@/types";

type WorkspaceErrorMap = Partial<Record<WorkspaceKey, string>>;

interface WorkstationDataState {
  session: SessionInfo | null;
  overview: SystemOverviewResponse | null;
  research: ResearchWorkspaceResponse | null;
  trading: TradingWorkspaceResponse | null;
  dataOperations: DataOperationsWorkspaceResponse | null;
  governance: GovernanceWorkspaceResponse | null;
  loading: boolean;
  error: string | null;
  workspaceErrors: WorkspaceErrorMap;
}

const initialState: WorkstationDataState = {
  session: null,
  overview: null,
  research: null,
  trading: null,
  dataOperations: null,
  governance: null,
  loading: true,
  error: null,
  workspaceErrors: {}
};

export function useWorkstationData() {
  const [state, setState] = useState<WorkstationDataState>(initialState);

  const refresh = useCallback(async () => {
    setState((current) => ({ ...current, loading: true, error: null, workspaceErrors: {} }));

    const [
      session,
      overview,
      research,
      trading,
      dataOperations,
      governance
    ] = await Promise.allSettled([
      getSession(),
      getSystemStatus(),
      getResearchWorkspace(),
      getTradingWorkspace(),
      getDataOperationsWorkspace(),
      getGovernanceWorkspace()
    ]);

    const workspaceErrors: WorkspaceErrorMap = {};
    const bootstrapErrors: string[] = [];
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

    const nextState: WorkstationDataState = {
      session: readBootstrap(session),
      overview: readBootstrap(overview),
      research: readWorkspace(["strategy"], research),
      trading: readWorkspace(["trading"], trading),
      dataOperations: readWorkspace(["data"], dataOperations),
      governance: readWorkspace(["accounting", "reporting"], governance),
      loading: false,
      error: Object.values(workspaceErrors)[0] ?? bootstrapErrors[0] ?? null,
      workspaceErrors
    };

    setState(nextState);
  }, []);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  return { ...state, refresh };
}
