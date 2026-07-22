import { useEffect, useState } from "react";
import type { WorkspaceKey } from "@/types";

export function useWorkspaceExpansion(activeWorkspaceKey: WorkspaceKey | undefined) {
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
  }, [activeWorkspaceKey]);

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
