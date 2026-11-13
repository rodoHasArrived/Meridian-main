// Promotion, order, paper session, strategy lifecycle, replay, and strategy run endpoints.
// Re-exported through @/lib/api.
import type { ApprovePromotionRequest, RejectPromotionRequest } from "./promotion.contracts";
import type { StrategySweepResultGroup } from "@/types/workstation-6";
import type { EquityCurveSummary, LedgerJournalLine, LedgerSummary, LedgerTrialBalanceLine, OrderResult, OrderSubmitRequest, PaperSessionSummary, PromotionDecisionResult, PromotionEvaluationResult, PromotionRecord, RiskRuleConfig, RiskRuleConfigUpdateRequest, RiskRuleStatus, StrategyRunSummaryApiRecord, RunAttributionSummary, RunCashFlowSummary, RunComparisonRow, RunDiff, RunFillSummary, ReplayFileRecord, ReplayStatus, StrategyRunContinuityDto, StrategyRunReviewPacket, TradingActionResult, CreateExecutionManualOverrideRequest, ExecutionManualOverride } from "@/types";
import { EXECUTION_API_ENDPOINTS, PROMOTION_API_ENDPOINTS, REPLAY_API_ENDPOINTS, executionManualOverrideClearEndpoint, executionOrderCancelEndpoint, executionPositionCloseEndpoint, executionSessionCloseEndpoint, riskRuleConfigEndpoint, riskRuleStatusEndpoint, portfolioRunCashFlowsEndpoint, promotionEvaluateEndpoint, replayFilesEndpoint, replaySessionActionEndpoint, strategyActionEndpoint, workstationRunAttributionEndpoint, workstationRunCompareEndpoint, workstationRunContinuityEndpoint, workstationRunDiffEndpoint, workstationRunEquityCurveEndpoint, workstationRunFillsEndpoint, workstationRunHistoryEndpoint, workstationRunLedgerEndpoint, workstationRunLedgerJournalEndpoint, workstationRunLedgerTrialBalanceEndpoint, workstationRunReconciliationEndpoint, workstationRunReconciliationHistoryEndpoint, workstationRunReviewPacketEndpoint, workstationRunSweepsEndpoint, workstationRunTimelineEndpoint, RISK_API_ENDPOINTS, type LedgerJournalQueryOptions, type LedgerTrialBalanceQueryOptions } from "@/lib/workstation-endpoints";
import { normalizeFundAccountGuid } from "@/lib/fund-account-scope";
import { getJson, postJson, putJson } from "@/lib/api/http-client";

// --- Promotion workflow ---

export function evaluatePromotion(runId: string) {
  return getJson<PromotionEvaluationResult>(promotionEvaluateEndpoint(runId));
}

export function approvePromotion(request: ApprovePromotionRequest) {
  return postJson<PromotionDecisionResult>(PROMOTION_API_ENDPOINTS.approve, request);
}

export function rejectPromotion(request: RejectPromotionRequest) {
  return postJson<PromotionDecisionResult>(PROMOTION_API_ENDPOINTS.reject, request);
}

export function getPromotionHistory() {
  return getJson<PromotionRecord[]>(PROMOTION_API_ENDPOINTS.history);
}

// --- Order management ---

export function submitOrder(request: OrderSubmitRequest) {
  return postJson<OrderResult>(EXECUTION_API_ENDPOINTS.ordersSubmit, { ...request,
    type: request.type === "Stop" ? "StopMarket" : request.type,
    ...(request.type === "Stop" ? { stopPrice: request.limitPrice, limitPrice: null } : {}) });
}

export function cancelOrder(orderId: string) {
  return postJson<TradingActionResult>(executionOrderCancelEndpoint(orderId));
}

export function cancelAllOrders() {
  return postJson<TradingActionResult>(EXECUTION_API_ENDPOINTS.ordersCancelAll);
}

export function closePosition(positionKey: string, fundAccountId?: string | null) {
  const normalizedFundAccountId = normalizeFundAccountGuid(fundAccountId);
  return postJson<TradingActionResult>(
    executionPositionCloseEndpoint(),
    {
      positionKey,
      ...(normalizedFundAccountId ? { fundAccountId: normalizedFundAccountId } : {})
    }
  );
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

export function getRiskRules() {
  return getJson<RiskRuleStatus[]>(RISK_API_ENDPOINTS.rules);
}

export function getRiskRuleStatus(ruleName: string) {
  return getJson<RiskRuleStatus>(riskRuleStatusEndpoint(ruleName));
}

export function getRiskRuleConfig(ruleName: string) {
  return getJson<RiskRuleConfig>(riskRuleConfigEndpoint(ruleName));
}

export function createExecutionManualOverride(request: CreateExecutionManualOverrideRequest) {
  return postJson<ExecutionManualOverride>(EXECUTION_API_ENDPOINTS.manualOverrides, request);
}

export function clearExecutionManualOverride(overrideId: string) {
  return postJson<TradingActionResult>(executionManualOverrideClearEndpoint(overrideId));
}

export function updateRiskRuleConfig(ruleName: string, request: RiskRuleConfigUpdateRequest) {
  return putJson<RiskRuleConfig>(riskRuleConfigEndpoint(ruleName), request);
}

// --- Strategy lifecycle ---

export function pauseStrategy(strategyId: string) {
  return postJson<{ strategyId: string; action: string; success: boolean; reason: string | null }>(
    strategyActionEndpoint(strategyId, "pause")
  );
}

export function stopStrategy(strategyId: string) {
  return postJson<{ strategyId: string; action: string; success: boolean; reason: string | null }>(
    strategyActionEndpoint(strategyId, "stop")
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
  return getJson<StrategyRunSummaryApiRecord[]>(`/api/strategies/${encodeURIComponent(strategyId)}/runs${params}`);
}

// --- Multi-run comparison and diff ---

export function compareRuns(runIds: string[]) {
  return postJson<RunComparisonRow[]>(workstationRunCompareEndpoint(), { runIds });
}

export function diffRuns(baseRunId: string, targetRunId: string) {
  return postJson<RunDiff>(workstationRunDiffEndpoint(), { baseRunId, targetRunId });
}

// --- Run detail drill-ins ---

export function getRunAttribution(runId: string) {
  return getJson<RunAttributionSummary>(workstationRunAttributionEndpoint(runId));
}

export function getRunFills(runId: string, symbol?: string) {
  return getJson<RunFillSummary>(workstationRunFillsEndpoint(runId, symbol));
}

export function getRunEquityCurve(runId: string) {
  return getJson<EquityCurveSummary>(workstationRunEquityCurveEndpoint(runId));
}

export function getRunCashFlows(runId: string) {
  return getJson<RunCashFlowSummary>(portfolioRunCashFlowsEndpoint(runId));
}

export function getRunLedger(runId: string) {
  return getJson<LedgerSummary>(workstationRunLedgerEndpoint(runId));
}

export function getRunTrialBalance(runId: string, accountTypeOrOptions?: string | LedgerTrialBalanceQueryOptions) {
  return getJson<LedgerTrialBalanceLine[]>(workstationRunLedgerTrialBalanceEndpoint(runId, accountTypeOrOptions));
}

export function getRunLedgerJournal(runId: string, options: LedgerJournalQueryOptions = {}) {
  return getJson<LedgerJournalLine[]>(workstationRunLedgerJournalEndpoint(runId, options));
}

export function getRunContinuity(runId: string) {
  return getJson<StrategyRunContinuityDto>(workstationRunContinuityEndpoint(runId));
}

export function getRunReviewPacketPath(runId: string, fundAccountId?: string) {
  return workstationRunReviewPacketEndpoint(runId, fundAccountId);
}

export function getRunReviewPacket(runId: string, fundAccountId?: string) {
  return getJson<StrategyRunReviewPacket>(getRunReviewPacketPath(runId, fundAccountId));
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
  return getJson<StrategySweepResultGroup[]>(workstationRunSweepsEndpoint(limit), { allowDevelopmentFallback: false });
}
