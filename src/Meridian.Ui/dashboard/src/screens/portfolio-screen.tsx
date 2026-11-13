import { DetailFields, DetailPane } from "@/components/meridian/detail-pane";
import { WorkspaceWorkbenchShell } from "@/components/meridian/workspace-workbench-shell";
import type { KeyboardEvent } from "react";
import { useEffect, useMemo, useRef, useState } from "react";
import { BriefcaseBusiness, FileCheck2, LineChart, Network, Settings, ShieldCheck, Wallet } from "lucide-react";
import { Link, useLocation, useNavigate } from "react-router-dom";
import { Badge } from "@/components/ui/badge";
import { MarkFreshnessCell } from "@/components/meridian/mark-freshness-cell";
import { PositionValueGraphic } from "@/components/meridian/position-value-graphic";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { TechnicalDetails } from "@/components/ui/technical-details";
import { buildFreshnessChipViewModel, freshnessInputFromLifecycle } from "@/components/ui/freshness-chip.view-model";
import type { RequestLifecycleStatus } from "@/hooks/use-request-lifecycle";
import {
  FinancialRecordExplorerShell,
  type FinancialRecordExplorerAction,
  type FinancialRecordExplorerSavedView,
  type FinancialRecordExplorerScopeItem,
  type FinancialRecordExplorerSummaryItem
} from "@/components/meridian/financial-record-explorer";
import { OperationalTrustSummary, type OperationalTrustTone } from "@/components/meridian/operational-trust-summary";
import type { DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { TableWorkbench, type TableWorkbenchColumn } from "@/components/meridian/table-workbench";
import { exportTableQuery } from "@/lib/table-workbench";
import { formatNumber, formatPercent, formatRatioAsPercent } from "@/lib/format";
import { EntityLink } from "@/components/meridian/entity-inspector";
import { AccountBalanceHistoryChart } from "@/components/meridian/account-balance-history-chart";
import { StatStrip } from "@/components/meridian/stat-strip";
import { EmptyState } from "@/components/data/concrete";
import { SeverityBadge, TrustStrip, type TrustStripItem } from "@/components/operations";
import {
  EquityCurve,
  Histogram,
  ChartCard,
  ChartSyncProvider,
  useChartSync,
  useChartCrosshairSync,
  nearestTimestampIndex
} from "@/components/charts";
import {
  getFinancialRecordExplorer,
  getRunAttribution,
  getRunCashFlows,
  getRunEquityCurve,
  getRunFills,
  saveFinancialRecordExplorerView
} from "@/lib/api";
import { cn } from "@/lib/utils";
import { getPositionSideOptions, selectPortfolioPositionRows } from "@/lib/portfolio-positions-table";
import { usePortfolioPositionsTable } from "@/hooks/use-portfolio-positions-table";
import { PortfolioPositionsToolbar } from "@/screens/portfolio-positions-toolbar";
import { WORKSTATION_ROUTE_CATALOG, workstationRouteWithQuery } from "@/lib/workspace";
import {
  buildPortfolioExplorerPresentation,
  resolveBrokerageAccountFilterKeyCommand,
  type PortfolioBrokerageAccountRow,
  type PortfolioBrokeragePositionRow,
  type PortfolioPositionRow,
  type PortfolioRunDrillInData,
  type PortfolioWorkflowTaskAction,
  type PortfolioRunRow,
  usePortfolioScreenViewModel
} from "@/screens/portfolio-screen.view-model";
import type {
  BrokerageConnectionStatus,
  BrokerageHouseholdPortfolio,
  AccountingWorkspaceResponse,
  FinancialRecordExplorerDto,
  EquityCurveSummary,
  FinancialRecordExplorerSavedViewSaveRequestDto,
  MetricFreshness,
  MultiAssetCoverageSummary,
  PortfolioWorkspaceResponse,
  StrategyWorkspaceResponse,
  TradingWorkspaceResponse
} from "@/types";
import { eyebrowClassName } from "@/design-system/label-case";
import { metricFreshnessFromTrustTone, withMetricContext } from "@/lib/metric-trust";
import { resolveFigureEvidence, resolveFigureScope, useWorkspaceFigureContext } from "@/lib/workspace-figure-context";
import { FigureDifferenceNote } from "@/components/meridian/figure-difference-note";
import { buildPortfolioFigureDifferenceNote } from "@/screens/portfolio-screen.figure-differences";
import { withPortfolioMarkEvidence } from "@/screens/portfolio-screen.headline-evidence";
import { toSeverity } from "@/design-system/status-styles";

interface PortfolioScreenProps {
  portfolio?: PortfolioWorkspaceResponse | null;
  trading: TradingWorkspaceResponse | null;
  strategy: StrategyWorkspaceResponse | null;
  accounting: AccountingWorkspaceResponse | null;
  brokerageConnection?: BrokerageConnectionStatus | null;
  brokeragePortfolio?: BrokerageHouseholdPortfolio | null;
  multiAssetCoverage?: MultiAssetCoverageSummary | null;
  refreshStatus?: RequestLifecycleStatus | null;
}

export const PORTFOLIO_FRESHNESS_BUDGET_MS = 5 * 60_000;

const pnlToneClass = {
  success: "text-success",
  danger: "text-danger",
  default: "text-foreground"
} as const;

const detailFieldToneClass = {
  default: "text-foreground",
  success: "text-success",
  warning: "text-warning",
  danger: "text-danger",
  muted: "text-muted-foreground"
} as const;

const cashFlowBorderClass = {
  default: "border-border/70",
  success: "border-success/30",
  warning: "border-warning/30",
  danger: "border-danger/30"
} as const;

const positionColumns: DenseDataTableColumn<PortfolioPositionRow>[] = [
  { id: "mark-readiness", label: "Mark readiness", sortable: true, render: (row) => <MarkFreshnessCell mark={row.markFreshness} /> },
  {
    id: "symbol",
    label: "Symbol",
    sortable: true,
    render: (row) => <EntityLink record={{ kind: "symbol", id: row.symbol, label: row.symbol,
      workspaceHref: workstationRouteWithQuery("accountingSecurityMaster", { symbol: row.symbol }),
      summary: [{ label: "Source", value: "Portfolio holdings snapshot" }, { label: "Side", value: row.side },
        { label: "Quantity", value: row.quantity }, { label: "Mark", value: row.markPrice }] }} />
  },
  {
    id: "side",
    label: "Side",
    sortable: true,
    render: (row) => <span className="font-mono text-foreground">{row.side}</span>
  },
  {
    id: "quantity",
    label: "Qty",
    numeric: true,
    hideable: true,
    render: (row) => <span className="font-mono text-foreground">{row.quantity}</span>
  },
  {
    id: "average",
    label: "Avg",
    numeric: true,
    hideable: true,
    render: (row) => <span className="font-mono text-foreground">{row.avgPrice}</span>
  },
  {
    id: "mark",
    label: "Recorded mark",
    numeric: true,
    hideable: true,
    render: (row) => <span className="font-mono text-foreground">{row.markPrice}</span>
  },
  {
    id: "unrealized-pnl",
    label: "Unrealized P&L",
    numeric: true,
    hideable: true,
    render: (row) => <PositionValueGraphic value={row.unrealizedPnl} measurement={row.pnlGraphic} kind="pnl" className={cn("font-semibold", pnlToneClass[row.pnlTone])} />
  },
  {
    id: "exposure",
    label: "Exposure",
    numeric: true,
    hideable: true,
    render: (row) => <PositionValueGraphic value={row.exposure} measurement={row.exposureGraphic} kind="exposure" reviewRequired={row.markFreshness.reviewRequired} className="text-foreground" />
  }
];

const positionWorkbenchColumns: TableWorkbenchColumn<PortfolioPositionRow>[] = positionColumns.map(column => ({
  ...column,
  exportValue: (row: PortfolioPositionRow) => ({ symbol: row.symbol, side: row.side, quantity: row.quantity,
    average: row.avgPrice, mark: row.markPrice, "unrealized-pnl": row.unrealizedPnl,
    exposure: row.exposure, "mark-readiness": `${row.markFreshness.label}; observed ${row.markFreshness.observedOn}; age ${row.markFreshness.age}`
  } as Record<string, string>)[column.id]
})).sort((left, right) => Number(right.id === "symbol") - Number(left.id === "symbol"));
positionWorkbenchColumns.splice(5, 0,
  { id: "market-value", label: "Market value", align: "right", exportValue: row => row.marketValue, render: row => row.marketValue },
  { id: "weight", label: "Weight", align: "right", exportValue: row => row.weight, render: row => <span title={row.weightBasis ?? "The source has not supplied a comparable denominator."}>{row.weight}</span> },
  { id: "day-pnl", label: "Day P&L", align: "right", exportValue: row => row.dayPnl, render: row => row.dayPnl });

const runColumns: DenseDataTableColumn<PortfolioRunRow>[] = [
  {
    id: "strategy",
    label: "Strategy",
    render: (row) => <span className="font-semibold text-foreground">{row.strategyName}</span>
  },
  {
    id: "mode",
    label: "Mode",
    render: (row) => <Badge variant={row.modeBadgeVariant}>{row.mode}</Badge>
  },
  {
    id: "status",
    label: "Status",
    render: (row) => <span className="text-foreground">{row.status}</span>
  },
  {
    id: "pnl",
    label: "P&L",
    align: "right",
    render: (row) => <span className={cn("font-mono font-semibold", pnlToneClass[row.pnlTone])}>{row.pnl}</span>
  },
  {
    id: "sharpe",
    label: "Sharpe",
    align: "right",
    render: (row) => <span className="font-mono text-foreground">{row.sharpe}</span>
  },
  {
    id: "promotion",
    label: "Promotion",
    render: (row) => <span className="text-muted-foreground">{row.promotionState ?? "-"}</span>
  }
];

const brokerageAccountColumns: DenseDataTableColumn<PortfolioBrokerageAccountRow>[] = [
  {
    id: "account",
    label: "Account",
    render: (row) => (
      <span className="block min-w-0">
        <span className="block font-semibold text-foreground">{row.kind}</span>
        <span className="block truncate text-xs text-muted-foreground">{row.label}</span>
      </span>
    )
  },
  {
    id: "health",
    label: "Health",
    render: (row) => <Badge variant={row.healthBadgeVariant}>{row.health}</Badge>
  },
  {
    id: "equity",
    label: "Equity",
    align: "right",
    render: (row) => <span className="font-mono text-foreground">{row.equity}</span>
  },
  {
    id: "cash",
    label: "Cash",
    align: "right",
    render: (row) => <span className="font-mono text-foreground">{row.cash}</span>
  },
  {
    id: "buying-power",
    label: "Buying power",
    align: "right",
    render: (row) => <span className="font-mono text-foreground">{row.buyingPower}</span>
  },
  {
    id: "synced",
    label: "Synced",
    render: (row) => <span className="font-mono text-xs text-muted-foreground">{row.syncedAt}</span>
  },
  {
    id: "warnings",
    label: "Warnings",
    align: "right",
    render: (row) => (
      <span className={cn("font-mono text-xs", row.hasWarning ? "text-warning" : "text-success")}>
        {row.warningCount}
      </span>
    )
  }
];

const brokeragePositionColumns: DenseDataTableColumn<PortfolioBrokeragePositionRow>[] = [
  { id: "mark-readiness", label: "Mark readiness", render: (row) => <MarkFreshnessCell mark={row.markFreshness} /> },
  {
    id: "account",
    label: "Account",
    render: (row) => (
      <span className="block min-w-0">
        <span className="block font-semibold text-foreground">{row.accountKind}</span>
        <span className="block truncate text-xs text-muted-foreground">{row.accountLabel}</span>
      </span>
    )
  },
  {
    id: "symbol",
    label: "Symbol",
    render: (row) => <span className="font-mono font-semibold text-foreground">{row.symbol}</span>
  },
  {
    id: "quantity",
    label: "Qty",
    align: "right",
    render: (row) => <span className="font-mono text-foreground">{row.quantity}</span>
  },
  {
    id: "average",
    label: "Avg",
    align: "right",
    render: (row) => <span className="font-mono text-foreground">{row.averagePrice}</span>
  },
  {
    id: "mark",
    label: "Mark",
    align: "right",
    render: (row) => <span className="font-mono text-foreground">{row.markPrice}</span>
  },
  {
    id: "market-value",
    label: "Market value",
    align: "right",
    render: (row) => <span className="font-mono text-foreground">{row.marketValue}</span>
  },
  {
    id: "unrealized-pnl",
    label: "Unrealized P&L",
    align: "right",
    render: (row) => <span className={cn("font-mono font-semibold", pnlToneClass[row.pnlTone])}>{row.unrealizedPnl}</span>
  },
  {
    id: "coverage",
    label: "Coverage",
    render: (row) => (
      <Badge variant={row.securityCoverage === "Covered" ? "success" : "warning"}>
        {row.securityCoverage}
      </Badge>
    )
  }
];

type PortfolioRouteViewId = "overview" | "attribution" | "brokerage-sync";

export function resolvePortfolioRouteView(pathname: string): PortfolioRouteViewId {
  // Match the segment right after /portfolio so a dynamic parameter deeper in
  // the path can never collide with a view keyword.
  const segments = pathname.split("/").filter(Boolean);
  if (segments[1] === "attribution") {
    return "attribution";
  }
  if (segments[1] === "brokerage-sync") {
    return "brokerage-sync";
  }
  return "overview";
}

const portfolioRouteViewCopy: Record<PortfolioRouteViewId, { title: string; description: string }> = {
  overview: {
    title: "Execution-linked holdings",
    description: "Open holdings, cash posture, and the record explorer stay aligned with the active paper workflow."
  },
  attribution: {
    title: "Run attribution evidence",
    description: "Run-linked equity evidence: comparison signals, drill-ins, and per-run attribution detail."
  },
  "brokerage-sync": {
    title: "Brokerage connection and coverage",
    description: "Live brokerage portfolio, connection trust, and multi-asset coverage posture."
  }
};

export function PortfolioScreen({
  portfolio,
  trading,
  strategy,
  accounting,
  brokerageConnection,
  brokeragePortfolio,
  multiAssetCoverage,
  refreshStatus
}: PortfolioScreenProps) {
  const location = useLocation();
  const navigate = useNavigate();
  const figureContext = useWorkspaceFigureContext();
  const routeView = resolvePortfolioRouteView(location.pathname);
  const routeCopy = portfolioRouteViewCopy[routeView];
  const showOverview = routeView === "overview";
  const showAttribution = routeView === "attribution";
  const showBrokerageSync = routeView === "brokerage-sync";
  const brokerageAccountButtonRefs = useRef<Record<string, HTMLButtonElement | null>>({});
  const shouldFocusBrokerageAccount = useRef(false);
  const drillInRequestId = useRef(0);
  const [selectedRunDrillIn, setSelectedRunDrillIn] = useState<PortfolioRunDrillInData | null>(null);
  const [portfolioExplorer, setPortfolioExplorer] = useState<FinancialRecordExplorerDto | null>(null);
  const [brokerageInspector, setBrokerageInspector] = useState<"account" | "position">("account");
  const vm = usePortfolioScreenViewModel({
    portfolio,
    trading,
    strategy,
    accounting,
    brokerageConnection,
    brokeragePortfolio,
    multiAssetCoverage,
    selectedRunDrillIn,
    pathname: location.pathname
  });
  const positionsTable = usePortfolioPositionsTable();
  const visiblePositionRows = useMemo(
    () => selectPortfolioPositionRows(vm.positionRows, positionsTable.state),
    [vm.positionRows, positionsTable.state]
  );
  const positionSideOptions = useMemo(() => getPositionSideOptions(vm.positionRows), [vm.positionRows]);
  const selectedVisiblePositionIndex = visiblePositionRows.findIndex((row) => row.id === vm.selectedPosition?.id);
  const selectedPositionOutsideResults = Boolean(vm.selectedPosition && selectedVisiblePositionIndex < 0);
  const portfolioExplorerPresentation = useMemo(
    () => buildPortfolioExplorerPresentation(portfolioExplorer, {
      sourceLabel: vm.positionSourceLabel,
      runLabel: vm.selectedRun?.title ?? "Linked strategy run"
    }),
    [portfolioExplorer, vm.positionSourceLabel, vm.selectedRun?.title]
  );

  useEffect(() => {
    if (!shouldFocusBrokerageAccount.current) {
      return;
    }

    shouldFocusBrokerageAccount.current = false;
    brokerageAccountButtonRefs.current[vm.selectedBrokerageAccountKey]?.focus();
  }, [vm.selectedBrokerageAccountKey]);

  useEffect(() => {
    if (selectedRunDrillIn && selectedRunDrillIn.runId !== vm.selectedRun?.id) {
      setSelectedRunDrillIn(null);
    }
  }, [selectedRunDrillIn, vm.selectedRun?.id]);

  useEffect(() => {
    let cancelled = false;

    void getFinancialRecordExplorer("portfolio").then((explorer) => {
      if (!cancelled) {
        setPortfolioExplorer(explorer);
      }
    }).catch(() => {
      if (!cancelled) {
        setPortfolioExplorer(null);
      }
    });

    return () => {
      cancelled = true;
    };
  }, []);

  async function savePortfolioExplorerView(request: FinancialRecordExplorerSavedViewSaveRequestDto) {
    await saveFinancialRecordExplorerView("portfolio", request);
    setPortfolioExplorer(await getFinancialRecordExplorer("portfolio"));
  }

  async function loadSelectedRunDrillIn() {
    const runId = vm.selectedRun?.id;
    if (!runId) {
      return;
    }

    const requestId = ++drillInRequestId.current;
    setSelectedRunDrillIn({
      runId,
      attribution: null,
      drawdownProfile: null,
      cashFlow: null,
      trades: null,
      isLoading: true,
      error: null
    });

    const [attribution, drawdownProfile, cashFlow, trades] = await Promise.allSettled([
      getRunAttribution(runId),
      getRunEquityCurve(runId),
      getRunCashFlows(runId),
      getRunFills(runId)
    ]);

    if (drillInRequestId.current !== requestId) {
      return;
    }

    const failures = [attribution, drawdownProfile, cashFlow, trades]
      .filter((result): result is PromiseRejectedResult => result.status === "rejected");
    setSelectedRunDrillIn({
      runId,
      attribution: attribution.status === "fulfilled" ? attribution.value : null,
      drawdownProfile: drawdownProfile.status === "fulfilled" ? drawdownProfile.value : null,
      cashFlow: cashFlow.status === "fulfilled" ? cashFlow.value : null,
      trades: trades.status === "fulfilled" ? trades.value : null,
      isLoading: false,
      error: failures.length > 0 ? `${failures.length} drill-in request${failures.length === 1 ? "" : "s"} failed.` : null
    });
  }

  function handleBrokerageAccountFilterKeyDown(event: KeyboardEvent<HTMLDivElement>) {
    const command = resolveBrokerageAccountFilterKeyCommand(event.key);
    if (!command) {
      return;
    }

    event.preventDefault();
    shouldFocusBrokerageAccount.current = true;
    vm.selectAdjacentBrokerageAccount(command);
  }

  const financialRecordExplorerScopeItems: FinancialRecordExplorerScopeItem[] = [
    { id: "portfolio-workspace", label: "Workspace", value: "Portfolio" },
    { id: "portfolio-source", label: "Source", value: vm.positionSourceLabel },
    { id: "portfolio-positions", label: "Positions", value: vm.positionCountLabel },
    { id: "portfolio-runs", label: "Runs", value: vm.runCountLabel }
  ];

  const financialRecordExplorerSavedViews: FinancialRecordExplorerSavedView[] = [
    {
      id: "portfolio-open-positions-run-evidence",
      label: "Open positions + run evidence",
      detail: `${vm.positionCountLabel}; ${vm.runCountLabel}; ${vm.runEvidenceChip.value}`,
      active: true
    },
    {
      id: "portfolio-selected-holding",
      label: "Selected holding",
      detail: `${vm.selectedPositionChip.label}: ${vm.selectedPositionChip.value}`
    },
    {
      id: "portfolio-selected-run",
      label: "Selected run proof",
      detail: `${vm.selectedRunChip.label}: ${vm.selectedRunChip.value}`
    }
  ];

  const financialRecordExplorerSummaryItems: FinancialRecordExplorerSummaryItem[] = [
    {
      id: "portfolio-open-position-count",
      label: "Open positions",
      value: vm.positionCountLabel,
      tone: vm.hasPositions ? "success" : "warning"
    },
    {
      id: "portfolio-selected-position",
      label: "Selected holding",
      value: vm.selectedPositionChip.value,
      tone: vm.selectedPosition?.statusTone ?? "warning"
    },
    {
      id: "portfolio-run-count",
      label: "Run evidence",
      value: vm.runEvidenceChip.value,
      tone: vm.hasRuns ? "success" : "warning"
    },
    {
      id: "portfolio-selected-run",
      label: "Selected run",
      value: vm.selectedRunChip.value,
      tone: vm.selectedRun?.statusTone ?? "warning"
    }
  ];

  const financialRecordExplorerAppliedFilters: FinancialRecordExplorerScopeItem[] = [
    { id: "portfolio-filter-source", label: "Position source", value: vm.positionSourceLabel },
    { id: "portfolio-filter-holding", label: vm.selectedPositionChip.label, value: vm.selectedPositionChip.value },
    { id: "portfolio-filter-run", label: vm.selectedRunChip.label, value: vm.selectedRunChip.value },
    { id: "portfolio-filter-brokerage-account", label: "Brokerage account", value: vm.selectedBrokerageAccount.title }
  ];

  const financialRecordExplorerActions: FinancialRecordExplorerAction[] = [
    vm.selectedPosition
      ? {
          id: "portfolio-position-proof",
          label: `Holding proof: ${vm.selectedPosition.title}`,
          href: `#${vm.positionDetailId}`,
          ariaLabel: vm.selectedPosition.ariaLabel
        }
      : {
          id: "portfolio-position-proof-empty",
          label: vm.positionDetailEmptyTitle
        },
    vm.selectedRun
      ? {
          id: "portfolio-run-evidence",
          label: vm.selectedRun.evidenceAction.label,
          href: vm.selectedRun.evidenceAction.href,
          ariaLabel: vm.selectedRun.evidenceAction.ariaLabel
        }
      : {
          id: "portfolio-run-proof-empty",
          label: vm.runDetailEmptyTitle
        },
    vm.multiAssetCoveragePanel
      ? {
          id: "portfolio-coverage-evidence",
          label: "Coverage proof",
          href: vm.multiAssetCoveragePanel.evidenceRoute,
          ariaLabel: "Open portfolio coverage proof"
        }
      : {
          id: "portfolio-coverage-evidence-empty",
          label: "Coverage proof unavailable"
        }
  ];
  const hasRefreshEvidence = Boolean(
    refreshStatus && (refreshStatus.lastSucceededAt || refreshStatus.inFlight || refreshStatus.phase === "failed")
  );
  const freshnessVm = hasRefreshEvidence && refreshStatus
    ? buildFreshnessChipViewModel({
        ...freshnessInputFromLifecycle(refreshStatus, PORTFOLIO_FRESHNESS_BUDGET_MS, "Portfolio workspace"),
        now: Date.now()
      })
    : brokeragePortfolio?.asOf
      ? buildFreshnessChipViewModel({
          timestamp: brokeragePortfolio.asOf,
          staleBudgetMs: PORTFOLIO_FRESHNESS_BUDGET_MS,
          now: Date.now(),
          label: "Brokerage portfolio snapshot",
          detail: "Latest brokerage portfolio snapshot"
        })
      : null;
  const heartbeat = portfolio?.brokerage.lastHeartbeat ?? trading?.brokerage.lastHeartbeat ?? null;
  // The totals on screen are a retained payload when the latest reload of the payload that
  // supplies them failed. In the shell that is the payload's own recorded outcome, which every
  // load path updates: a poll that failed earlier must not keep the totals stale after a shell or
  // workspace refresh reloaded them. Only outside the shell is the Portfolio poll's `partial`
  // phase (its Portfolio request was rejected) the evidence there is.
  const headlineSource = vm.metricsFromTrading ? "trading" : "portfolio";
  const headlineFigures = resolveFigureEvidence(figureContext, headlineSource);
  const headlineRefreshFailed = figureContext
    ? figureContext.staleSources.includes(headlineSource)
    : !vm.metricsFromTrading && refreshStatus?.phase === "partial";
  const evidenceTone: OperationalTrustTone = freshnessVm?.state === "failing"
    ? "blocked"
    : freshnessVm?.state === "aging"
      ? "review"
      : freshnessVm?.state === "fresh" || freshnessVm?.state === "live"
        ? "ready"
        : heartbeat
          ? "ready"
          : "unknown";
  const freshnessTone: OperationalTrustTone = headlineRefreshFailed && evidenceTone !== "blocked" ? "review" : evidenceTone;
  // The headline totals state the evidence of the payload that supplies them, so a stale
  // workspace cannot show figures that look current and another payload's time is not borrowed.
  // That payload last loaded at the shell's recorded time for it (Trading's when the totals come
  // from Trading), else, for Portfolio's own totals, at the Portfolio lifecycle's last success,
  // which survives a partial refresh. The brokerage snapshot and the readiness evidence date
  // other things and are used only when no load of the supplying payload is known at all.
  const headlineLoadedAt = headlineFigures.asOf
    ?? (!vm.metricsFromTrading && hasRefreshEvidence ? refreshStatus?.lastSucceededAt ?? null : null);
  const headlinePastBudget = headlineLoadedAt !== null
    && Date.now() - new Date(headlineLoadedAt).getTime() > PORTFOLIO_FRESHNESS_BUDGET_MS;
  const headlineFreshness: MetricFreshness = headlineRefreshFailed || headlinePastBudget
    ? "stale"
    : headlineFigures.freshness === "stale" || headlineFigures.freshness === "unverified"
      ? headlineFigures.freshness
      : headlineLoadedAt !== null
        ? "fresh"
        : metricFreshnessFromTrustTone(freshnessTone);
  // Their scope is every position in the workspace read model: selecting a brokerage account
  // filters the brokerage table, not these totals, so that account must not be named as scope.
  // Totals borrowed from Trading cover the fund account the Trading payload was loaded for.
  const borrowedTradingAccount = vm.metricsFromTrading ? resolveFigureScope(figureContext, "trading") : null;
  const headlineProvenance = {
    source: vm.metricsFromTrading ? "Trading workspace" : vm.positionSourceLabel,
    asOf: headlineLoadedAt ?? brokeragePortfolio?.asOf ?? heartbeat,
    scope: borrowedTradingAccount ?? "All workspace positions"
  };
  // Portfolio's own totals are sums over the positions, so they also carry those positions' mark
  // evidence. Trading's cards are server totals and are stated as loaded.
  const headlineMetrics = withMetricContext(
    vm.metricsFromTrading ? vm.metricCards : withPortfolioMarkEvidence(vm.fallbackStats, vm.positionRows, headlineFreshness),
    { provenance: headlineProvenance, freshness: headlineFreshness }
  );
  const figureDifferenceNote = showBrokerageSync
    ? null
    : buildPortfolioFigureDifferenceNote({
        headerMetrics: headlineMetrics,
        headerProvenance: headlineProvenance,
        explorer: portfolioExplorerPresentation
      });
  const completenessTone: OperationalTrustTone = vm.hasPositions && vm.hasRuns
    ? "ready"
    : vm.hasPositions || vm.hasRuns
      ? "review"
      : "blocked";
  const workflowBlocker = vm.workflowTaskPanel && vm.workflowTaskPanel.statusTone !== "success"
    ? {
        value: vm.workflowTaskPanel.statusLabel,
        detail: vm.workflowTaskPanel.selectedSummary,
        tone: vm.workflowTaskPanel.statusTone === "danger" ? "blocked" as const : "review" as const
      }
    : undefined;

  const readinessHandoff = vm.workflowTaskPanel ? (
    <section
      role="region"
      aria-label={vm.workflowTaskPanel.regionLabel}
      className="min-w-0"
    >
      <TechnicalDetails
        label={`${vm.workflowTaskPanel.title} · ${vm.workflowTaskPanel.statusLabel}`}
        open={showBrokerageSync ? true : undefined}
      >
        <div className="flex flex-col gap-4 lg:flex-row lg:items-start lg:justify-between">
          <div className="min-w-0">
            <div className={eyebrowClassName(vm.workflowTaskPanel.eyebrow)}>{vm.workflowTaskPanel.eyebrow}</div>
            <div className="mt-2 flex flex-wrap items-center gap-2">
              <h3 className="text-base font-semibold text-foreground">{vm.workflowTaskPanel.title}</h3>
              <Badge variant={workflowStatusVariant(vm.workflowTaskPanel.statusTone)}>
                {vm.workflowTaskPanel.statusLabel}
              </Badge>
            </div>
            <p className="mt-2 max-w-4xl text-sm leading-6 text-muted-foreground">
              {vm.workflowTaskPanel.description}
            </p>
            <p className="mt-3 text-sm leading-6 text-foreground">{vm.workflowTaskPanel.selectedSummary}</p>
            <div className="mt-4 flex flex-wrap gap-2" aria-label={vm.workflowTaskPanel.actionListLabel}>
              {vm.workflowTaskPanel.actions.map((action) => (
                <Button key={action.id} asChild variant={action.variant} size="sm">
                  <Link to={action.href} aria-label={action.ariaLabel} aria-describedby={action.detailId}>
                    <PortfolioWorkflowTaskActionIcon actionId={action.id} />
                    {action.label}
                    <span id={action.detailId} className="sr-only">{action.detail}</span>
                  </Link>
                </Button>
              ))}
            </div>
          </div>
          <div className="flex flex-wrap items-center gap-2 lg:justify-end">
            {vm.workflowTaskPanel.chips.map((chip) => (
              <PortfolioChip key={chip.label} label={chip.label} value={chip.value} />
            ))}
          </div>
        </div>

        <div className="mt-4 grid gap-4 xl:grid-cols-[1fr_0.8fr]">
          <DetailFields fields={vm.workflowTaskPanel.statusRows} columns="auto" />
          <TechnicalDetails
            label="Technical source health"
            description="Endpoint contracts supporting this workflow."
            className="h-fit"
          >
            <div className="grid gap-2">
              {vm.workflowTaskPanel.backendLinks.map((link) => (
                <a
                  key={link.id}
                  href={link.href}
                  aria-label={link.ariaLabel}
                  className="flex min-w-0 items-center justify-between gap-3 rounded-md border border-border/60 bg-secondary/20 px-3 py-2 text-xs transition-colors hover:border-primary/50 hover:bg-primary/10"
                >
                  <span className="min-w-0 truncate font-medium text-foreground">{link.label}</span>
                  <span className="shrink-0 font-mono text-muted-foreground">
                    {link.method} {link.href}
                  </span>
                </a>
              ))}
            </div>
          </TechnicalDetails>
        </div>
      </TechnicalDetails>
    </section>
  ) : null;

  return (
    <div className={showOverview ? "space-y-3" : "space-y-5"}>
      {/* Route heading first; workspace-wide metrics follow. The sidebar is the only
          sub-route navigation (no in-page tab strip). */}
      <section
        role="region"
        aria-label="Portfolio workbench context"
        className="flex flex-wrap items-end justify-between gap-3"
      >
        <div className="min-w-0">
          <h2 className="font-display text-lg font-semibold leading-tight text-foreground">
            {routeCopy.title}
          </h2>
          <p className={showOverview ? "sr-only" : "mt-0.5 max-w-3xl text-xs leading-5 text-muted-foreground"}>
            {routeCopy.description}
          </p>
        </div>
        <div className="flex flex-wrap items-center justify-end gap-2">
          <Button asChild variant="outline" size="sm">
            <Link
              to={WORKSTATION_ROUTE_CATALOG.portfolioCashLadder}
              aria-label="Open the portfolio cash ladder: projected inflows and outflows with liquidity scenarios"
            >
              <Wallet className="size-4" aria-hidden />
              Cash Ladder
            </Link>
          </Button>
        </div>
      </section>

      <StatStrip
        metrics={headlineMetrics}
        label="Portfolio headline metrics"
        compact={showOverview}
      />

      {figureDifferenceNote ? <FigureDifferenceNote note={figureDifferenceNote} /> : null}

      <OperationalTrustSummary
        label="Portfolio data confidence"
        source={{
          value: vm.positionSourceLabel,
          detail: "Holdings source used by this route",
          tone: vm.positionSourceLabel === "Unavailable" ? "blocked" : "ready"
        }}
        scope={{
          // A brokerage account, not the operating scope chosen in the header.
          label: "Account",
          value: vm.selectedBrokerageAccount.title,
          detail: routeView === "brokerage-sync" ? "Selected brokerage account" : "Current portfolio account scope",
          tone: vm.selectedBrokerageAccount.statusTone === "danger"
            ? "blocked"
            : vm.selectedBrokerageAccount.statusTone === "warning"
              ? "review"
              : vm.selectedBrokerageAccount.statusTone === "success"
                ? "ready"
                : "unknown"
        }}
        freshness={{
          value: freshnessVm?.text ?? heartbeat ?? "Unavailable",
          detail: headlineRefreshFailed
            ? "The latest refresh did not complete; the last loaded figures are shown."
            : freshnessVm?.title ?? (heartbeat ? "Latest brokerage heartbeat" : "Latest loaded portfolio evidence"),
          tone: freshnessTone
        }}
        completeness={{
          value: `${vm.positionCountLabel} · ${vm.runCountLabel}`,
          detail: "Holdings and run evidence loaded",
          tone: completenessTone
        }}
        blocker={workflowBlocker}
      />

      {showOverview ? null : readinessHandoff}

      {showBrokerageSync && vm.multiAssetCoveragePanel ? (
        <Card className={cn("panel-surface border", cashFlowBorderClass[vm.multiAssetCoveragePanel.statusTone])}>
          <CardHeader>
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <div className="eyebrow-label">Coverage</div>
                <CardTitle className="mt-2 flex items-center gap-2 text-base">
                  <Network className="h-4 w-4 text-primary" />
                  {vm.multiAssetCoveragePanel.title}
                </CardTitle>
                <CardDescription>{vm.multiAssetCoveragePanel.description}</CardDescription>
              </div>
              <Badge variant={workflowStatusVariant(vm.multiAssetCoveragePanel.statusTone)}>
                {vm.multiAssetCoveragePanel.statusLabel}
              </Badge>
            </div>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="flex flex-wrap items-center gap-2">
              {vm.multiAssetCoveragePanel.chips.map((chip) => (
                <PortfolioChip key={chip.label} label={chip.label} value={chip.value} />
              ))}
            </div>
            <div className="grid gap-3">
              {vm.multiAssetCoveragePanel.groups.map((group) => (
                <section
                  key={group.id}
                  role="group"
                  aria-label={`${group.label} multi-asset coverage`}
                  className={cn("rounded-lg border bg-background/20 p-3", cashFlowBorderClass[group.statusTone])}
                >
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <div className="min-w-0">
                      <div className="text-sm font-semibold text-foreground">{group.label}</div>
                      <div className="mt-1 text-xs text-muted-foreground">{group.summary}</div>
                    </div>
                    <Badge variant={workflowStatusVariant(group.statusTone)}>{group.label}</Badge>
                  </div>
                  <div className="mt-3 grid gap-3">
                    {group.rows.map((row) => (
                      <div key={row.id} className="rounded-md border border-border/60 bg-secondary/20 px-3 py-3">
                        <div className="flex flex-wrap items-start justify-between gap-3">
                          <div className="min-w-0">
                            <div className="font-semibold text-foreground">{row.displayName}</div>
                            <div className="mt-1 text-xs leading-5 text-muted-foreground">{row.summary}</div>
                          </div>
                          <Badge variant={workflowStatusVariant(row.statusTone)}>{row.statusLabel}</Badge>
                        </div>
                        <div className="mt-3 grid gap-2 md:grid-cols-3">
                          <div className="rounded-md border border-border/60 bg-background/30 px-3 py-2">
                            <div className="text-[11px] uppercase text-muted-foreground">Readiness</div>
                            <div className="mt-1 text-xs text-foreground">{row.readinessDetail}</div>
                          </div>
                          <div className="rounded-md border border-border/60 bg-background/30 px-3 py-2">
                            <div className="text-[11px] uppercase text-muted-foreground">Ledger</div>
                            <div className="mt-1 text-xs text-foreground">{row.ledgerLabel}</div>
                          </div>
                          <div className="rounded-md border border-border/60 bg-background/30 px-3 py-2">
                            <div className="text-[11px] uppercase text-muted-foreground">Reconciliation</div>
                            <div className="mt-1 text-xs text-foreground">{row.reconciliationLabel}</div>
                          </div>
                        </div>
                        <div className="mt-3 grid gap-3 xl:grid-cols-[1fr_0.9fr]">
                          <div>
                            <div className="text-[11px] uppercase text-muted-foreground">Evidence targets</div>
                            <div className="mt-2 flex flex-wrap gap-2">
                              {row.evidenceTargets.map((target) => (
                                <a
                                  key={target.id}
                                  href={target.href}
                                  aria-label={target.ariaLabel}
                                  className="inline-flex items-center gap-2 rounded-md border border-border/60 bg-background/40 px-2 py-1 text-xs hover:border-primary/50 hover:bg-primary/10"
                                >
                                  <Badge variant={workflowStatusVariant(target.statusTone)}>{target.statusLabel}</Badge>
                                  <span className="font-medium text-foreground">{target.label}</span>
                                  <span className="text-muted-foreground">{target.category}</span>
                                  <span className="font-mono text-muted-foreground">{target.requiredLabel}</span>
                                </a>
                              ))}
                            </div>
                          </div>
                          <div>
                            <div className="text-[11px] uppercase text-muted-foreground">Review targets</div>
                            <div className="mt-2 grid gap-2">
                              {row.blockerTargets.length > 0 ? row.blockerTargets.map((target) => (
                                target.href ? (
                                  <a
                                    key={target.id}
                                    href={target.href}
                                    aria-label={target.ariaLabel}
                                    className="rounded-md border border-warning/30 bg-warning/10 px-2 py-1 text-xs text-warning hover:border-warning/60"
                                  >
                                    <span className="font-semibold">{target.label}</span>
                                    <span className="ml-2">{target.source}</span>
                                    <span className="ml-2">{target.detail}</span>
                                  </a>
                                ) : (
                                  <div key={target.id} aria-label={target.ariaLabel} className="rounded-md border border-warning/30 bg-warning/10 px-2 py-1 text-xs text-warning">
                                    <span className="font-semibold">{target.label}</span>
                                    <span className="ml-2">{target.source}</span>
                                    <span className="ml-2">{target.detail}</span>
                                  </div>
                                )
                              )) : (
                                <div className="rounded-md border border-border/60 bg-background/30 px-2 py-1 text-xs text-muted-foreground">
                                  {row.blockerLabel}
                                </div>
                              )}
                            </div>
                          </div>
                        </div>
                        {row.drillThroughTargets.length > 0 ? (
                          <div className="mt-3">
                            <div className="text-[11px] uppercase text-muted-foreground">Drill-through targets</div>
                            <div className="mt-2 flex flex-wrap gap-2">
                              {row.drillThroughTargets.map((target) => (
                                <a
                                  key={target.id}
                                  href={target.href}
                                  aria-label={target.ariaLabel}
                                  className="inline-flex items-center gap-2 rounded-md border border-border/60 bg-background/40 px-2 py-1 text-xs hover:border-primary/50 hover:bg-primary/10"
                                >
                                  <Badge variant={workflowStatusVariant(target.statusTone)}>{target.statusLabel}</Badge>
                                  <span className="font-medium text-foreground">{target.label}</span>
                                  <span className="text-muted-foreground">{target.type}</span>
                                  <span className="font-mono text-muted-foreground">{target.source}</span>
                                </a>
                              ))}
                            </div>
                          </div>
                        ) : null}
                      </div>
                    ))}
                  </div>
                </section>
              ))}
            </div>
            {vm.multiAssetCoveragePanel.blockerMessages.length > 0 ? (
              <div className="rounded-md border border-warning/30 bg-warning/10 px-3 py-2 text-sm text-warning">
                {vm.multiAssetCoveragePanel.blockerMessages.map((message) => (
                  <div key={message}>{message}</div>
                ))}
              </div>
            ) : null}
            <TechnicalDetails
              label="Coverage source details"
              description="Technical route for the retained multi-asset coverage evidence."
            >
              <a href={vm.multiAssetCoveragePanel.evidenceRoute} className="text-xs font-medium text-primary hover:underline">
                Open coverage endpoint: {vm.multiAssetCoveragePanel.evidenceRouteLabel}
              </a>
            </TechnicalDetails>
          </CardContent>
        </Card>
      ) : null}

      {showBrokerageSync ? (
      <Card className={cn("panel-surface border", cashFlowBorderClass[vm.brokerageConnectionTone])}>
        <CardHeader>
          <div className="flex flex-wrap items-start justify-between gap-3">
            <div>
              <div className={eyebrowClassName(vm.brokeragePanelEyebrow)}>{vm.brokeragePanelEyebrow}</div>
              <CardTitle className="mt-2 flex items-center gap-2 text-base">
                <BriefcaseBusiness className="h-4 w-4 text-primary" />
                Live brokerage portfolio
              </CardTitle>
              <CardDescription>{vm.brokerageConnectionDetail}</CardDescription>
            </div>
            <Badge variant={vm.brokerageConnectionTone === "default" ? "outline" : vm.brokerageConnectionTone}>
              {vm.brokerageConnectionLabel}
            </Badge>
          </div>
        </CardHeader>
        <CardContent className="space-y-4">
          <section
            role="region"
            aria-label={vm.brokerageTrustSnapshot.regionLabel}
            className={cn("rounded-lg border bg-secondary/15 px-4 py-3", cashFlowBorderClass[vm.brokerageTrustSnapshot.statusTone])}
          >
            <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
              <div className="min-w-0">
                <div className="eyebrow-label">Sync trust</div>
                <div className="mt-2 flex flex-wrap items-center gap-2">
                  <h3 className="text-base font-semibold text-foreground">{vm.brokerageTrustSnapshot.title}</h3>
                  <SeverityBadge
                    status={toSeverity(vm.brokerageTrustSnapshot.statusTone)}
                    label={vm.brokerageTrustSnapshot.statusLabel}
                  />
                </div>
                <p className="mt-2 max-w-4xl text-sm leading-6 text-muted-foreground">
                  {vm.brokerageTrustSnapshot.summary}
                </p>
              </div>
              <div className="lg:justify-end">
                <TrustStrip items={toTrustStripItems(vm.brokerageTrustSnapshot.chips, vm.brokerageTrustSnapshot.statusTone)} />
              </div>
            </div>
            <DetailFields fields={vm.brokerageTrustSnapshot.fields} columns="auto" className="mt-3" />
          </section>

          <div
            className="flex flex-wrap items-center gap-2"
            role="group"
            aria-label={vm.brokerageAccountFilterLabel}
            onKeyDown={handleBrokerageAccountFilterKeyDown}
          >
            {vm.brokerageAccountOptions.map((option) => (
              <Button
                key={option.key}
                ref={(node) => {
                  brokerageAccountButtonRefs.current[option.key] = node;
                }}
                type="button"
                size="sm"
                variant={option.isSelected ? "secondary" : "outline"}
                aria-pressed={option.isSelected}
                aria-label={option.ariaLabel}
                tabIndex={option.tabIndex}
                onClick={() => { setBrokerageInspector("account"); vm.selectBrokerageAccount(option.key); }}
              >
                {option.label}
              </Button>
            ))}
          </div>

          {vm.brokerageWarningRows.length > 0 ? (
            <div
              role="status"
              aria-label={vm.brokerageWarningCountLabel}
              className="rounded-lg border border-warning/30 bg-warning/10 px-4 py-3 text-sm text-warning"
            >
              <div className="font-semibold text-warning">{vm.brokerageWarningCountLabel}</div>
              <ul className="mt-2 grid gap-2">
                {vm.brokerageWarningRows.map((warning) => (
                  <li key={warning.id} aria-label={warning.ariaLabel} className="grid gap-1 sm:grid-cols-[10rem_1fr]">
                    <span className="font-medium text-warning">{warning.label}</span>
                    <span className="text-warning/90">{warning.detail}</span>
                  </li>
                ))}
              </ul>
            </div>
          ) : null}

          <div className="grid gap-4 xl:grid-cols-[minmax(0,1fr)_minmax(18rem,22rem)]">
            <div className="min-w-0 space-y-4">
              <TableWorkbench workbenchId="portfolio-brokerage-accounts" sourceKey={location.search}
                columns={brokerageAccountColumns.map(column => ({ ...column, exportValue: (row: PortfolioBrokerageAccountRow) => ({
                  account: row.label, health: row.health, equity: row.equity, cash: row.cash,
                  "buying-power": row.buyingPower, synced: row.syncedAt, warnings: row.warningCount
                } as Record<string, string>)[column.id] }))}
                rows={vm.brokerageAccountRows} getRowId={row => row.id}
                getSearchText={row => `${row.label} ${row.kind} ${row.health}`}
                completePopulation sourceDescription="Complete authorized brokerage household account snapshot; values retain source denomination."
                getRowAriaLabel={row => row.ariaLabel} getRowSelectAriaLabel={row => row.selectAriaLabel}
                getRowAriaControls={() => "portfolio-brokerage-active-detail"}
                getRowAriaExpanded={row => brokerageInspector === "account" && row.expanded}
                getRowClassName={row => row.rowClassName}
                onRowSelect={row => { setBrokerageInspector("account"); vm.selectBrokerageAccount(row.id); }}
                selectedRowId={brokerageInspector === "account" && vm.selectedBrokerageAccount.id !== "all" ? vm.selectedBrokerageAccount.id : null}
                emptyText={vm.brokerageAccountEmptyText} ariaLabel={vm.brokerageAccountsTableLabel}
                caption="Select an account to filter positions and update the shared brokerage inspector." />
              {vm.hasBrokeragePositions ? <TableWorkbench workbenchId="portfolio-brokerage-positions"
                sourceKey={`${location.search}:${vm.selectedBrokerageAccountKey}`}
                columns={brokeragePositionColumns.map(column => ({ ...column, exportValue: (row: PortfolioBrokeragePositionRow) => ({
                  "mark-readiness": `${row.markFreshness.label}; ${row.markFreshness.observedOn}; ${row.markFreshness.age}`,
                  account: row.accountLabel, symbol: row.symbol, quantity: row.quantity, average: row.averagePrice,
                  mark: row.markPrice, "market-value": row.marketValue, "unrealized-pnl": row.unrealizedPnl, coverage: row.securityCoverage
                } as Record<string, string>)[column.id] })).sort((left, right) => Number(right.id === "symbol") - Number(left.id === "symbol"))}
                rows={vm.brokeragePositionRows} getRowId={row => row.id}
                getSearchText={row => `${row.symbol} ${row.accountLabel} ${row.assetClass}`}
                completePopulation sourceDescription="All authorized household positions matching the selected account; values retain source denomination."
                exportContext={{ "Account filter": vm.selectedBrokerageAccount.title }}
                getRowAriaLabel={row => row.ariaLabel} getRowSelectAriaLabel={row => row.selectAriaLabel}
                getRowAriaControls={() => "portfolio-brokerage-active-detail"}
                getRowAriaExpanded={row => brokerageInspector === "position" && row.expanded}
                getRowClassName={row => row.rowClassName}
                onRowSelect={row => { setBrokerageInspector("position"); vm.selectBrokeragePosition(row.id); }}
                selectedRowId={brokerageInspector === "position" ? vm.selectedBrokeragePositionId : null}
                emptyText={vm.brokerageEmptyText} ariaLabel={vm.brokeragePositionsTableLabel}
                caption="Select a position to update the shared brokerage inspector." /> :
                <div role="status" className="text-sm text-muted-foreground">{vm.brokerageEmptyText}
                  {vm.brokerageSetupAction ? <><Button asChild variant="outline" size="sm"><Link to={vm.brokerageSetupAction.href} aria-label={vm.brokerageSetupAction.ariaLabel}>{vm.brokerageSetupAction.label}</Link></Button><p className="mt-2 text-xs">{vm.brokerageSetupAction.detail}</p></> : null}
                </div>}
            </div>
            <DetailPane
              id="portfolio-brokerage-active-detail"
              objectType={brokerageInspector === "position" ? "Brokerage position" : "Brokerage account"}
              title={brokerageInspector === "position" ? vm.selectedBrokeragePosition?.title : vm.selectedBrokerageAccount.title}
              ariaLabel={brokerageInspector === "position" ? vm.selectedBrokeragePosition?.ariaLabel ?? "Brokerage position detail" : vm.selectedBrokerageAccount.ariaLabel}
              subtitle={brokerageInspector === "position" ? vm.selectedBrokeragePosition?.subtitle : vm.selectedBrokerageAccount.subtitle}
              summary={brokerageInspector === "position" ? vm.selectedBrokeragePosition?.statusDetail : vm.selectedBrokerageAccount.statusDetail}
              fields={brokerageInspector === "position" ? vm.selectedBrokeragePosition ? [
                ...vm.selectedBrokeragePosition.fields,
                ...vm.selectedBrokeragePosition.technicalFields.map(field => ({ ...field, technical: true }))
              ] : [] : vm.selectedBrokerageAccount.fields}
              shownInRow={brokerageInspector === "position" ? ["Mark readiness", "Account", "Quantity", "Market value", "Unrealized P&L"] : []}
              status={brokerageInspector === "position" ? vm.selectedBrokeragePosition ? { label: vm.selectedBrokeragePosition.statusBadgeLabel, tone: vm.selectedBrokeragePosition.statusBadgeVariant } : null
                : { label: vm.selectedBrokerageAccount.statusBadgeLabel, tone: vm.selectedBrokerageAccount.statusBadgeVariant }}
              empty={{ title: "No brokerage position selected", text: vm.brokerageEmptyText }}
            />
          </div>
        </CardContent>
      </Card>
      ) : null}

        {showOverview ? (
        <WorkspaceWorkbenchShell label="Portfolio holdings workbench" switchInspector inspectorRole="region" inspectorLabel="Holding inspection" inspector={(
          <DetailPane
            id={vm.positionDetailId}
            ariaLabel={vm.selectedPosition?.ariaLabel ?? "Portfolio holding detail"}
            objectType="Holding"
            title={vm.selectedPosition?.title}
            status={vm.selectedPosition?.statusLabel
              ? { label: vm.selectedPosition.statusLabel, tone: vm.selectedPosition.statusTone }
              : null}
            subtitle={vm.selectedPosition?.subtitle}
            summary={vm.selectedPosition?.statusDetail}
            fields={vm.selectedPosition?.fields}
            // The holdings table always shows these two; its other columns can be hidden.
            shownInRow={["Side", "Mark readiness"]}
            empty={{ title: vm.positionDetailEmptyTitle, text: vm.positionEmptyText }}
            className={vm.selectedPosition ? cashFlowBorderClass[vm.selectedPosition.statusTone] : undefined}
            notice={selectedPositionOutsideResults ? (
              <div className="space-y-2">
                <p role="status" className="text-sm text-muted-foreground">Selected holding is outside the current results.</p>
                <Button type="button" size="sm" variant="outline" onClick={positionsTable.clearFilters}>Clear filters</Button>
              </div>
            ) : null}
            actions={vm.selectedPosition ? (
              <Button asChild size="sm" variant="outline">
                <Link
                  to={workstationRouteWithQuery("portfolioAssetDetail", {
                    symbol: vm.selectedPosition.title,
                    source: "portfolio"
                  })}
                  aria-label={`Open asset detail for ${vm.selectedPosition.title}`}
                >
                  Open asset detail
                </Link>
              </Button>
            ) : null}
          />
        )}>

          <Card className="panel-surface min-w-0">
            <CardHeader className="px-3 py-2">
              <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
                <div>
                  <CardTitle className="flex items-center gap-2">
                    <Wallet className="h-5 w-5 text-primary" />
                    Open positions
                  </CardTitle>
                  <CardDescription className="mt-2">
                    Select a holding to inspect its mark readiness and evidence.
                  </CardDescription>
                </div>
                <Badge variant="outline" aria-label={vm.positionCountLabel}>
                  {vm.positionCountLabel}
                </Badge>
              </div>
            </CardHeader>
            <CardContent className="px-3 py-2">
              <PortfolioPositionsToolbar
                state={positionsTable.state}
                sides={positionSideOptions}
                resultCount={visiblePositionRows.length}
                loadedCount={vm.positionRows.length}
                onSearch={positionsTable.setSearch}
                onSearchBlur={positionsTable.flushSearch}
                onSide={positionsTable.setSide}
                onReadiness={positionsTable.setReadiness}
                onReset={positionsTable.resetControls}
                shareError={positionsTable.shareError}
              />
              {vm.hasPositions ? <p className="mb-2 text-xs text-muted-foreground">Bars compare rounded, displayed $ amounts only. Each column uses its largest magnitude across all loaded positions before filters, grouped by supplied source currency. Amounts without currency stay in a separate approximate display group. P&L extends left for losses and right for gains from zero; no currency is inferred from $, and no conversion is applied.</p> : null}
              {vm.hasPositions ? (
                <TableWorkbench
                  workbenchId="portfolio-positions"
                  sourceKey={location.search}
                  columns={positionWorkbenchColumns}
                  rows={visiblePositionRows}
                  filterState={{ side: positionsTable.state.side ?? "", readiness: positionsTable.state.readiness, search: positionsTable.state.search }}
                  onRestoreView={view => positionsTable.restoreControls({ search: view.filters?.search ?? "",
                    side: view.filters?.side || null, readiness: view.filters?.readiness,
                    sort: view.sortColumnId ? { columnId: view.sortColumnId, direction: view.sortDirection } : null })}
                  completePopulation
                  sourceDescription="All filtered holdings from the Portfolio workspace snapshot; original source denomination and precision are retained."
                  sort={positionsTable.state.sort}
                  onToggleSort={positionsTable.toggleSort}
                  hiddenColumnIds={positionsTable.state.hiddenColumns}
                  onHiddenColumnIdsChange={positionsTable.setHiddenColumns}
                  getRowId={(row) => row.id}
                  getRowAriaLabel={(row) => row.ariaLabel}
                  getRowSelectAriaLabel={(row) => row.selectAriaLabel}
                  getRowAriaControls={(row) => row.detailPanelId}
                  getRowAriaExpanded={(row) => row.expanded}
                  onRowSelect={(row) => vm.selectPosition(row.id)}
                  selectedRowId={vm.selectedPosition?.id ?? null}
                  emptyText="No positions match these controls."
                  ariaLabel={vm.positionListLabel}
                  caption="Select a position row to update the holding detail panel."
                />
              ) : (
                <div role="status" className="rounded-sm border border-border/70 bg-background/40">
                  <EmptyState
                    icon="table"
                    title="No open holdings"
                    detail={vm.positionEmptyText}
                    action={vm.positionEmptyAction?.label}
                    onAction={
                      vm.positionEmptyAction
                        ? () => navigate({ pathname: vm.positionEmptyAction!.route, search: location.search })
                        : undefined
                    }
                    compact
                  />
                </div>
              )}
              <div className="mt-2 flex flex-wrap items-center gap-2">
                <PortfolioChip label="Execution source" value={vm.positionSourceLabel} />
                <PortfolioChip label={vm.runEvidenceChip.label} value={vm.runEvidenceChip.value} />
              </div>
            </CardContent>
          </Card>

        </WorkspaceWorkbenchShell>
        ) : null}

      {showOverview ? <AccountBalanceHistoryChart accountId={new URLSearchParams(location.search).get("fundAccountId") ?? new URLSearchParams(location.search).get("accountId")} /> : null}

      {showOverview ? readinessHandoff : null}

      {showOverview || showAttribution ? (
      <details className="workspace-task-disclosure" open={showAttribution ? true : undefined}>
      <summary>Retained run records and evidence · separate from current holdings</summary>
      <FinancialRecordExplorerShell
        explorerLabel="Financial Record Explorer"
        title="Portfolio Explorer"
        titleId="portfolio-financial-record-explorer-title"
        description="Open holdings, run evidence, brokerage posture, and retained proof links stay anchored to the existing portfolio read model."
        scopeItems={financialRecordExplorerScopeItems}
        savedViews={financialRecordExplorerSavedViews}
        summaryItems={financialRecordExplorerSummaryItems}
        appliedFilters={financialRecordExplorerAppliedFilters}
        actions={financialRecordExplorerActions}
        explorer={portfolioExplorerPresentation}
        onSaveView={savePortfolioExplorerView}
      >

        {showAttribution ? (
        <Card className="panel-surface">
          <CardHeader>
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <CardTitle className="flex items-center gap-2 text-base">
                  <LineChart className="h-4 w-4 text-primary" />
                  Run-linked equity
                </CardTitle>
                <CardDescription>
                  Strategy runs contributing to portfolio equity state. Promote runs to paper to connect execution evidence.
                </CardDescription>
              </div>
              <div className="flex flex-wrap items-center justify-end gap-2">
                <PortfolioChip label="Runs" value={vm.runCountLabel} />
                <PortfolioChip label={vm.selectedRunChip.label} value={vm.selectedRunChip.value} />
              </div>
            </div>
          </CardHeader>
          <CardContent>
            {vm.hasRuns ? (
              <div className="space-y-4">
                <section
                  aria-label={vm.runComparisonSummary.ariaLabel}
                  className={cn("rounded-lg border bg-secondary/15 p-4", cashFlowBorderClass[vm.runComparisonSummary.statusTone])}
                >
                  <div className="flex flex-wrap items-start justify-between gap-3">
                    <div className="min-w-0">
                      <div className={eyebrowClassName(vm.runComparisonSummary.title)}>{vm.runComparisonSummary.title}</div>
                      <p className="mt-2 text-sm leading-6 text-muted-foreground">{vm.runComparisonSummary.description}</p>
                    </div>
                    <Badge variant={vm.runComparisonSummary.statusTone === "default" ? "outline" : vm.runComparisonSummary.statusTone}>
                      {vm.runComparisonSummary.cards.length} signals
                    </Badge>
                  </div>
                  <div className="mt-3 grid gap-3 sm:grid-cols-2 xl:grid-cols-5">
                    {vm.runComparisonSummary.cards.map((card) => (
                      <div key={card.id} className="rounded-md border border-border/60 bg-background/45 px-3 py-3">
                        <div className={eyebrowClassName(card.label)}>{card.label}</div>
                        <div className={cn("mt-2 font-mono text-sm font-semibold", detailFieldToneClass[card.tone])}>{card.value}</div>
                        <p className="mt-2 text-xs leading-5 text-muted-foreground">{card.detail}</p>
                      </div>
                    ))}
                  </div>
                  {selectedRunDrillIn?.drawdownProfile && vm.selectedRun && headlineMetrics.some(metric => /portfolio value|equity|exposure/i.test(metric.label)) ? (
                    <FigureDifferenceNote
                      left={{ label: "Portfolio headline value", value: headlineMetrics.find(metric => /portfolio value|equity|exposure/i.test(metric.label))!.value,
                        measure: headlineMetrics.find(metric => /portfolio value|equity|exposure/i.test(metric.label))!.label,
                        basis: "Portfolio workspace snapshot", scope: "Current workspace holdings", currency: "", asOf: "" }}
                      right={{ label: `${vm.selectedRun.title} final equity`, value: String(selectedRunDrillIn.drawdownProfile.finalEquity),
                        measure: "Final equity", basis: "Retained strategy run equity", scope: vm.selectedRun.title, currency: "",
                        asOf: selectedRunDrillIn.drawdownProfile.points.at(-1)?.date ?? "" }} />
                  ) : null}
                </section>
                <section
                  aria-label={vm.runDrillInSummary.ariaLabel}
                  className={cn("rounded-lg border bg-secondary/15 p-4", cashFlowBorderClass[vm.runDrillInSummary.statusTone])}
                >
                  <div className="flex flex-wrap items-start justify-between gap-3">
                    <div className="min-w-0">
                      <div className={eyebrowClassName(vm.runDrillInSummary.title)}>{vm.runDrillInSummary.title}</div>
                      <p className="mt-2 text-sm leading-6 text-muted-foreground">{vm.runDrillInSummary.description}</p>
                    </div>
                    <Button
                      size="sm"
                      variant="outline"
                      onClick={() => void loadSelectedRunDrillIn()}
                      disabled={!vm.selectedRun || vm.runDrillInSummary.actionLabel === "Loading drill-ins..."}
                      disabledReason={!vm.selectedRun ? "Select a run before loading drill-in evidence." : undefined}
                      busy={vm.runDrillInSummary.actionLabel === "Loading drill-ins..."}
                      busyLabel={vm.runDrillInSummary.actionLabel}
                      aria-label={vm.runDrillInSummary.actionAriaLabel}
                    >
                      {vm.runDrillInSummary.actionLabel}
                    </Button>
                  </div>
                  <div className="mt-3 grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
                    {vm.runDrillInSummary.cards.map((card) => (
                      <div key={card.id} className="rounded-md border border-border/60 bg-background/45 px-3 py-3">
                        <div className={eyebrowClassName(card.label)}>{card.label}</div>
                        <div className={cn("mt-2 font-mono text-sm font-semibold", detailFieldToneClass[card.tone])}>{card.value}</div>
                        <p className="mt-2 text-xs leading-5 text-muted-foreground">{card.detail}</p>
                      </div>
                    ))}
                  </div>
                  {selectedRunDrillIn?.runId === vm.selectedRun?.id &&
                  (selectedRunDrillIn?.drawdownProfile?.points?.length ?? 0) >= 2 ? (
                    <div className="mt-3">
                      <PortfolioDrillInChart
                        profile={selectedRunDrillIn!.drawdownProfile!}
                        runTitle={vm.selectedRun?.title ?? "selected run"}
                      />
                    </div>
                  ) : null}
                  {vm.runDrillInSummary.bridgeRows.length > 0 ? (
                    <div className="mt-3 grid gap-2 lg:grid-cols-2" aria-label="Selected run realized unrealized bridge">
                      {vm.runDrillInSummary.bridgeRows.map((row) => (
                        <div
                          key={row.id}
                          className="grid gap-2 rounded-md border border-border/60 bg-background/45 px-3 py-2 sm:grid-cols-[minmax(0,0.45fr)_minmax(0,0.25fr)_minmax(0,1fr)]"
                        >
                          <div className={eyebrowClassName(row.label)}>{row.label}</div>
                          <div className={cn("font-mono text-xs font-semibold sm:text-right", detailFieldToneClass[row.tone])}>
                            {row.value}
                          </div>
                          <p className="text-xs leading-5 text-muted-foreground">{row.detail}</p>
                        </div>
                      ))}
                    </div>
                  ) : null}
                  {vm.runDrillInSummary.tradeEvidenceRows.length > 0 ? (
                    <div className="mt-3 overflow-hidden rounded-md border border-border/60 bg-background/45">
                      <div className="border-b border-border/60 px-3 py-2">
                        <div className="eyebrow-label eyebrow-label--sentence">Recent trade evidence</div>
                      </div>
                      <div className="grid divide-y divide-border/60">
                        {vm.runDrillInSummary.tradeEvidenceRows.map((row) => (
                          <div
                            key={row.id}
                            aria-label={row.ariaLabel}
                            className="grid gap-2 px-3 py-2 text-xs sm:grid-cols-[minmax(0,0.5fr)_minmax(0,0.45fr)_minmax(0,0.45fr)_minmax(0,0.45fr)_minmax(0,0.7fr)_minmax(0,0.7fr)]"
                          >
                            <span className="font-semibold text-foreground">{row.symbol}</span>
                            <span className="font-mono text-muted-foreground">{row.quantity}</span>
                            <span className="font-mono text-muted-foreground">{row.price}</span>
                            <span className="font-mono text-muted-foreground">{row.commission}</span>
                            <span className="font-mono text-muted-foreground">{row.filledAt}</span>
                            <span className="break-words font-mono text-muted-foreground">{row.accountId}</span>
                          </div>
                        ))}
                      </div>
                    </div>
                  ) : null}
                </section>
                <div className="grid gap-4 xl:grid-cols-[1.25fr_0.75fr]">
                <TableWorkbench workbenchId="strategy-runs" sourceKey={location.search}
                  columns={runColumns.map(column => ({ ...column, exportValue: (row: PortfolioRunRow) => ({
                    strategy: row.strategyName, mode: row.mode, status: row.status, pnl: row.pnl, sharpe: row.sharpe, promotion: row.promotionState
                  } as Record<string, string | null | undefined>)[column.id] }))}
                  getSearchText={row => `${row.strategyName} ${row.mode} ${row.engine} ${row.status}`}
                  sourceDescription="Retained strategy runs. Export queries every authorized matching run beyond the current workspace snapshot."
                  onExportAll={(columns, search, sort, signal) => exportTableQuery("strategy-runs", columns, search, sort?.columnId, sort?.direction, signal)}
                  rows={vm.runRows}
                  getRowId={(row) => row.id}
                  getRowAriaLabel={(row) => row.ariaLabel}
                  getRowSelectAriaLabel={(row) => row.selectAriaLabel}
                  getRowAriaControls={(row) => row.detailPanelId}
                  getRowAriaExpanded={(row) => row.expanded}
                  onRowSelect={(row) => vm.selectRun(row.id)}
                  selectedRowId={vm.selectedRun?.id ?? null}
                  emptyText={vm.runEmptyText}
                  ariaLabel={vm.runListLabel}
                  caption="Select a run row to update the run evidence detail panel."
                />
                <DetailPane
                  id={vm.runDetailId}
                  objectType="Strategy run"
                  title={vm.selectedRun?.title}
                  ariaLabel={vm.selectedRun?.ariaLabel ?? "Run evidence detail"}
                  fields={vm.selectedRun?.fields.map(field => field.label === "Run ID" ? { ...field, technical: true } : field) ?? []}
                  shownInRow={["Status"]}
                  status={vm.selectedRun ? { label: vm.selectedRun.statusBadgeLabel, tone: vm.selectedRun.statusBadgeVariant } : null}
                  summary={vm.selectedRun?.statusDetail}
                  actions={vm.selectedRun ? <Button asChild size="sm" variant="outline"><Link to={vm.selectedRun.evidenceAction.href} aria-label={vm.selectedRun.evidenceAction.ariaLabel}><Network className="h-4 w-4" />{vm.selectedRun.evidenceAction.label}</Link></Button> : null}
                  empty={{ title: vm.runDetailEmptyTitle, text: vm.runEmptyText }}
                />
                </div>
              </div>
            ) : (
              <div role="status" className="rounded-sm border border-border/70 bg-background/40">
                <EmptyState icon="chart" title="No linked runs" detail={vm.runEmptyText} compact />
              </div>
            )}
          </CardContent>
        </Card>
        ) : null}
      </FinancialRecordExplorerShell>
      </details>
      ) : null}

      {showOverview && vm.cashFlowSummary ? (
        <Card className={cn("panel-surface border", cashFlowBorderClass[vm.cashFlowTone])}>
          <CardHeader>
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <CardTitle className="flex items-center gap-2 text-base">
                  <BriefcaseBusiness className="h-4 w-4 text-primary" />
                  Cash-flow posture
                </CardTitle>
                <CardDescription>{vm.cashFlowSummary}</CardDescription>
              </div>
              {vm.cashVarianceLabel ? <PortfolioChip label="Net variance" value={vm.cashVarianceLabel} /> : null}
            </div>
          </CardHeader>
          {vm.cashVarianceLabel ? (
            <CardContent>
              <div className="rounded-lg border border-border/70 bg-secondary/25 px-4 py-3 text-sm">
                <span className="text-muted-foreground">Net cash variance: </span>
                <span className="font-mono font-semibold text-foreground">{vm.cashVarianceLabel}</span>
              </div>
            </CardContent>
          ) : null}
        </Card>
      ) : null}
    </div>
  );
}

function PortfolioChip({ label, value }: { label: string; value: string }) {
  return (
    <span className="toolbar-chip">
      <span className="text-muted-foreground">{label}</span>
      <span className="font-mono text-foreground">{value}</span>
    </span>
  );
}

const formatRunAmount = (value: number) => formatNumber(value, { maximumFractionDigits: 0 });
const drillInSignedPercent = (value: number) => `${value >= 0 ? "+" : ""}${formatPercent(value, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;

/** Concrete equity + underwater drawdown view for a loaded run drill-in profile. Additive
 * evidence only — rendered when the selected run has a fetched equity/drawdown series. The
 * chart is scoped in its own ChartSyncProvider so crosshair and point activation stay local to
 * this drill-in (never global) and can drive a per-point evidence readout. */
function PortfolioDrillInChart(props: { profile: EquityCurveSummary; runTitle: string }) {
  return (
    <ChartSyncProvider>
      <PortfolioDrillInChartInner {...props} />
    </ChartSyncProvider>
  );
}

function PortfolioDrillInChartInner({
  profile,
  runTitle
}: {
  profile: EquityCurveSummary;
  runTitle: string;
}) {
  const points = profile.points;
  const equity = points.map((point) => point.totalEquity);
  const drawdown = points.map((point) => -Math.abs(point.drawdownFromPeakPercent * 100));
  const labels = points.map((point) => point.date);
  const timestamps = useMemo(() => points.map((point) => Date.parse(point.date)), [points]);
  const sync = useChartCrosshairSync(timestamps);
  // Stable reference: the chart re-renders on every crosshair move, the table should not.
  const accessibleData = useMemo(
    () => ({
      caption: `Equity and drawdown by date for ${runTitle}`,
      columns: ["Date", "Equity", "Drawdown"],
      rows: points.map((point) => [
        point.date,
        formatRunAmount(point.totalEquity),
        // `|| 0` keeps a zero drawdown from rendering as "-0.00%".
        formatPercent(-Math.abs(point.drawdownFromPeakPercent * 100) || 0, {
          minimumFractionDigits: 2,
          maximumFractionDigits: 2
        })
      ])
    }),
    [points, runTitle]
  );

  const maxDrawdownLabel = formatRatioAsPercent(-Math.abs(profile.maxDrawdownPercent), {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2
  });

  return (
    <div className="space-y-3">
      <ChartCard
        title="Run equity and drawdown"
        subtitle={`Retained equity curve and drawdown for ${runTitle}. Run denomination is unavailable; amounts are not assigned a currency.`}
        readout={[
          { label: "Final equity", value: formatRunAmount(profile.finalEquity) },
          {
            label: "Max drawdown",
            value: maxDrawdownLabel,
            color: "var(--chart-drawdown, #B54B42)"
          },
          { label: "Sharpe", value: formatNumber(profile.sharpeRatio, { minimumFractionDigits: 2, maximumFractionDigits: 2 }) }
        ]}
        height={280}
        style={{ flexShrink: 0 }}
      >
        <EquityCurve
          ariaLabel={`Equity and drawdown curve for ${runTitle}`}
          ariaDescription={
            points.length > 0
              ? `${points.length} points from ${points[0].date} to ${points[points.length - 1].date}. Final equity ${formatRunAmount(
                  profile.finalEquity
                )}, maximum drawdown ${maxDrawdownLabel}.`
              : undefined
          }
          accessibleData={accessibleData}
          series={[{ label: "Equity", color: "var(--chart-equity, #2E7655)", points: equity }]}
          drawdown={drawdown}
          labels={labels}
          valueFmt={formatRunAmount}
          crosshairIndex={sync.crosshairIndex}
          onCrosshairChange={sync.onCrosshairChange}
          onPointActivate={sync.onPointActivate}
        />
        <PortfolioDrillInPointEvidence profile={profile} timestamps={timestamps} />
      </ChartCard>
      <PortfolioDrillInReturnDistribution profile={profile} runTitle={runTitle} />
    </div>
  );
}

/** Daily-return distribution for a loaded run drill-in profile. Reuses the already-fetched
 * equity-curve points (no extra request) so the same drill-in that draws the equity/drawdown
 * curve also shows the shape of its returns — a signed histogram with a mean rule. Rendered only
 * when at least two return observations exist so a single-day profile stays a curve-only view. */
function PortfolioDrillInReturnDistribution({
  profile,
  runTitle
}: {
  profile: EquityCurveSummary;
  runTitle: string;
}) {
  // Memoize the returns series and its summary stats on the fetched points so they are not
  // recomputed on every crosshair move — the parent re-renders on each hover, but these O(n)
  // reductions only depend on profile.points.
  const stats = useMemo(() => {
    const returns = profile.points.map((point) => point.dailyReturn * 100);
    if (returns.length < 2) {
      return null;
    }
    const mean = returns.reduce((sum, value) => sum + value, 0) / returns.length;
    const best = Math.max(...returns);
    const worst = Math.min(...returns);
    const positiveShare = (returns.filter((value) => value > 0).length / returns.length) * 100;
    return { returns, mean, best, worst, positiveShare };
  }, [profile.points]);

  if (!stats) {
    return null;
  }

  const { returns, mean, best, worst, positiveShare } = stats;
  const positiveShareLabel = formatPercent(positiveShare, { maximumFractionDigits: 0 });

  return (
    <ChartCard
      title="Daily return distribution"
      subtitle={`Shape of the ${runTitle} daily returns behind the equity curve above.`}
      readout={[
        {
          label: "Mean / day",
          value: drillInSignedPercent(mean),
          color: mean >= 0 ? "var(--chart-equity, #2E7655)" : "var(--chart-drawdown, #B54B42)"
        },
        { label: "Best day", value: drillInSignedPercent(best), color: "var(--chart-equity, #2E7655)" },
        { label: "Worst day", value: drillInSignedPercent(worst), color: "var(--chart-drawdown, #B54B42)" },
        { label: "Positive days", value: positiveShareLabel }
      ]}
      height={220}
      style={{ flexShrink: 0 }}
    >
      <Histogram
        ariaLabel={`Daily return distribution for ${runTitle}`}
        ariaDescription={`${returns.length} daily returns. Mean ${drillInSignedPercent(mean)}, best ${drillInSignedPercent(
          best
        )}, worst ${drillInSignedPercent(worst)}, ${positiveShareLabel} positive days.`}
        values={returns}
        signed
        showMean
        valueFmt={(value) => formatPercent(value, { minimumFractionDigits: 1, maximumFractionDigits: 1 })}
      />
    </ChartCard>
  );
}

/** Evidence drill: when a chart point is activated, surface that point's source-backed
 * detail. Reads the selected timestamp from the shared chart-sync context so it stays in
 * step with the equity curve (and any future linked chart in the same provider). */
function PortfolioDrillInPointEvidence({
  profile,
  timestamps
}: {
  profile: EquityCurveSummary;
  timestamps: number[];
}) {
  const { selectedTimestamp } = useChartSync();
  const selectedIndex = nearestTimestampIndex(timestamps, selectedTimestamp);
  if (selectedIndex == null) {
    return null;
  }
  const point = profile.points[selectedIndex];
  if (!point) {
    return null;
  }

  return (
    <div
      role="status"
      aria-live="polite"
      className="mt-2 rounded-md border border-border/70 bg-secondary/20 px-3 py-2 text-xs"
    >
      <span className="font-semibold text-foreground">Selected point — {point.date}</span>
      <span className="ml-2 text-muted-foreground">
        Equity {formatRunAmount(point.totalEquity)} · drawdown{" "}
        {formatRatioAsPercent(-Math.abs(point.drawdownFromPeakPercent), { minimumFractionDigits: 2, maximumFractionDigits: 2 })}
      </span>
    </div>
  );
}

function PortfolioWorkflowTaskActionIcon({ actionId }: { actionId: PortfolioWorkflowTaskAction["id"] }) {
  const Icon =
    actionId === "provider-setup"
      ? Settings
      : actionId === "brokerage-sync"
        ? BriefcaseBusiness
      : actionId === "trading-readiness"
        ? ShieldCheck
        : actionId === "evidence"
          ? FileCheck2
          : Wallet;

  return <Icon className="h-4 w-4" aria-hidden="true" />;
}

function workflowStatusVariant(statusTone: "default" | "success" | "warning" | "danger") {
  return statusTone === "default" ? "outline" : statusTone;
}

type PortfolioTone = "default" | "success" | "warning" | "danger";

/** Map a read-model status tone to a {@link TrustStrip} state token. */
function trustStateFromTone(tone: PortfolioTone): TrustStripItem["state"] {
  switch (tone) {
    case "success":
      return "ready";
    case "warning":
      return "review";
    case "danger":
      return "blocked";
    default:
      return "muted";
  }
}

/** Render the brokerage sync-snapshot chip band as a Concrete {@link TrustStrip}. Each chip
 * inherits the snapshot's overall tone, except the "Issues" chip which reads healthy only when
 * its value shows zero issues. */
function toTrustStripItems(
  chips: { label: string; value: string }[],
  statusTone: PortfolioTone
): TrustStripItem[] {
  return chips.map((chip) => {
    const isIssueChip = /issue/i.test(chip.label);
    const hasIssues = isIssueChip && !/^0\b/.test(chip.value.trim());
    return {
      label: chip.label,
      value: chip.value,
      state: isIssueChip
        ? hasIssues
          ? "review"
          : "ready"
        : trustStateFromTone(statusTone)
    };
  });
}
