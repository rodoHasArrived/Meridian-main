import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { describeApiError, type ApiErrorDisplay } from "@/lib/api-errors";
import { ACTIVATION_OUTCOME_KEYS, recordActivationOutcome } from "@/lib/first-run/activation";
import {
  getCorporateActions,
  getReferenceDataWorkbenchCoverage,
  getReconciliationBreakQueue,
  getReconciliationCalibrationSummary,
  getReconciliationStatementRun,
  getReconciliationStatementRuns,
  getRunReviewPacketPath,
  getRunTrialBalance,
  getSecurityConflicts,
  getSecurityInstrumentPassport,
  getSecurityIdentity,
  getSecurityTrustSnapshot,
  previewInvestmentAccountingTransaction,
  getTradingParameters,
  runAnalysisExport,
  resolveReconciliationBreak,
  resolveSecurityConflict,
  reviewReconciliationBreak,
  searchSecurities,
  type AccountingReportPackageHistoryQuery,
  type CapitalAccountWorkbenchQuery,
  type ManualJournalEntryWorkbenchQuery,
  type ReferenceDataEndpointProbeResult,
  type ReferenceDataWorkbenchCoverage
} from "@/lib/api";
import {
  WORKSTATION_ROUTE_CATALOG
} from "@/lib/workspace";
import { EXPORT_API_ENDPOINTS, type ReferenceDataWorkbenchEndpointSeed } from "@/lib/workstation-endpoints";
import { formatReportPackRecipientList } from "@/lib/reporting-distributions";
import { markDevelopmentFixtureUsage } from "@/lib/api";
import { resolveDevSecurityScheduleEvents } from "@/lib/security-schedule-dev-fixtures";
import {
  requireSuccessfulReconciliationCasework,
  type AccountingReconciliationServices
} from "./reconciliation-casework-outcome";
import { formatBytes, formatCount, formatCurrency, formatCurrencyForCode, formatDateTimeLabel, formatSignedCurrency, toDomId } from "./accounting-screen.formatting";
import {
  accountingBasisDisplayName,
  buildBasisBridgeViewState
} from "./accounting-screen.basis-bridge.view-model";
import {
  buildSecurityConflictAction, buildSecurityIdentityAliasRow, buildSecurityIdentityIdentifierRow,
  formatConflictDate, formatFinanceFacingSourceSummary, formatSecurityConflictField, formatSecurityDate,
  formatSecurityDateRange, formatSecurityReferenceValue, referenceDataStatusBadgeVariant,
  referenceDataStatusLabel, referenceDataStatusTone, statusBadgeVariantForSecurityIdentity,
  summarizeReferenceDataRoutes,
} from "./accounting-screen.security-master-presenters";
import {
  buildCalibrationSummaryViewState
} from "./accounting-calibration-summary.view-model";
import {
  buildAccountingTaskMode,
  type AccountingTaskModeViewModel,
  type AccountingWorkstream
} from "./accounting-screen.task-mode-view-model";
import {
  DEFAULT_ACCOUNTING_BASIS,
  normalizeApiErrorDisplay
} from "./accounting-screen.view-model.shared";
import {
  areReconciliationBreakQueuesEquivalent,
  replaceBreakQueueItem,
} from "./accounting-screen.reconciliation-queue-utils";
import {
  buildOperationalExceptionWorkbenchState,
  buildReconciliationBreakQueueState,
  buildReconciliationComparisonViewState,
  buildReconciliationDetailActions,
  buildReconciliationDetailViewState,
  buildReconciliationQueuePanelViewState,
  buildReconciliationResolveDialogState,
  buildReconciliationStatementRunsViewState,
  sortStatementRunsNewestFirst,
  resolveSelectedReconciliation,
} from "./accounting-screen.reconciliation.view-model";
export {
  buildAccountingWorkflowLaunchViewState,
  buildCloseCommandCenterViewState,
  useAccountingCloseReportPackageViewModel,
} from "./accounting-screen.close-cockpit.view-model";
export {
  useManualJournalEntryWorkbenchViewModel,
} from "./accounting-screen.journal-entries.view-model";
export {
  useCapitalAccountWorkbenchViewModel,
} from "./accounting-screen.capital-accounts.view-model";
export {
  useAccountingConfigurationViewModel,
} from "./accounting-screen.governance.view-model";
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
  resolveSelectedReconciliation,
} from "./accounting-screen.reconciliation.view-model";
import type {
  AccountingBasisKind,
  AccountingConfigurationWorkspace,
  AccountingMigrationRunArtifactList,
  AccountingMigrationRunWorkerPlanList,
  AccountingProductionReadiness,
  AccountingProductionReadinessRequest,
  AccountingProductionCertificationProfile,
  AccountingProductionCertificationProfileUpsertRequest,
  AccountingTenantAdministrationProfile,
  AccountingTenantAdministrationProfileUpsertRequest,
  AccountingReportPackageBundle,
  AccountingReportPackageRequest,
  CertifyAccountingReportPackageRequest,
  ReportExportArtifactManifest,
  AccountingJournalTemplatePreview,
  AccountingTemplateLineSide,
  AccountingRuleTestSuiteResult,
  CreateLedgerBookRequest,
  CorporateAction,
  ExportAnalysisResult,
  AccountingCashFlowSummary,
  AccountingReportingProfile,
  AccountingReportingSummary,
  AccountingWorkspaceResponse,
  AccountingSystemMappingProfileUpsertRequest,
  AccountingSystemReconciliationSummary,
  ExternalGlMappingProfile,
  ClosePeriodPlan,
  ClosePeriodLockResult,
  UpsertClosePeriodPlanConfigurationRequest,
  CreateLateAdjustmentRequest,
  ReviewCloseEvidenceRequest,
  ReviewLateAdjustmentRequest,
  SignOffCloseTaskRequest,
  LedgerBook,
  LedgerJournalLine,
  LedgerTrialBalanceLine,
  ReconciliationBreakQueueItem,
  ReconciliationCalibrationSummary,
  InvestmentAccountingTransactionLabPreview,
  InvestmentAccountingTransactionLabRequest,
  ResolveConflictRequest,
  PreviewJournalTemplateRequest,
  PostingRuleJournalCandidateRequest,
  PostingRuleJournalCandidateResult,
  RuleDryRunRequest,
  RuleDryRunResult,
  ExecuteAccountingRuleTestCasesRequest,
  ApprovePostingRulePromotionRequest,
  UpsertAccountingRuleTestCaseRequest,
  UpsertChartOfAccountsNodeRequest,
  UpsertPostingRuleRequest,
  ActivateAccountingConfigurationRequest,
  AttachManualJournalEntryEvidenceRequest,
  JournalEntryLifecycleAction,
  JournalEntryLifecycleActionRequest,
  JournalEntryLifecycleActionResult,
  LockClosePeriodRequest,
  LedgerDimensionSet,
  ManualJournalEntryDraft,
  ManualJournalEntryLine,
  ManualJournalEntryWorkbench,
  ResolveReconciliationBreakRequest,
  SaveManualJournalEntryDraftRequest,
  SecurityIdentityDrillIn,
  SecurityAliasEntry,
  SecurityIdentifierEntry,
  SecurityMasterConflict,
  SecurityMasterEntry,
  InstrumentPassport,
  InstrumentPassportOperationsReadiness,
  InstrumentPassportOperationsWorkbenchItem,
  InstrumentPassportOperationsWorkbenchPanel,
  InstrumentPassportProviderConfidence,
  SecurityMasterOpenLot,
  SecurityMasterOpenLotReadModel,
  SecurityMasterTrustSnapshot,
  StatementRunSummary,
  SubmitManualJournalEntryApprovalRequest,
  TradingParameters,
  ValidateManualJournalEntryDraftRequest,
  CapitalAccountWorkbench
} from "@/types";

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
export {
  accountingWorkstreamHref,
  buildAccountingTaskMode,
  resolveAccountingWorkstream,
  resolveGovernanceWorkstream
} from "./accounting-screen.task-mode-view-model";
export type {
  AccountingTaskModeId,
  AccountingTaskModeViewModel,
  AccountingWorkstream,
  GovernanceWorkstream
} from "./accounting-screen.task-mode-view-model";

export type ReconciliationBreakCommand = "assign" | "resolve" | "dismiss";
export type ReconciliationBreakResolutionStatus = ResolveReconciliationBreakRequest["status"];
export type SecurityConflictResolution = ResolveConflictRequest["resolution"];

export interface SecurityMasterServices {
  search: (query: string) => Promise<SecurityMasterEntry[]>;
  getIdentity: (securityId: string) => Promise<SecurityIdentityDrillIn>;
  getConflicts: () => Promise<SecurityMasterConflict[]>;
  resolveConflict: (request: ResolveConflictRequest) => Promise<SecurityMasterConflict>;
}

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

export interface CorporateActionRowViewModel extends CorporateAction {
  rowId: string;
  eventTypeLabel: string;
  exDateLabel: string;
  payDateLabel: string;
  amountLabel: string;
  ariaLabel: string;
  selectAriaLabel: string;
  detailPanelId: string;
  isExpanded: boolean;
}

export interface CorporateActionDetailFieldViewModel {
  label: string;
  value: string;
  tone?: "default" | "warning";
}

export interface CorporateActionDetailViewState {
  id: string;
  eyebrow: string;
  title: string;
  subtitle: string;
  description: string;
  ariaLabel: string;
  statusLabel: string;
  fields: CorporateActionDetailFieldViewModel[];
}

export interface CorporateActionsViewState {
  securityId: string;
  tableLabel: string;
  tableCaption: string;
  detailPanelId: string;
  rows: CorporateActionRowViewModel[];
  selectedRowId: string | null;
  selectedDetail: CorporateActionDetailViewState | null;
  emptyText: string;
  detailEmptyTitle: string;
  detailEmptyText: string;
  detailEmptyAriaLabel: string;
  loadingText: string | null;
  errorText: string | null;
  errorDetails: string[];
  hasRows: boolean;
  statusAnnouncement: string;
}

export type SecurityScheduleFamily = "bond" | "structured" | "fund" | "derivative";
export type SecurityScheduleEventType = string;
export type SecuritySchedulePostingStatus = string;

export interface SecurityCashFlowScheduleEvent {
  eventId: string;
  securityId: string;
  scheduleFamily: SecurityScheduleFamily;
  eventType: SecurityScheduleEventType;
  paymentDate: string;
  accrualStartDate: string | null;
  accrualEndDate: string | null;
  couponRatePct: number | null;
  expectedAmount: number | null;
  actualAmount: number | null;
  principalAmount: number | null;
  interestAmount: number | null;
  factorStart: number | null;
  factorEnd: number | null;
  currency: string;
  postingStatus: SecuritySchedulePostingStatus;
  auditReference: string | null;
  note: string | null;
}

export interface SecurityScheduleRowViewModel extends SecurityCashFlowScheduleEvent {
  rowId: string;
  eventTypeLabel: string;
  paymentDateLabel: string;
  expectedAmountLabel: string;
  actualAmountLabel: string;
  varianceLabel: string;
  factorLabel: string;
  postingStatusLabel: string;
  postingStatusTone: "success" | "warning" | "danger" | "outline";
  ariaLabel: string;
  selectAriaLabel: string;
  detailPanelId: string;
  isExpanded: boolean;
}

export interface SecurityScheduleDetailFieldViewModel {
  label: string;
  value: string;
  tone?: "default" | "success" | "warning" | "danger";
}

export interface SecurityScheduleDetailViewState {
  id: string;
  eyebrow: string;
  title: string;
  subtitle: string;
  description: string;
  ariaLabel: string;
  statusLabel: string;
  statusTone: "success" | "warning" | "danger" | "outline";
  fields: SecurityScheduleDetailFieldViewModel[];
}

export interface SecurityScheduleToolbarItemViewModel {
  id: string;
  label: string;
  value?: string;
  active?: boolean;
}

export interface SecuritySchedulesViewState {
  securityId: string;
  title: string;
  description: string;
  tableLabel: string;
  tableCaption: string;
  detailPanelId: string;
  toolbarAriaLabel: string;
  toolbarItems: SecurityScheduleToolbarItemViewModel[];
  rows: SecurityScheduleRowViewModel[];
  selectedRowId: string | null;
  selectedDetail: SecurityScheduleDetailViewState | null;
  emptyText: string;
  detailEmptyTitle: string;
  detailEmptyText: string;
  detailEmptyAriaLabel: string;
  loadingText: string | null;
  errorText: string | null;
  errorDetails: string[];
  hasRows: boolean;
  statusAnnouncement: string;
}

export type TradingParametersField = { label: string; value: string; tone?: "default" | "warning" };

export interface TradingParametersViewState {
  securityId: string;
  asOfLabel: string;
  fields: TradingParametersField[];
  errorText: string | null;
  errorDetails: string[];
  loadingText: string | null;
  statusAnnouncement: string;
}

export interface SecurityOpenLotRowViewModel extends SecurityMasterOpenLot {
  rowId: string;
  tradeDateLabel: string;
  settleDateLabel: string;
  quantityLabel: string;
  faceLabel: string;
  factorAdjustedLabel: string;
  costBasisLabel: string;
  entryPriceLabel: string;
  unrealizedPnlLabel: string;
  scopeLabel: string;
  statusLabel: string;
  statusTone: "success" | "warning" | "danger" | "outline";
  ariaLabel: string;
  selectAriaLabel: string;
  detailPanelId: string;
  isExpanded: boolean;
}

export interface SecurityOpenLotDetailViewState {
  id: string;
  eyebrow: string;
  title: string;
  subtitle: string;
  description: string;
  ariaLabel: string;
  statusLabel: string;
  statusTone: "success" | "warning" | "danger" | "outline";
  fields: SecurityScheduleDetailFieldViewModel[];
}

export interface SecurityOpenLotReadModelViewState {
  securityId: string;
  title: string;
  description: string;
  summary: string;
  asOfLabel: string;
  tableLabel: string;
  tableCaption: string;
  detailPanelId: string;
  toolbarAriaLabel: string;
  toolbarItems: SecurityScheduleToolbarItemViewModel[];
  rows: SecurityOpenLotRowViewModel[];
  selectedRowId: string | null;
  selectedDetail: SecurityOpenLotDetailViewState | null;
  emptyText: string;
  detailEmptyTitle: string;
  detailEmptyText: string;
  detailEmptyAriaLabel: string;
  loadingText: string | null;
  errorText: string | null;
  errorDetails: string[];
  hasRows: boolean;
  statusAnnouncement: string;
}

export interface SecurityMasterDrillInServices {
  getCorporateActions: (securityId: string) => Promise<CorporateAction[]>;
  getReferenceDataCoverage: (seed: ReferenceDataWorkbenchEndpointSeed) => Promise<ReferenceDataWorkbenchCoverage>;
  getInstrumentPassport: (securityId: string) => Promise<InstrumentPassport>;
  getTradingParameters: (securityId: string) => Promise<TradingParameters>;
  getTrustSnapshot: (securityId: string) => Promise<SecurityMasterTrustSnapshot>;
}

export interface SecuritySearchResultColumnViewModel {
  id: "name" | "assetClass" | "primaryId" | "currency" | "status";
  label: string;
}

export interface SecuritySearchResultRowViewModel extends SecurityMasterEntry {
  rowId: string;
  isSelected: boolean;
  detailPanelId: string;
  isExpanded: boolean;
  selectAriaLabel: string;
  primaryIdentifierLabel: string;
  statusTone: "success" | "warning";
  ariaLabel: string;
}

export interface SecuritySearchState {
  trimmedQuery: string;
  resultCount: number;
  hasResults: boolean;
  resultsTableLabel: string;
  resultColumns: SecuritySearchResultColumnViewModel[];
  resultRows: SecuritySearchResultRowViewModel[];
  searchStatusText: string | null;
  searchErrorText: string | null;
  searchErrorDetails: string[];
  statusAnnouncement: string;
}

export type SecurityMasterPageMetricTone = "default" | "success" | "warning";

export interface SecurityMasterPageMetricViewModel {
  id: "results" | "selected" | "conflicts" | "detail" | "reference" | "passport";
  label: string;
  value: string;
  detail: string;
  tone: SecurityMasterPageMetricTone;
}

export interface SecurityMasterCoveragePostureViewModel {
  label: "Ready" | "Review required" | "Verification pending" | "Select a record";
  detail: string;
  tone: SecurityMasterPageMetricTone;
}

export interface SecurityMasterDetailSectionViewModel {
  id: "overview" | "reference" | "schedules" | "lots" | "controls" | "audit";
  label: string;
  value: string;
  active?: boolean;
}

export interface SecurityMasterPageViewState {
  ariaLabel: string;
  eyebrow: string;
  title: string;
  description: string;
  coveragePosture: SecurityMasterCoveragePostureViewModel;
  metrics: SecurityMasterPageMetricViewModel[];
  detailEyebrow: string;
  detailTitle: string;
  detailSubtitle: string;
  detailDescription: string;
  detailStatusLabel: string;
  detailStatusBadgeVariant: "success" | "warning" | "outline";
  detailToolbarAriaLabel: string;
  detailSections: SecurityMasterDetailSectionViewModel[];
}

export interface InstrumentPassportProviderConfidenceRowViewModel extends InstrumentPassportProviderConfidence {
  rowId: string;
  providerLabel: string;
  symbolLabel: string;
  confidenceLabel: string;
  freshnessLabel: string;
  statusLabel: string;
  statusTone: "success" | "warning";
  ariaLabel: string;
}

export interface InstrumentPassportFieldViewModel {
  label: string;
  value: string;
  tone?: "default" | "success" | "warning";
}

export interface InstrumentPassportOperationsWorkbenchItemViewModel extends InstrumentPassportOperationsWorkbenchItem {
  statusLabel: string;
  statusBadgeVariant: "success" | "warning" | "outline";
  evidenceLabel: string;
  blockerLabel: string;
}

export interface InstrumentPassportOperationsWorkbenchPanelViewModel extends Omit<InstrumentPassportOperationsWorkbenchPanel, "items"> {
  statusLabel: string;
  statusBadgeVariant: "success" | "warning" | "outline";
  items: InstrumentPassportOperationsWorkbenchItemViewModel[];
}

export interface InstrumentPassportOperationsReadinessViewModel extends InstrumentPassportOperationsReadiness {
  statusLabel: string;
  statusBadgeVariant: "success" | "warning" | "outline";
  evidenceLabel: string;
  blockerLabel: string;
}

export interface InstrumentPassportViewState {
  securityId: string;
  title: string;
  description: string;
  statusLabel: string;
  statusBadgeVariant: "success" | "warning" | "outline";
  fields: InstrumentPassportFieldViewModel[];
  providerRows: InstrumentPassportProviderConfidenceRowViewModel[];
  operationsWorkbenchTitle: string;
  operationsWorkbenchSummary: string;
  operationsWorkbenchStatusLabel: string;
  operationsWorkbenchStatusBadgeVariant: "success" | "warning" | "outline";
  operationsReadiness: InstrumentPassportOperationsReadinessViewModel[];
  operationsPanels: InstrumentPassportOperationsWorkbenchPanelViewModel[];
  providerTableLabel: string;
  providerTableCaption: string;
  providerEmptyText: string;
  loadingText: string | null;
  errorText: string | null;
  errorDetails: string[];
  statusAnnouncement: string;
}
export interface ReferenceDataWorkbenchMetricViewModel {
  id: "routes" | "ready" | "review" | "deferred";
  label: string;
  value: string;
  detail: string;
  tone: "default" | "success" | "warning" | "danger";
}

export interface ReferenceDataEndpointRowViewModel extends ReferenceDataEndpointProbeResult {
  rowId: string;
  familyLabel: string;
  methodLabel: string;
  accessLabel: string;
  displaySummary: string;
  statusLabel: string;
  statusBadgeVariant: "success" | "warning" | "outline" | "danger";
  countLabel: string;
  latencyLabel: string;
  ariaLabel: string;
  selectAriaLabel: string;
  detailPanelId: string;
  isExpanded: boolean;
}

export interface ReferenceDataEndpointDetailViewState {
  id: string;
  eyebrow: string;
  title: string;
  subtitle: string;
  description: string;
  ariaLabel: string;
  statusLabel: string;
  statusBadgeVariant: "success" | "warning" | "outline" | "danger";
  fields: SecurityScheduleDetailFieldViewModel[];
  responsePreview: string | null;
  errorSummary: string | null;
  errorDetails: string[];
}

export interface ReferenceDataWorkbenchViewState {
  securityId: string | null;
  title: string;
  description: string;
  metrics: ReferenceDataWorkbenchMetricViewModel[];
  rows: ReferenceDataEndpointRowViewModel[];
  selectedRowId: string | null;
  selectedDetail: ReferenceDataEndpointDetailViewState | null;
  tableLabel: string;
  tableCaption: string;
  detailPanelId: string;
  emptyText: string;
  detailEmptyTitle: string;
  detailEmptyText: string;
  detailEmptyAriaLabel: string;
  loadingText: string | null;
  errorText: string | null;
  errorDetails: string[];
  hasRows: boolean;
  statusAnnouncement: string;
}
export interface SecurityIdentitySummaryFieldViewModel {
  label: string;
  value: string;
}

export interface SecurityIdentityIdentifierRowViewModel extends SecurityIdentifierEntry {
  rowId: string;
  providerLabel: string;
  primaryLabel: string;
  primaryBadgeVariant: "success" | "outline";
  validRangeLabel: string;
  ariaLabel: string;
}

export interface SecurityIdentityAliasRowViewModel extends SecurityAliasEntry {
  rowId: string;
  providerLabel: string;
  enabledLabel: string;
  enabledBadgeVariant: "success" | "warning";
  validRangeLabel: string;
  createdLabel: string;
  reasonText: string;
  ariaLabel: string;
}

export interface SecurityIdentityDrillInViewState {
  panelId: string;
  title: string;
  subtitle: string;
  description: string;
  ariaLabel: string;
  statusLabel: string;
  statusBadgeVariant: "success" | "warning" | "outline";
  summaryFields: SecurityIdentitySummaryFieldViewModel[];
  identifiersTitle: string;
  identifiersTableLabel: string;
  identifiers: SecurityIdentityIdentifierRowViewModel[];
  identifierEmptyText: string;
  aliasesTitle: string;
  aliasesTableLabel: string;
  aliases: SecurityIdentityAliasRowViewModel[];
  aliasEmptyText: string;
}

export interface SecurityConflictActionViewModel {
  resolution: SecurityConflictResolution;
  label: string;
  ariaLabel: string;
  variant: "outline" | "ghost";
  disabled: boolean;
  disabledReason: string | null;
}

export interface SecurityConflictRefreshCommandViewModel {
  label: string;
  ariaLabel: string;
  disabled: boolean;
  disabledReason: string | null;
  busy: boolean;
  busyLabel: string | null;
  feedbackId: string;
  feedbackText: string | null;
}

export interface SecurityConflictRowViewModel extends SecurityMasterConflict {
  statusLabel: string;
  statusTone: "warning" | "neutral";
  isOpen: boolean;
  isResolving: boolean;
  fieldLabel: string;
  providerASummary: string;
  providerBSummary: string;
  detectedLabel: string;
  ariaLabel: string;
  resolutionStatusText: string | null;
  actions: SecurityConflictActionViewModel[];
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

export interface ReconciliationResolveDialogState {
  breakId: string;
  status: ReconciliationBreakResolutionStatus;
  rationale: string;
  inputId: string;
  helpId: string;
  formAriaLabel: string;
  label: string;
  placeholder: string;
  helpText: string;
  submitLabel: string;
  submitAriaLabel: string;
  submitDisabledReason: string | null;
  cancelLabel: string;
  cancelAriaLabel: string;
  isSubmitDisabled: boolean;
}

export interface ReconciliationResolveDialogViewModel {
  active: ReconciliationResolveDialogState | null;
  open: (breakId: string, status: ReconciliationBreakResolutionStatus) => void;
  close: () => void;
  updateRationale: (value: string) => void;
  submit: () => Promise<void>;
  isOpenFor: (breakId: string) => boolean;
  getActionDisabledReason: (
    breakId: string,
    command: ReconciliationBreakCommand,
    baseDisabledReason?: string | null
  ) => string | null;
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

const defaultSecurityMasterServices: SecurityMasterServices = {
  search: (query) => searchSecurities(query),
  getIdentity: (securityId) => getSecurityIdentity(securityId),
  getConflicts: () => getSecurityConflicts(),
  resolveConflict: (request) => resolveSecurityConflict(request)
};

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



const defaultSecurityMasterDrillInServices: SecurityMasterDrillInServices = {
  getCorporateActions: (securityId) => getCorporateActions(securityId),
  getReferenceDataCoverage: (seed) => getReferenceDataWorkbenchCoverage(seed),
  getInstrumentPassport: (securityId) => getSecurityInstrumentPassport(securityId),
  getTradingParameters: (securityId) => getTradingParameters(securityId),
  getTrustSnapshot: (securityId) => getSecurityTrustSnapshot(securityId)
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

export function useSecurityMasterViewModel(
  active: boolean,
  services: SecurityMasterServices = defaultSecurityMasterServices,
  drillInServices: SecurityMasterDrillInServices = defaultSecurityMasterDrillInServices,
  searchDelayMs = 350
) {
  const [query, setQuery] = useState("");
  const [results, setResults] = useState<SecurityMasterEntry[] | null>(null);
  const [searching, setSearching] = useState(false);
  const [searchError, setSearchError] = useState<ApiErrorDisplay | null>(null);
  const [selectedSecurityId, setSelectedSecurityId] = useState<string | null>(null);
  const [identity, setIdentity] = useState<SecurityIdentityDrillIn | null>(null);
  const [identityLoading, setIdentityLoading] = useState(false);
  const [identityError, setIdentityError] = useState<ApiErrorDisplay | null>(null);
  const [conflicts, setConflicts] = useState<SecurityMasterConflict[] | null>(null);
  const [conflictsLoading, setConflictsLoading] = useState(false);
  const [conflictsError, setConflictsError] = useState<ApiErrorDisplay | null>(null);
  const [conflictResolvingId, setConflictResolvingId] = useState<string | null>(null);
  const [conflictActionError, setConflictActionError] = useState<ApiErrorDisplay | null>(null);
  const [corporateActions, setCorporateActions] = useState<CorporateAction[] | null>(null);
  const [corporateActionsLoading, setCorporateActionsLoading] = useState(false);
  const [corporateActionsError, setCorporateActionsError] = useState<ApiErrorDisplay | null>(null);
  const [selectedCorporateActionId, setSelectedCorporateActionId] = useState<string | null>(null);
  const [referenceDataCoverage, setReferenceDataCoverage] = useState<ReferenceDataWorkbenchCoverage | null>(null);
  const [referenceDataCoverageLoading, setReferenceDataCoverageLoading] = useState(false);
  const [referenceDataCoverageError, setReferenceDataCoverageError] = useState<ApiErrorDisplay | null>(null);
  const [selectedReferenceDataEndpointId, setSelectedReferenceDataEndpointId] = useState<string | null>(null);
  const [selectedScheduleEventId, setSelectedScheduleEventId] = useState<string | null>(null);
  const [selectedOpenLotId, setSelectedOpenLotId] = useState<string | null>(null);
  const [trustSnapshot, setTrustSnapshot] = useState<SecurityMasterTrustSnapshot | null>(null);
  const [trustSnapshotLoading, setTrustSnapshotLoading] = useState(false);
  const [trustSnapshotError, setTrustSnapshotError] = useState<ApiErrorDisplay | null>(null);
  const [instrumentPassport, setInstrumentPassport] = useState<InstrumentPassport | null>(null);
  const [instrumentPassportLoading, setInstrumentPassportLoading] = useState(false);
  const [instrumentPassportError, setInstrumentPassportError] = useState<ApiErrorDisplay | null>(null);
  const [tradingParameters, setTradingParameters] = useState<TradingParameters | null>(null);
  const [tradingParametersLoading, setTradingParametersLoading] = useState(false);
  const [tradingParametersError, setTradingParametersError] = useState<ApiErrorDisplay | null>(null);
  const searchTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const searchGenerationRef = useRef(0);
  const identityGenerationRef = useRef(0);
  const conflictGenerationRef = useRef(0);
  const conflictResolvingIdRef = useRef<string | null>(null);
  const resetReferenceDataCoverage = useCallback(() => {
    setReferenceDataCoverage(null);
    setReferenceDataCoverageLoading(false);
    setReferenceDataCoverageError(null);
    setSelectedReferenceDataEndpointId(null);
  }, []);

  useEffect(() => () => {
    if (searchTimerRef.current) {
      clearTimeout(searchTimerRef.current);
    }
    searchGenerationRef.current += 1;
    identityGenerationRef.current += 1;
    conflictGenerationRef.current += 1;
    conflictResolvingIdRef.current = null;
  }, []);

  useEffect(() => {
    if (active) {
      return;
    }

    if (searchTimerRef.current) {
      clearTimeout(searchTimerRef.current);
      searchTimerRef.current = null;
    }

    searchGenerationRef.current += 1;
    identityGenerationRef.current += 1;
    conflictGenerationRef.current += 1;
    setSearching(false);
    setSelectedSecurityId(null);
    setIdentity(null);
    setIdentityLoading(false);
    setIdentityError(null);
    setConflictsLoading(false);
    setConflictsError(null);
    setConflictResolvingId(null);
    conflictResolvingIdRef.current = null;
    setConflictActionError(null);
    setCorporateActions(null);
    setCorporateActionsLoading(false);
    setCorporateActionsError(null);
    setSelectedCorporateActionId(null);
    resetReferenceDataCoverage();
    setSelectedScheduleEventId(null);
    setSelectedOpenLotId(null);
    setTrustSnapshot(null);
    setTrustSnapshotLoading(false);
    setTrustSnapshotError(null);
    setInstrumentPassport(null);
    setInstrumentPassportLoading(false);
    setInstrumentPassportError(null);
    setTradingParameters(null);
    setTradingParametersLoading(false);
    setTradingParametersError(null);
  }, [active, resetReferenceDataCoverage]);

  const refreshConflicts = useCallback(async () => {
    if (!active) {
      return;
    }

    if (conflictResolvingIdRef.current) {
      return;
    }

    const generation = conflictGenerationRef.current + 1;
    conflictGenerationRef.current = generation;
    setConflictsLoading(true);
    setConflictsError(null);

    try {
      const rows = await services.getConflicts();
      if (conflictGenerationRef.current === generation) {
        setConflicts(rows);
      }
    } catch (err) {
      if (conflictGenerationRef.current === generation) {
        setConflicts([]);
        setConflictsError(describeApiError(err, "Identifier conflicts failed to load."));
      }
    } finally {
      if (conflictGenerationRef.current === generation) {
        setConflictsLoading(false);
      }
    }
  }, [active, resetReferenceDataCoverage, services]);

  useEffect(() => {
    void refreshConflicts();
  }, [refreshConflicts]);

  useEffect(() => {
    if (!active || !selectedSecurityId) {
      setCorporateActions(null);
      setCorporateActionsLoading(false);
      setCorporateActionsError(null);
      setSelectedCorporateActionId(null);
      resetReferenceDataCoverage();
      setSelectedScheduleEventId(null);
      setSelectedOpenLotId(null);
      setTrustSnapshot(null);
      setTrustSnapshotLoading(false);
      setTrustSnapshotError(null);
      setTradingParameters(null);
      setTradingParametersLoading(false);
      setTradingParametersError(null);
      return;
    }

    let cancelled = false;
    setCorporateActionsLoading(true);
    setCorporateActionsError(null);
    setReferenceDataCoverage(null);
    setReferenceDataCoverageLoading(true);
    setReferenceDataCoverageError(null);
    setSelectedReferenceDataEndpointId(null);
    setTrustSnapshotLoading(true);
    setTrustSnapshotError(null);
    setInstrumentPassportLoading(true);
    setInstrumentPassportError(null);
    setTradingParametersLoading(true);
    setTradingParametersError(null);

    const selectedSearchResult = results?.find((entry) => entry.securityId === selectedSecurityId) ?? null;

    drillInServices.getReferenceDataCoverage(buildReferenceDataWorkbenchSeed(selectedSecurityId, selectedSearchResult))
      .then((coverage) => {
        if (!cancelled) {
          setReferenceDataCoverage(coverage);
        }
      })
      .catch((err) => {
        if (!cancelled) {
          setReferenceDataCoverage(null);
          setReferenceDataCoverageError(describeApiError(err, "Reference data workbench failed to load."));
        }
      })
      .finally(() => {
        if (!cancelled) {
          setReferenceDataCoverageLoading(false);
        }
      });
    drillInServices.getCorporateActions(selectedSecurityId)
      .then((rows) => {
        if (!cancelled) {
          setCorporateActions(rows);
        }
      })
      .catch((err) => {
        if (!cancelled) {
          setCorporateActions([]);
          setCorporateActionsError(describeApiError(err, "Corporate actions failed to load."));
        }
      })
      .finally(() => {
        if (!cancelled) {
          setCorporateActionsLoading(false);
        }
      });

    drillInServices.getTrustSnapshot(selectedSecurityId)
      .then((snapshot) => {
        if (!cancelled) {
          setTrustSnapshot(snapshot);
        }
      })
      .catch((err) => {
        if (!cancelled) {
          setTrustSnapshot(null);
          setTrustSnapshotError(describeApiError(err, "Trust snapshot failed to load."));
        }
      })
      .finally(() => {
        if (!cancelled) {
          setTrustSnapshotLoading(false);
        }
      });

    drillInServices.getInstrumentPassport(selectedSecurityId)
      .then((passport) => {
        if (!cancelled) {
          setInstrumentPassport(passport);
        }
      })
      .catch((err) => {
        if (!cancelled) {
          setInstrumentPassport(null);
          setInstrumentPassportError(describeApiError(err, "Instrument passport failed to load."));
        }
      })
      .finally(() => {
        if (!cancelled) {
          setInstrumentPassportLoading(false);
        }
      });

    drillInServices.getTradingParameters(selectedSecurityId)
      .then((params) => {
        if (!cancelled) {
          setTradingParameters(params);
        }
      })
      .catch((err) => {
        if (!cancelled) {
          setTradingParameters(null);
          setTradingParametersError(describeApiError(err, "Trading parameters failed to load."));
        }
      })
      .finally(() => {
        if (!cancelled) {
          setTradingParametersLoading(false);
        }
      });

    return () => {
      cancelled = true;
    };
  }, [active, selectedSecurityId, drillInServices, resetReferenceDataCoverage, results]);

  const updateQuery = useCallback((nextQuery: string) => {
    setQuery(nextQuery);
    setSelectedSecurityId(null);
    setIdentity(null);
    setIdentityError(null);
    setSearchError(null);
    setSelectedCorporateActionId(null);
    resetReferenceDataCoverage();
    setSelectedScheduleEventId(null);
    setSelectedOpenLotId(null);
    setTrustSnapshot(null);
    setTrustSnapshotLoading(false);
    setTrustSnapshotError(null);
    setInstrumentPassport(null);
    setInstrumentPassportLoading(false);
    setInstrumentPassportError(null);
    identityGenerationRef.current += 1;

    if (searchTimerRef.current) {
      clearTimeout(searchTimerRef.current);
      searchTimerRef.current = null;
    }

    const trimmed = nextQuery.trim();
    searchGenerationRef.current += 1;

    if (!trimmed) {
      setSearching(false);
      setResults(null);
      return;
    }

    searchTimerRef.current = setTimeout(() => {
      const generation = searchGenerationRef.current;
      setSearching(true);

      services.search(trimmed)
        .then((rows) => {
          if (searchGenerationRef.current === generation) {
            setResults(rows);
          }
        })
        .catch((err) => {
          if (searchGenerationRef.current === generation) {
            setResults([]);
            setSearchError(describeApiError(err, "Security search failed."));
          }
        })
        .finally(() => {
          if (searchGenerationRef.current === generation) {
            setSearching(false);
          }
        });
    }, searchDelayMs);
  }, [resetReferenceDataCoverage, searchDelayMs, services]);

  const selectSecurity = useCallback(async (securityId: string) => {
    if (!active) {
      return;
    }

    const generation = identityGenerationRef.current + 1;
    identityGenerationRef.current = generation;
    setSelectedSecurityId(securityId);
    setIdentity(null);
    setIdentityError(null);
    setIdentityLoading(true);
    setCorporateActions(null);
    setCorporateActionsError(null);
    setSelectedCorporateActionId(null);
    resetReferenceDataCoverage();
    setSelectedScheduleEventId(null);
    setSelectedOpenLotId(null);
    setTrustSnapshot(null);
    setTrustSnapshotLoading(false);
    setTrustSnapshotError(null);
    setInstrumentPassport(null);
    setInstrumentPassportLoading(false);
    setInstrumentPassportError(null);
    setTradingParameters(null);
    setTradingParametersError(null);

    try {
      const detail = await services.getIdentity(securityId);
      if (identityGenerationRef.current === generation) {
        setIdentity(detail);
      }
    } catch (err) {
      if (identityGenerationRef.current === generation) {
        setIdentityError(describeApiError(err, "Identity drill-in failed."));
      }
    } finally {
      if (identityGenerationRef.current === generation) {
        setIdentityLoading(false);
      }
    }
  }, [active, resetReferenceDataCoverage, services]);

  const resolveConflict = useCallback(async (
    conflictId: string,
    resolution: ResolveConflictRequest["resolution"]
  ) => {
    conflictResolvingIdRef.current = conflictId;
    setConflictResolvingId(conflictId);
    setConflictActionError(null);

    try {
      const updated = await services.resolveConflict({ conflictId, resolution, resolvedBy: "operator", reason: `${resolution} action from the shared conflict queue.` });
      setConflicts((current) => current?.map((conflict) => (
        conflict.conflictId === conflictId ? updated : conflict
      )) ?? current);
    } catch (err) {
      setConflictActionError(describeApiError(err, "Conflict resolution failed."));
    } finally {
      conflictResolvingIdRef.current = null;
      setConflictResolvingId(null);
    }
  }, [services]);

  const searchState = useMemo(
    () => buildSecuritySearchState({
      query,
      searching,
      results,
      selectedSecurityId,
      searchError,
      identityLoading,
      identityError
    }),
    [identityError, identityLoading, query, results, searchError, searching, selectedSecurityId]
  );
  const conflictRows = useMemo(
    () => buildSecurityConflictRows(conflicts, conflictResolvingId),
    [conflictResolvingId, conflicts]
  );
  const identityView = useMemo(
    () => buildSecurityIdentityDrillInState(identity),
    [identity]
  );
  const selectedSearchResult = useMemo(
    () => selectedSecurityId ? results?.find((entry) => entry.securityId === selectedSecurityId) ?? null : null,
    [results, selectedSecurityId]
  );
  const corporateActionRows = useMemo(
    () => buildCorporateActionRows(corporateActions, selectedCorporateActionId),
    [corporateActions, selectedCorporateActionId]
  );
  useEffect(() => {
    if (corporateActionRows.length === 0) {
      if (selectedCorporateActionId !== null) {
        setSelectedCorporateActionId(null);
      }
      return;
    }

    if (!selectedCorporateActionId || !corporateActionRows.some((row) => row.rowId === selectedCorporateActionId)) {
      setSelectedCorporateActionId(corporateActionRows[0].rowId);
    }
  }, [corporateActionRows, selectedCorporateActionId]);
  const corporateActionsView = useMemo(
    () => buildCorporateActionsViewState(
      selectedSecurityId,
      corporateActions,
      selectedCorporateActionId,
      corporateActionsLoading,
      corporateActionsError
    ),
    [corporateActions, corporateActionsError, corporateActionsLoading, selectedCorporateActionId, selectedSecurityId]
  );
  const securitySchedules = useMemo(() => {
    if (trustSnapshot?.scheduleBook) {
      return mapScheduleBookToCashFlowScheduleEvents(selectedSecurityId, trustSnapshot);
    }

    if (trustSnapshot || trustSnapshotLoading || trustSnapshotError) {
      return [];
    }

    return resolveSecurityScheduleEvents(selectedSecurityId);
  }, [selectedSecurityId, trustSnapshot, trustSnapshotError, trustSnapshotLoading]);
  const securityScheduleRows = useMemo(
    () => buildSecurityScheduleRows(securitySchedules, selectedScheduleEventId),
    [securitySchedules, selectedScheduleEventId]
  );
  useEffect(() => {
    if (securityScheduleRows.length === 0) {
      if (selectedScheduleEventId !== null) {
        setSelectedScheduleEventId(null);
      }
      return;
    }

    if (!selectedScheduleEventId || !securityScheduleRows.some((row) => row.rowId === selectedScheduleEventId)) {
      setSelectedScheduleEventId(securityScheduleRows[0].rowId);
    }
  }, [securityScheduleRows, selectedScheduleEventId]);
  const schedulesView = useMemo(
    () => buildSecuritySchedulesViewState({
      securityId: selectedSecurityId,
      displayName: identity?.displayName ?? selectedSearchResult?.displayName ?? null,
      assetClass: identity?.assetClass ?? selectedSearchResult?.classification.assetClass ?? null,
      schedules: securitySchedules,
      selectedRowId: selectedScheduleEventId,
      loading: trustSnapshotLoading,
      error: trustSnapshotError,
      factorHistoryCount: trustSnapshot?.scheduleBook?.factorHistory.length ?? 0,
      provenanceCount: trustSnapshot?.scheduleBook?.provenanceHistory.length ?? 0,
      sourceSummary: trustSnapshot?.scheduleBook?.sourceSummary ?? trustSnapshot?.scheduleSummary?.sourceSummary ?? null
    }),
    [
      identity?.assetClass,
      identity?.displayName,
      securitySchedules,
      selectedScheduleEventId,
      selectedSearchResult?.classification.assetClass,
      selectedSearchResult?.displayName,
      selectedSecurityId,
      trustSnapshot,
      trustSnapshotError,
      trustSnapshotLoading
    ]
  );
  const openLotRows = useMemo(
    () => buildSecurityOpenLotRows(trustSnapshot?.openLotReadModel ?? null, selectedOpenLotId),
    [selectedOpenLotId, trustSnapshot?.openLotReadModel]
  );
  useEffect(() => {
    if (openLotRows.length === 0) {
      if (selectedOpenLotId !== null) {
        setSelectedOpenLotId(null);
      }
      return;
    }

    if (!selectedOpenLotId || !openLotRows.some((row) => row.rowId === selectedOpenLotId)) {
      setSelectedOpenLotId(openLotRows[0].rowId);
    }
  }, [openLotRows, selectedOpenLotId]);
  const openLotReadModelView = useMemo(
    () => buildSecurityOpenLotReadModelViewState({
      securityId: selectedSecurityId,
      readModel: trustSnapshot?.openLotReadModel ?? null,
      selectedRowId: selectedOpenLotId,
      loading: trustSnapshotLoading,
      error: trustSnapshotError
    }),
    [
      selectedOpenLotId,
      selectedSecurityId,
      trustSnapshot?.openLotReadModel,
      trustSnapshotError,
      trustSnapshotLoading
    ]
  );
  const instrumentPassportView = useMemo(
    () => buildInstrumentPassportViewState({
      securityId: selectedSecurityId,
      passport: instrumentPassport,
      loading: instrumentPassportLoading,
      error: instrumentPassportError
    }),
    [instrumentPassport, instrumentPassportError, instrumentPassportLoading, selectedSecurityId]
  );
  const tradingParametersView = useMemo(
    () => buildTradingParametersViewState(tradingParameters, tradingParametersLoading, tradingParametersError),
    [tradingParameters, tradingParametersLoading, tradingParametersError]
  );
  const referenceDataWorkbenchView = useMemo(
    () => buildReferenceDataWorkbenchViewState({
      securityId: selectedSecurityId,
      coverage: referenceDataCoverage,
      loading: referenceDataCoverageLoading,
      error: referenceDataCoverageError,
      selectedRowId: selectedReferenceDataEndpointId
    }),
    [
      referenceDataCoverage,
      referenceDataCoverageError,
      referenceDataCoverageLoading,
      selectedReferenceDataEndpointId,
      selectedSecurityId
    ]
  );
  useEffect(() => {
    if (referenceDataWorkbenchView.rows.length === 0) {
      if (selectedReferenceDataEndpointId !== null) {
        setSelectedReferenceDataEndpointId(null);
      }
      return;
    }

    if (!selectedReferenceDataEndpointId || !referenceDataWorkbenchView.rows.some((row) => row.rowId === selectedReferenceDataEndpointId)) {
      setSelectedReferenceDataEndpointId(referenceDataWorkbenchView.rows[0].rowId);
    }
  }, [referenceDataWorkbenchView.rows, selectedReferenceDataEndpointId]);
  const openConflictCount = countOpenSecurityConflicts(conflicts);
  const conflictRefreshCommand = useMemo(
    () => buildSecurityConflictRefreshCommand(conflictsLoading, conflictsError, conflictResolvingId),
    [conflictResolvingId, conflictsError, conflictsLoading]
  );
  const pageView = useMemo(
    () => buildSecurityMasterPageViewState({
      query,
      results,
      selectedSecurityId,
      selectedDisplayName: identity?.displayName ?? selectedSearchResult?.displayName ?? null,
      selectedAssetClass: identity?.assetClass ?? selectedSearchResult?.classification.assetClass ?? null,
      selectedStatus: identity?.status ?? selectedSearchResult?.status ?? null,
      identity,
      identityLoading,
      conflicts,
      conflictsLoading,
      corporateActions,
      instrumentPassport,
      instrumentPassportLoading,
      instrumentPassportError,
      referenceDataCoverage,
      referenceDataLoading: referenceDataCoverageLoading,
      referenceDataError: referenceDataCoverageError,
      securitySchedules,
      openLotReadModel: trustSnapshot?.openLotReadModel ?? null,
      trustSnapshotLoading,
      trustSnapshotError,
      tradingParameters
    }),
    [
      conflicts,
      conflictsLoading,
      corporateActions,
      instrumentPassport,
      instrumentPassportError,
      instrumentPassportLoading,
      referenceDataCoverage,
      referenceDataCoverageError,
      referenceDataCoverageLoading,
      securitySchedules,
      trustSnapshot?.openLotReadModel,
      trustSnapshotError,
      trustSnapshotLoading,
      identity,
      identityLoading,
      query,
      results,
      selectedSearchResult,
      selectedSecurityId,
      tradingParameters
    ]
  );

  return {
    query,
    updateQuery,
    pageView,
    results,
    searching,
    selectedSecurityId,
    selectSecurity,
    identity,
    identityView,
    identityLoading,
    identityErrorText: identityError?.summary ?? null,
    identityErrorDetails: identityError?.details ?? [],
    conflicts,
    conflictRows,
    hasConflicts: conflictRows.length > 0,
    conflictEmptyText: "No identifier conflicts detected.",
    conflictSectionAriaLabel: "Security Master identifier conflict queue",
    conflictsLoading,
    conflictsErrorText: conflictsError?.summary ?? null,
    conflictsErrorDetails: conflictsError?.details ?? [],
    conflictResolvingId,
    conflictActionErrorText: conflictActionError?.summary ?? null,
    conflictActionErrorDetails: conflictActionError?.details ?? [],
    conflictRefreshCommand,
    refreshConflicts,
    resolveConflict,
    openConflictCount,
    conflictCountLabel: `${openConflictCount} open`,
    corporateActions,
    corporateActionRows,
    corporateActionsView,
    referenceDataCoverage,
    referenceDataWorkbenchView,
    selectReferenceDataEndpoint: setSelectedReferenceDataEndpointId,
    selectCorporateAction: setSelectedCorporateActionId,
    hasCorporateActions: (corporateActions?.length ?? 0) > 0,
    corporateActionsLoading,
    corporateActionsErrorText: corporateActionsError?.summary ?? null,
    securitySchedules,
    securityScheduleRows,
    schedulesView,
    selectScheduleEvent: setSelectedScheduleEventId,
    trustSnapshot,
    trustSnapshotLoading,
    trustSnapshotErrorText: trustSnapshotError?.summary ?? null,
    trustSnapshotErrorDetails: trustSnapshotError?.details ?? [],
    openLotRows,
    openLotReadModelView,
    selectOpenLot: setSelectedOpenLotId,
    instrumentPassport,
    instrumentPassportView,
    instrumentPassportLoading,
    instrumentPassportErrorText: instrumentPassportError?.summary ?? null,
    instrumentPassportErrorDetails: instrumentPassportError?.details ?? [],
    tradingParameters,
    tradingParametersView,
    tradingParametersLoading,
    tradingParametersErrorText: tradingParametersError?.summary ?? null,
    ...searchState
  };
}

export function useAccountingReconciliationViewModel(
  data: AccountingWorkspaceResponse | null,
  workstream: AccountingWorkstream,
  services: AccountingReconciliationServices = defaultAccountingReconciliationServices,
  systemReconciliation: AccountingSystemReconciliationSummary | null = null,
  operatorIdentity: string | null = null
) {
  const [selectedRunId, setSelectedRunId] = useState<string | null>(null);
  const [breakQueue, setBreakQueue] = useState<ReconciliationBreakQueueItem[]>(data?.breakQueue ?? []);
  const [breakQueueLoading, setBreakQueueLoading] = useState(false);
  const [breakQueueError, setBreakQueueError] = useState<ApiErrorDisplay | null>(null);
  const [breakAction, setBreakAction] = useState<ReconciliationBreakAction | null>(null);
  const [breakActionError, setBreakActionError] = useState<ApiErrorDisplay | null>(null);
  const [selectedBreakId, setSelectedBreakId] = useState<string | null>(null);
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

  const reconciliationQueue = data?.reconciliationQueue ?? [];
  const selectedReconciliation = useMemo(
    () => resolveSelectedReconciliation(reconciliationQueue, selectedRunId),
    [reconciliationQueue, selectedRunId]
  );

  useEffect(() => {
    const hasSelectedRun = selectedRunId
      ? reconciliationQueue.some((item) => item.runId === selectedRunId) || statementRuns.some((item) => item.runId === selectedRunId)
      : false;

    if (hasSelectedRun) {
      return;
    }

    const nextRunId = statementRuns[0]?.runId ?? reconciliationQueue[0]?.runId ?? null;
    setSelectedRunId(nextRunId);
  }, [reconciliationQueue, selectedRunId, statementRuns]);

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
  }, [data?.breakQueue, services, workstream]);

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
  }, [refreshCalibrationSummary, workstream]);

  const refreshStatementRuns = useCallback(async () => {
    const revision = statementRunsRequestRevisionRef.current + 1;
    statementRunsRequestRevisionRef.current = revision;
    setStatementRunsLoading(true);
    setStatementRunsError(null);

    try {
      const runs = await services.getStatementRuns();
      if (statementRunsRequestRevisionRef.current === revision) {
        setStatementRuns(sortStatementRunsNewestFirst(runs));
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
  }, [services]);

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
  }, [refreshStatementRuns, workstream]);

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

  const assignBreak = useCallback(async (breakId: string) => {
    setBreakAction({ breakId, command: "assign" });
    setBreakActionError(null);

    try {
      const operation = await services.reviewBreak({
        breakId,
        ...(operatorIdentity ? { assignedTo: operatorIdentity, reviewedBy: operatorIdentity } : {})
      });
      const updated = requireSuccessfulReconciliationCasework(operation);
      setBreakQueue((current) => replaceBreakQueueItem(current, updated));
    } catch (err) {
      setBreakActionError(describeApiError(err, "Break assignment failed."));
    } finally {
      setBreakAction(null);
    }
  }, [operatorIdentity, services]);

  const resolveBreak = useCallback(async (
    breakId: string,
    status: ResolveReconciliationBreakRequest["status"],
    operatorRationale: string | null | undefined
  ) => {
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
      setBreakQueue((current) => replaceBreakQueueItem(current, updated));
      void recordActivationOutcome(ACTIVATION_OUTCOME_KEYS.validationResolved);
    } catch (err) {
      setBreakActionError(describeApiError(err, "Break resolution failed."));
    } finally {
      setBreakAction(null);
    }
  }, [operatorIdentity, services]);

  const breakQueueState = useMemo(
    () => buildReconciliationBreakQueueState({
      breakQueue,
      selectedBreakId,
      loading: breakQueueLoading,
      loadError: breakQueueError,
      action: breakAction,
      actionError: breakActionError
    }),
    [breakAction, breakActionError, breakQueue, breakQueueError, breakQueueLoading, selectedBreakId]
  );
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

    setTransactionLabBusy(true);
    setTransactionLabError(null);

    try {
      const preview = await services.previewTransactionLab(buildTransactionLabPreviewRequest(selectedReconciliation));
      setTransactionLabPreview(preview);
    } catch (err) {
      setTransactionLabPreview(null);
      setTransactionLabError(describeApiError(err, "Transaction Lab preview failed."));
    } finally {
      setTransactionLabBusy(false);
    }
  }, [selectedReconciliation, services, transactionLabBusy]);

  return {
    reconciliationQueue,
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
    selectBreak: setSelectedBreakId,
    assignBreak,
    resolveBreak,
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

export function useReconciliationResolveDialogViewModel(
  resolveBreak: (
    breakId: string,
    status: ReconciliationBreakResolutionStatus,
    operatorRationale: string
  ) => Promise<void>
): ReconciliationResolveDialogViewModel {
  const [dialog, setDialog] = useState<{ breakId: string; status: ReconciliationBreakResolutionStatus } | null>(null);
  const [rationale, setRationale] = useState("");

  const close = useCallback(() => {
    setDialog(null);
    setRationale("");
  }, []);

  const open = useCallback((breakId: string, status: ReconciliationBreakResolutionStatus) => {
    setDialog({ breakId, status });
    setRationale("");
  }, []);

  const submit = useCallback(async () => {
    if (!dialog || !rationale.trim()) {
      return;
    }

    await resolveBreak(dialog.breakId, dialog.status, rationale);
    close();
  }, [close, dialog, rationale, resolveBreak]);

  const active = useMemo(
    () => (dialog ? buildReconciliationResolveDialogState(dialog.breakId, dialog.status, rationale) : null),
    [dialog, rationale]
  );

  const isOpenFor = useCallback((breakId: string) => dialog?.breakId === breakId, [dialog]);
  const getActionDisabledReason = useCallback((
    breakId: string,
    command: ReconciliationBreakCommand,
    baseDisabledReason: string | null = null
  ) => {
    if (baseDisabledReason) {
      return baseDisabledReason;
    }

    if (dialog?.breakId === breakId && (command === "resolve" || command === "dismiss")) {
      return "Enter the rationale or cancel the open queue action before choosing another action.";
    }

    return null;
  }, [dialog]);

  return {
    active,
    open,
    close,
    updateRationale: setRationale,
    submit,
    isOpenFor,
    getActionDisabledReason
  };
}

const securitySearchResultColumns: SecuritySearchResultColumnViewModel[] = [
  { id: "name", label: "Name" },
  { id: "assetClass", label: "Asset Class" },
  { id: "primaryId", label: "Primary ID" },
  { id: "currency", label: "Currency" },
  { id: "status", label: "Status" }
];

export const SECURITY_IDENTITY_DETAIL_PANEL_ID = "security-master-identity-detail";

export function buildSecurityMasterPageViewState({
  query,
  results,
  selectedSecurityId,
  selectedDisplayName,
  selectedAssetClass,
  selectedStatus,
  identity,
  identityLoading,
  conflicts,
  conflictsLoading,
  corporateActions,
  instrumentPassport,
  instrumentPassportLoading = false,
  instrumentPassportError = null,
  referenceDataCoverage = null,
  referenceDataLoading = false,
  referenceDataError = null,
  securitySchedules,
  openLotReadModel,
  trustSnapshotLoading = false,
  trustSnapshotError = null,
  tradingParameters
}: {
  query: string;
  results: SecurityMasterEntry[] | null;
  selectedSecurityId: string | null;
  selectedDisplayName: string | null;
  selectedAssetClass: string | null;
  selectedStatus: string | null;
  identity: SecurityIdentityDrillIn | null;
  identityLoading: boolean;
  conflicts: SecurityMasterConflict[] | null;
  conflictsLoading: boolean;
  corporateActions: CorporateAction[] | null;
  instrumentPassport?: InstrumentPassport | null;
  instrumentPassportLoading?: boolean;
  instrumentPassportError?: ApiErrorDisplay | string | null;
  referenceDataCoverage?: ReferenceDataWorkbenchCoverage | null;
  referenceDataLoading?: boolean;
  referenceDataError?: ApiErrorDisplay | string | null;
  securitySchedules?: SecurityCashFlowScheduleEvent[] | null;
  openLotReadModel?: SecurityMasterOpenLotReadModel | null;
  trustSnapshotLoading?: boolean;
  trustSnapshotError?: ApiErrorDisplay | string | null;
  tradingParameters: TradingParameters | null;
}): SecurityMasterPageViewState {
  const hasQuery = query.trim().length > 0;
  const resultCount = results?.length ?? 0;
  const openConflictCount = countOpenSecurityConflicts(conflicts);
  const selectedName = selectedDisplayName?.trim() || (selectedSecurityId ? "Selected security" : "None selected");
  const selectedClass = selectedAssetClass?.trim() || "Unclassified";
  const statusLabel = selectedStatus?.trim() || (selectedSecurityId ? "Pending" : "No selection");
  const identifiersLabel = identity
    ? formatCount(identity.identifiers?.length ?? 0, "identifier")
    : identityLoading
      ? "Loading identifiers"
      : "No identifiers loaded";
  const aliasesLabel = identity ? formatCount(identity.aliases?.length ?? 0, "alias") : "No aliases loaded";
  const corporateActionLabel = corporateActions
    ? formatCount(corporateActions.length, "corporate action")
    : selectedSecurityId
      ? "Loading schedules"
      : "No selection";
  const normalizedPassportError = normalizeApiErrorDisplay(instrumentPassportError);
  const referenceDataLabel = referenceDataError
    ? "Error"
    : referenceDataLoading
      ? "Loading"
      : referenceDataCoverage
        ? formatCount(referenceDataCoverage.endpoints?.length ?? 0, "route")
        : selectedSecurityId
          ? "Pending"
          : "No selection";
  const referenceRouteCounts = summarizeReferenceDataRoutes(referenceDataCoverage?.endpoints ?? []);
  const referenceDataDetail = referenceDataError
    ? "Reference coverage could not be loaded."
    : referenceDataLoading
      ? "Refreshing endpoint coverage for the selected record."
      : referenceDataCoverage
        ? `${referenceRouteCounts.readyCount.toLocaleString()} ready · ${referenceRouteCounts.reviewCount.toLocaleString()} need review · ${referenceRouteCounts.deferredOrBlockedCount.toLocaleString()} deferred or blocked · ${referenceRouteCounts.totalCount.toLocaleString()} total.`
        : selectedSecurityId
          ? "Reference coverage is queued for the selected record."
          : "Select a security to inspect source coverage.";
  const scheduleLabel = securitySchedules
    ? securitySchedules.length > 0
      ? formatCount(securitySchedules.length, "cash-flow event")
      : corporateActionLabel
    : corporateActionLabel;
  const openLotLabel = trustSnapshotError
    ? "Error"
    : trustSnapshotLoading
      ? "Loading"
      : openLotReadModel
        ? formatCount(openLotReadModel.lots?.length ?? 0, "lot")
        : selectedSecurityId
          ? "No lots"
          : "No selection";
  const operationsReadiness = instrumentPassport?.operationsWorkbench?.readiness ?? [];
  const readyOperationCount = operationsReadiness.filter((item) => item.status === "Ready" || item.status === "Complete").length;
  const totalOperationCount = operationsReadiness.length;
  const passportTrustSummary = instrumentPassport?.trustPosture?.summary?.trim() || "Passport evidence incomplete";
  const passportTrustTone = instrumentPassport?.trustPosture?.tone?.trim().toLowerCase() || "unknown";
  const passportControlLabel = normalizedPassportError
    ? "Error"
    : instrumentPassportLoading
      ? "Loading"
      : instrumentPassport
        ? passportTrustSummary
        : tradingParameters
          ? "Controls set"
          : selectedSecurityId
            ? "Pending"
            : "No selection";
  const passportControlDetail = normalizedPassportError
    ? "Passport readiness could not be loaded."
    : instrumentPassportLoading
      ? "Refreshing passport, provider confidence, and control readiness."
      : instrumentPassport
        ? totalOperationCount > 0
          ? `${readyOperationCount}/${totalOperationCount} operations checks ready; trading controls ${tradingParameters ? "loaded" : "pending"}.`
          : `Trust posture ${passportTrustSummary}; trading controls ${tradingParameters ? "loaded" : "pending"}.`
        : selectedSecurityId
          ? "Passport and control readiness are queued for the selected record."
          : "Select a security to inspect passport and control readiness.";
  const passportControlTone: SecurityMasterPageMetricTone = normalizedPassportError || instrumentPassportLoading
    ? "warning"
    : instrumentPassport
      ? passportTrustTone === "success" || passportTrustTone === "ready" || passportTrustTone === "trusted"
        ? "success"
        : "warning"
      : tradingParameters
        ? "success"
        : "default";
  const passportOperationsReady = operationsReadiness.length > 0
    && operationsReadiness.every((item) => item.status === "Ready" || item.status === "Complete");
  const passportEvidenceReady = Boolean(instrumentPassport)
    && (passportTrustTone === "success" || passportTrustTone === "ready" || passportTrustTone === "trusted")
    && passportOperationsReady;
  const coverageDetails: string[] = [];
  let coverageHasIssue = false;
  let coverageHasPendingCheck = false;

  if (conflictsLoading || conflicts === null) {
    coverageDetails.push("conflict checks are pending");
    coverageHasPendingCheck = true;
  } else if (openConflictCount > 0) {
    coverageDetails.push(formatCount(openConflictCount, "open conflict"));
    coverageHasIssue = true;
  }

  if (referenceDataError) {
    coverageDetails.push("reference-route coverage is unavailable");
    coverageHasIssue = true;
  } else if (referenceDataLoading || referenceDataCoverage === null) {
    coverageDetails.push("reference-route checks are pending");
    coverageHasPendingCheck = true;
  } else if (referenceRouteCounts.totalCount === 0) {
    coverageDetails.push("no reference routes returned coverage evidence");
    coverageHasIssue = true;
  } else {
    if (referenceRouteCounts.reviewCount > 0) {
      coverageDetails.push(`${formatCount(referenceRouteCounts.reviewCount, "route")} ${referenceRouteCounts.reviewCount === 1 ? "needs" : "need"} review`);
      coverageHasIssue = true;
    }
    if (referenceRouteCounts.deferredOrBlockedCount > 0) {
      coverageDetails.push(`${formatCount(referenceRouteCounts.deferredOrBlockedCount, "route")} ${referenceRouteCounts.deferredOrBlockedCount === 1 ? "is" : "are"} deferred or blocked`);
      coverageHasIssue ||= referenceRouteCounts.blockedCount > 0;
    }
  }

  if (normalizedPassportError) {
    coverageDetails.push("passport evidence is unavailable");
    coverageHasIssue = true;
  } else if (instrumentPassportLoading) {
    coverageDetails.push("passport checks are pending");
    coverageHasPendingCheck = true;
  } else if (!passportEvidenceReady) {
    coverageDetails.push(instrumentPassport ? "passport evidence is incomplete" : "passport evidence is missing");
    coverageHasIssue = true;
  }

  const coveragePosture: SecurityMasterCoveragePostureViewModel = !selectedSecurityId
    ? {
        label: "Select a record",
        detail: "Select a security before relying on conflicts, reference coverage, or passport evidence.",
        tone: "default"
      }
    : coverageHasIssue
      ? {
          label: "Review required",
          detail: `${coverageDetails.join("; ")}.`,
          tone: "warning"
        }
      : coverageHasPendingCheck
        ? {
            label: "Verification pending",
            detail: `${coverageDetails.join("; ")}.`,
            tone: "warning"
          }
        : {
            label: "Ready",
            detail: referenceRouteCounts.deferredCount > 0
              ? `No open conflicts; all probed reference routes are ready; passport evidence is trusted; ${formatCount(referenceRouteCounts.deferredCount, "write-capable route")} intentionally deferred.`
              : "No open conflicts; all reference routes are ready; passport evidence is trusted.",
            tone: "success"
          };

  return {
    ariaLabel: "Security Master command deck",
    eyebrow: "Security Master",
    title: "Security Master command deck",
    description: "Search, inspect, and reconcile trusted security reference records from one dense master-detail page.",
    coveragePosture,
    metrics: [
      {
        id: "selected",
        label: "Selected record",
        value: selectedName,
        detail: selectedSecurityId
          ? `${selectedClass} · ${statusLabel}.`
          : hasQuery
            ? `${formatCount(resultCount, "security")} returned. Select a row to open the record.`
            : "Search by ticker, ISIN, CUSIP, FIGI, or display name.",
        tone: selectedSecurityId ? "success" : "default"
      },
      {
        id: "conflicts",
        label: "Open conflicts",
        value: conflictsLoading ? "Loading" : openConflictCount.toLocaleString(),
        detail: conflictsLoading
          ? "Refreshing provider conflict evidence."
          : openConflictCount > 0
            ? `${formatCount(openConflictCount, "open conflict")} requiring operator review.`
            : "No open conflicts need operator review.",
        tone: openConflictCount > 0 || conflictsLoading ? "warning" : "success"
      },
      {
        id: "reference",
        label: "Reference coverage",
        value: referenceDataLabel,
        detail: referenceDataDetail,
        tone: referenceDataError
          || referenceDataLoading
          || referenceRouteCounts.reviewCount > 0
          || referenceRouteCounts.blockedCount > 0
          || (referenceDataCoverage !== null && referenceRouteCounts.totalCount === 0)
          ? "warning"
          : referenceDataCoverage
            ? "success"
            : "default"
      },
      {
        id: "passport",
        label: "Passport controls",
        value: passportControlLabel,
        detail: passportControlDetail,
        tone: passportControlTone
      }
    ],
    detailEyebrow: "Security detail",
    detailTitle: "Security detail page",
    detailSubtitle: selectedSecurityId ? `${selectedClass} · ${statusLabel}` : "Select a security",
    detailDescription: selectedSecurityId
      ? `${selectedName} reference data, schedules, trading controls, lots, and audit evidence are grouped below the selected master row.`
      : "Select a security from the master table to inspect its reference record.",
    detailStatusLabel: statusLabel,
    detailStatusBadgeVariant: selectedSecurityId
      ? statusLabel.toLowerCase() === "active"
        ? "success"
        : "warning"
      : "outline",
    detailToolbarAriaLabel: selectedSecurityId ? `Security detail sections for ${selectedName}` : "Security detail sections",
    detailSections: [
      { id: "overview", label: "Overview", value: identifiersLabel, active: true },
      { id: "reference", label: "Reference", value: referenceDataLabel },
      { id: "schedules", label: "Schedules", value: scheduleLabel },
      { id: "lots", label: "Open lots", value: openLotLabel },
      { id: "controls", label: "Controls", value: tradingParameters ? "Trading set" : selectedSecurityId ? "Pending" : "No selection" },
      { id: "audit", label: "Audit", value: openConflictCount > 0 ? formatCount(openConflictCount, "conflict") : aliasesLabel }
    ]
  };
}

export function buildSecuritySearchState({
  query,
  searching,
  results,
  selectedSecurityId,
  searchError,
  identityLoading,
  identityError
}: {
  query: string;
  searching: boolean;
  results: SecurityMasterEntry[] | null;
  selectedSecurityId?: string | null;
  searchError: string | ApiErrorDisplay | null;
  identityLoading: boolean;
  identityError: string | ApiErrorDisplay | null;
}): SecuritySearchState {
  const trimmedQuery = query.trim();
  const resultCount = results?.length ?? 0;
  const hasResults = resultCount > 0;
  const resultRows = buildSecuritySearchResultRows(results, selectedSecurityId ?? null);
  const normalizedSearchError = normalizeApiErrorDisplay(searchError);
  const normalizedIdentityError = normalizeApiErrorDisplay(identityError);
  const searchErrorText = normalizedSearchError
    ? normalizedSearchError.summary.startsWith("Security search failed")
      ? normalizedSearchError.summary
      : `Security search failed: ${normalizedSearchError.summary}`
    : null;

  let searchStatusText: string | null = null;
  if (!trimmedQuery) {
    searchStatusText = "Enter a ticker, ISIN, CUSIP, FIGI, or display name.";
  } else if (searching) {
    searchStatusText = `Searching Security Master for "${trimmedQuery}"...`;
  } else if (results === null) {
    searchStatusText = `Security Master search queued for "${trimmedQuery}".`;
  } else if (searchErrorText) {
    searchStatusText = searchErrorText;
  } else if (results !== null && resultCount === 0) {
    searchStatusText = `No securities found for "${trimmedQuery}".`;
  } else if (hasResults) {
    searchStatusText = `${resultCount} securities found for "${trimmedQuery}".`;
  }

  return {
    trimmedQuery,
    resultCount,
    hasResults,
    resultsTableLabel: "Security search results",
    resultColumns: securitySearchResultColumns,
    resultRows,
    searchStatusText,
    searchErrorText,
    searchErrorDetails: normalizedSearchError?.details ?? [],
    statusAnnouncement: buildSecurityStatusAnnouncement({
      searching,
      trimmedQuery,
      resultCount,
      results,
      searchErrorText,
      identityLoading,
      identityError: normalizedIdentityError?.summary ?? null
    })
  };
}

export function countOpenSecurityConflicts(conflicts: SecurityMasterConflict[] | null): number {
  return conflicts?.filter((conflict) => conflict.status === "Open").length ?? 0;
}

export function buildSecurityConflictRefreshCommand(
  loading: boolean,
  errorText: string | ApiErrorDisplay | null,
  resolvingConflictId: string | null = null
): SecurityConflictRefreshCommandViewModel {
  const normalizedError = normalizeApiErrorDisplay(errorText);
  if (resolvingConflictId) {
    const disabledReason = `Wait until identifier conflict ${resolvingConflictId} finishes resolving before refreshing the conflict queue.`;

    return {
      label: "Refresh conflicts",
      ariaLabel: `Refresh disabled while identifier conflict ${resolvingConflictId} is resolving`,
      disabled: true,
      disabledReason,
      busy: false,
      busyLabel: null,
      feedbackId: "security-conflict-refresh-feedback",
      feedbackText: disabledReason
    };
  }

  return {
    label: loading ? "Refreshing..." : normalizedError ? "Retry conflicts" : "Refresh conflicts",
    ariaLabel: loading
      ? "Refreshing Security Master identifier conflicts"
      : normalizedError
        ? "Retry loading Security Master identifier conflicts"
        : "Refresh Security Master identifier conflicts",
    disabled: loading,
    disabledReason: loading ? "Identifier conflicts are already loading." : null,
    busy: loading,
    busyLabel: loading ? "Refreshing..." : null,
    feedbackId: "security-conflict-refresh-feedback",
    feedbackText: null
  };
}

export function buildSecuritySearchResultRows(
  results: SecurityMasterEntry[] | null,
  selectedSecurityId: string | null
): SecuritySearchResultRowViewModel[] {
  return (results ?? []).map((entry) => {
    const primaryIdentifierLabel = entry.classification.primaryIdentifierKind
      ? `${entry.classification.primaryIdentifierKind}: ${entry.classification.primaryIdentifierValue}`
      : "-";
    const isSelected = selectedSecurityId === entry.securityId;

    return {
      ...entry,
      rowId: `security-result-${entry.securityId}`,
      isSelected,
      detailPanelId: SECURITY_IDENTITY_DETAIL_PANEL_ID,
      isExpanded: isSelected,
      selectAriaLabel: `Open identity drill-in for ${entry.displayName}`,
      primaryIdentifierLabel,
      statusTone: entry.status === "Active" ? "success" : "warning",
      ariaLabel: `${entry.displayName}, ${entry.classification.assetClass}, primary identifier ${primaryIdentifierLabel}, currency ${entry.economicDefinition.currency}, status ${entry.status}${isSelected ? ", selected" : ""}.`
    };
  });
}

export function buildSecurityIdentityDrillInState(
  identity: SecurityIdentityDrillIn | null
): SecurityIdentityDrillInViewState | null {
  if (!identity) {
    return null;
  }

  const effectiveRange = formatSecurityDateRange(identity.effectiveFrom, identity.effectiveTo);
  const identifiers = identity.identifiers.map(buildSecurityIdentityIdentifierRow);
  const aliases = identity.aliases.map(buildSecurityIdentityAliasRow);

  return {
    panelId: SECURITY_IDENTITY_DETAIL_PANEL_ID,
    title: `Identity drill-in · ${identity.displayName}`,
    subtitle: `${identity.assetClass || "Unclassified"} · ${identity.status || "Status unavailable"}`,
    description: `${formatCount(identifiers.length, "identifier")} · ${formatCount(aliases.length, "alias")} · effective ${effectiveRange}`,
    ariaLabel: `Security identity detail for ${identity.displayName}`,
    statusLabel: identity.status || "Unknown",
    statusBadgeVariant: statusBadgeVariantForSecurityIdentity(identity.status),
    summaryFields: [
      { label: "Security ID", value: identity.securityId },
      { label: "Version", value: `v${identity.version}` },
      { label: "Asset class", value: identity.assetClass || "—" },
      { label: "Effective", value: effectiveRange }
    ],
    identifiersTitle: "Identifiers",
    identifiersTableLabel: `Identifiers for ${identity.displayName}`,
    identifiers,
    identifierEmptyText: "No identifiers found for this security.",
    aliasesTitle: "Aliases",
    aliasesTableLabel: `Aliases for ${identity.displayName}`,
    aliases,
    aliasEmptyText: "No aliases found for this security."
  };
}

export function buildSecurityConflictRows(
  conflicts: SecurityMasterConflict[] | null,
  resolvingConflictId: string | null
): SecurityConflictRowViewModel[] {
  return (conflicts ?? []).map((conflict) => {
    const isOpen = conflict.status === "Open";
    const isResolving = resolvingConflictId === conflict.conflictId;
    const canResolve = isOpen && !isResolving;
    const actionDisabledReason = isResolving
      ? `Resolution is already in progress for identifier conflict ${conflict.conflictId}.`
      : null;
    const providerASummary = `${conflict.providerA} -> security ${formatSecurityReferenceValue(conflict.valueA)}`;
    const providerBSummary = `${conflict.providerB} -> security ${formatSecurityReferenceValue(conflict.valueB)}`;

    return {
      ...conflict,
      statusLabel: conflict.status,
      statusTone: isOpen ? "warning" : "neutral",
      isOpen,
      isResolving,
      fieldLabel: formatSecurityConflictField(conflict.fieldPath),
      providerASummary,
      providerBSummary,
      detectedLabel: `Detected ${formatConflictDate(conflict.detectedAt)}`,
      ariaLabel: `Identifier conflict ${conflict.conflictId} on ${conflict.fieldPath}: ${conflict.status}. ${providerASummary}. ${providerBSummary}.`,
      resolutionStatusText: isResolving ? `Resolving identifier conflict ${conflict.conflictId}.` : null,
      actions: isOpen
        ? [
            buildSecurityConflictAction(conflict, "AcceptA", `Use ${conflict.providerA}`, canResolve, "outline", actionDisabledReason),
            buildSecurityConflictAction(conflict, "AcceptB", `Use ${conflict.providerB}`, canResolve, "outline", actionDisabledReason),
            buildSecurityConflictAction(conflict, "Dismiss", "Dismiss conflict", canResolve, "ghost", actionDisabledReason)
          ]
        : []
    };
  });
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
  const lineCountLabel = line.lineCount === 1 ? "1 line" : `${line.lineCount.toLocaleString()} lines`;

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
  const entryCountLabel = line.entryCount.toLocaleString();
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

  return `${filteredCount.toLocaleString()} of ${totalCount.toLocaleString()} GL account rows`;
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


function buildSecurityStatusAnnouncement({
  searching,
  trimmedQuery,
  resultCount,
  results,
  searchErrorText,
  identityLoading,
  identityError
}: {
  searching: boolean;
  trimmedQuery: string;
  resultCount: number;
  results: SecurityMasterEntry[] | null;
  searchErrorText: string | null;
  identityLoading: boolean;
  identityError: string | null;
}): string {
  if (identityLoading) {
    return "Loading Security Master identity drill-in.";
  }

  if (identityError) {
    return identityError;
  }

  if (!trimmedQuery) {
    return "";
  }

  if (searching) {
    return `Searching Security Master for ${trimmedQuery}.`;
  }

  if (results === null) {
    return `Security Master search queued for ${trimmedQuery}.`;
  }

  if (searchErrorText) {
    return searchErrorText;
  }

  if (results !== null && resultCount === 0) {
    return `No securities found for ${trimmedQuery}.`;
  }

  if (resultCount > 0) {
    return `${resultCount} securities found for ${trimmedQuery}.`;
  }

  return "";
}

export function resolveSecurityScheduleEvents(securityId: string | null): SecurityCashFlowScheduleEvent[] {
  // Fixture schedules are a development-only affordance. In production a security without a
  // live trust snapshot must show an honest empty schedule, never fabricated rows with
  // posted/variance statuses. The DEV check is read at call time so tests can stub it.
  if (!securityId || !import.meta.env.DEV) {
    return [];
  }

  const events = resolveDevSecurityScheduleEvents(securityId);
  if (events.length > 0) {
    // Drives the app-shell "Demo data" notice, matching every other DEV fixture lane.
    markDevelopmentFixtureUsage();
  }
  return events;
}

export function mapScheduleBookToCashFlowScheduleEvents(
  securityId: string | null,
  snapshot: SecurityMasterTrustSnapshot | null
): SecurityCashFlowScheduleEvent[] {
  const scheduleBook = snapshot?.scheduleBook ?? null;
  if (!securityId || !scheduleBook) {
    return [];
  }

  return scheduleBook.events.map((event) => ({
    eventId: event.eventId,
    securityId,
    scheduleFamily: event.factorStart !== null || event.factorEnd !== null ? "structured" : "bond",
    eventType: event.eventType,
    paymentDate: event.payDate ?? event.effectiveDate,
    accrualStartDate: event.accrualStartDate,
    accrualEndDate: event.accrualEndDate,
    couponRatePct: null,
    expectedAmount: event.expectedAmount,
    actualAmount: event.actualAmount,
    principalAmount: null,
    interestAmount: null,
    factorStart: event.factorStart,
    factorEnd: event.factorEnd,
    currency: event.currency || scheduleBook.currency,
    postingStatus: event.postingStatus,
    auditReference: formatScheduleEventSource(event.sourceSystem, event.sourceRecordId),
    note: event.sourceReason ?? (event.isCurrentProjection ? "Current schedule projection." : null)
  }));
}

export function buildSecurityScheduleRows(
  schedules: SecurityCashFlowScheduleEvent[] | null,
  selectedRowId: string | null = null
): SecurityScheduleRowViewModel[] {
  const detailPanelId = "security-schedule-detail-panel";
  const rows = schedules ?? [];
  const effectiveSelectedRowId = selectedRowId && rows.some((event) => event.eventId === selectedRowId)
    ? selectedRowId
    : rows[0]?.eventId ?? null;

  return rows.map((event) => {
    const isSelected = event.eventId === effectiveSelectedRowId;
    const eventTypeLabel = formatSecurityScheduleEventType(event.eventType);
    const paymentDateLabel = formatSecurityDate(event.paymentDate);
    const expectedAmountLabel = formatScheduleAmount(event.expectedAmount, event.currency);
    const actualAmountLabel = formatScheduleAmount(event.actualAmount, event.currency);
    const varianceLabel = formatScheduleVariance(event.expectedAmount, event.actualAmount, event.currency);
    const factorLabel = formatScheduleFactor(event.factorStart, event.factorEnd);
    const postingStatusTone = securitySchedulePostingTone(event.postingStatus);

    return {
      ...event,
      rowId: event.eventId,
      eventTypeLabel,
      paymentDateLabel,
      expectedAmountLabel,
      actualAmountLabel,
      varianceLabel,
      factorLabel,
      postingStatusLabel: formatSecuritySchedulePostingStatus(event.postingStatus),
      postingStatusTone,
      ariaLabel: `${eventTypeLabel} for ${event.securityId}, payment ${paymentDateLabel}, expected ${expectedAmountLabel}, actual ${actualAmountLabel}, variance ${varianceLabel}, status ${event.postingStatus}`,
      selectAriaLabel: `Inspect schedule event ${eventTypeLabel} for ${event.securityId} on ${paymentDateLabel}`,
      detailPanelId,
      isExpanded: isSelected
    };
  });
}

export function buildSecuritySchedulesViewState({
  securityId,
  displayName,
  assetClass,
  schedules,
  selectedRowId,
  loading = false,
  error = null,
  factorHistoryCount = 0,
  provenanceCount = 0,
  sourceSummary = null
}: {
  securityId: string | null;
  displayName: string | null;
  assetClass: string | null;
  schedules: SecurityCashFlowScheduleEvent[] | null;
  selectedRowId: string | null;
  loading?: boolean;
  error?: string | ApiErrorDisplay | null;
  factorHistoryCount?: number;
  provenanceCount?: number;
  sourceSummary?: string | null;
}): SecuritySchedulesViewState {
  const normalizedError = normalizeApiErrorDisplay(error);
  const errorText = normalizedError?.summary ?? null;
  const displaySecurityId = securityId ?? "selected security";
  const displayNameLabel = displayName?.trim() || displaySecurityId;
  const displayAssetClass = assetClass?.trim() || "Unclassified";
  const rows = buildSecurityScheduleRows(schedules, selectedRowId);
  const effectiveSelectedRowId = rows.find((row) => row.rowId === selectedRowId)?.rowId ?? rows[0]?.rowId ?? null;
  const selectedRow = rows.find((row) => row.rowId === effectiveSelectedRowId) ?? null;
  const eventCount = rows.length;
  const pendingCount = rows.filter((row) => row.postingStatus === "Pending" || row.postingStatus === "Forecast").length;
  const varianceCount = rows.filter((row) => row.postingStatus === "Variance").length;
  const factorCount = rows.filter((row) => row.factorStart !== null || row.factorEnd !== null).length;

  return {
    securityId: displaySecurityId,
    title: "Cash-flow and factor schedules",
    description: sourceSummary?.trim()
      ? `${displayNameLabel} schedule events stay attached to the selected ${displayAssetClass} reference record. ${sourceSummary}`
      : `${displayNameLabel} schedule events stay attached to the selected ${displayAssetClass} reference record for payment, posting, variance, and audit review.`,
    tableLabel: `Cash-flow and factor schedules for ${displaySecurityId}`,
    tableCaption: `Cash-flow and factor schedule evidence for ${displaySecurityId}; select a row to inspect event detail.`,
    detailPanelId: "security-schedule-detail-panel",
    toolbarAriaLabel: `Cash-flow schedule status for ${displaySecurityId}`,
    toolbarItems: [
      { id: "events", label: "Events", value: String(eventCount), active: eventCount > 0 },
      { id: "pending", label: "Pending", value: String(pendingCount) },
      { id: "variance", label: "Variance", value: String(varianceCount) },
      { id: "factor", label: "Factor rows", value: String(Math.max(factorCount, factorHistoryCount)) },
      { id: "sources", label: "Sources", value: String(provenanceCount) }
    ],
    rows,
    selectedRowId: effectiveSelectedRowId,
    selectedDetail: selectedRow ? buildSecurityScheduleDetailViewState(selectedRow) : null,
    emptyText: `No cash-flow or factor schedule rows are available for ${displaySecurityId}.`,
    detailEmptyTitle: "No schedule event selected",
    detailEmptyText: "Select a schedule row to inspect payment dates, expected and actual amounts, factors, posting state, and audit evidence.",
    detailEmptyAriaLabel: "No cash-flow schedule event selected",
    loadingText: loading ? "Loading schedule book..." : null,
    errorText,
    errorDetails: normalizedError?.details ?? [],
    hasRows: eventCount > 0,
    statusAnnouncement: errorText
      ? `Schedule book error: ${errorText}`
      : loading
        ? `Loading schedule book for ${displaySecurityId}.`
        : eventCount > 0
          ? `${eventCount} cash-flow schedule ${eventCount === 1 ? "event" : "events"} loaded for ${displaySecurityId}.`
          : ""
  };
}

function buildSecurityScheduleDetailViewState(row: SecurityScheduleRowViewModel): SecurityScheduleDetailViewState {
  const accrualWindow = `${formatSecurityDate(row.accrualStartDate)} -> ${formatSecurityDate(row.accrualEndDate)}`;
  const varianceTone = scheduleVarianceTone(row.expectedAmount, row.actualAmount, row.postingStatus);

  return {
    id: row.detailPanelId,
    eyebrow: "Schedule event detail",
    title: row.eventTypeLabel,
    subtitle: `${row.securityId} · ${row.paymentDateLabel}`,
    description: `${row.eventTypeLabel} event expected at ${row.expectedAmountLabel}; posting state is ${row.postingStatusLabel}.`,
    ariaLabel: `Cash-flow schedule detail for ${row.eventTypeLabel} on ${row.securityId}`,
    statusLabel: row.postingStatusLabel,
    statusTone: row.postingStatusTone,
    fields: [
      { label: "Schedule event ID", value: row.eventId },
      { label: "Event type", value: row.eventTypeLabel },
      { label: "Payment date", value: row.paymentDateLabel },
      { label: "Accrual window", value: accrualWindow, tone: accrualWindow.includes("—") ? "warning" : "default" },
      { label: "Coupon rate", value: row.couponRatePct !== null ? `${row.couponRatePct.toFixed(3)}%` : "—" },
      { label: "Interest", value: formatScheduleAmount(row.interestAmount, row.currency) },
      { label: "Principal", value: formatScheduleAmount(row.principalAmount, row.currency) },
      { label: "Expected", value: row.expectedAmountLabel },
      { label: "Actual", value: row.actualAmountLabel, tone: row.actualAmount === null ? "warning" : "default" },
      { label: "Variance", value: row.varianceLabel, tone: varianceTone },
      { label: "Factor", value: row.factorLabel },
      { label: "Audit reference", value: row.auditReference ?? "—", tone: row.auditReference ? "default" : "warning" },
      { label: "Note", value: row.note ?? "—" }
    ]
  };
}

export function buildCorporateActionRows(
  actions: CorporateAction[] | null,
  selectedRowId: string | null = null
): CorporateActionRowViewModel[] {
  const detailPanelId = "corporate-action-detail-panel";
  const rows = actions ?? [];
  const effectiveSelectedRowId = selectedRowId && rows.some((action) => action.corpActId === selectedRowId)
    ? selectedRowId
    : rows[0]?.corpActId ?? null;

  return rows.map((action) => {
    const amountLabel = formatCorpActAmount(action);
    const eventTypeLabel = formatCorpActEventType(action.eventType);
    const exDateLabel = formatSecurityDate(action.exDate);
    const isSelected = action.corpActId === effectiveSelectedRowId;

    return {
      ...action,
      rowId: action.corpActId,
      eventTypeLabel,
      exDateLabel,
      payDateLabel: action.payDate ? formatSecurityDate(action.payDate) : "—",
      amountLabel,
      ariaLabel: `${eventTypeLabel} for ${action.securityId}, ex-date ${exDateLabel}, ${amountLabel}`,
      selectAriaLabel: `Inspect corporate action ${eventTypeLabel} for ${action.securityId}`,
      detailPanelId,
      isExpanded: isSelected
    };
  });
}

export function buildCorporateActionsViewState(
  securityId: string | null,
  actions: CorporateAction[] | null,
  selectedRowId: string | null,
  loading: boolean,
  error: string | ApiErrorDisplay | null
): CorporateActionsViewState {
  const normalizedError = normalizeApiErrorDisplay(error);
  const errorText = normalizedError?.summary ?? null;
  const rows = buildCorporateActionRows(actions, selectedRowId);
  const effectiveSelectedRowId = rows.find((row) => row.rowId === selectedRowId)?.rowId ?? rows[0]?.rowId ?? null;
  const selectedRow = rows.find((row) => row.rowId === effectiveSelectedRowId) ?? null;
  const displaySecurityId = securityId ?? "selected security";
  const detailPanelId = "corporate-action-detail-panel";

  return {
    securityId: displaySecurityId,
    tableLabel: `Corporate actions for ${displaySecurityId}`,
    tableCaption: `Corporate actions evidence for ${displaySecurityId}; select a row to inspect event detail.`,
    detailPanelId,
    rows,
    selectedRowId: effectiveSelectedRowId,
    selectedDetail: selectedRow ? buildCorporateActionDetailViewState(selectedRow, detailPanelId) : null,
    emptyText: `No corporate actions recorded for ${displaySecurityId}.`,
    detailEmptyTitle: "No corporate action selected",
    detailEmptyText: "Select a corporate action row to inspect dates, ratios, securities, and cash terms.",
    detailEmptyAriaLabel: "No corporate action selected",
    loadingText: loading ? "Loading corporate actions..." : null,
    errorText,
    errorDetails: normalizedError?.details ?? [],
    hasRows: rows.length > 0,
    statusAnnouncement: errorText
      ? `Corporate actions error: ${errorText}`
      : loading
        ? `Loading corporate actions for ${displaySecurityId}.`
        : rows.length > 0
          ? `${rows.length} corporate action${rows.length === 1 ? "" : "s"} loaded for ${displaySecurityId}.`
          : ""
  };
}

function buildCorporateActionDetailViewState(
  row: CorporateActionRowViewModel,
  detailPanelId: string
): CorporateActionDetailViewState {
  return {
    id: detailPanelId,
    eyebrow: "Corporate action detail",
    title: row.eventTypeLabel,
    subtitle: `${row.securityId} · ${row.corpActId}`,
    description: `${row.eventTypeLabel} event with ex-date ${row.exDateLabel} and recorded amount ${row.amountLabel}.`,
    ariaLabel: `Corporate action detail for ${row.eventTypeLabel} on ${row.securityId}`,
    statusLabel: row.payDate ? "Pay date scheduled" : "Pay date unavailable",
    fields: [
      { label: "Corporate action ID", value: row.corpActId },
      { label: "Event type", value: row.eventTypeLabel },
      { label: "Ex-date", value: row.exDateLabel },
      { label: "Pay date", value: row.payDateLabel, tone: row.payDate ? "default" : "warning" },
      { label: "Amount or ratio", value: row.amountLabel, tone: row.amountLabel === "—" ? "warning" : "default" },
      { label: "Currency", value: row.currency ?? "—", tone: row.currency ? "default" : "warning" },
      { label: "New security", value: row.newSecurityId ?? "—" },
      { label: "Acquirer security", value: row.acquirerSecurityId ?? "—" }
    ]
  };
}

export function buildSecurityOpenLotRows(
  readModel: SecurityMasterOpenLotReadModel | null,
  selectedRowId: string | null = null
): SecurityOpenLotRowViewModel[] {
  const detailPanelId = "security-open-lot-detail-panel";
  const rows = readModel?.lots ?? [];
  const effectiveSelectedRowId = selectedRowId && rows.some((lot) => lot.lotId === selectedRowId)
    ? selectedRowId
    : rows[0]?.lotId ?? null;

  return rows.map((lot) => {
    const isSelected = lot.lotId === effectiveSelectedRowId;
    const scopeLabel = formatOpenLotScope(lot);
    const quantityLabel = formatQuantity(lot.currentQuantity);
    const faceLabel = lot.currentFace !== null ? formatQuantity(lot.currentFace) : "—";
    const factorAdjustedLabel = lot.factorAdjustedQuantity !== null
      ? formatQuantity(lot.factorAdjustedQuantity)
      : lot.factorAdjustedFace !== null
        ? formatQuantity(lot.factorAdjustedFace)
        : "—";
    const costBasisLabel = formatScheduleAmount(lot.costBasis, lot.currency);
    const unrealizedPnlLabel = lot.unrealizedPnl !== null ? formatSignedScheduleAmount(lot.unrealizedPnl, lot.currency) : "—";
    const statusTone = openLotStatusTone(lot.lotStatus);

    return {
      ...lot,
      rowId: lot.lotId,
      tradeDateLabel: formatSecurityDate(lot.tradeDate),
      settleDateLabel: formatSecurityDate(lot.settleDate),
      quantityLabel,
      faceLabel,
      factorAdjustedLabel,
      costBasisLabel,
      entryPriceLabel: formatScheduleAmount(lot.entryPrice, lot.currency),
      unrealizedPnlLabel,
      scopeLabel,
      statusLabel: lot.lotStatus || "Unknown",
      statusTone,
      ariaLabel: `Open lot ${lot.lotId} for ${lot.symbol}, ${scopeLabel}, quantity ${quantityLabel}, cost ${costBasisLabel}, status ${lot.lotStatus}`,
      selectAriaLabel: `Inspect open lot ${lot.lotId} for ${lot.symbol}`,
      detailPanelId,
      isExpanded: isSelected
    };
  });
}

export function buildSecurityOpenLotReadModelViewState({
  securityId,
  readModel,
  selectedRowId,
  loading = false,
  error = null
}: {
  securityId: string | null;
  readModel: SecurityMasterOpenLotReadModel | null;
  selectedRowId: string | null;
  loading?: boolean;
  error?: string | ApiErrorDisplay | null;
}): SecurityOpenLotReadModelViewState {
  const normalizedError = normalizeApiErrorDisplay(error);
  const errorText = normalizedError?.summary ?? null;
  const displaySecurityId = securityId ?? "selected security";
  const rows = buildSecurityOpenLotRows(readModel, selectedRowId);
  const effectiveSelectedRowId = rows.find((row) => row.rowId === selectedRowId)?.rowId ?? rows[0]?.rowId ?? null;
  const selectedRow = rows.find((row) => row.rowId === effectiveSelectedRowId) ?? null;
  const lotCount = rows.length;
  const longTermCount = rows.filter((row) => row.isLongTerm).length;
  const factorAdjustedCount = rows.filter((row) => row.factorAdjustedQuantity !== null || row.factorAdjustedFace !== null).length;
  const provenanceCount = readModel?.provenanceHistory.length ?? 0;

  return {
    securityId: displaySecurityId,
    title: "Open lot read model",
    description: `Open lots for ${displaySecurityId} stay tied to run, account, cost, quantity, and factor-adjusted exposure evidence.`,
    summary: readModel?.summary ?? "Open lot read model is unavailable for the selected security.",
    asOfLabel: readModel?.asOfUtc ? formatDateTimeLabel(readModel.asOfUtc) : "—",
    tableLabel: `Open lot read model for ${displaySecurityId}`,
    tableCaption: `Open lots for ${displaySecurityId}; select a row to inspect source, account, and exposure detail.`,
    detailPanelId: "security-open-lot-detail-panel",
    toolbarAriaLabel: `Open lot posture for ${displaySecurityId}`,
    toolbarItems: [
      { id: "lots", label: "Lots", value: String(lotCount), active: lotCount > 0 },
      { id: "long-term", label: "Long term", value: String(longTermCount) },
      { id: "factor", label: "Factor adj.", value: String(factorAdjustedCount) },
      { id: "sources", label: "Sources", value: String(provenanceCount) }
    ],
    rows,
    selectedRowId: effectiveSelectedRowId,
    selectedDetail: selectedRow ? buildSecurityOpenLotDetailViewState(selectedRow, readModel) : null,
    emptyText: `No open lots are available for ${displaySecurityId}.`,
    detailEmptyTitle: "No open lot selected",
    detailEmptyText: "Select an open lot to inspect quantity model, cost, account scope, source record, and provenance.",
    detailEmptyAriaLabel: "No open lot selected",
    loadingText: loading ? "Loading open lot read model..." : null,
    errorText,
    errorDetails: normalizedError?.details ?? [],
    hasRows: lotCount > 0,
    statusAnnouncement: errorText
      ? `Open lot read model error: ${errorText}`
      : loading
        ? `Loading open lot read model for ${displaySecurityId}.`
        : lotCount > 0
          ? `${lotCount} open ${lotCount === 1 ? "lot" : "lots"} loaded for ${displaySecurityId}.`
          : ""
  };
}

function buildSecurityOpenLotDetailViewState(
  row: SecurityOpenLotRowViewModel,
  readModel: SecurityMasterOpenLotReadModel | null
): SecurityOpenLotDetailViewState {
  const quantityModel = readModel?.quantityModel || "Unspecified quantity model";
  const factor = readModel?.currentFactor !== null && readModel?.currentFactor !== undefined
    ? readModel.currentFactor.toFixed(6)
    : "—";

  return {
    id: row.detailPanelId,
    eyebrow: "Open lot detail",
    title: row.lotId,
    subtitle: `${row.symbol} · ${row.scopeLabel}`,
    description: `${row.lotStatus} lot using ${quantityModel}; current quantity ${row.quantityLabel}.`,
    ariaLabel: `Open lot detail for ${row.lotId} on ${row.symbol}`,
    statusLabel: row.statusLabel,
    statusTone: row.statusTone,
    fields: [
      { label: "Run", value: row.runId },
      { label: "Portfolio", value: row.portfolioId },
      { label: "Account", value: row.accountScopeDisplayName ?? row.accountScopeId ?? "—", tone: row.accountScopeId ? "default" : "warning" },
      { label: "Vehicle", value: row.vehicleScopeDisplayName ?? row.vehicleScopeId ?? "—" },
      { label: "Trade date", value: row.tradeDateLabel },
      { label: "Settle date", value: row.settleDateLabel, tone: row.settleDate ? "default" : "warning" },
      { label: "Original quantity", value: formatQuantity(row.originalQuantity) },
      { label: "Current quantity", value: row.quantityLabel },
      { label: "Current face", value: row.faceLabel },
      { label: "Factor-adjusted exposure", value: row.factorAdjustedLabel, tone: row.factorAdjustedLabel === "—" ? "warning" : "success" },
      { label: "Current factor", value: factor },
      { label: "Cost basis", value: row.costBasisLabel },
      { label: "Entry price", value: row.entryPriceLabel },
      { label: "Unrealized P&L", value: row.unrealizedPnlLabel, tone: row.unrealizedPnl === null ? "warning" : row.unrealizedPnl >= 0 ? "success" : "danger" },
      { label: "Source", value: formatScheduleEventSource(row.sourceSystem, row.sourceRecordId) },
      { label: "Source reason", value: row.sourceReason ?? "—" },
      { label: "Notes", value: row.notes ?? "—" }
    ]
  };
}

function buildReferenceDataWorkbenchSeed(
  securityId: string,
  entry: SecurityMasterEntry | null
): ReferenceDataWorkbenchEndpointSeed {
  const symbol = normalizeReferenceWorkbenchSymbol(
    entry?.classification.primaryIdentifierValue ?? entry?.displayName ?? securityId
  );
  const currency = entry?.economicDefinition.currency || "USD";
  const primaryKind = entry?.classification.primaryIdentifierKind ?? null;
  const primaryValue = entry?.classification.primaryIdentifierValue ?? null;
  const issuerName = entry?.displayName ?? symbol;

  return {
    securityId,
    displayName: entry?.displayName ?? null,
    primaryIdentifierKind: primaryKind,
    primaryIdentifierValue: primaryValue,
    currency,
    issuerName,
    optionContractSymbol: `${symbol}20261219C00150000`,
    optionChainId: `${symbol}-20261219`,
    optionExpiryDate: "2026-12-19",
    underlyingSymbol: symbol,
    exchangeCode: "XNYS",
    rootSymbol: symbol.slice(0, 3) || "ES",
    fxPairCode: currency === "USD" ? "EURUSD" : `${currency}USD`,
    swapType: entry?.economicDefinition.subType ?? "InterestRate",
    commodityType: entry?.economicDefinition.assetFamily ?? "Energy",
    network: "Ethereum",
    baseCurrency: currency === "USD" ? "BTC" : currency,
    institutionName: issuerName,
    fundFamily: issuerName,
    cik: primaryKind?.toLowerCase() === "cik" ? normalizeReferenceWorkbenchCik(primaryValue) : null,
    maturityFrom: "2026-01-01",
    maturityTo: "2036-12-31",
    beforeDate: "2031-12-31"
  };
}

export function buildReferenceDataWorkbenchViewState({
  securityId,
  coverage,
  loading = false,
  error = null,
  selectedRowId = null
}: {
  securityId: string | null;
  coverage: ReferenceDataWorkbenchCoverage | null;
  loading?: boolean;
  error?: string | ApiErrorDisplay | null;
  selectedRowId?: string | null;
}): ReferenceDataWorkbenchViewState {
  const normalizedError = normalizeApiErrorDisplay(error);
  const errorText = normalizedError?.summary ?? null;
  const rows = buildReferenceDataEndpointRows(coverage, selectedRowId);
  const effectiveSelectedRowId = rows.some((row) => row.rowId === selectedRowId)
    ? selectedRowId
    : rows[0]?.rowId ?? null;
  const selectedRow = rows.find((row) => row.rowId === effectiveSelectedRowId) ?? null;
  const routeCounts = summarizeReferenceDataRoutes(rows);
  const displaySecurityId = securityId ?? "selected security";

  return {
    securityId,
    title: "Multi-asset reference data",
    description: securityId
      ? `Reference data coverage for ${displaySecurityId} across bonds, options, equities, futures, FX spot, swaps, commodities, crypto, deposits, money-market funds, CDs, and EDGAR.`
      : "Select a security to inspect multi-asset reference data coverage.",
    metrics: [
      {
        id: "routes",
        label: "Mapped routes",
        value: routeCounts.totalCount > 0 ? routeCounts.totalCount.toLocaleString() : "Pending",
        detail: routeCounts.totalCount > 0
          ? `${routeCounts.readyCount.toLocaleString()} ready · ${routeCounts.reviewCount.toLocaleString()} need review · ${routeCounts.deferredOrBlockedCount.toLocaleString()} deferred or blocked · ${routeCounts.totalCount.toLocaleString()} total.`
          : "Select a security to build reference data checks.",
        tone: routeCounts.totalCount > 0 ? "default" : "warning"
      },
      {
        id: "ready",
        label: "Ready data",
        value: routeCounts.readyCount.toLocaleString(),
        detail: routeCounts.readyCount > 0 ? `${formatCount(routeCounts.readyCount, "reference route")} returned data.` : "No reference route has returned data yet.",
        tone: routeCounts.readyCount > 0 ? "success" : "default"
      },
      {
        id: "review",
        label: "Needs review",
        value: routeCounts.reviewCount.toLocaleString(),
        detail: routeCounts.reviewCount > 0 ? `${formatCount(routeCounts.reviewCount, "data source")} returned empty, missing, or error status.` : "No checked data source is flagged for review.",
        tone: routeCounts.reviewCount > 0 ? "warning" : "success"
      },
      {
        id: "deferred",
        label: "Deferred / blocked",
        value: routeCounts.deferredOrBlockedCount.toLocaleString(),
        detail: routeCounts.deferredOrBlockedCount > 0
          ? [
              routeCounts.deferredCount > 0 ? `${formatCount(routeCounts.deferredCount, "write-capable source")} intentionally deferred.` : null,
              routeCounts.blockedCount > 0 ? `${formatCount(routeCounts.blockedCount, "data source")} blocked.` : null
            ].filter((detail): detail is string => detail !== null).join(" ")
          : "No reference route is deferred or blocked.",
        tone: routeCounts.deferredOrBlockedCount > 0 ? "warning" : "default"
      }
    ],
    rows,
    selectedRowId: effectiveSelectedRowId,
    selectedDetail: selectedRow ? buildReferenceDataEndpointDetailViewState(selectedRow, coverage) : null,
    tableLabel: `Reference data source coverage for ${displaySecurityId}`,
    tableCaption: `Read-only status for mapped reference data sources backing ${displaySecurityId}.`,
    detailPanelId: "reference-data-endpoint-detail",
    emptyText: securityId ? "No reference data source rows are available for this security." : "Select a security to load reference data source coverage.",
    detailEmptyTitle: "No data source selected",
    detailEmptyText: "Select a data source row to inspect coverage detail, response summary, and error evidence.",
    detailEmptyAriaLabel: `Reference data source detail for ${displaySecurityId}`,
    loadingText: loading ? "Loading multi-asset reference data coverage..." : null,
    errorText,
    errorDetails: normalizedError?.details ?? [],
    hasRows: rows.length > 0,
    statusAnnouncement: errorText
        ? `Reference data workbench error: ${errorText}`
      : loading
        ? `Loading multi-asset reference data coverage for ${displaySecurityId}.`
        : rows.length > 0
          ? `${formatCount(rows.length, "reference data source")} loaded for ${displaySecurityId}; ${routeCounts.readyCount.toLocaleString()} ready, ${routeCounts.reviewCount.toLocaleString()} need review, and ${routeCounts.deferredOrBlockedCount.toLocaleString()} deferred or blocked.`
          : ""
  };
}

function buildReferenceDataEndpointRows(
  coverage: ReferenceDataWorkbenchCoverage | null,
  selectedRowId: string | null
): ReferenceDataEndpointRowViewModel[] {
  return (coverage?.endpoints ?? []).map((endpoint) => {
    const rowId = `reference-data-${endpoint.id}`;
    const statusBadgeVariant = referenceDataStatusBadgeVariant(endpoint.status);
    const countLabel = endpoint.responseCount === null ? "-" : formatCount(endpoint.responseCount, "record");
    const latencyLabel = endpoint.durationMs === null ? "-" : `${endpoint.durationMs} ms`;
    const statusLabel = referenceDataStatusLabel(endpoint.status);
    const accessLabel = endpoint.mutation ? "Write-capable" : "Read-only";
    const displaySummary = formatFinanceFacingSourceSummary(endpoint.responseSummary, endpoint.errorSummary);

    return {
      ...endpoint,
      rowId,
      familyLabel: endpoint.family,
      methodLabel: endpoint.method,
      accessLabel,
      displaySummary,
      statusLabel,
      statusBadgeVariant,
      countLabel,
      latencyLabel,
      ariaLabel: `${endpoint.family} ${endpoint.label}, ${statusLabel}. ${displaySummary}`,
      selectAriaLabel: `Inspect ${endpoint.label} reference data source`,
      detailPanelId: "reference-data-endpoint-detail",
      isExpanded: rowId === selectedRowId
    };
  });
}

function buildReferenceDataEndpointDetailViewState(
  row: ReferenceDataEndpointRowViewModel,
  coverage: ReferenceDataWorkbenchCoverage | null
): ReferenceDataEndpointDetailViewState {
  return {
    id: row.detailPanelId,
    eyebrow: row.familyLabel,
    title: row.label,
    subtitle: `Reference data source: ${row.familyLabel}`,
    description: row.errorSummary ?? row.displaySummary,
    ariaLabel: `${row.label} reference data source detail`,
    statusLabel: row.statusLabel,
    statusBadgeVariant: row.statusBadgeVariant,
    fields: [
      { label: "Family", value: row.familyLabel },
      { label: "Access", value: row.accessLabel, tone: row.mutation ? "warning" : "default" },
      { label: "Source family", value: row.familyLabel },
      { label: "Review scope", value: formatFinanceFacingSourceSummary(row.requestLabel) },
      { label: "Status", value: row.statusLabel, tone: referenceDataStatusTone(row.status) },
      { label: "Records", value: row.countLabel },
      { label: "Latency", value: row.latencyLabel },
      { label: "Catalogued", value: coverage?.requestedAtUtc ? formatDateTimeLabel(coverage.requestedAtUtc) : "-" }
    ],
    responsePreview: row.responsePreview,
    errorSummary: row.errorSummary,
    errorDetails: row.errorDetails
  };
}

function normalizeReferenceWorkbenchSymbol(value: string): string {
  const match = value.toUpperCase().match(/[A-Z0-9]{1,8}/);
  return match?.[0] ?? "AAPL";
}

function normalizeReferenceWorkbenchCik(value: string | null | undefined): string | null {
  const normalized = value?.replace(/\D/g, "");
  return normalized || null;
}

export function buildInstrumentPassportViewState({
  securityId,
  passport,
  loading = false,
  error = null
}: {
  securityId: string | null;
  passport: InstrumentPassport | null;
  loading?: boolean;
  error?: string | ApiErrorDisplay | null;
}): InstrumentPassportViewState {
  const normalizedError = normalizeApiErrorDisplay(error);
  const errorText = normalizedError?.summary ?? null;
  const displaySecurityId = securityId ?? passport?.securityId ?? "selected security";
  const providerRows = buildInstrumentPassportProviderRows(passport);
  const trustTone = passport?.trustPosture?.tone?.trim() || "Unknown";
  const trustSummary = passport?.trustPosture?.summary?.trim() || "Trust posture is unavailable.";
  const identifierSummary = passport?.identifierSummary?.summary?.trim() || "Identifier summary is unavailable.";
  const usageSummary = passport?.usage?.summary?.trim() || "Downstream usage is unavailable.";
  const pricingStatus = passport?.pricing?.status?.trim() || "Unknown";
  const pricingSummary = passport?.pricing?.summary?.trim() || "Pricing and trading controls are unavailable.";
  const operatingModel = passport?.operatingModel ?? null;
  const operatingModelStatus = operatingModel?.status?.trim() || "Unavailable";
  const operatingModelSummary = operatingModel?.summary?.trim() || "Security Master operating model is unavailable.";
  const operatingModelStageFields = (operatingModel?.stages ?? []).map((stage) => ({
    label: stage.title,
    value: `${stage.status}: ${stage.summary} Evidence ${stage.evidenceCount}; blockers ${stage.blockingIssueCount}.`,
    tone: stage.status.toLowerCase() === "ready" ? "success" as const : "warning" as const
  }));
  const mostSpecificEntitlements = operatingModel?.entitlementApplicability?.filter((row) => row.isApplicable && row.isMostSpecific) ?? [];
  const approvalPosture = operatingModel?.manualChangeApproval ?? null;
  const referenceDataWorkbench = passport?.referenceDataWorkbench ?? null;
  const referenceDataWorkbenchStatus = referenceDataWorkbench?.status?.trim() || "Unavailable";
  const referenceDataWorkbenchSummary = referenceDataWorkbench?.summary?.trim() || "Reference-data workbench is unavailable.";
  const referenceDataWorkbenchSections = (referenceDataWorkbench?.sections ?? []).map((section) => ({
    label: section.title,
    value: `${section.status}: ${section.summary} Evidence ${section.evidenceCount}; blockers ${section.blockingIssueCount}.`,
    tone: section.status.toLowerCase() === "ready" ? "success" as const : "warning" as const
  }));
  const classificationProfile = passport?.classificationProfile ?? null;
  const classificationFields: InstrumentPassportFieldViewModel[] = classificationProfile
    ? [
        {
          label: "Instrument type",
          value: `${classificationProfile.displayName} (${classificationProfile.instrumentType})`,
          tone: classificationProfile.isReferenceOnly ? "default" : "success"
        },
        {
          label: "Provider routing",
          value: `${classificationProfile.defaultProviderSecurityType}: ${formatInstrumentPassportProfileList(
            classificationProfile.providerCapabilities,
            "No provider capabilities mapped."
          )}`
        },
        {
          label: "Lifecycle profile",
          value: formatInstrumentPassportProfileList(
            classificationProfile.lifecycleEvents,
            "No lifecycle events mapped."
          )
        },
        {
          label: "Ledger behavior",
          value: formatInstrumentPassportProfileList(
            classificationProfile.ledgerBehaviorHints,
            "No ledger behavior hints mapped."
          )
        }
      ]
    : [];
  const enabledHandoffs = referenceDataWorkbench?.operationsHandoffs?.filter((handoff) => handoff.isEnabled).length ?? 0;
  const totalHandoffs = referenceDataWorkbench?.operationsHandoffs?.length ?? 0;
  const operationsWorkbench = passport?.operationsWorkbench ?? null;
  const operationsWorkbenchStatus = operationsWorkbench?.status?.trim() || "Unavailable";
  const operationsReadiness = buildInstrumentPassportOperationsReadinessRows(passport);
  const operationsPanels = buildInstrumentPassportOperationsPanelRows(passport);
  const activeProviderCount = providerRows.filter((row) => row.isActive).length;
  const statusBadgeVariant = trustTone.toLowerCase() === "trusted"
    ? "success"
    : trustTone.toLowerCase() === "blocked" || trustTone.toLowerCase() === "review"
      ? "warning"
      : "outline";

  return {
    securityId: displaySecurityId,
    title: "Instrument passport",
    description: passport
      ? `${passport.identity?.displayName ?? "Selected security"} passport combines identifiers, provider confidence, lifecycle, pricing, and downstream usage evidence.`
      : `Instrument passport evidence for ${displaySecurityId}.`,
    statusLabel: trustTone,
    statusBadgeVariant,
    fields: [
      { label: "Security ID", value: passport?.securityId ?? displaySecurityId },
      { label: "Display name", value: passport?.identity?.displayName ?? "-" },
      { label: "Asset class", value: passport?.identity?.assetClass ?? "-" },
      ...classificationFields,
      { label: "Trust", value: trustSummary, tone: statusBadgeVariant === "success" ? "success" : statusBadgeVariant === "warning" ? "warning" : "default" },
      { label: "Identifiers", value: identifierSummary },
      { label: "Provider confidence", value: `${activeProviderCount} active / ${providerRows.length} total` },
      { label: "Pricing", value: `${pricingStatus}: ${pricingSummary}` },
      { label: "Usage", value: usageSummary },
      {
        label: "Operating model",
        value: `${operatingModelStatus}: ${operatingModelSummary}`,
        tone: operatingModelStatus.toLowerCase() === "ready" ? "success" : "warning"
      },
      ...operatingModelStageFields,
      {
        label: "Entitlement applicability",
        value: `${mostSpecificEntitlements.length} most-specific applicable entitlement(s).`,
        tone: mostSpecificEntitlements.length > 0 ? "success" : "warning"
      },
      {
        label: "Manual-change approval",
        value: approvalPosture
          ? `${approvalPosture.status}: ${approvalPosture.policyKey} via ${approvalPosture.gate}; ${approvalPosture.summary}`
          : "Unavailable: operations approval policy posture is unavailable.",
        tone: approvalPosture?.status?.toLowerCase() === "ready" ? "success" : "warning"
      },
      {
        label: "Reference-data workbench",
        value: `${referenceDataWorkbenchStatus}: ${referenceDataWorkbenchSummary}`,
        tone: referenceDataWorkbenchStatus.toLowerCase() === "ready" ? "success" : "warning"
      },
      ...referenceDataWorkbenchSections,
      {
        label: "Operations handoff",
        value: `${enabledHandoffs} enabled / ${totalHandoffs} total handoff(s).`,
        tone: enabledHandoffs > 0 ? "success" : "warning"
      },
      {
        label: "Operations workbench",
        value: `${operationsWorkbenchStatus}: ${operationsWorkbench?.summary?.trim() || "Security Master operations workbench is unavailable."}`,
        tone: operationsWorkbenchStatus.toLowerCase() === "ready" ? "success" : "warning"
      },
      { label: "Retrieved", value: passport?.retrievedAtUtc ? formatDateTimeLabel(passport.retrievedAtUtc) : "-" }
    ],
    providerRows,
    operationsWorkbenchTitle: "Operations workbench",
    operationsWorkbenchSummary: operationsWorkbench?.summary?.trim() || "Operations workbench evidence is unavailable for this passport.",
    operationsWorkbenchStatusLabel: operationsWorkbenchStatus,
    operationsWorkbenchStatusBadgeVariant: instrumentPassportStatusVariant(operationsWorkbenchStatus),
    operationsReadiness,
    operationsPanels,
    providerTableLabel: `Provider confidence for ${displaySecurityId}`,
    providerTableCaption: `Provider symbol confidence and conflict evidence for ${displaySecurityId}.`,
    providerEmptyText: `No provider confidence rows are available for ${displaySecurityId}.`,
    loadingText: loading ? "Loading instrument passport..." : null,
    errorText,
    errorDetails: normalizedError?.details ?? [],
    statusAnnouncement: errorText
      ? `Instrument passport error: ${errorText}`
      : loading
        ? `Loading instrument passport for ${displaySecurityId}.`
        : passport
          ? `Instrument passport loaded for ${displaySecurityId}.`
          : ""
  };
}

function formatInstrumentPassportProfileList(values: string[] | null | undefined, fallback: string): string {
  const normalized = Array.from(new Set(
    (values ?? [])
      .map((value) => value.trim())
      .filter(Boolean)
  ));

  if (normalized.length === 0) {
    return fallback;
  }

  const visible = normalized.slice(0, 4);
  return normalized.length > visible.length
    ? `${visible.join(", ")}, +${normalized.length - visible.length} more`
    : visible.join(", ");
}

function buildInstrumentPassportOperationsReadinessRows(
  passport: InstrumentPassport | null
): InstrumentPassportOperationsReadinessViewModel[] {
  return (passport?.operationsWorkbench?.readiness ?? []).map((row) => ({
    ...row,
    route: normalizeInstrumentPassportRoute(row.route),
    statusLabel: row.status,
    statusBadgeVariant: instrumentPassportStatusVariant(row.status),
    evidenceLabel: `${row.evidenceCount} evidence`,
    blockerLabel: `${row.blockingIssueCount} blocker${row.blockingIssueCount === 1 ? "" : "s"}`
  }));
}

function buildInstrumentPassportOperationsPanelRows(
  passport: InstrumentPassport | null
): InstrumentPassportOperationsWorkbenchPanelViewModel[] {
  return (passport?.operationsWorkbench?.panels ?? []).map((panel) => ({
    ...panel,
    statusLabel: panel.status,
    statusBadgeVariant: instrumentPassportStatusVariant(panel.status),
    items: panel.items.map((item) => ({
      ...item,
      route: normalizeInstrumentPassportRoute(item.route),
      statusLabel: item.status,
      statusBadgeVariant: instrumentPassportStatusVariant(item.status),
      evidenceLabel: `${item.evidenceCount} evidence`,
      blockerLabel: `${item.blockingIssueCount} blocker${item.blockingIssueCount === 1 ? "" : "s"}`
    }))
  }));
}

function normalizeInstrumentPassportRoute(route: string | null | undefined): string | null {
  const trimmed = route?.trim();
  if (!trimmed) {
    return null;
  }

  if (trimmed.startsWith("/workstation/")) {
    return trimmed.slice("/workstation".length);
  }

  return trimmed.startsWith("/") ? trimmed : null;
}

function instrumentPassportStatusVariant(status: string | null | undefined): "success" | "warning" | "outline" {
  const normalized = status?.trim().toLowerCase();
  if (normalized === "ready" || normalized === "trusted" || normalized === "complete") {
    return "success";
  }

  if (normalized === "review" || normalized === "blocked" || normalized === "unavailable") {
    return "warning";
  }

  return "outline";
}

function buildInstrumentPassportProviderRows(
  passport: InstrumentPassport | null
): InstrumentPassportProviderConfidenceRowViewModel[] {
  return (passport?.providerConfidence ?? []).map((row, index) => {
    const confidenceLabel = formatInstrumentPassportConfidenceLabel(row.confidenceScore);
    const freshnessLabel = row.freshnessMinutes !== null
      ? `${row.freshnessMinutes} min`
      : row.freshnessAsOf
        ? formatDateTimeLabel(row.freshnessAsOf)
        : "-";
    const statusLabel = row.isPrimary ? "Primary" : row.isActive ? "Active" : "Inactive";

    return {
      ...row,
      rowId: `${row.provider}-${row.normalizedSymbol || row.symbol}-${index}`,
      providerLabel: `${row.provider} / ${row.providerSource}`,
      symbolLabel: `${row.mappingKind}: ${row.symbol}`,
      confidenceLabel,
      freshnessLabel,
      statusLabel,
      statusTone: row.isActive ? "success" : "warning",
      ariaLabel: `${row.provider} ${row.symbol}, confidence ${confidenceLabel}, ${statusLabel}. ${row.confidenceReason}`
    };
  });
}
function formatInstrumentPassportConfidenceLabel(score: number): string {
  if (!Number.isFinite(score)) {
    return "-";
  }

  const normalizedScore = Math.abs(score) <= 1 ? score * 100 : score;
  const boundedScore = Math.max(0, Math.min(100, normalizedScore));
  return `${Math.round(boundedScore)}%`;
}
export function buildTradingParametersViewState(
  params: TradingParameters | null,
  loading: boolean,
  error: string | ApiErrorDisplay | null
): TradingParametersViewState {
  const normalizedError = normalizeApiErrorDisplay(error);
  const errorText = normalizedError?.summary ?? null;
  const fields: TradingParametersField[] = params
    ? [
        { label: "Lot size", value: params.lotSize !== null ? String(params.lotSize) : "—" },
        { label: "Tick size", value: params.tickSize !== null ? String(params.tickSize) : "—" },
        { label: "Contract multiplier", value: params.contractMultiplier !== null ? String(params.contractMultiplier) : "—" },
        {
          label: "Margin requirement",
          value: params.marginRequirementPct !== null ? `${params.marginRequirementPct}%` : "—",
          tone: params.marginRequirementPct !== null && params.marginRequirementPct > 50 ? "warning" : "default"
        },
        { label: "Trading hours (UTC)", value: params.tradingHoursUtc ?? "—" },
        {
          label: "Circuit breaker",
          value: params.circuitBreakerThresholdPct !== null ? `${params.circuitBreakerThresholdPct}%` : "—",
          tone: params.circuitBreakerThresholdPct !== null ? "warning" : "default"
        }
      ]
    : [];

  return {
    securityId: params?.securityId ?? "",
    asOfLabel: params?.asOf ? formatSecurityDate(params.asOf) : "—",
    fields,
    errorText,
    errorDetails: normalizedError?.details ?? [],
    loadingText: loading ? "Loading trading parameters..." : null,
    statusAnnouncement: errorText
      ? `Trading parameters error: ${errorText}`
      : loading
        ? "Loading trading parameters."
        : params
          ? `Trading parameters loaded for ${params.securityId}.`
          : ""
  };
}

function formatSecurityScheduleEventType(eventType: SecurityScheduleEventType): string {
  const labels: Record<string, string> = {
    Coupon: "Coupon",
    Principal: "Principal",
    Paydown: "Paydown",
    Maturity: "Maturity",
    Call: "Call",
    Distribution: "Distribution",
    FactorUpdate: "Factor update"
  };

  return labels[eventType] ?? eventType;
}

function formatSecuritySchedulePostingStatus(status: SecuritySchedulePostingStatus): string {
  const labels: Record<string, string> = {
    Posted: "Posted",
    Pending: "Pending",
    Variance: "Variance review",
    Forecast: "Forecast"
  };

  return labels[status] ?? status;
}

function securitySchedulePostingTone(
  status: SecuritySchedulePostingStatus
): SecurityScheduleRowViewModel["postingStatusTone"] {
  if (status === "Posted") {
    return "success";
  }

  if (status === "Variance") {
    return "danger";
  }

  if (status === "Pending") {
    return "warning";
  }

  return "outline";
}

function formatScheduleEventSource(sourceSystem: string, sourceRecordId: string | null | undefined): string {
  const source = sourceSystem?.trim() || "Unknown source";
  return sourceRecordId?.trim() ? `${source} · ${sourceRecordId}` : source;
}

function formatScheduleAmount(value: number | null, currency: string): string {
  if (value === null || !Number.isFinite(value)) {
    return "—";
  }

  const prefix = value >= 0 ? "" : "-";
  const amount = Math.abs(value).toLocaleString(undefined, {
    maximumFractionDigits: 2,
    minimumFractionDigits: 0
  });
  return `${prefix}${amount} ${currency}`;
}

function formatSignedScheduleAmount(value: number, currency: string): string {
  if (!Number.isFinite(value)) {
    return "—";
  }

  if (value === 0) {
    return `0 ${currency}`;
  }

  const sign = value > 0 ? "+" : "-";
  return `${sign}${Math.abs(value).toLocaleString(undefined, { maximumFractionDigits: 2 })} ${currency}`;
}

function formatQuantity(value: number | null): string {
  if (value === null || !Number.isFinite(value)) {
    return "—";
  }

  return value.toLocaleString(undefined, { maximumFractionDigits: 4 });
}

function formatOpenLotScope(lot: SecurityMasterOpenLot): string {
  return lot.accountScopeDisplayName?.trim()
    || lot.accountScopeId?.trim()
    || lot.vehicleScopeDisplayName?.trim()
    || lot.vehicleScopeId?.trim()
    || lot.portfolioId;
}

function openLotStatusTone(status: string): SecurityOpenLotRowViewModel["statusTone"] {
  const normalized = status.trim().toLowerCase();
  if (normalized === "open" || normalized === "active") {
    return "success";
  }

  if (normalized === "closed" || normalized === "settled") {
    return "outline";
  }

  if (normalized === "blocked" || normalized === "failed") {
    return "danger";
  }

  return "warning";
}

function formatScheduleVariance(expected: number | null, actual: number | null, currency: string): string {
  if (expected === null || actual === null || !Number.isFinite(expected) || !Number.isFinite(actual)) {
    return "—";
  }

  const variance = actual - expected;
  if (variance === 0) {
    return `0 ${currency}`;
  }

  const sign = variance > 0 ? "+" : "-";
  return `${sign}${Math.abs(variance).toLocaleString(undefined, { maximumFractionDigits: 2 })} ${currency}`;
}

function formatScheduleFactor(start: number | null, end: number | null): string {
  if (start === null && end === null) {
    return "—";
  }

  const startLabel = start === null ? "—" : start.toFixed(6);
  const endLabel = end === null ? "—" : end.toFixed(6);
  return `${startLabel} -> ${endLabel}`;
}

function scheduleVarianceTone(
  expected: number | null,
  actual: number | null,
  postingStatus: SecuritySchedulePostingStatus
): SecurityScheduleDetailFieldViewModel["tone"] {
  if (postingStatus === "Variance") {
    return "danger";
  }

  if (expected === null || actual === null) {
    return "warning";
  }

  return Math.abs(actual - expected) > 0.0001 ? "warning" : "success";
}

function formatCorpActEventType(eventType: string): string {
  const labels: Record<string, string> = {
    Dividend: "Dividend",
    StockSplit: "Stock split",
    SpinOff: "Spin-off",
    Merger: "Merger",
    RightsIssue: "Rights issue"
  };

  return labels[eventType] ?? eventType;
}

function formatCorpActAmount(action: CorporateAction): string {
  if (action.dividendPerShare !== null) {
    const currency = action.currency ? ` ${action.currency}` : "";
    return `${action.dividendPerShare}${currency} / share`;
  }

  if (action.splitRatio !== null) {
    return `${action.splitRatio}:1 split`;
  }

  if (action.exchangeRatio !== null) {
    return `${action.exchangeRatio}:1 exchange`;
  }

  if (action.distributionRatio !== null) {
    return `${action.distributionRatio}:1 distribution`;
  }

  if (action.rightsPerShare !== null) {
    const price = action.subscriptionPricePerShare !== null
      ? ` @ ${action.subscriptionPricePerShare}${action.currency ? ` ${action.currency}` : ""}`
      : "";
    return `${action.rightsPerShare} rights/share${price}`;
  }

  return "—";
}
