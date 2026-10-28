import { useEffect, useState } from "react";
import type { WorkspaceKey } from "@/types";

/** Keep operator choices until navigation reveals a new active destination. */
export function useWorkspaceExpansion(activeWorkspaceKey: WorkspaceKey | undefined, activeRoute?: string) {
  const [expandedWorkspaces, setExpandedWorkspaces] = useState<Set<WorkspaceKey>>(() =>
    activeWorkspaceKey ? new Set([activeWorkspaceKey]) : new Set()
  );

  useEffect(() => {
    if (!activeWorkspaceKey) {
      return;
    }

    setExpandedWorkspaces((current) => {
      if (current.has(activeWorkspaceKey)) {
        return current;
      }

      const next = new Set(current);
      next.add(activeWorkspaceKey);
      return next;
    });
  }, [activeWorkspaceKey, activeRoute]);

  const toggleWorkspace = (key: WorkspaceKey) => {
    setExpandedWorkspaces((current) => {
      const next = new Set(current);
      if (next.has(key)) {
        next.delete(key);
      } else {
        next.add(key);
      }

      return next;
    });
  };

  return { expandedWorkspaces, toggleWorkspace };
}
