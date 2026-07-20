import type {
  AccountingBookContext,
  AccountingRulePackReference,
  EconomicEventReference,
  JournalEntryLifecycleTransition,
  LedgerDimensionSet,
  OperationsActionOrigin,
  ProjectionLineage,
  ReconciliationBreakExplanation,
  ReconciliationBreakDisposition,
  ReconciliationBreakMeasure,
  ReconciliationBreakQueueStatus,
  ReconciliationCaseCommentVisibility,
  ReconciliationCaseLifecycleState,
  ReconciliationCasePriority,
  ReconciliationCaseSlaState,
  ReconciliationCaseworkAction,
} from "../types";

export interface ReconciliationBreakQueueItem {
  breakId: string;
  runId: string;
  strategyName: string;
  category: string;
  status: ReconciliationBreakQueueStatus;
  variance: number;
  reason: string;
  assignedTo: string | null;
  detectedAt: string;
  lastUpdatedAt: string;
  reviewedBy: string | null;
  reviewedAt: string | null;
  resolvedBy: string | null;
  resolvedAt: string | null;
  resolutionNote: string | null;
  exceptionRoute?: string | null;
  toleranceProfileId?: string | null;
  toleranceBand?: number | null;
  requiredSignoffRole?: string | null;
  signoffStatus?: string | null;
  fundAccountId?: string | null;
  explainabilitySummary?: string | null;
  routingTarget?: string | null;
  routingDetail?: string | null;
  recommendedAction?: string | null;
  assigneeId?: string | null;
  assigneeDisplayName?: string | null;
  priority?: ReconciliationCasePriority;
  slaPolicyId?: string | null;
  slaDueAt?: string | null;
  slaWarningAt?: string | null;
  slaBreachedAt?: string | null;
  slaState?: ReconciliationCaseSlaState;
  ageBand?: string | null;
  businessAgeHours?: number;
  rootCauseCode?: string | null;
  resolutionCode?: string | null;
  signedOffBy?: string | null;
  signedOffAt?: string | null;
  signOffNote?: string | null;
  reopenedBy?: string | null;
  reopenedAt?: string | null;
  reopenReason?: string | null;
  version?: number;
  comments?: ReconciliationCaseComment[] | null;
  evidenceLinks?: string[] | null;
  commentCount?: number;
  evidenceCount?: number;
  lastActivityAt?: string | null;
  sourceType?: string | null;
  sourceSystem?: string | null;
  sourceReference?: string | null;
  sourceImportId?: string | null;
  sourceBreakId?: string | null;
  sourceFingerprint?: string | null;
  lastCommentExcerpt?: string | null;
  relatedCaseCount?: number;
  slaBadgeLabel?: string | null;
  slaBadgeTone?: "info" | "warning" | "danger" | "neutral" | "success" | string | null;
  breakExplanation?: ReconciliationBreakExplanation | null;
  measures?: ReconciliationBreakMeasure[] | null;
  disposition?: ReconciliationBreakDisposition | null;
  dispositionReason?: string | null;
  supersedingBreakId?: string | null;
  dispositionApprovedBy?: string | null;
  dispositionApprovalReference?: string | null;
  dispositionEvidenceHash?: string | null;
  disposedAt?: string | null;
  blockedOutputs?: string[] | null;
  accountingPeriodId?: string | null;
  asOfDate?: string | null;
}

export interface ReconciliationCaseComment {
  commentId: string;
  parentCommentId?: string | null;
  authorId: string;
  authorDisplayName: string;
  visibility: ReconciliationCaseCommentVisibility;
  body: string;
  evidenceLinks: string[];
  createdAt: string;
  editedAt?: string | null;
  deletedAt?: string | null;
  deletedBy?: string | null;
  mentions?: string[] | null;
  linkedEvidenceIds?: string[] | null;
  previousTextHash?: string | null;
  editReason?: string | null;
  deleteReason?: string | null;
  statusTransition?: ReconciliationCaseLifecycleState | null;
}

export interface ReconciliationCaseworkCommand {
  breakId: string;
  action: ReconciliationCaseworkAction;
  actor: string;
  commandId: string;
  correlationId: string;
  source: string;
  expectedVersion: number;
  reason?: string | null;
  assignee?: string | null;
  priority?: ReconciliationCasePriority | null;
  status?: ReconciliationCaseLifecycleState | null;
  note?: string | null;
  rootCauseCode?: string | null;
  resolutionCode?: string | null;
  causationId?: string | null;
  commentId?: string | null;
  parentCommentId?: string | null;
  visibility?: ReconciliationCaseCommentVisibility;
  evidenceLinks?: string[] | null;
  privileged?: boolean;
  statusTransition?: ReconciliationCaseLifecycleState | null;
  mentions?: string[] | null;
  actionOrigin?: OperationsActionOrigin;
  approvalActor?: string | null;
  approvalReference?: string | null;
  supersedingBreakId?: string | null;
}

export interface ReconciliationCaseworkOperationResult {
  transitionStatus: string;
  item?: ReconciliationBreakQueueItem | null;
  outcome: VerifiedOperationOutcome;
  error?: string | null;
  errorCode?: string | null;
  validationIssues?: string[] | null;
}

export interface ReconciliationBulkCaseworkRequest {
  breakIds: string[];
  action: ReconciliationCaseworkAction;
  actor: string;
  commandId: string;
  correlationId: string;
  source: string;
  idempotencyKey: string;
  dryRun: boolean;
  allowPartialSuccess: boolean;
  reason?: string | null;
  assignee?: string | null;
  priority?: ReconciliationCasePriority | null;
  status?: ReconciliationCaseLifecycleState | null;
  note?: string | null;
  rootCauseCode?: string | null;
  resolutionCode?: string | null;
  maxCaseCount?: number;
  actionOrigin?: OperationsActionOrigin;
  evidenceLinks?: string[] | null;
  approvalActor?: string | null;
  approvalReference?: string | null;
  supersedingBreakId?: string | null;
}

export interface ReconciliationBulkCaseworkResult {
  bulkActionId: string;
  idempotencyKey: string;
  dryRun: boolean;
  requestedCount: number;
  succeededCount: number;
  failedCount: number;
  results: Array<{ breakId: string; succeeded: boolean; wouldSucceed: boolean; error?: string | null; item?: ReconciliationBreakQueueItem | null }>;
  inputHashSha256: string;
  outcome: VerifiedOperationOutcome;
}

export type OperationTerminalState = "Succeeded" | "CompletedWithWarnings" | "Failed" | "Blocked";
export type OperationPostconditionState = "Satisfied" | "NotSatisfied" | "NotEvaluated";

export interface VerifiedOperationOutcome {
  operationId: string;
  operationKind: string;
  state: OperationTerminalState;
  startedAtUtc: string;
  completedAtUtc: string;
  attemptNumber: number;
  correlationId: string;
  inputHashSha256: string;
  postconditions: Array<{
    code: string;
    description: string;
    state: OperationPostconditionState;
    required: boolean;
    evidenceIds: string[];
    artifactIds: string[];
  }>;
  evidence: Array<{
    evidenceId: string;
    kind: string;
    description: string;
    uri?: string | null;
    contentHashSha256?: string | null;
    capturedAtUtc?: string | null;
  }>;
  artifacts: Array<{
    artifactId: string;
    fileName: string;
    contentType: string;
    byteLength: number;
    contentHashSha256: string;
    uri?: string | null;
    previewUri?: string | null;
  }>;
  issues: Array<{
    code: string;
    message: string;
    severity: "Warning" | "Error";
    exceptionType?: string | null;
    evidenceId?: string | null;
    artifactId?: string | null;
    isBlocking: boolean;
  }>;
  recovery: Array<{
    actionId: string;
    label: string;
    guidance: string;
    retryable: boolean;
    requiresHumanAction: boolean;
    route?: string | null;
    evidenceIds: string[];
    artifactIds: string[];
  }>;
  schemaVersion: string;
  isSuccessful: boolean;
}

export interface ReviewReconciliationBreakRequest {
  breakId: string;
  assignedTo: string;
  reviewedBy: string;
  reviewNote?: string;
}

export interface ResolveReconciliationBreakRequest {
  breakId: string;
  status: "Resolved" | "Dismissed";
  resolvedBy: string;
  resolutionNote: string;
  operatorRationale: string;
}

// --- Trading action result ---

export interface TradingActionResult {
  actionId: string;
  status: "Accepted" | "Completed" | "Rejected" | "Failed";
  message: string;
  occurredAt: string;
  auditId?: string | null;
}

// --- Multi-run comparison types ---

export interface RunComparisonRow {
  runId: string;
  strategyName: string;
  mode: string;
  engine: string;
  status: string;
  netPnl: number | null;
  totalReturn: number | null;
  finalEquity: number | null;
  maxDrawdown: number | null;
  sharpeRatio: number | null;
  fillCount: number;
  lastUpdatedAt: string;
  promotionState: string;
  hasLedger: boolean;
  hasAuditTrail: boolean;
  continuityStatus?: string;
  reconciliationBreakCount?: number;
  reconciliationHighestSeverity?: string;
  hasLedgerEntryCoverage?: boolean;
  ledgerCoverageStatus?: string;
  cashFlowHealth?: string;
  compatibilityWarnings?: string[] | null;
  artifactCompleteness?: StrategyRunArtifactCompleteness | null;
}

export interface StrategyRunArtifactCompleteness {
  hasPortfolio: boolean;
  hasLedger: boolean;
  hasCashFlow: boolean;
  hasFills: boolean;
  hasAuditTrail: boolean;
}

// --- Run diff types ---

export interface PositionDiffEntry {
  symbol: string;
  baseQuantity: number;
  targetQuantity: number;
  basePnl: number;
  targetPnl: number;
  changeType: "Added" | "Removed" | "Modified";
}

export interface ParameterDiff {
  key: string;
  baseValue: string | null;
  targetValue: string | null;
}

export interface MetricsDiff {
  netPnlDelta: number;
  totalReturnDelta: number;
  fillCountDelta: number;
  baseNetPnl: number | null;
  targetNetPnl: number | null;
  baseTotalReturn: number | null;
  targetTotalReturn: number | null;
  finalEquityDelta?: number | null;
  maxDrawdownDelta?: number | null;
  sharpeRatioDelta?: number | null;
  baseFinalEquity?: number | null;
  targetFinalEquity?: number | null;
  baseMaxDrawdown?: number | null;
  targetMaxDrawdown?: number | null;
  baseSharpeRatio?: number | null;
  targetSharpeRatio?: number | null;
}

export interface RunDiff {
  baseRunId: string;
  targetRunId: string;
  baseStrategyName: string;
  targetStrategyName: string;
  addedPositions: PositionDiffEntry[];
  removedPositions: PositionDiffEntry[];
  modifiedPositions: PositionDiffEntry[];
  parameterChanges: ParameterDiff[];
  metrics: MetricsDiff;
  compatibilityWarnings?: string[] | null;
  baseArtifactCompleteness?: StrategyRunArtifactCompleteness | null;
  targetArtifactCompleteness?: StrategyRunArtifactCompleteness | null;
  baseMode?: string | null;
  targetMode?: string | null;
  baseEngine?: string | null;
  targetEngine?: string | null;
  baseStrategyId?: string | null;
  targetStrategyId?: string | null;
  baseStrategyVersion?: string | null;
  targetStrategyVersion?: string | null;
  lineageRelation?: string;
  compatibilityLevel?: string;
}

// --- Security reference ---

export interface WorkstationSecurityReference {
  securityId: string;
  displayName: string;
  assetClass: string;
  currency: string;
  status: "Active" | "Inactive" | "Pending";
  primaryIdentifier: string | null;
  subType: string | null;
}

// --- Portfolio types ---

export interface NetSymbolPosition {
  symbol: string;
  netQuantity: number;
  grossQuantity: number;
}

export interface PortfolioPositionSummary {
  symbol: string;
  quantity: number;
  averageCostBasis: number;
  realizedPnl: number;
  unrealizedPnl: number;
  isShort: boolean;
  security: WorkstationSecurityReference | null;
}

export interface PortfolioSummary {
  portfolioId: string;
  runId: string;
  asOf: string;
  cash: number;
  longMarketValue: number;
  shortMarketValue: number;
  grossExposure: number;
  netExposure: number;
  totalEquity: number;
  realizedPnl: number;
  unrealizedPnl: number;
  commissions: number;
  financing: number;
  positions: PortfolioPositionSummary[];
  securityResolvedCount: number;
  securityMissingCount: number;
}

// --- Ledger types ---

export type AccountingBasisKind = "Primary" | "Gaap" | "Cash" | "Tax" | "Statutory";

export type AccountingConfigurationStatus = "Draft" | "Active" | "Archived";
export type AccountingConfigurationValidationSeverity = "Info" | "Warning" | "Critical";
export type AccountingProductionReadinessStatus = "Ready" | "ReviewRequired" | "Blocked" | "Unavailable";
export type AccountingProductionReadinessArea =
  | "LedgerBooks"
  | "RulesStudio"
  | "PostingRules"
  | "JournalLifecycle"
  | "DimensionalAccounting"
  | "ExternalGl"
  | "CloseReporting"
  | "TenantAdministration"
  | "MigrationRollout";
export type LedgerBookRolloutIssueSeverity = "Info" | "Warning" | "Critical";
export type AccountingTemplateLineSide = "Debit" | "Credit";
export type ManualJournalEntryStatus = "Draft" | "NeedsFix" | "Submitted" | "Approved" | "Rejected" | "Posted" | "Reversed" | "Rebooked" | "CloseLocked";
export type AccountingRuleConditionOperator = "Equals" | "NotEquals" | "Contains" | "AmountGreaterThanOrEqual" | "AmountLessThanOrEqual" | "AmountBetween" | "IsPresent";
export type AccountingRuleConditionGroupOperator = "All" | "Any";
export type AccountingRuleFormulaKind = "FixedAmount" | "SourceAmount" | "PercentageOfSourceAmount" | "AllocationResidual";
export type AllocationRuleBasis = "FixedPercent" | "InvestorCommitment" | "CapitalAccountBalance" | "StrategyWeight" | "CustomFormula";
export type JournalEntryLifecycleAction = "Validate" | "Submit" | "Approve" | "Reject" | "Post" | "Reverse" | "Rebook" | "LockAfterClose";
export type PrivateCapitalFundEventLedgerReadiness =
  | "Blocked"
  | "EvidenceMissing"
  | "ApprovalPending"
  | "PostingReview"
  | "ReportReview"
  | "Ready"
  | "Published";
export type PaymentIntentCashDirection = "Neutral" | "Inflow" | "Outflow";
export type PaymentIntentWorkflowStatus =
  | "EvidenceMissing"
  | "ApprovalPending"
  | "BankEvidencePending"
  | "BankReturned"
  | "ReconciliationPending"
  | "ExecutionDeferred"
  | "Blocked";
export type PrivateCapitalPaymentIntentEvidenceStatus =
  | "MissingIntent"
  | "CashEvidenceMissing"
  | "IntentCaptured"
  | "SettlementMatched";
export type ManualJournalEntryType =
  | "General"
  | "AccruedBalance"
  | "AccruedExpense"
  | "PrepaidExpense"
  | "Expense"
  | "Amortization"
  | "Deferral"
  | "Reclassification"
  | "Reversal"
  | "CapitalCall"
  | "Distribution"
  | "Subscription"
  | "Redemption"
  | "LpTransfer"
  | "ManagementFee"
  | "ClosingEntry";

export interface LedgerBook {
  ledgerBookId: string;
  fundProfileId: string;
  fundStructureNodeId: string;
  fundStructureNodeKind: string;
  displayName: string;
  baseCurrency: string;
  createdAt: string;
  updatedAt: string;
  description?: string | null;
  accountingBasis: AccountingBasisKind;
  accountingPolicyId: string;
  accountingPolicyVersion: string;
}

export interface CreateLedgerBookRequest {
  fundProfileId: string;
  fundStructureNodeId: string;
  fundStructureNodeKind: string;
  displayName: string;
  baseCurrency: string;
  description?: string | null;
  accountingBasis?: AccountingBasisKind;
  accountingPolicyId?: string;
  accountingPolicyVersion?: string;
}

export interface LedgerBookSetupCandidate {
  fundProfileId: string;
  fundStructureNodeId: string;
  fundStructureNodeKind: string;
  displayName: string;
  baseCurrency: string;
  accountingBasis: AccountingBasisKind;
  accountingPolicyId: string;
  accountingPolicyVersion: string;
  suggestedAction: string;
  description?: string | null;
  sourceLedgerBookId?: string | null;
  requestedLedgerBookId?: string | null;
}

export interface LedgerBookRequiredScope {
  fundStructureNodeId: string;
  fundStructureNodeKind: string;
  accountingBasis: AccountingBasisKind;
  displayName?: string | null;
}

export interface LedgerBookRolloutBookStatus {
  ledgerBookId: string;
  fundProfileId: string;
  fundStructureNodeId: string;
  fundStructureNodeKind: string;
  accountingBasis: AccountingBasisKind;
  accountingPolicyId: string;
  accountingPolicyVersion: string;
  periodCount: number;
  openPeriodCount: number;
  softClosedPeriodCount: number;
  hardClosedPeriodCount: number;
  firstPeriodStart?: string | null;
  lastPeriodEnd?: string | null;
}

export interface LedgerBookRolloutIssue {
  code: string;
  severity: LedgerBookRolloutIssueSeverity;
  message: string;
  scope?: string | null;
  ledgerBookId?: string | null;
  fundStructureNodeId?: string | null;
  accountingBasis?: AccountingBasisKind | null;
}

export interface LedgerBookRolloutAssessment {
  generatedAtUtc: string;
  fundProfileId?: string | null;
  fundStructureNodeId?: string | null;
  fundStructureNodeKind?: string | null;
  accountingBasis?: AccountingBasisKind | null;
  books: LedgerBookRolloutBookStatus[];
  issues: LedgerBookRolloutIssue[];
  isReady: boolean;
  criticalIssueCount: number;
  warningIssueCount: number;
  bookCount: number;
  openPeriodCount: number;
}

export interface AccountingProductionReadinessRequest {
  fundProfileId?: string | null;
  ledgerBookId?: string | null;
  accountingBasis?: AccountingBasisKind | null;
  providerId?: string | null;
  tenantId?: string | null;
  companyId?: string | null;
  requiredLedgerBookScopes?: LedgerBookRequiredScope[] | null;
  tenantScopeConfigured?: boolean;
  adminRoleProfileConfigured?: boolean;
  scopedAccessPoliciesConfigured?: boolean;
  reportingGroupsConfigured?: boolean;
  accountingAdminSurfaceConfigured?: boolean;
  browserAccountingAdminSurfaceConfigured?: boolean;
  wpfAccountingAdminSurfaceConfigured?: boolean;
  chartAdministrationStudioConfigured?: boolean;
  ruleTestPromotionStudioConfigured?: boolean;
  closeSetupStudioConfigured?: boolean;
  providerMappingStudioConfigured?: boolean;
  tenantCompanyReportGroupSetupStudioConfigured?: boolean;
  auditReviewToolingConfigured?: boolean;
  bulkImportExportSafeguardsConfigured?: boolean;
  performanceValidationConfigured?: boolean;
  disasterRecoveryRunbookConfigured?: boolean;
  ledgerBookAdministrationStudioConfigured?: boolean;
  postingRuleAuthoringStudioConfigured?: boolean;
  approvalQueueStudioConfigured?: boolean;
  dimensionMappingStudioConfigured?: boolean;
  implementationSandboxConfigured?: boolean;
  tenantAdministrationEvidenceLinks?: string[] | null;
  postingRulesLedgerBookNativeCertified?: boolean;
  journalLifecycleLedgerBookNativeCertified?: boolean;
  closeReportingLedgerBookNativeCertified?: boolean;
  closePlanConfigurationLedgerBookNativeCertified?: boolean;
  externalGlLedgerBookNativeCertified?: boolean;
  reconciliationLedgerBookNativeCertified?: boolean;
  directLendingLedgerBookNativeCertified?: boolean;
  strategyLedgerReadLedgerBookNativeCertified?: boolean;
  ledgerBookWorkflowEvidenceLinks?: string[] | null;
  periodReportDimensionQueriesCertified?: boolean;
  crossPeriodReportDimensionQueriesCertified?: boolean;
  journalQueryDimensionFiltersCertified?: boolean;
  externalExportDimensionMappingCertified?: boolean;
  ledgerLineDimensionsPersistedCertified?: boolean;
  trialBalanceDimensionFiltersCertified?: boolean;
  reportPackageDimensionProvenanceCertified?: boolean;
  dimensionalReportingEvidenceLinks?: string[] | null;
  ledgerBookMigrationCertified?: boolean;
  historicalJournalBackfillCertified?: boolean;
  dimensionalBackfillCertified?: boolean;
  accountingConfigurationPromotionCertified?: boolean;
  closeReportingEvidenceMigrationCertified?: boolean;
  migrationEvidenceLinks?: string[] | null;
  migrationRunArtifacts?: AccountingMigrationRunArtifact[] | null;
}

export type AccountingMigrationRunKind =
  | "LedgerBookScope"
  | "HistoricalJournalBackfill"
  | "DimensionalBackfill"
  | "AccountingConfigurationPromotion"
  | "CloseReportingEvidence";

export type AccountingMigrationRunStatus = "Planned" | "Running" | "Completed" | "Failed" | "Certified";

export interface AccountingMigrationRunArtifact {
  runId: string;
  kind: AccountingMigrationRunKind;
  status: AccountingMigrationRunStatus;
  startedAtUtc: string;
  completedAtUtc?: string | null;
  actor?: string | null;
  migratedRecordCount: number;
  issueCount: number;
  evidenceReferences: string[];
  fundProfileId?: string | null;
  ledgerBookId?: string | null;
  summary?: string | null;
  dimensions?: LedgerDimensionSet | null;
}

export interface AccountingMigrationRolloutPlanItem {
  kind: AccountingMigrationRunKind;
  code: string;
  label: string;
  certified: boolean;
  status: AccountingProductionReadinessStatus;
  scopeLabel: string;
  requiredAction: string;
  latestRunId?: string | null;
  latestRunStatus?: AccountingMigrationRunStatus | null;
  migratedRecordCount: number;
  issueCount: number;
  evidenceReferences: string[];
  blockingIssueCodes: string[];
}

export interface AccountingMigrationRunArtifactList {
  fundProfileId?: string | null;
  ledgerBookId?: string | null;
  artifacts: AccountingMigrationRunArtifact[];
}

export interface AccountingMigrationRunWorkerPlan {
  planId: string;
  kind: AccountingMigrationRunKind;
  fundProfileId: string;
  ledgerBookId: string;
  sourceRecordCount: number;
  migratedRecordCount: number;
  dimensions?: LedgerDimensionSet | null;
  evidenceReferences: string[];
  tenantId?: string | null;
  companyId?: string | null;
  summary?: string | null;
}

export interface AccountingMigrationRunWorkerPlanList {
  fundProfileId?: string | null;
  ledgerBookId?: string | null;
  kind?: AccountingMigrationRunKind | null;
  plans: AccountingMigrationRunWorkerPlan[];
  tenantId?: string | null;
  companyId?: string | null;
}

export interface AccountingProductionReadinessIssue {
  code: string;
  area: AccountingProductionReadinessArea;
  severity: AccountingConfigurationValidationSeverity;
  message: string;
  suggestedAction: string;
  evidenceReferences: string[];
}

export interface AccountingProductionReadinessComponent {
  area: AccountingProductionReadinessArea;
  label: string;
  status: AccountingProductionReadinessStatus;
  score: number;
  summary: string;
  issues: AccountingProductionReadinessIssue[];
  evidenceReferences: string[];
  route?: string | null;
}

export interface AccountingProductionGap {
  code: string;
  label: string;
  status: AccountingProductionReadinessStatus;
  highestSeverity: AccountingConfigurationValidationSeverity;
  summary: string;
  requiredAction: string;
  areas: AccountingProductionReadinessArea[];
  blockingIssueCodes: string[];
  routes: string[];
  issues?: AccountingProductionReadinessIssue[];
}

export interface AccountingTenantAdministrationReadiness {
  tenantId?: string | null;
  companyId?: string | null;
  tenantScopeConfigured: boolean;
  adminRoleProfileConfigured: boolean;
  scopedAccessPoliciesConfigured: boolean;
  reportingGroupsConfigured: boolean;
  accountingAdminSurfaceConfigured: boolean;
  browserAccountingAdminSurfaceConfigured: boolean;
  wpfAccountingAdminSurfaceConfigured: boolean;
  chartAdministrationStudioConfigured?: boolean;
  ruleTestPromotionStudioConfigured?: boolean;
  closeSetupStudioConfigured?: boolean;
  providerMappingStudioConfigured?: boolean;
  tenantCompanyReportGroupSetupStudioConfigured?: boolean;
  auditReviewToolingConfigured?: boolean;
  bulkImportExportSafeguardsConfigured?: boolean;
  performanceValidationConfigured?: boolean;
  disasterRecoveryRunbookConfigured?: boolean;
  ledgerBookAdministrationStudioConfigured?: boolean;
  postingRuleAuthoringStudioConfigured?: boolean;
  approvalQueueStudioConfigured?: boolean;
  dimensionMappingStudioConfigured?: boolean;
  implementationSandboxConfigured?: boolean;
  evidenceReferences: string[];
  completedControlCount: number;
  requiredControlCount: number;
  hasTenantScope: boolean;
  hasCompanyScope: boolean;
  hasRetainedEvidence: boolean;
}

export interface AccountingLedgerBookWorkflowReadiness {
  ledgerBookId?: string | null;
  postingRulesLedgerBookNativeCertified: boolean;
  journalLifecycleLedgerBookNativeCertified: boolean;
  closeReportingLedgerBookNativeCertified: boolean;
  closePlanConfigurationLedgerBookNativeCertified?: boolean;
  externalGlLedgerBookNativeCertified: boolean;
  reconciliationLedgerBookNativeCertified?: boolean;
  directLendingLedgerBookNativeCertified?: boolean;
  strategyLedgerReadLedgerBookNativeCertified?: boolean;
  evidenceReferences: string[];
  completedControlCount: number;
  requiredControlCount: number;
  hasLedgerBookScope: boolean;
  hasRetainedEvidence: boolean;
  hasLedgerBookScopedEvidence: boolean;
}

export interface AccountingDimensionalReportingReadiness {
  ledgerBookId?: string | null;
  periodReportDimensionQueriesCertified: boolean;
  crossPeriodReportDimensionQueriesCertified: boolean;
  journalQueryDimensionFiltersCertified: boolean;
  externalExportDimensionMappingCertified: boolean;
  ledgerLineDimensionsPersistedCertified?: boolean;
  trialBalanceDimensionFiltersCertified?: boolean;
  reportPackageDimensionProvenanceCertified?: boolean;
  evidenceReferences: string[];
  completedControlCount: number;
  requiredControlCount: number;
  hasLedgerBookScope: boolean;
  hasRetainedEvidence: boolean;
  hasLedgerBookScopedEvidence: boolean;
}

export interface AccountingTenantAdministrationProfile {
  tenantId: string;
  companyId: string;
  tenantScopeConfigured: boolean;
  adminRoleProfileConfigured: boolean;
  scopedAccessPoliciesConfigured: boolean;
  reportingGroupsConfigured: boolean;
  accountingAdminSurfaceConfigured: boolean;
  browserAccountingAdminSurfaceConfigured: boolean;
  wpfAccountingAdminSurfaceConfigured: boolean;
  chartAdministrationStudioConfigured?: boolean;
  ruleTestPromotionStudioConfigured?: boolean;
  closeSetupStudioConfigured?: boolean;
  providerMappingStudioConfigured?: boolean;
  tenantCompanyReportGroupSetupStudioConfigured?: boolean;
  auditReviewToolingConfigured?: boolean;
  bulkImportExportSafeguardsConfigured?: boolean;
  performanceValidationConfigured?: boolean;
  disasterRecoveryRunbookConfigured?: boolean;
  ledgerBookAdministrationStudioConfigured?: boolean;
  postingRuleAuthoringStudioConfigured?: boolean;
  approvalQueueStudioConfigured?: boolean;
  dimensionMappingStudioConfigured?: boolean;
  implementationSandboxConfigured?: boolean;
  approvalQueueConfigurations?: AccountingApprovalQueueConfiguration[] | null;
  dimensionMappingConfigurations?: AccountingDimensionMappingConfiguration[] | null;
  updatedAtUtc: string;
  updatedBy: string;
  evidenceReferences: string[];
  correlationId?: string | null;
}

export interface AccountingApprovalQueueConfiguration {
  queueId: string;
  displayName: string;
  workflowKind: string;
  requiredApprovalRole: string;
  requiredApprovalCount: number;
  segregationPolicy: string;
  evidenceRequirement: string;
}

export interface AccountingDimensionMappingConfiguration {
  mappingId: string;
  displayName: string;
  providerId: string;
  meridianDimensions: LedgerDimensionSet;
  providerDimensions: LedgerDimensionSet;
  evidenceRequirement: string;
}

export interface AccountingTenantAdministrationProfileUpsertRequest {
  profile: AccountingTenantAdministrationProfile;
  actor: string;
  correlationId?: string | null;
  evidenceLinks?: string[] | null;
}

export interface AccountingProductionCertificationProfile {
  fundProfileId: string;
  ledgerBookId?: string | null;
  tenantId?: string | null;
  companyId?: string | null;
  postingRulesLedgerBookNativeCertified: boolean;
  journalLifecycleLedgerBookNativeCertified: boolean;
  closeReportingLedgerBookNativeCertified: boolean;
  closePlanConfigurationLedgerBookNativeCertified?: boolean;
  externalGlLedgerBookNativeCertified: boolean;
  reconciliationLedgerBookNativeCertified?: boolean;
  directLendingLedgerBookNativeCertified?: boolean;
  strategyLedgerReadLedgerBookNativeCertified?: boolean;
  periodReportDimensionQueriesCertified: boolean;
  crossPeriodReportDimensionQueriesCertified: boolean;
  journalQueryDimensionFiltersCertified: boolean;
  externalExportDimensionMappingCertified: boolean;
  ledgerLineDimensionsPersistedCertified?: boolean;
  trialBalanceDimensionFiltersCertified?: boolean;
  reportPackageDimensionProvenanceCertified?: boolean;
  updatedAtUtc: string;
  updatedBy: string;
  evidenceReferences: string[];
  correlationId?: string | null;
  workflowCertificationArtifacts?: AccountingWorkflowCertificationArtifact[];
  dimensionalCertificationArtifacts?: AccountingDimensionalCertificationArtifact[];
  tenantAdminCertificationArtifacts?: AccountingTenantAdminCertificationArtifact[];
}

export interface AccountingProductionCertificationProfileUpsertRequest {
  profile: AccountingProductionCertificationProfile;
  actor: string;
  correlationId?: string | null;
  evidenceLinks?: string[] | null;
}

export type AccountingCertificationArtifactStatus =
  | "Draft"
  | "Certified"
  | "Rejected"
  | "Superseded";

export type AccountingCertificationArtifactLaneStatus =
  | "NotTested"
  | "Passed"
  | "Warning"
  | "Failed";

export type AccountingWorkflowCertificationLaneKind =
  | "PostingRules"
  | "JournalLifecycle"
  | "CloseReporting"
  | "ClosePlanConfiguration"
  | "ExternalGl"
  | "Reconciliation"
  | "DirectLendingProjection"
  | "StrategyLedgerReads";

export type AccountingDimensionalCertificationLaneKind =
  | "LedgerLinePersistence"
  | "TrialBalanceFilters"
  | "PeriodReports"
  | "CrossPeriodReports"
  | "JournalFilters"
  | "ReportPackageProvenance"
  | "ExternalExportMappings";

export type AccountingTenantAdminCertificationLaneKind =
  | "TenantScope"
  | "AdminRoleProfile"
  | "ScopedAccessPolicies"
  | "ReportingGroups"
  | "AccountingAdminSurface"
  | "BrowserAccountingAdminSurface"
  | "WpfAccountingAdminSurface"
  | "ChartAdministrationStudio"
  | "RuleTestPromotionStudio"
  | "CloseSetupStudio"
  | "ProviderMappingStudio"
  | "TenantCompanyReportGroupSetupStudio"
  | "AuditReviewTooling"
  | "BulkImportExportSafeguards"
  | "PerformanceValidation"
  | "DisasterRecoveryRunbook"
  | "LedgerBookAdministrationStudio"
  | "PostingRuleAuthoringStudio"
  | "ApprovalQueueStudio"
  | "DimensionMappingStudio"
  | "ImplementationSandbox";

export interface AccountingCertificationArtifactIssue {
  code: string;
  severity: AccountingConfigurationValidationSeverity;
  message: string;
  suggestedAction: string;
  evidenceReferences?: string[] | null;
}

export interface AccountingWorkflowCertificationLane {
  kind: AccountingWorkflowCertificationLaneKind;
  status: AccountingCertificationArtifactLaneStatus;
  evidenceReferences?: string[] | null;
  issues?: AccountingCertificationArtifactIssue[] | null;
}

export interface AccountingDimensionalCertificationLane {
  kind: AccountingDimensionalCertificationLaneKind;
  status: AccountingCertificationArtifactLaneStatus;
  evidenceReferences?: string[] | null;
  issues?: AccountingCertificationArtifactIssue[] | null;
}

export interface AccountingTenantAdminCertificationLane {
  kind: AccountingTenantAdminCertificationLaneKind;
  status: AccountingCertificationArtifactLaneStatus;
  evidenceReferences?: string[] | null;
  issues?: AccountingCertificationArtifactIssue[] | null;
}

export interface AccountingWorkflowCertificationArtifact {
  certificationId: string;
  status: AccountingCertificationArtifactStatus;
  tenantId?: string | null;
  companyId?: string | null;
  fundProfileId: string;
  ledgerBookId: string;
  certifiedBy: string;
  certifiedAtUtc: string;
  sourceService: string;
  lanes?: AccountingWorkflowCertificationLane[] | null;
  evidenceReferences?: string[] | null;
  issues?: AccountingCertificationArtifactIssue[] | null;
  correlationId?: string | null;
}

export interface AccountingDimensionalCertificationArtifact {
  certificationId: string;
  status: AccountingCertificationArtifactStatus;
  tenantId?: string | null;
  companyId?: string | null;
  fundProfileId: string;
  ledgerBookId: string;
  dimensionScopeEvidenceKey: string;
  certifiedBy: string;
  certifiedAtUtc: string;
  sourceService: string;
  lanes?: AccountingDimensionalCertificationLane[] | null;
  evidenceReferences?: string[] | null;
  issues?: AccountingCertificationArtifactIssue[] | null;
  correlationId?: string | null;
}

export interface AccountingTenantAdminCertificationArtifact {
  certificationId: string;
  status: AccountingCertificationArtifactStatus;
  tenantId: string;
  companyId: string;
  fundProfileId: string;
  ledgerBookId?: string | null;
  certifiedBy: string;
  certifiedAtUtc: string;
  sourceService: string;
  lanes?: AccountingTenantAdminCertificationLane[] | null;
  evidenceReferences?: string[] | null;
  issues?: AccountingCertificationArtifactIssue[] | null;
  correlationId?: string | null;
}

export interface AccountingProductionReadiness {
  generatedAtUtc: string;
  fundProfileId: string;
  ledgerBookId?: string | null;
  status: AccountingProductionReadinessStatus;
  score: number;
  components: AccountingProductionReadinessComponent[];
  issues: AccountingProductionReadinessIssue[];
  ledgerBookRollout?: LedgerBookRolloutAssessment | null;
  rulesStudioSummary?: AccountingRulesStudioSummary | null;
  ledgerBookWorkflows?: AccountingLedgerBookWorkflowReadiness | null;
  dimensionalReporting?: AccountingDimensionalReportingReadiness | null;
  externalGlProviderCount: number;
  certifiedExternalGlMappingProfileCount: number;
  externalGlLivePostingEnabled: boolean;
  migrationRunArtifacts?: AccountingMigrationRunArtifact[];
  migrationRolloutPlan?: AccountingMigrationRolloutPlanItem[];
  tenantAdministration?: AccountingTenantAdministrationReadiness | null;
  productionGaps?: AccountingProductionGap[];
  workflowCertificationArtifacts?: AccountingWorkflowCertificationArtifact[];
  dimensionalCertificationArtifacts?: AccountingDimensionalCertificationArtifact[];
  tenantAdminCertificationArtifacts?: AccountingTenantAdminCertificationArtifact[];
  criticalIssueCount: number;
  warningIssueCount: number;
}

export interface ChartOfAccountsNode {
  nodeId: string;
  path: string;
  accountName: string;
  accountType: string;
  parentPath?: string | null;
  symbol?: string | null;
  financialAccountId?: string | null;
  isArchived: boolean;
}

export interface JournalEntryTemplateLine {
  lineId: string;
  accountPath: string;
  side: AccountingTemplateLineSide;
  amount: number;
  currency: string;
  description?: string | null;
}

export interface JournalEntryTemplate {
  templateId: string;
  displayName: string;
  description: string;
  lines: JournalEntryTemplateLine[];
  isArchived: boolean;
  version: string;
}

export interface AccountingRuleCondition {
  conditionId: string;
  field: string;
  operator: AccountingRuleConditionOperator;
  value?: string | null;
  secondValue?: string | null;
  isRequired: boolean;
  description?: string | null;
}

export interface AccountingRuleConditionGroup {
  groupId: string;
  operator: AccountingRuleConditionGroupOperator;
  conditions: AccountingRuleCondition[];
  isRequired: boolean;
  description?: string | null;
}

export interface AccountingRuleFormula {
  formulaId: string;
  kind: AccountingRuleFormulaKind;
  value: number;
  currency: string;
  description?: string | null;
}

export interface AllocationRule {
  allocationRuleId: string;
  basis: AllocationRuleBasis;
  weight: number;
  targetDimensions?: LedgerDimensionSet | null;
  formulaId?: string | null;
  description?: string | null;
}

export interface GeneratedPostingLine {
  lineId: string;
  accountPath: string;
  side: AccountingTemplateLineSide;
  amountFormulaId: string;
  amount: number;
  currency: string;
  dimensions?: LedgerDimensionSet | null;
  description?: string | null;
}

export interface RulePromotionApproval {
  approvalId: string;
  requestedBy: string;
  requestedAtUtc: string;
  approvalState: ManualJournalEntryStatus;
  approvedBy?: string | null;
  approvedAtUtc?: string | null;
  notes?: string | null;
  evidenceLinks: string[];
}

export interface AccountingRuleVersion {
  version: string;
  createdAtUtc: string;
  createdBy: string;
  changeSummary: string;
  promotionApproval?: RulePromotionApproval | null;
  evidenceLinks: string[];
}

export interface PostingRule {
  ruleId: string;
  displayName: string;
  sourceEventType: string;
  templateId: string;
  ruleVersion: string;
  isArchived: boolean;
  description?: string | null;
  effectiveFrom?: string | null;
  effectiveTo?: string | null;
  priority?: number;
  scope?: LedgerDimensionSet | null;
  conditions?: AccountingRuleCondition[] | null;
  conditionGroups?: AccountingRuleConditionGroup[] | null;
  formulas?: AccountingRuleFormula[] | null;
  allocations?: AllocationRule[] | null;
  generatedPostings?: GeneratedPostingLine[] | null;
  versions?: AccountingRuleVersion[] | null;
  promotionApproval?: RulePromotionApproval | null;
  requiresPromotionApproval?: boolean;
}

export interface AccountingConfigurationValidationIssue {
  code: string;
  severity: AccountingConfigurationValidationSeverity;
  message: string;
  targetId?: string | null;
  suggestedAction?: string | null;
}

export interface AccountingActionAuditEvent {
  auditEventId: string;
  recordedAtUtc: string;
  actor: string;
  action: string;
  fundProfileId?: string | null;
  ledgerBookId?: string | null;
  correlationId?: string | null;
  beforeHash: string;
  afterHash: string;
  validationIssues: AccountingConfigurationValidationIssue[];
  evidenceLinks: string[];
  companyId?: string | null;
  reportGroupPrincipalIds?: string[] | null;
}

export interface AccountingRulesStudioSummary {
  totalRules: number;
  activeRules: number;
  archivedRules: number;
  effectiveDatedRules: number;
  generatedPostingRules: number;
  templateMappingRules: number;
  rulesWithConditions: number;
  rulesWithFormulas: number;
  rulesWithAllocations: number;
  rulesRequiringPromotionApproval: number;
  approvedPromotionRules: number;
  pendingPromotionApprovalRules: number;
  savedTestCaseCount: number;
  rulesWithSavedRegressionTests: number;
  rulesMissingCurrentVersionRegressionTests: number;
  criticalIssueCount: number;
  warningIssueCount: number;
}

export interface AccountingRulesStudioRuleRow {
  ruleId: string;
  displayName: string;
  sourceEventType: string;
  ruleVersion: string;
  priority: number;
  effectiveFrom?: string | null;
  effectiveTo?: string | null;
  templateId: string;
  isArchived: boolean;
  usesGeneratedPostings: boolean;
  conditionCount: number;
  conditionGroupCount: number;
  formulaCount: number;
  allocationCount: number;
  generatedPostingLineCount: number;
  versionCount: number;
  savedTestCaseCount: number;
  savedTestEvidenceLinkCount: number;
  requiresPromotionApproval: boolean;
  isPromotionApproved: boolean;
  promotionApprovalState?: ManualJournalEntryStatus | null;
  promotionApprovalId?: string | null;
  criticalIssueCount: number;
  warningIssueCount: number;
  canDryRun: boolean;
  canRequestPromotion: boolean;
  canActivate: boolean;
}

export interface AccountingRulesStudioPromotionQueueItem {
  ruleId: string;
  displayName: string;
  ruleVersion: string;
  requestedBy: string;
  requestedAtUtc?: string | null;
  approvalState?: ManualJournalEntryStatus | null;
  approvalId?: string | null;
  regressionTestCaseCount: number;
  missingRegressionEvidenceCount: number;
  criticalIssueCount: number;
  suggestedAction: string;
}

export interface AccountingRulesStudio {
  summary: AccountingRulesStudioSummary;
  rules: AccountingRulesStudioRuleRow[];
  promotionQueue: AccountingRulesStudioPromotionQueueItem[];
}

export interface AccountingConfigurationWorkspace {
  fundProfileId: string;
  ledgerBookId?: string | null;
  status: AccountingConfigurationStatus;
  configurationVersion: string;
  updatedAtUtc: string;
  ledgerBooks: LedgerBook[];
  chartOfAccounts: ChartOfAccountsNode[];
  journalTemplates: JournalEntryTemplate[];
  postingRules: PostingRule[];
  validationIssues: AccountingConfigurationValidationIssue[];
  auditTrail: AccountingActionAuditEvent[];
  ruleTestCases?: AccountingRuleTestCase[] | null;
  rulesStudio?: AccountingRulesStudio | null;
  ledgerBookSetupCandidate?: LedgerBookSetupCandidate | null;
}

export interface AccountingJournalPreviewLine {
  accountPath: string;
  accountName: string;
  side: AccountingTemplateLineSide;
  amount: number;
  currency: string;
  description?: string | null;
}

export interface AccountingJournalTemplatePreview {
  templateId: string;
  displayName: string;
  isBalanced: boolean;
  totalDebits: number;
  totalCredits: number;
  lines: AccountingJournalPreviewLine[];
  validationIssues: AccountingConfigurationValidationIssue[];
}

export interface RuleDryRunRequest {
  fundProfileId: string;
  sourceEventType: string;
  eventAmount: number;
  currency: string;
  effectiveDate: string;
  actor: string;
  ledgerBookId?: string | null;
  dimensions?: LedgerDimensionSet | null;
  counterpartyId?: string | null;
  instrumentSymbol?: string | null;
  correlationId?: string | null;
}

export interface AccountingRuleDryRunMatch {
  ruleId: string;
  displayName: string;
  ruleVersion: string;
  priority: number;
  isMatched: boolean;
  explanations: string[];
  validationIssues: AccountingConfigurationValidationIssue[];
}

export interface RuleDryRunResult {
  fundProfileId: string;
  ledgerBookId?: string | null;
  sourceEventType: string;
  effectiveDate: string;
  eventAmount: number;
  currency: string;
  isPostingBalanced: boolean;
  selectedRuleId?: string | null;
  ruleMatches: AccountingRuleDryRunMatch[];
  generatedLines: AccountingJournalPreviewLine[];
  generatedPostingLines?: GeneratedPostingLine[] | null;
  validationIssues: AccountingConfigurationValidationIssue[];
}

export type LedgerPostingKind = "Originating" | "Adjustment" | "ClosingEntry";
export type AccountingTreatmentKind =
  | "General"
  | "Accrual"
  | "Expense"
  | "PrepaidExpense"
  | "Amortization"
  | "Deferral"
  | "Reclassification"
  | "Reversal"
  | "FxTranslation"
  | "TaxLotRelief"
  | "DirectLendingAccrual"
  | "EquityMethodInvestment"
  | "Intercompany"
  | "ConsolidationElimination";
export type AccountingPostingIntent = "Originating" | "Adjustment" | "Reversal" | "Rebook" | "Restatement" | "AutomatedDraft";
export type AccountingPostingApprovalState = "NotRequired" | "Pending" | "Approved" | "Rejected";
export type AccountingPostingEvidenceKind =
  | "Source"
  | "Approval"
  | "Reconciliation"
  | "Settlement"
  | "PeriodLock"
  | "OperatorRationale"
  | "Correction"
  | "ReportOutput"
  | "AuditSupport";

export interface AccountingPostingEvidenceReference {
  evidenceId: string;
  uri: string;
  kind: AccountingPostingEvidenceKind;
  sourceSystem: string;
  retainedAtUtc: string;
  retainedBy: string;
  subjectId?: string | null;
  contentHash?: string | null;
  description?: string | null;
}

export interface AccountingPostingCommand {
  commandId: string;
  aggregateId: string;
  periodId: string;
  effectiveDate: string;
  postingDate: string;
  idempotencyKey: string;
  intent: AccountingPostingIntent;
  sourceEventId?: string | null;
  correlationId?: string | null;
  causationId?: string | null;
  sourceJournalEntryId?: string | null;
  expectedVersion?: number | null;
  sourceEventType?: string | null;
  treasuryContext?: TreasuryLedgerContext | null;
  approvalState: AccountingPostingApprovalState;
  approvalId?: string | null;
  operatorRationale?: string | null;
  evidence: AccountingPostingEvidenceReference[];
  actionOrigin: OperationsActionOrigin;
  ledgerBookId?: string | null;
  bookContext?: AccountingBookContext | null;
  bookPositionId?: string | null;
  economicEvent?: EconomicEventReference | null;
  projectionLineage?: ProjectionLineage | null;
  rulePackReference?: AccountingRulePackReference | null;
}

export interface PostingRuleJournalCandidateRequest {
  fundProfileId: string;
  sourceEventType: string;
  eventAmount: number;
  currency: string;
  effectiveDate: string;
  actor: string;
  aggregateId: string;
  periodId: string;
  accountingTimestamp: string;
  description: string;
  accountingBasis?: AccountingBasisKind;
  ledgerBookId?: string | null;
  dimensions?: LedgerDimensionSet | null;
  counterpartyId?: string | null;
  instrumentSymbol?: string | null;
  correlationId?: string | null;
  sourceEventId?: string | null;
  sourceJournalEntryId?: string | null;
  policyId?: string | null;
  treatmentKind?: AccountingTreatmentKind | null;
  postingKind?: LedgerPostingKind;
  treasuryContext?: TreasuryLedgerContext | null;
  evidenceLinks?: string[] | null;
  tenantId?: string | null;
  companyId?: string | null;
  bookContext?: AccountingBookContext | null;
  bookPositionId?: string | null;
  economicEvent?: EconomicEventReference | null;
  projectionLineage?: ProjectionLineage | null;
  rulePackReference?: AccountingRulePackReference | null;
}

export interface PostingRuleJournalCandidateIssue {
  code: string;
  severity: AccountingConfigurationValidationSeverity;
  message: string;
  blocksCandidate: boolean;
  targetId?: string | null;
  suggestedAction?: string | null;
}

export interface PostingRuleJournalCandidateResult {
  dryRunResult: RuleDryRunResult;
  selectedRuleId?: string | null;
  selectedRuleVersion?: string | null;
  generatedPostingLines: GeneratedPostingLine[];
  postingCommand?: AccountingPostingCommand | null;
  bookContext?: AccountingBookContext | null;
  bookPositionId?: string | null;
  economicEvent?: EconomicEventReference | null;
  projectionLineage?: ProjectionLineage | null;
  rulePackReference?: AccountingRulePackReference | null;
  journalEntryId?: string | null;
  totalDebits: number;
  totalCredits: number;
  imbalance: number;
  isBalanced: boolean;
  hasBlockingIssues: boolean;
  canSubmitForApproval: boolean;
  canPostWithoutAdditionalApproval: boolean;
  evidenceLinks: string[];
  issues: PostingRuleJournalCandidateIssue[];
}

export interface AccountingRuleTestCase {
  testCaseId: string;
  displayName: string;
  request: RuleDryRunRequest;
  expectedRuleId?: string | null;
  expectedRuleVersion?: string | null;
  expectBalancedPosting: boolean;
  expectedIssueCodes: string[];
  expectedGeneratedPostingLines?: GeneratedPostingLine[] | null;
  evidenceLinks?: string[] | null;
}

export interface AccountingRuleTestCaseResult {
  testCaseId: string;
  displayName: string;
  passed: boolean;
  dryRunResult: RuleDryRunResult;
  assertionIssues: AccountingConfigurationValidationIssue[];
}

export interface AccountingRuleTestSuiteResult {
  fundProfileId: string;
  ledgerBookId?: string | null;
  executedAtUtc: string;
  actor: string;
  totalCount: number;
  passedCount: number;
  failedCount: number;
  results: AccountingRuleTestCaseResult[];
}

export interface ExecuteAccountingRuleTestCasesRequest {
  fundProfileId: string;
  actor: string;
  testCases?: AccountingRuleTestCase[] | null;
  ledgerBookId?: string | null;
  correlationId?: string | null;
}

export interface UpsertAccountingRuleTestCaseRequest {
  fundProfileId: string;
  testCase: AccountingRuleTestCase;
  actor: string;
  ledgerBookId?: string | null;
  correlationId?: string | null;
  evidenceLinks?: string[] | null;
  companyId?: string | null;
  reportGroupPrincipalIds?: string[] | null;
}

export interface ManualJournalEntryLine {
  lineId: string;
  side: AccountingTemplateLineSide;
  amount: number;
  currency: string;
  accountPath: string;
  entityId?: string | null;
  fundAllocationId?: string | null;
  securityId?: string | null;
  securityDisplayName?: string | null;
  taxLotId?: string | null;
  description?: string | null;
  evidenceLink?: string | null;
  ledgerAccountSymbol?: string | null;
  ledgerAccountFinancialAccountId?: string | null;
}

export interface ManualJournalEntryEvidenceAttachment {
  attachmentId: string;
  displayName: string;
  evidenceKind: string;
  uri: string;
  sourceSystem: string;
  addedAtUtc: string;
  addedBy: string;
  lineId?: string | null;
  description?: string | null;
}

export interface JournalEntryReversal {
  originalJournalEntryId: string;
  reversalJournalEntryId: string;
  reason: string;
  createdAtUtc: string;
  createdBy: string;
}

export interface JournalEntryRebook {
  originalJournalEntryId: string;
  rebookJournalEntryId: string;
  reason: string;
  createdAtUtc: string;
  createdBy: string;
}

export interface TreasuryLedgerContext {
  effectiveDate?: string | null;
  idempotencyKey?: string | null;
  fundEventId?: string | null;
  fundEventType?: string | null;
  capitalAccountId?: string | null;
  investorId?: string | null;
  paymentIntentId?: string | null;
  settlementReference?: string | null;
}

export interface ManualJournalEntryDraft {
  journalEntryId: string;
  status: ManualJournalEntryStatus;
  fundProfileId: string;
  ledgerBookId?: string | null;
  accountingBasis: AccountingBasisKind;
  accountingDate: string;
  periodId?: string | null;
  entityId?: string | null;
  fundNodeId?: string | null;
  currency: string;
  memo: string;
  preparedBy: string;
  createdAtUtc: string;
  updatedAtUtc: string;
  version: number;
  lines: ManualJournalEntryLine[];
  evidenceLinks: string[];
  validationIssues: AccountingConfigurationValidationIssue[];
  evidenceAttachments?: ManualJournalEntryEvidenceAttachment[] | null;
  totalDebits: number;
  totalCredits: number;
  imbalance: number;
  approvalId?: string | null;
  submittedAtUtc?: string | null;
  submittedBy?: string | null;
  entryType: ManualJournalEntryType;
  treasuryContext?: TreasuryLedgerContext | null;
  dimensions?: LedgerDimensionSet | null;
  lifecycleTransitions?: JournalEntryLifecycleTransition[] | null;
  reversalOfJournalEntryId?: string | null;
  rebookedFromJournalEntryId?: string | null;
  approvedAtUtc?: string | null;
  approvedBy?: string | null;
  postedAtUtc?: string | null;
  postedBy?: string | null;
  closedLockedAtUtc?: string | null;
  closeLockedBy?: string | null;
  reversal?: JournalEntryReversal | null;
  rebook?: JournalEntryRebook | null;
  tenantId?: string | null;
  companyId?: string | null;
}
