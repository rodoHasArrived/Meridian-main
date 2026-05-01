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
}

export interface SettingsEventRow {
  id: string;
  type: "info" | "warning" | "error";
  message: string;
  source: string;
  timestamp: string;
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
  recentEvents: SettingsEventRow[];
  hasEvents: boolean;
  diagnosticLinks: SettingsDiagnosticLink[];
}

const DIAGNOSTIC_LINKS: SettingsDiagnosticLink[] = [
  { label: "System overview", href: "/api/workstation/overview", description: "System health, provider counts, and active run summary." },
  { label: "Session info", href: "/api/workstation/session", description: "Current operator session context and environment." },
  { label: "Providers", href: "/api/data/providers", description: "All registered market data provider statuses." },
  { label: "Research runs", href: "/api/research/runs", description: "Active and completed strategy runs." },
  { label: "Trading workspace", href: "/api/workstation/trading", description: "Live trading positions, orders, fills, and risk." },
  { label: "Governance workspace", href: "/api/workstation/governance", description: "Reconciliation queue, cash flow, and reporting profiles." }
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

  const recentEvents: SettingsEventRow[] = (overview?.recentEvents ?? []).map((e) => ({
    id: e.id,
    type: e.type,
    message: e.message,
    source: e.source,
    timestamp: e.timestamp
  }));

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
    recentEvents,
    hasEvents: recentEvents.length > 0,
    diagnosticLinks: DIAGNOSTIC_LINKS
  };
}
