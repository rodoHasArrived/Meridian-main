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
  WORKSTATION_ROUTE_CATALOG,
  workspacePath
} from "@/lib/workspace";
import type { WorkspaceKey, WorkspaceSummary } from "@/types";

export interface WorkspaceNavSubItemViewModel {
  label: string;
  route: string;
  active: boolean;
  ariaCurrent: "page" | undefined;
  ariaLabel: string;
}

export interface WorkspaceNavItemViewModel {
  key: WorkspaceKey;
  label: string;
  description: string;
  statusLabel: string;
  statusTone: WorkspaceNavStatusTone;
  route: string;
  active: boolean;
  ariaCurrent: "page" | undefined;
  ariaLabel: string;
  subItems: WorkspaceNavSubItemViewModel[];
}

export interface WorkspaceNavCurrentWorkspaceViewModel {
  label: string;
  description: string;
  statusLabel: string;
  statusTone: WorkspaceNavStatusTone;
  route: string;
  routeAriaLabel: string;
  ariaLabel: string;
}

export interface WorkspaceNavViewModel {
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

export type WorkspaceNavStatusTone = "live" | "review" | "paper" | "preview" | "setup";

type WorkspaceSubrouteDefinition = { label: string; route: string; match?: "exact" | "prefix" };

const WORKSPACE_SUBROUTES: Partial<Record<WorkspaceKey, WorkspaceSubrouteDefinition[]>> = {
  trading: [
    { label: "Overview", route: WORKSTATION_ROUTE_CATALOG.trading, match: "exact" },
    { label: "Orders", route: WORKSTATION_ROUTE_CATALOG.tradingOrders },
    { label: "Positions", route: WORKSTATION_ROUTE_CATALOG.tradingPositions },
    { label: "Risk", route: WORKSTATION_ROUTE_CATALOG.tradingRisk },
    { label: "Readiness", route: WORKSTATION_ROUTE_CATALOG.tradingReadiness }
  ],
  portfolio: [
    { label: "Overview", route: WORKSTATION_ROUTE_CATALOG.portfolio, match: "exact" },
    { label: "Attribution", route: WORKSTATION_ROUTE_CATALOG.portfolioAttribution },
    { label: "Asset detail", route: WORKSTATION_ROUTE_CATALOG.portfolioAssetDetail },
    { label: "Brokerage sync", route: WORKSTATION_ROUTE_CATALOG.portfolioBrokerageSync },
    { label: "Cash ladder", route: WORKSTATION_ROUTE_CATALOG.portfolioCashLadder },
    { label: "Family office", route: WORKSTATION_ROUTE_CATALOG.portfolioFamilyOffice }
  ],
  accounting: [
    { label: "Today", route: WORKSTATION_ROUTE_CATALOG.accounting, match: "exact" },
    { label: "Close", route: WORKSTATION_ROUTE_CATALOG.accountingOperationsContinuity },
    { label: "Entity setup", route: WORKSTATION_ROUTE_CATALOG.accountingEntitySetup },
    { label: "Ledger", route: WORKSTATION_ROUTE_CATALOG.accountingLedger },
    { label: "Adjustments", route: WORKSTATION_ROUTE_CATALOG.accountingJournalEntries },
    { label: "Reconciliation", route: WORKSTATION_ROUTE_CATALOG.accountingReconciliation, match: "exact" },
    { label: "External GL", route: WORKSTATION_ROUTE_CATALOG.accountingExternalGlReconciliation },
    { label: "Import statement", route: WORKSTATION_ROUTE_CATALOG.accountingStatementImport },
    { label: "Exceptions", route: WORKSTATION_ROUTE_CATALOG.accountingExceptions },
    { label: "Security Master", route: WORKSTATION_ROUTE_CATALOG.accountingSecurityMaster },
    { label: "Approvals", route: WORKSTATION_ROUTE_CATALOG.accountingApprovals },
    { label: "Configure", route: WORKSTATION_ROUTE_CATALOG.accountingConfigure }
  ],
  reporting: [
    { label: "Overview", route: WORKSTATION_ROUTE_CATALOG.reporting, match: "exact" },
    { label: "Report Library", route: WORKSTATION_ROUTE_CATALOG.reportingLibrary },
    { label: "Scheduled Reports", route: WORKSTATION_ROUTE_CATALOG.reportingScheduled },
    { label: "Run Report", route: WORKSTATION_ROUTE_CATALOG.reportingRunParameters },
    { label: "Operations record", route: WORKSTATION_ROUTE_CATALOG.reportingOperationsRecord },
    { label: "Report packs", route: WORKSTATION_ROUTE_CATALOG.reportingReportPacks },
    { label: "Evidence", route: WORKSTATION_ROUTE_CATALOG.reportingEvidence },
    { label: "Exports", route: WORKSTATION_ROUTE_CATALOG.reportingExports }
  ],
  strategy: [
    { label: "Overview", route: WORKSTATION_ROUTE_CATALOG.strategy, match: "exact" },
    { label: "Designer", route: WORKSTATION_ROUTE_CATALOG.strategyDesigner },
    { label: "Covered call", route: WORKSTATION_ROUTE_CATALOG.strategyCoveredCall },
    { label: "Promotions", route: WORKSTATION_ROUTE_CATALOG.strategyPromotions },
    { label: "Strategy Lab", route: WORKSTATION_ROUTE_CATALOG.strategyLab },
    { label: "Quant Lab", route: WORKSTATION_ROUTE_CATALOG.strategyQuantLab }
  ],
  data: [
    { label: "Overview", route: WORKSTATION_ROUTE_CATALOG.data, match: "exact" },
    { label: "Import data", route: WORKSTATION_ROUTE_CATALOG.dataImport },
    { label: "Providers", route: WORKSTATION_ROUTE_CATALOG.dataProviders },
    { label: "Market data", route: WORKSTATION_ROUTE_CATALOG.dataQuotes },
    { label: "Ingestion operations", route: WORKSTATION_ROUTE_CATALOG.dataOperations },
    { label: "Storage assurance", route: WORKSTATION_ROUTE_CATALOG.dataAssurance },
    { label: "Exports", route: WORKSTATION_ROUTE_CATALOG.dataExports },
    { label: "SQL query", route: WORKSTATION_ROUTE_CATALOG.dataQuery }
  ],
  settings: [
    { label: "Overview", route: WORKSTATION_ROUTE_CATALOG.settings, match: "exact" },
    { label: "Preferences", route: WORKSTATION_ROUTE_CATALOG.settingsPreferences },
    { label: "Access", route: WORKSTATION_ROUTE_CATALOG.settingsAccess },
    { label: "Provider Connections", route: WORKSTATION_ROUTE_CATALOG.settingsProviders },
    { label: "Accounting Systems", route: WORKSTATION_ROUTE_CATALOG.settingsAccountingSystems },
    { label: "Diagnostics", route: WORKSTATION_ROUTE_CATALOG.settingsDiagnostics }
  ]
};

export function buildWorkspaceNavViewModel(
  pathname: string,
  workspaces: WorkspaceSummary[] = WORKSPACES,
  search = "",
  operatingContextScope: AppShellOperatingScopeInput | null = null
): WorkspaceNavViewModel {
  const visibleWorkspaces = canonicalizeWorkspaceSummaries(workspaces);
  const currentWorkspace =
    visibleWorkspaces.find((workspace) => isWorkspacePathActive(pathname, workspace.key)) ?? visibleWorkspaces[0];
  const operatingScope = buildOperatingScopeFromSearch(search, operatingContextScope);

  const items = visibleWorkspaces.map<WorkspaceNavItemViewModel>((workspace) => {
    const active = isWorkspacePathActive(pathname, workspace.key);
    const statusTone = workspaceStatusTone(workspace.status);
    const workspaceCanonicalRoute = workspacePath(workspace.key);
    const exactWorkspaceActive = isExactRouteActive(pathname, workspaceCanonicalRoute);
    const workspaceRoute = appendOperatingScopeToRoute(workspaceCanonicalRoute, operatingScope);
    const workspaceScopeSummary = summarizeOperatingScopeForRoute(workspaceCanonicalRoute, operatingScope);
    const rawSubRoutes = visibleWorkspaceSubroutes(workspace.key);
    const subItems: WorkspaceNavSubItemViewModel[] = rawSubRoutes.map((sub) => {
      const subActive = isSubRouteActive(pathname, sub.route, sub.match);
      const subRoute = appendOperatingScopeToRoute(sub.route, operatingScope);
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
    });

    return {
      key: workspace.key,
      label: workspace.label,
      description: workspace.description,
      statusLabel: active ? `${workspace.status} · Current` : workspace.status,
      statusTone,
      route: workspaceRoute,
      active,
      ariaCurrent: exactWorkspaceActive ? "page" : undefined,
      ariaLabel: active
        ? exactWorkspaceActive
          ? `${workspace.label} workspace, current route, ${workspace.status}${formatPreservedScopeAriaSuffix(workspaceScopeSummary)}`
          : `${workspace.label} workspace, active section, ${workspace.status}${formatPreservedScopeAriaSuffix(workspaceScopeSummary)}`
        : `Open ${workspace.label} workspace, ${workspace.status}${formatPreservedScopeAriaSuffix(workspaceScopeSummary)}`,
      subItems
    };
  });
  const currentRoute = appendOperatingScopeToRoute(workspacePath(currentWorkspace.key), operatingScope);

  const contextItems = buildContextItems(pathname, currentWorkspace.key, operatingScope);

  return {
    brandTitle: "Meridian",
    brandSubtitle: "Operator Workstation",
    modelEyebrow: "Operating model",
    modelDescription:
      "Workflow-centric shell for trading, portfolio, accounting, reporting, strategy, data, and settings posture.",
    currentWorkspace: {
      label: currentWorkspace.label,
      description: currentWorkspace.description,
      statusLabel: `${currentWorkspace.status} posture`,
      statusTone: workspaceStatusTone(currentWorkspace.status),
      route: currentRoute,
      routeAriaLabel: operatingScope.hasScope ? `Scoped route ${currentRoute}` : `Canonical route ${currentRoute}`,
      ariaLabel: `Current workspace: ${currentWorkspace.label}, ${currentWorkspace.status} posture`
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
      "Use the shared command palette and canonical routes to move between lanes while legacy aliases stay available.",
    deliveryShortcutLabel: "Ctrl K",
    deliveryShortcutAriaLabel: "Open command palette with Control K",
    items
  };
}

function visibleWorkspaceSubroutes(workspaceKey: WorkspaceKey): WorkspaceSubrouteDefinition[] {
  return (WORKSPACE_SUBROUTES[workspaceKey] ?? []).filter((sub) => !UNWIRED_WORKSTATION_ROUTES.has(sub.route));
}

function buildContextItems(
  pathname: string,
  workspaceKey: WorkspaceKey,
  operatingScope: ReturnType<typeof buildOperatingScopeFromSearch>
): WorkspaceNavSubItemViewModel[] {
  return visibleWorkspaceSubroutes(workspaceKey).map((sub) => {
    const active = isSubRouteActive(pathname, sub.route, sub.match);
    const route = appendOperatingScopeToRoute(sub.route, operatingScope);
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

function workspaceStatusTone(status: string): WorkspaceNavStatusTone {
  switch (status.toLowerCase()) {
    case "live":
      return "live";
    case "paper":
      return "paper";
    case "preview":
      return "preview";
    case "setup":
      return "setup";
    default:
      return "review";
  }
}

function formatPreservedScopeAriaSuffix(scopeSummary: string | null): string {
  return scopeSummary ? `, preserving ${scopeSummary}` : "";
}
