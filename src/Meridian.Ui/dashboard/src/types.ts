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
  engine: "Meridian Native" | "Lean";
  mode: "paper" | "live";
  status: "Running" | "Queued" | "Needs Review" | "Completed";
  dataset: string;
  window: string;
  pnl: string;
  sharpe: string;
  lastUpdated: string;
  notes: string;
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
