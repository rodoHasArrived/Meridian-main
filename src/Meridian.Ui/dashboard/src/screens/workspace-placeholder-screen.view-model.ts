import { workspacePath } from "@/lib/workspace";
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
  routeStatus: string;
  statusCells: PlaceholderStatusCell[];
  telemetryCells: PlaceholderStatusCell[];
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
      actions: PlaceholderActionDefinition[];
    }
  >
> = {
  portfolio: {
    pendingTitle: "Portfolio surface is pending",
    pendingDescription:
      "The route is reserved for portfolio exposure, position attribution, and equity continuity. Use the linked workspaces for the current review path until the dedicated portfolio surface lands.",
    actions: [
      {
        id: "trading-readiness",
        label: "Review trading readiness",
        detail: "Check active sessions, orders, fills, replay evidence, and promotion blockers.",
        route: "/trading"
      },
      {
        id: "accounting-ledger",
        label: "Review ledger continuity",
        detail: "Inspect reconciliation, cash-flow, and Security Master evidence tied to positions.",
        route: "/accounting"
      },
      {
        id: "strategy-runs",
        label: "Inspect strategy runs",
        detail: "Compare run output before accepting portfolio attribution as operator-ready.",
        route: "/strategy"
      }
    ]
  },
  settings: {
    pendingTitle: "Settings surface is pending",
    pendingDescription:
      "The route is reserved for workstation setup, integrations, and operator preferences. Use the linked workspaces for provider posture and runtime readiness until the dedicated settings surface lands.",
    actions: [
      {
        id: "data-providers",
        label: "Review provider setup",
        detail: "Check provider health, backfills, and symbol readiness before changing integrations.",
        route: "/data"
      },
      {
        id: "trading-session",
        label: "Check session readiness",
        detail: "Confirm the active paper/live operating posture before adjusting workstation setup.",
        route: "/trading"
      },
      {
        id: "accounting-controls",
        label: "Review control evidence",
        detail: "Check trust-gate and reconciliation posture before treating setup as complete.",
        route: "/accounting"
      }
    ]
  }
};

const fallbackActions: PlaceholderActionDefinition[] = [
  {
    id: "trading-readiness",
    label: "Review trading readiness",
    detail: "Use the trading cockpit as the primary operator readiness surface.",
    route: "/trading"
  },
  {
    id: "strategy-runs",
    label: "Review strategy runs",
    detail: "Use the strategy surface for current run and promotion evidence.",
    route: "/strategy"
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
    title: `${workspace.label} route is available`,
    description: workspace.description,
    routeRegionLabel: `${workspace.label} route status`,
    pendingTitle: guidance?.pendingTitle ?? "Dedicated workspace surface pending",
    pendingDescription:
      guidance?.pendingDescription ??
      "This route is reserved in the canonical navigation while the web workstation moves remaining operator workflows into dedicated surfaces.",
    pendingRegionLabel: `${workspace.label} pending workspace guidance`,
    actionsLabel: `${workspace.label} temporary workflow actions`,
    telemetryLabel: `${workspace.label} route telemetry`,
    routeStatus,
    statusCells: [
      buildStatusCell("route", "Route", route),
      buildStatusCell("route-status", "Route status", routeStatus),
      buildStatusCell("session", "Session", session ? `${session.displayName} - ${session.role}` : "Session loading")
    ],
    telemetryCells: [
      buildStatusCell("system-status", "System status", overview?.systemStatus ?? "Not loaded"),
      buildStatusCell("last-heartbeat", "Last heartbeat", formatHeartbeat(overview?.lastHeartbeatUtc))
    ],
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
