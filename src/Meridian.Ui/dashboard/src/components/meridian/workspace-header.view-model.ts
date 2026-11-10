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
  /** Shown in the About disclosure: the product maturity that used to be a page badge. */
  maturityNote: string;
  /** Accessible name of the About section in the overflow menu. */
  aboutLabel: string;
  metaItems: WorkspaceHeaderMetaItem[];
  sessionLabel: string;
  sessionRoleLabel: string | null;
  sessionPillAriaLabel: string;
  refreshAction: WorkspaceHeaderAction | null;
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
  /** The page name that ends the breadcrumb and is the header's heading. Defaults to the workspace. */
  pageTitle?: string;
}

export function buildWorkspaceHeaderViewModel({
  workspace,
  session,
  canRefresh,
  refreshing = false,
  pageTitle
}: BuildWorkspaceHeaderViewModelOptions): WorkspaceHeaderViewModel {
  const isLiveEnvironment = session?.environment === "live";
  const refreshLabel = refreshing ? "Refreshing" : "Refresh";
  const refreshDisabledReason = refreshing
    ? `${workspace.label} workspace data is refreshing.`
    : null;

  return {
    eyebrow: "Workspace",
    // The breadcrumb's last crumb is the heading; the route body carries the task title.
    title: pageTitle ?? workspace.label,
    description: workspace.description,
    // Product maturity told an operator nothing about what is wrong or what to do as a page
    // badge, so it lives in the About section of the overflow menu instead.
    badges: [],
    maturityNote: `Product maturity: ${workspace.maturity}.`,
    aboutLabel: `About ${workspace.label}`,
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
