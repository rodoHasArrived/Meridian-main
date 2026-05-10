import type {
  BackfillProgressResponse,
  BackfillTriggerRequest,
  BackfillTriggerResult,
  AlpacaBrokerageConnectionRequest,
  BrokerageConnectionStatus,
  BrokerageHouseholdPortfolio,
  CorporateAction,
  DataOperationsWorkspaceResponse,
  EquityCurveSummary,
  EvidenceCompleteness,
  EvidenceGraph,
  EvidencePacket,
  EvidencePacketExportRequest,
  EvidencePacketExportResponse,
  EvidenceSubject,
  EvidenceTemplate,
  ExportAnalysisResult,
  ExecutionControlSnapshot,
  ExecutionAuditEntry,
  GovernanceWorkspaceResponse,
  LedgerJournalLine,
  LedgerSummary,
  LedgerTrialBalanceLine,
  MetricSnapshot,
  OperatorInbox,
  OrderResult,
  OrderSubmitRequest,
  PaperSessionSummary,
  PaperSessionDetail,
  PaperSessionReplayVerification,
  PromotionDecisionResult,
  PromotionEvaluationResult,
  PromotionRecord,
  ReconciliationBreakQueueItem,
  ReconciliationCalibrationSummary,
  ResolveReconciliationBreakRequest,
  ResolveConflictRequest,
  ReviewReconciliationBreakRequest,
  ResearchRunRecord,
  ResearchWorkspaceResponse,
  RunAttributionSummary,
  RunComparisonRow,
  RunDiff,
  RunFillSummary,
  SecurityIdentityDrillIn,
  SecurityMasterConflict,
  SecurityMasterEntry,
  SessionInfo,
  SystemEventRecord,
  SystemOverviewResponse,
  ReplayFileRecord,
  ReplayStatus,
  TradingActionResult,
  TradingOperatorReadiness,
  TradingParameters,
  TradingWorkspaceResponse,
  WorkflowLibrary,
  WorkflowPreset,
  WorkflowPresetLibrary,
  WorkflowPresetSaveRequest,
  CreateExecutionManualOverrideRequest,
  ExecutionManualOverride
} from "@/types";
import {
  EXECUTION_API_ENDPOINTS,
  PROMOTION_API_ENDPOINTS,
  REPLAY_API_ENDPOINTS,
  WORKSTATION_API_ENDPOINTS,
  executionAuditEndpoint,
  executionManualOverrideClearEndpoint,
  executionOrderCancelEndpoint,
  executionPositionCloseEndpoint,
  executionSessionCloseEndpoint,
  executionSessionEndpoint,
  executionSessionReplayEndpoint,
  portfolioHouseholdEndpoint,
  promotionEvaluateEndpoint,
  replayFilesEndpoint,
  replaySessionActionEndpoint,
  workstationEvidenceExportManifestEndpoint,
  workstationEvidenceGraphEndpoint,
  workstationEvidencePacketEndpoint,
  workstationEvidenceValidateEndpoint,
  workstationOperatorInboxEndpoint,
  workstationRunContinuityEndpoint,
  workstationRunHistoryEndpoint,
  workstationRunLedgerEndpoint,
  workstationRunLedgerJournalEndpoint,
  workstationRunReconciliationEndpoint,
  workstationRunReconciliationHistoryEndpoint,
  workstationRunReviewPacketEndpoint,
  workstationRunSweepsEndpoint,
  workstationRunTimelineEndpoint,
  workstationWorkflowSummaryEndpoint,
  workstationWorkflowPresetEndpoint,
  workstationWorkflowPresetPinEndpoint,
  workstationWorkflowPresetUsedEndpoint
} from "@/lib/workstation-endpoints";

export const developmentFixtureHeader = "x-meridian-dev-fixture";

let developmentFixtureUsage = false;

export function resetDevelopmentFixtureUsage() {
  developmentFixtureUsage = false;
}

export function hasDevelopmentFixtureUsage() {
  return developmentFixtureUsage;
}

async function getJson<T>(path: string): Promise<T> {
  const response = await fetch(path, {
    headers: {
      Accept: "application/json"
    }
  });

  if (!response.ok) {
    const fixture = await getDevelopmentFallback<T>(path, response.status);
    if (fixture !== undefined) {
      markDevelopmentFixtureUsage();
      return fixture;
    }

    throw new Error(`Request failed for ${path} (${response.status})`);
  }

  if (response.headers?.get?.(developmentFixtureHeader) === "true") {
    markDevelopmentFixtureUsage();
  }

  return response.json() as Promise<T>;
}

const developmentFallbackStatuses = new Set([404, 500, 502, 503, 504]);

async function getDevelopmentFallback<T>(path: string, status: number): Promise<T | undefined> {
  if (!import.meta.env.DEV || !developmentFallbackStatuses.has(status)) {
    return undefined;
  }

  const { resolveDevFixture } = await import("@/lib/dev-fixtures");
  return resolveDevFixture<T>(path);
}

function markDevelopmentFixtureUsage() {
  developmentFixtureUsage = true;
}

async function postJson<T>(path: string, body?: unknown): Promise<T> {
  const response = await fetch(path, {
    method: "POST",
    headers: {
      Accept: "application/json",
      "Content-Type": "application/json"
    },
    body: body !== undefined ? JSON.stringify(body) : undefined
  });

  if (!response.ok) {
    let errorDetail = "";
    try {
      const errBody = await response.text();
      errorDetail = errBody ? ` — ${errBody}` : "";
    } catch {
      // ignore parse failures
    }

    throw new Error(`Request failed for ${path} (${response.status})${errorDetail}`);
  }

  const text = await response.text();
  return (text ? JSON.parse(text) : null) as T;
}

async function putJson<T>(path: string, body?: unknown): Promise<T> {
  const response = await fetch(path, {
    method: "PUT",
    headers: {
      Accept: "application/json",
      "Content-Type": "application/json"
    },
    body: body !== undefined ? JSON.stringify(body) : undefined
  });

  if (!response.ok) {
    throw new Error(`Request failed for ${path} (${response.status})`);
  }

  const text = await response.text();
  return (text ? JSON.parse(text) : null) as T;
}

async function deleteJson<T>(path: string): Promise<T> {
  const response = await fetch(path, {
    method: "DELETE",
    headers: {
      Accept: "application/json"
    }
  });

  if (!response.ok) {
    throw new Error(`Request failed for ${path} (${response.status})`);
  }

  const text = await response.text();
  return (text ? JSON.parse(text) : null) as T;
}

function queryString(params: Record<string, string | number | boolean | null | undefined>) {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value !== null && value !== undefined && value !== "") {
      search.set(key, String(value));
    }
  }

  const value = search.toString();
  return value ? `?${value}` : "";
}

async function getDevelopmentSearchFallback(query: string, take: number, activeOnly: boolean) {
  if (!import.meta.env.DEV) {
    return undefined;
  }

  const { searchDevSecurityMasterEntries } = await import("@/lib/dev-fixtures");
  return searchDevSecurityMasterEntries(query, take, activeOnly);
}

export function getSession() {
  return getJson<SessionInfo>(WORKSTATION_API_ENDPOINTS.session);
}

export function getStrategyWorkspace() {
  return getJson<ResearchWorkspaceResponse>(WORKSTATION_API_ENDPOINTS.strategy);
}

export function getResearchWorkspace() {
  return getStrategyWorkspace();
}

export function getTradingWorkspace() {
  return getJson<TradingWorkspaceResponse>(WORKSTATION_API_ENDPOINTS.trading);
}

export function getTradingReadiness() {
  return getJson<TradingOperatorReadiness>(WORKSTATION_API_ENDPOINTS.tradingReadiness);
}

export function getOperatorInbox(fundAccountId?: string) {
  return getJson<OperatorInbox>(workstationOperatorInboxEndpoint(fundAccountId));
}

export function getWorkstationWorkflowSummary(options: {
  hasOperatingContext?: boolean;
  operatingContext?: string;
  fundProfileId?: string;
  fundDisplayName?: string;
} = {}) {
  return getJson<unknown>(workstationWorkflowSummaryEndpoint(options));
}

export function getWorkflowLibrary() {
  return getJson<WorkflowLibrary>(WORKSTATION_API_ENDPOINTS.workflowLibrary);
}

export function getWorkflowPresets() {
  return getJson<WorkflowPresetLibrary>(WORKSTATION_API_ENDPOINTS.workflowPresets);
}

export function getEvidenceSubjects() {
  return getJson<EvidenceSubject[]>(WORKSTATION_API_ENDPOINTS.evidenceSubjects);
}

export function getEvidencePacket(subjectKind: string, subjectId: string) {
  return getJson<EvidencePacket>(workstationEvidencePacketEndpoint(subjectKind, subjectId));
}

export function getEvidenceGraph(subjectKind: string, subjectId: string) {
  return getJson<EvidenceGraph>(workstationEvidenceGraphEndpoint(subjectKind, subjectId));
}

export function validateEvidencePacket(subjectKind: string, subjectId: string) {
  return postJson<EvidenceCompleteness>(workstationEvidenceValidateEndpoint(subjectKind, subjectId));
}

export function exportEvidenceManifest(
  subjectKind: string,
  subjectId: string,
  request: EvidencePacketExportRequest = { includeWarnings: true }
) {
  return postJson<EvidencePacketExportResponse>(workstationEvidenceExportManifestEndpoint(subjectKind, subjectId), request);
}

export function getEvidenceTemplates() {
  return getJson<EvidenceTemplate[]>(WORKSTATION_API_ENDPOINTS.evidenceTemplates);
}

export function saveWorkflowPreset(request: WorkflowPresetSaveRequest) {
  return postJson<WorkflowPreset>(workstationWorkflowPresetEndpoint(), request);
}

export function updateWorkflowPreset(presetId: string, request: WorkflowPresetSaveRequest) {
  return putJson<WorkflowPreset>(workstationWorkflowPresetEndpoint(presetId), request);
}

export function pinWorkflowPreset(presetId: string, isPinned: boolean) {
  return postJson<WorkflowPreset>(workstationWorkflowPresetPinEndpoint(presetId), { isPinned });
}

export function markWorkflowPresetUsed(presetId: string) {
  return postJson<WorkflowPreset>(workstationWorkflowPresetUsedEndpoint(presetId));
}

export function deleteWorkflowPreset(presetId: string) {
  return deleteJson<void>(workstationWorkflowPresetEndpoint(presetId));
}

export function getDataWorkspace() {
  return getJson<DataOperationsWorkspaceResponse>(WORKSTATION_API_ENDPOINTS.data);
}

export function getDataOperationsWorkspace() {
  return getDataWorkspace();
}

export function getGovernanceWorkspace() {
  return getJson<GovernanceWorkspaceResponse>(WORKSTATION_API_ENDPOINTS.accounting);
}

export function getReportingWorkspace() {
  return getJson<GovernanceWorkspaceResponse>(WORKSTATION_API_ENDPOINTS.reporting);
}

export function runAnalysisExport(profileId: string) {
  return postJson<ExportAnalysisResult>("/api/export/analysis", { profileId });
}

// --- Promotion workflow ---

export function evaluatePromotion(runId: string) {
  return getJson<PromotionEvaluationResult>(promotionEvaluateEndpoint(runId));
}

export interface ApprovePromotionRequest {
  runId: string;
  approvedBy: string;
  approvalReason: string;
  reviewNotes?: string;
  manualOverrideId?: string;
}

export function approvePromotion(request: ApprovePromotionRequest) {
  return postJson<PromotionDecisionResult>(PROMOTION_API_ENDPOINTS.approve, request);
}

export interface RejectPromotionRequest {
  runId: string;
  reason: string;
  rejectedBy?: string;
  reviewNotes?: string;
  manualOverrideId?: string;
}

export function rejectPromotion(request: RejectPromotionRequest) {
  return postJson<PromotionDecisionResult>(PROMOTION_API_ENDPOINTS.reject, request);
}

export function getPromotionHistory() {
  return getJson<PromotionRecord[]>(PROMOTION_API_ENDPOINTS.history);
}

// --- Order management ---

export function submitOrder(request: OrderSubmitRequest) {
  return postJson<OrderResult>(EXECUTION_API_ENDPOINTS.ordersSubmit, request);
}

export function cancelOrder(orderId: string) {
  return postJson<TradingActionResult>(executionOrderCancelEndpoint(orderId));
}

export function cancelAllOrders() {
  return postJson<TradingActionResult>(EXECUTION_API_ENDPOINTS.ordersCancelAll);
}

export function closePosition(symbol: string) {
  return postJson<TradingActionResult>(executionPositionCloseEndpoint(symbol));
}

// --- Paper session management ---

export function getExecutionSessions() {
  return getJson<PaperSessionSummary[]>(EXECUTION_API_ENDPOINTS.sessions);
}

export function createPaperSession(strategyId: string, strategyName: string | null, initialCash: number) {
  return postJson<PaperSessionSummary>(EXECUTION_API_ENDPOINTS.sessionsCreate, {
    strategyId,
    strategyName,
    initialCash
  });
}

export function closePaperSession(sessionId: string) {
  return postJson<TradingActionResult>(executionSessionCloseEndpoint(sessionId));
}

export function getPaperSessionDetail(sessionId: string) {
  return getJson<PaperSessionDetail>(executionSessionEndpoint(sessionId));
}

export function getPaperSessionReplayVerification(sessionId: string) {
  return getJson<PaperSessionReplayVerification>(executionSessionReplayEndpoint(sessionId));
}

export function getExecutionAudit(take = 20) {
  return getJson<ExecutionAuditEntry[]>(executionAuditEndpoint(take));
}

export function getExecutionControls() {
  return getJson<ExecutionControlSnapshot>(EXECUTION_API_ENDPOINTS.controls);
}

export function createExecutionManualOverride(request: CreateExecutionManualOverrideRequest) {
  return postJson<ExecutionManualOverride>(EXECUTION_API_ENDPOINTS.manualOverrides, request);
}

export function clearExecutionManualOverride(overrideId: string) {
  return postJson<TradingActionResult>(executionManualOverrideClearEndpoint(overrideId));
}

// --- Strategy lifecycle ---

export function pauseStrategy(strategyId: string) {
  return postJson<{ strategyId: string; action: string; success: boolean; reason: string | null }>(
    `/api/strategies/${encodeURIComponent(strategyId)}/pause`
  );
}

export function stopStrategy(strategyId: string) {
  return postJson<{ strategyId: string; action: string; success: boolean; reason: string | null }>(
    `/api/strategies/${encodeURIComponent(strategyId)}/stop`
  );
}

// --- Replay controls ---

export function getReplayFiles(symbol?: string) {
  return getJson<{ files: ReplayFileRecord[]; total: number; timestamp: string }>(replayFilesEndpoint(symbol));
}

export function startReplay(filePath: string, speedMultiplier = 1) {
  return postJson<{ sessionId: string; filePath: string; status: string; speedMultiplier: number }>(
    REPLAY_API_ENDPOINTS.start,
    { filePath, speedMultiplier }
  );
}

export function pauseReplay(sessionId: string) {
  return postJson<{ sessionId: string; status: string; eventsProcessed: number }>(replaySessionActionEndpoint(sessionId, "pause"));
}

export function resumeReplay(sessionId: string) {
  return postJson<{ sessionId: string; status: string; eventsProcessed: number }>(replaySessionActionEndpoint(sessionId, "resume"));
}

export function stopReplay(sessionId: string) {
  return postJson<{ sessionId: string; status: string; eventsProcessed: number }>(replaySessionActionEndpoint(sessionId, "stop"));
}

export function seekReplay(sessionId: string, positionMs: number) {
  return postJson<{ sessionId: string; positionMs: number; status: string }>(replaySessionActionEndpoint(sessionId, "seek"), { positionMs });
}

export function setReplaySpeed(sessionId: string, speedMultiplier: number) {
  return postJson<{ sessionId: string; speedMultiplier: number; status: string }>(
    replaySessionActionEndpoint(sessionId, "speed"),
    { speedMultiplier }
  );
}

export function getReplayStatus(sessionId: string) {
  return getJson<ReplayStatus>(replaySessionActionEndpoint(sessionId, "status"));
}

// --- Strategy runs ---

export function getStrategyRuns(strategyId: string, type?: "backtest" | "paper" | "live") {
  const params = type ? `?type=${encodeURIComponent(type)}` : "";
  return getJson<ResearchRunRecord[]>(`/api/strategies/${encodeURIComponent(strategyId)}/runs${params}`);
}

// --- Multi-run comparison and diff ---

export function compareRuns(runIds: string[]) {
  return postJson<RunComparisonRow[]>("/api/workstation/runs/compare", { runIds });
}

export function diffRuns(baseRunId: string, targetRunId: string) {
  return postJson<RunDiff>("/api/workstation/runs/diff", { baseRunId, targetRunId });
}

// --- Run detail drill-ins ---

export function getRunAttribution(runId: string) {
  return getJson<RunAttributionSummary>(`/api/workstation/runs/${encodeURIComponent(runId)}/attribution`);
}

export function getRunFills(runId: string, symbol?: string) {
  const params = symbol ? `?symbol=${encodeURIComponent(symbol)}` : "";
  return getJson<RunFillSummary>(`/api/workstation/runs/${encodeURIComponent(runId)}/fills${params}`);
}

export function getRunEquityCurve(runId: string) {
  return getJson<EquityCurveSummary>(`/api/workstation/runs/${encodeURIComponent(runId)}/equity-curve`);
}

export function getRunLedger(runId: string) {
  return getJson<LedgerSummary>(workstationRunLedgerEndpoint(runId));
}

export function getRunTrialBalance(runId: string, accountType?: string) {
  const params = accountType ? `?accountType=${encodeURIComponent(accountType)}` : "";
  return getJson<LedgerTrialBalanceLine[]>(`/api/workstation/runs/${encodeURIComponent(runId)}/ledger/trial-balance${params}`);
}

export function getRunLedgerJournal(runId: string, options: { from?: string; to?: string } = {}) {
  const params = queryString(options);
  return getJson<LedgerJournalLine[]>(`${workstationRunLedgerJournalEndpoint(runId)}${params}`);
}

export function getRunContinuity(runId: string) {
  return getJson<unknown>(workstationRunContinuityEndpoint(runId));
}

export function getRunReviewPacketPath(runId: string, fundAccountId?: string) {
  return workstationRunReviewPacketEndpoint(runId, fundAccountId);
}

export function getRunReviewPacket(runId: string, fundAccountId?: string) {
  return getJson<unknown>(getRunReviewPacketPath(runId, fundAccountId));
}

export function getRunReconciliation(runId: string) {
  return getJson<unknown>(workstationRunReconciliationEndpoint(runId));
}

export function getRunReconciliationHistory(runId: string) {
  return getJson<unknown>(workstationRunReconciliationHistoryEndpoint(runId));
}

export function getRunHistory(options: { mode?: string; status?: string; limit?: number } = {}) {
  return getJson<unknown>(workstationRunHistoryEndpoint(options));
}

export function getRunTimeline(options: { mode?: string; status?: string; strategyId?: string; limit?: number } = {}) {
  return getJson<unknown>(workstationRunTimelineEndpoint(options));
}

export function getRunSweeps(limit?: number) {
  return getJson<unknown>(workstationRunSweepsEndpoint(limit));
}

// --- Security Master search ---

export async function searchSecurities(query: string, take = 25, activeOnly = true) {
  const params = new URLSearchParams({
    query,
    take: String(take),
    activeOnly: String(activeOnly)
  });
  const path = `/api/workstation/security-master/securities?${params.toString()}`;
  const results = await getJson<SecurityMasterEntry[]>(path);

  if (import.meta.env.DEV && results.length === 0) {
    const fixtureResults = await getDevelopmentSearchFallback(query, take, activeOnly);
    if (fixtureResults && fixtureResults.length > 0) {
      return fixtureResults;
    }
  }

  return results;
}

export function getSecurityDetail(securityId: string) {
  return getJson<SecurityMasterEntry>(`/api/workstation/security-master/securities/${encodeURIComponent(securityId)}`);
}

export function getSecurityIdentity(securityId: string) {
  return getJson<SecurityIdentityDrillIn>(`/api/workstation/security-master/securities/${encodeURIComponent(securityId)}/identity`);
}

export function getSecurityHistory(securityId: string) {
  return getJson<unknown>(`/api/workstation/security-master/securities/${encodeURIComponent(securityId)}/history`);
}

export function getSecurityEconomicDefinition(securityId: string) {
  return getJson<unknown>(`/api/workstation/security-master/securities/${encodeURIComponent(securityId)}/economic-definition`);
}

export function getSecurityTrustSnapshot(securityId: string) {
  return getJson<unknown>(`/api/workstation/security-master/securities/${encodeURIComponent(securityId)}/trust-snapshot`);
}

export function createSecurityMasterEntry(request: Record<string, unknown>) {
  return postJson<SecurityMasterEntry>("/api/security-master", request);
}

export function amendSecurityMasterEntry(request: Record<string, unknown>) {
  return postJson<SecurityMasterEntry>("/api/security-master/amend", request);
}

export function upsertSecurityAlias(request: Record<string, unknown>) {
  return postJson<Record<string, unknown>>("/api/security-master/aliases/upsert", request);
}

// --- Security Master corporate actions and trading parameters ---

export function getCorporateActions(securityId: string) {
  return getJson<CorporateAction[]>(`/api/security-master/${encodeURIComponent(securityId)}/corporate-actions`);
}

export function getTradingParameters(securityId: string) {
  return getJson<TradingParameters>(`/api/security-master/${encodeURIComponent(securityId)}/trading-parameters`);
}

// --- Security Master conflicts ---

export function getSecurityConflicts() {
  return getJson<SecurityMasterConflict[]>("/api/security-master/conflicts");
}

export function resolveSecurityConflict(request: ResolveConflictRequest) {
  return postJson<SecurityMasterConflict>(
    `/api/security-master/conflicts/${encodeURIComponent(request.conflictId)}/resolve`,
    request
  );
}

export function bulkResolveSecurityConflicts(request: Record<string, unknown>) {
  return postJson<unknown>("/api/workstation/security-master/conflicts/bulk-resolve", request);
}

export function runReconciliation(request: Record<string, unknown>) {
  return postJson<unknown>("/api/workstation/reconciliation/runs", request);
}

export function getReconciliationRun(reconciliationRunId: string) {
  return getJson<unknown>(`/api/workstation/reconciliation/runs/${encodeURIComponent(reconciliationRunId)}`);
}

export function getReconciliationBreakQueue(status?: string, fundAccountId?: string) {
  const search = new URLSearchParams();
  if (status) search.set("status", status);
  if (fundAccountId) search.set("fundAccountId", fundAccountId);
  const params = search.toString() ? `?${search.toString()}` : "";
  return getJson<ReconciliationBreakQueueItem[]>(`/api/workstation/reconciliation/break-queue${params}`);
}

export function getReconciliationBreakDetail(breakId: string) {
  return getJson<ReconciliationBreakQueueItem>(`/api/workstation/reconciliation/break-queue/${encodeURIComponent(breakId)}`);
}

export function getReconciliationBreakAudit(breakId: string) {
  return getJson<unknown>(`/api/workstation/reconciliation/break-queue/${encodeURIComponent(breakId)}/audit`);
}

export function reviewReconciliationBreak(request: ReviewReconciliationBreakRequest) {
  return postJson<ReconciliationBreakQueueItem>(
    `/api/workstation/reconciliation/break-queue/${encodeURIComponent(request.breakId)}/review`,
    request
  );
}

export function resolveReconciliationBreak(request: ResolveReconciliationBreakRequest) {
  return postJson<ReconciliationBreakQueueItem>(
    `/api/workstation/reconciliation/break-queue/${encodeURIComponent(request.breakId)}/resolve`,
    request
  );
}

export function getReconciliationCalibrationSummary() {
  return getJson<ReconciliationCalibrationSummary>("/api/workstation/reconciliation/calibration-summary");
}

// --- Backfill mutations ---

export function getBackfillProgress() {
  return getJson<BackfillProgressResponse>("/api/backfill/progress");
}

export function triggerBackfill(request: BackfillTriggerRequest) {
  return postJson<BackfillTriggerResult>("/api/backfill/run", request);
}

export function previewBackfill(request: BackfillTriggerRequest) {
  return postJson<BackfillTriggerResult>("/api/backfill/run/preview", request);
}

// --- Provider management ---

export function setupProvider(request: import("@/types").ProviderSetupRequest) {
  return postJson<import("@/types").ProviderSetupResult>("/api/providers/configure", request);
}

export function removeProvider(providerId: string) {
  return postJson<{ success: boolean; message: string }>(`/api/providers/${encodeURIComponent(providerId)}/remove`);
}

export function testProviderConnection(providerId: string) {
  return postJson<{ success: boolean; latency: string | null; message: string }>(`/api/providers/${encodeURIComponent(providerId)}/test`);
}

// --- System overview ---

export function getSystemStatus() {
  return getJson<unknown>(WORKSTATION_API_ENDPOINTS.systemStatus).then(normalizeSystemOverviewResponse);
}

function normalizeSystemOverviewResponse(payload: unknown): SystemOverviewResponse {
  if (!isRecord(payload)) {
    return fallbackSystemOverview();
  }

  if ("systemStatus" in payload) {
    const heartbeat = readString(payload.lastHeartbeatUtc) ?? readString(payload.timestampUtc) ?? new Date().toISOString();
    return {
      systemStatus: readSystemStatus(payload.systemStatus),
      providersOnline: readNumber(payload.providersOnline) ?? 0,
      providersTotal: readNumber(payload.providersTotal) ?? 0,
      activeRuns: readNumber(payload.activeRuns) ?? 0,
      openPositions: readNumber(payload.openPositions) ?? 0,
      activeBackfills: readNumber(payload.activeBackfills) ?? 0,
      symbolsMonitored: readNumber(payload.symbolsMonitored) ?? 0,
      storageHealth: readStorageHealth(payload.storageHealth),
      lastHeartbeatUtc: heartbeat,
      metrics: Array.isArray(payload.metrics) ? payload.metrics as MetricSnapshot[] : [],
      recentEvents: Array.isArray(payload.recentEvents) ? payload.recentEvents as SystemEventRecord[] : []
    };
  }

  return normalizeLegacyStatusResponse(payload);
}

function normalizeLegacyStatusResponse(payload: Record<string, unknown>): SystemOverviewResponse {
  const metrics = isRecord(payload.metrics) ? payload.metrics : {};
  const pipeline = isRecord(payload.pipeline) ? payload.pipeline : {};
  const isConnected = readBoolean(payload.isConnected) ?? false;
  const timestampUtc = readString(payload.timestampUtc) ?? readString(metrics.lastUpdatedUtc) ?? new Date().toISOString();
  const published = readNumber(metrics.published) ?? readNumber(pipeline.publishedCount) ?? 0;
  const dropped = readNumber(metrics.dropped) ?? readNumber(pipeline.droppedCount) ?? 0;
  const eventsPerSecond = readNumber(metrics.eventsPerSecond);
  const queueSize = readNumber(pipeline.currentQueueSize) ?? 0;
  const queueCapacity = readNumber(pipeline.queueCapacity) ?? 0;
  const queueUtilization = readNumber(pipeline.queueUtilization) ?? 0;
  const isStale = readBoolean(metrics.isStale) ?? false;
  const dropRate = readNumber(metrics.dropRate) ?? 0;
  const systemStatus = deriveSystemStatus(isConnected, isStale, dropped, dropRate, queueUtilization);
  const storageHealth = deriveStorageHealth(systemStatus, dropped, queueUtilization);
  const sourceProvider = readString(metrics.sourceProvider);
  const providersTotal = sourceProvider || isConnected ? 1 : 0;
  const providersOnline = isConnected && providersTotal > 0 ? 1 : 0;

  return {
    systemStatus,
    providersOnline,
    providersTotal,
    activeRuns: 0,
    openPositions: 0,
    activeBackfills: 0,
    symbolsMonitored: 0,
    storageHealth,
    lastHeartbeatUtc: timestampUtc,
    metrics: [
      {
        id: "events",
        label: "Events Published",
        value: formatMetricNumber(published),
        delta: eventsPerSecond === null ? "Rate unavailable" : `${formatMetricNumber(eventsPerSecond)} / sec`,
        tone: systemStatus === "Offline" ? "danger" : "default"
      },
      {
        id: "drops",
        label: "Dropped Events",
        value: formatMetricNumber(dropped),
        delta: `${formatMetricNumber(dropRate)} drop rate`,
        tone: dropped > 0 || dropRate > 0 ? "warning" : "success"
      },
      {
        id: "queue",
        label: "Pipeline Queue",
        value: queueCapacity > 0 ? `${formatMetricNumber(queueSize)} / ${formatMetricNumber(queueCapacity)}` : formatMetricNumber(queueSize),
        delta: `${Math.round(queueUtilization * 100)}% utilized`,
        tone: queueUtilization >= 0.8 ? "warning" : "success"
      },
      {
        id: "historical-bars",
        label: "Historical Bars",
        value: formatMetricNumber(readNumber(metrics.historicalBars) ?? 0),
        delta: `${formatMetricNumber(readNumber(metrics.trades) ?? 0)} trades, ${formatMetricNumber(readNumber(metrics.depthUpdates) ?? 0)} depth updates`,
        tone: "default"
      }
    ],
    recentEvents: [
      {
        id: "host-status",
        type: systemStatus === "Offline" ? "error" : systemStatus === "Degraded" ? "warning" : "info",
        message: buildLegacyStatusMessage(systemStatus, readString(payload.uptime), sourceProvider),
        source: "Meridian host",
        timestamp: timestampUtc
      }
    ]
  };
}

function fallbackSystemOverview(): SystemOverviewResponse {
  const timestampUtc = new Date().toISOString();
  return {
    systemStatus: "Degraded",
    providersOnline: 0,
    providersTotal: 0,
    activeRuns: 0,
    openPositions: 0,
    activeBackfills: 0,
    symbolsMonitored: 0,
    storageHealth: "Warning",
    lastHeartbeatUtc: timestampUtc,
    metrics: [],
    recentEvents: [
      {
        id: "status-unavailable",
        type: "warning",
        message: "The host returned an unrecognized status payload.",
        source: "Meridian host",
        timestamp: timestampUtc
      }
    ]
  };
}

function deriveSystemStatus(
  isConnected: boolean,
  isStale: boolean,
  dropped: number,
  dropRate: number,
  queueUtilization: number
): SystemOverviewResponse["systemStatus"] {
  if (!isConnected) {
    return "Offline";
  }

  return isStale || dropped > 0 || dropRate > 0 || queueUtilization >= 0.8 ? "Degraded" : "Healthy";
}

function deriveStorageHealth(
  systemStatus: SystemOverviewResponse["systemStatus"],
  dropped: number,
  queueUtilization: number
): SystemOverviewResponse["storageHealth"] {
  if (systemStatus === "Offline") {
    return "Critical";
  }

  return dropped > 0 || queueUtilization >= 0.8 ? "Warning" : "Healthy";
}

function buildLegacyStatusMessage(
  systemStatus: SystemOverviewResponse["systemStatus"],
  uptime: string | null,
  sourceProvider: string | null
): string {
  const provider = sourceProvider ?? "local host pipeline";
  const suffix = uptime ? ` Uptime ${uptime}.` : "";

  if (systemStatus === "Offline") {
    return `Host connectivity is offline for ${provider}.${suffix}`;
  }

  if (systemStatus === "Degraded") {
    return `Host status is degraded for ${provider}.${suffix}`;
  }

  return `Host status is healthy for ${provider}.${suffix}`;
}

function readSystemStatus(value: unknown): SystemOverviewResponse["systemStatus"] {
  return value === "Healthy" || value === "Degraded" || value === "Offline" ? value : "Degraded";
}

function readStorageHealth(value: unknown): SystemOverviewResponse["storageHealth"] {
  return value === "Healthy" || value === "Warning" || value === "Critical" ? value : "Warning";
}

function readNumber(value: unknown): number | null {
  return typeof value === "number" && Number.isFinite(value) ? value : null;
}

function readString(value: unknown): string | null {
  return typeof value === "string" && value.trim() ? value : null;
}

function readBoolean(value: unknown): boolean | null {
  return typeof value === "boolean" ? value : null;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}

function formatMetricNumber(value: number): string {
  return value.toLocaleString(undefined, { maximumFractionDigits: value >= 10 ? 0 : 2 });
}

// --- Symbol management ---

export function getSymbols() {
  return getJson<import("@/types").SymbolRecord[]>("/api/symbols");
}

export function getSymbolsStatistics() {
  return getJson<import("@/types").SymbolStatistics>("/api/symbols/statistics");
}

export function searchSymbolsQuery(query: string) {
  return getJson<import("@/types").SymbolRecord[]>(`/api/symbols/search?query=${encodeURIComponent(query)}`);
}

export function addSymbol(symbol: string, provider?: string) {
  return postJson<{ success: boolean; symbol: string }>("/api/symbols/add", { symbol, provider: provider ?? null });
}

export function removeSymbol(symbol: string) {
  return postJson<{ success: boolean; symbol: string }>(`/api/symbols/${encodeURIComponent(symbol)}/remove`);
}

export function archiveSymbol(symbol: string) {
  return postJson<{ success: boolean; symbol: string }>(`/api/symbols/${encodeURIComponent(symbol)}/archive`);
}

export function bulkAddSymbols(symbols: string[]) {
  return postJson<{ added: number; skipped: number; errors: string[] }>("/api/symbols/bulk-add", { symbols });
}

// --- Quality monitoring ---

export function getQualityDashboard() {
  return getJson<import("@/types").QualityDashboardResponse>("/api/quality/dashboard");
}

export function getQualityGaps() {
  return getJson<import("@/types").QualityGapEntry[]>("/api/quality/gaps");
}

export function getQualityAnomalies() {
  return getJson<import("@/types").QualityAnomalyEntry[]>("/api/quality/anomalies");
}

export function acknowledgeAnomaly(anomalyId: string) {
  return postJson<void>(`/api/quality/anomalies/${encodeURIComponent(anomalyId)}/acknowledge`);
}

export function getQualityCompleteness() {
  return getJson<Array<{ symbol: string; score: number; sampledAt: string }>>("/api/quality/completeness");
}

export function getRobinhoodConnectionStatus() {
  return getJson<BrokerageConnectionStatus>("/api/brokerage-connections/robinhood/status");
}

export function startRobinhoodConnection() {
  return postJson<BrokerageConnectionStatus>("/api/brokerage-connections/robinhood/connect");
}

export function revokeRobinhoodConnection() {
  return deleteJson<BrokerageConnectionStatus>("/api/brokerage-connections/robinhood");
}

export function getPortfolioWorkspace() {
  return getJson<import("@/types").PortfolioWorkspaceResponse>(WORKSTATION_API_ENDPOINTS.portfolio);
}

export function getAlpacaConnectionStatus() {
  return getJson<BrokerageConnectionStatus>("/api/brokerage-connections/alpaca/status");
}

export function connectAlpacaConnection(request: AlpacaBrokerageConnectionRequest) {
  return postJson<BrokerageConnectionStatus>("/api/brokerage-connections/alpaca/connect", request);
}

export function revokeAlpacaConnection() {
  return deleteJson<BrokerageConnectionStatus>("/api/brokerage-connections/alpaca");
}

export function getBrokerageHouseholdPortfolio(provider = "alpaca") {
  return getJson<BrokerageHouseholdPortfolio>(portfolioHouseholdEndpoint(provider));
}

export function getPortfolioAggregate() {
  return getJson<unknown>("/api/portfolio/aggregate");
}

export function getPortfolioExposure() {
  return getJson<unknown>("/api/portfolio/exposure");
}

export function getPortfolioSymbolExposure(symbol: string) {
  return getJson<unknown>(`/api/portfolio/symbols/${encodeURIComponent(symbol)}/exposure`);
}

// --- Live market data ---

export function getLiveQuote(symbol: string) {
  return getJson<import("@/types").QuotesResponse>(`/api/data/quotes/${encodeURIComponent(symbol)}`);
}

export function getLiveTrades(symbol: string, limit = 25) {
  return getJson<import("@/types").TradesResponse>(`/api/data/trades/${encodeURIComponent(symbol)}?limit=${limit}`);
}

export function getLiveOrderbook(symbol: string, levels = 10) {
  return getJson<import("@/types").OrderBookResponse>(`/api/data/orderbook/${encodeURIComponent(symbol)}?levels=${levels}`);
}

export function getLiveQuotesSnapshot(symbols?: readonly string[]) {
  const trimmed = symbols?.map((s) => s.trim()).filter((s) => s.length > 0) ?? [];
  const query = trimmed.length > 0 ? `?symbols=${encodeURIComponent(trimmed.join(","))}` : "";
  return getJson<import("@/types").QuotesSnapshotResponse>(`/api/data/quotes-snapshot${query}`);
}

export interface HistoricalBarsRequest {
  intervalMinutes: number;
  from?: string;
  to?: string;
  maxBars?: number;
}

export function getHistoricalBars(symbol: string, request: HistoricalBarsRequest) {
  const params = new URLSearchParams();
  params.set("intervalMinutes", String(request.intervalMinutes));
  if (request.from) params.set("from", request.from);
  if (request.to) params.set("to", request.to);
  if (request.maxBars !== undefined) params.set("maxBars", String(request.maxBars));
  return getJson<import("@/types").HistoricalBarsResponse>(
    `/api/historical/${encodeURIComponent(symbol)}/bars?${params.toString()}`
  );
}

// --- Quant Lab ---

export function getQuantTemplates() {
  return getJson<import("@/types").QuantTemplatesResponse>("/api/quant/templates");
}

export function extractQuantParameters(source: string) {
  return postJson<import("@/types").QuantParametersResponse>("/api/quant/parameters", { source });
}

export function runQuantScript(request: import("@/types").QuantRunRequest) {
  return postJson<import("@/types").QuantRunResponse>("/api/quant/run", request);
}
