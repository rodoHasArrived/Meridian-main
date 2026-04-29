import { workspacePath } from "@/lib/workspace";
import type { SessionInfo, SystemOverviewResponse, WorkspaceKey, WorkspaceSummary } from "@/types";

export interface PlaceholderAction {
  id: string;
  label: string;
  detail: string;
  route: string;
}

export interface PlaceholderStatusCell {
  id: string;
  label: string;
  value: string;
}

export interface WorkspacePlaceholderViewModel {
  route: string;
  title: string;
  description: string;
  pendingTitle: string;
  pendingDescription: string;
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
      actions: PlaceholderAction[];
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

const fallbackActions: PlaceholderAction[] = [
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
    pendingTitle: guidance?.pendingTitle ?? "Dedicated workspace surface pending",
    pendingDescription:
      guidance?.pendingDescription ??
      "This route is reserved in the canonical navigation while the web workstation moves remaining operator workflows into dedicated surfaces.",
    routeStatus,
    statusCells: [
      {
        id: "route",
        label: "Route",
        value: route
      },
      {
        id: "route-status",
        label: "Route status",
        value: routeStatus
      },
      {
        id: "session",
        label: "Session",
        value: session ? `${session.displayName} - ${session.role}` : "Session loading"
      }
    ],
    telemetryCells: [
      {
        id: "system-status",
        label: "System status",
        value: overview?.systemStatus ?? "Not loaded"
      },
      {
        id: "last-heartbeat",
        label: "Last heartbeat",
        value: formatHeartbeat(overview?.lastHeartbeatUtc)
      }
    ],
    actions: guidance?.actions ?? fallbackActions
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
