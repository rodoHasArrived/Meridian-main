import type {
  BackfillProgressResponse,
  BackfillTriggerRequest,
  BackfillTriggerResult,
  DataOperationsWorkspaceResponse,
  EquityCurveSummary,
  GovernanceWorkspaceResponse,
  OrderResult,
  OrderSubmitRequest,
  PaperSessionSummary,
  PromotionDecisionResult,
  PromotionEvaluationResult,
  PromotionRecord,
  ResearchRunRecord,
  ResearchWorkspaceResponse,
  ResolveConflictRequest,
  RunAttributionSummary,
  RunComparisonRow,
  RunDiff,
  RunFillSummary,
  SecurityIdentityDrillIn,
  SecurityMasterConflict,
  SecurityMasterEntry,
  SessionInfo,
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

export function approvePromotion(runId: string, reviewNotes?: string) {
  return postJson<PromotionDecisionResult>("/api/promotion/approve", { runId, reviewNotes });
}

export function rejectPromotion(runId: string, reason: string) {
  return postJson<PromotionDecisionResult>("/api/promotion/reject", { runId, reason });
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
  return postJson<void>(`/api/execution/sessions/${encodeURIComponent(sessionId)}/close`);
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

// --- Backfill mutations ---

export function getBackfillStatus() {
  return getJson<BackfillTriggerResult | null>("/api/backfill/status");
}

export function getBackfillProgress() {
  return getJson<BackfillProgressResponse>("/api/backfill/progress");
}

export function triggerBackfill(request: BackfillTriggerRequest) {
  return postJson<BackfillTriggerResult>("/api/backfill/run", request);
}

export function previewBackfill(request: BackfillTriggerRequest) {
  return postJson<BackfillTriggerResult>("/api/backfill/run/preview", request);
}
