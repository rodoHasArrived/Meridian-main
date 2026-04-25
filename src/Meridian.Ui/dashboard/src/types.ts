export type WorkspaceKey = "overview" | "research" | "trading" | "data-operations" | "governance";

export interface SessionInfo {
  displayName: string;
  role: string;
  environment: "paper" | "live" | "research";
  activeWorkspace: WorkspaceKey;
  commandCount: number;
}

export interface WorkspaceSummary {
  key: WorkspaceKey;
  label: string;
  description: string;
  status: string;
}

export interface MetricSnapshot {
  id: string;
  label: string;
  value: string;
  delta: string;
  tone: "default" | "success" | "warning" | "danger";
}

export interface ResearchRunRecord {
  id: string;
  strategyName: string;
  engine: string;
  mode: "backtest" | "paper" | "live";
  status: "Running" | "Queued" | "Needs Review" | "Completed";
  dataset: string;
  window: string;
  pnl: string;
  sharpe: string;
  lastUpdated: string;
  notes: string;
  promotionState?: string | null;
  netPnl?: number | null;
  totalReturn?: number | null;
  finalEquity?: number | null;
}

// --- Promotion workflow types ---

export interface PromotionEvaluationResult {
  runId: string;
  strategyId: string | null;
  strategyName: string | null;
  sourceMode: string | null;
  targetMode: string | null;
  isEligible: boolean;
  sharpeRatio: number;
  maxDrawdownPercent: number;
  totalReturn: number;
  reason: string;
  found: boolean;
  ready: boolean;
}

export interface PromotionDecisionResult {
  success: boolean;
  promotionId: string | null;
  newRunId: string | null;
  reason: string;
}

export interface PromotionRecord {
  promotionId: string;
  strategyId: string;
  strategyName: string;
  sourceRunType: string;
  targetRunType: string;
  runId?: string;
  approvedBy?: string | null;
  approvalReason?: string | null;
  reviewNotes?: string | null;
  manualOverrideId?: string | null;
  qualifyingSharpe: number;
  qualifyingMaxDrawdownPercent: number;
  qualifyingTotalReturn: number;
  promotedAt: string;
}

// --- Execution / paper session types ---

export interface PaperSessionSummary {
  sessionId: string;
  strategyId: string;
  strategyName: string | null;
  initialCash: number;
  createdAt: string;
  closedAt: string | null;
  isActive: boolean;
}

export interface ExecutionPortfolioSnapshot {
  cash: number;
  portfolioValue: number;
  unrealisedPnl: number;
  realisedPnl: number;
  positions: ExecutionPositionSnapshot[];
  asOf: string;
}

export interface ExecutionPositionSnapshot {
  symbol: string;
  quantity: number;
  averageCostBasis: number;
  currentPrice: number;
  marketValue: number;
  unrealisedPnl: number;
  realisedPnl: number;
}

export interface SessionOrderHistoryEntry {
  orderId: string;
  symbol: string;
  side: string;
  type: string;
  quantity: number;
  filledQuantity: number;
  averageFillPrice: number | null;
  status: string;
  createdAt: string;
  updatedAt: string;
}

export interface PaperSessionDetail {
  summary: PaperSessionSummary;
  symbols: string[];
  portfolio: ExecutionPortfolioSnapshot | null;
  orderHistory: SessionOrderHistoryEntry[] | null;
}

export interface PaperSessionReplayVerification {
  summary: PaperSessionSummary;
  symbols: string[];
  replaySource: string;
  isConsistent: boolean;
  mismatchReasons: string[];
  currentPortfolio: ExecutionPortfolioSnapshot | null;
  replayPortfolio: ExecutionPortfolioSnapshot;
  verifiedAt: string;
  comparedFillCount: number;
  comparedOrderCount: number;
  comparedLedgerEntryCount: number;
  lastPersistedFillAt: string | null;
  lastPersistedOrderUpdateAt: string | null;
  verificationAuditId: string | null;
}

export interface ExecutionAuditEntry {
  auditId: string;
  category: string;
  action: string;
  outcome: string;
  occurredAt: string;
  actor: string | null;
  brokerName: string | null;
  orderId: string | null;
  runId: string | null;
  symbol: string | null;
  correlationId: string | null;
  message: string | null;
  metadata: Record<string, string> | null;
}

export interface ExecutionCircuitBreakerState {
  isOpen: boolean;
  reason: string | null;
  changedBy: string | null;
  changedAt: string | null;
}

export interface ExecutionManualOverride {
  overrideId: string;
  kind: string;
  reason: string;
  createdBy: string;
  createdAt: string;
  expiresAt: string | null;
  symbol: string | null;
  strategyId: string | null;
  runId: string | null;
}

export interface ExecutionControlSnapshot {
  circuitBreaker: ExecutionCircuitBreakerState;
  defaultMaxPositionSize: number | null;
  symbolPositionLimits: Record<string, number>;
  manualOverrides: ExecutionManualOverride[];
  asOf: string;
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
}

export interface OrderResult {
  success: boolean;
  orderId: string | null;
  reason: string | null;
}

export interface ResearchWorkspaceResponse {
  metrics: MetricSnapshot[];
  runs: ResearchRunRecord[];
}

export interface DataOperationsProviderRecord {
  provider: string;
  status: "Healthy" | "Warning" | "Degraded";
  capability: string;
  latency: string;
  note: string;
  trustScore?: string;
  signalSource?: string;
  reasonCode?: string;
  recommendedAction?: string;
  gateImpact?: string;
}

export interface DataOperationsBackfillRecord {
  jobId: string;
  scope: string;
  provider: string;
  status: "Queued" | "Running" | "Review";
  progress: string;
  updatedAt: string;
}

export interface DataOperationsExportRecord {
  exportId: string;
  profile: string;
  target: string;
  status: "Ready" | "Running" | "Attention";
  rows: string;
  updatedAt: string;
}

export interface DataOperationsWorkspaceResponse {
  metrics: MetricSnapshot[];
  providers: DataOperationsProviderRecord[];
  backfills: DataOperationsBackfillRecord[];
  exports: DataOperationsExportRecord[];
}

export interface TradingPosition {
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
}

export interface GovernanceReconciliationRecord {
  runId: string;
  strategyName: string;
  mode: "paper" | "live" | "backtest";
  status: string;
  lastUpdated: string;
  breakCount: number;
  openBreakCount: number;
  reconciliationStatus: "NotStarted" | "BreaksOpen" | "SecurityCoverageOpen" | "Resolved" | "Balanced";
}

export interface GovernanceCashFlowSummary {
  totalCash: number;
  totalLedgerCash: number;
  netVariance: number;
  totalFinancing: number;
  runsWithCashSignals: number;
  runsWithCashVariance: number;
  tone: "default" | "success" | "warning" | "danger";
  summary: string;
}

export interface GovernanceReportingProfile {
  id: string;
  name: string;
  targetTool: string;
  format: string;
  description: string;
  loaderScript: boolean;
  dataDictionary: boolean;
}

export interface GovernanceReportingSummary {
  profileCount: number;
  recommendedProfiles: string[];
  profiles: GovernanceReportingProfile[];
  reportPackTargets: string[];
  summary: string;
}

export interface GovernanceWorkspaceResponse {
  metrics: MetricSnapshot[];
  reconciliationQueue: GovernanceReconciliationRecord[];
  breakQueue: ReconciliationBreakQueueItem[];
  cashFlow: GovernanceCashFlowSummary;
  reporting: GovernanceReportingSummary;
}

export type ReconciliationBreakQueueStatus = "Open" | "InReview" | "Resolved" | "Dismissed";

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

export interface LedgerTrialBalanceLine {
  accountName: string;
  accountType: string;
  symbol: string | null;
  financialAccountId: string | null;
  balance: number;
  entryCount: number;
  security: WorkstationSecurityReference | null;
}

export interface LedgerJournalLine {
  journalEntryId: string;
  timestamp: string;
  description: string;
  totalDebits: number;
  totalCredits: number;
  lineCount: number;
}

export interface LedgerSummary {
  ledgerReference: string;
  runId: string;
  asOf: string;
  journalEntryCount: number;
  ledgerEntryCount: number;
  assetBalance: number;
  liabilityBalance: number;
  equityBalance: number;
  revenueBalance: number;
  expenseBalance: number;
  trialBalance: LedgerTrialBalanceLine[];
  journal: LedgerJournalLine[];
  securityResolvedCount: number;
  securityMissingCount: number;
}

// --- Equity curve types ---

export interface EquityCurvePoint {
  date: string;
  totalEquity: number;
  cash: number;
  dailyReturn: number;
  drawdownFromPeak: number;
  drawdownFromPeakPercent: number;
}

export interface EquityCurveSummary {
  runId: string;
  initialEquity: number;
  finalEquity: number;
  maxDrawdown: number;
  maxDrawdownPercent: number;
  maxDrawdownRecoveryDays: number;
  sharpeRatio: number;
  sortinoRatio: number;
  points: EquityCurvePoint[];
}

// --- Fill types ---

export interface RunFillEntry {
  fillId: string;
  orderId: string;
  symbol: string;
  filledQuantity: number;
  fillPrice: number;
  commission: number;
  filledAt: string;
  accountId: string | null;
}

export interface RunFillSummary {
  runId: string;
  totalFills: number;
  totalCommissions: number;
  fills: RunFillEntry[];
}

// --- Attribution types ---

export interface SymbolAttributionEntry {
  symbol: string;
  realizedPnl: number;
  unrealizedPnl: number;
  totalPnl: number;
  tradeCount: number;
  commissions: number;
  marginInterestAllocated: number;
}

export interface RunAttributionSummary {
  runId: string;
  totalRealizedPnl: number;
  totalUnrealizedPnl: number;
  totalCommissions: number;
  bySymbol: SymbolAttributionEntry[];
}

// --- Strategy run summary ---

export type StrategyRunMode = "Backtest" | "Paper" | "Live";
export type StrategyRunEngine = "Internal" | "QuantConnect" | "External";
export type StrategyRunStatus = "Running" | "Paused" | "Completed" | "Failed" | "Cancelled";

export interface StrategyRunSummary {
  runId: string;
  strategyId: string;
  strategyName: string;
  mode: StrategyRunMode;
  engine: StrategyRunEngine;
  status: StrategyRunStatus;
  startedAt: string;
  completedAt: string | null;
  datasetReference: string | null;
  feedReference: string | null;
  portfolioId: string | null;
  ledgerReference: string | null;
  netPnl: number | null;
  totalReturn: number | null;
  finalEquity: number | null;
  fillCount: number;
  lastUpdatedAt: string;
  auditReference: string | null;
}

// --- Security Master workstation types ---

export interface SecurityClassificationSummary {
  assetClass: string;
  subType: string | null;
  primaryIdentifierKind: string | null;
  primaryIdentifierValue: string | null;
  matchedIdentifierKind?: string | null;
  matchedIdentifierValue?: string | null;
  matchedProvider?: string | null;
}

export interface SecurityEconomicDefinitionSummary {
  currency: string;
  version: number;
  effectiveFrom: string | null;
  effectiveTo: string | null;
  subType: string | null;
  assetFamily: string | null;
  issuerType: string | null;
}

export interface SecurityMasterEntry {
  securityId: string;
  displayName: string;
  status: "Active" | "Inactive" | "Pending" | "Deactivated";
  classification: SecurityClassificationSummary;
  economicDefinition: SecurityEconomicDefinitionSummary;
}

export interface SecurityIdentifierEntry {
  kind: string;
  value: string;
  isPrimary: boolean;
  validFrom: string;
  validTo: string | null;
  provider: string | null;
}

export interface SecurityAliasEntry {
  aliasId: string;
  securityId: string;
  aliasKind: string;
  aliasValue: string;
  provider: string | null;
  scope: "Operations" | "Collector" | "Execution" | "Migration";
  reason: string | null;
  createdBy: string;
  createdAt: string;
  validFrom: string;
  validTo: string | null;
  isEnabled: boolean;
}

export interface SecurityIdentityDrillIn {
  securityId: string;
  displayName: string;
  assetClass: string;
  status: string;
  version: number;
  effectiveFrom: string;
  effectiveTo: string | null;
  identifiers: SecurityIdentifierEntry[];
  aliases: SecurityAliasEntry[];
}

export interface SecurityMasterConflict {
  conflictId: string;
  securityId: string;
  conflictKind: string;
  fieldPath: string;
  providerA: string;
  valueA: string;
  providerB: string;
  valueB: string;
  detectedAt: string;
  status: "Open" | "Resolved" | "Dismissed";
}

export interface ResolveConflictRequest {
  conflictId: string;
  resolution: "AcceptA" | "AcceptB" | "Dismiss";
  resolvedBy: string;
  reason?: string;
}

// --- Backfill mutation types ---

export interface BackfillTriggerRequest {
  provider: string | null;
  symbols: string[];
  from: string | null;
  to: string | null;
}

export interface BackfillTriggerResult {
  success: boolean;
  provider: string;
  symbols: string[];
  from: string | null;
  to: string | null;
  barsWritten: number;
  startedUtc: string;
  completedUtc: string;
  error: string | null;
}

export interface BackfillProgressEntry {
  symbol: string;
  barsWritten: number;
  completed: boolean;
}

export interface BackfillProgressResponse {
  active: boolean;
  provider: string | null;
  symbols: BackfillProgressEntry[];
  message: string | null;
}

// --- System Overview types ---

export interface SystemEventRecord {
  id: string;
  type: "info" | "warning" | "error";
  message: string;
  source: string;
  timestamp: string;
}

export interface SystemOverviewResponse {
  systemStatus: "Healthy" | "Degraded" | "Offline";
  providersOnline: number;
  providersTotal: number;
  activeRuns: number;
  openPositions: number;
  activeBackfills: number;
  symbolsMonitored: number;
  storageHealth: "Healthy" | "Warning" | "Critical";
  lastHeartbeatUtc: string;
  metrics: MetricSnapshot[];
  recentEvents: SystemEventRecord[];
}

// --- Symbol management types ---

export interface SymbolRecord {
  symbol: string;
  status: "Active" | "Monitored" | "Archived" | "Error";
  provider: string | null;
  lastEventAt: string | null;
  eventCount: number;
  hasHistoricalData: boolean;
}

export interface SymbolStatistics {
  totalSymbols: number;
  monitoredSymbols: number;
  archivedSymbols: number;
  symbolsWithErrors: number;
  totalEventsLast24h: number;
}

// --- Quality monitoring types ---

export interface QualitySymbolScore {
  symbol: string;
  completenessScore: number;
  freshnessScore: number;
  gapCount: number;
  anomalyCount: number;
  health: "Healthy" | "Warning" | "Critical";
}

export interface QualityGapEntry {
  symbol: string;
  provider: string;
  from: string;
  to: string;
  estimatedBars: number;
  status: "Open" | "Resolved";
}

export interface QualityAnomalyEntry {
  anomalyId: string;
  symbol: string;
  anomalyType: string;
  message: string;
  detectedAt: string;
  acknowledged: boolean;
}

export interface QualityDashboardResponse {
  overallScore: number;
  completenessScore: number;
  freshnessScore: number;
  anomalyRate: number;
  symbols: QualitySymbolScore[];
  recentGaps: QualityGapEntry[];
  recentAnomalies: QualityAnomalyEntry[];
}
