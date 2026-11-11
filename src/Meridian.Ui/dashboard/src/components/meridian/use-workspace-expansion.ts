import { useMemo, useState } from "react";
import type { WorkspaceKey } from "@/types";

/** One automatic group follows navigation; deliberate opens remain available. */
export function useWorkspaceExpansion(activeWorkspaceKey: WorkspaceKey | undefined, activeRoute?: string) {
  const [manualOpen, setManualOpen] = useState<Set<WorkspaceKey>>(() => new Set());
  const [collapsedDestination, setCollapsedDestination] = useState<string | null>(null);
  const destination = `${activeWorkspaceKey ?? ""}:${activeRoute ?? ""}`;
  const expandedWorkspaces = useMemo(() => {
    const next = new Set(manualOpen);
    if (activeWorkspaceKey && collapsedDestination !== destination) next.add(activeWorkspaceKey);
    return next;
  }, [manualOpen, activeWorkspaceKey, collapsedDestination, destination]);

  const toggleWorkspace = (key: WorkspaceKey) => {
    const isOpen = expandedWorkspaces.has(key);
    if (key === activeWorkspaceKey) setCollapsedDestination(isOpen ? destination : null);
    setManualOpen((current) => {
      const next = new Set(current);
      if (isOpen) {
        next.delete(key);
      } else {
        next.add(key);
      }

      return next;
    });
  };

  return { expandedWorkspaces, toggleWorkspace };
}
