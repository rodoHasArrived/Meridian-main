import {
  appendOperatingScopeToRoute,
  buildOperatingScopeFromSearch,
  summarizeOperatingScopeForRoute,
  type AppShellOperatingScopeInput
} from "@/app-shell.view-model";
import { isWorkspacePathActive, WORKSPACES, workspacePath } from "@/lib/workspace";
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
  deliveryEyebrow: string;
  deliveryTitle: string;
  deliveryDescription: string;
  deliveryShortcutLabel: string;
  deliveryShortcutAriaLabel: string;
  items: WorkspaceNavItemViewModel[];
}

export type WorkspaceNavStatusTone = "live" | "review" | "paper" | "preview" | "setup";

const WORKSPACE_SUBROUTES: Partial<Record<WorkspaceKey, { label: string; route: string }[]>> = {
  trading: [
    { label: "Orders", route: "/trading/orders" },
    { label: "Positions", route: "/trading/positions" },
    { label: "Risk", route: "/trading/risk" },
    { label: "Readiness", route: "/trading/readiness" }
  ],
  portfolio: [
    { label: "Attribution", route: "/portfolio/attribution" },
    { label: "Brokerage sync", route: "/portfolio/brokerage-sync" }
  ],
  accounting: [
    { label: "Ledger", route: "/accounting" },
    { label: "Reconciliation", route: "/accounting/reconciliation" },
    { label: "Security Master", route: "/accounting/security-master" },
    { label: "Approvals", route: "/accounting/approvals" }
  ],
  reporting: [
    { label: "Report packs", route: "/reporting/report-packs" },
    { label: "Evidence", route: "/reporting/evidence" },
    { label: "Exports", route: "/reporting/exports" }
  ],
  strategy: [
    { label: "Designer", route: "/strategy/designer" },
    { label: "Covered call", route: "/strategy/covered-call" },
    { label: "Promotions", route: "/strategy/promotions" },
    { label: "Research", route: "/strategy/research" },
    { label: "Quant Lab", route: "/strategy/quant-lab" }
  ],
  data: [
    { label: "Watchlist", route: "/data/watchlist" },
    { label: "Live quotes", route: "/data/quotes" },
    { label: "Price alerts", route: "/data/alerts" },
    { label: "Backfill queues", route: "/data/backfills" }
  ],
  settings: [
    { label: "Preferences", route: "/settings/preferences" },
    { label: "Integrations", route: "/settings/integrations" }
  ]
};

export function buildWorkspaceNavViewModel(
  pathname: string,
  workspaces: WorkspaceSummary[] = WORKSPACES,
  search = "",
  operatingContextScope: AppShellOperatingScopeInput | null = null
): WorkspaceNavViewModel {
  const currentWorkspace =
    workspaces.find((workspace) => isWorkspacePathActive(pathname, workspace.key)) ?? workspaces[0];
  const operatingScope = buildOperatingScopeFromSearch(search, operatingContextScope);

  const items = workspaces.map<WorkspaceNavItemViewModel>((workspace) => {
    const active = isWorkspacePathActive(pathname, workspace.key);
    const statusTone = workspaceStatusTone(workspace.status);
    const workspaceCanonicalRoute = workspacePath(workspace.key);
    const workspaceRoute = appendOperatingScopeToRoute(workspaceCanonicalRoute, operatingScope);
    const workspaceScopeSummary = summarizeOperatingScopeForRoute(workspaceCanonicalRoute, operatingScope);
    const rawSubRoutes = WORKSPACE_SUBROUTES[workspace.key] ?? [];
    const subItems: WorkspaceNavSubItemViewModel[] = active
      ? rawSubRoutes.map((sub) => {
          const subActive = isSubRouteActive(pathname, sub.route);
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
        })
      : [];

    return {
      key: workspace.key,
      label: workspace.label,
      description: workspace.description,
      statusLabel: active ? `${workspace.status} · Current` : workspace.status,
      statusTone,
      route: workspaceRoute,
      active,
      ariaCurrent: active ? "page" : undefined,
      ariaLabel: active
        ? `${workspace.label} workspace, current route, ${workspace.status}${formatPreservedScopeAriaSuffix(workspaceScopeSummary)}`
        : `Open ${workspace.label} workspace, ${workspace.status}${formatPreservedScopeAriaSuffix(workspaceScopeSummary)}`,
      subItems
    };
  });
  const currentRoute = appendOperatingScopeToRoute(workspacePath(currentWorkspace.key), operatingScope);

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
    deliveryEyebrow: "Shell controls",
    deliveryTitle: "Palette-first routing",
    deliveryDescription:
      "Use the shared command palette and canonical routes to move between lanes while legacy aliases stay available.",
    deliveryShortcutLabel: "Ctrl K",
    deliveryShortcutAriaLabel: "Open command palette with Control K",
    items
  };
}

function isSubRouteActive(pathname: string, route: string): boolean {
  const clean = pathname.split(/[?#]/)[0]?.replace(/\/+$/, "") || "/";
  const cleanRoute = route.replace(/\/+$/, "") || "/";
  return clean === cleanRoute || clean.startsWith(`${cleanRoute}/`);
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
