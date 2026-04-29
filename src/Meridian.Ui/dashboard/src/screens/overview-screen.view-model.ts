import { useCallback, useMemo, useState } from "react";
import { getSystemStatus } from "@/lib/api";
import { WORKSPACES, workspacePath } from "@/lib/workspace";
import type { MetricSnapshot, SystemEventRecord, SystemOverviewResponse, WorkspaceKey } from "@/types";

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

export interface OverviewStatusState {
  current: SystemOverviewResponse | null;
  metrics: MetricSnapshot[];
  events: SystemEventRecord[];
  activityRows: OverviewActivityRow[];
  fallbackStats: OverviewFallbackStat[];
  workspaceLinks: OverviewWorkspaceLink[];
  workspaceSummary: string;
  hasMetrics: boolean;
  hasEvents: boolean;
  activityListLabel: string;
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
  refreshing: boolean;
  refreshError: string | null;
  refreshedAt: Date | null;
}

export function buildOverviewStatusState({
  current,
  refreshing,
  refreshError,
  refreshedAt
}: BuildOverviewStatusStateOptions): OverviewStatusState {
  const metrics = current?.metrics ?? [];
  const events = current?.recentEvents ?? [];
  const activityRows = buildOverviewActivityRows(events);
  const refreshErrorText = refreshError
    ? `Refresh failed: ${refreshError}. Showing the last known status.`
    : null;
  const refreshedAtLabel = refreshedAt ? formatTime(refreshedAt) : null;

  return {
    current,
    metrics,
    events,
    activityRows,
    fallbackStats: buildFallbackStats(current),
    workspaceLinks: buildOverviewWorkspaceLinks(),
    workspaceSummary: "7 canonical operator routes. Legacy routes redirect to their canonical workspaces.",
    hasMetrics: metrics.length > 0,
    hasEvents: activityRows.length > 0,
    activityListLabel: activityRows.length === 1 ? "1 recent system event" : `${activityRows.length} recent system events`,
    statusLabel: current ? statusLabels[current.systemStatus] : "Connecting to system...",
    providerSummary: current ? `${current.providersOnline} of ${current.providersTotal} providers online` : null,
    storageLabel: current ? storageLabels[current.storageHealth] : null,
    lastHeartbeatLabel: current ? formatTime(current.lastHeartbeatUtc) : null,
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
  fetchSystemStatus: OverviewRefreshFetcher = getSystemStatus
) {
  const [refreshing, setRefreshing] = useState(false);
  const [liveData, setLiveData] = useState<SystemOverviewResponse | null>(initialData);
  const [refreshError, setRefreshError] = useState<string | null>(null);
  const [refreshedAt, setRefreshedAt] = useState<Date | null>(null);

  const current = liveData ?? initialData;

  const refresh = useCallback(async () => {
    setRefreshing(true);
    setRefreshError(null);

    try {
      const fresh = await fetchSystemStatus();
      setLiveData(fresh);
      setRefreshedAt(new Date());
    } catch (err) {
      setRefreshError(err instanceof Error ? err.message : "Unable to refresh system status.");
    } finally {
      setRefreshing(false);
    }
  }, [fetchSystemStatus]);

  const state = useMemo(
    () => buildOverviewStatusState({ current, refreshing, refreshError, refreshedAt }),
    [current, refreshing, refreshError, refreshedAt]
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
