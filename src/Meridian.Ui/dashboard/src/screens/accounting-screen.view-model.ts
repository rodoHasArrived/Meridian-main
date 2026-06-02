import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { describeApiError, type ApiErrorDisplay } from "@/lib/api-errors";
import {
  activateAccountingConfiguration,
  getAccountingConfiguration,
  getCorporateActions,
  getReconciliationBreakQueue,
  getReconciliationCalibrationSummary,
  getReconciliationStatementRun,
  getReconciliationStatementRuns,
  getRunReviewPacketPath,
  getRunTrialBalance,
  getSecurityConflicts,
  getSecurityIdentity,
  getSecurityTrustSnapshot,
  previewInvestmentAccountingTransaction,
  getTradingParameters,
  runAnalysisExport,
  previewAccountingConfigurationTemplate,
  resolveReconciliationBreak,
  resolveSecurityConflict,
  reviewReconciliationBreak,
  searchSecurities
} from "@/lib/api";
import {
  evidenceWorkbenchPath,
  normalizeLocalWorkstationRoute,
  WORKSTATION_ROUTE_CATALOG,
  workflowTargetPath
} from "@/lib/workspace";
import { EXPORT_API_ENDPOINTS } from "@/lib/workstation-endpoints";
import type {
  AccountingBasisKind,
  AccountingConfigurationWorkspace,
  AccountingJournalTemplatePreview,
  CorporateAction,
  ExportAnalysisResult,
  AccountingCashFlowSummary,
  AccountingReportingProfile,
  AccountingReportingSummary,
  AccountingWorkspaceResponse,
  LedgerTrialBalanceLine,
  ReconciliationBreakQueueItem,
  ReconciliationCalibrationSummary,
  ReconciliationCalibrationStatus,
  InvestmentAccountingTransactionLabPreview,
  InvestmentAccountingTransactionLabRequest,
  ResolveConflictRequest,
  PreviewJournalTemplateRequest,
  ActivateAccountingConfigurationRequest,
  ResolveReconciliationBreakRequest,
  ReviewReconciliationBreakRequest,
  SecurityIdentityDrillIn,
  SecurityAliasEntry,
  SecurityIdentifierEntry,
  SecurityMasterConflict,
  SecurityMasterEntry,
  SecurityMasterOpenLot,
  SecurityMasterOpenLotReadModel,
  SecurityMasterTrustSnapshot,
  StatementRunSummary,
  TradingParameters
} from "@/types";

export type AccountingWorkstream = "ledger" | "configure" | "reconciliation" | "security-master" | "approvals" | "reporting";
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
  previewTemplate: (request: PreviewJournalTemplateRequest) => Promise<AccountingJournalTemplatePreview>;
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
  templates: AccountingConfigurationTemplateViewModel[];
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

export type CalibrationStatusTone = "success" | "warning" | "danger";
export type CalibrationStatusIcon = "check" | "alert";

export interface CalibrationSummaryMetricViewModel {
  id: string;
  label: string;
  value: number;
  tone: "default" | "warning";
  ariaLabel: string;
}

export interface CalibrationProfileRowViewModel {
  toleranceProfileId: string;
  exceptionRoute: string;
  highestSeverity: string;
  maxToleranceBandLabel: string;
  totalBreakCount: number;
  openBreakCount: number;
  inReviewBreakCount: number;
  resolvedBreakCount: number;
  pendingSignoffCount: number;
  signedOffCount: number;
  lastUpdatedLabel: string;
  ariaLabel: string;
  selectAriaLabel: string;
  detailPanelId: string;
  isSelected: boolean;
}

export interface CalibrationProfileDetailFieldViewModel {
  label: string;
  value: string;
}

export interface CalibrationProfileDetailViewModel {
  id: string;
  title: string;
  subtitle: string;
  description: string;
  statusLabel: string;
  statusTone: "success" | "warning" | "danger";
  ariaLabel: string;
  fields: CalibrationProfileDetailFieldViewModel[];
}

export interface CalibrationSummaryRefreshCommandViewModel {
  label: string;
  ariaLabel: string;
  disabled: boolean;
  disabledReason: string | null;
}

export interface CalibrationSummaryViewState {
  status: ReconciliationCalibrationStatus;
  statusLabel: string;
  statusTone: CalibrationStatusTone;
  statusIcon: CalibrationStatusIcon;
  statusTextClassName: string;
  statusBannerClassName: string;
  summary: string;
  asOfLabel: string;
  totalBreakCount: number;
  openBreakCount: number;
  criticalOpenBreakCount: number;
  pendingSignoffCount: number;
  signedOffCount: number;
  missingMetadataCount: number;
  metricRows: CalibrationSummaryMetricViewModel[];
  profileRows: CalibrationProfileRowViewModel[];
  hasProfiles: boolean;
  profilesLabel: string;
  tableAriaLabel: string;
  emptyText: string;
  detailPanelId: string;
  selectedProfileId: string | null;
  selectedProfile: CalibrationProfileDetailViewModel | null;
  refreshCommand: CalibrationSummaryRefreshCommandViewModel;
  errorText: string | null;
  errorDetails: string[];
  loadingText: string | null;
  statusAnnouncement: string;
}

export interface CalibrationSummaryViewModel extends CalibrationSummaryViewState {
  selectProfile: (profileId: string) => void;
  refresh: () => void;
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
  id: "overview" | "schedules" | "lots" | "controls" | "audit";
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
  title: string;
  detail: string;
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
const CALIBRATION_PROFILE_DETAIL_PANEL_ID = "calibration-profile-detail-panel";

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

const defaultAccountingConfigurationServices: AccountingConfigurationServices = {
  getConfiguration: () => getAccountingConfiguration(),
  previewTemplate: (request) => previewAccountingConfigurationTemplate(request),
  activate: (request) => activateAccountingConfiguration(request)
};

const defaultSecurityMasterDrillInServices: SecurityMasterDrillInServices = {
  getCorporateActions: (securityId) => getCorporateActions(securityId),
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
  return {
    role: "status",
    ariaBusy: true,
    ariaLive: "polite",
    titleId: `${slug}-workspace-loading-title`,
    detailId: `${slug}-workspace-loading-detail`,
    title: `Loading ${workspaceLabel}`,
    detail: workspaceLabel === "Reporting"
      ? "Waiting for report-pack, governed export, and approval summaries from the workstation bootstrap payload."
      : "Waiting for ledger, reconciliation, cash-flow, and Security Master summaries from the workstation bootstrap payload."
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

export function useAccountingConfigurationViewModel(
  services: AccountingConfigurationServices = defaultAccountingConfigurationServices
): AccountingConfigurationViewModel {
  const [workspace, setWorkspace] = useState<AccountingConfigurationWorkspace | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<ApiErrorDisplay | null>(null);
  const [preview, setPreview] = useState<AccountingJournalTemplatePreview | null>(null);
  const [previewBusy, setPreviewBusy] = useState(false);
  const [previewError, setPreviewError] = useState<ApiErrorDisplay | null>(null);
  const [activateBusy, setActivateBusy] = useState(false);
  const [activateError, setActivateError] = useState<ApiErrorDisplay | null>(null);

  const refresh = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const next = await services.getConfiguration();
      setWorkspace(next);
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
        value: String(activeRuleCount),
        detail: "Source events mapped to non-posting previews.",
        tone: activeRuleCount > 0 ? "success" : "warning"
      },
      {
        id: "audit",
        label: "Audit events",
        value: String(workspace?.auditTrail.length ?? 0),
        detail: "Append-only action evidence for configuration changes.",
        tone: (workspace?.auditTrail.length ?? 0) > 0 ? "success" : "warning"
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

    return {
      title: "Configure accounting",
      description: "Set up books, chart accounts, journal templates, posting rules, validation, and audit evidence before accounting actions are activated.",
      statusLabel: workspace ? `${workspace.status} ${workspace.configurationVersion}` : "Not loaded",
      statusTone,
      loading,
      errorText: error?.summary ?? null,
      errorDetails: error?.details ?? [],
      metricRows,
      templates,
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
  }, [activate, activateBusy, activateError, error, loading, preview, previewBusy, previewError, refresh, previewFirstTemplate, workspace]);
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
  const [selectedScheduleEventId, setSelectedScheduleEventId] = useState<string | null>(null);
  const [selectedOpenLotId, setSelectedOpenLotId] = useState<string | null>(null);
  const [trustSnapshot, setTrustSnapshot] = useState<SecurityMasterTrustSnapshot | null>(null);
  const [trustSnapshotLoading, setTrustSnapshotLoading] = useState(false);
  const [trustSnapshotError, setTrustSnapshotError] = useState<ApiErrorDisplay | null>(null);
  const [tradingParameters, setTradingParameters] = useState<TradingParameters | null>(null);
  const [tradingParametersLoading, setTradingParametersLoading] = useState(false);
  const [tradingParametersError, setTradingParametersError] = useState<ApiErrorDisplay | null>(null);
  const searchTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const searchGenerationRef = useRef(0);
  const identityGenerationRef = useRef(0);
  const conflictGenerationRef = useRef(0);
  const conflictResolvingIdRef = useRef<string | null>(null);

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
    setSelectedScheduleEventId(null);
    setSelectedOpenLotId(null);
    setTrustSnapshot(null);
    setTrustSnapshotLoading(false);
    setTrustSnapshotError(null);
    setTradingParameters(null);
    setTradingParametersLoading(false);
    setTradingParametersError(null);
  }, [active]);

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
  }, [active, services]);

  useEffect(() => {
    void refreshConflicts();
  }, [refreshConflicts]);

  useEffect(() => {
    if (!active || !selectedSecurityId) {
      setCorporateActions(null);
      setCorporateActionsLoading(false);
      setCorporateActionsError(null);
      setSelectedCorporateActionId(null);
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
    setTrustSnapshotLoading(true);
    setTrustSnapshotError(null);
    setTradingParametersLoading(true);
    setTradingParametersError(null);

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
  }, [active, selectedSecurityId, drillInServices]);

  const updateQuery = useCallback((nextQuery: string) => {
    setQuery(nextQuery);
    setSelectedSecurityId(null);
    setIdentity(null);
    setIdentityError(null);
    setSearchError(null);
    setSelectedCorporateActionId(null);
    setSelectedScheduleEventId(null);
    setSelectedOpenLotId(null);
    setTrustSnapshot(null);
    setTrustSnapshotLoading(false);
    setTrustSnapshotError(null);
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
  }, [searchDelayMs, services]);

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
    setSelectedScheduleEventId(null);
    setSelectedOpenLotId(null);
    setTrustSnapshot(null);
    setTrustSnapshotLoading(false);
    setTrustSnapshotError(null);
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
  }, [active, services]);

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
  const tradingParametersView = useMemo(
    () => buildTradingParametersViewState(tradingParameters, tradingParametersLoading, tradingParametersError),
    [tradingParameters, tradingParametersLoading, tradingParametersError]
  );
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
    if (workstream !== "reconciliation") {
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
    if (workstream !== "reconciliation") {
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
    if (workstream !== "reconciliation") {
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
      loading: trialBalanceLoading,
      error: trialBalanceError
    }),
    [selectedAccountingBasis, selectedReconciliation?.runId, selectedTrialBalanceRowId, trialBalance, trialBalanceError, trialBalanceLoading]
  );
  const selectAccountingBasis = useCallback((basis: AccountingBasisKind) => {
    setSelectedAccountingBasis(basis);
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
    refreshStatementRuns,
    transactionLabView,
    runTransactionLabPreview,
    trialBalance,
    trialBalanceLoading,
    trialBalanceErrorText: trialBalanceError?.summary ?? null,
    trialBalanceView,
    selectTrialBalanceRow: setSelectedTrialBalanceRowId,
    selectAccountingBasis,
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

  if (pathname.includes("/reconciliation")) {
    return "reconciliation";
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

export function buildReconciliationStatementRunsViewState({
  statementRuns,
  fallbackQueue,
  selectedRunId,
  loading,
  error
}: ReconciliationStatementRunsBuildInput): ReconciliationStatementRunsViewState {
  const detailPanelId = "statement-run-detail-tabs";
  const fallbackRows = statementRuns.length > 0
    ? []
    : fallbackQueue.map((item): StatementRunSummary => ({
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
  const sourceRows = statementRuns.length > 0 ? statementRuns : fallbackRows;
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

function buildStatementRunRow(
  run: StatementRunSummary,
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
  const targetSummary = formatReportPackTargets(reporting?.reportPackTargets ?? []);
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
  loading,
  error
}: {
  runId: string | null;
  rows: LedgerTrialBalanceLine[];
  selectedRowId?: string | null;
  selectedBasis?: AccountingBasisKind | null;
  loading: boolean;
  error: string | ApiErrorDisplay | null;
}): AccountingTrialBalanceViewState {
  const detailPanelId = "trial-balance-account-detail";
  const runLabel = runId ?? "selected run";
  const resolvedBasis = normalizeAccountingBasis(selectedBasis);
  const normalizedRows = rows.map(normalizeTrialBalanceLine);
  const basisOptions = buildTrialBalanceBasisOptions(normalizedRows, resolvedBasis);
  const bridge = buildBasisBridgeViewState(normalizedRows, resolvedBasis, runLabel);
  const rawRows = normalizedRows
    .filter((line) => line.accountingBasis === resolvedBasis)
    .map((line) => buildTrialBalanceRow(line, detailPanelId));
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
    state,
    rows: viewRows,
    hasRows,
    selectedRowId: resolvedSelectedRowId,
    detailPanelId,
    selectedDetail: selectedRow ? buildTrialBalanceDetail(selectedRow, runLabel) : null,
    detailEmptyTitle: "No account selected",
    detailEmptyText: hasRows
      ? "Select an account line to inspect balance evidence for report handoff."
      : "Trial-balance account detail appears after ledger rows load.",
    detailEmptyAriaLabel: "No trial-balance account selected",
    loadingText,
    emptyTitle: "No trial balance lines",
    emptyDetail: `Meridian did not return account-balance rows for ${runLabel}. Select another reconciliation run or refresh ledger evidence before report handoff.`,
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

function buildTrialBalanceDetail(
  line: AccountingTrialBalanceRowViewModel,
  runLabel: string
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
  const firstSourceEventId = sourceEventIds[0] ?? null;
  const firstApprovalId = approvalIds[0] ?? null;

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
      { label: "Source events", value: sourceEventIds.length > 0 ? sourceEventIds.join(", ") : "No source events linked" },
      { label: "Approvals", value: approvalIds.length > 0 ? approvalIds.join(", ") : "No approvals linked" },
      { label: "Run", value: runLabel }
    ],
    auditDrillThroughLabel: firstSourceEventId ? `Open source event ${firstSourceEventId}` : "No source-event drill-through available",
    auditDrillThroughHref: firstSourceEventId ? `/accounting/audit?sourceEventId=${encodeURIComponent(firstSourceEventId)}` : null,
    approvalDrillThroughHref: firstApprovalId ? `/accounting/approvals?approvalId=${encodeURIComponent(firstApprovalId)}` : null
  };
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

function formatReportPackTargets(targets: string[]): string {
  if (targets.length === 0) {
    return "No report-pack targets configured.";
  }

  return `Targets: ${targets.join(", ")}.`;
}

function cashFlowContextLabel(workstream: AccountingWorkstream): string {
  if (workstream === "reporting") {
    return "Reporting packet context";
  }

  if (workstream === "reconciliation") {
    return "Reconciliation context";
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

export function buildCalibrationSummaryViewState(
  summary: ReconciliationCalibrationSummary | null,
  loading: boolean,
  error: string | ApiErrorDisplay | null,
  selectedProfileId: string | null = null
): CalibrationSummaryViewState {
  const normalizedError = normalizeApiErrorDisplay(error);
  const errorText = normalizedError?.summary ?? null;
  const statusTone = calibrationStatusTone(summary?.status ?? null);
  const profiles = summary?.profiles ?? [];
  const effectiveSelectedProfileId = selectedProfileId && profiles.some((profile) => profile.toleranceProfileId === selectedProfileId)
    ? selectedProfileId
    : profiles[0]?.toleranceProfileId ?? null;
  const profileRows = profiles.map((profile) => buildCalibrationProfileRow(profile, effectiveSelectedProfileId));
  const selectedProfileRow = profileRows.find((profile) => profile.toleranceProfileId === effectiveSelectedProfileId) ?? null;
  const metricRows = buildCalibrationSummaryMetrics(summary);

  return {
    status: summary?.status ?? "Ready",
    statusLabel: calibrationStatusLabel(summary?.status ?? null, loading),
    statusTone,
    statusIcon: statusTone === "success" ? "check" : "alert",
    statusTextClassName: calibrationStatusTextClass(statusTone),
    statusBannerClassName: calibrationStatusBannerClass(statusTone),
    summary: summary?.summary ?? "",
    asOfLabel: summary?.asOf ? formatSecurityDate(summary.asOf) : "—",
    totalBreakCount: summary?.totalBreakCount ?? 0,
    openBreakCount: summary?.openBreakCount ?? 0,
    criticalOpenBreakCount: summary?.criticalOpenBreakCount ?? 0,
    pendingSignoffCount: summary?.pendingSignoffCount ?? 0,
    signedOffCount: summary?.signedOffCount ?? 0,
    missingMetadataCount: summary?.missingCalibrationMetadataCount ?? 0,
    metricRows,
    profileRows,
    hasProfiles: profileRows.length > 0,
    profilesLabel: profileRows.length === 1 ? "1 tolerance profile" : `${profileRows.length} tolerance profiles`,
    tableAriaLabel: "Tolerance profile health by reconciliation route",
    emptyText: loading
      ? "Loading tolerance profiles..."
      : errorText
        ? "Tolerance profiles are unavailable until the calibration summary reloads."
        : "No tolerance profiles loaded. Run provider calibration before accepting reconciliation readiness.",
    detailPanelId: CALIBRATION_PROFILE_DETAIL_PANEL_ID,
    selectedProfileId: selectedProfileRow?.toleranceProfileId ?? null,
    selectedProfile: selectedProfileRow ? buildCalibrationProfileDetail(selectedProfileRow) : null,
    refreshCommand: buildCalibrationSummaryRefreshCommand(loading, errorText),
    errorText,
    errorDetails: normalizedError?.details ?? [],
    loadingText: loading ? "Loading calibration summary..." : null,
    statusAnnouncement: errorText
      ? `Calibration summary error: ${errorText}`
      : loading
        ? "Loading calibration summary."
        : summary
          ? `Calibration status: ${calibrationStatusLabel(summary.status, false)}. ${summary.summary}`
          : ""
  };
}

function buildCalibrationSummaryRefreshCommand(
  loading: boolean,
  errorText: string | null
): CalibrationSummaryRefreshCommandViewModel {
  if (loading) {
    return {
      label: "Refreshing...",
      ariaLabel: "Calibration summary refresh is already running",
      disabled: true,
      disabledReason: "Calibration summary refresh is already running."
    };
  }

  return {
    label: errorText ? "Retry calibration summary" : "Refresh calibration",
    ariaLabel: errorText ? "Retry calibration summary load" : "Refresh calibration summary",
    disabled: false,
    disabledReason: null
  };
}

function buildCalibrationSummaryMetrics(
  summary: ReconciliationCalibrationSummary | null
): CalibrationSummaryMetricViewModel[] {
  const totalBreakCount = summary?.totalBreakCount ?? 0;
  const openBreakCount = summary?.openBreakCount ?? 0;
  const criticalOpenBreakCount = summary?.criticalOpenBreakCount ?? 0;
  const pendingSignoffCount = summary?.pendingSignoffCount ?? 0;
  const signedOffCount = summary?.signedOffCount ?? 0;
  const missingMetadataCount = summary?.missingCalibrationMetadataCount ?? 0;

  return [
    buildCalibrationSummaryMetric("total", "Total breaks", totalBreakCount, false),
    buildCalibrationSummaryMetric("open", "Open", openBreakCount, openBreakCount > 0),
    buildCalibrationSummaryMetric("critical-open", "Critical open", criticalOpenBreakCount, criticalOpenBreakCount > 0),
    buildCalibrationSummaryMetric("pending-signoff", "Pending sign-off", pendingSignoffCount, pendingSignoffCount > 0),
    buildCalibrationSummaryMetric("signed-off", "Signed off", signedOffCount, false),
    buildCalibrationSummaryMetric("missing-metadata", "Missing metadata", missingMetadataCount, missingMetadataCount > 0)
  ];
}

function buildCalibrationSummaryMetric(
  id: string,
  label: string,
  value: number,
  warn: boolean
): CalibrationSummaryMetricViewModel {
  return {
    id,
    label,
    value,
    tone: warn ? "warning" : "default",
    ariaLabel: `${label}: ${value}`
  };
}

function buildCalibrationProfileRow(
  profile: ReconciliationCalibrationSummary["profiles"][number],
  selectedProfileId: string | null
): CalibrationProfileRowViewModel {
  const isSelected = profile.toleranceProfileId === selectedProfileId;
  const statusLabel = calibrationProfileStatusLabel(profile);

  return {
    toleranceProfileId: profile.toleranceProfileId,
    exceptionRoute: profile.exceptionRoute,
    highestSeverity: profile.highestSeverity,
    maxToleranceBandLabel: profile.maxToleranceBand === null ? "Policy default" : formatCurrency(profile.maxToleranceBand),
    totalBreakCount: profile.totalBreakCount,
    openBreakCount: profile.openBreakCount,
    inReviewBreakCount: profile.inReviewBreakCount,
    resolvedBreakCount: profile.resolvedBreakCount,
    pendingSignoffCount: profile.pendingSignoffCount,
    signedOffCount: profile.signedOffCount,
    lastUpdatedLabel: formatSecurityDate(profile.lastUpdatedAt),
    ariaLabel: `Profile ${profile.toleranceProfileId}: ${profile.openBreakCount} open, ${profile.pendingSignoffCount} pending sign-off, severity ${profile.highestSeverity}`,
    selectAriaLabel: `Inspect tolerance profile ${profile.toleranceProfileId}: ${statusLabel}`,
    detailPanelId: CALIBRATION_PROFILE_DETAIL_PANEL_ID,
    isSelected
  };
}

function buildCalibrationProfileDetail(
  profile: CalibrationProfileRowViewModel
): CalibrationProfileDetailViewModel {
  const statusLabel = calibrationProfileStatusLabel(profile);
  const statusTone = calibrationProfileStatusTone(profile);

  return {
    id: `${CALIBRATION_PROFILE_DETAIL_PANEL_ID}-${toDomId(profile.toleranceProfileId)}`,
    title: `Selected tolerance profile - ${profile.toleranceProfileId}`,
    subtitle: `${profile.exceptionRoute} route - ${profile.highestSeverity} severity`,
    description: `${statusLabel}. ${formatCount(profile.totalBreakCount, "break")} tracked for this exception route, with ${formatCount(profile.openBreakCount, "open break")} and ${formatCount(profile.pendingSignoffCount, "pending sign-off")}.`,
    statusLabel,
    statusTone,
    ariaLabel: `Tolerance profile detail for ${profile.toleranceProfileId}`,
    fields: [
      { label: "Tolerance band", value: profile.maxToleranceBandLabel },
      { label: "Total breaks", value: String(profile.totalBreakCount) },
      { label: "Open", value: String(profile.openBreakCount) },
      { label: "In review", value: String(profile.inReviewBreakCount) },
      { label: "Resolved", value: String(profile.resolvedBreakCount) },
      { label: "Pending sign-off", value: String(profile.pendingSignoffCount) },
      { label: "Signed off", value: String(profile.signedOffCount) },
      { label: "Last updated", value: profile.lastUpdatedLabel }
    ]
  };
}

function calibrationProfileStatusLabel(
  profile: Pick<CalibrationProfileRowViewModel, "highestSeverity" | "openBreakCount" | "pendingSignoffCount">
): string {
  if (profile.highestSeverity.toLowerCase() === "critical" || profile.openBreakCount > 0) {
    return "Operator review required";
  }

  if (profile.pendingSignoffCount > 0) {
    return "Pending sign-off";
  }

  return "Within tolerance";
}

function calibrationProfileStatusTone(
  profile: Pick<CalibrationProfileRowViewModel, "highestSeverity" | "openBreakCount" | "pendingSignoffCount">
): CalibrationProfileDetailViewModel["statusTone"] {
  if (profile.highestSeverity.toLowerCase() === "critical") {
    return "danger";
  }

  if (profile.openBreakCount > 0 || profile.pendingSignoffCount > 0) {
    return "warning";
  }

  return "success";
}

function calibrationStatusTone(status: ReconciliationCalibrationStatus | null): CalibrationStatusTone {
  if (status === "Ready") {
    return "success";
  }

  if (status === "Blocked") {
    return "danger";
  }

  return "warning";
}

function calibrationStatusTextClass(tone: CalibrationStatusTone): string {
  if (tone === "success") {
    return "text-success";
  }

  if (tone === "danger") {
    return "text-danger";
  }

  return "text-warning";
}

function calibrationStatusBannerClass(tone: CalibrationStatusTone): string {
  if (tone === "success") {
    return "border-success/30 bg-success/5";
  }

  if (tone === "danger") {
    return "border-danger/30 bg-danger/5";
  }

  return "border-warning/30 bg-warning/5";
}

function calibrationStatusLabel(status: ReconciliationCalibrationStatus | null, loading: boolean): string {
  if (loading) {
    return "Loading...";
  }

  if (status === "Ready") {
    return "Ready";
  }

  if (status === "Blocked") {
    return "Blocked";
  }

  if (status === "ReviewRequired") {
    return "Review required";
  }

  return "Unknown";
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
