import { normalizeWorkspacePath, WORKSPACES, workspaceForPath } from "@/lib/workspace";
import type {
  DataOperationsWorkspaceResponse,
  GovernanceWorkspaceResponse,
  PortfolioWorkspaceResponse,
  ResearchWorkspaceResponse,
  SessionInfo,
  SystemOverviewResponse,
  TradingWorkspaceResponse,
  WorkspaceKey,
  WorkspaceSummary
} from "@/types";

export type ShellStatusTone = "loading" | "warning" | "danger";

export interface ShellStatusItem {
  key: WorkspaceKey;
  label: string;
  detail: string;
  ariaLabel: string;
}

export interface ShellStatusPanel {
  id: string;
  titleId: string;
  detailId: string;
  tone: ShellStatusTone;
  title: string;
  detail: string;
  role: "status" | "alert";
  ariaLive: "polite" | "assertive";
  actionLabel: string | null;
  actionAriaLabel: string | null;
  itemListLabel: string;
  items: ShellStatusItem[];
}

export interface AppShellViewState {
  activeWorkspace: WorkspaceSummary;
  statusPanel: ShellStatusPanel | null;
  canRenderRoutes: boolean;
  routeFocus: AppShellRouteFocusState;
}

export interface AppShellRouteFocusState {
  routeKey: string;
  announcement: string;
  documentTitle: string;
  targetElementId: string | null;
  fallbackElementId: string;
}

export interface AppShellWorkspacePayload {
  session: SessionInfo | null;
  overview: SystemOverviewResponse | null;
  research: ResearchWorkspaceResponse | null;
  trading: TradingWorkspaceResponse | null;
  portfolio: PortfolioWorkspaceResponse | null;
  dataOperations: DataOperationsWorkspaceResponse | null;
  governance: GovernanceWorkspaceResponse | null;
  reporting: GovernanceWorkspaceResponse | null;
}

export type WorkspaceErrorMap = Partial<Record<WorkspaceKey, string>>;

export interface BuildAppShellViewStateOptions {
  pathname: string;
  hash?: string;
  loading: boolean;
  error: string | null;
  workspaceErrors: WorkspaceErrorMap;
  payload: AppShellWorkspacePayload;
}

export function buildAppShellViewState({
  pathname,
  hash = "",
  loading,
  error,
  workspaceErrors,
  payload
}: BuildAppShellViewStateOptions): AppShellViewState {
  const activeWorkspace = getWorkspaceForPath(pathname);
  const failedItems = buildWorkspaceFailureItems(workspaceErrors);
  const hasAnyPayload = Object.values(payload).some(Boolean);
  const bootstrapFailed = !loading && !hasAnyPayload;

  return {
    activeWorkspace,
    statusPanel: buildShellStatusPanel({
      loading,
      error,
      failedItems,
      bootstrapFailed
    }),
    canRenderRoutes: !loading && !bootstrapFailed,
    routeFocus: buildRouteFocusState(pathname, hash, activeWorkspace)
  };
}

export function getWorkspaceForPath(pathname: string): WorkspaceSummary {
  return workspaceForPath(pathname);
}

export function normalizeWorkspace(pathname: string): WorkspaceKey {
  return normalizeWorkspacePath(pathname);
}

export function buildRouteFocusState(
  pathname: string,
  hash: string,
  activeWorkspace: WorkspaceSummary
): AppShellRouteFocusState {
  const workspaceTitle = `${activeWorkspace.label} Workstation`;
  const targetElementId = normalizeHashTarget(hash);
  const targetLabel = targetElementId ? formatHashTargetLabel(targetElementId) : null;

  return {
    routeKey: `${pathname}${hash}`,
    announcement: targetLabel
      ? `${workspaceTitle} loaded. Jumping to ${targetLabel}.`
      : `${workspaceTitle} loaded.`,
    documentTitle: `${workspaceTitle} - Meridian`,
    targetElementId,
    fallbackElementId: "workbench-content"
  };
}

function buildShellStatusPanel({
  loading,
  error,
  failedItems,
  bootstrapFailed
}: {
  loading: boolean;
  error: string | null;
  failedItems: ShellStatusItem[];
  bootstrapFailed: boolean;
}): ShellStatusPanel | null {
  if (loading) {
    return {
      id: "workstation-shell-status-loading",
      titleId: "workstation-shell-status-loading-title",
      detailId: "workstation-shell-status-loading-detail",
      tone: "loading",
      title: "Booting workstation shell",
      detail: "Loading session state, operator workspaces, and the initial workstation evidence slices.",
      role: "status",
      ariaLive: "polite",
      actionLabel: null,
      actionAriaLabel: null,
      itemListLabel: "Workspace bootstrap status",
      items: []
    };
  }

  if (bootstrapFailed) {
    return {
      id: "workstation-shell-status-failed",
      titleId: "workstation-shell-status-failed-title",
      detailId: "workstation-shell-status-failed-detail",
      tone: "danger",
      title: "Workstation bootstrap failed",
      detail: error ?? "No workstation payloads loaded. Retry the bootstrap before reviewing operator state.",
      role: "alert",
      ariaLive: "assertive",
      actionLabel: "Retry bootstrap",
      actionAriaLabel: "Retry workstation bootstrap",
      itemListLabel: "Bootstrap failure details",
      items: failedItems
    };
  }

  if (failedItems.length > 0) {
    return {
      id: "workstation-shell-status-degraded",
      titleId: "workstation-shell-status-degraded-title",
      detailId: "workstation-shell-status-degraded-detail",
      tone: "warning",
      title: "Workstation bootstrap is partially degraded",
      detail: `${failedItems.length} workspace ${failedItems.length === 1 ? "slice" : "slices"} failed to load. Available routes remain open while those slices recover.`,
      role: "status",
      ariaLive: "polite",
      actionLabel: "Retry failed slices",
      actionAriaLabel: "Retry failed workstation slices",
      itemListLabel: "Failed workspace slices",
      items: failedItems
    };
  }

  return null;
}

function buildWorkspaceFailureItems(workspaceErrors: WorkspaceErrorMap): ShellStatusItem[] {
  return Object.entries(workspaceErrors)
    .map(([key, detail]) => {
      const workspaceKey = key as WorkspaceKey;
      const label = WORKSPACES.find((workspace) => workspace.key === workspaceKey)?.label ?? key;
      return {
        key: workspaceKey,
        label,
        detail: detail || "Workspace request failed.",
        ariaLabel: `${label}: ${detail || "Workspace request failed."}`
      };
    })
    .sort((left, right) => left.label.localeCompare(right.label));
}

function normalizeHashTarget(hash: string): string | null {
  if (!hash.startsWith("#") || hash.length <= 1) {
    return null;
  }

  try {
    return decodeURIComponent(hash.slice(1));
  } catch {
    return hash.slice(1);
  }
}

function formatHashTargetLabel(targetElementId: string): string {
  return targetElementId
    .split(/[-_\s]+/)
    .filter(Boolean)
    .join(" ")
    .toLowerCase();
}
