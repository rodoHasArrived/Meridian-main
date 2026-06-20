import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { describeApiError, type ApiErrorDisplay } from "@/lib/api-errors";
import {
  activateAccountingConfiguration,
  approveAccountingConfigurationPostingRulePromotion,
  applyManualJournalEntryLifecycleAction,
  attachManualJournalEntryEvidence,
  buildAccountingPostingRuleJournalCandidate,
  buildLedgerAccountingReportPackage,
  certifyLedgerAccountingReportPackage,
  createLedgerBook,
  createLedgerCloseManagementLateAdjustment,
  dryRunAccountingConfigurationPostingRule,
  executeAccountingConfigurationPostingRuleTests,
  getLedgerCloseManagementPeriodPlan,
  getLedgerAccountingReportPackageExport,
  reviewLedgerCloseManagementLateAdjustment,
  signOffLedgerCloseManagementTask,
  getAccountingConfiguration,
  getCapitalAccountWorkbench,
  getManualJournalEntryWorkbench,
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
  listLedgerAccountingReportPackages,
  previewAccountingConfigurationTemplate,
  saveManualJournalEntryDraft,
  resolveReconciliationBreak,
  resolveSecurityConflict,
  reviewReconciliationBreak,
  searchSecurities,
  submitManualJournalEntryApproval,
  upsertAccountingConfigurationPostingRule,
  upsertAccountingConfigurationPostingRuleTestCase,
  validateManualJournalEntryDraft,
  type AccountingReportPackageHistoryQuery,
  type CapitalAccountWorkbenchQuery,
  type ReferenceDataEndpointProbeStatus,
  type ReferenceDataEndpointProbeResult,
  type ReferenceDataWorkbenchCoverage
} from "@/lib/api";
import {
  evidenceWorkbenchPath,
  normalizeLocalWorkstationRoute,
  WORKSTATION_ROUTE_CATALOG,
  workflowTargetPath
} from "@/lib/workspace";
import { EXPORT_API_ENDPOINTS, WORKSTATION_API_ENDPOINTS, type ReferenceDataWorkbenchEndpointSeed } from "@/lib/workstation-endpoints";
import { formatReportPackRecipientList, getReportPackDistributions } from "@/lib/reporting-distributions";
import {
  buildCalibrationSummaryViewState,
  type CalibrationProfileDetailViewModel,
  type CalibrationProfileRowViewModel,
  type CalibrationStatusIcon,
  type CalibrationStatusTone,
  type CalibrationSummaryMetricViewModel,
  type CalibrationSummaryRefreshCommandViewModel,
  type CalibrationSummaryViewModel,
  type CalibrationSummaryViewState
} from "./accounting-calibration-summary.view-model";
import type {
  AccountingBasisKind,
  AccountingCertificationState,
  AccountingConfigurationWorkspace,
  AccountingReportPackageBundle,
  AccountingReportPackageRequest,
  CertifyAccountingReportPackageRequest,
  ReportExportArtifactManifest,
  AccountingJournalTemplatePreview,
  AccountingTemplateLineSide,
  AccountingRuleDryRunMatch,
  AccountingRuleTestCase,
  AccountingRuleTestSuiteResult,
  AllocationRule,
  CreateLedgerBookRequest,
  CorporateAction,
  ExportAnalysisResult,
  GeneratedPostingLine,
  AccountingCashFlowSummary,
  AccountingReportingProfile,
  AccountingReportingSummary,
  AccountingWorkspaceResponse,
  AccountingSystemImportDetail,
  AccountingSystemProvider,
  AccountingSystemReconciliationSummary,
  AccountingRulesStudioPromotionQueueItem,
  AccountingRulesStudioRuleRow,
  CloseCalendarMilestone,
  ClosePeriodPlan,
  CloseTask,
  CreateLateAdjustmentRequest,
  ReviewLateAdjustmentRequest,
  SignOffCloseTaskRequest,
  LedgerBook,
  LedgerTrialBalanceLine,
  LateAdjustmentRequest,
  MultiAssetCoverageSummary,
  OperationsContinuityWorkflow,
  OperationsWorkflowBlocker,
  ReconciliationBreakQueueItem,
  ReconciliationCalibrationSummary,
  ReconciliationCalibrationStatus,
  InvestmentAccountingTransactionLabPreview,
  InvestmentAccountingTransactionLabRequest,
  ResolveConflictRequest,
  PreviewJournalTemplateRequest,
  PostingRule,
  PostingRuleJournalCandidateRequest,
  PostingRuleJournalCandidateResult,
  RuleDryRunRequest,
  RuleDryRunResult,
  ExecuteAccountingRuleTestCasesRequest,
  ApprovePostingRulePromotionRequest,
  UpsertAccountingRuleTestCaseRequest,
  UpsertPostingRuleRequest,
  ActivateAccountingConfigurationRequest,
  AttachManualJournalEntryEvidenceRequest,
  JournalEntryLifecycleAction,
  JournalEntryLifecycleActionRequest,
  JournalEntryLifecycleActionResult,
  JournalEntryLifecycleTransition,
  ManualJournalEntryDraft,
  ManualJournalEntryEvidenceAttachment,
  ManualJournalEntryLine,
  ManualJournalEntryWorkbench,
  PrivateCapitalActivityProjection,
  PrivateCapitalCapitalAccountActivity,
  PrivateCapitalCapitalAccountSubledger,
  PrivateCapitalCapitalAccountSubledgerEntry,
  PrivateCapitalEvidenceCategory,
  PrivateCapitalFundEvent,
  PrivateCapitalFundEventLedgerRecord,
  PrivateCapitalLedgerImpact,
  PrivateCapitalPaymentIntentEvidence,
  PrivateCapitalReportOutput,
  PaymentIntentWorkflow,
  ResolveReconciliationBreakRequest,
  ReviewReconciliationBreakRequest,
  SaveManualJournalEntryDraftRequest,
  SecurityIdentityDrillIn,
  SecurityAliasEntry,
  SecurityIdentifierEntry,
  SecurityMasterConflict,
  SecurityMasterEntry,
  InstrumentPassport,
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

export type AccountingWorkstream = "ledger" | "configure" | "journal-entries" | "capital-accounts" | "reconciliation" | "exceptions" | "security-master" | "approvals" | "reporting";
export type GovernanceWorkstream = AccountingWorkstream;
export type ReconciliationBreakCommand = "assign" | "resolve" | "dismiss";
export type ReconciliationBreakResolutionStatus = ResolveReconciliationBreakRequest["status"];
export type SecurityConflictResolution = ResolveConflictRequest["resolution"];

export interface SecurityMasterServices {
  search: (query: string) => Promise<SecurityMasterEntry[]>;
  getIdentity: (securityId: string) => Promise<SecurityIdentityDrillIn>;
  getConflicts: () => Promise<SecurityMasterConflict[]>;
  resolveConflict: (request: ResolveConflictRequest) => Promise<SecurityMasterConflict>;
}

export interface AccountingReconciliationServices {
  getBreakQueue: () => Promise<ReconciliationBreakQueueItem[]>;
  reviewBreak: (request: ReviewReconciliationBreakRequest) => Promise<ReconciliationBreakQueueItem>;
  resolveBreak: (request: ResolveReconciliationBreakRequest) => Promise<ReconciliationBreakQueueItem>;
  getTrialBalance: (runId: string) => Promise<LedgerTrialBalanceLine[]>;
  getCalibrationSummary: () => Promise<ReconciliationCalibrationSummary>;
  getStatementRuns: () => Promise<StatementRunSummary[]>;
  getStatementRun: (runId: string) => Promise<StatementRunSummary>;
  previewTransactionLab: (request: InvestmentAccountingTransactionLabRequest) => Promise<InvestmentAccountingTransactionLabPreview>;
}

export interface AccountingReportingServices {
  runAnalysisExport: (profileId: string) => Promise<ExportAnalysisResult>;
}

export interface AccountingConfigurationServices {
  getConfiguration: () => Promise<AccountingConfigurationWorkspace>;
  createLedgerBook: (request: CreateLedgerBookRequest) => Promise<LedgerBook>;
  previewTemplate: (request: PreviewJournalTemplateRequest) => Promise<AccountingJournalTemplatePreview>;
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
}

export interface AccountingConfigurationAuditViewModel {
  id: string;
  title: string;
  subtitle: string;
  hashLabel: string;
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
  id: "results" | "selected" | "conflicts" | "detail";
  label: string;
  value: string;
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

export interface InstrumentPassportViewState {
  securityId: string;
  title: string;
  description: string;
  statusLabel: string;
  statusBadgeVariant: "success" | "warning" | "outline";
  fields: InstrumentPassportFieldViewModel[];
  providerRows: InstrumentPassportProviderConfidenceRowViewModel[];
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
  getWorkbench: () => Promise<ManualJournalEntryWorkbench>;
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
  loading: boolean;
  errorText: string | null;
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
  loading: boolean;
  errorText: string | null;
  statusLabel: string;
  draft: ManualJournalEntryDraft;
  drafts: ManualJournalEntryDraft[];
  accountOptions: { value: string; label: string }[];
  selectedLineId: string;
  securitySearchQuery: string;
  securitySearchResults: SecurityMasterEntry[];
  securitySearchBusy: boolean;
  securitySearchErrorText: string | null;
  securitySearchStatusText: string;
  attachmentDraft: ManualJournalEvidenceAttachmentDraft;
  totalsLabel: string;
  totalDebitsLabel: string;
  totalCreditsLabel: string;
  imbalanceLabel: string;
  balanceStatusLabel: string;
  balanceStatusTone: "success" | "warning";
  treasuryContextLabel: string;
  privateCapitalActivity: ManualJournalPrivateCapitalActivityViewModel;
  validationIssues: AccountingConfigurationIssueViewModel[];
  lifecycleCommands: ManualJournalLifecycleCommandViewModel[];
  lifecycleChecklist: ManualJournalLifecycleChecklistItemViewModel[];
  lifecycleTransitions: ManualJournalLifecycleTransitionViewModel[];
  lifecycleCorrectionRows: ManualJournalLifecycleCorrectionViewModel[];
  lifecycleStatusText: string | null;
  lifecycleBusyAction: JournalEntryLifecycleAction | null;
  saveBusy: boolean;
  validateBusy: boolean;
  submitBusy: boolean;
  attachEvidenceBusy: boolean;
  attachEvidenceStatusText: string | null;
  canSubmit: boolean;
  submitDisabledReason: string | null;
  refresh: () => Promise<void>;
  updateHeader: (field: keyof Pick<ManualJournalEntryDraft, "memo" | "currency" | "fundProfileId" | "entityId" | "fundNodeId" | "periodId" | "accountingDate">, value: string) => void;
  selectDraft: (journalEntryId: string) => void;
  selectLine: (lineId: string) => void;
  updateLine: (lineId: string, patch: Partial<ManualJournalEntryLine>) => void;
  getLineBadges: (lineId: string) => ManualJournalLineValidationBadge[];
  updateSecuritySearchQuery: (query: string) => void;
  searchSecurityMaster: () => Promise<void>;
  selectSecurity: (lineId: string, security: SecurityMasterEntry) => void;
  clearSecurity: (lineId: string) => void;
  addLine: (side: AccountingTemplateLineSide) => void;
  removeLine: (lineId: string) => void;
  updateAttachmentDraft: (patch: Partial<ManualJournalEvidenceAttachmentDraft>) => void;
  addAttachment: () => Promise<void>;
  removeAttachment: (attachmentId: string) => void;
  applyLifecycleAction: (action: JournalEntryLifecycleAction) => Promise<void>;
  save: () => Promise<void>;
  validate: () => Promise<void>;
  submit: () => Promise<void>;
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
  statusTone: "success" | "warning";
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
  ariaLabel: string;
}

type StatementRunSummaryWithMetadata = StatementRunSummary & {
  brokerCustodian?: string | null;
  account?: string | null;
  period?: string | null;
  status?: string | null;
  validationIssueCount?: number | null;
  matchCount?: number | null;
  breakCount?: number | null;
  caseCount?: number | null;
  importedAtUtc?: string | null;
};

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
}

export interface CloseCommandCenterActionViewModel {
  id: string;
  label: string;
  href: string;
  ariaLabel: string;
  tone: AccountingToolingTone;
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
  createLateAdjustmentBusy: boolean;
  createLateAdjustmentStatusText: string | null;
  createLateAdjustmentStatusTone: "neutral" | "success" | "danger";
  reviewLateAdjustmentBusy: boolean;
  reviewLateAdjustmentStatusText: string | null;
  reviewLateAdjustmentStatusTone: "neutral" | "success" | "danger";
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
  createLateAdjustmentDisabledReason: string | null;
  lateAdjustmentDraft: AccountingLateAdjustmentDraftViewModel;
  exportManifestButtonLabel: string;
  exportManifestDisabledReason: string | null;
  metrics: AccountingReportPackageHistoryMetricViewModel[];
  closeCalendar: AccountingCloseCalendarMilestoneViewModel[];
  tasks: AccountingClosePlanTaskRowViewModel[];
  lateAdjustments: AccountingLateAdjustmentRowViewModel[];
  packageRows: AccountingReportPackageRowViewModel[];
  selectedPackage: AccountingReportPackageRowViewModel | null;
  certificationSafeguards: AccountingReportCertificationSafeguardViewModel[];
  validationIssues: AccountingConfigurationIssueViewModel[];
  liveRegionText: string;
  refresh: () => Promise<void>;
  buildReportPackage: () => Promise<void>;
  certifyPackage: () => Promise<void>;
  signOffNextTask: () => Promise<void>;
  updateLateAdjustmentDraft: (patch: Partial<AccountingLateAdjustmentDraftViewModel>) => void;
  createLateAdjustment: () => Promise<void>;
  reviewLateAdjustment: (requestId: string, decision: "Approved" | "Rejected") => Promise<void>;
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
  buildPackage: (request: AccountingReportPackageRequest) => Promise<AccountingReportPackageBundle>;
  certifyPackage: (request: CertifyAccountingReportPackageRequest) => Promise<AccountingReportPackageBundle>;
  getExportManifest: (packageId: string, artifactId: string) => Promise<ReportExportArtifactManifest>;
  listPackages: (query: AccountingReportPackageHistoryQuery) => Promise<AccountingReportPackageBundle[]>;
}

interface CloseCommandCenterRawBlocker {
  code: string;
  category: string;
  severity: string;
  message: string;
  gate: OperationsWorkflowBlocker["gate"];
  routeHint: string | null;
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

const DEFAULT_ACCOUNTING_BASIS: AccountingBasisKind = "Primary";

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

const defaultAccountingCloseReportPackageServices: AccountingCloseReportPackageServices = {
  getClosePlan: (workflowId) => getLedgerCloseManagementPeriodPlan(workflowId),
  createLateAdjustment: (request) => createLedgerCloseManagementLateAdjustment(request),
  reviewLateAdjustment: (request) => reviewLedgerCloseManagementLateAdjustment(request),
  signOffCloseTask: (request) => signOffLedgerCloseManagementTask(request),
  buildPackage: (request) => buildLedgerAccountingReportPackage(request),
  certifyPackage: (request) => certifyLedgerAccountingReportPackage(request),
  getExportManifest: (packageId, artifactId) => getLedgerAccountingReportPackageExport(packageId, artifactId),
  listPackages: (query) => listLedgerAccountingReportPackages(query)
};

const defaultAccountingConfigurationServices: AccountingConfigurationServices = {
  getConfiguration: () => getAccountingConfiguration(),
  createLedgerBook: (request) => createLedgerBook(request),
  previewTemplate: (request) => previewAccountingConfigurationTemplate(request),
  dryRunRule: (request) => dryRunAccountingConfigurationPostingRule(request),
  buildJournalCandidate: (request) => buildAccountingPostingRuleJournalCandidate(request),
  runRuleTests: (request) => executeAccountingConfigurationPostingRuleTests(request),
  saveRuleTestCase: (request) => upsertAccountingConfigurationPostingRuleTestCase(request),
  upsertRule: (request) => upsertAccountingConfigurationPostingRule(request),
  approveRulePromotion: (request) => approveAccountingConfigurationPostingRulePromotion(request),
  activate: (request) => activateAccountingConfiguration(request)
};

const defaultManualJournalEntryWorkbenchServices: ManualJournalEntryWorkbenchServices = {
  getWorkbench: () => getManualJournalEntryWorkbench(),
  searchSecurities: (query) => searchSecurities(query, 8, true),
  saveDraft: (request) => saveManualJournalEntryDraft(request),
  validateDraft: (request) => validateManualJournalEntryDraft(request),
  submitApproval: (request) => submitManualJournalEntryApproval(request),
  attachEvidence: (request) => attachManualJournalEntryEvidence(request),
  applyLifecycleAction: (request) => applyManualJournalEntryLifecycleAction(request)
};

const defaultCapitalAccountWorkbenchServices: CapitalAccountWorkbenchServices = {
  getWorkbench: (query) => getCapitalAccountWorkbench(query)
};

const defaultSecurityMasterDrillInServices: SecurityMasterDrillInServices = {
  getCorporateActions: (securityId) => getCorporateActions(securityId),
  getReferenceDataCoverage: (seed) => getReferenceDataWorkbenchCoverage(seed),
  getInstrumentPassport: (securityId) => getSecurityInstrumentPassport(securityId),
  getTradingParameters: (securityId) => getTradingParameters(securityId),
  getTrustSnapshot: (securityId) => getSecurityTrustSnapshot(securityId)
};

const securityScheduleFixtures: Record<string, SecurityCashFlowScheduleEvent[]> = {
  "sec-dev-004": [
    {
      eventId: "sched-sec-dev-004-cpn-2026-06",
      securityId: "sec-dev-004",
      scheduleFamily: "bond",
      eventType: "Coupon",
      paymentDate: "2026-06-15",
      accrualStartDate: "2025-12-15",
      accrualEndDate: "2026-06-15",
      couponRatePct: 5.875,
      expectedAmount: 29375,
      actualAmount: null,
      principalAmount: null,
      interestAmount: 29375,
      factorStart: 1,
      factorEnd: 1,
      currency: "USD",
      postingStatus: "Forecast",
      auditReference: "fixture/security-master/cash-flow/sec-dev-004/cpn-2026-06",
      note: "Semi-annual fixed coupon projected from the reference coupon schedule."
    },
    {
      eventId: "sched-sec-dev-004-paydown-2026-09",
      securityId: "sec-dev-004",
      scheduleFamily: "structured",
      eventType: "Paydown",
      paymentDate: "2026-09-15",
      accrualStartDate: "2026-06-15",
      accrualEndDate: "2026-09-15",
      couponRatePct: 5.875,
      expectedAmount: 148750,
      actualAmount: 147920,
      principalAmount: 125000,
      interestAmount: 23750,
      factorStart: 1,
      factorEnd: 0.875,
      currency: "USD",
      postingStatus: "Variance",
      auditReference: "fixture/security-master/cash-flow/sec-dev-004/paydown-2026-09",
      note: "Principal paydown carries a small expected-versus-actual variance for operator review."
    },
    {
      eventId: "sched-sec-dev-004-maturity-2031-12",
      securityId: "sec-dev-004",
      scheduleFamily: "bond",
      eventType: "Maturity",
      paymentDate: "2031-12-15",
      accrualStartDate: "2031-06-15",
      accrualEndDate: "2031-12-15",
      couponRatePct: 5.875,
      expectedAmount: 529375,
      actualAmount: null,
      principalAmount: 500000,
      interestAmount: 29375,
      factorStart: 0.875,
      factorEnd: 0,
      currency: "USD",
      postingStatus: "Pending",
      auditReference: "fixture/security-master/cash-flow/sec-dev-004/maturity-2031-12",
      note: "Final coupon and principal repayment remain pending until trustee schedule confirmation."
    }
  ],
  "sec-1": [
    {
      eventId: "sched-sec-1-cpn-2026-05",
      securityId: "sec-1",
      scheduleFamily: "bond",
      eventType: "Coupon",
      paymentDate: "2026-05-15",
      accrualStartDate: "2025-11-15",
      accrualEndDate: "2026-05-15",
      couponRatePct: 5.25,
      expectedAmount: 26250,
      actualAmount: 26250,
      principalAmount: null,
      interestAmount: 26250,
      factorStart: 1,
      factorEnd: 1,
      currency: "USD",
      postingStatus: "Posted",
      auditReference: "fixture/security-master/cash-flow/sec-1/cpn-2026-05",
      note: "Fixture coupon row used by browser workbench tests."
    },
    {
      eventId: "sched-sec-1-principal-2026-11",
      securityId: "sec-1",
      scheduleFamily: "bond",
      eventType: "Principal",
      paymentDate: "2026-11-15",
      accrualStartDate: "2026-05-15",
      accrualEndDate: "2026-11-15",
      couponRatePct: 5.25,
      expectedAmount: 126250,
      actualAmount: null,
      principalAmount: 100000,
      interestAmount: 26250,
      factorStart: 1,
      factorEnd: 0.9,
      currency: "USD",
      postingStatus: "Pending",
      auditReference: "fixture/security-master/cash-flow/sec-1/principal-2026-11",
      note: "Fixture amortization row keeps schedule selection deterministic."
    }
  ]
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
  const workstream = workspaceLabel === "Accounting" ? resolveAccountingWorkstream(pathname) : "reporting";
  const workstreamLabel = workstream === "security-master"
    ? "Security Master"
    : workstream.charAt(0).toUpperCase() + workstream.slice(1).replace("-", " ");
  const accountingStatusItems: AccountingLoadingStatusItemViewModel[] = [
    {
      id: "ledger-reconciliation",
      label: "Ledger and reconciliation",
      detail: "Loading close metrics, reconciliation runs, open breaks, cash-flow evidence, and trial-balance rows."
    },
    {
      id: "approvals-exceptions",
      label: "Approvals and exceptions",
      detail: "Preparing dedicated approval and exception workstreams from shared operations-continuity payloads."
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
      detail: "Review close workflow gates while the workspace payload finishes loading.",
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
    eyebrow: `${workspaceLabel} bootstrap`,
    title: `Loading ${workspaceLabel}`,
    detail: workspaceLabel === "Reporting"
      ? "Waiting for report-pack, governed export, and approval summaries from the workstation bootstrap payload."
      : "Waiting for ledger, reconciliation, cash-flow, and Security Master summaries from the workstation bootstrap payload.",
    routeLabel: pathname,
    workstreamLabel,
    statusItemsLabel: `${workspaceLabel} payloads loading`,
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

export function useAccountingCloseReportPackageViewModel(
  workflow: OperationsContinuityWorkflow | null,
  services: AccountingCloseReportPackageServices = defaultAccountingCloseReportPackageServices
): AccountingCloseReportPackageViewModel {
  const [closePlan, setClosePlan] = useState<ClosePeriodPlan | null>(null);
  const [packages, setPackages] = useState<AccountingReportPackageBundle[]>([]);
  const [selectedPackageId, setSelectedPackageId] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [errorText, setErrorText] = useState<string | null>(null);
  const [buildBusy, setBuildBusy] = useState(false);
  const [buildStatusText, setBuildStatusText] = useState<string | null>(null);
  const [buildStatusTone, setBuildStatusTone] = useState<"neutral" | "success" | "danger">("neutral");
  const [certifyBusy, setCertifyBusy] = useState(false);
  const [certifyStatusText, setCertifyStatusText] = useState<string | null>(null);
  const [certifyStatusTone, setCertifyStatusTone] = useState<"neutral" | "success" | "danger">("neutral");
  const [signOffBusy, setSignOffBusy] = useState(false);
  const [signOffStatusText, setSignOffStatusText] = useState<string | null>(null);
  const [signOffStatusTone, setSignOffStatusTone] = useState<"neutral" | "success" | "danger">("neutral");
  const [createLateAdjustmentBusy, setCreateLateAdjustmentBusy] = useState(false);
  const [createLateAdjustmentStatusText, setCreateLateAdjustmentStatusText] = useState<string | null>(null);
  const [createLateAdjustmentStatusTone, setCreateLateAdjustmentStatusTone] = useState<"neutral" | "success" | "danger">("neutral");
  const [lateAdjustmentDraft, setLateAdjustmentDraft] = useState<AccountingLateAdjustmentDraftViewModel>(() => createAccountingLateAdjustmentDraft());
  const [reviewLateAdjustmentBusy, setReviewLateAdjustmentBusy] = useState(false);
  const [reviewLateAdjustmentStatusText, setReviewLateAdjustmentStatusText] = useState<string | null>(null);
  const [reviewLateAdjustmentStatusTone, setReviewLateAdjustmentStatusTone] = useState<"neutral" | "success" | "danger">("neutral");
  const [exportManifestBusy, setExportManifestBusy] = useState(false);
  const [exportManifestStatusText, setExportManifestStatusText] = useState<string | null>(null);
  const [exportManifestStatusTone, setExportManifestStatusTone] = useState<"neutral" | "success" | "danger">("neutral");
  const [exportManifest, setExportManifest] = useState<ReportExportArtifactManifest | null>(null);

  const refresh = useCallback(async () => {
    if (!workflow) {
      setClosePlan(null);
      setPackages([]);
      setSelectedPackageId(null);
      return;
    }

    setLoading(true);
    setErrorText(null);
    try {
      const [nextClosePlan, nextPackages] = await Promise.all([
        services.getClosePlan(workflow.workflowId),
        services.listPackages({
          fundProfileId: workflow.fundAccountId,
          periodId: workflow.periodId
        })
      ]);
      setClosePlan(nextClosePlan);
      setPackages(nextPackages);
      setSelectedPackageId((current) => {
        if (current && nextPackages.some((item) => item.financialStatements.packageId === current)) {
          return current;
        }

        return nextPackages[0]?.financialStatements.packageId ?? null;
      });
    } catch (error) {
      setErrorText(formatAccountingWorkflowError(error, "Close/report package detail could not be loaded."));
    } finally {
      setLoading(false);
    }
  }, [services, workflow]);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  const buildReportPackage = useCallback(async () => {
    if (!workflow) {
      setBuildStatusText("A close workflow is required before building a report package.");
      setBuildStatusTone("danger");
      return;
    }

    setBuildBusy(true);
    setBuildStatusText(null);
    setBuildStatusTone("neutral");
    try {
      const request = buildAccountingReportPackageRequest(workflow, closePlan, packages[0] ?? null);
      const bundle = await services.buildPackage(request);
      setPackages((current) => [
        bundle,
        ...current.filter((item) => item.financialStatements.packageId !== bundle.financialStatements.packageId)
      ]);
      setSelectedPackageId(bundle.financialStatements.packageId);
      setExportManifest(null);
      setBuildStatusText(`Built report package ${bundle.financialStatements.packageId}.`);
      setBuildStatusTone("success");
    } catch (error) {
      setBuildStatusText(formatAccountingWorkflowError(error, "Report package could not be built."));
      setBuildStatusTone("danger");
    } finally {
      setBuildBusy(false);
    }
  }, [closePlan, packages, services, workflow]);

  const certifyPackage = useCallback(async () => {
    const selectedBundle = packages.find((bundle) => bundle.financialStatements.packageId === selectedPackageId) ?? packages[0] ?? null;
    if (!selectedBundle) {
      setCertifyStatusText("A ready report package is required before certification.");
      setCertifyStatusTone("danger");
      return;
    }

    setCertifyBusy(true);
    setCertifyStatusText(null);
    setCertifyStatusTone("neutral");
    try {
      const request = buildCertifyAccountingReportPackageRequest(selectedBundle);
      const certified = await services.certifyPackage(request);
      setPackages((current) => [
        certified,
        ...current.filter((item) => item.financialStatements.packageId !== certified.financialStatements.packageId)
      ]);
      setSelectedPackageId(certified.financialStatements.packageId);
      setExportManifest(null);
      setCertifyStatusText(`Certified report package ${certified.financialStatements.packageId}.`);
      setCertifyStatusTone("success");
    } catch (error) {
      setCertifyStatusText(formatAccountingWorkflowError(error, "Report package could not be certified."));
      setCertifyStatusTone("danger");
    } finally {
      setCertifyBusy(false);
    }
  }, [packages, selectedPackageId, services]);

  const signOffNextTask = useCallback(async () => {
    if (!workflow || !closePlan) {
      setSignOffStatusText("A close plan is required before signing off a checklist task.");
      setSignOffStatusTone("danger");
      return;
    }

    const signOffTarget = resolveCloseTaskSignOffTarget(closePlan);
    if (!signOffTarget) {
      setSignOffStatusText("No close checklist task is ready for sign-off.");
      setSignOffStatusTone("danger");
      return;
    }

    setSignOffBusy(true);
    setSignOffStatusText(null);
    setSignOffStatusTone("neutral");
    try {
      const nextPlan = await services.signOffCloseTask({
        workflowId: workflow.workflowId,
        taskId: signOffTarget.task.taskId,
        role: signOffTarget.role,
        decision: "Approved",
        actor: "browser-accounting-controller",
        notes: `Approved ${signOffTarget.task.displayName} close checklist task from the Accounting close cockpit.`,
        evidenceLinks: [
          `browser://accounting/close/sign-off/${workflow.workflowId}/${signOffTarget.task.taskId}`,
          ...signOffTarget.task.evidenceLinks
        ],
        correlationId: `browser-close-signoff-${workflow.workflowId}-${signOffTarget.task.taskId}`
      });
      setClosePlan(nextPlan);
      setSignOffStatusText(`Signed off ${signOffTarget.task.displayName}.`);
      setSignOffStatusTone("success");
    } catch (error) {
      setSignOffStatusText(formatAccountingWorkflowError(error, "Close checklist task could not be signed off."));
      setSignOffStatusTone("danger");
    } finally {
      setSignOffBusy(false);
    }
  }, [closePlan, services, workflow]);

  const updateLateAdjustmentDraft = useCallback((patch: Partial<AccountingLateAdjustmentDraftViewModel>) => {
    setLateAdjustmentDraft((current) => ({ ...current, ...patch }));
  }, []);

  const createLateAdjustment = useCallback(async () => {
    if (!workflow || !closePlan) {
      setCreateLateAdjustmentStatusText("A close plan is required before requesting a late adjustment.");
      setCreateLateAdjustmentStatusTone("danger");
      return;
    }

    if (closePlan.isPeriodLocked) {
      setCreateLateAdjustmentStatusText("The period is locked; late adjustments must use governed reopen or remediation.");
      setCreateLateAdjustmentStatusTone("danger");
      return;
    }

    const journalEntryId = lateAdjustmentDraft.journalEntryId.trim();
    const reason = lateAdjustmentDraft.reason.trim();
    const amount = Number(lateAdjustmentDraft.amount);
    const currency = (lateAdjustmentDraft.currency.trim() || closePlan.materialityPolicy.currency || "USD").toUpperCase();
    if (!journalEntryId || !reason || !Number.isFinite(amount) || amount === 0) {
      setCreateLateAdjustmentStatusText("Journal entry, non-zero amount, currency, and reason are required before requesting a late adjustment.");
      setCreateLateAdjustmentStatusTone("danger");
      return;
    }

    setCreateLateAdjustmentBusy(true);
    setCreateLateAdjustmentStatusText(null);
    setCreateLateAdjustmentStatusTone("neutral");
    try {
      const nextPlan = await services.createLateAdjustment({
        workflowId: workflow.workflowId,
        journalEntryId,
        amount,
        currency,
        reason,
        requestedBy: "browser-accounting-controller",
        evidenceLinks: [
          `browser://accounting/close/late-adjustment/${workflow.workflowId}/${journalEntryId}`,
          `browser://accounting/close/materiality-review/${workflow.workflowId}`
        ],
        correlationId: `browser-late-adjustment-${workflow.workflowId}-${journalEntryId}`
      });
      setClosePlan(nextPlan);
      setLateAdjustmentDraft(createAccountingLateAdjustmentDraft(currency));
      setCreateLateAdjustmentStatusText(`Late adjustment requested for ${journalEntryId}.`);
      setCreateLateAdjustmentStatusTone("success");
    } catch (error) {
      setCreateLateAdjustmentStatusText(formatAccountingWorkflowError(error, "Late adjustment request could not be retained."));
      setCreateLateAdjustmentStatusTone("danger");
    } finally {
      setCreateLateAdjustmentBusy(false);
    }
  }, [closePlan, lateAdjustmentDraft, services, workflow]);

  const reviewLateAdjustment = useCallback(async (requestId: string, decision: "Approved" | "Rejected") => {
    if (!workflow || !closePlan) {
      setReviewLateAdjustmentStatusText("A close plan is required before reviewing a late adjustment.");
      setReviewLateAdjustmentStatusTone("danger");
      return;
    }

    const adjustment = closePlan.lateAdjustments.find((item) => item.requestId === requestId) ?? null;
    if (!adjustment) {
      setReviewLateAdjustmentStatusText(`Late adjustment ${requestId} is no longer loaded.`);
      setReviewLateAdjustmentStatusTone("danger");
      return;
    }

    if (adjustment.approvalState === "Approved" || adjustment.approvalState === "Rejected") {
      setReviewLateAdjustmentStatusText(`Late adjustment ${requestId} is already ${adjustment.approvalState.toLowerCase()}.`);
      setReviewLateAdjustmentStatusTone("danger");
      return;
    }

    setReviewLateAdjustmentBusy(true);
    setReviewLateAdjustmentStatusText(null);
    setReviewLateAdjustmentStatusTone("neutral");
    try {
      const action = decision === "Approved" ? "approved" : "rejected";
      const nextPlan = await services.reviewLateAdjustment({
        workflowId: workflow.workflowId,
        requestId,
        decision,
        actor: "browser-accounting-controller",
        notes: `${decision} late adjustment ${requestId} from the Accounting close cockpit.`,
        evidenceLinks: [
          `browser://accounting/close/late-adjustments/review/${workflow.workflowId}/${requestId}/${decision.toLowerCase()}`,
          ...adjustment.evidenceLinks
        ],
        correlationId: `browser-late-adjustment-review-${workflow.workflowId}-${requestId}-${decision.toLowerCase()}`
      });
      setClosePlan(nextPlan);
      setReviewLateAdjustmentStatusText(`Late adjustment ${requestId} ${action}.`);
      setReviewLateAdjustmentStatusTone("success");
    } catch (error) {
      setReviewLateAdjustmentStatusText(formatAccountingWorkflowError(error, "Late adjustment review could not be retained."));
      setReviewLateAdjustmentStatusTone("danger");
    } finally {
      setReviewLateAdjustmentBusy(false);
    }
  }, [closePlan, services, workflow]);

  const inspectSelectedPackageExport = useCallback(async () => {
    const selectedBundle = packages.find((bundle) => bundle.financialStatements.packageId === selectedPackageId) ?? packages[0] ?? null;
    const artifact = selectedBundle?.exportArtifacts?.[0] ?? null;
    if (!selectedBundle || !artifact) {
      setExportManifestStatusText("A report package with retained export artifacts is required before manifest inspection.");
      setExportManifestStatusTone("danger");
      return;
    }

    setExportManifestBusy(true);
    setExportManifestStatusText(null);
    setExportManifestStatusTone("neutral");
    try {
      const manifest = await services.getExportManifest(selectedBundle.financialStatements.packageId, artifact.artifactId);
      setExportManifest(manifest);
      setExportManifestStatusText(`Loaded export manifest ${manifest.artifactId}.`);
      setExportManifestStatusTone(manifest.externalPostingAllowed ? "danger" : "success");
    } catch (error) {
      setExportManifestStatusText(formatAccountingWorkflowError(error, "Report export manifest could not be loaded."));
      setExportManifestStatusTone("danger");
    } finally {
      setExportManifestBusy(false);
    }
  }, [packages, selectedPackageId, services]);

  return useMemo(
    () => buildAccountingCloseReportPackageViewState({
      workflow,
      closePlan,
      packages,
      selectedPackageId,
      loading,
      errorText,
      buildBusy,
      buildStatusText,
      buildStatusTone,
      certifyBusy,
      certifyStatusText,
      certifyStatusTone,
      signOffBusy,
      signOffStatusText,
      signOffStatusTone,
      createLateAdjustmentBusy,
      createLateAdjustmentStatusText,
      createLateAdjustmentStatusTone,
      lateAdjustmentDraft,
      reviewLateAdjustmentBusy,
      reviewLateAdjustmentStatusText,
      reviewLateAdjustmentStatusTone,
      exportManifestBusy,
      exportManifestStatusText,
      exportManifestStatusTone,
      exportManifest,
      refresh,
      buildReportPackage,
      certifyPackage,
      signOffNextTask,
      updateLateAdjustmentDraft,
      createLateAdjustment,
      reviewLateAdjustment,
      inspectSelectedPackageExport,
      selectPackage: setSelectedPackageId
    }),
    [
      buildBusy,
      buildReportPackage,
      buildStatusText,
      buildStatusTone,
      certifyBusy,
      certifyPackage,
      certifyStatusText,
      certifyStatusTone,
      closePlan,
      createLateAdjustment,
      createLateAdjustmentBusy,
      createLateAdjustmentStatusText,
      createLateAdjustmentStatusTone,
      errorText,
      exportManifest,
      exportManifestBusy,
      exportManifestStatusText,
      exportManifestStatusTone,
      inspectSelectedPackageExport,
      lateAdjustmentDraft,
      loading,
      packages,
      refresh,
      reviewLateAdjustment,
      reviewLateAdjustmentBusy,
      reviewLateAdjustmentStatusText,
      reviewLateAdjustmentStatusTone,
      selectedPackageId,
      signOffBusy,
      signOffNextTask,
      signOffStatusText,
      signOffStatusTone,
      updateLateAdjustmentDraft,
      workflow
    ]
  );
}

export function useAccountingConfigurationViewModel(
  services: AccountingConfigurationServices = defaultAccountingConfigurationServices
): AccountingConfigurationViewModel {
  const [workspace, setWorkspace] = useState<AccountingConfigurationWorkspace | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<ApiErrorDisplay | null>(null);
  const [preview, setPreview] = useState<AccountingJournalTemplatePreview | null>(null);
  const [previewBusy, setPreviewBusy] = useState(false);
  const [previewError, setPreviewError] = useState<ApiErrorDisplay | null>(null);
  const [selectedRuleId, setSelectedRuleId] = useState<string | null>(null);
  const [dryRunPreview, setDryRunPreview] = useState<RuleDryRunResult | null>(null);
  const [dryRunBusy, setDryRunBusy] = useState(false);
  const [dryRunError, setDryRunError] = useState<ApiErrorDisplay | null>(null);
  const [createLedgerBookBusy, setCreateLedgerBookBusy] = useState(false);
  const [createLedgerBookError, setCreateLedgerBookError] = useState<ApiErrorDisplay | null>(null);
  const [createdLedgerBookName, setCreatedLedgerBookName] = useState<string | null>(null);
  const [journalCandidatePreview, setJournalCandidatePreview] = useState<PostingRuleJournalCandidateResult | null>(null);
  const [journalCandidateBusy, setJournalCandidateBusy] = useState(false);
  const [journalCandidateError, setJournalCandidateError] = useState<ApiErrorDisplay | null>(null);
  const [applyEventPredicateBusy, setApplyEventPredicateBusy] = useState(false);
  const [applyEventPredicateError, setApplyEventPredicateError] = useState<ApiErrorDisplay | null>(null);
  const [eventPredicateRuleId, setEventPredicateRuleId] = useState<string | null>(null);
  const [applyThresholdBusy, setApplyThresholdBusy] = useState(false);
  const [applyThresholdError, setApplyThresholdError] = useState<ApiErrorDisplay | null>(null);
  const [thresholdRuleId, setThresholdRuleId] = useState<string | null>(null);
  const [applyEffectiveStartBusy, setApplyEffectiveStartBusy] = useState(false);
  const [applyEffectiveStartError, setApplyEffectiveStartError] = useState<ApiErrorDisplay | null>(null);
  const [effectiveStartRuleId, setEffectiveStartRuleId] = useState<string | null>(null);
  const [applyScopeBusy, setApplyScopeBusy] = useState(false);
  const [applyScopeError, setApplyScopeError] = useState<ApiErrorDisplay | null>(null);
  const [scopedRuleId, setScopedRuleId] = useState<string | null>(null);
  const [capturePostingsBusy, setCapturePostingsBusy] = useState(false);
  const [capturePostingsError, setCapturePostingsError] = useState<ApiErrorDisplay | null>(null);
  const [capturedPostingsRuleId, setCapturedPostingsRuleId] = useState<string | null>(null);
  const [applyFormulaBusy, setApplyFormulaBusy] = useState(false);
  const [applyFormulaError, setApplyFormulaError] = useState<ApiErrorDisplay | null>(null);
  const [formulaRuleId, setFormulaRuleId] = useState<string | null>(null);
  const [applyAllocationBusy, setApplyAllocationBusy] = useState(false);
  const [applyAllocationError, setApplyAllocationError] = useState<ApiErrorDisplay | null>(null);
  const [allocationRuleId, setAllocationRuleId] = useState<string | null>(null);
  const [raisePriorityBusy, setRaisePriorityBusy] = useState(false);
  const [raisePriorityError, setRaisePriorityError] = useState<ApiErrorDisplay | null>(null);
  const [raisedPriorityRuleId, setRaisedPriorityRuleId] = useState<string | null>(null);
  const [ruleTestSuite, setRuleTestSuite] = useState<AccountingRuleTestSuiteResult | null>(null);
  const [ruleTestBusy, setRuleTestBusy] = useState(false);
  const [ruleTestError, setRuleTestError] = useState<ApiErrorDisplay | null>(null);
  const [saveRuleTestBusy, setSaveRuleTestBusy] = useState(false);
  const [saveRuleTestError, setSaveRuleTestError] = useState<ApiErrorDisplay | null>(null);
  const [duplicateRuleBusy, setDuplicateRuleBusy] = useState(false);
  const [duplicateRuleError, setDuplicateRuleError] = useState<ApiErrorDisplay | null>(null);
  const [duplicatedRuleId, setDuplicatedRuleId] = useState<string | null>(null);
  const [archiveRuleBusy, setArchiveRuleBusy] = useState(false);
  const [archiveRuleError, setArchiveRuleError] = useState<ApiErrorDisplay | null>(null);
  const [archivedRuleId, setArchivedRuleId] = useState<string | null>(null);
  const [approveRulePromotionBusy, setApproveRulePromotionBusy] = useState(false);
  const [approveRulePromotionError, setApproveRulePromotionError] = useState<ApiErrorDisplay | null>(null);
  const [approvedRulePromotionId, setApprovedRulePromotionId] = useState<string | null>(null);
  const [activateBusy, setActivateBusy] = useState(false);
  const [activateError, setActivateError] = useState<ApiErrorDisplay | null>(null);

  const refresh = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const next = await services.getConfiguration();
      if (!next) {
        throw new Error("Accounting configuration response was empty.");
      }
      setWorkspace(next);
      setSelectedRuleId((current) => {
        if (current && next.postingRules.some((rule) => rule.ruleId === current && !rule.isArchived)) {
          return current;
        }

        return next.postingRules.find((rule) => !rule.isArchived)?.ruleId ?? null;
      });
    } catch (err) {
      setError(describeApiError(err, "Accounting configuration is unavailable."));
    } finally {
      setLoading(false);
    }
  }, [services]);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  const previewFirstTemplate = useCallback(async () => {
    const template = workspace?.journalTemplates.find((item) => !item.isArchived) ?? null;
    if (!workspace || !template || previewBusy) {
      return;
    }

    setPreviewBusy(true);
    setPreviewError(null);
    try {
      const result = await services.previewTemplate({
        fundProfileId: workspace.fundProfileId,
        ledgerBookId: workspace.ledgerBookId ?? null,
        templateId: template.templateId,
        actor: "browser-accounting-operator",
        correlationId: `browser-accounting-config-preview-${Date.now()}`
      });
      setPreview(result);
    } catch (err) {
      setPreviewError(describeApiError(err, "Template preview failed."));
    } finally {
      setPreviewBusy(false);
    }
  }, [previewBusy, services, workspace]);

  const dryRunSelectedRule = useCallback(async () => {
    const activeRules = workspace?.postingRules.filter((item) => !item.isArchived) ?? [];
    const rule = activeRules.find((item) => item.ruleId === selectedRuleId) ?? activeRules[0] ?? null;
    if (!workspace || !rule || dryRunBusy) {
      return;
    }

    setDryRunBusy(true);
    setDryRunError(null);
    try {
      const result = await services.dryRunRule({
        fundProfileId: workspace.fundProfileId,
        ledgerBookId: workspace.ledgerBookId ?? null,
        sourceEventType: rule.sourceEventType,
        eventAmount: resolveDryRunEventAmount(rule),
        currency: resolveDryRunCurrency(rule),
        effectiveDate: resolveDryRunEffectiveDate(rule),
        actor: "browser-accounting-operator",
        dimensions: rule.scope ?? null,
        counterpartyId: rule.scope?.counterpartyId ?? null,
        instrumentSymbol: null,
        correlationId: `browser-accounting-rule-dry-run-${Date.now()}`
      });
      setSelectedRuleId(result.selectedRuleId ?? rule.ruleId);
      setDryRunPreview(result);
    } catch (err) {
      setDryRunError(describeApiError(err, "Accounting rule dry run failed."));
    } finally {
      setDryRunBusy(false);
    }
  }, [dryRunBusy, selectedRuleId, services, workspace]);

  const createLedgerBookFromSetupCandidate = useCallback(async () => {
    const candidate = workspace?.ledgerBookSetupCandidate ?? null;
    if (!workspace || !candidate || createLedgerBookBusy) {
      return;
    }

    setCreateLedgerBookBusy(true);
    setCreateLedgerBookError(null);
    setCreatedLedgerBookName(null);
    try {
      const created = await services.createLedgerBook({
        fundProfileId: candidate.fundProfileId,
        fundStructureNodeId: candidate.fundStructureNodeId,
        fundStructureNodeKind: candidate.fundStructureNodeKind,
        displayName: candidate.displayName,
        baseCurrency: candidate.baseCurrency,
        description: candidate.description ?? null,
        accountingBasis: candidate.accountingBasis,
        accountingPolicyId: candidate.accountingPolicyId,
        accountingPolicyVersion: candidate.accountingPolicyVersion
      });
      setCreatedLedgerBookName(created.displayName);
      const refreshed = await services.getConfiguration();
      setWorkspace(refreshed);
      setSelectedRuleId((current) => {
        if (current && refreshed.postingRules.some((rule) => rule.ruleId === current && !rule.isArchived)) {
          return current;
        }

        return refreshed.postingRules.find((rule) => !rule.isArchived)?.ruleId ?? null;
      });
    } catch (err) {
      setCreateLedgerBookError(describeApiError(err, "Ledger book setup could not be created."));
    } finally {
      setCreateLedgerBookBusy(false);
    }
  }, [createLedgerBookBusy, services, workspace]);

  const runRuleTests = useCallback(async () => {
    const activeRules = workspace?.postingRules.filter((item) => !item.isArchived) ?? [];
    const savedTestCases = workspace?.ruleTestCases ?? [];
    if (!workspace || (activeRules.length === 0 && savedTestCases.length === 0) || ruleTestBusy) {
      return;
    }

    setRuleTestBusy(true);
    setRuleTestError(null);
    try {
      const result = await services.runRuleTests({
        fundProfileId: workspace.fundProfileId,
        ledgerBookId: workspace.ledgerBookId ?? null,
        actor: "browser-accounting-operator",
        correlationId: `browser-accounting-rule-tests-${Date.now()}`,
        testCases: savedTestCases.length > 0
          ? null
          : activeRules.map((rule) => buildAccountingRuleTestCase(workspace, rule))
      });
      setRuleTestSuite(result);
    } catch (err) {
      setRuleTestError(describeApiError(err, "Accounting rule test cases failed."));
    } finally {
      setRuleTestBusy(false);
    }
  }, [ruleTestBusy, services, workspace]);

  const buildJournalCandidate = useCallback(async () => {
    if (!workspace || !dryRunPreview || journalCandidateBusy) {
      return;
    }

    const selectedRule = workspace.postingRules.find((rule) => rule.ruleId === dryRunPreview.selectedRuleId && !rule.isArchived) ??
      workspace.postingRules.find((rule) => rule.ruleId === selectedRuleId && !rule.isArchived) ??
      null;
    if (!selectedRule) {
      setJournalCandidateError({ summary: "Dry-run did not select an active posting rule.", details: [] });
      return;
    }

    const timestamp = Date.now();
    const correlationId = buildBrowserGuid(timestamp, 1);
    const evidenceLinks = [
      `browser://accounting/rules-studio/dry-run/${selectedRule.ruleId}`,
      `browser://accounting/rules-studio/journal-candidate/${selectedRule.ruleId}/${correlationId}`
    ];
    const request: PostingRuleJournalCandidateRequest = {
      fundProfileId: workspace.fundProfileId,
      ledgerBookId: workspace.ledgerBookId ?? null,
      sourceEventType: dryRunPreview.sourceEventType,
      eventAmount: dryRunPreview.eventAmount,
      currency: dryRunPreview.currency,
      effectiveDate: dryRunPreview.effectiveDate,
      actor: "browser-accounting-operator",
      aggregateId: buildBrowserGuid(timestamp, 2),
      periodId: buildBrowserGuid(timestamp, 3),
      accountingTimestamp: new Date(timestamp).toISOString(),
      description: `${selectedRule.displayName} governed journal draft candidate`,
      accountingBasis: resolveRuleAccountingBasis(workspace),
      dimensions: cloneLedgerDimensionSet(selectedRule.scope),
      counterpartyId: selectedRule.scope?.counterpartyId ?? null,
      instrumentSymbol: selectedRule.scope?.instrumentId ?? null,
      correlationId,
      sourceEventId: buildBrowserGuid(timestamp, 4),
      policyId: resolveRulePolicyId(workspace),
      treatmentKind: "General",
      postingKind: "Originating",
      evidenceLinks
    };

    setJournalCandidateBusy(true);
    setJournalCandidateError(null);
    try {
      const result = await services.buildJournalCandidate(request);
      setJournalCandidatePreview(result);
      if (result.selectedRuleId) {
        setSelectedRuleId(result.selectedRuleId);
      }
    } catch (err) {
      setJournalCandidateError(describeApiError(err, "Journal draft candidate build failed."));
    } finally {
      setJournalCandidateBusy(false);
    }
  }, [dryRunPreview, journalCandidateBusy, selectedRuleId, services, workspace]);

  const saveDryRunAsRuleTest = useCallback(async () => {
    if (!workspace || !dryRunPreview || saveRuleTestBusy) {
      return;
    }

    const selectedRuleId = dryRunPreview.selectedRuleId;
    if (!selectedRuleId) {
      setSaveRuleTestError({ summary: "Dry-run did not select a posting rule.", details: [] });
      return;
    }

    const selectedRule = workspace.postingRules.find((rule) => rule.ruleId === selectedRuleId) ?? null;
    const testCase: AccountingRuleTestCase = {
      testCaseId: `rule-test-${selectedRuleId}`,
      displayName: `${selectedRule?.displayName ?? selectedRuleId} retained dry-run regression`,
      request: {
        fundProfileId: workspace.fundProfileId,
        ledgerBookId: workspace.ledgerBookId ?? null,
        sourceEventType: dryRunPreview.sourceEventType,
        eventAmount: dryRunPreview.eventAmount,
        currency: dryRunPreview.currency,
        effectiveDate: dryRunPreview.effectiveDate,
        actor: "browser-accounting-operator",
        dimensions: selectedRule?.scope ?? null,
        counterpartyId: selectedRule?.scope?.counterpartyId ?? null,
        instrumentSymbol: null,
        correlationId: `browser-accounting-rule-test-${selectedRuleId}`
      },
      expectedRuleId: selectedRuleId,
      expectedRuleVersion: selectedRule?.ruleVersion ?? null,
      expectBalancedPosting: dryRunPreview.isPostingBalanced,
      expectedIssueCodes: dryRunPreview.validationIssues.map((issue) => issue.code),
      expectedGeneratedPostingLines: (dryRunPreview.generatedPostingLines ?? []).map((line) => ({
        ...line,
        dimensions: cloneLedgerDimensionSet(line.dimensions)
      })),
      evidenceLinks: [
        `browser://accounting/rules-studio/dry-run/${selectedRuleId}`,
        `browser://accounting/rules-studio/test-case/${selectedRuleId}`
      ]
    };

    setSaveRuleTestBusy(true);
    setSaveRuleTestError(null);
    try {
      const next = await services.saveRuleTestCase({
        fundProfileId: workspace.fundProfileId,
        ledgerBookId: workspace.ledgerBookId ?? null,
        actor: "browser-accounting-operator",
        correlationId: `browser-accounting-rule-test-save-${Date.now()}`,
        evidenceLinks: testCase.evidenceLinks,
        testCase
      });
      setWorkspace(next);
    } catch (err) {
      setSaveRuleTestError(describeApiError(err, "Accounting rule test case could not be saved."));
    } finally {
      setSaveRuleTestBusy(false);
    }
  }, [dryRunPreview, saveRuleTestBusy, services, workspace]);

  const applyDryRunEventPredicate = useCallback(async () => {
    const activeRules = workspace?.postingRules.filter((item) => !item.isArchived) ?? [];
    const rule = activeRules.find((item) => item.ruleId === selectedRuleId) ?? activeRules[0] ?? null;
    if (!workspace || !rule || !dryRunPreview || applyEventPredicateBusy) {
      return;
    }

    const sourceEventConditionId = `${rule.ruleId}-source-event`;
    const existingConditions = rule.conditions ?? [];
    const eventPredicateRule: PostingRule = {
      ...rule,
      ruleVersion: `${rule.ruleVersion}.event`,
      conditions: [
        ...existingConditions
          .filter((condition) =>
            condition.conditionId !== sourceEventConditionId &&
            !(condition.field === "event.kind" && condition.operator === "Equals"))
          .map((condition) => ({ ...condition })),
        {
          conditionId: sourceEventConditionId,
          field: "event.kind",
          operator: "Equals",
          value: dryRunPreview.sourceEventType,
          secondValue: null,
          isRequired: true,
          description: `Source event predicate captured from dry-run ${dryRunPreview.effectiveDate}.`
        }
      ],
      scope: cloneLedgerDimensionSet(rule.scope),
      formulas: rule.formulas?.map((formula) => ({ ...formula })) ?? null,
      allocations: rule.allocations?.map((allocation) => ({
        ...allocation,
        targetDimensions: cloneLedgerDimensionSet(allocation.targetDimensions)
      })) ?? null,
      generatedPostings: rule.generatedPostings?.map((posting) => ({
        ...posting,
        dimensions: cloneLedgerDimensionSet(posting.dimensions)
      })) ?? null,
      promotionApproval: null,
      requiresPromotionApproval: true
    };

    const timestamp = Date.now();
    setApplyEventPredicateBusy(true);
    setApplyEventPredicateError(null);
    setEventPredicateRuleId(null);
    try {
      const next = await services.upsertRule({
        fundProfileId: workspace.fundProfileId,
        ledgerBookId: workspace.ledgerBookId ?? null,
        actor: "browser-accounting-operator",
        rule: eventPredicateRule,
        correlationId: `browser-accounting-rule-event-predicate-${timestamp}`,
        evidenceLinks: [`browser://accounting/rules-studio/event-predicate/${rule.ruleId}`]
      });
      setWorkspace(next);
      setSelectedRuleId(rule.ruleId);
      setEventPredicateRuleId(rule.ruleId);
    } catch (err) {
      setApplyEventPredicateError(describeApiError(err, "Posting rule event predicate could not be applied."));
    } finally {
      setApplyEventPredicateBusy(false);
    }
  }, [applyEventPredicateBusy, dryRunPreview, selectedRuleId, services, workspace]);

  const applyDryRunAmountThreshold = useCallback(async () => {
    const activeRules = workspace?.postingRules.filter((item) => !item.isArchived) ?? [];
    const rule = activeRules.find((item) => item.ruleId === selectedRuleId) ?? activeRules[0] ?? null;
    if (!workspace || !rule || !dryRunPreview || applyThresholdBusy) {
      return;
    }

    const thresholdConditionId = `${rule.ruleId}-minimum-amount`;
    const existingConditions = rule.conditions ?? [];
    const thresholdRule: PostingRule = {
      ...rule,
      ruleVersion: `${rule.ruleVersion}.threshold`,
      conditions: [
        ...existingConditions
          .filter((condition) => condition.conditionId !== thresholdConditionId)
          .map((condition) => ({ ...condition })),
        {
          conditionId: thresholdConditionId,
          field: "event.amount",
          operator: "AmountGreaterThanOrEqual",
          value: String(dryRunPreview.eventAmount),
          secondValue: null,
          isRequired: true,
          description: `Minimum amount captured from dry-run ${dryRunPreview.effectiveDate}.`
        }
      ],
      scope: cloneLedgerDimensionSet(rule.scope),
      formulas: rule.formulas?.map((formula) => ({ ...formula })) ?? null,
      allocations: rule.allocations?.map((allocation) => ({
        ...allocation,
        targetDimensions: cloneLedgerDimensionSet(allocation.targetDimensions)
      })) ?? null,
      generatedPostings: rule.generatedPostings?.map((posting) => ({
        ...posting,
        dimensions: cloneLedgerDimensionSet(posting.dimensions)
      })) ?? null,
      promotionApproval: null,
      requiresPromotionApproval: true
    };

    const timestamp = Date.now();
    setApplyThresholdBusy(true);
    setApplyThresholdError(null);
    setThresholdRuleId(null);
    try {
      const next = await services.upsertRule({
        fundProfileId: workspace.fundProfileId,
        ledgerBookId: workspace.ledgerBookId ?? null,
        actor: "browser-accounting-operator",
        rule: thresholdRule,
        correlationId: `browser-accounting-rule-threshold-${timestamp}`,
        evidenceLinks: [`browser://accounting/rules-studio/threshold/${rule.ruleId}`]
      });
      setWorkspace(next);
      setSelectedRuleId(rule.ruleId);
      setThresholdRuleId(rule.ruleId);
    } catch (err) {
      setApplyThresholdError(describeApiError(err, "Posting rule threshold could not be applied."));
    } finally {
      setApplyThresholdBusy(false);
    }
  }, [applyThresholdBusy, dryRunPreview, selectedRuleId, services, workspace]);

  const applyDryRunEffectiveStart = useCallback(async () => {
    const activeRules = workspace?.postingRules.filter((item) => !item.isArchived) ?? [];
    const rule = activeRules.find((item) => item.ruleId === selectedRuleId) ?? activeRules[0] ?? null;
    if (!workspace || !rule || !dryRunPreview || applyEffectiveStartBusy) {
      return;
    }

    const effectiveRule: PostingRule = {
      ...rule,
      ruleVersion: `${rule.ruleVersion}.effective`,
      effectiveFrom: dryRunPreview.effectiveDate,
      effectiveTo: rule.effectiveTo && rule.effectiveTo < dryRunPreview.effectiveDate ? null : rule.effectiveTo ?? null,
      conditions: rule.conditions?.map((condition) => ({ ...condition })) ?? null,
      scope: cloneLedgerDimensionSet(rule.scope),
      formulas: rule.formulas?.map((formula) => ({ ...formula })) ?? null,
      allocations: rule.allocations?.map((allocation) => ({
        ...allocation,
        targetDimensions: cloneLedgerDimensionSet(allocation.targetDimensions)
      })) ?? null,
      generatedPostings: rule.generatedPostings?.map((posting) => ({
        ...posting,
        dimensions: cloneLedgerDimensionSet(posting.dimensions)
      })) ?? null,
      promotionApproval: null,
      requiresPromotionApproval: true
    };

    const timestamp = Date.now();
    setApplyEffectiveStartBusy(true);
    setApplyEffectiveStartError(null);
    setEffectiveStartRuleId(null);
    try {
      const next = await services.upsertRule({
        fundProfileId: workspace.fundProfileId,
        ledgerBookId: workspace.ledgerBookId ?? null,
        actor: "browser-accounting-operator",
        rule: effectiveRule,
        correlationId: `browser-accounting-rule-effective-start-${timestamp}`,
        evidenceLinks: [`browser://accounting/rules-studio/effective-start/${rule.ruleId}`]
      });
      setWorkspace(next);
      setSelectedRuleId(rule.ruleId);
      setEffectiveStartRuleId(rule.ruleId);
    } catch (err) {
      setApplyEffectiveStartError(describeApiError(err, "Posting rule effective start could not be applied."));
    } finally {
      setApplyEffectiveStartBusy(false);
    }
  }, [applyEffectiveStartBusy, dryRunPreview, selectedRuleId, services, workspace]);

  const applyDryRunScope = useCallback(async () => {
    const activeRules = workspace?.postingRules.filter((item) => !item.isArchived) ?? [];
    const rule = activeRules.find((item) => item.ruleId === selectedRuleId) ?? activeRules[0] ?? null;
    const generatedPostings = dryRunPreview?.generatedPostingLines ?? [];
    if (!workspace || !rule || !dryRunPreview || generatedPostings.length === 0 || applyScopeBusy) {
      return;
    }

    const nextScope = mergeRuleScopeFromGeneratedPostings(rule.scope, generatedPostings);
    const scopedRule: PostingRule = {
      ...rule,
      ruleVersion: `${rule.ruleVersion}.scope`,
      conditions: rule.conditions?.map((condition) => ({ ...condition })) ?? null,
      scope: nextScope,
      formulas: rule.formulas?.map((formula) => ({ ...formula })) ?? null,
      allocations: rule.allocations?.map((allocation) => ({
        ...allocation,
        targetDimensions: cloneLedgerDimensionSet(allocation.targetDimensions)
      })) ?? null,
      generatedPostings: rule.generatedPostings?.map((posting) => ({
        ...posting,
        dimensions: cloneLedgerDimensionSet(posting.dimensions)
      })) ?? null,
      promotionApproval: null,
      requiresPromotionApproval: true
    };

    const timestamp = Date.now();
    setApplyScopeBusy(true);
    setApplyScopeError(null);
    setScopedRuleId(null);
    try {
      const next = await services.upsertRule({
        fundProfileId: workspace.fundProfileId,
        ledgerBookId: workspace.ledgerBookId ?? null,
        actor: "browser-accounting-operator",
        rule: scopedRule,
        correlationId: `browser-accounting-rule-scope-${timestamp}`,
        evidenceLinks: [`browser://accounting/rules-studio/scope/${rule.ruleId}`]
      });
      setWorkspace(next);
      setSelectedRuleId(rule.ruleId);
      setScopedRuleId(rule.ruleId);
    } catch (err) {
      setApplyScopeError(describeApiError(err, "Posting rule scope could not be applied."));
    } finally {
      setApplyScopeBusy(false);
    }
  }, [applyScopeBusy, dryRunPreview, selectedRuleId, services, workspace]);

  const captureDryRunGeneratedPostings = useCallback(async () => {
    const activeRules = workspace?.postingRules.filter((item) => !item.isArchived) ?? [];
    const rule = activeRules.find((item) => item.ruleId === selectedRuleId) ?? activeRules[0] ?? null;
    const generatedPostings = dryRunPreview?.generatedPostingLines ?? [];
    if (!workspace || !rule || !dryRunPreview || generatedPostings.length === 0 || capturePostingsBusy) {
      return;
    }

    const capturedRule: PostingRule = {
      ...rule,
      ruleVersion: `${rule.ruleVersion}.postings`,
      conditions: rule.conditions?.map((condition) => ({ ...condition })) ?? null,
      scope: cloneLedgerDimensionSet(rule.scope),
      formulas: rule.formulas?.map((formula) => ({ ...formula })) ?? null,
      allocations: rule.allocations?.map((allocation) => ({
        ...allocation,
        targetDimensions: cloneLedgerDimensionSet(allocation.targetDimensions)
      })) ?? null,
      generatedPostings: generatedPostings.map((posting) => ({
        ...posting,
        dimensions: cloneLedgerDimensionSet(posting.dimensions)
      })),
      promotionApproval: null,
      requiresPromotionApproval: true
    };

    const timestamp = Date.now();
    setCapturePostingsBusy(true);
    setCapturePostingsError(null);
    setCapturedPostingsRuleId(null);
    try {
      const next = await services.upsertRule({
        fundProfileId: workspace.fundProfileId,
        ledgerBookId: workspace.ledgerBookId ?? null,
        actor: "browser-accounting-operator",
        rule: capturedRule,
        correlationId: `browser-accounting-rule-generated-postings-${timestamp}`,
        evidenceLinks: [`browser://accounting/rules-studio/generated-postings/${rule.ruleId}`]
      });
      setWorkspace(next);
      setSelectedRuleId(rule.ruleId);
      setCapturedPostingsRuleId(rule.ruleId);
    } catch (err) {
      setCapturePostingsError(describeApiError(err, "Generated posting lines could not be captured."));
    } finally {
      setCapturePostingsBusy(false);
    }
  }, [capturePostingsBusy, dryRunPreview, selectedRuleId, services, workspace]);

  const applyDryRunFormulaAmount = useCallback(async () => {
    const activeRules = workspace?.postingRules.filter((item) => !item.isArchived) ?? [];
    const rule = activeRules.find((item) => item.ruleId === selectedRuleId) ?? activeRules[0] ?? null;
    const formulaCandidate = rule && dryRunPreview
      ? resolveDryRunFormulaCandidate(rule, dryRunPreview)
      : null;
    if (!workspace || !rule || !dryRunPreview || !formulaCandidate || applyFormulaBusy) {
      return;
    }

    const formulaRule: PostingRule = {
      ...rule,
      ruleVersion: `${rule.ruleVersion}.formula`,
      conditions: rule.conditions?.map((condition) => ({ ...condition })) ?? null,
      scope: cloneLedgerDimensionSet(rule.scope),
      formulas: (rule.formulas ?? []).map((formula) => formula.formulaId === formulaCandidate.formulaId
        ? {
            ...formula,
            value: formulaCandidate.amount,
            currency: formulaCandidate.currency,
            description: `Formula amount retained from dry-run ${dryRunPreview.effectiveDate}.`
          }
        : { ...formula }),
      allocations: rule.allocations?.map((allocation) => ({
        ...allocation,
        targetDimensions: cloneLedgerDimensionSet(allocation.targetDimensions)
      })) ?? null,
      generatedPostings: rule.generatedPostings?.map((posting) => ({
        ...posting,
        dimensions: cloneLedgerDimensionSet(posting.dimensions)
      })) ?? null,
      promotionApproval: null,
      requiresPromotionApproval: true
    };

    const timestamp = Date.now();
    setApplyFormulaBusy(true);
    setApplyFormulaError(null);
    setFormulaRuleId(null);
    try {
      const next = await services.upsertRule({
        fundProfileId: workspace.fundProfileId,
        ledgerBookId: workspace.ledgerBookId ?? null,
        actor: "browser-accounting-operator",
        rule: formulaRule,
        correlationId: `browser-accounting-rule-formula-${timestamp}`,
        evidenceLinks: [`browser://accounting/rules-studio/formula/${rule.ruleId}`]
      });
      setWorkspace(next);
      setSelectedRuleId(rule.ruleId);
      setFormulaRuleId(rule.ruleId);
    } catch (err) {
      setApplyFormulaError(describeApiError(err, "Posting rule formula amount could not be applied."));
    } finally {
      setApplyFormulaBusy(false);
    }
  }, [applyFormulaBusy, dryRunPreview, selectedRuleId, services, workspace]);

  const applyDryRunAllocationTargets = useCallback(async () => {
    const activeRules = workspace?.postingRules.filter((item) => !item.isArchived) ?? [];
    const rule = activeRules.find((item) => item.ruleId === selectedRuleId) ?? activeRules[0] ?? null;
    const nextAllocations = rule && dryRunPreview
      ? mergeAllocationTargetsFromGeneratedPostings(rule.allocations ?? [], dryRunPreview.generatedPostingLines ?? [])
      : null;
    if (!workspace || !rule || !dryRunPreview || !nextAllocations || applyAllocationBusy) {
      return;
    }

    const allocationRule: PostingRule = {
      ...rule,
      ruleVersion: `${rule.ruleVersion}.allocation`,
      conditions: rule.conditions?.map((condition) => ({ ...condition })) ?? null,
      scope: cloneLedgerDimensionSet(rule.scope),
      formulas: rule.formulas?.map((formula) => ({ ...formula })) ?? null,
      allocations: nextAllocations,
      generatedPostings: rule.generatedPostings?.map((posting) => ({
        ...posting,
        dimensions: cloneLedgerDimensionSet(posting.dimensions)
      })) ?? null,
      promotionApproval: null,
      requiresPromotionApproval: true
    };

    const timestamp = Date.now();
    setApplyAllocationBusy(true);
    setApplyAllocationError(null);
    setAllocationRuleId(null);
    try {
      const next = await services.upsertRule({
        fundProfileId: workspace.fundProfileId,
        ledgerBookId: workspace.ledgerBookId ?? null,
        actor: "browser-accounting-operator",
        rule: allocationRule,
        correlationId: `browser-accounting-rule-allocation-${timestamp}`,
        evidenceLinks: [`browser://accounting/rules-studio/allocation/${rule.ruleId}`]
      });
      setWorkspace(next);
      setSelectedRuleId(rule.ruleId);
      setAllocationRuleId(rule.ruleId);
    } catch (err) {
      setApplyAllocationError(describeApiError(err, "Posting rule allocation targets could not be applied."));
    } finally {
      setApplyAllocationBusy(false);
    }
  }, [applyAllocationBusy, dryRunPreview, selectedRuleId, services, workspace]);

  const raiseSelectedRulePriority = useCallback(async () => {
    const activeRules = workspace?.postingRules.filter((item) => !item.isArchived) ?? [];
    const rule = activeRules.find((item) => item.ruleId === selectedRuleId) ?? activeRules[0] ?? null;
    if (!workspace || !rule || raisePriorityBusy) {
      return;
    }

    const prioritizedRule: PostingRule = {
      ...rule,
      ruleVersion: `${rule.ruleVersion}.priority`,
      priority: (rule.priority ?? 0) + 1,
      conditions: rule.conditions?.map((condition) => ({ ...condition })) ?? null,
      scope: cloneLedgerDimensionSet(rule.scope),
      formulas: rule.formulas?.map((formula) => ({ ...formula })) ?? null,
      allocations: rule.allocations?.map((allocation) => ({
        ...allocation,
        targetDimensions: cloneLedgerDimensionSet(allocation.targetDimensions)
      })) ?? null,
      generatedPostings: rule.generatedPostings?.map((posting) => ({
        ...posting,
        dimensions: cloneLedgerDimensionSet(posting.dimensions)
      })) ?? null,
      promotionApproval: null,
      requiresPromotionApproval: true
    };

    const timestamp = Date.now();
    setRaisePriorityBusy(true);
    setRaisePriorityError(null);
    setRaisedPriorityRuleId(null);
    try {
      const next = await services.upsertRule({
        fundProfileId: workspace.fundProfileId,
        ledgerBookId: workspace.ledgerBookId ?? null,
        actor: "browser-accounting-operator",
        rule: prioritizedRule,
        correlationId: `browser-accounting-rule-priority-${timestamp}`,
        evidenceLinks: [`browser://accounting/rules-studio/priority/${rule.ruleId}`]
      });
      setWorkspace(next);
      setSelectedRuleId(rule.ruleId);
      setRaisedPriorityRuleId(rule.ruleId);
    } catch (err) {
      setRaisePriorityError(describeApiError(err, "Posting rule priority could not be raised."));
    } finally {
      setRaisePriorityBusy(false);
    }
  }, [raisePriorityBusy, selectedRuleId, services, workspace]);

  const duplicateSelectedRule = useCallback(async () => {
    const activeRules = workspace?.postingRules.filter((item) => !item.isArchived) ?? [];
    const rule = activeRules.find((item) => item.ruleId === selectedRuleId) ?? activeRules[0] ?? null;
    if (!workspace || !rule || duplicateRuleBusy) {
      return;
    }

    const timestamp = Date.now();
    const draftRuleId = `${rule.ruleId}-draft-${timestamp}`;
    const duplicatedRule: PostingRule = {
      ...rule,
      ruleId: draftRuleId,
      displayName: `${rule.displayName} draft`,
      description: rule.description
        ? `${rule.description} Drafted from ${rule.ruleId}.`
        : `Drafted from ${rule.ruleId}.`,
      ruleVersion: `${rule.ruleVersion}.draft`,
      isArchived: false,
      priority: (rule.priority ?? 0) + 1,
      scope: cloneLedgerDimensionSet(rule.scope),
      conditions: rule.conditions?.map((condition) => ({ ...condition })) ?? null,
      formulas: rule.formulas?.map((formula) => ({ ...formula })) ?? null,
      allocations: rule.allocations?.map((allocation) => ({
        ...allocation,
        targetDimensions: cloneLedgerDimensionSet(allocation.targetDimensions)
      })) ?? null,
      generatedPostings: rule.generatedPostings?.map((posting) => ({
        ...posting,
        dimensions: cloneLedgerDimensionSet(posting.dimensions)
      })) ?? null,
      versions: [],
      promotionApproval: null,
      requiresPromotionApproval: true
    };

    setDuplicateRuleBusy(true);
    setDuplicateRuleError(null);
    setDuplicatedRuleId(null);
    try {
      const next = await services.upsertRule({
        fundProfileId: workspace.fundProfileId,
        ledgerBookId: workspace.ledgerBookId ?? null,
        actor: "browser-accounting-operator",
        rule: duplicatedRule,
        correlationId: `browser-accounting-rule-duplicate-${timestamp}`,
        evidenceLinks: [`browser://accounting/rules-studio/duplicate/${rule.ruleId}`]
      });
      setWorkspace(next);
      setSelectedRuleId(draftRuleId);
      setDuplicatedRuleId(draftRuleId);
    } catch (err) {
      setDuplicateRuleError(describeApiError(err, "Posting rule could not be duplicated."));
    } finally {
      setDuplicateRuleBusy(false);
    }
  }, [duplicateRuleBusy, selectedRuleId, services, workspace]);

  const archiveSelectedRule = useCallback(async () => {
    const activeRules = workspace?.postingRules.filter((item) => !item.isArchived) ?? [];
    const rule = activeRules.find((item) => item.ruleId === selectedRuleId) ?? activeRules[0] ?? null;
    if (!workspace || !rule || archiveRuleBusy) {
      return;
    }

    const archivedRule: PostingRule = {
      ...rule,
      isArchived: true,
      scope: cloneLedgerDimensionSet(rule.scope),
      conditions: rule.conditions?.map((condition) => ({ ...condition })) ?? null,
      formulas: rule.formulas?.map((formula) => ({ ...formula })) ?? null,
      allocations: rule.allocations?.map((allocation) => ({
        ...allocation,
        targetDimensions: cloneLedgerDimensionSet(allocation.targetDimensions)
      })) ?? null,
      generatedPostings: rule.generatedPostings?.map((posting) => ({
        ...posting,
        dimensions: cloneLedgerDimensionSet(posting.dimensions)
      })) ?? null
    };

    const timestamp = Date.now();
    setArchiveRuleBusy(true);
    setArchiveRuleError(null);
    setArchivedRuleId(null);
    try {
      const next = await services.upsertRule({
        fundProfileId: workspace.fundProfileId,
        ledgerBookId: workspace.ledgerBookId ?? null,
        actor: "browser-accounting-operator",
        rule: archivedRule,
        correlationId: `browser-accounting-rule-archive-${timestamp}`,
        evidenceLinks: [`browser://accounting/rules-studio/archive/${rule.ruleId}`]
      });
      setWorkspace(next);
      setSelectedRuleId(next.postingRules.find((item) => !item.isArchived)?.ruleId ?? null);
      setDryRunPreview(null);
      setArchivedRuleId(rule.ruleId);
    } catch (err) {
      setArchiveRuleError(describeApiError(err, "Posting rule could not be archived."));
    } finally {
      setArchiveRuleBusy(false);
    }
  }, [archiveRuleBusy, selectedRuleId, services, workspace]);

  const approveRulePromotion = useCallback(async () => {
    const activeRules = workspace?.postingRules.filter((item) => !item.isArchived) ?? [];
    const rule = activeRules.find((item) => item.ruleId === selectedRuleId) ?? activeRules[0] ?? null;
    if (!workspace || !rule || approveRulePromotionBusy || isApprovedRulePromotion(rule.promotionApproval)) {
      return;
    }

    const evidenceLink = `browser://accounting/rules-studio/promotion-review/${rule.ruleId}/${rule.ruleVersion}`;
    setApproveRulePromotionBusy(true);
    setApproveRulePromotionError(null);
    try {
      const next = await services.approveRulePromotion({
        fundProfileId: workspace.fundProfileId,
        ledgerBookId: workspace.ledgerBookId ?? null,
        ruleId: rule.ruleId,
        ruleVersion: rule.ruleVersion,
        actor: "browser-accounting-operator",
        approvalId: `browser-approval-${rule.ruleId}-${rule.ruleVersion}`,
        notes: `Approved ${rule.displayName} ${rule.ruleVersion} from Accounting Rules Studio.`,
        evidenceLinks: [evidenceLink],
        requestedBy: rule.promotionApproval?.requestedBy ?? "browser-accounting-operator",
        requestedAtUtc: rule.promotionApproval?.requestedAtUtc ?? new Date().toISOString(),
        correlationId: `browser-accounting-rule-promotion-${Date.now()}`
      });
      setWorkspace(next);
      setSelectedRuleId(rule.ruleId);
      setApprovedRulePromotionId(rule.ruleId);
    } catch (err) {
      setApproveRulePromotionError(describeApiError(err, "Posting rule promotion approval failed."));
    } finally {
      setApproveRulePromotionBusy(false);
    }
  }, [approveRulePromotionBusy, selectedRuleId, services, workspace]);

  const activate = useCallback(async () => {
    if (!workspace || activateBusy) {
      return;
    }

    setActivateBusy(true);
    setActivateError(null);
    try {
      const activated = await services.activate({
        fundProfileId: workspace.fundProfileId,
        ledgerBookId: workspace.ledgerBookId ?? null,
        actor: "browser-accounting-operator",
        correlationId: `browser-accounting-config-activate-${Date.now()}`,
        evidenceLinks: ["browser://accounting/configure"]
      });
      setWorkspace(activated);
    } catch (err) {
      setActivateError(describeApiError(err, "Accounting configuration activation failed."));
    } finally {
      setActivateBusy(false);
    }
  }, [activateBusy, services, workspace]);

  return useMemo(() => {
    const issueCount = workspace?.validationIssues.length ?? 0;
    const criticalIssueCount = workspace?.validationIssues.filter((issue) => issue.severity === "Critical").length ?? 0;
    const activeTemplateCount = workspace?.journalTemplates.filter((item) => !item.isArchived).length ?? 0;
    const activeRuleCount = workspace?.postingRules.filter((item) => !item.isArchived).length ?? 0;
    const activeChartNodeCount = workspace?.chartOfAccounts.filter((item) => !item.isArchived).length ?? 0;
    const hasTemplate = activeTemplateCount > 0;
    const hasChart = activeChartNodeCount > 0;
    const hasRule = activeRuleCount > 0;
    const activeRules = workspace?.postingRules.filter((item) => !item.isArchived) ?? [];
    const savedRuleTestCases = workspace?.ruleTestCases ?? [];
    const studioSummary = workspace?.rulesStudio?.summary ?? null;
    const studioRuleRows = workspace?.rulesStudio?.rules ?? [];
    const studioRuleRowsById = new Map(studioRuleRows.map((row) => [row.ruleId, row]));
    const studioPromotionQueue = workspace?.rulesStudio?.promotionQueue ?? [];
    const studioPromotionQueueById = new Map(studioPromotionQueue.map((item) => [item.ruleId, item]));
    const promotionApprovalBlockedRules = activeRules.filter((rule) =>
      rule.requiresPromotionApproval === true && !isApprovedRulePromotion(rule.promotionApproval));
    const savedRuleTestExpectedRuleVersions = new Set(savedRuleTestCases
      .filter((testCase) => Boolean(testCase.expectedRuleId) && Boolean(testCase.expectedRuleVersion))
      .map((testCase) => `${testCase.expectedRuleId}:${testCase.expectedRuleVersion}`));
    const promotionTestCoverageBlockedRules = activeRules.filter((rule) =>
      rule.requiresPromotionApproval === true &&
      !savedRuleTestExpectedRuleVersions.has(`${rule.ruleId}:${rule.ruleVersion ?? "v1"}`));
    const lastRuleTestSuiteFailed = (ruleTestSuite?.failedCount ?? 0) > 0;
    const resolvedSelectedRuleId = selectedRuleId && activeRules.some((rule) => rule.ruleId === selectedRuleId)
      ? selectedRuleId
      : activeRules[0]?.ruleId ?? null;
    const selectedPostingRule = activeRules.find((rule) => rule.ruleId === resolvedSelectedRuleId) ?? null;
    const selectedStudioRule = resolvedSelectedRuleId ? studioRuleRowsById.get(resolvedSelectedRuleId) ?? null : null;
    const dryRunDisabledReason = loading
      ? "Accounting configuration is still loading."
      : !workspace
        ? "Load accounting configuration before running rule previews."
        : !resolvedSelectedRuleId
          ? "Create at least one active posting rule before dry run preview."
          : null;
    const createLedgerBookDisabledReason = loading
      ? "Accounting configuration is still loading."
      : !workspace
        ? "Load accounting configuration before creating ledger-book setup."
        : !workspace.ledgerBookSetupCandidate
          ? "No server-provided ledger-book setup candidate is available for this configuration scope."
          : null;
    const applyThresholdDisabledReason = loading
      ? "Accounting configuration is still loading."
      : !workspace
        ? "Load accounting configuration before applying a threshold."
        : !selectedPostingRule
          ? "Select an active posting rule before applying a threshold."
          : !dryRunPreview
            ? "Run a dry-run preview before applying an amount threshold."
            : dryRunPreview.selectedRuleId && dryRunPreview.selectedRuleId !== selectedPostingRule.ruleId
              ? "Dry-run preview must match the selected posting rule before applying a threshold."
              : null;
    const applyEventPredicateDisabledReason = loading
      ? "Accounting configuration is still loading."
      : !workspace
        ? "Load accounting configuration before applying an event predicate."
        : !selectedPostingRule
          ? "Select an active posting rule before applying an event predicate."
          : !dryRunPreview
            ? "Run a dry-run preview before applying an event predicate."
            : dryRunPreview.selectedRuleId && dryRunPreview.selectedRuleId !== selectedPostingRule.ruleId
              ? "Dry-run preview must match the selected posting rule before applying an event predicate."
              : null;
    const applyEffectiveStartDisabledReason = loading
      ? "Accounting configuration is still loading."
      : !workspace
        ? "Load accounting configuration before applying an effective start date."
        : !selectedPostingRule
          ? "Select an active posting rule before applying an effective start date."
          : !dryRunPreview
            ? "Run a dry-run preview before applying an effective start date."
            : dryRunPreview.selectedRuleId && dryRunPreview.selectedRuleId !== selectedPostingRule.ruleId
              ? "Dry-run preview must match the selected posting rule before applying an effective start date."
              : null;
    const applyScopeDisabledReason = loading
      ? "Accounting configuration is still loading."
      : !workspace
        ? "Load accounting configuration before applying dry-run scope."
        : !selectedPostingRule
          ? "Select an active posting rule before applying dry-run scope."
          : !dryRunPreview
            ? "Run a dry-run preview before applying dry-run scope."
            : dryRunPreview.selectedRuleId && dryRunPreview.selectedRuleId !== selectedPostingRule.ruleId
              ? "Dry-run preview must match the selected posting rule before applying dry-run scope."
              : (dryRunPreview.generatedPostingLines?.length ?? 0) === 0
                ? "Dry-run preview did not return generated posting-line dimensions."
                : ledgerDimensionSetsEqual(
                    selectedPostingRule.scope,
                    mergeRuleScopeFromGeneratedPostings(selectedPostingRule.scope, dryRunPreview.generatedPostingLines ?? [])
                  )
                  ? "Dry-run generated postings do not add new unambiguous rule-scope dimensions."
                  : null;
    const capturePostingsDisabledReason = loading
      ? "Accounting configuration is still loading."
      : !workspace
        ? "Load accounting configuration before capturing generated postings."
        : !selectedPostingRule
          ? "Select an active posting rule before capturing generated postings."
          : !dryRunPreview
            ? "Run a dry-run preview before capturing generated posting lines."
            : dryRunPreview.selectedRuleId && dryRunPreview.selectedRuleId !== selectedPostingRule.ruleId
              ? "Dry-run preview must match the selected posting rule before capturing generated postings."
              : (dryRunPreview.generatedPostingLines?.length ?? 0) === 0
                ? "Dry-run preview did not return generated posting-line metadata."
                : null;
    const formulaCandidate = selectedPostingRule && dryRunPreview
      ? resolveDryRunFormulaCandidate(selectedPostingRule, dryRunPreview)
      : null;
    const applyFormulaDisabledReason = loading
      ? "Accounting configuration is still loading."
      : !workspace
        ? "Load accounting configuration before applying formula amounts."
        : !selectedPostingRule
          ? "Select an active posting rule before applying formula amounts."
          : !dryRunPreview
            ? "Run a dry-run preview before applying formula amounts."
            : dryRunPreview.selectedRuleId && dryRunPreview.selectedRuleId !== selectedPostingRule.ruleId
              ? "Dry-run preview must match the selected posting rule before applying formula amounts."
              : !formulaCandidate
                ? "Dry-run generated postings did not reference a retained rule formula."
                : null;
    const allocationCandidate = selectedPostingRule && dryRunPreview
      ? mergeAllocationTargetsFromGeneratedPostings(selectedPostingRule.allocations ?? [], dryRunPreview.generatedPostingLines ?? [])
      : null;
    const applyAllocationDisabledReason = loading
      ? "Accounting configuration is still loading."
      : !workspace
        ? "Load accounting configuration before applying allocation targets."
        : !selectedPostingRule
          ? "Select an active posting rule before applying allocation targets."
          : !dryRunPreview
            ? "Run a dry-run preview before applying allocation targets."
            : dryRunPreview.selectedRuleId && dryRunPreview.selectedRuleId !== selectedPostingRule.ruleId
              ? "Dry-run preview must match the selected posting rule before applying allocation targets."
              : (selectedPostingRule.allocations?.length ?? 0) === 0
                ? "Selected posting rule has no allocation rules to update."
                : (dryRunPreview.generatedPostingLines?.length ?? 0) === 0
                  ? "Dry-run preview did not return generated posting-line dimensions."
                  : !allocationCandidate
                    ? "Dry-run generated postings do not add new allocation target dimensions."
                    : null;
    const raisePriorityDisabledReason = loading
      ? "Accounting configuration is still loading."
      : !workspace
        ? "Load accounting configuration before raising posting-rule priority."
        : !selectedPostingRule
          ? "Select an active posting rule before raising priority."
          : null;
    const ruleTestDisabledReason = loading
      ? "Accounting configuration is still loading."
      : !workspace
        ? "Load accounting configuration before running rule test cases."
        : activeRules.length === 0 && savedRuleTestCases.length === 0
          ? "Create at least one active posting rule or saved test case before running test cases."
          : null;
    const saveDryRunAsRuleTestDisabledReason = loading
      ? "Accounting configuration is still loading."
      : !workspace
        ? "Load accounting configuration before saving rule test cases."
        : !dryRunPreview
          ? "Run a dry-run preview before saving a regression test case."
          : !dryRunPreview.selectedRuleId
            ? "Dry-run preview must select a posting rule before saving."
            : null;
    const journalCandidateDisabledReason = loading
      ? "Accounting configuration is still loading."
      : !workspace
        ? "Load accounting configuration before building a journal candidate."
        : !selectedPostingRule
          ? "Select an active posting rule before building a journal candidate."
          : !dryRunPreview
            ? "Run a dry-run preview before building a journal candidate."
            : !dryRunPreview.selectedRuleId
              ? "Dry-run preview must select a posting rule before building a journal candidate."
              : dryRunPreview.selectedRuleId !== selectedPostingRule.ruleId
                ? "Dry-run preview must match the selected posting rule before building a journal candidate."
                : null;
    const duplicateRuleDisabledReason = loading
      ? "Accounting configuration is still loading."
      : !workspace
        ? "Load accounting configuration before drafting a posting rule."
        : !selectedPostingRule
          ? "Select an active posting rule before drafting a copy."
          : null;
    const archiveRuleDisabledReason = loading
      ? "Accounting configuration is still loading."
      : !workspace
        ? "Load accounting configuration before archiving a posting rule."
        : !selectedPostingRule
          ? "Select an active posting rule before archiving."
          : null;
    const approveRulePromotionDisabledReason = loading
      ? "Accounting configuration is still loading."
      : !workspace
        ? "Load accounting configuration before approving rule promotion."
        : !selectedPostingRule
          ? "Select an active posting rule before approving promotion."
          : selectedStudioRule?.isPromotionApproved === true || isApprovedRulePromotion(selectedPostingRule.promotionApproval)
            ? "Selected posting rule already has an approved promotion."
            : null;
    const previewDisabledReason = loading
      ? "Accounting configuration is still loading."
      : !workspace
        ? "Load accounting configuration before previewing a template."
        : !hasTemplate
          ? "Create at least one active journal template before preview."
          : null;
    const activateDisabledReason = loading
      ? "Accounting configuration is still loading."
      : !workspace
        ? "Load accounting configuration before activation."
        : workspace.status === "Active"
          ? "Accounting configuration is already active."
          : criticalIssueCount > 0
            ? "Resolve critical validation issues before activation."
            : !hasChart
              ? "Create at least one active chart account before activation."
              : !hasTemplate
                ? "Create at least one active journal template before activation."
                : !hasRule
                  ? "Create at least one active posting rule before activation."
                  : (studioSummary?.pendingPromotionApprovalRules ?? promotionApprovalBlockedRules.length) > 0
                    ? `Approve promotion for ${studioSummary?.pendingPromotionApprovalRules ?? promotionApprovalBlockedRules.length} required posting rule${(studioSummary?.pendingPromotionApprovalRules ?? promotionApprovalBlockedRules.length) === 1 ? "" : "s"} before activation.`
                    : (studioSummary?.rulesMissingCurrentVersionRegressionTests ?? promotionTestCoverageBlockedRules.length) > 0
                      ? `Save regression test cases for ${studioSummary?.rulesMissingCurrentVersionRegressionTests ?? promotionTestCoverageBlockedRules.length} promotion-gated posting rule${(studioSummary?.rulesMissingCurrentVersionRegressionTests ?? promotionTestCoverageBlockedRules.length) === 1 ? "" : "s"} before activation.`
                      : lastRuleTestSuiteFailed
                        ? "Resolve failing rule test cases before activation."
                        : null;
    const statusTone: AccountingConfigurationViewModel["statusTone"] = !workspace
      ? "default"
      : criticalIssueCount > 0
        ? "danger"
        : issueCount > 0 || workspace.status === "Draft"
          ? "warning"
          : workspace.status === "Active"
            ? "success"
            : "default";
    const metricRows: AccountingConfigurationMetricViewModel[] = [
      {
        id: "books",
        label: "Books",
        value: String(workspace?.ledgerBooks.length ?? 0),
        detail: "Reuses registered ledger books and basis policies.",
        tone: (workspace?.ledgerBooks.length ?? 0) > 0 ? "success" : "warning"
      },
      {
        id: "chart",
        label: "Chart accounts",
        value: String(activeChartNodeCount),
        detail: "Hierarchical account paths available to templates.",
        tone: activeChartNodeCount > 0 ? "success" : "warning"
      },
      {
        id: "templates",
        label: "Templates",
        value: String(activeTemplateCount),
        detail: "Balanced journal templates eligible for preview.",
        tone: activeTemplateCount > 0 ? "success" : "warning"
      },
      {
        id: "rules",
        label: "Posting rules",
        value: String(studioSummary?.activeRules ?? activeRuleCount),
        detail: studioSummary
          ? `${studioSummary.generatedPostingRules} generated / ${studioSummary.templateMappingRules} template mappings.`
          : "Source events mapped to non-posting previews.",
        tone: activeRuleCount > 0 ? "success" : "warning"
      },
      {
        id: "audit",
        label: "Audit events",
        value: String(workspace?.auditTrail.length ?? 0),
        detail: "Append-only action evidence for configuration changes.",
        tone: (workspace?.auditTrail.length ?? 0) > 0 ? "success" : "warning"
      },
      {
        id: "rule-tests",
        label: "Rule tests",
        value: String(studioSummary?.savedTestCaseCount ?? savedRuleTestCases.length),
        detail: studioSummary
          ? `${studioSummary.rulesWithSavedRegressionTests} rule(s) covered; ${studioSummary.rulesMissingCurrentVersionRegressionTests} current version gap(s).`
          : "Persisted dry-run regression cases for posting-rule promotion.",
        tone: (studioSummary?.savedTestCaseCount ?? savedRuleTestCases.length) > 0 ? "success" : "warning"
      }
    ];
    const selectedLedgerBook = workspace?.ledgerBooks.find((book) => book.ledgerBookId === workspace.ledgerBookId) ?? null;
    const missingLedgerBookIssue = workspace?.validationIssues.find((issue) => issue.code === "configuration.ledger-book-missing") ?? null;
    const setupReadinessRows: AccountingConfigurationMetricViewModel[] = [
      {
        id: "selected-ledger-book",
        label: "Selected book",
        value: missingLedgerBookIssue
          ? "Missing"
          : selectedLedgerBook
            ? selectedLedgerBook.displayName
            : workspace?.ledgerBookId
              ? "Scoped"
              : "Fund scope",
        detail: missingLedgerBookIssue?.message ??
          (selectedLedgerBook
            ? `${selectedLedgerBook.accountingBasis} basis; policy ${selectedLedgerBook.accountingPolicyId}/${selectedLedgerBook.accountingPolicyVersion}.`
            : workspace?.ledgerBookId
              ? `Ledger book ${workspace.ledgerBookId} is selected; setup details are not loaded.`
              : "Configuration is using the fund-level setup scope."),
        tone: missingLedgerBookIssue ? "danger" : selectedLedgerBook ? "success" : workspace?.ledgerBookId ? "warning" : "default"
      },
      {
        id: "activation-readiness",
        label: "Activation readiness",
        value: criticalIssueCount > 0 ? "Blocked" : "Ready",
        detail: missingLedgerBookIssue?.suggestedAction ??
          (criticalIssueCount > 0
            ? `${criticalIssueCount} critical validation issue${criticalIssueCount === 1 ? "" : "s"} must be cleared.`
            : "No critical validation blockers are attached to this configuration."),
        tone: criticalIssueCount > 0 ? "danger" : "success"
      }
    ];
    const templates = (workspace?.journalTemplates ?? []).map<AccountingConfigurationTemplateViewModel>((template) => {
      const debitTotal = template.lines.filter((line) => line.side === "Debit").reduce((sum, line) => sum + line.amount, 0);
      const creditTotal = template.lines.filter((line) => line.side === "Credit").reduce((sum, line) => sum + line.amount, 0);
      return {
        id: template.templateId,
        title: template.displayName,
        subtitle: `${template.version} | ${template.description || "No description supplied."}`,
        lineCountLabel: `${template.lines.length} line${template.lines.length === 1 ? "" : "s"}`,
        balanceLabel: `${formatCurrency(debitTotal)} debit / ${formatCurrency(creditTotal)} credit`,
        statusLabel: template.isArchived ? "Archived" : debitTotal === creditTotal ? "Balanced" : "Unbalanced"
      };
    });
    const rules = activeRules.map<AccountingRulesStudioRuleViewModel>((rule) =>
      buildAccountingRulesStudioRuleViewModel(
        rule,
        rule.ruleId === resolvedSelectedRuleId,
        savedRuleTestCases,
        ruleTestSuite,
        studioRuleRowsById.get(rule.ruleId) ?? null,
        studioPromotionQueueById.get(rule.ruleId) ?? null));
    const dryRunPreviewView = dryRunPreview
      ? buildAccountingRulesStudioDryRunViewModel(dryRunPreview)
      : null;
    const journalCandidatePreviewView = journalCandidatePreview
      ? buildAccountingRulesStudioJournalCandidateViewModel(journalCandidatePreview)
      : null;
    const ruleTestSuiteView = ruleTestSuite
      ? buildAccountingRulesStudioTestSuiteViewModel(ruleTestSuite)
      : null;
    const ruleTestCases = savedRuleTestCases.map(buildAccountingRulesStudioTestCaseViewModel);
    const validationIssues = (workspace?.validationIssues ?? []).map<AccountingConfigurationIssueViewModel>((issue, index) => ({
      id: `${issue.code}-${issue.targetId ?? index}`,
      label: `${issue.severity} | ${issue.code}`,
      message: issue.message,
      detail: issue.suggestedAction ?? issue.targetId ?? "No additional action supplied.",
      tone: issue.severity === "Critical" ? "danger" : issue.severity === "Warning" ? "warning" : "default"
    }));
    const auditTrail = (workspace?.auditTrail ?? []).slice(0, 8).map<AccountingConfigurationAuditViewModel>((event) => ({
      id: event.auditEventId,
      title: `${event.action} by ${event.actor}`,
      subtitle: `${event.recordedAtUtc} | ${event.correlationId ?? "no correlation id"}`,
      hashLabel: `${event.beforeHash.slice(0, 8)} -> ${event.afterHash.slice(0, 8)}`
    }));
    const previewView = preview
      ? {
          title: preview.displayName,
          balanceLabel: `${formatCurrency(preview.totalDebits)} debit / ${formatCurrency(preview.totalCredits)} credit`,
          statusLabel: preview.isBalanced ? "Balanced non-posting preview" : "Unbalanced preview",
          lineRows: preview.lines.map((line, index) => ({
            id: `${line.accountPath}-${line.side}-${index}`,
            account: `${line.accountPath} | ${line.accountName}`,
            side: line.side,
            amount: `${formatCurrency(line.amount)} ${line.currency}`,
            description: line.description ?? "Template line"
          }))
        }
      : null;
    const selectedDryRunRuleVersion = dryRunPreview
      ? workspace.postingRules.find((rule) => rule.ruleId === dryRunPreview.selectedRuleId)?.ruleVersion ?? "v1"
      : null;

    return {
      title: "Configure accounting",
      description: "Set up books, chart accounts, journal templates, posting rules, validation, and audit evidence before accounting actions are activated.",
      statusLabel: workspace ? `${workspace.status} ${workspace.configurationVersion}` : "Not loaded",
      statusTone,
      loading,
      errorText: error?.summary ?? null,
      errorDetails: error?.details ?? [],
      metricRows,
      setupReadinessRows,
      templates,
      rules,
      selectedRule: rules.find((rule) => rule.id === resolvedSelectedRuleId) ?? null,
      selectedRuleId: resolvedSelectedRuleId,
      selectRule: setSelectedRuleId,
      dryRunPreview: dryRunPreviewView,
      dryRunStatusText: dryRunError?.summary ?? (dryRunPreviewView ? dryRunPreviewView.balanceLabel : null),
      dryRunButtonLabel: dryRunBusy ? "Running dry run" : "Dry-run selected rule",
      dryRunDisabledReason,
      dryRunBusy,
      canDryRun: dryRunDisabledReason === null && !dryRunBusy,
      dryRunSelectedRule,
      createLedgerBookButtonLabel: createLedgerBookBusy ? "Creating ledger book" : "Create ledger book setup",
      createLedgerBookDisabledReason,
      createLedgerBookStatusText: createLedgerBookError?.summary ?? (createdLedgerBookName ? `Created ${createdLedgerBookName}.` : workspace?.ledgerBookSetupCandidate?.suggestedAction ?? null),
      createLedgerBookBusy,
      canCreateLedgerBook: createLedgerBookDisabledReason === null && !createLedgerBookBusy,
      createLedgerBookFromSetupCandidate,
      journalCandidatePreview: journalCandidatePreviewView,
      journalCandidateStatusText: journalCandidateError?.summary ?? (journalCandidatePreviewView ? journalCandidatePreviewView.commandLabel : null),
      journalCandidateButtonLabel: journalCandidateBusy ? "Building candidate" : "Build journal candidate",
      journalCandidateDisabledReason,
      journalCandidateBusy,
      canBuildJournalCandidate: journalCandidateDisabledReason === null && !journalCandidateBusy,
      buildJournalCandidate,
      applyEventPredicateButtonLabel: applyEventPredicateBusy ? "Applying event predicate" : "Apply event predicate",
      applyEventPredicateDisabledReason,
      applyEventPredicateStatusText: applyEventPredicateError?.summary ?? (eventPredicateRuleId ? `Applied event predicate to ${eventPredicateRuleId}.` : null),
      applyEventPredicateBusy,
      canApplyEventPredicate: applyEventPredicateDisabledReason === null && !applyEventPredicateBusy,
      applyDryRunEventPredicate,
      applyThresholdButtonLabel: applyThresholdBusy ? "Applying threshold" : "Apply amount threshold",
      applyThresholdDisabledReason,
      applyThresholdStatusText: applyThresholdError?.summary ?? (thresholdRuleId ? `Applied amount threshold to ${thresholdRuleId}.` : null),
      applyThresholdBusy,
      canApplyThreshold: applyThresholdDisabledReason === null && !applyThresholdBusy,
      applyDryRunAmountThreshold,
      applyEffectiveStartButtonLabel: applyEffectiveStartBusy ? "Applying effective date" : "Set effective start",
      applyEffectiveStartDisabledReason,
      applyEffectiveStartStatusText: applyEffectiveStartError?.summary ?? (effectiveStartRuleId ? `Applied effective start to ${effectiveStartRuleId}.` : null),
      applyEffectiveStartBusy,
      canApplyEffectiveStart: applyEffectiveStartDisabledReason === null && !applyEffectiveStartBusy,
      applyDryRunEffectiveStart,
      applyScopeButtonLabel: applyScopeBusy ? "Applying scope" : "Apply dry-run scope",
      applyScopeDisabledReason,
      applyScopeStatusText: applyScopeError?.summary ?? (scopedRuleId ? `Applied dry-run scope to ${scopedRuleId}.` : null),
      applyScopeBusy,
      canApplyScope: applyScopeDisabledReason === null && !applyScopeBusy,
      applyDryRunScope,
      capturePostingsButtonLabel: capturePostingsBusy ? "Capturing postings" : "Capture generated postings",
      capturePostingsDisabledReason,
      capturePostingsStatusText: capturePostingsError?.summary ?? (capturedPostingsRuleId ? `Captured generated postings for ${capturedPostingsRuleId}.` : null),
      capturePostingsBusy,
      canCapturePostings: capturePostingsDisabledReason === null && !capturePostingsBusy,
      captureDryRunGeneratedPostings,
      applyFormulaButtonLabel: applyFormulaBusy ? "Applying formula amount" : "Apply formula amount",
      applyFormulaDisabledReason,
      applyFormulaStatusText: applyFormulaError?.summary ?? (formulaRuleId ? `Applied formula amount to ${formulaRuleId}.` : null),
      applyFormulaBusy,
      canApplyFormula: applyFormulaDisabledReason === null && !applyFormulaBusy,
      applyDryRunFormulaAmount,
      applyAllocationButtonLabel: applyAllocationBusy ? "Applying allocation targets" : "Apply allocation targets",
      applyAllocationDisabledReason,
      applyAllocationStatusText: applyAllocationError?.summary ?? (allocationRuleId ? `Applied allocation targets to ${allocationRuleId}.` : null),
      applyAllocationBusy,
      canApplyAllocation: applyAllocationDisabledReason === null && !applyAllocationBusy,
      applyDryRunAllocationTargets,
      raisePriorityButtonLabel: raisePriorityBusy ? "Raising priority" : "Raise priority",
      raisePriorityDisabledReason,
      raisePriorityStatusText: raisePriorityError?.summary ?? (raisedPriorityRuleId ? `Raised priority for ${raisedPriorityRuleId}.` : null),
      raisePriorityBusy,
      canRaisePriority: raisePriorityDisabledReason === null && !raisePriorityBusy,
      raiseSelectedRulePriority,
      ruleTestSuite: ruleTestSuiteView,
      ruleTestStatusText: ruleTestError?.summary ?? (ruleTestSuiteView ? ruleTestSuiteView.summaryLabel : null),
      ruleTestButtonLabel: ruleTestBusy ? "Running rule tests" : "Run rule tests",
      ruleTestDisabledReason,
      ruleTestBusy,
      canRunRuleTests: ruleTestDisabledReason === null && !ruleTestBusy,
      runRuleTests,
      saveDryRunAsRuleTestButtonLabel: saveRuleTestBusy ? "Saving test case" : "Save dry-run as test case",
      saveDryRunAsRuleTestDisabledReason,
      saveDryRunAsRuleTestStatusText: saveRuleTestError?.summary ?? (dryRunPreview && savedRuleTestCases.some((testCase) =>
        testCase.expectedRuleId === dryRunPreview.selectedRuleId &&
        testCase.expectedRuleVersion === selectedDryRunRuleVersion) ? "Selected dry-run has a saved regression case." : null),
      saveDryRunAsRuleTestBusy: saveRuleTestBusy,
      canSaveDryRunAsRuleTest: saveDryRunAsRuleTestDisabledReason === null && !saveRuleTestBusy,
      saveDryRunAsRuleTest,
      duplicateRuleButtonLabel: duplicateRuleBusy ? "Duplicating rule" : "Duplicate as draft",
      duplicateRuleDisabledReason,
      duplicateRuleStatusText: duplicateRuleError?.summary ?? (duplicatedRuleId ? `Draft rule ${duplicatedRuleId} saved.` : null),
      duplicateRuleBusy,
      canDuplicateRule: duplicateRuleDisabledReason === null && !duplicateRuleBusy,
      duplicateSelectedRule,
      archiveRuleButtonLabel: archiveRuleBusy ? "Archiving rule" : "Archive rule",
      archiveRuleDisabledReason,
      archiveRuleStatusText: archiveRuleError?.summary ?? (archivedRuleId ? `Archived posting rule ${archivedRuleId}.` : null),
      archiveRuleBusy,
      canArchiveRule: archiveRuleDisabledReason === null && !archiveRuleBusy,
      archiveSelectedRule,
      approveRulePromotionButtonLabel: approveRulePromotionBusy ? "Approving rule" : "Approve rule promotion",
      approveRulePromotionDisabledReason,
      approveRulePromotionStatusText: approveRulePromotionError?.summary ?? (approvedRulePromotionId && selectedPostingRule?.ruleId === approvedRulePromotionId
        ? `Approved promotion for ${selectedPostingRule.displayName}.`
        : null),
      approveRulePromotionBusy,
      canApproveRulePromotion: approveRulePromotionDisabledReason === null && !approveRulePromotionBusy,
      approveRulePromotion,
      ruleTestCases,
      validationIssues,
      auditTrail,
      preview: previewView,
      previewStatusText: previewError?.summary ?? (preview ? previewView?.statusLabel ?? null : null),
      previewButtonLabel: previewBusy ? "Previewing" : "Preview first template",
      previewDisabledReason,
      previewBusy,
      canPreview: previewDisabledReason === null && !previewBusy,
      activateButtonLabel: activateBusy ? "Activating" : "Activate configuration",
      activateDisabledReason: activateError?.summary ?? activateDisabledReason,
      activateBusy,
      canActivate: activateDisabledReason === null && !activateBusy,
      activate,
      emptyText: loading ? "Loading accounting configuration." : "No accounting configuration records are available yet.",
      refresh,
      previewFirstTemplate
    };
  }, [
    activate,
    activateBusy,
    activateError,
    applyAllocationBusy,
    applyAllocationError,
    applyDryRunEventPredicate,
    applyDryRunEffectiveStart,
    applyDryRunAllocationTargets,
    applyDryRunAmountThreshold,
    applyDryRunFormulaAmount,
    applyDryRunScope,
    applyEventPredicateBusy,
    applyEventPredicateError,
    applyEffectiveStartBusy,
    applyEffectiveStartError,
    applyFormulaBusy,
    applyFormulaError,
    applyScopeBusy,
    applyScopeError,
    applyThresholdBusy,
    applyThresholdError,
    captureDryRunGeneratedPostings,
    capturePostingsBusy,
    capturePostingsError,
    capturedPostingsRuleId,
    createLedgerBookBusy,
    createLedgerBookError,
    createLedgerBookFromSetupCandidate,
    createdLedgerBookName,
    raisePriorityBusy,
    raisePriorityError,
    raiseSelectedRulePriority,
    raisedPriorityRuleId,
    archiveRuleBusy,
    archiveRuleError,
    archiveSelectedRule,
    archivedRuleId,
    allocationRuleId,
    duplicateRuleBusy,
    duplicateRuleError,
    duplicateSelectedRule,
    duplicatedRuleId,
    approveRulePromotion,
    approveRulePromotionBusy,
    approveRulePromotionError,
    approvedRulePromotionId,
    buildJournalCandidate,
    dryRunBusy,
    dryRunError,
    dryRunPreview,
    dryRunSelectedRule,
    error,
    eventPredicateRuleId,
    effectiveStartRuleId,
    formulaRuleId,
    journalCandidateBusy,
    journalCandidateError,
    journalCandidatePreview,
    loading,
    preview,
    previewBusy,
    previewError,
    refresh,
    previewFirstTemplate,
    ruleTestBusy,
    ruleTestError,
    ruleTestSuite,
    runRuleTests,
    saveDryRunAsRuleTest,
    saveRuleTestBusy,
    saveRuleTestError,
    selectedRuleId,
    scopedRuleId,
    thresholdRuleId,
    workspace
  ]);
}

function cloneLedgerDimensionSet(dimensions: PostingRule["scope"]): PostingRule["scope"] {
  if (!dimensions) {
    return dimensions ?? null;
  }

  const cloned = { ...dimensions };
  if (dimensions.externalGlDimensions) {
    cloned.externalGlDimensions = { ...dimensions.externalGlDimensions };
  }

  return cloned;
}

type LedgerScopeScalarKey = Exclude<keyof NonNullable<PostingRule["scope"]>, "externalGlDimensions">;

const LEDGER_SCOPE_SCALAR_KEYS: LedgerScopeScalarKey[] = [
  "fundId",
  "entityId",
  "sleeveId",
  "strategyId",
  "investorId",
  "capitalAccountId",
  "instrumentId",
  "taxLotId",
  "costCenterId",
  "counterpartyId"
];

function mergeRuleScopeFromGeneratedPostings(
  scope: PostingRule["scope"],
  generatedPostings: readonly GeneratedPostingLine[]
): PostingRule["scope"] {
  const merged = cloneLedgerDimensionSet(scope) ?? {};

  for (const key of LEDGER_SCOPE_SCALAR_KEYS) {
    const existing = normalizeDimensionValue(merged[key]);
    if (existing) {
      continue;
    }

    const values = new Set(
      generatedPostings
        .map((posting) => normalizeDimensionValue(posting.dimensions?.[key]))
        .filter((value): value is string => Boolean(value))
    );
    if (values.size === 1) {
      merged[key] = [...values][0];
    }
  }

  const externalGlDimensions = { ...(merged.externalGlDimensions ?? {}) };
  const externalKeys = new Set<string>();
  for (const posting of generatedPostings) {
    for (const key of Object.keys(posting.dimensions?.externalGlDimensions ?? {})) {
      externalKeys.add(key);
    }
  }

  for (const key of externalKeys) {
    if (normalizeDimensionValue(externalGlDimensions[key])) {
      continue;
    }

    const values = new Set(
      generatedPostings
        .map((posting) => normalizeDimensionValue(posting.dimensions?.externalGlDimensions?.[key]))
        .filter((value): value is string => Boolean(value))
    );
    if (values.size === 1) {
      externalGlDimensions[key] = [...values][0];
    }
  }

  merged.externalGlDimensions = Object.keys(externalGlDimensions).length > 0 ? externalGlDimensions : null;
  return merged;
}

function ledgerDimensionSetsEqual(left: PostingRule["scope"], right: PostingRule["scope"]): boolean {
  const normalizedLeft = normalizeLedgerDimensionSet(left);
  const normalizedRight = normalizeLedgerDimensionSet(right);
  return JSON.stringify(normalizedLeft) === JSON.stringify(normalizedRight);
}

function resolveDryRunFormulaCandidate(
  rule: PostingRule,
  dryRun: RuleDryRunResult
): { formulaId: string; amount: number; currency: string } | null {
  const formulaIds = new Set((rule.formulas ?? []).map((formula) => formula.formulaId));
  for (const posting of dryRun.generatedPostingLines ?? []) {
    if (formulaIds.has(posting.amountFormulaId) && posting.amount > 0) {
      return {
        formulaId: posting.amountFormulaId,
        amount: posting.amount,
        currency: posting.currency
      };
    }
  }

  return null;
}

function mergeAllocationTargetsFromGeneratedPostings(
  allocations: readonly AllocationRule[],
  generatedPostings: readonly GeneratedPostingLine[]
): AllocationRule[] | null {
  if (allocations.length === 0 || generatedPostings.length === 0) {
    return null;
  }

  let changed = false;
  const nextAllocations = allocations.map((allocation) => {
    const matchingPostings = allocation.formulaId
      ? generatedPostings.filter((posting) => posting.amountFormulaId === allocation.formulaId)
      : generatedPostings;
    if (matchingPostings.length === 0) {
      return {
        ...allocation,
        targetDimensions: cloneLedgerDimensionSet(allocation.targetDimensions)
      };
    }

    const targetDimensions = mergeRuleScopeFromGeneratedPostings(
      allocation.targetDimensions,
      matchingPostings
    );
    if (!ledgerDimensionSetsEqual(allocation.targetDimensions, targetDimensions)) {
      changed = true;
    }

    return {
      ...allocation,
      targetDimensions
    };
  });

  return changed ? nextAllocations : null;
}

function normalizeLedgerDimensionSet(dimensions: PostingRule["scope"]): Record<string, string> {
  const normalized: Record<string, string> = {};
  if (!dimensions) {
    return normalized;
  }

  for (const key of LEDGER_SCOPE_SCALAR_KEYS) {
    const value = normalizeDimensionValue(dimensions[key]);
    if (value) {
      normalized[key] = value;
    }
  }

  for (const [key, value] of Object.entries(dimensions.externalGlDimensions ?? {}).sort(([left], [right]) => left.localeCompare(right))) {
    const normalizedValue = normalizeDimensionValue(value);
    if (normalizedValue) {
      normalized[`externalGlDimensions.${key}`] = normalizedValue;
    }
  }

  return normalized;
}

function normalizeDimensionValue(value: string | null | undefined): string | null {
  const normalized = value?.trim();
  return normalized ? normalized : null;
}

function buildAccountingRulesStudioRuleViewModel(
  rule: PostingRule,
  isSelected: boolean,
  savedRuleTestCases: AccountingRuleTestCase[],
  ruleTestSuite: AccountingRuleTestSuiteResult | null,
  studioRule: AccountingRulesStudioRuleRow | null = null,
  promotionQueueItem: AccountingRulesStudioPromotionQueueItem | null = null
): AccountingRulesStudioRuleViewModel {
  const conditions = rule.conditions ?? [];
  const conditionGroups = rule.conditionGroups ?? [];
  const formulas = rule.formulas ?? [];
  const allocations = rule.allocations ?? [];
  const generatedPostings = rule.generatedPostings ?? [];
  const versions = rule.versions ?? [];
  const promotion = resolvePostingRulePromotion(rule);
  const needsApproval = studioRule
    ? studioRule.requiresPromotionApproval && !studioRule.isPromotionApproved
    : rule.requiresPromotionApproval && promotion?.approvalState !== "Approved";
  const usesGeneratedPostings = studioRule?.usesGeneratedPostings ?? generatedPostings.length > 0;
  const statusLabel = rule.isArchived
    ? "Archived"
    : needsApproval
      ? "Promotion review"
      : usesGeneratedPostings
        ? "Generated postings"
        : "Template mapping";

  return {
    id: rule.ruleId,
    title: rule.displayName,
    subtitle: `${rule.ruleVersion} | ${rule.description || "No description supplied."}`,
    eventLabel: rule.sourceEventType,
    effectiveLabel: formatRuleEffectiveRange(rule),
    priorityLabel: `Priority ${rule.priority ?? 0}`,
    scopeLabels: formatLedgerDimensionSet(rule.scope),
    conditionRows: conditions.length > 0 || conditionGroups.length > 0
      ? [
          ...conditions.map(formatAccountingRuleCondition),
          ...conditionGroups.map(formatAccountingRuleConditionGroup)
        ]
      : ["No predicates configured; rule falls back to source-event matching."],
    formulaRows: formulas.length > 0
      ? formulas.map(formatAccountingRuleFormula)
      : ["No formulas configured; legacy template amount mapping remains active."],
    allocationRows: allocations.length > 0
      ? allocations.map(formatAllocationRule)
      : ["No allocation split configured."],
    generatedPostingRows: generatedPostings.length > 0
      ? generatedPostings.map(formatGeneratedPostingLine)
      : [`Legacy template action: ${rule.templateId || "no template"}`],
    versionRows: versions.length > 0
      ? versions.map((version) => `${version.version} by ${version.createdBy} on ${formatDateOnly(version.createdAtUtc)} - ${version.changeSummary}`)
      : [`${rule.ruleVersion} is the only retained rule version.`],
    promotionReadiness: buildAccountingRulesStudioPromotionReadiness(
      rule,
      savedRuleTestCases,
      ruleTestSuite,
      studioRule,
      promotionQueueItem),
    promotionLabel: promotion
      ? `${promotion.approvalState} by ${promotion.approvedBy ?? promotion.requestedBy}`
      : rule.requiresPromotionApproval
        ? "Promotion approval required"
        : "Promotion approval not required",
    promotionTone: studioRule?.isPromotionApproved
      ? "success"
      : promotionQueueItem
        ? "warning"
        : promotionTone(promotion, rule.requiresPromotionApproval),
    statusLabel,
    statusTone: rule.isArchived
      ? "outline"
      : (studioRule?.criticalIssueCount ?? 0) > 0
        ? "danger"
        : needsApproval
          ? "warning"
          : "success",
    isSelected,
    selectAriaLabel: `Inspect accounting posting rule ${rule.displayName}`
  };
}

function resolvePostingRulePromotion(rule: PostingRule): PostingRule["promotionApproval"] {
  return rule.promotionApproval ?? rule.versions?.find((version) => version.promotionApproval)?.promotionApproval ?? null;
}

function buildAccountingRulesStudioPromotionReadiness(
  rule: PostingRule,
  savedRuleTestCases: AccountingRuleTestCase[],
  ruleTestSuite: AccountingRuleTestSuiteResult | null,
  studioRule: AccountingRulesStudioRuleRow | null = null,
  promotionQueueItem: AccountingRulesStudioPromotionQueueItem | null = null
): AccountingRulesStudioPromotionReadinessViewModel[] {
  const versions = rule.versions ?? [];
  const promotion = resolvePostingRulePromotion(rule);
  const promotionApproved = studioRule?.isPromotionApproved ?? isApprovedRulePromotion(promotion);
  const savedCases = savedRuleTestCases.filter((testCase) =>
    testCase.expectedRuleId === rule.ruleId &&
    testCase.expectedRuleVersion === (rule.ruleVersion ?? "v1"));
  const savedCaseCount = studioRule?.savedTestCaseCount ?? savedCases.length;
  const generatedPostings = rule.generatedPostings ?? [];
  const generatedPostingLineCount = studioRule?.generatedPostingLineCount ?? generatedPostings.length;
  const scopeRows = formatLedgerDimensionSet(rule.scope);
  const latestVersion = versions[0];
  const suiteTone: AccountingToolingTone = !ruleTestSuite
    ? "warning"
    : ruleTestSuite.failedCount > 0
      ? "danger"
      : ruleTestSuite.totalCount > 0
        ? "success"
        : "warning";
  const activationBlockedReason = studioRule && studioRule.canActivate
    ? "Rule satisfies current promotion gates."
    : promotionQueueItem
      ? promotionQueueItem.suggestedAction
      : rule.isArchived
        ? "Archived rules cannot be activated."
        : rule.requiresPromotionApproval && !promotionApproved
          ? "Promotion approval is required before activation."
          : rule.requiresPromotionApproval && savedCaseCount === 0
            ? "A saved regression case is required before activation."
            : (ruleTestSuite?.failedCount ?? 0) > 0
              ? "Latest regression suite has failing cases."
              : "Rule satisfies current promotion gates.";

  return [
    {
      id: "version-history",
      label: "Version history",
      value: versions.length > 0 ? `${versions.length} retained` : "Current only",
      detail: latestVersion
        ? `${latestVersion.version} by ${latestVersion.createdBy} on ${formatDateOnly(latestVersion.createdAtUtc)}.`
        : `${rule.ruleVersion} has no prior retained versions.`,
      tone: versions.length > 0 ? "success" : "warning"
    },
    ...(studioRule ? [{
      id: "server-readiness",
      label: "Shared readiness",
      value: studioRule.canActivate ? "Ready" : studioRule.canRequestPromotion ? "Promotion ready" : "Blocked",
      detail: `${studioRule.criticalIssueCount} critical, ${studioRule.warningIssueCount} warning, ${studioRule.savedTestCaseCount} saved test case(s).`,
      tone: studioRule.criticalIssueCount > 0 ? "danger" : studioRule.canActivate ? "success" : "warning"
    } satisfies AccountingRulesStudioPromotionReadinessViewModel] : []),
    ...(promotionQueueItem ? [{
      id: "promotion-queue",
      label: "Promotion queue",
      value: promotionQueueItem.approvalState ?? "Pending",
      detail: promotionQueueItem.suggestedAction,
      tone: promotionQueueItem.criticalIssueCount > 0 ? "danger" : "warning"
    } satisfies AccountingRulesStudioPromotionReadinessViewModel] : []),
    {
      id: "promotion-approval",
      label: "Promotion approval",
      value: promotionApproved
        ? "Approved"
        : rule.requiresPromotionApproval
          ? "Required"
          : "Not required",
      detail: promotion
        ? `${promotion.approvalState} by ${promotion.approvedBy ?? promotion.requestedBy}.`
        : rule.requiresPromotionApproval
          ? "No approval evidence has been attached."
          : "This rule can remain in draft configuration without a promotion gate.",
      tone: promotionApproved ? "success" : rule.requiresPromotionApproval ? "warning" : "default"
    },
    {
      id: "saved-regression",
      label: "Saved regression",
      value: savedCaseCount > 0
        ? `${savedCaseCount} saved`
        : "Missing",
      detail: savedCaseCount > 0
        ? `${savedCases[0]?.displayName ?? "Server-retained regression coverage"} anchors promotion coverage.`
        : "Save a dry-run regression case for this rule before promotion.",
      tone: savedCaseCount > 0 ? "success" : rule.requiresPromotionApproval ? "warning" : "default"
    },
    {
      id: "latest-suite",
      label: "Latest suite",
      value: ruleTestSuite
        ? `${ruleTestSuite.passedCount}/${ruleTestSuite.totalCount} passed`
        : "Not run",
      detail: ruleTestSuite
        ? `Executed ${ruleTestSuite.executedAtUtc} by ${ruleTestSuite.actor}.`
        : "Run rule tests to verify current rules against saved and generated cases.",
      tone: suiteTone
    },
    {
      id: "generated-postings",
      label: "Generated postings",
      value: generatedPostingLineCount > 0
        ? `${generatedPostingLineCount} lines`
        : "Template only",
      detail: generatedPostingLineCount > 0
        ? "Rule can generate explicit multi-line postings."
        : `Rule still maps to template ${rule.templateId || "none"}.`,
      tone: generatedPostingLineCount > 0 ? "success" : "warning"
    },
    {
      id: "scope-coverage",
      label: "Scope coverage",
      value: scopeRows.length > 0 ? `${scopeRows.length} scoped` : "Unscoped",
      detail: scopeRows.length > 0
        ? scopeRows.slice(0, 3).join(", ")
        : "No fund, entity, instrument, counterparty, or external GL dimensions are scoped.",
      tone: scopeRows.length > 0 ? "success" : "warning"
    },
    {
      id: "activation-gate",
      label: "Activation gate",
      value: activationBlockedReason === "Rule satisfies current promotion gates." ? "Ready" : "Blocked",
      detail: activationBlockedReason,
      tone: activationBlockedReason === "Rule satisfies current promotion gates." ? "success" : "warning"
    }
  ];
}

function buildAccountingRulesStudioDryRunViewModel(result: RuleDryRunResult): AccountingRulesStudioDryRunViewModel {
  const generatedPostingLines = result.generatedPostingLines ?? [];
  return {
    title: `${result.sourceEventType} dry run`,
    balanceLabel: result.isPostingBalanced
      ? `Balanced ${formatCurrency(result.eventAmount)} ${result.currency}`
      : `Unbalanced ${formatCurrency(result.eventAmount)} ${result.currency}`,
    selectedRuleLabel: result.selectedRuleId ? `Selected rule ${result.selectedRuleId}` : "No rule selected",
    matchRows: result.ruleMatches.map(formatRuleDryRunMatch),
    generatedLineRows: result.generatedLines.map((line, index) =>
      `${index + 1}. ${line.side} ${line.accountPath} ${formatCurrency(line.amount)} ${line.currency} - ${line.description ?? line.accountName}`
    ),
    generatedPostingRows: generatedPostingLines.length > 0
      ? generatedPostingLines.map(formatGeneratedPostingLine)
      : ["Dry run returned journal preview lines without generated posting-line metadata."],
    validationRows: result.validationIssues.map<AccountingConfigurationIssueViewModel>((issue, index) => ({
      id: `${issue.code}-${issue.targetId ?? index}`,
      label: `${issue.severity} | ${issue.code}`,
      message: issue.message,
      detail: issue.suggestedAction ?? issue.targetId ?? "No additional action supplied.",
      tone: issue.severity === "Critical" ? "danger" : issue.severity === "Warning" ? "warning" : "default"
    }))
  };
}

function buildAccountingRulesStudioJournalCandidateViewModel(
  result: PostingRuleJournalCandidateResult
): AccountingRulesStudioJournalCandidateViewModel {
  const command = result.postingCommand;
  const commandLabel = command
    ? `Draft command ${command.commandId} is ${command.approvalState.toLowerCase()} and remains lifecycle-gated.`
    : "No posting command was produced.";
  const approvalLabel = command
    ? `Approval ${command.approvalState}; post-without-approval ${result.canPostWithoutAdditionalApproval ? "allowed" : "blocked"}.`
    : "Approval state unavailable.";

  return {
    title: "Governed journal draft candidate",
    selectedRuleLabel: result.selectedRuleId
      ? `Selected rule ${result.selectedRuleId}${result.selectedRuleVersion ? ` version ${result.selectedRuleVersion}` : ""}`
      : "No selected posting rule",
    balanceLabel: result.isBalanced
      ? `Balanced ${formatCurrency(result.totalDebits)} / ${formatCurrency(result.totalCredits)}`
      : `Unbalanced by ${formatCurrency(result.imbalance)}`,
    commandLabel,
    approvalLabel,
    evidenceLabel: `${result.evidenceLinks.length} evidence link${result.evidenceLinks.length === 1 ? "" : "s"}`,
    generatedLineRows: result.generatedPostingLines.length > 0
      ? result.generatedPostingLines.map(formatGeneratedPostingLine)
      : ["No generated posting lines were converted to a draft candidate."],
    issueRows: result.issues.map<AccountingConfigurationIssueViewModel>((issue, index) => ({
      id: `${issue.code}-${issue.targetId ?? index}`,
      label: `${issue.severity} | ${issue.code}${issue.blocksCandidate ? " | blocking" : ""}`,
      message: issue.message,
      detail: issue.suggestedAction ?? issue.targetId ?? "No additional action supplied.",
      tone: issue.severity === "Critical" ? "danger" : issue.severity === "Warning" ? "warning" : "default"
    }))
  };
}

function buildAccountingRulesStudioTestCaseViewModel(testCase: AccountingRuleTestCase): AccountingRulesStudioTestCaseViewModel {
  const request = testCase.request;
  const expectedRule = testCase.expectedRuleId ?? "any matching rule";
  const expectedVersion = testCase.expectedRuleVersion ? ` version ${testCase.expectedRuleVersion}` : "";
  const expectedIssueCodes = testCase.expectedIssueCodes ?? [];
  const expectedGeneratedPostings = testCase.expectedGeneratedPostingLines?.length ?? 0;
  const evidenceCount = testCase.evidenceLinks?.length ?? 0;
  const expectedIssues = expectedIssueCodes.length > 0
    ? `${expectedIssueCodes.length} expected issue code${expectedIssueCodes.length === 1 ? "" : "s"}`
    : "no expected issue codes";
  const expectedPostings = expectedGeneratedPostings > 0
    ? `${expectedGeneratedPostings} expected generated posting line${expectedGeneratedPostings === 1 ? "" : "s"}`
    : "no expected generated posting lines";
  return {
    id: testCase.testCaseId,
    title: testCase.displayName,
    subtitle: `${request.sourceEventType} | ${request.effectiveDate} | ${formatCurrency(request.eventAmount)} ${request.currency}`,
    assertionLabel: `Expect ${expectedRule}${expectedVersion}, ${testCase.expectBalancedPosting ? "balanced" : "unbalanced"}, ${expectedIssues}, ${expectedPostings}.`,
    evidenceLabel: evidenceCount > 0
      ? `${evidenceCount} evidence link${evidenceCount === 1 ? "" : "s"}`
      : "Evidence required",
    evidenceTone: evidenceCount > 0 ? "success" : "warning"
  };
}

function isApprovedRulePromotion(approval: PostingRule["promotionApproval"]): boolean {
  return approval?.approvalState === "Approved" &&
    Boolean(approval.approvedBy) &&
    Boolean(approval.approvedAtUtc) &&
    (approval.evidenceLinks?.length ?? 0) > 0;
}

function buildAccountingRulesStudioTestSuiteViewModel(result: AccountingRuleTestSuiteResult): AccountingRulesStudioTestSuiteViewModel {
  const validationRows = result.results.flatMap((testCase) =>
    testCase.assertionIssues.map<AccountingConfigurationIssueViewModel>((issue, index) => ({
      id: `${testCase.testCaseId}-${issue.code}-${issue.targetId ?? index}`,
      label: `${testCase.displayName} | ${issue.severity} | ${issue.code}`,
      message: issue.message,
      detail: issue.suggestedAction ?? issue.targetId ?? "No additional action supplied.",
      tone: issue.severity === "Critical" ? "danger" : issue.severity === "Warning" ? "warning" : "default"
    }))
  );
  const statusTone: AccountingRulesStudioTestSuiteViewModel["statusTone"] = result.failedCount > 0
    ? "danger"
    : result.passedCount === result.totalCount
      ? "success"
      : "warning";

  return {
    title: "Accounting rule regression tests",
    summaryLabel: `${result.passedCount}/${result.totalCount} passed`,
    executedLabel: `${result.executedAtUtc} by ${result.actor}`,
    statusTone,
    resultRows: result.results.map((testCase) => {
      const selected = testCase.dryRunResult.selectedRuleId ?? "none";
      const balance = testCase.dryRunResult.isPostingBalanced ? "balanced" : "unbalanced";
      const issueCount = testCase.assertionIssues.length;
      return `${testCase.passed ? "Pass" : "Fail"}: ${testCase.displayName} selected ${selected}, ${balance}${issueCount > 0 ? `, ${issueCount} assertion issue(s)` : ""}.`;
    }),
    validationRows
  };
}

function buildAccountingRuleTestCase(
  workspace: AccountingConfigurationWorkspace,
  rule: PostingRule
): AccountingRuleTestCase {
  return {
    testCaseId: `rule-test-${rule.ruleId}`,
    displayName: `${rule.displayName} regression`,
    request: {
      fundProfileId: workspace.fundProfileId,
      ledgerBookId: workspace.ledgerBookId ?? null,
      sourceEventType: rule.sourceEventType,
      eventAmount: resolveDryRunEventAmount(rule),
      currency: resolveDryRunCurrency(rule),
      effectiveDate: resolveDryRunEffectiveDate(rule),
      actor: "browser-accounting-operator",
      dimensions: rule.scope ?? null,
      counterpartyId: rule.scope?.counterpartyId ?? null,
      instrumentSymbol: null,
      correlationId: `browser-accounting-rule-test-${rule.ruleId}`
    },
    expectedRuleId: rule.ruleId,
    expectedRuleVersion: rule.ruleVersion ?? "v1",
    expectBalancedPosting: true,
    expectedIssueCodes: []
  };
}

function buildBrowserGuid(timestamp: number, discriminator: number): string {
  const suffix = Math.abs(timestamp + discriminator).toString(16).padStart(12, "0").slice(-12);
  const group = discriminator.toString(16).padStart(4, "0").slice(-4);
  return `00000000-0000-4000-8000-${suffix.slice(0, 8)}${group}`;
}

function resolveRuleAccountingBasis(workspace: AccountingConfigurationWorkspace): AccountingBasisKind {
  return workspace.ledgerBooks.find((book) => book.ledgerBookId === workspace.ledgerBookId)?.accountingBasis ??
    workspace.ledgerBooks[0]?.accountingBasis ??
    "Primary";
}

function resolveRulePolicyId(workspace: AccountingConfigurationWorkspace): string | null {
  return workspace.ledgerBooks.find((book) => book.ledgerBookId === workspace.ledgerBookId)?.accountingPolicyId ??
    workspace.ledgerBooks[0]?.accountingPolicyId ??
    null;
}

function formatAccountingRuleCondition(condition: NonNullable<PostingRule["conditions"]>[number]): string {
  const value = condition.secondValue
    ? `${condition.value ?? "missing"} -> ${condition.secondValue}`
    : condition.value ?? "present";
  const required = condition.isRequired ? "required" : "optional";
  return `${condition.field} ${condition.operator} ${value} (${required})${condition.description ? ` - ${condition.description}` : ""}`;
}

function formatAccountingRuleConditionGroup(group: NonNullable<PostingRule["conditionGroups"]>[number]): string {
  const required = group.isRequired ? "required" : "optional";
  const conditions = group.conditions.length > 0
    ? group.conditions.map(formatAccountingRuleCondition).join("; ")
    : "no child predicates";
  return `${group.groupId}: ${group.operator} (${required}) -> ${conditions}${group.description ? ` - ${group.description}` : ""}`;
}

function formatAccountingRuleFormula(formula: NonNullable<PostingRule["formulas"]>[number]): string {
  return `${formula.formulaId}: ${formula.kind} ${formatCurrency(formula.value)} ${formula.currency}${formula.description ? ` - ${formula.description}` : ""}`;
}

function formatAllocationRule(allocation: AllocationRule): string {
  const scope = formatLedgerDimensionSet(allocation.targetDimensions).join(", ");
  return `${allocation.allocationRuleId}: ${allocation.basis} weight ${allocation.weight}${allocation.formulaId ? ` via ${allocation.formulaId}` : ""}${scope ? ` -> ${scope}` : ""}`;
}

function formatGeneratedPostingLine(line: GeneratedPostingLine): string {
  const scope = formatLedgerDimensionSet(line.dimensions).join(", ");
  return `${line.side} ${line.accountPath} ${formatCurrency(line.amount)} ${line.currency} via ${line.amountFormulaId}${scope ? ` | ${scope}` : ""}${line.description ? ` - ${line.description}` : ""}`;
}

function formatRuleDryRunMatch(match: AccountingRuleDryRunMatch): string {
  const state = match.isMatched ? "matched" : "skipped";
  const reasons = match.explanations.length > 0 ? match.explanations.join("; ") : "No explanation returned.";
  const issues = match.validationIssues.length > 0 ? ` Issues: ${match.validationIssues.map((issue) => issue.code).join(", ")}.` : "";
  return `${match.displayName} ${state} at priority ${match.priority}. ${reasons}${issues}`;
}

function formatRuleEffectiveRange(rule: PostingRule): string {
  const start = rule.effectiveFrom ?? "open start";
  const end = rule.effectiveTo ?? "open end";
  return `${start} -> ${end}`;
}

function formatLedgerDimensionSet(dimensions?: PostingRule["scope"]): string[] {
  if (!dimensions) {
    return [];
  }

  const rows: string[] = [];
  const scalarEntries: Array<[string, string | null | undefined]> = [
    ["Fund", dimensions.fundId],
    ["Entity", dimensions.entityId],
    ["Sleeve", dimensions.sleeveId],
    ["Strategy", dimensions.strategyId],
    ["Investor", dimensions.investorId],
    ["Capital account", dimensions.capitalAccountId],
    ["Instrument", dimensions.instrumentId],
    ["Tax lot", dimensions.taxLotId],
    ["Cost center", dimensions.costCenterId],
    ["Counterparty", dimensions.counterpartyId]
  ];
  for (const [label, value] of scalarEntries) {
    if (value) {
      rows.push(`${label}: ${value}`);
    }
  }

  for (const [key, value] of Object.entries(dimensions.externalGlDimensions ?? {})) {
    rows.push(`External ${key}: ${value}`);
  }

  return rows;
}

function promotionTone(
  promotion: PostingRule["promotionApproval"],
  requiresPromotionApproval?: boolean
): AccountingRulesStudioRuleViewModel["promotionTone"] {
  if (promotion?.approvalState === "Approved") {
    return "success";
  }

  if (promotion?.approvalState === "Rejected") {
    return "danger";
  }

  if (requiresPromotionApproval || promotion) {
    return "warning";
  }

  return "outline";
}

function resolveDryRunEventAmount(rule: PostingRule): number {
  const sourceAmountFormula = rule.formulas?.find((formula) => formula.kind === "SourceAmount");
  if (sourceAmountFormula && sourceAmountFormula.value > 0) {
    return sourceAmountFormula.value;
  }

  const generatedAmount = rule.generatedPostings?.find((line) => line.amount > 0)?.amount;
  return generatedAmount && generatedAmount > 0 ? generatedAmount : 1000;
}

function resolveDryRunCurrency(rule: PostingRule): string {
  return rule.generatedPostings?.find((line) => line.currency)?.currency
    ?? rule.formulas?.find((formula) => formula.currency)?.currency
    ?? "USD";
}

function resolveDryRunEffectiveDate(rule: PostingRule): string {
  if (rule.effectiveFrom) {
    return rule.effectiveFrom;
  }

  return new Date().toISOString().slice(0, 10);
}

function formatDateOnly(value: string): string {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toISOString().slice(0, 10);
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
      const updated = await services.resolveConflict({ conflictId, resolution, resolvedBy: "operator" });
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

export function useManualJournalEntryWorkbenchViewModel(
  active: boolean,
  services: ManualJournalEntryWorkbenchServices = defaultManualJournalEntryWorkbenchServices
): ManualJournalEntryWorkbenchViewModel {
  const [workbench, setWorkbench] = useState<ManualJournalEntryWorkbench | null>(null);
  const [draft, setDraft] = useState<ManualJournalEntryDraft>(() => createManualJournalEntryDraft());
  const [selectedLineId, setSelectedLineId] = useState(draft.lines[0]?.lineId ?? "line-1");
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<ApiErrorDisplay | null>(null);
  const [saveBusy, setSaveBusy] = useState(false);
  const [validateBusy, setValidateBusy] = useState(false);
  const [submitBusy, setSubmitBusy] = useState(false);
  const [attachEvidenceBusy, setAttachEvidenceBusy] = useState(false);
  const [attachEvidenceStatusText, setAttachEvidenceStatusText] = useState<string | null>(null);
  const [securitySearchQuery, setSecuritySearchQuery] = useState("");
  const [securitySearchResults, setSecuritySearchResults] = useState<SecurityMasterEntry[]>([]);
  const [securitySearchBusy, setSecuritySearchBusy] = useState(false);
  const [securitySearchError, setSecuritySearchError] = useState<ApiErrorDisplay | null>(null);
  const [attachmentDraft, setAttachmentDraft] = useState<ManualJournalEvidenceAttachmentDraft>(() => createManualJournalAttachmentDraft());
  const [lifecycleBusyAction, setLifecycleBusyAction] = useState<JournalEntryLifecycleAction | null>(null);
  const [lifecycleStatusText, setLifecycleStatusText] = useState<string | null>(null);
  const [lifecycleCorrectionDrafts, setLifecycleCorrectionDrafts] = useState<ManualJournalEntryDraft[]>([]);

  const refresh = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const next = await services.getWorkbench();
      setWorkbench(next);
      const selected = next.drafts[0] ?? createManualJournalEntryDraft(next.fundProfileId, next.ledgerBookId ?? null);
      setDraft(selected);
      setSelectedLineId(selected.lines[0]?.lineId ?? "line-1");
    } catch (err) {
      setError(describeApiError(err, "Manual journal entry workbench could not load."));
    } finally {
      setLoading(false);
    }
  }, [services]);

  useEffect(() => {
    if (!active) {
      return;
    }

    void refresh();
  }, [active, refresh]);

  const accountOptions = useMemo(
    () => (workbench?.chartOfAccounts ?? [])
      .filter((account) => !account.isArchived)
      .map((account) => ({
        value: account.path,
        label: `${account.path} - ${account.accountName}`
      })),
    [workbench]
  );

  const applyServerDraft = useCallback((next: ManualJournalEntryDraft) => {
    setDraft(next);
    setSelectedLineId(next.lines[0]?.lineId ?? selectedLineId);
    setWorkbench((current) => current
      ? {
        ...current,
        drafts: [next, ...current.drafts.filter((item) => item.journalEntryId !== next.journalEntryId)]
      }
      : current);
  }, [selectedLineId]);

  const applyLifecycleResult = useCallback((result: JournalEntryLifecycleActionResult) => {
    const generated = result.generatedJournalEntries ?? [];
    setDraft(result.journalEntry);
    setSelectedLineId(result.journalEntry.lines[0]?.lineId ?? selectedLineId);
    setLifecycleCorrectionDrafts(generated);
    setLifecycleStatusText(`${result.transition.action} recorded: ${result.transition.fromStatus} -> ${result.transition.toStatus}`);
    setWorkbench((current) => {
      if (!current) {
        return current;
      }

      const nextDrafts = [result.journalEntry, ...generated];
      const nextIds = new Set(nextDrafts.map((item) => item.journalEntryId));
      return {
        ...current,
        drafts: [...nextDrafts, ...current.drafts.filter((item) => !nextIds.has(item.journalEntryId))]
      };
    });
  }, [selectedLineId]);

  const updateHeader = useCallback<ManualJournalEntryWorkbenchViewModel["updateHeader"]>((field, value) => {
    setDraft((current) => ({ ...current, [field]: value }));
  }, []);

  const updateLine = useCallback<ManualJournalEntryWorkbenchViewModel["updateLine"]>((lineId, patch) => {
    setDraft((current) => ({
      ...withManualJournalTotals({
        ...current,
        lines: current.lines.map((line) => line.lineId === lineId ? { ...line, ...patch } : line)
      })
    }));
  }, []);

  const searchSecurityMaster = useCallback(async () => {
    const query = securitySearchQuery.trim();
    if (query.length < 2) {
      setSecuritySearchResults([]);
      setSecuritySearchError(null);
      return;
    }

    setSecuritySearchBusy(true);
    setSecuritySearchError(null);
    try {
      setSecuritySearchResults(await services.searchSecurities(query));
    } catch (err) {
      setSecuritySearchError(describeApiError(err, "Security Master search failed."));
      setSecuritySearchResults([]);
    } finally {
      setSecuritySearchBusy(false);
    }
  }, [securitySearchQuery, services]);

  const selectSecurity = useCallback<ManualJournalEntryWorkbenchViewModel["selectSecurity"]>((lineId, security) => {
    updateLine(lineId, {
      securityId: security.securityId,
      securityDisplayName: security.displayName
    });
    setSecuritySearchQuery(security.displayName);
    setSecuritySearchResults([]);
  }, [updateLine]);

  const clearSecurity = useCallback<ManualJournalEntryWorkbenchViewModel["clearSecurity"]>((lineId) => {
    updateLine(lineId, {
      securityId: null,
      securityDisplayName: null
    });
  }, [updateLine]);

  const addLine = useCallback((side: AccountingTemplateLineSide) => {
    const line = createManualJournalEntryLine(side, draft.currency, accountOptions[0]?.value ?? "");
    setDraft((current) => withManualJournalTotals({ ...current, lines: [...current.lines, line] }));
    setSelectedLineId(line.lineId);
  }, [accountOptions, draft.currency]);

  const removeLine = useCallback<ManualJournalEntryWorkbenchViewModel["removeLine"]>((lineId) => {
    setDraft((current) => {
      if (current.lines.length <= 2) {
        return current;
      }

      const nextLines = current.lines.filter((line) => line.lineId !== lineId);
      if (selectedLineId === lineId) {
        setSelectedLineId(nextLines[0]?.lineId ?? "line-1");
      }

      return withManualJournalTotals({ ...current, lines: nextLines });
    });
  }, [selectedLineId]);

  const updateAttachmentDraft = useCallback<ManualJournalEntryWorkbenchViewModel["updateAttachmentDraft"]>((patch) => {
    setAttachmentDraft((current) => ({ ...current, ...patch }));
  }, []);

  const addAttachment = useCallback(async () => {
    const displayName = attachmentDraft.displayName.trim();
    const uri = attachmentDraft.uri.trim();
    if (!displayName || !uri) {
      return;
    }

    const draftForAttachment = withManualJournalTotals(draft);
    const attachment: ManualJournalEntryEvidenceAttachment = {
      attachmentId: newClientId(),
      displayName,
      uri,
      evidenceKind: attachmentDraft.evidenceKind.trim() || "SourceDocument",
      sourceSystem: attachmentDraft.sourceSystem.trim() || "ManualUpload",
      addedAtUtc: new Date().toISOString(),
      addedBy: "browser-user",
      lineId: attachmentDraft.lineId,
      description: attachmentDraft.description.trim() || null
    };
    setAttachEvidenceBusy(true);
    setAttachEvidenceStatusText(null);
    setError(null);
    try {
      applyServerDraft(await services.attachEvidence({
        journalEntryId: draftForAttachment.journalEntryId,
        fundProfileId: draftForAttachment.fundProfileId,
        actor: "browser-user",
        version: draftForAttachment.version,
        attachment,
        correlationId: `manual-je-attach-evidence-${attachment.attachmentId}`,
        evidenceLinks: [uri],
        actionOrigin: "HumanOperator",
        periodIsLocked: draftForAttachment.status === "CloseLocked",
        ledgerBookId: draftForAttachment.ledgerBookId ?? null
      }));
      setAttachmentDraft(createManualJournalAttachmentDraft());
      setAttachEvidenceStatusText(`Evidence attached: ${displayName}.`);
    } catch (err) {
      const errorDisplay = describeApiError(err, "Manual journal evidence could not be attached.");
      setError(errorDisplay);
      setAttachEvidenceStatusText(errorDisplay.summary);
    } finally {
      setAttachEvidenceBusy(false);
    }
  }, [applyServerDraft, attachmentDraft, draft, services]);

  const removeAttachment = useCallback<ManualJournalEntryWorkbenchViewModel["removeAttachment"]>((attachmentId) => {
    setDraft((current) => {
      const nextAttachments = (current.evidenceAttachments ?? []).filter((item) => item.attachmentId !== attachmentId);
      const retainedUris = new Set(nextAttachments.map((item) => item.uri));
      return {
        ...current,
        evidenceAttachments: nextAttachments,
        evidenceLinks: current.evidenceLinks.filter((link) => retainedUris.has(link) || !(current.evidenceAttachments ?? []).some((item) => item.uri === link))
      };
    });
  }, []);

  const selectDraft = useCallback((journalEntryId: string) => {
    const selected = workbench?.drafts.find((item) => item.journalEntryId === journalEntryId);
    if (!selected) {
      return;
    }

    setDraft(selected);
    setSelectedLineId(selected.lines[0]?.lineId ?? selectedLineId);
  }, [selectedLineId, workbench?.drafts]);

  const validate = useCallback(async () => {
    setValidateBusy(true);
    setError(null);
    const draftForValidation = withManualJournalTotals(draft);
    try {
      applyServerDraft(await services.validateDraft({
        draft: draftForValidation,
        actor: "browser-user",
        correlationId: "manual-je-validate",
        ledgerBookId: draftForValidation.ledgerBookId ?? null
      }));
    } catch (err) {
      setError(describeApiError(err, "Manual journal entry validation failed."));
    } finally {
      setValidateBusy(false);
    }
  }, [applyServerDraft, draft, services]);

  const save = useCallback(async () => {
    setSaveBusy(true);
    setError(null);
    const draftForSave = withManualJournalTotals(draft);
    try {
      applyServerDraft(await services.saveDraft({
        draft: draftForSave,
        actor: "browser-user",
        correlationId: "manual-je-save",
        ledgerBookId: draftForSave.ledgerBookId ?? null
      }));
    } catch (err) {
      setError(describeApiError(err, "Manual journal entry draft could not be saved."));
    } finally {
      setSaveBusy(false);
    }
  }, [applyServerDraft, draft, services]);

  const submit = useCallback(async () => {
    setSubmitBusy(true);
    setError(null);
    const draftForSubmit = withManualJournalTotals(draft);
    try {
      applyServerDraft(await services.submitApproval({
        journalEntryId: draftForSubmit.journalEntryId,
        fundProfileId: draftForSubmit.fundProfileId,
        actor: "browser-user",
        version: draftForSubmit.version,
        correlationId: "manual-je-submit",
        ledgerBookId: draftForSubmit.ledgerBookId ?? null
      }));
    } catch (err) {
      setError(describeApiError(err, "Manual journal entry could not be submitted for approval."));
    } finally {
      setSubmitBusy(false);
    }
  }, [applyServerDraft, draft, services]);

  const applyLifecycleAction = useCallback<ManualJournalEntryWorkbenchViewModel["applyLifecycleAction"]>(async (action) => {
    if (lifecycleBusyAction) {
      return;
    }

    const draftForAction = withManualJournalTotals(draft);
    setLifecycleBusyAction(action);
    setLifecycleStatusText(null);
    setError(null);
    try {
      applyLifecycleResult(await services.applyLifecycleAction({
        journalEntryId: draftForAction.journalEntryId,
        fundProfileId: draftForAction.fundProfileId,
        action,
        actor: "browser-user",
        version: draftForAction.version,
        notes: lifecycleActionNotes(action, draftForAction),
        correlationId: `manual-je-${action.toLowerCase()}`,
        evidenceLinks: draftForAction.evidenceLinks ?? [],
        actionOrigin: "HumanOperator",
        periodIsLocked: action === "LockAfterClose",
        rebookLines: action === "Rebook" ? draftForAction.lines : [],
        ledgerBookId: draftForAction.ledgerBookId ?? null
      }));
    } catch (err) {
      const errorDisplay = describeApiError(err, `Manual journal entry lifecycle action ${action} failed.`);
      setError(errorDisplay);
      setLifecycleStatusText(errorDisplay.summary);
    } finally {
      setLifecycleBusyAction(null);
    }
  }, [applyLifecycleResult, draft, lifecycleBusyAction, services]);

  const validationIssues = draft.validationIssues.map<AccountingConfigurationIssueViewModel>((issue, index) => ({
    id: `${issue.code}-${index}`,
    label: issue.code,
    message: issue.message,
    detail: issue.suggestedAction ?? issue.targetId ?? "Review the journal entry.",
    tone: issue.severity === "Critical" ? "danger" : issue.severity === "Warning" ? "warning" : "default"
  }));
  const getLineBadges = useCallback<ManualJournalEntryWorkbenchViewModel["getLineBadges"]>((lineId) => {
    const line = draft.lines.find((item) => item.lineId === lineId);
    const serverIssues = draft.validationIssues
      .filter((issue) => issue.targetId === lineId)
      .map<ManualJournalLineValidationBadge>((issue) => ({
        id: `${lineId}-${issue.code}`,
        label: issue.severity === "Critical" ? "Blocked" : issue.severity,
        message: issue.message,
        tone: issue.severity === "Critical" ? "danger" : issue.severity === "Warning" ? "warning" : "default"
      }));
    if (!line) {
      return serverIssues;
    }

    const localBadges: ManualJournalLineValidationBadge[] = [];
    if (!line.accountPath) {
      localBadges.push({ id: `${lineId}-account-local`, label: "GL missing", message: "Select a GL account.", tone: "warning" });
    }
    if (line.amount <= 0) {
      localBadges.push({ id: `${lineId}-amount-local`, label: "Amount", message: "Enter a positive amount.", tone: "warning" });
    }
    if (line.securityId) {
      localBadges.push({ id: `${lineId}-security-local`, label: "Security", message: line.securityDisplayName ?? line.securityId, tone: "success" });
    }
    if ((draft.evidenceAttachments ?? []).some((item) => item.lineId === lineId) || line.evidenceLink) {
      localBadges.push({ id: `${lineId}-evidence-local`, label: "Evidence", message: "Line support is linked.", tone: "success" });
    }

    return [...serverIssues, ...localBadges];
  }, [draft.evidenceAttachments, draft.lines, draft.validationIssues]);
  const balancedDraft = withManualJournalTotals(draft);
  const hasEvidence = (balancedDraft.evidenceLinks?.length ?? 0) > 0 || (balancedDraft.evidenceAttachments?.length ?? 0) > 0;
  const canSubmit = balancedDraft.validationIssues.every((issue) => issue.severity !== "Critical") && Math.abs(balancedDraft.imbalance) === 0 && balancedDraft.lines.length >= 2 && hasEvidence;
  const balanceStatusLabel = Math.abs(balancedDraft.imbalance) === 0 ? "Balanced" : "Out by " + formatCurrency(Math.abs(balancedDraft.imbalance));
  const treasuryContextLabel = formatManualJournalTreasuryContext(draft);
  const privateCapitalActivity = useMemo(
    () => buildManualJournalPrivateCapitalActivityView(workbench?.privateCapitalActivity ?? null),
    [workbench?.privateCapitalActivity]
  );
  const lifecycleCommands = buildManualJournalLifecycleCommands(balancedDraft, lifecycleBusyAction);
  const lifecycleTransitions = (balancedDraft.lifecycleTransitions ?? []).slice().reverse().map(formatManualJournalLifecycleTransition);
  const lifecycleCorrectionRows = lifecycleCorrectionDrafts.map(formatManualJournalLifecycleCorrection);
  const lifecycleChecklist = buildManualJournalLifecycleChecklist(
    balancedDraft,
    lifecycleCommands,
    lifecycleTransitions,
    lifecycleCorrectionRows
  );
  const securitySearchStatusText = securitySearchBusy
    ? "Searching Security Master."
    : securitySearchError?.summary ?? (securitySearchResults.length > 0
      ? `${securitySearchResults.length} Security Master matches.`
      : securitySearchQuery.trim().length >= 2
        ? "No Security Master matches loaded."
        : "Enter at least two characters to search Security Master.");

  return {
    title: "Manual journal entry workbench",
    description: "Author controller-owned journal entries with GL account picks, line-level Security Master attribution, balancing validation, draft save, and approval submission.",
    loading,
    errorText: error?.summary ?? null,
    statusLabel: `${draft.status} v${draft.version}`,
    draft: balancedDraft,
    drafts: workbench?.drafts ?? [],
    accountOptions,
    selectedLineId,
    securitySearchQuery,
    securitySearchResults,
    securitySearchBusy,
    securitySearchErrorText: securitySearchError?.summary ?? null,
    securitySearchStatusText,
    attachmentDraft,
    totalsLabel: `Debits ${formatCurrency(balancedDraft.totalDebits)} / Credits ${formatCurrency(balancedDraft.totalCredits)}`,
    totalDebitsLabel: formatCurrency(balancedDraft.totalDebits),
    totalCreditsLabel: formatCurrency(balancedDraft.totalCredits),
    imbalanceLabel: `Imbalance ${formatCurrency(balancedDraft.imbalance)}`,
    balanceStatusLabel,
    balanceStatusTone: Math.abs(balancedDraft.imbalance) === 0 ? "success" : "warning",
    treasuryContextLabel,
    privateCapitalActivity,
    validationIssues,
    lifecycleCommands,
    lifecycleChecklist,
    lifecycleTransitions,
    lifecycleCorrectionRows,
    lifecycleStatusText,
    lifecycleBusyAction,
    saveBusy,
    validateBusy,
    submitBusy,
    attachEvidenceBusy,
    attachEvidenceStatusText,
    canSubmit,
    submitDisabledReason: canSubmit ? null : "Resolve critical validation issues, balance debits to credits, and attach source evidence before approval submission.",
    refresh,
    updateHeader,
    selectDraft,
    selectLine: setSelectedLineId,
    updateLine,
    getLineBadges,
    updateSecuritySearchQuery: setSecuritySearchQuery,
    searchSecurityMaster,
    selectSecurity,
    clearSecurity,
    addLine,
    removeLine,
    updateAttachmentDraft,
    addAttachment,
    removeAttachment,
    applyLifecycleAction,
    save,
    validate,
    submit
  };
}

export function useCapitalAccountWorkbenchViewModel(
  active: boolean,
  search = "",
  services: CapitalAccountWorkbenchServices = defaultCapitalAccountWorkbenchServices
): CapitalAccountWorkbenchViewModel {
  const [workbench, setWorkbench] = useState<CapitalAccountWorkbench | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<ApiErrorDisplay | null>(null);
  const query = useMemo(() => parseCapitalAccountWorkbenchQuery(search), [search]);

  const refresh = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      setWorkbench(await services.getWorkbench(query));
    } catch (err) {
      setError(describeApiError(err, "Capital Account Workbench could not load."));
      setWorkbench(null);
    } finally {
      setLoading(false);
    }
  }, [query, services]);

  useEffect(() => {
    if (!active) {
      return;
    }

    void refresh();
  }, [active, refresh]);

  return useMemo(() => ({
    ...buildCapitalAccountWorkbenchView(workbench),
    loading,
    errorText: error?.summary ?? null,
    refresh
  }), [error?.summary, loading, refresh, workbench]);
}

function parseCapitalAccountWorkbenchQuery(search: string): CapitalAccountWorkbenchQuery {
  const params = new URLSearchParams(search.startsWith("?") ? search.slice(1) : search);
  return {
    fundProfileId: normalizeQueryValue(params.get("fundProfileId")),
    ledgerBookId: normalizeQueryValue(params.get("ledgerBookId")),
    fundEventId: normalizeQueryValue(params.get("fundEventId")),
    capitalAccountId: normalizeQueryValue(params.get("capitalAccountId")),
    investorId: normalizeQueryValue(params.get("investorId")),
    currency: normalizeQueryValue(params.get("currency"))
  };
}

function buildCapitalAccountWorkbenchView(workbench: CapitalAccountWorkbench | null): Omit<CapitalAccountWorkbenchViewModel, "loading" | "errorText" | "refresh"> {
  if (!workbench) {
    return {
      title: "Capital Account Workbench",
      description: "Investor-level capital account evidence, allocation rules, statement lineage, and audit drill-throughs load from the shared private-capital workbench endpoint.",
      statusLabel: "Not loaded",
      statusTone: "default",
      statusReason: "The shared capital-account workbench endpoint has not returned a projection yet.",
      projectedAtLabel: "Not loaded",
      workbenchRouteLabel: "No workbench route",
      emptyText: "No capital-account workbench projection has loaded yet.",
      summaryCards: [
        { id: "investor-accounts", label: "Investor accounts", value: "0", detail: "No investor account rows loaded", tone: "default" },
        { id: "allocation-rules", label: "Allocation rules", value: "0", detail: "No allocation evidence checks loaded", tone: "default" },
        { id: "statements", label: "Statements", value: "0", detail: "No statement lineage loaded", tone: "default" },
        { id: "audit-drill-throughs", label: "Audit drill-throughs", value: "0", detail: "No drill-through targets loaded", tone: "default" }
      ],
      investorAccounts: [],
      allocationRules: [],
      statementLineage: [],
      auditDrillThroughs: [],
      fundEventCommandRows: [],
      validationIssues: [],
      liveCapabilities: [],
      plannedCapabilities: []
    };
  }

  return {
    title: "Capital Account Workbench",
    description: "Investor-level capital account evidence, allocation rules, statement and restatement lineage, and audit drill-throughs from the shared private-capital workbench endpoint.",
    statusLabel: workbench.statusLabel,
    statusTone: capitalAccountWorkbenchStatusTone(workbench.statusLabel),
    statusReason: workbench.statusReason,
    projectedAtLabel: `${formatDateTimeLabel(workbench.projectedAtUtc)} / ${workbench.currency}`,
    workbenchRouteLabel: workbench.workbenchRoute,
    emptyText: "No investor-level capital accounts matched the selected private-capital filters.",
    summaryCards: [
      {
        id: "investor-accounts",
        label: "Investor accounts",
        value: workbench.investorAccountCount.toLocaleString(),
        detail: `${formatCurrencyWithCode(workbench.netCapitalActivity, workbench.currency, true)} net activity`,
        tone: workbench.investorAccountCount > 0 ? "success" : "warning"
      },
      {
        id: "allocation-rules",
        label: "Allocation rules",
        value: workbench.allocationRules.length.toLocaleString(),
        detail: `${workbench.allocationRules.filter((item) => item.isSatisfied).length.toLocaleString()} satisfied`,
        tone: workbench.allocationRules.every((item) => item.isSatisfied) ? "success" : "warning"
      },
      {
        id: "statements",
        label: "Statements",
        value: workbench.statementCount.toLocaleString(),
        detail: `${workbench.restatementLineageCount.toLocaleString()} restatement lineage`,
        tone: workbench.statementCount > 0 ? "success" : "warning"
      },
      {
        id: "audit-drill-throughs",
        label: "Audit drill-throughs",
        value: workbench.auditDrillThroughCount.toLocaleString(),
        detail: `${workbench.auditDrillThroughs.filter((item) => item.isAvailable).length.toLocaleString()} available`,
        tone: workbench.auditDrillThroughs.every((item) => item.isAvailable) ? "success" : "warning"
      }
    ],
    investorAccounts: workbench.investorAccounts.map(buildCapitalAccountInvestorAccountRow),
    allocationRules: workbench.allocationRules.map(buildCapitalAccountAllocationRuleRow),
    statementLineage: workbench.statementLineage.map(buildCapitalAccountStatementLineageRow),
    auditDrillThroughs: workbench.auditDrillThroughs.map(buildCapitalAccountAuditDrillThroughRow),
    fundEventCommandRows: buildCapitalAccountFundEventCommandRows(workbench),
    validationIssues: workbench.validationIssues.map<AccountingConfigurationIssueViewModel>((issue, index) => ({
      id: `${issue.code}-${index}`,
      label: issue.code,
      message: issue.message,
      detail: issue.suggestedAction ?? issue.targetId ?? "Review the capital-account workbench.",
      tone: issue.severity === "Critical" ? "danger" : issue.severity === "Warning" ? "warning" : "default"
    })),
    liveCapabilities: workbench.liveCapabilities,
    plannedCapabilities: workbench.plannedCapabilities
  };
}

function buildCapitalAccountFundEventCommandRows(
  workbench: CapitalAccountWorkbench
): CapitalAccountWorkbenchFundEventCommandRowViewModel[] {
  const seen = new Set<string>();
  const rows: CapitalAccountWorkbenchFundEventCommandRowViewModel[] = [];

  for (const account of workbench.investorAccounts) {
    for (const record of account.fundEventRecords) {
      if (seen.has(record.fundEventId)) {
        continue;
      }

      seen.add(record.fundEventId);
      rows.push(buildManualJournalPrivateCapitalFundEventLedgerRecordRow(
        record,
        workbench.fundProfileId,
        workbench.ledgerBookId
      ));
    }
  }

  return rows;
}

function buildCapitalAccountInvestorAccountRow(
  account: CapitalAccountWorkbench["investorAccounts"][number]
): CapitalAccountWorkbenchInvestorAccountRowViewModel {
  const paymentEvidence = buildManualJournalPaymentIntentEvidenceView(
    account.paymentIntentEvidence,
    account.evidenceLinks,
    account.currency,
    account.netCapitalActivity,
    account.fundEventRecords[0]?.effectiveDate ?? null
  );

  return {
    id: account.accountKey,
    title: account.capitalAccountId,
    subtitle: `${account.investorId ?? "Unassigned investor"} / ${account.currency}`,
    statusLabel: account.readinessLabel || account.readiness,
    statusTone: capitalAccountReadinessTone(account.readiness),
    netActivityLabel: formatCurrencyWithCode(account.netCapitalActivity, account.currency, true),
    rollForwardLabel: `${formatCurrencyWithCode(account.openingNetActivity, account.currency, true)} opening -> ${formatCurrencyWithCode(account.endingNetActivity, account.currency, true)} ending`,
    activityMixLabel: [
      `Contributions ${formatCurrencyWithCode(account.contributions, account.currency)}`,
      `Distributions ${formatCurrencyWithCode(account.distributions, account.currency)}`,
      `Subscriptions ${formatCurrencyWithCode(account.subscriptions, account.currency)}`,
      `Redemptions ${formatCurrencyWithCode(account.redemptions, account.currency)}`,
      `Fees ${formatCurrencyWithCode(account.managementFees, account.currency)}`
    ].join(" / "),
    evidenceLabel: `${account.evidenceLinkCount.toLocaleString()} evidence / ${account.evidenceCategorySummary}`,
    eventLabel: `${account.fundEventCount.toLocaleString()} fund event(s) / ${account.postedFundEventCount.toLocaleString()} posted / ${account.publishedReportOutputCount.toLocaleString()} published`,
    paymentEvidenceLabel: paymentEvidence.label,
    paymentEvidenceTone: paymentEvidence.tone,
    paymentEvidenceSummaryLabel: paymentEvidence.summary,
    paymentEvidenceRequiredLabel: paymentEvidence.required,
    routeLabel: account.activityRoute
  };
}

function buildCapitalAccountAllocationRuleRow(
  rule: CapitalAccountWorkbench["allocationRules"][number]
): CapitalAccountWorkbenchAllocationRuleRowViewModel {
  const inputs = rule.inputs ?? [];
  const relatedFundEventIds = rule.relatedFundEventIds ?? [];
  return {
    id: rule.ruleId,
    accountLabel: `${rule.capitalAccountId} / ${rule.investorId ?? "Unassigned investor"}`,
    label: rule.label,
    statusLabel: rule.isSatisfied ? "Satisfied" : "Needs evidence",
    statusTone: rule.isSatisfied ? "success" : "warning",
    reason: rule.reason,
    basis: rule.basis,
    evidenceLabel: `${rule.evidenceLinkCount.toLocaleString()} evidence`,
    routeLabel: rule.route ?? "No route",
    requiredLabel: rule.requiredEvidence.length > 0 ? rule.requiredEvidence.join(" / ") : "No explicit evidence requirement",
    policyLabel: rule.ruleVersion ?? "No policy version",
    effectiveWindowLabel: formatCapitalAccountEffectiveWindow(rule.effectiveFrom, rule.effectiveTo),
    formulaLabel: rule.formula ?? "No allocation formula",
    approvalLabel: [
      rule.approvalState ?? "No approval state",
      rule.approvalReference ?? null
    ].filter(Boolean).join(" / "),
    traceLabel: rule.replayTrace ?? "No allocation replay trace",
    inputSummaryLabel: formatAllocationInputSummary(inputs),
    relatedFundEventLabel: relatedFundEventIds.length > 0 ? relatedFundEventIds.join(" / ") : "No related fund events"
  };
}

function buildCapitalAccountStatementLineageRow(
  lineage: CapitalAccountWorkbench["statementLineage"][number]
): CapitalAccountWorkbenchStatementLineageRowViewModel {
  const changedLineRows = (lineage.restatementChangedLines ?? []).map(buildCapitalAccountRestatementChangedLineRow);
  return {
    id: lineage.lineageId,
    title: lineage.displayName,
    subtitle: `${lineage.reportOutputType} / ${lineage.capitalAccountId} / ${lineage.investorId ?? "Unassigned investor"}`,
    statusLabel: lineage.hasRestatementLineage ? "Restated" : lineage.isPublished ? "Published" : lineage.isReportReady ? "Ready" : "Review",
    statusTone: lineage.hasRestatementLineage ? "warning" : lineage.isPublished || lineage.isReportReady ? "success" : "warning",
    publicationLabel: [
      lineage.reportWorkflowState ?? "No workflow state",
      lineage.publishedBy ? `by ${lineage.publishedBy}` : null,
      lineage.publishedAtUtc ? formatDateTimeLabel(lineage.publishedAtUtc) : null
    ].filter(Boolean).join(" / "),
    provenanceLabel: `${lineage.reportLineProvenanceCount.toLocaleString()} provenance line(s)`,
    restatementLabel: lineage.hasRestatementLineage
      ? `${lineage.restatementReasonCode ?? "Restated"} / ${lineage.restatementChangedLineCount.toLocaleString()} changed line(s) / ${lineage.restatementEvidenceLinkCount.toLocaleString()} evidence`
      : lineage.restatementStatus,
    manifestLabel: [lineage.publicationManifestId, lineage.retainedManifestPath, lineage.publicationEvidenceHash].filter(Boolean).join(" / ") || "No retained manifest metadata",
    routeLabel: lineage.reportOutputRoute ?? lineage.reportRoute,
    changedLineRows
  };
}

function buildCapitalAccountRestatementChangedLineRow(
  line: NonNullable<CapitalAccountWorkbench["statementLineage"][number]["restatementChangedLines"]>[number]
): CapitalAccountWorkbenchRestatementChangedLineRowViewModel {
  return {
    id: line.lineKey,
    lineKey: line.lineKey,
    valueLabel: `${line.previousValue} -> ${line.currentValue}`,
    evidenceLabel: `${line.evidenceLinkCount.toLocaleString()} changed-line evidence`
  };
}

function formatCapitalAccountEffectiveWindow(from?: string | null, to?: string | null): string {
  if (from && to) {
    return `${from} -> ${to}`;
  }

  if (from) {
    return `From ${from}`;
  }

  if (to) {
    return `Through ${to}`;
  }

  return "No effective window";
}

function formatAllocationInputSummary(
  inputs: NonNullable<CapitalAccountWorkbench["allocationRules"][number]["inputs"]>
): string {
  if (inputs.length === 0) {
    return "No allocation inputs";
  }

  const preview = inputs.slice(0, 3).map((input) => {
    const amount = input.amount == null
      ? "no amount"
      : formatCurrencyWithCode(input.amount, input.currency ?? "USD", true);
    return `${input.kind} ${input.sourceId} ${amount}`;
  });
  const remaining = inputs.length > preview.length
    ? ` + ${inputs.length - preview.length} more`
    : "";
  return `${inputs.length.toLocaleString()} input(s): ${preview.join(" / ")}${remaining}`;
}

function buildCapitalAccountAuditDrillThroughRow(
  drill: CapitalAccountWorkbench["auditDrillThroughs"][number]
): CapitalAccountWorkbenchAuditDrillThroughRowViewModel {
  return {
    id: drill.drillThroughId,
    kind: drill.kind,
    title: drill.label,
    summary: drill.summary,
    statusLabel: drill.isAvailable ? "Available" : "Missing route",
    statusTone: drill.isAvailable ? "success" : "warning",
    evidenceLabel: `${drill.evidenceLinkCount.toLocaleString()} evidence`,
    routeLabel: drill.route ?? "No route",
    relatedLabel: drill.relatedIds.length > 0 ? drill.relatedIds.join(" / ") : "No related ids"
  };
}

function capitalAccountWorkbenchStatusTone(statusLabel: string): AccountingToolingTone {
  const normalized = statusLabel.toLowerCase();
  if (normalized.includes("blocked")) {
    return "danger";
  }

  if (normalized.includes("ready") || normalized.includes("restated")) {
    return "success";
  }

  if (normalized.includes("review") || normalized.includes("missing") || normalized.includes("no capital")) {
    return "warning";
  }

  return "default";
}

function capitalAccountReadinessTone(readiness: CapitalAccountWorkbench["investorAccounts"][number]["readiness"]): AccountingToolingTone {
  const tone = manualJournalPrivateCapitalReadinessTone(readiness);
  return tone === "outline" ? "default" : tone;
}

function normalizeQueryValue(value: string | null): string | null {
  const normalized = value?.trim();
  return normalized ? normalized : null;
}

function buildManualJournalPrivateCapitalActivityView(
  activity: PrivateCapitalActivityProjection | null
): ManualJournalPrivateCapitalActivityViewModel {
  if (!activity) {
    return {
      title: "Private-capital activity",
      statusLabel: "No projection",
      projectedAtLabel: "Not loaded",
      emptyText: "No private-capital fund events are retained in the manual JE workbench yet.",
      summaryCards: [
        { id: "fund-events", label: "Fund events", value: "0", detail: "No retained fund-event rows", tone: "default" },
        { id: "capital-accounts", label: "Capital accounts", value: "0", detail: "No capital-account activity", tone: "default" },
        { id: "ledger-impacts", label: "Ledger impacts", value: "0", detail: "No GL impact rows", tone: "default" },
        { id: "report-outputs", label: "Report outputs", value: "0", detail: "No package candidates", tone: "default" },
        { id: "payment-intents", label: "Payment intents", value: "0", detail: "No pre-execution cash workflow", tone: "default" },
        { id: "net-activity", label: "Net activity", value: "$0", detail: "No projected balance movement", tone: "default" },
        { id: "projection-issues", label: "Projection issues", value: "0", detail: "No projection warnings", tone: "success" }
      ],
      fundEvents: [],
      capitalAccounts: [],
      capitalAccountSubledgers: [],
      capitalAccountSubledgerEntries: [],
      ledgerImpacts: [],
      reportOutputs: [],
      fundEventLedgerRecords: [],
      paymentIntents: [],
      validationIssues: []
    };
  }

  const currency = activity.currency || "USD";
  const capitalAccountSubledgers = activity.capitalAccountSubledgers ?? [];
  const capitalAccountSubledgerEntries = activity.capitalAccountSubledgerEntries;
  const ledgerImpacts = activity.ledgerImpacts;
  const reportOutputs = activity.reportOutputs;
  const fundEventLedgerRecords = activity.fundEventRecords;
  const paymentIntents = activity.paymentIntents ?? [];
  const fundEventLedgerRecordCount = fundEventLedgerRecords.length;
  const deferredPaymentIntentCount = paymentIntents.filter((item) => item.status === "ExecutionDeferred").length;
  const blockedPaymentIntentCount = paymentIntents.filter((item) => item.status === "Blocked" || item.status === "BankReturned").length;
  const postedFundEventCount = activity.postedFundEventCount ?? 0;
  const publishedReportOutputCount = activity.publishedReportOutputCount ?? 0;
  const validationIssues = activity.validationIssues.map<AccountingConfigurationIssueViewModel>((issue, index) => ({
    id: `${issue.code}-${index}`,
    label: issue.code,
    message: issue.message,
    detail: issue.suggestedAction ?? issue.targetId ?? "Review private-capital context.",
    tone: issue.severity === "Critical" ? "danger" : issue.severity === "Warning" ? "warning" : "default"
  }));

  return {
    title: "Private-capital activity",
    statusLabel: `${activity.fundEventCount} fund events / ${activity.capitalAccountCount} capital accounts`,
    projectedAtLabel: formatDateTimeLabel(activity.projectedAtUtc),
    emptyText: "No private-capital fund events are retained in the manual JE workbench yet.",
    summaryCards: [
      {
        id: "fund-events",
        label: "Fund events",
        value: activity.fundEventCount.toLocaleString(),
        detail: `${postedFundEventCount.toLocaleString()} posted / ${activity.submittedFundEventCount.toLocaleString()} submitted or approved`,
        tone: activity.fundEventCount > 0 ? "success" : "default"
      },
      {
        id: "posted-fund-events",
        label: "Posted events",
        value: postedFundEventCount.toLocaleString(),
        detail: "Ledger-backed fund-event rows",
        tone: postedFundEventCount > 0 ? "success" : "default"
      },
      {
        id: "fund-event-ledger-records",
        label: "Event ledger records",
        value: fundEventLedgerRecordCount.toLocaleString(),
        detail: "Event, subledger, GL, evidence, approval, and report output",
        tone: fundEventLedgerRecordCount === activity.fundEventCount && activity.fundEventCount > 0 ? "success" : fundEventLedgerRecordCount > 0 ? "warning" : "default"
      },
      {
        id: "capital-accounts",
        label: "Capital accounts",
        value: activity.capitalAccountCount.toLocaleString(),
        detail: `${activity.approvalQueueCount.toLocaleString()} pending approval`,
        tone: activity.approvalQueueCount > 0 ? "warning" : activity.capitalAccountCount > 0 ? "success" : "default"
      },
      {
        id: "capital-account-subledger",
        label: "Subledger rows",
        value: capitalAccountSubledgerEntries.length.toLocaleString(),
        detail: `${capitalAccountSubledgers.length.toLocaleString()} account-level subledger(s)`,
        tone: capitalAccountSubledgerEntries.length > 0 ? "success" : "default"
      },
      {
        id: "report-outputs",
        label: "Report outputs",
        value: reportOutputs.length.toLocaleString(),
        detail: `${publishedReportOutputCount.toLocaleString()} published / ${reportOutputs.filter((item) => item.isReportReady).length.toLocaleString()} ready`,
        tone: reportOutputs.some((item) => !item.isReportReady) ? "warning" : reportOutputs.length > 0 ? "success" : "default"
      },
      {
        id: "payment-intents",
        label: "Payment intents",
        value: paymentIntents.length.toLocaleString(),
        detail: `${deferredPaymentIntentCount.toLocaleString()} execution-deferred / ${blockedPaymentIntentCount.toLocaleString()} blocked or returned`,
        tone: blockedPaymentIntentCount > 0 ? "danger" : paymentIntents.some((item) => item.status !== "ExecutionDeferred") ? "warning" : paymentIntents.length > 0 ? "success" : "default"
      },
      {
        id: "published-report-outputs",
        label: "Published outputs",
        value: publishedReportOutputCount.toLocaleString(),
        detail: "Governed report-pack outputs",
        tone: publishedReportOutputCount > 0 ? "success" : "default"
      },
      {
        id: "ledger-impacts",
        label: "Ledger impacts",
        value: ledgerImpacts.length.toLocaleString(),
        detail: `${ledgerImpacts.filter((item) => item.isPostingReady).length.toLocaleString()} posting-ready`,
        tone: ledgerImpacts.some((item) => !item.isPostingReady) ? "warning" : ledgerImpacts.length > 0 ? "success" : "default"
      },
      {
        id: "net-activity",
        label: "Net activity",
        value: formatCurrencyWithCode(activity.netCapitalActivity, currency, true),
        detail: "Ledger-derived capital activity",
        tone: activity.netCapitalActivity < 0 ? "warning" : activity.netCapitalActivity > 0 ? "success" : "default"
      },
      {
        id: "projection-issues",
        label: "Projection issues",
        value: validationIssues.length.toLocaleString(),
        detail: validationIssues.length > 0 ? "Context needs review" : "Projection context complete",
        tone: validationIssues.length > 0 ? "warning" : "success"
      }
    ],
    fundEvents: activity.fundEvents.map(buildManualJournalPrivateCapitalFundEventRow),
    capitalAccounts: activity.capitalAccounts.map(buildManualJournalPrivateCapitalAccountRow),
    capitalAccountSubledgers: capitalAccountSubledgers.map(buildManualJournalPrivateCapitalCapitalAccountSubledgerRow),
    capitalAccountSubledgerEntries: capitalAccountSubledgerEntries.map(buildManualJournalPrivateCapitalSubledgerEntryRow),
    ledgerImpacts: ledgerImpacts.map(buildManualJournalPrivateCapitalLedgerImpactRow),
    reportOutputs: reportOutputs.map(buildManualJournalPrivateCapitalReportOutputRow),
    fundEventLedgerRecords: fundEventLedgerRecords.map((record) =>
      buildManualJournalPrivateCapitalFundEventLedgerRecordRow(record, activity.fundProfileId, activity.ledgerBookId)
    ),
    paymentIntents: paymentIntents.map(buildManualJournalPaymentIntentWorkflowRow),
    validationIssues
  };
}

function buildManualJournalPrivateCapitalCapitalAccountSubledgerRow(
  subledger: PrivateCapitalCapitalAccountSubledger
): ManualJournalPrivateCapitalCapitalAccountSubledgerRowViewModel {
  const evidenceCategories = (subledger.evidenceCategories ?? []).map(buildManualJournalPrivateCapitalEvidenceCategoryRow);
  const readyEvidenceCategoryCount = evidenceCategories.filter((category) => category.statusLabel === "Ready").length;
  const paymentEvidence = buildManualJournalPaymentIntentEvidenceView(
    subledger.paymentIntentEvidence,
    subledger.evidenceLinks,
    subledger.currency,
    subledger.endingNetActivity,
    subledger.lastEffectiveDate ?? null
  );
  const hasCriticalIssue = subledger.validationIssues.some((issue) => issue.severity === "Critical");
  const hasWarnings = subledger.validationIssues.length > 0 || subledger.approvalQueueCount > 0 || evidenceCategories.some((category) => category.statusLabel !== "Ready");
  const readiness = subledger.readiness ?? (hasCriticalIssue ? "Blocked" : hasWarnings ? "ReportReview" : subledger.fundEventCount > 0 ? "Ready" : "EvidenceMissing");

  return {
    id: subledger.subledgerId,
    title: subledger.capitalAccountId,
    subtitle: `${subledger.investorId ?? "Investor not assigned"} / ${subledger.currency}`,
    statusLabel: subledger.readinessLabel || (hasCriticalIssue ? "Blocked" : hasWarnings ? "Review" : subledger.fundEventCount > 0 ? "Ready" : "Empty"),
    statusTone: manualJournalPrivateCapitalReadinessTone(readiness),
    readinessLabel: subledger.readinessLabel || readiness,
    readinessTone: manualJournalPrivateCapitalReadinessTone(readiness),
    readinessReasonLabel: subledger.readinessReason || "No subledger readiness reason",
    nextActionLabel: subledger.nextAction || "No next action",
    nextActionRouteLabel: subledger.nextActionRoute || "No next-action route",
    activityRouteLabel: subledger.activityRoute || "No subledger route",
    netActivityLabel: formatCurrencyWithCode(subledger.netCapitalActivity, subledger.currency, true),
    openingLabel: formatCurrencyWithCode(subledger.openingNetActivity, subledger.currency, true),
    endingLabel: formatCurrencyWithCode(subledger.endingNetActivity, subledger.currency, true),
    contributionLabel: formatCurrencyWithCode(subledger.contributions, subledger.currency),
    distributionLabel: formatCurrencyWithCode(subledger.distributions, subledger.currency),
    otherActivityLabel: [
      `Subscriptions ${formatCurrencyWithCode(subledger.subscriptions, subledger.currency)}`,
      `Redemptions ${formatCurrencyWithCode(subledger.redemptions, subledger.currency)}`,
      `Fees ${formatCurrencyWithCode(subledger.managementFees, subledger.currency)}`
    ].join(" / "),
    eventCountLabel: `${subledger.fundEventCount.toLocaleString()} fund event(s)`,
    approvalQueueLabel: `${subledger.approvalQueueCount.toLocaleString()} approval queue`,
    postedEventLabel: `${subledger.postedFundEventCount.toLocaleString()} posted event(s)`,
    publishedReportLabel: `${subledger.publishedReportOutputCount.toLocaleString()} published report output(s)`,
    dateRangeLabel: [
      subledger.firstEffectiveDate ? `first ${subledger.firstEffectiveDate}` : "no first effective date",
      subledger.lastEffectiveDate ? `last ${subledger.lastEffectiveDate}` : "no last effective date",
      subledger.lastFundEventType ?? "no last event type"
    ].join(" / "),
    evidenceLabel: `${subledger.evidenceLinkCount.toLocaleString()} evidence`,
    paymentEvidenceLabel: paymentEvidence.label,
    paymentEvidenceTone: paymentEvidence.tone,
    paymentEvidenceSummaryLabel: paymentEvidence.summary,
    paymentEvidenceRequiredLabel: paymentEvidence.required,
    evidenceCategorySummaryLabel: evidenceCategories.length > 0
      ? `${readyEvidenceCategoryCount.toLocaleString()}/${evidenceCategories.length.toLocaleString()} evidence categories ready`
      : "No evidence categories",
    evidenceCategories,
    issueLabel: subledger.validationIssueCount > 0
      ? `${subledger.validationIssueCount.toLocaleString()} subledger issue(s)`
      : "No subledger issues"
  };
}

function buildManualJournalPrivateCapitalFundEventLedgerRecordRow(
  record: PrivateCapitalFundEventLedgerRecord,
  fundProfileId: string | null | undefined,
  ledgerBookId: string | null | undefined
): ManualJournalPrivateCapitalFundEventLedgerRecordViewModel {
  const evidenceCategories = (record.evidenceCategories ?? []).map(buildManualJournalPrivateCapitalEvidenceCategoryRow);
  const readyEvidenceCategoryCount = evidenceCategories.filter((category) => category.statusLabel === "Ready").length;
  const paymentEvidence = buildManualJournalPaymentIntentEvidenceView(
    record.paymentIntentEvidence,
    record.evidenceLinks,
    record.currency,
    record.netCapitalActivity,
    record.effectiveDate
  );
  return {
    id: record.fundEventRecordId,
    title: record.fundEventType || record.fundEventId,
    subtitle: `${record.capitalAccountId}${record.investorId ? ` / ${record.investorId}` : ""}`,
    statusLabel: record.isPosted ? "Posted" : record.approvalState,
    statusTone: record.isPosted
      ? "success"
      : record.validationIssues.some((issue) => issue.severity === "Critical")
        ? "danger"
        : record.validationIssues.length > 0
          ? "warning"
          : manualJournalPrivateCapitalStatusTone(record.approvalState),
    readinessLabel: record.readinessLabel || record.readiness,
    readinessTone: manualJournalPrivateCapitalReadinessTone(record.readiness),
    readinessReasonLabel: record.readinessReason || "No readiness reason",
    nextActionLabel: record.nextAction || "No next action",
    nextActionRouteLabel: record.nextActionRoute ?? "No next-action route",
    effectiveDateLabel: record.effectiveDate,
    netActivityLabel: formatCurrencyWithCode(record.netCapitalActivity, record.currency, true),
    grossActivityLabel: formatCurrencyWithCode(record.grossAmount, record.currency),
    capitalAccountRollForwardLabel: `${formatCurrencyWithCode(record.capitalAccountOpeningNetActivity, record.currency, true)} opening -> ${formatCurrencyWithCode(record.capitalAccountEndingNetActivity, record.currency, true)} ending`,
    memoLabel: record.memo || record.journalEntryId,
    referenceLabel: [record.paymentIntentId, record.settlementReference].filter((value): value is string => Boolean(value)).join(" / ") || record.journalEntryId,
    paymentEvidenceLabel: paymentEvidence.label,
    paymentEvidenceTone: paymentEvidence.tone,
    paymentEvidenceSummaryLabel: paymentEvidence.summary,
    paymentEvidenceRequiredLabel: paymentEvidence.required,
    activityRouteLabel: record.activityRoute || "No activity route",
    commandCenterRouteLabel: buildPrivateCapitalFundEventCommandCenterRoute(fundProfileId, ledgerBookId, record.fundEventId),
    evidenceRouteLabel: record.evidenceRoute || "No evidence route",
    approvalRouteLabel: record.approvalRoute ?? (record.approvalId ? `/accounting/approvals?approvalId=${encodeURIComponent(record.approvalId)}` : "No approval route"),
    evidenceLabel: `${record.evidenceLinkCount.toLocaleString()} evidence`,
    ledgerImpactLabel: `${record.ledgerImpactCount.toLocaleString()} ledger impact(s)${record.isPostingReady ? " / posting ready" : ""}`,
    subledgerLabel: `${record.capitalAccountSubledgerEntryCount.toLocaleString()} subledger movement(s)`,
    reportOutputLabel: `${record.reportOutputCount.toLocaleString()} report output(s)${record.isPublished ? " / published" : record.isReportReady ? " / ready" : ""}`,
    reportOutputDetailLabel: [
      record.primaryReportOutputType ?? "No primary output",
      record.reportWorkflowState ?? "No workflow state",
      `${record.reportLineProvenanceCount.toLocaleString()} provenance`
    ].join(" / "),
    reportOutputRouteLabel: record.primaryReportRoute ?? record.retainedManifestPath ?? record.publicationManifestId ?? "No report route",
    evidenceCategorySummaryLabel: evidenceCategories.length > 0
      ? `${readyEvidenceCategoryCount.toLocaleString()}/${evidenceCategories.length.toLocaleString()} evidence categories ready`
      : "No evidence categories",
    evidenceCategories,
    issueLabel: record.validationIssueCount > 0
      ? `${record.validationIssueCount.toLocaleString()} record issue(s)`
      : "No record issues"
  };
}

function buildPrivateCapitalFundEventCommandCenterRoute(
  fundProfileId: string | null | undefined,
  ledgerBookId: string | null | undefined,
  fundEventId: string
): string {
  const params = new URLSearchParams();
  const normalizedFundProfileId = normalizePrivateCapitalRouteId(fundProfileId);
  const normalizedLedgerBookId = normalizePrivateCapitalRouteId(ledgerBookId);
  if (normalizedFundProfileId) {
    params.set("fundProfileId", normalizedFundProfileId);
  }

  if (normalizedLedgerBookId && isGuid(normalizedLedgerBookId)) {
    params.set("ledgerBookId", normalizedLedgerBookId);
  }

  params.set("fundEventId", fundEventId);
  return `${WORKSTATION_API_ENDPOINTS.privateCapitalFundEventCommandCenter}?${params.toString()}`;
}

function normalizePrivateCapitalRouteId(value: string | null | undefined): string | null {
  const normalized = value?.trim();
  return normalized ? normalized : null;
}

function isGuid(value: string): boolean {
  return /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value);
}

function buildManualJournalPaymentIntentWorkflowRow(
  workflow: PaymentIntentWorkflow
): ManualJournalPaymentIntentWorkflowRowViewModel {
  const movement = workflow.expectedCashMovement;
  const approvedCount = workflow.approvalChain.filter((step) => step.status === "Approved").length;
  const bankConfirmedCount = workflow.bankEvidence.filter((item) => item.status === "Confirmed").length;
  const bankRetainedCount = workflow.bankEvidence.filter((item) => item.status === "Retained").length;
  const bankReturnedCount = workflow.bankEvidence.filter((item) => item.status === "Returned").length;
  const reconciliationReadyCount = workflow.reconciliationLinks.filter((item) => item.status === "Ready").length;

  return {
    id: workflow.paymentIntentId,
    title: workflow.paymentIntentId,
    subtitle: [workflow.fundEventId, movement.capitalAccountId, movement.investorId].filter(Boolean).join(" / ") || workflow.journalEntryId,
    statusLabel: workflow.statusLabel || formatPaymentIntentWorkflowStatus(workflow.status),
    statusTone: paymentIntentWorkflowTone(workflow.status),
    requestedLabel: `${workflow.requester} / ${formatDateTimeLabel(workflow.requestedAtUtc)}`,
    expectedCashLabel: [
      movement.direction,
      formatCurrencyWithCode(Math.abs(movement.amount), movement.currency),
      movement.effectiveDate,
      movement.settlementReference ?? "no settlement"
    ].join(" / "),
    requestMetadataLabel: [
      `payee ${movement.payee ?? "not assigned"}`,
      `scope ${movement.accountScope ?? "not assigned"}`,
      `purpose ${movement.businessPurpose ?? movement.purpose}`,
      `policy ${movement.approvalPolicy ?? "not assigned"}`
    ].join(" / "),
    sourceEvidenceLabel: `${(movement.sourceEvidenceLinks ?? []).length.toLocaleString()} source evidence link(s)`,
    approvalLabel: `${approvedCount.toLocaleString()}/${workflow.approvalChain.length.toLocaleString()} approved`,
    bankEvidenceLabel: `${bankConfirmedCount.toLocaleString()} confirmed / ${bankRetainedCount.toLocaleString()} retained / ${bankReturnedCount.toLocaleString()} returned`,
    reconciliationLabel: `${reconciliationReadyCount.toLocaleString()}/${workflow.reconciliationLinks.length.toLocaleString()} reconciliation ready`,
    auditLabel: `${workflow.auditHistory.length.toLocaleString()} audit event(s)`,
    readinessReasonLabel: workflow.readinessReason || "Payment intent readiness requires review.",
    executionDeferredLabel: workflow.executionDeferredReason || "Full payment execution is deferred.",
    evidenceRouteLabel: workflow.evidenceRoute || "No evidence route",
    workbenchRouteLabel: workflow.workbenchRoute || "No workbench route",
    approvalSteps: workflow.approvalChain.map((step) => ({
      id: `${workflow.paymentIntentId}-approval-${step.sequence}-${step.role}`,
      sequenceLabel: `Step ${step.sequence.toLocaleString()}`,
      roleLabel: step.role || "Approval",
      actorLabel: step.actor || "No actor assigned",
      statusLabel: step.status || "No status",
      decidedLabel: step.decidedAtUtc ? formatDateTimeLabel(step.decidedAtUtc) : "Decision pending",
      evidenceRouteLabel: step.evidenceRoute || "No approval evidence route"
    })),
    bankEvidence: workflow.bankEvidence.map((item) => ({
      id: item.evidenceId,
      title: item.evidenceKind || item.evidenceId,
      statusLabel: item.status || "No status",
      summaryLabel: item.summary || "No cash evidence summary",
      amountLabel: item.amount !== null && item.amount !== undefined && item.currency
        ? formatCurrencyWithCode(Math.abs(item.amount), item.currency)
        : "No amount",
      effectiveDateLabel: item.effectiveDate ?? "No effective date",
      recordedLabel: item.recordedAtUtc ? formatDateTimeLabel(item.recordedAtUtc) : "No recorded timestamp",
      recorderLabel: item.recordedBy ? `Recorded by ${item.recordedBy}` : "No retained recorder",
      referenceLabel: [
        item.bankTransactionId ? `bank ${item.bankTransactionId}` : null,
        item.transactionType ?? null,
        item.externalRef ?? null
      ].filter((value): value is string => Boolean(value)).join(" / ") || "No external reference",
      evidenceRouteLabel: item.evidenceRoute || "No bank evidence route"
    })),
    reconciliationLinks: workflow.reconciliationLinks.map((link) => ({
      id: link.linkId,
      statusLabel: link.status || "No status",
      summaryLabel: link.summary || "No reconciliation summary",
      routeLabel: link.evidenceRoute || "No reconciliation evidence route",
      caseLabel: [
        link.reconciliationCaseId ? `case ${link.reconciliationCaseId}` : null,
        link.reconciliationRunId ? `run ${link.reconciliationRunId}` : null
      ].filter((value): value is string => Boolean(value)).join(" / ") || "No reconciliation case"
    })),
    auditEvents: workflow.auditHistory.map((event) => ({
      id: event.auditEventId,
      actionLabel: event.action || "No action",
      actorLabel: event.actor || "No actor",
      recordedLabel: formatDateTimeLabel(event.recordedAtUtc),
      summaryLabel: event.summary || "No audit summary",
      evidenceLabel: `${event.evidenceLinks.length.toLocaleString()} evidence link(s)`,
      evidenceRouteLabels: event.evidenceLinks
    }))
  };
}

function buildManualJournalPrivateCapitalEvidenceCategoryRow(
  category: PrivateCapitalEvidenceCategory
): ManualJournalPrivateCapitalEvidenceCategoryViewModel {
  return {
    id: category.categoryId,
    label: category.label || category.categoryId,
    statusLabel: category.isReady ? "Ready" : "Missing",
    tone: category.isReady ? "success" : "warning",
    summaryLabel: category.summary || "No category summary",
    evidenceLabel: `${category.evidenceLinkCount.toLocaleString()} evidence`,
    requiredEvidenceLabel: (category.requiredEvidence ?? []).filter(Boolean).join(" / ") || "No required evidence listed"
  };
}

function buildManualJournalPaymentIntentEvidenceView(
  evidence: PrivateCapitalPaymentIntentEvidence | null | undefined,
  fallbackEvidenceLinks: string[],
  currency: string,
  amount: number,
  effectiveDate: string | null
): { label: string; tone: "success" | "warning"; summary: string; required: string } {
  if (!evidence) {
    return {
      label: `Missing intent / ${formatCurrencyWithCode(Math.abs(amount), currency)} / ${effectiveDate ?? "no effective date"}`,
      tone: "warning",
      summary: "Payment intent evidence is not included in the shared accounting payload.",
      required: "Payment intent id / Retained bank, cash, or settlement evidence"
    };
  }

  const required = (evidence.requiredEvidence ?? []).filter(Boolean);
  const fallbackCashCount = fallbackEvidenceLinks.filter((link) => /bank|cash|custodian|payment|plaid|settlement|treasury|wire/i.test(link)).length;
  const cashEvidenceCount = evidence.cashEvidenceLinkCount ?? fallbackCashCount;
  const labelParts = [
    formatPaymentIntentEvidenceStatus(evidence),
    evidence.direction,
    formatCurrencyWithCode(Math.abs(evidence.amount), evidence.currency),
    `${cashEvidenceCount.toLocaleString()} cash evidence`,
    evidence.settlementReference ? "settlement linked" : "no settlement"
  ].filter(Boolean);

  return {
    label: labelParts.join(" / "),
    tone: evidence.isReady ? "success" : "warning",
    summary: evidence.summary || "No payment intent evidence summary.",
    required: required.length > 0 ? required.join(" / ") : "No additional payment evidence required"
  };
}

function formatPaymentIntentEvidenceStatus(evidence: PrivateCapitalPaymentIntentEvidence): string {
  const status = evidence.status;
  if (status === "SettlementMatched") return "Settlement matched";
  if (status === "IntentCaptured") return "Cash evidence retained";
  if (status === "CashEvidenceMissing") return "Cash evidence missing";
  if (evidence.isReady && evidence.settlementReference) return "Settlement matched";
  if (evidence.isReady) return "Cash evidence retained";
  return "Payment intent missing";
}

function formatPaymentIntentWorkflowStatus(status: PaymentIntentWorkflow["status"]): string {
  if (status === "ExecutionDeferred") return "Ready, execution deferred";
  if (status === "ReconciliationPending") return "Reconciliation pending";
  if (status === "BankEvidencePending") return "Bank evidence pending";
  if (status === "BankReturned") return "Bank return captured";
  if (status === "ApprovalPending") return "Approval pending";
  if (status === "Blocked") return "Blocked";
  return "Intent evidence missing";
}

function paymentIntentWorkflowTone(status: PaymentIntentWorkflow["status"]): ManualJournalPaymentIntentWorkflowRowViewModel["statusTone"] {
  if (status === "ExecutionDeferred") return "success";
  if (status === "Blocked" || status === "BankReturned") return "danger";
  if (status === "EvidenceMissing" || status === "BankEvidencePending" || status === "ReconciliationPending" || status === "ApprovalPending") return "warning";
  return "outline";
}

function manualJournalPrivateCapitalReadinessTone(
  readiness: PrivateCapitalFundEventLedgerRecord["readiness"]
): ManualJournalPrivateCapitalFundEventLedgerRecordViewModel["readinessTone"] {
  if (readiness === "Blocked") return "danger";
  if (readiness === "Ready" || readiness === "Published") return "success";
  return readiness ? "warning" : "outline";
}

function buildManualJournalPrivateCapitalFundEventRow(
  event: PrivateCapitalFundEvent
): ManualJournalPrivateCapitalFundEventRowViewModel {
  return {
    id: event.fundEventId,
    title: event.fundEventType || event.entryType,
    subtitle: `${event.capitalAccountId}${event.investorId ? ` / ${event.investorId}` : ""}`,
    statusLabel: event.isPosted ? "Posted" : event.journalStatus,
    statusTone: event.isPosted ? "success" : manualJournalPrivateCapitalStatusTone(event.journalStatus),
    effectiveDateLabel: event.effectiveDate,
    amountLabel: formatCurrencyWithCode(event.netCapitalActivity, event.currency, true),
    grossAmountLabel: formatCurrencyWithCode(event.grossAmount, event.currency),
    evidenceLabel: `${event.evidenceLinks.length.toLocaleString()} evidence`,
    memoLabel: event.memo || event.journalEntryId,
    paymentLabel: [event.paymentIntentId, event.settlementReference].filter((value): value is string => Boolean(value)).join(" / ") || "No payment link",
    validationLabel: event.validationIssues.length > 0
      ? `${event.validationIssues.length.toLocaleString()} validation issues`
      : "No validation issues"
  };
}

function buildManualJournalPrivateCapitalAccountRow(
  account: PrivateCapitalCapitalAccountActivity
): ManualJournalPrivateCapitalAccountRowViewModel {
  return {
    id: `${account.capitalAccountId}-${account.currency}`,
    title: account.capitalAccountId,
    subtitle: account.investorId ?? "Investor not assigned",
    netActivityLabel: formatCurrencyWithCode(account.netActivity, account.currency, true),
    contributionLabel: formatCurrencyWithCode(account.contributions, account.currency),
    distributionLabel: formatCurrencyWithCode(account.distributions, account.currency),
    subscriptionLabel: formatCurrencyWithCode(account.subscriptions, account.currency),
    redemptionLabel: formatCurrencyWithCode(account.redemptions, account.currency),
    managementFeeLabel: formatCurrencyWithCode(account.managementFees, account.currency),
    eventCountLabel: `${account.fundEventCount.toLocaleString()} events`,
    lastEventLabel: account.lastEffectiveDate
      ? `${account.lastFundEventType ?? "Fund event"} / ${account.lastEffectiveDate}`
      : "No effective date"
  };
}

function buildManualJournalPrivateCapitalSubledgerEntryRow(
  entry: PrivateCapitalCapitalAccountSubledgerEntry
): ManualJournalPrivateCapitalSubledgerEntryRowViewModel {
  return {
    id: entry.subledgerEntryId,
    title: entry.fundEventType || entry.entryType,
    subtitle: `${entry.capitalAccountId}${entry.investorId ? ` / ${entry.investorId}` : ""}`,
    statusLabel: entry.isPosted ? "Posted" : entry.approvalState,
    statusTone: entry.isPosted ? "success" : manualJournalPrivateCapitalStatusTone(entry.approvalState),
    effectiveDateLabel: entry.effectiveDate,
    netActivityLabel: formatCurrencyWithCode(entry.netCapitalActivity, entry.currency, true),
    runningBalanceLabel: formatCurrencyWithCode(entry.runningNetActivity, entry.currency, true),
    grossAmountLabel: formatCurrencyWithCode(entry.grossAmount, entry.currency),
    evidenceLabel: `${entry.evidenceLinks.length.toLocaleString()} evidence`,
    memoLabel: entry.memo || entry.journalEntryId,
    issueLabel: entry.validationIssues.length > 0
      ? `${entry.validationIssues.length.toLocaleString()} subledger issues`
      : "No subledger issues"
  };
}

function buildManualJournalPrivateCapitalLedgerImpactRow(
  impact: PrivateCapitalLedgerImpact
): ManualJournalPrivateCapitalLedgerImpactRowViewModel {
  return {
    id: impact.ledgerImpactId,
    title: impact.fundEventType || impact.fundEventId,
    subtitle: `${impact.capitalAccountId}${impact.investorId ? ` / ${impact.investorId}` : ""}`,
    readinessLabel: impact.isPostingReady ? "Posting ready" : impact.isBalanced ? "Review" : "Unbalanced",
    readinessTone: impact.isPostingReady ? "success" : impact.validationIssues.some((issue) => issue.severity === "Critical") ? "danger" : "warning",
    effectiveDateLabel: impact.effectiveDate,
    debitLabel: formatCurrencyWithCode(impact.totalDebits, impact.currency),
    creditLabel: formatCurrencyWithCode(impact.totalCredits, impact.currency),
    imbalanceLabel: formatCurrencyWithCode(impact.imbalance, impact.currency, true),
    evidenceLabel: `${impact.evidenceLinks.length.toLocaleString()} evidence`,
    lineLabel: `${impact.lineCount.toLocaleString()} GL lines`,
    issueLabel: impact.validationIssues.length > 0
      ? `${impact.validationIssues.length.toLocaleString()} ledger issues`
      : "No ledger issues"
  };
}

function buildManualJournalPrivateCapitalReportOutputRow(
  output: PrivateCapitalReportOutput
): ManualJournalPrivateCapitalReportOutputRowViewModel {
  return {
    id: output.reportOutputId,
    title: output.displayName || output.reportOutputType,
    subtitle: `${output.capitalAccountId}${output.investorId ? ` / ${output.investorId}` : ""}`,
    readinessLabel: output.readinessLabel || (output.isPublished ? "Published" : output.isReportReady ? "Ready" : "Review"),
    readinessTone: output.isPublished || output.isReportReady ? "success" : output.validationIssues.some((issue) => issue.severity === "Critical") ? "danger" : "warning",
    readinessReasonLabel: output.readinessReason || "No report-output readiness reason",
    nextActionLabel: output.nextAction || "No next action",
    nextActionRouteLabel: output.nextActionRoute ?? output.reportOutputRoute ?? output.reportRoute,
    effectiveDateLabel: output.effectiveDate,
    amountLabel: formatCurrencyWithCode(output.netCapitalActivity, output.currency, true),
    evidenceLabel: `${output.evidenceLinkCount.toLocaleString()} evidence`,
    routeLabel: output.reportOutputRoute ?? output.reportRoute,
    issueLabel: output.validationIssues.length > 0
      ? `${output.validationIssues.length.toLocaleString()} readiness issues`
      : "No readiness issues",
    workflowLabel: [output.reportPackId, output.reportWorkflowState].filter((value): value is string => Boolean(value)).join(" / ") || output.reportOutputType,
    publicationLabel: output.publishedAtUtc
      ? `${output.publishedBy ?? "publisher"} / ${formatDateTimeLabel(output.publishedAtUtc)}`
      : output.publicationManifestId ?? "No publication manifest",
    provenanceLabel: `${(output.reportLineProvenanceCount ?? 0).toLocaleString()} provenance line(s)`
  };
}

function manualJournalPrivateCapitalStatusTone(
  status: PrivateCapitalFundEvent["journalStatus"]
): ManualJournalPrivateCapitalFundEventRowViewModel["statusTone"] {
  if (status === "Approved") {
    return "success";
  }

  if (status === "Submitted") {
    return "warning";
  }

  if (status === "NeedsFix" || status === "Rejected") {
    return "danger";
  }

  return "outline";
}

function createManualJournalEntryDraft(
  fundProfileId = "default-fund",
  ledgerBookId: string | null = null
): ManualJournalEntryDraft {
  const now = new Date().toISOString();
  const currency = "USD";
  return {
    journalEntryId: newClientId(),
    status: "Draft",
    fundProfileId,
    ledgerBookId,
    accountingBasis: DEFAULT_ACCOUNTING_BASIS,
    accountingDate: now.slice(0, 10),
    periodId: null,
    entityId: null,
    fundNodeId: null,
    currency,
    memo: "Manual close adjustment",
    preparedBy: "browser-user",
    createdAtUtc: now,
    updatedAtUtc: now,
    version: 0,
    lines: [
      createManualJournalEntryLine("Debit", currency, ""),
      createManualJournalEntryLine("Credit", currency, "")
    ],
    evidenceLinks: [],
    evidenceAttachments: [],
    validationIssues: [],
    totalDebits: 0,
    totalCredits: 0,
    imbalance: 0,
    approvalId: null,
    submittedAtUtc: null,
    submittedBy: null,
    entryType: "General",
    treasuryContext: null,
    dimensions: null,
    lifecycleTransitions: [],
    reversalOfJournalEntryId: null,
    rebookedFromJournalEntryId: null,
    approvedAtUtc: null,
    approvedBy: null,
    postedAtUtc: null,
    postedBy: null,
    closedLockedAtUtc: null,
    closeLockedBy: null
  };
}

function withManualJournalTotals(draft: ManualJournalEntryDraft): ManualJournalEntryDraft {
  const totalDebits = draft.lines
    .filter((line) => line.side === "Debit")
    .reduce((sum, line) => sum + (Number.isFinite(line.amount) ? line.amount : 0), 0);
  const totalCredits = draft.lines
    .filter((line) => line.side === "Credit")
    .reduce((sum, line) => sum + (Number.isFinite(line.amount) ? line.amount : 0), 0);

  return {
    ...draft,
    totalDebits,
    totalCredits,
    imbalance: totalDebits - totalCredits
  };
}

function formatManualJournalTreasuryContext(draft: ManualJournalEntryDraft): string {
  const context = draft.treasuryContext;
  if (!context) {
    return `${draft.entryType} | No treasury context`;
  }

  const parts = [
    draft.entryType,
    context.effectiveDate ? `effective ${context.effectiveDate}` : null,
    context.fundEventType ?? context.fundEventId ?? null,
    context.capitalAccountId ? `capital ${context.capitalAccountId}` : null,
    context.paymentIntentId || context.settlementReference ? "payment-linked" : null,
    context.idempotencyKey ? "idempotent" : null
  ].filter((part): part is string => Boolean(part));

  return parts.join(" | ");
}

function lifecycleActionNotes(action: JournalEntryLifecycleAction, draft: ManualJournalEntryDraft): string {
  switch (action) {
    case "Approve":
      return `Controller approval for journal entry ${draft.journalEntryId}.`;
    case "Reject":
      return `Controller rejection for journal entry ${draft.journalEntryId}.`;
    case "Post":
      return `Post approved journal entry ${draft.journalEntryId}.`;
    case "Reverse":
      return `Create reversal draft for posted journal entry ${draft.journalEntryId}.`;
    case "Rebook":
      return `Create rebook draft for posted journal entry ${draft.journalEntryId}.`;
    case "LockAfterClose":
      return `Lock posted journal entry ${draft.journalEntryId} after close.`;
    case "Submit":
      return `Submit journal entry ${draft.journalEntryId} for approval.`;
    case "Validate":
      return `Validate journal entry ${draft.journalEntryId}.`;
    default:
      return `Apply ${action} to journal entry ${draft.journalEntryId}.`;
  }
}

function buildManualJournalLifecycleCommands(
  draft: ManualJournalEntryDraft,
  busyAction: JournalEntryLifecycleAction | null
): ManualJournalLifecycleCommandViewModel[] {
  const hasEvidence = (draft.evidenceLinks?.length ?? 0) > 0 || (draft.evidenceAttachments?.length ?? 0) > 0;
  const isBalanced = Math.abs(draft.imbalance) === 0;
  const hasCriticalIssues = draft.validationIssues.some((issue) => issue.severity === "Critical");
  const commonBlocker = !hasEvidence
    ? "Attach retained evidence before lifecycle transitions."
    : !isBalanced
      ? "Balance debits and credits before lifecycle transitions."
      : hasCriticalIssues
        ? "Resolve critical validation issues before lifecycle transitions."
        : null;

  return [
    lifecycleCommand("Approve", "Approve", "Move a submitted journal entry to approved.", draft.status === "Submitted" ? commonBlocker : `Requires Submitted status; current status is ${draft.status}.`, "success", busyAction),
    lifecycleCommand("Reject", "Reject", "Reject a submitted journal entry for correction.", draft.status === "Submitted" ? null : `Requires Submitted status; current status is ${draft.status}.`, "danger", busyAction),
    lifecycleCommand("Post", "Post", "Post an approved journal entry without mutating it afterward.", draft.status === "Approved" ? commonBlocker : `Requires Approved status; current status is ${draft.status}.`, "success", busyAction),
    lifecycleCommand("Reverse", "Reverse", "Generate a separate reversal draft for a posted journal entry.", draft.status === "Posted" ? commonBlocker : `Requires Posted status; current status is ${draft.status}.`, "warning", busyAction),
    lifecycleCommand("Rebook", "Rebook", "Generate a separate rebook draft using the current posted lines.", draft.status === "Posted" ? commonBlocker : `Requires Posted status; current status is ${draft.status}.`, "warning", busyAction),
    lifecycleCommand("LockAfterClose", "Lock after close", "Lock a posted journal entry after close.", draft.status === "Posted" ? commonBlocker : `Requires Posted status; current status is ${draft.status}.`, "default", busyAction)
  ];
}

function buildManualJournalLifecycleChecklist(
  draft: ManualJournalEntryDraft,
  commands: ManualJournalLifecycleCommandViewModel[],
  transitions: ManualJournalLifecycleTransitionViewModel[],
  correctionRows: ManualJournalLifecycleCorrectionViewModel[]
): ManualJournalLifecycleChecklistItemViewModel[] {
  const evidenceCount = new Set([
    ...(draft.evidenceLinks ?? []),
    ...(draft.evidenceAttachments ?? []).map((attachment) => attachment.uri)
  ].filter((value) => value.trim().length > 0)).size;
  const criticalIssueCount = draft.validationIssues.filter((issue) => issue.severity === "Critical").length;
  const warningIssueCount = draft.validationIssues.filter((issue) => issue.severity === "Warning").length;
  const isBalanced = Math.abs(draft.imbalance) === 0;
  const commandByAction = new Map(commands.map((command) => [command.action, command]));
  const lifecycleState = new Set([draft.status, ...transitions.map((transition) => transition.title)]);
  const approved = draft.status === "Approved" || draft.status === "Posted" || draft.status === "Reversed" || draft.status === "Rebooked" || draft.status === "CloseLocked" || Boolean(draft.approvedAtUtc);
  const posted = draft.status === "Posted" || draft.status === "Reversed" || draft.status === "Rebooked" || draft.status === "CloseLocked" || Boolean(draft.postedAtUtc);
  const reversed = draft.status === "Reversed" || Boolean(draft.reversalOfJournalEntryId) || correctionRows.some((row) => /reversal/i.test(row.sourceLabel));
  const rebooked = draft.status === "Rebooked" || Boolean(draft.rebookedFromJournalEntryId) || correctionRows.some((row) => /rebook/i.test(row.sourceLabel));
  const locked = draft.status === "CloseLocked" || Boolean(draft.closedLockedAtUtc);

  const commandReadiness = (action: JournalEntryLifecycleAction): { value: string; detail: string; tone: AccountingToolingTone } => {
    const command = commandByAction.get(action);
    if (!command) {
      return { value: "Unavailable", detail: "Lifecycle command is not surfaced for this journal entry.", tone: "default" };
    }

    if (command.disabledReason) {
      return { value: "Blocked", detail: command.disabledReason, tone: "warning" };
    }

    return { value: "Ready", detail: command.description, tone: command.tone === "danger" ? "danger" : command.tone === "success" ? "success" : "warning" };
  };
  const approveReadiness = commandReadiness("Approve");
  const postReadiness = commandReadiness("Post");
  const reverseReadiness = commandReadiness("Reverse");
  const rebookReadiness = commandReadiness("Rebook");
  const lockReadiness = commandReadiness("LockAfterClose");

  return [
    {
      id: "draft",
      label: "Draft",
      value: draft.version > 0 ? `v${draft.version}` : "Unsaved",
      detail: draft.version > 0
        ? `Retained draft ${draft.journalEntryId} is loaded for ${draft.accountingDate}.`
        : "Save the draft before relying on version-guarded lifecycle actions.",
      tone: draft.version > 0 ? "success" : "warning"
    },
    {
      id: "validate",
      label: "Validate",
      value: criticalIssueCount > 0 ? `${criticalIssueCount} critical` : warningIssueCount > 0 ? `${warningIssueCount} warning` : "Clear",
      detail: criticalIssueCount > 0
        ? "Critical validation issues block approval and posting."
        : warningIssueCount > 0
          ? "Warnings are retained for reviewer attention."
          : isBalanced
            ? "No validation issues are attached and debits equal credits."
            : `Entry is out of balance by ${formatCurrencyWithCode(Math.abs(draft.imbalance), draft.currency)}.`,
      tone: criticalIssueCount > 0 ? "danger" : warningIssueCount > 0 || !isBalanced ? "warning" : "success"
    },
    {
      id: "evidence",
      label: "Attach evidence",
      value: evidenceCount > 0 ? formatCount(evidenceCount, "evidence link") : "Missing",
      detail: evidenceCount > 0
        ? "Source support is retained on the header or line evidence set."
        : "Approval, posting, reversal, rebook, and close-lock transitions require retained evidence.",
      tone: evidenceCount > 0 ? "success" : "warning"
    },
    {
      id: "submit",
      label: "Submit",
      value: draft.status === "Submitted" || approved || posted ? "Submitted" : draft.status === "Draft" ? "Draft" : draft.status,
      detail: draft.status === "Submitted" || approved || posted
        ? "Journal entry has crossed the approval submission gate."
        : "Use Submit approval after validation, balance, and evidence are ready.",
      tone: draft.status === "Submitted" || approved || posted ? "success" : "warning"
    },
    {
      id: "approve",
      label: "Approve",
      value: approved ? "Approved" : approveReadiness.value,
      detail: approved
        ? `${draft.approvedBy ?? "Reviewer"} approved the entry${draft.approvedAtUtc ? ` on ${formatDateTimeLabel(draft.approvedAtUtc)}` : ""}.`
        : approveReadiness.detail,
      tone: approved ? "success" : approveReadiness.tone
    },
    {
      id: "post",
      label: "Post",
      value: posted ? "Posted" : postReadiness.value,
      detail: posted
        ? `${draft.postedBy ?? "Operator"} posted the immutable entry${draft.postedAtUtc ? ` on ${formatDateTimeLabel(draft.postedAtUtc)}` : ""}.`
        : postReadiness.detail,
      tone: posted ? "success" : postReadiness.tone
    },
    {
      id: "reverse",
      label: "Reverse",
      value: reversed ? "Linked" : reverseReadiness.value,
      detail: reversed
        ? "A reversal relationship or generated reversal draft is retained; the posted entry was not edited in place."
        : reverseReadiness.detail,
      tone: reversed ? "success" : reverseReadiness.tone
    },
    {
      id: "rebook",
      label: "Rebook",
      value: rebooked ? "Linked" : rebookReadiness.value,
      detail: rebooked
        ? "A rebook relationship or generated rebook draft is retained; the original posted entry remains immutable."
        : rebookReadiness.detail,
      tone: rebooked ? "success" : rebookReadiness.tone
    },
    {
      id: "lock-after-close",
      label: "Lock after close",
      value: locked ? "Locked" : lockReadiness.value,
      detail: locked
        ? `${draft.closeLockedBy ?? "Close controller"} locked the entry after close${draft.closedLockedAtUtc ? ` on ${formatDateTimeLabel(draft.closedLockedAtUtc)}` : ""}.`
        : lockReadiness.detail,
      tone: locked ? "success" : lockReadiness.tone
    },
    {
      id: "audit",
      label: "Audit transitions",
      value: transitions.length > 0 ? formatCount(transitions.length, "transition") : "None",
      detail: transitions.length > 0
        ? "Lifecycle transitions retain audit id, correlation id, actor, notes, and evidence routes."
        : "No lifecycle transition audit rows are retained on this journal entry yet.",
      tone: transitions.length > 0 ? "success" : lifecycleState.has("Draft") ? "warning" : "default"
    }
  ];
}

function lifecycleCommand(
  action: JournalEntryLifecycleAction,
  label: string,
  description: string,
  disabledReason: string | null,
  tone: ManualJournalLifecycleCommandViewModel["tone"],
  busyAction: JournalEntryLifecycleAction | null
): ManualJournalLifecycleCommandViewModel {
  return {
    action,
    label,
    description,
    disabledReason: busyAction && busyAction !== action ? `Lifecycle action ${busyAction} is already running.` : disabledReason,
    tone,
    busy: busyAction === action
  };
}

function formatManualJournalLifecycleTransition(
  transition: JournalEntryLifecycleTransition
): ManualJournalLifecycleTransitionViewModel {
  const evidenceRows = transition.evidenceLinks.length > 0
    ? transition.evidenceLinks
    : ["No transition evidence links retained."];

  return {
    id: transition.transitionId,
    title: `${transition.action}: ${transition.fromStatus} -> ${transition.toStatus}`,
    detail: `${transition.actor} / ${formatDateTimeLabel(transition.recordedAtUtc)}${transition.notes ? ` / ${transition.notes}` : ""}`,
    auditLabel: `Audit ${transition.transitionId}`,
    correlationLabel: transition.correlationId ? `Correlation ${transition.correlationId}` : "No correlation id retained",
    evidenceLabel: transition.evidenceLinks.length > 0
      ? `${transition.evidenceLinks.length.toLocaleString()} evidence link(s)`
      : "No transition evidence links",
    evidenceTone: transition.evidenceLinks.length > 0 ? "success" : "outline",
    evidenceRows
  };
}

function formatManualJournalLifecycleCorrection(
  draft: ManualJournalEntryDraft
): ManualJournalLifecycleCorrectionViewModel {
  const source = draft.reversalOfJournalEntryId
    ? `Reversal of ${draft.reversalOfJournalEntryId}`
    : draft.rebookedFromJournalEntryId
      ? `Rebook from ${draft.rebookedFromJournalEntryId}`
      : "Generated correction draft";

  return {
    id: draft.journalEntryId,
    title: draft.memo || "Generated correction draft",
    subtitle: `${draft.status} / v${draft.version} / ${draft.entryType}`,
    balanceLabel: `${formatCurrencyWithCode(draft.totalDebits, draft.currency)} debit / ${formatCurrencyWithCode(draft.totalCredits, draft.currency)} credit`,
    sourceLabel: source
  };
}

function createManualJournalAttachmentDraft(): ManualJournalEvidenceAttachmentDraft {
  return {
    displayName: "",
    uri: "",
    evidenceKind: "SourceDocument",
    sourceSystem: "ManualUpload",
    lineId: null,
    description: ""
  };
}

function createManualJournalEntryLine(
  side: AccountingTemplateLineSide,
  currency: string,
  accountPath: string
): ManualJournalEntryLine {
  return {
    lineId: newClientId(),
    side,
    amount: 0,
    currency,
    accountPath,
    entityId: null,
    fundAllocationId: null,
    securityId: null,
    securityDisplayName: null,
    taxLotId: null,
    description: null,
    evidenceLink: null
  };
}

function newClientId(): string {
  if (typeof crypto !== "undefined" && typeof crypto.randomUUID === "function") {
    return crypto.randomUUID();
  }

  return `manual-je-${Date.now()}-${Math.round(Math.random() * 1_000_000)}`;
}

export function useAccountingReconciliationViewModel(
  data: AccountingWorkspaceResponse | null,
  workstream: AccountingWorkstream,
  services: AccountingReconciliationServices = defaultAccountingReconciliationServices
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

  const refreshStatementRuns = useCallback(() => {
    setStatementRunsLoading(true);
    setStatementRunsError(null);

    services.getStatementRuns()
      .then((runs) => {
        setStatementRuns(runs);
      })
      .catch((err) => {
        setStatementRuns([]);
        setStatementRunsError(describeApiError(err, "Statement runs failed to load."));
      })
      .finally(() => {
        setStatementRunsLoading(false);
      });
  }, [services]);

  useEffect(() => {
    if (workstream !== "reconciliation" && workstream !== "exceptions") {
      return;
    }

    let cancelled = false;
    setStatementRunsLoading(true);
    setStatementRunsError(null);

    services.getStatementRuns()
      .then((runs) => {
        if (!cancelled) {
          setStatementRuns(runs);
        }
      })
      .catch((err) => {
        if (!cancelled) {
          setStatementRuns([]);
          setStatementRunsError(describeApiError(err, "Statement runs failed to load."));
        }
      })
      .finally(() => {
        if (!cancelled) {
          setStatementRunsLoading(false);
        }
      });

    return () => {
      cancelled = true;
    };
  }, [services, workstream]);

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
      const updated = await services.reviewBreak({ breakId, assignedTo: "ops.gov", reviewedBy: "ops.gov" });
      setBreakQueue((current) => replaceBreakQueueItem(current, updated));
    } catch (err) {
      setBreakActionError(describeApiError(err, "Break assignment failed."));
    } finally {
      setBreakAction(null);
    }
  }, [services]);

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
      const updated = await services.resolveBreak({
        breakId,
        status,
        resolvedBy: "ops.gov",
        resolutionNote: "Reviewed in accounting panel.",
        operatorRationale: trimmedRationale
      });
      setBreakQueue((current) => replaceBreakQueueItem(current, updated));
    } catch (err) {
      setBreakActionError(describeApiError(err, "Break resolution failed."));
    } finally {
      setBreakAction(null);
    }
  }, [services]);

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
      cashFlow: data?.cashFlow ?? null
    }),
    [data?.cashFlow, reconciliationQueue, selectedRunId, statementRuns]
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
          ? "Requesting preview"
          : hasError
            ? "Request failed"
            : hasPreview
              ? "Preview ready"
              : "Ready for request";

      const statusText = !hasSelection
        ? "Select a reconciliation run before previewing accounting transaction impact."
        : transactionLabBusy
          ? "Requesting Transaction Lab preview from the shared endpoint."
          : hasError
            ? transactionLabError?.summary ?? "Transaction Lab preview failed."
            : hasPreview
              ? `Preview ${transactionLabPreview?.previewId ?? ""} loaded from shared endpoint calculations.`
              : "Ready to preview accounting impact through the shared Transaction Lab endpoint.";

      const impactRows = transactionLabPreview?.trialBalanceImpact.map((row, index) => ({
        id: `${row.accountName}-${index}`,
        label: row.accountName,
        value: formatSignedCurrency(row.balanceDelta),
        tone: row.balanceDelta > 0 ? "success" as const : row.balanceDelta < 0 ? "danger" as const : "default" as const
      })) ?? [];

      return {
        title: "Investment Accounting Transaction Lab",
        description: "Preview accounting journal impact before committing ledger or reconciliation changes.",
        statusTone,
        requestSummaryLabel,
        statusRole: hasError ? "alert" as const : "status" as const,
        statusText,
        journalLineCountLabel: hasPreview && transactionLabPreview
          ? formatCount(transactionLabPreview.journalPreview.lines.length, "line")
          : "Pending preview",
        ledgerImpactLabel: hasPreview && transactionLabPreview
          ? formatSignedCurrency(transactionLabPreview.ledgerImpact.netBalanceDelta)
          : "Pending preview",
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
          : "Select a reconciliation run before requesting a Transaction Lab preview.",
        busy: transactionLabBusy,
        previewButtonLabel: transactionLabBusy ? "Previewing accounting impact..." : "Preview accounting impact",
        previewButtonAriaLabel: "Preview accounting transaction impact"
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

export function resolveAccountingWorkstream(pathname: string): AccountingWorkstream {
  if (pathname.startsWith(WORKSTATION_ROUTE_CATALOG.reporting)) {
    return "reporting";
  }

  if (pathname.includes("/configure")) {
    return "configure";
  }

  if (pathname.includes("/journal-entries")) {
    return "journal-entries";
  }

  if (pathname.includes("/capital-accounts")) {
    return "capital-accounts";
  }

  if (pathname.includes("/reconciliation")) {
    return "reconciliation";
  }

  if (pathname.includes("/exceptions")) {
    return "exceptions";
  }

  if (pathname.includes("/security-master")) {
    return "security-master";
  }

  if (pathname.includes("/approvals")) {
    return "approvals";
  }

  return "ledger";
}

export function resolveGovernanceWorkstream(pathname: string): AccountingWorkstream {
  return resolveAccountingWorkstream(pathname);
}

export function resolveSelectedReconciliation(
  queue: AccountingWorkspaceResponse["reconciliationQueue"],
  selectedRunId: string | null
) {
  if (!selectedRunId) {
    return queue[0] ?? null;
  }

  return queue.find((item) => item.runId === selectedRunId) ?? null;
}

export function buildReconciliationDetailActions(
  item: AccountingWorkspaceResponse["reconciliationQueue"][number]
): ReconciliationDetailActionsViewModel {
  const openBreakLabel = `${item.openBreakCount} open break${item.openBreakCount === 1 ? "" : "s"}`;

  return {
    breakChecklistTargetId: "reconciliation-break-queue",
    breakChecklistHref: "#reconciliation-break-queue",
    breakChecklistLabel: "Open break checklist",
    breakChecklistAriaLabel: `Open break checklist for ${item.strategyName}; ${openBreakLabel}`,
    evidencePacketHref: evidenceWorkbenchPath("reconciliation-review", item.runId),
    evidencePacketLabel: "Evidence packet",
    evidencePacketAriaLabel: `Open reconciliation evidence packet for ${item.strategyName}`,
    auditPacketHref: getRunReviewPacketPath(item.runId),
    auditPacketLabel: "Review audit packet",
    auditPacketAriaLabel: `Review audit packet for ${item.strategyName}`
  };
}

export function buildReconciliationDetailViewState(
  item: AccountingWorkspaceResponse["reconciliationQueue"][number]
): ReconciliationDetailViewState {
  const openBreakTone: CashFlowEvidenceTone = item.openBreakCount === 0 ? "success" : "warning";
  const fields: ReconciliationDetailFieldViewModel[] = [
    buildReconciliationDetailField("Mode", item.mode.toUpperCase(), "default"),
    buildReconciliationDetailField("Run status", item.status, "default"),
    buildReconciliationDetailField("Break count", String(item.breakCount), "default"),
    buildReconciliationDetailField("Open breaks", String(item.openBreakCount), openBreakTone),
    buildReconciliationDetailField("Last updated", item.lastUpdated, "default")
  ];

  return {
    eyebrow: "Reconciliation detail",
    title: item.strategyName,
    description: `${item.runId} is currently ${item.reconciliationStatus}.`,
    ariaLabel: `Reconciliation detail for ${item.strategyName}`,
    narrative: buildReconciliationNarrative(item),
    narrativeLabel: `Reconciliation narrative for ${item.strategyName}`,
    fields
  };
}

interface ReconciliationStatementRunsBuildInput {
  statementRuns: StatementRunSummary[];
  fallbackQueue: AccountingWorkspaceResponse["reconciliationQueue"];
  selectedRunId: string | null;
  loading: boolean;
  error: ApiErrorDisplay | null;
}

interface ReconciliationComparisonBuildInput {
  statementRuns: StatementRunSummary[];
  fallbackQueue: AccountingWorkspaceResponse["reconciliationQueue"];
  selectedRunId: string | null;
  cashFlow: AccountingCashFlowSummary | null;
}

export function buildReconciliationStatementRunsViewState({
  statementRuns,
  fallbackQueue,
  selectedRunId,
  loading,
  error
}: ReconciliationStatementRunsBuildInput): ReconciliationStatementRunsViewState {
  const detailPanelId = "statement-run-detail-tabs";
  const fallbackRows: StatementRunSummaryWithMetadata[] = statementRuns.length > 0
    ? []
    : fallbackQueue.map((item): StatementRunSummaryWithMetadata => ({
      runId: item.runId,
      importId: item.runId,
      startedAtUtc: item.lastUpdated,
      completedAtUtc: item.lastUpdated,
      positionMatches: 0,
      cashMatches: 0,
      transactionMatches: 0,
      openExceptionCount: item.openBreakCount,
      status: item.reconciliationStatus,
      breakCount: item.breakCount,
      caseCount: item.openBreakCount,
      importedAtUtc: item.lastUpdated
    }));
  const sourceRows: StatementRunSummaryWithMetadata[] = statementRuns.length > 0 ? statementRuns : fallbackRows;
  const effectiveSelectedRunId = selectedRunId ?? sourceRows[0]?.runId ?? null;
  const rows = sourceRows.map((run) => buildStatementRunRow(run, effectiveSelectedRunId, detailPanelId));
  const selected = sourceRows.find((run) => run.runId === effectiveSelectedRunId) ?? null;

  return {
    title: "Statement runs",
    description: "Broker and custodian statement imports stay anchored to shared reconciliation endpoint data; React only presents counts supplied by the catalog/read-model seam.",
    tableLabel: "Accounting statement runs",
    tableCaption: "Statement run list with broker or custodian, account, period, status, validation issue count, match count, break count, case count, and imported timestamp.",
    detailPanelId,
    emptyText: "No broker or custodian statement runs are available for this accounting period.",
    loadingText: loading ? "Loading statement runs from the reconciliation endpoint." : null,
    errorText: error?.summary ?? null,
    errorDetails: error?.details ?? [],
    recoveryActionLabel: "Retry statement runs",
    recoveryActionAriaLabel: "Retry loading Accounting statement runs",
    statusAnnouncement: loading
      ? "Statement runs loading."
      : error
        ? "Statement runs failed to load."
        : `${rows.length} statement run${rows.length === 1 ? "" : "s"} available.`,
    hasRows: rows.length > 0,
    rows,
    tabs: buildReconciliationRunDetailTabs(selected)
  };
}

export function buildReconciliationComparisonViewState({
  statementRuns,
  fallbackQueue,
  selectedRunId,
  cashFlow
}: ReconciliationComparisonBuildInput): ReconciliationComparisonViewState {
  const fallbackRows: StatementRunSummaryWithMetadata[] = statementRuns.length > 0
    ? []
    : fallbackQueue.map((item): StatementRunSummaryWithMetadata => ({
      runId: item.runId,
      importId: item.runId,
      startedAtUtc: item.lastUpdated,
      completedAtUtc: item.lastUpdated,
      positionMatches: 0,
      cashMatches: Math.max(item.breakCount - item.openBreakCount, 0),
      transactionMatches: 0,
      openExceptionCount: item.openBreakCount,
      brokerCustodian: item.strategyName,
      account: item.mode.toUpperCase(),
      period: item.lastUpdated,
      status: item.reconciliationStatus,
      breakCount: item.breakCount,
      caseCount: item.openBreakCount,
      importedAtUtc: item.lastUpdated
    }));
  const sourceRows: StatementRunSummaryWithMetadata[] = statementRuns.length > 0 ? statementRuns : fallbackRows;
  const effectiveSelectedRunId = selectedRunId ?? sourceRows[0]?.runId ?? null;
  const sortedRows = [
    ...sourceRows.filter((row) => row.runId === effectiveSelectedRunId),
    ...sourceRows.filter((row) => row.runId !== effectiveSelectedRunId)
  ].slice(0, 4);
  const rows = sortedRows.map((run, index): ReconciliationComparisonRowViewModel => {
    const matchCount = run.matchCount ?? run.positionMatches + run.cashMatches + run.transactionMatches;
    const openCount = run.openExceptionCount;
    const statusLabel = run.status ?? (openCount > 0 ? "Open" : "Matched");
    const brokerCustodian = run.brokerCustodian?.trim() || `Statement ${index + 1}`;
    const account = run.account?.trim() || run.importId;
    const period = run.period?.trim() || run.completedAtUtc || run.startedAtUtc;
    const queueMatch = fallbackQueue.find((item) => item.runId === run.runId);
    const ledgerTitle = queueMatch?.strategyName ?? "Meridian ledger";
    const ledgerMeta = [
      run.runId,
      matchCount.toLocaleString() + " matched",
      openCount > 0 ? openCount.toLocaleString() + " open" : "no open breaks"
    ].join(" · ");

    return {
      id: run.runId,
      statementTitle: brokerCustodian,
      statementMeta: `${period} · ${account}`,
      statementValue: index === 0 && cashFlow ? formatCurrency(cashFlow.totalCash) : `${matchCount.toLocaleString()} matched`,
      ledgerTitle,
      ledgerMeta,
      ledgerValue: index === 0 && cashFlow ? formatCurrency(cashFlow.totalLedgerCash) : (openCount > 0 ? `${openCount.toLocaleString()} open` : "Matched"),
      statusLabel,
      statusTone: openCount > 0 || statusLabel === "BreaksOpen" || statusLabel === "SecurityCoverageOpen" ? "warning" : "success"
    };
  });
  const matchedCount = sourceRows.reduce((total, run) => total + (run.matchCount ?? run.positionMatches + run.cashMatches + run.transactionMatches), 0);
  const openCount = sourceRows.reduce((total, run) => total + run.openExceptionCount, 0);
  const variance = cashFlow?.netVariance ?? 0;

  return {
    title: "Cash reconciliation - broker statement vs. ledger",
    subtitle: cashFlow?.summary ?? "Broker and custodian statements are compared with Meridian ledger balances from shared reconciliation read models.",
    statementHeading: "Statement",
    ledgerHeading: "Ledger",
    matchedBadgeLabel: `${matchedCount.toLocaleString()} matched`,
    openBadgeLabel: `${openCount.toLocaleString()} open`,
    statementBalanceLabel: cashFlow ? formatCurrency(cashFlow.totalCash) : "Not loaded",
    ledgerBalanceLabel: cashFlow ? formatCurrency(cashFlow.totalLedgerCash) : "Not loaded",
    varianceLabel: variance === 0 ? "Balanced" : `Out by ${formatCurrency(Math.abs(variance))}`,
    varianceTone: variance === 0 ? "success" : "warning",
    rows,
    ariaLabel: "Cash reconciliation broker statement versus ledger comparison"
  };
}

function buildStatementRunRow(
  run: StatementRunSummaryWithMetadata,
  selectedRunId: string | null,
  detailPanelId: string
): ReconciliationStatementRunRowViewModel {
  const matchCount = run.matchCount ?? run.positionMatches + run.cashMatches + run.transactionMatches;
  const status = run.status ?? (run.openExceptionCount > 0 ? "ReviewRequired" : "Matched");
  const missing: string[] = [];
  const brokerCustodianLabel = valueOrMissing(run.brokerCustodian, "Broker/custodian", missing);
  const accountLabel = valueOrMissing(run.account, "Account", missing);
  const periodLabel = valueOrMissing(run.period, "Period", missing);
  const validationIssueCount = run.validationIssueCount ?? run.openExceptionCount;
  const breakCount = run.breakCount ?? run.openExceptionCount;
  const caseCount = run.caseCount ?? run.openExceptionCount;
  const importedAtLabel = run.importedAtUtc ?? run.completedAtUtc ?? run.startedAtUtc;

  return {
    runId: run.runId,
    brokerCustodianLabel,
    accountLabel,
    periodLabel,
    statusLabel: status,
    validationIssueCountLabel: String(validationIssueCount),
    matchCountLabel: String(matchCount),
    breakCountLabel: String(breakCount),
    caseCountLabel: String(caseCount),
    importedAtLabel,
    isSelected: run.runId === selectedRunId,
    controlsId: detailPanelId,
    ariaLabel: `Statement run ${run.runId}. ${status}. ${validationIssueCount} validation issues, ${matchCount} matches, ${breakCount} breaks, ${caseCount} cases. Imported ${importedAtLabel}.`,
    selectAriaLabel: `Inspect statement run ${run.runId}`,
    unavailableReason: missing.length > 0 ? `${missing.join(", ")} not provided by statement run payload.` : null
  };
}

function valueOrMissing(value: string | null | undefined, label: string, missing: string[]): string {
  const trimmed = value?.trim();
  if (trimmed) {
    return trimmed;
  }

  missing.push(label);
  return "—";
}

function buildReconciliationRunDetailTabs(run: StatementRunSummary | null): ReconciliationRunDetailTabViewModel[] {
  const disabledReason = run ? null : "Select a statement run before opening this detail tab.";
  const matchCount = run ? run.matchCount ?? run.positionMatches + run.cashMatches + run.transactionMatches : 0;
  const openExceptionCount = run?.openExceptionCount ?? 0;
  const tabs: Array<{ id: ReconciliationRunDetailTabId; label: string; badgeLabel: string | null; description: string }> = [
    { id: "overview", label: "Overview", badgeLabel: run?.status ?? null, description: "Statement source, account coverage, import timing, and endpoint-supplied reconciliation posture." },
    { id: "validation", label: "Validation", badgeLabel: run ? String(run.validationIssueCount ?? openExceptionCount) : null, description: "Validation issues reported by the shared statement reconciliation run." },
    { id: "positions", label: "Positions", badgeLabel: run ? String(run.positionMatches) : null, description: "Position match totals supplied by the reconciliation service." },
    { id: "cash", label: "Cash", badgeLabel: run ? String(run.cashMatches) : null, description: "Cash match totals supplied by the reconciliation service." },
    { id: "transactions", label: "Transactions", badgeLabel: run ? String(run.transactionMatches) : null, description: "Transaction match totals supplied by the reconciliation service." },
    { id: "breaks-cases", label: "Breaks & Cases", badgeLabel: run ? String(run.breakCount ?? openExceptionCount) : null, description: "Break and case counts from reconciliation/casework read models; no case-state logic runs in React." },
    { id: "evidence", label: "Evidence", badgeLabel: run ? String(matchCount) : null, description: "Evidence packet and imported statement references available through shared endpoint clients." }
  ];

  return tabs.map((tab) => ({
    ...tab,
    disabled: !run,
    disabledReason,
    ariaLabel: run
      ? `${tab.label} tab for statement run ${run.runId}. ${tab.description}`
      : `${tab.label} tab unavailable. ${disabledReason}`
  }));
}

export function buildReconciliationQueuePanelViewState(
  queue: AccountingWorkspaceResponse["reconciliationQueue"],
  selectedRunId: string | null
): ReconciliationQueuePanelViewState {
  const detailPanelId = "reconciliation-run-detail-panel";
  const effectiveSelectedRunId = selectedRunId ?? queue[0]?.runId ?? null;

  return {
    title: "Reconciliation detail queue",
    description: "Select a run to inspect its active reconciliation detail panel.",
    overviewTitle: "Reconciliation queue",
    overviewDescription: "Open breaks, timing drift, and balanced runs stay visible without leaving Accounting.",
    overviewCaption: "Read-only reconciliation queue summary. Open the reconciliation workstream to inspect selected run detail.",
    overviewActionHref: WORKSTATION_ROUTE_CATALOG.accountingReconciliation,
    overviewActionLabel: "Open reconciliation",
    overviewActionAriaLabel: "Open Accounting reconciliation workstream",
    listLabel: "Reconciliation runs",
    emptyText: "No reconciliation runs are available for this accounting scope.",
    detailPanelId,
    detailEmptyTitle: "No reconciliation run selected",
    detailEmptyText: "Reconciliation evidence is unavailable until the workspace payload includes at least one run.",
    detailEmptyAriaLabel: "No reconciliation run selected",
    hasRows: queue.length > 0,
    rows: queue.map((item) => {
      const isSelected = item.runId === effectiveSelectedRunId;
      return {
        runId: item.runId,
        strategyName: item.strategyName,
        modeLabel: item.mode.toUpperCase(),
        runStatusLabel: item.status,
        reconciliationStatusLabel: item.reconciliationStatus,
        reconciliationTone: reconciliationStatusTone(item.reconciliationStatus),
        breakCountLabel: `${item.breakCount} break${item.breakCount === 1 ? "" : "s"}`,
        openBreakLabel: `${item.openBreakCount} open`,
        lastUpdatedLabel: item.lastUpdated,
        isSelected,
        isExpanded: isSelected,
        controlsId: detailPanelId,
        ariaLabel: `${item.strategyName}. ${item.reconciliationStatus}. ${item.openBreakCount} open breaks. Updated ${item.lastUpdated}.`,
        selectAriaLabel: `Inspect reconciliation run ${item.strategyName}`
      };
    })
  };
}

function reconciliationStatusTone(
  status: AccountingWorkspaceResponse["reconciliationQueue"][number]["reconciliationStatus"]
): ReconciliationQueueRunTone {
  if (status === "Balanced") {
    return "success";
  }

  if (status === "Resolved") {
    return "primary";
  }

  if (status === "NotStarted") {
    return "muted";
  }

  return "warning";
}

function buildReconciliationDetailField(
  label: string,
  value: string,
  tone: CashFlowEvidenceTone
): ReconciliationDetailFieldViewModel {
  return {
    label,
    value,
    tone,
    ariaLabel: `${label}: ${value}`
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
  const selectedName = selectedDisplayName?.trim() || selectedSecurityId || "None selected";
  const selectedClass = selectedAssetClass?.trim() || "Unclassified";
  const statusLabel = selectedStatus?.trim() || (selectedSecurityId ? "Pending" : "No selection");
  const identifiersLabel = identity
    ? formatCount(identity.identifiers.length, "identifier")
    : identityLoading
      ? "Loading identifiers"
      : "No identifiers loaded";
  const aliasesLabel = identity ? formatCount(identity.aliases.length, "alias") : "No aliases loaded";
  const corporateActionLabel = corporateActions
    ? formatCount(corporateActions.length, "corporate action")
    : selectedSecurityId
      ? "Loading schedules"
      : "No selection";
  const referenceDataLabel = referenceDataError
    ? "Error"
    : referenceDataLoading
      ? "Loading"
      : referenceDataCoverage
        ? formatCount(referenceDataCoverage.endpoints.length, "route")
        : selectedSecurityId
          ? "Pending"
          : "No selection";
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
        ? formatCount(openLotReadModel.lots.length, "lot")
        : selectedSecurityId
          ? "No lots"
          : "No selection";

  return {
    ariaLabel: "Security Master command deck",
    eyebrow: "Security Master",
    title: "Security Master command deck",
    description: "Search, inspect, and reconcile trusted security reference records from one dense master-detail page.",
    metrics: [
      {
        id: "results",
        label: "Search results",
        value: hasQuery ? resultCount.toLocaleString() : "Search",
        detail: hasQuery ? `${formatCount(resultCount, "security")} returned for the active query.` : "Search by ticker, ISIN, CUSIP, FIGI, or display name.",
        tone: resultCount > 0 ? "success" : "default"
      },
      {
        id: "selected",
        label: "Selected detail",
        value: selectedName,
        detail: selectedSecurityId ? `Security ID ${selectedSecurityId}` : "Select a table row to open the security detail page.",
        tone: selectedSecurityId ? "success" : "default"
      },
      {
        id: "conflicts",
        label: "Identifier conflicts",
        value: conflictsLoading ? "Loading" : openConflictCount.toLocaleString(),
        detail: conflictsLoading
          ? "Refreshing provider conflict evidence."
          : openConflictCount > 0
            ? `${formatCount(openConflictCount, "open conflict")} requiring operator review.`
            : "No open conflicts need operator review.",
        tone: openConflictCount > 0 || conflictsLoading ? "warning" : "success"
      },
      {
        id: "detail",
        label: "Detail coverage",
        value: selectedSecurityId ? statusLabel : "No selection",
        detail: selectedSecurityId ? `${selectedClass} detail record with ${identifiersLabel}.` : "Overview, schedules, controls, lots, and audit cues stay attached to the selected security.",
        tone: selectedSecurityId ? (statusLabel.toLowerCase() === "active" ? "success" : "warning") : "default"
      }
    ],
    detailEyebrow: "Security detail",
    detailTitle: "Security detail page",
    detailSubtitle: selectedSecurityId ? `${selectedSecurityId} · ${selectedClass}` : "Select a security",
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
    subtitle: `${identity.securityId} · v${identity.version} · ${identity.assetClass || "—"}`,
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
      fieldLabel: conflict.fieldPath,
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

export function buildReconciliationBreakQueueState({
  breakQueue,
  selectedBreakId,
  loading,
  loadError,
  action,
  actionError
}: {
  breakQueue: ReconciliationBreakQueueItem[];
  selectedBreakId?: string | null;
  loading: boolean;
  loadError: string | ApiErrorDisplay | null;
  action: ReconciliationBreakAction | null;
  actionError: string | ApiErrorDisplay | null;
}): ReconciliationBreakQueueState {
  const effectiveSelectedBreakId = selectedBreakId && breakQueue.some((item) => item.breakId === selectedBreakId)
    ? selectedBreakId
    : breakQueue[0]?.breakId ?? null;
  const rows = buildReconciliationBreakRows(breakQueue, action, effectiveSelectedBreakId);
  const selectedRow = rows.find((row) => row.breakId === effectiveSelectedBreakId) ?? null;
  const loadingText = loading ? "Loading reconciliation break queue..." : null;
  const normalizedLoadError = normalizeApiErrorDisplay(loadError);
  const normalizedActionError = normalizeApiErrorDisplay(actionError);
  const errorText = normalizedLoadError
    ? normalizedLoadError.summary.startsWith("Reconciliation break queue failed")
      ? normalizedLoadError.summary
      : `Reconciliation break queue failed: ${normalizedLoadError.summary}`
    : null;
  const actionErrorText = normalizedActionError
    ? normalizedActionError.summary.startsWith("Break ")
      ? normalizedActionError.summary
      : `Break action failed: ${normalizedActionError.summary}`
    : null;

  return {
    rows,
    hasBreaks: rows.length > 0,
    tableLabel: "Reconciliation break queue",
    tableCaption: "Selectable reconciliation break queue. Select a break row to inspect reason, ownership, audit timestamps, and routing detail.",
    detailPanelId: "reconciliation-break-detail-panel",
    selectedBreakId: effectiveSelectedBreakId,
    selectedDetail: selectedRow ? buildReconciliationBreakDetail(selectedRow) : null,
    detailEmptyTitle: "No reconciliation break selected",
    detailEmptyText: "Break detail is unavailable until the queue includes at least one active or historical break.",
    detailEmptyAriaLabel: "No reconciliation break selected",
    loadingText,
    emptyText: "No reconciliation breaks in the current queue.",
    errorText,
    errorDetails: normalizedLoadError?.details ?? [],
    actionErrorText,
    actionErrorDetails: normalizedActionError?.details ?? [],
    statusAnnouncement: buildReconciliationBreakStatusAnnouncement({
      loading,
      action,
      loadError: errorText,
      actionError: actionErrorText,
      breakCount: rows.length
    })
  };
}

export function buildOperationalExceptionWorkbenchState({
  reconciliationQueue,
  breakRows
}: {
  reconciliationQueue: AccountingWorkspaceResponse["reconciliationQueue"];
  breakRows: ReconciliationBreakRowViewModel[];
}): OperationalExceptionWorkbenchViewState {
  const activeBreaks = breakRows.filter((row) => row.status === "Open" || row.status === "InReview");
  const openRunBreakCount = reconciliationQueue.reduce((total, run) => total + run.openBreakCount, 0);
  const commentCount = breakRows.reduce((total, row) => total + (row.commentCount ?? 0), 0);
  const evidenceCount = breakRows.reduce((total, row) => total + (row.evidenceCount ?? 0), 0);
  const signoffCount = breakRows.filter((row) => (row.signoffStatus ?? "").trim() || row.status === "Resolved" || row.status === "SignedOff").length;
  const cases = activeBreaks.length > 0 ? activeBreaks : breakRows.slice(0, 5);

  return {
    title: "Operational exception workbench",
    description: "Unified review for reconciliation breaks, workflow state, comments, audit evidence, and approval handoffs.",
    metricRows: [
      {
        id: "active-breaks",
        label: "Active exceptions",
        value: String(activeBreaks.length),
        detail: `${openRunBreakCount} open break${openRunBreakCount === 1 ? "" : "s"} across reconciliation runs.`,
        tone: activeBreaks.length > 0 ? "warning" : "success"
      },
      {
        id: "comments",
        label: "Comments",
        value: String(commentCount),
        detail: "Comment counts come from shared reconciliation casework metadata.",
        tone: commentCount > 0 ? "default" : "warning"
      },
      {
        id: "audit-evidence",
        label: "Audit evidence",
        value: String(evidenceCount),
        detail: "Evidence links stay attached to the originating break and reporting packet.",
        tone: evidenceCount > 0 ? "success" : "warning"
      },
      {
        id: "signoff",
        label: "Sign-off states",
        value: String(signoffCount),
        detail: "Resolved, signed-off, or role-gated cases are routed back to Accounting approvals.",
        tone: signoffCount > 0 ? "default" : "warning"
      }
    ],
    cases: cases.map((row) => ({
      id: row.breakId,
      title: `${row.strategyName} / ${row.category}`,
      subtitle: `${row.breakId} - ${row.reason}`,
      statusLabel: row.status,
      statusTone: row.statusBadgeVariant,
      ownerLabel: row.ownerLabel,
      slaLabel: row.slaBadgeLabel ?? buildReconciliationSlaText(row),
      commentLabel: `${row.commentCount ?? 0} comment${(row.commentCount ?? 0) === 1 ? "" : "s"}`,
      auditLabel: `${row.evidenceCount ?? 0} evidence link${(row.evidenceCount ?? 0) === 1 ? "" : "s"}`,
      routeHref: buildReconciliationBreakRoutingHref(row.routingTarget) ?? WORKSTATION_ROUTE_CATALOG.accountingReconciliation,
      routeLabel: row.routingTarget ? "Open routed workflow" : "Open reconciliation",
      ariaLabel: `Operational exception ${row.breakId}. ${row.status}. ${row.reason}`
    })),
    emptyText: "No reconciliation exceptions are available for this accounting scope.",
    reconciliationHref: WORKSTATION_ROUTE_CATALOG.accountingReconciliation,
    approvalsHref: WORKSTATION_ROUTE_CATALOG.accountingApprovals,
    evidenceHref: evidenceWorkbenchPath("accounting-exceptions", "active"),
    auditHref: WORKSTATION_ROUTE_CATALOG.accountingOperationsContinuity
  };
}

export function buildReconciliationResolveDialogState(
  breakId: string,
  status: ReconciliationBreakResolutionStatus,
  rationale: string
): ReconciliationResolveDialogState {
  const command = status === "Resolved" ? "resolve" : "dismiss";
  const commandLabel = status === "Resolved" ? "Resolve" : "Dismiss";
  const inputId = `rationale-${breakId}`;
  const helpId = `rationale-help-${breakId}`;

  return {
    breakId,
    status,
    rationale,
    inputId,
    helpId,
    formAriaLabel: `${commandLabel} reconciliation break ${breakId}`,
    label: `${commandLabel} rationale`,
    placeholder: `Describe why this break is being ${command === "resolve" ? "resolved" : "dismissed"}...`,
    helpText: "A rationale is required before this queue action can be submitted.",
    submitLabel: `Confirm ${command}`,
    submitAriaLabel: `Confirm ${command} for reconciliation break ${breakId}`,
    submitDisabledReason: rationale.trim()
      ? null
      : "Enter an operator rationale before confirming this queue action.",
    cancelLabel: "Cancel",
    cancelAriaLabel: `Cancel ${command} for reconciliation break ${breakId}`,
    isSubmitDisabled: !rationale.trim()
  };
}

export function buildReconciliationBreakRows(
  breakQueue: ReconciliationBreakQueueItem[],
  action: ReconciliationBreakAction | null,
  selectedBreakId: string | null = null
): ReconciliationBreakRowViewModel[] {
  return breakQueue.map((item) => {
    const actionBusy = action?.breakId === item.breakId;
    const assignBusy = actionBusy && action?.command === "assign";
    const resolveBusy = actionBusy && action?.command === "resolve";
    const dismissBusy = actionBusy && action?.command === "dismiss";
    const canAssign = !action && item.status === "Open";
    const canResolve = !action && item.status !== "Resolved";
    const canDismiss = !action && item.status !== "Dismissed";
    const isSelected = item.breakId === selectedBreakId;

    return {
      ...item,
      actionBusy,
      varianceLabel: formatSignedCurrency(item.variance),
      varianceTone: item.variance > 0 ? "success" : item.variance < 0 ? "danger" : "default",
      statusBadgeVariant: reconciliationBreakStatusBadgeVariant(item.status),
      detectedAtLabel: formatDateTimeLabel(item.detectedAt),
      lastUpdatedAtLabel: formatDateTimeLabel(item.lastUpdatedAt),
      ownerLabel: item.assignedTo ?? "Unassigned",
      rowAriaLabel: `${item.strategyName} ${item.category} break ${item.breakId}. ${item.status}. Variance ${formatSignedCurrency(item.variance)}. ${item.reason}`,
      rowSelectAriaLabel: `Inspect reconciliation break ${item.breakId}`,
      detailPanelId: "reconciliation-break-detail-panel",
      isSelected,
      isExpanded: isSelected,
      assignLabel: assignBusy ? "Assigning..." : "Assign",
      resolveLabel: resolveBusy ? "Resolving..." : "Resolve",
      dismissLabel: dismissBusy ? "Dismissing..." : "Dismiss",
      assignAriaLabel: `Assign reconciliation break ${item.breakId}`,
      resolveAriaLabel: `Resolve reconciliation break ${item.breakId}`,
      dismissAriaLabel: `Dismiss reconciliation break ${item.breakId}`,
      canAssign,
      canResolve,
      canDismiss,
      assignDisabledReason: buildBreakActionDisabledReason({
        item,
        action,
        busy: assignBusy,
        alreadyComplete: item.status !== "Open",
        busyReason: "Assignment is already in progress for this break.",
        completeReason: `Only open breaks can be assigned; this break is ${item.status}.`
      }),
      resolveDisabledReason: buildBreakActionDisabledReason({
        item,
        action,
        busy: resolveBusy,
        alreadyComplete: item.status === "Resolved",
        busyReason: "Resolution is already in progress for this break.",
        completeReason: "This break is already resolved."
      }),
      dismissDisabledReason: buildBreakActionDisabledReason({
        item,
        action,
        busy: dismissBusy,
        alreadyComplete: item.status === "Dismissed",
        busyReason: "Dismissal is already in progress for this break.",
        completeReason: "This break is already dismissed."
      })
    };
  });
}

function buildReconciliationBreakDetail(row: ReconciliationBreakRowViewModel): ReconciliationBreakDetailViewModel {
  const routingActionHref = buildReconciliationBreakRoutingHref(row.routingTarget);
  const explanation = row.breakExplanation;

  return {
    id: row.detailPanelId,
    eyebrow: "Break detail",
    title: `${row.strategyName} - ${row.category}`,
    subtitle: `${row.breakId} - ${row.status}`,
    description: row.reason,
    ariaLabel: `Reconciliation break detail for ${row.breakId}`,
    statusLabel: row.status,
    statusBadgeVariant: row.statusBadgeVariant,
    fields: [
      { label: "Run", value: row.runId },
      { label: "Variance", value: row.varianceLabel },
      { label: "Owner", value: row.ownerLabel },
      { label: "Detected", value: row.detectedAtLabel },
      { label: "Updated", value: row.lastUpdatedAtLabel },
      { label: "Exception route", value: formatReconciliationMetadata(row.exceptionRoute, "Unrouted") },
      { label: "Tolerance profile", value: formatReconciliationMetadata(row.toleranceProfileId, "Unassigned") },
      { label: "Tolerance band", value: row.toleranceBand == null ? "Policy default" : formatCurrency(row.toleranceBand) },
      { label: "Priority", value: formatReconciliationMetadata(row.priority, "Normal") },
      { label: "SLA", value: row.slaBadgeLabel ?? buildReconciliationSlaText(row) },
      { label: "SLA tone", value: formatReconciliationMetadata(row.slaBadgeTone, "info") },
      { label: "Age band", value: formatReconciliationMetadata(row.ageBand, "0-4h") },
      { label: "Root cause", value: formatReconciliationMetadata(row.rootCauseCode, "Unset") },
      { label: "Resolution code", value: formatReconciliationMetadata(row.resolutionCode, "Unset") },
      { label: "Comments", value: `${row.commentCount ?? 0} comment(s); latest: ${formatReconciliationMetadata(row.lastCommentExcerpt, "No visible comment")}` },
      { label: "Evidence links", value: `${row.evidenceCount ?? 0} evidence link(s)` },
      { label: "Related cases", value: `${row.relatedCaseCount ?? 0}` },
      { label: "Required sign-off", value: buildReconciliationBreakSignoffText(row) },
      { label: "Decision note", value: formatReconciliationMetadata(row.resolutionNote, "No decision captured") },
      { label: "Routing", value: row.routingTarget ?? "No routing target" },
      { label: "Fund account", value: row.fundAccountId ?? "Not scoped" },
      { label: "Explanation summary", value: formatReconciliationMetadata(explanation?.summary, "No shared explanation") },
      { label: "Source systems", value: formatReconciliationList(explanation?.sourceSystems, "No source systems") },
      { label: "Probable cause", value: formatReconciliationMetadata(explanation?.probableCause, "No probable cause") },
      { label: "Ledger impact", value: formatReconciliationMetadata(explanation?.ledgerImpact, "No ledger impact") },
      { label: "Suggested next action", value: formatReconciliationMetadata(explanation?.suggestedNextAction, "No suggested action") },
      { label: "Explanation evidence", value: formatReconciliationList(explanation?.evidenceLinks, "No explanation evidence") }
    ],
    analysisText: explanation?.summary ?? row.explainabilitySummary ?? null,
    recommendedActionText: explanation?.suggestedNextAction ?? row.recommendedAction ?? null,
    routingActionLabel: routingActionHref ? "Open routing target" : null,
    routingActionHref,
    routingActionAriaLabel: routingActionHref ? `Open routing target for reconciliation break ${row.breakId}` : null
  };
}

function formatReconciliationList(values: string[] | null | undefined, fallback: string): string {
  const normalized = values?.map((value) => value.trim()).filter(Boolean) ?? [];
  return normalized.length > 0 ? normalized.join(", ") : fallback;
}


function buildReconciliationSlaText(row: Pick<ReconciliationBreakQueueItem, "slaState" | "slaDueAt" | "slaWarningAt" | "slaBreachedAt">): string {
  const state = row.slaState ?? "OnTrack";
  if (row.slaBreachedAt) {
    return `${state}; breached ${formatDateTimeLabel(row.slaBreachedAt)}`;
  }
  if (row.slaDueAt) {
    return `${state}; due ${formatDateTimeLabel(row.slaDueAt)}`;
  }
  if (row.slaWarningAt) {
    return `${state}; warning ${formatDateTimeLabel(row.slaWarningAt)}`;
  }
  return state;
}

function buildReconciliationBreakRoutingHref(routingTarget: string | null | undefined): string | null {
  const trimmedTarget = routingTarget?.trim();
  if (!trimmedTarget) {
    return null;
  }

  if (trimmedTarget.startsWith("/")) {
    return normalizeLocalWorkstationRoute(trimmedTarget) ?? trimmedTarget;
  }

  return workflowTargetPath(trimmedTarget, "accounting");
}

function reconciliationBreakStatusBadgeVariant(
  status: ReconciliationBreakQueueItem["status"]
): ReconciliationBreakRowViewModel["statusBadgeVariant"] {
  if (status === "Resolved") return "success";
  if (status === "InReview") return "warning";
  if (status === "Dismissed") return "outline";
  return "danger";
}

function buildReconciliationBreakSignoffText(row: Pick<ReconciliationBreakQueueItem, "requiredSignoffRole" | "signoffStatus" | "status" | "resolvedAt">): string {
  const role = formatReconciliationMetadata(row.requiredSignoffRole, "Not configured");
  const status = formatReconciliationMetadata(row.signoffStatus, "Pending");

  if (role === "Not configured") {
    return `Sign-off: ${status}. Required role is not configured.`;
  }

  if (row.resolvedAt && !status.toLowerCase().includes("signed")) {
    return `Decision captured; sign-off: ${status} by ${role}. Close approval remains blocked.`;
  }

  return `Sign-off: ${status} by ${role}.`;
}

function formatReconciliationMetadata(value: string | null | undefined, fallback: string): string {
  const trimmed = value?.trim();
  return trimmed ? trimmed : fallback;
}

function buildBreakActionDisabledReason({
  item,
  action,
  busy,
  alreadyComplete,
  busyReason,
  completeReason
}: {
  item: ReconciliationBreakQueueItem;
  action: ReconciliationBreakAction | null;
  busy: boolean;
  alreadyComplete: boolean;
  busyReason: string;
  completeReason: string;
}): string | null {
  if (busy) {
    return busyReason;
  }

  if (action) {
    return action.breakId === item.breakId
      ? "Another action is already running for this break."
      : "Another reconciliation break action is in progress.";
  }

  if (alreadyComplete) {
    return completeReason;
  }

  return null;
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
      description: `${contextLabel} is waiting for the shared accounting cash-flow payload.`,
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
    description: `${contextLabel} at ${routePath} reuses the shared accounting/reporting cash-flow summary payload.`,
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

export function buildReconciliationNarrative(item: AccountingWorkspaceResponse["reconciliationQueue"][number]) {
  if (item.reconciliationStatus === "Balanced") {
    return "This run is currently balanced. Audit review should focus on evidence completeness and timing freshness rather than open break remediation.";
  }

  if (item.reconciliationStatus === "SecurityCoverageOpen") {
    return "Break counts are secondary here. The main task is resolving Security Master coverage so downstream ledger and reporting workflows are trustworthy.";
  }

  if (item.reconciliationStatus === "Resolved") {
    return "Historical breaks have been worked through, but the run still needs operator review before it can be treated as fully balanced.";
  }

  if (item.reconciliationStatus === "NotStarted") {
    return "No reconciliation pass has been recorded yet. This run should be queued behind currently active Accounting review work.";
  }

  return "Open reconciliation breaks remain on this run. Prioritize amount mismatches, timing drift, and unresolved references before moving on.";
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
      buildAccountingReportingBackendLink("preview", "Preview report payload", EXPORT_API_ENDPOINTS.preview),
      buildAccountingReportingBackendLink("formats", "List export formats", EXPORT_API_ENDPOINTS.formats)
    ]
  };
}

export function buildGovernanceReportingViewState(
  options: Parameters<typeof buildAccountingReportingViewState>[0]
): AccountingReportingViewState {
  return buildAccountingReportingViewState(options);
}

export function buildAccountingWorkflowLaunchViewState({
  data,
  workstream,
  closeCommandCenter
}: {
  data: AccountingWorkspaceResponse;
  workstream: AccountingWorkstream;
  closeCommandCenter: CloseCommandCenterViewState | null | undefined;
}): AccountingWorkflowLaunchViewState {
  const openBreakCount = data.breakQueue.filter((item) => isOpenAccountingBreakStatus(item.status)).length;
  const totalBreakCount = data.breakQueue.length;
  const metricRows = closeCommandCenter?.metricRows ?? [];
  const pendingAdjustmentMetric = metricRows.find((item) => item.id === "adjustments") ?? null;
  const providerMetric = metricRows.find((item) => item.id === "providers") ?? null;
  const sourceMetric = metricRows.find((item) => item.id === "source-files") ?? null;
  const reportPackMetric = metricRows.find((item) => item.id === "report-pack") ?? null;
  const pendingAdjustmentCount = parseAccountingWorkflowMetricCount(pendingAdjustmentMetric?.value);
  const sourceGapCount = parseAccountingWorkflowMetricCount(sourceMetric?.value);
  const securityGapCount = parseAccountingWorkflowMetricCount(
    data.metrics.find((item) => item.label.toLowerCase().includes("security"))?.value
  );
  const activeStepLabel = accountingWorkflowStepLabel(workstream);
  const workflowStatusTone = closeCommandCenter?.statusTone ?? (openBreakCount > 0 ? "warning" : "default");
  const workflowStatusLabel = closeCommandCenter?.statusLabel ?? (openBreakCount > 0 ? "Review" : "Ready");
  const steps: AccountingWorkflowStepViewModel[] = [
    buildAccountingWorkflowStep({
      id: "configure",
      label: "Set up books",
      caption: "Books, chart, templates, and posting controls.",
      href: WORKSTATION_ROUTE_CATALOG.accountingConfigure,
      metricLabel: "Setup",
      metricValue: "Shared",
      statusLabel: "Endpoint-backed",
      tone: "default",
      workstream
    }),
    buildAccountingWorkflowStep({
      id: "journal-entries",
      label: "Record adjustments",
      caption: "Manual JEs, evidence, Security Master, and approval submit.",
      href: WORKSTATION_ROUTE_CATALOG.accountingJournalEntries,
      metricLabel: "Pending",
      metricValue: pendingAdjustmentMetric?.value ?? "0",
      statusLabel: pendingAdjustmentCount > 0 ? "Approval review" : "Ready",
      tone: pendingAdjustmentCount > 0 ? "warning" : "success",
      workstream
    }),
    buildAccountingWorkflowStep({
      id: "capital-accounts",
      label: "Capital accounts",
      caption: "Investor evidence, allocation rules, statements, and audit support.",
      href: WORKSTATION_ROUTE_CATALOG.accountingCapitalAccounts,
      metricLabel: "Investor rows",
      metricValue: data.manualJournalWorkbench?.privateCapitalActivity?.capitalAccountCount.toLocaleString() ?? "0",
      statusLabel: (data.manualJournalWorkbench?.privateCapitalActivity?.capitalAccountCount ?? 0) > 0 ? "Evidence ready" : "No activity",
      tone: (data.manualJournalWorkbench?.privateCapitalActivity?.capitalAccountCount ?? 0) > 0 ? "success" : "warning",
      workstream
    }),
    buildAccountingWorkflowStep({
      id: "ledger",
      label: "Review ledger",
      caption: "Meridian-owned trial balance and journal evidence.",
      href: WORKSTATION_ROUTE_CATALOG.accountingLedger,
      metricLabel: providerMetric?.label ?? "Truth",
      metricValue: providerMetric?.value ?? "Meridian",
      statusLabel: providerMetric?.tone === "warning" || providerMetric?.tone === "danger"
        ? "Evidence review"
        : "Ledger authority",
      tone: providerMetric?.tone ?? "default",
      workstream
    }),
    buildAccountingWorkflowStep({
      id: "reconciliation",
      label: "Reconcile",
      caption: "Statement runs, trial balance, and open break review.",
      href: WORKSTATION_ROUTE_CATALOG.accountingReconciliation,
      metricLabel: "Open breaks",
      metricValue: String(openBreakCount),
      statusLabel: openBreakCount > 0 ? "Review breaks" : "Balanced queue",
      tone: openBreakCount > 0 ? "warning" : "success",
      workstream
    }),
    buildAccountingWorkflowStep({
      id: "exceptions",
      label: "Resolve exceptions",
      caption: "Casework, comments, evidence, and sign-off handoffs.",
      href: WORKSTATION_ROUTE_CATALOG.accountingExceptions,
      metricLabel: "Cases",
      metricValue: String(totalBreakCount),
      statusLabel: totalBreakCount > 0 ? "Casework open" : "No active cases",
      tone: totalBreakCount > 0 ? "warning" : "success",
      workstream
    }),
    buildAccountingWorkflowStep({
      id: "security-master",
      label: "Clear security",
      caption: "Identifiers, schedules, lots, and posting readiness.",
      href: WORKSTATION_ROUTE_CATALOG.accountingSecurityMaster,
      metricLabel: "Gaps",
      metricValue: String(securityGapCount),
      statusLabel: securityGapCount > 0 ? "Coverage gaps" : "Coverage clean",
      tone: securityGapCount > 0 ? "warning" : "success",
      workstream
    }),
    buildAccountingWorkflowStep({
      id: "approvals",
      label: "Approve close",
      caption: "Signer decisions, blockers, and audit trail.",
      href: WORKSTATION_ROUTE_CATALOG.accountingApprovals,
      metricLabel: "Approvals",
      metricValue: pendingAdjustmentMetric?.value ?? "0",
      statusLabel: pendingAdjustmentCount > 0 ? "Signer review" : "No backlog",
      tone: pendingAdjustmentCount > 0 ? "warning" : "success",
      workstream
    }),
    buildAccountingWorkflowStep({
      id: "reporting",
      label: "Package evidence",
      caption: "Report packs, retained manifests, exports, and audit output.",
      href: WORKSTATION_ROUTE_CATALOG.reportingEvidence,
      metricLabel: reportPackMetric?.label ?? "Profiles",
      metricValue: reportPackMetric?.value ?? String(data.reporting.profileCount),
      statusLabel: reportPackMetric?.tone === "success" ? "Ready" : "Evidence review",
      tone: reportPackMetric?.tone ?? (data.reporting.profileCount > 0 ? "default" : "warning"),
      workstream
    })
  ];
  const actionRows: AccountingWorkflowActionViewModel[] = [
    {
      id: "reconcile",
      label: openBreakCount > 0 ? "Reconcile breaks" : "Review reconciliation",
      detail: openBreakCount > 0 ? `${formatCount(openBreakCount, "open break")} require operator review.` : "Statement and run reconciliation are ready for inspection.",
      href: WORKSTATION_ROUTE_CATALOG.accountingReconciliation,
      ariaLabel: "Open Accounting reconciliation workflow",
      tone: openBreakCount > 0 ? "warning" : "success"
    },
    {
      id: "journal-entry",
      label: "Enter journal entry",
      detail: pendingAdjustmentCount > 0 ? `${formatCount(pendingAdjustmentCount, "adjustment")} remain in approval scope.` : "Create or validate a controller-owned accounting adjustment.",
      href: WORKSTATION_ROUTE_CATALOG.accountingJournalEntries,
      ariaLabel: "Open Accounting journal entry workbench",
      tone: pendingAdjustmentCount > 0 ? "warning" : "default"
    },
    {
      id: "approvals",
      label: "Review approvals",
      detail: pendingAdjustmentCount > 0 ? "Signer action is needed before close release." : "Open the close approval gate and audit trail.",
      href: WORKSTATION_ROUTE_CATALOG.accountingApprovals,
      ariaLabel: "Open Accounting approvals workflow",
      tone: pendingAdjustmentCount > 0 ? "warning" : "success"
    },
    {
      id: "evidence",
      label: sourceGapCount > 0 ? "Attach evidence" : "Open evidence",
      detail: sourceGapCount > 0 ? `${formatCount(sourceGapCount, "source gap")} are visible in close readiness.` : "Inspect retained accounting-record evidence and report manifests.",
      href: WORKSTATION_ROUTE_CATALOG.reportingEvidence,
      ariaLabel: "Open retained accounting record evidence",
      tone: sourceGapCount > 0 ? "warning" : reportPackMetric?.tone ?? "default"
    }
  ];

  return {
    title: "Accounting workflow",
    description: "Books-before-broker accounting, reconciliation, approvals, and retained report evidence are grouped in one operator lane.",
    ariaLabel: "Accounting workflow launch paths",
    activeLabel: `${activeStepLabel} active`,
    statusLabel: workflowStatusLabel,
    statusTone: workflowStatusTone,
    steps,
    actionRows,
    liveRegionText: `Accounting workflow ${workflowStatusLabel}. ${activeStepLabel} active. ${formatCount(openBreakCount, "open break")}.`
  };
}

function buildAccountingWorkflowStep({
  id,
  label,
  caption,
  href,
  metricLabel,
  metricValue,
  statusLabel,
  tone,
  workstream
}: {
  id: AccountingWorkstream;
  label: string;
  caption: string;
  href: string;
  metricLabel: string;
  metricValue: string;
  statusLabel: string;
  tone: AccountingToolingTone;
  workstream: AccountingWorkstream;
}): AccountingWorkflowStepViewModel {
  const isActive = id === workstream;

  return {
    id,
    label,
    caption,
    href,
    metricLabel,
    metricValue,
    statusLabel,
    tone,
    isActive,
    ariaLabel: `${label}: ${statusLabel}${isActive ? ", current Accounting workstream" : ""}`
  };
}

function accountingWorkflowStepLabel(workstream: AccountingWorkstream): string {
  if (workstream === "journal-entries") {
    return "Journal entries";
  }

  if (workstream === "capital-accounts") {
    return "Capital accounts";
  }

  if (workstream === "security-master") {
    return "Security Master";
  }

  return workstream.charAt(0).toUpperCase() + workstream.slice(1).replace("-", " ");
}

function parseAccountingWorkflowMetricCount(value: string | null | undefined): number {
  const normalized = value?.replace(/,/g, "").match(/-?\d+/)?.[0];
  return normalized ? Number.parseInt(normalized, 10) : 0;
}

function formatAccountingWorkflowError(error: unknown, fallback: string): string {
  return error instanceof Error ? error.message || fallback : fallback;
}

function buildAccountingCloseReportPackageViewState({
  workflow,
  closePlan,
  packages,
  selectedPackageId,
  loading,
  errorText,
  buildBusy,
  buildStatusText,
  buildStatusTone,
  certifyBusy,
  certifyStatusText,
  certifyStatusTone,
  signOffBusy,
  signOffStatusText,
  signOffStatusTone,
  createLateAdjustmentBusy,
  createLateAdjustmentStatusText,
  createLateAdjustmentStatusTone,
  lateAdjustmentDraft,
  reviewLateAdjustmentBusy,
  reviewLateAdjustmentStatusText,
  reviewLateAdjustmentStatusTone,
  exportManifestBusy,
  exportManifestStatusText,
  exportManifestStatusTone,
  exportManifest,
  refresh,
  buildReportPackage,
  certifyPackage,
  signOffNextTask,
  updateLateAdjustmentDraft,
  createLateAdjustment,
  reviewLateAdjustment,
  inspectSelectedPackageExport,
  selectPackage
}: {
  workflow: OperationsContinuityWorkflow | null;
  closePlan: ClosePeriodPlan | null;
  packages: AccountingReportPackageBundle[];
  selectedPackageId: string | null;
  loading: boolean;
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
  createLateAdjustmentBusy: boolean;
  createLateAdjustmentStatusText: string | null;
  createLateAdjustmentStatusTone: "neutral" | "success" | "danger";
  lateAdjustmentDraft: AccountingLateAdjustmentDraftViewModel;
  reviewLateAdjustmentBusy: boolean;
  reviewLateAdjustmentStatusText: string | null;
  reviewLateAdjustmentStatusTone: "neutral" | "success" | "danger";
  exportManifestBusy: boolean;
  exportManifestStatusText: string | null;
  exportManifestStatusTone: "neutral" | "success" | "danger";
  exportManifest: ReportExportArtifactManifest | null;
  refresh: () => Promise<void>;
  buildReportPackage: () => Promise<void>;
  certifyPackage: () => Promise<void>;
  signOffNextTask: () => Promise<void>;
  updateLateAdjustmentDraft: (patch: Partial<AccountingLateAdjustmentDraftViewModel>) => void;
  createLateAdjustment: () => Promise<void>;
  reviewLateAdjustment: (requestId: string, decision: "Approved" | "Rejected") => Promise<void>;
  inspectSelectedPackageExport: () => Promise<void>;
  selectPackage: (packageId: string) => void;
}): AccountingCloseReportPackageViewModel {
  const selectedBundle = packages.find((bundle) => bundle.financialStatements.packageId === selectedPackageId) ?? packages[0] ?? null;
  const selectedPackage = selectedBundle ? buildAccountingReportPackageRow(selectedBundle, selectedBundle.financialStatements.packageId) : null;
  const packageRows = packages.map((bundle) => buildAccountingReportPackageRow(
    bundle,
    selectedBundle?.financialStatements.packageId ?? selectedPackageId
  ));
  const tasks = (closePlan?.tasks ?? []).map(buildClosePlanTaskRow);
  const closeCalendar = (closePlan?.closeCalendar ?? []).map(buildCloseCalendarMilestoneRow);
  const locked = closePlan?.isPeriodLocked === true;
  const lateAdjustments = (closePlan?.lateAdjustments ?? []).map((adjustment) => buildLateAdjustmentRow(adjustment, locked));
  const openTaskCount = closePlan?.tasks.filter((task) => task.status !== "SignedOff").length ?? 0;
  const blockedCalendarCount = closeCalendar.filter((item) => item.statusTone === "danger").length;
  const closeValidationIssues = closePlan?.validationIssues ?? [];
  const packageValidationIssues = selectedBundle?.validationIssues ?? [];
  const validationIssues = [
    ...closeValidationIssues,
    ...packageValidationIssues
  ].map<AccountingConfigurationIssueViewModel>((issue, index) => ({
    id: `${issue.code}-${issue.targetId ?? index}`,
    label: `${issue.severity} | ${issue.code}`,
    message: issue.message,
    detail: issue.targetId ?? "No target",
    tone: issue.severity === "Critical" ? "danger" : issue.severity === "Warning" ? "warning" : "default"
  }));
  const packageCertification = selectedBundle?.certification.state ?? selectedBundle?.financialStatements.certificationState ?? "Draft";
  const certificationTone = accountingCertificationTone(packageCertification);
  const statusTone: AccountingToolingTone = errorText
    ? "danger"
    : !workflow
      ? "default"
      : validationIssues.length > 0 || openTaskCount > 0
        ? "warning"
        : packages.length > 0 && certificationTone === "success"
          ? "success"
          : "default";
  const statusLabel = errorText
    ? "Needs attention"
    : !workflow
      ? "Close workflow pending"
      : packages.length > 0
        ? `${formatAccountingCertificationState(packageCertification)} package`
        : "Package not built";
  const materiality = closePlan?.materialityPolicy;
  const materialityLabel = materiality
    ? `${formatCurrencyWithCode(materiality.amountThreshold, materiality.currency)} or ${materiality.percentThreshold}% review by ${materiality.reviewRole}`
    : "Materiality policy pending";
  const buildDisabledReason = !workflow
    ? "A close workflow must be loaded before building a package."
    : loading || buildBusy || certifyBusy || signOffBusy
      ? "Close/report package refresh is running."
      : null;
  const criticalIssueCount = [...closeValidationIssues, ...packageValidationIssues].filter((issue) => issue.severity === "Critical").length;
  const certifyDisabledReason = !selectedBundle
    ? "A report package must be built before certification."
    : selectedBundle.certification.state === "Certified"
      ? "The selected report package is already certified."
      : selectedBundle.certification.state !== "ReadyForReview"
        ? `Certification requires Ready for review state; current state is ${formatAccountingCertificationState(selectedBundle.certification.state)}.`
        : criticalIssueCount > 0
          ? "Critical validation issues must be cleared before certification."
          : loading || buildBusy || certifyBusy || signOffBusy
            ? "Close/report package certification is running."
            : null;
  const signOffTarget = closePlan ? resolveCloseTaskSignOffTarget(closePlan) : null;
  const signOffDisabledReason = !workflow
    ? "A close workflow must be loaded before signing off a task."
    : !closePlan
      ? "A close plan must be loaded before signing off a task."
      : locked
        ? "The period is locked; close task sign-off is disabled."
        : !signOffTarget
          ? "No close checklist task is ready for sign-off."
          : loading || buildBusy || certifyBusy || signOffBusy
            ? "Close/report package action is running."
            : null;
  const createLateAdjustmentAmount = Number(lateAdjustmentDraft.amount);
  const createLateAdjustmentDisabledReason = !workflow
    ? "A close workflow must be loaded before requesting a late adjustment."
    : !closePlan
      ? "A close plan must be loaded before requesting a late adjustment."
      : locked
        ? "The period is locked; late adjustment requests are disabled."
        : !lateAdjustmentDraft.journalEntryId.trim()
          ? "Enter the journal entry id for the late adjustment."
          : !Number.isFinite(createLateAdjustmentAmount) || createLateAdjustmentAmount === 0
            ? "Enter a non-zero late adjustment amount."
            : !lateAdjustmentDraft.reason.trim()
              ? "Enter the late adjustment reason."
              : loading || buildBusy || certifyBusy || signOffBusy || createLateAdjustmentBusy || reviewLateAdjustmentBusy
                ? "Close/report package action is running."
                : null;
  const selectedExportArtifact = selectedBundle?.exportArtifacts?.[0] ?? null;
  const exportManifestDisabledReason = !selectedBundle
    ? "A report package must be selected before export manifest inspection."
    : !selectedExportArtifact
      ? "The selected report package has no retained export artifacts."
      : loading || buildBusy || certifyBusy || signOffBusy || reviewLateAdjustmentBusy || exportManifestBusy
        ? "Close/report package action is running."
        : null;

  return {
    title: "Close and report package certification",
    description: "Shared close-plan, period-lock, materiality, late-adjustment, financial statement, investor statement, realized gain/loss, NAV, and restatement state from the ledger endpoints.",
    ariaLabel: "Accounting close and report package certification cockpit",
    statusLabel,
    statusTone,
    periodLabel: closePlan
      ? `${closePlan.periodId} (${formatDateOnly(closePlan.periodStart)} to ${formatDateOnly(closePlan.periodEnd)})`
      : workflow?.periodId ?? "Period pending",
    fundLabel: closePlan?.fundProfileId ?? workflow?.fundAccountId ?? "Fund pending",
    lockLabel: locked ? "Period locked" : closePlan ? "Period open" : "Lock state pending",
    materialityLabel,
    loading,
    loadingText: loading ? "Refreshing close plan and certified package history." : null,
    errorText,
    buildBusy,
    buildStatusText,
    buildStatusTone,
    certifyBusy,
    certifyStatusText,
    certifyStatusTone,
    signOffBusy,
    signOffStatusText,
    signOffStatusTone,
    createLateAdjustmentBusy,
    createLateAdjustmentStatusText,
    createLateAdjustmentStatusTone,
    reviewLateAdjustmentBusy,
    reviewLateAdjustmentStatusText,
    reviewLateAdjustmentStatusTone,
    exportManifestBusy,
    exportManifestStatusText,
    exportManifestStatusTone,
    exportManifest: exportManifest ? buildAccountingReportExportManifestViewModel(exportManifest) : null,
    buildButtonLabel: packages.length > 0 ? "Rebuild package" : "Build package",
    buildDisabledReason,
    certifyButtonLabel: selectedBundle?.certification.state === "Certified" ? "Certified" : "Certify package",
    certifyDisabledReason,
    signOffButtonLabel: signOffTarget ? `Sign off ${signOffTarget.task.displayName}` : "Sign off next task",
    signOffDisabledReason,
    createLateAdjustmentDisabledReason,
    lateAdjustmentDraft,
    exportManifestButtonLabel: selectedExportArtifact ? `Inspect ${selectedExportArtifact.displayName}` : "Inspect export manifest",
    exportManifestDisabledReason,
    metrics: [
      {
        id: "checklist",
        label: "Checklist",
        value: `${tasks.filter((task) => task.statusTone === "success").length}/${tasks.length}`,
        detail: tasks.length > 0 ? `${formatCount(openTaskCount, "task")} remain before sign-off.` : "Close checklist has not been loaded.",
        tone: openTaskCount > 0 ? "warning" : tasks.length > 0 ? "success" : "default"
      },
      {
        id: "late-adjustments",
        label: "Late adjustments",
        value: String(lateAdjustments.length),
        detail: lateAdjustments.length > 0 ? "Late adjustments require approval evidence." : "No late adjustments are surfaced for this close plan.",
        tone: lateAdjustments.length > 0 ? "warning" : "success"
      },
      {
        id: "calendar",
        label: "Calendar",
        value: String(closeCalendar.length),
        detail: closeCalendar.length > 0
          ? blockedCalendarCount > 0
            ? `${formatCount(blockedCalendarCount, "calendar milestone")} blocked.`
            : `${formatCount(closeCalendar.length, "calendar milestone")} sequenced.`
          : "Close calendar milestones have not been loaded.",
        tone: blockedCalendarCount > 0 ? "danger" : closeCalendar.length > 0 ? "success" : "default"
      },
      {
        id: "packages",
        label: "Packages",
        value: String(packageRows.length),
        detail: selectedPackage ? `${selectedPackage.packageId} is selected.` : "No certified package history is available.",
        tone: packageRows.length > 0 ? certificationTone : "default"
      },
      {
        id: "issues",
        label: "Validation",
        value: String(validationIssues.length),
        detail: validationIssues.length > 0 ? "Validation issues remain attached to the close/report package." : "No close/report validation issues are surfaced.",
        tone: validationIssues.length > 0 ? "warning" : "success"
      }
    ],
    closeCalendar,
    tasks,
    lateAdjustments,
    packageRows,
    selectedPackage,
    certificationSafeguards: buildAccountingReportCertificationSafeguards(closePlan, selectedBundle, criticalIssueCount),
    validationIssues,
    liveRegionText: `Close report package ${statusLabel}. ${formatCount(openTaskCount, "open task")}. ${formatCount(packageRows.length, "package")}.`,
    refresh,
    buildReportPackage,
    certifyPackage,
    signOffNextTask,
    updateLateAdjustmentDraft,
    createLateAdjustment,
    reviewLateAdjustment,
    inspectSelectedPackageExport,
    selectPackage
  };
}

function buildAccountingReportCertificationSafeguards(
  closePlan: ClosePeriodPlan | null,
  bundle: AccountingReportPackageBundle | null,
  criticalIssueCount: number
): AccountingReportCertificationSafeguardViewModel[] {
  const tasks = closePlan?.tasks ?? [];
  const signedOffTaskCount = tasks.filter((task) => task.status === "SignedOff").length;
  const openTaskCount = tasks.filter((task) => task.status !== "SignedOff").length;
  const exportArtifacts = bundle?.exportArtifacts ?? [];
  const certifiedExportCount = exportArtifacts.filter((artifact) => artifact.certificationState === "Certified").length;
  const restatement = bundle?.financialStatements.restatement ?? bundle?.navPackage.restatement ?? null;
  const evidenceCount = bundle ? collectAccountingReportPackageEvidenceLinks(bundle).length : 0;

  return [
    {
      id: "checklist-signoff",
      label: "Checklist sign-off",
      value: closePlan ? `${signedOffTaskCount}/${tasks.length}` : "Not loaded",
      detail: closePlan
        ? openTaskCount > 0
          ? `${formatCount(openTaskCount, "close task")} still requires retained sign-off evidence.`
          : "All close checklist tasks are signed off with retained evidence."
        : "Load the close plan before report package certification.",
      tone: closePlan
        ? openTaskCount > 0
          ? "warning"
          : "success"
        : "default"
    },
    {
      id: "period-lock",
      label: "Period lock",
      value: closePlan?.isPeriodLocked ? "Locked after close" : closePlan ? "Open for adjustments" : "Not loaded",
      detail: closePlan
        ? closePlan.isPeriodLocked
          ? "Close lock is retained; posted entries require reversal or rebook workflows."
          : "Period remains open, so late adjustments can still change the package evidence set."
        : "Load the close plan before evaluating period-lock posture.",
      tone: closePlan?.isPeriodLocked ? "success" : closePlan ? "warning" : "default"
    },
    {
      id: "critical-validation",
      label: "Critical validation blockers",
      value: criticalIssueCount > 0 ? String(criticalIssueCount) : "Clear",
      detail: criticalIssueCount > 0
        ? `${formatCount(criticalIssueCount, "critical issue")} must clear before certification.`
        : "No critical close or report package validation issues are surfaced.",
      tone: criticalIssueCount > 0 ? "danger" : "success"
    },
    {
      id: "export-certification",
      label: "Export certification",
      value: bundle
        ? exportArtifacts.length > 0
          ? `${certifiedExportCount}/${exportArtifacts.length}`
          : "No artifacts"
        : "Not loaded",
      detail: bundle
        ? exportArtifacts.length > 0
          ? certifiedExportCount === exportArtifacts.length
            ? "Every retained export artifact is certified."
            : `${formatCount(exportArtifacts.length - certifiedExportCount, "export artifact")} remains ready for review.`
          : "No retained export artifacts are attached to the selected package."
        : "Build or select a package before export certification review.",
      tone: bundle
        ? exportArtifacts.length === 0
          ? "default"
          : certifiedExportCount === exportArtifacts.length
            ? "success"
            : "warning"
        : "default"
    },
    {
      id: "restatement-workflow",
      label: "Restatement workflow",
      value: restatement ? restatement.approvalState : "No restatement",
      detail: restatement
        ? `${restatement.reasonCode} restates ${restatement.priorPackageId}; approval evidence is retained with the package.`
        : "No restatement workflow is attached to the selected package.",
      tone: restatement
        ? restatement.approvalState === "Approved"
          ? "success"
          : restatement.approvalState === "Rejected"
            ? "danger"
            : "warning"
        : "success"
    },
    {
      id: "evidence-package",
      label: "Evidence package",
      value: bundle ? formatCount(evidenceCount, "evidence link") : "Not loaded",
      detail: bundle
        ? "Certification will submit the retained financial statement, investor capital, realized gain/loss, NAV, restatement, and package evidence links."
        : "Build or select a package before evidence certification review.",
      tone: bundle ? evidenceCount > 0 ? "success" : "warning" : "default"
    }
  ];
}

function buildAccountingReportExportManifestViewModel(
  manifest: ReportExportArtifactManifest
): AccountingReportExportManifestViewModel {
  return {
    packageId: manifest.packageId,
    artifactId: manifest.artifactId,
    displayName: manifest.displayName,
    formatLabel: `${manifest.format} | ${manifest.contentType}`,
    fileName: manifest.fileName,
    certificationLabel: formatAccountingCertificationState(manifest.certificationState),
    generatedLabel: formatDateTimeLabel(manifest.generatedAtUtc),
    hashLabel: manifest.contentHash,
    evidenceLabel: formatCount(manifest.evidenceLinks.length, "evidence link"),
    postingLabel: manifest.externalPostingAllowed ? "External posting allowed" : "External posting disabled",
    routeLabel: manifest.route
  };
}

function resolveCloseTaskSignOffTarget(closePlan: ClosePeriodPlan): { task: CloseTask; role: string } | null {
  if (closePlan.isPeriodLocked) {
    return null;
  }

  for (const task of closePlan.tasks) {
    if (task.status !== "ReadyForSignOff") {
      continue;
    }

    const unsatisfiedRequirement = (task.signOffRequirements ?? []).find(
      (requirement) => !requirement.isSatisfied && requirement.approvedCount < requirement.requiredApprovalCount
    );
    if (unsatisfiedRequirement) {
      return { task, role: unsatisfiedRequirement.role };
    }

    if ((task.signOffRequirements ?? []).length === 0 && task.signOffs.length === 0) {
      return { task, role: task.owner || "controller" };
    }
  }

  return null;
}

function createAccountingLateAdjustmentDraft(currency = "USD"): AccountingLateAdjustmentDraftViewModel {
  return {
    journalEntryId: "",
    amount: "",
    currency,
    reason: ""
  };
}

function buildClosePlanTaskRow(task: CloseTask): AccountingClosePlanTaskRowViewModel {
  const signedOffCount = task.signOffs.filter((signOff) => signOff.approvalState === "Approved").length;
  const requirements = task.signOffRequirements ?? [];
  const requiredCount = requirements.reduce((total, requirement) => total + Math.max(0, requirement.requiredApprovalCount), 0);
  const approvedRequirementCount = requirements.reduce((total, requirement) => total + Math.max(0, requirement.approvedCount), 0);

  return {
    taskId: task.taskId,
    displayName: task.displayName,
    ownerLabel: task.owner || "Unassigned",
    dueDateLabel: formatDateOnly(task.dueDate),
    statusLabel: formatCloseTaskStatus(task.status),
    statusTone: closeTaskStatusTone(task.status),
    dependencyLabel: task.dependencies.length > 0
      ? `${formatCount(task.dependencies.length, "dependency")}: ${task.dependencies.map((dependency) => dependency.dependsOnTaskId).join(", ")}`
      : "No dependencies",
    signOffLabel: requirements.length > 0
      ? `${approvedRequirementCount}/${requiredCount} required sign-offs approved`
      : task.signOffs.length > 0
        ? `${signedOffCount}/${task.signOffs.length} sign-offs approved`
        : "No sign-off required",
    signOffDetailLabel: buildCloseTaskSignOffDetail(task),
    signOffRequirementLabel: requirements.length > 0
      ? requirements.map((requirement) => `${requirement.role}: ${requirement.approvedCount}/${requirement.requiredApprovalCount}`).join(" | ")
      : "No sign-off matrix supplied",
    evidenceLabel: formatCount(task.evidenceLinks.length, "evidence link"),
    blockerLabel: task.blockerReason?.trim() || null
  };
}

function buildCloseCalendarMilestoneRow(milestone: CloseCalendarMilestone): AccountingCloseCalendarMilestoneViewModel {
  const statusTone = closeTaskStatusTone(milestone.status);
  return {
    milestoneId: milestone.milestoneId,
    displayName: milestone.displayName,
    ownerLabel: milestone.owner || "Unassigned",
    dueDateLabel: formatDateOnly(milestone.dueDate),
    statusLabel: formatCloseTaskStatus(milestone.status),
    statusTone: milestone.isBlocked ? "danger" : milestone.isSatisfied ? "success" : statusTone,
    dependencyLabel: milestone.dependencyCount > 0
      ? formatCount(milestone.dependencyCount, "dependency")
      : "No dependencies",
    signOffLabel: milestone.requiredSignOffCount > 0
      ? `${milestone.approvedSignOffCount}/${milestone.requiredSignOffCount} sign-offs`
      : "No sign-off requirement",
    evidenceLabel: formatCount(milestone.evidenceLinks.length, "evidence link"),
    blockerLabel: milestone.blockerReason?.trim() || null,
    lockedLabel: milestone.isPeriodLocked ? "Locked after close" : "Open period"
  };
}

function buildCloseTaskSignOffDetail(task: CloseTask): string | null {
  const latest = [...task.signOffs]
    .sort((left, right) => String(right.signedAtUtc ?? "").localeCompare(String(left.signedAtUtc ?? "")))[0];
  if (!latest) {
    return null;
  }

  const actor = latest.actor?.trim() || "unassigned reviewer";
  const signedAt = latest.signedAtUtc ? ` on ${formatDateTimeLabel(latest.signedAtUtc)}` : "";
  const notes = latest.notes?.trim();
  return `${latest.approvalState} by ${actor}${signedAt}${notes ? ` | ${notes}` : ""}`;
}

function buildLateAdjustmentRow(
  adjustment: LateAdjustmentRequest,
  periodLocked: boolean
): AccountingLateAdjustmentRowViewModel {
  const materiality = adjustment.materialityPolicy;
  const absoluteAmount = Math.abs(adjustment.amount);
  const exceedsAmountThreshold = absoluteAmount > materiality.amountThreshold;
  const thresholdLabel = formatCurrencyWithCode(materiality.amountThreshold, materiality.currency);
  const materialityLabel = exceedsAmountThreshold
    ? `Material adjustment: exceeds ${thresholdLabel}; ${materiality.reviewRole} review required`
    : `Within materiality: at or below ${thresholdLabel}; ${materiality.reviewRole} review policy`;
  const reviewDisabledReason = periodLocked
    ? "The period is locked; late-adjustment review is disabled."
    : adjustment.approvalState === "Approved" || adjustment.approvalState === "Rejected"
      ? `Late adjustment is already ${adjustment.approvalState.toLowerCase()}.`
      : null;

  return {
    requestId: adjustment.requestId,
    journalEntryId: adjustment.journalEntryId,
    amountLabel: formatCurrencyWithCode(adjustment.amount, adjustment.currency, true),
    requestedByLabel: `${adjustment.requestedBy} on ${formatDateTimeLabel(adjustment.requestedAtUtc)}`,
    statusLabel: adjustment.approvalState,
    decisionLabel: adjustment.decidedBy
      ? `${adjustment.approvalState} by ${adjustment.decidedBy}${adjustment.decidedAtUtc ? ` on ${formatDateTimeLabel(adjustment.decidedAtUtc)}` : ""}`
      : null,
    evidenceLabel: formatCount(adjustment.evidenceLinks.length, "evidence link"),
    materialityLabel,
    materialityTone: exceedsAmountThreshold ? "warning" : "success",
    reason: adjustment.reason,
    reviewDisabledReason
  };
}

function buildAccountingReportPackageRow(
  bundle: AccountingReportPackageBundle,
  selectedPackageId: string | null | undefined
): AccountingReportPackageRowViewModel {
  const packageId = bundle.financialStatements.packageId;
  const certificationState = bundle.certification.state ?? bundle.financialStatements.certificationState;
  const restatement = bundle.financialStatements.restatement ?? bundle.navPackage.restatement ?? null;
  const exportArtifacts = bundle.exportArtifacts ?? [];
  const certifiedArtifactCount = exportArtifacts.filter((artifact) => artifact.certificationState === "Certified").length;
  const evidenceCount = new Set([
    ...bundle.financialStatements.evidenceLinks,
    ...bundle.investorCapitalStatements.flatMap((statement) => statement.evidenceLinks),
    ...bundle.realizedGainLoss.evidenceLinks,
    ...bundle.navPackage.evidenceLinks,
    ...bundle.certification.evidenceLinks,
    ...(restatement?.evidenceLinks ?? [])
  ]).size;

  return {
    packageId,
    periodLabel: bundle.financialStatements.periodId,
    certificationLabel: formatAccountingCertificationState(certificationState),
    certificationTone: accountingCertificationTone(certificationState),
    navLabel: formatCurrencyWithCode(bundle.navPackage.nav, bundle.navPackage.currency),
    investorStatementLabel: formatCount(bundle.investorCapitalStatements.length, "investor statement"),
    realizedGainLossLabel: formatCurrencyWithCode(
      bundle.realizedGainLoss.realizedGainLoss,
      bundle.realizedGainLoss.currency,
      true
    ),
    restatementLabel: restatement
      ? `${restatement.reasonCode} | ${restatement.approvalState}`
      : "No restatement",
    exportArtifactLabel: exportArtifacts.length > 0
      ? `${certifiedArtifactCount}/${exportArtifacts.length} exports certified`
      : "No export artifacts",
    exportArtifactTone: exportArtifacts.length === 0
      ? "default"
      : certifiedArtifactCount === exportArtifacts.length
        ? "success"
        : "warning",
    evidenceLabel: formatCount(evidenceCount, "evidence link"),
    validationLabel: formatCount(bundle.validationIssues.length, "validation issue"),
    selected: packageId === selectedPackageId
  };
}

function buildAccountingReportPackageRequest(
  workflow: OperationsContinuityWorkflow,
  closePlan: ClosePeriodPlan | null,
  seedPackage: AccountingReportPackageBundle | null
): AccountingReportPackageRequest {
  const investorStatement = seedPackage?.investorCapitalStatements[0] ?? null;

  return {
    fundProfileId: closePlan?.fundProfileId ?? workflow.fundAccountId,
    ledgerBookId: closePlan?.ledgerBookId ?? seedPackage?.financialStatements.ledgerBookId ?? null,
    periodId: closePlan?.periodId ?? workflow.periodId,
    actor: "browser-accounting-operator",
    closeWorkflowId: workflow.workflowId,
    capitalAccountId: investorStatement?.capitalAccountId ?? null,
    investorId: investorStatement?.investorId ?? null,
    beginningCapital: investorStatement?.beginningCapital ?? 0,
    contributions: investorStatement?.contributions ?? 0,
    distributions: investorStatement?.distributions ?? 0,
    realizedGainLoss: seedPackage?.realizedGainLoss.realizedGainLoss ?? investorStatement?.realizedGainLoss ?? 0,
    nav: seedPackage?.navPackage.nav ?? investorStatement?.endingCapital ?? 0,
    currency: seedPackage?.navPackage.currency ?? investorStatement?.currency ?? "USD",
    evidenceLinks: collectAccountingCloseEvidenceLinks(workflow, closePlan),
    correlationId: `browser-close-report-${workflow.workflowId}`
  };
}

function buildCertifyAccountingReportPackageRequest(
  bundle: AccountingReportPackageBundle
): CertifyAccountingReportPackageRequest {
  return {
    packageId: bundle.financialStatements.packageId,
    actor: "browser-accounting-controller",
    notes: `Certified accounting report package ${bundle.financialStatements.packageId} for ${bundle.financialStatements.periodId}.`,
    evidenceLinks: collectAccountingReportPackageEvidenceLinks(bundle),
    correlationId: `browser-certify-report-${bundle.financialStatements.packageId}`
  };
}

function collectAccountingCloseEvidenceLinks(
  workflow: OperationsContinuityWorkflow,
  closePlan: ClosePeriodPlan | null
): string[] {
  const links = new Set<string>();
  const add = (value: string | null | undefined) => {
    const normalized = value?.trim();
    if (normalized) {
      links.add(normalized);
    }
  };
  const addOperationsEvidence = (evidence: Array<{ evidenceId: string; route: string | null }> | null | undefined) => {
    evidence?.forEach((item) => {
      add(item.route);
      add(item.evidenceId);
    });
  };

  addOperationsEvidence(workflow.evidenceLinks);
  addOperationsEvidence(workflow.reportPackReadiness.evidenceLinks);
  addOperationsEvidence(workflow.accountingRecordSummary?.evidenceLinks);
  workflow.accountingRecordSummary?.evidenceCategories.forEach((category) => addOperationsEvidence(category.evidenceLinks));
  addOperationsEvidence(workflow.closePackage?.evidenceLinks);
  add(workflow.closePackage?.retainedManifestRoute);
  add(workflow.closePackage?.evidenceHash);
  closePlan?.tasks.forEach((task) => task.evidenceLinks.forEach(add));
  closePlan?.lateAdjustments.forEach((adjustment) => adjustment.evidenceLinks.forEach(add));

  return [...links];
}

function collectAccountingReportPackageEvidenceLinks(bundle: AccountingReportPackageBundle): string[] {
  const links = new Set<string>();
  for (const link of [
    ...bundle.financialStatements.evidenceLinks,
    ...bundle.investorCapitalStatements.flatMap((statement) => statement.evidenceLinks),
    ...bundle.realizedGainLoss.evidenceLinks,
    ...bundle.navPackage.evidenceLinks,
    ...bundle.certification.evidenceLinks,
    ...(bundle.financialStatements.restatement?.evidenceLinks ?? []),
    ...(bundle.navPackage.restatement?.evidenceLinks ?? [])
  ]) {
    const trimmed = link.trim();
    if (trimmed.length > 0) {
      links.add(trimmed);
    }
  }

  links.add(`evidence:report-certification:${bundle.financialStatements.packageId}`);
  return [...links].sort((left, right) => left.localeCompare(right));
}

function formatCloseTaskStatus(status: CloseTask["status"]): string {
  const labels: Record<CloseTask["status"], string> = {
    NotStarted: "Not started",
    WaitingOnDependency: "Waiting on dependency",
    InProgress: "In progress",
    ReadyForSignOff: "Ready for sign-off",
    SignedOff: "Signed off",
    Blocked: "Blocked"
  };
  return labels[status] ?? status;
}

function closeTaskStatusTone(status: CloseTask["status"]): AccountingToolingTone {
  if (status === "SignedOff") {
    return "success";
  }

  if (status === "Blocked") {
    return "danger";
  }

  if (status === "ReadyForSignOff" || status === "WaitingOnDependency") {
    return "warning";
  }

  return "default";
}

function formatAccountingCertificationState(state: AccountingCertificationState): string {
  const labels: Record<AccountingCertificationState, string> = {
    Draft: "Draft",
    ReadyForReview: "Ready for review",
    Certified: "Certified",
    Rejected: "Rejected",
    Superseded: "Superseded"
  };
  return labels[state] ?? state;
}

function accountingCertificationTone(state: AccountingCertificationState): AccountingToolingTone {
  if (state === "Certified") {
    return "success";
  }

  if (state === "Rejected") {
    return "danger";
  }

  if (state === "ReadyForReview" || state === "Superseded") {
    return "warning";
  }

  return "default";
}

export function buildCloseCommandCenterViewState({
  data,
  workflow,
  workflowLoading,
  workflowError,
  accountingSystemProviders,
  accountingSystemImport,
  accountingSystemReconciliation,
  multiAssetCoverage
}: {
  data: AccountingWorkspaceResponse;
  workflow: OperationsContinuityWorkflow | null;
  workflowLoading: boolean;
  workflowError: string | null;
  accountingSystemProviders: AccountingSystemProvider[];
  accountingSystemImport: AccountingSystemImportDetail | null;
  accountingSystemReconciliation: AccountingSystemReconciliationSummary | null;
  multiAssetCoverage: MultiAssetCoverageSummary | null | undefined;
}): CloseCommandCenterViewState {
  const openBreakCount = data.breakQueue.filter((item) => isOpenAccountingBreakStatus(item.status)).length;
  const workflowOpenBreakCount = workflow?.breakCases.filter((item) => isOpenAccountingBreakStatus(item.status)).length ?? 0;
  const closeBlockers = workflow ? collectCloseCommandCenterBlockers(workflow) : [];
  const incompleteEvidenceCategories = workflow?.accountingRecordSummary?.evidenceCategories.filter((category) => !category.isComplete) ?? [];
  const missingSourceCount = incompleteEvidenceCategories.filter((category) => closeCommandCenterTextMatches(category.key, category.label, "source")).length
    + (workflow?.closeChecklist.filter((task) => !isCloseChecklistDone(task.status) && !task.evidencePointer).length ?? 0);
  const missingEvidenceCount = incompleteEvidenceCategories.length
    + (workflow?.closeReadiness?.blockers.filter((blocker) => blocker.category.toLowerCase().includes("evidence")).length ?? 0);
  const pendingApprovalCount = workflow?.approvals.filter((approval) => approval.status !== "Approved").length ?? 0;
  const unapprovedChecklistCount = workflow?.closeChecklist.filter((task) => !isCloseChecklistDone(task.status) && task.requiredApprovalCount > 0).length ?? 0;
  const unapprovedAdjustmentCount = pendingApprovalCount + unapprovedChecklistCount;
  const staleValuationCount = countCloseCommandCenterValuationIssues(multiAssetCoverage);
  const providerWarningCount = countCloseCommandCenterProviderWarnings(accountingSystemProviders, accountingSystemImport, accountingSystemReconciliation);
  const reportPackReady = workflow?.reportPackReadiness.isReady ?? false;
  const reportPackLabel = workflow
    ? reportPackReady
      ? workflow.reportPackReadiness.reportPackId ?? "Ready"
      : "Not ready"
    : "Detail pending";
  const signOffStatus = resolveCloseCommandCenterSignOffStatus(workflow);
  const hasCriticalBlocker = closeBlockers.some((blocker) => closeCommandCenterSeverityTone(blocker.severity) === "danger");
  const hasFailedRequiredGate = workflow?.gates.some((gate) => gate.isRequired && gate.status === "Blocked") ?? false;
  const isReadyToClose = workflow?.closeReadiness?.isReadyToClose === true || workflow?.status === "ReadyForClose" || workflow?.status === "Closed";
  const status: CloseCommandCenterStatus = workflowLoading && !workflow
    ? "loading"
    : hasCriticalBlocker || hasFailedRequiredGate || workflow?.status === "Blocked"
      ? "blocked"
      : isReadyToClose
        && openBreakCount === 0
        && workflowOpenBreakCount === 0
        && missingEvidenceCount === 0
        && unapprovedAdjustmentCount === 0
        && staleValuationCount === 0
        && providerWarningCount === 0
        && reportPackReady
        && signOffStatus.tone === "success"
          ? "ready"
          : "at-risk";
  const statusTone = closeCommandCenterStatusTone(status);
  const readinessLabel = workflow?.closeReadiness?.severity
    ?? workflow?.status
    ?? data.controlCenter?.closeReadiness
    ?? "Close detail pending";
  const updatedLabel = workflow?.updatedAtUtc ?? accountingSystemReconciliation?.generatedAtUtc ?? multiAssetCoverage?.asOfUtc ?? "Not refreshed";

  const metricRows: CloseCommandCenterMetricViewModel[] = [
    {
      id: "period",
      label: "Period status",
      value: readinessLabel,
      detail: workflow ? `${workflow.status} workflow ${workflow.workflowId}` : "Using bootstrap close posture until workflow detail loads.",
      tone: statusTone,
      href: workflow ? WORKSTATION_ROUTE_CATALOG.accountingApprovals : null
    },
    {
      id: "breaks",
      label: "Open breaks",
      value: String(openBreakCount + workflowOpenBreakCount),
      detail: `${formatCount(openBreakCount, "queue break")} and ${formatCount(workflowOpenBreakCount, "workflow break")} remain open.`,
      tone: openBreakCount + workflowOpenBreakCount > 0 ? "warning" : "success",
      href: WORKSTATION_ROUTE_CATALOG.accountingReconciliation
    },
    {
      id: "source-files",
      label: "Missing source files",
      value: String(missingSourceCount),
      detail: missingSourceCount > 0 ? "Required source evidence or checklist evidence pointers are incomplete." : "Required source evidence is retained.",
      tone: missingSourceCount > 0 ? "warning" : "success",
      href: WORKSTATION_ROUTE_CATALOG.reportingEvidence
    },
    {
      id: "adjustments",
      label: "Unapproved adjustments",
      value: String(unapprovedAdjustmentCount),
      detail: unapprovedAdjustmentCount > 0 ? "Pending approvals or checklist controls remain before sign-off." : "No pending approval controls are surfaced.",
      tone: unapprovedAdjustmentCount > 0 ? "warning" : "success",
      href: WORKSTATION_ROUTE_CATALOG.accountingApprovals
    },
    {
      id: "valuations",
      label: "Stale valuations",
      value: String(staleValuationCount),
      detail: staleValuationCount > 0 ? "Asset readiness signals include valuation or stale-data review items." : "No stale valuation blockers are surfaced.",
      tone: staleValuationCount > 0 ? "warning" : "success",
      href: multiAssetCoverage?.drillThroughRoutes.coverage ?? null
    },
    {
      id: "providers",
      label: "Provider warnings",
      value: String(providerWarningCount),
      detail: providerWarningCount > 0 ? "Provider, external GL, or import reconciliation warnings need review." : "Provider and external GL evidence is clean.",
      tone: providerWarningCount > 0 ? "warning" : "success",
      href: WORKSTATION_ROUTE_CATALOG.accountingLedger
    },
    {
      id: "report-pack",
      label: "Report-pack readiness",
      value: reportPackLabel,
      detail: workflow?.reportPackReadiness.blockingReason ?? "Report-pack readiness comes from the selected close workflow.",
      tone: reportPackReady ? "success" : workflow ? "warning" : "default",
      href: WORKSTATION_ROUTE_CATALOG.reportingEvidence
    },
    {
      id: "signoff",
      label: "Sign-off status",
      value: signOffStatus.label,
      detail: signOffStatus.detail,
      tone: signOffStatus.tone,
      href: WORKSTATION_ROUTE_CATALOG.accountingApprovals
    }
  ];

  const blockerRows = [
    ...closeBlockers.map((blocker) => ({
      id: blocker.code,
      label: blocker.category ? `${blocker.category} - ${blocker.code}` : blocker.code,
      detail: blocker.message,
      tone: closeCommandCenterSeverityTone(blocker.severity),
      href: blocker.routeHint ?? closeCommandCenterGateRoute(blocker.gate)
    })),
    ...incompleteEvidenceCategories.slice(0, 3).map((category) => ({
      id: `evidence-${category.key}`,
      label: category.label,
      detail: category.requiredEvidence?.length
        ? `Missing: ${category.requiredEvidence.join(", ")}`
        : category.status,
      tone: "warning" as AccountingToolingTone,
      href: category.routeHint
    })),
    ...((data.controlCenter?.alerts ?? []).slice(0, 2).map((alert, index) => ({
      id: `bootstrap-alert-${index}`,
      label: "Control-center alert",
      detail: alert.message,
      tone: alert.tone === "danger" ? "danger" as AccountingToolingTone : "warning" as AccountingToolingTone,
      href: null
    })))
  ].slice(0, 8);
  return {
    title: "CFO / Controller close command center",
    description: "Controller-facing period readiness, close blockers, evidence gaps, provider warnings, report-pack readiness, and sign-off status from shared Accounting read models.",
    ariaLabel: "CFO and controller close command center",
    status,
    statusLabel: status === "ready" ? "Ready" : status === "blocked" ? "Blocked" : status === "loading" ? "Loading" : "At risk",
    statusTone,
    periodLabel: workflow?.periodId ?? "Current period",
    fundAccountLabel: workflow?.fundAccountId ?? multiAssetCoverage?.fundAccountId ?? "All accounts",
    summary: buildCloseCommandCenterSummary(status, metricRows, workflowError),
    updatedLabel,
    metricRows,
    blockerRows,
    actionRows: [
      {
        id: "reconciliation",
        label: "Review breaks",
        href: WORKSTATION_ROUTE_CATALOG.accountingReconciliation,
        ariaLabel: "Open Accounting reconciliation breaks from close command center",
        tone: openBreakCount + workflowOpenBreakCount > 0 ? "warning" : "success"
      },
      {
        id: "approvals",
        label: "Open approvals",
        href: WORKSTATION_ROUTE_CATALOG.accountingApprovals,
        ariaLabel: "Open Accounting approvals from close command center",
        tone: unapprovedAdjustmentCount > 0 || signOffStatus.tone !== "success" ? "warning" : "success"
      },
      {
        id: "reporting",
        label: "Report evidence",
        href: WORKSTATION_ROUTE_CATALOG.reportingEvidence,
        ariaLabel: "Open Reporting evidence from close command center",
        tone: reportPackReady ? "success" : "warning"
      }
    ],
    loadingText: workflowLoading ? "Refreshing close workflow detail." : null,
    errorText: workflowError,
    liveRegionText: `Close command center ${status}. ${readinessLabel}. ${formatCount(blockerRows.length, "visible blocker")}.`
  };
}

function collectCloseCommandCenterBlockers(workflow: OperationsContinuityWorkflow): CloseCommandCenterRawBlocker[] {
  const workflowBlockers = workflow.blockers.map((blocker) => ({
    code: blocker.code,
    category: blocker.gate ?? "Workflow",
    severity: blocker.severity,
    message: blocker.message,
    gate: blocker.gate,
    routeHint: null
  }));
  const gateBlockers = workflow.gates.flatMap((gate) => gate.blockers.map((blocker) => ({
    code: blocker.code,
    category: gate.displayName,
    severity: blocker.severity,
    message: blocker.message,
    gate: blocker.gate,
    routeHint: closeCommandCenterGateRoute(gate.gateKey)
  })));
  const readinessBlockers = workflow.closeReadiness?.blockers.map((blocker) => ({
    code: blocker.code,
    category: blocker.category,
    severity: blocker.severity,
    message: blocker.message,
    gate: blocker.gate,
    routeHint: blocker.routeHint
  })) ?? [];

  const byKey = new Map<string, CloseCommandCenterRawBlocker>();
  for (const blocker of [...workflowBlockers, ...gateBlockers, ...readinessBlockers]) {
    byKey.set(`${blocker.code}:${blocker.message}`, blocker);
  }

  return [...byKey.values()];
}

function isCloseChecklistDone(status: string): boolean {
  const normalized = status.trim().toLowerCase();
  return normalized === "done" || normalized === "complete" || normalized === "completed" || normalized === "acknowledged";
}

function closeCommandCenterTextMatches(left: string | null | undefined, right: string | null | undefined, needle: string): boolean {
  const normalizedNeedle = needle.toLowerCase();
  return [left, right]
    .map((value) => value?.toLowerCase() ?? "")
    .some((value) => value.includes(normalizedNeedle));
}

function countCloseCommandCenterValuationIssues(coverage: MultiAssetCoverageSummary | null | undefined): number {
  if (!coverage) {
    return 0;
  }

  return coverage.assetClasses.filter((assetClass) => {
    const blockerMatch = assetClass.blockers.some((blocker) => (
      closeCommandCenterTextMatches(blocker.code, blocker.message, "valuation")
      || closeCommandCenterTextMatches(blocker.source, blocker.message, "stale")
    ));
    const requirementMatch = assetClass.evidenceRequirements.some((requirement) => (
      requirement.status !== "Ready"
      && (closeCommandCenterTextMatches(requirement.label, requirement.category, "valuation")
        || closeCommandCenterTextMatches(requirement.label, requirement.category, "stale"))
    ));

    return blockerMatch || requirementMatch;
  }).length;
}

function countCloseCommandCenterProviderWarnings(
  providers: AccountingSystemProvider[],
  importDetail: AccountingSystemImportDetail | null,
  reconciliation: AccountingSystemReconciliationSummary | null
): number {
  const unavailableProviders = providers.filter((provider) => provider.state !== "Available").length;
  const importWarnings = importDetail?.summary.warnings.length ?? 0;
  const reconciliationRows = reconciliation?.rows.filter((row) => row.status !== "Matched").length ?? 0;

  return unavailableProviders + importWarnings + reconciliationRows;
}

function resolveCloseCommandCenterSignOffStatus(workflow: OperationsContinuityWorkflow | null): {
  label: string;
  detail: string;
  tone: AccountingToolingTone;
} {
  if (!workflow) {
    return {
      label: "Detail pending",
      detail: "Load the close workflow before sign-off status can be confirmed.",
      tone: "default"
    };
  }

  if (workflow.closePackage) {
    return {
      label: `Signed by ${workflow.closePackage.publishedBy}`,
      detail: workflow.closePackage.signOffRationale,
      tone: "success"
    };
  }

  const approvedCount = workflow.approvals.filter((approval) => approval.status === "Approved").length;
  const totalApprovalCount = workflow.approvals.length;
  if (totalApprovalCount > 0) {
    return {
      label: `${approvedCount}/${totalApprovalCount} approved`,
      detail: approvedCount === totalApprovalCount
        ? "Approval rows are complete; publish the close package when report evidence is ready."
        : "Approval rows still need reviewer decisions before close sign-off.",
      tone: approvedCount === totalApprovalCount ? "success" : "warning"
    };
  }

  return {
    label: workflow.approvalState,
    detail: "No approval rows are attached to the selected close workflow.",
    tone: workflow.approvalState === "Approved" ? "success" : "warning"
  };
}

function closeCommandCenterSeverityTone(severity: string): AccountingToolingTone {
  const normalized = severity.trim().toLowerCase();
  if (normalized === "critical" || normalized === "blocker" || normalized === "danger") {
    return "danger";
  }

  if (normalized === "warning" || normalized === "warn" || normalized === "review") {
    return "warning";
  }

  return "default";
}

function closeCommandCenterStatusTone(status: CloseCommandCenterStatus): AccountingToolingTone {
  if (status === "ready") return "success";
  if (status === "blocked") return "danger";
  if (status === "at-risk") return "warning";
  return "default";
}

function closeCommandCenterGateRoute(gate: OperationsWorkflowBlocker["gate"]): string | null {
  if (gate === "Reconciliation") return WORKSTATION_ROUTE_CATALOG.accountingReconciliation;
  if (gate === "Approval") return WORKSTATION_ROUTE_CATALOG.accountingApprovals;
  if (gate === "LedgerPosting") return WORKSTATION_ROUTE_CATALOG.accountingLedger;
  if (gate === "SecurityMaster") return WORKSTATION_ROUTE_CATALOG.accountingSecurityMaster;
  if (gate === "BrokerIngest") return WORKSTATION_ROUTE_CATALOG.accountingLedger;
  return null;
}

function buildCloseCommandCenterSummary(
  status: CloseCommandCenterStatus,
  metricRows: CloseCommandCenterMetricViewModel[],
  workflowError: string | null
): string {
  if (workflowError) {
    return `Close workflow detail could not be refreshed: ${workflowError}`;
  }

  const activeRows = metricRows.filter((row) => row.tone === "warning" || row.tone === "danger");
  if (status === "ready") {
    return "The close is ready: breaks, source evidence, approvals, valuations, providers, report pack, and sign-off are clear.";
  }

  if (status === "blocked") {
    return `The close is blocked by ${formatCount(activeRows.length, "active control")}; review the blocker list before sign-off.`;
  }

  if (status === "loading") {
    return "Refreshing workflow detail before confirming the close state.";
  }

  return `The close is at risk with ${formatCount(activeRows.length, "watch item")} across the command center.`;
}

function isOpenAccountingBreakStatus(status: string): boolean {
  const normalized = status.trim().toLowerCase();
  return normalized !== "resolved" && normalized !== "dismissed" && normalized !== "closed";
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
    ariaLabel: `Open GET ${href} for ${label}`
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
  error
}: {
  runId: string | null;
  rows: LedgerTrialBalanceLine[];
  selectedRowId?: string | null;
  selectedBasis?: AccountingBasisKind | null;
  accountFilter?: string | null;
  loading: boolean;
  error: string | ApiErrorDisplay | null;
}): AccountingTrialBalanceViewState {
  const detailPanelId = "trial-balance-account-detail";
  const runLabel = runId ?? "selected run";
  const resolvedBasis = normalizeAccountingBasis(selectedBasis);
  const normalizedAccountFilter = normalizeLedgerAccountFilter(accountFilter);
  const normalizedRows = rows.map(normalizeTrialBalanceLine);
  const basisOptions = buildTrialBalanceBasisOptions(normalizedRows, resolvedBasis);
  const bridge = buildBasisBridgeViewState(normalizedRows, resolvedBasis, runLabel);
  const basisRows = normalizedRows
    .filter((line) => line.accountingBasis === resolvedBasis)
    .map((line) => buildTrialBalanceRow(line, detailPanelId));
  const accountFilterOptions = buildLedgerAccountFilterOptions(basisRows, normalizedAccountFilter);
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
    selectedRowId: resolvedSelectedRowId,
    detailPanelId,
    selectedDetail: selectedRow ? buildTrialBalanceDetail(selectedRow, runLabel, runId) : null,
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
      : `Meridian did not return account-balance rows for ${runLabel}. Select another reconciliation run or refresh ledger evidence before report handoff.`,
    errorText,
    errorDetails: normalizedError?.details ?? [],
    statusAnnouncement: buildTrialBalanceAnnouncement({ runLabel, state, rowCount: viewRows.length, loading, errorText })
  };
}

type BasisAwareLedgerTrialBalanceLine = LedgerTrialBalanceLine & {
  accountingBasis: AccountingBasisKind;
  accountingPolicyId: string;
  accountingPolicyVersion: string;
};

function buildTrialBalanceRow(
  line: BasisAwareLedgerTrialBalanceLine,
  detailPanelId: string
): AccountingTrialBalanceRowViewModel {
  const accountLabel = line.accountName.trim() || "Unnamed account";
  const accountTypeLabel = line.accountType.trim() || "Unclassified";
  const basisName = accountingBasisDisplayName(line.accountingBasis);
  const basisLabel = `${basisName} basis`;
  const policyLabel = `${line.accountingPolicyId}/${line.accountingPolicyVersion}`;
  const balanceLabel = formatCurrency(line.balance);
  const entryCountLabel = line.entryCount.toLocaleString();
  const securityLabel = line.security?.primaryIdentifier?.trim() || line.symbol?.trim() || line.security?.displayName.trim() || null;
  const rowId = [
    line.accountingBasis,
    accountLabel,
    accountTypeLabel,
    line.financialAccountId,
    securityLabel
  ].filter(Boolean).join("-");

  return {
    ...line,
    rowId,
    accountLabel,
    accountTypeLabel,
    basisLabel,
    basisTone: trialBalanceBasisTone(line.accountingBasis),
    policyLabel,
    balanceLabel,
    balanceTone: line.balance < 0 ? "danger" : line.balance > 0 ? "success" : "default",
    entryCountLabel,
    ariaLabel: [
      `${accountLabel} ${accountTypeLabel}`,
      basisLabel,
      `Policy ${policyLabel}`,
      `Balance ${balanceLabel}`,
      `${entryCountLabel} entries`,
      securityLabel ? `Security ${securityLabel}` : null
    ].filter(Boolean).join(". "),
    selectAriaLabel: `Inspect trial-balance account ${accountLabel} for ${accountTypeLabel}`,
    detailPanelId,
    isExpanded: false
  };
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
    row.security?.primaryIdentifier
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
  runId: string | null
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
    subtitle: `${line.accountTypeLabel} · ${financialAccountId}`,
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
      { label: "Security", value: securityLabel },
      { label: "Journal entries", value: sourceJournalEntryIds.length > 0 ? sourceJournalEntryIds.join(", ") : "No journal entry references linked" },
      { label: "Source events", value: sourceEventIds.length > 0 ? sourceEventIds.join(", ") : "No source events linked" },
      { label: "Approvals", value: approvalIds.length > 0 ? approvalIds.join(", ") : "No approvals linked" },
      { label: "Run", value: runLabel }
    ],
    auditDrillThroughLabel: firstSourceEventId ? `Open source event ${firstSourceEventId}` : "No source-event drill-through available",
    auditDrillThroughHref,
    approvalDrillThroughHref,
    ledgerLinesTitle: "Ledger lines for selected account",
    ledgerLinesDescription: firstJournalEntryId
      ? `Journal support linked to ${line.accountLabel} for ${runLabel}.`
      : `Account-level ledger inquiry for ${line.accountLabel}; journal-entry ids appear when the ledger payload includes posting references.`,
    ledgerLines: buildLedgerLineRows(line, sourceJournalEntryIds, sourceEventIds, approvalDrillThroughHref),
    ledgerLinesEmptyText: "No ledger line support is attached to this account row yet.",
    supportingDocumentsTitle: "Supporting documentation",
    supportingDocuments: buildSupportingDocumentRows({
      line,
      runId,
      sourceEventIds,
      approvalIds,
      sourceJournalEntryIds
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
    ? `Source ${sourceEventIds[0]}`
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
  sourceJournalEntryIds
}: {
  line: AccountingTrialBalanceRowViewModel;
  runId: string | null;
  sourceEventIds: string[];
  approvalIds: string[];
  sourceJournalEntryIds: string[];
}): AccountingSupportingDocumentViewModel[] {
  const rows: AccountingSupportingDocumentViewModel[] = [];

  if (runId) {
    rows.push({
      id: `${line.rowId}-review-packet`,
      label: "Run review packet",
      detail: `Ledger, reconciliation, and evidence packet for ${runId}.`,
      href: getRunReviewPacketPath(runId),
      ariaLabel: `Open run review packet for ${line.accountLabel}`
    });
  }

  for (const sourceEventId of sourceEventIds) {
    rows.push({
      id: `${line.rowId}-source-${sourceEventId}`,
      label: `Source event ${sourceEventId}`,
      detail: "Source transaction, provider activity, or retained event evidence.",
      href: `/accounting/audit?sourceEventId=${encodeURIComponent(sourceEventId)}`,
      ariaLabel: `Open source event ${sourceEventId} for ${line.accountLabel}`
    });
  }

  for (const journalEntryId of sourceJournalEntryIds) {
    rows.push({
      id: `${line.rowId}-journal-${journalEntryId}`,
      label: `Journal entry ${journalEntryId}`,
      detail: "Posting support and ledger entry lineage.",
      href: `/accounting/ledger?journalEntryId=${encodeURIComponent(journalEntryId)}`,
      ariaLabel: `Open journal entry ${journalEntryId} for ${line.accountLabel}`
    });
  }

  for (const approvalId of approvalIds) {
    rows.push({
      id: `${line.rowId}-approval-${approvalId}`,
      label: `Approval ${approvalId}`,
      detail: "Controller approval and maker/checker evidence.",
      href: `/accounting/approvals?approvalId=${encodeURIComponent(approvalId)}`,
      ariaLabel: `Open approval ${approvalId} for ${line.accountLabel}`
    });
  }

  return rows;
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

function readSourceEventIds(value: unknown): string[] {
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

export function buildGovernanceTrialBalanceViewState(
  options: Parameters<typeof buildAccountingTrialBalanceViewState>[0]
): AccountingTrialBalanceViewState {
  return buildAccountingTrialBalanceViewState(options);
}

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

function buildBasisBridgeViewState(
  rows: BasisAwareLedgerTrialBalanceLine[],
  selectedBasis: AccountingBasisKind,
  runLabel: string
): AccountingBasisBridgeViewState {
  const comparisonBasis = selectedBasis === "Primary"
    ? rows.find((row) => row.accountingBasis !== "Primary")?.accountingBasis ?? "Gaap"
    : selectedBasis;
  const primaryRows = rows.filter((row) => row.accountingBasis === "Primary");
  const comparisonRows = rows.filter((row) => row.accountingBasis === comparisonBasis);
  const tableLabel = `${accountingBasisDisplayName(comparisonBasis)} to Primary basis bridge for ${runLabel}`;

  if (comparisonBasis === "Primary" || primaryRows.length === 0 || comparisonRows.length === 0) {
    return {
      title: "Basis bridge",
      description: `${accountingBasisDisplayName(comparisonBasis)} to Primary comparison grouped by source/rule/account where lineage is available.`,
      tableLabel,
      fromBasis: "Primary",
      toBasis: comparisonBasis,
      rows: [],
      hasRows: false,
      emptyText: "No non-primary basis rows are available for this run yet. The bridge will populate after GAAP, Cash, Tax, or Statutory projection posts journal lines."
    };
  }

  const primaryByKey = new Map(primaryRows.map((row) => [basisBridgeKey(row), row]));
  const comparisonByKey = new Map(comparisonRows.map((row) => [basisBridgeKey(row), row]));
  const keys = [...new Set([...primaryByKey.keys(), ...comparisonByKey.keys()])].sort((left, right) => left.localeCompare(right));
  const bridgeRows = keys.map((key) => {
    const primary = primaryByKey.get(key) ?? null;
    const comparison = comparisonByKey.get(key) ?? null;
    const source = comparison ?? primary;
    const primaryBalance = primary?.balance ?? 0;
    const comparisonBalance = comparison?.balance ?? 0;
    const variance = comparisonBalance - primaryBalance;
    const sourceLabel = buildBasisBridgeSourceLabel(source);
    const accountLabel = source?.accountName.trim() || "Unnamed account";
    const accountTypeLabel = source?.accountType.trim() || "Unclassified";
    const varianceLabel = formatCurrency(variance);

    return {
      rowId: `${comparisonBasis}-${key}`,
      accountLabel,
      accountTypeLabel,
      primaryBalanceLabel: formatCurrency(primaryBalance),
      comparisonBalanceLabel: formatCurrency(comparisonBalance),
      varianceLabel,
      varianceTone: variance < 0 ? "danger" : variance > 0 ? "success" : "default",
      sourceLabel,
      ariaLabel: `${accountLabel} ${accountTypeLabel}. Primary ${formatCurrency(primaryBalance)}. ${accountingBasisDisplayName(comparisonBasis)} ${formatCurrency(comparisonBalance)}. Variance ${varianceLabel}.`
    } satisfies AccountingBasisBridgeRowViewModel;
  });

  return {
    title: "Basis bridge",
    description: `${accountingBasisDisplayName(comparisonBasis)} compared with Primary for ${runLabel}, grouped by source/rule/account where lineage is available.`,
    tableLabel,
    fromBasis: "Primary",
    toBasis: comparisonBasis,
    rows: bridgeRows,
    hasRows: bridgeRows.length > 0,
    emptyText: "No bridge rows matched the selected basis pair."
  };
}

function basisBridgeKey(line: BasisAwareLedgerTrialBalanceLine): string {
  const sourceEventId = readSourceEventIds(line).join(",");
  const ruleId = "ruleId" in line ? String(line.ruleId ?? "") : "";
  return [
    sourceEventId,
    ruleId,
    line.accountName,
    line.accountType,
    line.symbol ?? "",
    line.financialAccountId ?? ""
  ].join("|");
}

function buildBasisBridgeSourceLabel(line: BasisAwareLedgerTrialBalanceLine | null): string {
  if (!line) {
    return "Missing source group";
  }

  const sourceEventIds = readSourceEventIds(line);
  const ruleId = "ruleId" in line ? String(line.ruleId ?? "").trim() : "";
  if (sourceEventIds.length > 0 || ruleId) {
    return [
      sourceEventIds.length > 0 ? `Source ${sourceEventIds.join(", ")}` : null,
      ruleId ? `Rule ${ruleId}` : null
    ].filter(Boolean).join(" / ");
  }

  return line.symbol?.trim() || line.financialAccountId?.trim() || "Account group";
}

function accountingBasisDisplayName(basis: AccountingBasisKind): string {
  return basis === "Gaap" ? "GAAP" : basis;
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

function formatCount(count: number, singular: string): string {
  return `${count} ${singular}${count === 1 ? "" : "s"}`;
}

function formatBytes(value: number): string {
  if (!Number.isFinite(value) || value <= 0) {
    return "0 B";
  }

  const units = ["B", "KB", "MB", "GB"];
  let size = value;
  let unitIndex = 0;
  while (size >= 1024 && unitIndex < units.length - 1) {
    size /= 1024;
    unitIndex += 1;
  }

  const formatted = size >= 10 || unitIndex === 0
    ? size.toFixed(0)
    : size.toFixed(1).replace(/\.0$/, "");
  return `${formatted} ${units[unitIndex]}`;
}

function formatCurrency(value: number) {
  const prefix = value >= 0 ? "$" : "-$";
  return `${prefix}${Math.abs(value).toLocaleString(undefined, { maximumFractionDigits: 2 })}`;
}

function formatCurrencyWithCode(value: number, currency: string, signed = false): string {
  const amount = signed ? formatSignedCurrency(value) : formatCurrency(value);
  const code = currency.trim();
  return code ? `${amount} ${code}` : amount;
}

function formatSignedCurrency(value: number): string {
  if (value === 0) {
    return "$0";
  }

  const sign = value > 0 ? "+" : "-";
  return `${sign}$${Math.abs(value).toLocaleString(undefined, { maximumFractionDigits: 2, minimumFractionDigits: 2 })}`;
}

function formatDateTimeLabel(value: string | null | undefined): string {
  if (!value) {
    return "Not recorded";
  }

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return value;
  }

  return `${UTC_MONTH_LABELS[date.getUTCMonth()]} ${date.getUTCDate()}, ${padUtc(date.getUTCHours())}:${padUtc(date.getUTCMinutes())} UTC`;
}

function padUtc(value: number): string {
  return String(value).padStart(2, "0");
}

const UTC_MONTH_LABELS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

function toDomId(value: string): string {
  const normalized = value.trim().toLowerCase().replace(/[^a-z0-9_-]+/g, "-").replace(/^-+|-+$/g, "");
  return normalized || "profile";
}

function buildSecurityConflictAction(
  conflict: SecurityMasterConflict,
  resolution: SecurityConflictResolution,
  label: string,
  enabled: boolean,
  variant: "outline" | "ghost",
  disabledReason: string | null
): SecurityConflictActionViewModel {
  const choice =
    resolution === "AcceptA"
      ? `${conflict.providerA} value ${formatSecurityReferenceValue(conflict.valueA)}`
      : resolution === "AcceptB"
        ? `${conflict.providerB} value ${formatSecurityReferenceValue(conflict.valueB)}`
        : "no provider value";
  const baseAriaLabel = resolution === "Dismiss"
    ? `Dismiss identifier conflict ${conflict.conflictId} on ${conflict.fieldPath}`
    : `Resolve identifier conflict ${conflict.conflictId} on ${conflict.fieldPath} with ${choice}`;

  return {
    resolution,
    label,
    ariaLabel: enabled || !disabledReason ? baseAriaLabel : `${baseAriaLabel}. Disabled: ${disabledReason}`,
    variant,
    disabled: !enabled,
    disabledReason: enabled ? null : disabledReason
  };
}

function buildSecurityIdentityIdentifierRow(
  identifier: SecurityIdentifierEntry
): SecurityIdentityIdentifierRowViewModel {
  const providerLabel = valueOrDash(identifier.provider);
  const primaryLabel = identifier.isPrimary ? "Primary" : "Secondary";
  const validRangeLabel = formatSecurityDateRange(identifier.validFrom, identifier.validTo);

  return {
    ...identifier,
    rowId: `identifier-${toDomId(`${identifier.kind}-${identifier.value}`)}`,
    providerLabel,
    primaryLabel,
    primaryBadgeVariant: identifier.isPrimary ? "success" : "outline",
    validRangeLabel,
    ariaLabel: `${identifier.kind} ${identifier.value}, ${primaryLabel}, provider ${providerLabel}, valid ${validRangeLabel}`
  };
}

function buildSecurityIdentityAliasRow(alias: SecurityAliasEntry): SecurityIdentityAliasRowViewModel {
  const providerLabel = valueOrDash(alias.provider);
  const enabledLabel = alias.isEnabled ? "Enabled" : "Disabled";
  const validRangeLabel = formatSecurityDateRange(alias.validFrom, alias.validTo);

  return {
    ...alias,
    rowId: `alias-${toDomId(alias.aliasId)}`,
    providerLabel,
    enabledLabel,
    enabledBadgeVariant: alias.isEnabled ? "success" : "warning",
    validRangeLabel,
    createdLabel: formatSecurityDate(alias.createdAt),
    reasonText: alias.reason?.trim() || "No alias reason recorded.",
    ariaLabel: `${alias.aliasKind} ${alias.aliasValue}, ${enabledLabel}, scope ${alias.scope}, provider ${providerLabel}, valid ${validRangeLabel}`
  };
}

function statusBadgeVariantForSecurityIdentity(
  status: string | null | undefined
): SecurityIdentityDrillInViewState["statusBadgeVariant"] {
  const normalized = status?.trim().toLowerCase();
  if (normalized === "active") {
    return "success";
  }

  if (normalized === "pending" || normalized === "inactive" || normalized === "deactivated") {
    return "warning";
  }

  return "outline";
}

function formatSecurityReferenceValue(value: string): string {
  return value.length > 8 ? `${value.substring(0, 8)}...` : value;
}

function formatSecurityDate(value: string | null | undefined): string {
  if (!value) {
    return "—";
  }

  const match = /^\d{4}-\d{2}-\d{2}/.exec(value);
  return match?.[0] ?? value;
}

function formatSecurityDateRange(from: string | null | undefined, to: string | null | undefined): string {
  return `${formatSecurityDate(from)} -> ${to ? formatSecurityDate(to) : "active"}`;
}

function formatConflictDate(value: string): string {
  const match = /^\d{4}-\d{2}-\d{2}/.exec(value);
  return match?.[0] ?? value;
}

function valueOrDash(value: string | null | undefined): string {
  return value?.trim() || "—";
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

function buildReconciliationBreakStatusAnnouncement({
  loading,
  action,
  loadError,
  actionError,
  breakCount
}: {
  loading: boolean;
  action: ReconciliationBreakAction | null;
  loadError: string | null;
  actionError: string | null;
  breakCount: number;
}): string {
  if (loading) {
    return "Loading reconciliation break queue.";
  }

  if (action?.command === "assign") {
    return `Assigning reconciliation break ${action.breakId}.`;
  }

  if (action?.command === "resolve") {
    return `Resolving reconciliation break ${action.breakId}.`;
  }

  if (action?.command === "dismiss") {
    return `Dismissing reconciliation break ${action.breakId}.`;
  }

  if (actionError) {
    return actionError;
  }

  if (loadError) {
    return loadError;
  }

  if (breakCount === 0) {
    return "No reconciliation breaks in the current queue.";
  }

  return `${breakCount} reconciliation ${breakCount === 1 ? "break" : "breaks"} loaded.`;
}

export function resolveSecurityScheduleEvents(securityId: string | null): SecurityCashFlowScheduleEvent[] {
  if (!securityId) {
    return [];
  }

  return (securityScheduleFixtures[securityId] ?? []).map((event) => ({ ...event }));
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
  const readyCount = rows.filter((row) => row.status === "Ready").length;
  const reviewCount = rows.filter((row) => row.status === "Empty" || row.status === "Missing" || row.status === "Blocked" || row.status === "Error").length;
  const deferredCount = rows.filter((row) => row.status === "Deferred").length;
  const routeCount = rows.length;
  const displaySecurityId = securityId ?? "selected security";

  return {
    securityId,
    title: "Multi-asset reference data",
    description: securityId
      ? `Endpoint-backed coverage for ${displaySecurityId} across bonds, options, equities, futures, FX spot, swaps, commodities, crypto, deposits, money-market funds, CDs, and EDGAR.`
      : "Select a security to inspect multi-asset reference data endpoint coverage.",
    metrics: [
      {
        id: "routes",
        label: "Mapped routes",
        value: routeCount > 0 ? routeCount.toLocaleString() : "Pending",
        detail: routeCount > 0 ? `${formatCount(routeCount, "reference endpoint")} catalogued for this selection.` : "Select a security to build endpoint probes.",
        tone: routeCount > 0 ? "default" : "warning"
      },
      {
        id: "ready",
        label: "Ready payloads",
        value: readyCount.toLocaleString(),
        detail: readyCount > 0 ? `${formatCount(readyCount, "endpoint")} returned payload data.` : "No endpoint has returned payload data yet.",
        tone: readyCount > 0 ? "success" : "default"
      },
      {
        id: "review",
        label: "Needs review",
        value: reviewCount.toLocaleString(),
        detail: reviewCount > 0 ? `${formatCount(reviewCount, "endpoint")} returned empty, missing, blocked, or error status.` : "No probed endpoint is flagged for review.",
        tone: reviewCount > 0 ? "warning" : "success"
      },
      {
        id: "deferred",
        label: "Deferred mutations",
        value: deferredCount.toLocaleString(),
        detail: deferredCount > 0 ? `${formatCount(deferredCount, "mutation endpoint")} catalogued without invocation.` : "No mutation endpoint is present in the catalog.",
        tone: deferredCount > 0 ? "warning" : "default"
      }
    ],
    rows,
    selectedRowId: effectiveSelectedRowId,
    selectedDetail: selectedRow ? buildReferenceDataEndpointDetailViewState(selectedRow, coverage) : null,
    tableLabel: `Reference data endpoint coverage for ${displaySecurityId}`,
    tableCaption: `Read-only probe status for mapped reference data endpoints backing ${displaySecurityId}.`,
    detailPanelId: "reference-data-endpoint-detail",
    emptyText: securityId ? "No reference endpoint rows are available for this security." : "Select a security to load reference endpoint coverage.",
    detailEmptyTitle: "No endpoint selected",
    detailEmptyText: "Select an endpoint row to inspect request details, response preview, and error evidence.",
    detailEmptyAriaLabel: `Reference data endpoint detail for ${displaySecurityId}`,
    loadingText: loading ? "Loading multi-asset reference data coverage..." : null,
    errorText,
    errorDetails: normalizedError?.details ?? [],
    hasRows: rows.length > 0,
    statusAnnouncement: errorText
      ? `Reference data workbench error: ${errorText}`
      : loading
        ? `Loading multi-asset reference data coverage for ${displaySecurityId}.`
        : rows.length > 0
          ? `${formatCount(rows.length, "reference endpoint")} loaded for ${displaySecurityId}; ${formatCount(readyCount, "endpoint")} ready.`
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

    return {
      ...endpoint,
      rowId,
      familyLabel: endpoint.family,
      methodLabel: endpoint.method,
      statusLabel,
      statusBadgeVariant,
      countLabel,
      latencyLabel,
      ariaLabel: `${endpoint.family} ${endpoint.label}, ${endpoint.method} ${endpoint.path}, ${statusLabel}. ${endpoint.responseSummary}`,
      selectAriaLabel: `Inspect ${endpoint.label} reference endpoint`,
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
    subtitle: `${row.methodLabel} ${row.path}`,
    description: row.errorSummary ?? row.responseSummary,
    ariaLabel: `${row.label} reference data endpoint detail`,
    statusLabel: row.statusLabel,
    statusBadgeVariant: row.statusBadgeVariant,
    fields: [
      { label: "Family", value: row.familyLabel },
      { label: "Method", value: row.methodLabel, tone: row.mutation ? "warning" : "default" },
      { label: "Endpoint", value: row.path },
      { label: "Request", value: row.requestLabel },
      { label: "Status", value: row.statusLabel, tone: referenceDataStatusTone(row.status) },
      { label: "Payload", value: row.countLabel },
      { label: "Latency", value: row.latencyLabel },
      { label: "Catalogued", value: coverage?.requestedAtUtc ? formatDateTimeLabel(coverage.requestedAtUtc) : "-" }
    ],
    responsePreview: row.responsePreview,
    errorSummary: row.errorSummary,
    errorDetails: row.errorDetails
  };
}

function referenceDataStatusLabel(status: ReferenceDataEndpointProbeResult["status"]): string {
  const labels: Record<ReferenceDataEndpointProbeResult["status"], string> = {
    Ready: "Ready",
    Empty: "Empty",
    Missing: "Missing",
    Blocked: "Blocked",
    Error: "Error",
    Deferred: "Deferred"
  };

  return labels[status];
}

function referenceDataStatusBadgeVariant(
  status: ReferenceDataEndpointProbeResult["status"]
): ReferenceDataEndpointRowViewModel["statusBadgeVariant"] {
  if (status === "Ready") {
    return "success";
  }

  if (status === "Error" || status === "Blocked") {
    return "danger";
  }

  if (status === "Empty" || status === "Missing") {
    return "warning";
  }

  return "outline";
}

function referenceDataStatusTone(status: ReferenceDataEndpointProbeResult["status"]): SecurityScheduleDetailFieldViewModel["tone"] {
  if (status === "Ready") {
    return "success";
  }

  if (status === "Error" || status === "Blocked") {
    return "danger";
  }

  if (status === "Empty" || status === "Missing" || status === "Deferred") {
    return "warning";
  }

  return "default";
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
  const trustTone = passport?.trustPosture.tone?.trim() || "Unknown";
  const trustSummary = passport?.trustPosture.summary?.trim() || "Trust posture is unavailable.";
  const identifierSummary = passport?.identifierSummary.summary?.trim() || "Identifier summary is unavailable.";
  const usageSummary = passport?.usage.summary?.trim() || "Downstream usage is unavailable.";
  const pricingStatus = passport?.pricing?.status?.trim() || "Unknown";
  const pricingSummary = passport?.pricing?.summary?.trim() || "Pricing and trading controls are unavailable.";
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
      ? `${passport.identity.displayName} passport combines identifiers, provider confidence, lifecycle, pricing, and downstream usage evidence.`
      : `Instrument passport evidence for ${displaySecurityId}.`,
    statusLabel: trustTone,
    statusBadgeVariant,
    fields: [
      { label: "Security ID", value: passport?.securityId ?? displaySecurityId },
      { label: "Display name", value: passport?.identity.displayName ?? "-" },
      { label: "Asset class", value: passport?.identity.assetClass ?? "-" },
      { label: "Trust", value: trustSummary, tone: statusBadgeVariant === "success" ? "success" : statusBadgeVariant === "warning" ? "warning" : "default" },
      { label: "Identifiers", value: identifierSummary },
      { label: "Provider confidence", value: `${activeProviderCount} active / ${providerRows.length} total` },
      { label: "Pricing", value: `${pricingStatus}: ${pricingSummary}` },
      { label: "Usage", value: usageSummary },
      { label: "Retrieved", value: passport?.retrievedAtUtc ? formatDateTimeLabel(passport.retrievedAtUtc) : "-" }
    ],
    providerRows,
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

function areReconciliationBreakQueuesEquivalent(
  current: ReconciliationBreakQueueItem[],
  next: ReconciliationBreakQueueItem[]
): boolean {
  if (current === next) {
    return true;
  }

  if (current.length !== next.length) {
    return false;
  }

  for (let index = 0; index < current.length; index += 1) {
    const left = current[index];
    const right = next[index];

    if (
      left.breakId !== right.breakId ||
      left.runId !== right.runId ||
      left.strategyName !== right.strategyName ||
      left.category !== right.category ||
      left.status !== right.status ||
      left.variance !== right.variance ||
      left.reason !== right.reason ||
      left.assignedTo !== right.assignedTo ||
      left.detectedAt !== right.detectedAt ||
      left.lastUpdatedAt !== right.lastUpdatedAt ||
      left.reviewedBy !== right.reviewedBy ||
      left.reviewedAt !== right.reviewedAt ||
      left.resolvedBy !== right.resolvedBy ||
      left.resolvedAt !== right.resolvedAt ||
      left.resolutionNote !== right.resolutionNote ||
      left.routingTarget !== right.routingTarget ||
      left.routingDetail !== right.routingDetail ||
      left.recommendedAction !== right.recommendedAction
    ) {
      return false;
    }
  }

  return true;
}

function replaceBreakQueueItem(
  current: ReconciliationBreakQueueItem[],
  updated: ReconciliationBreakQueueItem
): ReconciliationBreakQueueItem[] {
  if (!current.some((item) => item.breakId === updated.breakId)) {
    return [updated, ...current];
  }

  return current.map((item) => (item.breakId === updated.breakId ? updated : item));
}

function normalizeApiErrorDisplay(error: string | ApiErrorDisplay | null): ApiErrorDisplay | null {
  if (!error) {
    return null;
  }

  if (typeof error === "string") {
    return { summary: error, details: [] };
  }

  return error;
}
