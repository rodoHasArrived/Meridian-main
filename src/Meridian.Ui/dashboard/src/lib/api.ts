import type {
  BackfillProgressResponse,
  BackfillTriggerRequest,
  BackfillTriggerResult,
  ClearManualOverrideRequest,
  CreateManualOverrideRequest,
  DataOperationsWorkspaceResponse,
  EquityCurveSummary,
  ExecutionAuditEntry,
  ExecutionControlSnapshot,
  ExecutionManualOverride,
  GovernanceWorkspaceResponse,
  LedgerSummary,
  LedgerTrialBalanceLine,
  OrderResult,
  OrderSubmitRequest,
  PaperSessionSummary,
  PaperSessionDetail,
  PaperSessionReplayVerification,
  PromotionApprovalRequest,
  PromotionDecisionResult,
  PromotionEvaluationResult,
  PromotionRecord,
  ResearchRunRecord,
  ResearchWorkspaceResponse,
  ReconciliationBreakQueueItem,
  ResolveReconciliationBreakRequest,
  ResolveConflictRequest,
  ReviewReconciliationBreakRequest,
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
  TradingWorkspaceResponse
} from "@/types";

async function getJson<T>(path: string): Promise<T> {
  const response = await fetch(path, {
    headers: {
      Accept: "application/json"
    }
  });

  if (!response.ok) {
    throw new Error(`Request failed for ${path} (${response.status})`);
  }

  return response.json() as Promise<T>;
}

function createCorrelationId(): string {
  if (typeof crypto !== "undefined" && typeof crypto.randomUUID === "function") {
    return crypto.randomUUID();
  }

  return `corr-${Date.now()}-${Math.random().toString(36).slice(2, 10)}`;
}

function buildWriteHeaders(actor?: string): HeadersInit {
  const headers: Record<string, string> = {
    Accept: "application/json",
    "Content-Type": "application/json"
  };

  if (actor && actor.trim().length > 0) {
    headers["X-Meridian-Actor"] = actor.trim();
  }

  return headers;
}

function describeErrorBody(text: string): string {
  if (!text) {
    return "";
  }

  try {
    const parsed = JSON.parse(text) as { error?: string; message?: string };
    return parsed.error ?? parsed.message ?? text;
  } catch {
    return text;
  }
}

async function postJson<T>(path: string, body?: unknown, actor?: string): Promise<T> {
  const response = await fetch(path, {
    method: "POST",
    headers: buildWriteHeaders(actor),
    body: body !== undefined ? JSON.stringify(body) : undefined
  });

  if (!response.ok) {
    let errorDetail = "";
    try {
      const errBody = await response.text();
      const detail = describeErrorBody(errBody);
      errorDetail = detail ? ` — ${detail}` : "";
    } catch {
      // ignore parse failures
    }

    throw new Error(`Request failed for ${path} (${response.status})${errorDetail}`);
  }

  const text = await response.text();
  return (text ? JSON.parse(text) : null) as T;
}

export function getSession() {
  return getJson<SessionInfo>("/api/workstation/session");
}

export function getResearchWorkspace() {
  return getJson<ResearchWorkspaceResponse>("/api/workstation/research");
}

export function getTradingWorkspace() {
  return getJson<TradingWorkspaceResponse>("/api/workstation/trading");
}

export function getDataOperationsWorkspace() {
  return getJson<DataOperationsWorkspaceResponse>("/api/workstation/data-operations");
}

export function getGovernanceWorkspace() {
  return getJson<GovernanceWorkspaceResponse>("/api/workstation/governance");
}

// --- Promotion workflow ---

export function evaluatePromotion(runId: string) {
  return getJson<PromotionEvaluationResult>(`/api/promotion/evaluate/${encodeURIComponent(runId)}`);
}

export function approvePromotion(request: PromotionApprovalRequest, actor?: string) {
  return postJson<PromotionDecisionResult>("/api/promotion/approve", request, actor);
}

export function rejectPromotion(
  runId: string,
  reason: string,
  reviewNotes?: string | null,
  rejectedBy?: string | null,
  manualOverrideId?: string | null,
  actor?: string
) {
  return postJson<PromotionDecisionResult>(
    "/api/promotion/reject",
    { runId, reason, reviewNotes, rejectedBy, manualOverrideId },
    actor
  );
}

export function getPromotionHistory() {
  return getJson<PromotionRecord[]>("/api/promotion/history");
}

// --- Order management ---

export function submitOrder(request: OrderSubmitRequest, actor?: string) {
  const correlationId = request.metadata?.correlationId ?? createCorrelationId();
  const metadata: Record<string, string> = {
    ...(request.metadata ?? {}),
    correlationId
  };

  if (actor && actor.trim().length > 0) {
    metadata.actor = actor.trim();
  }

  if (request.runId) {
    metadata.runId = request.runId;
  }

  if (request.sessionId) {
    metadata.sessionId = request.sessionId;
  }

  if (request.manualOverrideId) {
    metadata.manualOverrideId = request.manualOverrideId;
  }

  return postJson<OrderResult>(
    "/api/execution/orders/submit",
    {
      symbol: request.symbol,
      side: request.side,
      type: request.type,
      quantity: request.quantity,
      limitPrice: request.limitPrice ?? null,
      timeInForce: request.timeInForce ?? "Day",
      strategyId: request.strategyId ?? null,
      metadata
    },
    actor
  );
}

export function cancelOrder(orderId: string, actor?: string) {
  return postJson<TradingActionResult>(`/api/execution/orders/${encodeURIComponent(orderId)}/cancel`, undefined, actor);
}

export function cancelAllOrders(actor?: string) {
  return postJson<TradingActionResult>("/api/execution/orders/cancel-all", undefined, actor);
}

export function closePosition(symbol: string, actor?: string) {
  return postJson<TradingActionResult>(`/api/execution/positions/${encodeURIComponent(symbol)}/close`, undefined, actor);
}

export function getExecutionControls() {
  return getJson<ExecutionControlSnapshot>("/api/execution/controls");
}

export function createManualOverride(request: CreateManualOverrideRequest, actor?: string) {
  return postJson<ExecutionManualOverride>(
    "/api/execution/controls/manual-overrides",
    {
      ...request,
      correlationId: request.correlationId ?? createCorrelationId()
    },
    actor
  );
}

export function clearManualOverride(overrideId: string, request?: ClearManualOverrideRequest, actor?: string) {
  return postJson<TradingActionResult>(
    `/api/execution/controls/manual-overrides/${encodeURIComponent(overrideId)}/clear`,
    {
      reason: request?.reason ?? null,
      correlationId: request?.correlationId ?? createCorrelationId()
    },
    actor
  );
}

// --- Paper session management ---

export function getExecutionSessions() {
  return getJson<PaperSessionSummary[]>("/api/execution/sessions");
}

export function createPaperSession(
  strategyId: string,
  strategyName: string | null,
  initialCash: number,
  symbols?: string[],
  actor?: string
) {
  return postJson<PaperSessionSummary>("/api/execution/sessions/create", {
    strategyId,
    strategyName,
    initialCash,
    symbols
  }, actor);
}

export function closePaperSession(sessionId: string, actor?: string) {
  return postJson<TradingActionResult>(`/api/execution/sessions/${encodeURIComponent(sessionId)}/close`, undefined, actor);
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

// --- Strategy lifecycle ---

export function pauseStrategy(strategyId: string, actor?: string) {
  return postJson<{ strategyId: string; action: string; success: boolean; reason: string | null }>(
    `/api/strategies/${encodeURIComponent(strategyId)}/pause`,
    undefined,
    actor
  );
}

export function stopStrategy(strategyId: string, actor?: string) {
  return postJson<{ strategyId: string; action: string; success: boolean; reason: string | null }>(
    `/api/strategies/${encodeURIComponent(strategyId)}/stop`,
    undefined,
    actor
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

// --- Security Master search ---

export function searchSecurities(query: string, take = 25, activeOnly = true) {
  const params = new URLSearchParams({
    query,
    take: String(take),
    activeOnly: String(activeOnly)
  });
  return getJson<SecurityMasterEntry[]>(`/api/workstation/security-master/securities?${params.toString()}`);
}

export function getSecurityDetail(securityId: string) {
  return getJson<SecurityMasterEntry>(`/api/workstation/security-master/securities/${encodeURIComponent(securityId)}`);
}

export function getSecurityIdentity(securityId: string) {
  return getJson<SecurityIdentityDrillIn>(`/api/workstation/security-master/securities/${encodeURIComponent(securityId)}/identity`);
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

export function getReconciliationBreakQueue(status?: string) {
  const params = status ? `?status=${encodeURIComponent(status)}` : "";
  return getJson<ReconciliationBreakQueueItem[]>(`/api/workstation/reconciliation/break-queue${params}`);
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
