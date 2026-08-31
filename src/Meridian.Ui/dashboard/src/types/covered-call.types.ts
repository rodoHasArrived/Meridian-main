/**
 * Type mirrors for `Meridian.Ui.Shared.Contracts.CoveredCall*` DTOs.
 * Field names match the camelCase JSON shape produced by source-generated serialisation.
 */

export type CoveredCallScoringMode = "Basic" | "Relative";

export interface CoveredCallBacktestRequest {
  underlyingSymbol: string;
  from: string; // ISO date (yyyy-MM-dd)
  to: string;
  minStrike: number;
  overwriteRatio: number;
  maxDelta: number;
  minDte: number;
  maxDte: number | null;
  minIvPercentile: number;
  minOpenInterest: number;
  minVolume: number;
  maxSpreadPct: number;
  takeProfitCapture: number;
  rollDelta: number;
  exDivWindowDays: number;
  scoringMode: CoveredCallScoringMode;
  depthBonusWeight: number;
  riskFreeRate: number;
  initialCash: number;
  initialUnderlyingShares: number;
  label?: string | null;
  operatorAcceptanceCriteria: string[];
  retainedEvidenceReferences: string[];
  accountingRecordReferences: string[];
  approvalReferences: string[];
  paperValidationReferences: string[];
  governedReportReferences: string[];
}

export interface CoveredCallRunHandle {
  runId: string;
  queuedAt: string; // ISO timestamp
}

export type CoveredCallRunPhase =
  | "Queued"
  | "WarmingUp"
  | "Running"
  | "Completed"
  | "Failed"
  | "Cancelled"
  | "PersistenceDegraded";

export interface CoveredCallRunStatus {
  runId: string;
  phase: CoveredCallRunPhase;
  percentComplete: number;
  currentBacktestDate: string | null;
  failureMessage: string | null;
}

export interface CoveredCallEquityPoint {
  date: string;
  strategyEquity: number;
  underlyingEquity: number;
}

export interface CoveredCallTrade {
  strike: number;
  expiration: string;
  contracts: number;
  multiplier: number;
  entryDate: string;
  entryCredit: number;
  exitDate: string;
  exitDebit: number;
  exitReason: string;
  entryImpliedVolatility: number | null;
  netPnlPerContract: number;
  totalNetPnl: number;
  holdingDays: number;
  isWin: boolean;
  wasAssigned: boolean;
}

export interface CoveredCallOpenPosition {
  positionId: string;
  strike: number;
  expiration: string;
  contracts: number;
  multiplier: number;
  entryDate: string;
  entryCredit: number;
  markToClose: number;
  currentDelta: number;
  currentDte: number;
  unrealisedPnl: number;
  premiumCaptured: number;
}

export interface CoveredCallMetrics {
  cagr: number;
  annualizedVolatility: number;
  sharpeRatio: number;
  sortinoRatio: number;
  calmarRatio: number;
  maxDrawdownPct: number;
  winRate: number;
  assignmentRate: number;
  averageHoldingDays: number;
  totalOptionTrades: number;
  assignedTrades: number;
  totalPremiumCollected: number;
  totalOptionPnl: number;
  upCapture: number;
  downCapture: number;
  monthlyVar1Pct: number;
  monthlyVar5Pct: number;
  monthlyCVar5Pct: number;
  returnSkewness: number;
  returnKurtosis: number;
  annualizedTurnover: number;
}

export interface CoveredCallRunResult {
  runId: string;
  underlyingSymbol: string;
  from: string;
  to: string;
  label: string | null;
  metrics: CoveredCallMetrics;
  equityCurve: CoveredCallEquityPoint[];
  trades: CoveredCallTrade[];
  openPositionsAtEnd: CoveredCallOpenPosition[];
}

export interface CoveredCallRunSummary {
  runId: string;
  underlyingSymbol: string;
  from: string;
  to: string;
  label: string | null;
  status: string;
  startedAt: string;
  endedAt: string | null;
  cagr: number | null;
  sharpeRatio: number | null;
  winRate: number | null;
}

export interface CoveredCallChainPreviewRequest {
  underlyingSymbol: string;
  asOf: string;
  minStrike: number;
  maxDelta: number;
  minDte: number;
  maxDte: number | null;
  minOpenInterest: number;
  minVolume: number;
  maxSpreadPct: number;
}

export interface CoveredCallChainRow {
  strike: number;
  expiration: string;
  daysToExpiration: number;
  bid: number;
  ask: number;
  delta: number;
  impliedVolatility: number | null;
  openInterest: number;
  volume: number;
  meetsAllFilters: boolean;
  rejectReason: string | null;
}

export interface CoveredCallChainPreview {
  underlyingSymbol: string;
  asOf: string;
  underlyingPrice: number;
  candidates: CoveredCallChainRow[];
  totalContractsScanned: number;
  filtersPassed: number;
}
