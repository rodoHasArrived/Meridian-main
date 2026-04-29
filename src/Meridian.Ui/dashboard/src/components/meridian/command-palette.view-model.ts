import { normalizeWorkspacePath, WORKSPACES, workspacePath } from "@/lib/workspace";
import type { WorkspaceSummary } from "@/types";

export interface CommandPaletteItem {
  id: string;
  label: string;
  description: string;
  route: string;
  statusLabel: string;
  commandLabel: string;
  ariaLabel: string;
  active: boolean;
}

export interface CommandPaletteEmptyState {
  title: string;
  detail: string;
}

export interface CommandPaletteViewModel {
  title: string;
  subtitle: string;
  routeSummary: string;
  commandListLabel: string;
  itemCountLabel: string;
  activeWorkspaceLabel: string;
  initialFocusItemId: string | null;
  items: CommandPaletteItem[];
  emptyState: CommandPaletteEmptyState | null;
}

export function buildCommandPaletteViewModel(
  pathname: string,
  workspaces: WorkspaceSummary[] = WORKSPACES
): CommandPaletteViewModel {
  const activeKey = normalizeWorkspacePath(pathname);
  const items = workspaces.map<CommandPaletteItem>((workspace) => {
    const active = workspace.key === activeKey;
    const route = workspacePath(workspace.key);

    return {
      id: workspace.key,
      label: workspace.label,
      description: workspace.description,
      route,
      statusLabel: active ? "Current" : workspace.status,
      commandLabel: active ? `Stay in ${workspace.label}` : `Open ${workspace.label}`,
      ariaLabel: active ? `${workspace.label}, current workspace` : `Open ${workspace.label} workspace`,
      active
    };
  });

  const activeWorkspace = items.find((item) => item.active);
  const activeWorkspaceLabel = activeWorkspace ? `Current: ${activeWorkspace.label}` : "No active workspace";

  return {
    title: "Open workspace",
    subtitle: "Route to a canonical operator workspace.",
    routeSummary: activeWorkspace ? `Route to a canonical operator workspace. ${activeWorkspaceLabel}.` : "Route to a canonical operator workspace. No active workspace.",
    commandListLabel: `${items.length} workspace command${items.length === 1 ? "" : "s"}`,
    itemCountLabel: `${items.length} workspace${items.length === 1 ? "" : "s"}`,
    activeWorkspaceLabel,
    initialFocusItemId: activeWorkspace?.id ?? items[0]?.id ?? null,
    items,
    emptyState:
      items.length === 0
        ? {
            title: "No workspace commands available",
            detail: "Workspace metadata did not load; retry the shell bootstrap before navigating."
          }
        : null
  };
}
