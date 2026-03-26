export type WorkspaceKey = "research" | "trading" | "data-operations" | "governance";

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
  engine: "Meridian Native" | "Lean" | string;
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
  status: string;
  initialCash: number;
  createdAt: string;
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
  cashFlow: GovernanceCashFlowSummary;
  reporting: GovernanceReportingSummary;
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
