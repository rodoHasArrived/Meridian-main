import type { SessionInfo, WorkspaceSummary } from "@/types";

export type WorkspaceHeaderBadgeVariant =
  | "default"
  | "outline"
  | "success"
  | "warning"
  | "danger"
  | "paper"
  | "live"
  | "research";

export interface WorkspaceHeaderBadge {
  id: string;
  label: string;
  variant: WorkspaceHeaderBadgeVariant;
  ariaLabel: string;
}

export interface WorkspaceHeaderAction {
  label: string;
  ariaLabel: string;
  title: string;
  disabled: boolean;
}

export interface WorkspaceHeaderViewModel {
  eyebrow: string;
  title: string;
  description: string;
  badges: WorkspaceHeaderBadge[];
  sessionLabel: string;
  sessionRoleLabel: string | null;
  sessionPillAriaLabel: string;
  refreshAction: WorkspaceHeaderAction | null;
  commandAction: WorkspaceHeaderAction;
  liveAnnouncement: string;
  ariaBusy: boolean;
}

interface BuildWorkspaceHeaderViewModelOptions {
  workspace: WorkspaceSummary;
  session: SessionInfo | null;
  canRefresh: boolean;
  refreshing?: boolean;
}

export function buildWorkspaceHeaderViewModel({
  workspace,
  session,
  canRefresh,
  refreshing = false
}: BuildWorkspaceHeaderViewModelOptions): WorkspaceHeaderViewModel {
  const environmentBadge = session
    ? {
        id: "environment",
        label: session.environment.toUpperCase(),
        variant: session.environment,
        ariaLabel: `${session.environment} environment`
      }
    : null;
  const refreshLabel = refreshing ? "Refreshing" : "Refresh";

  return {
    eyebrow: "Meridian workspace",
    title: `${workspace.label} Workstation`,
    description: workspace.description,
    badges: [
      {
        id: "workspace",
        label: workspace.label,
        variant: "outline",
        ariaLabel: `${workspace.label} workspace`
      },
      ...(environmentBadge ? [environmentBadge] : []),
      {
        id: "workspace-status",
        label: workspace.status,
        variant: statusVariant(workspace.status),
        ariaLabel: `${workspace.label} workspace status ${workspace.status}`
      }
    ],
    sessionLabel: session?.displayName ?? "Loading session",
    sessionRoleLabel: session?.role ?? null,
    sessionPillAriaLabel: session
      ? `Session ${session.displayName}, role ${session.role}`
      : "Session context loading",
    refreshAction: canRefresh
      ? {
          label: refreshLabel,
          ariaLabel: refreshing
            ? `Refreshing ${workspace.label} workspace data`
            : `Refresh ${workspace.label} workspace data`,
          title: refreshing
            ? `${workspace.label} workspace data is refreshing`
            : `Refresh ${workspace.label} workspace data`,
          disabled: refreshing
        }
      : null,
    commandAction: {
      label: "Open command palette",
      ariaLabel: "Open workspace command palette",
      title: "Open workspace command palette",
      disabled: false
    },
    liveAnnouncement: refreshing ? `Refreshing ${workspace.label} workspace data.` : "",
    ariaBusy: refreshing
  };
}

function statusVariant(status: string): WorkspaceHeaderBadgeVariant {
  switch (status.toLowerCase()) {
    case "live":
      return "success";
    case "paper":
      return "paper";
    case "review":
      return "warning";
    case "setup":
    case "preview":
      return "outline";
    default:
      return "default";
  }
}
