import { useState } from "react";
import { evidenceWorkbenchPath, WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";
import { PORTFOLIO_API_ENDPOINTS, WORKSTATION_API_ENDPOINTS } from "@/lib/workstation-endpoints";
import type {
  BrokerageConnectionStatus,
  BrokerageHouseholdAccount,
  BrokerageHouseholdPortfolio,
  BrokerageHouseholdPosition,
  GovernanceCashFlowSummary,
  AccountingWorkspaceResponse,
  MetricSnapshot,
  MultiAssetCoverageSummary,
  PortfolioWorkspaceResponse,
  StrategyWorkspaceResponse,
  RunAttributionSummary,
  RunCashFlowSummary,
  RunFillSummary,
  EquityCurveSummary,
  StrategyRunContinuityDto,
  StrategyRunContinuityWarningSeverity,
  TradingWorkspaceResponse
} from "@/types";

type PortfolioRiskState = TradingWorkspaceResponse["risk"];
type PortfolioBrokerageStatus = TradingWorkspaceResponse["brokerage"];
type PortfolioSourceRun = {
  id: string;
  strategyName: string;
  engine: string;
  mode: string;
  status: string;
  dataset: string;
  window: string;
  pnl: string;
  sharpe: string;
  lastUpdated: string;
  notes: string;
  promotionState?: string | null;
};

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
  expanded: boolean;
  detailPanelId: string;
  selectAriaLabel: string;
  ariaLabel: string;
}

export interface PortfolioRunRow {
  id: string;
  strategyName: string;
  engine: string;
  mode: string;
  modeBadgeVariant: "paper" | "live" | "outline";
  status: string;
  pnl: string;
  pnlTone: "success" | "danger" | "default";
  sharpe: string;
  dataset: string;
  window: string;
  lastUpdated: string;
  notes: string;
  promotionState: string | null | undefined;
  isSelected: boolean;
  expanded: boolean;
  detailPanelId: string;
  selectAriaLabel: string;
  ariaLabel: string;
}

export interface PortfolioHeaderChip {
  label: string;
  value: string;
}

export interface PortfolioBrokerageAccountOption {
  key: string;
  label: string;
  isSelected: boolean;
  tabIndex: 0 | -1;
  ariaLabel: string;
}

export interface PortfolioBrokerageAccountRow {
  id: string;
  label: string;
  kind: string;
  health: string;
  healthBadgeVariant: "outline" | "success" | "warning" | "danger";
  equity: string;
  cash: string;
  buyingPower: string;
  syncedAt: string;
  positionCount: string;
  warningCount: string;
  hasWarning: boolean;
  warningText: string;
  rowClassName: string;
  isSelected: boolean;
  expanded: boolean;
  detailPanelId: string;
  selectAriaLabel: string;
  ariaLabel: string;
}

export interface PortfolioBrokeragePositionRow {
  id: string;
  accountLabel: string;
  accountKind: string;
  symbol: string;
  quantity: string;
  averagePrice: string;
  markPrice: string;
  marketValue: string;
  unrealizedPnl: string;
  pnlTone: "success" | "danger" | "default";
  assetClass: string;
  securityCoverage: string;
  rowClassName: string;
  isSelected: boolean;
  detailPanelId: string;
  expanded: boolean;
  selectAriaLabel: string;
  ariaLabel: string;
}

export interface PortfolioDetailField {
  label: string;
  value: string;
  tone: "default" | "success" | "warning" | "danger" | "muted";
}

export interface PortfolioBrokerageWarningRow {
  id: string;
  label: string;
  detail: string;
  ariaLabel: string;
}

export interface PortfolioBrokerageSetupAction {
  label: string;
  href: string;
  ariaLabel: string;
  detail: string;
}

export interface PortfolioBrokerageTrustSnapshot {
  regionLabel: string;
  title: string;
  statusLabel: string;
  statusTone: "default" | "success" | "warning" | "danger";
  summary: string;
  chips: PortfolioHeaderChip[];
  fields: PortfolioDetailField[];
}

export interface PortfolioBackendLink {
  id: string;
  method: "GET";
  label: string;
  href: string;
  ariaLabel: string;
}

export interface PortfolioWorkflowTaskAction {
  id: "provider-setup" | "brokerage-sync" | "trading-readiness" | "trading-cockpit" | "evidence";
  label: string;
  href: string;
  ariaLabel: string;
  detail: string;
  detailId: string;
  variant: "default" | "outline";
}

export interface PortfolioWorkflowTaskPanel {
  regionLabel: string;
  eyebrow: string;
  title: string;
  description: string;
  statusLabel: string;
  statusTone: "default" | "success" | "warning" | "danger";
  chips: PortfolioHeaderChip[];
  statusRows: PortfolioDetailField[];
  actionListLabel: string;
  actions: PortfolioWorkflowTaskAction[];
  backendLinks: PortfolioBackendLink[];
  selectedSummary: string;
}

export interface PortfolioMultiAssetCoverageRow {
  id: string;
  assetClass: string;
  displayName: string;
  statusLabel: string;
  statusTone: "default" | "success" | "warning" | "danger";
  readinessGroupId: string;
  readinessGroupLabel: string;
  readinessDetail: string;
  summary: string;
  evidenceLabel: string;
  blockerLabel: string;
  ledgerLabel: string;
  reconciliationLabel: string;
  evidenceTargets: PortfolioMultiAssetEvidenceTarget[];
  blockerTargets: PortfolioMultiAssetBlockerTarget[];
  primaryEvidenceRoute: string;
}

export interface PortfolioMultiAssetEvidenceTarget {
  id: string;
  label: string;
  category: string;
  statusLabel: string;
  statusTone: "default" | "success" | "warning" | "danger";
  href: string;
  requiredLabel: string;
  ariaLabel: string;
}

export interface PortfolioMultiAssetBlockerTarget {
  id: string;
  label: string;
  detail: string;
  source: string;
  statusTone: "default" | "success" | "warning" | "danger";
  href: string | null;
  ariaLabel: string;
}

export interface PortfolioMultiAssetCoverageGroup {
  id: string;
  label: string;
  statusTone: "default" | "success" | "warning" | "danger";
  summary: string;
  rows: PortfolioMultiAssetCoverageRow[];
}

export interface PortfolioMultiAssetCoveragePanel {
  title: string;
  description: string;
  statusLabel: string;
  statusTone: "default" | "success" | "warning" | "danger";
  chips: PortfolioHeaderChip[];
  rows: PortfolioMultiAssetCoverageRow[];
  groups: PortfolioMultiAssetCoverageGroup[];
  blockerMessages: string[];
  evidenceRoute: string;
  evidenceRouteLabel: string;
  asOfLabel: string;
}

interface PortfolioRunContinuityBlocker {
  code: string;
  label: string;
  detail: string;
  tone: "warning" | "danger" | "muted";
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

export interface PortfolioRunDetail {
  id: string;
  title: string;
  subtitle: string;
  ariaLabel: string;
  evidenceAction: PortfolioRunEvidenceAction;
  statusTitle: string;
  statusDetail: string;
  statusTone: "default" | "success" | "warning" | "danger";
  statusBadgeLabel: string;
  statusBadgeVariant: "outline" | "success" | "warning" | "danger";
  fields: PortfolioDetailField[];
}

export interface PortfolioRunComparisonSummary {
  ariaLabel: string;
  title: string;
  description: string;
  statusTone: "default" | "success" | "warning" | "danger";
  cards: PortfolioRunComparisonCard[];
}

export interface PortfolioRunComparisonCard {
  id: string;
  label: string;
  value: string;
  detail: string;
  tone: "default" | "success" | "warning" | "danger";
}

export interface PortfolioRunDrillInData {
  runId: string;
  attribution: RunAttributionSummary | null;
  drawdownProfile: EquityCurveSummary | null;
  cashFlow: RunCashFlowSummary | null;
  trades: RunFillSummary | null;
  isLoading: boolean;
  error: string | null;
}

export interface PortfolioRunDrillInSummary {
  ariaLabel: string;
  title: string;
  description: string;
  statusTone: "default" | "success" | "warning" | "danger";
  actionLabel: string;
  actionAriaLabel: string;
  cards: PortfolioRunComparisonCard[];
  bridgeRows: PortfolioRunBridgeRow[];
  tradeEvidenceRows: PortfolioRunTradeEvidenceRow[];
}

export interface PortfolioRunBridgeRow {
  id: string;
  label: string;
  value: string;
  detail: string;
  tone: "default" | "success" | "warning" | "danger" | "muted";
}

export interface PortfolioRunTradeEvidenceRow {
  id: string;
  symbol: string;
  quantity: string;
  price: string;
  commission: string;
  filledAt: string;
  accountId: string;
  ariaLabel: string;
}

export interface PortfolioBrokeragePositionDetail {
  id: string;
  title: string;
  subtitle: string;
  ariaLabel: string;
  statusTitle: string;
  statusDetail: string;
  statusTone: "default" | "success" | "warning" | "danger";
  statusBadgeLabel: string;
  statusBadgeVariant: "outline" | "success" | "warning" | "danger";
  fields: PortfolioDetailField[];
}

export interface PortfolioBrokerageAccountDetail {
  id: string;
  title: string;
  subtitle: string;
  ariaLabel: string;
  statusTitle: string;
  statusDetail: string;
  statusTone: "default" | "success" | "warning" | "danger";
  statusBadgeLabel: string;
  statusBadgeVariant: "outline" | "success" | "warning" | "danger";
  fields: PortfolioDetailField[];
}

export interface PortfolioRunEvidenceAction {
  label: string;
  href: string;
  ariaLabel: string;
}

export interface PortfolioScreenViewModel {
  metricsFromTrading: boolean;
  metricCards: TradingWorkspaceResponse["metrics"];
  multiAssetCoveragePanel: PortfolioMultiAssetCoveragePanel | null;
  positionSourceLabel: string;
  fallbackStats: MetricSnapshot[];
  headerChips: PortfolioHeaderChip[];
  workflowTaskPanel: PortfolioWorkflowTaskPanel | null;
  brokerageProviderLabel: string;
  brokeragePanelEyebrow: string;
  brokerageConnectionLabel: string;
  brokerageConnectionTone: "default" | "success" | "warning" | "danger";
  brokerageConnectionDetail: string;
  brokerageConnectionWarnings: string[];
  brokerageTrustSnapshot: PortfolioBrokerageTrustSnapshot;
  brokerageWarningRows: PortfolioBrokerageWarningRow[];
  brokerageWarningCountLabel: string;
  brokerageAccountFilterLabel: string;
  brokeragePositionsTableLabel: string;
  brokerageAccountOptions: PortfolioBrokerageAccountOption[];
  selectedBrokerageAccountKey: string;
  selectBrokerageAccount: (key: string) => void;
  selectAdjacentBrokerageAccount: (direction: "next" | "previous" | "first" | "last") => void;
  hasBrokerageAccounts: boolean;
  brokerageAccountRows: PortfolioBrokerageAccountRow[];
  brokerageAccountsTableLabel: string;
  brokerageAccountDetailId: string;
  selectedBrokerageAccount: PortfolioBrokerageAccountDetail;
  brokerageAccountEmptyText: string;
  hasBrokeragePositions: boolean;
  brokeragePositionRows: PortfolioBrokeragePositionRow[];
  brokeragePositionDetailId: string;
  selectedBrokeragePositionId: string | null;
  selectedBrokeragePosition: PortfolioBrokeragePositionDetail | null;
  selectBrokeragePosition: (id: string) => void;
  brokerageEmptyText: string;
  brokerageSetupAction: PortfolioBrokerageSetupAction | null;
  hasPositions: boolean;
  positionRows: PortfolioPositionRow[];
  positionListLabel: string;
  positionCountLabel: string;
  positionDetailId: string;
  selectedPositionChip: PortfolioHeaderChip;
  runEvidenceChip: PortfolioHeaderChip;
  positionDetailEmptyTitle: string;
  positionEmptyText: string;
  selectedPosition: PortfolioPositionDetail | null;
  selectPosition: (id: string) => void;
  hasRuns: boolean;
  runRows: PortfolioRunRow[];
  runListLabel: string;
  runCountLabel: string;
  runDetailId: string;
  selectedRunChip: PortfolioHeaderChip;
  runDetailEmptyTitle: string;
  runEmptyText: string;
  selectedRun: PortfolioRunDetail | null;
  runComparisonSummary: PortfolioRunComparisonSummary;
  runDrillInSummary: PortfolioRunDrillInSummary;
  selectRun: (id: string) => void;
  cashFlowSummary: string | null;
  cashVarianceLabel: string | null;
  cashFlowTone: "default" | "success" | "warning" | "danger";
  openPositionCount: number;
}

export function buildPortfolioScreenViewModel({
  portfolio,
  trading,
  strategy,
  accounting,
  brokerageConnection,
  brokeragePortfolio,
  multiAssetCoverage,
  selectedPositionId = null,
  selectedRunId = null,
  selectedRunContinuity = null,
  selectedRunDrillIn = null,
  selectedBrokeragePositionId = null,
  selectedBrokerageAccountKey = "all",
  pathname = WORKSTATION_ROUTE_CATALOG.portfolio,
  selectPosition = () => {},
  selectRun = () => {},
  selectBrokeragePosition = () => {},
  selectBrokerageAccount = () => {}
}: {
  portfolio?: PortfolioWorkspaceResponse | null;
  trading: TradingWorkspaceResponse | null;
  strategy: StrategyWorkspaceResponse | null;
  accounting: AccountingWorkspaceResponse | null;
  brokerageConnection?: BrokerageConnectionStatus | null;
  brokeragePortfolio?: BrokerageHouseholdPortfolio | null;
  multiAssetCoverage?: MultiAssetCoverageSummary | null;
  selectedPositionId?: string | null;
  selectedRunId?: string | null;
  selectedRunContinuity?: StrategyRunContinuityDto | null;
  selectedRunDrillIn?: PortfolioRunDrillInData | null;
  selectedBrokeragePositionId?: string | null;
  selectedBrokerageAccountKey?: string;
  pathname?: string;
  selectPosition?: (id: string) => void;
  selectRun?: (id: string) => void;
  selectBrokeragePosition?: (id: string) => void;
  selectBrokerageAccount?: (key: string) => void;
}): PortfolioScreenViewModel {
  const positions = portfolio?.positions ?? trading?.positions ?? [];
  const runs = portfolio
    ? portfolio.runs.map(toPortfolioRunRecord)
    : strategy?.runs ?? [];
  const cashFlow = portfolio?.cashFlow ?? accounting?.cashFlow ?? null;
  const risk = portfolio?.risk ?? trading?.risk ?? null;
  const brokerage = portfolio?.brokerage ?? trading?.brokerage ?? null;
  const brokerageAccounts = brokeragePortfolio?.accounts ?? [];
  const providerLabel = brokerageProviderLabel(brokerageConnection, brokeragePortfolio);
  const brokerageAccountKeySet = new Set(brokerageAccounts.map((account) => account.fundAccountId));
  const selectedBrokerageKey = selectedBrokerageAccountKey === "all" || brokerageAccountKeySet.has(selectedBrokerageAccountKey)
    ? selectedBrokerageAccountKey
    : "all";
  const brokerageAccountOptions = buildBrokerageAccountOptions(brokerageAccounts, selectedBrokerageKey, providerLabel);
  const selectAdjacentBrokerageAccount = (direction: "next" | "previous" | "first" | "last") => {
    const nextKey = nextBrokerageAccountKey(brokerageAccountOptions, selectedBrokerageKey, direction);
    if (nextKey !== selectedBrokerageKey) {
      selectBrokerageAccount(nextKey);
    }
  };
  const brokeragePositions = (brokeragePortfolio?.positions ?? [])
    .filter((position) => selectedBrokerageKey === "all" || position.fundAccountId === selectedBrokerageKey);
  const selectedBrokerageStableId =
    brokeragePositions.find((position) => brokeragePositionId(position) === selectedBrokeragePositionId) !== undefined
      ? selectedBrokeragePositionId
      : brokeragePositions.length > 0
        ? brokeragePositionId(brokeragePositions[0])
        : null;
  const brokerageConnectionState = brokerageConnection?.state ?? "NotConfigured";
  const brokerageConnectionWarnings = [
    ...(brokerageConnection?.warnings ?? []),
    ...(brokeragePortfolio?.warnings ?? [])
  ];
  const brokerageWarningRows = buildBrokerageWarningRows(brokerageConnection, brokeragePortfolio, providerLabel);
  const brokerageTrustSnapshot = buildBrokerageTrustSnapshot({
    portfolio: brokeragePortfolio,
    providerLabel,
    warningCount: brokerageWarningRows.length,
    connectionState: brokerageConnectionState
  });
  const selectedId =
    positions.find((p, index) => positionId(p.symbol, p.side, index) === selectedPositionId) !== undefined
      ? selectedPositionId
      : positions.length > 0
        ? positionId(positions[0].symbol, positions[0].side, 0)
        : null;
  const selectedRunStableId =
    runs.find((run) => run.id === selectedRunId) !== undefined
      ? selectedRunId
      : runs.length > 0
        ? runs[0].id
        : null;

  const positionRows: PortfolioPositionRow[] = positions.map((p, index) => {
    const id = positionId(p.symbol, p.side, index);
    const tone = pnlTone(p.unrealizedPnl);
    const isSelected = id === selectedId;

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
      isSelected,
      expanded: isSelected,
      detailPanelId: "portfolio-position-detail",
      selectAriaLabel: `Inspect ${p.symbol} ${p.side} holding`,
      ariaLabel: `${p.symbol} ${p.side} position: ${p.quantity} shares, exposure ${p.exposure}, unrealized P&L ${p.unrealizedPnl}`
    };
  });

  const runRows: PortfolioRunRow[] = runs.map((r) => {
    const tone = pnlTone(r.pnl);
    const isSelected = r.id === selectedRunStableId;

    return {
      id: r.id,
      strategyName: r.strategyName,
      engine: r.engine,
      mode: r.mode,
      modeBadgeVariant: modeBadgeVariant(r.mode),
      status: r.status,
      pnl: r.pnl,
      pnlTone: tone,
      sharpe: r.sharpe,
      dataset: r.dataset,
      window: r.window,
      lastUpdated: r.lastUpdated,
      notes: r.notes,
      promotionState: r.promotionState,
      isSelected,
      expanded: isSelected,
      detailPanelId: "portfolio-run-detail",
      selectAriaLabel: `Inspect ${r.strategyName} run evidence`,
      ariaLabel: `${r.strategyName} ${r.mode} run: ${r.status}, P&L ${r.pnl}, Sharpe ${r.sharpe}`
    };
  });

  const totalExposure = sumNumericStrings(positions.map((p) => p.exposure));
  const totalUnrealizedPnl = sumNumericStrings(positions.map((p) => p.unrealizedPnl));
  const selectedRow = positionRows.find((row) => row.id === selectedId) ?? null;
  const selectedPosition = selectedRow
    ? buildSelectedPositionDetail(selectedRow, risk, brokerage)
    : null;
  const selectedRunRow = runRows.find((row) => row.id === selectedRunStableId) ?? null;
  const selectedContinuity = resolveSelectedRunContinuity(selectedRunRow, selectedRunContinuity);
  const selectedRun = selectedRunRow
    ? buildSelectedRunDetail(selectedRunRow, selectedContinuity)
    : null;
  const runComparisonSummary = buildPortfolioRunComparisonSummary(runRows, selectedRunRow);
  const runDrillInSummary = buildPortfolioRunDrillInSummary(selectedRunRow, selectedRunDrillIn);
  const brokeragePositionRows = brokeragePositions.map((position) =>
    toBrokeragePositionRow(position, brokerageAccounts, selectedBrokerageStableId)
  );
  const brokerageAccountRows = brokerageAccounts.map((account) =>
    toBrokerageAccountRow(account, selectedBrokerageKey)
  );
  const selectedBrokerageAccountRecord =
    selectedBrokerageKey === "all"
      ? null
      : brokerageAccounts.find((account) => account.fundAccountId === selectedBrokerageKey) ?? null;
  const selectedBrokerageAccount = selectedBrokerageAccountRecord
    ? buildSelectedBrokerageAccountDetail(selectedBrokerageAccountRecord, providerLabel)
    : buildAllBrokerageAccountsDetail(brokerageAccounts, brokeragePortfolio, providerLabel);
  const selectedBrokeragePositionRecord =
    brokeragePositions.find((position) => brokeragePositionId(position) === selectedBrokerageStableId) ?? null;
  const selectedBrokeragePosition = selectedBrokeragePositionRecord
    ? buildSelectedBrokeragePositionDetail(selectedBrokeragePositionRecord, brokerageAccounts, providerLabel)
    : null;

  const fallbackStats = buildPortfolioFallbackMetrics({
    openPositionCount: positions.length,
    totalExposure,
    totalUnrealizedPnl
  });
  const cashVarianceLabel = cashFlow !== null ? formatCurrency(cashFlow.netVariance) : null;

  return {
    metricsFromTrading: portfolio == null && trading !== null,
    metricCards: portfolio?.metrics ?? trading?.metrics ?? [],
    multiAssetCoveragePanel: buildMultiAssetCoveragePanel(multiAssetCoverage),
    positionSourceLabel: portfolio ? "Portfolio workspace" : trading ? "Trading workspace" : "Unavailable",
    fallbackStats,
    headerChips: buildPortfolioHeaderChips({
      openPositionCount: positions.length,
      totalExposure,
      totalUnrealizedPnl,
      hasPositions: positions.length > 0,
      cashVarianceLabel,
      brokeragePortfolio,
      providerLabel
    }),
    workflowTaskPanel: buildWorkflowTaskPanel({
      pathname,
      risk,
      brokerage,
      openPositionCount: positions.length,
      totalExposure,
      totalUnrealizedPnl,
      cashFlow,
      cashVarianceLabel,
      selectedRunId: selectedRunRow?.id ?? null,
      selectedRunName: selectedRunRow?.strategyName ?? null,
      selectedRunContinuity: selectedContinuity
    }),
    brokerageProviderLabel: providerLabel,
    brokeragePanelEyebrow: `${providerLabel} read-only`,
    brokerageConnectionLabel: brokerageConnectionLabel(brokerageConnectionState),
    brokerageConnectionTone: brokerageConnectionTone(brokerageConnectionState, brokerageConnection, brokeragePortfolio),
    brokerageConnectionDetail: brokerageConnectionDetail(brokerageConnection, brokeragePortfolio, providerLabel),
    brokerageConnectionWarnings,
    brokerageTrustSnapshot,
    brokerageWarningRows,
    brokerageWarningCountLabel: `${brokerageWarningRows.length} brokerage warning${brokerageWarningRows.length === 1 ? "" : "s"}`,
    brokerageAccountFilterLabel: `${providerLabel} account filter`,
    brokeragePositionsTableLabel: `${providerLabel} current positions`,
    brokerageAccountOptions,
    selectedBrokerageAccountKey: selectedBrokerageKey,
    selectBrokerageAccount,
    selectAdjacentBrokerageAccount,
    hasBrokerageAccounts: brokerageAccounts.length > 0,
    brokerageAccountRows,
    brokerageAccountsTableLabel: `${providerLabel} brokerage accounts`,
    brokerageAccountDetailId: "portfolio-brokerage-account-detail",
    selectedBrokerageAccount,
    brokerageAccountEmptyText: brokeragePortfolio
      ? `No ${providerLabel} brokerage accounts are available in the household snapshot.`
      : `${providerLabel} portfolio sync has not produced account evidence yet.`,
    hasBrokeragePositions: brokeragePositions.length > 0,
    brokeragePositionRows,
    brokeragePositionDetailId: "portfolio-brokerage-position-detail",
    selectedBrokeragePositionId: selectedBrokerageStableId,
    selectedBrokeragePosition,
    selectBrokeragePosition,
    brokerageEmptyText: brokeragePortfolio
      ? `No ${providerLabel} positions are available for the selected account.`
      : `${providerLabel} portfolio sync has not produced a household projection yet.`,
    brokerageSetupAction: buildBrokerageSetupAction({
      connection: brokerageConnection,
      portfolio: brokeragePortfolio,
      providerLabel
    }),
    hasPositions: positionRows.length > 0,
    positionRows,
    positionListLabel: "Open positions",
    positionCountLabel: `${positionRows.length} position${positionRows.length === 1 ? "" : "s"}`,
    positionDetailId: "portfolio-position-detail",
    selectedPositionChip: { label: "Selected detail", value: selectedPosition?.title ?? "None" },
    runEvidenceChip: { label: "Run evidence", value: buildLinkedRunEvidenceLabel(runRows.length) },
    positionDetailEmptyTitle: "No holding selected",
    positionEmptyText: trading
      ? "No open positions in the active paper session."
      : portfolio
        ? "No open positions in the Portfolio workspace."
        : "Portfolio workspace data unavailable.",
    selectedPosition,
    selectPosition,
    hasRuns: runRows.length > 0,
    runRows,
    runListLabel: "Run-linked equity",
    runCountLabel: `${runRows.length} run${runRows.length === 1 ? "" : "s"}`,
    runDetailId: "portfolio-run-detail",
    selectedRunChip: { label: "Selected run", value: selectedRun?.title ?? "None" },
    runDetailEmptyTitle: "No run selected",
    runEmptyText: strategy
      ? "No runs available. Create a strategy run in the Strategy workspace."
      : portfolio
        ? "No runs available in the Portfolio workspace."
        : "Strategy workspace data unavailable.",
    selectedRun,
    runComparisonSummary,
    runDrillInSummary,
    selectRun,
    cashFlowSummary: cashFlow?.summary ?? null,
    cashVarianceLabel,
    cashFlowTone: cashFlow?.tone ?? "default",
    openPositionCount: positions.length
  };
}

export function buildPortfolioFallbackMetrics({
  openPositionCount,
  totalExposure,
  totalUnrealizedPnl
}: {
  openPositionCount: number;
  totalExposure: number;
  totalUnrealizedPnl: number;
}): MetricSnapshot[] {
  const hasPositions = openPositionCount > 0;

  return [
    {
      id: "portfolio-total-exposure",
      label: "Total exposure",
      value: hasPositions ? formatCurrency(totalExposure) : "—",
      delta: hasPositions ? "From open positions" : "No holdings",
      tone: "default"
    },
    {
      id: "portfolio-unrealized-pnl",
      label: "Unrealized P&L",
      value: hasPositions
        ? (totalUnrealizedPnl >= 0 ? "+" : "") + formatCurrency(totalUnrealizedPnl)
        : "—",
      delta: hasPositions ? "Open position mark" : "No holdings",
      tone: hasPositions ? numericPnlTone(totalUnrealizedPnl) : "default"
    },
    {
      id: "portfolio-cash",
      label: "Cash",
      value: "—",
      delta: "Awaiting portfolio cash feed",
      tone: "warning"
    },
    {
      id: "portfolio-open-positions",
      label: "Open positions",
      value: String(openPositionCount),
      delta: hasPositions ? "Selectable detail" : "No holdings",
      tone: hasPositions ? "success" : "default"
    }
  ];
}

function buildBrokerageSetupAction({
  connection,
  portfolio,
  providerLabel
}: {
  connection: BrokerageConnectionStatus | null | undefined;
  portfolio: BrokerageHouseholdPortfolio | null | undefined;
  providerLabel: string;
}): PortfolioBrokerageSetupAction | null {
  const connectionNeedsSetup = connection?.isConnected !== true;
  const portfolioNeedsSetup = !portfolio || portfolio.accounts.length === 0;
  if (!connectionNeedsSetup && !portfolioNeedsSetup) {
    return null;
  }

  return {
    label: "Open provider setup",
    href: WORKSTATION_ROUTE_CATALOG.settingsAlpacaProviderSetup,
    ariaLabel: `Open ${providerLabel} provider setup from Portfolio brokerage panel`,
    detail: connectionNeedsSetup
      ? `Verify ${providerLabel} credentials before accepting brokerage portfolio state.`
      : `Review ${providerLabel} sync setup before accepting an empty household portfolio.`
  };
}

function buildBrokerageTrustSnapshot({
  portfolio,
  providerLabel,
  warningCount,
  connectionState
}: {
  portfolio: BrokerageHouseholdPortfolio | null | undefined;
  providerLabel: string;
  warningCount: number;
  connectionState: BrokerageConnectionStatus["state"];
}): PortfolioBrokerageTrustSnapshot {
  const regionLabel = `${providerLabel} brokerage sync snapshot`;

  if (!portfolio) {
    return {
      regionLabel,
      title: `${providerLabel} household snapshot`,
      statusLabel: connectionState === "NotConfigured" ? "Provider setup needed" : "Awaiting sync",
      statusTone: connectionState === "NotConfigured" ? "warning" : "default",
      summary: `No ${providerLabel} household snapshot has loaded yet. Connect the provider or run brokerage sync before accepting live portfolio state.`,
      chips: [
        { label: "Snapshot", value: "Unavailable" },
        { label: "Connection", value: brokerageConnectionLabel(connectionState) },
        { label: "Issues", value: formatCountLabel(warningCount, "issue") }
      ],
      fields: [
        { label: "As of", value: "Unavailable", tone: "warning" },
        { label: "Accounts", value: "0", tone: "muted" },
        { label: "Positions", value: "0", tone: "muted" },
        { label: "Warnings", value: formatCountLabel(warningCount, "warning"), tone: warningCount > 0 ? "warning" : "muted" }
      ]
    };
  }

  const unhealthyCount = portfolio.accounts.filter((account) => account.health !== "Healthy").length;
  const issueCount = warningCount + unhealthyCount;
  const hasAccounts = portfolio.accounts.length > 0;
  const statusTone: PortfolioBrokerageTrustSnapshot["statusTone"] = issueCount > 0
    ? "warning"
    : hasAccounts
      ? "success"
      : "warning";
  const statusLabel = issueCount > 0
    ? "Review sync"
    : hasAccounts
      ? "Household synced"
      : "Awaiting accounts";
  const accountHealthLabel = unhealthyCount > 0
    ? `${unhealthyCount} account${unhealthyCount === 1 ? "" : "s"} need review`
    : hasAccounts
      ? formatCountLabel(portfolio.accounts.length, "healthy account")
      : "No accounts";

  return {
    regionLabel,
    title: `${providerLabel} household snapshot`,
    statusLabel,
    statusTone,
    summary: `${providerLabel} snapshot was generated at ${formatDateTime(portfolio.asOf)} with ${formatCurrency(portfolio.totalEquity)} equity, ${formatCurrency(portfolio.totalCash)} cash, and ${formatCurrency(portfolio.totalBuyingPower)} buying power.`,
    chips: [
      { label: "Accounts", value: formatCountLabel(portfolio.accounts.length, "account") },
      { label: "Positions", value: formatCountLabel(portfolio.positions.length, "position") },
      { label: "Currency", value: portfolio.currency },
      { label: "Issues", value: formatCountLabel(issueCount, "issue") }
    ],
    fields: [
      { label: "As of", value: formatDateTime(portfolio.asOf), tone: "muted" },
      { label: "Total equity", value: formatCurrency(portfolio.totalEquity), tone: "default" },
      { label: "Cash", value: formatCurrency(portfolio.totalCash), tone: "default" },
      { label: "Buying power", value: formatCurrency(portfolio.totalBuyingPower), tone: "default" },
      { label: "Account health", value: accountHealthLabel, tone: unhealthyCount > 0 ? "warning" : "success" },
      { label: "Warnings", value: formatCountLabel(warningCount, "warning"), tone: warningCount > 0 ? "warning" : "success" }
    ]
  };
}

export function buildLinkedRunEvidenceLabel(runCount: number): string {
  if (runCount <= 0) {
    return "No linked runs";
  }

  return `${runCount} linked run${runCount === 1 ? "" : "s"}`;
}

export function buildPortfolioRunComparisonSummary(
  runs: PortfolioRunRow[],
  selectedRun: PortfolioRunRow | null
): PortfolioRunComparisonSummary {
  const rankedByPnl = runs
    .map((run) => ({ run, pnl: parseNumericValue(run.pnl), sharpe: parseNumericValue(run.sharpe) }))
    .filter((item) => item.pnl !== null || item.sharpe !== null);
  const bestPnl = rankedByPnl
    .filter((item) => item.pnl !== null)
    .sort((a, b) => (b.pnl ?? Number.NEGATIVE_INFINITY) - (a.pnl ?? Number.NEGATIVE_INFINITY))[0] ?? null;
  const weakestPnl = rankedByPnl
    .filter((item) => item.pnl !== null)
    .sort((a, b) => (a.pnl ?? Number.POSITIVE_INFINITY) - (b.pnl ?? Number.POSITIVE_INFINITY))[0] ?? null;
  const bestSharpe = rankedByPnl
    .filter((item) => item.sharpe !== null)
    .sort((a, b) => (b.sharpe ?? Number.NEGATIVE_INFINITY) - (a.sharpe ?? Number.NEGATIVE_INFINITY))[0] ?? null;
  const modeSet = distinctNormalizedValues(runs.map((run) => run.mode));
  const engineSet = distinctNormalizedValues(runs.map((run) => run.engine));
  const selectedPnl = selectedRun ? parseNumericValue(selectedRun.pnl) : null;
  const selectedRank = selectedRun && selectedPnl !== null
    ? rankedByPnl
      .filter((item) => item.pnl !== null)
      .sort((a, b) => (b.pnl ?? Number.NEGATIVE_INFINITY) - (a.pnl ?? Number.NEGATIVE_INFINITY))
      .findIndex((item) => item.run.id === selectedRun.id) + 1
    : 0;
  const hasCrossMode = modeSet.length > 1;
  const hasCrossEngine = engineSet.length > 1;

  return {
    ariaLabel: "Portfolio run comparison summary",
    title: "Run comparison evidence",
    description: runs.length > 0
      ? `${formatCountLabel(runs.length, "strategy run")} compared across ${formatCountLabel(modeSet.length, "mode")} and ${formatCountLabel(engineSet.length, "engine")}.`
      : "No strategy runs are available for portfolio comparison.",
    statusTone: runs.length === 0 ? "warning" : hasCrossMode || hasCrossEngine ? "warning" : "success",
    cards: [
      {
        id: "selected-rank",
        label: "Selected rank",
        value: selectedRun && selectedRank > 0 ? `#${selectedRank}` : "—",
        detail: selectedRun
          ? `${selectedRun.strategyName} vs ${formatCountLabel(runs.length, "linked run")} by P&L.`
          : "Select a run to compare it against the portfolio run set.",
        tone: selectedRank === 1 ? "success" : selectedRank > 0 ? "default" : "warning"
      },
      {
        id: "best-pnl",
        label: "Best P&L",
        value: bestPnl ? bestPnl.run.pnl : "—",
        detail: bestPnl ? `${bestPnl.run.strategyName} (${bestPnl.run.mode}, ${bestPnl.run.engine})` : "No comparable P&L values.",
        tone: bestPnl ? comparisonToneForPnl(bestPnl.run.pnl) : "warning"
      },
      {
        id: "weakest-pnl",
        label: "Weakest P&L",
        value: weakestPnl ? weakestPnl.run.pnl : "—",
        detail: weakestPnl ? `${weakestPnl.run.strategyName} (${weakestPnl.run.mode}, ${weakestPnl.run.engine})` : "No comparable P&L values.",
        tone: weakestPnl ? comparisonToneForPnl(weakestPnl.run.pnl) : "warning"
      },
      {
        id: "best-sharpe",
        label: "Best Sharpe",
        value: bestSharpe ? bestSharpe.run.sharpe : "—",
        detail: bestSharpe ? `${bestSharpe.run.strategyName} risk-adjusted lead.` : "No comparable Sharpe values.",
        tone: bestSharpe ? "success" : "warning"
      },
      {
        id: "coverage",
        label: "Coverage",
        value: `${modeSet.length}/${engineSet.length}`,
        detail: `${modeSet.join(", ") || "No modes"}; ${engineSet.join(", ") || "no engines"}.`,
        tone: hasCrossMode || hasCrossEngine ? "warning" : runs.length > 0 ? "success" : "warning"
      }
    ]
  };
}

export function buildPortfolioRunDrillInSummary(
  selectedRun: PortfolioRunRow | null,
  drillIn: PortfolioRunDrillInData | null
): PortfolioRunDrillInSummary {
  const hasSelectedRun = selectedRun !== null;
  const isCurrentRun = hasSelectedRun && drillIn?.runId === selectedRun.id;
  const isLoading = isCurrentRun && drillIn?.isLoading === true;
  const error = isCurrentRun ? drillIn?.error ?? null : null;
  const attribution = isCurrentRun ? drillIn?.attribution ?? null : null;
  const drawdown = isCurrentRun ? drillIn?.drawdownProfile ?? null : null;
  const cashFlow = isCurrentRun ? drillIn?.cashFlow ?? null : null;
  const trades = isCurrentRun ? drillIn?.trades ?? null : null;
  const loadedCount = [attribution, drawdown, cashFlow, trades].filter(Boolean).length;
  const statusTone = error
    ? "danger"
    : isLoading
      ? "default"
      : loadedCount === 4
        ? "success"
        : loadedCount > 0
          ? "warning"
          : "warning";

  const bridgeRows = buildPortfolioRunBridgeRows(attribution, cashFlow, trades);
  const tradeEvidenceRows = buildPortfolioRunTradeEvidenceRows(trades);

  return {
    ariaLabel: "Selected run portfolio drill-in evidence",
    title: "Portfolio drill-ins",
    description: !hasSelectedRun
      ? "Select a run to load attribution, drawdown, cash-flow, and trade-level evidence."
      : error
        ? `Drill-in evidence failed for ${selectedRun.strategyName}: ${error}`
        : isLoading
          ? `Loading drill-in evidence for ${selectedRun.strategyName}.`
          : loadedCount > 0
            ? `${loadedCount}/4 drill-in evidence slices loaded for ${selectedRun.strategyName}.`
            : `Load shared drill-in evidence for ${selectedRun.strategyName}.`,
    statusTone,
    actionLabel: isLoading ? "Loading drill-ins..." : "Load drill-in evidence",
    actionAriaLabel: hasSelectedRun
      ? `Load portfolio drill-in evidence for ${selectedRun.strategyName}`
      : "Load portfolio drill-in evidence unavailable: no run selected",
    cards: [
      {
        id: "attribution",
        label: "Attribution",
        value: attribution ? formatSignedCurrency(attribution.totalRealizedPnl + attribution.totalUnrealizedPnl) : "—",
        detail: attribution
          ? `Realized ${formatSignedCurrency(attribution.totalRealizedPnl)}; unrealized ${formatSignedCurrency(attribution.totalUnrealizedPnl)}; ${formatCountLabel(attribution.bySymbol.length, "symbol")}.`
          : "Run attribution has not been loaded.",
        tone: attribution ? numericPnlTone(attribution.totalRealizedPnl + attribution.totalUnrealizedPnl) : "warning"
      },
      {
        id: "drawdown",
        label: "Drawdown",
        value: drawdown ? formatPercent(drawdown.maxDrawdownPercent) : "—",
        detail: drawdown
          ? `${formatCountLabel(drawdown.points.length, "equity point")}; recovery ${drawdown.maxDrawdownRecoveryDays} days; final equity ${formatCurrency(drawdown.finalEquity)}.`
          : "Equity curve and drawdown profile have not been loaded.",
        tone: drawdown ? drawdown.maxDrawdownPercent > 0.1 ? "danger" : drawdown.maxDrawdownPercent > 0.03 ? "warning" : "success" : "warning"
      },
      {
        id: "cash-flow",
        label: "Cash-flow",
        value: cashFlow ? formatSignedCurrency(cashFlow.netCashFlow) : "—",
        detail: cashFlow
          ? `${cashFlow.totalEntries} cash-flow ${cashFlow.totalEntries === 1 ? "entry" : "entries"}; inflows ${formatCurrency(cashFlow.totalInflows)}; outflows ${formatCurrency(cashFlow.totalOutflows)}.`
          : "Cash-flow projection has not been loaded.",
        tone: cashFlow ? numericPnlTone(cashFlow.netCashFlow) : "warning"
      },
      {
        id: "trades",
        label: "Trades",
        value: trades ? trades.totalFills.toLocaleString() : "—",
        detail: trades
          ? `${formatCountLabel(trades.fills.length, "fill")} with ${formatCurrency(trades.totalCommissions)} commissions.`
          : "Trade-level fill evidence has not been loaded.",
        tone: trades ? trades.totalFills > 0 ? "success" : "warning" : "warning"
      }
    ],
    bridgeRows,
    tradeEvidenceRows
  };
}

function buildPortfolioRunBridgeRows(
  attribution: RunAttributionSummary | null,
  cashFlow: RunCashFlowSummary | null,
  trades: RunFillSummary | null
): PortfolioRunBridgeRow[] {
  if (!attribution && !cashFlow && !trades) {
    return [];
  }

  const rows: PortfolioRunBridgeRow[] = [];
  if (attribution) {
    const topSymbol = attribution.bySymbol
      .slice()
      .sort((a, b) => Math.abs(b.totalPnl) - Math.abs(a.totalPnl))[0] ?? null;
    rows.push({
      id: "realized-pnl",
      label: "Realized bridge",
      value: formatSignedCurrency(attribution.totalRealizedPnl),
      detail: topSymbol
        ? `${topSymbol.symbol} contributes ${formatSignedCurrency(topSymbol.realizedPnl)} realized P&L across ${formatCountLabel(topSymbol.tradeCount, "trade")}.`
        : "No per-symbol realized attribution rows were returned.",
      tone: numericPnlTone(attribution.totalRealizedPnl)
    });
    rows.push({
      id: "unrealized-pnl",
      label: "Unrealized bridge",
      value: formatSignedCurrency(attribution.totalUnrealizedPnl),
      detail: topSymbol
        ? `${topSymbol.symbol} carries ${formatSignedCurrency(topSymbol.unrealizedPnl)} unrealized P&L into the selected portfolio view.`
        : "No per-symbol unrealized attribution rows were returned.",
      tone: numericPnlTone(attribution.totalUnrealizedPnl)
    });
    rows.push({
      id: "commission-drag",
      label: "Commission drag",
      value: formatCurrency(attribution.totalCommissions),
      detail: `${formatCountLabel(attribution.bySymbol.length, "symbol")} contributes to attribution after commissions and margin-interest allocation.`,
      tone: attribution.totalCommissions > 0 ? "warning" : "success"
    });
  }

  if (cashFlow) {
    rows.push({
      id: "cash-flow-bridge",
      label: "Cash-flow bridge",
      value: formatSignedCurrency(cashFlow.netCashFlow),
      detail: `${formatCountLabel(cashFlow.totalEntries, "cash-flow entry")} tie inflows ${formatCurrency(cashFlow.totalInflows)} to outflows ${formatCurrency(cashFlow.totalOutflows)}.`,
      tone: numericPnlTone(cashFlow.netCashFlow)
    });
  }

  if (trades) {
    rows.push({
      id: "trade-evidence",
      label: "Trade evidence",
      value: formatCountLabel(trades.totalFills, "fill"),
      detail: `${formatCountLabel(trades.fills.length, "retained fill")} available for order, account, fill-price, and commission review.`,
      tone: trades.totalFills > 0 ? "success" : "warning"
    });
  }

  return rows;
}

function buildPortfolioRunTradeEvidenceRows(trades: RunFillSummary | null): PortfolioRunTradeEvidenceRow[] {
  if (!trades) {
    return [];
  }

  return trades.fills
    .slice()
    .sort((a, b) => new Date(b.filledAt).getTime() - new Date(a.filledAt).getTime())
    .slice(0, 5)
    .map((fill) => ({
      id: fill.fillId,
      symbol: fill.symbol,
      quantity: fill.filledQuantity.toLocaleString(),
      price: formatCurrencyPrecise(fill.fillPrice),
      commission: formatCurrencyPrecise(fill.commission),
      filledAt: formatDateTime(fill.filledAt),
      accountId: fill.accountId ?? "No account",
      ariaLabel: `${fill.symbol} fill ${fill.fillId}: ${fill.filledQuantity.toLocaleString()} at ${formatCurrencyPrecise(fill.fillPrice)} on ${formatDateTime(fill.filledAt)}`
    }));
}

function buildPortfolioHeaderChips({
  openPositionCount,
  totalExposure,
  totalUnrealizedPnl,
  hasPositions,
  cashVarianceLabel,
  brokeragePortfolio,
  providerLabel
}: {
  openPositionCount: number;
  totalExposure: number;
  totalUnrealizedPnl: number;
  hasPositions: boolean;
  cashVarianceLabel: string | null;
  brokeragePortfolio: BrokerageHouseholdPortfolio | null | undefined;
  providerLabel: string;
}): PortfolioHeaderChip[] {
  const chips: PortfolioHeaderChip[] = [
    { label: `${providerLabel} equity`, value: brokeragePortfolio ? formatCurrency(brokeragePortfolio.totalEquity) : "—" },
    { label: `${providerLabel} cash`, value: brokeragePortfolio ? formatCurrency(brokeragePortfolio.totalCash) : "—" },
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
  portfolio,
  trading,
  strategy,
  accounting,
  brokerageConnection,
  brokeragePortfolio,
  multiAssetCoverage,
  selectedRunContinuity = null,
  selectedRunDrillIn = null,
  pathname = WORKSTATION_ROUTE_CATALOG.portfolio
}: {
  portfolio?: PortfolioWorkspaceResponse | null;
  trading: TradingWorkspaceResponse | null;
  strategy: StrategyWorkspaceResponse | null;
  accounting: AccountingWorkspaceResponse | null;
  brokerageConnection?: BrokerageConnectionStatus | null;
  brokeragePortfolio?: BrokerageHouseholdPortfolio | null;
  multiAssetCoverage?: MultiAssetCoverageSummary | null;
  selectedRunContinuity?: StrategyRunContinuityDto | null;
  selectedRunDrillIn?: PortfolioRunDrillInData | null;
  pathname?: string;
}): PortfolioScreenViewModel {
  const [selectedPositionId, setSelectedPositionId] = useState<string | null>(null);
  const [selectedRunId, setSelectedRunId] = useState<string | null>(null);
  const [selectedBrokeragePositionId, setSelectedBrokeragePositionId] = useState<string | null>(null);
  const [selectedBrokerageAccountKey, setSelectedBrokerageAccountKey] = useState<string>("all");

  return buildPortfolioScreenViewModel({
    portfolio,
    trading,
    strategy,
    accounting,
    brokerageConnection,
    brokeragePortfolio,
    multiAssetCoverage,
    selectedRunContinuity,
    selectedRunDrillIn,
    pathname,
    selectedPositionId,
    selectedRunId,
    selectedBrokeragePositionId,
    selectedBrokerageAccountKey,
    selectPosition: setSelectedPositionId,
    selectRun: setSelectedRunId,
    selectBrokeragePosition: setSelectedBrokeragePositionId,
    selectBrokerageAccount: setSelectedBrokerageAccountKey
  });
}

export function buildMultiAssetCoveragePanel(
  coverage: MultiAssetCoverageSummary | null | undefined
): PortfolioMultiAssetCoveragePanel | null {
  if (!coverage) {
    return null;
  }

  const rows: PortfolioMultiAssetCoverageRow[] = coverage.assetClasses.map((item) => {
    const evidenceReady = item.evidenceRequirements.filter((requirement) => requirement.status === "Ready").length;
    const evidenceTotal = item.evidenceRequirements.length;
    const evidenceTargets = item.evidenceRequirements.map((requirement) => ({
      id: requirement.requirementId,
      label: requirement.label,
      category: requirement.category,
      statusLabel: multiAssetStatusLabel(requirement.status),
      statusTone: multiAssetStatusTone(requirement.status),
      href: requirement.evidenceRoute,
      requiredLabel: requirement.required ? "Required" : "Optional",
      ariaLabel: `Open ${item.displayName} ${requirement.label} target`
    }));
    const blockerTargets = item.blockers.map((blocker) => ({
      id: blocker.code,
      label: blocker.severity,
      detail: blocker.message,
      source: blocker.source,
      statusTone: blocker.severity === "Blocker" ? "danger" : "warning",
      href: blocker.evidenceRoute,
      ariaLabel: blocker.evidenceRoute
        ? `Open ${item.displayName} ${blocker.source} blocker evidence`
        : `${item.displayName} ${blocker.source} blocker has no drill-through route`
    }));
    const readinessGroup = multiAssetReadinessGroup(item.status);

    return {
      id: item.assetClass,
      assetClass: item.assetClass,
      displayName: item.displayName,
      statusLabel: item.statusLabel,
      statusTone: multiAssetStatusTone(item.status),
      readinessGroupId: readinessGroup.id,
      readinessGroupLabel: readinessGroup.label,
      readinessDetail: multiAssetReadinessDetail(item.status, item.statusLabel, item.blockers.length, evidenceReady, evidenceTotal),
      summary: item.summary,
      evidenceLabel: `${evidenceReady}/${evidenceTotal} ready`,
      blockerLabel: item.blockers.length === 0 ? "None" : `${item.blockers.length} blocker${item.blockers.length === 1 ? "" : "s"}`,
      ledgerLabel: item.ledgerClassification.classification ?? "Ledger classification retained",
      reconciliationLabel: item.reconciliationSignals.breaks ?? "Reconciliation evidence retained",
      evidenceTargets,
      blockerTargets,
      primaryEvidenceRoute: evidenceTargets.find((target) => target.statusTone !== "success")?.href
        ?? evidenceTargets[0]?.href
        ?? coverage.drillThroughRoutes.coverage
        ?? WORKSTATION_API_ENDPOINTS.portfolioMultiAssetCoverage
    };
  });

  const blockerMessages = coverage.assetClasses
    .flatMap((item) => item.blockers.map((blocker) => `${item.displayName}: ${blocker.message}`))
    .slice(0, 4);
  const blockedCount = coverage.assetClasses.filter((item) => item.status === "Blocked").length;
  const reviewCount = coverage.assetClasses.filter((item) => multiAssetReadinessGroup(item.status).id === "review").length;
  const statusTone = blockedCount > 0 ? "danger" : reviewCount > 0 ? "warning" : "success";
  const evidenceRoute = coverage.drillThroughRoutes.coverage ?? WORKSTATION_API_ENDPOINTS.portfolioMultiAssetCoverage;

  return {
    title: "Multi-asset operational coverage",
    description: "Security Master validation, provider evidence, ledger classification, reconciliation signals, and close blockers are rendered from the shared workstation readiness endpoint.",
    statusLabel: blockedCount > 0
      ? `${blockedCount} blocked`
      : reviewCount > 0
        ? `${reviewCount} review`
        : "Ready",
    statusTone,
    chips: coverage.metrics.map((metric) => ({ label: metric.label, value: metric.value })),
    rows,
    groups: buildMultiAssetCoverageGroups(rows),
    blockerMessages,
    evidenceRoute,
    evidenceRouteLabel: `GET ${evidenceRoute}`,
    asOfLabel: `As of ${coverage.asOfUtc}`
  };
}

function multiAssetStatusTone(status: string): PortfolioMultiAssetCoverageRow["statusTone"] {
  if (status === "Ready") return "success";
  if (status === "Blocked") return "danger";
  if (status === "ReviewRequired" || status === "Degraded") return "warning";
  return "default";
}

function multiAssetStatusLabel(status: string): string {
  if (status === "ReviewRequired") return "Review required";
  return status;
}

function multiAssetReadinessGroup(status: string): Pick<PortfolioMultiAssetCoverageGroup, "id" | "label" | "statusTone"> {
  if (status === "Ready") return { id: "ready", label: "Ready", statusTone: "success" };
  if (status === "Blocked") return { id: "blocked", label: "Blocked", statusTone: "danger" };
  if (status === "ReviewRequired" || status === "Degraded") return { id: "review", label: "Review required", statusTone: "warning" };
  return { id: "other", label: "Other state", statusTone: "default" };
}

function multiAssetReadinessDetail(
  status: string,
  statusLabel: string,
  blockerCount: number,
  evidenceReady: number,
  evidenceTotal: number
): string {
  if (status === "Ready") {
    return `${statusLabel}: ${evidenceReady}/${evidenceTotal} evidence targets ready.`;
  }

  const blockerLabel = blockerCount === 0
    ? "no blockers"
    : `${blockerCount} blocker${blockerCount === 1 ? "" : "s"}`;
  return `${statusLabel}: ${evidenceReady}/${evidenceTotal} evidence targets ready with ${blockerLabel}.`;
}

function buildMultiAssetCoverageGroups(rows: PortfolioMultiAssetCoverageRow[]): PortfolioMultiAssetCoverageGroup[] {
  const order = ["blocked", "review", "ready", "other"];
  const groups = order
    .map((id) => {
      const groupRows = rows.filter((row) => row.readinessGroupId === id);
      if (groupRows.length === 0) {
        return null;
      }

      const label = groupRows[0].readinessGroupLabel;
      return {
        id,
        label,
        statusTone: groupRows[0].readinessGroupId === "blocked"
          ? "danger"
          : groupRows[0].readinessGroupId === "ready"
            ? "success"
            : groupRows[0].readinessGroupId === "review"
              ? "warning"
              : "default",
        summary: `${groupRows.length} asset class${groupRows.length === 1 ? "" : "es"}`,
        rows: groupRows
      };
    })
    .filter((group): group is PortfolioMultiAssetCoverageGroup => group !== null);

  return groups;
}

function buildBrokerageAccountOptions(
  accounts: BrokerageHouseholdAccount[],
  selectedKey: string,
  providerLabel: string
): PortfolioBrokerageAccountOption[] {
  const options: PortfolioBrokerageAccountOption[] = [
    {
      key: "all",
      label: "All",
      isSelected: selectedKey === "all",
      tabIndex: selectedKey === "all" ? 0 : -1,
      ariaLabel: `Show all ${providerLabel} accounts`
    }
  ];

  for (const account of accounts) {
    const label = accountKindLabel(account.accountKind);
    options.push({
      key: account.fundAccountId,
      label,
      isSelected: selectedKey === account.fundAccountId,
      tabIndex: selectedKey === account.fundAccountId ? 0 : -1,
      ariaLabel: `Show ${providerLabel} ${label} account`
    });
  }

  return options;
}

function toPortfolioRunRecord(run: PortfolioWorkspaceResponse["runs"][number]): PortfolioSourceRun {
  return {
    id: run.runId,
    strategyName: run.strategyName,
    engine: run.engine,
    mode: run.mode,
    status: run.status,
    dataset: run.dataset,
    window: run.window,
    pnl: run.pnl,
    sharpe: run.sharpe,
    lastUpdated: run.lastUpdated,
    notes: run.notes,
    promotionState: run.promotionState
  };
}

export function resolveBrokerageAccountFilterKeyCommand(
  key: string
): "next" | "previous" | "first" | "last" | null {
  if (key === "ArrowRight" || key === "ArrowDown") {
    return "next";
  }

  if (key === "ArrowLeft" || key === "ArrowUp") {
    return "previous";
  }

  if (key === "Home") {
    return "first";
  }

  if (key === "End") {
    return "last";
  }

  return null;
}

function nextBrokerageAccountKey(
  options: PortfolioBrokerageAccountOption[],
  selectedKey: string,
  direction: "next" | "previous" | "first" | "last"
): string {
  if (options.length === 0) {
    return selectedKey;
  }

  if (direction === "first") {
    return options[0].key;
  }

  if (direction === "last") {
    return options[options.length - 1].key;
  }

  const selectedIndex = Math.max(0, options.findIndex((option) => option.key === selectedKey));
  const offset = direction === "next" ? 1 : -1;
  const nextIndex = (selectedIndex + offset + options.length) % options.length;
  return options[nextIndex].key;
}

function toBrokerageAccountRow(
  account: BrokerageHouseholdAccount,
  selectedAccountKey: string
): PortfolioBrokerageAccountRow {
  const kind = accountKindLabel(account.accountKind);
  const isSelected = account.fundAccountId === selectedAccountKey;
  const warningCount = account.warnings.length;
  return {
    id: account.fundAccountId,
    label: account.displayName,
    kind,
    health: account.health,
    healthBadgeVariant: brokerageAccountHealthBadgeVariant(account.health),
    equity: formatCurrency(account.equity),
    cash: formatCurrency(account.cash),
    buyingPower: formatCurrency(account.buyingPower),
    syncedAt: formatDateTime(account.syncedAt),
    positionCount: formatCountLabel(account.positionCount, "position"),
    warningCount: formatCountLabel(warningCount, "warning"),
    hasWarning: warningCount > 0,
    warningText: warningCount > 0 ? account.warnings.join(" ") : "No account sync warnings.",
    rowClassName: brokerageAccountRowClassName(account.health, warningCount),
    isSelected,
    expanded: isSelected,
    detailPanelId: "portfolio-brokerage-account-detail",
    selectAriaLabel: `Filter brokerage positions to ${kind} account`,
    ariaLabel: `${kind} brokerage account ${account.displayName}: ${account.health}, equity ${formatCurrency(account.equity)}, cash ${formatCurrency(account.cash)}, ${formatCountLabel(warningCount, "warning")}`
  };
}

function brokerageAccountHealthBadgeVariant(
  health: string
): "outline" | "success" | "warning" | "danger" {
  const normalized = health.trim().toLowerCase();
  if (normalized === "healthy") return "success";
  if (normalized === "failed" || normalized === "error" || normalized === "critical") return "danger";
  if (normalized === "unknown" || normalized === "") return "outline";
  return "warning";
}

function brokerageAccountStatusTone(health: string): PortfolioBrokerageAccountDetail["statusTone"] {
  const variant = brokerageAccountHealthBadgeVariant(health);
  return variant === "outline" ? "default" : variant;
}

function brokerageAccountRowClassName(health: string, warningCount: number): string {
  if (warningCount > 0) {
    return "bg-warning/5";
  }

  const variant = brokerageAccountHealthBadgeVariant(health);
  if (variant === "danger") {
    return "bg-danger/5";
  }

  if (variant === "warning") {
    return "bg-warning/5";
  }

  return "bg-background/50";
}

function buildSelectedBrokerageAccountDetail(
  account: BrokerageHouseholdAccount,
  providerLabel: string
): PortfolioBrokerageAccountDetail {
  const kind = accountKindLabel(account.accountKind);
  const warningCount = account.warnings.length;
  const statusTone: PortfolioBrokerageAccountDetail["statusTone"] = warningCount > 0
    ? "warning"
    : brokerageAccountStatusTone(account.health);
  return {
    id: account.fundAccountId,
    title: kind,
    subtitle: `${providerLabel} / ${account.displayName}`,
    ariaLabel: `${kind} brokerage account detail`,
    statusTitle: warningCount > 0 ? "Account sync warning" : "Account sync posture",
    statusDetail: warningCount > 0
      ? account.warnings.join(" ")
      : `${kind} account is ${account.health.toLowerCase()} with ${formatCountLabel(account.positionCount, "position")} and ${formatCountLabel(account.cashTransactionCount, "cash transaction")} in the latest household snapshot.`,
    statusTone,
    statusBadgeLabel: warningCount > 0 ? "Review" : account.health,
    statusBadgeVariant: brokerageAccountStatusBadgeVariant(statusTone),
    fields: [
      { label: "Fund account", value: account.fundAccountId, tone: "muted" },
      { label: "External account", value: account.externalAccountId, tone: "muted" },
      { label: "Equity", value: formatCurrency(account.equity), tone: "default" },
      { label: "Cash", value: formatCurrency(account.cash), tone: "default" },
      { label: "Buying power", value: formatCurrency(account.buyingPower), tone: "default" },
      { label: "Positions", value: formatCountLabel(account.positionCount, "position"), tone: "muted" },
      { label: "Cash activity", value: formatCountLabel(account.cashTransactionCount, "cash transaction"), tone: "muted" },
      { label: "Synced", value: formatDateTime(account.syncedAt), tone: "muted" }
    ]
  };
}

function buildAllBrokerageAccountsDetail(
  accounts: BrokerageHouseholdAccount[],
  portfolio: BrokerageHouseholdPortfolio | null | undefined,
  providerLabel: string
): PortfolioBrokerageAccountDetail {
  const accountWarningCount = accounts.reduce((sum, account) => sum + account.warnings.length, 0);
  const warningCount = accountWarningCount + (portfolio?.warnings.length ?? 0);
  const hasAccounts = accounts.length > 0;
  const statusTone: PortfolioBrokerageAccountDetail["statusTone"] = !hasAccounts
    ? "danger"
    : warningCount > 0
      ? "warning"
      : "success";
  const latestSync = latestAccountSync(accounts);

  return {
    id: "all",
    title: "All brokerage accounts",
    subtitle: `${providerLabel} household account scope`,
    ariaLabel: "All brokerage accounts detail",
    statusTitle: hasAccounts ? "Household account scope" : "No account evidence",
    statusDetail: hasAccounts
      ? `Positions table is showing all ${providerLabel} brokerage accounts with ${formatCountLabel(warningCount, "warning")} in the latest household snapshot.`
      : `${providerLabel} portfolio sync has not produced account evidence yet.`,
    statusTone,
    statusBadgeLabel: hasAccounts ? (warningCount > 0 ? "Review" : "Synced") : "Missing",
    statusBadgeVariant: brokerageAccountStatusBadgeVariant(statusTone),
    fields: [
      { label: "Accounts", value: formatCountLabel(accounts.length, "account"), tone: hasAccounts ? "default" : "warning" },
      { label: "Equity", value: formatCurrency(portfolio?.totalEquity ?? sumAccountValue(accounts, "equity")), tone: "default" },
      { label: "Cash", value: formatCurrency(portfolio?.totalCash ?? sumAccountValue(accounts, "cash")), tone: "default" },
      { label: "Buying power", value: formatCurrency(portfolio?.totalBuyingPower ?? sumAccountValue(accounts, "buyingPower")), tone: "default" },
      { label: "Warnings", value: formatCountLabel(warningCount, "warning"), tone: warningCount > 0 ? "warning" : "success" },
      { label: "Latest sync", value: latestSync, tone: latestSync === "—" ? "warning" : "muted" }
    ]
  };
}

function brokerageAccountStatusBadgeVariant(
  tone: PortfolioBrokerageAccountDetail["statusTone"]
): PortfolioBrokerageAccountDetail["statusBadgeVariant"] {
  return tone === "default" ? "outline" : tone;
}

function sumAccountValue(
  accounts: BrokerageHouseholdAccount[],
  key: "equity" | "cash" | "buyingPower"
): number {
  return accounts.reduce((sum, account) => sum + account[key], 0);
}

function latestAccountSync(accounts: BrokerageHouseholdAccount[]): string {
  const latest = accounts
    .map((account) => new Date(account.syncedAt))
    .filter((date) => !Number.isNaN(date.getTime()))
    .sort((a, b) => b.getTime() - a.getTime())[0];

  return latest ? formatDateTime(latest.toISOString()) : "—";
}

function toBrokeragePositionRow(
  position: BrokerageHouseholdPosition,
  accounts: BrokerageHouseholdAccount[],
  selectedId: string | null
): PortfolioBrokeragePositionRow {
  const account = accounts.find((candidate) => candidate.fundAccountId === position.fundAccountId);
  const pnl = formatSignedCurrency(position.unrealizedPnl);
  const id = brokeragePositionId(position);
  const accountKind = accountKindLabel(position.accountKind);
  const accountLabel = account?.displayName ?? accountKind;
  return {
    id,
    accountLabel,
    accountKind,
    symbol: position.symbol,
    quantity: formatNumber(position.quantity),
    averagePrice: formatCurrencyPrecise(position.averageEntryPrice),
    markPrice: formatCurrencyPrecise(position.marketPrice),
    marketValue: formatCurrency(position.marketValue),
    unrealizedPnl: pnl,
    pnlTone: pnlTone(pnl),
    assetClass: position.assetClass,
    securityCoverage: position.security ? "Covered" : "Missing",
    rowClassName: position.security ? "bg-background/50" : "bg-warning/5",
    isSelected: id === selectedId,
    detailPanelId: "portfolio-brokerage-position-detail",
    expanded: id === selectedId,
    selectAriaLabel: `Inspect ${position.symbol} ${accountKind} live position`,
    ariaLabel: `${position.symbol} ${accountKind} brokerage position: ${formatNumber(position.quantity)} shares, market value ${formatCurrency(position.marketValue)}, unrealized P&L ${pnl}`
  };
}

function brokeragePositionId(position: BrokerageHouseholdPosition): string {
  return `${position.fundAccountId}-${position.symbol}-${position.positionId ?? "position"}`;
}

function buildSelectedBrokeragePositionDetail(
  position: BrokerageHouseholdPosition,
  accounts: BrokerageHouseholdAccount[],
  providerLabel: string
): PortfolioBrokeragePositionDetail {
  const account = accounts.find((candidate) => candidate.fundAccountId === position.fundAccountId);
  const accountKind = accountKindLabel(position.accountKind);
  const accountLabel = account?.displayName ?? accountKind;
  const pnl = formatSignedCurrency(position.unrealizedPnl);
  const pnlStatusTone = numericPnlTone(position.unrealizedPnl);
  const coverageTone: PortfolioBrokeragePositionDetail["statusTone"] = position.security ? "success" : "warning";
  const statusTone = position.security ? pnlStatusTone : "warning";
  const statusBadgeVariant = coverageTone === "success" ? "success" : "warning";
  const coverageLabel = position.security ? "Covered" : "Security master missing";

  return {
    id: brokeragePositionId(position),
    title: position.symbol,
    subtitle: `${providerLabel} / ${accountLabel} / ${position.assetClass}`,
    ariaLabel: `${position.symbol} brokerage position detail`,
    statusTitle: "Brokerage position inspector",
    statusDetail: `${formatNumber(position.quantity)} ${position.symbol} shares in ${accountLabel} with ${formatCurrency(position.marketValue)} market value and ${pnl} unrealized P&L.`,
    statusTone,
    statusBadgeLabel: coverageLabel,
    statusBadgeVariant,
    fields: [
      { label: "Account", value: accountLabel, tone: "default" },
      { label: "Account kind", value: accountKind, tone: "muted" },
      { label: "Quantity", value: formatNumber(position.quantity), tone: "default" },
      { label: "Average entry", value: formatCurrencyPrecise(position.averageEntryPrice), tone: "muted" },
      { label: "Mark price", value: formatCurrencyPrecise(position.marketPrice), tone: "muted" },
      { label: "Market value", value: formatCurrency(position.marketValue), tone: "default" },
      { label: "Unrealized P&L", value: pnl, tone: pnlStatusTone },
      { label: "Security coverage", value: coverageLabel, tone: coverageTone },
      { label: "Position ID", value: position.positionId ?? "Unavailable", tone: position.positionId ? "muted" : "warning" },
      { label: "Currency", value: position.currency, tone: "muted" }
    ]
  };
}

function brokerageConnectionLabel(state: BrokerageConnectionStatus["state"]): string {
  switch (state) {
    case "Connected":
      return "Connected";
    case "AuthorizationPending":
      return "Authorization pending";
    case "ReauthorizationRequired":
      return "Reauthorization required";
    case "Degraded":
      return "Connection degraded";
    case "Disconnected":
      return "Disconnected";
    default:
      return "Not configured";
  }
}

function brokerageConnectionTone(
  state: BrokerageConnectionStatus["state"],
  connection: BrokerageConnectionStatus | null | undefined,
  portfolio: BrokerageHouseholdPortfolio | null | undefined
): PortfolioScreenViewModel["brokerageConnectionTone"] {
  if (state === "Degraded" || state === "ReauthorizationRequired") return "danger";
  if (hasBrokerageWarnings(connection, portfolio)) return "warning";
  if (state === "Connected" && portfolio && portfolio.accounts.length > 0) return "success";
  if (state === "AuthorizationPending" || state === "Disconnected") return "warning";
  return "default";
}

function hasBrokerageWarnings(
  connection: BrokerageConnectionStatus | null | undefined,
  portfolio: BrokerageHouseholdPortfolio | null | undefined
): boolean {
  return Boolean(
    connection?.warnings.length ||
      portfolio?.warnings.length ||
      portfolio?.accounts.some((account) => account.warnings.length > 0)
  );
}

function buildBrokerageWarningRows(
  connection: BrokerageConnectionStatus | null | undefined,
  portfolio: BrokerageHouseholdPortfolio | null | undefined,
  providerLabel: string
): PortfolioBrokerageWarningRow[] {
  const rows: PortfolioBrokerageWarningRow[] = [];

  for (const [index, warning] of (connection?.warnings ?? []).entries()) {
    rows.push({
      id: `connection-${index}`,
      label: `${providerLabel} connection`,
      detail: warning,
      ariaLabel: `${providerLabel} connection warning: ${warning}`
    });
  }

  for (const [index, warning] of (portfolio?.warnings ?? []).entries()) {
    rows.push({
      id: `portfolio-${index}`,
      label: `${providerLabel} portfolio`,
      detail: warning,
      ariaLabel: `${providerLabel} portfolio warning: ${warning}`
    });
  }

  for (const account of portfolio?.accounts ?? []) {
    for (const [index, warning] of account.warnings.entries()) {
      const accountLabel = accountKindLabel(account.accountKind);
      rows.push({
        id: `${account.fundAccountId}-${index}`,
        label: `${accountLabel} account`,
        detail: warning,
        ariaLabel: `${providerLabel} ${accountLabel} account warning: ${warning}`
      });
    }
  }

  return rows;
}

function brokerageConnectionDetail(
  connection: BrokerageConnectionStatus | null | undefined,
  portfolio: BrokerageHouseholdPortfolio | null | undefined,
  providerLabel: string
): string {
  if (connection?.isConnected && portfolio) {
    return `${portfolio.accounts.length} ${providerLabel} account${portfolio.accounts.length === 1 ? "" : "s"} synced with ${formatCurrency(portfolio.totalEquity)} total equity.`;
  }

  if (connection?.authorizationUrl) {
    return `${providerLabel} authorization is ready with the configured provider.`;
  }

  return connection?.warnings[0] ?? `Connect ${providerLabel}, then discover, link, and sync accounts.`;
}

function brokerageProviderLabel(
  connection: BrokerageConnectionStatus | null | undefined,
  portfolio: BrokerageHouseholdPortfolio | null | undefined
): string {
  const displayName = connection?.displayName?.trim();
  if (displayName) {
    return displayName;
  }

  const providerId = portfolio?.providerId?.trim();
  if (providerId) {
    return humanizeProviderId(providerId);
  }

  return "Alpaca paper";
}

function humanizeProviderId(providerId: string): string {
  if (providerId.toLowerCase() === "alpaca") return "Alpaca";
  if (providerId.toLowerCase() === "robinhood") return "Robinhood";
  return providerId;
}

function accountKindLabel(kind: BrokerageHouseholdAccount["accountKind"]): string {
  switch (kind) {
    case "RothIra":
      return "Roth IRA";
    case "TraditionalIra":
      return "Traditional IRA";
    case "TaxableBrokerage":
      return "Brokerage";
    default:
      return "Unknown";
  }
}

function buildWorkflowTaskPanel({
  pathname,
  risk,
  brokerage,
  openPositionCount,
  totalExposure,
  totalUnrealizedPnl,
  cashFlow,
  cashVarianceLabel,
  selectedRunId,
  selectedRunName,
  selectedRunContinuity
}: {
  pathname: string;
  risk: PortfolioRiskState | null;
  brokerage: PortfolioBrokerageStatus | null;
  openPositionCount: number;
  totalExposure: number;
  totalUnrealizedPnl: number;
  cashFlow: GovernanceCashFlowSummary | null;
  cashVarianceLabel: string | null;
  selectedRunId: string | null;
  selectedRunName: string | null;
  selectedRunContinuity: StrategyRunContinuityDto | null;
}): PortfolioWorkflowTaskPanel | null {
  const normalizedPathname = normalizePathname(pathname);
  if (normalizedPathname === WORKSTATION_ROUTE_CATALOG.portfolio) {
    return buildPortfolioReadinessTaskPanel({
      risk,
      brokerage,
      openPositionCount,
      totalExposure,
      totalUnrealizedPnl,
      cashFlow,
      cashVarianceLabel,
      selectedRunId,
      selectedRunName,
      selectedRunContinuity
    });
  }

  if (normalizedPathname !== WORKSTATION_ROUTE_CATALOG.portfolioBrokerageSync) {
    return null;
  }

  const hasPosture = risk !== null || brokerage !== null;
  const connected = brokerage?.connection === "Connected";
  const feedsHealthy = brokerage?.orderIngress === "healthy" && brokerage?.fillFeed === "healthy";
  const statusTone: PortfolioWorkflowTaskPanel["statusTone"] = !hasPosture
    ? "danger"
    : connected && feedsHealthy
      ? "success"
      : "warning";
  const statusLabel = !hasPosture
    ? "Portfolio unavailable"
    : connected && feedsHealthy
      ? "Brokerage synced"
      : "Sync review";
  const providerLabel = brokerage
    ? `${brokerage.provider} / ${brokerage.environment}`
    : "Provider unavailable";
  const accountLabel = brokerage?.account ?? "Account unavailable";
  const selectedSummary = !hasPosture
    ? "Portfolio workspace data is unavailable; refresh the workstation backend before accepting brokerage-sync posture."
    : `${providerLabel} account ${accountLabel} is ${brokerage?.connection ?? "unavailable"} with order ingress ${brokerage?.orderIngress ?? "unknown"} and fill feed ${brokerage?.fillFeed ?? "unknown"}.`;

  return {
    regionLabel: "Brokerage sync task",
    eyebrow: "Portfolio workflow",
    title: "Brokerage sync review",
    description: "Review account connection, execution feed health, exposure, and risk posture before accepting portfolio state.",
    statusLabel,
    statusTone,
    selectedSummary,
    actionListLabel: "Brokerage sync next actions",
    chips: [
      { label: "Provider", value: providerLabel },
      { label: "Account", value: accountLabel },
      { label: "Positions", value: String(openPositionCount) },
      { label: "Exposure", value: openPositionCount > 0 ? formatCurrency(totalExposure) : "—" },
      {
        label: "Unrealized P&L",
        value: openPositionCount > 0
          ? (totalUnrealizedPnl >= 0 ? "+" : "") + formatCurrency(totalUnrealizedPnl)
          : "—"
      }
    ],
    statusRows: [
      { label: "Connection", value: brokerage?.connection ?? "Unavailable", tone: connected ? "success" : "warning" },
      { label: "Order ingress", value: brokerage?.orderIngress ?? "Unavailable", tone: brokerage?.orderIngress === "healthy" ? "success" : "warning" },
      { label: "Fill feed", value: brokerage?.fillFeed ?? "Unavailable", tone: brokerage?.fillFeed === "healthy" ? "success" : "warning" },
      { label: "Last heartbeat", value: brokerage?.lastHeartbeat ?? "—", tone: "muted" },
      { label: "Risk state", value: risk?.state ?? "Unavailable", tone: riskFieldTone(risk?.state) },
      { label: "Buying power", value: risk?.buyingPowerUsed ?? "—", tone: "muted" },
      { label: "Cash variance", value: cashVarianceLabel ?? "—", tone: cashVarianceLabel ? "warning" : "muted" },
      {
        label: "Guardrails",
        value: risk?.activeGuardrails.length ? risk.activeGuardrails.join(" · ") : "No active guardrails",
        tone: risk?.activeGuardrails.length ? "warning" : "success"
      }
    ],
    actions: buildWorkflowTaskActions({
      hasPosture,
      connected,
      feedsHealthy,
      providerLabel
    }),
    backendLinks: [
      buildPortfolioBackendLink("workstation-trading", "Trading workspace", WORKSTATION_API_ENDPOINTS.trading),
      buildPortfolioBackendLink("trading-readiness", "Trading readiness", WORKSTATION_API_ENDPOINTS.tradingReadiness),
      buildPortfolioBackendLink("portfolio-aggregate", "Portfolio aggregate", PORTFOLIO_API_ENDPOINTS.aggregate),
      buildPortfolioBackendLink("portfolio-exposure", "Portfolio exposure", PORTFOLIO_API_ENDPOINTS.exposure)
    ]
  };
}

function buildPortfolioReadinessTaskPanel({
  risk,
  brokerage,
  openPositionCount,
  totalExposure,
  totalUnrealizedPnl,
  cashFlow,
  cashVarianceLabel,
  selectedRunId,
  selectedRunName,
  selectedRunContinuity
}: {
  risk: PortfolioRiskState | null;
  brokerage: PortfolioBrokerageStatus | null;
  openPositionCount: number;
  totalExposure: number;
  totalUnrealizedPnl: number;
  cashFlow: GovernanceCashFlowSummary | null;
  cashVarianceLabel: string | null;
  selectedRunId: string | null;
  selectedRunName: string | null;
  selectedRunContinuity: StrategyRunContinuityDto | null;
}): PortfolioWorkflowTaskPanel {
  const hasPosture = risk !== null || brokerage !== null || openPositionCount > 0;
  const connected = brokerage?.connection === "Connected";
  const feedsHealthy = brokerage?.orderIngress === "healthy" && brokerage?.fillFeed === "healthy";
  const hasCashVariance = cashFlow !== null && cashFlow.netVariance !== 0;
  const continuityBlockers = buildRunContinuityBlockers(selectedRunContinuity);
  const hasDangerContinuityBlocker = continuityBlockers.some((blocker) => blocker.tone === "danger");
  const needsReview = !connected || !feedsHealthy || hasCashVariance || !hasPosture || continuityBlockers.length > 0;
  const statusTone: PortfolioWorkflowTaskPanel["statusTone"] = !hasPosture
    ? "danger"
    : hasDangerContinuityBlocker
      ? "danger"
    : needsReview
      ? "warning"
      : "success";
  const statusLabel = !hasPosture
    ? "Portfolio unavailable"
    : hasDangerContinuityBlocker
      ? "Continuity blocked"
    : needsReview
      ? "Review blockers"
      : "Ready for review";
  const providerLabel = brokerage
    ? `${brokerage.provider} / ${brokerage.environment}`
    : "Provider unavailable";
  const accountLabel = brokerage?.account ?? "Account unavailable";
  const selectedSummary = !hasPosture
    ? "Portfolio posture is unavailable. Repair provider setup or refresh workstation data before accepting holdings."
    : continuityBlockers.length > 0
      ? `${providerLabel} account ${accountLabel} is the current portfolio source. Resolve ${formatCountLabel(continuityBlockers.length, "selected-run continuity blocker")} before accepting the portfolio-to-ledger handoff.`
    : `${providerLabel} account ${accountLabel} is the current portfolio source. Review brokerage sync, trading readiness, cash variance, and linked run evidence before accepting holdings.`;

  return {
    regionLabel: "Portfolio readiness handoff",
    eyebrow: "Portfolio readiness",
    title: "Portfolio acceptance handoff",
    description: "Review brokerage sync, paper readiness, cash variance, and linked run evidence before accepting portfolio state.",
    statusLabel,
    statusTone,
    selectedSummary,
    actionListLabel: "Portfolio readiness next actions",
    chips: [
      { label: "Provider", value: providerLabel },
      { label: "Account", value: accountLabel },
      { label: "Positions", value: String(openPositionCount) },
      { label: "Exposure", value: openPositionCount > 0 ? formatCurrency(totalExposure) : "—" },
      {
        label: "Unrealized P&L",
        value: openPositionCount > 0
          ? (totalUnrealizedPnl >= 0 ? "+" : "") + formatCurrency(totalUnrealizedPnl)
          : "—"
      }
    ],
    statusRows: [
      { label: "Connection", value: brokerage?.connection ?? "Unavailable", tone: connected ? "success" : "warning" },
      { label: "Order ingress", value: brokerage?.orderIngress ?? "Unavailable", tone: brokerage?.orderIngress === "healthy" ? "success" : "warning" },
      { label: "Fill feed", value: brokerage?.fillFeed ?? "Unavailable", tone: brokerage?.fillFeed === "healthy" ? "success" : "warning" },
      { label: "Risk state", value: risk?.state ?? "Unavailable", tone: riskFieldTone(risk?.state) },
      { label: "Buying power", value: risk?.buyingPowerUsed ?? "—", tone: "muted" },
      buildRunContinuityStatusRow(selectedRunId, selectedRunContinuity, continuityBlockers),
      { label: "Cash variance", value: cashVarianceLabel ?? "—", tone: hasCashVariance ? "warning" : "success" },
      {
        label: "Guardrails",
        value: risk?.activeGuardrails.length ? risk.activeGuardrails.join(" · ") : "No active guardrails",
        tone: risk?.activeGuardrails.length ? "warning" : "success"
      }
    ],
    actions: buildPortfolioReadinessActions({
      hasPosture,
      connected,
      feedsHealthy,
      selectedRunId,
      selectedRunName
    }),
    backendLinks: [
      buildPortfolioBackendLink("workstation-portfolio", "Portfolio workspace", WORKSTATION_API_ENDPOINTS.portfolio),
      buildPortfolioBackendLink("workstation-trading", "Trading workspace", WORKSTATION_API_ENDPOINTS.trading),
      buildPortfolioBackendLink("trading-readiness", "Trading readiness", WORKSTATION_API_ENDPOINTS.tradingReadiness),
      buildPortfolioBackendLink("portfolio-exposure", "Portfolio exposure", PORTFOLIO_API_ENDPOINTS.exposure)
    ]
  };
}

function buildPortfolioReadinessActions({
  hasPosture,
  connected,
  feedsHealthy,
  selectedRunId,
  selectedRunName
}: {
  hasPosture: boolean;
  connected: boolean;
  feedsHealthy: boolean;
  selectedRunId: string | null;
  selectedRunName: string | null;
}): PortfolioWorkflowTaskAction[] {
  const actions: PortfolioWorkflowTaskAction[] = [];

  if (!hasPosture || !connected || !feedsHealthy) {
    actions.push({
      id: "provider-setup",
      label: "Repair provider setup",
      href: WORKSTATION_ROUTE_CATALOG.settingsAlpacaProviderSetup,
      ariaLabel: "Repair Alpaca provider setup from Portfolio readiness",
      detail: "Verify credentials and connection posture before accepting portfolio state.",
      detailId: portfolioWorkflowTaskActionDetailId("readiness", "provider-setup"),
      variant: "default"
    });
  }

  actions.push({
    id: "brokerage-sync",
    label: "Review brokerage sync",
    href: WORKSTATION_ROUTE_CATALOG.portfolioBrokerageSync,
    ariaLabel: "Open brokerage sync review from Portfolio readiness",
    detail: "Inspect account sync, execution feed health, exposure, and brokerage evidence.",
    detailId: portfolioWorkflowTaskActionDetailId("readiness", "brokerage-sync"),
    variant: actions.length === 0 ? "default" : "outline"
  });

  actions.push({
    id: "trading-readiness",
    label: "Inspect readiness",
    href: WORKSTATION_ROUTE_CATALOG.tradingReadiness,
    ariaLabel: "Open Trading readiness from Portfolio readiness",
    detail: "Check paper-session, replay, execution-control, and readiness evidence.",
    detailId: portfolioWorkflowTaskActionDetailId("readiness", "trading-readiness"),
    variant: "outline"
  });

  if (selectedRunId) {
    const runName = selectedRunName ?? "selected run";
    actions.push({
      id: "evidence",
      label: "Open evidence",
      href: evidenceWorkbenchPath("strategy-run", selectedRunId),
      ariaLabel: `Open ${runName} evidence from Portfolio readiness`,
      detail: "Review the linked strategy-run evidence packet before accepting portfolio state.",
      detailId: portfolioWorkflowTaskActionDetailId("readiness", "evidence"),
      variant: "outline"
    });
  }

  return actions;
}

function buildWorkflowTaskActions({
  hasPosture,
  connected,
  feedsHealthy,
  providerLabel
}: {
  hasPosture: boolean;
  connected: boolean;
  feedsHealthy: boolean;
  providerLabel: string;
}): PortfolioWorkflowTaskAction[] {
  const needsProviderRepair = !hasPosture || !connected || !feedsHealthy;
  const providerSetupTarget =
    providerLabel === "Provider unavailable" ? "provider setup" : `${providerLabel} provider setup`;
  const actions: PortfolioWorkflowTaskAction[] = [];

  if (needsProviderRepair) {
    actions.push({
      id: "provider-setup",
      label: "Repair provider setup",
      href: WORKSTATION_ROUTE_CATALOG.settingsAlpacaProviderSetup,
      ariaLabel: `Repair ${providerSetupTarget} from brokerage sync review`,
      detail: "Verify credentials and connection posture before accepting brokerage-sync state.",
      detailId: portfolioWorkflowTaskActionDetailId("brokerage-sync", "provider-setup"),
      variant: "default"
    });
  }

  actions.push({
    id: "trading-readiness",
    label: "Inspect readiness",
    href: WORKSTATION_ROUTE_CATALOG.tradingReadiness,
    ariaLabel: "Open Trading readiness from brokerage sync review",
    detail: "Check paper-session, replay, execution-control, and readiness evidence.",
    detailId: portfolioWorkflowTaskActionDetailId("brokerage-sync", "trading-readiness"),
    variant: needsProviderRepair ? "outline" : "default"
  });

  actions.push({
    id: "trading-cockpit",
    label: "Open Trading cockpit",
    href: WORKSTATION_ROUTE_CATALOG.trading,
    ariaLabel: "Open Trading cockpit from brokerage sync review",
    detail: "Review active positions, orders, and paper execution controls.",
    detailId: portfolioWorkflowTaskActionDetailId("brokerage-sync", "trading-cockpit"),
    variant: "outline"
  });

  return actions;
}

function portfolioWorkflowTaskActionDetailId(
  panel: "readiness" | "brokerage-sync",
  actionId: PortfolioWorkflowTaskAction["id"]
): string {
  return `portfolio-${panel}-${actionId}-detail`;
}

function buildPortfolioBackendLink(id: string, label: string, href: string): PortfolioBackendLink {
  return {
    id,
    method: "GET",
    label,
    href,
    ariaLabel: `Open GET ${href} backend payload`
  };
}

function normalizePathname(pathname: string): string {
  const normalized = pathname.trim().toLowerCase().replace(/\/+$/, "");
  return normalized === "" ? "/" : normalized;
}

function positionId(symbol: string, side: string, index: number): string {
  return `${symbol.toLowerCase()}-${side.toLowerCase()}-${index}`;
}

function pnlTone(value: string): "success" | "danger" | "default" {
  if (value.startsWith("+")) return "success";
  if (value.startsWith("-")) return "danger";
  return "default";
}

function parseNumericValue(value: string): number | null {
  const normalizedPercent = value.trim().endsWith("%");
  const cleaned = value.replace(/[$+,%]/g, "").trim();
  if (!cleaned || cleaned === "—") {
    return null;
  }

  const parsed = Number.parseFloat(cleaned);
  if (!Number.isFinite(parsed)) {
    return null;
  }

  return normalizedPercent ? parsed / 100 : parsed;
}

function distinctNormalizedValues(values: string[]): string[] {
  return [...new Set(values.map((value) => value.trim()).filter((value) => value.length > 0))].sort();
}

function numericPnlTone(value: number): "success" | "danger" | "default" {
  if (value > 0) return "success";
  if (value < 0) return "danger";
  return "default";
}

function modeBadgeVariant(mode: string): "paper" | "live" | "outline" {
  if (mode === "paper") return "paper";
  if (mode === "live") return "live";
  return "outline";
}

function buildSelectedPositionDetail(
  position: PortfolioPositionRow,
  risk: PortfolioRiskState | null,
  brokerage: PortfolioBrokerageStatus | null
): PortfolioPositionDetail {
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

function buildSelectedRunDetail(run: PortfolioRunRow, continuity: StrategyRunContinuityDto | null): PortfolioRunDetail {
  const continuityBlockers = buildRunContinuityBlockers(continuity);
  const hasDangerContinuityBlocker = continuityBlockers.some((blocker) => blocker.tone === "danger");
  const statusTone = hasDangerContinuityBlocker
    ? "danger"
    : continuityBlockers.length > 0
      ? "warning"
      : runStatusTone(run.status, run.pnlTone);
  const promotionValue = run.promotionState ?? "Not promoted";
  const continuityLabel = buildRunContinuityStatusLabel(run.id, continuity, continuityBlockers);
  const continuityDetail = buildRunContinuityDetail(continuityBlockers);

  return {
    id: run.id,
    title: run.strategyName,
    subtitle: `${run.mode} - ${run.engine} - ${run.id}`,
    ariaLabel: `${run.strategyName} run detail`,
    evidenceAction: {
      label: "Open evidence packet",
      href: evidenceWorkbenchPath("strategy-run", run.id),
      ariaLabel: `Open ${run.strategyName} evidence packet`
    },
    statusTitle: continuityBlockers.length > 0 ? `${run.strategyName} continuity review` : `${run.strategyName} selected`,
    statusDetail: continuityBlockers.length > 0
      ? `${run.status} ${run.mode} run has ${formatCountLabel(continuityBlockers.length, "continuity blocker")}. ${continuityDetail}`
      : `${run.status} ${run.mode} run with ${run.pnl} P&L and ${run.sharpe} Sharpe. ${run.notes || "No operator notes attached."}`,
    statusTone,
    statusBadgeLabel: continuityBlockers.length > 0 ? continuityLabel : run.status,
    statusBadgeVariant: statusTone === "default" ? "outline" : statusTone,
    fields: [
      { label: "Run ID", value: run.id, tone: "muted" },
      { label: "Mode", value: run.mode, tone: run.mode === "live" ? "danger" : run.mode === "paper" ? "warning" : "default" },
      { label: "Engine", value: run.engine, tone: "muted" },
      { label: "Status", value: run.status, tone: statusTone },
      { label: "Dataset", value: run.dataset, tone: "default" },
      { label: "Window", value: run.window, tone: "muted" },
      { label: "P&L", value: run.pnl, tone: pnlFieldTone(run.pnl) },
      { label: "Sharpe", value: run.sharpe, tone: "default" },
      {
        label: "Continuity",
        value: continuityLabel,
        tone: continuityBlockers.length === 0
          ? continuity === null
            ? "muted"
            : "success"
          : hasDangerContinuityBlocker
            ? "danger"
            : "warning"
      },
      { label: "Promotion", value: promotionValue, tone: run.promotionState ? "success" : "warning" },
      { label: "Last updated", value: run.lastUpdated, tone: "muted" }
    ]
  };
}

function resolveSelectedRunContinuity(
  selectedRun: PortfolioRunRow | null,
  continuity: StrategyRunContinuityDto | null
): StrategyRunContinuityDto | null {
  if (!selectedRun || !continuity) {
    return null;
  }

  return continuity.run.summary.runId === selectedRun.id ? continuity : null;
}

function buildRunContinuityStatusRow(
  selectedRunId: string | null,
  continuity: StrategyRunContinuityDto | null,
  blockers: PortfolioRunContinuityBlocker[]
): PortfolioDetailField {
  if (!selectedRunId) {
    return { label: "Run continuity", value: "No selected run", tone: "muted" };
  }

  if (!continuity) {
    return { label: "Run continuity", value: "Not loaded", tone: "muted" };
  }

  const hasDangerBlocker = blockers.some((blocker) => blocker.tone === "danger");
  return {
    label: "Run continuity",
    value: buildRunContinuityStatusLabel(selectedRunId, continuity, blockers),
    tone: blockers.length === 0 ? "success" : hasDangerBlocker ? "danger" : "warning"
  };
}

function buildRunContinuityStatusLabel(
  selectedRunId: string,
  continuity: StrategyRunContinuityDto | null,
  blockers: PortfolioRunContinuityBlocker[]
): string {
  if (!continuity) {
    return selectedRunId ? "Not loaded" : "No selected run";
  }

  if (blockers.length === 0) {
    return "Continuity ready";
  }

  return `${blockers.length} blocker${blockers.length === 1 ? "" : "s"}`;
}

function buildRunContinuityDetail(blockers: PortfolioRunContinuityBlocker[]): string {
  if (blockers.length === 0) {
    return "Portfolio and ledger continuity are ready for review.";
  }

  return blockers.slice(0, 3).map((blocker) => blocker.detail).join(" ");
}

function buildRunContinuityBlockers(continuity: StrategyRunContinuityDto | null): PortfolioRunContinuityBlocker[] {
  if (!continuity) {
    return [];
  }

  const blockers: PortfolioRunContinuityBlocker[] = [];
  const status = continuity.continuityStatus;
  addMissingContinuityBlocker(blockers, !status.hasPortfolio, "missing-portfolio", "Portfolio coverage", "Portfolio read model is missing for the selected run.", "danger");
  addMissingContinuityBlocker(blockers, !status.hasLedger, "missing-ledger", "Ledger coverage", "Ledger read model is missing for the selected run.", "danger");
  addMissingContinuityBlocker(blockers, !status.hasCashFlow, "missing-cash-flow", "Cash-flow coverage", "Cash-flow digest is missing for the selected run.", "warning");
  addMissingContinuityBlocker(blockers, !status.hasReconciliation, "missing-reconciliation", "Reconciliation coverage", "Reconciliation summary is missing for the selected run.", "warning");
  addMissingContinuityBlocker(blockers, status.openReconciliationBreaks > 0, "open-reconciliation-breaks", "Open reconciliation breaks", `${formatCountLabel(status.openReconciliationBreaks, "reconciliation break")} remain open for the selected run.`, "warning");
  addMissingContinuityBlocker(blockers, status.securityCoverageIssueCount > 0, "security-coverage", "Security coverage", `${formatCountLabel(status.securityCoverageIssueCount, "security coverage issue")} remain for the selected run.`, "warning");

  for (const warning of status.warnings) {
    if (blockers.some((blocker) => blocker.code === warning.code)) {
      continue;
    }

    blockers.push({
      code: warning.code,
      label: warning.sourceSeam || warning.code,
      detail: warning.message,
      tone: continuityWarningTone(warning.severity)
    });
  }

  return blockers;
}

function addMissingContinuityBlocker(
  blockers: PortfolioRunContinuityBlocker[],
  condition: boolean,
  code: string,
  label: string,
  detail: string,
  tone: PortfolioRunContinuityBlocker["tone"]
) {
  if (!condition) {
    return;
  }

  blockers.push({ code, label, detail, tone });
}

function continuityWarningTone(severity: StrategyRunContinuityWarningSeverity): PortfolioRunContinuityBlocker["tone"] {
  if (severity === "Critical") {
    return "danger";
  }

  return severity === "Warning" ? "warning" : "muted";
}

function pnlFieldTone(value: string): PortfolioDetailField["tone"] {
  const tone = pnlTone(value);
  if (tone === "success") return "success";
  if (tone === "danger") return "danger";
  return "default";
}

function comparisonToneForPnl(value: string): PortfolioRunComparisonCard["tone"] {
  const tone = pnlTone(value);
  if (tone === "success") return "success";
  if (tone === "danger") return "danger";
  return "default";
}

function runStatusTone(
  status: string,
  pnl: PortfolioRunRow["pnlTone"]
): PortfolioRunDetail["statusTone"] {
  if (status === "Needs Review") return "warning";
  if (status === "Completed") return pnl === "danger" ? "warning" : "success";
  if (status === "Queued" || status === "Running") return "default";
  return pnl === "danger" ? "danger" : "default";
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

function formatSignedCurrency(value: number): string {
  const prefix = value >= 0 ? "+" : "-";
  return `${prefix}${formatCurrency(Math.abs(value))}`;
}

function formatCurrencyPrecise(value: number): string {
  const prefix = value >= 0 ? "$" : "-$";
  return `${prefix}${Math.abs(value).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
}

function formatPercent(value: number): string {
  return `${(value * 100).toLocaleString(undefined, { maximumFractionDigits: 2 })}%`;
}

function formatNumber(value: number): string {
  return value.toLocaleString(undefined, { maximumFractionDigits: 4 });
}

function formatCountLabel(count: number, noun: string): string {
  return `${count} ${noun}${count === 1 ? "" : "s"}`;
}

function formatDateTime(value: string): string {
  const date = new Date(value);
  return Number.isNaN(date.getTime())
    ? "—"
    : `${UTC_MONTH_LABELS[date.getUTCMonth()]} ${date.getUTCDate()}, ${padUtc(date.getUTCHours())}:${padUtc(date.getUTCMinutes())} UTC`;
}

const UTC_MONTH_LABELS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

function padUtc(value: number): string {
  return value.toString().padStart(2, "0");
}
