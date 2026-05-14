import { normalizeWorkspacePath, WORKSPACES, workflowTargetPath, workspacePath } from "@/lib/workspace";
import type {
  WorkflowAction,
  WorkflowDefinition,
  WorkflowLibrary,
  WorkflowPreset,
  WorkflowPresetLibrary,
  WorkspaceKey,
  WorkspaceSummary
} from "@/types";

export type CommandPaletteItemKind = "workspace" | "route" | "workflow" | "preset";
export type CommandPaletteFocusBoundary = "first" | "last" | "middle" | "outside" | "none";
export type CommandPaletteFocusTarget = "search" | "command" | "other";
export type CommandPaletteKeyCommand =
  | "close"
  | "activate-first-command"
  | "focus-first"
  | "focus-last"
  | "focus-next-command"
  | "focus-previous-command"
  | null;

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

export interface CommandPaletteGroup {
  kind: CommandPaletteItemKind;
  label: string;
  countLabel: string;
  ariaLabel: string;
  items: CommandPaletteItem[];
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

interface CommandPaletteRouteDefinition {
  id: string;
  label: string;
  description: string;
  route: string;
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
  commandGroups: CommandPaletteGroup[];
  filteredItemCountLabel: string;
  emptyState: CommandPaletteEmptyState | null;
}

const COMMAND_KIND_LABELS: Record<CommandPaletteItemKind, string> = {
  workspace: "Workspaces",
  route: "Quick routes",
  preset: "Presets",
  workflow: "Workflows"
};

const COMMAND_KIND_ORDER: CommandPaletteItemKind[] = ["workspace", "route", "preset", "workflow"];

const LOCAL_ROUTE_COMMANDS: CommandPaletteRouteDefinition[] = [
  {
    id: "trading-readiness",
    label: "Readiness console",
    description: "Review paper cockpit blockers, operator work items, and promotion evidence.",
    route: "/trading/readiness"
  },
  {
    id: "portfolio-brokerage-sync",
    label: "Brokerage sync",
    description: "Review household brokerage account sync posture and recovery actions.",
    route: "/portfolio/brokerage-sync"
  },
  {
    id: "accounting-reconciliation",
    label: "Reconciliation breaks",
    description: "Work position breaks, sign-off detail, and reconciliation recovery.",
    route: "/accounting/reconciliation"
  },
  {
    id: "accounting-security-master",
    label: "Security Master",
    description: "Review reference-data coverage, identifier conflicts, and trusted instruments.",
    route: "/accounting/security-master"
  },
  {
    id: "reporting-report-packs",
    label: "Report packs",
    description: "Open approval-ready report packet review and governed outputs.",
    route: "/reporting/report-packs"
  },
  {
    id: "reporting-evidence",
    label: "Evidence workbench",
    description: "Inspect packet completeness, stale evidence, and lineage.",
    route: "/reporting/evidence"
  },
  {
    id: "strategy-quant-lab",
    label: "Quant Lab",
    description: "Run scripts with parameter hints, templates, plots, and metrics.",
    route: "/strategy/quant-lab"
  },
  {
    id: "strategy-covered-call",
    label: "Covered call backtest",
    description: "Configure covered-call chain preview, run backtests, and review payoff evidence.",
    route: "/strategy/covered-call"
  },
  {
    id: "data-watchlist",
    label: "Watchlist",
    description: "Add symbols and starter packs before validating live quotes.",
    route: "/data/watchlist"
  },
  {
    id: "data-quotes",
    label: "Live quotes",
    description: "Inspect quotes, trades, depth, charts, and staged tickets.",
    route: "/data/quotes"
  },
  {
    id: "data-alerts",
    label: "Price alerts",
    description: "Create local quote-threshold alerts and review alert trigger state.",
    route: "/data/alerts"
  },
  {
    id: "data-backfills",
    label: "Backfill queues",
    description: "Preview, trigger, and review historical data backfill jobs.",
    route: "/data/backfills"
  },
  {
    id: "settings-integrations",
    label: "Alpaca provider setup",
    description: "Repair paper credentials, endpoint acknowledgements, and broker connection readiness.",
    route: "/settings#alpaca-provider-setup"
  }
];

export interface CommandPaletteKeyboardState {
  key: string;
  shiftKey?: boolean;
  focusBoundary: CommandPaletteFocusBoundary;
  focusTarget?: CommandPaletteFocusTarget;
}

export function buildCommandPaletteViewModel(
  pathname: string,
  workspaces: WorkspaceSummary[] = WORKSPACES,
  workflowData: CommandPaletteWorkflowData = {},
  query = ""
): CommandPaletteViewModel {
  const activeKey = normalizeWorkspacePath(pathname);
  const workspaceItems = buildWorkspaceItems(workspaces, activeKey);
  const routeItems = buildRouteItems(pathname);
  const presetItems = buildPresetItems(workflowData.workflowPresets?.presets ?? [], pathname);
  const workflowItems = buildWorkflowItems(workflowData.workflowLibrary?.workflows ?? [], pathname);
  const items = [...workspaceItems, ...routeItems, ...presetItems, ...workflowItems];
  const normalizedQuery = query.trim();
  const filteredItems = filterCommandItems(items, normalizedQuery);
  const commandGroups = buildCommandPaletteGroups(filteredItems);

  const activeWorkspace = workspaceItems.find((item) => item.active);
  const activeRoute = routeItems.find((item) => item.active);
  const activeWorkspaceLabel = activeWorkspace ? `Current: ${activeWorkspace.label}` : "No active workspace";
  const hasWorkflowBackend = Boolean(
    workflowData.workflowLibrary || workflowData.workflowPresets || workflowData.workflowError
  );

  return {
    title: hasWorkflowBackend ? "Open workflow command" : "Open workstation command",
    subtitle: hasWorkflowBackend
      ? "Route through shared workflows, presets, quick routes, and canonical workspaces."
      : "Route to common operator workflows and canonical workspaces.",
    routeSummary: buildRouteSummary(activeWorkspaceLabel, Boolean(activeWorkspace), hasWorkflowBackend, workflowData.workflowError),
    shortcutHint: "Esc to close",
    scopeLabel: hasWorkflowBackend ? "Shared workflow, route, and workspace routing" : "Canonical workspace and route routing",
    backendStatusLabel: buildBackendStatusLabel(hasWorkflowBackend, workflowItems.length, presetItems.length, workflowData.workflowError),
    commandListLabel: hasWorkflowBackend
      ? `${items.length} command${items.length === 1 ? "" : "s"}`
      : `${items.length} workstation command${items.length === 1 ? "" : "s"}`,
    itemCountLabel: hasWorkflowBackend
      ? buildItemCountLabel(workspaceItems.length, routeItems.length, presetItems.length, workflowItems.length)
      : buildLocalItemCountLabel(workspaceItems.length, routeItems.length),
    activeWorkspaceLabel,
    initialFocusItemId:
      filteredItems.find((item) => item.id === activeRoute?.id)?.id
      ?? filteredItems.find((item) => item.id === activeWorkspace?.id)?.id
      ?? filteredItems[0]?.id
      ?? null,
    items,
    query,
    searchInputLabel: "Search command palette",
    searchPlaceholder: hasWorkflowBackend
      ? "Search workflows, routes, presets, or workspaces"
      : "Search routes or workspaces",
    filteredItems,
    commandGroups,
    filteredItemCountLabel: buildFilteredItemCountLabel(filteredItems.length, items.length, normalizedQuery),
    emptyState:
      items.length === 0
        ? {
            title: hasWorkflowBackend ? "No workflow commands available" : "No workstation commands available",
            detail: hasWorkflowBackend
              ? "Workflow and workspace metadata did not load; retry the shell bootstrap before navigating."
              : "Workspace metadata did not load; retry the shell bootstrap before navigating."
          }
        : normalizedQuery && filteredItems.length === 0
          ? {
              title: "No matching commands",
              detail: "Try a workspace name, route, workflow title, or status label."
            }
        : null
  };
}

export function buildCommandPaletteGroups(items: CommandPaletteItem[]): CommandPaletteGroup[] {
  return COMMAND_KIND_ORDER
    .map((kind) => {
      const groupItems = items.filter((item) => item.kind === kind);
      const label = COMMAND_KIND_LABELS[kind];
      const countLabel = `${groupItems.length} ${label.toLowerCase().replace(/s$/, "")}${groupItems.length === 1 ? "" : "s"}`;

      return {
        kind,
        label,
        countLabel,
        ariaLabel: `${label}: ${countLabel}`,
        items: groupItems
      };
    })
    .filter((group) => group.items.length > 0);
}

export function resolveCommandPaletteKeyCommand({
  key,
  shiftKey = false,
  focusBoundary,
  focusTarget = "other"
}: CommandPaletteKeyboardState): CommandPaletteKeyCommand {
  if (key === "Escape") {
    return "close";
  }

  if (key === "Enter" && focusTarget === "search") {
    return "activate-first-command";
  }

  if (key === "ArrowDown") {
    return "focus-next-command";
  }

  if (key === "ArrowUp") {
    return "focus-previous-command";
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

function buildRouteItems(pathname: string): CommandPaletteItem[] {
  return LOCAL_ROUTE_COMMANDS.map<CommandPaletteItem>((routeCommand) => {
    const active = isActiveRoute(pathname, routeCommand.route);
    return {
      id: `route:${routeCommand.id}`,
      kind: "route",
      label: routeCommand.label,
      description: routeCommand.description,
      route: routeCommand.route,
      routeLabel: routeCommand.route,
      statusLabel: active ? "Current" : "Route",
      commandLabel: active ? `Stay on ${routeCommand.label}` : `Open ${routeCommand.label}`,
      ariaLabel: active ? `${routeCommand.label}, current route` : `Open ${routeCommand.label} route`,
      presetId: null,
      active
    };
  });
}

function buildPresetItems(presets: WorkflowPreset[], pathname: string): CommandPaletteItem[] {
  return [...presets]
    .sort(comparePresets)
    .map<CommandPaletteItem>((preset) => {
      const route = workflowTargetPath(preset.targetPageTag, preset.workspaceId);
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
  const route = workflowTargetPath(action.targetPageTag || workflow.entryPageTag, workflow.workspaceId);
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
    return `Route to common operator workflows and canonical workspaces. ${current}.`;
  }

  if (workflowError) {
    return `Route through shared backend workflow commands. ${current}. Workflow library unavailable; local route commands remain available.`;
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

function buildLocalItemCountLabel(workspaceCount: number, routeCount: number) {
  return `${workspaceCount} workspace${workspaceCount === 1 ? "" : "s"} - ${routeCount} quick route${routeCount === 1 ? "" : "s"}`;
}

function buildItemCountLabel(workspaceCount: number, routeCount: number, presetCount: number, workflowActionCount: number) {
  return `${workspaceCount} workspace${workspaceCount === 1 ? "" : "s"} - ${routeCount} quick route${routeCount === 1 ? "" : "s"} - ${presetCount} preset${presetCount === 1 ? "" : "s"} - ${workflowActionCount} workflow action${workflowActionCount === 1 ? "" : "s"}`;
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

function isExactActivePath(pathname: string, route: string) {
  const current = splitActiveRoute(pathname);
  const candidate = splitActiveRoute(route);
  return current.pathname === candidate.pathname
    && routeSearchMatches(current, candidate)
    && routeHashMatches(current, candidate);
}

function isActiveRoute(pathname: string, route: string) {
  const current = splitActiveRoute(pathname);
  const candidate = splitActiveRoute(route);
  const pathMatches = candidate.hash
    ? current.pathname === candidate.pathname
    : current.pathname === candidate.pathname || current.pathname.startsWith(`${candidate.pathname}/`);

  return pathMatches
    && routeSearchMatches(current, candidate)
    && routeHashMatches(current, candidate);
}

function splitActiveRoute(route: string) {
  const hashIndex = route.indexOf("#");
  const routeWithoutHash = hashIndex >= 0 ? route.slice(0, hashIndex) : route;
  const hash = hashIndex >= 0 ? route.slice(hashIndex) : "";
  const searchIndex = routeWithoutHash.indexOf("?");
  const pathname = searchIndex >= 0 ? routeWithoutHash.slice(0, searchIndex) : routeWithoutHash;
  const search = searchIndex >= 0 ? routeWithoutHash.slice(searchIndex) : "";

  return {
    pathname: pathname.replace(/\/+$/, "") || "/",
    search,
    hash
  };
}

function routeSearchMatches(
  current: ReturnType<typeof splitActiveRoute>,
  candidate: ReturnType<typeof splitActiveRoute>
) {
  return !candidate.search || current.search === candidate.search;
}

function routeHashMatches(
  current: ReturnType<typeof splitActiveRoute>,
  candidate: ReturnType<typeof splitActiveRoute>
) {
  return !candidate.hash || current.hash === candidate.hash;
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
