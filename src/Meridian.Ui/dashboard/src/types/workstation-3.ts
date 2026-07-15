import type {
  AccountingCashFlowSummary,
  AccountingCertificationState,
  AccountingConfigurationValidationIssue,
  AccountingSystemEvidencePackageStatus,
  AccountingSystemImportState,
  AccountingSystemProviderState,
  AccountingSystemReconciliationStatus,
  BrokerageAccountKind,
  BookPosition,
  DimensionMappingProfile,
  ExternalGlExportReconciliationSafeguardState,
  InstrumentRole,
  LedgerDimensionSet,
  ManualJournalEntryStatus,
  MetricSnapshot,
  OperationsActionOrigin,
  OperationsChecklistControlApproval,
  OperationsTransitionResult,
  OperatorWorkItem,
  PositionEconomicState,
  ProjectionLineage,
  ProviderConnectionRow,
  StrategyRunRecord,
  TradingAcceptanceGate,
  TradingAcceptanceGateStatus,
  TradingControlReadiness,
  TradingExecutionReconciliationReadiness,
  TradingLiveOperationRequirement,
  TradingPaperSessionReadiness,
  TradingPromotionReadiness,
  TradingReplayReadiness,
  TradingTrustGateReadiness,
  WorkstationBrokerageSyncStatus,
  WorkstationSecurityReference,
} from "../types";

export interface ExternalGlMappingProfile {
  profileId: string;
  providerId: string;
  displayName: string;
  updatedAtUtc: string;
  dimensionMappings: DimensionMappingProfile[];
  accountMappings: Record<string, string>;
  certificationState: AccountingCertificationState;
}

export interface AccountingSystemMappingProfileUpsertRequest {
  profile: ExternalGlMappingProfile;
  actor: string;
  providerId?: string | null;
  fundProfileId?: string | null;
  ledgerBookId?: string | null;
  correlationId?: string | null;
  evidenceLinks?: string[];
  tenantId?: string | null;
  companyId?: string | null;
  actionOrigin?: string | null;
}

export interface AccountingSystemExportPackageRequest {
  actor: string;
  providerId?: string | null;
  fundProfileId?: string | null;
  ledgerBookId?: string | null;
  periodStart?: string | null;
  periodEnd?: string | null;
  mappingProfileId?: string | null;
  journalEntryIds?: string[];
  requireBalancedReconciliation?: boolean;
  evidenceLinks?: string[];
  correlationId?: string | null;
}

export interface CertifyAccountingSystemExportPackageRequest {
  exportPackageId: string;
  actor: string;
  notes: string;
  evidenceLinks?: string[];
  correlationId?: string | null;
}

  export interface ExternalGlExportCertification {
    certificationId: string;
    state: AccountingCertificationState;
    actor: string;
    recordedAtUtc: string;
    summary: string;
    evidenceLinks: string[];
  }

  export interface ExternalGlExportLine {
    exportLineId: string;
    reconciliationRowId: string;
    sourceStatus: AccountingSystemReconciliationStatus;
    meridianAccountCode: string;
    externalAccountId: string;
    accountName: string;
    currency: string;
    debit: number;
    credit: number;
    netAmount: number;
    meridianDimensions?: LedgerDimensionSet | null;
    externalDimensions?: LedgerDimensionSet | null;
    evidenceLinks: string[];
  }

  export interface ExternalGlExportPackage {
    exportPackageId: string;
    providerId: string;
    fundProfileId: string;
    ledgerBookId: string | null;
    periodStart: string;
    periodEnd: string;
  createdAtUtc: string;
  createdBy: string;
  postingEnabled: boolean;
  postingDisabledReason: string;
  journalEntryIds: string[];
    evidenceLinks: string[];
    certification?: ExternalGlExportCertification | null;
    validationIssues: AccountingConfigurationValidationIssue[];
    generatedLines?: ExternalGlExportLine[];
    mappingProfileId?: string | null;
    reconciliationId?: string | null;
    requireBalancedReconciliation?: boolean;
    reconciliationSafeguardState?: ExternalGlExportReconciliationSafeguardState;
    reconciliationSafeguardIssueCodes?: string[];
  }

  export interface ExternalGlExportPackageManifest {
    exportPackageId: string;
    providerId: string;
    fundProfileId: string;
    ledgerBookId: string | null;
    periodStart: string;
    periodEnd: string;
    certificationState: AccountingCertificationState;
    generatedAtUtc: string;
    contentHash: string;
    contentType: string;
    fileName: string;
    externalPostingAllowed: boolean;
    postingDisabledReason: string;
    payload: string;
    generatedLines: ExternalGlExportLine[];
    evidenceLinks: string[];
    validationIssues: AccountingConfigurationValidationIssue[];
    mappingProfileId?: string | null;
    reconciliationId?: string | null;
    requireBalancedReconciliation?: boolean;
    reconciliationSafeguardState?: ExternalGlExportReconciliationSafeguardState;
    reconciliationSafeguardIssueCodes?: string[];
  }

export type CloseTaskStatus = "NotStarted" | "WaitingOnDependency" | "InProgress" | "ReadyForSignOff" | "SignedOff" | "Blocked";

export interface CloseDependency {
  dependencyId: string;
  dependsOnTaskId: string;
  reason: string;
}

export interface CloseSignOff {
  signOffId: string;
  role: string;
  actor: string | null;
  approvalState: ManualJournalEntryStatus;
  signedAtUtc: string | null;
  evidenceLinks: string[];
  notes?: string | null;
}

export interface CloseSignOffRequirement {
  requirementId: string;
  role: string;
  requiredApprovalCount: number;
  approvedCount: number;
  isSatisfied: boolean;
  evidenceRequirement: string;
}

export interface MaterialityPolicy {
  policyId: string;
  amountThreshold: number;
  percentThreshold: number;
  currency: string;
  reviewRole: string;
  requiresLateAdjustmentApproval: boolean;
}

export interface CloseTask {
  taskId: string;
  displayName: string;
  status: CloseTaskStatus;
  owner: string;
  dueDate: string;
  dependencies: CloseDependency[];
  signOffs: CloseSignOff[];
  evidenceLinks: string[];
  blockerReason?: string | null;
  signOffRequirements?: CloseSignOffRequirement[] | null;
}

export interface CloseCalendarMilestone {
  milestoneId: string;
  taskId: string;
  displayName: string;
  owner: string;
  dueDate: string;
  status: CloseTaskStatus;
  isBlocked: boolean;
  isSatisfied: boolean;
  isPeriodLocked: boolean;
  dependencyCount: number;
  requiredSignOffCount: number;
  approvedSignOffCount: number;
  evidenceLinks: string[];
  blockerReason?: string | null;
}

export interface LateAdjustmentRequest {
  requestId: string;
  journalEntryId: string;
  requestedBy: string;
  requestedAtUtc: string;
  amount: number;
  currency: string;
  reason: string;
  approvalState: ManualJournalEntryStatus;
  materialityPolicy: MaterialityPolicy;
  evidenceLinks: string[];
  decidedBy?: string | null;
  decidedAtUtc?: string | null;
  decisionNotes?: string | null;
}

export interface CreateLateAdjustmentRequest {
  workflowId: string;
  journalEntryId: string;
  amount: number;
  currency: string;
  reason: string;
  requestedBy: string;
  evidenceLinks?: string[];
  correlationId?: string | null;
}

export interface ReviewLateAdjustmentRequest {
  workflowId: string;
  requestId: string;
  decision: ManualJournalEntryStatus;
  actor: string;
  notes: string;
  evidenceLinks?: string[];
  correlationId?: string | null;
}

export interface SignOffCloseTaskRequest {
  workflowId: string;
  taskId: string;
  role: string;
  decision: ManualJournalEntryStatus;
  actor: string;
  notes: string;
  evidenceLinks?: string[];
  correlationId?: string | null;
}

export interface CloseEvidenceReview {
  reviewId: string;
  issueCode: string;
  targetId?: string | null;
  reviewedBy: string;
  reviewedAtUtc: string;
  notes: string;
  evidenceLinks: string[];
}

export interface ReviewCloseEvidenceRequest {
  workflowId: string;
  issueCode: string;
  targetId?: string | null;
  actor: string;
  notes: string;
  evidenceLinks?: string[];
  correlationId?: string | null;
  actionOrigin?: OperationsActionOrigin | null;
}

export interface CloseTaskConfiguration {
  taskId: string;
  displayName?: string | null;
  owner?: string | null;
  dueDate?: string | null;
  requiredApprovalCount?: number | null;
  requiredApprovalRole?: string | null;
  requiredEvidence?: string | null;
  dependsOnTaskIds?: string[] | null;
  dependencyConfigurations?: CloseTaskDependencyConfiguration[] | null;
  signOffRequirementConfigurations?: CloseTaskSignOffRequirementConfiguration[] | null;
}

export interface CloseTaskDependencyConfiguration {
  dependsOnTaskId: string;
  reason?: string | null;
}

export interface CloseTaskSignOffRequirementConfiguration {
  role: string;
  requiredApprovalCount: number;
  evidenceRequirement?: string | null;
}

export interface ClosePeriodPlanConfiguration {
  workflowId: string;
  materialityPolicy: MaterialityPolicy;
  taskConfigurations?: CloseTaskConfiguration[] | null;
  configuredBy?: string | null;
  configuredAtUtc?: string | null;
  evidenceLinks?: string[] | null;
}

export interface UpsertClosePeriodPlanConfigurationRequest {
  workflowId: string;
  materialityPolicy?: MaterialityPolicy | null;
  taskConfigurations?: CloseTaskConfiguration[] | null;
  actor?: string | null;
  evidenceLinks?: string[] | null;
  correlationId?: string | null;
  actionOrigin?: OperationsActionOrigin | null;
  expectedConfiguredAtUtc?: string | null;
}

export interface LockClosePeriodRequest {
  workflowId: string;
  expectedWorkflowVersion: number;
  actor: string;
  rationale: string;
  reportPackId: string;
  evidenceLinks?: string[] | null;
  checklistControlApprovals?: OperationsChecklistControlApproval[] | null;
  correlationId?: string | null;
  closePackageId?: string | null;
  closePackageManifestId?: string | null;
  closePackageRetainedManifestRoute?: string | null;
  actionOrigin?: OperationsActionOrigin | null;
}

export interface ClosePeriodLockResult {
  isLocked: boolean;
  plan?: ClosePeriodPlan | null;
  transition?: OperationsTransitionResult | null;
  issues: AccountingConfigurationValidationIssue[];
}

export interface CloseOperatingCoverageItem {
  controlId: string;
  label: string;
  state: AccountingReadinessState;
  evidenceCount: number;
  blockingIssueCount: number;
  requiredAction: string;
  evidenceLinks?: string[] | null;
  blockingIssues?: AccountingConfigurationValidationIssue[] | null;
}

export interface ClosePeriodPlan {
  closePlanId: string;
  fundProfileId: string;
  ledgerBookId: string | null;
  periodId: string;
  periodStart: string;
  periodEnd: string;
  closeDueDate: string;
  isPeriodLocked: boolean;
  tasks: CloseTask[];
  lateAdjustments: LateAdjustmentRequest[];
  materialityPolicy: MaterialityPolicy;
  validationIssues: AccountingConfigurationValidationIssue[];
  closeCalendar?: CloseCalendarMilestone[] | null;
  configuration?: ClosePeriodPlanConfiguration | null;
  evidenceReviews?: CloseEvidenceReview[] | null;
  operatingCoverage?: CloseOperatingCoverageItem[] | null;
}

export interface ReportCertification {
  certificationId: string;
  state: AccountingCertificationState;
  actor: string;
  recordedAtUtc: string;
  summary: string;
  evidenceLinks: string[];
}

  export interface RestatementWorkflow {
    restatementId: string;
    priorPackageId: string;
    reasonCode: string;
  approvalState: ManualJournalEntryStatus;
  requestedBy: string;
  requestedAtUtc: string;
    evidenceLinks: string[];
  }

  export interface ReportLineProvenance {
    statementId: string;
    lineId: string;
    lineLabel: string;
    sourceKind: string;
    amount: number;
    currency: string;
    dimensions: LedgerDimensionSet;
    evidenceLinks: string[];
  }

  export interface ReportExportArtifact {
    artifactId: string;
    artifactKind: string;
    displayName: string;
    format: string;
    route: string;
    certificationState: AccountingCertificationState;
    generatedAtUtc: string;
    contentHash: string;
    evidenceLinks: string[];
    sourceStatementId?: string | null;
    dimensionScope?: ReportDimensionScope | null;
  }

  export interface ReportExportArtifactManifest {
    packageId: string;
    artifactId: string;
    artifactKind: string;
    displayName: string;
    format: string;
    route: string;
    certificationState: AccountingCertificationState;
    generatedAtUtc: string;
    contentHash: string;
    contentType: string;
    fileName: string;
    externalPostingAllowed: boolean;
    payload: string;
    evidenceLinks: string[];
    sourceStatementId?: string | null;
    dimensionScope?: ReportDimensionScope | null;
  }

  export interface ReportDimensionScope {
    ledgerBookId?: string | null;
    dimensions: LedgerDimensionSet;
    hasExplicitScope: boolean;
    scopeHash: string;
    certificationEvidenceToken: string;
    scopedDimensionKeys: string[];
  }

  export interface FinancialStatementPackage {
    packageId: string;
    fundProfileId: string;
    ledgerBookId: string | null;
  periodId: string;
  certificationState: AccountingCertificationState;
  statementIds: string[];
    evidenceLinks: string[];
    certification?: ReportCertification | null;
    restatement?: RestatementWorkflow | null;
    lineProvenance?: ReportLineProvenance[];
  }

export interface InvestorCapitalStatement {
  statementId: string;
  fundProfileId: string;
  capitalAccountId: string;
  investorId: string | null;
  periodId: string;
  beginningCapital: number;
  contributions: number;
  distributions: number;
  realizedGainLoss: number;
  endingCapital: number;
  currency: string;
  certificationState: AccountingCertificationState;
  evidenceLinks: string[];
}

export interface RealizedGainLossReport {
  reportId: string;
  fundProfileId: string;
  ledgerBookId: string | null;
  periodId: string;
  dimensions: LedgerDimensionSet;
  realizedGainLoss: number;
  currency: string;
  certificationState: AccountingCertificationState;
  evidenceLinks: string[];
}

export interface NavPackage {
  packageId: string;
  fundProfileId: string;
  ledgerBookId: string | null;
  periodId: string;
  nav: number;
  currency: string;
  certificationState: AccountingCertificationState;
  evidenceLinks: string[];
  certification?: ReportCertification | null;
  restatement?: RestatementWorkflow | null;
}

export interface AccountingReportPackageRequest {
  fundProfileId: string;
  periodId: string;
  actor: string;
  ledgerBookId?: string | null;
  closeWorkflowId?: string | null;
  capitalAccountId?: string | null;
  investorId?: string | null;
  beginningCapital?: number;
  contributions?: number;
  distributions?: number;
  realizedGainLoss?: number;
  nav?: number;
  currency?: string;
  restatementReasonCode?: string | null;
  priorPackageId?: string | null;
  evidenceLinks?: string[];
  correlationId?: string | null;
}

export interface CertifyAccountingReportPackageRequest {
  packageId: string;
  actor: string;
  notes: string;
  evidenceLinks?: string[];
  correlationId?: string | null;
}

export type AccountingReadinessState = "NotStarted" | "NeedsAttention" | "Blocked" | "ReadyForReview" | "Certified";

export interface AccountingCloseReadinessItem {
  itemId: string;
  category: string;
  label: string;
  state: AccountingReadinessState;
  summary: string;
  requiredAction: string;
  blockingIssueCount: number;
  evidenceLinks: string[];
  blockingIssues: AccountingConfigurationValidationIssue[];
  ledgerBookId?: string | null;
  dimensions?: LedgerDimensionSet | null;
}

  export interface AccountingReportPackageBundle {
  financialStatements: FinancialStatementPackage;
  investorCapitalStatements: InvestorCapitalStatement[];
  realizedGainLoss: RealizedGainLossReport;
  navPackage: NavPackage;
  certification: ReportCertification;
  validationIssues: AccountingConfigurationValidationIssue[];
  exportArtifacts?: ReportExportArtifact[] | null;
  closeWorkflowId?: string | null;
  closeReadinessItems?: AccountingCloseReadinessItem[] | null;
  dimensionScope?: ReportDimensionScope | null;
}

export interface AccountingSystemProvider {
  providerId: string;
  displayName: string;
  state: AccountingSystemProviderState;
  requiresCredentials: boolean;
  supportsChartOfAccounts: boolean;
  supportsJournalEntries: boolean;
  supportsTrialBalance: boolean;
  supportsPosting: boolean;
  statusLabel: string;
  statusDetail: string;
  evidenceKinds: string[];
  connection?: AccountingSystemConnectionMetadata | null;
  mappingRequirements: AccountingSystemProviderMappingRequirement[];
}

export interface AccountingSystemProviderMappingRequirement {
  requirementId: string;
  label: string;
  requiredEvidenceKind: string;
  requiredAction: string;
  requiredForGuardedExport: boolean;
}

export interface AccountingSystemConnectionMetadata {
  providerId: string;
  environment: string | null;
  companyId: string | null;
  companyName: string | null;
  hasLocalConfig: boolean;
  hasRefreshToken: boolean;
  lastConnectedAtUtc: string | null;
  statusLabel: string;
  statusDetail: string;
  missingFields: string[];
}

export interface AccountingSystemImportRequest {
  providerId?: string | null;
  fundProfileId?: string | null;
  ledgerBookId?: string | null;
  periodStart?: string | null;
  periodEnd?: string | null;
  persistPreview?: boolean;
}

export interface AccountingSystemImportSummary {
  importId: string;
  providerId: string;
  providerDisplayName: string;
  fundProfileId: string;
  ledgerBookId: string | null;
  state: AccountingSystemImportState;
  periodStart: string;
  periodEnd: string;
  importedAtUtc: string;
  chartAccountCount: number;
  journalEntryCount: number;
  trialBalanceLineCount: number;
  evidenceReferences: string[];
  warnings: string[];
}

export interface AccountingSystemChartAccount {
  externalAccountId: string;
  accountCode: string;
  displayName: string;
  accountType: string;
  currency: string;
  isActive: boolean;
  parentExternalAccountId: string | null;
  evidenceRef: string | null;
}

export interface AccountingSystemJournalLine {
  externalLineId: string;
  externalAccountId: string;
  accountCode: string;
  description: string;
  debit: number;
  credit: number;
  currency: string;
  evidenceRef: string | null;
}

export interface AccountingSystemJournalEntry {
  externalJournalEntryId: string;
  accountingDate: string;
  description: string;
  currency: string;
  totalDebits: number;
  totalCredits: number;
  lines: AccountingSystemJournalLine[];
  evidenceRef: string | null;
}

export interface AccountingSystemTrialBalanceLine {
  externalAccountId: string;
  accountCode: string;
  accountName: string;
  accountType: string;
  debit: number;
  credit: number;
  currency: string;
  asOfDate: string;
  evidenceRef: string | null;
}

export interface AccountingSystemImportDetail {
  summary: AccountingSystemImportSummary;
  chartAccounts: AccountingSystemChartAccount[];
  journalEntries: AccountingSystemJournalEntry[];
  trialBalance: AccountingSystemTrialBalanceLine[];
}

export interface AccountingSystemReconciliationRow {
  rowId: string;
  accountCode: string;
  accountName: string;
  currency: string;
  status: AccountingSystemReconciliationStatus;
  externalDebit: number;
  externalCredit: number;
  meridianDebit: number;
  meridianCredit: number;
  variance: number;
  detail: string;
  evidenceRef: string | null;
  externalEvidenceReferences?: string[];
  meridianEvidenceReferences?: string[];
  evidenceReferences?: string[];
}

export interface AccountingSystemReconciliationEvidencePackage {
  packageId: string;
  label: string;
  status: AccountingSystemEvidencePackageStatus;
  evidenceReferenceCount: number;
  evidenceReferences: string[];
  requiredActions: string[];
}

export interface AccountingSystemReconciliationSummary {
  reconciliationId: string;
  importId: string;
  providerId: string;
  fundProfileId: string;
  periodStart: string;
  periodEnd: string;
  generatedAtUtc: string;
  matchedCount: number;
  breakCount: number;
  totalExternalDebits: number;
  totalExternalCredits: number;
  totalMeridianDebits: number;
  totalMeridianCredits: number;
  postingEnabled: boolean;
  postingDisabledReason: string;
  rows: AccountingSystemReconciliationRow[];
  evidenceReferences: string[];
  evidencePackages?: AccountingSystemReconciliationEvidencePackage[];
}

export interface BrokerageHouseholdAccount {
  fundAccountId: string;
  providerId: string;
  externalAccountId: string;
  displayName: string;
  accountKind: BrokerageAccountKind;
  health: WorkstationBrokerageSyncStatus["health"];
  cash: number;
  equity: number;
  buyingPower: number;
  currency: string;
  syncedAt: string;
  positionCount: number;
  cashTransactionCount: number;
  warnings: string[];
}

export interface BrokerageHouseholdPosition {
  fundAccountId: string;
  providerId: string;
  externalAccountId: string;
  accountKind: BrokerageAccountKind;
  symbol: string;
  quantity: number;
  averageEntryPrice: number;
  marketPrice: number;
  marketValue: number;
  unrealizedPnl: number;
  assetClass: string;
  security: WorkstationSecurityReference | null;
  description: string | null;
  positionId: string | null;
  currency: string | null;
}

export interface BrokerageHouseholdPortfolio {
  providerId: string;
  asOf: string;
  totalCash: number;
  totalEquity: number;
  totalBuyingPower: number;
  currency: string;
  accounts: BrokerageHouseholdAccount[];
  positions: BrokerageHouseholdPosition[];
  warnings: string[];
}

export interface BrokerageCashFlowEntry {
  transactionId: string;
  transactionType: string;
  category: string;
  amount: number;
  currency: string;
  postedAt: string;
  symbol: string | null;
  description: string | null;
}

export interface BrokerageCashFlowSummary {
  fundAccountId: string;
  providerId: string | null;
  externalAccountId: string | null;
  accountKind: BrokerageAccountKind;
  from: string | null;
  to: string | null;
  totalInflows: number;
  totalOutflows: number;
  netCashFlow: number;
  currency: string;
  transactionCount: number;
  entries: BrokerageCashFlowEntry[];
  warnings: string[];
}

export interface TradingOperatorReadiness {
  asOf: string;
  overallStatus: TradingAcceptanceGateStatus;
  readyForPaperOperation: boolean;
  readyForLiveOperation?: boolean;
  liveOperationBlockers?: string[];
  liveOperationRequirements?: TradingLiveOperationRequirement[];
  acceptanceGates: TradingAcceptanceGate[];
  activeSession: TradingPaperSessionReadiness | null;
  sessions: TradingPaperSessionReadiness[];
  replay: TradingReplayReadiness | null;
  controls: TradingControlReadiness;
  promotion: TradingPromotionReadiness | null;
  trustGate: TradingTrustGateReadiness;
  brokerageSync: WorkstationBrokerageSyncStatus | null;
  executionReconciliation?: TradingExecutionReconciliationReadiness | null;
  workItems: OperatorWorkItem[];
  warnings: string[];
}

export interface CreateExecutionManualOverrideRequest {
  kind: string;
  reason: string;
  createdBy?: string | null;
  symbol?: string | null;
  strategyId?: string | null;
  runId?: string | null;
  expiresAt?: string | null;
}

export interface ReplayFileRecord {
  path: string;
  name: string;
  symbol: string | null;
  eventType: string | null;
  sizeBytes: number;
  isCompressed: boolean;
  lastModified: string;
}

export interface ReplayStatus {
  sessionId: string;
  filePath: string;
  status: string;
  speedMultiplier: number;
  eventsProcessed: number;
  totalEvents: number;
  progressPercent: number;
  startedAt: string;
}

export interface OrderSubmitRequest {
  symbol: string;
  side: "Buy" | "Sell";
  type: "Market" | "Limit" | "Stop";
  quantity: number;
  limitPrice?: number | null;
  fundAccountId?: string | null;
}

export interface OrderResult {
  success: boolean;
  orderId: string | null;
  reason: string | null;
}

export interface StrategyWorkspaceResponse {
  metrics: MetricSnapshot[];
  runs: StrategyRunRecord[];
  plotTool?: StrategyPlotToolPayload | null;
}

export interface StrategyRunDrillInLinks {
  equityCurve: string;
  fills: string;
  attribution: string;
  ledger: string | null;
  cashFlows: string;
  continuity: string | null;
}

export interface StrategyBriefingRun {
  runId: string;
  strategyName: string;
  mode: number;
  status: number;
  dataset: string;
  windowLabel: string;
  returnLabel: string;
  sharpeLabel: string;
  lastUpdatedLabel: string;
  notes: string;
  promotionState: number | null;
  netPnl: number | null;
  totalReturn: number | null;
  finalEquity: number | null;
  drillIn: StrategyRunDrillInLinks;
}

export interface StrategySavedComparisonMode {
  runId: string;
  mode: number;
  status: number;
  netPnl: number | null;
  totalReturn: number | null;
  drillIn: StrategyRunDrillInLinks;
}

export interface StrategySavedComparison {
  comparisonId: string;
  strategyName: string;
  modeSummary: string;
  summary: string;
  anchorRunId: string | null;
  modes: StrategySavedComparisonMode[];
}

export interface StrategyBriefingAlert {
  alertId: string;
  title: string;
  summary: string;
  tone: string;
  runId: string | null;
  actionLabel: string | null;
}

export interface StrategyWhatChangedItem {
  changeId: string;
  title: string;
  summary: string;
  category: string;
  timestamp: string;
  relativeTime: string;
  runId: string | null;
}

export interface StrategyBriefingWorkspaceSummary {
  totalRuns: number;
  activeRuns: number;
  promotionCandidates: number;
  positivePnlRuns: number;
  latestRunId: string | null;
  latestStrategyName: string | null;
  hasLedgerCoverage: boolean;
  hasPortfolioCoverage: boolean;
  summary: string;
}

export interface StrategyBriefingResponse {
  workspace: StrategyBriefingWorkspaceSummary;
  insightFeed: {
    feedId: string;
    title: string;
    summary: string;
    generatedAt: string;
    widgets: Array<{
      widgetId: string;
      title: string;
      subtitle: string;
      headline: string;
      tone: string;
      summary: string;
      runId: string | null;
      drillInRoute: string | null;
    }>;
  };
  watchlists: Array<{
    watchlistId: string;
    name: string;
    symbols: string[];
    symbolCount: number;
    isPinned: boolean;
    sortOrder: number;
    accentColor: string | null;
    summary: string | null;
  }>;
  recentRuns: StrategyBriefingRun[];
  savedComparisons: StrategySavedComparison[];
  alerts: StrategyBriefingAlert[];
  whatChanged: StrategyWhatChangedItem[];
}

export interface StrategyPlotToolTabPayload {
  id: string;
  label: string;
  tabId: string;
  panelId: string;
  selected: boolean;
  buttonVariant: "secondary" | "ghost";
  tabIndex: number;
  ariaLabel: string;
}

export interface StrategyPlotToolPayload {
  workspace: unknown;
  statistics: unknown;
  studies: unknown[];
  tabs: StrategyPlotToolTabPayload[];
  activeView?: "workspace" | "statistics";
}

export interface DataProviderRecord {
  providerId?: string;
  displayName?: string;
  provider: string;
  status: "Healthy" | "Warning" | "Degraded" | "Blocked";
  capability: string;
  latency: string;
  note: string;
  trustScore?: string;
  signalSource?: string;
  reasonCode?: string;
  recommendedAction?: string;
  gateImpact?: string;
  connectionSummary?: ProviderConnectionRow | null;
  routingSummary?: DataProviderRoutingSummary | null;
  diagnostics?: DataProviderDiagnosticSummary[] | null;
}

export interface DataProviderRoutingSummary {
  connectionId: string | null;
  providerFamilyId: string | null;
  productionReady: boolean | null;
  certificationFresh: boolean | null;
  bindingCount: number;
  fallbackRouteCount: number;
  healthStatus: string | null;
}

export interface DataProviderDiagnosticSummary {
  id: string;
  label: string;
  status: "pass" | "warning" | "fail" | "pending";
  statusLabel: string;
  detail: string;
}

export interface DataBackfillRecord {
  jobId: string;
  scope: string;
  provider: string;
  status: "Queued" | "Running" | "Review";
  progress: string;
  updatedAt: string;
}

export interface DataExportRecord {
  exportId: string;
  profile: string;
  target: string;
  status: "Ready" | "Running" | "Attention";
  rows: string;
  updatedAt: string;
}

export interface DataUploadTemplateField {
  name: string;
  label: string;
  required: boolean;
  example: string;
  description: string;
}

export interface DataUploadTemplate {
  templateId: string;
  label: string;
  description: string;
  dataDomain: string;
  targetWorkflow: string;
  fileName: string;
  contentType: string;
  headerLine: string;
  fields: DataUploadTemplateField[];
  sampleRows: string[];
  validationNotes: string[];
  sourceKinds?: string[] | null;
  setupChecklist?: string[] | null;
  mappingGuidance?: string[] | null;
}

export interface DataUploadTemplateCatalog {
  templates: DataUploadTemplate[];
  acceptedFileExtensions: string[];
  maxPreviewRows: number;
  maxFileBytes: number;
  workbookFileName?: string | null;
  workbookAcceptedFileExtensions?: string[] | null;
  workbookMaxFileBytes?: number;
}

export interface DataUploadValidationIssue {
  severity: "Error" | "Warning" | string;
  field: string;
  message: string;
  rowNumber: number | null;
  sheetName?: string | null;
  cellReference?: string | null;
}

export interface DataUploadWorkbookSheetPreview {
  sheetName: string;
  templateId: string | null;
  templateLabel: string | null;
  dataDomain: string | null;
  parsedRowCount: number;
  previewRowCount: number;
  headers: string[];
  previewRows: Record<string, string>[];
  issues: DataUploadValidationIssue[];
  status: "ReadyForReview" | "NeedsRepair" | "Empty" | string;
}

export interface DataUploadWorkbookPreviewResult {
  uploadId: string;
  fileName: string;
  fileSizeBytes: number;
  contentType: string;
  uploadedBy: string;
  uploadedAtUtc: string;
  retainedPath: string;
  sheetCount: number;
  totalParsedRowCount: number;
  sheets: DataUploadWorkbookSheetPreview[];
  crossSheetIssues: DataUploadValidationIssue[];
  status: "ReadyForReview" | "NeedsSchemaRepair" | string;
  nextAction: string;
}

export interface DataUploadPreviewResult {
  uploadId: string;
  templateId: string;
  templateLabel: string;
  fileName: string;
  fileSizeBytes: number;
  contentType: string;
  uploadedBy: string;
  uploadedAtUtc: string;
  retainedPath: string;
  parsedRowCount: number;
  previewRowCount: number;
  headers: string[];
  previewRows: Record<string, string>[];
  issues: DataUploadValidationIssue[];
  status: "ReadyForReview" | "NeedsSchemaRepair" | string;
  nextAction: string;
}

export interface DataQueryRequest {
  sql: string;
}

export interface DataQueryResult {
  success: boolean;
  error: string | null;
  columns: string[];
  columnTypes: string[];
  rows: (string | null)[][];
  rowCount: number;
  truncated: boolean;
  elapsedMs: number;
}

export interface DataWorkspaceResponse {
  metrics: MetricSnapshot[];
  providers: DataProviderRecord[];
  backfills: DataBackfillRecord[];
  exports: DataExportRecord[];
  uploadTemplates?: DataUploadTemplateCatalog | null;
}

export type DataOperationsProviderRecord = DataProviderRecord;
export type DataOperationsProviderRoutingSummary = DataProviderRoutingSummary;
export type DataOperationsProviderDiagnosticSummary = DataProviderDiagnosticSummary;
export type DataOperationsBackfillRecord = DataBackfillRecord;
export type DataOperationsExportRecord = DataExportRecord;
export type DataOperationsWorkspaceResponse = DataWorkspaceResponse;

export interface TradingPosition {
  positionKey?: string;
  symbol: string;
  side: "Long" | "Short";
  quantity: string;
  averagePrice: string;
  markPrice: string;
  dayPnl: string;
  unrealizedPnl: string;
  exposure: string;
}

export interface TradingOrder {
  orderId: string;
  symbol: string;
  side: "Buy" | "Sell";
  type: "Market" | "Limit" | "Stop";
  quantity: string;
  limitPrice: string;
  status: "Working" | "Partially Filled" | "Pending Routing";
  submittedAt: string;
}

export interface TradingFill {
  fillId: string;
  orderId: string;
  symbol: string;
  side: "Buy" | "Sell";
  quantity: string;
  price: string;
  venue: string;
  timestamp: string;
}

export interface TradingRiskState {
  state: "Healthy" | "Observe" | "Constrained";
  summary: string;
  netExposure: string;
  grossExposure: string;
  var95: string;
  maxDrawdown: string;
  buyingPowerUsed: string;
  activeGuardrails: string[];
}

export interface BrokerageWiringStatus {
  provider: string;
  account: string;
  environment: "paper" | "live";
  connection: "Connected" | "Degraded" | "Disconnected";
  lastHeartbeat: string;
  orderIngress: string;
  fillFeed: string;
  notes: string;
}

export interface TradingWorkspaceResponse {
  metrics: MetricSnapshot[];
  positions: TradingPosition[];
  openOrders: TradingOrder[];
  fills: TradingFill[];
  risk: TradingRiskState;
  brokerage: BrokerageWiringStatus;
  readiness?: TradingOperatorReadiness | null;
}

export interface PortfolioRunRow {
  runId: string;
  strategyName: string;
  engine: string;
  mode: string;
  status: string;
  pnl: string;
  sharpe: string;
  dataset: string;
  window: string;
  lastUpdated: string;
  notes: string;
  promotionState: string | null;
}

export interface PortfolioWorkspaceResponse {
  metrics: MetricSnapshot[];
  positions: TradingPosition[];
  risk: TradingRiskState;
  brokerage: BrokerageWiringStatus;
  runs: PortfolioRunRow[];
  cashFlow: AccountingCashFlowSummary | null;
}

export interface MultiAssetEvidenceRequirement {
  requirementId: string;
  label: string;
  category: string;
  status: "Ready" | "ReviewRequired" | "Blocked" | string;
  evidenceRoute: string;
  required: boolean;
}

export interface MultiAssetReadinessBlocker {
  code: string;
  severity: "Review" | "Blocker" | string;
  message: string;
  source: string;
  evidenceRoute: string | null;
}

export interface MultiAssetDrillThroughTarget {
  targetId: string;
  targetType: string;
  label: string;
  route: string;
  evidenceLink: string | null;
  status: "Ready" | "ReviewRequired" | "Blocked" | string;
  source: string;
}

export interface MultiAssetClassCoverage {
  assetClass: string;
  displayName: string;
  status: "Ready" | "ReviewRequired" | "Blocked" | string;
  statusLabel: string;
  summary: string;
  evidenceRequirements: MultiAssetEvidenceRequirement[];
  blockers: MultiAssetReadinessBlocker[];
  drillThroughTargets?: MultiAssetDrillThroughTarget[];
  ledgerClassification: Record<string, string>;
  reconciliationSignals: Record<string, string>;
}

export interface MultiAssetCoverageSummary {
  fundAccountId: string;
  entity: string;
  asOfUtc: string;
  metrics: MetricSnapshot[];
  assetClasses: MultiAssetClassCoverage[];
  drillThroughRoutes: Record<string, string>;
}

export interface AssetOperationSubject {
  securityId: string;
  assetClass: string;
  displayName: string;
  primaryIdentifier: string | null;
  operationalProfile: string[];
}

export interface AssetTermsVersion {
  termsVersionId: string;
  securityId: string;
  versionNumber: number;
  termsHash: string;
  effectiveDate: string;
  recordedAt: string;
  sourceDomain: string;
  sourceEntityId: string;
  summary: string;
}

export interface AssetLifecycleEvent {
  lifecycleEventId: string;
  securityId: string;
  eventType: string;
  eventStatus: string;
  effectiveDate: string;
  recordedAt: string;
  sourceDomain: string;
  sourceEntityId: string;
  notes: string | null;
}

export interface AssetCashFlowProjectionRun {
  projectionRunId: string;
  securityId: string;
  projectionAsOf: string;
  engineVersion: string;
  status: string;
  generatedAt: string;
  sourceDomain: string;
  sourceEntityId: string;
}

export interface AssetProjectedCashFlow {
  projectedCashFlowId: string;
  projectionRunId: string;
  securityId: string;
  sequenceNumber: number;
  flowType: string;
  dueDate: string;
  amount: number;
  currency: string;
  status: string;
  sourceDomain: string;
  sourceEntityId: string;
}

export interface AssetActualActivity {
  activityId: string;
  securityId: string;
  activityType: string;
  effectiveDate: string;
  settlementDate: string | null;
  amount: number;
  currency: string;
  status: string;
  sourceDomain: string;
  sourceEntityId: string;
  evidenceReference: string | null;
}

export interface AssetReconciliationRun {
  reconciliationRunId: string;
  securityId: string;
  projectionRunId: string | null;
  status: string;
  requestedAt: string;
  completedAt: string | null;
  sourceDomain: string;
  sourceEntityId: string;
}

export interface AssetReconciliationResult {
  reconciliationResultId: string;
  reconciliationRunId: string;
  securityId: string;
  matchStatus: string;
  expectedAmount: number | null;
  actualAmount: number | null;
  varianceAmount: number | null;
  expectedDate: string | null;
  actualDate: string | null;
  notes: string[];
  sourceDomain: string;
  sourceEntityId: string;
}

export interface AssetLedgerProjection {
  ledgerProjectionId: string;
  securityId: string;
  projectionType: string;
  accountingDate: string;
  ledgerBasis: string;
  status: string;
  debitAmount: number;
  creditAmount: number;
  currency: string;
  sourceDomain: string;
  sourceEntityId: string;
  ledgerReference: string | null;
}

export interface AssetOperationsReadiness {
  securityId: string;
  status: string;
  capabilities: string[];
  readyCapabilities: string[];
  missingCapabilities: string[];
  blockers: string[];
  evaluatedAt: string;
  sourceDomain: string;
  sourceEntityId: string;
}

export interface AssetOperationsDetail {
  subject: AssetOperationSubject;
  termsHistory: AssetTermsVersion[];
  lifecycleEvents: AssetLifecycleEvent[];
  cashFlowProjectionRuns: AssetCashFlowProjectionRun[];
  projectedCashFlows: AssetProjectedCashFlow[];
  actualActivity: AssetActualActivity[];
  reconciliationRuns: AssetReconciliationRun[];
  reconciliationResults: AssetReconciliationResult[];
  ledgerProjections: AssetLedgerProjection[];
  readiness: AssetOperationsReadiness;
  workflowAudit: AssetLifecycleEvent[];
  instrumentRoles?: InstrumentRole[];
  bookPositions?: BookPosition[];
  positionEconomicStates?: PositionEconomicState[];
  projectionLineages?: ProjectionLineage[];
}

export type AssetOperationsProjection = AssetOperationsDetail;


export interface StatementRunSummary {
  runId: string;
  importId: string;
  startedAtUtc: string;
  completedAtUtc: string;
  positionMatches: number;
  cashMatches: number;
  transactionMatches: number;
  openExceptionCount: number;
  brokerCustodian?: string | null;
  account?: string | null;
  period?: string | null;
  status?: string | null;
  validationIssueCount?: number | null;
  matchCount?: number | null;
  breakCount?: number | null;
  caseCount?: number | null;
  importedAtUtc?: string | null;
}

export interface StatementRunException {
  breakId: string;
  runId: string;
  importId: string;
  sourceReference: string;
  breakCode: string;
  category: string;
  delta: number;
  tolerance: number;
  toleranceBreached: boolean;
  createdAtUtc: string;
  status: string;
}

export interface StatementConnectorDescriptor {
  connectorId: string;
  displayName: string;
  fileExtensions: string[];
  supportsFileImport: boolean;
  supportsRemoteFetch: boolean;
  requiresMappingProfile: boolean;
  defaultProfileId: string | null;
}

export interface StatementMappingProfileField {
  canonicalField: string;
  sourceColumn: string;
  aliases: string[] | null;
  required: boolean;
}

export interface StatementMappingProfileActivityCode {
  sourceCode: string;
  canonicalActivityType: string;
}

export interface StatementMappingProfileCsvOptions {
  delimiter: string;
  quote: string;
  hasHeader: boolean;
}

export interface StatementMappingProfile {
  schemaVersion: number;
  profileId: string;
  displayName: string;
  format: string;
  csv: StatementMappingProfileCsvOptions | null;
  culture: string | null;
  dateFormats: string[] | null;
  fields: StatementMappingProfileField[];
  activityCodes: StatementMappingProfileActivityCode[];
  lastAcceptedFingerprint: string | null;
  isBuiltIn: boolean;
  notes: string | null;
}

export type StatementColumnConfidence = "Exact" | "Alias" | "Fuzzy" | "Unmapped";

export interface StatementColumnMapping {
  sourceColumn: string;
  canonicalField: string | null;
  confidence: StatementColumnConfidence;
  score: number;
  rationale: string;
}

export interface StatementImportIssue {
  code: string;
  severity: "Error" | "Warning" | "Info" | string;
  rowNumber: number | null;
  field: string | null;
  message: string;
}

export interface StatementRecordPreview {
  kind: string;
  account: string;
  symbol: string;
  quantity: number;
  price: number;
  cashAmount: number;
  activityType: string;
  tradeDate: string;
  settlementDate: string | null;
  currency: string | null;
  feesCommission: number | null;
  externalTransactionId: string | null;
}
