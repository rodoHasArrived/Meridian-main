import type { SessionInfo, SystemOverviewResponse } from "@/types";

export interface SettingsSessionItem {
  label: string;
  value: string;
  tone: "default" | "success" | "warning" | "muted";
}

export interface SettingsSystemItem {
  label: string;
  value: string;
  tone: "default" | "success" | "warning" | "danger" | "muted";
}

export interface SettingsDiagnosticLink {
  label: string;
  href: string;
  description: string;
  ariaLabel: string;
}

export interface SettingsEventRow {
  id: string;
  type: "info" | "warning" | "error";
  statusCode: string;
  badgeVariant: "default" | "warning" | "danger";
  tone: "default" | "warning" | "danger";
  message: string;
  source: string;
  timestamp: string;
  ariaLabel: string;
}

export interface SettingsRecentEventsSection {
  title: string;
  description: string;
  listLabel: string;
  statusLabel: string;
  statusDetail: string;
  state: "ready" | "empty" | "unavailable";
  rows: SettingsEventRow[];
}

export interface SettingsScreenViewModel {
  sessionTitle: string;
  sessionItems: SettingsSessionItem[];
  hasSession: boolean;
  systemTitle: string;
  systemSummary: string;
  systemTone: "default" | "success" | "warning" | "danger";
  systemItems: SettingsSystemItem[];
  hasOverview: boolean;
  recentEventsSection: SettingsRecentEventsSection;
  diagnosticLinks: SettingsDiagnosticLink[];
}

const DIAGNOSTIC_LINKS: SettingsDiagnosticLink[] = [
  {
    label: "System overview",
    href: "/api/workstation/overview",
    description: "System health, provider counts, and active run summary.",
    ariaLabel: "Open System overview diagnostic endpoint"
  },
  {
    label: "Session info",
    href: "/api/workstation/session",
    description: "Current operator session context and environment.",
    ariaLabel: "Open Session info diagnostic endpoint"
  },
  {
    label: "Providers",
    href: "/api/data/providers",
    description: "All registered market data provider statuses.",
    ariaLabel: "Open Providers diagnostic endpoint"
  },
  {
    label: "Research runs",
    href: "/api/research/runs",
    description: "Active and completed strategy runs.",
    ariaLabel: "Open Research runs diagnostic endpoint"
  },
  {
    label: "Trading workspace",
    href: "/api/workstation/trading",
    description: "Live trading positions, orders, fills, and risk.",
    ariaLabel: "Open Trading workspace diagnostic endpoint"
  },
  {
    label: "Accounting workspace",
    href: "/api/workstation/accounting",
    description: "Reconciliation queue, cash flow, and accounting evidence.",
    ariaLabel: "Open Accounting workspace diagnostic endpoint"
  },
  {
    label: "Reporting workspace",
    href: "/api/workstation/reporting",
    description: "Reporting profiles and governed report-pack targets.",
    ariaLabel: "Open Reporting workspace diagnostic endpoint"
  }
];

function systemTone(status: SystemOverviewResponse["systemStatus"]): SettingsScreenViewModel["systemTone"] {
  if (status === "Healthy") return "success";
  if (status === "Degraded") return "warning";
  if (status === "Offline") return "danger";
  return "default";
}

function storageTone(health: SystemOverviewResponse["storageHealth"]): SettingsSystemItem["tone"] {
  if (health === "Healthy") return "success";
  if (health === "Warning") return "warning";
  if (health === "Critical") return "danger";
  return "default";
}

function eventBadgeVariant(type: SettingsEventRow["type"]): SettingsEventRow["badgeVariant"] {
  if (type === "error") return "danger";
  if (type === "warning") return "warning";
  return "default";
}

function eventStatusCode(type: SettingsEventRow["type"]): string {
  if (type === "error") return "CRIT";
  if (type === "warning") return "OBS";
  return "INFO";
}

function eventTone(type: SettingsEventRow["type"]): SettingsEventRow["tone"] {
  if (type === "error") return "danger";
  if (type === "warning") return "warning";
  return "default";
}

function buildRecentEventsSection(overview: SystemOverviewResponse | null): SettingsRecentEventsSection {
  if (!overview) {
    return {
      title: "Recent events",
      description: "System events from the active session. Check source subsystems for detail.",
      listLabel: "Recent system events unavailable",
      statusLabel: "Event stream unavailable",
      statusDetail: "System overview is unavailable. Reconnect to the Meridian API before reviewing event posture.",
      state: "unavailable",
      rows: []
    };
  }

  const rows = overview.recentEvents.map((event) => {
    const source = event.source.trim() || "Unknown source";
    const timestamp = event.timestamp.trim() || "Timestamp unavailable";
    const statusCode = eventStatusCode(event.type);

    return {
      id: event.id,
      type: event.type,
      statusCode,
      badgeVariant: eventBadgeVariant(event.type),
      tone: eventTone(event.type),
      message: event.message.trim() || "Event detail unavailable.",
      source,
      timestamp,
      ariaLabel: `${statusCode} event from ${source} at ${timestamp}. ${event.message.trim() || "Event detail unavailable."}`
    };
  });

  if (rows.length === 0) {
    return {
      title: "Recent events",
      description: "System events from the active session. Check source subsystems for detail.",
      listLabel: "No recent system events",
      statusLabel: "No recent events",
      statusDetail: "No system events reported for the active session. Diagnostic endpoints remain available below.",
      state: "empty",
      rows
    };
  }

  return {
    title: "Recent events",
    description: "System events from the active session. Check source subsystems for detail.",
    listLabel: rows.length === 1 ? "1 recent system event" : `${rows.length} recent system events`,
    statusLabel: rows.length === 1 ? "1 event reported" : `${rows.length} events reported`,
    statusDetail: "Latest workstation events remain visible with source and timestamp evidence.",
    state: "ready",
    rows
  };
}

export function buildSettingsScreenViewModel(
  session: SessionInfo | null,
  overview: SystemOverviewResponse | null
): SettingsScreenViewModel {
  const sessionItems: SettingsSessionItem[] = session
    ? [
        { label: "Display name", value: session.displayName, tone: "default" },
        { label: "Role", value: session.role, tone: "default" },
        { label: "Environment", value: session.environment, tone: session.environment === "live" ? "warning" : "default" },
        { label: "Active workspace", value: session.activeWorkspace, tone: "muted" },
        { label: "Commands issued", value: String(session.commandCount), tone: "muted" }
      ]
    : [];

  const systemItems: SettingsSystemItem[] = overview
    ? [
        { label: "Status", value: overview.systemStatus, tone: systemTone(overview.systemStatus) },
        { label: "Providers online", value: `${overview.providersOnline} / ${overview.providersTotal}`, tone: overview.providersOnline === overview.providersTotal ? "success" : "warning" },
        { label: "Active runs", value: String(overview.activeRuns), tone: "default" },
        { label: "Open positions", value: String(overview.openPositions), tone: "default" },
        { label: "Symbols monitored", value: String(overview.symbolsMonitored), tone: "default" },
        { label: "Active backfills", value: String(overview.activeBackfills), tone: "muted" },
        { label: "Storage health", value: overview.storageHealth, tone: storageTone(overview.storageHealth) },
        { label: "Last heartbeat", value: overview.lastHeartbeatUtc, tone: "muted" }
      ]
    : [];

  const sysTone = overview ? systemTone(overview.systemStatus) : "default";
  const sysSummary = overview
    ? `${overview.systemStatus} · ${overview.providersOnline}/${overview.providersTotal} providers · ${overview.activeRuns} active run${overview.activeRuns === 1 ? "" : "s"}`
    : "System overview unavailable.";

  return {
    sessionTitle: session ? `Session — ${session.displayName}` : "Session",
    sessionItems,
    hasSession: session !== null,
    systemTitle: "System posture",
    systemSummary: sysSummary,
    systemTone: sysTone,
    systemItems,
    hasOverview: overview !== null,
    recentEventsSection: buildRecentEventsSection(overview),
    diagnosticLinks: DIAGNOSTIC_LINKS
  };
}
