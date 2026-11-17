import {
  appendOperatingScopeToRoute,
  buildOperatingScopeFromSearch,
  summarizeOperatingScopeForRoute,
  type AppShellOperatingScopeInput
} from "@/app-shell.operating-scope";
import {
  canonicalizeWorkspaceSummaries,
  isWorkspacePathActive,
  UNWIRED_WORKSTATION_ROUTES,
  WORKSPACES,
  workspacePath
} from "@/lib/workspace";
import {
  ACCOUNTING_NAVIGATION_GROUPS,
  appendAccountingNavigationContextToRoute,
  isAccountingNavigationItemActive,
  type AccountingNavigationItemDefinition
} from "@/lib/accounting-navigation";
import { WORKSPACE_NAVIGATION_FEATURES, type WorkstationFeatureDefinition } from "@/lib/workstation-features";
import type { WorkspaceAttentionCounts } from "@/lib/daily-control-tower";
import { pluralizeCount } from "@/lib/format";
import type { WorkspaceKey, WorkspaceSummary } from "@/types";
import type { AppShellOperatorFocusItem } from "@/app-shell.workflow-continuity-types";

export interface WorkspaceNavSubItemViewModel {
  label: string;
  route: string;
  active: boolean;
  ariaCurrent: "page" | undefined;
  ariaLabel: string;
}

export interface WorkspaceNavSubItemGroupViewModel {
  id: string;
  label: string;
  items: WorkspaceNavSubItemViewModel[];
}

export interface WorkspaceNavItemViewModel {
  key: WorkspaceKey;
  label: string;
  description: string;
  maturityLabel: string;
  maturityTone: WorkspaceNavMaturityTone;
  /** The bare product maturity ("Preview"), for the link tooltip. */
  maturity: string;
  route: string;
  active: boolean;
  ariaCurrent: "page" | undefined;
  ariaLabel: string;
  /** Control Tower queue items owned by this workspace; 0 renders no badge. */
  attentionCount: number;
  /** Names the badge's unit in the link tooltip (the badge itself takes no pointer events); null when there is no badge. */
  attentionTitle: string | null;
  /** Every destination, primary and "More" alike, in browse order. */
  subItems: WorkspaceNavSubItemViewModel[];
  /** Grouped destinations the sidebar always lists. */
  subItemGroups: WorkspaceNavSubItemGroupViewModel[];
  /** Grouped destinations behind the sidebar's "More" disclosure; empty when there is none. */
  moreItemGroups: WorkspaceNavSubItemGroupViewModel[];
  /** True when the current page is behind "More", so the disclosure opens to show it. */
  moreActive: boolean;
  attentionSeverity: "blocked" | "review" | "info" | null;
}

export interface WorkspaceNavCurrentWorkspaceViewModel {
  label: string;
  description: string;
  maturityLabel: string;
  maturityTone: WorkspaceNavMaturityTone;
  route: string;
  routeAriaLabel: string;
  ariaLabel: string;
}

/**
 * The Daily Control Tower as the sidebar's front door: pinned above the seven workspaces and
 * deliberately not one of them (the seven-workspace navigation rule is unchanged).
 */
export interface WorkspaceNavTodayViewModel {
  label: string;
  route: string;
  active: boolean;
  ariaCurrent: "page" | undefined;
  ariaLabel: string;
  attentionCount: number;
  /** Names the badge's unit in the link tooltip (the badge itself takes no pointer events); null when there is no badge. */
  attentionTitle: string | null;
}

export interface WorkspaceNavViewModel {
  isHome: boolean;
  today: WorkspaceNavTodayViewModel;
  activeWorkspaceKey: WorkspaceKey | null;
  brandTitle: string;
  brandSubtitle: string;
  modelEyebrow: string;
  modelDescription: string;
  currentWorkspace: WorkspaceNavCurrentWorkspaceViewModel;
  operatingScopeLabel: string | null;
  operatingScopeAriaLabel: string | null;
  navEyebrow: string;
  contextEyebrow: string;
  contextDescription: string;
  contextItems: WorkspaceNavSubItemViewModel[];
  deliveryEyebrow: string;
  deliveryTitle: string;
  deliveryDescription: string;
  deliveryShortcutLabel: string;
  deliveryShortcutAriaLabel: string;
  items: WorkspaceNavItemViewModel[];
}

export type WorkspaceNavMaturityTone = "available" | "preview" | "setup";

type WorkspaceSubrouteDefinition = WorkstationFeatureDefinition;

export function buildWorkspaceNavViewModel(
  pathname: string,
  workspaces: WorkspaceSummary[] = WORKSPACES,
  search = "",
  operatingContextScope: AppShellOperatingScopeInput | null = null,
  attention: WorkspaceAttentionCounts | readonly AppShellOperatorFocusItem[] | null = null,
  suppliedAttentionItems: readonly AppShellOperatorFocusItem[] = []
): WorkspaceNavViewModel {
  const attentionItems: readonly AppShellOperatorFocusItem[] = Array.isArray(attention) ? attention : suppliedAttentionItems;
  const attentionCounts = attention && !Array.isArray(attention) ? attention as WorkspaceAttentionCounts : null;
  const isHome = pathname === "/";
  const visibleWorkspaces = canonicalizeWorkspaceSummaries(workspaces);
  const currentWorkspace =
    visibleWorkspaces.find((workspace) => isWorkspacePathActive(pathname, workspace.key)) ?? visibleWorkspaces[0];
  const operatingScope = buildOperatingScopeFromSearch(search, operatingContextScope);

  const items = visibleWorkspaces.map<WorkspaceNavItemViewModel>((workspace) => {
    const active = !isHome && isWorkspacePathActive(pathname, workspace.key);
    const maturityTone = workspaceMaturityTone(workspace.maturity);
    const workspaceCanonicalRoute = workspacePath(workspace.key);
    const exactWorkspaceActive = isExactRouteActive(pathname, workspaceCanonicalRoute);
    const workspaceRoute = buildWorkspaceNavigationRoute(
      workspace.key,
      workspaceCanonicalRoute,
      operatingScope,
      search
    );
    const workspaceScopeSummary = summarizeOperatingScopeForRoute(workspaceCanonicalRoute, operatingScope);
    const workspaceAttention = [...new Map(attentionItems.filter((item) => item.tone !== "ready"
      && ((visibleWorkspaces.find((candidate) => isWorkspacePathActive(item.route.split(/[?#]/)[0], candidate.key))?.key
        ?? item.workspaceLabel.toLowerCase()) === workspace.key)).map((item) => [item.id, item])).values()];
    // Supplied counts already carry the worst severity of exactly the items they count.
    const countedSeverity = attentionCounts?.severityByWorkspace?.[workspace.key];
    const attentionSeverity = attentionCounts?.severityByWorkspace
      ? countedSeverity === "blocked" ? "blocked" as const : countedSeverity === "action" ? "review" as const : null
      : workspaceAttention.some((item) => item.tone === "blocked") ? "blocked" as const
      : workspaceAttention.some((item) => item.tone === "review") ? "review" as const
      : workspaceAttention.length ? "info" as const : null;
    const attentionCount = attentionCounts?.byWorkspace[workspace.key] ?? workspaceAttention.length;
    const buildSubItem = (
      sub: WorkspaceSubrouteDefinition | AccountingNavigationItemDefinition
    ): WorkspaceNavSubItemViewModel => {
      const subActive = workspace.key === "accounting"
        ? isAccountingNavigationItemActive(pathname, sub as AccountingNavigationItemDefinition)
        : isSubRouteActiveForView(pathname, search, sub.route, sub.match, visibleWorkspaceSubroutes(workspace.key));
      const subRoute = buildWorkspaceNavigationRoute(workspace.key, sub.route, operatingScope, search);
      const subScopeSummary = summarizeOperatingScopeForRoute(sub.route, operatingScope);
      return {
        label: sub.label,
        route: subRoute,
        active: subActive,
        ariaCurrent: subActive ? "page" : undefined,
        ariaLabel: subActive
          ? `${sub.label}, current page${formatPreservedScopeAriaSuffix(subScopeSummary)}`
          : `Open ${sub.label}${formatPreservedScopeAriaSuffix(subScopeSummary)}`
      };
    };
    const rawSubRouteGroups = visibleWorkspaceSubrouteGroups(workspace.key);
    const allItemGroups = rawSubRouteGroups.map((group) => ({
      id: group.id,
      label: group.label,
      items: group.items.map((sub) => ({ sub, view: buildSubItem(sub) }))
    }));
    const pickGroups = (primary: boolean) => allItemGroups
      .map<WorkspaceNavSubItemGroupViewModel>((group) => ({
        id: group.id,
        label: group.label,
        items: group.items.filter(({ sub }) => Boolean((sub as AccountingNavigationItemDefinition).primary) === primary).map(({ view }) => view)
      }))
      .filter((group) => group.items.length > 0);
    // Only Accounting marks primary destinations; a workspace without them lists everything.
    const hasPrimaryTier = allItemGroups.some((group) => group.items.some(({ sub }) => (sub as AccountingNavigationItemDefinition).primary));
    const subItemGroups = hasPrimaryTier
      ? pickGroups(true)
      : allItemGroups.map((group) => ({ id: group.id, label: group.label, items: group.items.map(({ view }) => view) }));
    const moreItemGroups = hasPrimaryTier ? pickGroups(false) : [];
    const subItems = allItemGroups.length > 0
      ? allItemGroups.flatMap((group) => group.items.map(({ view }) => view))
      : visibleWorkspaceSubroutes(workspace.key).map(buildSubItem);
    const moreActive = moreItemGroups.some((group) => group.items.some((sub) => sub.active));

    return {
      key: workspace.key,
      label: workspace.label,
      description: workspace.description,
      maturityLabel: active ? `${workspace.maturity} · Current` : workspace.maturity,
      maturityTone,
      maturity: workspace.maturity,
      route: workspaceRoute,
      active,
      ariaCurrent: exactWorkspaceActive ? "page" : undefined,
      ariaLabel: `${active
        ? exactWorkspaceActive
          ? `${workspace.label} workspace, current route, ${workspace.maturity} product maturity`
          : `${workspace.label} workspace, active section, ${workspace.maturity} product maturity`
        : `Open ${workspace.label} workspace, ${workspace.maturity} product maturity`}${formatAttentionAriaSuffix(attentionCount)}${formatPreservedScopeAriaSuffix(workspaceScopeSummary)}`,
      attentionCount,
      attentionTitle: attentionCount > 0
        ? `${pluralizeCount(attentionCount, "item")} from ${workspace.label} in Today's finance queue`
        : null,
      subItems,
      subItemGroups,
      moreItemGroups,
      moreActive,
      attentionSeverity
    };
  });
  const currentRoute = items.find((item) => item.key === currentWorkspace.key)?.route
    ?? buildWorkspaceNavigationRoute(currentWorkspace.key, workspacePath(currentWorkspace.key), operatingScope, search);

  const contextItems = buildContextItems(pathname, currentWorkspace.key, operatingScope, search);

  const todayAttentionCount = attentionCounts?.total ?? new Set(attentionItems.filter((item) => item.tone !== "ready").map((item) => item.id)).size;
  const todayRoute = appendOperatingScopeToRoute("/", operatingScope);
  const today: WorkspaceNavTodayViewModel = {
    label: "Today",
    route: todayRoute,
    active: isHome,
    ariaCurrent: isHome ? "page" : undefined,
    ariaLabel: `${isHome ? "Today, Daily Control Tower, current route" : "Open Today, the Daily Control Tower"}${formatAttentionAriaSuffix(todayAttentionCount)}`,
    attentionCount: todayAttentionCount,
    attentionTitle: todayAttentionCount > 0
      ? `${pluralizeCount(todayAttentionCount, "item")} in Today's finance queue`
      : null
  };

  return {
    isHome,
    today,
    activeWorkspaceKey: isHome ? null : currentWorkspace.key,
    brandTitle: "Meridian",
    brandSubtitle: "Operator Workstation",
    modelEyebrow: "Operating model",
    modelDescription:
      "Workflow-centric shell for trading, portfolio, accounting, reporting, strategy, data, and settings.",
    currentWorkspace: {
      label: currentWorkspace.label,
      description: currentWorkspace.description,
      maturityLabel: `${currentWorkspace.maturity} product maturity`,
      maturityTone: workspaceMaturityTone(currentWorkspace.maturity),
      route: currentRoute,
      routeAriaLabel: operatingScope.hasScope ? `Scoped route ${currentRoute}` : `Canonical route ${currentRoute}`,
      ariaLabel: `Current workspace: ${currentWorkspace.label}, ${currentWorkspace.maturity} product maturity`
    },
    operatingScopeLabel: operatingScope.hasScope ? operatingScope.summary : null,
    operatingScopeAriaLabel: operatingScope.hasScope ? `Navigation preserves operating scope: ${operatingScope.summary}` : null,
    navEyebrow: "Workspaces",
    contextEyebrow: workspaceContextEyebrow(currentWorkspace.key),
    contextDescription: workspaceContextDescription(currentWorkspace.key),
    contextItems,
    deliveryEyebrow: "Shell controls",
    deliveryTitle: "Palette-first routing",
    deliveryDescription:
      "Use the shared command palette and canonical routes to move between workspaces while legacy aliases stay available.",
    deliveryShortcutLabel: "Ctrl K",
    deliveryShortcutAriaLabel: "Open command palette with Control K",
    items
  };
}

function visibleWorkspaceSubroutes(workspaceKey: WorkspaceKey): WorkspaceSubrouteDefinition[] {
  return WORKSPACE_NAVIGATION_FEATURES[workspaceKey].filter((sub) => !UNWIRED_WORKSTATION_ROUTES.has(sub.route));
}

function visibleWorkspaceSubrouteGroups(workspaceKey: WorkspaceKey) {
  return workspaceKey === "accounting"
    ? ACCOUNTING_NAVIGATION_GROUPS.map((group) => ({
      ...group,
      items: group.items.filter((sub) => !UNWIRED_WORKSTATION_ROUTES.has(sub.route))
    })).filter((group) => group.items.length > 0)
    : [];
}

function buildContextItems(
  pathname: string,
  workspaceKey: WorkspaceKey,
  operatingScope: ReturnType<typeof buildOperatingScopeFromSearch>,
  search: string
): WorkspaceNavSubItemViewModel[] {
  return visibleWorkspaceSubroutes(workspaceKey).map((sub) => {
    const active = workspaceKey === "accounting"
      ? isAccountingNavigationItemActive(pathname, sub as AccountingNavigationItemDefinition)
      : isSubRouteActiveForView(pathname, search, sub.route, sub.match, visibleWorkspaceSubroutes(workspaceKey));
    const route = buildWorkspaceNavigationRoute(workspaceKey, sub.route, operatingScope, search);
    const scopeSummary = summarizeOperatingScopeForRoute(sub.route, operatingScope);
    return {
      label: sub.label,
      route,
      active,
      ariaCurrent: active ? "page" : undefined,
      ariaLabel: active
        ? `${sub.label}, current page${formatPreservedScopeAriaSuffix(scopeSummary)}`
        : `Open ${sub.label}${formatPreservedScopeAriaSuffix(scopeSummary)}`
    };
  });
}

function buildWorkspaceNavigationRoute(
  workspaceKey: WorkspaceKey,
  route: string,
  operatingScope: ReturnType<typeof buildOperatingScopeFromSearch>,
  search: string
): string {
  const scopedRoute = appendOperatingScopeToRoute(route, operatingScope);
  return workspaceKey === "accounting"
    ? appendAccountingNavigationContextToRoute(scopedRoute, search)
    : scopedRoute;
}

function workspaceContextEyebrow(workspaceKey: WorkspaceKey): string {
  switch (workspaceKey) {
    case "portfolio":
      return "Clients and funds";
    case "reporting":
      return "Report pages";
    case "data":
      return "Data folders";
    case "strategy":
      return "Strategy views";
    case "accounting":
      return "Close queues";
    case "trading":
      return "Trading surfaces";
    case "settings":
      return "Task pages";
    default:
      return "Workspace";
  }
}

function workspaceContextDescription(workspaceKey: WorkspaceKey): string {
  switch (workspaceKey) {
    case "portfolio":
      return "Client, fund, attribution, and sync contexts.";
    case "reporting":
      return "Report pack, evidence, and export canvases.";
    case "data":
      return "Provider, ingestion, storage assurance, quote, and evidence folders.";
    case "strategy":
      return "Designer, lab, promotion, and projection contexts.";
    case "accounting":
      return "Ledger, reconciliation, approvals, and evidence queues.";
    case "trading":
      return "Orders, positions, risk, and readiness controls.";
    case "settings":
      return "Preferences, access, provider connections, accounting systems, and diagnostics.";
    default:
      return "Workspace routes.";
  }
}

/**
 * Some destinations are one route with a `view` query (Data > Market data is `/data/quotes`,
 * Data > Options screening is `/data/quotes?view=options`). The one whose view matches the page
 * is active; the bare route is active only when no sibling claims the page's view.
 */
function isSubRouteActiveForView(
  pathname: string,
  search: string,
  route: string,
  match: "exact" | "prefix" | undefined,
  siblings: readonly WorkspaceSubrouteDefinition[]
): boolean {
  const routeView = routeQueryView(route);
  const pageView = new URLSearchParams(search).get("view");
  if (!isSubRouteActive(pathname, route.split(/[?#]/)[0] ?? route, match)) {
    return false;
  }
  if (routeView !== null) {
    return routeView === pageView;
  }
  const routePath = route.split(/[?#]/)[0];
  return !siblings.some((sibling) => sibling.route.split(/[?#]/)[0] === routePath
    && routeQueryView(sibling.route) !== null
    && routeQueryView(sibling.route) === pageView);
}

function routeQueryView(route: string): string | null {
  const queryIndex = route.indexOf("?");
  return queryIndex < 0 ? null : new URLSearchParams(route.slice(queryIndex).split("#")[0]).get("view");
}

function isSubRouteActive(pathname: string, route: string, match: "exact" | "prefix" = "prefix"): boolean {
  if (match === "exact") {
    return isExactRouteActive(pathname, route);
  }

  const clean = pathname.split(/[?#]/)[0]?.replace(/\/+$/, "") || "/";
  const cleanRoute = route.replace(/\/+$/, "") || "/";
  return clean === cleanRoute || clean.startsWith(`${cleanRoute}/`);
}

function isExactRouteActive(pathname: string, route: string): boolean {
  const clean = pathname.split(/[?#]/)[0]?.replace(/\/+$/, "") || "/";
  const cleanRoute = route.split(/[?#]/)[0]?.replace(/\/+$/, "") || "/";
  return clean === cleanRoute;
}

function workspaceMaturityTone(maturity: WorkspaceSummary["maturity"]): WorkspaceNavMaturityTone {
  switch (maturity) {
    case "Available":
      return "available";
    case "Preview":
      return "preview";
    case "Setup":
      return "setup";
  }
}

function formatPreservedScopeAriaSuffix(scopeSummary: string | null): string {
  return scopeSummary ? `, preserving ${scopeSummary}` : "";
}

function formatAttentionAriaSuffix(count: number): string {
  return count > 0 ? `, ${count} ${count === 1 ? "item needs" : "items need"} attention` : "";
}
