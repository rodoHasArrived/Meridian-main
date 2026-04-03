import { useEffect, useRef, useState } from "react";
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

type BootstrapResourceKey = "session" | WorkspaceKey;

type BootstrapErrors = Partial<Record<BootstrapResourceKey, string>>;

interface WorkstationState {
  session: SessionInfo | null;
  overview: SystemOverviewResponse | null;
  research: ResearchWorkspaceResponse | null;
  trading: TradingWorkspaceResponse | null;
  dataOperations: DataOperationsWorkspaceResponse | null;
  governance: GovernanceWorkspaceResponse | null;
  loading: boolean;
  error: string | null;
  workspaceErrors: BootstrapErrors;
  refresh: () => void;
}

const REFRESH_INTERVAL_MS = 60_000;

export function useWorkstationData(): WorkstationState {
  const [state, setState] = useState<Omit<WorkstationState, "refresh">>({
    session: null,
    overview: null,
    research: null,
    trading: null,
    dataOperations: null,
    governance: null,
    loading: true,
    error: null,
    workspaceErrors: {}
  });

  const refreshCounterRef = useRef(0);
  const [refreshTick, setRefreshTick] = useState(0);

  useEffect(() => {
    let cancelled = false;

    async function load() {
      const bootstrapEntries = [
        ["session", getSession()],
        ["overview", getSystemStatus()],
        ["research", getResearchWorkspace()],
        ["trading", getTradingWorkspace()],
        ["data-operations", getDataOperationsWorkspace()],
        ["governance", getGovernanceWorkspace()]
      ] as const satisfies ReadonlyArray<readonly [BootstrapResourceKey, Promise<unknown>]>;

      const settled = await Promise.allSettled(bootstrapEntries.map(([, request]) => request));
      if (cancelled) {
        return;
      }

      const nextState: Omit<WorkstationState, "refresh"> = {
        session: null,
        overview: null,
        research: null,
        trading: null,
        dataOperations: null,
        governance: null,
        loading: false,
        error: null,
        workspaceErrors: {}
      };

      settled.forEach((result, index) => {
        const [key] = bootstrapEntries[index];
        if (result.status === "fulfilled") {
          switch (key) {
            case "session":
              nextState.session = result.value as SessionInfo;
              break;
            case "overview":
              nextState.overview = result.value as SystemOverviewResponse;
              break;
            case "research":
              nextState.research = result.value as ResearchWorkspaceResponse;
              break;
            case "trading":
              nextState.trading = result.value as TradingWorkspaceResponse;
              break;
            case "data-operations":
              nextState.dataOperations = result.value as DataOperationsWorkspaceResponse;
              break;
            case "governance":
              nextState.governance = result.value as GovernanceWorkspaceResponse;
              break;
          }
          return;
        }

        if (key !== "overview") {
          (nextState.workspaceErrors as BootstrapErrors)[key as BootstrapResourceKey] =
            result.reason instanceof Error
              ? result.reason.message
              : `Unable to load ${key} workspace data.`;
        }
      });

      const failedCount = Object.keys(nextState.workspaceErrors).length;
      const sessionFailed = nextState.workspaceErrors["session"] !== undefined;
      if (sessionFailed || failedCount >= bootstrapEntries.length - 1) {
        nextState.error = "Unable to load workstation data.";
      } else if (failedCount > 0) {
        nextState.error = `${failedCount} workstation bootstrap request${failedCount === 1 ? "" : "s"} failed.`;
      }

      setState(nextState);
    }

    void load();
    return () => {
      cancelled = true;
    };
  }, [refreshTick]);

  useEffect(() => {
    const timer = setInterval(() => {
      refreshCounterRef.current += 1;
      setRefreshTick(refreshCounterRef.current);
    }, REFRESH_INTERVAL_MS);

    return () => {
      clearInterval(timer);
    };
  }, []);

  const refresh = () => {
    setState((prev) => ({ ...prev, loading: true }));
    refreshCounterRef.current += 1;
    setRefreshTick(refreshCounterRef.current);
  };

  return { ...state, refresh };
}
