import { WorkspaceTaskWorkbench } from "@/components/meridian/workspace-task-workbench";
import { Link } from "react-router-dom";
import { Badge } from "@/components/ui/badge";
import { TechnicalDetails } from "@/components/ui/technical-details";
import type { StrategyRunLibraryState } from "@/screens/strategy-screen.view-model";

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
    description: "Compile scripts against governed price, statistics, and backtest APIs.",
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


export function StrategyOverviewHub({ vm }: { vm: StrategyRunLibraryState }) {
  const plotToolConnected = vm.plotTool.workspace.statusBadgeLabel !== "NOT CONNECTED";
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
      <WorkspaceTaskWorkbench title="Retained strategy runs" description="Select a run to inspect its source, results, and next review action. Comparison and promotion controls remain in the run library." items={runs} stateKey="strategyRun" emptyText="No retained runs are available yet. Open a focused lab to create or inspect strategy evidence." />
      <div className="workspace-task-health" role="status">
        <strong>PlotTool analytics</strong>
        <Badge variant={vm.plotTool.workspace.statusBadgeVariant}>{vm.plotTool.workspace.statusBadgeLabel}</Badge>
        {!plotToolConnected ? <Link to="/settings/providers" className="text-xs font-semibold text-primary underline" aria-label="Review provider connections for PlotTool analytics">Review provider connections</Link> : null}
      </div>
      <TechnicalDetails label="Run coverage and analytics posture">
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
