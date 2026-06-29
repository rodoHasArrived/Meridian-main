import {
  canonicalizeWorkspaceSummaries,
  normalizeWorkspacePath,
  WORKSPACES,
  WORKSTATION_ROUTE_CATALOG,
  workflowTargetPath,
  workspacePath
} from "@/lib/workspace";
import {
  appendOperatingScopeToRoute,
  buildOperatingScopeFromSearch,
  summarizeOperatingScopeForRoute,
  type AppShellOperatingScopeInput,
  type AppShellOperatingScopeState
} from "@/lib/app-shell-operating-scope";
import type {
  WorkflowAction,
  WorkflowDefinition,
  WorkflowLibrary,
  WorkflowPreset,
  WorkflowPresetLibrary,
  WorkspaceKey,
  WorkspaceSummary
} from "@/types";

export type CommandPaletteItemKind = "focus" | "workspace" | "route" | "workflow" | "preset";
export type CommandPaletteItemStatusTone = "blocked" | "review" | "ready" | "current" | "neutral";
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
  statusTone: CommandPaletteItemStatusTone;
  statusVisible: boolean;
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
  id: string;
  titleId: string;
  detailId: string;
  actionId: string | null;
  title: string;
  detail: string;
  statusLabel: string;
  actionLabel: string | null;
  actionAriaLabel: string | null;
  canClearSearch: boolean;
}

export interface CommandPaletteWorkflowData {
  workflowLibrary?: WorkflowLibrary | null;
  workflowPresets?: WorkflowPresetLibrary | null;
  workflowError?: string | null;
  operatorFocusItems?: CommandPaletteFocusAction[] | null;
}

export interface CommandPaletteFocusAction {
  id: string;
  label: string;
  detail: string;
  route: string;
  workspaceLabel: string;
  actionLabel: string;
  tone: "ready" | "review" | "blocked" | "pending";
  ariaLabel: string;
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
  operatingContextLabel: string | null;
  backendStatusLabel: string | null;
  commandListLabel: string;
  itemCountLabel: string;
  activeWorkspaceLabel: string;
  initialFocusItemId: string | null;
  items: CommandPaletteItem[];
  query: string;
  searchInputLabel: string;
  searchPlaceholder: string;
  searchDescribedBy: string;
  filteredItems: CommandPaletteItem[];
  commandGroups: CommandPaletteGroup[];
  recommendedItems: CommandPaletteItem[];
  recommendedItemsLabel: string;
  recommendedItemsCountLabel: string;
  filteredItemCountLabel: string;
  emptyState: CommandPaletteEmptyState | null;
}

const COMMAND_KIND_LABELS: Record<CommandPaletteItemKind, string> = {
  focus: "Focus actions",
  workspace: "Workspaces",
  route: "Quick routes",
  preset: "Presets",
  workflow: "Workflows"
};

const COMMAND_KIND_ORDER: CommandPaletteItemKind[] = ["focus", "workspace", "route", "preset", "workflow"];
const COMMAND_PALETTE_FILTER_COUNT_ID = "command-palette-filter-count";
const COMMAND_PALETTE_EMPTY_STATE_ID = "command-palette-empty-state";
const COMMAND_PALETTE_EMPTY_STATE_TITLE_ID = "command-palette-empty-state-title";
const COMMAND_PALETTE_EMPTY_STATE_DETAIL_ID = "command-palette-empty-state-detail";
const COMMAND_PALETTE_CLEAR_SEARCH_ID = "command-palette-clear-search";

const LOCAL_ROUTE_COMMANDS: CommandPaletteRouteDefinition[] = [
  {
    id: "trading-readiness",
    label: "Readiness console",
    description: "Review paper cockpit blockers, operator work items, and promotion evidence.",
    route: WORKSTATION_ROUTE_CATALOG.tradingReadiness
  },
  {
    id: "portfolio-brokerage-sync",
    label: "Brokerage sync",
    description: "Review household brokerage account sync posture and recovery actions.",
    route: WORKSTATION_ROUTE_CATALOG.portfolioBrokerageSync
  },
  {
    id: "portfolio-family-office",
    label: "Family office",
    description: "Review family net worth, entity ownership, asset-class exposure, commitments, breaks, and stale valuations.",
    route: WORKSTATION_ROUTE_CATALOG.portfolioFamilyOffice
  },
  {
    id: "accounting-reconciliation",
    label: "Reconciliation breaks",
    description: "Work position breaks, sign-off detail, and reconciliation recovery.",
    route: WORKSTATION_ROUTE_CATALOG.accountingReconciliation
  },
  {
    id: "accounting-security-master",
    label: "Security Master",
    description: "Review reference-data coverage, identifier conflicts, and trusted instruments.",
    route: WORKSTATION_ROUTE_CATALOG.accountingSecurityMaster
  },
  {
    id: "reporting-operations-record",
    label: "Operations record",
    description: "Demo the W1-W5 path from source data through accounting record evidence to report-pack publication.",
    route: WORKSTATION_ROUTE_CATALOG.reportingOperationsRecord
  },
  {
    id: "reporting-report-builder",
    label: "Report Builder",
    description: "Author governed templates, report-writer grids, previews, and report-pack outputs.",
    route: WORKSTATION_ROUTE_CATALOG.reportingReportBuilder
  },
  {
    id: "reporting-run-status",
    label: "Run Status",
    description: "Review generated runs, retry posture, approval state, lineage, and executable next actions.",
    route: WORKSTATION_ROUTE_CATALOG.reportingRunStatus
  },
  {
    id: "reporting-delivery-evidence",
    label: "Delivery Evidence",
    description: "Inspect retained delivery proof, publication attempts, restatements, and schedule evidence.",
    route: WORKSTATION_ROUTE_CATALOG.reportingDeliveryEvidence
  },
  {
    id: "reporting-exports",
    label: "Exports",
    description: "Run on-demand reports and review generated export run posture.",
    route: WORKSTATION_ROUTE_CATALOG.reportingExports
  },
  {
    id: "reporting-governance",
    label: "Governance",
    description: "Review access scope, report-pack workflow records, approvals, and audit links before release.",
    route: WORKSTATION_ROUTE_CATALOG.reportingGovernance
  },
  {
    id: "strategy-quant-lab",
    label: "Quant Lab",
    description: "Run scripts with parameter hints, templates, plots, and metrics.",
    route: WORKSTATION_ROUTE_CATALOG.strategyQuantLab
  },
  {
    id: "strategy-formula-workbench",
    label: "Formula Workbench",
    description: "Author cell-based strategy formulas with field search and suggestions.",
    route: WORKSTATION_ROUTE_CATALOG.strategyFormulaWorkbench
  },
  {
    id: "strategy-covered-call",
    label: "Covered call backtest",
    description: "Configure covered-call chain preview, run backtests, and review payoff evidence.",
    route: WORKSTATION_ROUTE_CATALOG.strategyCoveredCall
  },
  {
    id: "data-providers",
    label: "Providers",
    description: "Review provider catalog, onboarding posture, connection health, and routing evidence.",
    route: WORKSTATION_ROUTE_CATALOG.dataProviders
  },
  {
    id: "data-watchlist",
    label: "Watchlist",
    description: "Add symbols and starter packs before validating live quotes.",
    route: WORKSTATION_ROUTE_CATALOG.dataWatchlist
  },
  {
    id: "data-quotes",
    label: "Live quotes",
    description: "Inspect quotes, trades, depth, charts, and staged tickets.",
    route: WORKSTATION_ROUTE_CATALOG.dataQuotes
  },
  {
    id: "data-alerts",
    label: "Price alerts",
    description: "Create local quote-threshold alerts and review alert trigger state.",
    route: WORKSTATION_ROUTE_CATALOG.dataAlerts
  },
  {
    id: "data-backfills",
    label: "Backfill queues",
    description: "Preview, trigger, and review historical data backfill jobs.",
    route: WORKSTATION_ROUTE_CATALOG.dataBackfills
  },
  {
    id: "settings-integrations",
    label: "Alpaca provider setup",
    description: "Repair paper credentials, service acknowledgements, and broker connection readiness.",
    route: WORKSTATION_ROUTE_CATALOG.settingsAlpacaProviderSetup
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
  query = "",
  operatingContextSymbol: string | null = null,
  operatingContextScope: AppShellOperatingScopeInput | null = null
): CommandPaletteViewModel {
  const activeKey = normalizeWorkspacePath(pathname);
  const activeRouteParts = splitActiveRoute(pathname);
  const visibleWorkspaces = canonicalizeWorkspaceSummaries(workspaces);
  const operatingScope = buildOperatingScopeFromSearch(activeRouteParts.search, {
    ...(operatingContextScope ?? {}),
    symbol: operatingContextScope?.symbol ?? operatingContextSymbol
  });
  const focusItems = buildFocusItems(workflowData.operatorFocusItems ?? [], pathname, operatingScope);
  const workspaceItems = buildWorkspaceItems(visibleWorkspaces, activeKey, operatingScope);
  const routeItems = buildRouteItems(pathname, operatingScope);
  const presetItems = buildPresetItems(workflowData.workflowPresets?.presets ?? [], pathname, operatingScope);
  const workflowItems = buildWorkflowItems(workflowData.workflowLibrary?.workflows ?? [], pathname, operatingScope);
  const items = [...focusItems, ...workspaceItems, ...routeItems, ...presetItems, ...workflowItems];
  const normalizedQuery = query.trim();
  const filteredItems = filterCommandItems(items, normalizedQuery);
  const commandGroups = buildCommandPaletteGroups(filteredItems);
  const hasWorkflowBackend = Boolean(
    workflowData.workflowLibrary || workflowData.workflowPresets || workflowData.workflowError
  );
  const emptyState = buildCommandPaletteEmptyState(items.length, filteredItems.length, normalizedQuery, hasWorkflowBackend);

  const activeWorkspace = workspaceItems.find((item) => item.active);
  const activeRoute = routeItems.find((item) => item.active);
  const activeWorkspaceLabel = activeWorkspace ? `Current: ${activeWorkspace.label}` : "No active workspace";
  const recommendedItems = buildRecommendedCommandItems(items, activeRoute?.id ?? null, activeWorkspace?.id ?? null);

  return {
    title: hasWorkflowBackend ? "Open workflow command" : "Open workstation command",
    subtitle: focusItems.length > 0
      ? "Jump to ranked operator focus, shared workflows, presets, quick routes, and canonical workspaces."
      : hasWorkflowBackend
      ? "Route through shared workflows, presets, quick routes, and canonical workspaces."
      : "Route to common operator workflows and canonical workspaces.",
    routeSummary: buildRouteSummary(
      activeWorkspaceLabel,
      Boolean(activeWorkspace),
      hasWorkflowBackend,
      workflowData.workflowError,
      focusItems.length
    ),
    shortcutHint: "Esc to close",
    scopeLabel: hasWorkflowBackend ? "Shared workflow, route, and workspace routing" : "Canonical workspace and route routing",
    operatingContextLabel: operatingScope.hasScope ? operatingScope.summary : null,
    backendStatusLabel: buildBackendStatusLabel(hasWorkflowBackend, workflowItems.length, presetItems.length, workflowData.workflowError),
    commandListLabel: hasWorkflowBackend
      ? `${items.length} command${items.length === 1 ? "" : "s"}`
      : `${items.length} workstation command${items.length === 1 ? "" : "s"}`,
    itemCountLabel: hasWorkflowBackend
      ? buildItemCountLabel(
          workspaceItems.length,
          routeItems.length,
          presetItems.length,
          workflowItems.length,
          focusItems.length
        )
      : buildLocalItemCountLabel(workspaceItems.length, routeItems.length, focusItems.length),
    activeWorkspaceLabel,
    initialFocusItemId:
      filteredItems.find((item) => item.kind === "focus")?.id
      ?? filteredItems.find((item) => item.id === activeRoute?.id)?.id
      ?? filteredItems.find((item) => item.id === activeWorkspace?.id)?.id
      ?? filteredItems[0]?.id
      ?? null,
    items,
    query,
    searchInputLabel: "Search command palette",
    searchPlaceholder: "Go to route, action, evidence...",
    searchDescribedBy: emptyState
      ? `${COMMAND_PALETTE_FILTER_COUNT_ID} ${emptyState.detailId}`
      : COMMAND_PALETTE_FILTER_COUNT_ID,
    filteredItems,
    commandGroups,
    recommendedItems,
    recommendedItemsLabel: "Recommended commands",
    recommendedItemsCountLabel: `${recommendedItems.length} recommended command${recommendedItems.length === 1 ? "" : "s"}`,
    filteredItemCountLabel: buildFilteredItemCountLabel(filteredItems.length, items.length, normalizedQuery),
    emptyState
  };
}

function buildRecommendedCommandItems(
  items: CommandPaletteItem[],
  activeRouteId: string | null,
  activeWorkspaceId: string | null
): CommandPaletteItem[] {
  const recommendedIds = [
    ...items.filter((item) => item.kind === "focus").map((item) => item.id),
    activeRouteId,
    activeWorkspaceId,
    ...items
      .filter((item) => item.kind === "route" && /evidence|readiness|reconciliation|provider|report/i.test(`${item.label} ${item.description}`))
      .map((item) => item.id),
    ...items.filter((item) => item.kind === "preset").map((item) => item.id)
  ].filter((id): id is string => Boolean(id));

  const seen = new Set<string>();
  const recommendedItems: CommandPaletteItem[] = [];

  for (const id of recommendedIds) {
    if (seen.has(id)) continue;
    const item = items.find((candidate) => candidate.id === id);
    if (!item) continue;

    seen.add(id);
    recommendedItems.push(item);

    if (recommendedItems.length >= 4) break;
  }

  return recommendedItems;
}

function buildCommandPaletteEmptyState(
  totalCount: number,
  filteredCount: number,
  normalizedQuery: string,
  hasWorkflowBackend: boolean
): CommandPaletteEmptyState | null {
  if (totalCount === 0) {
    return {
      id: COMMAND_PALETTE_EMPTY_STATE_ID,
      titleId: COMMAND_PALETTE_EMPTY_STATE_TITLE_ID,
      detailId: COMMAND_PALETTE_EMPTY_STATE_DETAIL_ID,
      actionId: null,
      title: hasWorkflowBackend ? "No workflow commands available" : "No workstation commands available",
      detail: hasWorkflowBackend
        ? "Workflow and workspace data did not load; retry workspace data before navigating."
        : "Workspace data did not load; retry workspace data before navigating.",
      statusLabel: "Unavailable",
      actionLabel: null,
      actionAriaLabel: null,
      canClearSearch: false
    };
  }

  if (normalizedQuery && filteredCount === 0) {
    return {
      id: COMMAND_PALETTE_EMPTY_STATE_ID,
      titleId: COMMAND_PALETTE_EMPTY_STATE_TITLE_ID,
      detailId: COMMAND_PALETTE_EMPTY_STATE_DETAIL_ID,
      actionId: COMMAND_PALETTE_CLEAR_SEARCH_ID,
      title: "No matching commands",
      detail: `No commands match "${normalizedQuery}". Clear the search to return to all workstation commands.`,
      statusLabel: "Empty",
      actionLabel: "Clear search",
      actionAriaLabel: `Clear command palette search for ${normalizedQuery}`,
      canClearSearch: true
    };
  }

  return null;
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

function buildWorkspaceItems(
  workspaces: WorkspaceSummary[],
  activeKey: WorkspaceKey,
  operatingScope: AppShellOperatingScopeState
): CommandPaletteItem[] {
  return workspaces.map<CommandPaletteItem>((workspace) => {
    const active = workspace.key === activeKey;
    const route = appendOperatingScopeToRoute(workspacePath(workspace.key), operatingScope);

    return {
      id: workspace.key,
      kind: "workspace",
      label: workspace.label,
      description: workspace.description,
      route,
      routeLabel: route,
      statusLabel: active ? "Current" : workspace.status,
      statusTone: active ? "current" : "neutral",
      statusVisible: active,
      commandLabel: active ? `Stay in ${workspace.label}` : `Open ${workspace.label}`,
      ariaLabel: active ? `${workspace.label}, current workspace` : `Open ${workspace.label} workspace`,
      presetId: null,
      active
    };
  });
}

function buildFocusItems(
  actions: CommandPaletteFocusAction[],
  pathname: string,
  operatingScope: AppShellOperatingScopeState
): CommandPaletteItem[] {
  return actions.map<CommandPaletteItem>((action) => {
    const route = materializeCommandRoute(action.route, operatingScope);
    const active = isActiveRoute(pathname, route);
    const carriedScopeSummary = summarizeOperatingScopeForRoute(action.route, operatingScope);
    const description = carriedScopeSummary && route !== action.route
      ? `${action.workspaceLabel}: ${action.detail} ${carriedScopeSummary}.`
      : `${action.workspaceLabel}: ${action.detail}`;
    const tone = commandStatusToneFromFocus(action.tone);
    const statusLabel = active
      ? `Current ${formatFocusTone(action.tone).toLowerCase()}`
      : formatFocusTone(action.tone);

    return {
      id: `focus:${action.id}`,
      kind: "focus",
      label: action.label,
      description,
      route,
      routeLabel: route,
      statusLabel,
      statusTone: tone,
      statusVisible: true,
      commandLabel: action.actionLabel || `Open ${action.label}`,
      ariaLabel: action.ariaLabel || `${action.workspaceLabel}: ${action.label}. ${action.detail}`,
      presetId: null,
      active
    };
  });
}

function commandStatusToneFromFocus(tone: CommandPaletteFocusAction["tone"]): CommandPaletteItemStatusTone {
  switch (tone) {
    case "blocked":
      return "blocked";
    case "review":
      return "review";
    case "ready":
      return "ready";
    case "pending":
      return "neutral";
  }
}

function formatFocusTone(tone: CommandPaletteFocusAction["tone"]) {
  switch (tone) {
    case "blocked":
      return "Blocked";
    case "review":
      return "Review";
    case "ready":
      return "Ready";
    case "pending":
      return "Pending";
  }
}

function buildRouteItems(pathname: string, operatingScope: AppShellOperatingScopeState): CommandPaletteItem[] {
  return LOCAL_ROUTE_COMMANDS.map<CommandPaletteItem>((routeCommand) => {
    const route = materializeCommandRoute(routeCommand.route, operatingScope);
    const carriedScopeSummary = summarizeOperatingScopeForRoute(routeCommand.route, operatingScope);
    const description = carriedScopeSummary && route !== routeCommand.route
      ? `${routeCommand.description} ${carriedScopeSummary}.`
      : routeCommand.description;
    const active = isActiveRoute(pathname, route);
    return {
      id: `route:${routeCommand.id}`,
      kind: "route",
      label: routeCommand.label,
      description,
      route,
      routeLabel: route,
      statusLabel: active ? "Current" : "Route",
      statusTone: active ? "current" : "neutral",
      statusVisible: active,
      commandLabel: active ? `Stay on ${routeCommand.label}` : `Open ${routeCommand.label}`,
      ariaLabel: active ? `${routeCommand.label}, current route` : `Open ${routeCommand.label} route`,
      presetId: null,
      active
    };
  });
}

function buildPresetItems(
  presets: WorkflowPreset[],
  pathname: string,
  operatingScope: AppShellOperatingScopeState
): CommandPaletteItem[] {
  return [...presets]
    .sort(comparePresets)
    .map<CommandPaletteItem>((preset) => {
      const route = materializeCommandRoute(workflowTargetPath(preset.targetPageTag, preset.workspaceId), operatingScope);
      const current = isExactActivePath(pathname, route);

      return {
        id: `preset:${preset.presetId}`,
        kind: "preset",
        label: preset.name,
        description: preset.description?.trim() || `${preset.workflowTitle}: ${preset.actionLabel}`,
        route,
        routeLabel: route,
        statusLabel: current ? "Current" : preset.isPinned ? "Pinned" : "Preset",
        statusTone: current ? "current" : preset.isPinned ? "ready" : "neutral",
        statusVisible: current,
        commandLabel: `Open preset ${preset.name}`,
        ariaLabel: `Open workflow preset ${preset.name}`,
        presetId: preset.presetId,
        active: false
      };
    });
}

function buildWorkflowItems(
  workflows: WorkflowDefinition[],
  pathname: string,
  operatingScope: AppShellOperatingScopeState
): CommandPaletteItem[] {
  return workflows.flatMap((workflow) =>
    workflow.actions.map<CommandPaletteItem>((action) => buildWorkflowItem(workflow, action, pathname, operatingScope))
  );
}

function buildWorkflowItem(
  workflow: WorkflowDefinition,
  action: WorkflowAction,
  pathname: string,
  operatingScope: AppShellOperatingScopeState
): CommandPaletteItem {
  const route = materializeCommandRoute(
    workflowTargetPath(action.targetPageTag || workflow.entryPageTag, workflow.workspaceId),
    operatingScope
  );
  const current = isExactActivePath(pathname, route);
  const status = workflowActionStatus(action.tone);

  return {
    id: `workflow:${workflow.workflowId}:${action.actionId}`,
    kind: "workflow",
    label: action.label,
    description: `${workflow.title}: ${action.detail || workflow.summary}`,
    route,
    routeLabel: route,
    statusLabel: current ? "Current" : status.label,
    statusTone: current ? "current" : status.tone,
    statusVisible: current || status.visible,
    commandLabel: action.label,
    ariaLabel: `${action.label}, ${workflow.title}`,
    presetId: null,
    active: false
  };
}

function workflowActionStatus(tone: string): {
  label: string;
  tone: CommandPaletteItemStatusTone;
  visible: boolean;
} {
  switch (tone.trim().toLowerCase()) {
    case "critical":
    case "danger":
    case "blocked":
      return { label: "Blocked", tone: "blocked", visible: true };
    case "warning":
    case "review":
    case "reviewrequired":
      return { label: "Review", tone: "review", visible: true };
    case "primary":
    case "success":
    case "ready":
      return { label: "Ready", tone: "ready", visible: true };
    default:
      return { label: "Workflow", tone: "neutral", visible: false };
  }
}

function buildRouteSummary(
  activeWorkspaceLabel: string,
  hasActiveWorkspace: boolean,
  hasWorkflowBackend: boolean,
  workflowError?: string | null,
  focusCount = 0
) {
  const current = hasActiveWorkspace ? activeWorkspaceLabel : "No active workspace";
  const focus = focusCount > 0
    ? ` ${focusCount} ranked focus action${focusCount === 1 ? "" : "s"} available.`
    : "";
  if (!hasWorkflowBackend) {
    return `Route to common operator workflows and canonical workspaces. ${current}.${focus}`;
  }

  if (workflowError) {
    return `Route through shared workflow commands. ${current}.${focus} Workflow library unavailable; local route commands remain available.`;
  }

  return `Route through shared workflow commands. ${current}.${focus}`;
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

function buildLocalItemCountLabel(workspaceCount: number, routeCount: number, focusCount = 0) {
  return joinCommandPaletteCounts([
    focusCount > 0 ? `${focusCount} focus action${focusCount === 1 ? "" : "s"}` : null,
    `${workspaceCount} workspace${workspaceCount === 1 ? "" : "s"}`,
    `${routeCount} quick route${routeCount === 1 ? "" : "s"}`
  ]);
}

function buildItemCountLabel(
  workspaceCount: number,
  routeCount: number,
  presetCount: number,
  workflowActionCount: number,
  focusCount = 0
) {
  return joinCommandPaletteCounts([
    focusCount > 0 ? `${focusCount} focus action${focusCount === 1 ? "" : "s"}` : null,
    `${workspaceCount} workspace${workspaceCount === 1 ? "" : "s"}`,
    `${routeCount} quick route${routeCount === 1 ? "" : "s"}`,
    `${presetCount} preset${presetCount === 1 ? "" : "s"}`,
    `${workflowActionCount} workflow action${workflowActionCount === 1 ? "" : "s"}`
  ]);
}

function joinCommandPaletteCounts(parts: Array<string | null>) {
  return parts.filter((part): part is string => Boolean(part)).join(" - ");
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

function materializeCommandRoute(route: string, operatingScope: AppShellOperatingScopeState) {
  return appendOperatingScopeToRoute(route, operatingScope);
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
