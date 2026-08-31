import type { BiasDisclosure } from "./workstation-6";
import type {
  CorporateAction,
  InstrumentPassportEconomicDefinition,
  InstrumentPassportIdentifierSummary,
  InstrumentPassportOperationsHandoff,
  InstrumentPassportOperationsWorkbenchPanel,
  InstrumentPassportProviderConfidence,
  InstrumentPassportReferenceDataWorkbenchSection,
  InstrumentPassportTrustPosture,
  InstrumentPassportUsage,
  SecurityIdentityDrillIn,
  TradingParameters,
} from "../types";

export interface InstrumentPassportOperationsReadiness {
  readinessId: string;
  label: string;
  status: string;
  isReady: boolean;
  summary: string;
  evidenceCount: number;
  blockingIssueCount: number;
  nextAction: string;
  route?: string | null;
}

export interface InstrumentPassportOperationsWorkbench {
  status: string;
  summary: string;
  panels: InstrumentPassportOperationsWorkbenchPanel[];
  readiness: InstrumentPassportOperationsReadiness[];
  handoffs: InstrumentPassportOperationsHandoff[];
}

export interface InstrumentPassportClassificationProfile {
  instrumentType: string;
  displayName: string;
  securityMasterAssetClass: string;
  assetFamily?: string | null;
  subType?: string | null;
  defaultProviderSecurityType: string;
  isTradeable: boolean;
  isReferenceOnly: boolean;
  isDerivative: boolean;
  requiresUnderlying: boolean;
  producesCashFlows: boolean;
  requiresLotTracking: boolean;
  settlementModel: string;
  compatibleSecurityMasterAssetClasses: string[];
  preferredIdentifierKinds: string[];
  requiredEconomicTerms: string[];
  providerCapabilities: string[];
  lifecycleEvents: string[];
  validationRules: string[];
  ledgerBehaviorHints: string[];
  riskModelHints: string[];
  summary: string;
}

export interface SecurityMasterOperatingModelStage {
  stageId: string;
  title: string;
  status: string;
  summary: string;
  evidenceCount: number;
  blockingIssueCount: number;
}

export interface SecurityMasterEntitlementApplicability {
  entitlementId: string;
  vendorName: string;
  dataType: string;
  scope: string;
  clientId?: string | null;
  accountId?: string | null;
  fundProfileId?: string | null;
  securityId?: string | null;
  isApplicable: boolean;
  isMostSpecific: boolean;
  status: string;
  requiresDirectClientContract: boolean;
  contractReference?: string | null;
  summary: string;
}

export interface SecurityMasterOperatorMetadata {
  metadataId: string;
  vendorName: string;
  dataType: string;
  sourceCategory: string;
  expectedRefreshCadence: string;
  defaultMaxDaysStale?: number | null;
  requiresDirectClientContract: boolean;
  operatorMetadata?: string | null;
  summary: string;
}

export interface SecurityMasterManualChangeApprovalPosture {
  policyKey: string;
  gate: string;
  route: string;
  requiredPermission: string;
  requiredDistinctApprovals: number;
  requiresIndependentReviewer: boolean;
  evidenceRequirement: string;
  status: string;
  manualChangeCount: number;
  unapprovedManualChangeCount: number;
  summary: string;
}

export interface SecurityMasterOperatingModel {
  securityId: string;
  clientId?: string | null;
  accountId?: string | null;
  fundProfileId?: string | null;
  status: string;
  summary: string;
  stages: SecurityMasterOperatingModelStage[];
  entitlementApplicability: SecurityMasterEntitlementApplicability[];
  operatorMetadata: SecurityMasterOperatorMetadata[];
  manualChangeApproval: SecurityMasterManualChangeApprovalPosture;
  controls: InstrumentPassportReferenceDataWorkbenchSection[];
  retrievedAtUtc: string;
}

export interface InstrumentPassportReferenceDataWorkbench {
  status: string;
  summary: string;
  sections: InstrumentPassportReferenceDataWorkbenchSection[];
  operationsHandoffs: InstrumentPassportOperationsHandoff[];
}

export interface InstrumentPassportPricing {
  status: string;
  summary: string;
  tradingParameters: TradingParameters | null;
  lotSize: number | null;
  tickSize: number | null;
  contractMultiplier: number | null;
  tradingHoursUtc: string | null;
  circuitBreakerThresholdPct: number | null;
}

export interface InstrumentPassport {
  securityId: string;
  identity: SecurityIdentityDrillIn;
  economicDefinition: InstrumentPassportEconomicDefinition;
  identifierSummary: InstrumentPassportIdentifierSummary;
  providerMappings: Record<string, unknown>[];
  lifecycleEvents: Record<string, unknown>[];
  corporateActions: CorporateAction[];
  pricing: InstrumentPassportPricing;
  usage: InstrumentPassportUsage;
  trustPosture: InstrumentPassportTrustPosture;
  retrievedAtUtc: string;
  providerConfidence: InstrumentPassportProviderConfidence[];
  referenceDataWorkbench?: InstrumentPassportReferenceDataWorkbench | null;
  operatingModel?: SecurityMasterOperatingModel | null;
  operationsWorkbench?: InstrumentPassportOperationsWorkbench | null;
  classificationProfile?: InstrumentPassportClassificationProfile | null;
}

export type QuantPlotKind =
  | "Line"
  | "MultiLine"
  | "CumulativeReturn"
  | "Drawdown"
  | "Heatmap"
  | "Candlestick"
  | "Bar"
  | "Scatter"
  | "Histogram";

export interface QuantPlotPoint {
  date: string;
  value: number;
}

export interface QuantPlotSeries {
  label: string;
  values: QuantPlotPoint[];
}

export interface QuantPlotBar {
  date: string;
  open: number;
  high: number;
  low: number;
  close: number;
  volume: number;
}

export interface QuantPlot {
  title: string;
  type: QuantPlotKind;
  series: QuantPlotPoint[] | null;
  multiSeries: QuantPlotSeries[] | null;
  candlestick: QuantPlotBar[] | null;
  heatmapData: number[][] | null;
  heatmapLabels: string[] | null;
}

export interface QuantDiagnostic {
  severity: string;
  message: string;
  line: number;
  column: number;
}

export interface QuantMetric {
  label: string;
  value: string;
}

export interface QuantTrade {
  timestamp: string;
  symbol: string;
  side: string;
  quantity: number;
  price: number;
  commission: number;
}

export interface QuantParameter {
  name: string;
  label: string;
  typeName: string;
  defaultValue: string | null;
  min: number | null;
  max: number | null;
  description: string | null;
}

export interface QuantRunResponse {
  success: boolean;
  elapsedMs: number;
  compileTimeMs: number;
  peakMemoryBytes: number;
  runtimeError: string | null;
  consoleOutput: string;
  compilationErrors: QuantDiagnostic[];
  runtimeDiagnostics: QuantDiagnostic[];
  metrics: QuantMetric[];
  plots: QuantPlot[];
  trades: QuantTrade[];
  runtimeParameters: QuantParameter[];
}

export interface QuantParametersResponse {
  parameters: QuantParameter[];
}

export interface QuantTemplate {
  id: string;
  title: string;
  description: string;
  source: string;
}

export interface QuantTemplatesResponse {
  templates: QuantTemplate[];
}

export interface QuantRunRequest {
  source: string;
  parameters: Record<string, string | number | boolean | null>;
}

// --- Strategy Designer ---

export interface StrategyDesignDocument {
  documentId: string;
  name: string;
  description: string;
  version: string;
  datasetReference: string;
  universe: string[];
  cells: StrategyDesignCell[];
  transitions: StrategyDesignTransition[];
  metadata?: Record<string, string> | null;
  createdAt: string;
  updatedAt: string;
}

export interface StrategyDesignCell {
  cellId: string;
  label: string;
  kind: string;
  purpose: string;
  source: string;
  fieldRefs: string[];
  parameters?: Record<string, string> | null;
  disabledReason?: string | null;
}

export interface StrategyDesignTransition {
  transitionId: string;
  fromCellId: string;
  toCellId: string;
  kind: string;
  condition: string;
  maxIterations?: number | null;
  rationale?: string | null;
}

export interface StrategyDesignFieldCatalogItem {
  fieldId: string;
  label: string;
  source: string;
  dataSet: string;
  typeName: string;
  description: string;
  isEnabled: boolean;
  disabledReason: string | null;
  synonyms: string[];
}

export interface StrategyDesignTemplate {
  templateId: string;
  name: string;
  description: string;
  category: string;
  sourcePrototype: string;
  tags: string[];
  document: StrategyDesignDocument;
}

export interface StrategyDesignValidationMessage {
  code: string;
  severity: string;
  targetId: string;
  message: string;
}

export interface StrategyDesignValidationResult {
  isValid: boolean;
  summary: string;
  messages: StrategyDesignValidationMessage[];
}

export interface StrategyDesignDraftSummary {
  documentId: string;
  name: string;
  version: string;
  datasetReference: string;
  cellCount: number;
  transitionCount: number;
  updatedAt: string;
  validationSummary: string;
}

export interface StrategyDesignDraftSaveRequest {
  document: StrategyDesignDocument;
}

export interface StrategyDesignDraftSaveResponse {
  document: StrategyDesignDocument;
  summary: StrategyDesignDraftSummary;
  validation: StrategyDesignValidationResult;
  trace: StrategyDesignRunTraceEntry[];
}

export interface StrategyDesignCompiledScript {
  source: string;
  datasetFingerprint: string;
  fieldRefs: string[];
  disabledFieldRefs: string[];
}

export interface StrategyDesignPreviewRow {
  rowId: string;
  cellId: string;
  label: string;
  purpose: string;
  status: string;
  fieldRefs: string[];
  detail: string;
}

export interface StrategyDesignRunTraceEntry {
  stepId: string;
  label: string;
  status: string;
  detail: string;
  cellId?: string | null;
  occurredAt?: string | null;
}

export interface StrategyDesignPreviewResult {
  validation: StrategyDesignValidationResult;
  compiled: StrategyDesignCompiledScript;
  rows: StrategyDesignPreviewRow[];
  trace: StrategyDesignRunTraceEntry[];
}

export interface StrategyDesignRunBacktestRequest {
  document: StrategyDesignDocument;
  parameters?: Record<string, string> | null;
  operatorAcceptanceCriteria: string[];
  retainedEvidenceReferences: string[];
  accountingRecordReferences: string[];
  approvalReferences: string[];
  paperValidationReferences: string[];
  governedReportReferences: string[];
}

export interface StrategyDesignRunBacktestResponse {
  success: boolean;
  runId: string | null;
  strategyId: string;
  strategyName: string;
  validation: StrategyDesignValidationResult;
  compiled: StrategyDesignCompiledScript;
  trace: StrategyDesignRunTraceEntry[];
  previewRows: StrategyDesignPreviewRow[];
  metrics: Record<string, string>;
  runtimeError: string | null;
  promotionCandidatePath: string | null;
  reviewPacketPath: string | null;
  biasDisclosure?: BiasDisclosure | null;
}

// --- Quant Notebook ---

export type CellKind = "code" | "markdown";
export type CellExecutionState = "idle" | "running" | "done" | "error" | "stale";

export interface CellExecutionContext {
  symbol?: string;
  from?: string;
  to?: string;
  interval?: DataFetchRequest["interval"];
}

export interface CellOutput {
  kind: "console" | "metric" | "signal" | "error";
  text: string;
  tone?: "default" | "success" | "warning" | "danger";
  timestamp?: string;
}

export interface NotebookCell {
  id: string;
  ordinal: number;
  kind: CellKind;
  source: string;
  state: CellExecutionState;
  statusText: string;
  collapsed: boolean;
  output: CellOutput[];
}

export interface CellSnippet {
  id: string;
  label: string;
  description: string;
  kind: CellKind;
  source: string;
}

export interface CellExecuteRequest {
  cellId: string;
  source: string;
  context: CellExecutionContext;
}

export interface CellExecuteResult {
  cellId: string;
  success: boolean;
  output: CellOutput[];
  elapsedMs: number;
  errorMessage: string | null;
}

export interface PriceBar {
  timestamp: string;
  open: number;
  high: number;
  low: number;
  close: number;
  volume: number;
}

export interface DataFetchRequest {
  symbol: string;
  from: string;
  to: string;
  interval: "daily" | "hourly" | "minute";
}

export interface DataFetchResult {
  symbol: string;
  from: string;
  to: string;
  interval: DataFetchRequest["interval"];
  bars: PriceBar[];
  rowCount: number;
}

export type AdminMaintenanceTaskType =
  | "HealthCheck"
  | "Cleanup"
  | "Defragmentation"
  | "TierMigration"
  | "Compression"
  | "Repair"
  | "FullMaintenance"
  | "IntegrityCheck"
  | "Archival"
  | "RetentionEnforcement";

export interface ArchiveMaintenanceSchedule {
  scheduleId: string;
  name: string;
  description?: string | null;
  enabled: boolean;
  cronExpression: string;
  timeZoneId?: string | null;
  taskType: AdminMaintenanceTaskType | string;
  priority?: string | null;
  targetPaths?: string[] | null;
  lastExecutedAt?: string | null;
  nextExecutionAt?: string | null;
  lastExecutionId?: string | null;
  lastExecutionStatus?: string | null;
  executionCount?: number;
  successfulExecutions?: number;
  failedExecutions?: number;
  tags?: string[] | null;
}

export interface AdminMaintenanceScheduleResponse {
  schedules: ArchiveMaintenanceSchedule[];
  summary?: string | Record<string, unknown> | null;
  timestamp?: string;
  total?: number;
}

export interface MaintenanceExecution {
  executionId: string;
  scheduleId?: string | null;
  scheduleName?: string | null;
  taskType: AdminMaintenanceTaskType | string;
  status: string;
  manualTrigger?: boolean;
  startedAt: string;
  completedAt?: string | null;
  filesProcessed?: number;
  issuesFound?: number;
  issuesResolved?: number;
  bytesProcessed?: number;
  bytesSaved?: number;
  errorMessage?: string | null;
  logMessages?: string[] | null;
  result?: Record<string, unknown> | null;
}

export interface AdminMaintenanceHistoryResponse {
  executions: MaintenanceExecution[];
  total: number;
  statistics?: Record<string, unknown> | null;
  timestamp?: string;
}

export interface AdminMaintenanceRunRequest {
  taskType?: AdminMaintenanceTaskType | string;
  targetPaths?: string[] | null;
}

export interface AdminTierInfo {
  fileCount: number;
  totalBytes: number;
  oldestFile?: string | null;
  newestFile?: string | null;
}

export interface AdminStorageTiersResponse {
  tiers: Record<string, AdminTierInfo>;
  generatedAt?: string;
}

export interface AdminStorageUsageBreakdownEntry {
  fileCount: number;
  bytes: number;
}

export interface AdminStorageUsageResponse {
  rootPath: string;
  totalBytes: number;
  fileCount: number;
  breakdown: Record<string, AdminStorageUsageBreakdownEntry>;
  timestamp?: string;
}

export interface AdminRetentionResponse {
  retentionDays: number;
  maxStorageSizeGb: number;
  timestamp?: string;
}

export interface AdminCleanupCandidate {
  path: string;
  sizeBytes: number;
  lastModified: string;
}

export interface AdminCleanupPreviewResponse {
  candidateCount: number;
  reclaimableBytes: number;
  candidates: AdminCleanupCandidate[];
  timestamp?: string;
}

export interface AdminCleanupExecuteResponse {
  executed: boolean;
  message?: string | null;
  timestamp?: string;
}

export interface AdminStoragePermissionsResponse {
  rootPath: string;
  readable: boolean;
  writable: boolean;
  timestamp?: string;
}

export interface AdminQuickCheckResponse {
  configLoaded?: boolean;
  dataRoot?: string;
  dataRootExists?: boolean;
  symbolCount?: number;
  dataSource?: string;
  timestamp?: string;
}

export interface AdminShowConfigResponse {
  dataSource?: string;
  symbolCount?: number;
  symbols?: string[];
  dataRoot?: string;
  compress?: boolean;
  storage?: Record<string, unknown> | null;
  timestamp?: string;
}

export interface AdminErrorCodesResponse {
  errorCodes: Array<{ code: number; name: string }>;
  timestamp?: string;
}

export interface AdminSelfTestResponse {
  passed?: boolean;
  checks: Array<{ check: string; passed: boolean }>;
  timestamp?: string;
}

export interface MaintenanceSchedulesResponse extends AdminMaintenanceScheduleResponse {
  total?: number;
}

export interface MaintenanceScheduleHistoryResponse {
  scheduleId: string;
  executions: MaintenanceExecution[];
  total: number;
  summary?: Record<string, unknown> | null;
}

export interface DataPackageListItem {
  path: string;
  fileName: string;
  sizeBytes: number;
  createdAt: string;
  modifiedAt: string;
}

export interface DataPackageListResponse {
  packages: DataPackageListItem[];
}

export interface DataPackageCreateRequest {
  name?: string | null;
  description?: string | null;
  outputDirectory?: string | null;
  symbols?: string[] | null;
  eventTypes?: string[] | null;
  startDate?: string | null;
  endDate?: string | null;
  format?: string | null;
  compressionLevel?: string | null;
  includeQualityReport?: boolean | null;
  includeDataDictionary?: boolean | null;
  includeLoaderScripts?: boolean | null;
  verifyChecksums?: boolean | null;
  tags?: string[] | null;
  customMetadata?: Record<string, string> | null;
}

export interface DataPackageResult {
  success: boolean;
  jobId?: string;
  packageId?: string | null;
  packagePath?: string | null;
  packageFileName?: string | null;
  packageSizeBytes?: number;
  uncompressedSizeBytes?: number;
  filesIncluded?: number;
  totalEvents?: number;
  symbols?: string[];
  eventTypes?: string[];
  packageChecksum?: string | null;
  warnings?: string[];
  error?: string | null;
  completedAt?: string | null;
}

export interface DataPackageValidateRequest {
  packagePath: string;
}

export interface DataPackageValidationResponse {
  isValid: boolean;
  packagePath?: string;
  manifest?: Record<string, unknown> | null;
  issues?: string[] | null;
  missingFiles?: string[] | null;
  error?: string | null;
}

export interface DataPackageContentsResponse {
  packageId: string;
  name: string;
  description?: string | null;
  createdAt?: string;
  totalFiles: number;
  totalEvents: number;
  packageSizeBytes: number;
  uncompressedSizeBytes: number;
  symbols: string[];
  eventTypes: string[];
  files?: Array<{ relativePath?: string; path?: string; sizeBytes?: number; recordCount?: number }>;
  quality?: Record<string, unknown> | null;
}
