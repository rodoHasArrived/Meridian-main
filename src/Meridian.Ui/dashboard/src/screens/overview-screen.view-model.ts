import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { getSystemStatus } from "@/lib/api";
import { WORKSPACES, workspacePath } from "@/lib/workspace";
import type { MetricSnapshot, SessionInfo, SystemEventRecord, SystemOverviewResponse, WorkspaceKey } from "@/types";

export type OverviewRefreshFetcher = () => Promise<SystemOverviewResponse>;

export type OverviewFallbackStatId = "providers" | "runs" | "symbols" | "backfills";

export interface OverviewFallbackStat {
  id: OverviewFallbackStatId;
  label: string;
  value: string;
  tone: "default" | "success" | "warning" | "danger";
}

export interface OverviewWorkspaceLink {
  id: WorkspaceKey;
  label: string;
  description: string;
  href: string;
  status: string;
  badgeVariant: "outline" | "warning" | "paper" | "live";
  ariaLabel: string;
}

export type OverviewActivityTone = "default" | "warning" | "danger";
export type OverviewActivityBadgeVariant = "outline" | "warning" | "danger";

export interface OverviewActivityRow {
  id: string;
  type: SystemEventRecord["type"];
  typeLabel: string;
  statusCode: string;
  badgeVariant: OverviewActivityBadgeVariant;
  tone: OverviewActivityTone;
  message: string;
  source: string;
  timestampLabel: string;
  ariaLabel: string;
}

export type OverviewBriefingTone = "default" | "success" | "warning" | "danger";
export type OverviewBriefingBadgeVariant = "paper" | "live" | "research";

export interface OverviewBriefingItem {
  id: "session" | "environment" | "providers" | "heartbeat";
  label: string;
  value: string;
  detail: string;
  tone: OverviewBriefingTone;
  badgeVariant: OverviewBriefingBadgeVariant | null;
  ariaLabel: string;
}

export interface OverviewPriorityRoute {
  id: "trading" | "accounting" | "reporting";
  eyebrow: string;
  title: string;
  detail: string;
  buttonLabel: string;
  href: string;
  status: string;
  badgeVariant: OverviewWorkspaceLink["badgeVariant"];
  description: string;
  ariaLabel: string;
}

export interface OverviewStatusBannerState {
  role: "status" | "alert";
  ariaLive: "polite" | "assertive";
  titleId: string;
  detailId: string | null;
  label: string;
  detailText: string | null;
  ariaLabel: string;
}

export interface OverviewStatusState {
  current: SystemOverviewResponse | null;
  metrics: MetricSnapshot[];
  events: SystemEventRecord[];
  activityRows: OverviewActivityRow[];
  briefingItems: OverviewBriefingItem[];
  priorityRoutes: OverviewPriorityRoute[];
  fallbackStats: OverviewFallbackStat[];
  workspaceLinks: OverviewWorkspaceLink[];
  workspaceSummary: string;
  hasMetrics: boolean;
  hasEvents: boolean;
  activityListLabel: string;
  statusBanner: OverviewStatusBannerState;
  statusLabel: string;
  providerSummary: string | null;
  storageLabel: string | null;
  lastHeartbeatLabel: string | null;
  activityEmptyText: string;
  refreshButtonLabel: string;
  refreshAriaLabel: string;
  refreshErrorText: string | null;
  refreshAnnouncement: string;
}

interface BuildOverviewStatusStateOptions {
  current: SystemOverviewResponse | null;
  session: SessionInfo | null;
  refreshing: boolean;
  refreshError: string | null;
  refreshedAt: Date | null;
}

export function buildOverviewStatusState({
  current,
  session,
  refreshing,
  refreshError,
  refreshedAt
}: BuildOverviewStatusStateOptions): OverviewStatusState {
  const metrics = current?.metrics ?? [];
  const events = current?.recentEvents ?? [];
  const activityRows = buildOverviewActivityRows(events);
  const workspaceLinks = buildOverviewWorkspaceLinks();
  const refreshErrorText = refreshError
    ? `Refresh failed: ${refreshError}. Showing the last known status.`
    : null;
  const refreshedAtLabel = refreshedAt ? formatTime(refreshedAt) : null;
  const statusLabel = current ? statusLabels[current.systemStatus] : "Connecting to system...";
  const providerSummary = current ? `${current.providersOnline} of ${current.providersTotal} providers online` : null;
  const storageLabel = current ? storageLabels[current.storageHealth] : null;
  const lastHeartbeatLabel = current ? formatTime(current.lastHeartbeatUtc) : null;

  return {
    current,
    metrics,
    events,
    activityRows,
    briefingItems: buildOverviewBriefingItems({
      session,
      current,
      providerSummary,
      storageLabel,
      lastHeartbeatLabel,
      refreshErrorText
    }),
    priorityRoutes: buildOverviewPriorityRoutes(workspaceLinks),
    fallbackStats: buildFallbackStats(current),
    workspaceLinks,
    workspaceSummary: "7 canonical operator routes. Legacy routes redirect to their canonical workspaces.",
    hasMetrics: metrics.length > 0,
    hasEvents: activityRows.length > 0,
    activityListLabel: activityRows.length === 1 ? "1 recent system event" : `${activityRows.length} recent system events`,
    statusBanner: buildOverviewStatusBanner({
      current,
      statusLabel,
      providerSummary,
      storageLabel,
      lastHeartbeatLabel,
      refreshErrorText
    }),
    statusLabel,
    providerSummary,
    storageLabel,
    lastHeartbeatLabel,
    activityEmptyText: current ? "No recent events." : "Loading activity feed...",
    refreshButtonLabel: refreshing ? "Refreshing..." : "Refresh",
    refreshAriaLabel: refreshing ? "Refreshing system status" : "Refresh system status",
    refreshErrorText,
    refreshAnnouncement: refreshing
      ? "Refreshing system status."
      : refreshErrorText ?? (refreshedAtLabel ? `System status refreshed at ${refreshedAtLabel}.` : "")
  };
}

export function useOverviewStatusViewModel(
  initialData: SystemOverviewResponse | null,
  session: SessionInfo | null,
  fetchSystemStatus: OverviewRefreshFetcher = getSystemStatus
) {
  const mountedRef = useRef(false);
  const refreshRevisionRef = useRef(0);
  const [refreshing, setRefreshing] = useState(false);
  const [liveData, setLiveData] = useState<SystemOverviewResponse | null>(initialData);
  const [refreshError, setRefreshError] = useState<string | null>(null);
  const [refreshedAt, setRefreshedAt] = useState<Date | null>(null);

  const current = liveData ?? initialData;

  useEffect(() => {
    mountedRef.current = true;

    return () => {
      mountedRef.current = false;
      refreshRevisionRef.current += 1;
    };
  }, []);

  const refresh = useCallback(async () => {
    const revision = refreshRevisionRef.current + 1;
    refreshRevisionRef.current = revision;
    setRefreshing(true);
    setRefreshError(null);

    try {
      const fresh = await fetchSystemStatus();
      if (!mountedRef.current || refreshRevisionRef.current !== revision) {
        return;
      }

      setLiveData(fresh);
      setRefreshedAt(new Date());
    } catch (err) {
      if (!mountedRef.current || refreshRevisionRef.current !== revision) {
        return;
      }

      setRefreshError(err instanceof Error ? err.message : "Unable to refresh system status.");
    } finally {
      if (mountedRef.current && refreshRevisionRef.current === revision) {
        setRefreshing(false);
      }
    }
  }, [fetchSystemStatus]);

  const state = useMemo(
    () => buildOverviewStatusState({ current, session, refreshing, refreshError, refreshedAt }),
    [current, session, refreshing, refreshError, refreshedAt]
  );

  return {
    ...state,
    refreshing,
    refresh
  };
}

export function buildOverviewWorkspaceLinks(): OverviewWorkspaceLink[] {
  return WORKSPACES.map((workspace) => ({
    id: workspace.key,
    label: workspace.label,
    description: workspace.description,
    href: workspacePath(workspace.key),
    status: workspace.status,
    badgeVariant: badgeVariantForWorkspaceStatus(workspace.status),
    ariaLabel: `Open ${workspace.label} workspace. ${workspace.description} Status ${workspace.status}.`
  }));
}

export function buildOverviewPriorityRoutes(workspaces: OverviewWorkspaceLink[]): OverviewPriorityRoute[] {
  return workspaces
    .filter((workspace): workspace is OverviewWorkspaceLink & { id: OverviewPriorityRoute["id"] } => (
      workspace.id === "trading" || workspace.id === "accounting" || workspace.id === "reporting"
    ))
    .map((workspace) => ({
      id: workspace.id,
      ...priorityRouteCopy[workspace.id],
      href: workspace.href,
      status: workspace.status,
      badgeVariant: workspace.badgeVariant,
      description: workspace.description,
      ariaLabel: workspace.ariaLabel
    }));
}

export function buildOverviewBriefingItems({
  session,
  current,
  providerSummary,
  storageLabel,
  lastHeartbeatLabel,
  refreshErrorText
}: {
  session: SessionInfo | null;
  current: SystemOverviewResponse | null;
  providerSummary: string | null;
  storageLabel: string | null;
  lastHeartbeatLabel: string | null;
  refreshErrorText: string | null;
}): OverviewBriefingItem[] {
  const items: Omit<OverviewBriefingItem, "ariaLabel">[] = [
    {
      id: "session",
      label: "Session",
      value: session ? session.displayName : "Awaiting session",
      detail: session ? `${session.role} - ${session.commandCount} commands ready` : "Load a session to unlock command context",
      tone: "default",
      badgeVariant: null
    },
    {
      id: "environment",
      label: "Operating mode",
      value: session ? session.environment : "Pending",
      detail: session ? `Current route ${session.activeWorkspace}` : "Environment is not loaded yet",
      tone: session?.environment === "live" ? "danger" : session?.environment === "paper" ? "success" : "default",
      badgeVariant: session ? session.environment : null
    },
    {
      id: "providers",
      label: "Provider posture",
      value: providerSummary ?? "Provider posture loading",
      detail: storageLabel ? `Storage ${storageLabel}` : "Storage posture loading",
      tone: current?.systemStatus === "Offline"
        ? "danger"
        : current?.systemStatus === "Degraded"
          ? "warning"
          : current
            ? "success"
            : "default",
      badgeVariant: null
    },
    {
      id: "heartbeat",
      label: "Heartbeat",
      value: lastHeartbeatLabel ?? "Waiting for heartbeat",
      detail: refreshErrorText ?? "Refresh the status banner to confirm the latest control-room posture",
      tone: refreshErrorText ? "danger" : "default",
      badgeVariant: null
    }
  ];

  return items.map((item) => ({
    ...item,
    ariaLabel: `${item.label}: ${item.value}. ${item.detail}`
  }));
}

export function buildOverviewActivityRows(events: SystemEventRecord[]): OverviewActivityRow[] {
  return events.map((event) => {
    const typeState = activityTypeState[event.type];
    const source = event.source.trim() || "Unknown source";
    const timestampLabel = formatTime(event.timestamp);

    return {
      id: event.id,
      type: event.type,
      typeLabel: typeState.typeLabel,
      statusCode: typeState.statusCode,
      badgeVariant: typeState.badgeVariant,
      tone: typeState.tone,
      message: event.message,
      source,
      timestampLabel,
      ariaLabel: `${typeState.typeLabel} event from ${source} at ${timestampLabel}: ${event.message}`
    };
  });
}

export function buildOverviewStatusBanner({
  current,
  statusLabel,
  providerSummary,
  storageLabel,
  lastHeartbeatLabel,
  refreshErrorText
}: {
  current: SystemOverviewResponse | null;
  statusLabel: string;
  providerSummary: string | null;
  storageLabel: string | null;
  lastHeartbeatLabel: string | null;
  refreshErrorText: string | null;
}): OverviewStatusBannerState {
  const detailText = current && providerSummary && storageLabel && lastHeartbeatLabel
    ? `${providerSummary}. Storage ${storageLabel}. Last heartbeat ${lastHeartbeatLabel}.`
    : current
      ? "Status detail is unavailable."
      : "Waiting for the workstation status payload.";
  const isInterruptive = refreshErrorText !== null || current?.systemStatus === "Degraded" || current?.systemStatus === "Offline";

  return {
    role: isInterruptive ? "alert" : "status",
    ariaLive: isInterruptive ? "assertive" : "polite",
    titleId: "overview-status-title",
    detailId: detailText ? "overview-status-detail" : null,
    label: statusLabel,
    detailText,
    ariaLabel: `${statusLabel}. ${refreshErrorText ?? detailText}`
  };
}

const statusLabels: Record<SystemOverviewResponse["systemStatus"], string> = {
  Healthy: "All Systems Healthy",
  Degraded: "System Degraded",
  Offline: "System Offline"
};

const storageLabels: Record<SystemOverviewResponse["storageHealth"], string> = {
  Healthy: "Healthy",
  Warning: "Warning",
  Critical: "Critical"
};

const activityTypeState: Record<SystemEventRecord["type"], Pick<OverviewActivityRow, "typeLabel" | "statusCode" | "badgeVariant" | "tone">> = {
  info: {
    typeLabel: "Info",
    statusCode: "INFO",
    badgeVariant: "outline",
    tone: "default"
  },
  warning: {
    typeLabel: "Warning",
    statusCode: "OBS",
    badgeVariant: "warning",
    tone: "warning"
  },
  error: {
    typeLabel: "Error",
    statusCode: "ERR",
    badgeVariant: "danger",
    tone: "danger"
  }
};

const priorityRouteCopy: Record<OverviewPriorityRoute["id"], Pick<OverviewPriorityRoute, "eyebrow" | "title" | "detail" | "buttonLabel">> = {
  trading: {
    eyebrow: "Execution posture",
    title: "Keep the active session ready",
    detail: "Review paper-session evidence, promotion blockers, and live readiness before the next operator action.",
    buttonLabel: "Open trading cockpit"
  },
  accounting: {
    eyebrow: "Control evidence",
    title: "Clear ledger and trust-gate blockers",
    detail: "Resolve reconciliation, Security Master, and control follow-up before treating the day as sign-off ready.",
    buttonLabel: "Open accounting lane"
  },
  reporting: {
    eyebrow: "Governed outputs",
    title: "Prepare distribution-ready reporting",
    detail: "Check report-pack posture, approvals, and export readiness before circulating governed output.",
    buttonLabel: "Open reporting lane"
  }
};

function buildFallbackStats(current: SystemOverviewResponse | null): OverviewFallbackStat[] {
  return [
    {
      id: "providers",
      label: "Providers Online",
      value: current ? `${current.providersOnline} / ${current.providersTotal}` : "-",
      tone: !current
        ? "default"
        : current.providersOnline === current.providersTotal
          ? "success"
          : current.providersOnline === 0
            ? "danger"
            : "warning"
    },
    {
      id: "runs",
      label: "Active Runs",
      value: current ? String(current.activeRuns) : "-",
      tone: current && current.activeRuns > 0 ? "success" : "default"
    },
    {
      id: "symbols",
      label: "Monitored Symbols",
      value: current ? String(current.symbolsMonitored) : "-",
      tone: "default"
    },
    {
      id: "backfills",
      label: "Active Backfills",
      value: current ? String(current.activeBackfills) : "-",
      tone: current && current.activeBackfills > 0 ? "warning" : "default"
    }
  ];
}

function badgeVariantForWorkspaceStatus(status: string): OverviewWorkspaceLink["badgeVariant"] {
  if (status === "Live") {
    return "live";
  }

  if (status === "Paper") {
    return "paper";
  }

  if (status === "Review") {
    return "warning";
  }

  return "outline";
}

function formatTime(value: string | Date): string {
  const date = typeof value === "string" ? new Date(value) : value;
  return Number.isNaN(date.getTime()) ? "Unavailable" : date.toLocaleTimeString();
}
