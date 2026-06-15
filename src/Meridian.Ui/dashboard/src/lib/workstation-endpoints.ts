import { UI_API_ROUTES } from "./ui-api-routes.generated";

export const WORKSTATION_API_ENDPOINTS = {
  systemStatus: UI_API_ROUTES.Status,
  session: UI_API_ROUTES.WorkstationSession,
  strategy: UI_API_ROUTES.WorkstationStrategy,
  strategyBriefing: UI_API_ROUTES.WorkstationStrategyBriefing,
  trading: UI_API_ROUTES.WorkstationTrading,
  tradingReadiness: UI_API_ROUTES.WorkstationTradingReadiness,
  operatorInbox: UI_API_ROUTES.WorkstationOperatorInbox,
  portfolio: UI_API_ROUTES.WorkstationPortfolio,
  portfolioSummary: UI_API_ROUTES.WorkstationPortfolioSummary,
  portfolioMultiAssetCoverage: UI_API_ROUTES.WorkstationPortfolioMultiAssetCoverage,
  assetOperations: "/api/workstation/assets",
  financialRecordExplorer: UI_API_ROUTES.WorkstationFinancialRecordExplorer,
  financialRecordExplorerRecord: UI_API_ROUTES.WorkstationFinancialRecordExplorerRecord,
  financialRecordExplorerSavedViews: UI_API_ROUTES.WorkstationFinancialRecordExplorerSavedViews,
  data: UI_API_ROUTES.WorkstationData,
  dataUploadTemplates: UI_API_ROUTES.WorkstationDataUploadTemplates,
  dataUploadPreview: UI_API_ROUTES.WorkstationDataUploadPreview,
  accounting: UI_API_ROUTES.WorkstationAccounting,
  accountingConfiguration: UI_API_ROUTES.LedgerAccountingConfiguration,
  accountingConfigurationChart: UI_API_ROUTES.LedgerAccountingConfigurationChart,
  accountingConfigurationTemplates: UI_API_ROUTES.LedgerAccountingConfigurationTemplates,
  accountingConfigurationPostingRules: UI_API_ROUTES.LedgerAccountingConfigurationPostingRules,
  accountingConfigurationPreview: UI_API_ROUTES.LedgerAccountingConfigurationPreview,
  accountingConfigurationActivate: UI_API_ROUTES.LedgerAccountingConfigurationActivate,
  accountingConfigurationAudit: UI_API_ROUTES.LedgerAccountingConfigurationAudit,
  manualJournalEntryWorkbench: UI_API_ROUTES.LedgerManualJournalEntryWorkbench,
  privateCapitalActivity: UI_API_ROUTES.LedgerPrivateCapitalActivity,
  privateCapitalFundEventRecord: UI_API_ROUTES.LedgerPrivateCapitalFundEventRecord,
  privateCapitalFundEventCommandCenter: UI_API_ROUTES.LedgerPrivateCapitalFundEventCommandCenter,
  privateCapitalCapitalAccountSubledger: UI_API_ROUTES.LedgerPrivateCapitalCapitalAccountSubledger,
  privateCapitalReportOutput: UI_API_ROUTES.LedgerPrivateCapitalReportOutput,
  privateCapitalCapitalAccountWorkbench: UI_API_ROUTES.LedgerPrivateCapitalCapitalAccountWorkbench,
  manualJournalEntryDrafts: UI_API_ROUTES.LedgerManualJournalEntryDrafts,
  manualJournalEntryValidate: UI_API_ROUTES.LedgerManualJournalEntryValidate,
  manualJournalEntrySubmitApproval: UI_API_ROUTES.LedgerManualJournalEntrySubmitApproval,
  reporting: UI_API_ROUTES.WorkstationReporting,
  reportingStructuredExport: UI_API_ROUTES.WorkstationReportingStructuredExport,
  workflowSummary: UI_API_ROUTES.WorkstationWorkflowSummary,
  featureCapabilities: UI_API_ROUTES.WorkstationFeatureCapabilities,
  extensibilityCatalog: UI_API_ROUTES.WorkstationExtensibilityCatalog,
  extensibilityTenantTemplates: UI_API_ROUTES.WorkstationExtensibilityTenantTemplates,
  extensibilityTenantTemplate: UI_API_ROUTES.WorkstationExtensibilityTenantTemplateById,
  extensibilityTenantTemplateActivate: UI_API_ROUTES.WorkstationExtensibilityTenantTemplateActivate,
  extensibilityTenantTemplateActivations: UI_API_ROUTES.WorkstationExtensibilityTenantTemplateActivations,
  extensibilityTenantTemplateReadiness: UI_API_ROUTES.WorkstationExtensibilityTenantTemplateReadiness,
  workflowLibrary: "/api/workstation/workflows",
  workflowPresets: "/api/workstation/workflows/presets",
  operationsContinuity: UI_API_ROUTES.OperationsContinuity,
  operationsContinuityApprovalPolicyMatrix: UI_API_ROUTES.OperationsContinuityApprovalPolicyMatrix,
  operationsContinuityApprovalPolicyRules: UI_API_ROUTES.OperationsContinuityApprovalPolicyRules,
  operationsContinuityCloseCalendar: UI_API_ROUTES.OperationsContinuityCloseCalendar,
  operationsContinuityCloseCalendarItems: UI_API_ROUTES.OperationsContinuityCloseCalendarItems,
  operationsPrivateCapitalCloseCockpit: UI_API_ROUTES.OperationsPrivateCapitalCloseCockpit,
  chiefOfStaff: "/api/workstation/chief-of-staff",
  runHistory: UI_API_ROUTES.RunHistory,
  runTimeline: "/api/workstation/runs/timeline",
  runSweeps: "/api/workstation/runs/sweeps",
  evidenceSubjects: UI_API_ROUTES.WorkstationEvidenceSubjects,
  evidenceVaultSearch: "/api/workstation/evidence/vault/search",
  evidenceVaultRequestLists: "/api/workstation/evidence/vault/request-lists",
  evidenceTemplates: UI_API_ROUTES.WorkstationEvidenceTemplates
} as const;

export const AUTH_API_ENDPOINTS = {
  roles: UI_API_ROUTES.AuthApiRoles,
  roleProfiles: UI_API_ROUTES.AuthApiRoleProfiles,
  accounts: UI_API_ROUTES.AuthApiAccounts,
  accountByUsername: UI_API_ROUTES.AuthApiAccountByUsername,
  accountPasswordReset: UI_API_ROUTES.AuthApiAccountPasswordReset,
  accountDisable: UI_API_ROUTES.AuthApiAccountDisable,
  sessionsRevoke: UI_API_ROUTES.AuthApiSessionsRevoke,
  audit: UI_API_ROUTES.AuthApiAudit,
  accessAssignments: UI_API_ROUTES.AuthApiAccessAssignments,
  accessAssignmentRevoke: UI_API_ROUTES.AuthApiAccessAssignmentRevoke
} as const;

export const FUND_STRUCTURE_API_ENDPOINTS = {
  setupDraftValidate: UI_API_ROUTES.FundStructureSetupDraftValidate,
  setupDraftCreate: UI_API_ROUTES.FundStructureSetupDraftCreate,
  ledgerMappingWorkbench: "/api/fund-structure/ledger-mapping-view",
  ledgerMappingAssignments: UI_API_ROUTES.FundStructureLedgerMappingAssignments,
  transactionLabPreview: "/api/fund-structure/accounting/transaction-lab/preview",
  reportPackWorkflows: UI_API_ROUTES.ReportingPackWorkflows,
  reportPackWorkflowDeliveries: UI_API_ROUTES.ReportingPackWorkflowDeliveries,
  reportPackWorkflowDeliveryPackage: UI_API_ROUTES.ReportingPackWorkflowDeliveryPackage,
  reportPackWorkflowDeliveryFailures: UI_API_ROUTES.ReportingPackWorkflowDeliveryFailures,
  reportPackDeliveryPortalPackage: UI_API_ROUTES.ReportingPackDeliveryPortalPackage,
  reportPackPreview: "/api/fund-structure/report-pack-preview",
  reportPacks: UI_API_ROUTES.FundReportPacks,
  reportingStructuredExport: UI_API_ROUTES.ReportingStructuredExport,
  reportingTemplateDrafts: "/api/fund-structure/reporting/templates/drafts",
  reportingTemplateRender: "/api/fund-structure/reporting/templates/render",
  reportingRuns: UI_API_ROUTES.ReportingRuns,
  reportingRunAuditTrail: UI_API_ROUTES.ReportingRunAuditTrail,
  reportingRunReportWriterGrid: UI_API_ROUTES.ReportingRunReportWriterGrid,
  reportingSchedules: UI_API_ROUTES.ReportingSchedules,
  reportingScheduleRunDue: UI_API_ROUTES.ReportingScheduleRunDue,
  reportingSchedulePause: UI_API_ROUTES.ReportingSchedulePause,
  reportingScheduleResume: UI_API_ROUTES.ReportingScheduleResume,
  reportingScheduleRunNow: UI_API_ROUTES.ReportingScheduleRunNow
} as const;

export const WORKSTATION_API_ENDPOINT_TEMPLATES = {
  runLedger: UI_API_ROUTES.RunsLedger,
  runContinuity: UI_API_ROUTES.RunsContinuity,
  runReviewPacket: UI_API_ROUTES.RunsReviewPacket,
  runReconciliation: UI_API_ROUTES.RunsReconciliation
} as const;

export const EXECUTION_API_ENDPOINTS = {
  ordersSubmit: UI_API_ROUTES.ExecutionOrderSubmit,
  ordersCancelAll: "/api/execution/orders/cancel-all",
  positionsActionClose: UI_API_ROUTES.ExecutionPositionActionClose,
  sessions: UI_API_ROUTES.ExecutionSessions,
  sessionsCreate: UI_API_ROUTES.ExecutionSessionCreate,
  audit: UI_API_ROUTES.ExecutionAudit,
  controls: UI_API_ROUTES.ExecutionControls,
  defaultPositionLimit: UI_API_ROUTES.ExecutionControlsDefaultPositionLimit,
  symbolPositionLimits: "/api/execution/controls/position-limits",
  manualOverrides: UI_API_ROUTES.ExecutionControlsManualOverrides
} as const;

export const RISK_API_ENDPOINTS = {
  rules: "/api/risk/rules"
} as const;

export const REPLAY_API_ENDPOINTS = {
  files: "/api/replay/files",
  start: "/api/replay/start"
} as const;

export const PROMOTION_API_ENDPOINTS = {
  approve: UI_API_ROUTES.PromotionApprove,
  evaluate: "/api/promotion/evaluate",
  reject: UI_API_ROUTES.PromotionReject,
  history: UI_API_ROUTES.PromotionHistory
} as const;

export const PORTFOLIO_API_ENDPOINTS = {
  aggregate: UI_API_ROUTES.PortfolioAggregate,
  exposure: UI_API_ROUTES.PortfolioExposure,
  household: "/api/portfolio/household"
} as const;

export const EXPORT_API_ENDPOINTS = {
  analysis: UI_API_ROUTES.ExportAnalysis,
  formats: UI_API_ROUTES.ExportFormats,
  preview: UI_API_ROUTES.ExportPreview,
  reportPacks: UI_API_ROUTES.FundReportPacks,
  reportPackEvidenceBundle: UI_API_ROUTES.FundReportPackEvidenceBundle
} as const;

export const BROKERAGE_CONNECTION_API_ENDPOINTS = {
  base: "/api/brokerage-connections"
} as const;

export const STRATEGY_API_ENDPOINTS = {
  base: "/api/strategies"
} as const;

export const STRATEGY_DESIGNER_API_ENDPOINTS = {
  templates: "/api/workstation/strategy/designer/templates",
  fieldCatalog: "/api/workstation/strategy/designer/field-catalog",
  drafts: "/api/workstation/strategy/designer/drafts",
  validate: "/api/workstation/strategy/designer/validate",
  preview: "/api/workstation/strategy/designer/preview",
  runBacktest: "/api/workstation/strategy/designer/run-backtest"
} as const;

export const STRATEGY_ENGINE_API_ENDPOINTS = {
  definitions: "/api/workstation/strategy/engine/definitions",
  validateRun: "/api/workstation/strategy/engine/validate-run"
} as const;

export const SECURITY_MASTER_API_ENDPOINTS = {
  base: UI_API_ROUTES.SecurityMasterCreate,
  assetProfiles: UI_API_ROUTES.SecurityMasterAssetProfiles,
  assetProfileDrafts: UI_API_ROUTES.SecurityMasterAssetProfileDrafts,
  assetProfileApprove: UI_API_ROUTES.SecurityMasterAssetProfileApprove,
  assetProfileRollback: UI_API_ROUTES.SecurityMasterAssetProfileRollback,
  workstationSecurities: UI_API_ROUTES.WorkstationSecurityMasterSearch,
  workstationConflictsBulkResolve: UI_API_ROUTES.WorkstationSecurityMasterBulkResolveConflicts
} as const;

export const RECONCILIATION_API_ENDPOINTS = {
  runs: UI_API_ROUTES.ReconciliationRuns,
  statementRuns: UI_API_ROUTES.ReconciliationStatementRuns,
  statementExceptions: UI_API_ROUTES.ReconciliationStatementExceptions,
  breakQueue: UI_API_ROUTES.ReconciliationBreakQueue,
  calibrationSummary: UI_API_ROUTES.ReconciliationCalibrationSummary
} as const;

export const BACKFILL_API_ENDPOINTS = {
  checkpoints: UI_API_ROUTES.BackfillCheckpoints,
  checkpointsResumable: UI_API_ROUTES.BackfillCheckpointsResumable,
  checkpointsValidation: UI_API_ROUTES.BackfillCheckpointsValidation,
  progress: UI_API_ROUTES.BackfillProgress,
  run: UI_API_ROUTES.BackfillRun,
  runPreview: UI_API_ROUTES.BackfillRunPreview
} as const;

export const PROVIDER_API_ENDPOINTS = {
  configure: UI_API_ROUTES.ProviderConfigure,
  status: UI_API_ROUTES.ProviderStatus,
  connections: UI_API_ROUTES.ProviderConnections,
  readiness: UI_API_ROUTES.ProviderReadiness
} as const;

export const ACCOUNTING_SYSTEM_API_ENDPOINTS = {
  providers: "/api/accounting-system/providers",
  importPreview: "/api/accounting-system/import/preview",
  importLatest: "/api/accounting-system/import/latest",
  reconciliationLatest: "/api/accounting-system/reconciliation/latest"
} as const;

export const PLAID_API_ENDPOINTS = {
  institutionSearch: "/api/plaid/institutions/search",
  linkToken: "/api/plaid/link-token",
  publicTokenExchange: "/api/plaid/public-token/exchange"
} as const;

export const PROVIDER_ROUTING_API_ENDPOINTS = {
  connections: UI_API_ROUTES.ProviderRoutingConnections,
  bindings: UI_API_ROUTES.ProviderRoutingBindings,
  trustSnapshots: UI_API_ROUTES.ProviderRoutingTrustSnapshots,
  preview: UI_API_ROUTES.ProviderRoutingPreview
} as const;

export const SYMBOL_API_ENDPOINTS = {
  symbols: UI_API_ROUTES.Symbols,
  statistics: UI_API_ROUTES.SymbolsStatistics,
  search: UI_API_ROUTES.SymbolsSearch,
  add: UI_API_ROUTES.SymbolsAdd,
  bulkAdd: UI_API_ROUTES.SymbolsBulkAdd
} as const;

export const QUALITY_API_ENDPOINTS = {
  dashboard: UI_API_ROUTES.QualityDashboard,
  gaps: UI_API_ROUTES.QualityGaps,
  anomalies: UI_API_ROUTES.QualityAnomalies,
  completeness: UI_API_ROUTES.QualityCompleteness
} as const;

export const MARKET_DATA_API_ENDPOINTS = {
  quotes: "/api/data/quotes",
  trades: "/api/data/trades",
  orderbook: "/api/data/orderbook",
  quotesSnapshot: UI_API_ROUTES.DataQuotesSnapshot,
  historical: UI_API_ROUTES.HistoricalData
} as const;

export const QUANT_API_ENDPOINTS = {
  templates: UI_API_ROUTES.QuantTemplates,
  parameters: UI_API_ROUTES.QuantParameters,
  run: UI_API_ROUTES.QuantRun
} as const;

export const COVERED_CALL_API_ENDPOINTS = {
  runs: UI_API_ROUTES.CoveredCallRuns,
  chainPreview: UI_API_ROUTES.CoveredCallChainPreview,
  runStatus: coveredCallRunStatusEndpoint,
  runResult: coveredCallRunResultEndpoint,
  runCancel: coveredCallRunCancelEndpoint
} as const;

export const CONFIG_API_ENDPOINTS = {
  config: UI_API_ROUTES.Config
} as const;

export type BrokerageConnectionProvider = "alpaca" | "robinhood";

export function brokerageConnectionEndpoint(provider: BrokerageConnectionProvider): string {
  return `${BROKERAGE_CONNECTION_API_ENDPOINTS.base}/${pathSegment(provider, "provider")}`;
}

export function brokerageConnectionStatusEndpoint(provider: BrokerageConnectionProvider): string {
  return `${brokerageConnectionEndpoint(provider)}/status`;
}

export function brokerageConnectionConnectEndpoint(provider: BrokerageConnectionProvider): string {
  return `${brokerageConnectionEndpoint(provider)}/connect`;
}

export function providerCredentialEndpoint(providerId: string): string {
  return routeWithParam(UI_API_ROUTES.ProviderCredentialMutation, "providerId", providerId);
}

export function providerVerifyEndpoint(providerId: string): string {
  return routeWithParam(UI_API_ROUTES.ProviderCredentialVerify, "providerId", providerId);
}

export function workstationTradingReadinessEndpoint(fundAccountId?: string): string {
  return fundAccountId
    ? `${WORKSTATION_API_ENDPOINTS.tradingReadiness}${queryString({ fundAccountId })}`
    : WORKSTATION_API_ENDPOINTS.tradingReadiness;
}

export function workstationOperatorInboxEndpoint(fundAccountId?: string): string {
  return fundAccountId
    ? `${WORKSTATION_API_ENDPOINTS.operatorInbox}${queryString({ fundAccountId })}`
    : WORKSTATION_API_ENDPOINTS.operatorInbox;
}

export function riskRuleEndpoint(ruleName: string): string {
  return `${RISK_API_ENDPOINTS.rules}/${pathSegment(ruleName, "ruleName")}`;
}

export function riskRuleStatusEndpoint(ruleName: string): string {
  return `${riskRuleEndpoint(ruleName)}/status`;
}

export function riskRuleConfigEndpoint(ruleName: string): string {
  return `${riskRuleEndpoint(ruleName)}/config`;
}

export function workstationWorkflowSummaryEndpoint(options: {
  hasOperatingContext?: boolean;
  operatingContext?: string;
  fundProfileId?: string;
  fundAccountId?: string;
  fundDisplayName?: string;
} = {}): string {
  return `${WORKSTATION_API_ENDPOINTS.workflowSummary}${queryString(options)}`;
}

export function workstationWorkflowPresetEndpoint(presetId?: string): string {
  return presetId
    ? `${WORKSTATION_API_ENDPOINTS.workflowPresets}/${pathSegment(presetId, "presetId")}`
    : WORKSTATION_API_ENDPOINTS.workflowPresets;
}

export function workstationWorkflowPresetPinEndpoint(presetId: string): string {
  return `${workstationWorkflowPresetEndpoint(presetId)}/pin`;
}

export function workstationWorkflowPresetUsedEndpoint(presetId: string): string {
  return `${workstationWorkflowPresetEndpoint(presetId)}/used`;
}

export function workstationExtensibilityTenantTemplateEndpoint(tenantTemplateId: string): string {
  return routeWithParam(WORKSTATION_API_ENDPOINTS.extensibilityTenantTemplate, "tenantTemplateId", tenantTemplateId);
}

export function workstationExtensibilityTenantTemplateActivateEndpoint(tenantTemplateId: string): string {
  return routeWithParam(
    WORKSTATION_API_ENDPOINTS.extensibilityTenantTemplateActivate,
    "tenantTemplateId",
    tenantTemplateId
  );
}

export function workstationExtensibilityTenantTemplateActivationsEndpoint(tenantTemplateId: string): string {
  return routeWithParam(
    WORKSTATION_API_ENDPOINTS.extensibilityTenantTemplateActivations,
    "tenantTemplateId",
    tenantTemplateId
  );
}

export function workstationExtensibilityTenantTemplateReadinessEndpoint(tenantTemplateId: string): string {
  return routeWithParam(
    WORKSTATION_API_ENDPOINTS.extensibilityTenantTemplateReadiness,
    "tenantTemplateId",
    tenantTemplateId
  );
}

export function workstationOperationsContinuityEndpoint(options: {
  fundAccountId?: string;
  periodId?: string;
  status?: string;
} = {}): string {
  return `${WORKSTATION_API_ENDPOINTS.operationsContinuity}${queryString(options)}`;
}

export function reportPackDeliveryPackageEndpoint(reportId: string, attemptId: string, token?: string): string {
  return `${routeWithParam(
    routeWithParam(FUND_STRUCTURE_API_ENDPOINTS.reportPackWorkflowDeliveryPackage, "reportId", reportId),
    "attemptId",
    attemptId
  )}${queryString({ token })}`;
}

export function reportPackDeliveryPortalPackageEndpoint(packageId: string, token?: string): string {
  return `${routeWithParam(FUND_STRUCTURE_API_ENDPOINTS.reportPackDeliveryPortalPackage, "packageId", packageId)}${queryString({ token })}`;
}

export function reportingTemplateSubmitEndpoint(templateName: string, version: number): string {
  return reportingTemplateLifecycleEndpoint(templateName, version, "submit");
}

export function reportingTemplateApproveEndpoint(templateName: string, version: number): string {
  return reportingTemplateLifecycleEndpoint(templateName, version, "approve");
}

export function reportingTemplateRejectEndpoint(templateName: string, version: number): string {
  return reportingTemplateLifecycleEndpoint(templateName, version, "reject");
}

export function workstationOperationsContinuityDetailEndpoint(workflowId: string): string {
  return routeWithParam(UI_API_ROUTES.OperationsContinuityById, "workflowId", workflowId);
}

export function workstationOperationsContinuityApprovalApproveEndpoint(workflowId: string): string {
  return routeWithParam(UI_API_ROUTES.OperationsContinuityApprovalApprove, "workflowId", workflowId);
}

export function workstationOperationsContinuityApprovalRejectEndpoint(workflowId: string): string {
  return routeWithParam(UI_API_ROUTES.OperationsContinuityApprovalReject, "workflowId", workflowId);
}

export function workstationOperationsContinuityCloseEndpoint(workflowId: string): string {
  return routeWithParam(UI_API_ROUTES.OperationsContinuityClose, "workflowId", workflowId);
}

export function workstationOperationsContinuityReopenEndpoint(workflowId: string): string {
  return routeWithParam(UI_API_ROUTES.OperationsContinuityReopen, "workflowId", workflowId);
}

export function workstationOperationsContinuityTimelineEndpoint(workflowId: string): string {
  return routeWithParam(UI_API_ROUTES.OperationsContinuityTimeline, "workflowId", workflowId);
}

export function workstationOperationsContinuityBreaksEndpoint(workflowId: string): string {
  return routeWithParam(UI_API_ROUTES.OperationsContinuityBreaks, "workflowId", workflowId);
}

export function workstationOperationsContinuityBreakAssignEndpoint(workflowId: string, breakId: string): string {
  return routeWithParam(
    routeWithParam(UI_API_ROUTES.OperationsContinuityReconciliationBreakAssign, "workflowId", workflowId),
    "breakId",
    breakId
  );
}

export function workstationOperationsContinuityLedgerPreviewEndpoint(workflowId: string): string {
  return routeWithParam(UI_API_ROUTES.OperationsContinuityLedgerPreview, "workflowId", workflowId);
}

export function workstationOperationsContinuityCloseCalendarEndpoint(options: {
  fundAccountId?: string;
  periodId?: string;
} = {}): string {
  return `${WORKSTATION_API_ENDPOINTS.operationsContinuityCloseCalendar}${queryString(options)}`;
}

export function workstationOperationsPrivateCapitalCloseCockpitEndpoint(options: {
  fundProfileId?: string;
  ledgerBookId?: string;
  fundAccountId?: string;
  periodId?: string;
  entityId?: string;
} = {}): string {
  return `${WORKSTATION_API_ENDPOINTS.operationsPrivateCapitalCloseCockpit}${queryString(options)}`;
}

export function workstationChiefOfStaffSessionsEndpoint(options: {
  workspace?: string;
  fundProfileId?: string;
  fundAccountId?: string;
  status?: string;
  limit?: number;
} = {}): string {
  return `${WORKSTATION_API_ENDPOINTS.chiefOfStaff}/sessions${queryString(options)}`;
}

export function workstationChiefOfStaffSessionEndpoint(sessionId: string): string {
  return `${WORKSTATION_API_ENDPOINTS.chiefOfStaff}/sessions/${pathSegment(sessionId, "sessionId")}`;
}

export function workstationChiefOfStaffDecisionEndpoint(sessionId: string): string {
  return `${workstationChiefOfStaffSessionEndpoint(sessionId)}/decisions`;
}

export function workstationChiefOfStaffTraceExportEndpoint(sessionId: string): string {
  return `${workstationChiefOfStaffSessionEndpoint(sessionId)}/export-trace`;
}

export function workstationChiefOfStaffHealthEndpoint(): string {
  return `${WORKSTATION_API_ENDPOINTS.chiefOfStaff}/health`;
}

export function workstationRunLedgerEndpoint(runId: string): string {
  return routeWithParam(UI_API_ROUTES.RunsLedger, "runId", runId);
}

export function workstationRunLedgerJournalEndpoint(runId: string, options: { from?: string; to?: string } = {}): string {
  return `${workstationRunLedgerEndpoint(runId)}/journal${queryString(options)}`;
}

export function workstationRunContinuityEndpoint(runId: string): string {
  return routeWithParam(UI_API_ROUTES.RunsContinuity, "runId", runId);
}

export function workstationRunReviewPacketEndpoint(runId: string, fundAccountId?: string): string {
  return `${routeWithParam(UI_API_ROUTES.RunsReviewPacket, "runId", runId)}${queryString({ fundAccountId })}`;
}

export function workstationRunReconciliationEndpoint(runId: string): string {
  return routeWithParam(UI_API_ROUTES.RunsReconciliation, "runId", runId);
}

export function workstationRunReconciliationHistoryEndpoint(runId: string): string {
  return routeWithParam(UI_API_ROUTES.RunsReconciliationHistory, "runId", runId);
}

export function workstationRunHistoryEndpoint(options: { mode?: string; status?: string; limit?: number } = {}): string {
  return `${WORKSTATION_API_ENDPOINTS.runHistory}${queryString(options)}`;
}

export function workstationRunTimelineEndpoint(
  options: { mode?: string; status?: string; strategyId?: string; limit?: number } = {}
): string {
  return `${WORKSTATION_API_ENDPOINTS.runTimeline}${queryString(options)}`;
}

export function workstationRunSweepsEndpoint(limit?: number): string {
  return `${WORKSTATION_API_ENDPOINTS.runSweeps}${queryString({ limit })}`;
}

export function workstationEvidenceSubjectBaseEndpoint(subjectKind: string, subjectId: string): string {
  return `${WORKSTATION_API_ENDPOINTS.evidenceSubjects}/${pathSegment(subjectKind, "subjectKind")}/${pathSegment(subjectId, "subjectId")}`;
}

export function workstationEvidencePacketEndpoint(subjectKind: string, subjectId: string): string {
  return routeWithParam(
    routeWithParam(UI_API_ROUTES.WorkstationEvidenceSubjectPacket, "subjectKind", subjectKind),
    "subjectId",
    subjectId
  );
}

export function workstationEvidenceGraphEndpoint(subjectKind: string, subjectId: string): string {
  return routeWithParam(
    routeWithParam(UI_API_ROUTES.WorkstationEvidenceSubjectGraph, "subjectKind", subjectKind),
    "subjectId",
    subjectId
  );
}

export function workstationEvidenceValidateEndpoint(subjectKind: string, subjectId: string): string {
  return routeWithParam(
    routeWithParam(UI_API_ROUTES.WorkstationEvidenceSubjectValidate, "subjectKind", subjectKind),
    "subjectId",
    subjectId
  );
}

export function workstationEvidenceExportManifestEndpoint(subjectKind: string, subjectId: string): string {
  return routeWithParam(
    routeWithParam(UI_API_ROUTES.WorkstationEvidenceSubjectExportManifest, "subjectKind", subjectKind),
    "subjectId",
    subjectId
  );
}

export function coveredCallRunsEndpoint(limit?: number): string {
  return `${COVERED_CALL_API_ENDPOINTS.runs}${queryString({ limit })}`;
}

export function coveredCallRunEndpoint(runId: string): string {
  return `${COVERED_CALL_API_ENDPOINTS.runs}/${pathSegment(runId, "runId")}`;
}

export function coveredCallRunStatusEndpoint(runId: string): string {
  return routeWithParam(UI_API_ROUTES.CoveredCallRunStatus, "runId", runId);
}

export function coveredCallRunResultEndpoint(runId: string): string {
  return routeWithParam(UI_API_ROUTES.CoveredCallRunResult, "runId", runId);
}

export function coveredCallRunCancelEndpoint(runId: string): string {
  return routeWithParam(UI_API_ROUTES.CoveredCallRunCancel, "runId", runId);
}

export function promotionEvaluateEndpoint(runId: string): string {
  return routeWithParam(UI_API_ROUTES.PromotionEvaluate, "runId", runId);
}

export function executionOrderCancelEndpoint(orderId: string): string {
  return routeWithParam(UI_API_ROUTES.ExecutionOrderCancel, "orderId", orderId);
}

export function executionPositionCloseEndpoint(): string {
  return EXECUTION_API_ENDPOINTS.positionsActionClose;
}

export function executionSessionEndpoint(sessionId: string): string {
  return routeWithParam(UI_API_ROUTES.ExecutionSessionById, "sessionId", sessionId);
}

export function executionSessionCloseEndpoint(sessionId: string): string {
  return routeWithParam(UI_API_ROUTES.ExecutionSessionClose, "sessionId", sessionId);
}

export function executionSessionReplayEndpoint(sessionId: string): string {
  return routeWithParam(UI_API_ROUTES.ExecutionSessionReplay, "sessionId", sessionId);
}

export function executionAuditEndpoint(take = 20): string {
  return `${EXECUTION_API_ENDPOINTS.audit}${queryString({ take })}`;
}

export function executionManualOverrideClearEndpoint(overrideId: string): string {
  return routeWithParam(UI_API_ROUTES.ExecutionControlsManualOverrideClear, "overrideId", overrideId);
}

export function executionSymbolPositionLimitEndpoint(symbol: string): string {
  return routeWithParam(UI_API_ROUTES.ExecutionControlsSymbolPositionLimit, "symbol", symbol);
}

export function replayFilesEndpoint(symbol?: string): string {
  return `${REPLAY_API_ENDPOINTS.files}${queryString({ symbol })}`;
}

export function portfolioHouseholdEndpoint(provider = "alpaca"): string {
  return `${PORTFOLIO_API_ENDPOINTS.household}${queryString({ provider })}`;
}

export function portfolioSymbolExposureEndpoint(symbol: string): string {
  return `${PORTFOLIO_API_ENDPOINTS.exposure.replace(/\/exposure$/, "/symbols")}/${pathSegment(symbol, "symbol")}/exposure`;
}

export function exportPreviewEndpoint(profile?: string): string {
  return `${EXPORT_API_ENDPOINTS.preview}${queryString({ profile })}`;
}

export function reportPackEvidenceBundleEndpoint(reportId?: string): string {
  return reportId
    ? routeWithParam(UI_API_ROUTES.FundReportPackEvidenceBundle, "reportId", reportId)
    : EXPORT_API_ENDPOINTS.reportPackEvidenceBundle;
}

export function reportingPackDeliveriesEndpoint(reportId: string): string {
  return routeWithParam(FUND_STRUCTURE_API_ENDPOINTS.reportPackWorkflowDeliveries, "reportId", reportId);
}

export function reportingPackDeliveryFailuresEndpoint(reportId: string): string {
  return routeWithParam(FUND_STRUCTURE_API_ENDPOINTS.reportPackWorkflowDeliveryFailures, "reportId", reportId);
}

export function reportingRunAuditTrailEndpoint(runId: string): string {
  return routeWithParam(FUND_STRUCTURE_API_ENDPOINTS.reportingRunAuditTrail, "runId", runId);
}

export function reportingRunReportWriterGridEndpoint(
  runId: string,
  gridId: string,
  format?: "json" | "csv" | "xls" | "xlsx"
): string {
  const route = routeWithParam(
    routeWithParam(FUND_STRUCTURE_API_ENDPOINTS.reportingRunReportWriterGrid, "runId", runId),
    "gridId",
    gridId
  );
  return format && format !== "json" ? `${route}${queryString({ format })}` : route;
}

export function reportingSchedulePauseEndpoint(scheduleId: string): string {
  return routeWithParam(FUND_STRUCTURE_API_ENDPOINTS.reportingSchedulePause, "scheduleId", scheduleId);
}

export function reportingScheduleResumeEndpoint(scheduleId: string): string {
  return routeWithParam(FUND_STRUCTURE_API_ENDPOINTS.reportingScheduleResume, "scheduleId", scheduleId);
}

export function reportingScheduleRunNowEndpoint(scheduleId: string): string {
  return routeWithParam(FUND_STRUCTURE_API_ENDPOINTS.reportingScheduleRunNow, "scheduleId", scheduleId);
}

export function strategyEndpoint(strategyId: string): string {
  return `${STRATEGY_API_ENDPOINTS.base}/${pathSegment(strategyId, "strategyId")}`;
}

export function strategyActionEndpoint(strategyId: string, action: "pause" | "stop"): string {
  return `${strategyEndpoint(strategyId)}/${action}`;
}

export function strategyRunsEndpoint(strategyId: string, type?: "backtest" | "paper" | "live"): string {
  return `${strategyEndpoint(strategyId)}/runs${queryString({ type })}`;
}

export function strategyDesignerDraftEndpoint(documentId?: string): string {
  return documentId
    ? `${STRATEGY_DESIGNER_API_ENDPOINTS.drafts}/${pathSegment(documentId, "documentId")}`
    : STRATEGY_DESIGNER_API_ENDPOINTS.drafts;
}

export function replaySessionActionEndpoint(
  sessionId: string,
  action: "pause" | "resume" | "stop" | "seek" | "speed" | "status"
): string {
  return `/api/replay/${pathSegment(sessionId, "sessionId")}/${action}`;
}

export function workstationRunCompareEndpoint(): string {
  return UI_API_ROUTES.RunsCompare;
}

export function workstationRunDiffEndpoint(): string {
  return UI_API_ROUTES.RunsDiff;
}

export function workstationRunAttributionEndpoint(runId: string): string {
  return routeWithParam(UI_API_ROUTES.RunsAttribution, "runId", runId);
}

export function workstationRunFillsEndpoint(runId: string, symbol?: string): string {
  return `${routeWithParam(UI_API_ROUTES.RunsFills, "runId", runId)}${queryString({ symbol })}`;
}

export function workstationRunEquityCurveEndpoint(runId: string): string {
  return routeWithParam(UI_API_ROUTES.RunsEquityCurve, "runId", runId);
}

export function portfolioRunCashFlowsEndpoint(runId: string): string {
  return routeWithParam(UI_API_ROUTES.PortfolioCashFlows, "runId", runId);
}

export function workstationRunLedgerTrialBalanceEndpoint(runId: string, accountType?: string): string {
  return `${routeWithParam(UI_API_ROUTES.RunsLedgerTrialBalance, "runId", runId)}${queryString({ accountType })}`;
}

export function workstationSecurityMasterSearchEndpoint(options: {
  query?: string;
  take?: number;
  activeOnly?: boolean;
} = {}): string {
  return `${SECURITY_MASTER_API_ENDPOINTS.workstationSecurities}${queryString(options)}`;
}

export function workstationSecurityMasterEntryEndpoint(securityId: string): string {
  return routeWithParam(UI_API_ROUTES.WorkstationSecurityMasterById, "securityId", securityId);
}

export function workstationAssetOperationsEndpoint(securityId: string): string {
  return routeWithParam(UI_API_ROUTES.WorkstationAssetOperations, "securityId", securityId);
}

export function workstationFinancialRecordExplorerEndpoint(explorerId: string): string {
  return routeWithParam(WORKSTATION_API_ENDPOINTS.financialRecordExplorer, "explorerId", explorerId);
}

export function workstationFinancialRecordExplorerRecordEndpoint(explorerId: string, recordId: string): string {
  return routeWithParam(
    routeWithParam(WORKSTATION_API_ENDPOINTS.financialRecordExplorerRecord, "explorerId", explorerId),
    "recordId",
    recordId
  );
}

export function workstationFinancialRecordExplorerSavedViewsEndpoint(explorerId: string): string {
  return routeWithParam(WORKSTATION_API_ENDPOINTS.financialRecordExplorerSavedViews, "explorerId", explorerId);
}

export function workstationSecurityMasterIdentityEndpoint(securityId: string): string {
  return `${workstationSecurityMasterEntryEndpoint(securityId)}/identity`;
}

export function workstationSecurityMasterHistoryEndpoint(securityId: string): string {
  return routeWithParam(UI_API_ROUTES.WorkstationSecurityMasterHistory, "securityId", securityId);
}

export function workstationSecurityMasterEconomicDefinitionEndpoint(securityId: string): string {
  return routeWithParam(UI_API_ROUTES.WorkstationSecurityMasterEconomicDefinition, "securityId", securityId);
}

export function workstationSecurityMasterTrustSnapshotEndpoint(securityId: string): string {
  return routeWithParam(UI_API_ROUTES.WorkstationSecurityMasterTrustSnapshot, "securityId", securityId);
}

export function workstationSecurityMasterInstrumentPassportEndpoint(securityId: string): string {
  return routeWithParam(UI_API_ROUTES.WorkstationSecurityMasterInstrumentPassport, "securityId", securityId);
}

export function securityMasterEntryEndpoint(): string {
  return SECURITY_MASTER_API_ENDPOINTS.base;
}

export function securityMasterAmendEndpoint(): string {
  return `${SECURITY_MASTER_API_ENDPOINTS.base}/amend`;
}

export function securityMasterAssetProfilesEndpoint(): string {
  return SECURITY_MASTER_API_ENDPOINTS.assetProfiles;
}

export function securityMasterAssetProfileLineageEndpoint(profileId: string): string {
  return routeWithParam(UI_API_ROUTES.SecurityMasterAssetProfileLineage, "profileId", profileId);
}

export function securityMasterAssetProfileDraftsEndpoint(): string {
  return SECURITY_MASTER_API_ENDPOINTS.assetProfileDrafts;
}

export function securityMasterAssetProfileApproveEndpoint(): string {
  return SECURITY_MASTER_API_ENDPOINTS.assetProfileApprove;
}

export function securityMasterAssetProfileRollbackEndpoint(): string {
  return SECURITY_MASTER_API_ENDPOINTS.assetProfileRollback;
}

export function securityMasterAliasUpsertEndpoint(): string {
  return UI_API_ROUTES.SecurityMasterAliasesUpsert;
}

export function securityMasterCorporateActionsEndpoint(securityId: string): string {
  return routeWithParam(UI_API_ROUTES.SecurityMasterCorporateActions, "securityId", securityId);
}

export function securityMasterTradingParametersEndpoint(securityId: string): string {
  return routeWithParam(UI_API_ROUTES.SecurityMasterTradingParameters, "securityId", securityId);
}

export function securityMasterOperatorOverridesEndpoint(securityId: string): string {
  return routeWithParam(UI_API_ROUTES.SecurityMasterOperatorOverrides, "securityId", securityId);
}

export function securityMasterConflictsEndpoint(): string {
  return UI_API_ROUTES.SecurityMasterConflicts;
}

export function securityMasterConflictResolveEndpoint(conflictId: string): string {
  return routeWithParam(UI_API_ROUTES.SecurityMasterConflictResolve, "conflictId", conflictId);
}

export function reconciliationRunEndpoint(reconciliationRunId: string): string {
  return routeWithParam(UI_API_ROUTES.ReconciliationRunById, "reconciliationRunId", reconciliationRunId);
}

export function reconciliationStatementRunsEndpoint(): string {
  return RECONCILIATION_API_ENDPOINTS.statementRuns;
}

export function reconciliationStatementRunEndpoint(runId: string): string {
  return routeWithParam(UI_API_ROUTES.ReconciliationStatementRunById, "runId", runId);
}

export function reconciliationStatementExceptionsEndpoint(): string {
  return RECONCILIATION_API_ENDPOINTS.statementExceptions;
}

export function reconciliationBreakQueueEndpoint(options: { status?: string; fundAccountId?: string } = {}): string {
  return `${RECONCILIATION_API_ENDPOINTS.breakQueue}${queryString(options)}`;
}

export function reconciliationBreakEndpoint(breakId: string): string {
  return routeWithParam(UI_API_ROUTES.ReconciliationBreakQueueById, "breakId", breakId);
}

export function reconciliationBreakAuditEndpoint(breakId: string): string {
  return routeWithParam(UI_API_ROUTES.ReconciliationBreakAudit, "breakId", breakId);
}

export function reconciliationBreakReviewEndpoint(breakId: string): string {
  return routeWithParam(UI_API_ROUTES.ReconciliationBreakReview, "breakId", breakId);
}

export function reconciliationBreakResolveEndpoint(breakId: string): string {
  return routeWithParam(UI_API_ROUTES.ReconciliationBreakResolve, "breakId", breakId);
}

export function reconciliationBreakAssignEndpoint(breakId: string): string {
  return routeWithParam(UI_API_ROUTES.ReconciliationBreakAssign, "breakId", breakId);
}

export function reconciliationBreakTransitionEndpoint(breakId: string): string {
  return routeWithParam(UI_API_ROUTES.ReconciliationBreakTransition, "breakId", breakId);
}

export function reconciliationBreakCommentsEndpoint(breakId: string): string {
  return routeWithParam(UI_API_ROUTES.ReconciliationBreakComments, "breakId", breakId);
}

export function reconciliationBreakCommentEndpoint(breakId: string, commentId: string): string {
  return routeWithParam(
    routeWithParam(UI_API_ROUTES.ReconciliationBreakComment, "breakId", breakId),
    "commentId",
    commentId
  );
}

export function reconciliationBreakRootCauseEndpoint(breakId: string): string {
  return routeWithParam(UI_API_ROUTES.ReconciliationBreakRootCause, "breakId", breakId);
}

export function reconciliationBreakResolutionEndpoint(breakId: string): string {
  return routeWithParam(UI_API_ROUTES.ReconciliationBreakResolution, "breakId", breakId);
}

export function reconciliationBreakSignOffEndpoint(breakId: string): string {
  return routeWithParam(UI_API_ROUTES.ReconciliationBreakSignOff, "breakId", breakId);
}

export function reconciliationBreakReopenEndpoint(breakId: string): string {
  return routeWithParam(UI_API_ROUTES.ReconciliationBreakReopen, "breakId", breakId);
}

export function reconciliationBreakBulkDryRunEndpoint(): string {
  return UI_API_ROUTES.ReconciliationBreakBulkDryRun;
}

export function reconciliationBreakBulkExecuteEndpoint(): string {
  return UI_API_ROUTES.ReconciliationBreakBulkExecute;
}

export function reconciliationBreakBulkStatusEndpoint(bulkActionId: string): string {
  return routeWithParam(UI_API_ROUTES.ReconciliationBreakBulkStatus, "bulkActionId", bulkActionId);
}

export function reconciliationBreakBulkResultEndpoint(bulkActionId: string): string {
  return routeWithParam(UI_API_ROUTES.ReconciliationBreakBulkResult, "bulkActionId", bulkActionId);
}

export function backfillCheckpointEndpoint(jobId: string): string {
  return routeWithParam(UI_API_ROUTES.BackfillCheckpointById, "jobId", jobId);
}

export function backfillCheckpointPendingEndpoint(jobId: string): string {
  return routeWithParam(UI_API_ROUTES.BackfillCheckpointPending, "jobId", jobId);
}

export function backfillCheckpointResumeEndpoint(jobId: string): string {
  return routeWithParam(UI_API_ROUTES.BackfillCheckpointResume, "jobId", jobId);
}

export function providerEndpoint(providerId: string): string {
  return `/api/providers/${pathSegment(providerId, "providerId")}`;
}

export function providerRemoveEndpoint(providerId: string): string {
  return `${providerEndpoint(providerId)}/remove`;
}

export function providerTestEndpoint(providerId: string): string {
  return `${providerEndpoint(providerId)}/test`;
}

export function symbolSearchEndpoint(query: string): string {
  return `${SYMBOL_API_ENDPOINTS.search}${queryString({ query })}`;
}

export function symbolEndpoint(symbol: string): string {
  return `${SYMBOL_API_ENDPOINTS.symbols}/${pathSegment(symbol, "symbol")}`;
}

export function symbolRemoveEndpoint(symbol: string): string {
  return routeWithParam(UI_API_ROUTES.SymbolRemove, "symbol", symbol);
}

export function symbolArchiveEndpoint(symbol: string): string {
  return routeWithParam(UI_API_ROUTES.SymbolArchive, "symbol", symbol);
}

export function qualityAnomalyAcknowledgeEndpoint(anomalyId: string): string {
  return routeWithParam(UI_API_ROUTES.QualityAnomaliesAcknowledge, "anomalyId", anomalyId);
}

export function marketDataQuoteEndpoint(symbol: string): string {
  return routeWithParam(UI_API_ROUTES.DataQuotes, "symbol", symbol);
}

export function marketDataTradesEndpoint(symbol: string, limit = 25): string {
  return `${MARKET_DATA_API_ENDPOINTS.trades}/${pathSegment(symbol, "symbol")}${queryString({ limit })}`;
}

export function marketDataOrderbookEndpoint(symbol: string, levels = 10): string {
  return `${MARKET_DATA_API_ENDPOINTS.orderbook}/${pathSegment(symbol, "symbol")}${queryString({ levels })}`;
}

export function marketDataQuotesSnapshotEndpoint(symbols?: readonly string[]): string {
  const trimmed = symbols?.map((symbol) => symbol.trim()).filter(Boolean) ?? [];
  return `${MARKET_DATA_API_ENDPOINTS.quotesSnapshot}${queryString({ symbols: trimmed })}`;
}

export function historicalBarsEndpoint(
  symbol: string,
  request: { intervalMinutes: number; from?: string; to?: string; maxBars?: number }
): string {
  return `${MARKET_DATA_API_ENDPOINTS.historical}/${pathSegment(symbol, "symbol")}/bars${queryString(request)}`;
}

function workstationRunBaseEndpoint(runId: string): string {
  return `/api/workstation/runs/${pathSegment(runId, "runId")}`;
}

function workstationRunRootEndpoint(): string {
  return "/api/workstation/runs";
}

function reportingTemplateLifecycleEndpoint(
  templateName: string,
  version: number,
  action: "submit" | "approve" | "reject"
): string {
  if (!Number.isInteger(version) || version < 1) {
    throw new Error("Cannot build Meridian API endpoint: report template version must be a positive integer.");
  }

  return `${FUND_STRUCTURE_API_ENDPOINTS.reportingTemplateDrafts.replace(/\/drafts$/, "")}/${pathSegment(
    templateName,
    "templateName"
  )}/versions/${version}/${action}`;
}

function pathSegment(value: string, name: string): string {
  const trimmed = value.trim();
  if (!trimmed) {
    throw new Error(`Cannot build Meridian API endpoint: ${name} is required.`);
  }

  return encodeURIComponent(trimmed);
}

function routeWithParam(route: string, paramName: string, value: string): string {
  const encodedValue = pathSegment(value, paramName);
  const simplePlaceholder = `{${paramName}}`;
  if (route.includes(simplePlaceholder)) {
    return route.replace(simplePlaceholder, encodedValue);
  }

  return route.replace(new RegExp(`\\{${paramName}:[^}]+\\}`, "g"), encodedValue);
}

function queryString(params: Record<string, string | number | boolean | readonly string[] | null | undefined>): string {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (Array.isArray(value)) {
      const normalizedValues = value.map((entry) => entry.trim()).filter(Boolean);
      if (normalizedValues.length > 0) {
        search.set(key, normalizedValues.join(","));
      }
      continue;
    }

    if (typeof value === "string") {
      const trimmed = value.trim();
      if (trimmed) {
        search.set(key, trimmed);
      }
      continue;
    }

    if (typeof value === "number") {
      if (Number.isFinite(value)) {
        search.set(key, String(value));
      }
      continue;
    }

    if (typeof value === "boolean") {
      search.set(key, String(value));
    }
  }

  const value = search.toString();
  return value ? `?${value}` : "";
}
