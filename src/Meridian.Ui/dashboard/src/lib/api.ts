import type {
  BackfillPreviewResult,
  BackfillProgressResponse,
  BackfillTriggerRequest,
  BackfillTriggerResult,
  AlpacaBrokerageConnectionRequest,
  BrokerageConnectionStatus,
  CellExecuteRequest,
  CellExecuteResult,
  CellExecutionContext,
  CellOutput,
  BrokerageHouseholdPortfolio,
  ChiefOfStaffDecisionRequest,
  ChiefOfStaffEvidenceExport,
  ChiefOfStaffRuntimeHealth,
  ChiefOfStaffSession,
  ChiefOfStaffSessionQuery,
  ChiefOfStaffSessionSummary,
  ChiefOfStaffTraceExportRequest,
  CorporateAction,
  DataFetchRequest,
  DataFetchResult,
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
  NetSymbolPosition,
  OperatorInbox,
  OperationsContinuityWorkflow,
  OperationsContinuityWorkflowSummary,
  OrderResult,
  OrderSubmitRequest,
  PaperSessionSummary,
  PaperSessionDetail,
  PaperSessionReplayVerification,
  ProviderConnectionRow,
  ProviderCredentialMutationResult,
  ProviderCredentialUpsertRequest,
  ProviderCredentialVerificationResult,
  ProviderRoutePreviewRequest,
  ProviderRoutePreviewResponse,
  ProviderRoutingBinding,
  ProviderRoutingConnection,
  ProviderRoutingTrustSnapshot,
  PromotionDecisionResult,
  PromotionEvaluationResult,
  PromotionRecord,
  RiskRuleConfig,
  RiskRuleConfigUpdateRequest,
  RiskRuleStatus,
  ReconciliationBreakQueueItem,
  ReconciliationCalibrationSummary,
  ResolveReconciliationBreakRequest,
  ResolveConflictRequest,
  ReviewReconciliationBreakRequest,
  ResearchRunRecord,
  StrategyRunSummaryApiRecord,
  ResearchWorkspaceResponse,
  RunAttributionSummary,
  RunComparisonRow,
  RunDiff,
  RunFillSummary,
  OperatorOverridesDto,
  OperatorOverridesPatchRequest,
  SecurityIdentityDrillIn,
  SecurityMasterConflict,
  SecurityMasterEntry,
  SecurityMasterTrustSnapshot,
  SessionInfo,
  SystemEventRecord,
  SystemOverviewResponse,
  ReplayFileRecord,
  ReplayStatus,
  StrategyDesignDocument,
  StrategyDesignDraftSaveRequest,
  StrategyDesignDraftSaveResponse,
  StrategyDesignDraftSummary,
  StrategyDesignFieldCatalogItem,
  StrategyDesignPreviewResult,
  StrategyDesignRunBacktestRequest,
  StrategyDesignRunBacktestResponse,
  StrategyDesignTemplate,
  StrategyDesignValidationResult,
  StrategyRunContinuityDto,
  TradingActionResult,
  TradingOperatorReadiness,
  TradingParameters,
  TradingWorkspaceResponse,
  WorkflowLibrary,
  WorkflowPreset,
  WorkflowPresetLibrary,
  WorkflowPresetSaveRequest,
  CreateExecutionManualOverrideRequest,
  ExecutionManualOverride,
  FeatureCapabilitySettingsResponse
} from "@/types";
import {
  BACKFILL_API_ENDPOINTS,
  EXECUTION_API_ENDPOINTS,
  EXPORT_API_ENDPOINTS,
  PORTFOLIO_API_ENDPOINTS,
  PROVIDER_API_ENDPOINTS,
  PROVIDER_ROUTING_API_ENDPOINTS,
  PROMOTION_API_ENDPOINTS,
  QUALITY_API_ENDPOINTS,
  QUANT_API_ENDPOINTS,
  RECONCILIATION_API_ENDPOINTS,
  REPLAY_API_ENDPOINTS,
  SECURITY_MASTER_API_ENDPOINTS,
  SYMBOL_API_ENDPOINTS,
  STRATEGY_DESIGNER_API_ENDPOINTS,
  WORKSTATION_API_ENDPOINTS,
  brokerageConnectionConnectEndpoint,
  brokerageConnectionEndpoint,
  brokerageConnectionStatusEndpoint,
  executionAuditEndpoint,
  executionManualOverrideClearEndpoint,
  executionOrderCancelEndpoint,
  executionPositionCloseEndpoint,
  executionSessionCloseEndpoint,
  executionSessionEndpoint,
  executionSessionReplayEndpoint,
  riskRuleConfigEndpoint,
  riskRuleStatusEndpoint,
  historicalBarsEndpoint,
  marketDataOrderbookEndpoint,
  marketDataQuoteEndpoint,
  marketDataQuotesSnapshotEndpoint,
  marketDataTradesEndpoint,
  portfolioHouseholdEndpoint,
  portfolioSymbolExposureEndpoint,
  promotionEvaluateEndpoint,
  providerCredentialEndpoint,
  providerVerifyEndpoint,
  providerRemoveEndpoint,
  providerTestEndpoint,
  qualityAnomalyAcknowledgeEndpoint,
  reconciliationBreakAssignEndpoint,
  reconciliationBreakAuditEndpoint,
  reconciliationBreakBulkDryRunEndpoint,
  reconciliationBreakBulkExecuteEndpoint,
  reconciliationBreakBulkResultEndpoint,
  reconciliationBreakCommentEndpoint,
  reconciliationBreakCommentsEndpoint,
  reconciliationBreakEndpoint,
  reconciliationBreakQueueEndpoint,
  reconciliationBreakReopenEndpoint,
  reconciliationBreakResolutionEndpoint,
  reconciliationBreakResolveEndpoint,
  reconciliationBreakReviewEndpoint,
  reconciliationBreakRootCauseEndpoint,
  reconciliationBreakSignOffEndpoint,
  reconciliationBreakTransitionEndpoint,
  reconciliationRunEndpoint,
  replayFilesEndpoint,
  replaySessionActionEndpoint,
  securityMasterAliasUpsertEndpoint,
  securityMasterAmendEndpoint,
  securityMasterConflictsEndpoint,
  securityMasterConflictResolveEndpoint,
  securityMasterCorporateActionsEndpoint,
  securityMasterEntryEndpoint,
  securityMasterOperatorOverridesEndpoint,
  securityMasterTradingParametersEndpoint,
  strategyActionEndpoint,
  strategyDesignerDraftEndpoint,
  strategyRunsEndpoint,
  symbolArchiveEndpoint,
  symbolRemoveEndpoint,
  symbolSearchEndpoint,
  workstationEvidenceExportManifestEndpoint,
  workstationEvidenceGraphEndpoint,
  workstationEvidencePacketEndpoint,
  workstationEvidenceValidateEndpoint,
  workstationOperatorInboxEndpoint,
  workstationOperationsContinuityDetailEndpoint,
  workstationOperationsContinuityEndpoint,
  workstationChiefOfStaffDecisionEndpoint,
  workstationChiefOfStaffHealthEndpoint,
  workstationChiefOfStaffSessionEndpoint,
  workstationChiefOfStaffSessionsEndpoint,
  workstationChiefOfStaffTraceExportEndpoint,
  workstationRunAttributionEndpoint,
  workstationRunCompareEndpoint,
  workstationRunContinuityEndpoint,
  workstationRunDiffEndpoint,
  workstationRunEquityCurveEndpoint,
  workstationRunFillsEndpoint,
  workstationRunHistoryEndpoint,
  workstationRunLedgerEndpoint,
  workstationRunLedgerJournalEndpoint,
  workstationRunLedgerTrialBalanceEndpoint,
  workstationRunReconciliationEndpoint,
  workstationRunReconciliationHistoryEndpoint,
  workstationRunReviewPacketEndpoint,
  workstationRunSweepsEndpoint,
  workstationRunTimelineEndpoint,
  RISK_API_ENDPOINTS,
  workstationSecurityMasterEconomicDefinitionEndpoint,
  workstationSecurityMasterEntryEndpoint,
  workstationSecurityMasterHistoryEndpoint,
  workstationSecurityMasterIdentityEndpoint,
  workstationSecurityMasterSearchEndpoint,
  workstationSecurityMasterTrustSnapshotEndpoint,
  workstationTradingReadinessEndpoint,
  workstationWorkflowSummaryEndpoint,
  workstationWorkflowPresetEndpoint,
  workstationWorkflowPresetPinEndpoint,
  workstationWorkflowPresetUsedEndpoint
} from "@/lib/workstation-endpoints";
import { createApiErrorFromResponseBody } from "@/lib/api-errors";

export const developmentFixtureHeader = "x-meridian-dev-fixture";

export interface ApiRequestOptions {
  signal?: AbortSignal;
}

let developmentFixtureUsage = false;

export function resetDevelopmentFixtureUsage() {
  developmentFixtureUsage = false;
}

export function hasDevelopmentFixtureUsage() {
  return developmentFixtureUsage;
}

async function getJson<T>(path: string, options: ApiRequestOptions = {}): Promise<T> {
  const response = await fetch(path, {
    signal: options.signal,
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

    throw await buildApiError(path, response);
  }

  if (response.headers?.get?.(developmentFixtureHeader) === "true") {
    markDevelopmentFixtureUsage();
  }

  return readJsonResponse<T>(path, response);
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

async function postJson<T>(path: string, body?: unknown, options: ApiRequestOptions = {}): Promise<T> {
  const response = await fetch(path, {
    method: "POST",
    signal: options.signal,
    headers: {
      Accept: "application/json",
      "Content-Type": "application/json"
    },
    body: body !== undefined ? JSON.stringify(body) : undefined
  });

  if (!response.ok) {
    throw await buildApiError(path, response);
  }

  return readJsonResponse<T>(path, response);
}

export function apiGetJson<T>(path: string, options: ApiRequestOptions = {}): Promise<T> {
  return getJson<T>(path, options);
}

export function apiPostJson<T>(path: string, body?: unknown, options: ApiRequestOptions = {}): Promise<T> {
  return postJson<T>(path, body, options);
}

async function putJson<T>(path: string, body?: unknown, options: ApiRequestOptions = {}): Promise<T> {
  const response = await fetch(path, {
    method: "PUT",
    signal: options.signal,
    headers: {
      Accept: "application/json",
      "Content-Type": "application/json"
    },
    body: body !== undefined ? JSON.stringify(body) : undefined
  });

  if (!response.ok) {
    throw await buildApiError(path, response);
  }

  return readJsonResponse<T>(path, response);
}

async function patchJson<T>(path: string, body?: unknown, options: ApiRequestOptions = {}): Promise<T> {
  const response = await fetch(path, {
    method: "PATCH",
    signal: options.signal,
    headers: {
      Accept: "application/json",
      "Content-Type": "application/json"
    },
    body: body !== undefined ? JSON.stringify(body) : undefined
  });

  if (!response.ok) {
    throw await buildApiError(path, response);
  }

  return readJsonResponse<T>(path, response);
}

async function deleteJson<T>(path: string, options: ApiRequestOptions = {}): Promise<T> {
  const response = await fetch(path, {
    method: "DELETE",
    signal: options.signal,
    headers: {
      Accept: "application/json"
    }
  });

  if (!response.ok) {
    throw await buildApiError(path, response);
  }

  return readJsonResponse<T>(path, response);
}

async function readJsonResponse<T>(path: string, response: Response): Promise<T> {
  if (response.status === 204 || response.status === 205) {
    return null as T;
  }

  const textFallback = typeof response.clone === "function" ? response.clone() : response;
  try {
    if (typeof response.json === "function") {
      return await response.json() as T;
    }
  } catch (error) {
    const text = await readResponseSuccessBody(textFallback);
    if (!text.trim()) {
      return null as T;
    }

    try {
      return JSON.parse(text) as T;
    } catch {
      const detail = error instanceof Error && error.message ? ` ${error.message}` : "";
      throw new Error(`Response from ${path} was not valid JSON.${detail}`);
    }
  }

  const text = await readResponseSuccessBody(response);
  if (!text.trim()) {
    return null as T;
  }

  try {
    return JSON.parse(text) as T;
  } catch (error) {
    const detail = error instanceof Error && error.message ? ` ${error.message}` : "";
    throw new Error(`Response from ${path} was not valid JSON.${detail}`);
  }
}

async function readResponseSuccessBody(response: Response): Promise<string> {
  if (typeof response.text !== "function") {
    return "";
  }

  try {
    return await response.text();
  } catch {
    return "";
  }
}

async function buildApiError(path: string, response: Response) {
  return createApiErrorFromResponseBody(path, response.status, await readResponseErrorBody(response));
}

async function readResponseErrorBody(response: Response): Promise<string> {
  try {
    return await response.text();
  } catch {
    return "";
  }
}

async function getDevelopmentSearchFallback(query: string, take: number, activeOnly: boolean) {
  if (!import.meta.env.DEV) {
    return undefined;
  }

  const { searchDevSecurityMasterEntries } = await import("@/lib/dev-fixtures");
  return searchDevSecurityMasterEntries(query, take, activeOnly);
}

export function getSession(options: ApiRequestOptions = {}) {
  return getJson<SessionInfo>(WORKSTATION_API_ENDPOINTS.session, options);
}

export function getStrategyWorkspace(options: ApiRequestOptions = {}) {
  return getJson<ResearchWorkspaceResponse>(WORKSTATION_API_ENDPOINTS.strategy, options);
}

export function getResearchWorkspace() {
  return getStrategyWorkspace();
}

export function getTradingWorkspace(options: ApiRequestOptions = {}) {
  return getJson<TradingWorkspaceResponse>(WORKSTATION_API_ENDPOINTS.trading, options);
}

export function getTradingReadiness(options: ApiRequestOptions & { fundAccountId?: string } = {}) {
  const { fundAccountId, ...requestOptions } = options;
  return getJson<TradingOperatorReadiness>(workstationTradingReadinessEndpoint(fundAccountId), requestOptions);
}

export function getOperatorInbox(fundAccountId?: string, options: ApiRequestOptions = {}) {
  return getJson<OperatorInbox>(workstationOperatorInboxEndpoint(fundAccountId), options);
}

export function getWorkstationWorkflowSummary(options: {
  hasOperatingContext?: boolean;
  operatingContext?: string;
  fundProfileId?: string;
  fundDisplayName?: string;
} = {}) {
  return getJson<unknown>(workstationWorkflowSummaryEndpoint(options));
}

export function getWorkflowLibrary(options: ApiRequestOptions = {}) {
  return getJson<WorkflowLibrary>(WORKSTATION_API_ENDPOINTS.workflowLibrary, options);
}

export function getWorkflowPresets(options: ApiRequestOptions = {}) {
  return getJson<WorkflowPresetLibrary>(WORKSTATION_API_ENDPOINTS.workflowPresets, options);
}

export function getFeatureCapabilities(options: ApiRequestOptions = {}) {
  return getJson<FeatureCapabilitySettingsResponse>(WORKSTATION_API_ENDPOINTS.featureCapabilities, options);
}

export function setFeatureCapability(capabilityKey: string, isEnabled: boolean, options: ApiRequestOptions = {}) {
  return putJson<FeatureCapabilitySettingsResponse>(
    `${WORKSTATION_API_ENDPOINTS.featureCapabilities}/${encodeURIComponent(capabilityKey)}`,
    { isEnabled },
    options
  );
}

export function getOperationsContinuityWorkflows(
  filters: { fundAccountId?: string; periodId?: string; status?: string } = {},
  options: ApiRequestOptions = {}
) {
  return getJson<OperationsContinuityWorkflowSummary[]>(workstationOperationsContinuityEndpoint(filters), options);
}

export function getOperationsContinuityWorkflow(workflowId: string, options: ApiRequestOptions = {}) {
  return getJson<OperationsContinuityWorkflow>(workstationOperationsContinuityDetailEndpoint(workflowId), options);
}

export function getChiefOfStaffSessions(query: ChiefOfStaffSessionQuery = {}, options: ApiRequestOptions = {}) {
  return getJson<ChiefOfStaffSessionSummary[]>(workstationChiefOfStaffSessionsEndpoint(query), options);
}

export function getChiefOfStaffSession(sessionId: string, options: ApiRequestOptions = {}) {
  return getJson<ChiefOfStaffSession>(workstationChiefOfStaffSessionEndpoint(sessionId), options);
}

export function getChiefOfStaffHealth(options: ApiRequestOptions = {}) {
  return getJson<ChiefOfStaffRuntimeHealth>(workstationChiefOfStaffHealthEndpoint(), options);
}

export function submitChiefOfStaffDecision(
  sessionId: string,
  request: ChiefOfStaffDecisionRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<ChiefOfStaffSession>(workstationChiefOfStaffDecisionEndpoint(sessionId), request, options);
}

export function exportChiefOfStaffTrace(
  sessionId: string,
  request: ChiefOfStaffTraceExportRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<ChiefOfStaffEvidenceExport>(workstationChiefOfStaffTraceExportEndpoint(sessionId), request, options);
}

export function getEvidenceSubjects(options: ApiRequestOptions = {}) {
  return getJson<EvidenceSubject[]>(WORKSTATION_API_ENDPOINTS.evidenceSubjects, options);
}

export function getEvidencePacket(subjectKind: string, subjectId: string, options: ApiRequestOptions = {}) {
  return getJson<EvidencePacket>(workstationEvidencePacketEndpoint(subjectKind, subjectId), options);
}

export function getEvidenceGraph(subjectKind: string, subjectId: string, options: ApiRequestOptions = {}) {
  return getJson<EvidenceGraph>(workstationEvidenceGraphEndpoint(subjectKind, subjectId), options);
}

export function validateEvidencePacket(subjectKind: string, subjectId: string, options: ApiRequestOptions = {}) {
  return postJson<EvidenceCompleteness>(workstationEvidenceValidateEndpoint(subjectKind, subjectId), undefined, options);
}

export function exportEvidenceManifest(
  subjectKind: string,
  subjectId: string,
  request: EvidencePacketExportRequest = { includeWarnings: true },
  options: ApiRequestOptions = {}
) {
  return postJson<EvidencePacketExportResponse>(workstationEvidenceExportManifestEndpoint(subjectKind, subjectId), request, options);
}

export function getEvidenceTemplates(options: ApiRequestOptions = {}) {
  return getJson<EvidenceTemplate[]>(WORKSTATION_API_ENDPOINTS.evidenceTemplates, options);
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

export function getDataWorkspace(options: ApiRequestOptions = {}) {
  return getJson<DataOperationsWorkspaceResponse>(WORKSTATION_API_ENDPOINTS.data, options);
}

export function getDataOperationsWorkspace() {
  return getDataWorkspace();
}

export function getGovernanceWorkspace(options: ApiRequestOptions = {}) {
  return getJson<GovernanceWorkspaceResponse>(WORKSTATION_API_ENDPOINTS.accounting, options);
}

export function getReportingWorkspace(options: ApiRequestOptions = {}) {
  return getJson<GovernanceWorkspaceResponse>(WORKSTATION_API_ENDPOINTS.reporting, options);
}

export function runAnalysisExport(profileId: string, options: ApiRequestOptions = {}) {
  return postJson<ExportAnalysisResult>(EXPORT_API_ENDPOINTS.analysis, { profileId }, options);
}

// --- Promotion workflow ---

export function evaluatePromotion(runId: string) {
  return getJson<PromotionEvaluationResult>(promotionEvaluateEndpoint(runId));
}

export interface ApprovePromotionRequest {
  runId: string;
  approvedBy: string;
  approvalReason: string;
  approvalChecklist?: string[];
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

export function closePosition(positionKey: string) {
  return postJson<TradingActionResult>(executionPositionCloseEndpoint(), { positionKey });
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

export function getRunLedger(runId: string) {
  return getJson<LedgerSummary>(workstationRunLedgerEndpoint(runId));
}

export function getRunTrialBalance(runId: string, accountType?: string) {
  return getJson<LedgerTrialBalanceLine[]>(workstationRunLedgerTrialBalanceEndpoint(runId, accountType));
}

export function getRunLedgerJournal(runId: string, options: { from?: string; to?: string } = {}) {
  return getJson<LedgerJournalLine[]>(workstationRunLedgerJournalEndpoint(runId, options));
}

export function getRunContinuity(runId: string) {
  return getJson<StrategyRunContinuityDto>(workstationRunContinuityEndpoint(runId));
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
  const path = workstationSecurityMasterSearchEndpoint({ query, take, activeOnly });
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
  return getJson<SecurityMasterEntry>(workstationSecurityMasterEntryEndpoint(securityId));
}

export function getSecurityIdentity(securityId: string) {
  return getJson<SecurityIdentityDrillIn>(workstationSecurityMasterIdentityEndpoint(securityId));
}

export function getSecurityHistory(securityId: string) {
  return getJson<unknown>(workstationSecurityMasterHistoryEndpoint(securityId));
}

export function getSecurityEconomicDefinition(securityId: string) {
  return getJson<unknown>(workstationSecurityMasterEconomicDefinitionEndpoint(securityId));
}

export function getSecurityTrustSnapshot(securityId: string) {
  return getJson<SecurityMasterTrustSnapshot>(workstationSecurityMasterTrustSnapshotEndpoint(securityId));
}

export function createSecurityMasterEntry(request: Record<string, unknown>) {
  return postJson<SecurityMasterEntry>(securityMasterEntryEndpoint(), request);
}

export function amendSecurityMasterEntry(request: Record<string, unknown>) {
  return postJson<SecurityMasterEntry>(securityMasterAmendEndpoint(), request);
}

export function upsertSecurityAlias(request: Record<string, unknown>) {
  return postJson<Record<string, unknown>>(securityMasterAliasUpsertEndpoint(), request);
}

// --- Security Master corporate actions and trading parameters ---

export function getCorporateActions(securityId: string) {
  return getJson<CorporateAction[]>(securityMasterCorporateActionsEndpoint(securityId));
}

export function getTradingParameters(securityId: string) {
  return getJson<TradingParameters>(securityMasterTradingParametersEndpoint(securityId));
}

export function getOperatorOverrides(securityId: string) {
  return getJson<OperatorOverridesDto>(securityMasterOperatorOverridesEndpoint(securityId));
}

export function patchOperatorOverrides(securityId: string, request: OperatorOverridesPatchRequest) {
  return patchJson<OperatorOverridesDto>(securityMasterOperatorOverridesEndpoint(securityId), request);
}

// --- Security Master conflicts ---

export function getSecurityConflicts() {
  return getJson<SecurityMasterConflict[]>(securityMasterConflictsEndpoint());
}

export function resolveSecurityConflict(request: ResolveConflictRequest) {
  return postJson<SecurityMasterConflict>(
    securityMasterConflictResolveEndpoint(request.conflictId),
    request
  );
}

export function bulkResolveSecurityConflicts(request: Record<string, unknown>) {
  return postJson<unknown>(SECURITY_MASTER_API_ENDPOINTS.workstationConflictsBulkResolve, request);
}

export function runReconciliation(request: Record<string, unknown>) {
  return postJson<unknown>(RECONCILIATION_API_ENDPOINTS.runs, request);
}

export function getReconciliationRun(reconciliationRunId: string) {
  return getJson<unknown>(reconciliationRunEndpoint(reconciliationRunId));
}

export function getReconciliationBreakQueue(status?: string, fundAccountId?: string) {
  return getJson<ReconciliationBreakQueueItem[]>(reconciliationBreakQueueEndpoint({ status, fundAccountId }));
}

export function getReconciliationBreakDetail(breakId: string) {
  return getJson<ReconciliationBreakQueueItem>(reconciliationBreakEndpoint(breakId));
}

export function getReconciliationBreakAudit(breakId: string) {
  return getJson<unknown>(reconciliationBreakAuditEndpoint(breakId));
}

export function reviewReconciliationBreak(request: ReviewReconciliationBreakRequest) {
  return postJson<ReconciliationBreakQueueItem>(
    reconciliationBreakReviewEndpoint(request.breakId),
    request
  );
}

export function resolveReconciliationBreak(request: ResolveReconciliationBreakRequest) {
  return postJson<ReconciliationBreakQueueItem>(
    reconciliationBreakResolveEndpoint(request.breakId),
    request
  );
}

export function assignReconciliationBreak(request: { breakId: string; [key: string]: unknown }) {
  return postJson<ReconciliationBreakQueueItem>(reconciliationBreakAssignEndpoint(request.breakId), request);
}

export function transitionReconciliationBreak(request: { breakId: string; [key: string]: unknown }) {
  return postJson<ReconciliationBreakQueueItem>(reconciliationBreakTransitionEndpoint(request.breakId), request);
}

export function addReconciliationBreakComment(request: { breakId: string; [key: string]: unknown }) {
  return postJson<ReconciliationBreakQueueItem>(reconciliationBreakCommentsEndpoint(request.breakId), request);
}

export function editReconciliationBreakComment(request: { breakId: string; commentId: string; [key: string]: unknown }) {
  return putJson<ReconciliationBreakQueueItem>(reconciliationBreakCommentEndpoint(request.breakId, request.commentId), request);
}

export function deleteReconciliationBreakComment(breakId: string, commentId: string) {
  return deleteJson<ReconciliationBreakQueueItem>(reconciliationBreakCommentEndpoint(breakId, commentId));
}

export function setReconciliationBreakRootCause(request: { breakId: string; [key: string]: unknown }) {
  return postJson<ReconciliationBreakQueueItem>(reconciliationBreakRootCauseEndpoint(request.breakId), request);
}

export function setReconciliationBreakResolution(request: { breakId: string; [key: string]: unknown }) {
  return postJson<ReconciliationBreakQueueItem>(reconciliationBreakResolutionEndpoint(request.breakId), request);
}

export function signOffReconciliationBreak(request: { breakId: string; [key: string]: unknown }) {
  return postJson<ReconciliationBreakQueueItem>(reconciliationBreakSignOffEndpoint(request.breakId), request);
}

export function reopenReconciliationBreak(request: { breakId: string; [key: string]: unknown }) {
  return postJson<ReconciliationBreakQueueItem>(reconciliationBreakReopenEndpoint(request.breakId), request);
}

export function dryRunReconciliationBreakBulkAction(request: Record<string, unknown>) {
  return postJson<unknown>(reconciliationBreakBulkDryRunEndpoint(), request);
}

export function executeReconciliationBreakBulkAction(request: Record<string, unknown>) {
  return postJson<unknown>(reconciliationBreakBulkExecuteEndpoint(), request);
}

export function getReconciliationBreakBulkActionResult(bulkActionId: string) {
  return getJson<unknown>(reconciliationBreakBulkResultEndpoint(bulkActionId));
}

export function getReconciliationCalibrationSummary() {
  return getJson<ReconciliationCalibrationSummary>(RECONCILIATION_API_ENDPOINTS.calibrationSummary);
}

// --- Backfill mutations ---

export function getBackfillProgress() {
  return getJson<BackfillProgressResponse>(BACKFILL_API_ENDPOINTS.progress);
}

export function triggerBackfill(request: BackfillTriggerRequest) {
  return postJson<BackfillTriggerResult>(BACKFILL_API_ENDPOINTS.run, request);
}

export function previewBackfill(request: BackfillTriggerRequest) {
  return postJson<BackfillPreviewResult>(BACKFILL_API_ENDPOINTS.runPreview, request);
}

// --- Provider management ---

export function setupProvider(request: import("@/types").ProviderSetupRequest) {
  return postJson<import("@/types").ProviderSetupResult>(PROVIDER_API_ENDPOINTS.configure, request);
}

export function getProviderRoutingConnections(options: ApiRequestOptions = {}) {
  return getJson<ProviderRoutingConnection[]>(PROVIDER_ROUTING_API_ENDPOINTS.connections, options);
}

export function getProviderRoutingBindings(options: ApiRequestOptions = {}) {
  return getJson<ProviderRoutingBinding[]>(PROVIDER_ROUTING_API_ENDPOINTS.bindings, options);
}

export function getProviderRoutingTrustSnapshots(options: ApiRequestOptions = {}) {
  return getJson<ProviderRoutingTrustSnapshot[]>(PROVIDER_ROUTING_API_ENDPOINTS.trustSnapshots, options);
}

export function previewProviderRoute(request: ProviderRoutePreviewRequest, options: ApiRequestOptions = {}) {
  return postJson<ProviderRoutePreviewResponse>(PROVIDER_ROUTING_API_ENDPOINTS.preview, request, options);
}

export function removeProvider(providerId: string) {
  return postJson<{ success: boolean; message: string }>(providerRemoveEndpoint(providerId));
}

export function testProviderConnection(providerId: string) {
  return postJson<{ success: boolean; latency: string | null; message: string }>(providerTestEndpoint(providerId));
}

// --- System overview ---

export function getSystemStatus(options: ApiRequestOptions = {}) {
  return getJson<unknown>(WORKSTATION_API_ENDPOINTS.systemStatus, options).then(normalizeSystemOverviewResponse);
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

export function getSymbols(options: ApiRequestOptions = {}) {
  return getJson<import("@/types").SymbolRecord[]>(SYMBOL_API_ENDPOINTS.symbols, options);
}

export function getSymbolsStatistics(options: ApiRequestOptions = {}) {
  return getJson<import("@/types").SymbolStatistics>(SYMBOL_API_ENDPOINTS.statistics, options);
}

export function searchSymbolsQuery(query: string, options: ApiRequestOptions = {}) {
  return getJson<import("@/types").SymbolRecord[]>(symbolSearchEndpoint(query), options);
}

export function addSymbol(symbol: string, provider?: string) {
  return postJson<{ success: boolean; symbol: string }>(SYMBOL_API_ENDPOINTS.add, { symbol, provider: provider ?? null });
}

export function removeSymbol(symbol: string) {
  return postJson<{ success: boolean; symbol: string }>(symbolRemoveEndpoint(symbol));
}

export function archiveSymbol(symbol: string) {
  return postJson<{ success: boolean; symbol: string }>(symbolArchiveEndpoint(symbol));
}

export function bulkAddSymbols(symbols: string[]) {
  return postJson<{ added: number; skipped: number; errors: string[] }>(SYMBOL_API_ENDPOINTS.bulkAdd, { symbols });
}

// --- Quality monitoring ---

export function getQualityDashboard() {
  return getJson<import("@/types").QualityDashboardResponse>(QUALITY_API_ENDPOINTS.dashboard);
}

export function getQualityGaps() {
  return getJson<import("@/types").QualityGapEntry[]>(QUALITY_API_ENDPOINTS.gaps);
}

export function getQualityAnomalies() {
  return getJson<import("@/types").QualityAnomalyEntry[]>(QUALITY_API_ENDPOINTS.anomalies);
}

export function acknowledgeAnomaly(anomalyId: string) {
  return postJson<void>(qualityAnomalyAcknowledgeEndpoint(anomalyId));
}

export function getQualityCompleteness() {
  return getJson<Array<{ symbol: string; score: number; sampledAt: string }>>(QUALITY_API_ENDPOINTS.completeness);
}

export function getRobinhoodConnectionStatus() {
  return getJson<BrokerageConnectionStatus>(brokerageConnectionStatusEndpoint("robinhood"));
}

export function startRobinhoodConnection() {
  return postJson<BrokerageConnectionStatus>(brokerageConnectionConnectEndpoint("robinhood"));
}

export function revokeRobinhoodConnection() {
  return deleteJson<BrokerageConnectionStatus>(brokerageConnectionEndpoint("robinhood"));
}

export function getPortfolioWorkspace(options: ApiRequestOptions = {}) {
  return getJson<import("@/types").PortfolioWorkspaceResponse>(WORKSTATION_API_ENDPOINTS.portfolio, options);
}

export function getAlpacaConnectionStatus(options: ApiRequestOptions = {}) {
  return getJson<BrokerageConnectionStatus>(brokerageConnectionStatusEndpoint("alpaca"), options);
}

export function getProviderConnections(options: ApiRequestOptions = {}) {
  return getJson<ProviderConnectionRow[]>(PROVIDER_API_ENDPOINTS.connections, options);
}

export function putProviderCredentials(
  providerId: string,
  request: ProviderCredentialUpsertRequest,
  options: ApiRequestOptions = {}
) {
  return putJson<ProviderCredentialMutationResult>(providerCredentialEndpoint(providerId), request, options);
}

export function verifyProviderConnection(providerId: string, options: ApiRequestOptions = {}) {
  return postJson<ProviderCredentialVerificationResult>(providerVerifyEndpoint(providerId), undefined, options);
}

export function deleteProviderCredentials(providerId: string, options: ApiRequestOptions = {}) {
  return deleteJson<ProviderCredentialMutationResult>(providerCredentialEndpoint(providerId), options);
}

export function connectAlpacaConnection(
  request: AlpacaBrokerageConnectionRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<BrokerageConnectionStatus>(brokerageConnectionConnectEndpoint("alpaca"), request, options);
}

export function revokeAlpacaConnection(options: ApiRequestOptions = {}) {
  return deleteJson<BrokerageConnectionStatus>(brokerageConnectionEndpoint("alpaca"), options);
}

export function getBrokerageHouseholdPortfolio(provider = "alpaca", options: ApiRequestOptions = {}) {
  return getJson<BrokerageHouseholdPortfolio>(portfolioHouseholdEndpoint(provider), options);
}

export function getPortfolioAggregate() {
  return getJson<unknown>(PORTFOLIO_API_ENDPOINTS.aggregate);
}

export function getPortfolioExposure() {
  return getJson<unknown>(PORTFOLIO_API_ENDPOINTS.exposure);
}

export function getPortfolioSymbolExposure(symbol: string) {
  return getJson<NetSymbolPosition>(portfolioSymbolExposureEndpoint(symbol));
}

// --- Live market data ---

export function getLiveQuote(symbol: string, options: ApiRequestOptions = {}) {
  return getJson<import("@/types").QuotesResponse>(marketDataQuoteEndpoint(symbol), options);
}

export function getLiveTrades(symbol: string, limit = 25, options: ApiRequestOptions = {}) {
  return getJson<import("@/types").TradesResponse>(marketDataTradesEndpoint(symbol, limit), options);
}

export function getLiveOrderbook(symbol: string, levels = 10, options: ApiRequestOptions = {}) {
  return getJson<import("@/types").OrderBookResponse>(marketDataOrderbookEndpoint(symbol, levels), options);
}

export function getLiveQuotesSnapshot(symbols?: readonly string[], options: ApiRequestOptions = {}) {
  return getJson<import("@/types").QuotesSnapshotResponse>(marketDataQuotesSnapshotEndpoint(symbols), options);
}

export interface HistoricalBarsRequest {
  intervalMinutes: number;
  from?: string;
  to?: string;
  maxBars?: number;
}

export function getHistoricalBars(symbol: string, request: HistoricalBarsRequest, options: ApiRequestOptions = {}) {
  return getJson<import("@/types").HistoricalBarsResponse>(historicalBarsEndpoint(symbol, request), options);
}

// --- Strategy Designer ---

export function getStrategyDesignerTemplates(options: ApiRequestOptions = {}) {
  return getJson<StrategyDesignTemplate[]>(STRATEGY_DESIGNER_API_ENDPOINTS.templates, options);
}

export function getStrategyDesignerFieldCatalog(options: ApiRequestOptions = {}) {
  return getJson<StrategyDesignFieldCatalogItem[]>(STRATEGY_DESIGNER_API_ENDPOINTS.fieldCatalog, options);
}

export function getStrategyDesignerDrafts(options: ApiRequestOptions = {}) {
  return getJson<StrategyDesignDraftSummary[]>(STRATEGY_DESIGNER_API_ENDPOINTS.drafts, options);
}

export function getStrategyDesignerDraft(documentId: string, options: ApiRequestOptions = {}) {
  return getJson<StrategyDesignDocument>(strategyDesignerDraftEndpoint(documentId), options);
}

export function saveStrategyDesignerDraft(request: StrategyDesignDraftSaveRequest, options: ApiRequestOptions = {}) {
  return postJson<StrategyDesignDraftSaveResponse>(STRATEGY_DESIGNER_API_ENDPOINTS.drafts, request, options);
}

export function validateStrategyDesignerDocument(document: StrategyDesignDocument, options: ApiRequestOptions = {}) {
  return postJson<StrategyDesignValidationResult>(STRATEGY_DESIGNER_API_ENDPOINTS.validate, document, options);
}

export function previewStrategyDesignerDocument(document: StrategyDesignDocument, options: ApiRequestOptions = {}) {
  return postJson<StrategyDesignPreviewResult>(STRATEGY_DESIGNER_API_ENDPOINTS.preview, document, options);
}

export function runStrategyDesignerBacktest(request: StrategyDesignRunBacktestRequest, options: ApiRequestOptions = {}) {
  return postJson<StrategyDesignRunBacktestResponse>(STRATEGY_DESIGNER_API_ENDPOINTS.runBacktest, request, options);
}

// --- Quant Lab ---

export function getQuantTemplates() {
  return getJson<import("@/types").QuantTemplatesResponse>(QUANT_API_ENDPOINTS.templates);
}

export function extractQuantParameters(source: string) {
  return postJson<import("@/types").QuantParametersResponse>(QUANT_API_ENDPOINTS.parameters, { source });
}

export function runQuantScript(request: import("@/types").QuantRunRequest) {
  return postJson<import("@/types").QuantRunResponse>(QUANT_API_ENDPOINTS.run, request);
}

export async function executeCell(request: CellExecuteRequest): Promise<CellExecuteResult> {
  const response = await runQuantScript({
    source: request.source,
    parameters: quantContextToParameters(request.context)
  });

  return mapQuantRunResponseToCellResult(request.cellId, response);
}

export async function fetchQuantData(request: DataFetchRequest, options: ApiRequestOptions = {}): Promise<DataFetchResult> {
  const intervalMinutes = quantDataIntervalMinutes(request.interval);
  const response = await getHistoricalBars(request.symbol, {
    intervalMinutes,
    from: request.from,
    to: request.to
  }, options);
  const bars = response.bars.map((bar) => ({
    timestamp: bar.start,
    open: bar.open,
    high: bar.high,
    low: bar.low,
    close: bar.close,
    volume: bar.volume
  }));

  return {
    symbol: response.symbol || request.symbol.trim().toUpperCase(),
    from: response.from ?? request.from,
    to: response.to ?? request.to,
    interval: request.interval,
    bars,
    rowCount: response.totalBars || bars.length
  };
}

function quantContextToParameters(context: CellExecutionContext): Record<string, string | number | boolean | null> {
  return {
    symbol: context.symbol ?? null,
    from: context.from ?? null,
    to: context.to ?? null,
    interval: context.interval ?? null
  };
}

function mapQuantRunResponseToCellResult(
  cellId: string,
  response: import("@/types").QuantRunResponse
): CellExecuteResult {
  const output: CellOutput[] = [];

  for (const line of response.consoleOutput.split(/\r?\n/).map((value) => value.trim()).filter(Boolean)) {
    output.push({ kind: "console", text: line, tone: "default" });
  }

  for (const metric of response.metrics) {
    output.push({ kind: "metric", text: `${metric.label}: ${metric.value}`, tone: "default" });
  }

  for (const diagnostic of [...response.compilationErrors, ...response.runtimeDiagnostics]) {
    output.push({
      kind: "error",
      text: diagnostic.line > 0
        ? `${diagnostic.severity}: ${diagnostic.message} (${diagnostic.line}:${diagnostic.column})`
        : `${diagnostic.severity}: ${diagnostic.message}`,
      tone: diagnostic.severity.toLowerCase() === "warning" ? "warning" : "danger"
    });
  }

  if (response.runtimeError) {
    output.push({ kind: "error", text: response.runtimeError, tone: "danger" });
  }

  return {
    cellId,
    success: response.success,
    output,
    elapsedMs: response.elapsedMs,
    errorMessage: response.runtimeError ?? response.compilationErrors[0]?.message ?? null
  };
}

function quantDataIntervalMinutes(interval: DataFetchRequest["interval"]): number {
  switch (interval) {
    case "minute":
      return 1;
    case "hourly":
      return 60;
    case "daily":
    default:
      return 1440;
  }
}
