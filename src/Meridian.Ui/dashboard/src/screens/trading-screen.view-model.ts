import { useCallback, useEffect, useMemo, useState } from "react";
import * as workstationApi from "@/lib/api";
import type { ApprovePromotionRequest, RejectPromotionRequest } from "@/lib/api";
import type {
  ExecutionAuditEntry,
  ExecutionControlSnapshot,
  OperatorWorkItem,
  OrderResult,
  OrderSubmitRequest,
  PaperSessionDetail,
  PaperSessionReplayVerification,
  PaperSessionSummary,
  PromotionDecisionResult,
  PromotionEvaluationResult,
  PromotionRecord,
  ReplayFileRecord,
  ReplayStatus,
  TradingActionResult,
  TradingAcceptanceGateStatus,
  TradingOperatorReadiness,
  WorkstationBrokerageSyncStatus
} from "@/types";

export type AcceptanceLevel = "ready" | "review" | "atRisk";

export interface TradingReadinessSummaryRow {
  id: string;
  label: string;
  value: string;
  level: AcceptanceLevel;
  ariaLabel: string;
}

export interface TradingReadinessState {
  readiness: TradingOperatorReadiness | null;
  refreshing: boolean;
  errorText: string | null;
  workItems: OperatorWorkItem[];
  warnings: string[];
  summaryRows: TradingReadinessSummaryRow[];
  summaryLabel: string;
  refreshButtonLabel: string;
  refreshAriaLabel: string;
  statusAnnouncement: string;
}

export interface TradingReadinessViewModel extends TradingReadinessState {
  refresh: () => Promise<void>;
}

export interface TradingReadinessServices {
  getTradingReadiness: () => Promise<TradingOperatorReadiness | null>;
}

export interface BuildTradingReadinessStateOptions {
  readiness: TradingOperatorReadiness | null;
  refreshing: boolean;
  errorText: string | null;
}

const defaultTradingReadinessServices: TradingReadinessServices = {
  getTradingReadiness: () => workstationApi.getTradingReadiness()
};

export function useTradingReadinessViewModel({
  initialReadiness,
  services = defaultTradingReadinessServices
}: {
  initialReadiness: TradingOperatorReadiness | null;
  services?: TradingReadinessServices;
}): TradingReadinessViewModel {
  const [readiness, setReadiness] = useState<TradingOperatorReadiness | null>(initialReadiness);
  const [refreshing, setRefreshing] = useState(false);
  const [errorText, setErrorText] = useState<string | null>(null);

  useEffect(() => {
    setReadiness(initialReadiness);
    setErrorText(null);
  }, [initialReadiness]);

  const refresh = useCallback(async () => {
    setRefreshing(true);
    setErrorText(null);

    try {
      setReadiness(await services.getTradingReadiness());
    } catch (err) {
      setErrorText(toErrorMessage(err, "Failed to refresh trading readiness."));
    } finally {
      setRefreshing(false);
    }
  }, [services]);

  const state = useMemo(
    () => buildTradingReadinessState({ readiness, refreshing, errorText }),
    [errorText, readiness, refreshing]
  );

  return {
    ...state,
    refresh
  };
}

export function buildTradingReadinessState({
  readiness,
  refreshing,
  errorText
}: BuildTradingReadinessStateOptions): TradingReadinessState {
  const summaryRows = readiness ? buildTradingReadinessSummaryRows(readiness) : [];

  return {
    readiness,
    refreshing,
    errorText,
    workItems: readiness?.workItems ?? [],
    warnings: readiness?.warnings ?? [],
    summaryRows,
    summaryLabel: "Trading readiness contract summary",
    refreshButtonLabel: refreshing ? "Refreshing..." : "Refresh readiness",
    refreshAriaLabel: refreshing ? "Refreshing trading readiness" : "Refresh trading readiness",
    statusAnnouncement: buildTradingReadinessAnnouncement({ readiness, refreshing, errorText })
  };
}

export function formatReadinessStatusValue(status: TradingAcceptanceGateStatus | string): string {
  if (status === "ReviewRequired") {
    return "Review required";
  }

  return status;
}

export function mapReadinessStatusLevel(status: TradingAcceptanceGateStatus | string): AcceptanceLevel {
  if (status === "Ready") {
    return "ready";
  }

  if (status === "Blocked") {
    return "atRisk";
  }

  return "review";
}

export function mapBrokerageSyncLevel(status: WorkstationBrokerageSyncStatus): AcceptanceLevel {
  if (status.health === "Healthy" && !status.isStale) {
    return "ready";
  }

  if (status.health === "Failed" || status.health === "Degraded") {
    return "atRisk";
  }

  return "review";
}

function buildTradingReadinessSummaryRows(readiness: TradingOperatorReadiness): TradingReadinessSummaryRow[] {
  const overallValue = formatReadinessStatusValue(readiness.overallStatus);
  const paperValue = readiness.readyForPaperOperation ? "Ready for paper" : "Not paper ready";
  const brokerageValue = readiness.brokerageSync
    ? formatBrokerageSyncValue(readiness.brokerageSync)
    : "No account sync";

  return [
    {
      id: "overall",
      label: "Overall",
      value: overallValue,
      level: mapReadinessStatusLevel(readiness.overallStatus),
      ariaLabel: `Overall readiness: ${overallValue}`
    },
    {
      id: "paper",
      label: "Paper",
      value: paperValue,
      level: readiness.readyForPaperOperation ? "ready" : "review",
      ariaLabel: `Paper operation readiness: ${paperValue}`
    },
    {
      id: "brokerage",
      label: "Brokerage",
      value: brokerageValue,
      level: readiness.brokerageSync ? mapBrokerageSyncLevel(readiness.brokerageSync) : "review",
      ariaLabel: `Brokerage sync: ${brokerageValue}`
    },
    {
      id: "as-of",
      label: "As of",
      value: readiness.asOf,
      level: "review",
      ariaLabel: `Readiness snapshot timestamp: ${readiness.asOf}`
    }
  ];
}

function formatBrokerageSyncValue(status: WorkstationBrokerageSyncStatus): string {
  const staleSuffix = status.isStale && status.health !== "Stale" ? " stale" : "";
  return `${status.health}${staleSuffix}`;
}

function buildTradingReadinessAnnouncement({
  readiness,
  refreshing,
  errorText
}: BuildTradingReadinessStateOptions): string {
  if (refreshing) {
    return "Refreshing trading readiness.";
  }

  if (errorText) {
    return `Trading readiness refresh failed: ${errorText}`;
  }

  if (readiness) {
    return `Trading readiness ${formatReadinessStatusValue(readiness.overallStatus).toLowerCase()} as of ${readiness.asOf}.`;
  }

  return "";
}

export type ExecutionEvidenceTone = "success" | "warning" | "danger";

export interface ExecutionEvidenceFieldRow {
  id: string;
  label: string;
  value: string;
}

export interface ExecutionAuditRow {
  id: string;
  action: string;
  outcome: string;
  outcomeTone: ExecutionEvidenceTone;
  message: string;
  metadataText: string;
  ariaLabel: string;
}

export interface ExecutionControlsPanel {
  title: string;
  statusLabel: string;
  statusTone: ExecutionEvidenceTone;
  ariaLabel: string;
  rows: ExecutionEvidenceFieldRow[];
}

export interface ExecutionEvidenceState {
  auditEntries: ExecutionAuditEntry[];
  controlsSnapshot: ExecutionControlSnapshot | null;
  auditRows: ExecutionAuditRow[];
  controlsPanel: ExecutionControlsPanel | null;
  loading: boolean;
  errorText: string | null;
  auditTitle: string;
  auditListLabel: string;
  auditEmptyText: string;
  auditCountLabel: string;
  controlsEmptyText: string;
  refreshButtonLabel: string;
  refreshAriaLabel: string;
  statusAnnouncement: string;
}

export interface ExecutionEvidenceViewModel extends ExecutionEvidenceState {
  refresh: () => Promise<void>;
}

export interface ExecutionEvidenceServices {
  getExecutionAudit: (take: number) => Promise<ExecutionAuditEntry[]>;
  getExecutionControls: () => Promise<ExecutionControlSnapshot>;
}

export interface BuildExecutionEvidenceStateOptions {
  auditEntries: ExecutionAuditEntry[];
  controlsSnapshot: ExecutionControlSnapshot | null;
  loading: boolean;
  errorText: string | null;
}

const defaultExecutionEvidenceServices: ExecutionEvidenceServices = {
  getExecutionAudit: (take) => workstationApi.getExecutionAudit(take),
  getExecutionControls: () => workstationApi.getExecutionControls()
};

export function useExecutionEvidenceViewModel({
  services = defaultExecutionEvidenceServices,
  auditTake = 8
}: {
  services?: ExecutionEvidenceServices;
  auditTake?: number;
} = {}): ExecutionEvidenceViewModel {
  const [auditEntries, setAuditEntries] = useState<ExecutionAuditEntry[]>([]);
  const [controlsSnapshot, setControlsSnapshot] = useState<ExecutionControlSnapshot | null>(null);
  const [loading, setLoading] = useState(false);
  const [errorText, setErrorText] = useState<string | null>(null);

  const refresh = useCallback(async () => {
    setLoading(true);
    setErrorText(null);

    const [auditResult, controlsResult] = await Promise.allSettled([
      Promise.resolve().then(() => services.getExecutionAudit(auditTake)),
      Promise.resolve().then(() => services.getExecutionControls())
    ]);

    if (auditResult.status === "fulfilled") {
      setAuditEntries(auditResult.value);
    } else {
      setAuditEntries([]);
    }

    if (controlsResult.status === "fulfilled") {
      setControlsSnapshot(controlsResult.value);
    } else {
      setControlsSnapshot(null);
    }

    const failures = [auditResult, controlsResult].filter((result) => result.status === "rejected");
    if (failures.length > 0) {
      const firstFailure = failures[0];
      const reason = firstFailure.status === "rejected" ? firstFailure.reason : null;
      setErrorText(toErrorMessage(reason, "Execution evidence refresh failed."));
    }

    setLoading(false);
  }, [auditTake, services]);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  const state = useMemo(
    () => buildExecutionEvidenceState({ auditEntries, controlsSnapshot, loading, errorText }),
    [auditEntries, controlsSnapshot, errorText, loading]
  );

  return {
    ...state,
    refresh
  };
}

export function buildExecutionEvidenceState({
  auditEntries,
  controlsSnapshot,
  loading,
  errorText
}: BuildExecutionEvidenceStateOptions): ExecutionEvidenceState {
  const auditRows = auditEntries.map(buildExecutionAuditRow);
  const controlsPanel = controlsSnapshot ? buildExecutionControlsPanel(controlsSnapshot) : null;

  return {
    auditEntries,
    controlsSnapshot,
    auditRows,
    controlsPanel,
    loading,
    errorText,
    auditTitle: "Recent execution audit",
    auditListLabel: "Recent execution audit entries",
    auditEmptyText: loading ? "Loading execution audit entries..." : "No execution audit entries available.",
    auditCountLabel: `${auditRows.length} audit ${auditRows.length === 1 ? "entry" : "entries"}`,
    controlsEmptyText: loading ? "Loading execution controls snapshot..." : "Snapshot unavailable.",
    refreshButtonLabel: loading ? "Refreshing..." : "Refresh evidence",
    refreshAriaLabel: loading ? "Refreshing execution evidence" : "Refresh execution audit and controls evidence",
    statusAnnouncement: buildExecutionEvidenceAnnouncement({ auditRows, controlsPanel, loading, errorText })
  };
}

function buildExecutionAuditRow(entry: ExecutionAuditEntry): ExecutionAuditRow {
  const metadataText = formatExecutionAuditMetadata(entry);
  const message = entry.message?.trim() || "No operator message recorded.";

  return {
    id: entry.auditId,
    action: entry.action,
    outcome: entry.outcome,
    outcomeTone: mapExecutionOutcomeTone(entry.outcome),
    message,
    metadataText,
    ariaLabel: `${entry.action} ${entry.outcome}. ${message} ${metadataText}`.trim()
  };
}

function buildExecutionControlsPanel(snapshot: ExecutionControlSnapshot): ExecutionControlsPanel {
  const breakerOpen = snapshot.circuitBreaker.isOpen;
  const symbolLimitCount = Object.keys(snapshot.symbolPositionLimits).length;
  const overrideCount = snapshot.manualOverrides.length;

  return {
    title: "Execution controls snapshot",
    statusLabel: `Breaker ${breakerOpen ? "Open" : "Closed"}`,
    statusTone: breakerOpen ? "danger" : "success",
    ariaLabel: `Execution controls snapshot: breaker ${breakerOpen ? "open" : "closed"}, ${symbolLimitCount} symbol ${symbolLimitCount === 1 ? "limit" : "limits"}, ${overrideCount} active ${overrideCount === 1 ? "override" : "overrides"}.`,
    rows: [
      {
        id: "default-limit",
        label: "Default limit",
        value: snapshot.defaultMaxPositionSize === null ? "Not set" : String(snapshot.defaultMaxPositionSize)
      },
      {
        id: "symbol-limits",
        label: "Symbol limits",
        value: formatExecutionSymbolLimits(snapshot.symbolPositionLimits)
      },
      {
        id: "active-overrides",
        label: "Active overrides",
        value: formatExecutionManualOverrides(snapshot.manualOverrides)
      },
      {
        id: "as-of",
        label: "As of",
        value: snapshot.asOf
      }
    ]
  };
}

function formatExecutionAuditMetadata(entry: ExecutionAuditEntry): string {
  const parts = [
    entry.occurredAt,
    entry.metadata?.sessionId ? `session ${entry.metadata.sessionId}` : null,
    entry.orderId ? `order ${entry.orderId}` : null,
    entry.symbol ? `symbol ${entry.symbol}` : null,
    entry.runId ? `run ${entry.runId}` : null
  ].filter(Boolean);

  return parts.join(" · ");
}

function mapExecutionOutcomeTone(outcome: string): ExecutionEvidenceTone {
  const normalized = outcome.toLowerCase();
  if (normalized.includes("fail") || normalized.includes("reject") || normalized.includes("error")) {
    return "danger";
  }

  if (normalized.includes("complete") || normalized.includes("accept") || normalized.includes("success")) {
    return "success";
  }

  return "warning";
}

function formatExecutionSymbolLimits(limits: Record<string, number>): string {
  const entries = Object.entries(limits);
  if (entries.length === 0) {
    return "None";
  }

  return entries.map(([symbol, limit]) => `${symbol}=${limit}`).join(", ");
}

function formatExecutionManualOverrides(overrides: ExecutionControlSnapshot["manualOverrides"]): string {
  if (overrides.length === 0) {
    return "None";
  }

  return overrides
    .map((entry) => `${entry.kind}${entry.symbol ? ` (${entry.symbol})` : ""}`)
    .join(", ");
}

function buildExecutionEvidenceAnnouncement({
  auditRows,
  controlsPanel,
  loading,
  errorText
}: {
  auditRows: ExecutionAuditRow[];
  controlsPanel: ExecutionControlsPanel | null;
  loading: boolean;
  errorText: string | null;
}): string {
  if (loading) {
    return "Refreshing execution evidence.";
  }

  if (errorText) {
    return `Execution evidence refresh failed: ${errorText}`;
  }

  if (controlsPanel) {
    return `${controlsPanel.statusLabel}. ${auditRows.length} audit ${auditRows.length === 1 ? "entry" : "entries"} loaded.`;
  }

  if (auditRows.length > 0) {
    return `${auditRows.length} execution audit ${auditRows.length === 1 ? "entry" : "entries"} loaded.`;
  }

  return "";
}

export type PaperSessionField = "strategyId" | "initialCash";
export type PaperSessionCommandKind = "loading" | "creating" | "restoring" | "verifying" | "closing";

export interface PaperSessionForm {
  strategyId: string;
  initialCash: string;
}

export interface PaperSessionBusyCommand {
  kind: PaperSessionCommandKind;
  sessionId?: string;
}

export interface PaperSessionRow {
  sessionId: string;
  strategyId: string;
  initialCashText: string;
  statusLabel: string;
  isActive: boolean;
  isSelected: boolean;
  ariaLabel: string;
  restoreButtonLabel: string;
  verifyButtonLabel: string;
  closeButtonLabel: string;
  restoreAriaLabel: string;
  verifyAriaLabel: string;
  closeAriaLabel: string;
  canRestore: boolean;
  canVerify: boolean;
  canClose: boolean;
}

export interface PaperSessionFieldRow {
  label: string;
  value: string;
}

export interface PaperSessionMetricRow {
  label: string;
  value: string;
}

export interface PaperSessionReplayPanel {
  tone: "success" | "warning";
  statusLabel: string;
  ariaLabel: string;
  metadataText: string;
  rows: PaperSessionFieldRow[];
  mismatchReasons: string[];
}

export interface PaperSessionDetailPanel {
  sessionId: string;
  statusLabel: string;
  statusTone: AcceptanceLevel;
  ariaLabel: string;
  infoRows: PaperSessionFieldRow[];
  metricRows: PaperSessionMetricRow[];
  replay: PaperSessionReplayPanel | null;
}

export interface PaperSessionState {
  sessions: PaperSessionSummary[];
  selectedSessionId: string | null;
  selectedSessionDetail: PaperSessionDetail | null;
  sessionReplayVerification: PaperSessionReplayVerification | null;
  form: PaperSessionForm;
  showCreateForm: boolean;
  busyCommand: PaperSessionBusyCommand | null;
  isBusy: boolean;
  errorText: string | null;
  rows: PaperSessionRow[];
  detail: PaperSessionDetailPanel | null;
  emptyText: string;
  selectedSessionLabel: string | null;
  formPanelId: string;
  formDescriptionId: string;
  formRequirementText: string;
  toggleCreateButtonLabel: string;
  createButtonLabel: string;
  cancelCreateButtonLabel: string;
  createButtonAriaLabel: string;
  canSubmitCreate: boolean;
  canCloseCreateForm: boolean;
  statusAnnouncement: string;
}

export interface PaperSessionServices {
  getExecutionSessions: () => Promise<PaperSessionSummary[]>;
  createPaperSession: (strategyId: string, strategyName: string | null, initialCash: number) => Promise<PaperSessionSummary>;
  closePaperSession: (sessionId: string) => Promise<TradingActionResult>;
  getPaperSessionDetail: (sessionId: string) => Promise<PaperSessionDetail>;
  getPaperSessionReplayVerification: (sessionId: string) => Promise<PaperSessionReplayVerification>;
}

export interface BuildPaperSessionStateOptions {
  sessions: PaperSessionSummary[];
  selectedSessionId: string | null;
  selectedSessionDetail: PaperSessionDetail | null;
  sessionReplayVerification: PaperSessionReplayVerification | null;
  form: PaperSessionForm;
  showCreateForm: boolean;
  busyCommand: PaperSessionBusyCommand | null;
  errorText: string | null;
}

export interface PaperSessionCreateRequest {
  strategyId: string;
  initialCash: number;
}

export const emptyPaperSessionForm: PaperSessionForm = {
  strategyId: "",
  initialCash: "100000"
};

const defaultPaperSessionServices: PaperSessionServices = {
  getExecutionSessions: () => workstationApi.getExecutionSessions(),
  createPaperSession: (strategyId, strategyName, initialCash) => workstationApi.createPaperSession(strategyId, strategyName, initialCash),
  closePaperSession: (sessionId) => workstationApi.closePaperSession(sessionId),
  getPaperSessionDetail: (sessionId) => workstationApi.getPaperSessionDetail(sessionId),
  getPaperSessionReplayVerification: (sessionId) => workstationApi.getPaperSessionReplayVerification(sessionId)
};

export function usePaperSessionsViewModel({
  services = defaultPaperSessionServices,
  onSessionEvidenceChanged
}: {
  services?: PaperSessionServices;
  onSessionEvidenceChanged?: () => Promise<void> | void;
} = {}) {
  const [sessions, setSessions] = useState<PaperSessionSummary[]>([]);
  const [selectedSessionId, setSelectedSessionId] = useState<string | null>(null);
  const [selectedSessionDetail, setSelectedSessionDetail] = useState<PaperSessionDetail | null>(null);
  const [sessionReplayVerification, setSessionReplayVerification] = useState<PaperSessionReplayVerification | null>(null);
  const [form, setForm] = useState<PaperSessionForm>(emptyPaperSessionForm);
  const [showCreateForm, setShowCreateForm] = useState(false);
  const [busyCommand, setBusyCommand] = useState<PaperSessionBusyCommand | null>({ kind: "loading" });
  const [errorText, setErrorText] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    setBusyCommand({ kind: "loading" });
    setErrorText(null);

    services.getExecutionSessions()
      .then((rows) => {
        if (!cancelled) {
          setSessions(rows);
        }
      })
      .catch((err) => {
        if (!cancelled) {
          setSessions([]);
          setErrorText(toErrorMessage(err, "Failed to load paper sessions."));
        }
      })
      .finally(() => {
        if (!cancelled) {
          setBusyCommand(null);
        }
      });

    return () => {
      cancelled = true;
    };
  }, [services]);

  const state = useMemo(
    () => buildPaperSessionState({
      sessions,
      selectedSessionId,
      selectedSessionDetail,
      sessionReplayVerification,
      form,
      showCreateForm,
      busyCommand,
      errorText
    }),
    [busyCommand, errorText, form, selectedSessionDetail, selectedSessionId, sessionReplayVerification, sessions, showCreateForm]
  );

  const toggleCreateForm = useCallback(() => {
    setShowCreateForm((current) => !current);
    setErrorText(null);
  }, []);

  const closeCreateForm = useCallback(() => {
    setShowCreateForm(false);
    setErrorText(null);
  }, []);

  const updateField = useCallback((field: PaperSessionField, value: string) => {
    setForm((current) => ({ ...current, [field]: value }));
    setErrorText(null);
  }, []);

  const createSession = useCallback(async () => {
    const validationError = validatePaperSessionForm(form);
    if (validationError) {
      setErrorText(validationError);
      return;
    }

    const request = buildPaperSessionCreateRequest(form);
    setBusyCommand({ kind: "creating" });
    setErrorText(null);

    try {
      const summary = await services.createPaperSession(request.strategyId, null, request.initialCash);
      setSessions((current) => [summary, ...current]);
      setShowCreateForm(false);
      setForm(emptyPaperSessionForm);
      await onSessionEvidenceChanged?.();
    } catch (err) {
      setErrorText(toErrorMessage(err, "Failed to create session."));
    } finally {
      setBusyCommand(null);
    }
  }, [form, onSessionEvidenceChanged, services]);

  const closeSession = useCallback(async (sessionId: string) => {
    setBusyCommand({ kind: "closing", sessionId });
    setErrorText(null);

    try {
      const result = await services.closePaperSession(sessionId);
      setSessions((current) => current.map((session) => (
        session.sessionId === sessionId
          ? { ...session, closedAt: result.occurredAt, isActive: false }
          : session
      )));
      setSelectedSessionDetail((current) => {
        if (!current || current.summary.sessionId !== sessionId) {
          return current;
        }

        return {
          ...current,
          summary: {
            ...current.summary,
            closedAt: result.occurredAt,
            isActive: false
          }
        };
      });
      await onSessionEvidenceChanged?.();
    } catch (err) {
      setErrorText(toErrorMessage(err, "Failed to close session."));
    } finally {
      setBusyCommand(null);
    }
  }, [onSessionEvidenceChanged, services]);

  const restoreSession = useCallback(async (sessionId: string) => {
    setBusyCommand({ kind: "restoring", sessionId });
    setErrorText(null);

    try {
      const detail = await services.getPaperSessionDetail(sessionId);
      setSelectedSessionId(sessionId);
      setSelectedSessionDetail(detail);
      setSessionReplayVerification(null);
    } catch (err) {
      setErrorText(toErrorMessage(err, "Failed to restore session."));
    } finally {
      setBusyCommand(null);
    }
  }, [services]);

  const verifySessionReplay = useCallback(async (sessionId: string) => {
    setBusyCommand({ kind: "verifying", sessionId });
    setErrorText(null);

    try {
      const [detail, verification] = await Promise.all([
        services.getPaperSessionDetail(sessionId),
        services.getPaperSessionReplayVerification(sessionId)
      ]);
      setSelectedSessionId(sessionId);
      setSelectedSessionDetail(detail);
      setSessionReplayVerification(verification);
      await onSessionEvidenceChanged?.();
    } catch (err) {
      setErrorText(toErrorMessage(err, "Failed to verify session replay."));
    } finally {
      setBusyCommand(null);
    }
  }, [onSessionEvidenceChanged, services]);

  return {
    ...state,
    toggleCreateForm,
    closeCreateForm,
    updateField,
    createSession,
    closeSession,
    restoreSession,
    verifySessionReplay
  };
}

export function buildPaperSessionState({
  sessions,
  selectedSessionId,
  selectedSessionDetail,
  sessionReplayVerification,
  form,
  showCreateForm,
  busyCommand,
  errorText
}: BuildPaperSessionStateOptions): PaperSessionState {
  const validationError = validatePaperSessionForm(form);
  const isBusy = busyCommand !== null;

  return {
    sessions,
    selectedSessionId,
    selectedSessionDetail,
    sessionReplayVerification,
    form,
    showCreateForm,
    busyCommand,
    isBusy,
    errorText,
    rows: buildPaperSessionRows({ sessions, selectedSessionId, busyCommand }),
    detail: selectedSessionDetail
      ? buildPaperSessionDetailPanel(selectedSessionDetail, sessionReplayVerification)
      : null,
    emptyText: busyCommand?.kind === "loading"
      ? "Loading paper sessions."
      : "No paper sessions active. Create one above to start tracking execution.",
    selectedSessionLabel: selectedSessionId ? `Selected session: ${selectedSessionId}` : null,
    formPanelId: "paper-session-create-form",
    formDescriptionId: "paper-session-create-requirements",
    formRequirementText: validationError ?? "Strategy ID is optional. Initial cash must be at least $1,000.",
    toggleCreateButtonLabel: showCreateForm ? "Close form" : "New session",
    createButtonLabel: busyCommand?.kind === "creating" ? "Creating..." : "Create session",
    cancelCreateButtonLabel: "Cancel",
    createButtonAriaLabel: busyCommand?.kind === "creating" ? "Creating paper session" : "Create paper session",
    canSubmitCreate: !isBusy && validationError === null,
    canCloseCreateForm: !isBusy,
    statusAnnouncement: buildPaperSessionAnnouncement({
      sessions,
      selectedSessionDetail,
      sessionReplayVerification,
      busyCommand,
      errorText
    })
  };
}

export function validatePaperSessionForm(form: PaperSessionForm): string | null {
  const initialCash = parsePaperSessionCash(form.initialCash);

  if (initialCash === null || initialCash < 1_000) {
    return "Enter initial cash of at least $1,000.";
  }

  return null;
}

export function buildPaperSessionCreateRequest(
  form: PaperSessionForm,
  now: () => number = Date.now
): PaperSessionCreateRequest {
  return {
    strategyId: form.strategyId.trim() || `strat-${now()}`,
    initialCash: parsePaperSessionCash(form.initialCash) ?? 100_000
  };
}

function buildPaperSessionRows({
  sessions,
  selectedSessionId,
  busyCommand
}: {
  sessions: PaperSessionSummary[];
  selectedSessionId: string | null;
  busyCommand: PaperSessionBusyCommand | null;
}): PaperSessionRow[] {
  return sessions.map((session) => {
    const rowBusy = busyCommand?.sessionId === session.sessionId;
    const anyBusy = busyCommand !== null;
    const statusLabel = getPaperSessionStatus(session);

    return {
      sessionId: session.sessionId,
      strategyId: session.strategyId,
      initialCashText: formatUsdValue(session.initialCash),
      statusLabel,
      isActive: session.isActive,
      isSelected: selectedSessionId === session.sessionId,
      ariaLabel: `${session.sessionId}, ${session.strategyId}, ${statusLabel}, ${formatUsdValue(session.initialCash)} initial cash`,
      restoreButtonLabel: rowBusy && busyCommand?.kind === "restoring" ? "Restoring..." : "Restore",
      verifyButtonLabel: rowBusy && busyCommand?.kind === "verifying" ? "Verifying..." : "Verify replay",
      closeButtonLabel: rowBusy && busyCommand?.kind === "closing" ? "Closing..." : "Close",
      restoreAriaLabel: `Restore paper session ${session.sessionId}`,
      verifyAriaLabel: `Verify replay for paper session ${session.sessionId}`,
      closeAriaLabel: `Close paper session ${session.sessionId}`,
      canRestore: !anyBusy,
      canVerify: !anyBusy,
      canClose: session.isActive && !anyBusy
    };
  });
}

function buildPaperSessionDetailPanel(
  detail: PaperSessionDetail,
  replayVerification: PaperSessionReplayVerification | null
): PaperSessionDetailPanel {
  const replay = replayVerification?.summary.sessionId === detail.summary.sessionId
    ? buildPaperSessionReplayPanel(replayVerification)
    : null;

  return {
    sessionId: detail.summary.sessionId,
    statusLabel: getPaperSessionStatus(detail.summary),
    statusTone: detail.summary.isActive ? "ready" : "review",
    ariaLabel: `Paper session detail for ${detail.summary.sessionId}`,
    infoRows: [
      { label: "Strategy", value: detail.summary.strategyId },
      { label: "Initial cash", value: formatUsdValue(detail.summary.initialCash) },
      { label: "Tracked symbols", value: detail.symbols.length > 0 ? detail.symbols.join(", ") : "None" },
      { label: "Orders retained", value: String(detail.orderHistory?.length ?? 0) }
    ],
    metricRows: detail.portfolio
      ? [
          { label: "Cash", value: formatUsdValue(detail.portfolio.cash) },
          { label: "Portfolio value", value: formatUsdValue(detail.portfolio.portfolioValue) },
          { label: "Open positions", value: String(detail.portfolio.positions.length) }
        ]
      : [],
    replay
  };
}

function buildPaperSessionReplayPanel(verification: PaperSessionReplayVerification): PaperSessionReplayPanel {
  return {
    tone: verification.isConsistent ? "success" : "warning",
    statusLabel: verification.isConsistent ? "Matched current state" : "Mismatch detected",
    ariaLabel: verification.isConsistent
      ? `Replay verification matched current state for ${verification.summary.sessionId}`
      : `Replay verification mismatch detected for ${verification.summary.sessionId}`,
    metadataText: `Source: ${verification.replaySource} · Verified at ${verification.verifiedAt}`,
    rows: [
      { label: "Compared fills", value: String(verification.comparedFillCount) },
      { label: "Compared orders", value: String(verification.comparedOrderCount) },
      { label: "Compared ledger entries", value: String(verification.comparedLedgerEntryCount) },
      { label: "Verification audit", value: verification.verificationAuditId ?? "Unavailable" },
      { label: "Last persisted fill", value: verification.lastPersistedFillAt ?? "N/A" },
      { label: "Last persisted order update", value: verification.lastPersistedOrderUpdateAt ?? "N/A" }
    ],
    mismatchReasons: verification.mismatchReasons.slice(0, 3)
  };
}

function buildPaperSessionAnnouncement({
  sessions,
  selectedSessionDetail,
  sessionReplayVerification,
  busyCommand,
  errorText
}: {
  sessions: PaperSessionSummary[];
  selectedSessionDetail: PaperSessionDetail | null;
  sessionReplayVerification: PaperSessionReplayVerification | null;
  busyCommand: PaperSessionBusyCommand | null;
  errorText: string | null;
}): string {
  if (busyCommand) {
    if (busyCommand.kind === "loading") {
      return "Loading paper sessions.";
    }

    if (busyCommand.kind === "creating") {
      return "Creating paper session.";
    }

    if (busyCommand.sessionId) {
      return `${capitalizeWord(busyCommand.kind)} paper session ${busyCommand.sessionId}.`;
    }
  }

  if (errorText) {
    return `Paper session workflow failed: ${errorText}`;
  }

  if (sessionReplayVerification) {
    return sessionReplayVerification.isConsistent
      ? `Replay verification matched current state for ${sessionReplayVerification.summary.sessionId}.`
      : `Replay verification mismatch detected for ${sessionReplayVerification.summary.sessionId}.`;
  }

  if (selectedSessionDetail) {
    return `Paper session ${selectedSessionDetail.summary.sessionId} restored.`;
  }

  return `${sessions.length} paper session${sessions.length === 1 ? "" : "s"} loaded.`;
}

function parsePaperSessionCash(value: string): number | null {
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : null;
}

function getPaperSessionStatus(session: PaperSessionSummary): string {
  return session.isActive ? "Active" : "Closed";
}

function formatUsdValue(value: number): string {
  return value.toLocaleString(undefined, {
    style: "currency",
    currency: "USD",
    maximumFractionDigits: 2
  });
}

export type SessionReplayCommandKind =
  | "loading"
  | "starting"
  | "pausing"
  | "resuming"
  | "stopping"
  | "seeking"
  | "applying-speed";

export interface SessionReplayFileOption {
  path: string;
  name: string;
  ariaLabel: string;
  metadataText: string;
}

export interface SessionReplayControlsState {
  files: ReplayFileRecord[];
  fileOptions: SessionReplayFileOption[];
  selectedFilePath: string;
  replayStatus: ReplayStatus | null;
  replaySpeed: string;
  seekMs: string;
  loadingFiles: boolean;
  activeCommand: SessionReplayCommandKind | null;
  errorText: string | null;
  isBusy: boolean;
  fileSelectId: string;
  speedInputId: string;
  seekInputId: string;
  statusId: string;
  errorId: string;
  speedValidationText: string | null;
  seekValidationText: string | null;
  canStart: boolean;
  canPause: boolean;
  canResume: boolean;
  canStop: boolean;
  canSeek: boolean;
  canApplySpeed: boolean;
  startButtonLabel: string;
  pauseButtonLabel: string;
  resumeButtonLabel: string;
  stopButtonLabel: string;
  seekButtonLabel: string;
  applySpeedButtonLabel: string;
  statusText: string;
  statusAnnouncement: string;
}

export interface SessionReplayControlsViewModel extends SessionReplayControlsState {
  selectReplayFile: (path: string) => void;
  updateReplaySpeed: (value: string) => void;
  updateSeekMs: (value: string) => void;
  startReplay: () => Promise<void>;
  pauseReplay: () => Promise<void>;
  resumeReplay: () => Promise<void>;
  stopReplay: () => Promise<void>;
  seekReplay: () => Promise<void>;
  applyReplaySpeed: () => Promise<void>;
}

export interface SessionReplayControlsServices {
  getReplayFiles: () => Promise<{ files: ReplayFileRecord[]; total: number; timestamp: string }>;
  startReplay: (filePath: string, speedMultiplier?: number) => Promise<{ sessionId: string }>;
  getReplayStatus: (sessionId: string) => Promise<ReplayStatus>;
  pauseReplay: (sessionId: string) => Promise<unknown>;
  resumeReplay: (sessionId: string) => Promise<unknown>;
  stopReplay: (sessionId: string) => Promise<unknown>;
  seekReplay: (sessionId: string, positionMs: number) => Promise<unknown>;
  setReplaySpeed: (sessionId: string, speedMultiplier: number) => Promise<unknown>;
}

export interface BuildSessionReplayControlsStateOptions {
  files: ReplayFileRecord[];
  selectedFilePath: string;
  replayStatus: ReplayStatus | null;
  replaySpeed: string;
  seekMs: string;
  loadingFiles: boolean;
  activeCommand: SessionReplayCommandKind | null;
  errorText: string | null;
}

const defaultSessionReplayControlsServices: SessionReplayControlsServices = {
  getReplayFiles: () => workstationApi.getReplayFiles(),
  startReplay: (filePath, speedMultiplier) => workstationApi.startReplay(filePath, speedMultiplier),
  getReplayStatus: (sessionId) => workstationApi.getReplayStatus(sessionId),
  pauseReplay: (sessionId) => workstationApi.pauseReplay(sessionId),
  resumeReplay: (sessionId) => workstationApi.resumeReplay(sessionId),
  stopReplay: (sessionId) => workstationApi.stopReplay(sessionId),
  seekReplay: (sessionId, positionMs) => workstationApi.seekReplay(sessionId, positionMs),
  setReplaySpeed: (sessionId, speedMultiplier) => workstationApi.setReplaySpeed(sessionId, speedMultiplier)
};

export function useSessionReplayControlsViewModel(
  services: SessionReplayControlsServices = defaultSessionReplayControlsServices
): SessionReplayControlsViewModel {
  const [files, setFiles] = useState<ReplayFileRecord[]>([]);
  const [selectedFilePath, setSelectedFilePath] = useState("");
  const [replayStatus, setReplayStatus] = useState<ReplayStatus | null>(null);
  const [replaySpeed, setReplaySpeed] = useState("1");
  const [seekMs, setSeekMs] = useState("0");
  const [loadingFiles, setLoadingFiles] = useState(true);
  const [activeCommand, setActiveCommand] = useState<SessionReplayCommandKind | null>("loading");
  const [errorText, setErrorText] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;

    setLoadingFiles(true);
    setActiveCommand("loading");
    setErrorText(null);

    services.getReplayFiles()
      .then((result) => {
        if (cancelled) {
          return;
        }

        setFiles(result.files);
        setSelectedFilePath((current) => current || result.files[0]?.path || "");
      })
      .catch((err) => {
        if (cancelled) {
          return;
        }

        setFiles([]);
        setSelectedFilePath("");
        setErrorText(toErrorMessage(err, "Failed to load replay files."));
      })
      .finally(() => {
        if (!cancelled) {
          setLoadingFiles(false);
          setActiveCommand(null);
        }
      });

    return () => {
      cancelled = true;
    };
  }, [services]);

  const state = useMemo(
    () => buildSessionReplayControlsState({
      files,
      selectedFilePath,
      replayStatus,
      replaySpeed,
      seekMs,
      loadingFiles,
      activeCommand,
      errorText
    }),
    [activeCommand, errorText, files, loadingFiles, replaySpeed, replayStatus, seekMs, selectedFilePath]
  );

  const selectReplayFile = useCallback((path: string) => {
    setSelectedFilePath(path);
    setErrorText(null);
  }, []);

  const updateReplaySpeed = useCallback((value: string) => {
    setReplaySpeed(value);
    setErrorText(null);
  }, []);

  const updateSeekMs = useCallback((value: string) => {
    setSeekMs(value);
    setErrorText(null);
  }, []);

  const startReplayCommand = useCallback(async () => {
    const speed = parseReplaySpeed(replaySpeed);

    if (!selectedFilePath) {
      setErrorText("Select a replay file before starting replay.");
      return;
    }

    if (speed === null) {
      setErrorText(validateReplaySpeed(replaySpeed));
      return;
    }

    setActiveCommand("starting");
    setErrorText(null);

    try {
      const started = await services.startReplay(selectedFilePath, speed);
      setReplayStatus(await services.getReplayStatus(started.sessionId));
    } catch (err) {
      setErrorText(toErrorMessage(err, "Failed to start replay."));
    } finally {
      setActiveCommand(null);
    }
  }, [replaySpeed, selectedFilePath, services]);

  const runReplayCommand = useCallback(async (
    command: Exclude<SessionReplayCommandKind, "loading" | "starting">,
    action: (sessionId: string) => Promise<unknown>,
    fallback: string,
    options: { clearStatus?: boolean } = {}
  ) => {
    const status = replayStatus;

    if (!status) {
      setErrorText("Start a replay before using controls.");
      return;
    }

    setActiveCommand(command);
    setErrorText(null);

    try {
      await action(status.sessionId);
      if (options.clearStatus) {
        setReplayStatus(null);
      } else {
        setReplayStatus(await services.getReplayStatus(status.sessionId));
      }
    } catch (err) {
      setErrorText(toErrorMessage(err, fallback));
    } finally {
      setActiveCommand(null);
    }
  }, [replayStatus, services]);

  const seekReplayCommand = useCallback(async () => {
    const positionMs = parseReplaySeekMs(seekMs);

    if (positionMs === null) {
      setErrorText(validateReplaySeekMs(seekMs));
      return;
    }

    await runReplayCommand(
      "seeking",
      (sessionId) => services.seekReplay(sessionId, positionMs),
      "Replay seek failed."
    );
  }, [runReplayCommand, seekMs, services]);

  const applyReplaySpeedCommand = useCallback(async () => {
    const speed = parseReplaySpeed(replaySpeed);

    if (speed === null) {
      setErrorText(validateReplaySpeed(replaySpeed));
      return;
    }

    await runReplayCommand(
      "applying-speed",
      (sessionId) => services.setReplaySpeed(sessionId, speed),
      "Replay speed update failed."
    );
  }, [replaySpeed, runReplayCommand, services]);

  return {
    ...state,
    selectReplayFile,
    updateReplaySpeed,
    updateSeekMs,
    startReplay: startReplayCommand,
    pauseReplay: () => runReplayCommand(
      "pausing",
      (sessionId) => services.pauseReplay(sessionId),
      "Replay pause failed."
    ),
    resumeReplay: () => runReplayCommand(
      "resuming",
      (sessionId) => services.resumeReplay(sessionId),
      "Replay resume failed."
    ),
    stopReplay: () => runReplayCommand(
      "stopping",
      (sessionId) => services.stopReplay(sessionId),
      "Replay stop failed.",
      { clearStatus: true }
    ),
    seekReplay: seekReplayCommand,
    applyReplaySpeed: applyReplaySpeedCommand
  };
}

export function buildSessionReplayControlsState({
  files,
  selectedFilePath,
  replayStatus,
  replaySpeed,
  seekMs,
  loadingFiles,
  activeCommand,
  errorText
}: BuildSessionReplayControlsStateOptions): SessionReplayControlsState {
  const speedValidationText = validateReplaySpeed(replaySpeed);
  const seekValidationText = validateReplaySeekMs(seekMs);
  const isBusy = loadingFiles || activeCommand !== null;
  const hasReplayStatus = replayStatus !== null;

  return {
    files,
    fileOptions: files.map(buildSessionReplayFileOption),
    selectedFilePath,
    replayStatus,
    replaySpeed,
    seekMs,
    loadingFiles,
    activeCommand,
    errorText,
    isBusy,
    fileSelectId: "session-replay-file",
    speedInputId: "session-replay-speed",
    seekInputId: "session-replay-seek",
    statusId: "session-replay-status",
    errorId: "session-replay-error",
    speedValidationText,
    seekValidationText,
    canStart: !isBusy && selectedFilePath.trim().length > 0 && speedValidationText === null,
    canPause: !isBusy && hasReplayStatus,
    canResume: !isBusy && hasReplayStatus,
    canStop: !isBusy && hasReplayStatus,
    canSeek: !isBusy && hasReplayStatus && seekValidationText === null,
    canApplySpeed: !isBusy && hasReplayStatus && speedValidationText === null,
    startButtonLabel: activeCommand === "starting" ? "Starting..." : "Start",
    pauseButtonLabel: activeCommand === "pausing" ? "Pausing..." : "Pause",
    resumeButtonLabel: activeCommand === "resuming" ? "Resuming..." : "Resume",
    stopButtonLabel: activeCommand === "stopping" ? "Stopping..." : "Stop",
    seekButtonLabel: activeCommand === "seeking" ? "Seeking..." : "Seek",
    applySpeedButtonLabel: activeCommand === "applying-speed" ? "Applying speed..." : "Apply speed",
    statusText: buildSessionReplayStatusText({ files, selectedFilePath, replayStatus, loadingFiles }),
    statusAnnouncement: buildSessionReplayAnnouncement({
      files,
      selectedFilePath,
      replayStatus,
      loadingFiles,
      activeCommand,
      errorText
    })
  };
}

function buildSessionReplayFileOption(file: ReplayFileRecord): SessionReplayFileOption {
  const metadata = formatReplayFileMetadata(file);

  return {
    path: file.path,
    name: file.name,
    metadataText: metadata,
    ariaLabel: metadata ? `${file.name}, ${metadata}` : file.name
  };
}

function formatReplayFileMetadata(file: ReplayFileRecord): string {
  const details = [
    file.symbol,
    file.eventType,
    file.isCompressed ? "compressed" : null,
    file.lastModified
  ].filter(Boolean);

  return details.length > 0 ? details.join(" / ") : "";
}

function buildSessionReplayStatusText({
  files,
  selectedFilePath,
  replayStatus,
  loadingFiles
}: {
  files: ReplayFileRecord[];
  selectedFilePath: string;
  replayStatus: ReplayStatus | null;
  loadingFiles: boolean;
}): string {
  if (replayStatus) {
    return `Replay ${replayStatus.status} · ${replayStatus.eventsProcessed}/${replayStatus.totalEvents} (${replayStatus.progressPercent}%)`;
  }

  if (loadingFiles) {
    return "Loading replay files.";
  }

  if (files.length === 0) {
    return "No replay files available.";
  }

  const selectedFileName = files.find((file) => file.path === selectedFilePath)?.name ?? "selected file";
  return `Ready to replay ${selectedFileName}.`;
}

function buildSessionReplayAnnouncement({
  files,
  selectedFilePath,
  replayStatus,
  loadingFiles,
  activeCommand,
  errorText
}: {
  files: ReplayFileRecord[];
  selectedFilePath: string;
  replayStatus: ReplayStatus | null;
  loadingFiles: boolean;
  activeCommand: SessionReplayCommandKind | null;
  errorText: string | null;
}): string {
  if (activeCommand) {
    return formatSessionReplayCommandAnnouncement(activeCommand);
  }

  if (errorText) {
    return `Session replay failed: ${errorText}`;
  }

  if (replayStatus) {
    return `Replay ${replayStatus.status} for ${replayStatus.sessionId} at ${replayStatus.progressPercent} percent.`;
  }

  if (loadingFiles) {
    return "Loading replay files.";
  }

  if (files.length === 0) {
    return "No replay files available.";
  }

  const selectedFileName = files.find((file) => file.path === selectedFilePath)?.name ?? "selected file";
  return `Replay file ${selectedFileName} selected.`;
}

function formatSessionReplayCommandAnnouncement(command: SessionReplayCommandKind): string {
  const announcements: Record<SessionReplayCommandKind, string> = {
    loading: "Loading replay files.",
    starting: "Starting session replay.",
    pausing: "Pausing session replay.",
    resuming: "Resuming session replay.",
    stopping: "Stopping session replay.",
    seeking: "Seeking session replay.",
    "applying-speed": "Applying replay speed."
  };

  return announcements[command];
}

function validateReplaySpeed(value: string): string | null {
  return parseReplaySpeed(value) === null
    ? "Enter a replay speed greater than 0."
    : null;
}

function validateReplaySeekMs(value: string): string | null {
  return parseReplaySeekMs(value) === null
    ? "Enter a seek position of 0 ms or greater."
    : null;
}

function parseReplaySpeed(value: string): number | null {
  const parsed = Number(value);
  return Number.isFinite(parsed) && parsed > 0 ? parsed : null;
}

function parseReplaySeekMs(value: string): number | null {
  const parsed = Number(value);
  return Number.isFinite(parsed) && parsed >= 0 ? parsed : null;
}

export type TradingConfirmAction =
  | { kind: "cancel-order"; orderId: string }
  | { kind: "cancel-all" }
  | { kind: "close-position"; symbol: string }
  | { kind: "pause-strategy"; strategyId: string }
  | { kind: "stop-strategy"; strategyId: string };

export interface StrategyLifecycleControlsState {
  strategyId: string;
  strategyIdValue: string;
  strategyIdInputId: string;
  strategyIdHelpId: string;
  strategyIdStatusId: string;
  titleId: string;
  title: string;
  description: string;
  strategyIdLabel: string;
  strategyIdPlaceholder: string;
  helpText: string;
  statusText: string;
  statusAnnouncement: string;
  canPause: boolean;
  canStop: boolean;
  pauseButtonLabel: string;
  stopButtonLabel: string;
  pauseAriaLabel: string;
  stopAriaLabel: string;
  pauseAction: TradingConfirmAction | null;
  stopAction: TradingConfirmAction | null;
}

export interface StrategyLifecycleControlsViewModel extends StrategyLifecycleControlsState {
  updateStrategyId: (value: string) => void;
  openPauseConfirm: () => void;
  openStopConfirm: () => void;
}

export function useStrategyLifecycleControlsViewModel({
  openConfirm
}: {
  openConfirm: (action: TradingConfirmAction) => void;
}): StrategyLifecycleControlsViewModel {
  const [strategyId, setStrategyId] = useState("");
  const state = useMemo(() => buildStrategyLifecycleControlsState(strategyId), [strategyId]);

  const openPauseConfirm = useCallback(() => {
    if (state.pauseAction) {
      openConfirm(state.pauseAction);
    }
  }, [openConfirm, state.pauseAction]);

  const openStopConfirm = useCallback(() => {
    if (state.stopAction) {
      openConfirm(state.stopAction);
    }
  }, [openConfirm, state.stopAction]);

  return {
    ...state,
    updateStrategyId: setStrategyId,
    openPauseConfirm,
    openStopConfirm
  };
}

export function buildStrategyLifecycleControlsState(strategyId: string): StrategyLifecycleControlsState {
  const strategyIdValue = strategyId.trim();
  const hasStrategyId = strategyIdValue.length > 0;
  const pauseAction: TradingConfirmAction | null = hasStrategyId
    ? { kind: "pause-strategy", strategyId: strategyIdValue }
    : null;
  const stopAction: TradingConfirmAction | null = hasStrategyId
    ? { kind: "stop-strategy", strategyId: strategyIdValue }
    : null;
  const statusText = hasStrategyId
    ? `Ready to open lifecycle confirmation for ${strategyIdValue}.`
    : "Enter a registered strategy ID to enable lifecycle actions.";

  return {
    strategyId,
    strategyIdValue,
    strategyIdInputId: "trading-strategy-lifecycle-id",
    strategyIdHelpId: "trading-strategy-lifecycle-help",
    strategyIdStatusId: "trading-strategy-lifecycle-status",
    titleId: "trading-strategy-lifecycle-title",
    title: "Strategy lifecycle",
    description: "Pause or stop a running strategy by its registered ID. Changes open a confirmation before execution.",
    strategyIdLabel: "Strategy ID",
    strategyIdPlaceholder: "e.g. mean-reversion-fx-01",
    helpText: "Use the registered strategy ID from the active paper session or Strategy run record.",
    statusText,
    statusAnnouncement: hasStrategyId
      ? `Strategy lifecycle controls ready for ${strategyIdValue}.`
      : "Strategy lifecycle controls are waiting for a strategy ID.",
    canPause: hasStrategyId,
    canStop: hasStrategyId,
    pauseButtonLabel: "Pause",
    stopButtonLabel: "Stop",
    pauseAriaLabel: hasStrategyId
      ? `Open pause confirmation for strategy ${strategyIdValue}`
      : "Enter a strategy ID before pausing a strategy",
    stopAriaLabel: hasStrategyId
      ? `Open stop confirmation for strategy ${strategyIdValue}`
      : "Enter a strategy ID before stopping a strategy",
    pauseAction,
    stopAction
  };
}

export interface TradingConfirmState {
  action: TradingConfirmAction | null;
  busy: boolean;
  result: TradingActionResult | null;
  error: string | null;
}

export interface TradingConfirmResultPanel {
  status: string;
  message: string;
  actionId: string;
  tone: "success" | "warning";
  ariaLabel: string;
}

export interface TradingConfirmErrorPanel {
  text: string;
  ariaLabel: string;
}

export interface TradingConfirmDialogState {
  open: boolean;
  title: string;
  description: string;
  dialogTitleId: string;
  dialogDescriptionId: string;
  statusAnnouncement: string;
  cancelButtonLabel: string;
  confirmButtonLabel: string;
  closeButtonLabel: string;
  confirmAriaLabel: string;
  closeAriaLabel: string;
  canClose: boolean;
  canConfirm: boolean;
  isCompleted: boolean;
  resultPanel: TradingConfirmResultPanel | null;
  errorPanel: TradingConfirmErrorPanel | null;
}

export interface TradingConfirmServices {
  cancelOrder: (orderId: string) => Promise<TradingActionResult>;
  cancelAllOrders: () => Promise<TradingActionResult>;
  closePosition: (symbol: string) => Promise<TradingActionResult>;
  pauseStrategy: (strategyId: string) => Promise<{ success: boolean; reason: string | null }>;
  stopStrategy: (strategyId: string) => Promise<{ success: boolean; reason: string | null }>;
}

export interface TradingConfirmViewModel extends TradingConfirmDialogState {
  state: TradingConfirmState;
  openConfirm: (action: TradingConfirmAction) => void;
  closeConfirm: () => void;
  executeConfirm: () => Promise<void>;
}

const defaultTradingConfirmServices: TradingConfirmServices = {
  cancelOrder: (orderId) => workstationApi.cancelOrder(orderId),
  cancelAllOrders: () => workstationApi.cancelAllOrders(),
  closePosition: (symbol) => workstationApi.closePosition(symbol),
  pauseStrategy: (strategyId) => workstationApi.pauseStrategy(strategyId),
  stopStrategy: (strategyId) => workstationApi.stopStrategy(strategyId)
};

export function useTradingConfirmViewModel({
  services = defaultTradingConfirmServices,
  onActionSettled
}: {
  services?: TradingConfirmServices;
  onActionSettled?: () => Promise<void> | void;
} = {}): TradingConfirmViewModel {
  const [state, setState] = useState<TradingConfirmState>(() => createTradingConfirmState());

  const openConfirm = useCallback((action: TradingConfirmAction) => {
    setState(createTradingConfirmState(action));
  }, []);

  const closeConfirm = useCallback(() => {
    setState((current) => current.busy ? current : createTradingConfirmState());
  }, []);

  const executeConfirm = useCallback(async () => {
    const action = state.action;
    if (!action) {
      return;
    }

    setState((current) => ({ ...current, busy: true, result: null, error: null }));

    try {
      const result = await executeTradingConfirmAction(action, services);
      await onActionSettled?.();
      setState((current) => ({ ...current, busy: false, result }));
    } catch (err) {
      setState((current) => ({
        ...current,
        busy: false,
        error: toErrorMessage(err, "Action failed.")
      }));
    }
  }, [onActionSettled, services, state.action]);

  const dialogState = useMemo(() => buildTradingConfirmDialogState(state), [state]);

  return {
    ...dialogState,
    state,
    openConfirm,
    closeConfirm,
    executeConfirm
  };
}

export function createTradingConfirmState(action: TradingConfirmAction | null = null): TradingConfirmState {
  return {
    action,
    busy: false,
    result: null,
    error: null
  };
}

export function buildTradingConfirmDialogState(state: TradingConfirmState): TradingConfirmDialogState {
  const title = state.action ? tradingConfirmActionLabel(state.action) : "";
  const description = state.action ? tradingConfirmActionDescription(state.action) : "";
  const isCompleted = state.result !== null;
  const actionId = state.action ? sanitizeDomId(tradingConfirmActionDomKey(state.action)) : "none";
  const resultPanel = state.result ? buildTradingConfirmResultPanel(state.result) : null;
  const errorPanel = state.error ? { text: state.error, ariaLabel: `Confirmation action failed: ${state.error}` } : null;

  return {
    open: state.action !== null,
    title,
    description,
    dialogTitleId: `trading-confirm-${actionId}-title`,
    dialogDescriptionId: `trading-confirm-${actionId}-description`,
    statusAnnouncement: buildTradingConfirmStatusAnnouncement({ title, busy: state.busy, result: state.result, error: state.error }),
    cancelButtonLabel: "Cancel",
    confirmButtonLabel: state.busy ? "Processing..." : "Confirm",
    closeButtonLabel: "Close",
    confirmAriaLabel: title ? `Confirm ${title.toLowerCase()}` : "Confirm trading action",
    closeAriaLabel: title ? `Close ${title.toLowerCase()} confirmation` : "Close trading action confirmation",
    canClose: !state.busy,
    canConfirm: Boolean(state.action) && !state.busy && !isCompleted,
    isCompleted,
    resultPanel,
    errorPanel
  };
}

function buildTradingConfirmResultPanel(result: TradingActionResult): TradingConfirmResultPanel {
  const isSuccess = result.status === "Accepted" || result.status === "Completed";

  return {
    status: result.status,
    message: result.message,
    actionId: result.actionId,
    tone: isSuccess ? "success" : "warning",
    ariaLabel: `Action ${result.status.toLowerCase()}: ${result.message}`
  };
}

async function executeTradingConfirmAction(
  action: TradingConfirmAction,
  services: TradingConfirmServices
): Promise<TradingActionResult> {
  if (action.kind === "cancel-order") {
    return services.cancelOrder(action.orderId);
  }

  if (action.kind === "cancel-all") {
    return services.cancelAllOrders();
  }

  if (action.kind === "close-position") {
    return services.closePosition(action.symbol);
  }

  const raw = action.kind === "pause-strategy"
    ? await services.pauseStrategy(action.strategyId)
    : await services.stopStrategy(action.strategyId);
  const actionName = action.kind === "pause-strategy" ? "paused" : "stopped";

  return {
    actionId: `act-${Date.now()}`,
    status: raw.success ? "Completed" : "Rejected",
    message: raw.reason ?? (raw.success ? `Strategy ${actionName}.` : `${capitalizeWord(actionName)} rejected.`),
    occurredAt: new Date().toISOString()
  };
}

function tradingConfirmActionLabel(action: TradingConfirmAction): string {
  switch (action.kind) {
    case "cancel-order": return `Cancel order ${action.orderId}`;
    case "cancel-all": return "Cancel all open orders";
    case "close-position": return `Close position - ${action.symbol}`;
    case "pause-strategy": return `Pause strategy - ${action.strategyId}`;
    case "stop-strategy": return `Stop strategy - ${action.strategyId}`;
  }
}

function tradingConfirmActionDescription(action: TradingConfirmAction): string {
  switch (action.kind) {
    case "cancel-order":
      return "This will request cancellation of the selected order. Partial fills that already occurred are not reversed.";
    case "cancel-all":
      return "This will request cancellation of every open order in the current session. Partial fills that already occurred are not reversed.";
    case "close-position":
      return "A market order will be submitted to flatten the full position at the next available price. You will remain responsible for the resulting fill.";
    case "pause-strategy":
      return "The strategy will stop processing new signals until manually resumed. Open positions and orders remain unchanged.";
    case "stop-strategy":
      return "The strategy will be stopped and its session will be closed. Open positions remain until manually flattened.";
  }
}

function tradingConfirmActionDomKey(action: TradingConfirmAction): string {
  switch (action.kind) {
    case "cancel-order": return `${action.kind}-${action.orderId}`;
    case "cancel-all": return action.kind;
    case "close-position": return `${action.kind}-${action.symbol}`;
    case "pause-strategy":
    case "stop-strategy":
      return `${action.kind}-${action.strategyId}`;
  }
}

function buildTradingConfirmStatusAnnouncement({
  title,
  busy,
  result,
  error
}: {
  title: string;
  busy: boolean;
  result: TradingActionResult | null;
  error: string | null;
}): string {
  if (!title) {
    return "";
  }

  if (busy) {
    return `${title} processing.`;
  }

  if (error) {
    return `${title} failed: ${error}`;
  }

  if (result) {
    return `${title} ${result.status.toLowerCase()}: ${result.message}`;
  }

  return `${title} confirmation open.`;
}

function sanitizeDomId(value: string): string {
  const normalized = value.trim().toLowerCase().replace(/[^a-z0-9_-]+/g, "-").replace(/^-+|-+$/g, "");
  return normalized || "action";
}

function capitalizeWord(value: string): string {
  return value.charAt(0).toUpperCase() + value.slice(1);
}

export type OrderTicketField = "symbol" | "side" | "type" | "quantity" | "limitPrice";
export type OrderTicketPhase = "idle" | "submitting" | "submitted" | "error";

export interface OrderTicketServices {
  submitOrder: (request: OrderSubmitRequest) => Promise<OrderResult>;
}

export interface OrderTicketState {
  form: OrderSubmitRequest;
  open: boolean;
  phase: OrderTicketPhase;
  orderId: string | null;
  errorText: string | null;
  validationError: string | null;
  invalidField: OrderTicketField | null;
  canSubmit: boolean;
  canClose: boolean;
  requiresLimitPrice: boolean;
  priceLabel: string;
  openButtonLabel: string;
  submitButtonLabel: string;
  submitAriaLabel: string;
  requirementText: string;
  successText: string | null;
  statusAnnouncement: string;
}

export interface BuildOrderTicketStateOptions {
  form: OrderSubmitRequest;
  open: boolean;
  phase: OrderTicketPhase;
  orderId: string | null;
  errorText: string | null;
}

export const emptyOrderTicketForm: OrderSubmitRequest = {
  symbol: "",
  side: "Buy",
  type: "Market",
  quantity: 0,
  limitPrice: null
};

const defaultOrderTicketServices: OrderTicketServices = {
  submitOrder: (request) => workstationApi.submitOrder(request)
};

export function useOrderTicketViewModel({
  services = defaultOrderTicketServices,
  onOrderAccepted
}: {
  services?: OrderTicketServices;
  onOrderAccepted?: () => Promise<void> | void;
} = {}) {
  const [form, setForm] = useState<OrderSubmitRequest>(emptyOrderTicketForm);
  const [open, setOpen] = useState(false);
  const [phase, setPhase] = useState<OrderTicketPhase>("idle");
  const [orderId, setOrderId] = useState<string | null>(null);
  const [errorText, setErrorText] = useState<string | null>(null);

  const state = useMemo(
    () => buildOrderTicketState({ form, open, phase, orderId, errorText }),
    [errorText, form, open, orderId, phase]
  );

  const openTicket = useCallback(() => {
    setOpen(true);
    setErrorText(null);
    if (phase !== "submitted") {
      return;
    }

    setPhase("idle");
    setOrderId(null);
  }, [phase]);

  const closeTicket = useCallback(() => {
    if (phase === "submitting") {
      return;
    }

    setOpen(false);
    setPhase("idle");
    setOrderId(null);
    setErrorText(null);
  }, [phase]);

  const toggleTicket = useCallback(() => {
    if (open) {
      closeTicket();
      return;
    }

    openTicket();
  }, [closeTicket, open, openTicket]);

  const updateField = useCallback((field: OrderTicketField, value: string) => {
    setForm((current) => updateOrderTicketForm(current, field, value));
    setPhase((current) => current === "submitted" ? "idle" : current);
    setOrderId(null);
    setErrorText(null);
  }, []);

  const normalizeSymbol = useCallback(() => {
    setForm((current) => ({
      ...current,
      symbol: normalizeOrderSymbol(current.symbol)
    }));
  }, []);

  const submitOrderTicket = useCallback(async () => {
    const validationError = validateOrderTicketForm(form);
    if (validationError) {
      setPhase("error");
      setOrderId(null);
      setErrorText(validationError);
      return;
    }

    setPhase("submitting");
    setOrderId(null);
    setErrorText(null);

    try {
      const result = await services.submitOrder(buildOrderSubmitRequest(form));
      if (result.success) {
        setPhase("submitted");
        setOrderId(result.orderId);
        setErrorText(null);
        setOpen(false);
        setForm(emptyOrderTicketForm);
        await onOrderAccepted?.();
        return;
      }

      setPhase("error");
      setErrorText(result.reason ?? "Order failed.");
    } catch (err) {
      setPhase("error");
      setErrorText(toErrorMessage(err, "Order submission failed."));
    }
  }, [form, onOrderAccepted, services]);

  return {
    ...state,
    openTicket,
    closeTicket,
    toggleTicket,
    updateField,
    normalizeSymbol,
    submitOrder: submitOrderTicket
  };
}

export function buildOrderTicketState({
  form,
  open,
  phase,
  orderId,
  errorText
}: BuildOrderTicketStateOptions): OrderTicketState {
  const validationError = validateOrderTicketForm(form);
  const requiresLimitPrice = orderTypeRequiresPrice(form.type);
  const successText = phase === "submitted"
    ? `Order submitted${orderId ? ` - ${orderId}` : ""}.`
    : null;

  return {
    form,
    open,
    phase,
    orderId,
    errorText,
    validationError,
    invalidField: getOrderTicketInvalidField(form),
    canSubmit: phase !== "submitting" && validationError === null,
    canClose: phase !== "submitting",
    requiresLimitPrice,
    priceLabel: `${form.type} price`,
    openButtonLabel: open ? "Close order ticket" : "New order",
    submitButtonLabel: phase === "submitting" ? "Submitting..." : "Submit order",
    submitAriaLabel: phase === "submitting" ? "Submitting order request" : "Submit order request",
    requirementText: buildOrderRequirementText(form, phase, validationError),
    successText,
    statusAnnouncement: buildOrderTicketStatusAnnouncement({ phase, errorText, orderId })
  };
}

export function updateOrderTicketForm(
  current: OrderSubmitRequest,
  field: OrderTicketField,
  value: string
): OrderSubmitRequest {
  if (field === "symbol") {
    return { ...current, symbol: value };
  }

  if (field === "side") {
    return { ...current, side: value === "Sell" ? "Sell" : "Buy" };
  }

  if (field === "type") {
    const type = value === "Limit" || value === "Stop" ? value : "Market";
    return {
      ...current,
      type,
      limitPrice: orderTypeRequiresPrice(type) ? current.limitPrice : null
    };
  }

  if (field === "quantity") {
    return { ...current, quantity: parsePositiveNumber(value) ?? 0 };
  }

  return { ...current, limitPrice: parsePositiveNumber(value) };
}

export function buildOrderSubmitRequest(form: OrderSubmitRequest): OrderSubmitRequest {
  const request: OrderSubmitRequest = {
    symbol: normalizeOrderSymbol(form.symbol),
    side: form.side,
    type: form.type,
    quantity: form.quantity
  };

  if (orderTypeRequiresPrice(form.type)) {
    request.limitPrice = form.limitPrice;
  }

  return request;
}

export function validateOrderTicketForm(form: OrderSubmitRequest): string | null {
  if (!normalizeOrderSymbol(form.symbol)) {
    return "Enter a symbol before submitting an order.";
  }

  if (!Number.isFinite(form.quantity) || form.quantity <= 0) {
    return "Enter an order quantity greater than zero.";
  }

  if (orderTypeRequiresPrice(form.type) && (!Number.isFinite(form.limitPrice) || (form.limitPrice ?? 0) <= 0)) {
    return `Enter a ${form.type.toLowerCase()} price greater than zero.`;
  }

  return null;
}

function getOrderTicketInvalidField(form: OrderSubmitRequest): OrderTicketField | null {
  if (!normalizeOrderSymbol(form.symbol)) {
    return "symbol";
  }

  if (!Number.isFinite(form.quantity) || form.quantity <= 0) {
    return "quantity";
  }

  if (orderTypeRequiresPrice(form.type) && (!Number.isFinite(form.limitPrice) || (form.limitPrice ?? 0) <= 0)) {
    return "limitPrice";
  }

  return null;
}

function buildOrderRequirementText(
  form: OrderSubmitRequest,
  phase: OrderTicketPhase,
  validationError: string | null
): string {
  if (phase === "submitting") {
    return "Submitting order request to the execution layer.";
  }

  if (validationError) {
    return validationError;
  }

  const symbol = normalizeOrderSymbol(form.symbol);
  const priceText = orderTypeRequiresPrice(form.type) && form.limitPrice
    ? ` at ${form.limitPrice}`
    : "";
  return `${form.side} ${form.quantity} ${symbol} ${form.type.toLowerCase()}${priceText}.`;
}

function buildOrderTicketStatusAnnouncement({
  phase,
  errorText,
  orderId
}: {
  phase: OrderTicketPhase;
  errorText: string | null;
  orderId: string | null;
}): string {
  if (phase === "submitting") {
    return "Submitting order request.";
  }

  if (errorText) {
    return `Order submission failed: ${errorText}`;
  }

  if (phase === "submitted") {
    return `Order submitted${orderId ? ` with id ${orderId}` : ""}.`;
  }

  return "";
}

function normalizeOrderSymbol(symbol: string): string {
  return symbol.trim().toUpperCase();
}

function orderTypeRequiresPrice(type: OrderSubmitRequest["type"]): boolean {
  return type === "Limit" || type === "Stop";
}

function parsePositiveNumber(value: string): number | null {
  const parsed = Number(value);
  return Number.isFinite(parsed) && parsed > 0 ? parsed : null;
}

export type PromotionGateField =
  | "runId"
  | "approvedBy"
  | "approvalReason"
  | "rejectionReason"
  | "reviewNotes"
  | "manualOverrideId";

export interface PromotionGateForm {
  runId: string;
  approvedBy: string;
  approvalReason: string;
  rejectionReason: string;
  reviewNotes: string;
  manualOverrideId: string;
}

export type PromotionGatePhase = "idle" | "evaluating" | "approving" | "rejecting";
export type PromotionOutcomeLevel = "success" | "warning" | "error";

export interface PromotionOutcome {
  level: PromotionOutcomeLevel;
  message: string;
}

export interface PromotionGateServices {
  evaluatePromotion: (runId: string) => Promise<PromotionEvaluationResult>;
  approvePromotion: (request: ApprovePromotionRequest) => Promise<PromotionDecisionResult>;
  rejectPromotion: (request: RejectPromotionRequest) => Promise<PromotionDecisionResult>;
  getPromotionHistory: () => Promise<PromotionRecord[]>;
}

export interface PromotionGateState {
  form: PromotionGateForm;
  trimmedForm: PromotionGateForm;
  evaluation: PromotionEvaluationResult | null;
  history: PromotionRecord[];
  busy: boolean;
  phase: PromotionGatePhase;
  errorText: string | null;
  outcome: PromotionOutcome | null;
  canEvaluate: boolean;
  canPromote: boolean;
  canReject: boolean;
  evaluateButtonLabel: string;
  promoteButtonLabel: string;
  rejectButtonLabel: string;
  nextActionText: string;
  approvalRequirementText: string;
  rejectionRequirementText: string;
  historyEmptyText: string;
  statusAnnouncement: string;
}

export interface BuildPromotionGateStateOptions {
  form: PromotionGateForm;
  busy: boolean;
  phase: PromotionGatePhase;
  errorText: string | null;
  outcome: PromotionOutcome | null;
  evaluation: PromotionEvaluationResult | null;
  history: PromotionRecord[];
}

export const emptyPromotionGateForm: PromotionGateForm = {
  runId: "",
  approvedBy: "",
  approvalReason: "",
  rejectionReason: "",
  reviewNotes: "",
  manualOverrideId: ""
};

const defaultPromotionServices: PromotionGateServices = {
  evaluatePromotion: (runId) => workstationApi.evaluatePromotion(runId),
  approvePromotion: (request) => workstationApi.approvePromotion(request),
  rejectPromotion: (request) => workstationApi.rejectPromotion(request),
  getPromotionHistory: () => workstationApi.getPromotionHistory()
};

export function usePromotionGateViewModel(
  services: PromotionGateServices = defaultPromotionServices
) {
  const [form, setForm] = useState<PromotionGateForm>(emptyPromotionGateForm);
  const [evaluation, setEvaluation] = useState<PromotionEvaluationResult | null>(null);
  const [history, setHistory] = useState<PromotionRecord[]>([]);
  const [errorText, setErrorText] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [phase, setPhase] = useState<PromotionGatePhase>("idle");
  const [outcome, setOutcome] = useState<PromotionOutcome | null>(null);

  useEffect(() => {
    let cancelled = false;

    services.getPromotionHistory()
      .then((rows) => {
        if (!cancelled) {
          setHistory(rows);
        }
      })
      .catch(() => {
        if (!cancelled) {
          setHistory([]);
        }
      });

    return () => {
      cancelled = true;
    };
  }, [services]);

  const state = useMemo(
    () => buildPromotionGateState({ form, busy, phase, errorText, outcome, evaluation, history }),
    [busy, errorText, evaluation, form, history, outcome, phase]
  );

  const refreshHistory = useCallback(async () => {
    const rows = await services.getPromotionHistory();
    setHistory(rows);
  }, [services]);

  const updateField = useCallback((field: PromotionGateField, value: string) => {
    setForm((current) => ({ ...current, [field]: value }));
    setErrorText(null);
    setOutcome(null);

    if (field === "runId") {
      setEvaluation(null);
    }
  }, []);

  const evaluateGateChecks = useCallback(async () => {
    const runId = form.runId.trim();
    if (!runId) {
      setErrorText("Run id is required before evaluating gate checks.");
      return;
    }

    setBusy(true);
    setPhase("evaluating");
    setErrorText(null);
    setOutcome(null);

    try {
      const result = await services.evaluatePromotion(runId);
      setEvaluation(result);
    } catch (err) {
      setErrorText(toErrorMessage(err, "Evaluation failed."));
    } finally {
      setBusy(false);
      setPhase("idle");
    }
  }, [form.runId, services]);

  const promoteToPaper = useCallback(async () => {
    const validationError = validatePromotionApproval(form, evaluation);
    if (validationError) {
      setErrorText(validationError);
      setOutcome(null);
      return;
    }

    setBusy(true);
    setPhase("approving");
    setErrorText(null);
    setOutcome(null);

    try {
      const result = await services.approvePromotion(buildPromotionApprovalRequest(form));
      setOutcome(buildApprovalOutcome(result));
      await refreshHistory();
    } catch (err) {
      setErrorText(toErrorMessage(err, "Promotion approval failed."));
    } finally {
      setBusy(false);
      setPhase("idle");
    }
  }, [evaluation, form, refreshHistory, services]);

  const rejectPromotionDecision = useCallback(async () => {
    const validationError = validatePromotionRejection(form);
    if (validationError) {
      setErrorText(validationError);
      setOutcome(null);
      return;
    }

    setBusy(true);
    setPhase("rejecting");
    setErrorText(null);
    setOutcome(null);

    try {
      const result = await services.rejectPromotion(buildPromotionRejectionRequest(form));
      setOutcome(buildRejectionOutcome(result));
      await refreshHistory();

      if (result.success) {
        setForm((current) => ({ ...current, rejectionReason: "" }));
      }
    } catch (err) {
      setErrorText(toErrorMessage(err, "Promotion rejection failed."));
    } finally {
      setBusy(false);
      setPhase("idle");
    }
  }, [form, refreshHistory, services]);

  return {
    ...state,
    updateField,
    evaluateGateChecks,
    promoteToPaper,
    rejectPromotion: rejectPromotionDecision
  };
}

export function buildPromotionGateState({
  form,
  busy,
  phase,
  errorText,
  outcome,
  evaluation,
  history
}: BuildPromotionGateStateOptions): PromotionGateState {
  const trimmedForm = trimPromotionGateForm(form);
  const canEvaluate = !busy && Boolean(trimmedForm.runId);
  const canPromote = !busy && validatePromotionApproval(trimmedForm, evaluation) === null;
  const canReject = !busy && validatePromotionRejection(trimmedForm) === null;

  return {
    form,
    trimmedForm,
    evaluation,
    history,
    busy,
    phase,
    errorText,
    outcome,
    canEvaluate,
    canPromote,
    canReject,
    evaluateButtonLabel: phase === "evaluating" ? "Evaluating..." : "Evaluate gate checks",
    promoteButtonLabel: phase === "approving" ? "Promoting..." : "Confirm promote",
    rejectButtonLabel: phase === "rejecting" ? "Rejecting..." : "Reject promotion",
    nextActionText: buildNextActionText({ trimmedForm, evaluation, busy, phase }),
    approvalRequirementText: buildApprovalRequirementText(trimmedForm, evaluation),
    rejectionRequirementText: buildRejectionRequirementText(trimmedForm),
    historyEmptyText: "No promotion decisions recorded.",
    statusAnnouncement: buildPromotionStatusAnnouncement({ phase, errorText, outcome, evaluation, history })
  };
}

export function validatePromotionApproval(
  form: PromotionGateForm,
  evaluation: PromotionEvaluationResult | null
): string | null {
  const trimmedForm = trimPromotionGateForm(form);

  if (!evaluation) {
    return "Evaluate gate checks before confirming promotion.";
  }

  if (!evaluation.isEligible) {
    return evaluation.blockingReasons?.[0] ?? evaluation.reason ?? "Promotion gate is blocked.";
  }

  if (!trimmedForm.runId || !trimmedForm.approvedBy || !trimmedForm.approvalReason) {
    return "Run id, operator, and approval reason are required.";
  }

  return null;
}

export function validatePromotionRejection(form: PromotionGateForm): string | null {
  const trimmedForm = trimPromotionGateForm(form);

  if (!trimmedForm.runId || !trimmedForm.approvedBy || !trimmedForm.rejectionReason) {
    return "Run id, operator, and rejection reason are required.";
  }

  return null;
}

export function buildPromotionApprovalRequest(form: PromotionGateForm): ApprovePromotionRequest {
  const trimmedForm = trimPromotionGateForm(form);
  return {
    runId: trimmedForm.runId,
    approvedBy: trimmedForm.approvedBy,
    approvalReason: trimmedForm.approvalReason,
    reviewNotes: trimmedForm.reviewNotes || undefined,
    manualOverrideId: trimmedForm.manualOverrideId || undefined
  };
}

export function buildPromotionRejectionRequest(form: PromotionGateForm): RejectPromotionRequest {
  const trimmedForm = trimPromotionGateForm(form);
  return {
    runId: trimmedForm.runId,
    reason: trimmedForm.rejectionReason,
    rejectedBy: trimmedForm.approvedBy,
    reviewNotes: trimmedForm.reviewNotes || undefined,
    manualOverrideId: trimmedForm.manualOverrideId || undefined
  };
}

function trimPromotionGateForm(form: PromotionGateForm): PromotionGateForm {
  return {
    runId: form.runId.trim(),
    approvedBy: form.approvedBy.trim(),
    approvalReason: form.approvalReason.trim(),
    rejectionReason: form.rejectionReason.trim(),
    reviewNotes: form.reviewNotes.trim(),
    manualOverrideId: form.manualOverrideId.trim()
  };
}

function buildApprovalOutcome(result: PromotionDecisionResult): PromotionOutcome {
  return {
    level: result.success ? "success" : "error",
    message: result.success
      ? `Promoted. Promotion ID: ${result.promotionId ?? "n/a"}${result.auditReference ? ` · Audit reference ${result.auditReference}` : ""}`
      : result.reason
  };
}

function buildRejectionOutcome(result: PromotionDecisionResult): PromotionOutcome {
  return {
    level: result.success ? "warning" : "error",
    message: `${result.reason}${result.auditReference ? ` · Audit reference ${result.auditReference}` : ""}`
  };
}

function buildNextActionText({
  trimmedForm,
  evaluation,
  busy,
  phase
}: {
  trimmedForm: PromotionGateForm;
  evaluation: PromotionEvaluationResult | null;
  busy: boolean;
  phase: PromotionGatePhase;
}): string {
  if (busy) {
    if (phase === "evaluating") {
      return "Evaluating promotion gate checks.";
    }

    if (phase === "approving") {
      return "Writing promotion decision and refreshing audit trail.";
    }

    if (phase === "rejecting") {
      return "Writing rejection decision and refreshing audit trail.";
    }
  }

  if (!trimmedForm.runId) {
    return "Enter a backtest run id to evaluate promotion readiness.";
  }

  if (!evaluation) {
    return "Evaluate gate checks before approving or rejecting this run.";
  }

  if (!evaluation.isEligible) {
    return "Gate is blocked. Reject with rationale or review the blocking reason.";
  }

  if (!trimmedForm.approvedBy || !trimmedForm.approvalReason) {
    return "Add operator and approval reason before confirming promotion.";
  }

  return "Promotion trace is ready for confirmation.";
}

function buildApprovalRequirementText(
  trimmedForm: PromotionGateForm,
  evaluation: PromotionEvaluationResult | null
): string {
  if (!evaluation) {
    return "Approval remains disabled until gate checks return an eligible result.";
  }

  if (!evaluation.isEligible) {
    return evaluation.blockingReasons?.[0] ?? evaluation.reason ?? "Gate checks did not return eligible.";
  }

  if (!trimmedForm.approvedBy || !trimmedForm.approvalReason) {
    return "Approval requires an operator id and approval reason.";
  }

  return "Approval request includes run id, operator, rationale, and optional audit notes.";
}

function buildRejectionRequirementText(trimmedForm: PromotionGateForm): string {
  if (!trimmedForm.runId || !trimmedForm.approvedBy || !trimmedForm.rejectionReason) {
    return "Rejection requires run id, operator id, and rejection reason.";
  }

  return "Rejection request is ready to write an audit-linked decision.";
}

function buildPromotionStatusAnnouncement({
  phase,
  errorText,
  outcome,
  evaluation,
  history
}: {
  phase: PromotionGatePhase;
  errorText: string | null;
  outcome: PromotionOutcome | null;
  evaluation: PromotionEvaluationResult | null;
  history: PromotionRecord[];
}): string {
  if (phase === "evaluating") {
    return "Evaluating promotion gate checks.";
  }

  if (phase === "approving") {
    return "Writing promotion approval.";
  }

  if (phase === "rejecting") {
    return "Writing promotion rejection.";
  }

  if (errorText) {
    return `Promotion gate failed: ${errorText}`;
  }

  if (outcome) {
    return "Promotion gate command completed.";
  }

  if (evaluation) {
    return evaluation.isEligible
      ? "Promotion gate checks returned eligible."
      : "Promotion gate checks returned blocked.";
  }

  if (history.length > 0) {
    return `${history.length} promotion decision ${history.length === 1 ? "record" : "records"} loaded.`;
  }

  return "";
}

function toErrorMessage(err: unknown, fallback: string): string {
  if (err instanceof Error && err.message.trim()) {
    return err.message;
  }

  return fallback;
}
