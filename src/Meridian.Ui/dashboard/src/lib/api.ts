import type {
  BackfillPreviewResult,
  BackfillProgressResponse,
  BackfillTriggerRequest,
  BackfillTriggerResult,
  AccountingMigrationRunArtifactList,
  AccountingProductionCertificationProfile,
  AccountingProductionCertificationProfileUpsertRequest,
  AccountingProductionReadiness,
  AccountingProductionReadinessRequest,
  AccountingTenantAdministrationProfile,
  AccountingTenantAdministrationProfileUpsertRequest,
  AccountingSystemExportPackageRequest,
  AccountingSystemImportDetail,
  AccountingSystemImportRequest,
  AccountingSystemMappingProfileUpsertRequest,
  AccountingSystemProvider,
  AccountingSystemReconciliationSummary,
  AccountingReportPackageBundle,
  AccountingReportPackageRequest,
  CertifyAccountingReportPackageRequest,
  CertifyAccountingSystemExportPackageRequest,
  ReportExportArtifactManifest,
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
  ClosePeriodPlan,
  CorporateAction,
  CreateLateAdjustmentRequest,
  ReviewLateAdjustmentRequest,
  ExternalGlExportPackage,
  ExternalGlExportPackageManifest,
  ExternalGlMappingProfile,
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
  EvidenceVaultRequestListEntry,
  EvidenceVaultRequestListQuery,
  ExtensibilityActivationReadiness,
  ExtensibilityCatalog,
  FinancialRecordExplorerDto,
  FinancialRecordExplorerId,
  FinancialRecordExplorerSavedViewDto,
  FinancialRecordExplorerSavedViewSaveRequestDto,
  FinancialRecordExplorerSelectedRecordDto,
  ExportAnalysisResult,
  ExecutionControlSnapshot,
  ExecutionAuditEntry,
  AccountingWorkspaceResponse,
  AssetOperationsDetail,
  ReportingWorkspaceResponse,
  SignOffCloseTaskRequest,
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
  OperationsAssignBreakCaseRequest,
  OperationsBreakCase,
  OperatorWorkflowHomeSummary,
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
  ManualCsvProviderIntegrationDryRunRequest,
  ProviderIntegrationActivationReadiness,
  ProviderIntegrationActivationRequest,
  ProviderIntegrationActivationResult,
  ProviderIntegrationConnectionMonitor,
  ProviderIntegrationDryRunResult,
  ProviderIntegrationManifest,
  ProviderIntegrationOpenApiImportRequest,
  ProviderIntegrationOpenApiImportResult,
  ProviderIntegrationPromotionReadinessPreview,
  ProviderIntegrationQuarantineReplayRequest,
  ProviderIntegrationQuarantineReplayResult,
  ProviderIntegrationQuarantineResolutionRequest,
  ProviderIntegrationQuarantineResolutionResult,
  ProviderIntegrationQuarantineReview,
  ProviderIntegrationReconciliationHandoffHistory,
  ProviderIntegrationReconciliationHandoffRequest,
  ProviderIntegrationReconciliationHandoffResult,
  ProviderIntegrationRestDryRunRequest,
  ProviderIntegrationRunDueSyncRequest,
  ProviderIntegrationRunDueSyncResult,
  ProviderIntegrationSchemaDriftCheckRequest,
  ProviderIntegrationSchemaDriftCheckResult,
  ProviderIntegrationSetupSaveRequest,
  ProviderIntegrationSetupSaveResult,
  ProviderIntegrationStagingIdentityResolutionPreview,
  ProviderIntegrationStagingReview,
  ProviderIntegrationSyncPlan,
  ProviderIntegrationSyncRunHistory,
  ProviderIntegrationTemplateCatalogEntry,
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
  TenantTemplateActivationRequest,
  TenantTemplateActivationResult,
  TenantTemplateConfigurationBundle,
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
  LedgerMappingAssignmentRequest,
  LedgerMappingAssignmentResult,
  LedgerMappingWorkbench,
  OperationsApprovalDecisionRequest,
  OperationsApprovalPolicyMatrix,
  OperationsApprovalPolicyRuleUpsertRequest,
  OperationsApprovalPolicyRuleUpsertResult,
  OperationsChecklistAcknowledgeRequest,
  OperationsCloseCalendar,
  OperationsCloseCalendarItemUpsertRequest,
  OperationsCloseCalendarItemUpsertResult,
  OperationsCloseChecklistTask,
  OperationsCloseReadiness,
  OperationsCloseWorkflowRequest,
  OperationsGatePostureRequest,
  OperationsLedgerDraftRequest,
  OperationsLedgerPostRequest,
  OperationsLedgerPreview,
  OperationsLedgerValidationRequest,
  OperationsReconciliationRunRequest,
  OperationsReopenWorkflowRequest,
  OperationsRejectWorkflowRequest,
  OperationsResolveBreakCaseRequest,
  OperationsSecurityMasterOverrideApprovalRequest,
  OperationsSecurityMasterResolveRequest,
  OperationsStartWorkflowRequest,
  OperationsSubmitApprovalRequest,
  OperationsTimelineEntry,
  OperationsTransitionRequest,
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
  UserAccessAssignment,
  UserAccessAssignmentCreateRequest,
  UserAccessAssignmentMutationResult,
  UserAccessAssignmentQuery,
  UserAccessAssignmentRevokeRequest,
  UserPasswordResetRequest,
  UserSessionRevokeRequest,
  UserSessionRevokeResult,
  AttachManualJournalEntryEvidenceRequest,
  JournalEntryLifecycleActionRequest,
  JournalEntryLifecycleActionResult,
  ManualJournalEntryDraft,
  ManualJournalEntryWorkbench,
  CapitalAccountWorkbench,
  PrivateCapitalActivityProjection,
  PrivateCapitalCapitalAccountSubledger,
  PrivateCapitalCloseCockpit,
  PrivateCapitalFundEventCommandCenter,
  PrivateCapitalFundEventLedgerRecord,
  PrivateCapitalReportOutput,
  FundReportPackGenerateRequest,
  FundReportPackPreview,
  FundReportPackPreviewRequest,
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
  ValidateManualJournalEntryDraftRequest,
  AdminCleanupExecuteResponse,
  AdminCleanupPreviewResponse,
  AdminErrorCodesResponse,
  AdminMaintenanceHistoryResponse,
  AdminMaintenanceRunRequest,
  AdminMaintenanceScheduleResponse,
  AdminQuickCheckResponse,
  AdminRetentionResponse,
  AdminSelfTestResponse,
  AdminShowConfigResponse,
  AdminStoragePermissionsResponse,
  AdminStorageTiersResponse,
  AdminStorageUsageResponse,
  DataPackageContentsResponse,
  DataPackageCreateRequest,
  DataPackageListResponse,
  DataPackageResult,
  DataPackageValidateRequest,
  DataPackageValidationResponse,
  MaintenanceExecution,
  MaintenanceScheduleHistoryResponse,
  MaintenanceSchedulesResponse
} from "@/types";
import {
  AUTH_API_ENDPOINTS,
  ADMIN_OPERATIONS_API_ENDPOINTS,
  DIAGNOSTICS_API_ENDPOINTS,
  MAINTENANCE_API_ENDPOINTS,
  PACKAGING_API_ENDPOINTS,
  adminMaintenanceHistoryEndpoint,
  adminMaintenanceRunEndpoint,
  adminRetentionDeleteEndpoint,
  adminStorageMigrateEndpoint,
  ACCOUNTING_SYSTEM_API_ENDPOINTS,
  BACKFILL_API_ENDPOINTS,
  EXECUTION_API_ENDPOINTS,
  EXPORT_API_ENDPOINTS,
  FUND_STRUCTURE_API_ENDPOINTS,
  PORTFOLIO_API_ENDPOINTS,
  PLAID_API_ENDPOINTS,
  PROVIDER_API_ENDPOINTS,
  PROVIDER_INTEGRATION_API_ENDPOINTS,
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
  buildReferenceDataWorkbenchEndpoints,
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
  packagingContentsEndpoint,
  packagingListEndpoint,
  providerCredentialEndpoint,
  providerVerifyEndpoint,
  maintenanceScheduleDisableEndpoint,
  maintenanceScheduleEnableEndpoint,
  maintenanceScheduleEndpoint,
  maintenanceScheduleHistoryEndpoint,
  maintenanceScheduleRunEndpoint,
  workstationProviderIntegrationConnectionMonitorEndpoint,
  workstationProviderIntegrationConnectionRunDueSyncEndpoint,
  workstationProviderIntegrationConnectionSyncPlanEndpoint,
  workstationProviderIntegrationConnectionSyncRunsEndpoint,
  workstationProviderIntegrationIdentityResolutionEndpoint,
  workstationProviderIntegrationPromotionReadinessEndpoint,
  workstationProviderIntegrationQuarantineReviewEndpoint,
  workstationProviderIntegrationReadinessEndpoint,
  workstationProviderIntegrationReconciliationHandoffHistoryEndpoint,
  workstationProviderIntegrationStagingReviewEndpoint,
  workstationProviderIntegrationTemplateEndpoint,
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
  workstationExtensibilityTenantTemplateActivateEndpoint,
  workstationExtensibilityTenantTemplateActivationsEndpoint,
  workstationExtensibilityTenantTemplateEndpoint,
  workstationExtensibilityTenantTemplateReadinessEndpoint,
  workstationAssetOperationsEndpoint,
  workstationFinancialRecordExplorerEndpoint,
  workstationFinancialRecordExplorerRecordEndpoint,
  workstationFinancialRecordExplorerSavedViewsEndpoint,
  workstationOperatorInboxEndpoint,
  workstationOperationsContinuityApprovalApproveEndpoint,
  workstationOperationsContinuityApprovalRejectEndpoint,
  workstationOperationsContinuityApprovalSubmitEndpoint,
  workstationOperationsContinuityBrokerImportEndpoint,
  workstationOperationsContinuityBrokerNormalizeEndpoint,
  workstationOperationsContinuityBreakAssignEndpoint,
  workstationOperationsContinuityBreakResolveEndpoint,
  workstationOperationsContinuityBreaksEndpoint,
  workstationOperationsContinuityChecklistEndpoint,
  workstationOperationsContinuityChecklistAcknowledgeEndpoint,
  workstationOperationsContinuityCloseEndpoint,
  workstationOperationsContinuityDetailEndpoint,
  workstationOperationsContinuityEndpoint,
  workstationOperationsContinuityCloseCalendarEndpoint,
  workstationOperationsContinuityCloseReadinessEndpoint,
  workstationOperationsContinuityLedgerDraftEndpoint,
  workstationOperationsContinuityLedgerPostEndpoint,
  workstationOperationsPrivateCapitalCloseCockpitEndpoint,
  workstationOperationsContinuityLedgerPreviewEndpoint,
  workstationOperationsContinuityLedgerValidateEndpoint,
  workstationOperationsContinuityPostureRefreshEndpoint,
  workstationOperationsContinuityReconciliationRunEndpoint,
  workstationOperationsContinuityReopenEndpoint,
  workstationOperationsContinuitySecurityMasterOverrideApproveEndpoint,
  workstationOperationsContinuitySecurityMasterResolveEndpoint,
  workstationOperationsContinuityTimelineEndpoint,
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
  workstationWorkflowPresetUsedEndpoint,
  type LedgerJournalQueryOptions,
  type LedgerTrialBalanceQueryOptions,
  type ReferenceDataEndpointDefinition,
  type ReferenceDataWorkbenchEndpointSeed
} from "@/lib/workstation-endpoints";
import { createApiErrorFromResponseBody, isApiError } from "@/lib/api-errors";

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

export type ReferenceDataEndpointProbeStatus = "Ready" | "Empty" | "Missing" | "Blocked" | "Error" | "Deferred";

export interface ReferenceDataEndpointProbeResult extends ReferenceDataEndpointDefinition {
  status: ReferenceDataEndpointProbeStatus;
  statusCode: number | null;
  durationMs: number | null;
  responseCount: number | null;
  responseSummary: string;
  responsePreview: string | null;
  errorSummary: string | null;
  errorDetails: string[];
}

export interface ReferenceDataWorkbenchCoverage {
  requestedAtUtc: string;
  endpoints: ReferenceDataEndpointProbeResult[];
}

export async function getReferenceDataWorkbenchCoverage(
  seed: ReferenceDataWorkbenchEndpointSeed,
  options: ApiRequestOptions = {}
): Promise<ReferenceDataWorkbenchCoverage> {
  const endpoints = buildReferenceDataWorkbenchEndpoints(seed);
  const results = await Promise.all(endpoints.map((endpoint) => probeReferenceDataEndpoint(endpoint, options)));

  return {
    requestedAtUtc: new Date().toISOString(),
    endpoints: results
  };
}

async function probeReferenceDataEndpoint(
  endpoint: ReferenceDataEndpointDefinition,
  options: ApiRequestOptions
): Promise<ReferenceDataEndpointProbeResult> {
  if (!endpoint.probe) {
    return {
      ...endpoint,
      status: "Deferred",
      statusCode: null,
      durationMs: null,
      responseCount: null,
      responseSummary: endpoint.requestLabel,
      responsePreview: null,
      errorSummary: null,
      errorDetails: []
    };
  }

  const startedAt = Date.now();
  try {
    const payload = await getJson<unknown>(endpoint.path, options);
    const summary = summarizeReferenceDataPayload(payload);

    return {
      ...endpoint,
      status: summary.count > 0 ? "Ready" : "Empty",
      statusCode: 200,
      durationMs: Date.now() - startedAt,
      responseCount: summary.count,
      responseSummary: summary.summary,
      responsePreview: previewReferenceDataPayload(payload),
      errorSummary: null,
      errorDetails: []
    };
  } catch (error) {
    const durationMs = Date.now() - startedAt;
    if (isApiError(error)) {
      return {
        ...endpoint,
        status: classifyReferenceDataApiError(error.status),
        statusCode: error.status,
        durationMs,
        responseCount: null,
        responseSummary: `Endpoint returned ${error.status}.`,
        responsePreview: null,
        errorSummary: error.detail || error.title || `Endpoint returned ${error.status}`,
        errorDetails: buildReferenceDataApiErrorDetails(error)
      };
    }

    const message = error instanceof Error && error.message.trim() ? error.message : "Reference endpoint probe failed.";
    return {
      ...endpoint,
      status: "Error",
      statusCode: null,
      durationMs,
      responseCount: null,
      responseSummary: message,
      responsePreview: null,
      errorSummary: message,
      errorDetails: []
    };
  }
}

function summarizeReferenceDataPayload(payload: unknown): { count: number; summary: string } {
  if (payload === null || payload === undefined) {
    return { count: 0, summary: "No payload returned." };
  }

  if (Array.isArray(payload)) {
    return {
      count: payload.length,
      summary: payload.length > 0 ? `${payload.length} records returned.` : "No records returned."
    };
  }

  if (typeof payload === "object") {
    const keys = Object.keys(payload as Record<string, unknown>);
    return {
      count: keys.length > 0 ? 1 : 0,
      summary: keys.length > 0 ? `${keys.length} fields returned.` : "Empty object returned."
    };
  }

  return { count: 1, summary: "Scalar response returned." };
}

function previewReferenceDataPayload(payload: unknown): string | null {
  if (payload === null || payload === undefined) {
    return null;
  }

  const previewPayload = Array.isArray(payload) ? payload.slice(0, 3) : payload;
  try {
    const preview = JSON.stringify(previewPayload, null, 2);
    return preview.length > 1600 ? `${preview.slice(0, 1600)}...` : preview;
  } catch {
    return String(previewPayload).slice(0, 1600);
  }
}

function classifyReferenceDataApiError(status: number): ReferenceDataEndpointProbeStatus {
  if (status === 401 || status === 403) {
    return "Blocked";
  }

  if (status === 404) {
    return "Missing";
  }

  return "Error";
}

function buildReferenceDataApiErrorDetails(error: { path: string; status: number; title: string | null; detail: string | null; responseBody: string | null; validationIssues: Array<{ label: string; messages: string[] }> }): string[] {
  const details: string[] = [`Endpoint returned ${error.status} for ${error.path}.`];

  if (error.title && error.title !== error.detail) {
    details.push(error.title);
  }

  if (error.detail) {
    details.push(error.detail);
  }

  for (const issue of error.validationIssues) {
    for (const message of issue.messages) {
      details.push(`${issue.label}: ${message}`);
    }
  }

  if (details.length === 1 && error.responseBody) {
    details.push(error.responseBody);
  }

  return details;
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

export function getAdminMaintenanceSchedule(options: ApiRequestOptions = {}) {
  return getJson<AdminMaintenanceScheduleResponse>(ADMIN_OPERATIONS_API_ENDPOINTS.maintenanceSchedule, options);
}

export function runAdminMaintenance(request: AdminMaintenanceRunRequest = {}, options: ApiRequestOptions = {}) {
  return postJson<MaintenanceExecution>(ADMIN_OPERATIONS_API_ENDPOINTS.maintenanceRun, request, options);
}

export function getAdminMaintenanceRun(runId: string, options: ApiRequestOptions = {}) {
  return getJson<MaintenanceExecution>(adminMaintenanceRunEndpoint(runId), options);
}

export function getAdminMaintenanceHistory(limit = 20, options: ApiRequestOptions = {}) {
  return getJson<AdminMaintenanceHistoryResponse>(adminMaintenanceHistoryEndpoint(limit), options);
}

export function getAdminStorageTiers(options: ApiRequestOptions = {}) {
  return getJson<AdminStorageTiersResponse>(ADMIN_OPERATIONS_API_ENDPOINTS.storageTiers, options);
}

export function migrateAdminStorage(targetTier: string, options: ApiRequestOptions = {}) {
  return postJson<{ targetTier: string; plan: Record<string, unknown>; timestamp?: string }>(adminStorageMigrateEndpoint(targetTier), undefined, options);
}

export function getAdminStorageUsage(options: ApiRequestOptions = {}) {
  return getJson<AdminStorageUsageResponse>(ADMIN_OPERATIONS_API_ENDPOINTS.storageUsage, options);
}

export function getAdminRetention(options: ApiRequestOptions = {}) {
  return getJson<AdminRetentionResponse>(ADMIN_OPERATIONS_API_ENDPOINTS.retention, options);
}

export function deleteAdminRetentionPolicy(policyId: string, options: ApiRequestOptions = {}) {
  return deleteJson<{ policyId: string; deleted: boolean; timestamp?: string }>(adminRetentionDeleteEndpoint(policyId), options);
}

export function applyAdminRetention(options: ApiRequestOptions = {}) {
  return postJson<Record<string, unknown>>(ADMIN_OPERATIONS_API_ENDPOINTS.retentionApply, undefined, options);
}

export function getAdminCleanupPreview(options: ApiRequestOptions = {}) {
  return getJson<AdminCleanupPreviewResponse>(ADMIN_OPERATIONS_API_ENDPOINTS.cleanupPreview, options);
}

export function executeAdminCleanup(options: ApiRequestOptions = {}) {
  return postJson<AdminCleanupExecuteResponse>(ADMIN_OPERATIONS_API_ENDPOINTS.cleanupExecute, undefined, options);
}

export function getAdminStoragePermissions(options: ApiRequestOptions = {}) {
  return getJson<AdminStoragePermissionsResponse>(ADMIN_OPERATIONS_API_ENDPOINTS.storagePermissions, options);
}

export function runAdminSelftest(options: ApiRequestOptions = {}) {
  return postJson<AdminSelfTestResponse>(ADMIN_OPERATIONS_API_ENDPOINTS.selftest, undefined, options);
}

export function getAdminErrorCodes(options: ApiRequestOptions = {}) {
  return getJson<AdminErrorCodesResponse>(ADMIN_OPERATIONS_API_ENDPOINTS.errorCodes, options);
}

export function getAdminShowConfig(options: ApiRequestOptions = {}) {
  return getJson<AdminShowConfigResponse>(ADMIN_OPERATIONS_API_ENDPOINTS.showConfig, options);
}

export function getAdminQuickCheck(options: ApiRequestOptions = {}) {
  return getJson<AdminQuickCheckResponse>(ADMIN_OPERATIONS_API_ENDPOINTS.quickCheck, options);
}

export function getDiagnosticsQuickCheck(options: ApiRequestOptions = {}) {
  return getJson<AdminQuickCheckResponse>(DIAGNOSTICS_API_ENDPOINTS.quickCheck, options);
}

export function getMaintenanceSchedules(options: ApiRequestOptions = {}) {
  return getJson<MaintenanceSchedulesResponse>(MAINTENANCE_API_ENDPOINTS.schedules, options);
}

export function getMaintenanceSchedule(scheduleId: string, options: ApiRequestOptions = {}) {
  return getJson<MaintenanceSchedulesResponse>(maintenanceScheduleEndpoint(scheduleId), options);
}

export function enableMaintenanceSchedule(scheduleId: string, options: ApiRequestOptions = {}) {
  return postJson<MaintenanceSchedulesResponse>(maintenanceScheduleEnableEndpoint(scheduleId), undefined, options);
}

export function disableMaintenanceSchedule(scheduleId: string, options: ApiRequestOptions = {}) {
  return postJson<MaintenanceSchedulesResponse>(maintenanceScheduleDisableEndpoint(scheduleId), undefined, options);
}

export function runMaintenanceSchedule(scheduleId: string, options: ApiRequestOptions = {}) {
  return postJson<MaintenanceExecution>(maintenanceScheduleRunEndpoint(scheduleId), undefined, options);
}

export function getMaintenanceScheduleHistory(scheduleId: string, limit = 20, options: ApiRequestOptions = {}) {
  return getJson<MaintenanceScheduleHistoryResponse>(maintenanceScheduleHistoryEndpoint(scheduleId, limit), options);
}

export function listDataPackages(directory?: string | null, options: ApiRequestOptions = {}) {
  return getJson<DataPackageListResponse>(packagingListEndpoint(directory), options);
}

export function createDataPackage(request: DataPackageCreateRequest, options: ApiRequestOptions = {}) {
  return postJson<DataPackageResult>(PACKAGING_API_ENDPOINTS.create, request, options);
}

export function validateDataPackage(request: DataPackageValidateRequest, options: ApiRequestOptions = {}) {
  return postJson<DataPackageValidationResponse>(PACKAGING_API_ENDPOINTS.validate, request, options);
}

export function getDataPackageContents(packagePath: string, options: ApiRequestOptions = {}) {
  return getJson<DataPackageContentsResponse>(packagingContentsEndpoint(packagePath), options);
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

export function getWorkstationWorkflowSummary(options: ApiRequestOptions & {
  hasOperatingContext?: boolean;
  operatingContext?: string;
  fundProfileId?: string;
  fundAccountId?: string;
  fundDisplayName?: string;
} = {}) {
  const { signal, ...queryOptions } = options;
  return getJson<OperatorWorkflowHomeSummary>(workstationWorkflowSummaryEndpoint(queryOptions), { signal });
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

export function getExtensibilityCatalog(options: ApiRequestOptions = {}) {
  return getJson<ExtensibilityCatalog>(WORKSTATION_API_ENDPOINTS.extensibilityCatalog, options);
}

export function listExtensibilityTenantTemplates(options: ApiRequestOptions = {}) {
  return getJson<TenantTemplateConfigurationBundle[]>(WORKSTATION_API_ENDPOINTS.extensibilityTenantTemplates, options);
}

export function saveExtensibilityTenantTemplate(
  tenantTemplate: TenantTemplateConfigurationBundle,
  options: ApiRequestOptions = {}
) {
  return putJson<TenantTemplateConfigurationBundle>(
    workstationExtensibilityTenantTemplateEndpoint(tenantTemplate.tenantTemplateId),
    tenantTemplate,
    options
  );
}

export function activateExtensibilityTenantTemplate(
  tenantTemplateId: string,
  request: TenantTemplateActivationRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<TenantTemplateActivationResult>(
    workstationExtensibilityTenantTemplateActivateEndpoint(tenantTemplateId),
    request,
    options
  );
}

export function listExtensibilityTenantTemplateActivations(tenantTemplateId: string, options: ApiRequestOptions = {}) {
  return getJson<TenantTemplateActivationResult[]>(
    workstationExtensibilityTenantTemplateActivationsEndpoint(tenantTemplateId),
    options
  );
}

export function getExtensibilityTenantTemplateReadiness(tenantTemplateId: string, options: ApiRequestOptions = {}) {
  return getJson<ExtensibilityActivationReadiness>(
    workstationExtensibilityTenantTemplateReadinessEndpoint(tenantTemplateId),
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

export function listScopedAccessAssignments(
  query: UserAccessAssignmentQuery = {},
  options: ApiRequestOptions = {}
) {
  const params = new URLSearchParams();
  if (query.principalId) {
    params.set("principalId", query.principalId);
  }

  if (query.scopeKind) {
    params.set("scopeKind", query.scopeKind);
  }

  if (query.scopeId) {
    params.set("scopeId", query.scopeId);
  }

  if (query.includeRevoked) {
    params.set("includeRevoked", "true");
  }

  const suffix = params.toString();
  return getJson<UserAccessAssignment[]>(
    suffix ? `${AUTH_API_ENDPOINTS.accessAssignments}?${suffix}` : AUTH_API_ENDPOINTS.accessAssignments,
    options
  );
}

export function createScopedAccessAssignment(
  request: UserAccessAssignmentCreateRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<UserAccessAssignmentMutationResult>(AUTH_API_ENDPOINTS.accessAssignments, request, options);
}

export function revokeScopedAccessAssignment(
  request: UserAccessAssignmentRevokeRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<UserAccessAssignmentMutationResult>(
    authAccessAssignmentRevokeEndpoint(request.assignmentId),
    request,
    options
  );
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

function authAccessAssignmentRevokeEndpoint(assignmentId: string) {
  return AUTH_API_ENDPOINTS.accessAssignmentRevoke.replace("{assignmentId}", encodeURIComponent(assignmentId));
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

export function getOperationsContinuityTimeline(workflowId: string, options: ApiRequestOptions = {}) {
  return getJson<OperationsTimelineEntry[]>(workstationOperationsContinuityTimelineEndpoint(workflowId), options);
}

export function getOperationsContinuityBreaks(workflowId: string, options: ApiRequestOptions = {}) {
  return getJson<OperationsBreakCase[]>(workstationOperationsContinuityBreaksEndpoint(workflowId), options);
}

export function getOperationsContinuityLedgerPreview(workflowId: string, options: ApiRequestOptions = {}) {
  return getJson<OperationsLedgerPreview | null>(workstationOperationsContinuityLedgerPreviewEndpoint(workflowId), options);
}

export function getOperationsContinuityChecklist(workflowId: string, options: ApiRequestOptions = {}) {
  return getJson<OperationsCloseChecklistTask[]>(workstationOperationsContinuityChecklistEndpoint(workflowId), options);
}

export function getOperationsContinuityCloseReadiness(workflowId: string, options: ApiRequestOptions = {}) {
  return getJson<OperationsCloseReadiness>(workstationOperationsContinuityCloseReadinessEndpoint(workflowId), options);
}

export function startOperationsContinuityWorkflow(
  request: OperationsStartWorkflowRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<OperationsTransitionResult>(workstationOperationsContinuityEndpoint(), request, options);
}

export function importOperationsContinuityBrokerData(
  workflowId: string,
  request: OperationsTransitionRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<OperationsTransitionResult>(
    workstationOperationsContinuityBrokerImportEndpoint(workflowId),
    request,
    options
  );
}

export function normalizeOperationsContinuityBrokerTransactions(
  workflowId: string,
  request: OperationsTransitionRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<OperationsTransitionResult>(
    workstationOperationsContinuityBrokerNormalizeEndpoint(workflowId),
    request,
    options
  );
}

export function refreshOperationsContinuityGatePosture(
  workflowId: string,
  request: OperationsGatePostureRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<OperationsTransitionResult>(
    workstationOperationsContinuityPostureRefreshEndpoint(workflowId),
    request,
    options
  );
}

export function resolveOperationsContinuitySecurityMasterMappings(
  workflowId: string,
  request: OperationsSecurityMasterResolveRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<OperationsTransitionResult>(
    workstationOperationsContinuitySecurityMasterResolveEndpoint(workflowId),
    request,
    options
  );
}

export function approveOperationsContinuitySecurityMasterOverride(
  workflowId: string,
  overrideId: string,
  request: OperationsSecurityMasterOverrideApprovalRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<OperationsTransitionResult>(
    workstationOperationsContinuitySecurityMasterOverrideApproveEndpoint(workflowId, overrideId),
    request,
    options
  );
}

export function draftOperationsContinuityLedger(
  workflowId: string,
  request: OperationsLedgerDraftRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<OperationsTransitionResult>(
    workstationOperationsContinuityLedgerDraftEndpoint(workflowId),
    request,
    options
  );
}

export function validateOperationsContinuityLedger(
  workflowId: string,
  request: OperationsLedgerValidationRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<OperationsTransitionResult>(
    workstationOperationsContinuityLedgerValidateEndpoint(workflowId),
    request,
    options
  );
}

export function postOperationsContinuityLedger(
  workflowId: string,
  request: OperationsLedgerPostRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<OperationsTransitionResult>(
    workstationOperationsContinuityLedgerPostEndpoint(workflowId),
    request,
    options
  );
}

export function runOperationsContinuityReconciliation(
  workflowId: string,
  request: OperationsReconciliationRunRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<OperationsTransitionResult>(
    workstationOperationsContinuityReconciliationRunEndpoint(workflowId),
    request,
    options
  );
}

export function assignOperationsContinuityBreakCase(
  workflowId: string,
  breakId: string,
  request: OperationsAssignBreakCaseRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<OperationsTransitionResult>(
    workstationOperationsContinuityBreakAssignEndpoint(workflowId, breakId),
    request,
    options
  );
}

export function resolveOperationsContinuityBreakCase(
  workflowId: string,
  breakId: string,
  request: OperationsResolveBreakCaseRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<OperationsTransitionResult>(
    workstationOperationsContinuityBreakResolveEndpoint(workflowId, breakId),
    request,
    options
  );
}

export function acknowledgeOperationsContinuityChecklistTask(
  workflowId: string,
  taskId: string,
  request: OperationsChecklistAcknowledgeRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<OperationsTransitionResult>(
    workstationOperationsContinuityChecklistAcknowledgeEndpoint(workflowId, taskId),
    request,
    options
  );
}

export function submitOperationsContinuityApproval(
  workflowId: string,
  request: OperationsSubmitApprovalRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<OperationsTransitionResult>(
    workstationOperationsContinuityApprovalSubmitEndpoint(workflowId),
    request,
    options
  );
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

export function closeOperationsContinuityWorkflow(
  workflowId: string,
  request: OperationsCloseWorkflowRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<OperationsTransitionResult>(
    workstationOperationsContinuityCloseEndpoint(workflowId),
    request,
    options
  );
}

export function reopenOperationsContinuityWorkflow(
  workflowId: string,
  request: OperationsReopenWorkflowRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<OperationsTransitionResult>(
    workstationOperationsContinuityReopenEndpoint(workflowId),
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

export interface PrivateCapitalCloseCockpitQuery {
  fundProfileId?: string | null;
  ledgerBookId?: string | null;
  fundAccountId?: string | null;
  periodId?: string | null;
  entityId?: string | null;
}

export function getPrivateCapitalCloseCockpit(
  query: PrivateCapitalCloseCockpitQuery = {},
  options: ApiRequestOptions = {}
) {
  return getJson<PrivateCapitalCloseCockpit>(
    workstationOperationsPrivateCapitalCloseCockpitEndpoint({
      fundProfileId: query.fundProfileId ?? undefined,
      ledgerBookId: query.ledgerBookId ?? undefined,
      fundAccountId: query.fundAccountId ?? undefined,
      periodId: query.periodId ?? undefined,
      entityId: query.entityId ?? undefined
    }),
    options
  );
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

export function listEvidenceVaultRequestLists(
  query: EvidenceVaultRequestListQuery = {},
  options: ApiRequestOptions = {}
) {
  const params = new URLSearchParams();
  if (query.requestListKind) {
    params.set("requestListKind", query.requestListKind);
  }

  if (query.targetKind) {
    params.set("targetKind", query.targetKind);
  }

  if (query.targetId) {
    params.set("targetId", query.targetId);
  }

  if (query.status) {
    params.set("status", query.status);
  }

  if (query.subjectKind) {
    params.set("subjectKind", query.subjectKind);
  }

  if (query.subjectId) {
    params.set("subjectId", query.subjectId);
  }

  if (query.maxResults !== undefined && query.maxResults !== null) {
    params.set("maxResults", String(query.maxResults));
  }

  const suffix = params.toString();
  return getJson<EvidenceVaultRequestListEntry[]>(
    suffix ? `${WORKSTATION_API_ENDPOINTS.evidenceVaultRequestLists}?${suffix}` : WORKSTATION_API_ENDPOINTS.evidenceVaultRequestLists,
    options
  );
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

export function getFinancialRecordExplorer(
  explorerId: FinancialRecordExplorerId,
  options: ApiRequestOptions = {}
) {
  return getJson<FinancialRecordExplorerDto>(workstationFinancialRecordExplorerEndpoint(explorerId), options);
}

export function getFinancialRecordExplorerRecord(
  explorerId: FinancialRecordExplorerId,
  recordId: string,
  options: ApiRequestOptions = {}
) {
  return getJson<FinancialRecordExplorerSelectedRecordDto>(
    workstationFinancialRecordExplorerRecordEndpoint(explorerId, recordId),
    options
  );
}

export function saveFinancialRecordExplorerView(
  explorerId: FinancialRecordExplorerId,
  request: FinancialRecordExplorerSavedViewSaveRequestDto,
  options: ApiRequestOptions = {}
) {
  return postJson<FinancialRecordExplorerSavedViewDto>(
    workstationFinancialRecordExplorerSavedViewsEndpoint(explorerId),
    request,
    options
  );
}

export function getAccountingConfiguration(options: ApiRequestOptions = {}) {
  return getJson<import("@/types").AccountingConfigurationWorkspace>(WORKSTATION_API_ENDPOINTS.accountingConfiguration, options);
}

export function createLedgerBook(
  request: import("@/types").CreateLedgerBookRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<import("@/types").LedgerBook>(WORKSTATION_API_ENDPOINTS.ledgerBooks, request, options);
}

export interface ManualJournalEntryWorkbenchQuery {
  fundProfileId?: string | null;
  ledgerBookId?: string | null;
}

export function getManualJournalEntryWorkbench(
  query: ManualJournalEntryWorkbenchQuery = {},
  options: ApiRequestOptions = {}
) {
  const params = new URLSearchParams();
  if (query.fundProfileId) {
    params.set("fundProfileId", query.fundProfileId);
  }

  if (query.ledgerBookId) {
    params.set("ledgerBookId", query.ledgerBookId);
  }

  const suffix = params.toString();
  return getJson<ManualJournalEntryWorkbench>(
    `${WORKSTATION_API_ENDPOINTS.manualJournalEntryWorkbench}${suffix ? `?${suffix}` : ""}`,
    options
  );
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

export interface PrivateCapitalFundEventCommandCenterQuery {
  fundProfileId?: string | null;
  ledgerBookId?: string | null;
  fundEventId: string;
}

export function getPrivateCapitalFundEventCommandCenter(
  query: PrivateCapitalFundEventCommandCenterQuery,
  options: ApiRequestOptions = {}
) {
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(query)) {
    if (value) {
      params.set(key, value);
    }
  }

  return getJson<PrivateCapitalFundEventCommandCenter>(
    `${WORKSTATION_API_ENDPOINTS.privateCapitalFundEventCommandCenter}?${params.toString()}`,
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

export interface CapitalAccountWorkbenchQuery {
  fundProfileId?: string | null;
  ledgerBookId?: string | null;
  fundEventId?: string | null;
  capitalAccountId?: string | null;
  investorId?: string | null;
  currency?: string | null;
}

export function getCapitalAccountWorkbench(
  query: CapitalAccountWorkbenchQuery = {},
  options: ApiRequestOptions = {}
) {
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(query)) {
    if (value) {
      params.set(key, value);
    }
  }

  const suffix = params.toString();
  return getJson<CapitalAccountWorkbench>(
    `${WORKSTATION_API_ENDPOINTS.privateCapitalCapitalAccountWorkbench}${suffix ? `?${suffix}` : ""}`,
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

export function attachManualJournalEntryEvidence(
  request: AttachManualJournalEntryEvidenceRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<ManualJournalEntryDraft>(WORKSTATION_API_ENDPOINTS.manualJournalEntryEvidence, request, options);
}

export function applyManualJournalEntryLifecycleAction(
  request: JournalEntryLifecycleActionRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<JournalEntryLifecycleActionResult>(WORKSTATION_API_ENDPOINTS.manualJournalEntryLifecycleAction, request, options);
}

export function getAccountingSystemProviders(options: ApiRequestOptions = {}) {
  return getJson<AccountingSystemProvider[]>(ACCOUNTING_SYSTEM_API_ENDPOINTS.providers, options);
}

export function assessAccountingProductionReadiness(
  request: AccountingProductionReadinessRequest = {},
  options: ApiRequestOptions = {}
) {
  return postJson<AccountingProductionReadiness>(ACCOUNTING_SYSTEM_API_ENDPOINTS.productionReadiness, request, options);
}

export function getAccountingMigrationRunArtifacts(
  query: { fundProfileId?: string | null; ledgerBookId?: string | null } = {},
  options: ApiRequestOptions = {}
) {
  const params = new URLSearchParams();
  if (query.fundProfileId) {
    params.set("fundProfileId", query.fundProfileId);
  }

  if (query.ledgerBookId) {
    params.set("ledgerBookId", query.ledgerBookId);
  }

  const suffix = params.toString();
  const route = suffix ? `${ACCOUNTING_SYSTEM_API_ENDPOINTS.migrationRunArtifacts}?${suffix}` : ACCOUNTING_SYSTEM_API_ENDPOINTS.migrationRunArtifacts;
  return getJson<AccountingMigrationRunArtifactList>(route, options);
}

export function getAccountingTenantAdministrationProfile(
  query: { tenantId?: string | null; companyId?: string | null } = {},
  options: ApiRequestOptions = {}
) {
  const params = new URLSearchParams();
  if (query.tenantId) {
    params.set("tenantId", query.tenantId);
  }

  if (query.companyId) {
    params.set("companyId", query.companyId);
  }

  const suffix = params.toString();
  const route = suffix
    ? `${ACCOUNTING_SYSTEM_API_ENDPOINTS.tenantAdministrationProfile}?${suffix}`
    : ACCOUNTING_SYSTEM_API_ENDPOINTS.tenantAdministrationProfile;
  return getJson<AccountingTenantAdministrationProfile>(route, options);
}

export function upsertAccountingTenantAdministrationProfile(
  request: AccountingTenantAdministrationProfileUpsertRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<AccountingTenantAdministrationProfile>(ACCOUNTING_SYSTEM_API_ENDPOINTS.tenantAdministrationProfile, request, options);
}

export function getAccountingProductionCertificationProfile(
  query: { tenantId?: string | null; companyId?: string | null; fundProfileId?: string | null; ledgerBookId?: string | null } = {},
  options: ApiRequestOptions = {}
) {
  const params = new URLSearchParams();
  if (query.tenantId) {
    params.set("tenantId", query.tenantId);
  }

  if (query.companyId) {
    params.set("companyId", query.companyId);
  }

  if (query.fundProfileId) {
    params.set("fundProfileId", query.fundProfileId);
  }

  if (query.ledgerBookId) {
    params.set("ledgerBookId", query.ledgerBookId);
  }

  const suffix = params.toString();
  const route = suffix
    ? `${ACCOUNTING_SYSTEM_API_ENDPOINTS.productionCertificationProfile}?${suffix}`
    : ACCOUNTING_SYSTEM_API_ENDPOINTS.productionCertificationProfile;
  return getJson<AccountingProductionCertificationProfile>(route, options);
}

export function upsertAccountingProductionCertificationProfile(
  request: AccountingProductionCertificationProfileUpsertRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<AccountingProductionCertificationProfile>(ACCOUNTING_SYSTEM_API_ENDPOINTS.productionCertificationProfile, request, options);
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

export function getAccountingSystemMappingProfiles(options: ApiRequestOptions = {}) {
  return getJson<ExternalGlMappingProfile[]>(ACCOUNTING_SYSTEM_API_ENDPOINTS.mappingProfiles, options);
}

export function upsertAccountingSystemMappingProfile(
  request: AccountingSystemMappingProfileUpsertRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<ExternalGlMappingProfile>(ACCOUNTING_SYSTEM_API_ENDPOINTS.mappingProfiles, request, options);
}

export function listAccountingSystemExportPackages(
  query: {
    providerId?: string | null;
    fundProfileId?: string | null;
    ledgerBookId?: string | null;
    certificationState?: string | null;
    tenantId?: string | null;
    companyId?: string | null;
  } = {},
  options: ApiRequestOptions = {}
) {
  const params = new URLSearchParams();
  if (query.providerId) {
    params.set("providerId", query.providerId);
  }

  if (query.fundProfileId) {
    params.set("fundProfileId", query.fundProfileId);
  }

  if (query.ledgerBookId) {
    params.set("ledgerBookId", query.ledgerBookId);
  }

  if (query.certificationState) {
    params.set("certificationState", query.certificationState);
  }

  if (query.tenantId) {
    params.set("tenantId", query.tenantId);
  }

  if (query.companyId) {
    params.set("companyId", query.companyId);
  }

  const suffix = params.toString();
  const route = suffix
    ? `${ACCOUNTING_SYSTEM_API_ENDPOINTS.exportPackages}?${suffix}`
    : ACCOUNTING_SYSTEM_API_ENDPOINTS.exportPackages;
  return getJson<ExternalGlExportPackage[]>(route, options);
}

export function createAccountingSystemExportPackage(
  request: AccountingSystemExportPackageRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<ExternalGlExportPackage>(ACCOUNTING_SYSTEM_API_ENDPOINTS.exportPackages, request, options);
}

export function getAccountingSystemExportPackageManifest(
  exportPackageId: string,
  options: ApiRequestOptions = {}
) {
  const route = ACCOUNTING_SYSTEM_API_ENDPOINTS.exportPackageManifest
    .replace("{exportPackageId}", encodeURIComponent(exportPackageId));
  return getJson<ExternalGlExportPackageManifest>(route, options);
}

export function certifyAccountingSystemExportPackage(
  request: CertifyAccountingSystemExportPackageRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<ExternalGlExportPackage>(ACCOUNTING_SYSTEM_API_ENDPOINTS.exportPackageCertification, request, options);
}

export function getLedgerCloseManagementPeriodPlan(workflowId: string, options: ApiRequestOptions = {}) {
  const endpoint = WORKSTATION_API_ENDPOINTS.closeManagementPeriodPlan.replace("{workflowId:guid}", workflowId);
  return getJson<ClosePeriodPlan>(endpoint, options);
}

export function createLedgerCloseManagementLateAdjustment(
  request: CreateLateAdjustmentRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<ClosePeriodPlan>(WORKSTATION_API_ENDPOINTS.closeManagementLateAdjustments, request, options);
}

export function reviewLedgerCloseManagementLateAdjustment(
  request: ReviewLateAdjustmentRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<ClosePeriodPlan>(WORKSTATION_API_ENDPOINTS.closeManagementLateAdjustmentReview, request, options);
}

export function signOffLedgerCloseManagementTask(
  request: SignOffCloseTaskRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<ClosePeriodPlan>(WORKSTATION_API_ENDPOINTS.closeManagementTaskSignOffs, request, options);
}

export function buildLedgerAccountingReportPackage(
  request: AccountingReportPackageRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<AccountingReportPackageBundle>(WORKSTATION_API_ENDPOINTS.accountingReportPackage, request, options);
}

export function certifyLedgerAccountingReportPackage(
  request: CertifyAccountingReportPackageRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<AccountingReportPackageBundle>(
    WORKSTATION_API_ENDPOINTS.accountingReportPackageCertification,
    request,
    options
  );
}

export interface AccountingReportPackageHistoryQuery {
  fundProfileId?: string | null;
  periodId?: string | null;
}

export function listLedgerAccountingReportPackages(
  query: AccountingReportPackageHistoryQuery = {},
  options: ApiRequestOptions = {}
) {
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(query)) {
    if (value) {
      params.set(key, value);
    }
  }

  const suffix = params.toString();
  return getJson<AccountingReportPackageBundle[]>(
    `${WORKSTATION_API_ENDPOINTS.accountingReportPackages}${suffix ? `?${suffix}` : ""}`,
    options
  );
}

export function getLedgerAccountingReportPackageExport(
  packageId: string,
  artifactId: string,
  options: ApiRequestOptions = {}
) {
  const route = WORKSTATION_API_ENDPOINTS.accountingReportPackageExport
    .replace("{packageId}", encodeURIComponent(packageId))
    .replace("{artifactId}", encodeURIComponent(artifactId));
  return getJson<ReportExportArtifactManifest>(route, options);
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

export function approveAccountingConfigurationPostingRulePromotion(
  request: import("@/types").ApprovePostingRulePromotionRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<import("@/types").AccountingConfigurationWorkspace>(
    WORKSTATION_API_ENDPOINTS.accountingConfigurationPostingRulePromotionApprovals,
    request,
    options
  );
}

export function dryRunAccountingConfigurationPostingRule(
  request: import("@/types").RuleDryRunRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<import("@/types").RuleDryRunResult>(WORKSTATION_API_ENDPOINTS.accountingConfigurationPostingRuleDryRun, request, options);
}

export function buildAccountingPostingRuleJournalCandidate(
  request: import("@/types").PostingRuleJournalCandidateRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<import("@/types").PostingRuleJournalCandidateResult>(
    WORKSTATION_API_ENDPOINTS.accountingConfigurationPostingRuleCandidates,
    request,
    options
  );
}

export function executeAccountingConfigurationPostingRuleTests(
  request: import("@/types").ExecuteAccountingRuleTestCasesRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<import("@/types").AccountingRuleTestSuiteResult>(WORKSTATION_API_ENDPOINTS.accountingConfigurationPostingRuleTests, request, options);
}

export function upsertAccountingConfigurationPostingRuleTestCase(
  request: import("@/types").UpsertAccountingRuleTestCaseRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<import("@/types").AccountingConfigurationWorkspace>(WORKSTATION_API_ENDPOINTS.accountingConfigurationPostingRuleTestCases, request, options);
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

export function previewReportPack(request: FundReportPackPreviewRequest, options: ApiRequestOptions = {}) {
  return postJson<FundReportPackPreview>(FUND_STRUCTURE_API_ENDPOINTS.reportPackPreview, request, options);
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

export function getProviderIntegrationTemplates(options: ApiRequestOptions = {}) {
  return getJson<ProviderIntegrationTemplateCatalogEntry[]>(PROVIDER_INTEGRATION_API_ENDPOINTS.templates, options);
}

export function getProviderIntegrationTemplate(manifestId: string, options: ApiRequestOptions = {}) {
  return getJson<ProviderIntegrationManifest>(workstationProviderIntegrationTemplateEndpoint(manifestId), options);
}

export function importProviderIntegrationOpenApi(
  request: ProviderIntegrationOpenApiImportRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<ProviderIntegrationOpenApiImportResult>(
    PROVIDER_INTEGRATION_API_ENDPOINTS.openApiImport,
    request,
    options
  );
}

export function saveProviderIntegrationSetup(
  request: ProviderIntegrationSetupSaveRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<ProviderIntegrationSetupSaveResult>(PROVIDER_INTEGRATION_API_ENDPOINTS.setupSave, request, options);
}

export function getProviderIntegrationReadiness(
  manifestId: string,
  connectionId?: string | null,
  options: ApiRequestOptions = {}
) {
  return getJson<ProviderIntegrationActivationReadiness>(
    workstationProviderIntegrationReadinessEndpoint(manifestId, connectionId),
    options
  );
}

export function runManualCsvProviderIntegrationDryRun(
  request: ManualCsvProviderIntegrationDryRunRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<ProviderIntegrationDryRunResult>(
    PROVIDER_INTEGRATION_API_ENDPOINTS.manualCsvDryRun,
    request,
    options
  );
}

export function runRestProviderIntegrationDryRun(
  request: ProviderIntegrationRestDryRunRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<ProviderIntegrationDryRunResult>(PROVIDER_INTEGRATION_API_ENDPOINTS.restDryRun, request, options);
}

export function activateProviderIntegration(
  request: ProviderIntegrationActivationRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<ProviderIntegrationActivationResult>(PROVIDER_INTEGRATION_API_ENDPOINTS.activate, request, options);
}

export function getProviderIntegrationConnectionMonitor(
  connectionId: string,
  recentRunLimit?: number,
  options: ApiRequestOptions = {}
) {
  return getJson<ProviderIntegrationConnectionMonitor>(
    workstationProviderIntegrationConnectionMonitorEndpoint(connectionId, recentRunLimit),
    options
  );
}

export function getProviderIntegrationConnectionSyncRuns(
  connectionId: string,
  recentRunLimit?: number,
  options: ApiRequestOptions = {}
) {
  return getJson<ProviderIntegrationSyncRunHistory>(
    workstationProviderIntegrationConnectionSyncRunsEndpoint(connectionId, recentRunLimit),
    options
  );
}

export function getProviderIntegrationConnectionSyncPlan(
  connectionId: string,
  evaluatedAt?: string | null,
  options: ApiRequestOptions = {}
) {
  return getJson<ProviderIntegrationSyncPlan>(
    workstationProviderIntegrationConnectionSyncPlanEndpoint(connectionId, evaluatedAt),
    options
  );
}

export function runDueProviderIntegrationSync(
  connectionId: string,
  request: ProviderIntegrationRunDueSyncRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<ProviderIntegrationRunDueSyncResult>(
    workstationProviderIntegrationConnectionRunDueSyncEndpoint(connectionId),
    request,
    options
  );
}

export function checkProviderIntegrationSchemaDrift(
  request: ProviderIntegrationSchemaDriftCheckRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<ProviderIntegrationSchemaDriftCheckResult>(
    PROVIDER_INTEGRATION_API_ENDPOINTS.schemaDriftCheck,
    request,
    options
  );
}

export function getProviderIntegrationStagingReview(
  connectionId: string,
  recentRunLimit?: number,
  options: ApiRequestOptions = {}
) {
  return getJson<ProviderIntegrationStagingReview>(
    workstationProviderIntegrationStagingReviewEndpoint(connectionId, recentRunLimit),
    options
  );
}

export function getProviderIntegrationIdentityResolution(
  connectionId: string,
  recentRunLimit?: number,
  options: ApiRequestOptions = {}
) {
  return getJson<ProviderIntegrationStagingIdentityResolutionPreview>(
    workstationProviderIntegrationIdentityResolutionEndpoint(connectionId, recentRunLimit),
    options
  );
}

export function getProviderIntegrationPromotionReadiness(
  connectionId: string,
  recentRunLimit?: number,
  options: ApiRequestOptions = {}
) {
  return getJson<ProviderIntegrationPromotionReadinessPreview>(
    workstationProviderIntegrationPromotionReadinessEndpoint(connectionId, recentRunLimit),
    options
  );
}

export function getProviderIntegrationReconciliationHandoffHistory(
  connectionId: string,
  options: ApiRequestOptions = {}
) {
  return getJson<ProviderIntegrationReconciliationHandoffHistory>(
    workstationProviderIntegrationReconciliationHandoffHistoryEndpoint(connectionId),
    options
  );
}

export function createProviderIntegrationReconciliationHandoff(
  request: ProviderIntegrationReconciliationHandoffRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<ProviderIntegrationReconciliationHandoffResult>(
    PROVIDER_INTEGRATION_API_ENDPOINTS.reconciliationHandoff,
    request,
    options
  );
}

export function getProviderIntegrationQuarantineReview(
  connectionId: string,
  recentRunLimit?: number,
  options: ApiRequestOptions = {}
) {
  return getJson<ProviderIntegrationQuarantineReview>(
    workstationProviderIntegrationQuarantineReviewEndpoint(connectionId, recentRunLimit),
    options
  );
}

export function resolveProviderIntegrationQuarantineRecord(
  request: ProviderIntegrationQuarantineResolutionRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<ProviderIntegrationQuarantineResolutionResult>(
    PROVIDER_INTEGRATION_API_ENDPOINTS.quarantineResolve,
    request,
    options
  );
}

export function replayProviderIntegrationQuarantineRecords(
  request: ProviderIntegrationQuarantineReplayRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<ProviderIntegrationQuarantineReplayResult>(
    PROVIDER_INTEGRATION_API_ENDPOINTS.quarantineReplay,
    request,
    options
  );
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
