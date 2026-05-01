import type {
  GovernanceWorkspaceResponse,
  ResearchWorkspaceResponse,
  TradingWorkspaceResponse
} from "@/types";

export interface PortfolioPositionRow {
  symbol: string;
  side: string;
  quantity: string;
  avgPrice: string;
  markPrice: string;
  unrealizedPnl: string;
  exposure: string;
  pnlTone: "success" | "danger" | "default";
  ariaLabel: string;
}

export interface PortfolioRunRow {
  id: string;
  strategyName: string;
  mode: string;
  status: string;
  pnl: string;
  sharpe: string;
  promotionState: string | null | undefined;
}

export interface PortfolioMetricStat {
  label: string;
  value: string;
}

export interface PortfolioScreenViewModel {
  metricsFromTrading: boolean;
  fallbackStats: PortfolioMetricStat[];
  hasPositions: boolean;
  positionRows: PortfolioPositionRow[];
  positionEmptyText: string;
  hasRuns: boolean;
  runRows: PortfolioRunRow[];
  runEmptyText: string;
  cashFlowSummary: string | null;
  cashVarianceLabel: string | null;
  cashFlowTone: "default" | "success" | "warning" | "danger";
  openPositionCount: number;
}

export function buildPortfolioScreenViewModel({
  trading,
  research,
  governance
}: {
  trading: TradingWorkspaceResponse | null;
  research: ResearchWorkspaceResponse | null;
  governance: GovernanceWorkspaceResponse | null;
}): PortfolioScreenViewModel {
  const positions = trading?.positions ?? [];
  const runs = research?.runs ?? [];
  const cashFlow = governance?.cashFlow ?? null;

  const positionRows: PortfolioPositionRow[] = positions.map((p) => ({
    symbol: p.symbol,
    side: p.side,
    quantity: p.quantity,
    avgPrice: p.averagePrice,
    markPrice: p.markPrice,
    unrealizedPnl: p.unrealizedPnl,
    exposure: p.exposure,
    pnlTone: pnlTone(p.unrealizedPnl),
    ariaLabel: `${p.symbol} ${p.side} position: ${p.quantity} shares, unrealized P&L ${p.unrealizedPnl}`
  }));

  const runRows: PortfolioRunRow[] = runs.map((r) => ({
    id: r.id,
    strategyName: r.strategyName,
    mode: r.mode,
    status: r.status,
    pnl: r.pnl,
    sharpe: r.sharpe,
    promotionState: r.promotionState
  }));

  const totalExposure = sumNumericStrings(positions.map((p) => p.exposure));
  const totalUnrealizedPnl = sumNumericStrings(positions.map((p) => p.unrealizedPnl));

  const fallbackStats: PortfolioMetricStat[] = [
    {
      label: "Total Exposure",
      value: positions.length > 0 ? formatCurrency(totalExposure) : "—"
    },
    {
      label: "Unrealized P&L",
      value: positions.length > 0
        ? (totalUnrealizedPnl >= 0 ? "+" : "") + formatCurrency(totalUnrealizedPnl)
        : "—"
    },
    { label: "Cash", value: "—" },
    { label: "Open Positions", value: String(positions.length) }
  ];

  return {
    metricsFromTrading: trading !== null,
    fallbackStats,
    hasPositions: positionRows.length > 0,
    positionRows,
    positionEmptyText: trading
      ? "No open positions in the active paper session."
      : "Trading workspace data unavailable.",
    hasRuns: runRows.length > 0,
    runRows,
    runEmptyText: research
      ? "No runs available. Create a strategy run in the Strategy workspace."
      : "Strategy workspace data unavailable.",
    cashFlowSummary: cashFlow?.summary ?? null,
    cashVarianceLabel: cashFlow !== null ? formatCurrency(cashFlow.netVariance) : null,
    cashFlowTone: cashFlow?.tone ?? "default",
    openPositionCount: positions.length
  };
}

function pnlTone(value: string): "success" | "danger" | "default" {
  if (value.startsWith("+")) return "success";
  if (value.startsWith("-")) return "danger";
  return "default";
}

function sumNumericStrings(values: string[]): number {
  return values.reduce((sum, v) => {
    const cleaned = v.replace(/[$+,]/g, "");
    const n = parseFloat(cleaned);
    return sum + (isNaN(n) ? 0 : n);
  }, 0);
}

function formatCurrency(value: number): string {
  const prefix = value >= 0 ? "$" : "-$";
  return `${prefix}${Math.abs(value).toLocaleString(undefined, { maximumFractionDigits: 0 })}`;
}
