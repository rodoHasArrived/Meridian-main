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
  ExportAnalysisResult,
  ExecutionControlSnapshot,
  ExecutionAuditEntry,
  GovernanceWorkspaceResponse,
  LedgerSummary,
  LedgerTrialBalanceLine,
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

async function getJson<T>(path: string): Promise<T> {
  const response = await fetch(path, {
    headers: {
      Accept: "application/json"
    }
  });

  if (!response.ok) {
    const fixture = await getDevelopmentFallback<T>(path, response.status);
    if (fixture !== undefined) {
      return fixture;
    }

    throw new Error(`Request failed for ${path} (${response.status})`);
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
  return getJson<SessionInfo>("/api/workstation/session");
}

export function getStrategyWorkspace() {
  return getJson<ResearchWorkspaceResponse>("/api/workstation/strategy");
}

export function getResearchWorkspace() {
  return getStrategyWorkspace();
}

export function getTradingWorkspace() {
  return getJson<TradingWorkspaceResponse>("/api/workstation/trading");
}

export function getTradingReadiness() {
  return getJson<TradingOperatorReadiness>("/api/workstation/trading/readiness");
}

export function getOperatorInbox(fundAccountId?: string) {
  const params = fundAccountId ? `?fundAccountId=${encodeURIComponent(fundAccountId)}` : "";
  return getJson<OperatorInbox>(`/api/workstation/operator/inbox${params}`);
}

export function getWorkstationWorkflowSummary(options: {
  hasOperatingContext?: boolean;
  operatingContext?: string;
  fundProfileId?: string;
  fundDisplayName?: string;
} = {}) {
  return getJson<unknown>(`/api/workstation/workflow-summary${queryString(options)}`);
}

export function getWorkflowLibrary() {
  return getJson<WorkflowLibrary>("/api/workstation/workflows");
}

export function getWorkflowPresets() {
  return getJson<WorkflowPresetLibrary>("/api/workstation/workflows/presets");
}

export function saveWorkflowPreset(request: WorkflowPresetSaveRequest) {
  return postJson<WorkflowPreset>("/api/workstation/workflows/presets", request);
}

export function updateWorkflowPreset(presetId: string, request: WorkflowPresetSaveRequest) {
  return putJson<WorkflowPreset>(`/api/workstation/workflows/presets/${encodeURIComponent(presetId)}`, request);
}

export function pinWorkflowPreset(presetId: string, isPinned: boolean) {
  return postJson<WorkflowPreset>(`/api/workstation/workflows/presets/${encodeURIComponent(presetId)}/pin`, { isPinned });
}

export function markWorkflowPresetUsed(presetId: string) {
  return postJson<WorkflowPreset>(`/api/workstation/workflows/presets/${encodeURIComponent(presetId)}/used`);
}

export function deleteWorkflowPreset(presetId: string) {
  return deleteJson<void>(`/api/workstation/workflows/presets/${encodeURIComponent(presetId)}`);
}

export function getDataWorkspace() {
  return getJson<DataOperationsWorkspaceResponse>("/api/workstation/data");
}

export function getDataOperationsWorkspace() {
  return getDataWorkspace();
}

export function getGovernanceWorkspace() {
  return getJson<GovernanceWorkspaceResponse>("/api/workstation/accounting");
}

export function getReportingWorkspace() {
  return getJson<GovernanceWorkspaceResponse>("/api/workstation/reporting");
}

export function runAnalysisExport(profileId: string) {
  return postJson<ExportAnalysisResult>("/api/export/analysis", { profileId });
}

// --- Promotion workflow ---

export function evaluatePromotion(runId: string) {
  return getJson<PromotionEvaluationResult>(`/api/promotion/evaluate/${encodeURIComponent(runId)}`);
}

export interface ApprovePromotionRequest {
  runId: string;
  approvedBy: string;
  approvalReason: string;
  reviewNotes?: string;
  manualOverrideId?: string;
}

export function approvePromotion(request: ApprovePromotionRequest) {
  return postJson<PromotionDecisionResult>("/api/promotion/approve", request);
}

export interface RejectPromotionRequest {
  runId: string;
  reason: string;
  rejectedBy?: string;
  reviewNotes?: string;
  manualOverrideId?: string;
}

export function rejectPromotion(request: RejectPromotionRequest) {
  return postJson<PromotionDecisionResult>("/api/promotion/reject", request);
}

export function getPromotionHistory() {
  return getJson<PromotionRecord[]>("/api/promotion/history");
}

// --- Order management ---

export function submitOrder(request: OrderSubmitRequest) {
  return postJson<OrderResult>("/api/execution/orders/submit", request);
}

export function cancelOrder(orderId: string) {
  return postJson<TradingActionResult>(`/api/execution/orders/${encodeURIComponent(orderId)}/cancel`);
}

export function cancelAllOrders() {
  return postJson<TradingActionResult>("/api/execution/orders/cancel-all");
}

export function closePosition(symbol: string) {
  return postJson<TradingActionResult>(`/api/execution/positions/${encodeURIComponent(symbol)}/close`);
}

// --- Paper session management ---

export function getExecutionSessions() {
  return getJson<PaperSessionSummary[]>("/api/execution/sessions");
}

export function createPaperSession(strategyId: string, strategyName: string | null, initialCash: number) {
  return postJson<PaperSessionSummary>("/api/execution/sessions/create", {
    strategyId,
    strategyName,
    initialCash
  });
}

export function closePaperSession(sessionId: string) {
  return postJson<TradingActionResult>(`/api/execution/sessions/${encodeURIComponent(sessionId)}/close`);
}

export function getPaperSessionDetail(sessionId: string) {
  return getJson<PaperSessionDetail>(`/api/execution/sessions/${encodeURIComponent(sessionId)}`);
}

export function getPaperSessionReplayVerification(sessionId: string) {
  return getJson<PaperSessionReplayVerification>(`/api/execution/sessions/${encodeURIComponent(sessionId)}/replay`);
}

export function getExecutionAudit(take = 20) {
  return getJson<ExecutionAuditEntry[]>(`/api/execution/audit?take=${encodeURIComponent(String(take))}`);
}

export function getExecutionControls() {
  return getJson<ExecutionControlSnapshot>("/api/execution/controls");
}

export function createExecutionManualOverride(request: CreateExecutionManualOverrideRequest) {
  return postJson<ExecutionManualOverride>("/api/execution/controls/manual-overrides", request);
}

export function clearExecutionManualOverride(overrideId: string) {
  return postJson<TradingActionResult>(`/api/execution/controls/manual-overrides/${encodeURIComponent(overrideId)}/clear`);
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
  const params = symbol ? `?symbol=${encodeURIComponent(symbol)}` : "";
  return getJson<{ files: ReplayFileRecord[]; total: number; timestamp: string }>(`/api/replay/files${params}`);
}

export function startReplay(filePath: string, speedMultiplier = 1) {
  return postJson<{ sessionId: string; filePath: string; status: string; speedMultiplier: number }>(
    "/api/replay/start",
    { filePath, speedMultiplier }
  );
}

export function pauseReplay(sessionId: string) {
  return postJson<{ sessionId: string; status: string; eventsProcessed: number }>(`/api/replay/${encodeURIComponent(sessionId)}/pause`);
}

export function resumeReplay(sessionId: string) {
  return postJson<{ sessionId: string; status: string; eventsProcessed: number }>(`/api/replay/${encodeURIComponent(sessionId)}/resume`);
}

export function stopReplay(sessionId: string) {
  return postJson<{ sessionId: string; status: string; eventsProcessed: number }>(`/api/replay/${encodeURIComponent(sessionId)}/stop`);
}

export function seekReplay(sessionId: string, positionMs: number) {
  return postJson<{ sessionId: string; positionMs: number; status: string }>(`/api/replay/${encodeURIComponent(sessionId)}/seek`, { positionMs });
}

export function setReplaySpeed(sessionId: string, speedMultiplier: number) {
  return postJson<{ sessionId: string; speedMultiplier: number; status: string }>(`/api/replay/${encodeURIComponent(sessionId)}/speed`, { speedMultiplier });
}

export function getReplayStatus(sessionId: string) {
  return getJson<ReplayStatus>(`/api/replay/${encodeURIComponent(sessionId)}/status`);
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
  return getJson<LedgerSummary>(`/api/workstation/runs/${encodeURIComponent(runId)}/ledger`);
}

export function getRunTrialBalance(runId: string, accountType?: string) {
  const params = accountType ? `?accountType=${encodeURIComponent(accountType)}` : "";
  return getJson<LedgerTrialBalanceLine[]>(`/api/workstation/runs/${encodeURIComponent(runId)}/ledger/trial-balance${params}`);
}

export function getRunLedgerJournal(runId: string, take?: number) {
  const params = queryString({ take });
  return getJson<unknown>(`/api/workstation/runs/${encodeURIComponent(runId)}/ledger/journal${params}`);
}

export function getRunContinuity(runId: string) {
  return getJson<unknown>(`/api/workstation/runs/${encodeURIComponent(runId)}/continuity`);
}

export function getRunReviewPacket(runId: string, fundAccountId?: string) {
  const params = queryString({ fundAccountId });
  return getJson<unknown>(`/api/workstation/runs/${encodeURIComponent(runId)}/review-packet${params}`);
}

export function getRunReconciliation(runId: string) {
  return getJson<unknown>(`/api/workstation/runs/${encodeURIComponent(runId)}/reconciliation`);
}

export function getRunReconciliationHistory(runId: string) {
  return getJson<unknown>(`/api/workstation/runs/${encodeURIComponent(runId)}/reconciliation/history`);
}

export function getRunHistory(options: { mode?: string; status?: string; limit?: number } = {}) {
  return getJson<unknown>(`/api/workstation/runs/history${queryString(options)}`);
}

export function getRunTimeline(options: { runId?: string; strategyId?: string; limit?: number } = {}) {
  return getJson<unknown>(`/api/workstation/runs/timeline${queryString(options)}`);
}

export function getRunSweeps(limit?: number) {
  return getJson<unknown>(`/api/workstation/runs/sweeps${queryString({ limit })}`);
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
  return getJson<import("@/types").SystemOverviewResponse>("/api/status");
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
  return getJson<BrokerageHouseholdPortfolio>(`/api/portfolio/household${queryString({ provider })}`);
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
