import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { getSystemStatus } from "@/lib/api";
import { WORKSPACES, workspacePath } from "@/lib/workspace";
import type {
  MetricSnapshot,
  PortfolioWorkspaceResponse,
  SessionInfo,
  SystemEventRecord,
  SystemOverviewResponse,
  TradingWorkspaceResponse,
  WorkspaceKey
} from "@/types";

export type OverviewRefreshFetcher = () => Promise<SystemOverviewResponse>;

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
  selectAriaLabel: string;
  detailPanelId: string;
  expanded: boolean;
}

export interface OverviewActivityDetailField {
  label: string;
  value: string;
}

export interface OverviewActivityDetail {
  eyebrow: string;
  title: string;
  subtitle: string;
  description: string;
  badgeLabel: string;
  badgeVariant: OverviewActivityBadgeVariant;
  ariaLabel: string;
  fields: OverviewActivityDetailField[];
}

export interface OverviewActivitySelectionViewModel {
  rows: OverviewActivityRow[];
  selectedRowId: string | null;
  selectedDetail: OverviewActivityDetail | null;
  detailPanelId: string;
  detailPanelTitle: string;
  detailPanelDescription: string;
  detailPanelEmptyText: string;
  detailPanelAriaLabel: string;
  tableLabel: string;
  tableCaption: string;
  selectActivity: (id: string) => void;
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
  id: "trading" | "accounting" | "reporting" | "data" | "settings";
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

export type OverviewValueBlockerTone = "default" | "warning" | "danger";
export type OverviewValueBlockerBadgeVariant = "outline" | "warning" | "danger";

export interface OverviewValueBlocker {
  id: string;
  title: string;
  detail: string;
  href: string;
  actionLabel: string;
  badgeLabel: string;
  badgeVariant: OverviewValueBlockerBadgeVariant;
  tone: OverviewValueBlockerTone;
  ariaLabel: string;
}

export type OverviewStatusBannerIcon = "healthy" | "warning" | "offline" | "pending";

export interface OverviewStatusBannerDetailParts {
  providerSummary: string;
  storageLabel: string;
  storageClassName: string;
  lastHeartbeatLabel: string;
}

export interface OverviewStatusBannerState {
  role: "status" | "alert";
  ariaLive: "polite" | "assertive";
  titleId: string;
  detailId: string | null;
  label: string;
  detailText: string | null;
  detailParts: OverviewStatusBannerDetailParts | null;
  ariaLabel: string;
  icon: OverviewStatusBannerIcon;
  containerClassName: string;
  iconClassName: string;
  titleClassName: string;
}

export interface OverviewRefreshCommandState {
  label: string;
  ariaLabel: string;
  busyLabel: string | null;
  disabled: boolean;
  disabledReason: string | null;
  busy: boolean;
}

export interface OverviewStatusState {
  current: SystemOverviewResponse | null;
  metrics: MetricSnapshot[];
  events: SystemEventRecord[];
  activityRows: OverviewActivityRow[];
  briefingItems: OverviewBriefingItem[];
  priorityRoutes: OverviewPriorityRoute[];
  valueBlockers: OverviewValueBlocker[];
  fallbackStats: MetricSnapshot[];
  workspaceLinks: OverviewWorkspaceLink[];
  workspaceSummary: string;
  hasMetrics: boolean;
  hasEvents: boolean;
  hasValueBlockers: boolean;
  activityListLabel: string;
  valueBlockerRegionLabel: string;
  valueBlockerSummary: string;
  statusBanner: OverviewStatusBannerState;
  statusLabel: string;
  providerSummary: string | null;
  storageLabel: string | null;
  lastHeartbeatLabel: string | null;
  activityEmptyText: string;
  refreshCommand: OverviewRefreshCommandState;
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
  const metrics = Array.isArray(current?.metrics) ? current.metrics : [];
  const events = Array.isArray(current?.recentEvents) ? current.recentEvents : [];
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
  const valueBlockers = buildOverviewValueBlockers(current, refreshErrorText);
  const refreshCommand = buildOverviewRefreshCommand(refreshing);

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
    priorityRoutes: buildOverviewPriorityRoutes(workspaceLinks, current),
    valueBlockers,
    fallbackStats: buildFallbackStats(current),
    workspaceLinks,
    workspaceSummary: "7 canonical operator routes. Legacy routes redirect to their canonical workspaces.",
    hasMetrics: metrics.length > 0,
    hasEvents: activityRows.length > 0,
    hasValueBlockers: valueBlockers.length > 0,
    activityListLabel: activityRows.length === 1 ? "1 recent system event" : `${activityRows.length} recent system events`,
    valueBlockerRegionLabel: valueBlockers.length === 1 ? "1 readiness blocker" : `${valueBlockers.length} readiness blockers`,
    valueBlockerSummary: buildOverviewValueBlockerSummary(valueBlockers),
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
    refreshCommand,
    refreshButtonLabel: refreshCommand.label,
    refreshAriaLabel: refreshCommand.ariaLabel,
    refreshErrorText,
    refreshAnnouncement: refreshing
      ? "Refreshing system status."
      : refreshErrorText ?? (refreshedAtLabel ? `System status refreshed at ${refreshedAtLabel}.` : "")
  };
}

export function buildOverviewRefreshCommand(refreshing: boolean): OverviewRefreshCommandState {
  return {
    label: refreshing ? "Refreshing..." : "Refresh",
    ariaLabel: refreshing ? "Refreshing system status" : "Refresh system status",
    busyLabel: refreshing ? "Refreshing..." : null,
    disabled: refreshing,
    disabledReason: refreshing ? "System status refresh is already in progress." : null,
    busy: refreshing
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

export const OVERVIEW_ACTIVITY_DETAIL_PANEL_ID = "overview-activity-selected-detail";

export function useOverviewActivitySelectionViewModel(
  activityRows: OverviewActivityRow[]
): OverviewActivitySelectionViewModel {
  const [selectedActivityId, setSelectedActivityId] = useState<string | null>(null);

  const selectedRowId = useMemo(
    () => resolveSelectedActivityId(activityRows, selectedActivityId),
    [activityRows, selectedActivityId]
  );

  useEffect(() => {
    if (selectedActivityId !== selectedRowId) {
      setSelectedActivityId(selectedRowId);
    }
  }, [selectedActivityId, selectedRowId]);

  const rows = useMemo(
    () => activityRows.map((row) => ({ ...row, expanded: row.id === selectedRowId })),
    [activityRows, selectedRowId]
  );
  const selectedRow = rows.find((row) => row.id === selectedRowId) ?? null;
  const selectedDetail = useMemo(() => buildOverviewActivityDetail(selectedRow), [selectedRow]);

  return {
    rows,
    selectedRowId,
    selectedDetail,
    detailPanelId: OVERVIEW_ACTIVITY_DETAIL_PANEL_ID,
    detailPanelTitle: selectedDetail ? "Selected activity" : "No activity selected",
    detailPanelDescription: selectedDetail
      ? `${selectedDetail.title} from ${selectedDetail.subtitle}.`
      : "Select an event row to inspect event source, timestamp, and operator severity.",
    detailPanelEmptyText: activityRows.length > 0
      ? "Select an event row to inspect its triage detail."
      : "No recent events are available for this status snapshot.",
    detailPanelAriaLabel: selectedDetail?.ariaLabel ?? "Recent activity detail",
    tableLabel: activityRows.length === 1 ? "1 recent system event" : `${activityRows.length} recent system events`,
    tableCaption: "Recent system events. Select a row to update the activity detail panel.",
    selectActivity: setSelectedActivityId
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

export function buildOverviewPriorityRoutes(
  workspaces: OverviewWorkspaceLink[],
  current: SystemOverviewResponse | null = null
): OverviewPriorityRoute[] {
  const workspaceById = new Map(workspaces.map((workspace) => [workspace.id, workspace]));
  const priorityIds: OverviewPriorityRoute["id"][] = [];

  if (current && (current.providersTotal === 0 || current.providersOnline === 0)) {
    priorityIds.push("settings");
  }

  if (current && current.symbolsMonitored === 0) {
    priorityIds.push("data");
  }

  priorityIds.push("trading", "accounting", "reporting");

  return distinctPriorityIds(priorityIds)
    .map((id) => {
      const workspace = workspaceById.get(id);
      if (!workspace) {
        return null;
      }

      const copy = priorityRouteCopy[id];
      return {
        id,
        eyebrow: copy.eyebrow,
        title: copy.title,
        detail: priorityRouteDetail(id, current, copy.detail),
        buttonLabel: copy.buttonLabel,
        href: copy.href ?? workspace.href,
        status: workspace.status,
        badgeVariant: workspace.badgeVariant,
        description: workspace.description,
        ariaLabel: copy.ariaLabel ?? workspace.ariaLabel
      };
    })
    .filter((route): route is OverviewPriorityRoute => route !== null)
    .slice(0, 3);
}

export function buildOverviewValueBlockers(
  current: SystemOverviewResponse | null,
  refreshErrorText: string | null
): OverviewValueBlocker[] {
  const blockers: OverviewValueBlocker[] = [];

  if (refreshErrorText) {
    blockers.push(createOverviewValueBlocker({
      id: "refresh-failed",
      title: "Live status refresh failed",
      detail: refreshErrorText,
      href: "/settings#backend-capability-coverage",
      actionLabel: "Review diagnostics",
      badgeLabel: "Refresh",
      badgeVariant: "danger",
      tone: "danger"
    }));
  }

  if (!current) {
    blockers.push(createOverviewValueBlocker({
      id: "status-unavailable",
      title: "System posture is still loading",
      detail: "The workstation has not returned a status payload yet. If this remains blank, review backend capability coverage.",
      href: "/settings#backend-capability-coverage",
      actionLabel: "Review diagnostics",
      badgeLabel: "Waiting",
      badgeVariant: "warning",
      tone: "warning"
    }));

    return blockers;
  }

  if (current.systemStatus === "Offline") {
    blockers.push(createOverviewValueBlocker({
      id: "system-offline",
      title: "Workstation host is offline",
      detail: "Operator workflows cannot be trusted until the local host reports online status.",
      href: "/settings#backend-capability-coverage",
      actionLabel: "Review diagnostics",
      badgeLabel: "Offline",
      badgeVariant: "danger",
      tone: "danger"
    }));
  }

  if (current.providersTotal === 0) {
    blockers.push(createOverviewValueBlocker({
      id: "providers-missing",
      title: "No provider baseline configured",
      detail: "Connect a paper provider before expecting quotes, backfills, readiness checks, or paper orders to be useful.",
      href: "/settings#alpaca-provider-setup",
      actionLabel: "Connect provider",
      badgeLabel: "Setup",
      badgeVariant: "danger",
      tone: "danger"
    }));
  } else if (current.providersOnline === 0) {
    blockers.push(createOverviewValueBlocker({
      id: "providers-offline",
      title: "All configured providers are offline",
      detail: `${current.providersTotal} configured ${pluralize(current.providersTotal, "provider")} need connectivity before market-data workflows are useful.`,
      href: "/settings#alpaca-provider-setup",
      actionLabel: "Repair provider",
      badgeLabel: "Offline",
      badgeVariant: "danger",
      tone: "danger"
    }));
  } else if (current.providersOnline < current.providersTotal) {
    const offlineCount = current.providersTotal - current.providersOnline;
    blockers.push(createOverviewValueBlocker({
      id: "providers-degraded",
      title: "Provider baseline is degraded",
      detail: `${offlineCount} of ${current.providersTotal} ${pluralize(current.providersTotal, "provider")} are offline. Review provider posture before accepting readiness evidence.`,
      href: "/data/providers",
      actionLabel: "Review providers",
      badgeLabel: "Provider",
      badgeVariant: "warning",
      tone: "warning"
    }));
  }

  if (current.symbolsMonitored === 0) {
    blockers.push(createOverviewValueBlocker({
      id: "symbols-empty",
      title: "No monitored symbols",
      detail: "Seed a watchlist before validating quotes, historical data, backfills, or paper-trade tickets.",
      href: "/data/watchlist",
      actionLabel: "Seed watchlist",
      badgeLabel: "Symbols",
      badgeVariant: "warning",
      tone: "warning"
    }));
  }

  if (current.storageHealth === "Critical" || current.storageHealth === "Warning") {
    blockers.push(createOverviewValueBlocker({
      id: current.storageHealth === "Critical" ? "storage-critical" : "storage-warning",
      title: current.storageHealth === "Critical" ? "Storage evidence is critical" : "Storage evidence needs review",
      detail: current.storageHealth === "Critical"
        ? "Generated evidence and replay outputs are not safe to trust until storage health recovers."
        : "Storage is warning. Confirm evidence durability before relying on generated reports or replay packets.",
      href: "/settings#backend-capability-coverage",
      actionLabel: "Review storage",
      badgeLabel: "Storage",
      badgeVariant: current.storageHealth === "Critical" ? "danger" : "warning",
      tone: current.storageHealth === "Critical" ? "danger" : "warning"
    }));
  }

  if (current.activeBackfills > 0) {
    blockers.push(createOverviewValueBlocker({
      id: "backfills-active",
      title: "Backfill work is still running",
      detail: `${current.activeBackfills} active ${pluralize(current.activeBackfills, "backfill")} may change data coverage and downstream evidence.`,
      href: "/data/backfills",
      actionLabel: "Review backfills",
      badgeLabel: "Backfill",
      badgeVariant: "outline",
      tone: "default"
    }));
  }

  const recentEvents = Array.isArray(current.recentEvents) ? current.recentEvents : [];
  const latestError = recentEvents.find((event) => event.type === "error");
  if (latestError) {
    blockers.push(createOverviewValueBlocker({
      id: `event-${latestError.id}`,
      title: "Recent system error needs triage",
      detail: `${latestError.source.trim() || "Unknown source"}: ${latestError.message}`,
      href: "/settings#backend-capability-coverage",
      actionLabel: "Review diagnostics",
      badgeLabel: "Error",
      badgeVariant: "danger",
      tone: "danger"
    }));
  }

  return blockers.slice(0, 4);
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
    const ariaLabel = `${typeState.typeLabel} event from ${source} at ${timestampLabel}: ${event.message}`;

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
      ariaLabel,
      selectAriaLabel: `Inspect ${ariaLabel}`,
      detailPanelId: OVERVIEW_ACTIVITY_DETAIL_PANEL_ID,
      expanded: false
    };
  });
}

export function buildOverviewActivityDetail(
  row: OverviewActivityRow | null
): OverviewActivityDetail | null {
  if (!row) {
    return null;
  }

  return {
    eyebrow: `${row.typeLabel} event`,
    title: row.message,
    subtitle: row.source,
    description: overviewActivityDetailDescription[row.tone],
    badgeLabel: row.statusCode,
    badgeVariant: row.badgeVariant,
    ariaLabel: `Selected recent activity detail for ${row.typeLabel} event from ${row.source}`,
    fields: [
      { label: "Source", value: row.source },
      { label: "Timestamp", value: row.timestampLabel },
      { label: "Severity", value: row.typeLabel },
      { label: "Event ID", value: row.id }
    ]
  };
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
  const detailParts = current && providerSummary && storageLabel && lastHeartbeatLabel
    ? {
        providerSummary,
        storageLabel,
        storageClassName: storageHealthClassNames[current.storageHealth],
        lastHeartbeatLabel
      }
    : null;
  const isInterruptive = refreshErrorText !== null || current?.systemStatus === "Degraded" || current?.systemStatus === "Offline";
  const presentation = overviewStatusBannerPresentationFor(current, refreshErrorText);

  return {
    role: isInterruptive ? "alert" : "status",
    ariaLive: isInterruptive ? "assertive" : "polite",
    titleId: "overview-status-title",
    detailId: detailText ? "overview-status-detail" : null,
    label: statusLabel,
    detailText,
    detailParts,
    ariaLabel: `${statusLabel}. ${refreshErrorText ?? detailText}`,
    icon: presentation.icon,
    containerClassName: presentation.containerClassName,
    iconClassName: presentation.iconClassName,
    titleClassName: presentation.titleClassName
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

const storageHealthClassNames: Record<SystemOverviewResponse["storageHealth"], string> = {
  Healthy: "text-success",
  Warning: "text-warning",
  Critical: "text-danger"
};

type OverviewStatusBannerTone = "healthy" | "warning" | "danger" | "pending";

const overviewStatusBannerPresentation: Record<
  OverviewStatusBannerTone,
  Pick<OverviewStatusBannerState, "icon" | "containerClassName" | "iconClassName" | "titleClassName">
> = {
  healthy: {
    icon: "healthy",
    containerClassName: "border-success/30 bg-success/10",
    iconClassName: "text-success",
    titleClassName: "text-success"
  },
  warning: {
    icon: "warning",
    containerClassName: "border-warning/30 bg-warning/10",
    iconClassName: "text-warning",
    titleClassName: "text-warning"
  },
  danger: {
    icon: "offline",
    containerClassName: "border-danger/35 bg-danger/10",
    iconClassName: "text-danger",
    titleClassName: "text-danger"
  },
  pending: {
    icon: "pending",
    containerClassName: "border-border/70 bg-secondary/25",
    iconClassName: "text-muted-foreground",
    titleClassName: "text-foreground"
  }
};

function overviewStatusBannerPresentationFor(
  current: SystemOverviewResponse | null,
  refreshErrorText: string | null
) {
  if (refreshErrorText) {
    return overviewStatusBannerPresentation.danger;
  }

  if (!current) {
    return overviewStatusBannerPresentation.pending;
  }

  if (current.systemStatus === "Healthy") {
    return overviewStatusBannerPresentation.healthy;
  }

  if (current.systemStatus === "Offline") {
    return overviewStatusBannerPresentation.danger;
  }

  return overviewStatusBannerPresentation.warning;
}

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

const overviewActivityDetailDescription: Record<OverviewActivityTone, string> = {
  default: "Informational evidence is available for operator context.",
  warning: "Observation evidence needs review before treating the workstation posture as fully clear.",
  danger: "Error evidence needs triage before the related workflow can be trusted."
};

function resolveSelectedActivityId(
  rows: OverviewActivityRow[],
  selectedActivityId: string | null
): string | null {
  if (selectedActivityId && rows.some((row) => row.id === selectedActivityId)) {
    return selectedActivityId;
  }

  return rows[0]?.id ?? null;
}

const priorityRouteCopy: Record<
  OverviewPriorityRoute["id"],
  Pick<OverviewPriorityRoute, "eyebrow" | "title" | "detail" | "buttonLabel"> & Pick<Partial<OverviewPriorityRoute>, "href" | "ariaLabel">
> = {
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
  },
  data: {
    eyebrow: "First-run data setup",
    title: "Seed a working watchlist",
    detail: "No monitored symbols are loaded yet. Add a starter pack before validating quotes, backfills, or paper orders.",
    buttonLabel: "Open watchlist",
    href: "/data/watchlist",
    ariaLabel: "Open Data watchlist starter packs"
  },
  settings: {
    eyebrow: "Integration setup",
    title: "Connect provider baseline",
    detail: "Provider setup is blocking useful validation. Verify workstation setup before expecting quotes or readiness checks.",
    buttonLabel: "Open setup checks",
    href: "/settings#alpaca-provider-setup",
    ariaLabel: "Open Alpaca paper provider setup checklist"
  }
};

function createOverviewValueBlocker(
  blocker: Omit<OverviewValueBlocker, "ariaLabel">
): OverviewValueBlocker {
  return {
    ...blocker,
    ariaLabel: `${blocker.title}. ${blocker.detail} ${blocker.actionLabel}.`
  };
}

function buildOverviewValueBlockerSummary(blockers: OverviewValueBlocker[]): string {
  if (blockers.length === 0) {
    return "No immediate readiness blockers detected. Continue with the priority routes below.";
  }

  return blockers.length === 1
    ? "1 blocker needs attention before a confident operator handoff."
    : `${blockers.length} blockers need attention before a confident operator handoff.`;
}

function distinctPriorityIds(ids: OverviewPriorityRoute["id"][]): OverviewPriorityRoute["id"][] {
  return ids.filter((id, index) => ids.indexOf(id) === index);
}

function priorityRouteDetail(
  id: OverviewPriorityRoute["id"],
  current: SystemOverviewResponse | null,
  fallback: string
): string {
  if (!current) {
    return fallback;
  }

  if (id === "settings") {
    if (current.providersTotal === 0) {
      return "No providers are configured yet. Verify setup before expecting quotes, backfills, or cockpit readiness.";
    }

    return `0 of ${current.providersTotal} providers are online. Restore provider connectivity before running operator validation.`;
  }

  if (id === "data") {
    return "No monitored symbols are loaded yet. Add a starter pack before validating quotes, backfills, or paper orders.";
  }

  return fallback;
}

function buildFallbackStats(current: SystemOverviewResponse | null): MetricSnapshot[] {
  return [
    {
      id: "providers",
      label: "Providers Online",
      value: current ? `${current.providersOnline} / ${current.providersTotal}` : "-",
      delta: current ? providerFallbackDelta(current) : "Awaiting API",
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
      delta: current ? activeCountDelta(current.activeRuns, "run", "runs") : "Awaiting API",
      tone: current && current.activeRuns > 0 ? "success" : "default"
    },
    {
      id: "symbols",
      label: "Monitored Symbols",
      value: current ? String(current.symbolsMonitored) : "-",
      delta: current ? activeCountDelta(current.symbolsMonitored, "symbol", "symbols") : "Awaiting API",
      tone: "default"
    },
    {
      id: "backfills",
      label: "Active Backfills",
      value: current ? String(current.activeBackfills) : "-",
      delta: current ? activeCountDelta(current.activeBackfills, "backfill", "backfills") : "Awaiting API",
      tone: current && current.activeBackfills > 0 ? "warning" : "default"
    }
  ];
}

function providerFallbackDelta(current: SystemOverviewResponse): string {
  const offline = Math.max(current.providersTotal - current.providersOnline, 0);
  if (current.providersTotal === 0) {
    return "No providers configured";
  }

  return offline === 0 ? "All online" : `${offline} offline`;
}

function activeCountDelta(count: number, singular: string, plural: string): string {
  if (count === 0) {
    return `No active ${plural}`;
  }

  return count === 1 ? `1 active ${singular}` : `${count} active ${plural}`;
}

function pluralize(count: number, singular: string, plural = `${singular}s`): string {
  return count === 1 ? singular : plural;
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

function formatTime(value: string | Date | null | undefined): string {
  if (!value) {
    return "Unavailable";
  }

  const date = typeof value === "string" ? new Date(value) : value;
  return Number.isNaN(date.getTime()) ? "Unavailable" : formatUtcMinute(date);
}

function formatUtcMinute(date: Date): string {
  return `${UTC_MONTH_LABELS[date.getUTCMonth()]} ${date.getUTCDate()}, ${padUtc(date.getUTCHours())}:${padUtc(date.getUTCMinutes())} UTC`;
}

const UTC_MONTH_LABELS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

function padUtc(value: number): string {
  return String(value).padStart(2, "0");
}

// --- Portfolio at-a-glance panel ---

export type PortfolioPanelTone = "default" | "success" | "warning" | "danger";

export interface OverviewPositionRow {
  key: string;
  symbol: string;
  side: "Long" | "Short";
  quantity: string;
  markPrice: string;
  unrealizedPnl: string;
  pnlTone: PortfolioPanelTone;
  exposure: string;
  ariaLabel: string;
}

export interface OverviewPortfolioEmptyAction {
  href: string;
  label: string;
  ariaLabel: string;
}

export interface OverviewPortfolioPanel {
  hasData: boolean;
  metrics: MetricSnapshot[];
  positions: OverviewPositionRow[];
  riskState: string;
  riskTone: PortfolioPanelTone;
  riskSummary: string;
  brokerageLabel: string;
  openOrderCount: number;
  emptyMessage: string;
  emptyAction: OverviewPortfolioEmptyAction;
}

export function buildOverviewPortfolioPanel(
  trading: TradingWorkspaceResponse | null,
  portfolio: PortfolioWorkspaceResponse | null
): OverviewPortfolioPanel {
  const source = trading ?? portfolio;

  if (!source) {
    return {
      hasData: false,
      metrics: [],
      positions: [],
      riskState: "—",
      riskTone: "default",
      riskSummary: "",
      brokerageLabel: "—",
      openOrderCount: 0,
      emptyMessage: "Connect a brokerage account or start a paper session to see your portfolio here.",
      emptyAction: {
        href: "/settings#alpaca-provider-setup",
        label: "Connect provider",
        ariaLabel: "Open Alpaca paper provider setup checklist from the empty portfolio panel"
      }
    };
  }

  const tradingMetrics = trading?.metrics ?? [];
  const portfolioMetrics = portfolio?.metrics ?? tradingMetrics;

  const pnlMetric = tradingMetrics.find((m) => m.id === "pnl") ?? portfolioMetrics.find((m) => m.id === "portfolio-equity");
  const equityMetric = portfolioMetrics.find((m) => m.id === "portfolio-equity");
  const cashMetric = portfolioMetrics.find((m) => m.id === "portfolio-cash");
  const ordersMetric = tradingMetrics.find((m) => m.id === "orders");

  const metrics: MetricSnapshot[] = [
    pnlMetric
      ? { ...pnlMetric, id: "overview-pnl", label: "Net P&L" }
      : { id: "overview-pnl", label: "Net P&L", value: "—", delta: "Unavailable", tone: "default" },
    equityMetric
      ? { ...equityMetric, id: "overview-equity", label: "Portfolio Equity" }
      : { id: "overview-equity", label: "Portfolio Equity", value: "—", delta: "Unavailable", tone: "default" },
    cashMetric
      ? { ...cashMetric, id: "overview-cash", label: "Cash" }
      : { id: "overview-cash", label: "Cash", value: "—", delta: "Unavailable", tone: "default" },
    ordersMetric
      ? { ...ordersMetric, id: "overview-orders", label: "Open Orders" }
      : {
          id: "overview-orders",
          label: "Open Orders",
          value: String(trading?.openOrders.length ?? 0),
          delta: activeCountDelta(trading?.openOrders.length ?? 0, "order", "orders"),
          tone: "default"
        }
  ];

  const riskState = source.risk?.state ?? "—";
  const riskTone: PortfolioPanelTone =
    riskState === "Constrained" ? "danger" : riskState === "Observe" ? "warning" : riskState === "Healthy" ? "success" : "default";

  const positions: OverviewPositionRow[] = (source.positions ?? [])
    .filter((pos) => pos.symbol !== "—")
    .slice(0, 5)
    .map((pos) => {
      const pnlStr = pos.unrealizedPnl ?? "";
      const pnlTone: PortfolioPanelTone = pnlStr.startsWith("+") ? "success" : pnlStr.startsWith("-") ? "danger" : "default";
      return {
        key: pos.positionKey ?? pos.symbol,
        symbol: pos.symbol,
        side: pos.side,
        quantity: pos.quantity,
        markPrice: pos.markPrice,
        unrealizedPnl: pos.unrealizedPnl,
        pnlTone,
        exposure: pos.exposure,
        ariaLabel: `${pos.symbol} ${pos.side} ${pos.quantity} shares, mark ${pos.markPrice}, unrealized P&L ${pos.unrealizedPnl}`
      };
    });

  const brokerage = source.brokerage;
  const brokerageLabel = brokerage
    ? `${brokerage.provider} · ${brokerage.account} · ${brokerage.environment}`
    : "—";

  return {
    hasData: true,
    metrics,
    positions,
    riskState,
    riskTone,
    riskSummary: source.risk?.summary ?? "",
    brokerageLabel,
    openOrderCount: trading?.openOrders.length ?? 0,
    emptyMessage: "No open positions. Use the trading cockpit to place your first order.",
    emptyAction: {
      href: "/trading",
      label: "Open trading cockpit",
      ariaLabel: "Open Trading cockpit from the empty portfolio positions panel"
    }
  };
}
