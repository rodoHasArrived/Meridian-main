import { normalizeWorkspacePath, WORKSPACES, workspacePath } from "@/lib/workspace";
import type {
  WorkflowAction,
  WorkflowDefinition,
  WorkflowLibrary,
  WorkflowPreset,
  WorkflowPresetLibrary,
  WorkspaceKey,
  WorkspaceSummary
} from "@/types";

export type CommandPaletteItemKind = "workspace" | "workflow" | "preset";
export type CommandPaletteFocusBoundary = "first" | "last" | "middle" | "outside" | "none";
export type CommandPaletteKeyCommand = "close" | "focus-first" | "focus-last" | null;

export interface CommandPaletteItem {
  id: string;
  kind: CommandPaletteItemKind;
  label: string;
  description: string;
  route: string;
  routeLabel: string;
  statusLabel: string;
  commandLabel: string;
  ariaLabel: string;
  presetId: string | null;
  active: boolean;
}

export interface CommandPaletteEmptyState {
  title: string;
  detail: string;
}

export interface CommandPaletteWorkflowData {
  workflowLibrary?: WorkflowLibrary | null;
  workflowPresets?: WorkflowPresetLibrary | null;
  workflowError?: string | null;
}

export interface CommandPaletteViewModel {
  title: string;
  subtitle: string;
  routeSummary: string;
  shortcutHint: string;
  scopeLabel: string;
  backendStatusLabel: string | null;
  commandListLabel: string;
  itemCountLabel: string;
  activeWorkspaceLabel: string;
  initialFocusItemId: string | null;
  items: CommandPaletteItem[];
  query: string;
  searchInputLabel: string;
  searchPlaceholder: string;
  filteredItems: CommandPaletteItem[];
  filteredItemCountLabel: string;
  emptyState: CommandPaletteEmptyState | null;
}

export interface CommandPaletteKeyboardState {
  key: string;
  shiftKey?: boolean;
  focusBoundary: CommandPaletteFocusBoundary;
}

const PAGE_TAG_ROUTES: Record<string, string> = {
  AccountPortfolio: "/portfolio/brokerage-sync",
  AccountingShell: "/accounting",
  Backfill: "/data/backfills",
  DataShell: "/data",
  FundAuditTrail: "/accounting",
  FundReconciliation: "/accounting/reconciliation",
  FundReportPack: "/reporting/report-packs",
  FundTrialBalance: "/accounting",
  PortfolioShell: "/portfolio",
  ProviderHealth: "/data/providers",
  ReportingShell: "/reporting",
  RunRisk: "/trading/readiness",
  SecurityMaster: "/data/security-master",
  SettingsShell: "/settings",
  StrategyRuns: "/strategy",
  StrategyShell: "/strategy",
  TradingReadinessConsole: "/trading/readiness",
  TradingShell: "/trading"
};

export function buildCommandPaletteViewModel(
  pathname: string,
  workspaces: WorkspaceSummary[] = WORKSPACES,
  workflowData: CommandPaletteWorkflowData = {},
  query = ""
): CommandPaletteViewModel {
  const activeKey = normalizeWorkspacePath(pathname);
  const workspaceItems = buildWorkspaceItems(workspaces, activeKey);
  const presetItems = buildPresetItems(workflowData.workflowPresets?.presets ?? [], pathname);
  const workflowItems = buildWorkflowItems(workflowData.workflowLibrary?.workflows ?? [], pathname);
  const items = [...workspaceItems, ...presetItems, ...workflowItems];
  const normalizedQuery = query.trim();
  const filteredItems = filterCommandItems(items, normalizedQuery);

  const activeWorkspace = workspaceItems.find((item) => item.active);
  const activeWorkspaceLabel = activeWorkspace ? `Current: ${activeWorkspace.label}` : "No active workspace";
  const hasWorkflowBackend = Boolean(
    workflowData.workflowLibrary || workflowData.workflowPresets || workflowData.workflowError
  );

  return {
    title: hasWorkflowBackend ? "Open workflow command" : "Open workspace",
    subtitle: hasWorkflowBackend
      ? "Route through shared workflows, presets, and canonical workspaces."
      : "Route to a canonical operator workspace.",
    routeSummary: buildRouteSummary(activeWorkspaceLabel, Boolean(activeWorkspace), hasWorkflowBackend, workflowData.workflowError),
    shortcutHint: "Esc to close",
    scopeLabel: hasWorkflowBackend ? "Shared workflow and workspace routing" : "Canonical workspace routing",
    backendStatusLabel: buildBackendStatusLabel(hasWorkflowBackend, workflowItems.length, presetItems.length, workflowData.workflowError),
    commandListLabel: hasWorkflowBackend
      ? `${items.length} command${items.length === 1 ? "" : "s"}`
      : `${items.length} workspace command${items.length === 1 ? "" : "s"}`,
    itemCountLabel: hasWorkflowBackend
      ? buildItemCountLabel(workspaceItems.length, presetItems.length, workflowItems.length)
      : `${items.length} workspace${items.length === 1 ? "" : "s"}`,
    activeWorkspaceLabel,
    initialFocusItemId:
      filteredItems.find((item) => item.id === activeWorkspace?.id)?.id ?? filteredItems[0]?.id ?? null,
    items,
    query,
    searchInputLabel: "Search command palette",
    searchPlaceholder: hasWorkflowBackend
      ? "Search workflows, presets, or workspaces"
      : "Search workspaces",
    filteredItems,
    filteredItemCountLabel: buildFilteredItemCountLabel(filteredItems.length, items.length, normalizedQuery),
    emptyState:
      items.length === 0
        ? {
            title: hasWorkflowBackend ? "No workflow commands available" : "No workspace commands available",
            detail: hasWorkflowBackend
              ? "Workflow and workspace metadata did not load; retry the shell bootstrap before navigating."
              : "Workspace metadata did not load; retry the shell bootstrap before navigating."
          }
        : normalizedQuery && filteredItems.length === 0
          ? {
              title: "No matching commands",
              detail: "Try a workspace name, workflow title, route, or status label."
            }
        : null
  };
}

export function resolveCommandPaletteKeyCommand({
  key,
  shiftKey = false,
  focusBoundary
}: CommandPaletteKeyboardState): CommandPaletteKeyCommand {
  if (key === "Escape") {
    return "close";
  }

  if (key !== "Tab") {
    return null;
  }

  if (focusBoundary === "none") {
    return null;
  }

  if (shiftKey && (focusBoundary === "first" || focusBoundary === "outside")) {
    return "focus-last";
  }

  if (!shiftKey && (focusBoundary === "last" || focusBoundary === "outside")) {
    return "focus-first";
  }

  return null;
}

function buildWorkspaceItems(workspaces: WorkspaceSummary[], activeKey: WorkspaceKey): CommandPaletteItem[] {
  return workspaces.map<CommandPaletteItem>((workspace) => {
    const active = workspace.key === activeKey;
    const route = workspacePath(workspace.key);

    return {
      id: workspace.key,
      kind: "workspace",
      label: workspace.label,
      description: workspace.description,
      route,
      routeLabel: route,
      statusLabel: active ? "Current" : workspace.status,
      commandLabel: active ? `Stay in ${workspace.label}` : `Open ${workspace.label}`,
      ariaLabel: active ? `${workspace.label}, current workspace` : `Open ${workspace.label} workspace`,
      presetId: null,
      active
    };
  });
}

function buildPresetItems(presets: WorkflowPreset[], pathname: string): CommandPaletteItem[] {
  return [...presets]
    .sort(comparePresets)
    .map<CommandPaletteItem>((preset) => {
      const route = routeForWorkflowTarget(preset.targetPageTag, preset.workspaceId);
      const current = isExactActivePath(pathname, route);

      return {
        id: `preset:${preset.presetId}`,
        kind: "preset",
        label: preset.name,
        description: preset.description?.trim() || `${preset.workflowTitle}: ${preset.actionLabel}`,
        route,
        routeLabel: route,
        statusLabel: current ? "Current" : preset.isPinned ? "Pinned" : "Preset",
        commandLabel: `Open preset ${preset.name}`,
        ariaLabel: `Open workflow preset ${preset.name}`,
        presetId: preset.presetId,
        active: false
      };
    });
}

function buildWorkflowItems(workflows: WorkflowDefinition[], pathname: string): CommandPaletteItem[] {
  return workflows.flatMap((workflow) =>
    workflow.actions.map<CommandPaletteItem>((action) => buildWorkflowItem(workflow, action, pathname))
  );
}

function buildWorkflowItem(workflow: WorkflowDefinition, action: WorkflowAction, pathname: string): CommandPaletteItem {
  const route = routeForWorkflowTarget(action.targetPageTag || workflow.entryPageTag, workflow.workspaceId);
  const current = isExactActivePath(pathname, route);

  return {
    id: `workflow:${workflow.workflowId}:${action.actionId}`,
    kind: "workflow",
    label: action.label,
    description: `${workflow.title}: ${action.detail || workflow.summary}`,
    route,
    routeLabel: route,
    statusLabel: current ? "Current" : "Workflow",
    commandLabel: action.label,
    ariaLabel: `${action.label}, ${workflow.title}`,
    presetId: null,
    active: false
  };
}

function buildRouteSummary(
  activeWorkspaceLabel: string,
  hasActiveWorkspace: boolean,
  hasWorkflowBackend: boolean,
  workflowError?: string | null
) {
  const current = hasActiveWorkspace ? activeWorkspaceLabel : "No active workspace";
  if (!hasWorkflowBackend) {
    return `Route to a canonical operator workspace. ${current}.`;
  }

  if (workflowError) {
    return `Route through shared backend workflow commands. ${current}. Workflow library unavailable; workspace commands remain available.`;
  }

  return `Route through shared backend workflow commands. ${current}.`;
}

function buildBackendStatusLabel(
  hasWorkflowBackend: boolean,
  workflowActionCount: number,
  presetCount: number,
  workflowError?: string | null
) {
  if (!hasWorkflowBackend) {
    return null;
  }

  if (workflowError) {
    return "Workflow library unavailable";
  }

  return `${workflowActionCount} workflow action${workflowActionCount === 1 ? "" : "s"} - ${presetCount} preset${presetCount === 1 ? "" : "s"}`;
}

function buildItemCountLabel(workspaceCount: number, presetCount: number, workflowActionCount: number) {
  return `${workspaceCount} workspace${workspaceCount === 1 ? "" : "s"} - ${presetCount} preset${presetCount === 1 ? "" : "s"} - ${workflowActionCount} workflow action${workflowActionCount === 1 ? "" : "s"}`;
}

function buildFilteredItemCountLabel(filteredCount: number, totalCount: number, query: string) {
  if (!query) {
    return `${totalCount} command${totalCount === 1 ? "" : "s"} available`;
  }

  return `${filteredCount} of ${totalCount} command${totalCount === 1 ? "" : "s"} match`;
}

function filterCommandItems(items: CommandPaletteItem[], query: string) {
  if (!query) {
    return items;
  }

  const terms = query.toLowerCase().split(/\s+/).filter(Boolean);
  if (terms.length === 0) {
    return items;
  }

  return items.filter((item) => {
    const haystack = [
      item.kind,
      item.label,
      item.commandLabel,
      item.description,
      item.route,
      item.routeLabel,
      item.statusLabel,
      item.ariaLabel
    ].join(" ").toLowerCase();

    return terms.every((term) => haystack.includes(term));
  });
}

function routeForWorkflowTarget(targetPageTag: string | null | undefined, workspaceId: string | null | undefined) {
  const tagRoute = targetPageTag ? PAGE_TAG_ROUTES[targetPageTag] : undefined;
  if (tagRoute) {
    return tagRoute;
  }

  const workspaceKey = workspaceKeyFromId(workspaceId);
  return workspaceKey ? workspacePath(workspaceKey) : "/trading";
}

function workspaceKeyFromId(workspaceId: string | null | undefined): WorkspaceKey | null {
  const normalized = workspaceId?.trim().toLowerCase();
  if (!normalized) {
    return null;
  }

  return WORKSPACES.some((workspace) => workspace.key === normalized) ? (normalized as WorkspaceKey) : null;
}

function isExactActivePath(pathname: string, route: string) {
  const cleanPath = pathname.split(/[?#]/)[0]?.replace(/\/+$/, "") || "/";
  const cleanRoute = route.replace(/\/+$/, "") || "/";
  return cleanPath === cleanRoute;
}

function comparePresets(left: WorkflowPreset, right: WorkflowPreset) {
  if (left.isPinned !== right.isPinned) {
    return left.isPinned ? -1 : 1;
  }

  const leftUsed = left.lastUsedAt ?? left.updatedAt;
  const rightUsed = right.lastUsedAt ?? right.updatedAt;
  const usageComparison = rightUsed.localeCompare(leftUsed);
  return usageComparison !== 0 ? usageComparison : left.name.localeCompare(right.name);
}
