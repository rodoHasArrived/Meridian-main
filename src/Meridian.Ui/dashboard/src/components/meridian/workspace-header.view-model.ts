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
  disabledReason: string | null;
  busy: boolean;
}

export interface WorkspaceHeaderMetaItem {
  id: string;
  label: string;
  value: string;
  ariaLabel: string;
}

export interface LiveEnvironmentBanner {
  label: string;
  detail: string;
  ariaLabel: string;
  role: "alert";
  ariaLive: "assertive";
}

export interface WorkspaceHeaderViewModel {
  eyebrow: string;
  title: string;
  description: string;
  badges: WorkspaceHeaderBadge[];
  metaItems: WorkspaceHeaderMetaItem[];
  sessionLabel: string;
  sessionRoleLabel: string | null;
  sessionPillAriaLabel: string;
  refreshAction: WorkspaceHeaderAction | null;
  commandAction: WorkspaceHeaderAction;
  liveAnnouncement: string;
  ariaBusy: boolean;
  isLiveEnvironment: boolean;
  liveBanner: LiveEnvironmentBanner | null;
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
  const isLiveEnvironment = session?.environment === "live";
  const refreshLabel = refreshing ? "Refreshing" : "Refresh";
  const refreshDisabledReason = refreshing
    ? `${workspace.label} workspace data is refreshing.`
    : null;

  return {
    eyebrow: "Workspace",
    title: `${workspace.label} Workstation`,
    description: workspace.description,
    badges: [
      {
        id: "workspace-status",
        label: workspace.status,
        variant: statusVariant(workspace.status),
        ariaLabel: `${workspace.label} workspace status ${workspace.status}`
      }
    ],
    metaItems: [],
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
          disabled: refreshing,
          disabledReason: refreshDisabledReason,
          busy: refreshing
        }
      : null,
    commandAction: {
      label: "Open command palette",
      ariaLabel: "Open workspace command palette",
      title: "Open workspace command palette",
      disabled: false,
      disabledReason: null,
      busy: false
    },
    liveAnnouncement: refreshing ? `Refreshing ${workspace.label} workspace data.` : "",
    ariaBusy: refreshing,
    isLiveEnvironment,
    liveBanner: isLiveEnvironment
      ? {
          label: "LIVE ENVIRONMENT",
          detail: "All orders and position changes are executed against real accounts. Review all actions carefully before confirming.",
          ariaLabel: "Live environment warning: all actions execute against real accounts",
          role: "alert",
          ariaLive: "assertive"
        }
      : null
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
