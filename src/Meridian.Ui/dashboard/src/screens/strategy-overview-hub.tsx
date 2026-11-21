import { WorkspaceTaskWorkbench } from "@/components/meridian/workspace-task-workbench";
import { Link } from "react-router-dom";
import { Badge } from "@/components/ui/badge";
import { TechnicalDetails } from "@/components/ui/technical-details";
import { Sparkline } from "@/components/charts/Sparkline";
import { formatNumber } from "@/lib/format";
import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { buildRunLeaderboard } from "@/screens/strategy-run-leaderboard";
import { PLOT_TOOL_NOT_CONNECTED_LABEL, type StrategyRunLibraryState } from "@/screens/strategy-screen.view-model";

// Domain-specific projections and components shared by the screen composition.


export const strategyHubRoutes = [
  {
    id: "designer",
    title: "Design strategies",
    description: "Compose cells, inspect field vocabulary, and review backtest proof.",
    href: "/strategy/designer",
    action: "Open Designer"
  },
  {
    id: "lab",
    title: "Inspect PlotTool",
    description: "Review retained notebooks, scatter observations, and statistical evidence.",
    href: "/strategy/lab",
    action: "Open Strategy Lab"
  },
  {
    id: "promotions",
    title: "Review retained runs",
    description: "Compare candidates and review paper-promotion evidence.",
    href: "/strategy/promotions",
    action: "Open Promotions"
  },
  {
    id: "quant-lab",
    title: "Run Quant Lab",
    description: "Compile scripts against controlled price, statistics, and backtest APIs.",
    href: "/strategy/quant-lab",
    action: "Open Quant Lab"
  },
  {
    id: "covered-call",
    title: "Model covered calls",
    description: "Configure a chain preview and review backtest payoff evidence.",
    href: "/strategy/covered-call",
    action: "Open Covered Call"
  }
] as const;


/** Top runs by Sharpe (UX Phase D). The card is a table, so it is its own text alternative to the trend lines. */
export function RunLeaderboard({ runs }: { runs: StrategyRunLibraryState["runs"] }) {
  const model = buildRunLeaderboard(runs);
  const rows = [...model.ranked.map((row, index) => ({ ...row, rank: index + 1 })), ...model.unranked.map((row) => ({ ...row, rank: null }))];
  if (rows.length === 0) return null;
  // A value no run retains is said once in the note, not as a column of blanks.
  const showDrawdown = rows.some((row) => row.maxDrawdown !== null);
  const showTrend = rows.some((row) => row.trendValues !== null);
  const missing = [!showDrawdown ? "maximum drawdown" : null, !showTrend ? "an equity trend" : null].filter(Boolean).join(" or ");
  type Row = (typeof rows)[number];
  const columns: DenseDataTableColumn<Row>[] = [
    { id: "rank", label: "Rank", numeric: true, render: (row) => row.rank ?? <span className="text-xs text-muted-foreground">Not ranked</span> },
    { id: "run", label: "Run", rowHeader: true, render: (row) => (
      <span><Link to={row.href} className="font-medium text-primary underline-offset-2 hover:underline">{row.strategyName}</Link>
        <span className="block font-mono text-xs text-muted-foreground">{row.id}</span></span>) },
    { id: "sharpe", label: "Sharpe", numeric: true, render: (row) => row.sharpe === null ? "—" : row.sharpeText },
    { id: "pnl", label: "P&L", numeric: true, render: (row) => row.pnlText },
    ...(showDrawdown ? [{ id: "drawdown", label: "Max drawdown", numeric: true, render: (row: Row) => row.maxDrawdown === null ? "—" : formatNumber(row.maxDrawdown) }] : []),
    ...(showTrend ? [{ id: "trend", label: "Equity trend", render: (row: Row) => row.trendValues ? <Sparkline points={row.trendValues} width={96} height={24} /> : "—" }] : [])
  ];
  return (
    <section aria-labelledby="strategy-run-leaderboard-title" className="rounded-lg border border-border/70 bg-card px-4 py-3" data-chart-table="run-leaderboard">
      <h2 id="strategy-run-leaderboard-title" className="text-sm font-semibold">Run leaderboard</h2>
      <p className="text-xs text-muted-foreground">
        Top {model.ranked.length} retained {model.ranked.length === 1 ? "run" : "runs"} by Sharpe{model.unranked.length > 0 ? "; runs with no Sharpe follow, not ranked" : ""}.
        {showDrawdown ? " Drawdown is the retained amount in the run's own denomination." : ""}{missing ? ` These runs do not retain ${missing}.` : ""}
      </p>
      <div className="mt-2">
        <DenseDataTable columns={columns} rows={rows} getRowId={(row) => row.id} ariaLabel="Strategy runs ranked by Sharpe" emptyText="No retained runs." />
      </div>
    </section>
  );
}

export function StrategyOverviewHub({ vm }: { vm: StrategyRunLibraryState }) {
  const plotToolConnected = vm.plotTool.workspace.statusBadgeLabel !== PLOT_TOOL_NOT_CONNECTED_LABEL;
  const runs = vm.runs.map((run) => ({
    id: run.id,
    title: run.strategyName,
    description: `${run.engine} · ${run.mode} · ${run.dataset}`,
    status: run.status,
    href: `/strategy/promotions?runId=${encodeURIComponent(run.id)}`,
    actionLabel: "Review run",
    facts: [
      { label: "Run", value: run.id },
      { label: "Engine", value: run.engine },
      { label: "Mode", value: run.mode },
      { label: "Dataset", value: run.dataset },
      { label: "Window", value: run.window },
      { label: "P&L", value: run.pnl },
      { label: "Sharpe", value: run.sharpe },
      { label: "Updated", value: run.lastUpdated },
      { label: "Notes", value: run.notes }
    ]
  }));
  return (
    <section className="space-y-3" aria-label="Strategy run workbench">
      <RunLeaderboard runs={vm.runs} />
      <WorkspaceTaskWorkbench title="Retained strategy runs" description="Select a run to inspect its source, results, and next review action. Comparison and promotion controls remain in the run library." items={runs} stateKey="strategyRun" emptyText="No retained runs are available yet. Open a focused lab to create or inspect strategy evidence." />
      <div className="workspace-task-health" role="status">
        <strong>PlotTool analytics</strong>
        <Badge variant={vm.plotTool.workspace.statusBadgeVariant}>{vm.plotTool.workspace.statusBadgeLabel}</Badge>
        {!plotToolConnected ? <Link to="/settings/providers" className="text-xs font-semibold text-primary underline" aria-label="Review provider connections for PlotTool analytics">Review provider connections</Link> : null}
      </div>
      <TechnicalDetails label="Run coverage and analytics status">
        <section aria-label={vm.runHistorySummary.ariaLabel} className="workspace-task-facts">
          {vm.runHistorySummary.cards.map((card) => <div key={card.id}><strong>{card.label}: {card.value}</strong><p>{card.detail}</p></div>)}
        </section>
      </TechnicalDetails>
      <details className="workspace-task-disclosure">
        <summary>Strategy authoring and analysis tools</summary>
        <WorkspaceTaskWorkbench title="Strategy tasks" description="Open the focused authoring, analytics, or promotion workflow." items={strategyHubRoutes.map((route) => ({ ...route, actionLabel: route.action }))} stateKey="strategyTask" />
      </details>
    </section>
  );
}
