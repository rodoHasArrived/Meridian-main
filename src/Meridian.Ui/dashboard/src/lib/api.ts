import type {
  BackfillPreviewResult,
  BackfillProgressResponse,
  BackfillTriggerRequest,
  BackfillTriggerResult,
  AccountingSystemImportDetail,
  AccountingSystemImportRequest,
  AccountingSystemProvider,
  AccountingSystemReconciliationSummary,
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
  DataUploadPreviewResult,
  DataUploadTemplateCatalog,
  DataWorkspaceResponse,
  EquityCurveSummary,
  EvidenceCompleteness,
  EvidenceGraph,
  EvidencePacket,
  EvidencePacketExportRequest,
  EvidencePacketExportResponse,
  EvidenceSubject,
  EvidenceTemplate,
  EvidenceVaultIdentity,
  EvidenceVaultLookupRequest,
  ExportAnalysisResult,
  ExecutionControlSnapshot,
  ExecutionAuditEntry,
  AccountingWorkspaceResponse,
  AssetOperationsDetail,
  ReportingWorkspaceResponse,
  InvestmentAccountingTransactionLabPreview,
  InvestmentAccountingTransactionLabRequest,
  InstrumentPassport,
  LedgerJournalLine,
  LedgerSummary,
  LedgerTrialBalanceLine,
  MetricSnapshot,
  MultiAssetCoverageSummary,
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
  ProviderReadinessSummary,
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
  ReconciliationBulkCaseworkRequest,
  ReconciliationBulkCaseworkResult,
  ReconciliationCalibrationSummary,
  StatementRunException,
  StatementRunSummary,
  ReconciliationCaseworkCommand,
  ResolveReconciliationBreakRequest,
  ResolveConflictRequest,
  ReviewReconciliationBreakRequest,
  StrategyBriefingResponse,
  StrategyRunSummaryApiRecord,
  StrategyWorkspaceResponse,
  RunAttributionSummary,
  RunCashFlowSummary,
  RunComparisonRow,
  RunDiff,
  RunFillSummary,
  OperatorOverridesDto,
  OperatorOverridesPatchRequest,
  SecurityAssetProfileApprovalRequest,
  SecurityAssetProfileDefinition,
  SecurityAssetProfileDraftRequest,
  SecurityAssetProfileGovernanceResult,
  SecurityAssetProfileLineage,
  SecurityAssetProfileRollbackRequest,
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
  UpdateExecutionPositionLimitRequest,
  FeatureCapabilitySettingsResponse,
  FinancialRecordExplorer,
  FinancialRecordExplorerSavedViewDto,
  FinancialRecordExplorerSavedViewSaveRequest,
  LedgerMappingAssignmentRequest,
  LedgerMappingAssignmentResult,
  LedgerMappingWorkbench,
  OperationsApprovalDecisionRequest,
  OperationsApprovalPolicyMatrix,
  OperationsApprovalPolicyRuleUpsertRequest,
  OperationsApprovalPolicyRuleUpsertResult,
  OperationsCloseCalendar,
  OperationsCloseCalendarItemUpsertRequest,
  OperationsCloseCalendarItemUpsertResult,
  OperationsRejectWorkflowRequest,
  OperationsTransitionResult,
  PlaidLinkTokenRequest,
  PlaidLinkTokenResponse,
  PlaidPublicTokenExchangeRequest,
  PlaidPublicTokenExchangeResult,
  PlaidInstitutionSearchResult,
  RolePermissionCatalog,
  RolePermissionProfileUpsertRequest,
  RolePermissionProfileUpsertResult,
  UserAccount,
  UserAccountAuditEvent,
  UserAccountDisableRequest,
  UserAccountMutationResult,
  UserAccountUpsertRequest,
  UserPasswordResetRequest,
  UserSessionRevokeRequest,
  UserSessionRevokeResult,
  ManualJournalEntryDraft,
  ManualJournalEntryWorkbench,
  PrivateCapitalActivityProjection,
  PrivateCapitalCapitalAccountSubledger,
  PrivateCapitalFundEventLedgerRecord,
  PrivateCapitalReportOutput,
  FundReportPackGenerateRequest,
  FundReportPackSnapshot,
  ReportPackDeliveryAttempt,
  ReportPackDeliveryFailureRequest,
  ReportPackDeliveryHistory,
  ReportPackDeliveryRequest,
  ReportTemplateDecisionRequest,
  ReportTemplateDraftRequest,
  ReportTemplateGovernanceRecord,
  RenderReportTemplateRequest,
  RenderReportTemplateResponse,
  ReportingDueScheduleRunResult,
  ReportingRunRequest,
  ReportingRunResult,
  ReportingScheduleRecord,
  ReportingScheduleRunResult,
  ReportingScheduleUpsertRequest,
  SaveManualJournalEntryDraftRequest,
  SubmitManualJournalEntryApprovalRequest,
  ValidateManualJournalEntryDraftRequest
} from "@/types";
import {
  AUTH_API_ENDPOINTS,
  ACCOUNTING_SYSTEM_API_ENDPOINTS,
  BACKFILL_API_ENDPOINTS,
  EXECUTION_API_ENDPOINTS,
  EXPORT_API_ENDPOINTS,
  FUND_STRUCTURE_API_ENDPOINTS,
  PORTFOLIO_API_ENDPOINTS,
  PLAID_API_ENDPOINTS,
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
  executionSymbolPositionLimitEndpoint,
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
  portfolioRunCashFlowsEndpoint,
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
  reconciliationBreakBulkStatusEndpoint,
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
  reconciliationStatementExceptionsEndpoint,
  reconciliationStatementRunEndpoint,
  reconciliationStatementRunsEndpoint,
  replayFilesEndpoint,
  replaySessionActionEndpoint,
  reportingPackDeliveriesEndpoint,
  reportingPackDeliveryFailuresEndpoint,
  reportingTemplateApproveEndpoint,
  reportingTemplateRejectEndpoint,
  reportingTemplateSubmitEndpoint,
  reportingSchedulePauseEndpoint,
  reportingScheduleResumeEndpoint,
  reportingScheduleRunNowEndpoint,
  securityMasterAssetProfileApproveEndpoint,
  securityMasterAssetProfileDraftsEndpoint,
  securityMasterAssetProfileLineageEndpoint,
  securityMasterAssetProfileRollbackEndpoint,
  securityMasterAssetProfilesEndpoint,
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
  workstationFinancialRecordExplorerEndpoint,
  workstationFinancialRecordExplorerSavedViewsEndpoint,
  workstationAssetOperationsEndpoint,
  workstationOperatorInboxEndpoint,
  workstationOperationsContinuityApprovalApproveEndpoint,
  workstationOperationsContinuityApprovalRejectEndpoint,
  workstationOperationsContinuityDetailEndpoint,
  workstationOperationsContinuityEndpoint,
  workstationOperationsContinuityCloseCalendarEndpoint,
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
  workstationSecurityMasterInstrumentPassportEndpoint,
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
const csrfCookieName = "mdc-csrf";
const csrfHeaderName = "X-CSRF-Token";

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
    headers: buildMutationHeaders(),
    body: body !== undefined ? JSON.stringify(body) : undefined
  });

  if (!response.ok) {
    throw await buildApiError(path, response);
  }

  return readJsonResponse<T>(path, response);
}

async function postFormData<T>(path: string, formData: FormData, options: ApiRequestOptions = {}): Promise<T> {
  const response = await fetch(path, {
    method: "POST",
    signal: options.signal,
    headers: buildMultipartMutationHeaders(),
    body: formData
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
    headers: buildMutationHeaders(),
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
    headers: buildMutationHeaders(),
    body: body !== undefined ? JSON.stringify(body) : undefined
  });

  if (!response.ok) {
    throw await buildApiError(path, response);
  }

  return readJsonResponse<T>(path, response);
}

async function deleteJson<T>(path: string, options: ApiRequestOptions = {}, body?: unknown): Promise<T> {
  const response = await fetch(path, {
    method: "DELETE",
    signal: options.signal,
    headers: buildMutationHeaders(),
    body: body !== undefined ? JSON.stringify(body) : undefined
  });

  if (!response.ok) {
    throw await buildApiError(path, response);
  }

  return readJsonResponse<T>(path, response);
}

function buildMutationHeaders(): Record<string, string> {
  const headers: Record<string, string> = {
    Accept: "application/json",
    "Content-Type": "application/json"
  };

  const csrfToken = readCookie(csrfCookieName);
  if (csrfToken) {
    headers[csrfHeaderName] = csrfToken;
  }

  return headers;
}

function buildMultipartMutationHeaders(): Record<string, string> {
  const headers: Record<string, string> = {
    Accept: "application/json"
  };

  const csrfToken = readCookie(csrfCookieName);
  if (csrfToken) {
    headers[csrfHeaderName] = csrfToken;
  }

  return headers;
}

function readCookie(name: string): string | undefined {
  if (typeof document === "undefined" || !document.cookie) {
    return undefined;
  }

  const prefix = `${name}=`;
  const cookie = document.cookie
    .split(";")
    .map((part) => part.trim())
    .find((part) => part.startsWith(prefix));
  if (!cookie) {
    return undefined;
  }

  return decodeURIComponent(cookie.slice(prefix.length));
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
  return getJson<StrategyWorkspaceResponse>(WORKSTATION_API_ENDPOINTS.strategy, options);
}

export function getStrategyBriefing(options: ApiRequestOptions = {}) {
  return getJson<StrategyBriefingResponse>(WORKSTATION_API_ENDPOINTS.strategyBriefing, options);
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

export function getFinancialRecordExplorer(explorerId: string, options: ApiRequestOptions = {}) {
  return getJson<FinancialRecordExplorer>(workstationFinancialRecordExplorerEndpoint(explorerId), options);
}

export function saveFinancialRecordExplorerView(
  explorerId: string,
  request: FinancialRecordExplorerSavedViewSaveRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<FinancialRecordExplorerSavedViewDto>(
    workstationFinancialRecordExplorerSavedViewsEndpoint(explorerId),
    request,
    options
  );
}

export function getRolePermissionCatalog(options: ApiRequestOptions = {}) {
  return getJson<RolePermissionCatalog>(AUTH_API_ENDPOINTS.roles, options);
}

export function createRolePermissionProfile(
  request: RolePermissionProfileUpsertRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<RolePermissionProfileUpsertResult>(AUTH_API_ENDPOINTS.roleProfiles, request, options);
}

export function getUserAccounts(options: ApiRequestOptions = {}) {
  return getJson<UserAccount[]>(AUTH_API_ENDPOINTS.accounts, options);
}

export function upsertUserAccount(request: UserAccountUpsertRequest, options: ApiRequestOptions = {}) {
  return putJson<UserAccountMutationResult>(authAccountEndpoint(request.username), request, options);
}

export function resetUserPassword(request: UserPasswordResetRequest, options: ApiRequestOptions = {}) {
  return postJson<UserAccountMutationResult>(authAccountPasswordResetEndpoint(request.username), request, options);
}

export function setUserAccountDisabled(request: UserAccountDisableRequest, options: ApiRequestOptions = {}) {
  return postJson<UserAccountMutationResult>(authAccountDisableEndpoint(request.username), request, options);
}

export function revokeUserSessions(request: UserSessionRevokeRequest, options: ApiRequestOptions = {}) {
  return postJson<UserSessionRevokeResult>(AUTH_API_ENDPOINTS.sessionsRevoke, request, options);
}

export function getUserAccountAudit(options: ApiRequestOptions = {}) {
  return getJson<UserAccountAuditEvent[]>(AUTH_API_ENDPOINTS.audit, options);
}

function authAccountEndpoint(username: string) {
  return AUTH_API_ENDPOINTS.accountByUsername.replace("{username}", encodeURIComponent(username));
}

function authAccountPasswordResetEndpoint(username: string) {
  return AUTH_API_ENDPOINTS.accountPasswordReset.replace("{username}", encodeURIComponent(username));
}

function authAccountDisableEndpoint(username: string) {
  return AUTH_API_ENDPOINTS.accountDisable.replace("{username}", encodeURIComponent(username));
}

export function getSecurityAssetProfiles(options: ApiRequestOptions = {}) {
  return getJson<SecurityAssetProfileDefinition[]>(securityMasterAssetProfilesEndpoint(), options);
}

export function getSecurityAssetProfileLineage(profileId: string, options: ApiRequestOptions = {}) {
  return getJson<SecurityAssetProfileLineage>(securityMasterAssetProfileLineageEndpoint(profileId), options);
}

export function draftSecurityAssetProfile(
  request: SecurityAssetProfileDraftRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<SecurityAssetProfileGovernanceResult>(securityMasterAssetProfileDraftsEndpoint(), request, options);
}

export function approveSecurityAssetProfile(
  request: SecurityAssetProfileApprovalRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<SecurityAssetProfileGovernanceResult>(securityMasterAssetProfileApproveEndpoint(), request, options);
}

export function rollbackSecurityAssetProfile(
  request: SecurityAssetProfileRollbackRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<SecurityAssetProfileGovernanceResult>(securityMasterAssetProfileRollbackEndpoint(), request, options);
}

export function getLedgerMappingWorkbench(options: ApiRequestOptions = {}) {
  return getJson<LedgerMappingWorkbench>(FUND_STRUCTURE_API_ENDPOINTS.ledgerMappingWorkbench, options);
}

export function assignLedgerMapping(request: LedgerMappingAssignmentRequest, options: ApiRequestOptions = {}) {
  return postJson<LedgerMappingAssignmentResult>(
    FUND_STRUCTURE_API_ENDPOINTS.ledgerMappingAssignments,
    request,
    options
  );
}

export function previewInvestmentAccountingTransaction(
  request: InvestmentAccountingTransactionLabRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<InvestmentAccountingTransactionLabPreview>(
    FUND_STRUCTURE_API_ENDPOINTS.transactionLabPreview,
    request,
    options
  );
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

export function approveOperationsContinuityWorkflow(
  workflowId: string,
  request: OperationsApprovalDecisionRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<OperationsTransitionResult>(
    workstationOperationsContinuityApprovalApproveEndpoint(workflowId),
    request,
    options
  );
}

export function rejectOperationsContinuityWorkflow(
  workflowId: string,
  request: OperationsRejectWorkflowRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<OperationsTransitionResult>(
    workstationOperationsContinuityApprovalRejectEndpoint(workflowId),
    request,
    options
  );
}

export function getOperationsApprovalPolicyMatrix(options: ApiRequestOptions = {}) {
  return getJson<OperationsApprovalPolicyMatrix>(WORKSTATION_API_ENDPOINTS.operationsContinuityApprovalPolicyMatrix, options);
}

export function upsertOperationsApprovalPolicyRule(
  request: OperationsApprovalPolicyRuleUpsertRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<OperationsApprovalPolicyRuleUpsertResult>(
    WORKSTATION_API_ENDPOINTS.operationsContinuityApprovalPolicyRules,
    request,
    options
  );
}

export function getOperationsCloseCalendar(
  filters: { fundAccountId?: string; periodId?: string } = {},
  options: ApiRequestOptions = {}
) {
  return getJson<OperationsCloseCalendar>(workstationOperationsContinuityCloseCalendarEndpoint(filters), options);
}

export function upsertOperationsCloseCalendarItem(
  request: OperationsCloseCalendarItemUpsertRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<OperationsCloseCalendarItemUpsertResult>(
    WORKSTATION_API_ENDPOINTS.operationsContinuityCloseCalendarItems,
    request,
    options
  );
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

export function searchEvidenceVault(request: EvidenceVaultLookupRequest, options: ApiRequestOptions = {}) {
  return postJson<EvidenceVaultIdentity[]>(WORKSTATION_API_ENDPOINTS.evidenceVaultSearch, request, options);
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
  return getJson<DataWorkspaceResponse>(WORKSTATION_API_ENDPOINTS.data, options);
}

export function getDataUploadTemplates(options: ApiRequestOptions = {}) {
  return getJson<DataUploadTemplateCatalog>(WORKSTATION_API_ENDPOINTS.dataUploadTemplates, options);
}

export function previewDataUpload(
  request: { templateId: string; file: File },
  options: ApiRequestOptions = {}
) {
  const formData = new FormData();
  formData.append("templateId", request.templateId);
  formData.append("file", request.file);
  return postFormData<DataUploadPreviewResult>(WORKSTATION_API_ENDPOINTS.dataUploadPreview, formData, options);
}

export function getDataOperationsWorkspace(options: ApiRequestOptions = {}) {
  return getDataWorkspace(options);
}

export function getAccountingWorkspace(options: ApiRequestOptions = {}) {
  return getJson<AccountingWorkspaceResponse>(WORKSTATION_API_ENDPOINTS.accounting, options);
}

export function getAccountingConfiguration(options: ApiRequestOptions = {}) {
  return getJson<import("@/types").AccountingConfigurationWorkspace>(WORKSTATION_API_ENDPOINTS.accountingConfiguration, options);
}

export function getManualJournalEntryWorkbench(options: ApiRequestOptions = {}) {
  return getJson<ManualJournalEntryWorkbench>(WORKSTATION_API_ENDPOINTS.manualJournalEntryWorkbench, options);
}

export interface PrivateCapitalActivityQuery {
  fundProfileId?: string | null;
  ledgerBookId?: string | null;
  fundEventId?: string | null;
  capitalAccountId?: string | null;
  investorId?: string | null;
}

export function getPrivateCapitalActivity(
  query: PrivateCapitalActivityQuery = {},
  options: ApiRequestOptions = {}
) {
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(query)) {
    if (value) {
      params.set(key, value);
    }
  }

  const suffix = params.toString();
  return getJson<PrivateCapitalActivityProjection>(
    `${WORKSTATION_API_ENDPOINTS.privateCapitalActivity}${suffix ? `?${suffix}` : ""}`,
    options
  );
}

export interface PrivateCapitalFundEventRecordQuery {
  fundProfileId?: string | null;
  ledgerBookId?: string | null;
  fundEventId: string;
}

export function getPrivateCapitalFundEventRecord(
  query: PrivateCapitalFundEventRecordQuery,
  options: ApiRequestOptions = {}
) {
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(query)) {
    if (value) {
      params.set(key, value);
    }
  }

  return getJson<PrivateCapitalFundEventLedgerRecord>(
    `${WORKSTATION_API_ENDPOINTS.privateCapitalFundEventRecord}?${params.toString()}`,
    options
  );
}

export interface PrivateCapitalCapitalAccountSubledgerQuery {
  fundProfileId?: string | null;
  ledgerBookId?: string | null;
  capitalAccountId: string;
  investorId?: string | null;
  currency?: string | null;
}

export function getPrivateCapitalCapitalAccountSubledger(
  query: PrivateCapitalCapitalAccountSubledgerQuery,
  options: ApiRequestOptions = {}
) {
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(query)) {
    if (value) {
      params.set(key, value);
    }
  }

  return getJson<PrivateCapitalCapitalAccountSubledger>(
    `${WORKSTATION_API_ENDPOINTS.privateCapitalCapitalAccountSubledger}?${params.toString()}`,
    options
  );
}

export interface PrivateCapitalReportOutputQuery {
  fundProfileId?: string | null;
  ledgerBookId?: string | null;
  reportOutputId?: string | null;
  reportPackId?: string | null;
  fundEventId?: string | null;
  capitalAccountId?: string | null;
  investorId?: string | null;
}

export function getPrivateCapitalReportOutput(
  query: PrivateCapitalReportOutputQuery,
  options: ApiRequestOptions = {}
) {
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(query)) {
    if (value) {
      params.set(key, value);
    }
  }

  return getJson<PrivateCapitalReportOutput>(
    `${WORKSTATION_API_ENDPOINTS.privateCapitalReportOutput}?${params.toString()}`,
    options
  );
}

export function saveManualJournalEntryDraft(
  request: SaveManualJournalEntryDraftRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<ManualJournalEntryDraft>(WORKSTATION_API_ENDPOINTS.manualJournalEntryDrafts, request, options);
}

export function validateManualJournalEntryDraft(
  request: ValidateManualJournalEntryDraftRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<ManualJournalEntryDraft>(WORKSTATION_API_ENDPOINTS.manualJournalEntryValidate, request, options);
}

export function submitManualJournalEntryApproval(
  request: SubmitManualJournalEntryApprovalRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<ManualJournalEntryDraft>(WORKSTATION_API_ENDPOINTS.manualJournalEntrySubmitApproval, request, options);
}

export function getAccountingSystemProviders(options: ApiRequestOptions = {}) {
  return getJson<AccountingSystemProvider[]>(ACCOUNTING_SYSTEM_API_ENDPOINTS.providers, options);
}

export function previewAccountingSystemImport(
  request: AccountingSystemImportRequest = { providerId: "quickbooks-fixture", persistPreview: true },
  options: ApiRequestOptions = {}
) {
  return postJson<AccountingSystemImportDetail>(ACCOUNTING_SYSTEM_API_ENDPOINTS.importPreview, request, options);
}

export function getLatestAccountingSystemImport(options: ApiRequestOptions = {}) {
  return getJson<AccountingSystemImportDetail>(ACCOUNTING_SYSTEM_API_ENDPOINTS.importLatest, options);
}

export function getLatestAccountingSystemReconciliation(options: ApiRequestOptions = {}) {
  return getJson<AccountingSystemReconciliationSummary>(ACCOUNTING_SYSTEM_API_ENDPOINTS.reconciliationLatest, options);
}

export function previewAccountingConfigurationTemplate(
  request: import("@/types").PreviewJournalTemplateRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<import("@/types").AccountingJournalTemplatePreview>(WORKSTATION_API_ENDPOINTS.accountingConfigurationPreview, request, options);
}

export function upsertAccountingConfigurationChartNode(
  request: import("@/types").UpsertChartOfAccountsNodeRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<import("@/types").AccountingConfigurationWorkspace>(WORKSTATION_API_ENDPOINTS.accountingConfigurationChart, request, options);
}

export function upsertAccountingConfigurationTemplate(
  request: import("@/types").UpsertJournalEntryTemplateRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<import("@/types").AccountingConfigurationWorkspace>(WORKSTATION_API_ENDPOINTS.accountingConfigurationTemplates, request, options);
}

export function upsertAccountingConfigurationPostingRule(
  request: import("@/types").UpsertPostingRuleRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<import("@/types").AccountingConfigurationWorkspace>(WORKSTATION_API_ENDPOINTS.accountingConfigurationPostingRules, request, options);
}

export function activateAccountingConfiguration(
  request: import("@/types").ActivateAccountingConfigurationRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<import("@/types").AccountingConfigurationWorkspace>(WORKSTATION_API_ENDPOINTS.accountingConfigurationActivate, request, options);
}

export function getGovernanceWorkspace(options: ApiRequestOptions = {}) {
  return getAccountingWorkspace(options);
}

export function getReportingWorkspace(options: ApiRequestOptions = {}) {
  return getJson<ReportingWorkspaceResponse>(WORKSTATION_API_ENDPOINTS.reporting, options);
}

export function runReportingNow(request: ReportingRunRequest, options: ApiRequestOptions = {}) {
  return postJson<ReportingRunResult>(FUND_STRUCTURE_API_ENDPOINTS.reportingRuns, request, options);
}

export function generateReportPack(request: FundReportPackGenerateRequest, options: ApiRequestOptions = {}) {
  return postJson<FundReportPackSnapshot>(FUND_STRUCTURE_API_ENDPOINTS.reportPacks, request, options);
}

export function createReportTemplateDraft(request: ReportTemplateDraftRequest, options: ApiRequestOptions = {}) {
  return postJson<ReportTemplateGovernanceRecord>(
    FUND_STRUCTURE_API_ENDPOINTS.reportingTemplateDrafts,
    request,
    options
  );
}

export function renderReportTemplate(request: RenderReportTemplateRequest, options: ApiRequestOptions = {}) {
  return postJson<RenderReportTemplateResponse>(
    FUND_STRUCTURE_API_ENDPOINTS.reportingTemplateRender,
    request,
    options
  );
}

export function submitReportTemplateDraft(
  templateName: string,
  version: number,
  request: ReportTemplateDecisionRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<ReportTemplateGovernanceRecord>(
    reportingTemplateSubmitEndpoint(templateName, version),
    request,
    options
  );
}

export function approveReportTemplateDraft(
  templateName: string,
  version: number,
  request: ReportTemplateDecisionRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<ReportTemplateGovernanceRecord>(
    reportingTemplateApproveEndpoint(templateName, version),
    request,
    options
  );
}

export function rejectReportTemplateDraft(
  templateName: string,
  version: number,
  request: ReportTemplateDecisionRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<ReportTemplateGovernanceRecord>(
    reportingTemplateRejectEndpoint(templateName, version),
    request,
    options
  );
}

export function deliverReportPack(
  reportId: string,
  request: ReportPackDeliveryRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<ReportPackDeliveryAttempt>(reportingPackDeliveriesEndpoint(reportId), request, options);
}

export function recordReportPackDeliveryFailure(
  reportId: string,
  request: ReportPackDeliveryFailureRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<ReportPackDeliveryAttempt>(reportingPackDeliveryFailuresEndpoint(reportId), request, options);
}

export function getReportPackDeliveryHistory(reportId: string, options: ApiRequestOptions = {}) {
  return getJson<ReportPackDeliveryHistory>(reportingPackDeliveriesEndpoint(reportId), options);
}

export function listReportingSchedules(options: ApiRequestOptions = {}) {
  return getJson<ReportingScheduleRecord[]>(FUND_STRUCTURE_API_ENDPOINTS.reportingSchedules, options);
}

export function saveReportingSchedule(request: ReportingScheduleUpsertRequest, options: ApiRequestOptions = {}) {
  return postJson<ReportingScheduleRecord>(FUND_STRUCTURE_API_ENDPOINTS.reportingSchedules, request, options);
}

export function pauseReportingSchedule(scheduleId: string, options: ApiRequestOptions = {}) {
  return postJson<ReportingScheduleRecord>(reportingSchedulePauseEndpoint(scheduleId), undefined, options);
}

export function resumeReportingSchedule(scheduleId: string, options: ApiRequestOptions = {}) {
  return postJson<ReportingScheduleRecord>(reportingScheduleResumeEndpoint(scheduleId), undefined, options);
}

export function runReportingScheduleNow(scheduleId: string, options: ApiRequestOptions = {}) {
  return postJson<ReportingScheduleRunResult>(reportingScheduleRunNowEndpoint(scheduleId), undefined, options);
}

export function runDueReportingSchedules(options: ApiRequestOptions = {}) {
  return postJson<ReportingDueScheduleRunResult>(FUND_STRUCTURE_API_ENDPOINTS.reportingScheduleRunDue, undefined, options);
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
  evidenceReferences?: string[];
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

export function updateExecutionDefaultPositionLimit(request: UpdateExecutionPositionLimitRequest) {
  return postJson<ExecutionControlSnapshot>(EXECUTION_API_ENDPOINTS.defaultPositionLimit, request);
}

export function updateExecutionSymbolPositionLimit(symbol: string, request: UpdateExecutionPositionLimitRequest) {
  return postJson<ExecutionControlSnapshot>(executionSymbolPositionLimitEndpoint(symbol), request);
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

export function getSecurityInstrumentPassport(securityId: string) {
  return getJson<InstrumentPassport>(workstationSecurityMasterInstrumentPassportEndpoint(securityId));
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

export function getReconciliationStatementRuns(options: ApiRequestOptions = {}) {
  return getJson<StatementRunSummary[]>(reconciliationStatementRunsEndpoint(), options);
}

export function getReconciliationStatementRun(runId: string, options: ApiRequestOptions = {}) {
  return getJson<StatementRunSummary>(reconciliationStatementRunEndpoint(runId), options);
}

export function getReconciliationStatementExceptions(options: ApiRequestOptions = {}) {
  return getJson<StatementRunException[]>(reconciliationStatementExceptionsEndpoint(), options);
}

export const getStatementRuns = getReconciliationStatementRuns;
export const getStatementRun = getReconciliationStatementRun;
export const getStatementRunExceptions = getReconciliationStatementExceptions;

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

export function assignReconciliationBreak(request: ReconciliationCaseworkCommand) {
  return postJson<ReconciliationBreakQueueItem>(reconciliationBreakAssignEndpoint(request.breakId), request);
}

export function transitionReconciliationBreak(request: ReconciliationCaseworkCommand) {
  return postJson<ReconciliationBreakQueueItem>(reconciliationBreakTransitionEndpoint(request.breakId), request);
}

export function addReconciliationBreakComment(request: ReconciliationCaseworkCommand) {
  return postJson<ReconciliationBreakQueueItem>(reconciliationBreakCommentsEndpoint(request.breakId), request);
}

export function editReconciliationBreakComment(request: ReconciliationCaseworkCommand) {
  return postJson<ReconciliationBreakQueueItem>(
    reconciliationBreakCommentEndpoint(request.breakId, request.commentId ?? ""),
    request
  );
}

export function deleteReconciliationBreakComment(request: ReconciliationCaseworkCommand) {
  return deleteJson<ReconciliationBreakQueueItem>(
    reconciliationBreakCommentEndpoint(request.breakId, request.commentId ?? ""),
    {},
    request
  );
}

export function setReconciliationBreakRootCause(request: ReconciliationCaseworkCommand) {
  return postJson<ReconciliationBreakQueueItem>(reconciliationBreakRootCauseEndpoint(request.breakId), request);
}

export function setReconciliationBreakResolution(request: ReconciliationCaseworkCommand) {
  return postJson<ReconciliationBreakQueueItem>(reconciliationBreakResolutionEndpoint(request.breakId), request);
}

export function signOffReconciliationBreak(request: ReconciliationCaseworkCommand) {
  return postJson<ReconciliationBreakQueueItem>(reconciliationBreakSignOffEndpoint(request.breakId), request);
}

export function reopenReconciliationBreak(request: ReconciliationCaseworkCommand) {
  return postJson<ReconciliationBreakQueueItem>(reconciliationBreakReopenEndpoint(request.breakId), request);
}

export function dryRunReconciliationBreakBulkAction(request: ReconciliationBulkCaseworkRequest) {
  return postJson<ReconciliationBulkCaseworkResult>(reconciliationBreakBulkDryRunEndpoint(), request);
}

export function executeReconciliationBreakBulkAction(request: ReconciliationBulkCaseworkRequest) {
  return postJson<ReconciliationBulkCaseworkResult>(reconciliationBreakBulkExecuteEndpoint(), request);
}

export function getReconciliationBreakBulkActionStatus(bulkActionId: string) {
  return getJson<unknown>(reconciliationBreakBulkStatusEndpoint(bulkActionId));
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

export function searchPlaidInstitutions(query: string, options: ApiRequestOptions = {}) {
  const params = new URLSearchParams({
    query,
    products: "transactions,auth,identity,investments",
    countryCodes: "US"
  });
  return getJson<PlaidInstitutionSearchResult>(`${PLAID_API_ENDPOINTS.institutionSearch}?${params.toString()}`, options);
}

export function createPlaidLinkToken(request: PlaidLinkTokenRequest, options: ApiRequestOptions = {}) {
  return postJson<PlaidLinkTokenResponse>(PLAID_API_ENDPOINTS.linkToken, request, options);
}

export function exchangePlaidPublicToken(request: PlaidPublicTokenExchangeRequest, options: ApiRequestOptions = {}) {
  return postJson<PlaidPublicTokenExchangeResult>(PLAID_API_ENDPOINTS.publicTokenExchange, request, options);
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

export function getPortfolioMultiAssetCoverage(options: ApiRequestOptions = {}) {
  return getJson<MultiAssetCoverageSummary>(WORKSTATION_API_ENDPOINTS.portfolioMultiAssetCoverage, options);
}

export function getAssetOperations(securityId: string, options: ApiRequestOptions = {}) {
  return getJson<AssetOperationsDetail>(workstationAssetOperationsEndpoint(securityId), options);
}

export function getAlpacaConnectionStatus(options: ApiRequestOptions = {}) {
  return getJson<BrokerageConnectionStatus>(brokerageConnectionStatusEndpoint("alpaca"), options);
}

export function getProviderConnections(options: ApiRequestOptions = {}) {
  return getJson<ProviderConnectionRow[]>(PROVIDER_API_ENDPOINTS.connections, options);
}

export function getProviderReadiness(options: ApiRequestOptions = {}) {
  return getJson<ProviderReadinessSummary>(PROVIDER_API_ENDPOINTS.readiness, options);
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

export interface FundStructureSetupDraft {
  organization: { organizationId?: string | null; code: string; name: string; baseCurrency: string; description?: string | null };
  businessLane: { businessId?: string | null; businessKind: string; code: string; name: string; baseCurrency: string; description?: string | null };
  clientOrFund: { clientId?: string | null; fundId?: string | null; createClient: boolean; code: string; name: string; baseCurrency: string; description?: string | null; clientSegmentKind?: string };
  legalEntity: {
    entityId?: string | null;
    entityType: string;
    code: string;
    name: string;
    jurisdiction: string;
    baseCurrency: string;
    description?: string | null;
    legalForm?: string;
    lifecycleStatus?: string;
    registrationNumber?: string | null;
    beneficialOwners?: Array<{
      ownerName: string;
      ownershipPercent?: number | null;
      ownerIdentifier?: string | null;
      isControlPerson?: boolean;
      effectiveFrom?: string | null;
      effectiveTo?: string | null;
      notes?: string | null;
    }>;
    initialLifecycleEventKind?: string;
    initialLifecycleEventSummary?: string | null;
    initialLifecycleEvidenceReference?: string | null;
  };
  vehicle: { vehicleId?: string | null; code: string; name: string; baseCurrency: string; description?: string | null };
  investmentPortfolio: { investmentPortfolioId?: string | null; code: string; name: string; baseCurrency: string; description?: string | null };
  accountHandoff: { accountCode: string; displayName: string; accountType: string; baseCurrency: string; institution?: string | null; ledgerReference?: string | null; notes?: string | null };
  initialOwnershipLinks?: Array<{ ownershipLinkId?: string | null; parent: string; child: string; relationshipType: string; ownershipPercent?: number | null; isPrimary?: boolean; notes?: string | null }>;
  effectiveFrom?: string | null;
  requestedBy?: string | null;
}

export interface FundStructureNodePreview {
  nodeId: string;
  kind: string;
  code: string;
  name: string;
  description?: string | null;
  isActive: boolean;
  effectiveFrom: string;
  effectiveTo?: string | null;
}

export interface FundStructureSetupValidationIssue {
  code: string;
  message: string;
  fieldPath: string;
  isBlocking: boolean;
}

export interface FundStructureSetupValidationSummary {
  isValid: boolean;
  issues: FundStructureSetupValidationIssue[];
}

export interface FundStructureSetupPreview {
  nodes: FundStructureNodePreview[];
  ownershipLinks: Array<{ parent: string; child: string; relationshipType: string; ownershipPercent?: number | null; isPrimary: boolean; notes?: string | null }>;
  validationSummary: FundStructureSetupValidationSummary;
}

export interface FundStructureSetupResult {
  organization: { organizationId: string; code: string; name: string };
  businessLane: { businessId: string; code: string; name: string };
  client?: { clientId: string; code: string; name: string } | null;
  fund?: { fundId: string; code: string; name: string } | null;
  legalEntity: { entityId: string; code: string; name: string; legalForm?: string; lifecycleStatus?: string; registrationNumber?: string | null };
  vehicle: { vehicleId: string; code: string; name: string };
  investmentPortfolio: { investmentPortfolioId: string; code: string; name: string };
  ownershipLinks: unknown[];
  accountHandoffAssignment: unknown;
  graph: { nodes: FundStructureNodePreview[]; ownershipLinks: unknown[] };
  validationSummary: FundStructureSetupValidationSummary;
}

export async function validateFundStructureSetupDraft(draft: FundStructureSetupDraft, options: ApiRequestOptions = {}): Promise<FundStructureSetupPreview> {
  return postJson<FundStructureSetupPreview>(FUND_STRUCTURE_API_ENDPOINTS.setupDraftValidate, draft, options);
}

export async function createFundStructureSetupDraft(draft: FundStructureSetupDraft, options: ApiRequestOptions = {}): Promise<FundStructureSetupResult> {
  return postJson<FundStructureSetupResult>(FUND_STRUCTURE_API_ENDPOINTS.setupDraftCreate, draft, options);
}
