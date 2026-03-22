import { useEffect, useState } from "react";
import { getResearchWorkspace, getSession } from "@/lib/api";
import type { ResearchWorkspaceResponse, SessionInfo } from "@/types";

interface WorkstationState {
  session: SessionInfo | null;
  research: ResearchWorkspaceResponse | null;
  loading: boolean;
  error: string | null;
}

export function useWorkstationData(): WorkstationState {
  const [state, setState] = useState<WorkstationState>({
    session: null,
    research: null,
    loading: true,
    error: null
  });

  useEffect(() => {
    let cancelled = false;

    async function load() {
      try {
        const [session, research] = await Promise.all([getSession(), getResearchWorkspace()]);
        if (!cancelled) {
          setState({
            session,
            research,
            loading: false,
            error: null
          });
        }
      } catch (error) {
        if (!cancelled) {
          setState({
            session: null,
            research: null,
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
