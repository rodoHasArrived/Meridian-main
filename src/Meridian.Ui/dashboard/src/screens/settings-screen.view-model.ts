import { useState, type FormEvent } from "react";
import { connectAlpacaConnection, revokeAlpacaConnection } from "@/lib/api";
import type {
  AlpacaBrokerageConnectionRequest,
  BrokerageConnectionStatus,
  DataOperationsWorkspaceResponse,
  GovernanceWorkspaceResponse,
  ResearchWorkspaceResponse,
  SessionInfo,
  SystemOverviewResponse,
  TradingWorkspaceResponse,
  WorkspaceKey
} from "@/types";

type AlpacaEnvironment = AlpacaBrokerageConnectionRequest["environment"];

export interface SettingsAlpacaConnectionFormState {
  keyId: string;
  secretKey: string;
  environment: AlpacaEnvironment;
  busyAction: "connect" | "clear" | null;
  submitted: boolean;
  actionMessage: string | null;
  actionTone: "default" | "success" | "danger";
}

export interface SettingsAlpacaConnectionCommandState {
  keyIdError: boolean;
  secretKeyError: boolean;
  canSubmit: boolean;
  canEdit: boolean;
  submitBusy: boolean;
  clearBusy: boolean;
  submitDisabledReason: string | null;
  clearDisabledReason: string | null;
  statusRole: "status" | "alert";
  statusClassName: string;
  keyIdHelpText: string;
  secretKeyHelpText: string;
}

export interface SettingsAlpacaConnectionFormViewModel extends SettingsAlpacaConnectionFormState, SettingsAlpacaConnectionCommandState {
  setKeyId: (value: string) => void;
  setSecretKey: (value: string) => void;
  setEnvironment: (value: AlpacaEnvironment) => void;
  connect: (event: FormEvent<HTMLFormElement>) => Promise<void>;
  clear: () => Promise<void>;
}

interface SettingsAlpacaConnectionDependencies {
  connectConnection?: (request: AlpacaBrokerageConnectionRequest) => Promise<BrokerageConnectionStatus>;
  revokeConnection?: () => Promise<BrokerageConnectionStatus>;
}

const emptyAlpacaConnectionForm: SettingsAlpacaConnectionFormState = {
  keyId: "",
  secretKey: "",
  environment: "paper",
  busyAction: null,
  submitted: false,
  actionMessage: null,
  actionTone: "default"
};

export function buildAlpacaConnectionCommandState({
  form,
  canClear
}: {
  form: SettingsAlpacaConnectionFormState;
  canClear: boolean;
}): SettingsAlpacaConnectionCommandState {
  const keyIdMissing = form.keyId.trim().length === 0;
  const secretKeyMissing = form.secretKey.trim().length === 0;
  const hasValidationErrors = keyIdMissing || secretKeyMissing;
  const busy = form.busyAction !== null;
  const keyIdError = form.submitted && keyIdMissing;
  const secretKeyError = form.submitted && secretKeyMissing;

  return {
    keyIdError,
    secretKeyError,
    canSubmit: !busy && !hasValidationErrors,
    canEdit: !busy,
    submitBusy: form.busyAction === "connect",
    clearBusy: form.busyAction === "clear",
    submitDisabledReason: busy
      ? "Alpaca credential request is already running."
      : keyIdMissing
        ? "Enter an Alpaca key ID before testing the connection."
        : secretKeyMissing
          ? "Enter an Alpaca secret key before testing the connection."
          : null,
    clearDisabledReason: busy
      ? "Alpaca credential request is already running."
      : canClear
        ? null
        : "No stored Alpaca credentials are available to clear.",
    statusRole: form.actionTone === "danger" ? "alert" : "status",
    statusClassName: form.actionTone === "danger" ? "text-sm text-danger" : "text-sm text-muted-foreground",
    keyIdHelpText: keyIdError ? "Key ID is required before Meridian can test the Alpaca account." : "Stored values remain masked after refresh.",
    secretKeyHelpText: secretKeyError ? "Secret key is required and is cleared after a connection test." : "Secret key is never displayed after submit."
  };
}

export function useAlpacaConnectionFormViewModel({
  onRefresh,
  canClear,
  connectConnection = connectAlpacaConnection,
  revokeConnection = revokeAlpacaConnection
}: {
  onRefresh?: () => Promise<void> | void;
  canClear: boolean;
} & SettingsAlpacaConnectionDependencies): SettingsAlpacaConnectionFormViewModel {
  const [form, setForm] = useState<SettingsAlpacaConnectionFormState>(emptyAlpacaConnectionForm);
  const command = buildAlpacaConnectionCommandState({ form, canClear });

  const setKeyId = (keyId: string) => {
    setForm((current) => ({ ...current, keyId, actionMessage: null, actionTone: "default" }));
  };

  const setSecretKey = (secretKey: string) => {
    setForm((current) => ({ ...current, secretKey, actionMessage: null, actionTone: "default" }));
  };

  const setEnvironment = (environment: AlpacaEnvironment) => {
    setForm((current) => ({ ...current, environment, actionMessage: null, actionTone: "default" }));
  };

  const connect = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();

    const submittedForm = { ...form, submitted: true, actionMessage: null, actionTone: "default" as const };
    const submittedCommand = buildAlpacaConnectionCommandState({ form: submittedForm, canClear });
    if (!submittedCommand.canSubmit) {
      setForm(submittedForm);
      return;
    }

    setForm({ ...submittedForm, busyAction: "connect" });

    try {
      const status = await connectConnection({
        keyId: form.keyId.trim(),
        secretKey: form.secretKey.trim(),
        environment: form.environment
      });
      await onRefresh?.();
      setForm((current) => ({
        ...current,
        secretKey: "",
        busyAction: null,
        submitted: false,
        actionMessage: status.isConnected
          ? "Alpaca account verified."
          : status.lastError ?? status.warnings[0] ?? "Alpaca connection updated.",
        actionTone: status.isConnected ? "success" : "danger"
      }));
    } catch (err) {
      setForm((current) => ({
        ...current,
        busyAction: null,
        actionMessage: err instanceof Error ? err.message : "Alpaca connection request failed.",
        actionTone: "danger"
      }));
    }
  };

  const clear = async () => {
    const currentCommand = buildAlpacaConnectionCommandState({ form, canClear });
    if (currentCommand.clearDisabledReason) {
      return;
    }

    setForm((current) => ({ ...current, busyAction: "clear", actionMessage: null, actionTone: "default" }));

    try {
      await revokeConnection();
      await onRefresh?.();
      setForm({
        ...emptyAlpacaConnectionForm,
        actionMessage: "Alpaca credentials cleared.",
        actionTone: "success"
      });
    } catch (err) {
      setForm((current) => ({
        ...current,
        busyAction: null,
        actionMessage: err instanceof Error ? err.message : "Alpaca clear request failed.",
        actionTone: "danger"
      }));
    }
  };

  return {
    ...form,
    ...command,
    setKeyId,
    setSecretKey,
    setEnvironment,
    connect,
    clear
  };
}

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

export interface SettingsBackendCapabilityEndpoint {
  id: string;
  method: "GET" | "POST" | "PUT" | "DELETE";
  label: string;
  href: string;
  ariaLabel: string;
}

export interface SettingsBackendCapabilityGroup {
  id: string;
  workspaceLabel: string;
  route: string;
  title: string;
  description: string;
  endpointCountLabel: string;
  loadedCountLabel: string;
  statusLabel: string;
  statusDetail: string;
  statusVariant: "success" | "warning" | "danger" | "outline";
  endpoints: SettingsBackendCapabilityEndpoint[];
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

export interface SettingsAlpacaConnectionPanel {
  providerLabel: string;
  stateLabel: string;
  statusDetail: string;
  statusTone: "default" | "success" | "warning" | "danger";
  badgeVariant: "outline" | "success" | "warning" | "danger";
  environmentLabel: string;
  accountLabel: string;
  maskedKeyIdLabel: string;
  verifiedAtLabel: string;
  warnings: string[];
  canClear: boolean;
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
  alpacaConnectionPanel: SettingsAlpacaConnectionPanel;
  diagnosticLinks: SettingsDiagnosticLink[];
  diagnosticCounts: SettingsDiagnosticCounts;
  diagnosticSummary: string;
  diagnosticListLabel: string;
  diagnosticStatusLabel: string;
  diagnosticStatusVariant: "default" | "success" | "warning" | "danger" | "outline";
  backendCapabilityGroups: SettingsBackendCapabilityGroup[];
  backendCapabilitySummary: string;
  backendCapabilityListLabel: string;
  backendCapabilityStatusLabel: string;
  backendCapabilityStatusVariant: "default" | "success" | "warning" | "danger" | "outline";
}

export interface SettingsScreenPayload {
  session: SessionInfo | null;
  overview: SystemOverviewResponse | null;
  research?: ResearchWorkspaceResponse | null;
  trading?: TradingWorkspaceResponse | null;
  dataOperations?: DataOperationsWorkspaceResponse | null;
  governance?: GovernanceWorkspaceResponse | null;
  reporting?: GovernanceWorkspaceResponse | null;
  brokerageConnection?: BrokerageConnectionStatus | null;
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

interface CapabilityEndpointDefinition {
  id: string;
  method: SettingsBackendCapabilityEndpoint["method"];
  label: string;
  href: string;
}

interface BackendCapabilityDefinition {
  id: string;
  workspaceKey: WorkspaceKey;
  workspaceLabel: string;
  route: string;
  title: string;
  description: string;
  endpoints: CapabilityEndpointDefinition[];
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
    id: "data-workspace",
    label: "Data workspace",
    href: "/api/workstation/data",
    description: "Provider posture, backfill queues, and export readiness.",
    ariaLabel: "Open Data workspace diagnostic endpoint",
    workspaceKey: "data",
    isAvailable: (payload) => payload.dataOperations !== null && payload.dataOperations !== undefined,
    unavailableDetail: "Data workspace provider posture has not loaded."
  },
  {
    id: "strategy-workspace",
    label: "Strategy workspace",
    href: "/api/workstation/strategy",
    description: "Strategy run metrics and active run rows.",
    ariaLabel: "Open Strategy workspace diagnostic endpoint",
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

const BACKEND_CAPABILITY_GROUPS: BackendCapabilityDefinition[] = [
  {
    id: "trading",
    workspaceKey: "trading",
    workspaceLabel: "Trading",
    route: "/trading",
    title: "Paper trading cockpit",
    description: "Trading positions, orders, sessions, replay, promotion, controls, and operator inbox readiness.",
    isAvailable: (payload) => payload.trading !== null && payload.trading !== undefined,
    unavailableDetail: "Trading cockpit payload has not loaded.",
    endpoints: [
      { id: "trading-workspace", method: "GET", label: "Workspace", href: "/api/workstation/trading" },
      { id: "trading-readiness", method: "GET", label: "Readiness", href: "/api/workstation/trading/readiness" },
      { id: "operator-inbox", method: "GET", label: "Operator inbox", href: "/api/workstation/operator/inbox" },
      { id: "orders-submit", method: "POST", label: "Submit order", href: "/api/execution/orders/submit" },
      { id: "sessions", method: "GET", label: "Paper sessions", href: "/api/execution/sessions" },
      { id: "replay-files", method: "GET", label: "Replay files", href: "/api/replay/files" }
    ]
  },
  {
    id: "portfolio",
    workspaceKey: "portfolio",
    workspaceLabel: "Portfolio",
    route: "/portfolio",
    title: "Portfolio and run continuity",
    description: "Aggregate exposure, symbol exposure, run fills, ledger, attribution, continuity, and review packets.",
    isAvailable: (payload) => payload.trading !== null && payload.trading !== undefined,
    unavailableDetail: "Portfolio uses trading and run-continuity payloads; trading workspace data has not loaded.",
    endpoints: [
      { id: "portfolio-aggregate", method: "GET", label: "Portfolio aggregate", href: "/api/portfolio/aggregate" },
      { id: "portfolio-exposure", method: "GET", label: "Portfolio exposure", href: "/api/portfolio/exposure" },
      { id: "run-ledger", method: "GET", label: "Run ledger", href: "/api/workstation/runs/{runId}/ledger" },
      { id: "run-continuity", method: "GET", label: "Run continuity", href: "/api/workstation/runs/{runId}/continuity" },
      { id: "run-review-packet", method: "GET", label: "Review packet", href: "/api/workstation/runs/{runId}/review-packet" }
    ]
  },
  {
    id: "accounting",
    workspaceKey: "accounting",
    workspaceLabel: "Accounting",
    route: "/accounting",
    title: "Accounting and reconciliation",
    description: "Reconciliation run creation, break queues, audit history, calibration summary, cash flow, and ledger drill-ins.",
    isAvailable: (payload) => payload.governance !== null && payload.governance !== undefined,
    unavailableDetail: "Accounting workspace payload has not loaded.",
    endpoints: [
      { id: "accounting-workspace", method: "GET", label: "Workspace", href: "/api/workstation/accounting" },
      { id: "recon-runs", method: "POST", label: "Run reconciliation", href: "/api/workstation/reconciliation/runs" },
      { id: "break-queue", method: "GET", label: "Break queue", href: "/api/workstation/reconciliation/break-queue" },
      { id: "calibration", method: "GET", label: "Calibration", href: "/api/workstation/reconciliation/calibration-summary" },
      { id: "break-audit", method: "GET", label: "Break audit", href: "/api/workstation/reconciliation/break-queue/{breakId}/audit" }
    ]
  },
  {
    id: "reporting",
    workspaceKey: "reporting",
    workspaceLabel: "Reporting",
    route: "/reporting",
    title: "Governed reports and exports",
    description: "Reporting workspace posture, analysis exports, report-pack targets, data dictionaries, and approval lanes.",
    isAvailable: (payload) => payload.reporting !== null && payload.reporting !== undefined,
    unavailableDetail: "Reporting workspace payload has not loaded.",
    endpoints: [
      { id: "reporting-workspace", method: "GET", label: "Workspace", href: "/api/workstation/reporting" },
      { id: "analysis-export", method: "POST", label: "Analysis export", href: "/api/export/analysis" },
      { id: "fund-report-packs", method: "GET", label: "Report packs", href: "/api/fund-structure/report-packs" },
      { id: "export-formats", method: "GET", label: "Export formats", href: "/api/export/formats" }
    ]
  },
  {
    id: "strategy",
    workspaceKey: "strategy",
    workspaceLabel: "Strategy",
    route: "/strategy",
    title: "Strategy run library",
    description: "Strategy workspace payloads, run history, timeline, sweeps, comparisons, diffs, and promotion actions.",
    isAvailable: (payload) => payload.research !== null && payload.research !== undefined,
    unavailableDetail: "Strategy workspace payload has not loaded.",
    endpoints: [
      { id: "strategy-workspace", method: "GET", label: "Workspace", href: "/api/workstation/strategy" },
      { id: "run-history", method: "GET", label: "Run history", href: "/api/workstation/runs/history" },
      { id: "run-timeline", method: "GET", label: "Run timeline", href: "/api/workstation/runs/timeline" },
      { id: "run-sweeps", method: "GET", label: "Run sweeps", href: "/api/workstation/runs/sweeps" },
      { id: "run-compare", method: "POST", label: "Compare runs", href: "/api/workstation/runs/compare" },
      { id: "promotion", method: "GET", label: "Promotion check", href: "/api/promotion/evaluate/{runId}" }
    ]
  },
  {
    id: "data",
    workspaceKey: "data",
    workspaceLabel: "Data",
    route: "/data",
    title: "Data trust and provider operations",
    description: "Provider status, backfill trigger and preview, Security Master, symbols, storage quality, and data-quality queues.",
    isAvailable: (payload) => payload.dataOperations !== null && payload.dataOperations !== undefined,
    unavailableDetail: "Data workspace payload has not loaded.",
    endpoints: [
      { id: "data-workspace", method: "GET", label: "Workspace", href: "/api/workstation/data" },
      { id: "provider-status", method: "GET", label: "Provider status", href: "/api/providers/status" },
      { id: "backfill-run", method: "POST", label: "Backfill run", href: "/api/backfill/run" },
      { id: "security-master", method: "GET", label: "Security Master", href: "/api/workstation/security-master/securities" },
      { id: "symbols", method: "GET", label: "Symbols", href: "/api/symbols" },
      { id: "quality-dashboard", method: "GET", label: "Quality", href: "/api/quality/dashboard" }
    ]
  },
  {
    id: "settings",
    workspaceKey: "settings",
    workspaceLabel: "Settings",
    route: "/settings",
    title: "Configuration and diagnostics",
    description: "Session context, health, configuration, workflow library, workflow presets, credentials, and diagnostics.",
    isAvailable: (payload) => payload.session !== null && payload.overview !== null,
    unavailableDetail: "Session or system overview has not loaded.",
    endpoints: [
      { id: "session", method: "GET", label: "Session", href: "/api/workstation/session" },
      { id: "status", method: "GET", label: "System status", href: "/api/status" },
      { id: "workflow-summary", method: "GET", label: "Workflow summary", href: "/api/workstation/workflow-summary" },
      { id: "workflow-library", method: "GET", label: "Workflow library", href: "/api/workstation/workflows" },
      { id: "workflow-presets", method: "GET", label: "Workflow presets", href: "/api/workstation/workflows/presets" },
      { id: "config", method: "GET", label: "Config", href: "/api/config" }
    ]
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

function buildAlpacaConnectionPanel(connection: BrokerageConnectionStatus | null): SettingsAlpacaConnectionPanel {
  const environment = connection?.environment?.trim() || "paper";
  const isLive = environment.toLowerCase() === "live";
  const warnings = [
    ...(connection?.warnings ?? []),
    ...(isLive ? ["Live Alpaca endpoint is selected. Paper remains the default workstation path."] : [])
  ];
  const state = connection?.state ?? "NotConfigured";
  const tone: SettingsAlpacaConnectionPanel["statusTone"] = state === "Connected"
    ? "success"
    : state === "Degraded" || state === "ReauthorizationRequired"
      ? "danger"
      : state === "Disconnected" || state === "AuthorizationPending"
        ? "warning"
        : "default";

  return {
    providerLabel: connection?.displayName ?? "Alpaca paper",
    stateLabel: connectionStateLabel(state),
    statusDetail: connectionStatusDetail(connection),
    statusTone: tone,
    badgeVariant: tone === "default" ? "outline" : tone,
    environmentLabel: environment.toUpperCase(),
    accountLabel: connection?.externalAccountId?.trim() || "Not verified",
    maskedKeyIdLabel: connection?.maskedKeyId?.trim() || "Not stored",
    verifiedAtLabel: connection?.verifiedAt?.trim() || "Not verified",
    warnings,
    canClear: connection?.isConfigured === true
  };
}

function connectionStateLabel(state: BrokerageConnectionStatus["state"]): string {
  switch (state) {
    case "Connected":
      return "Connected";
    case "AuthorizationPending":
      return "Verification pending";
    case "ReauthorizationRequired":
      return "Review required";
    case "Degraded":
      return "Verification failed";
    case "Disconnected":
      return "Stored";
    default:
      return "Not configured";
  }
}

function connectionStatusDetail(connection: BrokerageConnectionStatus | null): string {
  if (connection?.isConnected) {
    const account = connection.externalAccountId?.trim();
    return account
      ? `Verified Alpaca account ${account} through /v2/account.`
      : "Verified Alpaca account through /v2/account.";
  }

  if (connection?.lastError) {
    return connection.lastError;
  }

  if (connection?.isConfigured) {
    return "Alpaca API keys are stored but the account has not been verified.";
  }

  return "No Alpaca API-key connection is stored.";
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
    sessionTitle: session ? `Session - ${session.displayName}` : "Session",
    sessionItems,
    hasSession: session !== null,
    systemTitle: "System posture",
    systemSummary: sysSummary,
    systemTone: sysTone,
    systemItems,
    hasOverview: overview !== null,
    recentEventsSection: buildRecentEventsSection(overview),
    alpacaConnectionPanel: buildAlpacaConnectionPanel(payload.brokerageConnection ?? null),
    ...buildDiagnosticEndpointSection(payload),
    ...buildBackendCapabilitySection(payload)
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

function buildBackendCapabilitySection(payload: SettingsScreenPayload): Pick<
  SettingsScreenViewModel,
  "backendCapabilityGroups" | "backendCapabilitySummary" | "backendCapabilityListLabel" | "backendCapabilityStatusLabel" | "backendCapabilityStatusVariant"
> {
  const groups = BACKEND_CAPABILITY_GROUPS.map((group) => buildBackendCapabilityGroup(group, payload));
  const loaded = groups.filter((group) => group.statusVariant === "success").length;
  const failed = groups.filter((group) => group.statusVariant === "danger").length;
  const checking = payload.loading === true ? groups.length : 0;
  const statusLabel = checking > 0
    ? `${checking} checking`
    : failed > 0
      ? `${failed} unavailable`
      : "All surfaced";

  return {
    backendCapabilityGroups: groups,
    backendCapabilitySummary: checking > 0
      ? `Checking ${checking} backend capability group${checking === 1 ? "" : "s"} across the browser workstation.`
      : failed > 0
        ? `${failed} backend capability group${failed === 1 ? "" : "s"} needs API attention before the browser can claim full workflow reachability.`
        : `${loaded} backend capability group${loaded === 1 ? "" : "s"} are represented by browser routes and mapped API endpoints.`,
    backendCapabilityListLabel: "Backend capability coverage by workstation route",
    backendCapabilityStatusLabel: statusLabel,
    backendCapabilityStatusVariant: checking > 0 ? "warning" : failed > 0 ? "danger" : "success"
  };
}

function buildBackendCapabilityGroup(
  definition: BackendCapabilityDefinition,
  payload: SettingsScreenPayload
): SettingsBackendCapabilityGroup {
  const error = payload.workspaceErrors?.[definition.workspaceKey];
  const isLoading = payload.loading === true;
  const endpointCount = definition.endpoints.length;
  const endpoints = definition.endpoints.map((endpoint) => ({
    ...endpoint,
    ariaLabel: `${endpoint.method} ${endpoint.href} for ${definition.workspaceLabel} ${endpoint.label}`
  }));

  if (isLoading) {
    return {
      ...definition,
      endpointCountLabel: `${endpointCount} endpoint${endpointCount === 1 ? "" : "s"}`,
      loadedCountLabel: "Checking",
      statusLabel: "Checking",
      statusDetail: "Workstation bootstrap is refreshing this capability group.",
      statusVariant: "warning",
      endpoints
    };
  }

  if (error) {
    return {
      ...definition,
      endpointCountLabel: `${endpointCount} endpoint${endpointCount === 1 ? "" : "s"}`,
      loadedCountLabel: "0 loaded",
      statusLabel: "Unavailable",
      statusDetail: error,
      statusVariant: "danger",
      endpoints
    };
  }

  if (definition.isAvailable(payload)) {
    return {
      ...definition,
      endpointCountLabel: `${endpointCount} endpoint${endpointCount === 1 ? "" : "s"}`,
      loadedCountLabel: `${endpointCount} mapped`,
      statusLabel: "Surfaced",
      statusDetail: `${definition.workspaceLabel} has a browser route and mapped backend endpoints.`,
      statusVariant: "success",
      endpoints
    };
  }

  return {
    ...definition,
    endpointCountLabel: `${endpointCount} endpoint${endpointCount === 1 ? "" : "s"}`,
    loadedCountLabel: "0 loaded",
    statusLabel: "Unavailable",
    statusDetail: payload.error ?? definition.unavailableDetail,
    statusVariant: "danger",
    endpoints
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
