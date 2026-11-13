import {
  getReconciliationBreakQueue,
  getReconciliationCalibrationSummary,
  getReconciliationStatementRun,
  getReconciliationStatementRuns,
  getRunReviewPacketPath,
  getRunTrialBalance,
  previewInvestmentAccountingTransaction,
  resolveReconciliationBreak,
  reviewReconciliationBreak,
  runAnalysisExport,
  type AccountingReportPackageHistoryQuery,
  type CapitalAccountWorkbenchQuery,
  type ManualJournalEntryWorkbenchQuery
} from "@/lib/api";
import { describeApiError, type ApiErrorDisplay } from "@/lib/api-errors";
import { ACTIVATION_OUTCOME_KEYS, recordActivationOutcome } from "@/lib/first-run/activation";
import { formatCount as formatCountValue } from "@/lib/format";
import { formatReportPackRecipientList } from "@/lib/reporting-distributions";
import { WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";
import { EXPORT_API_ENDPOINTS } from "@/lib/workstation-endpoints";
import type {
  AccountingBasisKind,
  AccountingCashFlowSummary,
  AccountingConfigurationWorkspace,
  AccountingJournalTemplatePreview,
  AccountingMigrationRunArtifactList,
  AccountingMigrationRunWorkerPlanList,
  AccountingProductionCertificationProfile,
  AccountingProductionCertificationProfileUpsertRequest,
  AccountingProductionReadiness,
  AccountingProductionReadinessRequest,
  AccountingReportPackageBundle,
  AccountingReportPackageRequest,
  AccountingReportingProfile,
  AccountingReportingSummary,
  AccountingRuleTestSuiteResult,
  AccountingSystemMappingProfileUpsertRequest,
  AccountingSystemReconciliationSummary,
  AccountingTemplateLineSide,
  AccountingTenantAdministrationProfile,
  AccountingTenantAdministrationProfileUpsertRequest,
  AccountingWorkspaceResponse,
  ActivateAccountingConfigurationRequest,
  ApprovePostingRulePromotionRequest,
  AttachManualJournalEntryEvidenceRequest,
  CapitalAccountWorkbench,
  CertifyAccountingReportPackageRequest,
  ClosePeriodLockResult,
  ClosePeriodPlan,
  CreateLateAdjustmentRequest,
  CreateLedgerBookRequest,
  ExecuteAccountingRuleTestCasesRequest,
  ExportAnalysisResult,
  ExternalGlMappingProfile,
  InvestmentAccountingTransactionLabPreview,
  InvestmentAccountingTransactionLabRequest,
  JournalEntryLifecycleAction,
  JournalEntryLifecycleActionRequest,
  JournalEntryLifecycleActionResult,
  LedgerBook,
  LedgerDimensionSet,
  LedgerJournalLine,
  LedgerTrialBalanceLine,
  LockClosePeriodRequest,
  ManualJournalEntryDraft,
  ManualJournalEntryLine,
  ManualJournalEntryWorkbench,
  PostingRuleJournalCandidateRequest,
  PostingRuleJournalCandidateResult,
  PreviewJournalTemplateRequest,
  ReconciliationBreakQueueItem,
  ReconciliationCalibrationSummary,
  ReportExportArtifactManifest,
  ResolveReconciliationBreakRequest,
  ReviewCloseEvidenceRequest,
  ReviewLateAdjustmentRequest,
  RuleDryRunRequest,
  RuleDryRunResult,
  SaveManualJournalEntryDraftRequest,
  SecurityMasterEntry,
  SignOffCloseTaskRequest,
  StatementRunSummary,
  SubmitManualJournalEntryApprovalRequest,
  UpsertAccountingRuleTestCaseRequest,
  UpsertChartOfAccountsNodeRequest,
  UpsertClosePeriodPlanConfigurationRequest,
  UpsertPostingRuleRequest,
  ValidateManualJournalEntryDraftRequest
} from "@/types";
import {
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState
} from "react";
import { buildCalibrationSummaryViewState } from "./accounting-calibration-summary.view-model";
import type { AssetAccountingWorkflowViewModel } from "./accounting-screen.asset-workflow";
import { accountingBasisDisplayName, buildBasisBridgeViewState } from "./accounting-screen.basis-bridge.view-model";
import {
  formatBytes,
  formatCount,
  formatCurrency,
  formatCurrencyForCode,
  formatDateTimeLabel,
  formatSignedCurrency,
  toDomId
} from "./accounting-screen.formatting";
import { areReconciliationBreakQueuesEquivalent, replaceBreakQueueItem } from "./accounting-screen.reconciliation-queue-utils";
import {
  buildOperationalExceptionWorkbenchState,
  buildReconciliationBreakQueueState,
  buildReconciliationComparisonViewState,
  buildReconciliationDetailActions,
  buildReconciliationDetailViewState,
  buildReconciliationQueuePanelViewState,
  buildReconciliationStatementRunsViewState,
  resolveSelectedReconciliation,
  sortStatementRunsNewestFirst
} from "./accounting-screen.reconciliation.view-model";
import { buildAccountingTaskMode, type AccountingTaskModeViewModel, type AccountingWorkstream } from "./accounting-screen.task-mode-view-model";
import { DEFAULT_ACCOUNTING_BASIS, normalizeApiErrorDisplay } from "./accounting-screen.view-model.shared";
import { requireSuccessfulReconciliationCasework, type AccountingReconciliationServices } from "./reconciliation-casework-outcome";
import type { ReconciliationSelectionState } from "./reconciliation-view-state";

export { useCapitalAccountWorkbenchViewModel } from "./accounting-screen.capital-accounts.view-model";
export { buildAccountingWorkflowLaunchViewState, buildCloseCommandCenterViewState, useAccountingCloseReportPackageViewModel } from "./accounting-screen.close-cockpit.view-model";
export { useAccountingConfigurationViewModel } from "./accounting-screen.governance.view-model";
export { useManualJournalEntryWorkbenchViewModel } from "./accounting-screen.journal-entries.view-model";
export {
  buildOperationalExceptionWorkbenchState,
  buildReconciliationBreakQueueState,
  buildReconciliationBreakRows,
  buildReconciliationComparisonViewState,
  buildReconciliationDetailActions,
  buildReconciliationDetailViewState,
  buildReconciliationNarrative,
  buildReconciliationQueuePanelViewState,
  buildReconciliationResolveDialogState,
  buildReconciliationStatementRunsViewState,
  financeBreakLabel,
  resolveSelectedReconciliation
} from "./accounting-screen.reconciliation.view-model";
export {
  SECURITY_IDENTITY_DETAIL_PANEL_ID,
  buildSecurityConflictRefreshCommand,
  buildSecurityConflictRows,
  buildSecurityIdentityDrillInState,
  buildSecurityMasterPageViewState,
  buildSecuritySearchResultRows,
  buildSecuritySearchState,
  countOpenSecurityConflicts,
  useSecurityMasterViewModel
} from "./accounting-screen.security-master";
export type {
  SecurityConflictActionViewModel,
  SecurityConflictRefreshCommandViewModel,
  SecurityConflictResolution,
  SecurityConflictRowViewModel,
  SecurityIdentityAliasRowViewModel,
  SecurityIdentityDrillInViewState,
  SecurityIdentityIdentifierRowViewModel,
  SecurityIdentitySummaryFieldViewModel,
  SecurityMasterCoveragePostureViewModel,
  SecurityMasterDetailSectionViewModel,
  SecurityMasterDrillInServices,
  SecurityMasterPageMetricTone,
  SecurityMasterPageMetricViewModel,
  SecurityMasterPageViewState,
  SecurityMasterServices,
  SecuritySearchResultColumnViewModel,
  SecuritySearchResultRowViewModel,
  SecuritySearchState
} from "./accounting-screen.security-master";
export {
  buildCorporateActionRows,
  buildCorporateActionsViewState,
  buildInstrumentPassportViewState,
  buildReferenceDataWorkbenchViewState,
  buildSecurityOpenLotReadModelViewState,
  buildSecurityOpenLotRows,
  buildSecurityScheduleRows,
  buildSecuritySchedulesViewState,
  buildTradingParametersViewState,
  mapScheduleBookToCashFlowScheduleEvents,
  resolveSecurityScheduleEvents
} from "./accounting-screen.security-projections";
export type {
  CorporateActionDetailFieldViewModel,
  CorporateActionDetailViewState,
  CorporateActionRowViewModel,
  CorporateActionsViewState,
  InstrumentPassportFieldViewModel,
  InstrumentPassportOperationsReadinessViewModel,
  InstrumentPassportOperationsWorkbenchItemViewModel,
  InstrumentPassportOperationsWorkbenchPanelViewModel,
  InstrumentPassportProviderConfidenceRowViewModel,
  InstrumentPassportViewState,
  ReferenceDataEndpointDetailViewState,
  ReferenceDataEndpointRowViewModel,
  ReferenceDataWorkbenchMetricViewModel,
  ReferenceDataWorkbenchViewState,
  SecurityCashFlowScheduleEvent,
  SecurityOpenLotDetailViewState,
  SecurityOpenLotReadModelViewState,
  SecurityOpenLotRowViewModel,
  SecurityScheduleDetailFieldViewModel,
  SecurityScheduleDetailViewState,
  SecurityScheduleEventType,
  SecurityScheduleFamily,
  SecuritySchedulePostingStatus,
  SecurityScheduleRowViewModel,
  SecurityScheduleToolbarItemViewModel,
  SecuritySchedulesViewState,
  TradingParametersField,
  TradingParametersViewState
} from "./accounting-screen.security-projections";

export {
buildCalibrationSummaryViewState
};

export type {
  CalibrationProfileDetailViewModel,
  CalibrationProfileRowViewModel,
  CalibrationStatusIcon,
  CalibrationStatusTone,
  CalibrationSummaryMetricViewModel,
  CalibrationSummaryRefreshCommandViewModel,
  CalibrationSummaryViewModel,
  CalibrationSummaryViewState
} from "./accounting-calibration-summary.view-model";
export { accountingWorkstreamHref, buildAccountingTaskMode, resolveAccountingWorkstream, resolveGovernanceWorkstream } from "./accounting-screen.task-mode-view-model";
export type { AccountingTaskModeId, AccountingTaskModeViewModel, AccountingWorkstream, GovernanceWorkstream } from "./accounting-screen.task-mode-view-model";

export type ReconciliationBreakCommand = "assign" | "resolve" | "dismiss";
export { useReconciliationResolveDialogViewModel } from "./accounting-screen.reconciliation-resolve-dialog";
export type { ReconciliationBreakResolutionStatus, ReconciliationResolveDialogState, ReconciliationResolveDialogViewModel } from "./accounting-screen.reconciliation-resolve-dialog";

export type { AccountingReconciliationServices } from "./reconciliation-casework-outcome";

export interface AccountingReportingServices {
  runAnalysisExport: (profileId: string) => Promise<ExportAnalysisResult>;
}

export interface AccountingConfigurationServices {
  getConfiguration: () => Promise<AccountingConfigurationWorkspace>;
  assessProductionReadiness: (request: AccountingProductionReadinessRequest) => Promise<AccountingProductionReadiness>;
  listMigrationRunArtifacts: (query: { fundProfileId?: string | null; ledgerBookId?: string | null }) => Promise<AccountingMigrationRunArtifactList>;
  listMigrationWorkerPlans: (query: { fundProfileId?: string | null; ledgerBookId?: string | null }) => Promise<AccountingMigrationRunWorkerPlanList>;
  listExternalGlMappingProfiles: (query: { providerId?: string | null; fundProfileId?: string | null; ledgerBookId?: string | null }) => Promise<ExternalGlMappingProfile[]>;
  upsertExternalGlMappingProfile: (request: AccountingSystemMappingProfileUpsertRequest) => Promise<ExternalGlMappingProfile>;
  getProductionCertificationProfile: (query: { tenantId?: string | null; companyId?: string | null; fundProfileId?: string | null; ledgerBookId?: string | null }) => Promise<AccountingProductionCertificationProfile>;
  upsertProductionCertificationProfile: (request: AccountingProductionCertificationProfileUpsertRequest) => Promise<AccountingProductionCertificationProfile>;
  getTenantAdministrationProfile: (query: { tenantId?: string | null; companyId?: string | null }) => Promise<AccountingTenantAdministrationProfile>;
  upsertTenantAdministrationProfile: (request: AccountingTenantAdministrationProfileUpsertRequest) => Promise<AccountingTenantAdministrationProfile>;
  createLedgerBook: (request: CreateLedgerBookRequest) => Promise<LedgerBook>;
  previewTemplate: (request: PreviewJournalTemplateRequest) => Promise<AccountingJournalTemplatePreview>;
  upsertChartNode: (request: UpsertChartOfAccountsNodeRequest) => Promise<AccountingConfigurationWorkspace>;
  upsertRule: (request: UpsertPostingRuleRequest) => Promise<AccountingConfigurationWorkspace>;
  dryRunRule: (request: RuleDryRunRequest) => Promise<RuleDryRunResult>;
  buildJournalCandidate: (request: PostingRuleJournalCandidateRequest) => Promise<PostingRuleJournalCandidateResult>;
  runRuleTests: (request: ExecuteAccountingRuleTestCasesRequest) => Promise<AccountingRuleTestSuiteResult>;
  saveRuleTestCase: (request: UpsertAccountingRuleTestCaseRequest) => Promise<AccountingConfigurationWorkspace>;
  approveRulePromotion: (request: ApprovePostingRulePromotionRequest) => Promise<AccountingConfigurationWorkspace>;
  activate: (request: ActivateAccountingConfigurationRequest) => Promise<AccountingConfigurationWorkspace>;
}

export type GovernanceReconciliationServices = AccountingReconciliationServices;
export type GovernanceReportingServices = AccountingReportingServices;

export interface AccountingConfigurationMetricViewModel {
  id: string;
  label: string;
  value: string;
  detail: string;
  tone: "default" | "success" | "warning" | "danger";
}

export interface AccountingConfigurationTemplateViewModel {
  id: string;
  title: string;
  subtitle: string;
  lineCountLabel: string;
  balanceLabel: string;
  statusLabel: string;
}

export interface AccountingRulesStudioRuleViewModel {
  id: string;
  title: string;
  subtitle: string;
  eventLabel: string;
  effectiveLabel: string;
  priorityLabel: string;
  scopeLabels: string[];
  conditionRows: string[];
  formulaRows: string[];
  allocationRows: string[];
  generatedPostingRows: string[];
  versionRows: string[];
  promotionReadiness: AccountingRulesStudioPromotionReadinessViewModel[];
  promotionLabel: string;
  promotionTone: "success" | "warning" | "danger" | "outline";
  statusLabel: string;
  statusTone: "success" | "warning" | "danger" | "outline";
  isSelected: boolean;
  selectAriaLabel: string;
}

export interface AccountingRulesStudioPromotionReadinessViewModel {
  id: string;
  label: string;
  value: string;
  detail: string;
  tone: AccountingToolingTone;
}

export interface AccountingRulesStudioDryRunViewModel {
  title: string;
  balanceLabel: string;
  selectedRuleLabel: string;
  matchRows: string[];
  generatedLineRows: string[];
  generatedPostingRows: string[];
  validationRows: AccountingConfigurationIssueViewModel[];
}

export interface AccountingRulesStudioJournalCandidateViewModel {
  title: string;
  selectedRuleLabel: string;
  balanceLabel: string;
  commandLabel: string;
  approvalLabel: string;
  evidenceLabel: string;
  generatedLineRows: string[];
  issueRows: AccountingConfigurationIssueViewModel[];
}

export interface AccountingRulesStudioTestSuiteViewModel {
  title: string;
  summaryLabel: string;
  executedLabel: string;
  statusTone: "success" | "warning" | "danger";
  resultRows: string[];
  validationRows: AccountingConfigurationIssueViewModel[];
}

export interface AccountingRulesStudioTestCaseViewModel {
  id: string;
  title: string;
  subtitle: string;
  assertionLabel: string;
  evidenceLabel: string;
  evidenceTone: "success" | "warning";
}

export interface AccountingConfigurationIssueViewModel {
  id: string;
  label: string;
  message: string;
  detail: string;
  tone: "default" | "warning" | "danger";
  targetId?: string | null; severity?: "Critical" | "Warning" | "Info";
}

export interface AccountingConfigurationAuditViewModel {
  id: string;
  title: string;
  subtitle: string;
  hashLabel: string;
}

export interface AccountingProductionReadinessComponentViewModel {
  id: string;
  label: string;
  statusLabel: string;
  scoreLabel: string;
  summary: string;
  issueCountLabel: string;
  evidenceLabel: string;
  routeLabel: string;
  tone: "default" | "success" | "warning" | "danger";
}

export interface AccountingProductionReadinessIssueViewModel {
  id: string;
  label: string;
  message: string;
  suggestedAction: string;
  evidenceLabel: string;
  tone: "default" | "warning" | "danger";
}

export interface AccountingProductionGapViewModel {
  id: string;
  label: string;
  statusLabel: string;
  severityLabel: string;
  summary: string;
  requiredAction: string;
  areaLabel: string;
  blockingIssueLabel: string;
  issueDetailLabel: string;
  routeLabel: string;
  tone: "default" | "success" | "warning" | "danger";
}

export interface AccountingMigrationRunArtifactViewModel {
  id: string;
  title: string;
  detail: string;
  statusLabel: string;
  kindLabel: string;
  recordCountLabel: string;
  issueCountLabel: string;
  evidenceLabel: string;
  tone: "default" | "success" | "warning" | "danger";
}

export interface AccountingMigrationRolloutPlanItemViewModel {
  id: string;
  title: string;
  statusLabel: string;
  certificationLabel: string;
  scopeLabel: string;
  latestRunLabel: string;
  metricsLabel: string;
  evidenceLabel: string;
  requiredAction: string;
  blockingIssueLabel: string;
  tone: "default" | "success" | "warning" | "danger";
}

export interface AccountingMigrationWorkerPlanViewModel {
  id: string;
  title: string;
  detail: string;
  kindLabel: string;
  countLabel: string;
  evidenceLabel: string;
  scopeLabel: string;
  tone: "default" | "success" | "warning" | "danger";
}

export interface AccountingTenantAdministrationControlViewModel {
  id: string;
  label: string;
  statusLabel: string;
  tone: "default" | "success" | "warning" | "danger";
}

export interface AccountingProductionReadinessViewModel {
  title: string;
  statusLabel: string;
  scoreLabel: string;
  generatedAtLabel: string;
  scopeLabel: string;
  issueSummaryLabel: string;
  externalGlLabel: string;
  ledgerBookRolloutLabel: string;
  dimensionalReportingLabel: string;
  dimensionalReportingEvidenceLabel: string;
  tenantAdministrationLabel: string;
  tenantAdministrationEvidenceLabel: string;
  tenantAdministrationControls: AccountingTenantAdministrationControlViewModel[];
  migrationPlanRows: AccountingMigrationRolloutPlanItemViewModel[];
  migrationArtifactSummaryLabel: string;
  migrationArtifactRows: AccountingMigrationRunArtifactViewModel[];
  migrationWorkerPlanSummaryLabel: string;
  migrationWorkerPlanRows: AccountingMigrationWorkerPlanViewModel[];
  productionGapRows: AccountingProductionGapViewModel[];
  components: AccountingProductionReadinessComponentViewModel[];
  blockerIssues: AccountingProductionReadinessIssueViewModel[];
  loading: boolean;
  errorText: string | null;
  errorDetails: string[];
}

export interface AccountingTenantAdministrationProfileControlEditViewModel {
  id: string;
  label: string;
  description: string;
  checked: boolean;
}

export interface AccountingTenantAdministrationProfileEditorViewModel {
  title: string;
  scopeLabel: string;
  updatedLabel: string;
  evidenceValue: string;
  controls: AccountingTenantAdministrationProfileControlEditViewModel[];
  approvalQueueSetup: AccountingApprovalQueueSetupEditorViewModel;
  dimensionMappingSetup: AccountingDimensionMappingSetupEditorViewModel;
  saveButtonLabel: string;
  saveDisabledReason: string | null;
  saveBusy: boolean;
  canSave: boolean;
  statusText: string | null;
  errorText: string | null;
  errorDetails: string[];
  sandboxButtonLabel: string;
  sandboxDisabledReason: string | null;
  sandboxBusy: boolean;
  sandboxStatusText: string | null;
  canRetainSandboxProof: boolean;
  updateControl: (controlId: string, checked: boolean) => void;
  updateApprovalQueueSetup: (patch: Partial<AccountingApprovalQueueSetupDraft>) => void;
  updateDimensionMappingSetup: (patch: Partial<AccountingDimensionMappingSetupDraft>) => void;
  updateEvidence: (value: string) => void;
  retainSandboxProof: () => Promise<void>;
  save: () => Promise<void>;
}

export interface AccountingApprovalQueueSetupEditorViewModel {
  queueIdValue: string;
  displayNameValue: string;
  workflowKindValue: string;
  requiredApprovalRoleValue: string;
  requiredApprovalCountValue: string;
  segregationPolicyValue: string;
  evidenceRequirementValue: string;
}

export interface AccountingDimensionMappingSetupEditorViewModel {
  mappingIdValue: string;
  displayNameValue: string;
  providerIdValue: string;
  meridianDimensionsValue: string;
  providerDimensionsValue: string;
  evidenceRequirementValue: string;
}

export interface AccountingExternalGlMappingProfileEditorViewModel {
  title: string;
  scopeLabel: string;
  providerIdValue: string;
  profileIdValue: string;
  displayNameValue: string;
  accountMappingsValue: string;
  meridianDimensionsValue: string;
  externalDimensionsValue: string;
  evidenceValue: string;
  certified: boolean;
  mappingRows: AccountingTenantAdministrationControlViewModel[];
  saveButtonLabel: string;
  saveDisabledReason: string | null;
  saveBusy: boolean;
  canSave: boolean;
  statusText: string | null;
  errorText: string | null;
  errorDetails: string[];
  updateProviderId: (value: string) => void;
  updateProfileId: (value: string) => void;
  updateDisplayName: (value: string) => void;
  updateAccountMappings: (value: string) => void;
  updateMeridianDimensions: (value: string) => void;
  updateExternalDimensions: (value: string) => void;
  updateEvidence: (value: string) => void;
  updateCertified: (checked: boolean) => void;
  save: () => Promise<void>;
}

export interface AccountingProductionCertificationProfileControlEditViewModel {
  id: string;
  label: string;
  description: string;
  checked: boolean;
}

export interface AccountingProductionCertificationProfileEditorViewModel {
  title: string;
  scopeLabel: string;
  updatedLabel: string;
  evidenceValue: string;
  controls: AccountingProductionCertificationProfileControlEditViewModel[];
  saveButtonLabel: string;
  saveDisabledReason: string | null;
  saveBusy: boolean;
  canSave: boolean;
  statusText: string | null;
  errorText: string | null;
  errorDetails: string[];
  updateControl: (controlId: string, checked: boolean) => void;
  updateEvidence: (value: string) => void;
  save: () => Promise<void>;
}

export interface AccountingTenantAdministrationProfileDraft {
  tenantScopeConfigured: boolean;
  adminRoleProfileConfigured: boolean;
  scopedAccessPoliciesConfigured: boolean;
  reportingGroupsConfigured: boolean;
  accountingAdminSurfaceConfigured: boolean;
  browserAccountingAdminSurfaceConfigured: boolean;
  wpfAccountingAdminSurfaceConfigured: boolean;
  chartAdministrationStudioConfigured: boolean;
  ruleTestPromotionStudioConfigured: boolean;
  closeSetupStudioConfigured: boolean;
  providerMappingStudioConfigured: boolean;
  tenantCompanyReportGroupSetupStudioConfigured: boolean;
  auditReviewToolingConfigured: boolean;
  bulkImportExportSafeguardsConfigured: boolean;
  performanceValidationConfigured: boolean;
  disasterRecoveryRunbookConfigured: boolean;
  ledgerBookAdministrationStudioConfigured: boolean;
  postingRuleAuthoringStudioConfigured: boolean;
  approvalQueueStudioConfigured: boolean;
  approvalQueueSetup: AccountingApprovalQueueSetupDraft;
  dimensionMappingStudioConfigured: boolean;
  dimensionMappingSetup: AccountingDimensionMappingSetupDraft;
  implementationSandboxConfigured: boolean;
  evidenceText: string;
}

export interface AccountingApprovalQueueSetupDraft {
  queueId: string;
  displayName: string;
  workflowKind: string;
  requiredApprovalRole: string;
  requiredApprovalCount: string;
  segregationPolicy: string;
  evidenceRequirement: string;
}

export interface AccountingDimensionMappingSetupDraft {
  mappingId: string;
  displayName: string;
  providerId: string;
  meridianDimensionsText: string;
  providerDimensionsText: string;
  evidenceRequirement: string;
}

export interface AccountingProductionCertificationProfileDraft {
  postingRulesLedgerBookNativeCertified: boolean;
  journalLifecycleLedgerBookNativeCertified: boolean;
  closeReportingLedgerBookNativeCertified: boolean;
  closePlanConfigurationLedgerBookNativeCertified: boolean;
  externalGlLedgerBookNativeCertified: boolean;
  reconciliationLedgerBookNativeCertified: boolean;
  directLendingLedgerBookNativeCertified: boolean;
  strategyLedgerReadLedgerBookNativeCertified: boolean;
  periodReportDimensionQueriesCertified: boolean;
  crossPeriodReportDimensionQueriesCertified: boolean;
  journalQueryDimensionFiltersCertified: boolean;
  externalExportDimensionMappingCertified: boolean;
  ledgerLineDimensionsPersistedCertified: boolean;
  trialBalanceDimensionFiltersCertified: boolean;
  reportPackageDimensionProvenanceCertified: boolean;
  evidenceText: string;
}

export interface AccountingExternalGlMappingProfileDraft {
  providerId: string;
  profileId: string;
  displayName: string;
  accountMappingsText: string;
  meridianDimensionsText: string;
  externalDimensionsText: string;
  evidenceText: string;
  certified: boolean;
}

export interface AccountingChartAccountDraft {
  nodeId: string;
  path: string;
  accountName: string;
  accountType: string;
  parentPath: string;
  financialAccountId: string;
  evidenceText: string;
}

export interface AccountingConfigurationPreviewViewModel {
  title: string;
  balanceLabel: string;
  statusLabel: string;
  lineRows: Array<{
    id: string;
    account: string;
    side: string;
    amount: string;
    description: string;
  }>;
}

export interface AccountingLedgerBookAdminRowViewModel {
  id: string;
  title: string;
  subtitle: string;
  scopeLabel: string;
  policyLabel: string;
  currencyLabel: string;
  updatedLabel: string;
  description: string;
  statusLabel: string;
  tone: "default" | "success" | "warning" | "danger";
}

export interface AccountingChartAccountEditorViewModel {
  nodeIdValue: string;
  pathValue: string;
  accountNameValue: string;
  accountTypeValue: string;
  parentPathValue: string;
  financialAccountIdValue: string;
  evidenceValue: string;
  saveButtonLabel: string;
  saveDisabledReason: string | null;
  statusText: string | null;
  saveBusy: boolean;
  canSave: boolean;
  updateDraft: (patch: Partial<AccountingChartAccountDraft>) => void;
  save: () => Promise<void>;
}

export interface AccountingConfigurationViewModel {
  title: string;
  description: string;
  statusLabel: string;
  statusTone: "default" | "success" | "warning" | "danger";
  loading: boolean;
  errorText: string | null;
  errorDetails: string[];
  metricRows: AccountingConfigurationMetricViewModel[];
  setupReadinessRows: AccountingConfigurationMetricViewModel[];
  ledgerBookRows: AccountingLedgerBookAdminRowViewModel[];
  ledgerBookSummaryLabel: string;
  ledgerBookEmptyText: string | null;
  chartAccountEditor: AccountingChartAccountEditorViewModel;
  productionReadiness: AccountingProductionReadinessViewModel;
  productionCertificationProfile: AccountingProductionCertificationProfileEditorViewModel;
  tenantAdministrationProfile: AccountingTenantAdministrationProfileEditorViewModel;
  externalGlMappingProfile: AccountingExternalGlMappingProfileEditorViewModel;
  templates: AccountingConfigurationTemplateViewModel[];
  rules: AccountingRulesStudioRuleViewModel[];
  selectedRule: AccountingRulesStudioRuleViewModel | null;
  selectedRuleId: string | null;
  selectRule: (ruleId: string) => void;
  dryRunPreview: AccountingRulesStudioDryRunViewModel | null;
  dryRunStatusText: string | null;
  dryRunButtonLabel: string;
  dryRunDisabledReason: string | null;
  dryRunBusy: boolean;
  canDryRun: boolean;
  dryRunSelectedRule: () => Promise<void>;
  createLedgerBookButtonLabel: string;
  createLedgerBookDisabledReason: string | null;
  createLedgerBookStatusText: string | null;
  createLedgerBookBusy: boolean;
  canCreateLedgerBook: boolean;
  createLedgerBookFromSetupCandidate: () => Promise<void>;
  journalCandidatePreview: AccountingRulesStudioJournalCandidateViewModel | null;
  journalCandidateStatusText: string | null;
  journalCandidateButtonLabel: string;
  journalCandidateDisabledReason: string | null;
  journalCandidateBusy: boolean;
  canBuildJournalCandidate: boolean;
  buildJournalCandidate: () => Promise<void>;
  applyEventPredicateButtonLabel: string;
  applyEventPredicateDisabledReason: string | null;
  applyEventPredicateStatusText: string | null;
  applyEventPredicateBusy: boolean;
  canApplyEventPredicate: boolean;
  applyDryRunEventPredicate: () => Promise<void>;
  applyThresholdButtonLabel: string;
  applyThresholdDisabledReason: string | null;
  applyThresholdStatusText: string | null;
  applyThresholdBusy: boolean;
  canApplyThreshold: boolean;
  applyDryRunAmountThreshold: () => Promise<void>;
  applyEffectiveStartButtonLabel: string;
  applyEffectiveStartDisabledReason: string | null;
  applyEffectiveStartStatusText: string | null;
  applyEffectiveStartBusy: boolean;
  canApplyEffectiveStart: boolean;
  applyDryRunEffectiveStart: () => Promise<void>;
  applyScopeButtonLabel: string;
  applyScopeDisabledReason: string | null;
  applyScopeStatusText: string | null;
  applyScopeBusy: boolean;
  canApplyScope: boolean;
  applyDryRunScope: () => Promise<void>;
  capturePostingsButtonLabel: string;
  capturePostingsDisabledReason: string | null;
  capturePostingsStatusText: string | null;
  capturePostingsBusy: boolean;
  canCapturePostings: boolean;
  captureDryRunGeneratedPostings: () => Promise<void>;
  applyFormulaButtonLabel: string;
  applyFormulaDisabledReason: string | null;
  applyFormulaStatusText: string | null;
  applyFormulaBusy: boolean;
  canApplyFormula: boolean;
  applyDryRunFormulaAmount: () => Promise<void>;
  applyAllocationButtonLabel: string;
  applyAllocationDisabledReason: string | null;
  applyAllocationStatusText: string | null;
  applyAllocationBusy: boolean;
  canApplyAllocation: boolean;
  applyDryRunAllocationTargets: () => Promise<void>;
  raisePriorityButtonLabel: string;
  raisePriorityDisabledReason: string | null;
  raisePriorityStatusText: string | null;
  raisePriorityBusy: boolean;
  canRaisePriority: boolean;
  raiseSelectedRulePriority: () => Promise<void>;
  ruleTestSuite: AccountingRulesStudioTestSuiteViewModel | null;
  ruleTestStatusText: string | null;
  ruleTestButtonLabel: string;
  ruleTestDisabledReason: string | null;
  ruleTestBusy: boolean;
  canRunRuleTests: boolean;
  runRuleTests: () => Promise<void>;
  saveDryRunAsRuleTestButtonLabel: string;
  saveDryRunAsRuleTestDisabledReason: string | null;
  saveDryRunAsRuleTestStatusText: string | null;
  saveDryRunAsRuleTestBusy: boolean;
  canSaveDryRunAsRuleTest: boolean;
  saveDryRunAsRuleTest: () => Promise<void>;
  duplicateRuleButtonLabel: string;
  duplicateRuleDisabledReason: string | null;
  duplicateRuleStatusText: string | null;
  duplicateRuleBusy: boolean;
  canDuplicateRule: boolean;
  duplicateSelectedRule: () => Promise<void>;
  archiveRuleButtonLabel: string;
  archiveRuleDisabledReason: string | null;
  archiveRuleStatusText: string | null;
  archiveRuleBusy: boolean;
  canArchiveRule: boolean;
  archiveSelectedRule: () => Promise<void>;
  approveRulePromotionButtonLabel: string;
  approveRulePromotionDisabledReason: string | null;
  approveRulePromotionStatusText: string | null;
  approveRulePromotionBusy: boolean;
  canApproveRulePromotion: boolean;
  approveRulePromotion: () => Promise<void>;
  ruleTestCases: AccountingRulesStudioTestCaseViewModel[];
  validationIssues: AccountingConfigurationIssueViewModel[];
  auditTrail: AccountingConfigurationAuditViewModel[];
  preview: AccountingConfigurationPreviewViewModel | null;
  previewStatusText: string | null;
  previewButtonLabel: string;
  previewDisabledReason: string | null;
  previewBusy: boolean;
  canPreview: boolean;
  activateButtonLabel: string;
  activateDisabledReason: string | null;
  activateBusy: boolean;
  canActivate: boolean;
  activate: () => Promise<void>;
  emptyText: string;
  refresh: () => Promise<void>;
  previewFirstTemplate: () => Promise<void>;
}

export interface ReconciliationBreakAction {
  breakId: string;
  command: ReconciliationBreakCommand;
}

export interface ReconciliationBreakRowViewModel extends ReconciliationBreakQueueItem {
  actionBusy: boolean;
  financeLabel: string;
  varianceLabel: string;
  varianceTone: "default" | "success" | "danger";
  statusBadgeVariant: "success" | "warning" | "outline" | "danger";
  detectedAtLabel: string;
  lastUpdatedAtLabel: string;
  ownerLabel: string;
  rowAriaLabel: string;
  rowSelectAriaLabel: string;
  detailPanelId: string;
  isSelected: boolean;
  isExpanded: boolean;
  assignLabel: string;
  resolveLabel: string;
  dismissLabel: string;
  assignAriaLabel: string;
  resolveAriaLabel: string;
  dismissAriaLabel: string;
  canAssign: boolean;
  canResolve: boolean;
  canDismiss: boolean;
  assignDisabledReason: string | null;
  resolveDisabledReason: string | null;
  dismissDisabledReason: string | null;
}

export interface ReconciliationBreakDetailFieldViewModel {
  label: string;
  value: string;
}

export interface ReconciliationBreakDetailViewModel {
  id: string;
  eyebrow: string;
  title: string;
  subtitle: string;
  rawCategoryLabel: string;
  description: string;
  ariaLabel: string;
  statusLabel: string;
  statusBadgeVariant: "success" | "warning" | "outline" | "danger";
  fields: ReconciliationBreakDetailFieldViewModel[];
  analysisText: string | null;
  recommendedActionText: string | null;
  routingActionLabel: string | null;
  routingActionHref: string | null;
  routingActionAriaLabel: string | null;
}

export interface ReconciliationBreakQueueState {
  rows: ReconciliationBreakRowViewModel[];
  hasBreaks: boolean;
  tableLabel: string;
  tableCaption: string;
  detailPanelId: string;
  selectedBreakId: string | null;
  selectedDetail: ReconciliationBreakDetailViewModel | null;
  detailEmptyTitle: string;
  detailEmptyText: string;
  detailEmptyAriaLabel: string;
  loadingText: string | null;
  emptyText: string;
  errorText: string | null;
  errorDetails: string[];
  actionErrorText: string | null;
  actionErrorDetails: string[];
  statusAnnouncement: string;
}

export interface ReconciliationDetailActionsViewModel {
  breakChecklistTargetId: string;
  breakChecklistHref: string;
  breakChecklistLabel: string;
  breakChecklistAriaLabel: string;
  evidencePacketHref: string;
  evidencePacketLabel: string;
  evidencePacketAriaLabel: string;
  auditPacketHref: string;
  auditPacketLabel: string;
  auditPacketAriaLabel: string;
}

export type CashFlowEvidenceTone = "default" | "success" | "warning" | "danger";

export interface ReconciliationDetailFieldViewModel {
  label: string;
  value: string;
  tone: CashFlowEvidenceTone;
  ariaLabel: string;
}

export interface ReconciliationDetailViewState {
  eyebrow: string;
  title: string;
  description: string;
  ariaLabel: string;
  narrative: string;
  narrativeLabel: string;
  fields: ReconciliationDetailFieldViewModel[];
}

export type ReconciliationQueueRunTone = "muted" | "warning" | "success" | "primary";

export interface ReconciliationQueueRunRowViewModel {
  runId: string;
  strategyName: string;
  modeLabel: string;
  runStatusLabel: string;
  reconciliationStatusLabel: string;
  reconciliationTone: ReconciliationQueueRunTone;
  breakCountLabel: string;
  openBreakLabel: string;
  lastUpdatedLabel: string;
  isSelected: boolean;
  isExpanded: boolean;
  controlsId: string;
  ariaLabel: string;
  selectAriaLabel: string;
}

export interface ReconciliationQueuePanelViewState {
  title: string;
  description: string;
  overviewTitle: string;
  overviewDescription: string;
  overviewCaption: string;
  overviewActionHref: string;
  overviewActionLabel: string;
  overviewActionAriaLabel: string;
  listLabel: string;
  emptyText: string;
  detailPanelId: string;
  detailEmptyTitle: string;
  detailEmptyText: string;
  detailEmptyAriaLabel: string;
  hasRows: boolean;
  rows: ReconciliationQueueRunRowViewModel[];
}

export interface ManualJournalEntryWorkbenchServices {
  getAssetCapabilities?: typeof import("@/lib/api/asset-accounting-workflow.api").getAssetAccountingDraftCapabilities;
  getWorkbench: (query?: ManualJournalEntryWorkbenchQuery) => Promise<ManualJournalEntryWorkbench>;
  searchSecurities: (query: string) => Promise<SecurityMasterEntry[]>;
  saveDraft: (request: SaveManualJournalEntryDraftRequest) => Promise<ManualJournalEntryDraft>;
  validateDraft: (request: ValidateManualJournalEntryDraftRequest) => Promise<ManualJournalEntryDraft>;
  submitApproval: (request: SubmitManualJournalEntryApprovalRequest) => Promise<ManualJournalEntryDraft>;
  attachEvidence: (request: AttachManualJournalEntryEvidenceRequest) => Promise<ManualJournalEntryDraft>;
  applyLifecycleAction: (request: JournalEntryLifecycleActionRequest) => Promise<JournalEntryLifecycleActionResult>;
}

export interface CapitalAccountWorkbenchServices {
  getWorkbench: (query?: CapitalAccountWorkbenchQuery) => Promise<CapitalAccountWorkbench>;
}

export interface ManualJournalLineValidationBadge {
  id: string;
  label: string;
  message: string;
  tone: "default" | "success" | "warning" | "danger";
}

export interface ManualJournalLifecycleCommandViewModel {
  action: JournalEntryLifecycleAction;
  label: string;
  description: string;
  disabledReason: string | null;
  tone: "default" | "success" | "warning" | "danger";
  busy: boolean;
}

export interface ManualJournalLifecycleTransitionViewModel {
  id: string;
  title: string;
  detail: string;
  auditLabel: string;
  correlationLabel: string;
  evidenceLabel: string;
  evidenceTone: "outline" | "success";
  evidenceRows: string[];
}

export interface ManualJournalLifecycleCorrectionViewModel {
  id: string;
  title: string;
  subtitle: string;
  balanceLabel: string;
  sourceLabel: string;
}

export interface ManualJournalLifecycleChecklistItemViewModel {
  id: string;
  label: string;
  value: string;
  detail: string;
  tone: AccountingToolingTone;
}

export interface ManualJournalEvidenceAttachmentDraft {
  displayName: string;
  uri: string;
  evidenceKind: string;
  sourceSystem: string;
  lineId: string | null;
  description: string;
}

export interface ManualJournalPrivateCapitalMetricViewModel {
  id: string;
  label: string;
  value: string;
  detail: string;
  tone: "default" | "success" | "warning" | "danger";
}

export interface ManualJournalPrivateCapitalFundEventRowViewModel {
  id: string;
  title: string;
  subtitle: string;
  statusLabel: string;
  statusTone: "outline" | "success" | "warning" | "danger";
  effectiveDateLabel: string;
  amountLabel: string;
  grossAmountLabel: string;
  evidenceLabel: string;
  memoLabel: string;
  paymentLabel: string;
  validationLabel: string;
}

export interface ManualJournalPrivateCapitalAccountRowViewModel {
  id: string;
  title: string;
  subtitle: string;
  netActivityLabel: string;
  contributionLabel: string;
  distributionLabel: string;
  subscriptionLabel: string;
  redemptionLabel: string;
  managementFeeLabel: string;
  eventCountLabel: string;
  lastEventLabel: string;
}

export interface ManualJournalPrivateCapitalCapitalAccountSubledgerRowViewModel {
  id: string;
  title: string;
  subtitle: string;
  statusLabel: string;
  statusTone: "outline" | "success" | "warning" | "danger";
  readinessLabel: string;
  readinessTone: "outline" | "success" | "warning" | "danger";
  readinessReasonLabel: string;
  nextActionLabel: string;
  nextActionRouteLabel: string;
  activityRouteLabel: string;
  netActivityLabel: string;
  openingLabel: string;
  endingLabel: string;
  contributionLabel: string;
  distributionLabel: string;
  otherActivityLabel: string;
  eventCountLabel: string;
  approvalQueueLabel: string;
  postedEventLabel: string;
  publishedReportLabel: string;
  dateRangeLabel: string;
  evidenceLabel: string;
  paymentEvidenceLabel: string;
  paymentEvidenceTone: "success" | "warning";
  paymentEvidenceSummaryLabel: string;
  paymentEvidenceRequiredLabel: string;
  evidenceCategorySummaryLabel: string;
  evidenceCategories: ManualJournalPrivateCapitalEvidenceCategoryViewModel[];
  issueLabel: string;
}

export interface ManualJournalPrivateCapitalSubledgerEntryRowViewModel {
  id: string;
  title: string;
  subtitle: string;
  statusLabel: string;
  statusTone: "outline" | "success" | "warning" | "danger";
  effectiveDateLabel: string;
  netActivityLabel: string;
  runningBalanceLabel: string;
  grossAmountLabel: string;
  evidenceLabel: string;
  memoLabel: string;
  issueLabel: string;
}

export interface ManualJournalPrivateCapitalLedgerImpactRowViewModel {
  id: string;
  title: string;
  subtitle: string;
  readinessLabel: string;
  readinessTone: "outline" | "success" | "warning" | "danger";
  effectiveDateLabel: string;
  debitLabel: string;
  creditLabel: string;
  imbalanceLabel: string;
  evidenceLabel: string;
  lineLabel: string;
  issueLabel: string;
}

export interface ManualJournalPrivateCapitalReportOutputRowViewModel {
  id: string;
  title: string;
  subtitle: string;
  readinessLabel: string;
  readinessTone: "outline" | "success" | "warning" | "danger";
  readinessReasonLabel: string;
  nextActionLabel: string;
  nextActionRouteLabel: string;
  effectiveDateLabel: string;
  amountLabel: string;
  evidenceLabel: string;
  routeLabel: string;
  issueLabel: string;
  workflowLabel: string;
  publicationLabel: string;
  provenanceLabel: string;
}

export interface ManualJournalPrivateCapitalEvidenceCategoryViewModel {
  id: string;
  label: string;
  statusLabel: string;
  tone: "success" | "warning";
  summaryLabel: string;
  evidenceLabel: string;
  requiredEvidenceLabel: string;
}

export interface ManualJournalPaymentIntentWorkflowRowViewModel {
  id: string;
  title: string;
  subtitle: string;
  statusLabel: string;
  statusTone: "outline" | "success" | "warning" | "danger";
  requestedLabel: string;
  expectedCashLabel: string;
  requestMetadataLabel: string;
  sourceEvidenceLabel: string;
  approvalLabel: string;
  bankEvidenceLabel: string;
  reconciliationLabel: string;
  auditLabel: string;
  readinessReasonLabel: string;
  executionDeferredLabel: string;
  evidenceRouteLabel: string;
  workbenchRouteLabel: string;
  approvalSteps: ManualJournalPaymentIntentApprovalStepViewModel[];
  bankEvidence: ManualJournalPaymentIntentBankEvidenceViewModel[];
  reconciliationLinks: ManualJournalPaymentIntentReconciliationLinkViewModel[];
  auditEvents: ManualJournalPaymentIntentAuditEventViewModel[];
}

export interface ManualJournalPaymentIntentApprovalStepViewModel {
  id: string;
  sequenceLabel: string;
  roleLabel: string;
  actorLabel: string;
  statusLabel: string;
  decidedLabel: string;
  evidenceRouteLabel: string;
}

export interface ManualJournalPaymentIntentBankEvidenceViewModel {
  id: string;
  title: string;
  statusLabel: string;
  summaryLabel: string;
  amountLabel: string;
  effectiveDateLabel: string;
  recordedLabel: string;
  recorderLabel: string;
  referenceLabel: string;
  evidenceRouteLabel: string;
}

export interface ManualJournalPaymentIntentReconciliationLinkViewModel {
  id: string;
  statusLabel: string;
  summaryLabel: string;
  routeLabel: string;
  caseLabel: string;
}

export interface ManualJournalPaymentIntentAuditEventViewModel {
  id: string;
  actionLabel: string;
  actorLabel: string;
  recordedLabel: string;
  summaryLabel: string;
  evidenceLabel: string;
  evidenceRouteLabels: string[];
}

export interface ManualJournalPrivateCapitalFundEventLedgerRecordViewModel {
  id: string;
  title: string;
  subtitle: string;
  statusLabel: string;
  statusTone: "outline" | "success" | "warning" | "danger";
  readinessLabel: string;
  readinessTone: "outline" | "success" | "warning" | "danger";
  readinessReasonLabel: string;
  nextActionLabel: string;
  nextActionRouteLabel: string;
  effectiveDateLabel: string;
  netActivityLabel: string;
  grossActivityLabel: string;
  capitalAccountRollForwardLabel: string;
  memoLabel: string;
  referenceLabel: string;
  paymentEvidenceLabel: string;
  paymentEvidenceTone: "success" | "warning";
  paymentEvidenceSummaryLabel: string;
  paymentEvidenceRequiredLabel: string;
  activityRouteLabel: string;
  commandCenterRouteLabel: string;
  evidenceRouteLabel: string;
  approvalRouteLabel: string;
  evidenceLabel: string;
  ledgerImpactLabel: string;
  subledgerLabel: string;
  reportOutputLabel: string;
  reportOutputDetailLabel: string;
  reportOutputRouteLabel: string;
  evidenceCategorySummaryLabel: string;
  evidenceCategories: ManualJournalPrivateCapitalEvidenceCategoryViewModel[];
  issueLabel: string;
}

export interface ManualJournalPrivateCapitalActivityViewModel {
  title: string;
  statusLabel: string;
  projectedAtLabel: string;
  emptyText: string;
  summaryCards: ManualJournalPrivateCapitalMetricViewModel[];
  fundEvents: ManualJournalPrivateCapitalFundEventRowViewModel[];
  capitalAccounts: ManualJournalPrivateCapitalAccountRowViewModel[];
  capitalAccountSubledgers: ManualJournalPrivateCapitalCapitalAccountSubledgerRowViewModel[];
  capitalAccountSubledgerEntries: ManualJournalPrivateCapitalSubledgerEntryRowViewModel[];
  ledgerImpacts: ManualJournalPrivateCapitalLedgerImpactRowViewModel[];
  reportOutputs: ManualJournalPrivateCapitalReportOutputRowViewModel[];
  fundEventLedgerRecords: ManualJournalPrivateCapitalFundEventLedgerRecordViewModel[];
  paymentIntents: ManualJournalPaymentIntentWorkflowRowViewModel[];
  validationIssues: AccountingConfigurationIssueViewModel[];
}

export interface CapitalAccountWorkbenchMetricViewModel {
  id: string;
  label: string;
  value: string;
  detail: string;
  tone: AccountingToolingTone;
}

export interface CapitalAccountWorkbenchInvestorAccountRowViewModel {
  id: string;
  title: string;
  subtitle: string;
  statusLabel: string;
  statusTone: AccountingToolingTone;
  netActivityLabel: string;
  rollForwardLabel: string;
  activityMixLabel: string;
  evidenceLabel: string;
  eventLabel: string;
  paymentEvidenceLabel: string;
  paymentEvidenceTone: "success" | "warning";
  paymentEvidenceSummaryLabel: string;
  paymentEvidenceRequiredLabel: string;
  routeLabel: string;
}

export interface CapitalAccountWorkbenchAllocationRuleRowViewModel {
  id: string;
  accountLabel: string;
  label: string;
  statusLabel: string;
  statusTone: AccountingToolingTone;
  reason: string;
  basis: string;
  evidenceLabel: string;
  routeLabel: string;
  requiredLabel: string;
  policyLabel: string;
  effectiveWindowLabel: string;
  formulaLabel: string;
  approvalLabel: string;
  traceLabel: string;
  inputSummaryLabel: string;
  relatedFundEventLabel: string;
}

export interface CapitalAccountWorkbenchRestatementChangedLineRowViewModel {
  id: string;
  lineKey: string;
  valueLabel: string;
  evidenceLabel: string;
}

export interface CapitalAccountWorkbenchStatementLineageRowViewModel {
  id: string;
  title: string;
  subtitle: string;
  statusLabel: string;
  statusTone: AccountingToolingTone;
  publicationLabel: string;
  provenanceLabel: string;
  restatementLabel: string;
  manifestLabel: string;
  routeLabel: string;
  changedLineRows: CapitalAccountWorkbenchRestatementChangedLineRowViewModel[];
}

export interface CapitalAccountWorkbenchAuditDrillThroughRowViewModel {
  id: string;
  kind: string;
  title: string;
  summary: string;
  statusLabel: string;
  statusTone: AccountingToolingTone;
  evidenceLabel: string;
  routeLabel: string;
  relatedLabel: string;
}

export type CapitalAccountWorkbenchFundEventCommandRowViewModel =
  ManualJournalPrivateCapitalFundEventLedgerRecordViewModel;

export interface CapitalAccountWorkbenchViewModel {
  title: string;
  description: string;
  available: boolean; loading: boolean; errorText: string | null;
  statusLabel: string;
  statusTone: AccountingToolingTone;
  statusReason: string;
  projectedAtLabel: string;
  workbenchRouteLabel: string;
  emptyText: string;
  summaryCards: CapitalAccountWorkbenchMetricViewModel[];
  investorAccounts: CapitalAccountWorkbenchInvestorAccountRowViewModel[];
  allocationRules: CapitalAccountWorkbenchAllocationRuleRowViewModel[];
  statementLineage: CapitalAccountWorkbenchStatementLineageRowViewModel[];
  auditDrillThroughs: CapitalAccountWorkbenchAuditDrillThroughRowViewModel[];
  fundEventCommandRows: CapitalAccountWorkbenchFundEventCommandRowViewModel[];
  validationIssues: AccountingConfigurationIssueViewModel[];
  liveCapabilities: string[];
  plannedCapabilities: string[];
  refresh: () => Promise<void>;
}

export interface ManualJournalEntryWorkbenchViewModel {
  assetWorkflow?: AssetAccountingWorkflowViewModel;
  title: string;
  description: string;
  available: boolean;
  loading: boolean;
  errorText: string | null;
  statusLabel: string;
  draft: ManualJournalEntryDraft;
  drafts: ManualJournalEntryDraft[];
  accountOptions: { value: string; label: string }[];
  selectedLineId: string;
  securitySearchQuery: string; securitySearchResults: SecurityMasterEntry[];
  securitySearchBusy: boolean; securitySearchErrorText: string | null;
  securitySearchStatusText: string;
  attachmentDraft: ManualJournalEvidenceAttachmentDraft; totalsLabel: string;
  totalDebitsLabel: string;
  totalCreditsLabel: string;
  imbalanceLabel: string;
  balanceStatusLabel: string;
  balanceStatusTone: "success" | "warning";
  balanceImpactRows: ManualJournalBalanceImpactRowViewModel[];
  treasuryContextLabel: string;
  privateCapitalActivity: ManualJournalPrivateCapitalActivityViewModel;
  validationIssues: AccountingConfigurationIssueViewModel[];
  blockingIssueCount: number; warningIssueCount: number;
  saveState: "saved" | "unsaved" | "saving" | "error" | "recovered"; saveStatusLabel: string;
  validationStatusLabel: string; recoveryStatusText: string | null;
  lifecycleCommands: ManualJournalLifecycleCommandViewModel[];
  lifecycleChecklist: ManualJournalLifecycleChecklistItemViewModel[];
  lifecycleTransitions: ManualJournalLifecycleTransitionViewModel[];
  lifecycleCorrectionRows: ManualJournalLifecycleCorrectionViewModel[];
  lifecycleStatusText: string | null;
  lifecycleBusyAction: JournalEntryLifecycleAction | null;
  saveBusy: boolean; validateBusy: boolean; submitBusy: boolean;
  attachEvidenceBusy: boolean;
  attachEvidenceStatusText: string | null;
  validationIsCurrent: boolean;
  canSubmit: boolean;
  submitDisabledReason: string | null;
  refresh: () => Promise<void>;
  updateHeader: (field: keyof Pick<ManualJournalEntryDraft, "memo" | "currency" | "fundProfileId" | "entityId" | "fundNodeId" | "periodId" | "accountingDate">, value: string) => void;
  selectDraft: (journalEntryId: string) => void;
  selectLine: (lineId: string) => void;
  updateLine: (lineId: string, patch: Partial<ManualJournalEntryLine>) => void; updateDraftDimensions: (patch: Partial<LedgerDimensionSet>) => void;
  getLineBadges: (lineId: string) => ManualJournalLineValidationBadge[];
  updateSecuritySearchQuery: (query: string) => void;
  searchSecurityMaster: () => Promise<void>;
  selectSecurity: (lineId: string, security: SecurityMasterEntry) => void;
  clearSecurity: (lineId: string) => void;
  addLine: (side: AccountingTemplateLineSide) => void;
  insertLineAfter: (lineId: string, side?: AccountingTemplateLineSide) => string; duplicateLine: (lineId: string) => string | null;
  removeLine: (lineId: string) => void;
  discardRecoveredDraft: () => void;
  updateAttachmentDraft: (patch: Partial<ManualJournalEvidenceAttachmentDraft>) => void;
  addAttachment: () => Promise<void>;
  removeAttachment: (attachmentId: string) => void;
  applyLifecycleAction: (action: JournalEntryLifecycleAction) => Promise<void>;
  save: () => Promise<void>;
  validate: () => Promise<void>;
  submit: () => Promise<void>;
}

export interface ManualJournalBalanceImpactRowViewModel {
  id: string;
  accountPath: string;
  accountName: string;
  accountType: string;
  debitLabel: string;
  creditLabel: string;
  netEffectLabel: string;
  balanceDirectionLabel: string;
  lineCountLabel: string;
  tone: "success" | "warning" | "default";
}

export interface OperationalExceptionWorkbenchMetricViewModel {
  id: string;
  label: string;
  value: string;
  detail: string;
  tone: "default" | "success" | "warning" | "danger";
}

export interface OperationalExceptionWorkbenchCaseViewModel {
  id: string;
  title: string;
  subtitle: string;
  rawCategoryLabel: string;
  statusLabel: string;
  statusTone: "success" | "warning" | "outline" | "danger";
  ownerLabel: string;
  slaLabel: string;
  commentLabel: string;
  auditLabel: string;
  routeHref: string;
  routeLabel: string;
  ariaLabel: string;
}

export interface OperationalExceptionWorkbenchViewState {
  title: string;
  description: string;
  metricRows: OperationalExceptionWorkbenchMetricViewModel[];
  cases: OperationalExceptionWorkbenchCaseViewModel[];
  emptyText: string;
  reconciliationHref: string;
  approvalsHref: string;
  evidenceHref: string;
  auditHref: string;
}

export type ReconciliationStatementRunLoadStatus = "idle" | "loading" | "ready" | "error";
export type ReconciliationRunDetailTabId = "overview" | "validation" | "positions" | "cash" | "transactions" | "breaks-cases" | "evidence";

export interface ReconciliationStatementRunRowViewModel {
  runId: string;
  brokerCustodianLabel: string;
  accountLabel: string;
  periodLabel: string;
  statusLabel: string;
  validationIssueCountLabel: string;
  matchCountLabel: string;
  breakCountLabel: string;
  caseCountLabel: string;
  importedAtLabel: string;
  isSelected: boolean;
  controlsId: string;
  ariaLabel: string;
  selectAriaLabel: string;
  unavailableReason: string | null;
}

export interface ReconciliationRunDetailTabViewModel {
  id: ReconciliationRunDetailTabId;
  label: string;
  badgeLabel: string | null;
  description: string;
  disabled: boolean;
  disabledReason: string | null;
  ariaLabel: string;
}

export interface ReconciliationStatementRunsViewState {
  title: string;
  description: string;
  tableLabel: string;
  tableCaption: string;
  detailPanelId: string;
  emptyText: string;
  loadingText: string | null;
  errorText: string | null;
  errorDetails: string[];
  recoveryActionLabel: string;
  recoveryActionAriaLabel: string;
  statusAnnouncement: string;
  hasRows: boolean;
  rows: ReconciliationStatementRunRowViewModel[];
  tabs: ReconciliationRunDetailTabViewModel[];
}

export interface ReconciliationComparisonRowViewModel {
  id: string;
  statementTitle: string;
  statementMeta: string;
  statementValue: string;
  ledgerTitle: string;
  ledgerMeta: string;
  ledgerValue: string;
  statusLabel: string;
  statusTone: "success" | "warning" | "danger";
}

export interface ReconciliationLineItemViewModel {
  id: string;
  matchKey: string;
  title: string;
  meta: string;
  amountLabel: string;
  statusLabel: string;
  statusTone: "success" | "warning" | "danger";
}

export interface ReconciliationComparisonViewState {
  title: string;
  subtitle: string;
  statementHeading: string;
  ledgerHeading: string;
  matchedBadgeLabel: string;
  openBadgeLabel: string;
  statementBalanceLabel: string;
  ledgerBalanceLabel: string;
  varianceLabel: string;
  varianceTone: "success" | "warning";
  rows: ReconciliationComparisonRowViewModel[];
  /** Per-line statement side. Transaction-level when an external GL reconciliation is loaded, else one line per run. */
  statementLines: ReconciliationLineItemViewModel[];
  /** Per-line ledger side, paired with statementLines by matchKey for cross-highlighting. */
  ledgerLines: ReconciliationLineItemViewModel[];
  /** "transactions" when driven by AccountingSystemReconciliationRow detail; "runs" for the run-level fallback. */
  lineSource: "transactions" | "runs";
  ariaLabel: string;
}

export interface AccountingCashFlowRowViewModel {
  id: string;
  label: string;
  value: string;
  tone: CashFlowEvidenceTone;
  ariaLabel: string;
}

export interface AccountingCashFlowViewState {
  eyebrow: string;
  title: string;
  description: string;
  routePath: string;
  statusLabel: string;
  statusTone: CashFlowEvidenceTone;
  statusAriaLabel: string;
  ariaLabel: string;
  rowGroupLabel: string;
  rows: AccountingCashFlowRowViewModel[];
  statusAnnouncement: string;
}

export interface AccountingLoadingViewState {
  role: "status";
  ariaBusy: true;
  ariaLive: "polite";
  titleId: string;
  detailId: string;
  eyebrow: string;
  title: string;
  detail: string;
  routeLabel: string;
  workstreamLabel: string;
  statusItemsLabel: string;
  statusItems: AccountingLoadingStatusItemViewModel[];
  actionsLabel: string;
  actions: AccountingLoadingActionViewModel[];
}

export interface AccountingLoadingStatusItemViewModel {
  id: string;
  label: string;
  detail: string;
}

export interface AccountingLoadingActionViewModel {
  id: string;
  label: string;
  detail: string;
  href: string;
  ariaLabel: string;
}

export interface ReportingProfileBadgeViewModel {
  label: string;
  tone: "primary" | "success" | "warning" | "muted";
}

export interface ReportingProfileRowViewModel extends AccountingReportingProfile {
  formatLabel: string;
  targetLabel: string;
  recommendationLabel: string | null;
  badges: ReportingProfileBadgeViewModel[];
  isSelected: boolean;
  selectAriaLabel: string;
  detailId: string;
}

export interface ReportingProfileDetailField {
  label: string;
  value: string;
  tone?: "success" | "warning" | "muted";
}

export interface ReportingProfileDetailViewModel {
  id: string;
  title: string;
  subtitle: string;
  description: string;
  fields: ReportingProfileDetailField[];
}

export interface AccountingReportingViewState {
  title: string;
  description: string;
  countLabel: string;
  visibleCountLabel: string;
  targetSummary: string;
  listLabel: string;
  detailId: string;
  rows: ReportingProfileRowViewModel[];
  hasRows: boolean;
  emptyText: string;
  selectedProfile: ReportingProfileDetailViewModel | null;
  statusTitle: string;
  statusDetail: string;
  nextAction: string;
  selectedExportProfileId: string | null;
  exportButtonLabel: string;
  exportAriaLabel: string;
  exportDisabledReason: string | null;
  exportStatusText: string | null;
  exportStatusTone: "neutral" | "success" | "danger";
  exportStatusRole: "status" | "alert";
  exportCanRun: boolean;
  exportBusy: boolean;
  backendLinks: AccountingReportingBackendLink[];
}

export interface AccountingReportingBackendLink {
  id: string;
  label: string;
  href: string;
  ariaLabel: string;
}

export type AccountingTrialBalanceState = "ready" | "loading" | "empty" | "error";

export interface AccountingTrialBalanceBasisOption {
  id: AccountingBasisKind;
  label: string;
  description: string;
  rowCount: number;
  rowCountLabel: string;
  isSelected: boolean;
}

export interface AccountingTrialBalanceRowViewModel extends LedgerTrialBalanceLine {
  rowId: string;
  accountLabel: string;
  accountTypeLabel: string;
  basisLabel: string;
  basisTone: "default" | "outline" | "success" | "warning" | "danger";
  policyLabel: string;
  dimensionLabel: string;
  dimensionDetailLabel: string;
  balanceLabel: string;
  balanceTone: "default" | "success" | "danger";
  entryCountLabel: string;
  ariaLabel: string;
  selectAriaLabel: string;
  detailPanelId: string;
  isExpanded: boolean;
}

export interface AccountingTrialBalanceDetailViewState {
  eyebrow: string;
  title: string;
  subtitle: string;
  description: string;
  statusLabel: string;
  statusVariant: "outline" | "success" | "danger";
  ariaLabel: string;
  fields: Array<{ label: string; value: string }>;
  auditDrillThroughLabel: string;
  auditDrillThroughHref: string | null;
  approvalDrillThroughHref: string | null;
  ledgerLinesTitle: string;
  ledgerLinesDescription: string;
  ledgerLines: AccountingLedgerLineViewModel[];
  ledgerLinesEmptyText: string;
  supportingDocumentsTitle: string;
  supportingDocuments: AccountingSupportingDocumentViewModel[];
  supportingDocumentsEmptyText: string;
}

export interface AccountingLedgerJournalEvidenceViewState {
  title: string;
  description: string;
  rows: AccountingLedgerJournalEvidenceRowViewModel[];
  filteredRowCountLabel: string;
  hasRows: boolean;
  emptyText: string;
}

export interface AccountingLedgerJournalEvidenceRowViewModel extends LedgerJournalLine {
  rowId: string;
  timestampLabel: string;
  amountLabel: string;
  lineCountLabel: string;
  dimensionLabel: string;
  dimensionDetailLabel: string;
  ariaLabel: string;
}

export interface AccountingLedgerAccountFilterOption {
  id: string;
  label: string;
  detail: string;
  rowCount: number;
  rowCountLabel: string;
  isSelected: boolean;
}

export interface AccountingLedgerLineViewModel {
  rowId: string;
  journalEntryId: string;
  description: string;
  debitLabel: string;
  creditLabel: string;
  balanceLabel: string;
  evidenceLabel: string;
  evidenceHref: string | null;
  approvalHref: string | null;
  ariaLabel: string;
}

export interface AccountingSupportingDocumentViewModel {
  id: string;
  label: string;
  detail: string;
  href: string | null;
  ariaLabel: string;
}

export interface AccountingBasisBridgeRowViewModel {
  rowId: string;
  accountLabel: string;
  accountTypeLabel: string;
  primaryBalanceLabel: string;
  comparisonBalanceLabel: string;
  varianceLabel: string;
  varianceTone: "default" | "success" | "danger";
  sourceLabel: string;
  ariaLabel: string;
}

export interface AccountingBasisBridgeViewState {
  title: string;
  description: string;
  tableLabel: string;
  fromBasis: AccountingBasisKind;
  toBasis: AccountingBasisKind;
  rows: AccountingBasisBridgeRowViewModel[];
  hasRows: boolean;
  emptyText: string;
}

export interface AccountingTrialBalanceViewState {
  title: string;
  description: string;
  tableLabel: string;
  selectedBasis: AccountingBasisKind;
  basisOptions: AccountingTrialBalanceBasisOption[];
  basisBridge: AccountingBasisBridgeViewState;
  accountFilterLabel: string;
  accountFilterPlaceholder: string;
  accountFilterValue: string;
  accountFilterOptions: AccountingLedgerAccountFilterOption[];
  filteredRowCountLabel: string;
  clearAccountFilterLabel: string;
  state: AccountingTrialBalanceState;
  rows: AccountingTrialBalanceRowViewModel[];
  hasRows: boolean;
  /**
   * Signed sum of the whole selected basis, independent of the account filter.
   *
   * The balance control answers "does this book tie", which is a property of the book, not of
   * whatever subset an operator has searched for. Summing the filtered rows instead declared the
   * book out of balance by the value of everything filtered out, and told the operator to resolve
   * a variance that does not exist before approving or reporting.
   */
  basisVariance: number;
  isBasisOutOfBalance: boolean;
  selectedRowId: string | null;
  detailPanelId: string;
  selectedDetail: AccountingTrialBalanceDetailViewState | null;
  detailEmptyTitle: string;
  detailEmptyText: string;
  detailEmptyAriaLabel: string;
  loadingText: string | null;
  emptyTitle: string;
  emptyDetail: string;
  errorText: string | null;
  errorDetails: string[];
  statusAnnouncement: string;
}

export type AccountingToolingTone = "default" | "success" | "warning" | "danger";

export interface AccountingWorkflowStepViewModel {
  id: AccountingWorkstream;
  label: string;
  caption: string;
  href: string;
  metricLabel: string;
  metricValue: string;
  statusLabel: string;
  tone: AccountingToolingTone;
  isActive: boolean;
  ariaLabel: string;
}

export interface AccountingWorkflowActionViewModel {
  id: string;
  label: string;
  detail: string;
  href: string;
  ariaLabel: string;
  tone: AccountingToolingTone;
}

export interface AccountingWorkflowLaunchViewState {
  title: string;
  description: string;
  ariaLabel: string;
  taskMode: AccountingTaskModeViewModel;
  activeLabel: string;
  statusLabel: string;
  statusTone: AccountingToolingTone;
  steps: AccountingWorkflowStepViewModel[];
  actionRows: AccountingWorkflowActionViewModel[];
  liveRegionText: string;
}

export type CloseCommandCenterStatus = "ready" | "at-risk" | "blocked" | "loading";

export interface CloseCommandCenterMetricViewModel {
  id: string;
  label: string;
  value: string;
  detail: string;
  tone: AccountingToolingTone;
  href: string | null;
}

export interface CloseCommandCenterBlockerViewModel {
  id: string;
  label: string;
  detail: string;
  tone: AccountingToolingTone;
  href: string | null;
  statusLabel: string;
  ownerLabel: string | null;
  dueLabel: string | null;
  evidenceLabel: string;
  /** Raw identifiers of the records behind the blocker; shown only under audit details. */
  sourceRecordIds?: string[];
  actionLabel: string;
  impactLabel: string;
}

export interface CloseCommandCenterActionViewModel {
  id: string;
  label: string;
  href: string;
  ariaLabel: string;
  tone: AccountingToolingTone;
  command?:
    | "configure-daily-valuation-schedule"
    | "run-due-daily-valuation-schedules"
    | "approve-daily-valuation-batch"
    | "retry-daily-valuation-batch";
  busyLabel?: string;
  disabledReason?: string | null;
}

export interface CloseCommandCenterViewState {
  intercompanyAdvisories?: import("@/types/intercompany-obligations").IntercompanyObligationMonitor | null;
  title: string;
  description: string;
  ariaLabel: string;
  status: CloseCommandCenterStatus;
  statusLabel: string;
  statusTone: AccountingToolingTone;
  periodLabel: string;
  fundAccountLabel: string;
  summary: string;
  updatedLabel: string;
  updatedAtUtc: string | null;
  metricRows: CloseCommandCenterMetricViewModel[];
  blockerRows: CloseCommandCenterBlockerViewModel[];
  actionRows: CloseCommandCenterActionViewModel[];
  loadingText: string | null;
  errorText: string | null;
  liveRegionText: string;
}

export interface AccountingReportPackageHistoryMetricViewModel {
  id: string;
  label: string;
  value: string;
  detail: string;
  tone: AccountingToolingTone;
}

export interface AccountingClosePlanTaskRowViewModel {
  taskId: string;
  displayName: string;
  ownerLabel: string;
  dueDateLabel: string;
  statusLabel: string;
  statusTone: AccountingToolingTone;
  dependencyLabel: string;
  signOffLabel: string;
  signOffDetailLabel: string | null;
  signOffRequirementLabel: string;
  evidenceLabel: string;
  blockerLabel: string | null;
}

export interface AccountingCloseCalendarMilestoneViewModel {
  milestoneId: string;
  displayName: string;
  ownerLabel: string;
  dueDateLabel: string;
  statusLabel: string;
  statusTone: AccountingToolingTone;
  dependencyLabel: string;
  signOffLabel: string;
  evidenceLabel: string;
  blockerLabel: string | null;
  lockedLabel: string;
}

export interface AccountingLateAdjustmentRowViewModel {
  requestId: string;
  journalEntryId: string;
  amountLabel: string;
  requestedByLabel: string;
  statusLabel: string;
  decisionLabel: string | null;
  evidenceLabel: string;
  materialityLabel: string;
  materialityTone: AccountingToolingTone;
  reason: string;
  reviewDisabledReason: string | null;
}

export interface AccountingLateAdjustmentDraftViewModel {
  journalEntryId: string;
  amount: string;
  currency: string;
  reason: string;
}

export interface AccountingCloseDependencyGraphRowViewModel {
  dependencyId: string;
  taskId: string;
  taskLabel: string;
  dependsOnTaskId: string;
  predecessorLabel: string;
  reason: string;
  statusLabel: string;
  statusTone: AccountingToolingTone;
  blockerLabel: string | null;
}

export interface AccountingCloseSignOffMatrixRowViewModel {
  rowId: string;
  taskId: string;
  taskLabel: string;
  roleLabel: string;
  approvedLabel: string;
  statusLabel: string;
  statusTone: AccountingToolingTone;
  evidenceRequirementLabel: string;
  latestSignOffLabel: string | null;
}

export interface AccountingCloseEvidenceReviewRowViewModel {
  rowId: string;
  issueCode: string | null;
  targetId: string | null;
  label: string;
  categoryLabel: string;
  evidenceLabel: string;
  statusLabel: string;
  statusTone: AccountingToolingTone;
  detailLabel: string;
  latestReviewLabel: string | null;
  reviewDisabledReason: string | null;
}

export interface AccountingCloseOperatingCoverageRowViewModel {
  controlId: string;
  label: string;
  statusLabel: string;
  statusTone: AccountingToolingTone;
  evidenceLabel: string;
  blockerLabel: string;
  requiredAction: string;
  issueLabels: string[];
  /**
   * The retained evidence references themselves, not just the count in `evidenceLabel`. The shared
   * close plan carries these per control; showing only the count told an operator reviewing a
   * blocked control that evidence exists while giving them no way to reach it (ACCT-CHECKLIST-07).
   */
  evidenceReferences: string[];
}

export interface AccountingClosePostingBalanceRowViewModel {
  rowId: string;
  accountLabel: string;
  accountTypeLabel: string;
  balanceLabel: string;
  scopeLabel: string;
  financialAccountLabel: string;
}

export interface AccountingClosePostingGateViewModel {
  gateId: string;
  label: string;
  statusLabel: string;
  statusTone: AccountingToolingTone;
  isReadyForLock: boolean;
  netIncomeRollLabel: string;
  temporaryAccountBalanceLabel: string;
  detail: string;
  draftLabel: string;
  idempotencyLabel: string;
  closingBatchLabel: string;
  reversalDraftLabel: string;
  evidenceLabel: string;
  balances: AccountingClosePostingBalanceRowViewModel[];
}

export interface AccountingCloseSetupTaskOptionViewModel {
  taskId: string;
  displayName: string;
  statusLabel: string;
  statusTone: AccountingToolingTone;
  ownerLabel: string;
  dueDateLabel: string;
  dependencyLabel: string;
  signOffLabel: string;
  selected: boolean;
  selectAriaLabel: string;
}

export interface AccountingCloseSetupDependencyOptionViewModel {
  taskId: string;
  displayName: string;
  statusLabel: string;
  statusTone: AccountingToolingTone;
  ownerLabel: string;
  dueDateLabel: string;
  checked: boolean;
  toggleAriaLabel: string;
}

export interface AccountingCloseSetupSignOffRoleOptionViewModel {
  role: string;
  label: string;
  sourceLabel: string;
  selected: boolean;
  selectAriaLabel: string;
}

export type AccountingCloseSignOffDecision = "Approved" | "Rejected";

export interface AccountingCloseSignOffTaskOptionViewModel {
  taskId: string;
  displayName: string;
  statusLabel: string;
  statusTone: AccountingToolingTone;
  ownerLabel: string;
  signOffLabel: string;
  selected: boolean;
  selectAriaLabel: string;
}

export interface AccountingCloseSignOffRoleOptionViewModel {
  role: string;
  label: string;
  sourceLabel: string;
  selected: boolean;
  selectAriaLabel: string;
}

export interface AccountingCloseSignOffDecisionOptionViewModel {
  decision: AccountingCloseSignOffDecision;
  label: string;
  selected: boolean;
  selectAriaLabel: string;
}

export interface AccountingCloseSignOffDraftViewModel {
  taskId: string;
  role: string;
  decision: AccountingCloseSignOffDecision;
  notes: string;
}

export interface AccountingCloseSetupDraftViewModel {
  amountThreshold: string;
  percentThreshold: string;
  currency: string;
  reviewRole: string;
  requiresLateAdjustmentApproval: boolean;
  taskId: string;
  taskDisplayName: string;
  taskOwner: string;
  taskDueDate: string;
  taskRequiredApprovalCount: string;
  taskRequiredApprovalRole: string;
  taskRequiredEvidence: string;
  taskSignOffRequirements: string;
  taskDependsOnTaskIds: string;
  taskDependencyReason: string;
}

export interface AccountingReportPackageRowViewModel {
  packageId: string;
  periodLabel: string;
  certificationLabel: string;
  certificationTone: AccountingToolingTone;
  navLabel: string;
  investorStatementLabel: string;
  realizedGainLossLabel: string;
  restatementLabel: string;
  exportArtifactLabel: string;
  exportArtifactTone: AccountingToolingTone;
  evidenceLabel: string;
  validationLabel: string;
  selected: boolean;
}

export interface AccountingReportCertificationSafeguardViewModel {
  id: string;
  label: string;
  value: string;
  detail: string;
  tone: AccountingToolingTone;
}

export type AccountingCloseWorkflowActionId =
  | "configure-close-plan"
  | "sign-off-task"
  | "request-late-adjustment"
  | "review-evidence"
  | "build-package"
  | "certify-package"
  | "inspect-export"
  | "lock-period";

export interface AccountingCloseWorkflowStepViewModel {
  id: string;
  label: string;
  statusLabel: string;
  detail: string;
  evidenceLabel: string;
  tone: AccountingToolingTone;
  actionLabel: string | null;
  actionId: AccountingCloseWorkflowActionId | null;
  disabledReason: string | null;
}

export interface AccountingCloseReportPackageViewModel {
  title: string;
  description: string;
  ariaLabel: string;
  statusLabel: string;
  statusTone: AccountingToolingTone;
  periodLabel: string;
  fundLabel: string;
  lockLabel: string;
  materialityLabel: string;
  loading: boolean;
  loadingText: string | null;
  errorText: string | null;
  buildBusy: boolean;
  buildStatusText: string | null;
  buildStatusTone: "neutral" | "success" | "danger";
  certifyBusy: boolean;
  certifyStatusText: string | null;
  certifyStatusTone: "neutral" | "success" | "danger";
  signOffBusy: boolean;
  signOffStatusText: string | null;
  signOffStatusTone: "neutral" | "success" | "danger";
  lockClosePeriodBusy: boolean;
  lockClosePeriodStatusText: string | null;
  lockClosePeriodStatusTone: "neutral" | "success" | "danger";
  lockClosePeriodArmed: boolean;
  queueClosingEntriesBusy: boolean;
  queueClosingEntriesStatusText: string | null;
  queueClosingEntriesStatusTone: "neutral" | "success" | "danger";
  configureClosePlanBusy: boolean;
  configureClosePlanStatusText: string | null;
  configureClosePlanStatusTone: "neutral" | "success" | "danger";
  createLateAdjustmentBusy: boolean;
  createLateAdjustmentStatusText: string | null;
  createLateAdjustmentStatusTone: "neutral" | "success" | "danger";
  reviewLateAdjustmentBusy: boolean;
  reviewLateAdjustmentStatusText: string | null;
  reviewLateAdjustmentStatusTone: "neutral" | "success" | "danger";
  reviewCloseEvidenceBusy: boolean;
  reviewCloseEvidenceStatusText: string | null;
  reviewCloseEvidenceStatusTone: "neutral" | "success" | "danger";
  exportManifestBusy: boolean;
  exportManifestStatusText: string | null;
  exportManifestStatusTone: "neutral" | "success" | "danger";
  exportManifest: AccountingReportExportManifestViewModel | null;
  buildButtonLabel: string;
  buildDisabledReason: string | null;
  certifyButtonLabel: string;
  certifyDisabledReason: string | null;
  signOffButtonLabel: string;
  signOffDisabledReason: string | null;
  lockClosePeriodButtonLabel: string;
  lockClosePeriodDisabledReason: string | null;
  queueClosingEntriesButtonLabel: string;
  queueClosingEntriesDisabledReason: string | null;
  configureClosePlanButtonLabel: string;
  configureClosePlanDisabledReason: string | null;
  createLateAdjustmentDisabledReason: string | null;
  closeSetupDraft: AccountingCloseSetupDraftViewModel;
  closeSetupTaskOptions: AccountingCloseSetupTaskOptionViewModel[];
  closeSetupDependencyOptions: AccountingCloseSetupDependencyOptionViewModel[];
  closeSetupSignOffRoleOptions: AccountingCloseSetupSignOffRoleOptionViewModel[];
  closeSignOffDraft: AccountingCloseSignOffDraftViewModel;
  closeSignOffTaskOptions: AccountingCloseSignOffTaskOptionViewModel[];
  closeSignOffRoleOptions: AccountingCloseSignOffRoleOptionViewModel[];
  closeSignOffDecisionOptions: AccountingCloseSignOffDecisionOptionViewModel[];
  lateAdjustmentDraft: AccountingLateAdjustmentDraftViewModel;
  exportManifestButtonLabel: string;
  exportManifestDisabledReason: string | null;
  metrics: AccountingReportPackageHistoryMetricViewModel[];
  closeCalendar: AccountingCloseCalendarMilestoneViewModel[];
  tasks: AccountingClosePlanTaskRowViewModel[];
  dependencyGraphRows: AccountingCloseDependencyGraphRowViewModel[];
  signOffMatrixRows: AccountingCloseSignOffMatrixRowViewModel[];
  evidenceReviewRows: AccountingCloseEvidenceReviewRowViewModel[];
  operatingCoverageRows: AccountingCloseOperatingCoverageRowViewModel[];
  closingEntriesGate: AccountingClosePostingGateViewModel | null;
  lateAdjustments: AccountingLateAdjustmentRowViewModel[];
  packageRows: AccountingReportPackageRowViewModel[];
  selectedPackage: AccountingReportPackageRowViewModel | null;
  certificationSafeguards: AccountingReportCertificationSafeguardViewModel[];
  closeWorkflowSteps: AccountingCloseWorkflowStepViewModel[];
  validationIssues: AccountingConfigurationIssueViewModel[];
  liveRegionText: string;
  refresh: () => Promise<void>;
  buildReportPackage: () => Promise<void>;
  certifyPackage: () => Promise<void>;
  lockClosePeriod: () => Promise<void>;
  queueClosingEntries: () => Promise<void>;
  configureClosePlan: () => Promise<void>;
  signOffNextTask: () => Promise<void>;
  updateCloseSetupDraft: (patch: Partial<AccountingCloseSetupDraftViewModel>) => void;
  selectCloseSetupTask: (taskId: string) => void;
  toggleCloseSetupDependency: (taskId: string) => void;
  selectCloseSetupSignOffRole: (role: string) => void;
  updateCloseSignOffDraft: (patch: Partial<AccountingCloseSignOffDraftViewModel>) => void;
  selectCloseSignOffTask: (taskId: string) => void;
  selectCloseSignOffRole: (role: string) => void;
  selectCloseSignOffDecision: (decision: AccountingCloseSignOffDecision) => void;
  updateLateAdjustmentDraft: (patch: Partial<AccountingLateAdjustmentDraftViewModel>) => void;
  createLateAdjustment: () => Promise<void>;
  reviewLateAdjustment: (requestId: string, decision: "Approved" | "Rejected") => Promise<void>;
  reviewCloseEvidence: (rowId: string) => Promise<void>;
  inspectSelectedPackageExport: () => Promise<void>;
  selectPackage: (packageId: string) => void;
}

export interface AccountingReportExportManifestViewModel {
  packageId: string;
  artifactId: string;
  displayName: string;
  formatLabel: string;
  fileName: string;
  certificationLabel: string;
  generatedLabel: string;
  hashLabel: string;
  evidenceLabel: string;
  postingLabel: string;
  routeLabel: string;
}

export interface AccountingCloseReportPackageServices {
  getClosePlan: (workflowId: string) => Promise<ClosePeriodPlan>;
  createLateAdjustment: (request: CreateLateAdjustmentRequest) => Promise<ClosePeriodPlan>;
  reviewLateAdjustment: (request: ReviewLateAdjustmentRequest) => Promise<ClosePeriodPlan>;
  signOffCloseTask: (request: SignOffCloseTaskRequest) => Promise<ClosePeriodPlan>;
  reviewCloseEvidence: (request: ReviewCloseEvidenceRequest) => Promise<ClosePeriodPlan>;
  configureClosePlan: (request: UpsertClosePeriodPlanConfigurationRequest) => Promise<ClosePeriodPlan>;
  lockClosePeriod: (request: LockClosePeriodRequest) => Promise<ClosePeriodLockResult>;
  buildPackage: (request: AccountingReportPackageRequest) => Promise<AccountingReportPackageBundle>;
  certifyPackage: (request: CertifyAccountingReportPackageRequest) => Promise<AccountingReportPackageBundle>;
  getExportManifest: (packageId: string, artifactId: string) => Promise<ReportExportArtifactManifest>;
  listPackages: (query: AccountingReportPackageHistoryQuery) => Promise<AccountingReportPackageBundle[]>;
}


export type GovernanceCashFlowRowViewModel = AccountingCashFlowRowViewModel;
export type GovernanceCashFlowViewState = AccountingCashFlowViewState;
export type GovernanceLoadingViewState = AccountingLoadingViewState;
export type GovernanceReportingViewState = AccountingReportingViewState;
export type GovernanceReportingBackendLink = AccountingReportingBackendLink;
export type GovernanceTrialBalanceState = AccountingTrialBalanceState;
export type GovernanceTrialBalanceBasisOption = AccountingTrialBalanceBasisOption;
export type GovernanceTrialBalanceRowViewModel = AccountingTrialBalanceRowViewModel;
export type GovernanceTrialBalanceDetailViewState = AccountingTrialBalanceDetailViewState;
export type GovernanceBasisBridgeRowViewModel = AccountingBasisBridgeRowViewModel;
export type GovernanceBasisBridgeViewState = AccountingBasisBridgeViewState;
export type GovernanceTrialBalanceViewState = AccountingTrialBalanceViewState;

const ACCOUNTING_BASIS_OPTIONS: Array<Pick<AccountingTrialBalanceBasisOption, "id" | "label" | "description">> = [
  {
    id: "Primary",
    label: "Primary",
    description: "Legacy run evidence and current report-pack baseline."
  },
  {
    id: "Gaap",
    label: "GAAP",
    description: "Accrual policy books and configured adjustment rules."
  },
  {
    id: "Cash",
    label: "Cash",
    description: "Settlement and payment-driven recognition."
  },
  {
    id: "Tax",
    label: "Tax",
    description: "Configured lot-relief and taxable recognition policy."
  },
  {
    id: "Statutory",
    label: "Statutory",
    description: "Statutory-only presentation and adjustment policy."
  }
];

const defaultAccountingReconciliationServices: AccountingReconciliationServices = {
  getBreakQueue: () => getReconciliationBreakQueue(),
  reviewBreak: (request) => reviewReconciliationBreak(request),
  resolveBreak: (request) => resolveReconciliationBreak(request),
  getTrialBalance: (runId) => getRunTrialBalance(runId),
  getCalibrationSummary: () => getReconciliationCalibrationSummary(),
  getStatementRuns: () => getReconciliationStatementRuns(),
  getStatementRun: (runId) => getReconciliationStatementRun(runId),
  previewTransactionLab: (request) => previewInvestmentAccountingTransaction(request)
};

const defaultAccountingReportingServices: AccountingReportingServices = {
  runAnalysisExport: (profileId) => runAnalysisExport(profileId)
};

export function useAccountingCashFlowViewModel(
  cashFlow: AccountingCashFlowSummary | null,
  pathname: string,
  workstream: AccountingWorkstream
) {
  return useMemo(
    () => buildAccountingCashFlowViewState(cashFlow, pathname, workstream),
    [cashFlow, pathname, workstream]
  );
}

export function useGovernanceCashFlowViewModel(
  cashFlow: AccountingCashFlowSummary | null,
  pathname: string,
  workstream: AccountingWorkstream
) {
  return useAccountingCashFlowViewModel(cashFlow, pathname, workstream);
}

export function buildAccountingLoadingViewState(pathname: string): AccountingLoadingViewState {
  const workspaceLabel = pathname.startsWith(WORKSTATION_ROUTE_CATALOG.reporting) ? "Reporting" : "Accounting";
  const slug = workspaceLabel.toLowerCase();
  const workstreamLabel = workspaceLabel === "Accounting"
    ? buildAccountingTaskMode(pathname).label
    : "Reporting";
  const accountingStatusItems: AccountingLoadingStatusItemViewModel[] = [
    {
      id: "ledger-reconciliation",
      label: "Ledger and reconciliation",
      detail: "Loading close metrics, reconciliation runs, open breaks, cash-flow evidence, and trial-balance rows."
    },
    {
      id: "approvals-exceptions",
      label: "Approvals and exceptions",
      detail: "Preparing dedicated approval and exception workstreams from close-control data."
    },
    {
      id: "security-reporting",
      label: "Security and reporting evidence",
      detail: "Loading Security Master coverage, report profiles, external GL evidence, and retained report-pack context."
    }
  ];
  const reportingStatusItems: AccountingLoadingStatusItemViewModel[] = [
    {
      id: "report-packs",
      label: "Report packs",
      detail: "Loading governed report-pack runs, retained manifests, and evidence-bundle readiness."
    },
    {
      id: "approvals",
      label: "Approval context",
      detail: "Preparing accounting approval and exception handoffs for report evidence review."
    },
    {
      id: "exports",
      label: "Export setup",
      detail: "Loading profile, recipient, dictionary, and loader-script state."
    }
  ];
  const accountingActions: AccountingLoadingActionViewModel[] = [
    {
      id: "continuity",
      label: "Open continuity",
      detail: "Review close workflow gates while workspace data finishes loading.",
      href: WORKSTATION_ROUTE_CATALOG.accountingOperationsContinuity,
      ariaLabel: "Open Accounting operations continuity while Accounting loads"
    },
    {
      id: "entity-setup",
      label: "Entity setup",
      detail: "Configure fund structure, account context, and setup evidence.",
      href: WORKSTATION_ROUTE_CATALOG.accountingEntitySetup,
      ariaLabel: "Open Accounting entity setup while Accounting loads"
    },
    {
      id: "provider-posture",
      label: "Provider posture",
      detail: "Check source and provider diagnostics before relying on fresh close data.",
      href: WORKSTATION_ROUTE_CATALOG.dataProviders,
      ariaLabel: "Open Data provider posture while Accounting loads"
    },
    {
      id: "report-evidence",
      label: "Report evidence",
      detail: "Open retained report-pack evidence for close and audit review.",
      href: WORKSTATION_ROUTE_CATALOG.reportingEvidence,
      ariaLabel: "Open Reporting evidence while Accounting loads"
    }
  ];
  const reportingActions: AccountingLoadingActionViewModel[] = [
    {
      id: "report-evidence",
      label: "Report evidence",
      detail: "Open retained report-pack evidence and manifests.",
      href: WORKSTATION_ROUTE_CATALOG.reportingEvidence,
      ariaLabel: "Open Reporting evidence while Reporting loads"
    },
    {
      id: "approvals",
      label: "Accounting approvals",
      detail: "Review close approvals linked to reporting release.",
      href: WORKSTATION_ROUTE_CATALOG.accountingApprovals,
      ariaLabel: "Open Accounting approvals while Reporting loads"
    },
    {
      id: "exceptions",
      label: "Exceptions",
      detail: "Review exception evidence that may block report release.",
      href: WORKSTATION_ROUTE_CATALOG.accountingExceptions,
      ariaLabel: "Open Accounting exceptions while Reporting loads"
    }
  ];

  return {
    role: "status",
    ariaBusy: true,
    ariaLive: "polite",
    titleId: `${slug}-workspace-loading-title`,
    detailId: `${slug}-workspace-loading-detail`,
    eyebrow: `${workspaceLabel} workspace data`,
    title: `Loading ${workspaceLabel}`,
    detail: workspaceLabel === "Reporting"
      ? "Waiting for report-pack, governed export, and approval summaries from workspace data."
      : "Waiting for ledger, reconciliation, cash-flow, and Security Master summaries from workspace data.",
    routeLabel: pathname,
    workstreamLabel,
    statusItemsLabel: `${workspaceLabel} workspace data loading`,
    statusItems: workspaceLabel === "Reporting" ? reportingStatusItems : accountingStatusItems,
    actionsLabel: `${workspaceLabel} actions available while loading`,
    actions: workspaceLabel === "Reporting" ? reportingActions : accountingActions
  };
}

export function buildGovernanceLoadingViewState(pathname: string): AccountingLoadingViewState {
  return buildAccountingLoadingViewState(pathname);
}

export function useAccountingReportingViewModel(
  reporting: AccountingReportingSummary | null,
  services: AccountingReportingServices = defaultAccountingReportingServices
) {
  const [selectedProfileId, setSelectedProfileId] = useState<string | null>(null);
  const [exportBusy, setExportBusy] = useState(false);
  const [exportStatus, setExportStatus] = useState<{
    text: string;
    tone: AccountingReportingViewState["exportStatusTone"];
    role: AccountingReportingViewState["exportStatusRole"];
  } | null>(null);
  const viewState = useMemo(
    () => buildAccountingReportingViewState({
      reporting,
      selectedProfileId,
      exportBusy,
      exportStatus
    }),
    [exportBusy, exportStatus, reporting, selectedProfileId]
  );
  const selectProfile = useCallback((profileId: string) => {
    setSelectedProfileId(profileId);
    setExportStatus(null);
  }, []);
  const runExport = useCallback(async () => {
    if (!viewState.selectedExportProfileId || exportBusy) {
      return;
    }

    const profileId = viewState.selectedExportProfileId;
    setExportBusy(true);
    setExportStatus({
      text: `Starting export for ${profileId}.`,
      tone: "neutral",
      role: "status"
    });

    try {
      const result = await services.runAnalysisExport(profileId);
      setExportStatus(formatReportingExportResult(result));
    } catch (err) {
      setExportStatus({
        text: err instanceof Error && err.message.trim()
          ? `Export failed: ${err.message}`
          : "Export failed.",
        tone: "danger",
        role: "alert"
      });
    } finally {
      setExportBusy(false);
    }
  }, [exportBusy, services, viewState.selectedExportProfileId]);

  return {
    ...viewState,
    selectProfile,
    runExport
  };
}


export function useGovernanceReportingViewModel(
  reporting: AccountingReportingSummary | null,
  services: AccountingReportingServices = defaultAccountingReportingServices
) {
  return useAccountingReportingViewModel(reporting, services);
}

export function useAccountingReconciliationViewModel(
  data: AccountingWorkspaceResponse | null,
  workstream: AccountingWorkstream,
  services: AccountingReconciliationServices = defaultAccountingReconciliationServices,
  systemReconciliation: AccountingSystemReconciliationSummary | null = null,
  operatorIdentity: string | null = null,
  selectionState?: ReconciliationSelectionState
) {
  const [localSelectedRunId, setLocalSelectedRunId] = useState<string | null>(null);
  const selectedRunId = selectionState ? selectionState.selectedRunId : localSelectedRunId;
  const setSelectedRunId = selectionState?.selectRun ?? setLocalSelectedRunId;
  const scopeKey = selectionState?.scopeKey ?? "";
  const scopeRevisionRef = useRef({ key: scopeKey, revision: 0 });
  if (scopeRevisionRef.current.key !== scopeKey) {
    scopeRevisionRef.current = { key: scopeKey, revision: scopeRevisionRef.current.revision + 1 };
  }
  const scopeRevision = scopeRevisionRef.current.revision;
  const [breakQueue, setBreakQueue] = useState<ReconciliationBreakQueueItem[]>(data?.breakQueue ?? []);
  const [breakQueueLoading, setBreakQueueLoading] = useState(false);
  const [breakQueueReadScope, setBreakQueueReadScope] = useState<number | null>(null);
  const [statementRunsReadScope, setStatementRunsReadScope] = useState<number | null>(null);
  const [breakQueueError, setBreakQueueError] = useState<ApiErrorDisplay | null>(null);
  const [breakAction, setBreakAction] = useState<ReconciliationBreakAction | null>(null);
  const [breakActionError, setBreakActionError] = useState<ApiErrorDisplay | null>(null);
  const [localSelectedBreakId, setLocalSelectedBreakId] = useState<string | null>(null);
  const selectedBreakId = selectionState ? selectionState.selectedBreakId : localSelectedBreakId;
  const setSelectedBreakId = selectionState?.selectBreak ?? setLocalSelectedBreakId;
  const [trialBalance, setTrialBalance] = useState<LedgerTrialBalanceLine[]>([]);
  const [selectedTrialBalanceRowId, setSelectedTrialBalanceRowId] = useState<string | null>(null);
  const [selectedAccountingBasis, setSelectedAccountingBasis] = useState<AccountingBasisKind>(DEFAULT_ACCOUNTING_BASIS);
  const [ledgerAccountFilter, setLedgerAccountFilter] = useState("");
  const [trialBalanceLoading, setTrialBalanceLoading] = useState(false);
  const [trialBalanceError, setTrialBalanceError] = useState<ApiErrorDisplay | null>(null);
  const [calibrationSummary, setCalibrationSummary] = useState<ReconciliationCalibrationSummary | null>(null);
  const [statementRuns, setStatementRuns] = useState<StatementRunSummary[]>([]);
  const [statementRunsLoading, setStatementRunsLoading] = useState(false);
  const [statementRunsError, setStatementRunsError] = useState<ApiErrorDisplay | null>(null);
  const [calibrationLoading, setCalibrationLoading] = useState(false);
  const [calibrationError, setCalibrationError] = useState<ApiErrorDisplay | null>(null);
  const [selectedCalibrationProfileId, setSelectedCalibrationProfileId] = useState<string | null>(null);
  const [transactionLabBusy, setTransactionLabBusy] = useState(false);
  const [transactionLabPreview, setTransactionLabPreview] = useState<InvestmentAccountingTransactionLabPreview | null>(null);
  const [transactionLabError, setTransactionLabError] = useState<ApiErrorDisplay | null>(null);
  const calibrationRequestRevisionRef = useRef(0);
  const statementRunsRequestRevisionRef = useRef(0);

  const activeScopeRef = useRef(scopeRevision);
  activeScopeRef.current = scopeRevision;
  const actionContextRef = useRef({ scopeRevision, runId: selectedRunId, revision: 0 });
  if (actionContextRef.current.scopeRevision !== scopeRevision || actionContextRef.current.runId !== selectedRunId) {
    actionContextRef.current = { scopeRevision, runId: selectedRunId, revision: actionContextRef.current.revision + 1 };
  }
  const actionContextRevision = actionContextRef.current.revision;
  useEffect(() => {
    setBreakAction(null);
    setBreakActionError(null);
  }, [actionContextRevision]);
  useEffect(() => {
    setTransactionLabPreview(null);
    setTransactionLabError(null);
    setTransactionLabBusy(false);
  }, [scopeRevision]);

  const reconciliationQueue = useMemo(() => data?.reconciliationQueue ?? [], [data?.reconciliationQueue]);
  const selectedReconciliation = useMemo(
    () => resolveSelectedReconciliation(reconciliationQueue, selectedRunId),
    [reconciliationQueue, selectedRunId]
  );

  const activeRunRef = useRef(selectedReconciliation?.runId);
  activeRunRef.current = selectedReconciliation?.runId;

  useEffect(() => {
    // URL selection is authoritative, including a retained record that is no longer available.
    if (selectionState) return;
    const hasSelectedRun = selectedRunId
      ? reconciliationQueue.some((item) => item.runId === selectedRunId) || statementRuns.some((item) => item.runId === selectedRunId)
      : false;

    if (hasSelectedRun) {
      return;
    }

    const nextRunId = statementRuns[0]?.runId ?? reconciliationQueue[0]?.runId ?? null;
    setSelectedRunId(nextRunId);
  }, [reconciliationQueue, selectedRunId, selectionState, setSelectedRunId, statementRuns]);

  useEffect(() => {
    const nextBreakQueue = data?.breakQueue ?? [];
    setBreakQueue((current) => (
      areReconciliationBreakQueuesEquivalent(current, nextBreakQueue)
        ? current
        : nextBreakQueue
    ));
  }, [data?.breakQueue]);

  useEffect(() => {
    if (workstream !== "reconciliation" && workstream !== "exceptions") {
      return;
    }

    let cancelled = false;
    setBreakQueueLoading(true);
    setBreakQueueError(null);

    services.getBreakQueue()
      .then((rows) => {
        if (!cancelled) {
          setBreakQueue(rows);
          setBreakQueueReadScope(scopeRevision);
        }
      })
      .catch((err) => {
        if (!cancelled) {
          setBreakQueue(data?.breakQueue ?? []);
          setBreakQueueError(describeApiError(err, "Reconciliation break queue failed to load."));
        }
      })
      .finally(() => {
        if (!cancelled) {
          setBreakQueueLoading(false);
        }
      });

    return () => {
      cancelled = true;
    };
  }, [data?.breakQueue, scopeRevision, services, workstream]);

  const refreshCalibrationSummary = useCallback(() => {
    const revision = calibrationRequestRevisionRef.current + 1;
    calibrationRequestRevisionRef.current = revision;
    setCalibrationLoading(true);
    setCalibrationError(null);

    services.getCalibrationSummary()
      .then((summary) => {
        if (calibrationRequestRevisionRef.current === revision) {
          setCalibrationSummary(summary);
        }
      })
      .catch((err) => {
        if (calibrationRequestRevisionRef.current === revision) {
          setCalibrationError(describeApiError(err, "Calibration summary failed to load."));
        }
      })
      .finally(() => {
        if (calibrationRequestRevisionRef.current === revision) {
          setCalibrationLoading(false);
        }
      });
  }, [services]);

  useEffect(() => {
    if (workstream !== "reconciliation" && workstream !== "exceptions") {
      return;
    }

    refreshCalibrationSummary();

    return () => {
      calibrationRequestRevisionRef.current += 1;
    };
  }, [refreshCalibrationSummary, scopeRevision, workstream]);

  const refreshStatementRuns = useCallback(async () => {
    const revision = statementRunsRequestRevisionRef.current + 1;
    statementRunsRequestRevisionRef.current = revision;
    setStatementRunsLoading(true);
    setStatementRunsError(null);

    try {
      const runs = await services.getStatementRuns();
      if (statementRunsRequestRevisionRef.current === revision) {
        setStatementRuns(sortStatementRunsNewestFirst(runs));
        setStatementRunsReadScope(scopeRevision);
      }
    } catch (err) {
      if (statementRunsRequestRevisionRef.current === revision) {
        setStatementRunsError(describeApiError(err, "Statement runs failed to load."));
      }
    } finally {
      if (statementRunsRequestRevisionRef.current === revision) {
        setStatementRunsLoading(false);
      }
    }
  }, [scopeRevision, services]);

  useEffect(() => {
    if (workstream !== "reconciliation" && workstream !== "exceptions") {
      statementRunsRequestRevisionRef.current += 1;
      setStatementRunsLoading(false);
      return;
    }

    void refreshStatementRuns();

    return () => {
      statementRunsRequestRevisionRef.current += 1;
    };
  }, [refreshStatementRuns, scopeRevision, workstream]);

  useEffect(() => {
    if (!selectedReconciliation || workstream !== "ledger") {
      setTrialBalance([]);
      setTrialBalanceError(null);
      setTrialBalanceLoading(false);
      return;
    }

    let cancelled = false;
    setTrialBalanceLoading(true);
    setTrialBalanceError(null);

    services.getTrialBalance(selectedReconciliation.runId)
      .then((rows) => {
        if (!cancelled) {
          setTrialBalance(rows);
        }
      })
      .catch((err) => {
        if (!cancelled) {
          setTrialBalance([]);
          setTrialBalanceError(describeApiError(err, "Trial balance failed to load."));
        }
      })
      .finally(() => {
        if (!cancelled) {
          setTrialBalanceLoading(false);
        }
      });

    return () => {
      cancelled = true;
    };
  }, [selectedReconciliation, services, workstream]);

  useEffect(() => {
    setTransactionLabPreview(null);
    setTransactionLabError(null);
    setTransactionLabBusy(false);
  }, [selectedReconciliation?.runId]);

  const canActOnCurrentQueue = useCallback((breakId: string) => !selectionState || (
    breakQueueReadScope === scopeRevision && !breakQueueLoading && !breakQueueError
    && breakQueue.some((item) => item.breakId === breakId && (workstream !== "reconciliation" || !selectedRunId || item.runId === selectedRunId))
  ), [breakQueue, breakQueueError, breakQueueLoading, breakQueueReadScope, scopeRevision, selectedRunId, selectionState, workstream]);

  const assignBreak = useCallback(async (breakId: string) => {
    if (!canActOnCurrentQueue(breakId)) {
      setBreakActionError({ summary: "Refresh the current reconciliation queue before taking an action.", details: [] });
      return;
    }
    const requestScope = scopeRevision;
    const requestActionContext = actionContextRevision;
    setBreakAction({ breakId, command: "assign" });
    setBreakActionError(null);

    try {
      const operation = await services.reviewBreak({
        breakId,
        ...(operatorIdentity ? { assignedTo: operatorIdentity, reviewedBy: operatorIdentity } : {})
      });
      const updated = requireSuccessfulReconciliationCasework(operation);
      // A committed result remains authoritative for this queue when the operator changes runs.
      if (activeScopeRef.current !== requestScope) return;
      setBreakQueue((current) => replaceBreakQueueItem(current, updated));
    } catch (err) {
      if (activeScopeRef.current === requestScope && actionContextRef.current.revision === requestActionContext) setBreakActionError(describeApiError(err, "Break assignment failed."));
    } finally {
      if (activeScopeRef.current === requestScope && actionContextRef.current.revision === requestActionContext) setBreakAction(null);
    }
  }, [actionContextRevision, canActOnCurrentQueue, operatorIdentity, scopeRevision, services]);

  const resolveBreak = useCallback(async (
    breakId: string,
    status: ResolveReconciliationBreakRequest["status"],
    operatorRationale: string | null | undefined
  ) => {
    if (!canActOnCurrentQueue(breakId)) {
      setBreakActionError({ summary: "Refresh the current reconciliation queue before taking an action.", details: [] });
      return;
    }
    const requestScope = scopeRevision;
    const requestActionContext = actionContextRevision;
    const trimmedRationale = (operatorRationale ?? "").trim();
    if (!trimmedRationale) {
      setBreakActionError({
        summary: "Operator rationale is required.",
        details: []
      });
      return;
    }

    const command: ReconciliationBreakCommand = status === "Resolved" ? "resolve" : "dismiss";
    setBreakAction({ breakId, command });
    setBreakActionError(null);

    try {
      const operation = await services.resolveBreak({
        breakId,
        status,
        ...(operatorIdentity ? { resolvedBy: operatorIdentity } : {}),
        resolutionNote: trimmedRationale,
        operatorRationale: trimmedRationale
      });
      const updated = requireSuccessfulReconciliationCasework(operation);
      // A committed result remains authoritative for this queue when the operator changes runs.
      if (activeScopeRef.current !== requestScope) return;
      setBreakQueue((current) => replaceBreakQueueItem(current, updated));
      void recordActivationOutcome(ACTIVATION_OUTCOME_KEYS.validationResolved);
    } catch (err) {
      if (activeScopeRef.current === requestScope && actionContextRef.current.revision === requestActionContext) setBreakActionError(describeApiError(err, "Break resolution failed."));
    } finally {
      if (activeScopeRef.current === requestScope && actionContextRef.current.revision === requestActionContext) setBreakAction(null);
    }
  }, [actionContextRevision, canActOnCurrentQueue, operatorIdentity, scopeRevision, services]);

  // Bulk triage (C2) returns the committed cases from the governed bulk endpoint. Like the
  // single-case handlers, the update is bound to the scope the action started in: a response that
  // lands after the operator changed scope is not merged into (or prepended to) the new queue.
  const beginBreakQueueUpdate = useCallback(() => {
    const requestScope = activeScopeRef.current;
    return (items: ReconciliationBreakQueueItem[]) => {
      if (items.length === 0 || activeScopeRef.current !== requestScope) return;
      setBreakQueue((current) => items.reduce((queue, item) => replaceBreakQueueItem(queue, item), current));
    };
  }, []);

  useEffect(() => {
    if (!selectionState?.replaceSelection || workstream !== "reconciliation"
      || breakQueueReadScope !== scopeRevision || statementRunsReadScope !== scopeRevision
      || breakQueueLoading || statementRunsLoading || breakQueueError || statementRunsError) return;
    const knownBreakRun = breakQueue.find((item) => reconciliationQueue.some((run) => run.runId === item.runId)
      || statementRuns.some((run) => run.runId === item.runId))?.runId;
    const runId = selectedRunId ?? knownBreakRun ?? statementRuns[0]?.runId ?? reconciliationQueue[0]?.runId ?? null;
    const breakId = selectedBreakId ?? breakQueue.find((item) => !runId || item.runId === runId)?.breakId ?? null;
    if (runId !== selectedRunId || breakId !== selectedBreakId) selectionState.replaceSelection(runId, breakId);
  }, [breakQueue, breakQueueError, breakQueueLoading, breakQueueReadScope, reconciliationQueue, scopeRevision,
    selectedBreakId, selectedRunId, selectionState, statementRuns, statementRunsError, statementRunsLoading,
    statementRunsReadScope, workstream]);

  const scopedBreakQueue = useMemo(
    () => selectionState && breakQueueReadScope !== scopeRevision ? []
      : selectionState && workstream === "reconciliation" && selectedRunId
        ? breakQueue.filter((item) => item.runId === selectedRunId)
        : breakQueue,
    [breakQueue, breakQueueReadScope, scopeRevision, selectedRunId, selectionState, workstream]
  );
  const breakQueueState = useMemo(() => {
    const state = buildReconciliationBreakQueueState({
      breakQueue: scopedBreakQueue,
      selectedBreakId,
      loading: breakQueueLoading || Boolean(selectionState && breakQueueReadScope !== scopeRevision && !breakQueueError),
      loadError: breakQueueError,
      action: breakAction,
      actionError: breakActionError
    });
    if (!selectionState || (!breakQueueLoading && !breakQueueError)) return state;
    const reason = "Refresh the current reconciliation queue before taking an action.";
    return { ...state, rows: state.rows.map((row) => ({
      ...row, canAssign: false, canResolve: false, canDismiss: false,
      assignDisabledReason: reason, resolveDisabledReason: reason, dismissDisabledReason: reason
    })) };
  }, [breakAction, breakActionError, scopedBreakQueue, breakQueueError, breakQueueLoading,
    breakQueueReadScope, scopeRevision, selectedBreakId, selectionState]);
  const trialBalanceView = useMemo(
    () => buildAccountingTrialBalanceViewState({
      runId: selectedReconciliation?.runId ?? null,
      rows: trialBalance,
      selectedRowId: selectedTrialBalanceRowId,
      selectedBasis: selectedAccountingBasis,
      accountFilter: ledgerAccountFilter,
      loading: trialBalanceLoading,
      error: trialBalanceError
    }),
    [ledgerAccountFilter, selectedAccountingBasis, selectedReconciliation?.runId, selectedTrialBalanceRowId, trialBalance, trialBalanceError, trialBalanceLoading]
  );
  const selectAccountingBasis = useCallback((basis: AccountingBasisKind) => {
    setSelectedAccountingBasis(basis);
    setSelectedTrialBalanceRowId(null);
  }, []);
  const updateLedgerAccountFilter = useCallback((value: string) => {
    setLedgerAccountFilter(value);
    setSelectedTrialBalanceRowId(null);
  }, []);
  const calibrationViewState = useMemo(
    () => buildCalibrationSummaryViewState(
      calibrationSummary,
      calibrationLoading,
      calibrationError,
      selectedCalibrationProfileId
    ),
    [calibrationSummary, calibrationLoading, calibrationError, selectedCalibrationProfileId]
  );
  const selectCalibrationProfile = useCallback((profileId: string) => {
    setSelectedCalibrationProfileId(profileId);
  }, []);
  const calibrationView = useMemo(
    () => ({
      ...calibrationViewState,
      selectProfile: selectCalibrationProfile,
      refresh: refreshCalibrationSummary
    }),
    [calibrationViewState, refreshCalibrationSummary, selectCalibrationProfile]
  );
  const detailActions = useMemo(
    () => selectedReconciliation ? buildReconciliationDetailActions(selectedReconciliation) : null,
    [selectedReconciliation]
  );
  const detailView = useMemo(
    () => selectedReconciliation ? buildReconciliationDetailViewState(selectedReconciliation) : null,
    [selectedReconciliation]
  );
  const queuePanelView = useMemo(
    () => buildReconciliationQueuePanelViewState(reconciliationQueue, selectedReconciliation?.runId ?? null),
    [reconciliationQueue, selectedReconciliation?.runId]
  );
  const statementRunsView = useMemo(
    () => buildReconciliationStatementRunsViewState({
      statementRuns,
      fallbackQueue: reconciliationQueue,
      selectedRunId,
      loading: statementRunsLoading,
      error: statementRunsError
    }),
    [reconciliationQueue, selectedRunId, statementRuns, statementRunsError, statementRunsLoading]
  );
  const comparisonView = useMemo(
    () => buildReconciliationComparisonViewState({
      statementRuns,
      fallbackQueue: reconciliationQueue,
      selectedRunId,
      cashFlow: data?.cashFlow ?? null,
      systemReconciliation
    }),
    [data?.cashFlow, reconciliationQueue, selectedRunId, statementRuns, systemReconciliation]
  );
  const exceptionWorkbench = useMemo(
    () => buildOperationalExceptionWorkbenchState({
      reconciliationQueue,
      breakRows: breakQueueState.rows
    }),
    [breakQueueState.rows, reconciliationQueue]
  );
  const transactionLabView = useMemo(
    () => {
      const hasSelection = Boolean(selectedReconciliation);
      const hasPreview = Boolean(transactionLabPreview);
      const hasError = Boolean(transactionLabError);
      const canPreview = hasSelection && !transactionLabBusy;
      const statusTone: "default" | "success" | "warning" | "danger" = hasError
        ? "danger"
        : hasPreview && transactionLabPreview?.ledgerImpact.hasValidationWarnings
          ? "warning"
          : hasPreview
            ? "success"
            : "default";

      const requestSummaryLabel = !hasSelection
        ? "Select run"
        : transactionLabBusy
          ? "Requesting projection"
          : hasError
            ? "Request failed"
            : hasPreview
              ? "Projection ready"
              : "Ready for request";

      const statusText = !hasSelection
        ? "Select a reconciliation run before projecting the expected accounting effect."
        : transactionLabBusy
          ? "Requesting an expected accounting projection from Meridian accounting services."
          : hasError
            ? transactionLabError?.summary ?? "Transaction Lab projection failed."
            : hasPreview
              ? `Expected accounting projection ${transactionLabPreview?.previewId ?? ""} loaded; no journal has been posted.`
              : "Ready to project the expected, unposted accounting effect through Transaction Lab.";

      const impactRows = transactionLabPreview?.trialBalanceImpact.map((row, index) => ({
        id: `${row.accountName}-${index}`,
        label: row.accountName,
        value: formatSignedCurrency(row.balanceDelta),
        tone: row.balanceDelta > 0 ? "success" as const : row.balanceDelta < 0 ? "danger" as const : "default" as const
      })) ?? [];

      return {
        title: "Investment Accounting Transaction Lab",
        description: "Review expected and projected accounting effects before any posting candidate or reconciliation action.",
        statusTone,
        requestSummaryLabel,
        statusRole: hasError ? "alert" as const : "status" as const,
        statusText,
        journalLineCountLabel: hasPreview && transactionLabPreview
          ? formatCount(transactionLabPreview.journalPreview.lines.length, "line")
          : "Pending projection",
        ledgerImpactLabel: hasPreview && transactionLabPreview
          ? formatSignedCurrency(transactionLabPreview.ledgerImpact.netBalanceDelta)
          : "Pending projection",
        reconciliationLabel: hasPreview && transactionLabPreview
          ? transactionLabPreview.reconciliationExpectation.expectedState
          : selectedReconciliation?.status ?? "No run selected",
        evidenceLabel: hasPreview && transactionLabPreview
          ? formatCount(transactionLabPreview.evidenceIds.length, "evidence item")
          : selectedReconciliation?.runId ?? "Pending evidence",
        impactRows,
        canPreview,
        disabledReason: hasSelection
          ? null
          : "Select a reconciliation run before requesting a Transaction Lab accounting projection.",
        busy: transactionLabBusy,
        previewButtonLabel: transactionLabBusy ? "Projecting accounting effect..." : "Project accounting effect",
        previewButtonAriaLabel: "Project expected accounting transaction effect"
      };
    },
    [selectedReconciliation, transactionLabBusy, transactionLabError, transactionLabPreview]
  );
  const runTransactionLabPreview = useCallback(async () => {
    if (!selectedReconciliation || transactionLabBusy) {
      return;
    }

    const requestScope = scopeRevision;
    const requestRunId = selectedReconciliation.runId;
    setTransactionLabBusy(true);
    setTransactionLabError(null);

    try {
      const preview = await services.previewTransactionLab(buildTransactionLabPreviewRequest(selectedReconciliation));
      if (activeScopeRef.current === requestScope && activeRunRef.current === requestRunId) setTransactionLabPreview(preview);
    } catch (err) {
      if (activeScopeRef.current !== requestScope || activeRunRef.current !== requestRunId) return;
      setTransactionLabPreview(null);
      setTransactionLabError(describeApiError(err, "Transaction Lab preview failed."));
    } finally {
      if (activeScopeRef.current === requestScope && activeRunRef.current === requestRunId) setTransactionLabBusy(false);
    }
  }, [scopeRevision, selectedReconciliation, services, transactionLabBusy]);

  return {
    reconciliationQueue,
    sourceRows: scopedBreakQueue,
    applyCaseworkUpdate: (updated: ReconciliationBreakQueueItem) => {
      if (scopedBreakQueue.some((item) => item.breakId === updated.breakId)) {
        setBreakQueue((current) => {
          const retained = current.find((item) => item.breakId === updated.breakId);
          if (retained?.version != null && (updated.version == null || updated.version < retained.version)) return current;
          return replaceBreakQueueItem(current, updated);
        });
      }
    },
    selectionNotice: selectedBreakId && (!selectionState || breakQueueReadScope === scopeRevision) && !breakQueueLoading && !breakQueueError && !scopedBreakQueue.some((item) => item.breakId === selectedBreakId)
      ? "The selected break is no longer available in this queue. Select another break to continue."
      : selectedRunId && !statementRunsLoading && !statementRunsError && !reconciliationQueue.some((item) => item.runId === selectedRunId) && !statementRuns.some((item) => item.runId === selectedRunId)
        ? "The selected run is no longer available. Select another run to continue."
        : null,
    selectedRunId,
    selectedReconciliation,
    selectRun: setSelectedRunId,
    detailActions,
    detailView,
    queuePanelView,
    statementRunsView,
    comparisonView,
    exceptionWorkbench,
    refreshStatementRuns,
    transactionLabView,
    runTransactionLabPreview,
    trialBalance,
    trialBalanceLoading,
    trialBalanceErrorText: trialBalanceError?.summary ?? null,
    trialBalanceView,
    selectTrialBalanceRow: setSelectedTrialBalanceRowId,
    selectAccountingBasis,
    updateLedgerAccountFilter,
    breakAction,
    selectBreak: (breakId: string | null) => {
      if (selectionState) selectionState.selectBreak(breakId, breakQueue.find((item) => item.breakId === breakId)?.runId);
      else setSelectedBreakId(breakId);
    },
    assignBreak,
    resolveBreak,
    beginBreakQueueUpdate,
    calibrationSummary,
    calibrationLoading,
    calibrationErrorText: calibrationError?.summary ?? null,
    calibrationView,
    ...breakQueueState
  };
}

export function useGovernanceReconciliationViewModel(
  data: AccountingWorkspaceResponse | null,
  workstream: AccountingWorkstream,
  services: AccountingReconciliationServices = defaultAccountingReconciliationServices
) {
  return useAccountingReconciliationViewModel(data, workstream, services);
}

export function buildAccountingCashFlowViewState(
  cashFlow: AccountingCashFlowSummary | null,
  pathname: string,
  workstream: AccountingWorkstream
): AccountingCashFlowViewState {
  const routePath = pathname || WORKSTATION_ROUTE_CATALOG.accounting;
  const contextLabel = cashFlowContextLabel(workstream);

  if (!cashFlow) {
    return {
      eyebrow: "Cash Flow",
      title: "Cash-flow evidence loading",
      description: `${contextLabel} is waiting for shared accounting cash-flow data.`,
      routePath,
      statusLabel: "Pending",
      statusTone: "warning",
      statusAriaLabel: "Cash-flow status pending",
      ariaLabel: `Cash-flow evidence for ${contextLabel} at ${routePath}`,
      rowGroupLabel: "Cash-flow evidence rows",
      rows: [],
      statusAnnouncement: "Cash-flow evidence is loading."
    };
  }

  const statusTone = normalizeCashFlowTone(cashFlow.tone, cashFlow.netVariance);
  const statusLabel = cashFlow.netVariance === 0
    ? "Balanced"
    : cashFlow.runsWithCashVariance > 0
      ? "Variance review"
      : "Observe";
  const rows: AccountingCashFlowRowViewModel[] = [
    buildCashFlowRow("portfolio-cash", "Portfolio cash", cashFlow.totalCash, "default"),
    buildCashFlowRow("ledger-cash", "Ledger cash", cashFlow.totalLedgerCash, "default"),
    buildCashFlowRow("net-variance", "Net variance", cashFlow.netVariance, statusTone),
    buildCashFlowRow("financing", "Financing", cashFlow.totalFinancing, "default"),
    buildCashFlowCountRow("cash-signal-runs", "Runs with cash signals", cashFlow.runsWithCashSignals, "default"),
    buildCashFlowCountRow("variance-runs", "Runs with variance", cashFlow.runsWithCashVariance, statusTone)
  ];

  return {
    eyebrow: "Cash Flow",
    title: cashFlow.summary,
    description: `${contextLabel} at ${routePath} reuses the shared accounting/reporting cash-flow summary data.`,
    routePath,
    statusLabel,
    statusTone,
    statusAriaLabel: `Cash-flow status ${statusLabel}. Net variance ${formatCurrency(cashFlow.netVariance)}.`,
    ariaLabel: `Cash-flow evidence for ${contextLabel} at ${routePath}`,
    rowGroupLabel: "Cash-flow evidence rows",
    rows,
    statusAnnouncement: `${statusLabel}: ${cashFlow.summary}`
  };
}

export function buildGovernanceCashFlowViewState(
  cashFlow: AccountingCashFlowSummary | null,
  pathname: string,
  workstream: AccountingWorkstream
): AccountingCashFlowViewState {
  return buildAccountingCashFlowViewState(cashFlow, pathname, workstream);
}

export function buildAccountingReportingViewState({
  reporting,
  selectedProfileId,
  exportBusy = false,
  exportStatus = null
}: {
  reporting: AccountingReportingSummary | null;
  selectedProfileId: string | null;
  exportBusy?: boolean;
  exportStatus?: {
    text: string;
    tone: AccountingReportingViewState["exportStatusTone"];
    role: AccountingReportingViewState["exportStatusRole"];
  } | null;
}): AccountingReportingViewState {
  const profileCount = reporting?.profileCount ?? 0;
  const profiles = reporting?.profiles ?? [];
  const visibleProfiles = profiles.slice(0, 4);
  const recommendedProfiles = new Set((reporting?.recommendedProfiles ?? []).map((value) => value.toLowerCase()));
  const selectedId = selectedProfileId && visibleProfiles.some((profile) => profile.id === selectedProfileId)
    ? selectedProfileId
    : visibleProfiles[0]?.id ?? null;
  const rows = visibleProfiles.map((profile) => buildReportingProfileRow(profile, recommendedProfiles, profile.id === selectedId));
  const selectedRow = rows.find((profile) => profile.id === selectedId) ?? null;
  const selectedProfile = selectedRow ? buildReportingProfileDetail(selectedRow) : null;
  const targetSummary = formatReportPackRecipientList(reporting);
  const hiddenProfileCount = Math.max(profileCount - rows.length, 0);
  const visibleCountLabel = hiddenProfileCount > 0
    ? `Showing ${rows.length} of ${profileCount} profiles.`
    : `${formatCount(rows.length, "profile")} loaded.`;

  const exportDisabledReason = buildReportingExportDisabledReason(selectedRow, exportBusy);
  const exportCanRun = exportDisabledReason === null;

  return {
    title: "Reporting profiles",
    description: reporting?.summary ?? "Reporting profile metadata has not loaded yet.",
    countLabel: formatCount(profileCount, "profile"),
    visibleCountLabel,
    targetSummary,
    listLabel: "Reporting profile selector",
    detailId: "reporting-profile-detail",
    rows,
    hasRows: rows.length > 0,
    emptyText: "No reporting profiles available. Sync report-pack metadata before export review.",
    selectedProfile,
    statusTitle: "Report packet posture",
    statusDetail: profileCount > 0
      ? `${formatCount(profileCount, "profile")} configured. ${targetSummary}`
      : "No reporting profiles are configured for packet generation.",
    nextAction: selectedRow
      ? `Inspect ${selectedRow.name} before packet generation.`
      : "Sync reporting profile metadata before packet generation.",
    selectedExportProfileId: selectedRow?.id ?? null,
    exportButtonLabel: exportBusy ? "Export running..." : "Run reporting export",
    exportAriaLabel: buildReportingExportAriaLabel(selectedRow, exportBusy),
    exportDisabledReason,
    exportStatusText: exportStatus?.text ?? null,
    exportStatusTone: exportStatus?.tone ?? "neutral",
    exportStatusRole: exportStatus?.role ?? "status",
    exportCanRun,
    exportBusy,
    backendLinks: [
      buildAccountingReportingBackendLink("preview", "Preview report", EXPORT_API_ENDPOINTS.preview),
      buildAccountingReportingBackendLink("formats", "List export formats", EXPORT_API_ENDPOINTS.formats)
    ]
  };
}

export function buildGovernanceReportingViewState(
  options: Parameters<typeof buildAccountingReportingViewState>[0]
): AccountingReportingViewState {
  return buildAccountingReportingViewState(options);
}


function buildReportingExportDisabledReason(
  selectedRow: ReportingProfileRowViewModel | null,
  exportBusy: boolean
): string | null {
  if (exportBusy) {
    return selectedRow
      ? `${selectedRow.name} reporting export is already running.`
      : "Reporting export is already running.";
  }

  if (!selectedRow) {
    return "Load or select a reporting profile before running an export.";
  }

  return null;
}

function buildReportingExportAriaLabel(
  selectedRow: ReportingProfileRowViewModel | null,
  exportBusy: boolean
): string {
  if (exportBusy && selectedRow) {
    return `${selectedRow.name} reporting export is already running`;
  }

  if (exportBusy) {
    return "Reporting export is already running";
  }

  return selectedRow
    ? `Run reporting export for ${selectedRow.name}`
    : "Run reporting export unavailable until a reporting profile is loaded";
}

function buildAccountingReportingBackendLink(id: string, label: string, href: string): AccountingReportingBackendLink {
  return {
    id,
    label,
    href,
    ariaLabel: `Open ${label} service reference`
  };
}

export function formatReportingExportResult(result: ExportAnalysisResult): {
  text: string;
  tone: AccountingReportingViewState["exportStatusTone"];
  role: AccountingReportingViewState["exportStatusRole"];
} {
  const jobLabel = result.jobId ?? result.profileId;
  if (result.success) {
    const output = result.outputDirectory ? ` Output ${result.outputDirectory}.` : "";
    return {
      text: `Export ${jobLabel} completed with ${result.filesGenerated} file(s), ${result.totalRecords} record(s), and ${formatBytes(result.totalBytes)}.${output}`,
      tone: "success",
      role: "status"
    };
  }

  return {
    text: `Export ${jobLabel} failed: ${result.error ?? "No error detail returned."}`,
    tone: "danger",
    role: "alert"
  };
}

function buildReportingProfileRow(
  profile: AccountingReportingProfile,
  recommendedProfiles: Set<string>,
  isSelected: boolean
): ReportingProfileRowViewModel {
  const isRecommended = recommendedProfiles.has(profile.id.toLowerCase()) || recommendedProfiles.has(profile.name.toLowerCase());
  const badges: ReportingProfileBadgeViewModel[] = [
    { label: profile.dataDictionary ? "Data dictionary" : "Dictionary missing", tone: profile.dataDictionary ? "success" : "warning" },
    { label: profile.loaderScript ? "Loader script" : "No loader", tone: profile.loaderScript ? "primary" : "muted" }
  ];

  if (isRecommended) {
    badges.unshift({ label: "Recommended", tone: "primary" });
  }

  return {
    ...profile,
    formatLabel: profile.format.toUpperCase(),
    targetLabel: `Target - ${profile.targetTool}`,
    recommendationLabel: isRecommended ? "Recommended for current packet flow" : null,
    badges,
    isSelected,
    selectAriaLabel: `Inspect reporting profile ${profile.name} for ${profile.targetTool} ${profile.format}`,
    detailId: `reporting-profile-${toDomId(profile.id)}`
  };
}

function buildReportingProfileDetail(profile: ReportingProfileRowViewModel): ReportingProfileDetailViewModel {
  return {
    id: profile.detailId,
    title: `Selected reporting profile - ${profile.name}`,
    subtitle: `${profile.formatLabel} - ${profile.targetTool}`,
    description: profile.description,
    fields: [
      { label: "Profile ID", value: profile.id },
      { label: "Format", value: profile.formatLabel },
      { label: "Target", value: profile.targetTool },
      { label: "Data dictionary", value: profile.dataDictionary ? "Included" : "Missing", tone: profile.dataDictionary ? "success" : "warning" },
      { label: "Loader script", value: profile.loaderScript ? "Available" : "Not configured", tone: profile.loaderScript ? "success" : "muted" },
      { label: "Recommendation", value: profile.recommendationLabel ?? "Not recommended for current packet flow", tone: profile.recommendationLabel ? "success" : "muted" }
    ]
  };
}

export function buildAccountingTrialBalanceViewState({
  runId,
  rows,
  selectedRowId,
  selectedBasis = DEFAULT_ACCOUNTING_BASIS,
  accountFilter = "",
  loading,
  error,
  scopeLabel = null,
  currency = null,
  periodId = null
}: {
  runId: string | null;
  rows: LedgerTrialBalanceLine[];
  selectedRowId?: string | null;
  selectedBasis?: AccountingBasisKind | null;
  accountFilter?: string | null;
  loading: boolean;
  error: string | ApiErrorDisplay | null;
  /** Overrides the scope wording in labels; defaults to the strategy-run phrasing. */
  scopeLabel?: string | null;
  /** The book's base currency, when these rows come from a posted book rather than a run. */
  currency?: string | null;
  /**
   * The ledger period these rows were posted in, when they come from a posted book. Journal
   * drill-through needs it: the detail screen resolves a posted entry by period, and a link
   * without one cannot reach the entry it names.
   */
  periodId?: string | null;
}): AccountingTrialBalanceViewState {
  const detailPanelId = "trial-balance-account-detail";
  const runLabel = scopeLabel?.trim() || (runId ? "the selected ledger run" : "the current ledger selection");
  const resolvedBasis = normalizeAccountingBasis(selectedBasis);
  const normalizedAccountFilter = normalizeLedgerAccountFilter(accountFilter);
  const normalizedRows = rows.map(normalizeTrialBalanceLine);
  const basisOptions = buildTrialBalanceBasisOptions(normalizedRows, resolvedBasis);
  const bridge = buildBasisBridgeViewState(normalizedRows, resolvedBasis, runLabel);
  const basisRows = normalizedRows
    .filter((line) => line.accountingBasis === resolvedBasis)
    .map((line) => buildTrialBalanceRow(line, detailPanelId, currency));
  const accountFilterOptions = buildLedgerAccountFilterOptions(basisRows, normalizedAccountFilter);
  const basisVariance = basisRows.reduce((total, row) => total + row.balance, 0);
  const rawRows = basisRows.filter((row) => ledgerAccountRowMatchesFilter(row, normalizedAccountFilter));
  const hasRows = rawRows.length > 0;
  const resolvedSelectedRowId = rawRows.some((row) => row.rowId === selectedRowId)
    ? selectedRowId ?? null
    : rawRows[0]?.rowId ?? null;
  const viewRows = rawRows.map((row) => ({
    ...row,
    isExpanded: row.rowId === resolvedSelectedRowId
  }));
  const selectedRow = viewRows.find((row) => row.rowId === resolvedSelectedRowId) ?? null;
  const normalizedError = normalizeApiErrorDisplay(error);
  const errorText = normalizedError?.summary ?? null;
  const state: AccountingTrialBalanceState = errorText
    ? "error"
    : loading && !hasRows
      ? "loading"
      : hasRows
        ? "ready"
        : "empty";
  const loadingText = loading
    ? hasRows
      ? `Refreshing trial balance for ${runLabel}.`
      : `Loading trial balance for ${runLabel}.`
    : null;

  return {
    title: `${accountingBasisDisplayName(resolvedBasis)} trial balance`,
    description: `${accountingBasisDisplayName(resolvedBasis)} basis ledger balances for ${runLabel} grouped by account type. Values are basis per configured policy until accountant review.`,
    tableLabel: `${accountingBasisDisplayName(resolvedBasis)} trial balance lines for ${runLabel}`,
    selectedBasis: resolvedBasis,
    basisOptions,
    basisBridge: bridge,
    accountFilterLabel: "Filter by General Ledger account",
    accountFilterPlaceholder: "Account name, account id, type, symbol, or security",
    accountFilterValue: accountFilter ?? "",
    accountFilterOptions,
    filteredRowCountLabel: buildLedgerAccountFilteredCountLabel(rawRows.length, basisRows.length, normalizedAccountFilter),
    clearAccountFilterLabel: "Clear GL account filter",
    state,
    rows: viewRows,
    hasRows,
    basisVariance,
    isBasisOutOfBalance: Math.abs(basisVariance) > 0.005,
    selectedRowId: resolvedSelectedRowId,
    detailPanelId,
    selectedDetail: selectedRow ? buildTrialBalanceDetail(selectedRow, runLabel, runId, periodId) : null,
    detailEmptyTitle: "No account selected",
    detailEmptyText: hasRows
      ? "Select an account line to inspect balance evidence for report handoff."
      : normalizedAccountFilter
        ? `No ${accountingBasisDisplayName(resolvedBasis)} ledger accounts match "${accountFilter ?? ""}". Clear the filter or search another GL account.`
        : "Trial-balance account detail appears after ledger rows load.",
    detailEmptyAriaLabel: "No trial-balance account selected",
    loadingText,
    emptyTitle: "No trial balance lines",
    emptyDetail: normalizedAccountFilter && basisRows.length > 0
      ? `No ${accountingBasisDisplayName(resolvedBasis)} ledger accounts match "${accountFilter ?? ""}". Clear the GL account filter or search another account.`
      : `Meridian did not return account-balance rows for ${runLabel}. ${scopeLabel ? "Select another ledger period" : "Select another reconciliation run"} or refresh ledger evidence before report handoff.`,
    errorText,
    errorDetails: normalizedError?.details ?? [],
    statusAnnouncement: buildTrialBalanceAnnouncement({ runLabel, state, rowCount: viewRows.length, loading, errorText })
  };
}

export function buildAccountingLedgerJournalEvidenceViewState({
  runId,
  rows,
  dimensionFilter = "", scopeLabel = null, currency = null
}: {
  runId: string | null;
  rows: LedgerJournalLine[];
  dimensionFilter?: string | null;
  scopeLabel?: string | null; // overrides the run phrasing: the posted journal is period-scoped
  /**
   * The ledger book's base currency, when these rows are a posted book's. Without it the debits
   * and credits below were labelled in dollars beside a trial balance on the same page labelled
   * in the book's own currency — the same governed figures, in two currencies.
   */
  currency?: string | null;
}): AccountingLedgerJournalEvidenceViewState {
  const runLabel = scopeLabel?.trim() || (runId ? "the selected ledger run" : "the current ledger selection");
  const normalizedFilter = normalizeLedgerAccountFilter(dimensionFilter);
  const journalRows = rows
    .map((row) => buildLedgerJournalEvidenceRow(row, currency))
    .filter((row) => ledgerJournalRowMatchesDimensionFilter(row, normalizedFilter));

  return {
    title: "Journal evidence dimensions",
    description: `Retained journal rows for ${runLabel} with canonical dimensional scope preserved for ledger evidence review.`,
    rows: journalRows,
    filteredRowCountLabel: buildLedgerAccountFilteredCountLabel(journalRows.length, rows.length, normalizedFilter),
    hasRows: journalRows.length > 0,
    emptyText: normalizedFilter
      ? `No journal rows match dimensional scope "${dimensionFilter ?? ""}".`
      : `No journal rows are retained for ${runLabel}.`
  };
}

function buildLedgerJournalEvidenceRow(
  line: LedgerJournalLine,
  currency: string | null = null
): AccountingLedgerJournalEvidenceRowViewModel {
  const dimensionLabels = buildLedgerDimensionLabels(line);
  const money = (value: number) => (currency ? formatCurrencyForCode(value, currency) : formatCurrency(value));
  const amountLabel = `${money(line.totalDebits)} debit / ${money(line.totalCredits)} credit`;
  const lineCountLabel = line.lineCount === 1 ? "1 line" : `${formatCountValue(line.lineCount)} lines`;

  return {
    ...line,
    rowId: line.journalEntryId,
    timestampLabel: formatDateTimeLabel(line.timestamp),
    amountLabel,
    lineCountLabel,
    dimensionLabel: dimensionLabels.summary,
    dimensionDetailLabel: dimensionLabels.detail,
    ariaLabel: [
      `Journal ${line.journalEntryId}`,
      line.description,
      amountLabel,
      lineCountLabel,
      dimensionLabels.summary !== "No dimensions" ? `Dimensions ${dimensionLabels.summary}` : null
    ].filter(Boolean).join(". ")
  };
}

function ledgerJournalRowMatchesDimensionFilter(
  row: AccountingLedgerJournalEvidenceRowViewModel,
  normalizedFilter: string
): boolean {
  if (!normalizedFilter) {
    return true;
  }

  return [
    row.journalEntryId,
    row.description,
    row.dimensionLabel,
    row.dimensionDetailLabel
  ]
    .filter((value): value is string => typeof value === "string" && value.trim().length > 0)
    .join(" ")
    .toLocaleLowerCase()
    .includes(normalizedFilter);
}

export type BasisAwareLedgerTrialBalanceLine = LedgerTrialBalanceLine & {
  accountingBasis: AccountingBasisKind;
  accountingPolicyId: string;
  accountingPolicyVersion: string;
};

function buildTrialBalanceRow(
  line: BasisAwareLedgerTrialBalanceLine,
  detailPanelId: string,
  currency: string | null = null
): AccountingTrialBalanceRowViewModel {
  const accountLabel = line.accountName.trim() || "Unnamed account";
  const accountTypeLabel = line.accountType.trim() || "Unclassified";
  const basisName = accountingBasisDisplayName(line.accountingBasis);
  const basisLabel = `${basisName} basis`;
  const policyLabel = `${line.accountingPolicyId}/${line.accountingPolicyVersion}`;
  // Posted balances are in the book's base currency; the bare formatter prefixes a dollar sign.
  const balanceLabel = currency ? formatCurrencyForCode(line.balance, currency) : formatCurrency(line.balance);
  const entryCountLabel = formatCountValue(line.entryCount);
  const securityLabel = line.security?.primaryIdentifier?.trim() || line.symbol?.trim() || line.security?.displayName.trim() || null;
  const dimensionLabels = buildLedgerDimensionLabels(line);
  const dimensionLabel = dimensionLabels.summary;
  const dimensionDetailLabel = dimensionLabels.detail;
  // Identity from the full dimension set, not the summary. The summary shows the first three
  // dimensions and a "+N" count, so two rows differing only in a later one produced the same
  // string -- duplicate React keys, and selecting the second row resolving the first row's detail
  // and evidence. Widening the enumeration to every declared dimension made that collision far
  // easier to hit. The summary is still what the operator reads; it is just not the identity.
  const rowId = [
    line.accountingBasis,
    accountLabel,
    accountTypeLabel,
    line.financialAccountId,
    securityLabel,
    dimensionDetailLabel === "No ledger dimensions are attached to this row." ? null : dimensionDetailLabel
  ].filter(Boolean).join("-");

  return {
    ...line,
    rowId,
    accountLabel,
    accountTypeLabel,
    basisLabel,
    basisTone: trialBalanceBasisTone(line.accountingBasis),
    policyLabel,
    dimensionLabel,
    dimensionDetailLabel,
    balanceLabel,
    balanceTone: line.balance < 0 ? "danger" : line.balance > 0 ? "success" : "default",
    entryCountLabel,
    ariaLabel: [
      `${accountLabel} ${accountTypeLabel}`,
      basisLabel,
      `Policy ${policyLabel}`,
      dimensionLabel !== "No dimensions" ? `Dimensions ${dimensionLabel}` : null,
      `Balance ${balanceLabel}`,
      `${entryCountLabel} entries`,
      securityLabel ? `Security ${securityLabel}` : null
    ].filter(Boolean).join(". "),
    selectAriaLabel: `Inspect trial-balance account ${accountLabel} for ${accountTypeLabel}`,
    detailPanelId,
    isExpanded: false
  };
}

function buildLedgerDimensionLabels(line: Pick<LedgerTrialBalanceLine, "dimensions" | "accountScopeId" | "accountScopeDisplayName" | "entityScopeId" | "entityScopeDisplayName" | "sleeveScopeId" | "sleeveScopeDisplayName">): { summary: string; detail: string } {
  const dimensions = line.dimensions ?? null;
  const labels: string[] = [];

  // Every dimension LedgerDimensionSet declares, in the canonical order the desktop workstation's
  // PostedLedgerProjection.DescribeDimensionScope also uses. Enumerating a subset meant two rows
  // differing only by an omitted dimension rendered an identical scope, and the two lanes omitted
  // different ones — so the same balance was distinguishable on one workstation and not the other.
  appendDimensionLabel(labels, "Organization", dimensions?.organizationId);
  appendDimensionLabel(labels, "Fund", dimensions?.fundId);
  appendDimensionLabel(labels, "Entity", dimensions?.entityId ?? line.entityScopeDisplayName ?? line.entityScopeId);
  appendDimensionLabel(labels, "Portfolio", dimensions?.portfolioId);
  appendDimensionLabel(labels, "Book", dimensions?.bookId);
  appendDimensionLabel(labels, "Sleeve", dimensions?.sleeveId ?? line.sleeveScopeDisplayName ?? line.sleeveScopeId);
  appendDimensionLabel(labels, "Strategy", dimensions?.strategyId);
  appendDimensionLabel(labels, "Investor", dimensions?.investorId);
  appendDimensionLabel(labels, "Capital account", dimensions?.capitalAccountId ?? line.accountScopeDisplayName ?? line.accountScopeId);
  appendDimensionLabel(labels, "Customer", dimensions?.customerId);
  appendDimensionLabel(labels, "Vendor", dimensions?.vendorId);
  appendDimensionLabel(labels, "Project", dimensions?.projectId);
  appendDimensionLabel(labels, "Account", dimensions?.accountId);
  appendDimensionLabel(labels, "Instrument", dimensions?.instrumentId);
  appendDimensionLabel(labels, "Position", dimensions?.positionId);
  appendDimensionLabel(labels, "Tax lot", dimensions?.taxLotId);
  appendDimensionLabel(labels, "Cost center", dimensions?.costCenterId);
  appendDimensionLabel(labels, "Counterparty", dimensions?.counterpartyId);
  appendDimensionLabel(labels, "Agreement", dimensions?.intercompanyAgreementId);
  appendDimensionLabel(labels, "Agreement version", dimensions?.intercompanyAgreementVersion == null ? null : String(dimensions.intercompanyAgreementVersion));

  for (const [key, value] of Object.entries(dimensions?.externalGlDimensions ?? {}).sort(([left], [right]) => left.localeCompare(right))) {
    appendDimensionLabel(labels, `External ${key}`, value);
  }

  if (labels.length === 0) {
    return {
      summary: "No dimensions",
      detail: "No ledger dimensions are attached to this row."
    };
  }

  return {
    summary: labels.slice(0, 3).join(" / ") + (labels.length > 3 ? ` +${labels.length - 3}` : ""),
    detail: labels.join(" | ")
  };
}

function appendDimensionLabel(labels: string[], label: string, value: string | null | undefined): void {
  const normalized = typeof value === "string" ? value.trim() : "";
  if (normalized) {
    labels.push(`${label}: ${normalized}`);
  }
}

function normalizeLedgerAccountFilter(value: string | null | undefined): string {
  return (value ?? "").trim().toLocaleLowerCase();
}

function ledgerAccountRowMatchesFilter(
  row: AccountingTrialBalanceRowViewModel,
  normalizedFilter: string
): boolean {
  if (!normalizedFilter) {
    return true;
  }

  const searchable = [
    row.accountLabel,
    row.accountTypeLabel,
    row.financialAccountId,
    row.symbol,
    row.security?.displayName,
    row.security?.primaryIdentifier,
    row.dimensionLabel,
    row.dimensionDetailLabel
  ]
    .filter((value): value is string => typeof value === "string" && value.trim().length > 0)
    .join(" ")
    .toLocaleLowerCase();

  return searchable.includes(normalizedFilter);
}

function buildLedgerAccountFilterOptions(
  rows: AccountingTrialBalanceRowViewModel[],
  normalizedFilter: string
): AccountingLedgerAccountFilterOption[] {
  const grouped = new Map<string, { row: AccountingTrialBalanceRowViewModel; count: number }>();
  for (const row of rows) {
    const key = [
      row.accountLabel.toLocaleLowerCase(),
      row.financialAccountId?.toLocaleLowerCase() ?? "",
      row.accountTypeLabel.toLocaleLowerCase()
    ].join("|");
    const existing = grouped.get(key);
    if (existing) {
      existing.count += 1;
    } else {
      grouped.set(key, { row, count: 1 });
    }
  }

  return [...grouped.values()]
    .sort((left, right) => left.row.accountLabel.localeCompare(right.row.accountLabel))
    .slice(0, 8)
    .map(({ row, count }) => {
      const detail = [
        row.financialAccountId ?? "Unassigned",
        row.accountTypeLabel,
        row.basisLabel
      ].join(" / ");
      const normalizedLabel = [row.accountLabel, row.financialAccountId ?? "", row.accountTypeLabel].join(" ").toLocaleLowerCase();

      return {
        id: row.rowId,
        label: row.accountLabel,
        detail,
        rowCount: count,
        rowCountLabel: count === 1 ? "1 row" : `${count} rows`,
        isSelected: normalizedFilter.length > 0 && normalizedLabel.includes(normalizedFilter)
      };
    });
}

function buildLedgerAccountFilteredCountLabel(
  filteredCount: number,
  totalCount: number,
  normalizedFilter: string
): string {
  if (!normalizedFilter) {
    return totalCount === 1 ? "1 GL account row" : `${totalCount} GL account rows`;
  }

  return `${formatCountValue(filteredCount)} of ${formatCountValue(totalCount)} GL account rows`;
}

function buildTrialBalanceDetail(
  line: AccountingTrialBalanceRowViewModel,
  runLabel: string,
  runId: string | null,
  periodId: string | null = null
): AccountingTrialBalanceDetailViewState {
  const securityLabel = line.security?.displayName?.trim()
    || line.security?.primaryIdentifier?.trim()
    || line.symbol?.trim()
    || "No linked security";
  const financialAccountId = line.financialAccountId?.trim() || "Unassigned";
  const statusVariant = line.balanceTone === "danger" ? "danger" : line.balanceTone === "success" ? "success" : "outline";
  const statusLabel = line.balanceTone === "danger" ? "Credit / payable" : line.balanceTone === "success" ? "Debit / asset" : "Flat";

  const sourceEventIds = readSourceEventIds(line);
  const approvalIds = readStringArrayField(line, "approvalIds");
  const sourceJournalEntryIds = readSourceJournalEntryIds(line);
  const firstSourceEventId = sourceEventIds[0] ?? null;
  const firstApprovalId = approvalIds[0] ?? null;
  const firstJournalEntryId = sourceJournalEntryIds[0] ?? null;
  const auditDrillThroughHref = firstSourceEventId
    ? `/accounting/audit?sourceEventId=${encodeURIComponent(firstSourceEventId)}`
    : null;
  const approvalDrillThroughHref = firstApprovalId
    ? `/accounting/approvals?approvalId=${encodeURIComponent(firstApprovalId)}`
    : null;

  return {
    eyebrow: "Trial-balance detail",
    title: line.accountLabel,
    subtitle: `${line.accountTypeLabel} · ${line.basisLabel}`,
    description: `${line.accountLabel} contributes ${line.balanceLabel} across ${line.entryCountLabel} ledger entr${line.entryCount === 1 ? "y" : "ies"} for ${runLabel}. Source events and approvals stay attached for audit drill-through.`,
    statusLabel,
    statusVariant,
    ariaLabel: `Trial-balance detail for ${line.accountLabel}`,
    fields: [
      { label: "Account type", value: line.accountTypeLabel },
      { label: "Basis", value: line.basisLabel },
      { label: "Policy", value: line.policyLabel },
      { label: "Balance", value: line.balanceLabel },
      { label: "Entries", value: line.entryCountLabel },
      { label: "Financial account", value: financialAccountId },
      { label: "Dimensions", value: line.dimensionDetailLabel },
      { label: "Security", value: securityLabel },
      { label: "Journal entries", value: sourceJournalEntryIds.length > 0 ? sourceJournalEntryIds.join(", ") : "No journal entry references linked" },
      { label: "Source events", value: sourceEventIds.length > 0 ? sourceEventIds.join(", ") : "No source events linked" },
      { label: "Approvals", value: approvalIds.length > 0 ? approvalIds.join(", ") : "No approvals linked" },
      { label: "Run", value: runLabel }
    ],
    auditDrillThroughLabel: firstSourceEventId ? "Open source evidence" : "No source-event drill-through available",
    auditDrillThroughHref,
    approvalDrillThroughHref,
    ledgerLinesTitle: "Ledger lines for selected account",
    ledgerLinesDescription: firstJournalEntryId
      ? `Journal support linked to ${line.accountLabel} for ${runLabel}.`
      : `Account-level ledger inquiry for ${line.accountLabel}; journal-entry ids appear when ledger data includes posting references.`,
    ledgerLines: buildLedgerLineRows(line, sourceJournalEntryIds, sourceEventIds, approvalDrillThroughHref),
    ledgerLinesEmptyText: "No ledger line support is attached to this account row yet.",
    supportingDocumentsTitle: "Supporting documentation",
    supportingDocuments: buildSupportingDocumentRows({
      line,
      runId,
      sourceEventIds,
      approvalIds,
      sourceJournalEntryIds,
      periodId
    }),
    supportingDocumentsEmptyText: "No source documents, approvals, or review packet links are attached to this GL account yet."
  };
}

function buildLedgerLineRows(
  line: AccountingTrialBalanceRowViewModel,
  sourceJournalEntryIds: string[],
  sourceEventIds: string[],
  approvalHref: string | null
): AccountingLedgerLineViewModel[] {
  if (sourceJournalEntryIds.length === 0) {
    return [];
  }

  const debit = line.balance >= 0 ? line.balance : 0;
  const credit = line.balance < 0 ? Math.abs(line.balance) : 0;
  const evidenceLabel = sourceEventIds.length > 0
    ? "Source evidence"
    : "No source event linked";
  const evidenceHref = sourceEventIds[0]
    ? `/accounting/audit?sourceEventId=${encodeURIComponent(sourceEventIds[0])}`
    : null;

  return sourceJournalEntryIds.map((journalEntryId, index) => ({
    rowId: `${line.rowId}-journal-${index}`,
    journalEntryId,
    description: `${line.accountLabel} ${line.accountTypeLabel.toLocaleLowerCase()} activity`,
    debitLabel: formatCurrency(debit),
    creditLabel: formatCurrency(credit),
    balanceLabel: line.balanceLabel,
    evidenceLabel,
    evidenceHref,
    approvalHref,
    ariaLabel: `${line.accountLabel} journal ${journalEntryId}. Debit ${formatCurrency(debit)}. Credit ${formatCurrency(credit)}. Balance ${line.balanceLabel}. ${evidenceLabel}.`
  }));
}

function buildSupportingDocumentRows({
  line,
  runId,
  sourceEventIds,
  approvalIds,
  sourceJournalEntryIds,
  periodId
}: {
  line: AccountingTrialBalanceRowViewModel;
  runId: string | null;
  sourceEventIds: string[];
  approvalIds: string[];
  sourceJournalEntryIds: string[];
  periodId?: string | null;
}): AccountingSupportingDocumentViewModel[] {
  const rows: AccountingSupportingDocumentViewModel[] = [];

  if (runId) {
    rows.push({
      id: `${line.rowId}-review-packet`,
      label: "Run review packet",
      detail: "Ledger, reconciliation, and evidence packet for the selected run.",
      href: getRunReviewPacketPath(runId),
      ariaLabel: `Open run review packet for ${line.accountLabel}`
    });
  }

  for (const sourceEventId of sourceEventIds) {
    rows.push({
      id: `${line.rowId}-source-${sourceEventId}`,
      label: "Source event evidence",
      detail: "Source transaction, provider activity, or retained event evidence.",
      href: `/accounting/audit?sourceEventId=${encodeURIComponent(sourceEventId)}`,
      ariaLabel: `Open source event ${sourceEventId} for ${line.accountLabel}`
    });
  }

  for (const journalEntryId of sourceJournalEntryIds) {
    rows.push({
      id: `${line.rowId}-journal-${journalEntryId}`,
      label: "Journal entry evidence",
      detail: "Posting support and ledger entry lineage.",
      // The journal-entry detail screen, not the ledger explorer: the explorer reads `view` and
      // its book and period, never a journalEntryId, so this used to land on whatever period the
      // explorer defaulted to with the entry silently dropped. The period is carried because that
      // is how the detail screen resolves a posted entry.
      href: buildJournalEntryEvidenceHref(journalEntryId, periodId, runId),
      ariaLabel: `Open journal entry ${journalEntryId} for ${line.accountLabel}`
    });
  }

  for (const approvalId of approvalIds) {
    rows.push({
      id: `${line.rowId}-approval-${approvalId}`,
      label: "Approval evidence",
      detail: "Controller approval and maker/checker evidence.",
      href: `/accounting/approvals?approvalId=${encodeURIComponent(approvalId)}`,
      ariaLabel: `Open approval ${approvalId} for ${line.accountLabel}`
    });
  }

  return rows;
}

/**
 * Where a journal-entry evidence link goes. The detail screen resolves a posted entry from its
 * period and a run-scoped one from its run, so whichever scope these rows came from is carried.
 */
function buildJournalEntryEvidenceHref(
  journalEntryId: string,
  periodId: string | null | undefined,
  runId: string | null
): string {
  const params = new URLSearchParams({ journalEntryId });
  if (periodId) {
    params.set("periodId", periodId);
  } else if (runId) {
    params.set("runId", runId);
  }

  return `/accounting/journal-entries/detail?${params.toString()}`;
}

function readStringArrayField(value: unknown, fieldName: string): string[] {
  if (!value || typeof value !== "object" || !(fieldName in value)) {
    return [];
  }

  const field = (value as Record<string, unknown>)[fieldName];
  if (!Array.isArray(field)) {
    return [];
  }

  return field
    .map((item) => typeof item === "string" ? item.trim() : "")
    .filter((item) => item.length > 0);
}

export function readSourceEventIds(value: unknown): string[] {
  return uniqueStrings([
    ...readStringArrayField(value, "sourceEventIds"),
    ...readStringScalarField(value, "sourceEventId")
  ]);
}

function readSourceJournalEntryIds(value: unknown): string[] {
  return uniqueStrings([
    ...readStringArrayField(value, "sourceJournalEntryIds"),
    ...readStringScalarField(value, "sourceJournalEntryId"),
    ...readStringArrayField(value, "journalEntryIds")
  ]);
}

function readStringScalarField(value: unknown, fieldName: string): string[] {
  if (!value || typeof value !== "object" || !(fieldName in value)) {
    return [];
  }

  const field = (value as Record<string, unknown>)[fieldName];
  if (typeof field !== "string") {
    return [];
  }

  const trimmed = field.trim();
  return trimmed.length > 0 ? [trimmed] : [];
}

function uniqueStrings(values: string[]): string[] {
  const seen = new Set<string>();
  return values.filter((value) => {
    if (seen.has(value)) {
      return false;
    }

    seen.add(value);
    return true;
  });
}

function buildTrialBalanceAnnouncement({
  runLabel,
  state,
  rowCount,
  loading,
  errorText
}: {
  runLabel: string;
  state: AccountingTrialBalanceState;
  rowCount: number;
  loading: boolean;
  errorText: string | null;
}): string {
  if (errorText) {
    return `Trial balance failed for ${runLabel}: ${errorText}`;
  }

  if (loading) {
    return `Loading trial balance for ${runLabel}.`;
  }

  if (state === "empty") {
    return `No trial balance lines returned for ${runLabel}.`;
  }

  return rowCount === 1
    ? `1 trial balance line loaded for ${runLabel}.`
    : `${rowCount} trial balance lines loaded for ${runLabel}.`;
}

function normalizeTrialBalanceLine(line: LedgerTrialBalanceLine): BasisAwareLedgerTrialBalanceLine {
  return {
    ...line,
    accountingBasis: normalizeAccountingBasis(line.accountingBasis),
    accountingPolicyId: line.accountingPolicyId?.trim() || "legacy-v1",
    accountingPolicyVersion: line.accountingPolicyVersion?.trim() || "legacy-v1"
  };
}

export const buildGovernanceTrialBalanceViewState = buildAccountingTrialBalanceViewState;

function normalizeAccountingBasis(value: AccountingBasisKind | null | undefined): AccountingBasisKind {
  return ACCOUNTING_BASIS_OPTIONS.some((option) => option.id === value)
    ? value as AccountingBasisKind
    : DEFAULT_ACCOUNTING_BASIS;
}

function buildTrialBalanceBasisOptions(
  rows: BasisAwareLedgerTrialBalanceLine[],
  selectedBasis: AccountingBasisKind
): AccountingTrialBalanceBasisOption[] {
  const rowCounts = rows.reduce<Record<AccountingBasisKind, number>>((accumulator, row) => {
    accumulator[row.accountingBasis] += 1;
    return accumulator;
  }, {
    Primary: 0,
    Gaap: 0,
    Cash: 0,
    Tax: 0,
    Statutory: 0
  });

  return ACCOUNTING_BASIS_OPTIONS.map((option) => ({
    ...option,
    rowCount: rowCounts[option.id],
    rowCountLabel: rowCounts[option.id] === 1 ? "1 row" : `${rowCounts[option.id]} rows`,
    isSelected: option.id === selectedBasis
  }));
}

function trialBalanceBasisTone(basis: AccountingBasisKind): AccountingTrialBalanceRowViewModel["basisTone"] {
  switch (basis) {
    case "Gaap":
      return "success";
    case "Tax":
      return "warning";
    case "Statutory":
      return "danger";
    case "Cash":
      return "default";
    case "Primary":
    default:
      return "outline";
  }
}

function cashFlowContextLabel(workstream: AccountingWorkstream): string {
  if (workstream === "reporting") {
    return "Reporting packet context";
  }

  if (workstream === "reconciliation") {
    return "Reconciliation context";
  }

  if (workstream === "capital-accounts") {
    return "Capital account context";
  }

  if (workstream === "security-master") {
    return "Security coverage context";
  }

  if (workstream === "approvals") {
    return "Approval context";
  }

  return "Ledger context";
}

function buildCashFlowRow(
  id: string,
  label: string,
  value: number,
  tone: CashFlowEvidenceTone
): AccountingCashFlowRowViewModel {
  const formattedValue = formatCurrency(value);
  return {
    id,
    label,
    value: formattedValue,
    tone,
    ariaLabel: `${label}: ${formattedValue}`
  };
}

function buildCashFlowCountRow(
  id: string,
  label: string,
  value: number,
  tone: CashFlowEvidenceTone
): AccountingCashFlowRowViewModel {
  const formattedValue = String(value);
  return {
    id,
    label,
    value: formattedValue,
    tone,
    ariaLabel: `${label}: ${formattedValue}`
  };
}

function normalizeCashFlowTone(
  tone: AccountingCashFlowSummary["tone"],
  netVariance: number
): CashFlowEvidenceTone {
  if (netVariance === 0) {
    return "success";
  }

  if (tone === "danger") {
    return "danger";
  }

  if (tone === "success" || tone === "warning") {
    return tone;
  }

  return "warning";
}

function buildTransactionLabPreviewRequest(
  reconciliation: AccountingWorkspaceResponse["reconciliationQueue"][number]
): InvestmentAccountingTransactionLabRequest {
  return {
    kind: "BrokerReconciliation",
    fundAccountId: "fund-account-ops",
    symbol: "BOOKS",
    eventDate: new Date().toISOString().slice(0, 10),
    currency: "USD",
    amount: Math.max(1, reconciliation.openBreakCount || reconciliation.breakCount || 0),
    sourceRunId: reconciliation.runId,
    brokerStatementId: `statement-${reconciliation.runId}`,
    reconciliationCaseId: `case-${reconciliation.runId}`,
    evidenceIds: [`reconciliation-run:${reconciliation.runId}`],
    previewMode: "BooksBeforeBroker"
  };
}
