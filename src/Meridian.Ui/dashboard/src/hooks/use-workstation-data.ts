import { useEffect, useState } from "react";
import { getResearchWorkspace, getSession, getTradingWorkspace } from "@/lib/api";
import type { ResearchWorkspaceResponse, SessionInfo, TradingWorkspaceResponse } from "@/types";

interface WorkstationState {
  session: SessionInfo | null;
  research: ResearchWorkspaceResponse | null;
  trading: TradingWorkspaceResponse | null;
  loading: boolean;
  error: string | null;
}

export function useWorkstationData(): WorkstationState {
  const [state, setState] = useState<WorkstationState>({
    session: null,
    research: null,
    trading: null,
    loading: true,
    error: null
  });

  useEffect(() => {
    let cancelled = false;

    async function load() {
      try {
        const [session, research, trading] = await Promise.all([getSession(), getResearchWorkspace(), getTradingWorkspace()]);
        if (!cancelled) {
          setState({
            session,
            research,
            trading,
            loading: false,
            error: null
          });
        }
      } catch (error) {
        if (!cancelled) {
          setState({
            session: null,
            research: null,
            trading: null,
            loading: false,
            error: error instanceof Error ? error.message : "Unable to load workstation data."
          });
        }
      }
    }

    void load();
    return () => {
      cancelled = true;
    };
  }, []);

  return state;
}
