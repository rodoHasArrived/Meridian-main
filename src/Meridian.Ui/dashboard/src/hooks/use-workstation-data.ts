import { useEffect, useState } from "react";
import {
  getDataOperationsWorkspace,
  getGovernanceWorkspace,
  getResearchWorkspace,
  getSession,
  getTradingWorkspace
} from "@/lib/api";
import type {
  DataOperationsWorkspaceResponse,
  GovernanceWorkspaceResponse,
  ResearchWorkspaceResponse,
  SessionInfo,
  TradingWorkspaceResponse,
  WorkspaceKey
} from "@/types";

type BootstrapResourceKey = "session" | WorkspaceKey;

type BootstrapErrors = Partial<Record<BootstrapResourceKey, string>>;

interface WorkstationState {
  session: SessionInfo | null;
  research: ResearchWorkspaceResponse | null;
  trading: TradingWorkspaceResponse | null;
  dataOperations: DataOperationsWorkspaceResponse | null;
  governance: GovernanceWorkspaceResponse | null;
  loading: boolean;
  error: string | null;
  workspaceErrors: BootstrapErrors;
}

export function useWorkstationData(): WorkstationState {
  const [state, setState] = useState<WorkstationState>({
    session: null,
    research: null,
    trading: null,
    dataOperations: null,
    governance: null,
    loading: true,
    error: null,
    workspaceErrors: {}
  });

  useEffect(() => {
    let cancelled = false;

    async function load() {
      const bootstrapEntries = [
        ["session", getSession()],
        ["research", getResearchWorkspace()],
        ["trading", getTradingWorkspace()],
        ["data-operations", getDataOperationsWorkspace()],
        ["governance", getGovernanceWorkspace()]
      ] as const satisfies ReadonlyArray<readonly [BootstrapResourceKey, Promise<unknown>]>;

      const settled = await Promise.allSettled(bootstrapEntries.map(([, request]) => request));
      if (cancelled) {
        return;
      }

      const nextState: WorkstationState = {
        session: null,
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

        nextState.workspaceErrors[key] = result.reason instanceof Error
          ? result.reason.message
          : `Unable to load ${key} workspace data.`;
      });

      const failedCount = Object.keys(nextState.workspaceErrors).length;
      if (failedCount === bootstrapEntries.length) {
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
  }, []);

  return state;
}
