import { useState } from "react";
import type {
  GovernanceWorkspaceResponse,
  ResearchWorkspaceResponse,
  TradingWorkspaceResponse
} from "@/types";

export interface PortfolioPositionRow {
  id: string;
  symbol: string;
  side: string;
  quantity: string;
  avgPrice: string;
  markPrice: string;
  dayPnl: string;
  unrealizedPnl: string;
  exposure: string;
  pnlTone: "success" | "danger" | "default";
  isSelected: boolean;
  selectAriaLabel: string;
  ariaLabel: string;
}

export interface PortfolioRunRow {
  id: string;
  strategyName: string;
  mode: string;
  modeBadgeVariant: "paper" | "live" | "outline";
  status: string;
  pnl: string;
  pnlTone: "success" | "danger" | "default";
  sharpe: string;
  promotionState: string | null | undefined;
}

export interface PortfolioMetricStat {
  label: string;
  value: string;
}

export interface PortfolioHeaderChip {
  label: string;
  value: string;
}

export interface PortfolioDetailField {
  label: string;
  value: string;
  tone: "default" | "success" | "warning" | "danger" | "muted";
}

export interface PortfolioPositionDetail {
  id: string;
  title: string;
  subtitle: string;
  ariaLabel: string;
  statusTitle: string;
  statusDetail: string;
  statusTone: "default" | "success" | "warning" | "danger";
  fields: PortfolioDetailField[];
}

export interface PortfolioScreenViewModel {
  metricsFromTrading: boolean;
  metricCards: TradingWorkspaceResponse["metrics"];
  fallbackStats: PortfolioMetricStat[];
  headerChips: PortfolioHeaderChip[];
  hasPositions: boolean;
  positionRows: PortfolioPositionRow[];
  positionListLabel: string;
  positionCountLabel: string;
  positionDetailId: string;
  positionEmptyText: string;
  selectedPosition: PortfolioPositionDetail | null;
  selectPosition: (id: string) => void;
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
  governance,
  selectedPositionId = null,
  selectPosition = () => {}
}: {
  trading: TradingWorkspaceResponse | null;
  research: ResearchWorkspaceResponse | null;
  governance: GovernanceWorkspaceResponse | null;
  selectedPositionId?: string | null;
  selectPosition?: (id: string) => void;
}): PortfolioScreenViewModel {
  const positions = trading?.positions ?? [];
  const runs = research?.runs ?? [];
  const cashFlow = governance?.cashFlow ?? null;
  const selectedId =
    positions.find((p, index) => positionId(p.symbol, p.side, index) === selectedPositionId) !== undefined
      ? selectedPositionId
      : positions.length > 0
        ? positionId(positions[0].symbol, positions[0].side, 0)
        : null;

  const positionRows: PortfolioPositionRow[] = positions.map((p, index) => {
    const id = positionId(p.symbol, p.side, index);
    const tone = pnlTone(p.unrealizedPnl);

    return {
      id,
      symbol: p.symbol,
      side: p.side,
      quantity: p.quantity,
      avgPrice: p.averagePrice,
      markPrice: p.markPrice,
      dayPnl: p.dayPnl,
      unrealizedPnl: p.unrealizedPnl,
      exposure: p.exposure,
      pnlTone: tone,
      isSelected: id === selectedId,
      selectAriaLabel: `Inspect ${p.symbol} ${p.side} holding`,
      ariaLabel: `${p.symbol} ${p.side} position: ${p.quantity} shares, exposure ${p.exposure}, unrealized P&L ${p.unrealizedPnl}`
    };
  });

  const runRows: PortfolioRunRow[] = runs.map((r) => ({
    id: r.id,
    strategyName: r.strategyName,
    mode: r.mode,
    modeBadgeVariant: modeBadgeVariant(r.mode),
    status: r.status,
    pnl: r.pnl,
    pnlTone: pnlTone(r.pnl),
    sharpe: r.sharpe,
    promotionState: r.promotionState
  }));

  const totalExposure = sumNumericStrings(positions.map((p) => p.exposure));
  const totalUnrealizedPnl = sumNumericStrings(positions.map((p) => p.unrealizedPnl));
  const selectedRow = positionRows.find((row) => row.id === selectedId) ?? null;
  const selectedPosition = selectedRow
    ? buildSelectedPositionDetail(selectedRow, trading)
    : null;

  const fallbackStats: PortfolioMetricStat[] = [
    {
      label: "Total exposure",
      value: positions.length > 0 ? formatCurrency(totalExposure) : "—"
    },
    {
      label: "Unrealized P&L",
      value: positions.length > 0
        ? (totalUnrealizedPnl >= 0 ? "+" : "") + formatCurrency(totalUnrealizedPnl)
        : "—"
    },
    { label: "Cash", value: "—" },
    { label: "Open positions", value: String(positions.length) }
  ];
  const cashVarianceLabel = cashFlow !== null ? formatCurrency(cashFlow.netVariance) : null;

  return {
    metricsFromTrading: trading !== null,
    metricCards: trading?.metrics ?? [],
    fallbackStats,
    headerChips: buildPortfolioHeaderChips({
      openPositionCount: positions.length,
      totalExposure,
      totalUnrealizedPnl,
      hasPositions: positions.length > 0,
      cashVarianceLabel
    }),
    hasPositions: positionRows.length > 0,
    positionRows,
    positionListLabel: "Open positions",
    positionCountLabel: `${positionRows.length} position${positionRows.length === 1 ? "" : "s"}`,
    positionDetailId: "portfolio-position-detail",
    positionEmptyText: trading
      ? "No open positions in the active paper session."
      : "Trading workspace data unavailable.",
    selectedPosition,
    selectPosition,
    hasRuns: runRows.length > 0,
    runRows,
    runEmptyText: research
      ? "No runs available. Create a strategy run in the Strategy workspace."
      : "Strategy workspace data unavailable.",
    cashFlowSummary: cashFlow?.summary ?? null,
    cashVarianceLabel,
    cashFlowTone: cashFlow?.tone ?? "default",
    openPositionCount: positions.length
  };
}

function buildPortfolioHeaderChips({
  openPositionCount,
  totalExposure,
  totalUnrealizedPnl,
  hasPositions,
  cashVarianceLabel
}: {
  openPositionCount: number;
  totalExposure: number;
  totalUnrealizedPnl: number;
  hasPositions: boolean;
  cashVarianceLabel: string | null;
}): PortfolioHeaderChip[] {
  const chips: PortfolioHeaderChip[] = [
    { label: "Open positions", value: String(openPositionCount) },
    { label: "Exposure", value: hasPositions ? formatCurrency(totalExposure) : "—" },
    {
      label: "Unrealized P&L",
      value: hasPositions
        ? (totalUnrealizedPnl >= 0 ? "+" : "") + formatCurrency(totalUnrealizedPnl)
        : "—"
    }
  ];

  if (cashVarianceLabel) {
    chips.push({ label: "Cash variance", value: cashVarianceLabel });
  }

  return chips;
}

export function usePortfolioScreenViewModel({
  trading,
  research,
  governance
}: {
  trading: TradingWorkspaceResponse | null;
  research: ResearchWorkspaceResponse | null;
  governance: GovernanceWorkspaceResponse | null;
}): PortfolioScreenViewModel {
  const [selectedPositionId, setSelectedPositionId] = useState<string | null>(null);

  return buildPortfolioScreenViewModel({
    trading,
    research,
    governance,
    selectedPositionId,
    selectPosition: setSelectedPositionId
  });
}

function positionId(symbol: string, side: string, index: number): string {
  return `${symbol.toLowerCase()}-${side.toLowerCase()}-${index}`;
}

function pnlTone(value: string): "success" | "danger" | "default" {
  if (value.startsWith("+")) return "success";
  if (value.startsWith("-")) return "danger";
  return "default";
}

function modeBadgeVariant(mode: string): "paper" | "live" | "outline" {
  if (mode === "paper") return "paper";
  if (mode === "live") return "live";
  return "outline";
}

function buildSelectedPositionDetail(
  position: PortfolioPositionRow,
  trading: TradingWorkspaceResponse | null
): PortfolioPositionDetail {
  const risk = trading?.risk ?? null;
  const brokerage = trading?.brokerage ?? null;
  const statusTone = riskTone(risk?.state, position.pnlTone);
  const guardrailSummary = risk?.activeGuardrails.length
    ? risk.activeGuardrails.join(" · ")
    : "No active guardrails";

  return {
    id: position.id,
    title: position.symbol,
    subtitle: `${position.side} · ${position.quantity} shares`,
    ariaLabel: `${position.symbol} holding detail`,
    statusTitle: `${position.symbol} selected`,
    statusDetail: `${position.exposure} exposure with ${position.unrealizedPnl} unrealized P&L. ${risk?.summary ?? "Risk context unavailable."}`,
    statusTone,
    fields: [
      { label: "Side", value: position.side, tone: "default" },
      { label: "Quantity", value: position.quantity, tone: "default" },
      { label: "Average price", value: position.avgPrice, tone: "muted" },
      { label: "Mark price", value: position.markPrice, tone: "muted" },
      { label: "Exposure", value: position.exposure, tone: "default" },
      { label: "Day P&L", value: position.dayPnl, tone: pnlFieldTone(position.dayPnl) },
      { label: "Unrealized P&L", value: position.unrealizedPnl, tone: pnlFieldTone(position.unrealizedPnl) },
      { label: "Risk state", value: risk?.state ?? "Unavailable", tone: riskFieldTone(risk?.state) },
      { label: "Buying power", value: risk?.buyingPowerUsed ?? "—", tone: "muted" },
      { label: "Guardrails", value: guardrailSummary, tone: risk?.activeGuardrails.length ? "warning" : "success" },
      { label: "Brokerage", value: brokerage ? `${brokerage.provider} · ${brokerage.environment}` : "—", tone: "muted" },
      { label: "Last heartbeat", value: brokerage?.lastHeartbeat ?? "—", tone: "muted" }
    ]
  };
}

function pnlFieldTone(value: string): PortfolioDetailField["tone"] {
  const tone = pnlTone(value);
  if (tone === "success") return "success";
  if (tone === "danger") return "danger";
  return "default";
}

function riskFieldTone(state: TradingWorkspaceResponse["risk"]["state"] | undefined): PortfolioDetailField["tone"] {
  if (state === "Healthy") return "success";
  if (state === "Observe") return "warning";
  if (state === "Constrained") return "danger";
  return "muted";
}

function riskTone(
  riskState: TradingWorkspaceResponse["risk"]["state"] | undefined,
  pnl: PortfolioPositionRow["pnlTone"]
): PortfolioPositionDetail["statusTone"] {
  if (riskState === "Constrained") return "danger";
  if (riskState === "Observe") return "warning";
  if (pnl === "danger") return "warning";
  if (pnl === "success" || riskState === "Healthy") return "success";
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
