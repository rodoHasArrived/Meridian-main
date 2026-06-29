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
  ledgerBooks: UI_API_ROUTES.LedgerBooks,
  accountingConfiguration: UI_API_ROUTES.LedgerAccountingConfiguration,
  accountingConfigurationChart: UI_API_ROUTES.LedgerAccountingConfigurationChart,
  accountingConfigurationTemplates: UI_API_ROUTES.LedgerAccountingConfigurationTemplates,
  accountingConfigurationPostingRules: UI_API_ROUTES.LedgerAccountingConfigurationPostingRules,
  accountingConfigurationPostingRulePromotionApprovals: UI_API_ROUTES.LedgerAccountingConfigurationPostingRulePromotionApprovals,
  accountingConfigurationPreview: UI_API_ROUTES.LedgerAccountingConfigurationPreview,
  accountingConfigurationPostingRuleDryRun: UI_API_ROUTES.LedgerAccountingConfigurationPostingRuleDryRun,
  accountingConfigurationPostingRuleCandidates: UI_API_ROUTES.LedgerAccountingConfigurationPostingRuleCandidates,
  accountingConfigurationPostingRuleTests: UI_API_ROUTES.LedgerAccountingConfigurationPostingRuleTests,
  accountingConfigurationPostingRuleTestCases: UI_API_ROUTES.LedgerAccountingConfigurationPostingRuleTestCases,
  accountingConfigurationActivate: UI_API_ROUTES.LedgerAccountingConfigurationActivate,
  accountingConfigurationAudit: UI_API_ROUTES.LedgerAccountingConfigurationAudit,
  closeManagementPeriodPlan: UI_API_ROUTES.LedgerCloseManagementPeriodPlan,
  closeManagementPeriodPlanConfiguration: UI_API_ROUTES.LedgerCloseManagementPeriodPlanConfiguration,
  closeManagementLateAdjustments: UI_API_ROUTES.LedgerCloseManagementLateAdjustments,
  closeManagementLateAdjustmentReview: UI_API_ROUTES.LedgerCloseManagementLateAdjustmentReview,
  closeManagementTaskSignOffs: UI_API_ROUTES.LedgerCloseManagementTaskSignOffs,
  closeManagementEvidenceReview: UI_API_ROUTES.LedgerCloseManagementEvidenceReview,
  closeManagementPeriodLock: UI_API_ROUTES.LedgerCloseManagementPeriodLock,
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
  manualJournalEntryEvidence: UI_API_ROUTES.LedgerManualJournalEntryEvidence,
  manualJournalEntryLifecycleAction: UI_API_ROUTES.LedgerManualJournalEntryLifecycleAction,
  accountingReportPackage: UI_API_ROUTES.LedgerReportsAccountingPackage,
  accountingReportPackages: UI_API_ROUTES.LedgerReportsAccountingPackages,
  accountingReportPackageExport: UI_API_ROUTES.LedgerReportsAccountingPackageExport,
  accountingReportPackageCertification: UI_API_ROUTES.LedgerReportsAccountingPackageCertification,
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
  financialOperationsCommandCenter: UI_API_ROUTES.FinancialOperationsCommandCenter,
  operationsPrivateCapitalCloseCockpit: UI_API_ROUTES.OperationsPrivateCapitalCloseCockpit,
  chiefOfStaff: "/api/workstation/chief-of-staff",
  runHistory: UI_API_ROUTES.RunHistory,
  runTimeline: "/api/workstation/runs/timeline",
  runSweeps: "/api/workstation/runs/sweeps",
  evidenceSubjects: UI_API_ROUTES.WorkstationEvidenceSubjects,
  evidenceVaultSearch: "/api/workstation/evidence/vault/search",
  evidenceVaultIntake: "/api/workstation/evidence/vault/intake",
  evidenceVaultRequestLists: "/api/workstation/evidence/vault/request-lists",
  evidenceVaultDocuments: "/api/workstation/evidence/vault/documents",
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

export function evidenceVaultDocumentReviewEndpoint(vaultId: string, documentId: string): string {
  return `/api/workstation/evidence/vault/${encodeURIComponent(vaultId)}/documents/${encodeURIComponent(documentId)}/review`;
}

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

/**
 * Passport Workbench governed-write routes. Each carries a `{securityId:guid}` path segment that is
 * authoritative server-side, so the helpers substitute it explicitly rather than relying on the body.
 */
function securityMasterWorkbenchPath(route: string, securityId: string): string {
  return route.replace("{securityId:guid}", encodeURIComponent(securityId));
}

export function securityMasterWorkbenchFieldEndpoint(securityId: string): string {
  return securityMasterWorkbenchPath(UI_API_ROUTES.SecurityMasterWorkbenchField, securityId);
}

export function securityMasterWorkbenchResolveConflictEndpoint(securityId: string): string {
  return securityMasterWorkbenchPath(UI_API_ROUTES.SecurityMasterWorkbenchResolveConflict, securityId);
}

export function securityMasterWorkbenchSubmitEndpoint(securityId: string): string {
  return securityMasterWorkbenchPath(UI_API_ROUTES.SecurityMasterWorkbenchSubmit, securityId);
}

export function securityMasterWorkbenchApproveEndpoint(securityId: string): string {
  return securityMasterWorkbenchPath(UI_API_ROUTES.SecurityMasterWorkbenchApprove, securityId);
}

export function securityMasterWorkbenchPublishEndpoint(securityId: string): string {
  return securityMasterWorkbenchPath(UI_API_ROUTES.SecurityMasterWorkbenchPublish, securityId);
}

export const REFERENCE_DATA_API_ENDPOINTS = {
  edgarFiler: UI_API_ROUTES.ReferenceDataEdgarFiler,
  edgarFacts: UI_API_ROUTES.ReferenceDataEdgarFacts,
  edgarSecurityData: UI_API_ROUTES.ReferenceDataEdgarSecurityData,
  bondReference: UI_API_ROUTES.ReferenceDataBondReference,
  bondLifecycle: UI_API_ROUTES.ReferenceDataBondLifecycle,
  bondAccrualConvention: UI_API_ROUTES.ReferenceDataBondAccrualConvention,
  bondIssuerLadder: UI_API_ROUTES.ReferenceDataBondIssuerLadder,
  bondMaturityLadder: UI_API_ROUTES.ReferenceDataBondMaturityLadder,
  optionContract: UI_API_ROUTES.ReferenceDataOptionContract,
  optionSeries: UI_API_ROUTES.ReferenceDataOptionSeries,
  optionUnderlyingLinkage: UI_API_ROUTES.ReferenceDataOptionUnderlyingLinkage,
  optionExpiryLadder: UI_API_ROUTES.ReferenceDataOptionExpiryLadder,
  optionChainImport: UI_API_ROUTES.ReferenceDataOptionChainImport,
  optionChainSnapshot: UI_API_ROUTES.ReferenceDataOptionChainSnapshot,
  equityReference: UI_API_ROUTES.ReferenceDataEquityReference,
  equityByExchange: UI_API_ROUTES.ReferenceDataEquityByExchange,
  equityByIssuer: UI_API_ROUTES.ReferenceDataEquityByIssuer,
  futureReference: UI_API_ROUTES.ReferenceDataFutureReference,
  futuresByRoot: UI_API_ROUTES.ReferenceDataFuturesByRoot,
  futureExpiryLadder: UI_API_ROUTES.ReferenceDataFutureExpiryLadder,
  futureFrontMonth: UI_API_ROUTES.ReferenceDataFutureFrontMonth,
  fxSpotReference: UI_API_ROUTES.ReferenceDataFxSpotReference,
  fxSpotByPairCode: UI_API_ROUTES.ReferenceDataFxSpotByPairCode,
  fxSpotByCurrency: UI_API_ROUTES.ReferenceDataFxSpotByCurrency,
  swapReference: UI_API_ROUTES.ReferenceDataSwapReference,
  swapsByType: UI_API_ROUTES.ReferenceDataSwapsByType,
  swapsMaturingBefore: UI_API_ROUTES.ReferenceDataSwapsMaturingBefore,
  commodityReference: UI_API_ROUTES.ReferenceDataCommodityReference,
  commoditiesByType: UI_API_ROUTES.ReferenceDataCommoditiesByType,
  commoditiesByExchange: UI_API_ROUTES.ReferenceDataCommoditiesByExchange,
  cryptoReference: UI_API_ROUTES.ReferenceDataCryptoReference,
  cryptoByNetwork: UI_API_ROUTES.ReferenceDataCryptoByNetwork,
  cryptoByBaseCurrency: UI_API_ROUTES.ReferenceDataCryptoByBaseCurrency,
  depositReference: UI_API_ROUTES.ReferenceDataDepositReference,
  depositsByInstitution: UI_API_ROUTES.ReferenceDataDepositsByInstitution,
  depositsMaturingBefore: UI_API_ROUTES.ReferenceDataDepositsMaturingBefore,
  moneyMarketFundReference: UI_API_ROUTES.ReferenceDataMoneyMarketFundReference,
  moneyMarketFundsByFamily: UI_API_ROUTES.ReferenceDataMoneyMarketFundsByFamily,
  moneyMarketFundsBySweepEligibility: UI_API_ROUTES.ReferenceDataMoneyMarketFundsBySweepEligibility,
  certificateOfDepositReference: UI_API_ROUTES.ReferenceDataCertificateOfDepositReference,
  certificatesOfDepositByIssuer: UI_API_ROUTES.ReferenceDataCertificatesOfDepositByIssuer,
  certificatesOfDepositMaturingBefore: UI_API_ROUTES.ReferenceDataCertificatesOfDepositMaturingBefore,
  edgarIngest: UI_API_ROUTES.SecurityMasterEdgarIngest
} as const;

export type ReferenceDataEndpointFamily =
  | "Bonds"
  | "Options"
  | "Equities"
  | "Futures"
  | "FX spot"
  | "Swaps"
  | "Commodities"
  | "Crypto"
  | "Deposits"
  | "Money-market funds"
  | "Certificates of deposit"
  | "EDGAR";

export interface ReferenceDataWorkbenchEndpointSeed {
  securityId: string;
  displayName?: string | null;
  primaryIdentifierKind?: string | null;
  primaryIdentifierValue?: string | null;
  currency?: string | null;
  issuerName?: string | null;
  optionContractSymbol?: string | null;
  optionChainId?: string | null;
  optionExpiryDate?: string | null;
  underlyingSymbol?: string | null;
  exchangeCode?: string | null;
  rootSymbol?: string | null;
  fxPairCode?: string | null;
  swapType?: string | null;
  commodityType?: string | null;
  network?: string | null;
  baseCurrency?: string | null;
  institutionName?: string | null;
  fundFamily?: string | null;
  cik?: string | null;
  maturityFrom?: string | null;
  maturityTo?: string | null;
  beforeDate?: string | null;
}

export interface ReferenceDataEndpointDefinition {
  id: string;
  family: ReferenceDataEndpointFamily;
  label: string;
  method: "GET" | "POST";
  path: string;
  requestLabel: string;
  probe: boolean;
  mutation?: boolean;
}

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
  providers: UI_API_ROUTES.AccountingSystemProviders,
  productionReadiness: UI_API_ROUTES.AccountingSystemProductionReadiness,
  importPreview: UI_API_ROUTES.AccountingSystemImportPreview,
  importLatest: UI_API_ROUTES.AccountingSystemImportLatest,
  reconciliationLatest: UI_API_ROUTES.AccountingSystemReconciliationLatest,
  mappingProfiles: UI_API_ROUTES.AccountingSystemMappingProfiles,
  exportPackages: UI_API_ROUTES.AccountingSystemExportPackages,
  exportPackageManifest: UI_API_ROUTES.AccountingSystemExportPackageManifest,
  exportPackageCertification: UI_API_ROUTES.AccountingSystemExportPackageCertification,
  migrationRunArtifacts: UI_API_ROUTES.AccountingSystemMigrationRunArtifacts,
  migrationWorkerPlans: UI_API_ROUTES.AccountingSystemMigrationWorkerPlans,
  tenantAdministrationProfile: UI_API_ROUTES.AccountingSystemTenantAdministrationProfile,
  productionCertificationProfile: UI_API_ROUTES.AccountingSystemProductionCertificationProfile
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

export const PROVIDER_INTEGRATION_API_ENDPOINTS = {
  templates: UI_API_ROUTES.WorkstationProviderIntegrationTemplates,
  template: UI_API_ROUTES.WorkstationProviderIntegrationTemplateById,
  openApiImport: UI_API_ROUTES.WorkstationProviderIntegrationOpenApiImport,
  setupSave: UI_API_ROUTES.WorkstationProviderIntegrationSetupSave,
  manifestReadiness: UI_API_ROUTES.WorkstationProviderIntegrationManifestReadiness,
  manualCsvDryRun: UI_API_ROUTES.WorkstationProviderIntegrationManualCsvDryRun,
  restDryRun: UI_API_ROUTES.WorkstationProviderIntegrationRestDryRun,
  activate: UI_API_ROUTES.WorkstationProviderIntegrationActivate,
  connectionMonitor: UI_API_ROUTES.WorkstationProviderIntegrationConnectionMonitor,
  connectionSyncRuns: UI_API_ROUTES.WorkstationProviderIntegrationConnectionSyncRuns,
  connectionSyncPlan: UI_API_ROUTES.WorkstationProviderIntegrationConnectionSyncPlan,
  connectionRunDueSync: UI_API_ROUTES.WorkstationProviderIntegrationConnectionRunDueSync,
  schemaDriftCheck: UI_API_ROUTES.WorkstationProviderIntegrationSchemaDriftCheck,
  stagingReview: UI_API_ROUTES.WorkstationProviderIntegrationStagingReview,
  identityResolution: UI_API_ROUTES.WorkstationProviderIntegrationIdentityResolution,
  promotionReadiness: UI_API_ROUTES.WorkstationProviderIntegrationPromotionReadiness,
  reconciliationHandoffHistory: UI_API_ROUTES.WorkstationProviderIntegrationReconciliationHandoffHistory,
  reconciliationHandoff: UI_API_ROUTES.WorkstationProviderIntegrationReconciliationHandoff,
  quarantineReview: UI_API_ROUTES.WorkstationProviderIntegrationQuarantineReview,
  quarantineResolve: UI_API_ROUTES.WorkstationProviderIntegrationQuarantineResolve,
  quarantineReplay: UI_API_ROUTES.WorkstationProviderIntegrationQuarantineReplay
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

export const ADMIN_OPERATIONS_API_ENDPOINTS = {
  maintenanceSchedule: UI_API_ROUTES.AdminMaintenanceSchedule,
  maintenanceRun: UI_API_ROUTES.AdminMaintenanceRun,
  maintenanceRunById: UI_API_ROUTES.AdminMaintenanceRunById,
  maintenanceHistory: UI_API_ROUTES.AdminMaintenanceHistory,
  storageTiers: UI_API_ROUTES.AdminStorageTiers,
  storageMigrate: UI_API_ROUTES.AdminStorageMigrate,
  storageUsage: UI_API_ROUTES.AdminStorageUsage,
  retention: UI_API_ROUTES.AdminRetention,
  retentionDelete: UI_API_ROUTES.AdminRetentionDelete,
  retentionApply: UI_API_ROUTES.AdminRetentionApply,
  cleanupPreview: UI_API_ROUTES.AdminCleanupPreview,
  cleanupExecute: UI_API_ROUTES.AdminCleanupExecute,
  storagePermissions: UI_API_ROUTES.AdminStoragePermissions,
  selftest: UI_API_ROUTES.AdminSelftest,
  errorCodes: UI_API_ROUTES.AdminErrorCodes,
  showConfig: UI_API_ROUTES.AdminShowConfig,
  quickCheck: UI_API_ROUTES.AdminQuickCheck
} as const;

export const MAINTENANCE_API_ENDPOINTS = {
  schedules: UI_API_ROUTES.MaintenanceSchedules,
  schedule: UI_API_ROUTES.MaintenanceSchedulesById,
  scheduleDelete: UI_API_ROUTES.MaintenanceSchedulesDelete,
  scheduleEnable: UI_API_ROUTES.MaintenanceSchedulesEnable,
  scheduleDisable: UI_API_ROUTES.MaintenanceSchedulesDisable,
  scheduleRun: UI_API_ROUTES.MaintenanceSchedulesRun,
  scheduleHistory: UI_API_ROUTES.MaintenanceSchedulesHistory
} as const;

export const DIAGNOSTICS_API_ENDPOINTS = {
  quickCheck: UI_API_ROUTES.DiagnosticsQuickCheck,
  showConfig: UI_API_ROUTES.DiagnosticsShowConfig,
  errorCodes: UI_API_ROUTES.DiagnosticsErrorCodes,
  selftest: UI_API_ROUTES.DiagnosticsSelftest,
  storage: UI_API_ROUTES.DiagnosticsStorage,
  bundle: UI_API_ROUTES.DiagnosticsBundle,
  metrics: UI_API_ROUTES.DiagnosticsMetrics,
  validate: UI_API_ROUTES.DiagnosticsValidate,
  validateCredentials: UI_API_ROUTES.DiagnosticsValidateCredentials,
  testConnectivity: UI_API_ROUTES.DiagnosticsTestConnectivity,
  validateConfig: UI_API_ROUTES.DiagnosticsValidateConfig,
  coordination: UI_API_ROUTES.DiagnosticsCoordination
} as const;

export const PACKAGING_API_ENDPOINTS = {
  create: "/api/packaging/create",
  import: "/api/packaging/import",
  validate: "/api/packaging/validate",
  contents: "/api/packaging/contents",
  list: "/api/packaging/list",
  delete: "/api/packaging/{fileName}",
  download: "/api/packaging/download/{fileName}"
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

export function adminMaintenanceRunEndpoint(runId: string): string {
  return routeWithParam(ADMIN_OPERATIONS_API_ENDPOINTS.maintenanceRunById, "runId", runId);
}

export function adminMaintenanceHistoryEndpoint(limit?: number): string {
  return `${ADMIN_OPERATIONS_API_ENDPOINTS.maintenanceHistory}${queryString({ limit })}`;
}

export function adminStorageMigrateEndpoint(targetTier: string): string {
  return routeWithParam(ADMIN_OPERATIONS_API_ENDPOINTS.storageMigrate, "targetTier", targetTier);
}

export function adminRetentionDeleteEndpoint(policyId: string): string {
  return routeWithParam(ADMIN_OPERATIONS_API_ENDPOINTS.retentionDelete, "policyId", policyId);
}

export function maintenanceScheduleEndpoint(scheduleId: string): string {
  return routeWithParam(MAINTENANCE_API_ENDPOINTS.schedule, "id", scheduleId);
}

export function maintenanceScheduleDeleteEndpoint(scheduleId: string): string {
  return routeWithParam(MAINTENANCE_API_ENDPOINTS.scheduleDelete, "id", scheduleId);
}

export function maintenanceScheduleEnableEndpoint(scheduleId: string): string {
  return routeWithParam(MAINTENANCE_API_ENDPOINTS.scheduleEnable, "id", scheduleId);
}

export function maintenanceScheduleDisableEndpoint(scheduleId: string): string {
  return routeWithParam(MAINTENANCE_API_ENDPOINTS.scheduleDisable, "id", scheduleId);
}

export function maintenanceScheduleRunEndpoint(scheduleId: string): string {
  return routeWithParam(MAINTENANCE_API_ENDPOINTS.scheduleRun, "id", scheduleId);
}

export function maintenanceScheduleHistoryEndpoint(scheduleId: string, limit?: number): string {
  return `${routeWithParam(MAINTENANCE_API_ENDPOINTS.scheduleHistory, "id", scheduleId)}${queryString({ limit })}`;
}

export function packagingContentsEndpoint(packagePath: string): string {
  return `${PACKAGING_API_ENDPOINTS.contents}${queryString({ path: packagePath })}`;
}

export function packagingListEndpoint(directory?: string | null): string {
  return `${PACKAGING_API_ENDPOINTS.list}${queryString({ directory })}`;
}

export function packagingDeleteEndpoint(fileName: string, directory?: string | null): string {
  return `${routeWithParam(PACKAGING_API_ENDPOINTS.delete, "fileName", fileName)}${queryString({ directory })}`;
}

export function packagingDownloadEndpoint(fileName: string, directory?: string | null): string {
  return `${routeWithParam(PACKAGING_API_ENDPOINTS.download, "fileName", fileName)}${queryString({ directory })}`;
}

export function providerCredentialEndpoint(providerId: string): string {
  return routeWithParam(UI_API_ROUTES.ProviderCredentialMutation, "providerId", providerId);
}

export function providerVerifyEndpoint(providerId: string): string {
  return routeWithParam(UI_API_ROUTES.ProviderCredentialVerify, "providerId", providerId);
}

export function workstationProviderIntegrationTemplateEndpoint(manifestId: string): string {
  return routeWithParam(PROVIDER_INTEGRATION_API_ENDPOINTS.template, "manifestId", manifestId);
}

export function workstationProviderIntegrationReadinessEndpoint(
  manifestId: string,
  connectionId?: string | null
): string {
  return `${routeWithParam(PROVIDER_INTEGRATION_API_ENDPOINTS.manifestReadiness, "manifestId", manifestId)}${queryString({ connectionId })}`;
}

export function workstationProviderIntegrationConnectionMonitorEndpoint(
  connectionId: string,
  recentRunLimit?: number
): string {
  return `${routeWithParam(PROVIDER_INTEGRATION_API_ENDPOINTS.connectionMonitor, "connectionId", connectionId)}${queryString({ recentRunLimit })}`;
}

export function workstationProviderIntegrationConnectionSyncRunsEndpoint(
  connectionId: string,
  recentRunLimit?: number
): string {
  return `${routeWithParam(PROVIDER_INTEGRATION_API_ENDPOINTS.connectionSyncRuns, "connectionId", connectionId)}${queryString({ recentRunLimit })}`;
}

export function workstationProviderIntegrationConnectionSyncPlanEndpoint(
  connectionId: string,
  evaluatedAt?: string | null
): string {
  return `${routeWithParam(PROVIDER_INTEGRATION_API_ENDPOINTS.connectionSyncPlan, "connectionId", connectionId)}${queryString({ evaluatedAt })}`;
}

export function workstationProviderIntegrationConnectionRunDueSyncEndpoint(connectionId: string): string {
  return routeWithParam(PROVIDER_INTEGRATION_API_ENDPOINTS.connectionRunDueSync, "connectionId", connectionId);
}

export function workstationProviderIntegrationStagingReviewEndpoint(
  connectionId: string,
  recentRunLimit?: number
): string {
  return `${routeWithParam(PROVIDER_INTEGRATION_API_ENDPOINTS.stagingReview, "connectionId", connectionId)}${queryString({ recentRunLimit })}`;
}

export function workstationProviderIntegrationIdentityResolutionEndpoint(
  connectionId: string,
  recentRunLimit?: number
): string {
  return `${routeWithParam(PROVIDER_INTEGRATION_API_ENDPOINTS.identityResolution, "connectionId", connectionId)}${queryString({ recentRunLimit })}`;
}

export function workstationProviderIntegrationPromotionReadinessEndpoint(
  connectionId: string,
  recentRunLimit?: number
): string {
  return `${routeWithParam(PROVIDER_INTEGRATION_API_ENDPOINTS.promotionReadiness, "connectionId", connectionId)}${queryString({ recentRunLimit })}`;
}

export function workstationProviderIntegrationReconciliationHandoffHistoryEndpoint(connectionId: string): string {
  return routeWithParam(PROVIDER_INTEGRATION_API_ENDPOINTS.reconciliationHandoffHistory, "connectionId", connectionId);
}

export function workstationProviderIntegrationQuarantineReviewEndpoint(
  connectionId: string,
  recentRunLimit?: number
): string {
  return `${routeWithParam(PROVIDER_INTEGRATION_API_ENDPOINTS.quarantineReview, "connectionId", connectionId)}${queryString({ recentRunLimit })}`;
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
  ledgerBookId?: string;
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

export function workstationOperationsContinuityChecklistEndpoint(workflowId: string): string {
  return routeWithParam(UI_API_ROUTES.OperationsContinuityChecklist, "workflowId", workflowId);
}

export function workstationOperationsContinuityCloseReadinessEndpoint(workflowId: string): string {
  return routeWithParam(UI_API_ROUTES.OperationsContinuityCloseReadiness, "workflowId", workflowId);
}

export function workstationOperationsContinuityBrokerImportEndpoint(workflowId: string): string {
  return routeWithParam(UI_API_ROUTES.OperationsContinuityBrokerImport, "workflowId", workflowId);
}

export function workstationOperationsContinuityBrokerNormalizeEndpoint(workflowId: string): string {
  return routeWithParam(UI_API_ROUTES.OperationsContinuityBrokerNormalize, "workflowId", workflowId);
}

export function workstationOperationsContinuityPostureRefreshEndpoint(workflowId: string): string {
  return routeWithParam(UI_API_ROUTES.OperationsContinuityPostureRefresh, "workflowId", workflowId);
}

export function workstationOperationsContinuitySecurityMasterResolveEndpoint(workflowId: string): string {
  return routeWithParam(UI_API_ROUTES.OperationsContinuitySecurityMasterResolve, "workflowId", workflowId);
}

export function workstationOperationsContinuitySecurityMasterOverrideApproveEndpoint(
  workflowId: string,
  overrideId: string
): string {
  return routeWithParam(
    routeWithParam(UI_API_ROUTES.OperationsContinuitySecurityMasterOverrideApprove, "workflowId", workflowId),
    "overrideId",
    overrideId
  );
}

export function workstationOperationsContinuityLedgerDraftEndpoint(workflowId: string): string {
  return routeWithParam(UI_API_ROUTES.OperationsContinuityLedgerDraft, "workflowId", workflowId);
}

export function workstationOperationsContinuityLedgerValidateEndpoint(workflowId: string): string {
  return routeWithParam(UI_API_ROUTES.OperationsContinuityLedgerValidate, "workflowId", workflowId);
}

export function workstationOperationsContinuityLedgerPostEndpoint(workflowId: string): string {
  return routeWithParam(UI_API_ROUTES.OperationsContinuityLedgerPost, "workflowId", workflowId);
}

export function workstationOperationsContinuityReconciliationRunEndpoint(workflowId: string): string {
  return routeWithParam(UI_API_ROUTES.OperationsContinuityReconciliationRun, "workflowId", workflowId);
}

export function workstationOperationsContinuityApprovalSubmitEndpoint(workflowId: string): string {
  return routeWithParam(UI_API_ROUTES.OperationsContinuityApprovalSubmit, "workflowId", workflowId);
}

export function workstationOperationsContinuityChecklistAcknowledgeEndpoint(workflowId: string, taskId: string): string {
  return routeWithParam(
    routeWithParam(UI_API_ROUTES.OperationsContinuityChecklistAcknowledge, "workflowId", workflowId),
    "taskId",
    taskId
  );
}

export function workstationOperationsContinuityBreakAssignEndpoint(workflowId: string, breakId: string): string {
  return routeWithParam(
    routeWithParam(UI_API_ROUTES.OperationsContinuityReconciliationBreakAssign, "workflowId", workflowId),
    "breakId",
    breakId
  );
}

export function workstationOperationsContinuityBreakResolveEndpoint(workflowId: string, breakId: string): string {
  return routeWithParam(
    routeWithParam(UI_API_ROUTES.OperationsContinuityReconciliationBreakResolve, "workflowId", workflowId),
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

export function workstationFinancialOperationsCommandCenterEndpoint(options: {
  fundProfileId?: string;
  ledgerBookId?: string;
  fundAccountId?: string;
  periodId?: string;
  entityId?: string;
} = {}): string {
  return `${WORKSTATION_API_ENDPOINTS.financialOperationsCommandCenter}${queryString(options)}`;
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

export interface LedgerDimensionQueryOptions {
  fundId?: string;
  entityId?: string;
  sleeveId?: string;
  strategyId?: string;
  portfolioId?: string;
  accountId?: string;
  investorId?: string;
  capitalAccountId?: string;
  instrumentId?: string;
  taxLotId?: string;
  costCenterId?: string;
  counterpartyId?: string;
  externalGlDimensions?: Record<string, string | null | undefined>;
}

export interface LedgerJournalQueryOptions extends LedgerDimensionQueryOptions {
  from?: string;
  to?: string;
}

export interface LedgerTrialBalanceQueryOptions extends LedgerDimensionQueryOptions {
  accountType?: string;
}

function ledgerDimensionQueryParams(options: LedgerDimensionQueryOptions): Record<string, string | undefined> {
  const params: Record<string, string | undefined> = {
    fundId: options.fundId,
    entityId: options.entityId,
    sleeveId: options.sleeveId,
    strategyId: options.strategyId,
    portfolioId: options.portfolioId,
    accountId: options.accountId,
    investorId: options.investorId,
    capitalAccountId: options.capitalAccountId,
    instrumentId: options.instrumentId,
    taxLotId: options.taxLotId,
    costCenterId: options.costCenterId,
    counterpartyId: options.counterpartyId
  };

  for (const [key, value] of Object.entries(options.externalGlDimensions ?? {}).sort(([left], [right]) =>
    left.localeCompare(right)
  )) {
    if (key.trim()) {
      params[`externalGl.${key.trim()}`] = value ?? undefined;
    }
  }

  return params;
}

export function workstationRunLedgerEndpoint(runId: string): string {
  return routeWithParam(UI_API_ROUTES.RunsLedger, "runId", runId);
}

export function workstationRunLedgerJournalEndpoint(runId: string, options: LedgerJournalQueryOptions = {}): string {
  return `${workstationRunLedgerEndpoint(runId)}/journal${queryString({
    from: options.from,
    to: options.to,
    ...ledgerDimensionQueryParams(options)
  })}`;
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
  format?: "json" | "csv" | "pdf" | "xls" | "xlsx"
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

export function workstationRunLedgerTrialBalanceEndpoint(
  runId: string,
  accountTypeOrOptions?: string | LedgerTrialBalanceQueryOptions
): string {
  const options =
    typeof accountTypeOrOptions === "string" ? { accountType: accountTypeOrOptions } : accountTypeOrOptions ?? {};
  return `${routeWithParam(UI_API_ROUTES.RunsLedgerTrialBalance, "runId", runId)}${queryString({
    accountType: options.accountType,
    ...ledgerDimensionQueryParams(options)
  })}`;
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
export function buildReferenceDataWorkbenchEndpoints(
  seed: ReferenceDataWorkbenchEndpointSeed
): ReferenceDataEndpointDefinition[] {
  const issuerName = coalesceReferenceSeedValue(seed.issuerName, seed.displayName, "Meridian");
  const symbol = normalizeReferenceSymbol(coalesceReferenceSeedValue(seed.primaryIdentifierValue, seed.displayName, "AAPL"));
  const currency = normalizeReferenceSymbol(seed.currency || "USD");
  const optionContractSymbol = normalizeReferenceSymbol(
    coalesceReferenceSeedValue(seed.optionContractSymbol, `${symbol}20261219C00150000`)
  );
  const optionChainId = coalesceReferenceSeedValue(seed.optionChainId, `${symbol}-20261219`);
  const optionExpiryDate = seed.optionExpiryDate || "2026-12-19";
  const underlyingSymbol = normalizeReferenceSymbol(coalesceReferenceSeedValue(seed.underlyingSymbol, symbol));
  const exchangeCode = normalizeReferenceSymbol(seed.exchangeCode || "XNYS");
  const rootSymbol = normalizeReferenceSymbol(seed.rootSymbol || inferReferenceRootSymbol(symbol) || "ES");
  const fxPairCode = normalizeReferenceSymbol(seed.fxPairCode || `${currency === "USD" ? "EUR" : currency}USD`);
  const swapType = coalesceReferenceSeedValue(seed.swapType, "InterestRate");
  const commodityType = coalesceReferenceSeedValue(seed.commodityType, "Energy");
  const network = coalesceReferenceSeedValue(seed.network, "Ethereum");
  const baseCurrency = normalizeReferenceSymbol(seed.baseCurrency || (currency === "USD" ? "BTC" : currency));
  const institutionName = coalesceReferenceSeedValue(seed.institutionName, issuerName);
  const fundFamily = coalesceReferenceSeedValue(seed.fundFamily, issuerName);
  const cik = normalizeReferenceSymbol(
    seed.cik || inferCikFromIdentifier(seed.primaryIdentifierKind, seed.primaryIdentifierValue) || "789019"
  );
  const maturityFrom = seed.maturityFrom || "2026-01-01";
  const maturityTo = seed.maturityTo || "2036-12-31";
  const beforeDate = seed.beforeDate || "2031-12-31";
  const endpoint = (
    id: string,
    family: ReferenceDataEndpointFamily,
    label: string,
    path: string,
    requestLabel: string
  ): ReferenceDataEndpointDefinition => ({ id, family, label, method: "GET", path, requestLabel, probe: true });
  const mutation = (
    id: string,
    family: ReferenceDataEndpointFamily,
    label: string,
    path: string,
    requestLabel: string
  ): ReferenceDataEndpointDefinition => ({ id, family, label, method: "POST", path, requestLabel, probe: false, mutation: true });

  return [
    endpoint("bond-reference", "Bonds", "Bond reference", routeWithParam(REFERENCE_DATA_API_ENDPOINTS.bondReference, "securityId", seed.securityId), `GET bond reference for ${seed.securityId}`),
    endpoint("bond-lifecycle", "Bonds", "Bond lifecycle", routeWithParam(REFERENCE_DATA_API_ENDPOINTS.bondLifecycle, "securityId", seed.securityId), `GET bond lifecycle for ${seed.securityId}`),
    endpoint("bond-accrual-convention", "Bonds", "Bond accrual convention", routeWithParam(REFERENCE_DATA_API_ENDPOINTS.bondAccrualConvention, "securityId", seed.securityId), `GET bond accrual convention for ${seed.securityId}`),
    endpoint("bond-issuer-ladder", "Bonds", "Bond issuer ladder", withReferenceQuery(REFERENCE_DATA_API_ENDPOINTS.bondIssuerLadder, { issuerName }), `GET bond issuer ladder for ${issuerName}`),
    endpoint("bond-maturity-ladder", "Bonds", "Bond maturity ladder", withReferenceQuery(REFERENCE_DATA_API_ENDPOINTS.bondMaturityLadder, { from: maturityFrom, to: maturityTo }), `GET bond maturity ladder from ${maturityFrom} to ${maturityTo}`),
    endpoint("option-contract", "Options", "Option contract", routeWithParam(REFERENCE_DATA_API_ENDPOINTS.optionContract, "contractSymbol", optionContractSymbol), `GET option contract ${optionContractSymbol}`),
    endpoint("option-series", "Options", "Option series", withReferenceQuery(routeWithParam(REFERENCE_DATA_API_ENDPOINTS.optionSeries, "optionChainId", optionChainId), { expiryDate: optionExpiryDate }), `GET option series ${optionChainId} expiring ${optionExpiryDate}`),
    endpoint("option-underlying-linkage", "Options", "Option underlying linkage", routeWithParam(REFERENCE_DATA_API_ENDPOINTS.optionUnderlyingLinkage, "contractSymbol", optionContractSymbol), `GET underlying linkage for ${optionContractSymbol}`),
    endpoint("option-expiry-ladder", "Options", "Option expiry ladder", withReferenceQuery(REFERENCE_DATA_API_ENDPOINTS.optionExpiryLadder, { underlyingSymbol }), `GET option expiry ladder for ${underlyingSymbol}`),
    endpoint("option-chain-snapshot", "Options", "Option chain snapshot", withReferenceQuery(REFERENCE_DATA_API_ENDPOINTS.optionChainSnapshot, { underlyingSymbol, expiryDate: optionExpiryDate }), `GET option chain snapshot for ${underlyingSymbol} on ${optionExpiryDate}`),
    mutation("option-chain-import", "Options", "Option chain import", REFERENCE_DATA_API_ENDPOINTS.optionChainImport, "POST option chain import endpoint catalogued; not invoked by this read-only workbench."),
    endpoint("equity-reference", "Equities", "Equity reference", routeWithParam(REFERENCE_DATA_API_ENDPOINTS.equityReference, "securityId", seed.securityId), `GET equity reference for ${seed.securityId}`),
    endpoint("equity-by-exchange", "Equities", "Equities by exchange", withReferenceQuery(REFERENCE_DATA_API_ENDPOINTS.equityByExchange, { exchangeCode }), `GET equities listed on ${exchangeCode}`),
    endpoint("equity-by-issuer", "Equities", "Equities by issuer", withReferenceQuery(REFERENCE_DATA_API_ENDPOINTS.equityByIssuer, { issuerName }), `GET equities issued by ${issuerName}`),
    endpoint("future-reference", "Futures", "Future reference", routeWithParam(REFERENCE_DATA_API_ENDPOINTS.futureReference, "securityId", seed.securityId), `GET future reference for ${seed.securityId}`),
    endpoint("futures-by-root", "Futures", "Futures by root", withReferenceQuery(REFERENCE_DATA_API_ENDPOINTS.futuresByRoot, { rootSymbol }), `GET futures for root ${rootSymbol}`),
    endpoint("future-expiry-ladder", "Futures", "Future expiry ladder", withReferenceQuery(REFERENCE_DATA_API_ENDPOINTS.futureExpiryLadder, { rootSymbol }), `GET futures expiry ladder for ${rootSymbol}`),
    endpoint("future-front-month", "Futures", "Future front month", withReferenceQuery(REFERENCE_DATA_API_ENDPOINTS.futureFrontMonth, { rootSymbol }), `GET futures front month for ${rootSymbol}`),
    endpoint("fxspot-reference", "FX spot", "FX spot reference", routeWithParam(REFERENCE_DATA_API_ENDPOINTS.fxSpotReference, "securityId", seed.securityId), `GET FX spot reference for ${seed.securityId}`),
    endpoint("fxspot-by-pair", "FX spot", "FX spot by pair", routeWithParam(REFERENCE_DATA_API_ENDPOINTS.fxSpotByPairCode, "pairCode", fxPairCode), `GET FX spot pair ${fxPairCode}`),
    endpoint("fxspot-by-currency", "FX spot", "FX spot by currency", withReferenceQuery(REFERENCE_DATA_API_ENDPOINTS.fxSpotByCurrency, { currency }), `GET FX spot pairs involving ${currency}`),
    endpoint("swap-reference", "Swaps", "Swap reference", routeWithParam(REFERENCE_DATA_API_ENDPOINTS.swapReference, "securityId", seed.securityId), `GET swap reference for ${seed.securityId}`),
    endpoint("swaps-by-type", "Swaps", "Swaps by type", withReferenceQuery(REFERENCE_DATA_API_ENDPOINTS.swapsByType, { swapType }), `GET swaps by type ${swapType}`),
    endpoint("swaps-maturing-before", "Swaps", "Swaps maturing before", withReferenceQuery(REFERENCE_DATA_API_ENDPOINTS.swapsMaturingBefore, { beforeDate }), `GET swaps maturing before ${beforeDate}`),
    endpoint("commodity-reference", "Commodities", "Commodity reference", routeWithParam(REFERENCE_DATA_API_ENDPOINTS.commodityReference, "securityId", seed.securityId), `GET commodity reference for ${seed.securityId}`),
    endpoint("commodities-by-type", "Commodities", "Commodities by type", withReferenceQuery(REFERENCE_DATA_API_ENDPOINTS.commoditiesByType, { commodityType }), `GET commodities by type ${commodityType}`),
    endpoint("commodities-by-exchange", "Commodities", "Commodities by exchange", withReferenceQuery(REFERENCE_DATA_API_ENDPOINTS.commoditiesByExchange, { exchangeCode }), `GET commodities on ${exchangeCode}`),
    endpoint("crypto-reference", "Crypto", "Crypto reference", routeWithParam(REFERENCE_DATA_API_ENDPOINTS.cryptoReference, "securityId", seed.securityId), `GET crypto reference for ${seed.securityId}`),
    endpoint("crypto-by-network", "Crypto", "Crypto by network", withReferenceQuery(REFERENCE_DATA_API_ENDPOINTS.cryptoByNetwork, { network }), `GET crypto instruments on ${network}`),
    endpoint("crypto-by-base-currency", "Crypto", "Crypto by base currency", withReferenceQuery(REFERENCE_DATA_API_ENDPOINTS.cryptoByBaseCurrency, { baseCurrency }), `GET crypto instruments by base ${baseCurrency}`),
    endpoint("deposit-reference", "Deposits", "Deposit reference", routeWithParam(REFERENCE_DATA_API_ENDPOINTS.depositReference, "securityId", seed.securityId), `GET deposit reference for ${seed.securityId}`),
    endpoint("deposits-by-institution", "Deposits", "Deposits by institution", withReferenceQuery(REFERENCE_DATA_API_ENDPOINTS.depositsByInstitution, { institutionName }), `GET deposits by ${institutionName}`),
    endpoint("deposits-maturing-before", "Deposits", "Deposits maturing before", withReferenceQuery(REFERENCE_DATA_API_ENDPOINTS.depositsMaturingBefore, { beforeDate }), `GET deposits maturing before ${beforeDate}`),
    endpoint("money-market-fund-reference", "Money-market funds", "Money-market fund reference", routeWithParam(REFERENCE_DATA_API_ENDPOINTS.moneyMarketFundReference, "securityId", seed.securityId), `GET money-market fund reference for ${seed.securityId}`),
    endpoint("money-market-funds-by-family", "Money-market funds", "Money-market funds by family", withReferenceQuery(REFERENCE_DATA_API_ENDPOINTS.moneyMarketFundsByFamily, { fundFamily }), `GET money-market funds in ${fundFamily}`),
    endpoint("money-market-funds-by-sweep-eligibility", "Money-market funds", "Money-market funds by sweep eligibility", withReferenceQuery(REFERENCE_DATA_API_ENDPOINTS.moneyMarketFundsBySweepEligibility, { sweepEligible: true }), "GET sweep-eligible money-market funds"),
    endpoint("certificate-of-deposit-reference", "Certificates of deposit", "Certificate of deposit reference", routeWithParam(REFERENCE_DATA_API_ENDPOINTS.certificateOfDepositReference, "securityId", seed.securityId), `GET certificate of deposit reference for ${seed.securityId}`),
    endpoint("certificates-of-deposit-by-issuer", "Certificates of deposit", "Certificates of deposit by issuer", withReferenceQuery(REFERENCE_DATA_API_ENDPOINTS.certificatesOfDepositByIssuer, { issuerName }), `GET certificates of deposit issued by ${issuerName}`),
    endpoint("certificates-of-deposit-maturing-before", "Certificates of deposit", "Certificates of deposit maturing before", withReferenceQuery(REFERENCE_DATA_API_ENDPOINTS.certificatesOfDepositMaturingBefore, { beforeDate }), `GET certificates of deposit maturing before ${beforeDate}`),
    endpoint("edgar-filer", "EDGAR", "EDGAR filer", routeWithParam(REFERENCE_DATA_API_ENDPOINTS.edgarFiler, "cik", cik), `GET EDGAR filer ${cik}`),
    endpoint("edgar-facts", "EDGAR", "EDGAR facts", routeWithParam(REFERENCE_DATA_API_ENDPOINTS.edgarFacts, "cik", cik), `GET EDGAR facts for ${cik}`),
    endpoint("edgar-security-data", "EDGAR", "EDGAR security data", routeWithParam(REFERENCE_DATA_API_ENDPOINTS.edgarSecurityData, "cik", cik), `GET EDGAR security data for ${cik}`),
    mutation("edgar-ingest", "EDGAR", "EDGAR ingest", REFERENCE_DATA_API_ENDPOINTS.edgarIngest, "POST EDGAR ingest endpoint catalogued; not invoked by this read-only workbench.")
  ];
}

function withReferenceQuery(path: string, options: Record<string, string | number | boolean | null | undefined>): string {
  return `${path}${queryString(options)}`;
}

function coalesceReferenceSeedValue(...values: Array<string | null | undefined>): string {
  for (const value of values) {
    const trimmed = value?.trim();
    if (trimmed) {
      return trimmed;
    }
  }

  return "";
}

function normalizeReferenceSymbol(value: string): string {
  const normalized = value.replace(/[^A-Za-z0-9]/g, "").toUpperCase();
  return normalized || "AAPL";
}

function inferReferenceRootSymbol(symbol: string): string | null {
  const match = symbol.match(/^[A-Z]+/);
  return match?.[0] ?? null;
}

function inferCikFromIdentifier(kind: string | null | undefined, value: string | null | undefined): string | null {
  if (kind?.toLowerCase() !== "cik") {
    return null;
  }

  const normalized = value?.replace(/\D/g, "");
  return normalized || null;
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
