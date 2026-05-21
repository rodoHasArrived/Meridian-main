import { WORKSTATION_ROUTE_CATALOG, workspacePath } from "@/lib/workspace";
import type { SessionInfo, SystemOverviewResponse, WorkspaceKey, WorkspaceSummary } from "@/types";

interface PlaceholderActionDefinition {
  id: string;
  label: string;
  detail: string;
  route: string;
}

export interface PlaceholderAction extends PlaceholderActionDefinition {
  detailId: string;
  routeLabel: string;
  ariaLabel: string;
}

export interface PlaceholderStatusCell {
  id: string;
  label: string;
  value: string;
  ariaLabel: string;
}

export interface PlaceholderCoverageItem {
  id: string;
  title: string;
  detail: string;
}

export interface WorkspacePlaceholderViewModel {
  route: string;
  title: string;
  description: string;
  routeRegionLabel: string;
  pendingTitle: string;
  pendingDescription: string;
  pendingRegionLabel: string;
  actionsLabel: string;
  telemetryLabel: string;
  coverageLabel: string;
  coverageTitle: string;
  coverageDescription: string;
  routeStatus: string;
  statusCells: PlaceholderStatusCell[];
  telemetryCells: PlaceholderStatusCell[];
  coverageItems: PlaceholderCoverageItem[];
  actions: PlaceholderAction[];
}

export interface BuildWorkspacePlaceholderViewModelOptions {
  workspace: WorkspaceSummary;
  session: SessionInfo | null;
  overview: SystemOverviewResponse | null;
}

const placeholderGuidance: Partial<
  Record<
    WorkspaceKey,
    {
      pendingTitle: string;
      pendingDescription: string;
      coverageTitle: string;
      coverageDescription: string;
      coverageItems: PlaceholderCoverageItem[];
      actions: PlaceholderActionDefinition[];
    }
  >
> = {
  portfolio: {
    pendingTitle: "Portfolio surface is pending",
    pendingDescription:
      "The route is reserved for portfolio exposure, position attribution, and equity continuity. Use the linked workspaces for the current review path until the dedicated portfolio surface lands.",
    coverageTitle: "Current portfolio review path",
    coverageDescription:
      "Meridian already covers the portfolio handoff across the live web workstation. Use these routes to move from exposure review to attribution evidence without losing the operator trail.",
    coverageItems: [
      {
        id: "portfolio-exposure",
        title: "Exposure posture stays in Trading",
        detail: "Use the trading cockpit for open positions, mark-to-market posture, and paper-session evidence."
      },
      {
        id: "portfolio-ledger",
        title: "Control evidence lands in Accounting",
        detail: "Reconciliation, cash-flow, and Security Master coverage continue to anchor portfolio sign-off."
      },
      {
        id: "portfolio-attribution",
        title: "Run attribution stays linked in Strategy",
        detail: "Compare contributing runs before treating portfolio continuity as operator-ready."
      }
    ],
    actions: [
      {
        id: "trading-readiness",
        label: "Review trading readiness",
        detail: "Check active sessions, orders, fills, replay evidence, and promotion blockers.",
        route: WORKSTATION_ROUTE_CATALOG.trading
      },
      {
        id: "accounting-ledger",
        label: "Review ledger continuity",
        detail: "Inspect reconciliation, cash-flow, and Security Master evidence tied to positions.",
        route: WORKSTATION_ROUTE_CATALOG.accounting
      },
      {
        id: "strategy-runs",
        label: "Inspect strategy runs",
        detail: "Compare run output before accepting portfolio attribution as operator-ready.",
        route: WORKSTATION_ROUTE_CATALOG.strategy
      }
    ]
  },
  settings: {
    pendingTitle: "Settings surface is pending",
    pendingDescription:
      "The route is reserved for workstation setup, integrations, and operator preferences. Use the linked workspaces for provider posture and runtime readiness until the dedicated settings surface lands.",
    coverageTitle: "Current setup and controls path",
    coverageDescription:
      "The web workstation already exposes the supporting checks operators need for setup reviews. Use these routes to validate posture first, then return here once the dedicated surface lands.",
    coverageItems: [
      {
        id: "settings-provider",
        title: "Provider posture is live in Data",
        detail: "Review feed wiring, backfill status, and symbol readiness before changing integrations."
      },
      {
        id: "settings-session",
        title: "Session posture is anchored in Trading",
        detail: "Confirm paper/live readiness, replay evidence, and the active operating mode before updating setup."
      },
      {
        id: "settings-controls",
        title: "Control evidence remains in Accounting",
        detail: "Trust gates, reconciliation breaks, and ledger continuity still define completion for workstation setup."
      }
    ],
    actions: [
      {
        id: "data-providers",
        label: "Review provider setup",
        detail: "Check provider health, backfills, and symbol readiness before changing integrations.",
        route: WORKSTATION_ROUTE_CATALOG.data
      },
      {
        id: "trading-session",
        label: "Check session readiness",
        detail: "Confirm the active paper/live operating posture before adjusting workstation setup.",
        route: WORKSTATION_ROUTE_CATALOG.trading
      },
      {
        id: "accounting-controls",
        label: "Review control evidence",
        detail: "Check trust-gate and reconciliation posture before treating setup as complete.",
        route: WORKSTATION_ROUTE_CATALOG.accounting
      }
    ]
  }
};

const fallbackActions: PlaceholderActionDefinition[] = [
  {
    id: "trading-readiness",
    label: "Review trading readiness",
    detail: "Use the trading cockpit as the primary operator readiness surface.",
    route: WORKSTATION_ROUTE_CATALOG.trading
  },
  {
    id: "strategy-runs",
    label: "Review strategy runs",
    detail: "Use the strategy surface for current run and promotion evidence.",
    route: WORKSTATION_ROUTE_CATALOG.strategy
  }
];

const fallbackCoverageItems: PlaceholderCoverageItem[] = [
  {
    id: "fallback-canonical-route",
    title: "Canonical navigation is already in place",
    detail: "This route stays visible in the workstation shell so operators can adopt the final information architecture before the dedicated screen lands."
  },
  {
    id: "fallback-live-workflows",
    title: "Live workflows remain available in linked workspaces",
    detail: "Use Trading, Accounting, Reporting, Strategy, or Data to continue the active operator flow today."
  }
];

export function buildWorkspacePlaceholderViewModel({
  workspace,
  session,
  overview
}: BuildWorkspacePlaceholderViewModelOptions): WorkspacePlaceholderViewModel {
  const route = workspacePath(workspace.key);
  const guidance = placeholderGuidance[workspace.key];
  const routeStatus = guidance ? "Reserved pending surface" : "Reserved route";

  return {
    route,
    title: `${workspace.label} workspace route is staged`,
    description: workspace.description,
    routeRegionLabel: `${workspace.label} route status`,
    pendingTitle: guidance?.pendingTitle ?? "Dedicated workspace surface pending",
    pendingDescription:
      guidance?.pendingDescription ??
      "This route is reserved in the canonical navigation while the web workstation moves remaining operator workflows into dedicated surfaces.",
    pendingRegionLabel: `${workspace.label} pending workspace guidance`,
    actionsLabel: `${workspace.label} temporary workflow actions`,
    telemetryLabel: `${workspace.label} route telemetry`,
    coverageLabel: `${workspace.label} current workflow coverage`,
    coverageTitle: guidance?.coverageTitle ?? "Current operator coverage",
    coverageDescription:
      guidance?.coverageDescription ??
      "This route is staged while Meridian keeps the active review path inside the canonical workspaces already available in the web workstation.",
    routeStatus,
    statusCells: [
      buildStatusCell("route", "Route", route),
      buildStatusCell("route-status", "Route status", routeStatus),
      buildStatusCell("session", "Session", session ? `${session.displayName} - ${session.role}` : "Session loading"),
      buildStatusCell("commands", "Commands", formatCommandStatus(session))
    ],
    telemetryCells: [
      buildStatusCell("system-status", "System status", overview?.systemStatus ?? "Not loaded"),
      buildStatusCell("providers", "Providers", formatProviderStatus(overview)),
      buildStatusCell("last-heartbeat", "Last heartbeat", formatHeartbeat(overview?.lastHeartbeatUtc))
    ],
    coverageItems: guidance?.coverageItems ?? fallbackCoverageItems,
    actions: buildPlaceholderActions(guidance?.actions ?? fallbackActions)
  };
}

function buildPlaceholderActions(actions: PlaceholderActionDefinition[]): PlaceholderAction[] {
  return actions.map((action) => ({
    ...action,
    detailId: `placeholder-action-${sanitizeDomId(action.id)}-detail`,
    routeLabel: `Route ${action.route}`,
    ariaLabel: `${action.label}. ${action.detail} Opens ${action.route}.`
  }));
}

function buildStatusCell(id: string, label: string, value: string): PlaceholderStatusCell {
  return {
    id,
    label,
    value,
    ariaLabel: `${label}: ${value}`
  };
}

function formatCommandStatus(session: SessionInfo | null): string {
  if (!session) {
    return "Command surface loading";
  }

  return `${session.commandCount} commands ready`;
}

function formatProviderStatus(overview: SystemOverviewResponse | null): string {
  if (!overview) {
    return "Provider posture loading";
  }

  return `${overview.providersOnline} of ${overview.providersTotal} online`;
}

export function formatHeartbeat(heartbeatUtc: string | null | undefined): string {
  if (!heartbeatUtc) {
    return "No heartbeat loaded";
  }

  const heartbeat = new Date(heartbeatUtc);
  if (Number.isNaN(heartbeat.getTime())) {
    return "Invalid heartbeat timestamp";
  }

  const month = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"][
    heartbeat.getUTCMonth()
  ];
  const day = String(heartbeat.getUTCDate()).padStart(2, "0");
  const year = heartbeat.getUTCFullYear();
  const hour = String(heartbeat.getUTCHours()).padStart(2, "0");
  const minute = String(heartbeat.getUTCMinutes()).padStart(2, "0");

  return `${month} ${day}, ${year} ${hour}:${minute} UTC`;
}

function sanitizeDomId(value: string): string {
  const normalized = value.trim().toLowerCase().replace(/[^a-z0-9_-]+/g, "-").replace(/^-+|-+$/g, "");
  return normalized || "action";
}
