import type {
  DataOperationsWorkspaceResponse,
  GovernanceWorkspaceResponse,
  ResearchWorkspaceResponse,
  SessionInfo,
  SystemOverviewResponse,
  TradingWorkspaceResponse,
  WorkspaceKey
} from "@/types";

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
  statusLabel: string;
  statusDetail: string;
  tone: "default" | "success" | "warning" | "danger";
  badgeVariant: "default" | "success" | "warning" | "danger" | "outline";
  isLoading: boolean;
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
  countLabel: string;
  statusLabel: string;
  statusDetail: string;
  state: "ready" | "empty" | "unavailable";
  rows: SettingsEventRow[];
}

export interface SettingsDiagnosticCounts {
  loadedLabel: string;
  failedLabel: string;
  checkingLabel: string;
  loaded: number;
  failed: number;
  checking: number;
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
  diagnosticCounts: SettingsDiagnosticCounts;
  diagnosticSummary: string;
  diagnosticListLabel: string;
  diagnosticStatusLabel: string;
  diagnosticStatusVariant: "default" | "success" | "warning" | "danger" | "outline";
}

export interface SettingsScreenPayload {
  session: SessionInfo | null;
  overview: SystemOverviewResponse | null;
  research?: ResearchWorkspaceResponse | null;
  trading?: TradingWorkspaceResponse | null;
  dataOperations?: DataOperationsWorkspaceResponse | null;
  governance?: GovernanceWorkspaceResponse | null;
  reporting?: GovernanceWorkspaceResponse | null;
  loading?: boolean;
  error?: string | null;
  workspaceErrors?: Partial<Record<WorkspaceKey, string>>;
}

interface DiagnosticEndpointDefinition {
  id: string;
  label: string;
  href: string;
  description: string;
  ariaLabel: string;
  workspaceKey?: WorkspaceKey;
  isAvailable: (payload: SettingsScreenPayload) => boolean;
  unavailableDetail: string;
}

const DIAGNOSTIC_ENDPOINTS: DiagnosticEndpointDefinition[] = [
  {
    id: "system-overview",
    label: "System overview",
    href: "/api/status",
    description: "System health, provider counts, and active run summary.",
    ariaLabel: "Open System overview diagnostic endpoint",
    isAvailable: (payload) => payload.overview !== null,
    unavailableDetail: "System overview has not loaded in this workstation session."
  },
  {
    id: "session-info",
    label: "Session info",
    href: "/api/workstation/session",
    description: "Current operator session context and environment.",
    ariaLabel: "Open Session info diagnostic endpoint",
    isAvailable: (payload) => payload.session !== null,
    unavailableDetail: "Operator session context has not loaded."
  },
  {
    id: "providers",
    label: "Providers",
    href: "/api/data/providers",
    description: "All registered market data provider statuses.",
    ariaLabel: "Open Providers diagnostic endpoint",
    workspaceKey: "data",
    isAvailable: (payload) => payload.dataOperations !== null && payload.dataOperations !== undefined,
    unavailableDetail: "Data workspace provider posture has not loaded."
  },
  {
    id: "research-runs",
    label: "Research runs",
    href: "/api/research/runs",
    description: "Active and completed strategy runs.",
    ariaLabel: "Open Research runs diagnostic endpoint",
    workspaceKey: "strategy",
    isAvailable: (payload) => payload.research !== null && payload.research !== undefined,
    unavailableDetail: "Strategy run payload has not loaded."
  },
  {
    id: "trading-workspace",
    label: "Trading workspace",
    href: "/api/workstation/trading",
    description: "Live trading positions, orders, fills, and risk.",
    ariaLabel: "Open Trading workspace diagnostic endpoint",
    workspaceKey: "trading",
    isAvailable: (payload) => payload.trading !== null && payload.trading !== undefined,
    unavailableDetail: "Trading workspace payload has not loaded."
  },
  {
    id: "accounting-workspace",
    label: "Accounting workspace",
    href: "/api/workstation/accounting",
    description: "Reconciliation queue, cash flow, and accounting evidence.",
    ariaLabel: "Open Accounting workspace diagnostic endpoint",
    workspaceKey: "accounting",
    isAvailable: (payload) => payload.governance !== null && payload.governance !== undefined,
    unavailableDetail: "Accounting workspace payload has not loaded."
  },
  {
    id: "reporting-workspace",
    label: "Reporting workspace",
    href: "/api/workstation/reporting",
    description: "Reporting profiles and governed report-pack targets.",
    ariaLabel: "Open Reporting workspace diagnostic endpoint",
    workspaceKey: "reporting",
    isAvailable: (payload) => payload.reporting !== null && payload.reporting !== undefined,
    unavailableDetail: "Reporting workspace payload has not loaded."
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
      countLabel: "0",
      statusLabel: "Event stream unavailable",
      statusDetail: "System overview is unavailable. Reconnect to the Meridian API before reviewing event posture.",
      state: "unavailable",
      rows: []
    };
  }

  const events = overview.recentEvents ?? [];
  const rows = events.map((event) => {
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
      countLabel: "0",
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
    countLabel: String(rows.length),
    statusLabel: rows.length === 1 ? "1 event reported" : `${rows.length} events reported`,
    statusDetail: "Latest workstation events remain visible with source and timestamp evidence.",
    state: "ready",
    rows
  };
}

export function buildSettingsScreenViewModel(payload: SettingsScreenPayload): SettingsScreenViewModel;
export function buildSettingsScreenViewModel(
  session: SessionInfo | null,
  overview: SystemOverviewResponse | null
): SettingsScreenViewModel;
export function buildSettingsScreenViewModel(
  payloadOrSession: SettingsScreenPayload | SessionInfo | null,
  overviewArg?: SystemOverviewResponse | null
): SettingsScreenViewModel {
  const payload: SettingsScreenPayload = isSettingsScreenPayload(payloadOrSession)
    ? payloadOrSession
    : {
        session: payloadOrSession,
        overview: overviewArg ?? null
      };
  const { session, overview } = payload;
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
    ...buildDiagnosticEndpointSection(payload)
  };
}

function isSettingsScreenPayload(value: SettingsScreenPayload | SessionInfo | null): value is SettingsScreenPayload {
  return value !== null && "session" in value && "overview" in value;
}

function buildDiagnosticEndpointSection(payload: SettingsScreenPayload): Pick<
  SettingsScreenViewModel,
  "diagnosticLinks" | "diagnosticSummary" | "diagnosticListLabel" | "diagnosticStatusLabel" | "diagnosticStatusVariant"
  | "diagnosticCounts"
> {
  const diagnosticLinks = DIAGNOSTIC_ENDPOINTS.map((endpoint) => buildDiagnosticLink(endpoint, payload));
  const counts = buildDiagnosticCounts(diagnosticLinks);

  const diagnosticStatusLabel = counts.checking > 0
    ? `${counts.checking} checking`
    : counts.failed > 0
      ? `${counts.failed} unavailable`
      : "All reachable";

  return {
    diagnosticLinks,
    diagnosticCounts: counts,
    diagnosticSummary: counts.checking > 0
      ? `Checking ${counts.checking} diagnostic endpoint${counts.checking === 1 ? "" : "s"}; ${counts.loaded} already loaded.`
      : counts.failed > 0
        ? `${counts.failed} diagnostic endpoint${counts.failed === 1 ? "" : "s"} failed to load in the workstation bootstrap. Open the endpoint card for raw API evidence.`
        : "All diagnostic endpoint payloads represented on this page are loaded.",
    diagnosticListLabel: "Diagnostic endpoint availability",
    diagnosticStatusLabel,
    diagnosticStatusVariant: counts.checking > 0 ? "warning" : counts.failed > 0 ? "danger" : "success"
  };
}

function buildDiagnosticCounts(links: SettingsDiagnosticLink[]): SettingsDiagnosticCounts {
  const loaded = links.filter((link) => link.tone === "success").length;
  const failed = links.filter((link) => link.tone === "danger").length;
  const checking = links.filter((link) => link.isLoading).length;

  return {
    loaded,
    failed,
    checking,
    loadedLabel: String(loaded),
    failedLabel: String(failed),
    checkingLabel: String(checking)
  };
}

function buildDiagnosticLink(
  endpoint: DiagnosticEndpointDefinition,
  payload: SettingsScreenPayload
): SettingsDiagnosticLink {
  const error = endpoint.workspaceKey ? payload.workspaceErrors?.[endpoint.workspaceKey] : null;
  const isLoading = payload.loading === true;

  if (isLoading) {
    return {
      ...endpoint,
      statusLabel: "Checking",
      statusDetail: "Workstation bootstrap is refreshing this diagnostic payload.",
      tone: "warning",
      badgeVariant: "warning",
      isLoading
    };
  }

  if (error) {
    return {
      ...endpoint,
      statusLabel: "Failed",
      statusDetail: error,
      tone: "danger",
      badgeVariant: "danger",
      isLoading: false
    };
  }

  if (endpoint.isAvailable(payload)) {
    return {
      ...endpoint,
      statusLabel: "Loaded",
      statusDetail: "Payload is represented in the current workstation view model.",
      tone: "success",
      badgeVariant: "success",
      isLoading: false
    };
  }

  return {
    ...endpoint,
    statusLabel: "Unavailable",
    statusDetail: payload.error ?? endpoint.unavailableDetail,
    tone: "danger",
    badgeVariant: "danger",
    isLoading: false
  };
}
