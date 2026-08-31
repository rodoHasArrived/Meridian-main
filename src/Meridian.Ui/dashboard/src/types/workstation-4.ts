import type {
  EvidenceVaultIdentity,
  FinancialRecordExplorerDto,
  ManualJournalEntryWorkbench,
  MetricSnapshot,
  OperationsActionOrigin,
  ReconciliationBreakQueueItem,
  StatementColumnMapping,
  StatementImportIssue,
  StatementRecordPreview,
} from "../types";

export interface StatementKindSummary {
  kind: string;
  recordCount: number;
  sampleRecords: StatementRecordPreview[];
}

export interface StatementProfileSuggestion {
  profileId: string;
  displayName: string;
  score: number;
}

export interface StatementImportPreview {
  connectorId: string;
  connectorDisplayName: string;
  profileId: string | null;
  fileName: string;
  fileSizeBytes: number;
  detectedColumns: string[];
  columnMappings: StatementColumnMapping[];
  recordCount: number;
  kindSummaries: StatementKindSummary[];
  issues: StatementImportIssue[];
  profileSuggestions: StatementProfileSuggestion[];
  status: "ReadyToImport" | "NeedsAttention" | string;
  nextAction: string;
  accountSnapshots?: StatementAccountSnapshotPreview[];
  activitySubtypeSummaries?: StatementActivitySubtypeSummary[];
  activityCompleteness?: StatementActivityCompleteness[];
  taxLotCount?: number;
  borrowPositionCount?: number;
}

export interface StatementAccountSnapshotPreview {
  providerId: string;
  accountId: string;
  asOf: string;
  currency: string;
  status: string;
  marginRegime: string;
  cash: number;
  equity: number;
  buyingPower: number;
  initialMargin: number | null;
  maintenanceMargin: number | null;
  excessLiquidity: number | null;
  marginLoan: number | null;
  multiplier: number | null;
  tradingBlocked: boolean;
  transfersBlocked: boolean;
  accountBlocked: boolean;
  shortingEnabled: boolean;
  optionsApprovedLevel: number | null;
  optionsTradingLevel: number | null;
  restrictions: string[];
}

export interface StatementActivitySubtypeSummary {
  category: string;
  subtype: string;
  recordCount: number;
}

export interface StatementActivityCompleteness {
  lastEventId: string | null;
  highWatermark: string | null;
  pageCount: number;
  sourceRecordCount: number;
  isComplete: boolean;
}

export interface StatementImportCommitResult {
  runId: string;
  duplicate: boolean;
  recordCount: number;
  kindSummaries: StatementKindSummary[];
  breakCount: number;
  caseCount: number;
  retainedSourcePath: string;
  retainedCanonicalPath: string;
  status: string;
  nextAction: string;
  evidenceVaultIdentity?: EvidenceVaultIdentity | null;
  evidenceWorkbenchRoute?: string | null;
  reconciliationRoute?: string | null;
  breakIds?: string[];
  caseIds?: string[];
  reconciliationCaseRoutes?: string[];
  reconciliationCaseLinks?: StatementImportReconciliationCaseLink[];
  nextActions?: string[];
  retainedCanonicalEvidencePath?: string | null;
  statementReconciliationReportWorkflowId?: string | null;
  statementReconciliationReportStatusRoute?: string | null;
  operationsWorkflowId?: string | null;
  accountingScope?: StatementReconciliationAccountingScope | null;
}

export interface MarginPositionContribution {
  symbol: string;
  quantity: number;
  marketValue: number;
  shadowInitialMargin: number;
  shadowMaintenanceMargin: number;
  borrowStatus: string | null;
  borrowRate: number | null;
  taxLotCount: number;
  optionLifecycleEventCount: number;
  securityId?: string | null;
  securityMasterSource?: string | null;
}

export interface MarginControlAccount {
  providerId: string;
  accountId: string;
  asOf: string;
  snapshotPhase: string;
  certificationState: string;
  currency: string;
  marginRegime: string;
  cash: number;
  equity: number;
  buyingPower: number;
  providerInitialMargin: number | null;
  providerMaintenanceMargin: number | null;
  providerExcessLiquidity: number | null;
  providerMarginLoan: number | null;
  shadowModelName: string;
  shadowInitialMargin: number | null;
  shadowMaintenanceMargin: number | null;
  shadowExcessLiquidity: number | null;
  maintenanceVariance: number | null;
  riskLevel: string;
  activityComplete: boolean | null;
  restrictions: string[];
  positionContributions: MarginPositionContribution[];
  optionLifecycleEventCount: number;
  borrowPositionCount: number;
  taxLotCount: number;
  evidencePath: string;
  certifiedBy?: string | null;
  certifiedAtUtc?: string | null;
  certificationNote?: string | null;
}

export interface MarginControlPrimeSummary {
  providerId: string;
  accountCount: number;
  totalEquity: number;
  providerMaintenanceMargin: number | null;
  providerExcessLiquidity: number | null;
  criticalAccountCount: number;
}

export interface MarginControlAlert {
  severity: string;
  providerId: string;
  accountId: string;
  code: string;
  message: string;
  suggestedAction: string;
}

export interface MarginControlCenter {
  generatedAtUtc: string;
  accounts: MarginControlAccount[];
  primeSummaries: MarginControlPrimeSummary[];
  alerts: MarginControlAlert[];
  providerCount: number;
  accountCount: number;
  provisionalAccountCount: number;
  endOfDayCertificationCandidateCount: number;
  authorityNote: string;
  nextAction: string;
}

export interface MarginCertificationRequest {
  providerId: string;
  accountId: string;
  asOf: string;
  evidencePath: string;
  note: string;
}

export interface MarginCertificationResult extends MarginCertificationRequest {
  certifiedBy: string;
  certifiedAtUtc: string;
  status: string;
}

export interface StatementImportReconciliationCaseLink {
  caseId: string;
  breakId?: string | null;
  route: string;
  label: string;
  status: string;
  priority: string;
  reason: string;
  suggestedNextAction: string;
}

export interface StatementFetchSchedule {
  scheduleId: string;
  connectorId: string;
  externalAccountId: string;
  fundAccountId: string;
  sourceInstitution: string;
  mappingProfileId: string | null;
  toleranceProfileId: string;
  cadenceHours: number;
  enabled: boolean;
  lastRunAtUtc: string | null;
  lastRunStatus: string | null;
  nextDueAtUtc: string | null;
  sourceKind: StatementImportSourceKind;
  periodStart: string | null;
  periodEnd: string | null;
  accountingScope: StatementReconciliationAccountingScope | null;
}

export interface StatementReconciliationAccountingScope {
  fundProfileId: string;
  ledgerBookId: string;
  accountingPeriodId: string;
  asOfDate: string;
}

export type StatementImportSourceKind = "broker" | "custodian";
export type StatementFetchDatasets = "activity" | "positions" | "all";

export interface StatementImportPreviewRequest {
  file: File;
  connectorId?: string | null;
  mappingProfileId?: string | null;
  externalAccountId: string;
  sourceKind: StatementImportSourceKind;
  sourceInstitution: string;
  fundAccountId: string;
  periodStart: string;
  periodEnd: string;
}

export interface StatementFetchPreviewRequest {
  connectorId: string;
  externalAccountId: string;
  fundAccountId: string;
  sourceInstitution: string;
  sourceKind: StatementImportSourceKind;
  since?: string | null;
  mappingProfileId?: string | null;
  datasets?: StatementFetchDatasets | null;
}

export interface StatementFetchScheduleUpsertRequest {
  scheduleId?: string | null;
  connectorId: string;
  externalAccountId: string;
  fundAccountId: string;
  sourceInstitution: string;
  mappingProfileId?: string | null;
  toleranceProfileId?: string | null;
  cadenceHours: number;
  enabled: boolean;
  sourceKind?: StatementImportSourceKind | null;
  periodStart: string;
  periodEnd: string;
}

export interface AccountingReconciliationRecord {
  runId: string;
  strategyName: string;
  mode: "paper" | "live" | "backtest";
  status: string;
  lastUpdated: string;
  breakCount: number;
  openBreakCount: number;
  reconciliationStatus: "NotStarted" | "BreaksOpen" | "SecurityCoverageOpen" | "Resolved" | "Balanced";
}

export type GovernanceReconciliationRecord = AccountingReconciliationRecord;

export interface AccountingCashFlowSummary {
  totalCash: number;
  totalLedgerCash: number;
  netVariance: number;
  totalFinancing: number;
  runsWithCashSignals: number;
  runsWithCashVariance: number;
  tone: "default" | "success" | "warning" | "danger";
  summary: string;
}

export interface AccountingReportingProfile {
  id: string;
  name: string;
  targetTool: string;
  format: string;
  description: string;
  loaderScript: boolean;
  dataDictionary: boolean;
}

export interface ReportBrandingTheme {
  themeId: string;
  name: string;
  firmName: string;
  primaryColor: string;
  accentColor: string;
  textColor: string;
  backgroundColor: string;
  logoUri: string | null;
  footerText: string | null;
  disclaimer: string | null;
  isBuiltIn: boolean;
}

export type PortfolioReportingCutKind = "Fund" | "Strategy" | "UserTag";
export type PortfolioReportingLiveViewState = "LiveLinked" | "SourceBacked" | "Stale" | "Blocked";
export type PortfolioReportingPnlSlicePeriod = "Daily" | "Weekly" | "Monthly" | "Yearly";
export type PortfolioReportingAnalyticsKind = "TopWinner" | "TopLaggard" | "Contribution";
export type PortfolioReportingAnalyticsScope = "Security" | "Strategy" | "AssetClass";
export type CrossFundReportingConsolidationScope = "Company" | "Fund" | "Entity";

export interface PortfolioReportingCut {
  cutId: string;
  label: string;
  kind: PortfolioReportingCutKind;
  currency: string;
  grossExposure: number;
  netExposure: number;
  longMarketValue: number;
  shortMarketValue: number;
  totalCash: number;
  pendingSettlement: number;
  realizedPnl: number;
  unrealizedPnl: number;
  totalPnl: number;
  shadowNav: number;
  shadowNavVariance: number;
  sourceCount: number;
  tags: string[];
  asOf: string;
  evidenceRoute: string | null;
  shadowNavNote: string | null;
  versionStamp: string | null;
}

export interface PortfolioReportingLiveViewFreshnessPolicy {
  policyName: string;
  evaluatedAtUtc: string;
  sourceAgeSeconds: number | null;
  liveLinkWindowSeconds: number;
  staleWindowSeconds: number;
  isWithinLiveLinkWindow: boolean;
  isBeyondStaleWindow: boolean;
  reason: string;
}

export interface PortfolioReportingLiveView {
  viewId: string;
  label: string;
  kind: PortfolioReportingCutKind;
  state: PortfolioReportingLiveViewState;
  currency: string;
  grossExposure: number;
  netExposure: number;
  totalCash: number;
  pendingSettlement: number;
  totalPnl: number;
  shadowNav: number;
  asOf: string;
  sourceAsOfUtc: string | null;
  sourceCount: number;
  route: string;
  liquiditySummary: string;
  cashLadderSummary: string;
  telemetrySummary: string;
  tags: string[];
  cashLadderRoute: string | null;
  versionStamp: string | null;
  readinessBlockers?: string[] | null;
  marketTickAsOfUtc?: string | null;
  marketTickAgeSeconds?: number | null;
  marketTickSequence?: number | null;
  marketDataProvider?: string | null;
  tickFreshnessSummary?: string | null;
  isMarketTickLinked?: boolean;
  freshnessPolicy?: PortfolioReportingLiveViewFreshnessPolicy | null;
}

export interface PortfolioReportingPnlSlice {
  sliceId: string;
  period: PortfolioReportingPnlSlicePeriod;
  label: string;
  currency: string;
  startDate: string;
  endDate: string;
  realizedPnl: number;
  unrealizedPnl: number;
  totalPnl: number;
  priorTotalPnl: number;
  pnlChange: number;
  sourceCount: number;
  asOf: string;
  route: string;
  readinessSummary: string;
  tags: string[];
  versionStamp: string | null;
}

export interface PortfolioReportingAnalyticsRow {
  analyticsId: string;
  kind: PortfolioReportingAnalyticsKind;
  scope: PortfolioReportingAnalyticsScope;
  rank: number;
  label: string;
  symbol: string | null;
  classification: string | null;
  currency: string;
  realizedPnl: number;
  unrealizedPnl: number;
  totalPnl: number;
  contributionPercent: number;
  heatMapIntensity: number;
  sourceCount: number;
  asOf: string;
  route: string;
  readinessSummary: string;
  tags: string[];
  versionStamp: string | null;
}

export interface CrossFundReportingConsolidation {
  consolidationId: string;
  label: string;
  scope: CrossFundReportingConsolidationScope;
  currency: string;
  isReady: boolean;
  fundCount: number;
  entityCount: number;
  accountCount: number;
  runCount: number;
  grossExposure: number;
  netExposure: number;
  longMarketValue: number;
  shortMarketValue: number;
  totalCash: number;
  pendingSettlement: number;
  totalPnl: number;
  shadowNav: number;
  shadowNavVariance: number;
  sourceCount: number;
  asOf: string;
  route: string;
  readinessSummary: string;
  tags: string[];
  versionStamp: string | null;
}

export interface ReportingTemplateMetadata {
  templateId: string;
  family: string;
  name: string;
  version: string;
  sections: string[];
  reportWriterGrids?: ReportingTemplateGridMetadata[] | null;
  lifecycleStatus?: string;
  isBuiltIn?: boolean;
  isLatestApproved?: boolean;
  approvalSummary?: string;
  authoringRoute?: string;
  accessMode?: string;
  accessSummary?: string;
  isAccessible?: boolean;
  createdBy?: string | null;
  createdAt?: string | null;
  updatedBy?: string | null;
  updatedAt?: string | null;
  submittedBy?: string | null;
  submittedAt?: string | null;
  approvedBy?: string | null;
  approvedAt?: string | null;
  rejectedBy?: string | null;
  rejectedAt?: string | null;
  decisionRationale?: string | null;
  approvalReference?: string | null;
  basedOnTemplateId?: VersionedReportTemplateId | null;
  auditTrail?: ReportTemplateAuditEvent[] | null;
  validationIssues?: string[] | null;
}

export interface ReportingTemplateGridMetadata {
  gridId: string;
  title: string;
  kind: string;
  dimensionCount: number;
  metricCount: number;
  formulaCount: number;
  rowFields?: string[] | null;
  columnFields?: string[] | null;
  metrics?: ReportingTemplateGridMetricMetadata[] | null;
  formulas?: ReportingTemplateGridFormulaMetadata[] | null;
  topN?: number | null;
  sortBy?: string | null;
  sortDescending?: boolean;
  filters?: ReportingTemplateGridFilterMetadata[] | null;
  sourceFields?: ReportingTemplateGridFieldMetadata[] | null;
}

export interface ReportingTemplateGridFieldMetadata {
  name: string;
  label: string;
  role: string;
  dataType: string;
  dataset: string;
  description?: string | null;
}

export interface ReportWriterDatasetSource {
  sourceId: string;
  label: string;
  description: string;
  rowCount: number;
  fields: ReportingTemplateGridFieldMetadata[];
  rows: Record<string, string>[];
  tags?: string[] | null;
  certificationState?: string | null;
  validationState?: string | null;
  reconciliationState?: string | null;
  refreshCadence?: string | null;
  owner?: string | null;
  version?: string | null;
  releaseApproval?: string | null;
  lineageManifest?: string | null;
  sourceRunIds?: string[] | null;
  permittedConsumers?: string[] | null;
  rowLineageKeyField?: string | null;
  evidenceIndexField?: string | null;
}

export interface ReportingTemplateGridMetricMetadata {
  name: string;
  sourceField: string;
  function: string;
  label: string | null;
}

export interface ReportingTemplateGridFormulaMetadata {
  name: string;
  expression: string;
  label: string | null;
}

export interface ReportingTemplateGridFilterMetadata {
  field: string;
  operator: ReportWriterFilterOperator | string;
  value?: string | null;
  label?: string | null;
}

export type ReportWriterGridKind = "Detail" | "Pivot" | "TopN" | "Contribution";
export type ReportWriterAggregateFunction = "Sum" | "Count" | "Average" | "Min" | "Max";
export type ReportWriterFilterOperator =
  | "Equals"
  | "NotEquals"
  | "Contains"
  | "StartsWith"
  | "EndsWith"
  | "GreaterThan"
  | "GreaterThanOrEqual"
  | "LessThan"
  | "LessThanOrEqual"
  | "IsBlank"
  | "IsNotBlank";
export type ReportAccessMode = "Private" | "Restricted" | "CompanyWide";
export type ReportAccessPrincipalKind = "User" | "Group" | "Company";
export type ReportWriterCellStyle = "None" | "Success" | "Warning" | "Danger" | "Info";
export type ReportWriterChartType = "Bar" | "StackedBar" | "Line" | "Area" | "Pie";
export type ReportWriterDiffRowState = "Unchanged" | "Changed" | "Added" | "Removed";
export type ReportWriterDiffDirection = "Flat" | "Up" | "Down";

export interface VersionedReportTemplateId {
  name: string;
  version: number;
}

export interface ReportTemplateParameterDefinition {
  name: string;
  required: boolean;
}

export interface ReportWriterMetricDefinition {
  name: string;
  sourceField: string;
  function?: ReportWriterAggregateFunction;
  label?: string | null;
}

export interface ReportWriterFormulaDefinition {
  name: string;
  expression: string;
  label?: string | null;
}

export interface ReportWriterFilterDefinition {
  field: string;
  operator?: ReportWriterFilterOperator;
  value?: string | null;
  label?: string | null;
}

export interface ReportWriterGridDefinition {
  gridId: string;
  title: string;
  kind: ReportWriterGridKind;
  rowFields?: string[] | null;
  columnFields?: string[] | null;
  metrics?: ReportWriterMetricDefinition[] | null;
  formulas?: ReportWriterFormulaDefinition[] | null;
  topN?: number | null;
  sortBy?: string | null;
  sortDescending?: boolean;
  filters?: ReportWriterFilterDefinition[] | null;
  formatRules?: ReportWriterFormatRule[] | null;
  chart?: ReportWriterChartDefinition | null;
}

export interface ReportWriterFormatRule {
  column: string;
  operator: ReportWriterFilterOperator;
  style: ReportWriterCellStyle;
  value?: string | null;
  label?: string | null;
}

export interface ReportWriterChartDefinition {
  type: ReportWriterChartType;
  categoryField: string;
  valueColumns: string[];
  title?: string | null;
}

export interface ReportWriterChartSeries {
  key: string;
  label: string;
  values: number[];
}

export interface ReportWriterChartRender {
  type: ReportWriterChartType;
  title: string;
  categoryField: string;
  categories: string[];
  series: ReportWriterChartSeries[];
  warnings: string[];
}

export interface ReportWriterGridColumn {
  key: string;
  label: string;
  role: string;
}

export interface ReportWriterGridRow {
  rowKey: string;
  values: Record<string, string>;
  styleHints?: Record<string, ReportWriterCellStyle> | null;
}

export interface ReportWriterMetricLineage {
  name: string;
  sourceField: string;
  function: ReportWriterAggregateFunction | string;
}

export interface ReportWriterFormulaLineage {
  name: string;
  expression: string;
  sourceFields: string[];
}

export interface ReportWriterFilterLineage {
  field: string;
  operator: ReportWriterFilterOperator | string;
  value?: string | null;
  label?: string | null;
}

export interface ReportWriterGridLineage {
  inputRowCount: number;
  outputRowCount: number;
  sourceFields: string[];
  metrics: ReportWriterMetricLineage[];
  formulas: ReportWriterFormulaLineage[];
  filteredInputRowCount?: number | null;
  filters?: ReportWriterFilterLineage[] | null;
}

export interface ReportWriterGridDataDictionaryField {
  key: string;
  label: string;
  role: string;
  sourceField: string;
  dataType: string;
  isGenerated: boolean;
  description: string;
}

export interface ReportWriterGridValidationCheck {
  checkId: string;
  status: string;
  detail: string;
}

export interface ReportWriterGridRender {
  gridId: string;
  title: string;
  kind: ReportWriterGridKind;
  columns: ReportWriterGridColumn[];
  rows: ReportWriterGridRow[];
  warnings: string[];
  lineage?: ReportWriterGridLineage | null;
  dataDictionary?: ReportWriterGridDataDictionaryField[] | null;
  validationChecks?: ReportWriterGridValidationCheck[] | null;
  chart?: ReportWriterChartRender | null;
}

export interface ReportWriterGridDiffCell {
  column: string;
  priorValue?: string | null;
  currentValue?: string | null;
  delta?: string | null;
  direction: ReportWriterDiffDirection;
}

export interface ReportWriterGridDiffRow {
  rowKey: string;
  state: ReportWriterDiffRowState;
  priorValues: Record<string, string>;
  currentValues: Record<string, string>;
  cells: ReportWriterGridDiffCell[];
}

export interface ReportWriterGridDiff {
  gridId: string;
  title: string;
  columns: ReportWriterGridColumn[];
  rows: ReportWriterGridDiffRow[];
  warnings: string[];
  addedRowCount: number;
  removedRowCount: number;
  changedRowCount: number;
  unchangedRowCount: number;
}

export interface ReportAccessPrincipal {
  kind: ReportAccessPrincipalKind;
  principalId: string;
  displayName?: string | null;
}

export interface ReportAccessPolicy {
  mode: ReportAccessMode;
  ownerPrincipalId?: string | null;
  principals?: ReportAccessPrincipal[] | null;
  companyId?: string | null;
  allowOwnerAccess?: boolean;
}

export interface ReportTemplateDraftRequest {
  name: string;
  displayName: string;
  sections: string[];
  parameters: ReportTemplateParameterDefinition[];
  family?: string | null;
  basedOnVersion?: number | null;
  rationale?: string | null;
  grids?: ReportWriterGridDefinition[] | null;
  accessPolicy?: ReportAccessPolicy | null;
}

export interface RenderReportTemplateRequest {
  templateId: VersionedReportTemplateId;
  parameters: Record<string, string>;
  datasetRows?: Record<string, string>[] | null;
  grids?: ReportWriterGridDefinition[] | null;
}

export interface RenderReportTemplateResponse {
  templateId: VersionedReportTemplateId;
  renderedContent: string;
  missingRequiredParameters: string[];
  grids?: ReportWriterGridRender[] | null;
  warnings?: string[] | null;
}

export interface ReportTemplateDecisionRequest {
  rationale: string;
  approvalReference?: string | null;
}

export interface ReportTemplateAuditEvent {
  at: string;
  actor: string;
  action: string;
  fromStatus: string;
  toStatus: string;
  note: string | null;
}

export interface ReportTemplateDefinition {
  templateId: VersionedReportTemplateId;
  displayName: string;
  parameters: ReportTemplateParameterDefinition[];
  sections: string[] | null;
  grids: ReportWriterGridDefinition[] | null;
  accessPolicy: ReportAccessPolicy | null;
}

export interface ReportTemplateGovernanceRecord {
  definition: ReportTemplateDefinition;
  status: string;
  family: string;
  isBuiltIn: boolean;
  isLatestApproved: boolean;
  createdBy: string;
  createdAt: string;
  updatedBy: string;
  updatedAt: string;
  validationIssues: string[];
  auditTrail: ReportTemplateAuditEvent[];
  submittedBy?: string | null;
  submittedAt?: string | null;
  approvedBy?: string | null;
  approvedAt?: string | null;
  rejectedBy?: string | null;
  rejectedAt?: string | null;
  decisionRationale?: string | null;
  approvalReference?: string | null;
  basedOnTemplateId?: VersionedReportTemplateId | null;
}

export interface ReportingRunStatusProjection {
  runId: string;
  templateId: string;
  family: string;
  status: string;
  trigger: string;
  asOfDate?: string | null;
  attemptCount: number;
  sectionCount: number;
  lineageLinkedSections: number;
  artifacts: string[];
  auditActions: string[];
  failureReason: string | null;
  drilldownLinks?: ReportingRunDrilldownLink[];
  nextActions?: ReportingRunNextAction[];
  generatedReportWriterGrids?: ReportingGeneratedReportWriterGrid[] | null;
  reportWriterDatasetSourceId?: string | null;
  reportWriterDatasetSourceLabel?: string | null;
  reportWriterDatasetRowCount?: number | null;
  brandingThemeId?: string | null;
  brandingTheme?: ReportBrandingTheme | null;
  runSeriesId?: string | null;
  runAttemptOrdinal?: number | null;
  priorRunId?: string | null;
  retryReason?: string | null;
  latestGeneratedRunId?: string | null;
  latestApprovedRunId?: string | null;
  isLatestGenerated?: boolean | null;
  isLatestApproved?: boolean | null;
  comparisonSummary?: string | null;
  changedLineCount?: number | null;
  addedLineCount?: number | null;
  removedLineCount?: number | null;
  resolvedTemplate?: VersionedReportTemplateId | null;
  resolvedParameters?: ReportingRunParameters | null;
  readiness?: ReportingRunReadiness | null;
}

export interface ReportingGeneratedReportWriterGrid {
  gridId: string;
  title: string;
  kind: string;
  artifact: string;
  dimensionCount: number;
  metricCount: number;
  formulaCount: number;
  validationSummary?: string | null;
  validationPassedCount?: number | null;
  validationWarningCount?: number | null;
  validationFailedCount?: number | null;
}

export interface ReportingRunDrilldownLink {
  id: string;
  kind: string;
  label: string;
  href: string;
  method: string;
  isBrowserNavigable: boolean;
  source: string;
}

export interface ReportingRunNextAction {
  id: string;
  kind: string;
  label: string;
  href: string;
  method: string;
  isEnabled: boolean;
  disabledReason: string | null;
  isBrowserNavigable: boolean;
}

export interface ReportPackDistributionRecord {
  distributionId: string;
  recipient: string;
  recipientRole: string;
  channel: string;
  state: string;
  pendingItems: number;
  pendingSummary: string;
  owner: string;
  dueAtUtc: string | null;
  lastSentAtUtc: string | null;
  route: string;
}

export type GovernanceReportKind =
  | "TrialBalance"
  | "NavSummary"
  | "AssetAllocation"
  | "ReconciliationPack"
  | "PerformanceReport"
  | "HoldingsReport"
  | "CapitalAccountStatement"
  | "InvestorStatement"
  | "BoardPacket"
  | "AuditPackage"
  | "CertifiedDataset"
  | "CustomReport";
export type GovernanceReportArtifactFormat = "Json" | "Csv" | "Xlsx" | "Html" | "Pdf";
export type GovernanceReportPackStatus =
  | "Unknown"
  | "Draft"
  | "Generated"
  | "Validated"
  | "ReviewRequired"
  | "Approved"
  | "Rejected"
  | "Exported"
  | "Retained"
  | "Superseded"
  | "Restated"
  | "InReview"
  | "Published";
export type ReportPackDeliveryMode = "EmailLink" | "SecurePortal" | "EvidenceVault" | "InternalRoute";

export interface FundReportPackGenerateRequest {
  fundProfileId: string;
  auditActor: string;
  reportKind?: GovernanceReportKind;
  asOf?: string | null;
  currency?: string | null;
  correlationId?: string | null;
  decisionRationale?: string | null;
  formats?: GovernanceReportArtifactFormat[] | null;
  expectedSchemaVersion?: number | null;
  brandingThemeId?: string | null;
  brandingThemeOverride?: ReportBrandingTheme | null;
}

export interface FundReportPackPreviewRequest {
  fundProfileId: string;
  reportKind?: GovernanceReportKind;
  asOf?: string | null;
  currency?: string | null;
  brandingThemeId?: string | null;
  brandingThemeOverride?: ReportBrandingTheme | null;
}

export interface FundReportAssetClassSection {
  assetClass: string;
  total: number;
}

export interface FundReportPackPreview {
  reportId: string;
  fundProfileId: string;
  displayName: string;
  reportKind: GovernanceReportKind;
  currency: string;
  asOf: string;
  generatedAt: string;
  totalNetAssets: number;
  trialBalanceLineCount: number;
  assetClassSectionCount: number;
  assetClassSections: FundReportAssetClassSection[];
  brandingTheme?: ReportBrandingTheme | null;
}

export interface FundReportPackArtifact {
  artifactKind: string;
  format: GovernanceReportArtifactFormat;
  relativePath: string;
  sizeBytes: number;
  checksumSha256: string;
  schemaVersion: number;
}

export interface FundReportPackSnapshot {
  reportId: string;
  fundProfileId: string;
  displayName: string;
  reportKind: GovernanceReportKind;
  currency: string;
  asOf: string;
  generatedAt: string;
  totalNetAssets: number;
  auditActor: string;
  correlationId: string;
  decisionRationale: string | null;
  provenance: unknown;
  artifacts: FundReportPackArtifact[];
  warnings: string[];
  contractName: string;
  schemaVersion: number;
  brandingTheme: ReportBrandingTheme | null;
  status: GovernanceReportPackStatus;
  validationIssues: unknown[];
  lifecycleEvents: unknown[];
  auditPackReadiness: unknown | null;
}

export interface ReportPackDeliveryArtifact {
  format: GovernanceReportArtifactFormat;
  artifactName: string;
  contentType: string;
  retainedPath: string;
  byteSize: number;
  evidenceId: string;
  checksumSha256?: string | null;
  versionStamp?: string | null;
  downloadRoute?: string | null;
}

export interface ReportPackDeliveryAccessLink {
  kind: string;
  label: string;
  href: string;
  requiresToken: boolean;
  expiresAtUtc?: string | null;
  description?: string | null;
}

export interface ReportPackDeliveryNotification {
  notificationId: string;
  channel: string;
  recipient: string;
  recipientRole: string;
  deliveryMode: ReportPackDeliveryMode;
  subject: string;
  body: string;
  href: string;
  requiresToken: boolean;
  createdAtUtc: string;
  expiresAtUtc?: string | null;
  status: string;
}

export interface ReportPackDeliveryRecipient {
  distributionId: string;
  recipient: string;
  recipientRole: string;
  channel: string;
}

export interface ReportPackDeliveryApprovalStep {
  at: string;
  actor: string;
  action: string;
  fromState: string;
  toState: string;
  note?: string | null;
}

export interface ReportPackDeliveryEvidencePacket {
  packetId: string;
  packetKind: string;
  packageId: string;
  reportId: string;
  fundProfileId: string;
  fundAccountId: string;
  period: string;
  packageContents: string[];
  supportEvidenceIds: string[];
  recipientList: ReportPackDeliveryRecipient[];
  entitlementScope: string;
  approvalChain: ReportPackDeliveryApprovalStep[];
  datasetVersion: string;
  templateVersion: string;
  deliveryChannel: string;
  deliveredAtUtc: string;
  deliveryEvidence: ReportingWorkflowEvidenceLink[];
  requestHistory: string[];
  amendmentReason?: string | null;
  restatementLineage?: string | null;
  auditEventReferences?: string[] | null;
  blockedDownstreamOutputs?: string[] | null;
}

export interface ReportPackDeliveryPackage {
  packageId: string;
  reportId: string;
  distributionId: string;
  deliveryMode: ReportPackDeliveryMode;
  secureLink: string;
  portalRoute: string;
  formats: GovernanceReportArtifactFormat[];
  artifacts: ReportPackDeliveryArtifact[];
  createdAtUtc: string;
  retainedManifestPath: string;
  publicationEvidenceHash?: string | null;
  integritySummary?: string | null;
  reportingRunId?: string | null;
  reportingTemplateId?: string | null;
  reportingScheduleId?: string | null;
  reportingRunAsOfDate?: string | null;
  reportingRunStatus?: string | null;
  reportingRunTrigger?: string | null;
  reportingRunAttemptCount?: number | null;
  reportingRunSectionCount?: number | null;
  reportingRunLineageLinkedSections?: number | null;
  deliveryAccessSummary?: string | null;
  deliveryChannelSummary?: string | null;
  downloadSummary?: string | null;
  accessExpiresAtUtc?: string | null;
  accessLinks?: ReportPackDeliveryAccessLink[] | null;
  notifications?: ReportPackDeliveryNotification[] | null;
  sourceArtifacts?: string[] | null;
  generatedReportWriterGrids?: ReportingGeneratedReportWriterGrid[] | null;
  renderedReportWriterGrids?: ReportWriterGridRender[] | null;
  reportWriterDatasetSourceId?: string | null;
  reportWriterDatasetSourceLabel?: string | null;
  reportWriterDatasetRowCount?: number | null;
  lineProvenance?: ReportingWorkflowLineProvenance[] | null;
  deliveryEvidencePacket?: ReportPackDeliveryEvidencePacket | null;
  brandingTheme?: ReportBrandingTheme | null;
}

export interface ReportPackDeliveryAttempt {
  attemptId: string;
  reportId: string;
  distributionId: string;
  recipient: string;
  recipientRole: string;
  channel: string;
  state: string;
  attemptedAtUtc: string;
  actor: string;
  attemptNumber: number;
  deliveryReference: string;
  note: string | null;
  failureReason: string | null;
  evidenceLinks: ReportingWorkflowEvidenceLink[] | null;
  package: ReportPackDeliveryPackage | null;
}

export interface WorkstationReportingDeliveryReceipt {
  receiptId: string;
  kind: string;
  occurredAtUtc: string;
  transportId: string;
  providerReference?: string | null;
  evidenceReference?: string | null;
  detail?: string | null;
}

export interface WorkstationReportingDelivery {
  jobId: string;
  runId: string;
  packageId: string;
  releaseReceiptId: string;
  releaseVersion: string;
  artifactManifestHashSha256: string;
  distributionId: string;
  transportId: string;
  recipient: string;
  recipientRole: string;
  destination: string;
  state: string;
  attemptCount: number;
  maxAttempts: number;
  createdAtUtc: string;
  updatedAtUtc: string;
  nextAttemptAtUtc: string | null;
  requestedBy: string;
  lastErrorCode: string | null;
  lastError: string | null;
  providerMessageId: string | null;
  accessGrantId: string | null;
  receipts: WorkstationReportingDeliveryReceipt[];
}

export interface WorkstationReportingHistory {
  runs: ReportingRunStatusProjection[];
  deliveries: WorkstationReportingDelivery[];
  limit: number;
  generatedAtUtc: string;
}

export interface ReportPackDeliveryRequest {
  distributionId: string;
  actor?: string | null;
  deliveryReference?: string | null;
  note?: string | null;
  evidenceLinks?: ReportingWorkflowEvidenceLink[] | null;
  formats?: GovernanceReportArtifactFormat[] | null;
  deliveryMode?: ReportPackDeliveryMode | null;
  actionOrigin?: OperationsActionOrigin | null;
}

export interface ReportPackDeliveryFailureRequest extends ReportPackDeliveryRequest {
  failureReason: string;
}

export interface ReportPackDeliveryHistory {
  reportId: string;
  attempts: ReportPackDeliveryAttempt[];
}

export interface ReportingScheduleDeliveryTarget {
  distributionId: string;
  formats?: GovernanceReportArtifactFormat[] | null;
  deliveryMode?: ReportPackDeliveryMode | null;
  note?: string | null;
  recipientPrincipalId?: string | null;
  recipientPrincipalKind?: "User" | "Group" | "Company" | null;
}

export interface ReportingScheduleDeliveryPlan {
  planId: string;
  scheduleId: string;
  templateId: string;
  distributionId: string;
  recipient: string;
  recipientRole: string;
  channel: string;
  deliveryMode: ReportPackDeliveryMode;
  formats: GovernanceReportArtifactFormat[];
  isReady: boolean;
  readinessSummary: string;
  route: string;
  dueAtUtc: string;
  nextAsOfDate: string;
  owner: string;
  note: string | null;
  lastDeliveryAttemptId: string | null;
  lastDeliveryState: string | null;
  lastDeliveryAtUtc: string | null;
  lastDeliveryPackageRoute: string | null;
  lastDeliverySecureLink: string | null;
  lastDeliveryAccessLinks?: ReportPackDeliveryAccessLink[] | null;
  lastDeliveryAccessExpiresAtUtc?: string | null;
  lastDeliveryAccessSummary?: string | null;
  lastDeliveryChannelSummary?: string | null;
  lastDeliveryDownloadSummary?: string | null;
  lastDeliveryNotificationCount?: number;
  lastDeliveryNotificationSummary?: string | null;
  lastDeliveryGeneratedReportWriterGridCount?: number;
  lastDeliveryRenderedReportWriterGridCount?: number;
  lastDeliveryReportWriterDatasetSummary?: string | null;
  lastDeliveryReportWriterGridSummary?: string | null;
  versionStamp: string | null;
  lastDeliveryArtifactCount?: number;
  lastDeliveryIntegritySummary?: string | null;
  readinessBlockers?: string[] | null;
  brandingThemeId?: string | null;
  brandingTheme?: ReportBrandingTheme | null;
  lastDeliveryEntitlementScope?: string | null;
}

export type StructuredReportingExportPurpose = "Regulatory" | "DataWarehouse" | "InvestmentDecision";

export interface StructuredReportingExport {
  exportId: string;
  label: string;
  purpose: StructuredReportingExportPurpose;
  format: GovernanceReportArtifactFormat;
  dataset: string;
  consumer: string;
  schemaVersion: number;
  rowCount: number;
  fieldCount: number;
  sourceCount: number;
  currency: string;
  asOf: string;
  isReady: boolean;
  retainedPath: string;
  route: string;
  dataDictionaryRoute: string | null;
  validationSummary: string | null;
  evidenceRoute: string | null;
  versionStamp: string | null;
  tags: string[] | null;
  readinessBlockers?: string[] | null;
  retainedManifestPath?: string | null;
  integrityHashSha256?: string | null;
  integritySummary?: string | null;
  rowLineageCount?: number | null;
}

export interface StructuredReportingExportColumn {
  name: string;
  dataType: string;
  description: string | null;
}

export interface StructuredReportingExportDataDictionaryField {
  name: string;
  dataType: string;
  description: string | null;
  ordinal: number;
  required: boolean;
}

export interface StructuredReportingExportValidationCheck {
  checkId: string;
  status: string;
  detail: string;
}

export interface StructuredReportingExportRowLineage {
  rowNumber: number;
  rowKey: string;
  rowHashSha256: string;
}

export interface StructuredReportingExportPayload {
  export: StructuredReportingExport;
  columns: StructuredReportingExportColumn[];
  rows: Record<string, string | null>[];
  warnings: string[];
  generatedAtUtc: string;
  dataDictionary?: StructuredReportingExportDataDictionaryField[] | null;
  validationChecks?: StructuredReportingExportValidationCheck[] | null;
  generatedByPrincipalId?: string | null;
  generatedForCompanyId?: string | null;
  generatedForGroupPrincipalIds?: string[] | null;
  rowLineage?: StructuredReportingExportRowLineage[] | null;
}

export type ReportingScheduledReleaseHandoffState = "PendingRelease" | "Enqueued";

export interface ReportingScheduledReleaseHandoff {
  handoffId: string;
  tenantId: string;
  companyId: string;
  scheduleId: string;
  runId: string;
  templateId: string;
  distributionId: string;
  targetDistributionId: string;
  transportId: string;
  recipientPrincipalId: string | null;
  recipientPrincipalKind?: "User" | "Group" | "Company" | null;
  destination: string;
  subject: string;
  body: string;
  requestedFormats: GovernanceReportArtifactFormat[] | null;
  artifactIds: string[] | null;
  grantLifetimeSeconds: number | null;
  grantMaxUses: number | null;
  maxAttempts: number;
  createdAtUtc: string;
  state: ReportingScheduledReleaseHandoffState;
  enqueuedDeliveryJobId: string | null;
  enqueuedAtUtc: string | null;
  deliveryTargetsSnapshotHash?: string | null;
}

export interface ReportingScheduleRecord {
  scheduleId: string;
  templateId: string;
  cronExpression: string;
  nextAsOfDate: string;
  dueAtUtc: string;
  maxRetries: number;
  requestedBy: string;
  state: string;
  createdAtUtc: string;
  updatedAtUtc: string;
  lastRunAtUtc: string | null;
  lastRunId: string | null;
  runCount: number;
  description: string | null;
  deliveryTargets?: ReportingScheduleDeliveryTarget[] | null;
  datasetRows?: Record<string, string>[] | null;
  datasetSourceId?: string | null;
  brandingThemeId?: string | null;
  brandingThemeOverride?: ReportBrandingTheme | null;
  template?: VersionedReportTemplateId | null;
  runParameters?: ReportingRunParameters | null;
  tenantId?: string | null;
  companyId?: string | null;
  accessPolicySnapshot?: ReportAccessPolicy | null;
  lastReadiness?: ReportingRunReadiness | null;
  releaseDeliveryHandoffs?: ReportingScheduledReleaseHandoff[] | null;
  accessPolicySnapshotHash?: string | null;
  deliveryTargetsSnapshotHash?: string | null;
}

export interface ReportingScheduleUpsertRequest {
  scheduleId: string;
  templateId: string;
  cronExpression: string;
  nextAsOfDate: string;
  dueAtUtc: string;
  maxRetries: number;
  requestedBy: string;
  description?: string | null;
  state?: string;
  deliveryTargets?: ReportingScheduleDeliveryTarget[] | null;
  datasetRows?: Record<string, string>[] | null;
  datasetSourceId?: string | null;
  brandingThemeId?: string | null;
  brandingThemeOverride?: ReportBrandingTheme | null;
  template?: VersionedReportTemplateId | null;
  runParameters?: ReportingRunParameters | null;
}

export interface ReportingScheduleRunResult {
  schedule: ReportingScheduleRecord;
  run: ReportingRunStatusProjection;
  deliveryAttempts?: ReportPackDeliveryAttempt[] | null;
  deliveryWarnings?: string[] | null;
}

export interface ReportingDueScheduleRunResult {
  evaluatedAtUtc: string;
  runs: ReportingScheduleRunResult[];
}

export interface ReportingStarterSeedSchedule {
  scheduleId: string;
  templateId: string;
  cronExpression: string;
  cadence: string;
  description: string;
  state?: ReportingScheduleRecord["state"] | string;
  defaultPeriod?: string | null;
  deliveryTargets?: ReportingScheduleDeliveryTarget[] | null;
}

export interface ReportingStarterKit {
  kitId: string;
  archetype: string;
  displayName: string;
  description: string;
  templateIds: string[];
  defaultLayoutId: string;
  defaultPeriod: string;
  seedSchedules: ReportingStarterSeedSchedule[];
}

export interface ReportingStarterKitState {
  isProvisioned: boolean;
  selectedKitId?: string | null;
  archetype?: string | null;
  enabledTemplateIds: string[];
  defaultLayoutId?: string | null;
  defaultPeriod?: string | null;
  seedScheduleIds: string[];
  provisionedAtUtc?: string | null;
  provisionedBy?: string | null;
}

export interface ReportingStarterKitProvisionResult {
  kit: ReportingStarterKit;
  state: ReportingStarterKitState;
  seededSchedules: ReportingScheduleRecord[];
}

export interface ReportingRunRequest {
  templateId: string;
  asOfDate?: string | null;
  maxRetries?: number;
  jobId?: string | null;
  requestedBy?: string | null;
  datasetRows?: Record<string, string>[] | null;
  datasetSourceId?: string | null;
  retryReason?: string | null;
  allowRestatement?: boolean;
  template?: VersionedReportTemplateId | null;
  parameters?: ReportingRunParameters | null;
}

export type ReportingEntityScopeKind = "AllEntities" | "Entity" | "Portfolio" | "Investor";
export type ReportingAccountingBasis = "Gaap" | "Tax" | "Management" | "Cash" | "Statutory";
export type ReportingConsolidationLevel = "Fund" | "Entity" | "Portfolio" | "Investor";
export type ReportingOutputFormat = "Pdf" | "Xlsx" | "Csv" | "EvidenceVault" | "ClientPackage";
export type ReportingFinality = "Draft" | "Final";
export type ReportingRunReadinessStatus = "Ready" | "ReviewRequired" | "Blocked" | "Unavailable";

export interface ReportingRunScope {
  fundProfileId: string;
  entityScopeKind: ReportingEntityScopeKind;
  entityId?: string | null;
  portfolioId?: string | null;
  investorId?: string | null;
  dimensions?: import("./workstation-2").LedgerDimensionSet | null;
}

export interface ReportingLedgerBookSelection {
  ledgerBookId?: string | null;
  ledgerBookCode?: string | null;
}

export interface ReportingRunParameters {
  scope: ReportingRunScope;
  periodId: string;
  asOfDate: string;
  ledgerBook: ReportingLedgerBookSelection;
  accountingBasis: ReportingAccountingBasis;
  presentationCurrency: string;
  consolidationLevel: ReportingConsolidationLevel;
  outputFormat: ReportingOutputFormat;
  finality: ReportingFinality;
  includeSupportingSchedules: boolean;
  includeEvidenceAppendix: boolean;
  templateParameters: Record<string, string>;
}

export interface ReportingRunReadinessCheck {
  checkId: string;
  label: string;
  status: ReportingRunReadinessStatus;
  summary: string;
  issueCount: number;
  blocksDraft: boolean;
  blocksFinal: boolean;
  route?: string | null;
  evidenceReferences: string[];
}

export interface ReportingRunReadiness {
  evaluationId: string;
  evaluatedAtUtc: string;
  resolvedTemplate: VersionedReportTemplateId;
  resolvedParameters: ReportingRunParameters;
  status: ReportingRunReadinessStatus;
  canGenerateDraft: boolean;
  canGenerateFinal: boolean;
  checks: ReportingRunReadinessCheck[];
  blockingReasons: string[];
  evidenceHash: string;
}

export interface ReportingRunResult {
  run: ReportingRunStatusProjection;
}

export interface ReportingRunAuditEntry {
  runId: string;
  timestampUtc: string;
  action: string;
  actor: string;
  notes: string;
}

export interface ReportingRunAuditTrail {
  runId: string;
  templateId: string;
  asOfDate: string;
  status: string;
  trigger: string;
  attemptCount: number;
  entries: ReportingRunAuditEntry[];
  reportWriterDatasetSourceId?: string | null;
  reportWriterDatasetSourceLabel?: string | null;
  reportWriterDatasetRowCount?: number | null;
}

export interface ReportingWorkflowEvidenceLink {
  evidenceId: string;
  label: string;
  route: string | null;
  source: string | null;
  capturedAtUtc: string | null;
}

export interface ReportingWorkflowAuditEntry {
  at: string;
  actor: string;
  action: string;
  fromState: string | null;
  toState: string | null;
  note: string | null;
}

export interface ReportingWorkflowChangedLine {
  lineKey: string;
  previousValue: string;
  currentValue: string;
  evidenceLinks: ReportingWorkflowEvidenceLink[] | null;
}

export interface ReportingWorkflowRestatement {
  reasonCode: string;
  approver: string | null;
  priorVersionReportId: string | null;
  changedLines: ReportingWorkflowChangedLine[];
  evidenceLinks: ReportingWorkflowEvidenceLink[] | null;
}

export interface ReportingWorkflowLineProvenance {
  lineKey: string;
  sourceKind: string;
  sourceId: string;
  evidenceId: string | null;
  runId: string | null;
  ledgerEntryId: string | null;
  reconciliationCaseId: string | null;
  reportValue: string | null;
  sourceSessionId: string | null;
  reconciliationRunId: string | null;
  providerEventId?: string | null;
  securityMasterId?: string | null;
  securityDefinitionId?: string | null;
  reconciliationOutcome?: string | null;
  approvalId?: string | null;
  financialRecordExplorerId?: string | null;
  financialRecordHref?: string | null;
}

export interface ReportingWorkflowPublication {
  manifestId: string;
  retainedManifestPath: string;
  evidenceHash: string;
  signedOffBy: string;
  signedOffAt: string;
  evidenceLinks: ReportingWorkflowEvidenceLink[] | null;
  brandingTheme?: ReportBrandingTheme | null;
  signedOffRole?: string | null;
  signOffReason?: string | null;
  signOffContext?: string | null;
  actionOrigin?: string | null;
}

export interface ReportingWorkflowRecord {
  reportId: string;
  fundProfileId: string;
  fundAccountId: string;
  period: string;
  templateId: { name: string; version: number };
  state: string;
  version: number;
  createdAt: string;
  createdBy: string;
  updatedAt: string;
  auditTrail: ReportingWorkflowAuditEntry[];
  restatement: ReportingWorkflowRestatement | null;
  lineProvenance: ReportingWorkflowLineProvenance[];
  publication: ReportingWorkflowPublication | null;
}

export interface ReportingAccessAuditSummary {
  evaluationScope: string;
  summary: string;
  principalScopes: string[];
  visibleTemplateCount: number;
  hiddenTemplateCount: number;
  visibleReportPackCount: number;
  hiddenReportPackCount: number;
  visibleScheduleCount: number;
  hiddenScheduleCount: number;
  visibleDeliveryAttemptCount: number;
  hiddenDeliveryAttemptCount: number;
  visibleStructuredExportCount: number;
  hiddenStructuredExportCount: number;
  denialReasons: string[];
}

export interface ReportingDeploymentComponent {
  componentId: string;
  displayName: string;
  isReady: boolean;
  summary: string;
}

export interface ReportingDeploymentCapability {
  isReady: boolean;
  durableGovernance: boolean;
  durableArtifacts: boolean;
  durableReconciliationEvidence: boolean;
  durableRuns: boolean;
  durableScheduling: boolean;
  durableDelivery: boolean;
  recipientDestinationsConfigured: boolean;
  clientDocumentsConfigured: boolean;
  migrationsManaged: boolean;
  components: ReportingDeploymentComponent[];
  blockingReasons: string[];
}

export interface ReportingDailyWorkItem {
  workItemId: string;
  kind: string;
  title: string;
  statusLabel: string;
  detail: string;
  tone: string;
  owner: string;
  dueAtUtc: string | null;
  primaryActionLabel: string;
  primaryActionHref: string | null;
  evidenceGaps?: string[] | null;
  context?: string[] | null;
  secondaryActionLabel?: string | null;
  secondaryActionHref?: string | null;
}

export interface AccountingReportingSummary {
  profileCount: number;
  fundProfileId?: string | null;
  selectedFundProfileId?: string | null;
  recommendedProfiles: string[];
  profiles: AccountingReportingProfile[];
  reportPackDistributions?: ReportPackDistributionRecord[];
  reportPackTargets?: string[];
  summary: string;
  templates?: ReportingTemplateMetadata[];
  recentRuns?: ReportingRunStatusProjection[];
  workflowRecords?: ReportingWorkflowRecord[];
  schedules?: ReportingScheduleRecord[];
  /** Compatibility-only file-backed delivery records; empty in production composition. */
  deliveryAttempts?: ReportPackDeliveryAttempt[];
  canonicalDeliveries?: WorkstationReportingDelivery[];
  scheduleDeliveryPlans?: ReportingScheduleDeliveryPlan[];
  portfolioCuts?: PortfolioReportingCut[];
  structuredExports?: StructuredReportingExport[];
  brandingThemes?: ReportBrandingTheme[];
  reportWriterDatasetSources?: ReportWriterDatasetSource[];
  dailyWork?: ReportingDailyWorkItem[];
  starterKits?: ReportingStarterKit[] | null;
  starterKitState?: ReportingStarterKitState | null;
  livePortfolioViews?: PortfolioReportingLiveView[];
  crossFundConsolidations?: CrossFundReportingConsolidation[];
  pnlSlices?: PortfolioReportingPnlSlice[];
  analyticsRows?: PortfolioReportingAnalyticsRow[];
  reportLineProvenanceExplorer?: FinancialRecordExplorerDto | null;
  accessAudit?: ReportingAccessAuditSummary | null;
  deploymentCapability?: ReportingDeploymentCapability | null;
}

export type GovernanceCashFlowSummary = AccountingCashFlowSummary;
export type GovernanceReportingProfile = AccountingReportingProfile;
export type GovernanceReportingSummary = AccountingReportingSummary;

export interface AccountingWorkspaceResponse {
  metrics: MetricSnapshot[];
  reconciliationQueue: AccountingReconciliationRecord[];
  breakQueue: ReconciliationBreakQueueItem[];
  cashFlow: AccountingCashFlowSummary;
  reporting: AccountingReportingSummary;
  manualJournalWorkbench?: ManualJournalEntryWorkbench | null;
  controlCenter?: {
    closeReadiness: string;
    portfolioFilterOptions: string[];
    accountFilterOptions: string[];
    blockerSeverityDistribution: { severity: string; count: number }[];
    agingCurves: { bucket: string; count: number }[];
    ownerWorkload: { owner: string; openCount: number }[];
    slaBreachCount: number;
    trendSnapshots: { metric: string; value: number; trend: string }[];
    drillLinks: { label: string; href: string }[];
    alerts: { tone: "danger" | "warning" | "info"; message: string }[];
  };
}

export type GovernanceWorkspaceResponse = AccountingWorkspaceResponse;
export type ReportingWorkspaceResponse = AccountingReportingSummary;

export interface ExportAnalysisResult {
  jobId: string | null;
  success: boolean;
  status: string;
  profileId: string;
  symbols: string[] | null;
  filesGenerated: number;
  totalRecords: number;
  totalBytes: number;
  outputDirectory: string | null;
  durationSeconds: number;
  error: string | null;
  warnings: string[] | null;
  files: ExportAnalysisFile[];
  timestamp: string;
}

export interface ExportAnalysisFile {
  path: string;
  symbol: string | null;
  format: string | null;
  sizeBytes: number;
  recordCount: number;
}


export type ReconciliationBreakQueueStatus = "Open" | "InReview" | "Resolved" | "Dismissed" | "SignedOff";
export type ReconciliationCaseLifecycleState = "Open" | "InReview" | "AwaitingApproval" | "Approved" | "Posted" | "Reopened" | "Superseded" | "Investigating" | "AwaitingEvidence" | "Resolved" | "SignedOff";
export type ReconciliationCasePriority = "Low" | "Normal" | "High" | "Critical";
export type ReconciliationCaseSlaState = "NotStarted" | "OnTrack" | "Warning" | "Breached" | "Paused" | "Stopped";
export type ReconciliationCaseCommentVisibility = "Internal" | "CloseEvidence" | "ExternalSummary";
export type ReconciliationCaseworkAction = "Assign" | "ChangePriority" | "TransitionStatus" | "AddComment" | "EditComment" | "DeleteComment" | "SetRootCause" | "SetResolution" | "LinkEvidence" | "SignOff" | "Reopen" | "Resolve" | "Waive" | "Supersede";
export type ReconciliationBreakMeasureKind = "Value" | "Quantity" | "CostBasis";
export type ReconciliationBreakDisposition = "Resolved" | "Waived" | "Superseded";

export interface ReconciliationBreakMeasure {
  kind: ReconciliationBreakMeasureKind;
  expected: number | null;
  actual: number | null;
  variance: number | null;
  tolerance: number | null;
  unit: string;
  unavailableReason?: string | null;
}

export interface ReconciliationBreakExplanation {
  summary: string;
  sourceSystems: string[];
  probableCause: string;
  ledgerImpact: string;
  suggestedNextAction: string;
  evidenceLinks: string[];
}
