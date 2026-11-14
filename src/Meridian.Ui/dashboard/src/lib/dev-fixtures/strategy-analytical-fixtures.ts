import type { StrategyRunSummary, StrategySweepResultGroup } from "@/types/workstation-6";

const points = [
  { lookback: "10", threshold: "1", totalReturn: 0.025 },
  { lookback: "20", threshold: "1", totalReturn: 0.038 },
  { lookback: "10", threshold: "2", totalReturn: -0.012 },
  { lookback: "30", threshold: "2", totalReturn: 0.029 }
];

const runs: StrategyRunSummary[] = points.map((point, index) => ({
  runId: `synthetic-sensitivity-${index + 1}`,
  strategyId: "synthetic-mean-reversion",
  strategyName: "Synthetic mean reversion",
  mode: "Backtest",
  engine: "Internal",
  status: "Completed",
  startedAt: "2026-05-29T10:00:00Z",
  completedAt: "2026-05-29T10:01:00Z",
  datasetReference: "synthetic-fixed-universe-v1",
  feedReference: null,
  portfolioId: null,
  ledgerReference: null,
  netPnl: point.totalReturn * 100_000,
  totalReturn: point.totalReturn,
  finalEquity: 100_000 * (1 + point.totalReturn),
  fillCount: 0,
  lastUpdatedAt: "2026-05-29T10:01:00Z",
  auditReference: "synthetic-sensitivity-evidence",
  parameters: { lookback: point.lookback, threshold: point.threshold, universe: "synthetic-fixed-universe-v1" }
}));

export const analyticalStrategySweeps: StrategySweepResultGroup[] = [{
  sweepId: "Synthetic sensitivity example",
  sweepDefinitionHash: "context-v2-synthetic-demo",
  objective: "TotalReturn",
  startedAt: "2026-05-29T10:00:00Z",
  runCount: runs.length,
  runs,
  objectiveRankings: [...runs].sort((a, b) => b.totalReturn! - a.totalReturn!)
    .map((run) => ({ runId: run.runId, objectiveValue: run.totalReturn })),
  comparisonContextVerified: true,
  historyMayBeTruncated: false,
  coverageDescription: "Synthetic in-sample illustration. Unobserved cells have no retained result; no complete expected-grid manifest is claimed."
}];
