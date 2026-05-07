import { useState } from "react";
import type {
  BrokerageConnectionStatus,
  BrokerageHouseholdAccount,
  BrokerageHouseholdPortfolio,
  BrokerageHouseholdPosition,
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
  selectAriaLabel: string;
  ariaLabel: string;
}

export interface PortfolioMetricStat {
  label: string;
  value: string;
}

export interface PortfolioHeaderChip {
  label: string;
  value: string;
}

export interface PortfolioBrokerageAccountOption {
  key: string;
  label: string;
  isSelected: boolean;
  ariaLabel: string;
}

export interface PortfolioBrokerageAccountRow {
  id: string;
  label: string;
  kind: string;
  health: string;
  equity: string;
  cash: string;
  buyingPower: string;
  syncedAt: string;
  hasWarning: boolean;
  warningText: string;
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

export interface PortfolioBackendLink {
  id: string;
  method: "GET";
  label: string;
  href: string;
  ariaLabel: string;
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
  backendLinks: PortfolioBackendLink[];
  selectedSummary: string;
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
  statusTitle: string;
  statusDetail: string;
  statusTone: "default" | "success" | "warning" | "danger";
  statusBadgeLabel: string;
  statusBadgeVariant: "outline" | "success" | "warning" | "danger";
  fields: PortfolioDetailField[];
}

export interface PortfolioScreenViewModel {
  metricsFromTrading: boolean;
  metricCards: TradingWorkspaceResponse["metrics"];
  fallbackStats: PortfolioMetricStat[];
  headerChips: PortfolioHeaderChip[];
  workflowTaskPanel: PortfolioWorkflowTaskPanel | null;
  brokerageProviderLabel: string;
  brokeragePanelEyebrow: string;
  brokerageConnectionLabel: string;
  brokerageConnectionTone: "default" | "success" | "warning" | "danger";
  brokerageConnectionDetail: string;
  brokerageConnectionWarnings: string[];
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
  hasBrokeragePositions: boolean;
  brokeragePositionRows: PortfolioBrokeragePositionRow[];
  brokerageEmptyText: string;
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
  runListLabel: string;
  runCountLabel: string;
  runDetailId: string;
  runEmptyText: string;
  selectedRun: PortfolioRunDetail | null;
  selectRun: (id: string) => void;
  cashFlowSummary: string | null;
  cashVarianceLabel: string | null;
  cashFlowTone: "default" | "success" | "warning" | "danger";
  openPositionCount: number;
}

export function buildPortfolioScreenViewModel({
  trading,
  research,
  governance,
  brokerageConnection,
  brokeragePortfolio,
  selectedPositionId = null,
  selectedRunId = null,
  selectedBrokerageAccountKey = "all",
  pathname = "/portfolio",
  selectPosition = () => {},
  selectRun = () => {},
  selectBrokerageAccount = () => {}
}: {
  trading: TradingWorkspaceResponse | null;
  research: ResearchWorkspaceResponse | null;
  governance: GovernanceWorkspaceResponse | null;
  brokerageConnection?: BrokerageConnectionStatus | null;
  brokeragePortfolio?: BrokerageHouseholdPortfolio | null;
  selectedPositionId?: string | null;
  selectedRunId?: string | null;
  selectedBrokerageAccountKey?: string;
  pathname?: string;
  selectPosition?: (id: string) => void;
  selectRun?: (id: string) => void;
  selectBrokerageAccount?: (key: string) => void;
}): PortfolioScreenViewModel {
  const positions = trading?.positions ?? [];
  const runs = research?.runs ?? [];
  const cashFlow = governance?.cashFlow ?? null;
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
  const brokerageConnectionState = brokerageConnection?.state ?? "NotConfigured";
  const brokerageConnectionWarnings = [
    ...(brokerageConnection?.warnings ?? []),
    ...(brokeragePortfolio?.warnings ?? [])
  ];
  const brokerageWarningRows = buildBrokerageWarningRows(brokerageConnection, brokeragePortfolio, providerLabel);
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

  const runRows: PortfolioRunRow[] = runs.map((r) => {
    const tone = pnlTone(r.pnl);

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
      isSelected: r.id === selectedRunStableId,
      selectAriaLabel: `Inspect ${r.strategyName} run evidence`,
      ariaLabel: `${r.strategyName} ${r.mode} run: ${r.status}, P&L ${r.pnl}, Sharpe ${r.sharpe}`
    };
  });

  const totalExposure = sumNumericStrings(positions.map((p) => p.exposure));
  const totalUnrealizedPnl = sumNumericStrings(positions.map((p) => p.unrealizedPnl));
  const selectedRow = positionRows.find((row) => row.id === selectedId) ?? null;
  const selectedPosition = selectedRow
    ? buildSelectedPositionDetail(selectedRow, trading)
    : null;
  const selectedRunRow = runRows.find((row) => row.id === selectedRunStableId) ?? null;
  const selectedRun = selectedRunRow
    ? buildSelectedRunDetail(selectedRunRow)
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
      cashVarianceLabel,
      brokeragePortfolio,
      providerLabel
    }),
    workflowTaskPanel: buildWorkflowTaskPanel({
      pathname,
      trading,
      openPositionCount: positions.length,
      totalExposure,
      totalUnrealizedPnl,
      cashVarianceLabel
    }),
    brokerageProviderLabel: providerLabel,
    brokeragePanelEyebrow: `${providerLabel} read-only`,
    brokerageConnectionLabel: brokerageConnectionLabel(brokerageConnectionState),
    brokerageConnectionTone: brokerageConnectionTone(brokerageConnectionState, brokerageConnection, brokeragePortfolio),
    brokerageConnectionDetail: brokerageConnectionDetail(brokerageConnection, brokeragePortfolio, providerLabel),
    brokerageConnectionWarnings,
    brokerageWarningRows,
    brokerageWarningCountLabel: `${brokerageWarningRows.length} brokerage warning${brokerageWarningRows.length === 1 ? "" : "s"}`,
    brokerageAccountFilterLabel: `${providerLabel} account filter`,
    brokeragePositionsTableLabel: `${providerLabel} current positions`,
    brokerageAccountOptions,
    selectedBrokerageAccountKey: selectedBrokerageKey,
    selectBrokerageAccount,
    selectAdjacentBrokerageAccount,
    hasBrokerageAccounts: brokerageAccounts.length > 0,
    brokerageAccountRows: brokerageAccounts.map(toBrokerageAccountRow),
    hasBrokeragePositions: brokeragePositions.length > 0,
    brokeragePositionRows: brokeragePositions.map((position) => toBrokeragePositionRow(position, brokerageAccounts)),
    brokerageEmptyText: brokeragePortfolio
      ? `No ${providerLabel} positions are available for the selected account.`
      : `${providerLabel} portfolio sync has not produced a household projection yet.`,
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
    runListLabel: "Run-linked equity",
    runCountLabel: `${runRows.length} run${runRows.length === 1 ? "" : "s"}`,
    runDetailId: "portfolio-run-detail",
    runEmptyText: research
      ? "No runs available. Create a strategy run in the Strategy workspace."
      : "Strategy workspace data unavailable.",
    selectedRun,
    selectRun,
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
  trading,
  research,
  governance,
  brokerageConnection,
  brokeragePortfolio,
  pathname = "/portfolio"
}: {
  trading: TradingWorkspaceResponse | null;
  research: ResearchWorkspaceResponse | null;
  governance: GovernanceWorkspaceResponse | null;
  brokerageConnection?: BrokerageConnectionStatus | null;
  brokeragePortfolio?: BrokerageHouseholdPortfolio | null;
  pathname?: string;
}): PortfolioScreenViewModel {
  const [selectedPositionId, setSelectedPositionId] = useState<string | null>(null);
  const [selectedRunId, setSelectedRunId] = useState<string | null>(null);
  const [selectedBrokerageAccountKey, setSelectedBrokerageAccountKey] = useState<string>("all");

  return buildPortfolioScreenViewModel({
    trading,
    research,
    governance,
    brokerageConnection,
    brokeragePortfolio,
    pathname,
    selectedPositionId,
    selectedRunId,
    selectedBrokerageAccountKey,
    selectPosition: setSelectedPositionId,
    selectRun: setSelectedRunId,
    selectBrokerageAccount: setSelectedBrokerageAccountKey
  });
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
      ariaLabel: `Show all ${providerLabel} accounts`
    }
  ];

  for (const account of accounts) {
    const label = accountKindLabel(account.accountKind);
    options.push({
      key: account.fundAccountId,
      label,
      isSelected: selectedKey === account.fundAccountId,
      ariaLabel: `Show ${providerLabel} ${label} account`
    });
  }

  return options;
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

function toBrokerageAccountRow(account: BrokerageHouseholdAccount): PortfolioBrokerageAccountRow {
  return {
    id: account.fundAccountId,
    label: account.displayName,
    kind: accountKindLabel(account.accountKind),
    health: account.health,
    equity: formatCurrency(account.equity),
    cash: formatCurrency(account.cash),
    buyingPower: formatCurrency(account.buyingPower),
    syncedAt: formatDateTime(account.syncedAt),
    hasWarning: account.warnings.length > 0,
    warningText: account.warnings.length > 0 ? account.warnings.join(" ") : "No account sync warnings."
  };
}

function toBrokeragePositionRow(
  position: BrokerageHouseholdPosition,
  accounts: BrokerageHouseholdAccount[]
): PortfolioBrokeragePositionRow {
  const account = accounts.find((candidate) => candidate.fundAccountId === position.fundAccountId);
  const pnl = formatSignedCurrency(position.unrealizedPnl);
  return {
    id: `${position.fundAccountId}-${position.symbol}-${position.positionId ?? "position"}`,
    accountLabel: account?.displayName ?? accountKindLabel(position.accountKind),
    accountKind: accountKindLabel(position.accountKind),
    symbol: position.symbol,
    quantity: formatNumber(position.quantity),
    averagePrice: formatCurrencyPrecise(position.averageEntryPrice),
    markPrice: formatCurrencyPrecise(position.marketPrice),
    marketValue: formatCurrency(position.marketValue),
    unrealizedPnl: pnl,
    pnlTone: pnlTone(pnl),
    assetClass: position.assetClass,
    securityCoverage: position.security ? "Covered" : "Missing",
    ariaLabel: `${position.symbol} ${accountKindLabel(position.accountKind)} position: ${formatNumber(position.quantity)} shares, market value ${formatCurrency(position.marketValue)}, unrealized P&L ${pnl}`
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
  trading,
  openPositionCount,
  totalExposure,
  totalUnrealizedPnl,
  cashVarianceLabel
}: {
  pathname: string;
  trading: TradingWorkspaceResponse | null;
  openPositionCount: number;
  totalExposure: number;
  totalUnrealizedPnl: number;
  cashVarianceLabel: string | null;
}): PortfolioWorkflowTaskPanel | null {
  if (normalizePathname(pathname) !== "/portfolio/brokerage-sync") {
    return null;
  }

  const brokerage = trading?.brokerage ?? null;
  const risk = trading?.risk ?? null;
  const connected = brokerage?.connection === "Connected";
  const feedsHealthy = brokerage?.orderIngress === "healthy" && brokerage?.fillFeed === "healthy";
  const statusTone: PortfolioWorkflowTaskPanel["statusTone"] = trading === null
    ? "danger"
    : connected && feedsHealthy
      ? "success"
      : "warning";
  const statusLabel = trading === null
    ? "Trading unavailable"
    : connected && feedsHealthy
      ? "Brokerage synced"
      : "Sync review";
  const providerLabel = brokerage
    ? `${brokerage.provider} / ${brokerage.environment}`
    : "Provider unavailable";
  const accountLabel = brokerage?.account ?? "Account unavailable";
  const selectedSummary = trading === null
    ? "Trading workspace data is unavailable; refresh the workstation backend before accepting brokerage-sync posture."
    : `${providerLabel} account ${accountLabel} is ${brokerage?.connection ?? "unavailable"} with order ingress ${brokerage?.orderIngress ?? "unknown"} and fill feed ${brokerage?.fillFeed ?? "unknown"}.`;

  return {
    regionLabel: "Brokerage sync task",
    eyebrow: "Portfolio workflow",
    title: "Brokerage sync review",
    description: "Review account connection, execution feed health, exposure, and risk posture before accepting portfolio state.",
    statusLabel,
    statusTone,
    selectedSummary,
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
    backendLinks: [
      {
        id: "workstation-trading",
        method: "GET",
        label: "Trading workspace",
        href: "/api/workstation/trading",
        ariaLabel: "Open GET /api/workstation/trading backend payload"
      },
      {
        id: "trading-readiness",
        method: "GET",
        label: "Trading readiness",
        href: "/api/workstation/trading/readiness",
        ariaLabel: "Open GET /api/workstation/trading/readiness backend payload"
      },
      {
        id: "portfolio-aggregate",
        method: "GET",
        label: "Portfolio aggregate",
        href: "/api/portfolio/aggregate",
        ariaLabel: "Open GET /api/portfolio/aggregate backend payload"
      },
      {
        id: "portfolio-exposure",
        method: "GET",
        label: "Portfolio exposure",
        href: "/api/portfolio/exposure",
        ariaLabel: "Open GET /api/portfolio/exposure backend payload"
      }
    ]
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

function buildSelectedRunDetail(run: PortfolioRunRow): PortfolioRunDetail {
  const statusTone = runStatusTone(run.status, run.pnlTone);
  const promotionValue = run.promotionState ?? "Not promoted";

  return {
    id: run.id,
    title: run.strategyName,
    subtitle: `${run.mode} - ${run.engine} - ${run.id}`,
    ariaLabel: `${run.strategyName} run detail`,
    statusTitle: `${run.strategyName} selected`,
    statusDetail: `${run.status} ${run.mode} run with ${run.pnl} P&L and ${run.sharpe} Sharpe. ${run.notes || "No operator notes attached."}`,
    statusTone,
    statusBadgeLabel: run.status,
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
      { label: "Promotion", value: promotionValue, tone: run.promotionState ? "success" : "warning" },
      { label: "Last updated", value: run.lastUpdated, tone: "muted" }
    ]
  };
}

function pnlFieldTone(value: string): PortfolioDetailField["tone"] {
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

function formatNumber(value: number): string {
  return value.toLocaleString(undefined, { maximumFractionDigits: 4 });
}

function formatDateTime(value: string): string {
  const date = new Date(value);
  return Number.isNaN(date.getTime())
    ? "—"
    : date.toLocaleString(undefined, { dateStyle: "short", timeStyle: "short" });
}
