import { MetricCard } from "@/components/meridian/metric-card";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { useResearchRunLibraryViewModel } from "@/screens/research-screen.view-model";
import type { ResearchWorkspaceResponse } from "@/types";

interface ResearchScreenProps {
  data: ResearchWorkspaceResponse | null;
}

export function ResearchScreen({ data }: ResearchScreenProps) {
  const vm = useResearchRunLibraryViewModel(data);

  if (!data) {
    return (
      <Card>
        <CardHeader>
          <CardTitle>Loading Research</CardTitle>
          <CardDescription>Waiting for run history and comparison state.</CardDescription>
        </CardHeader>
      </Card>
    );
  }

  return (
    <div className="space-y-8">
      <section className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
        {data.metrics.map((metric) => <MetricCard key={metric.id} {...metric} />)}
      </section>

      <Card>
        <CardHeader>
          <div className="eyebrow-label">Research Lane</div>
          <CardTitle>Strategy run library</CardTitle>
          <CardDescription>Review retained runs, compare candidates, and open promotion history from the web workstation.</CardDescription>
        </CardHeader>
        <CardContent className="space-y-4">
          <div className="flex flex-col gap-3 rounded-lg border border-border/70 bg-secondary/25 px-4 py-3 md:flex-row md:items-center md:justify-between">
            <div>
              <div className="eyebrow-label">Selection</div>
              <p className="mt-1 text-sm font-semibold">{vm.selectionText}</p>
              <p className="mt-1 text-xs text-muted-foreground">{vm.selectionDetail}</p>
            </div>
            <div className="flex flex-wrap gap-2">
              <Button variant="secondary" onClick={() => void vm.loadPromotionHistory()} disabled={!vm.canLoadPromotionHistory}>
                {vm.promotionHistoryButtonLabel}
              </Button>
              <Button variant="outline" onClick={() => void vm.compareSelectedRuns()} disabled={!vm.canCompare}>
                {vm.compareButtonLabel}
              </Button>
              <Button variant="outline" onClick={() => void vm.diffSelectedRuns()} disabled={!vm.canDiff}>
                {vm.diffButtonLabel}
              </Button>
            </div>
          </div>
          <span className="sr-only" aria-live="polite">{vm.statusAnnouncement}</span>
          {vm.actionError && (
            <div role="alert" className="rounded-lg border border-danger/40 bg-danger/10 px-4 py-3 text-sm text-danger">
              {vm.actionError}
            </div>
          )}

          <div className="overflow-x-auto rounded-lg border border-border/70">
            <table className="min-w-full divide-y divide-border/60 text-left text-sm">
              <thead className="bg-secondary/30">
                <tr>
                  {["", "Strategy", "Mode", "Engine", "Status", "P&L", "Sharpe", "Updated", ""].map((column, index) => (
                    <th key={`${column || "action"}-${index}`} className="px-3 py-2 text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">{column}</th>
                  ))}
                </tr>
              </thead>
              <tbody className="divide-y divide-border/50">
                {vm.runs.map((run) => (
                  <tr key={run.id}>
                    <td className="px-3 py-2">
                      <input
                        type="checkbox"
                        aria-label={`Select ${run.strategyName}`}
                        checked={vm.selectedIds.includes(run.id)}
                        onChange={() => vm.toggleRun(run.id)}
                      />
                    </td>
                    <td className="px-3 py-2 font-semibold">{run.strategyName}</td>
                    <td className="px-3 py-2"><Badge variant={run.mode === "paper" ? "paper" : "outline"}>{run.mode.toUpperCase()}</Badge></td>
                    <td className="px-3 py-2">{run.engine}</td>
                    <td className="px-3 py-2">{run.status}</td>
                    <td className="px-3 py-2 font-mono">{run.pnl}</td>
                    <td className="px-3 py-2 font-mono">{run.sharpe}</td>
                    <td className="px-3 py-2">{run.lastUpdated}</td>
                    <td className="px-3 py-2">
                      <Button size="sm" variant="outline" onClick={() => vm.openRunDetail(run)}>
                        Open
                      </Button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </CardContent>
      </Card>

      {vm.comparison.length > 0 && (
        <Card>
          <CardHeader>
            <CardTitle>Run comparison</CardTitle>
            <CardDescription>Shared comparison rows returned by the workstation API.</CardDescription>
          </CardHeader>
          <CardContent>
            <div className="overflow-x-auto rounded-lg border border-border/70">
              <table className="min-w-full divide-y divide-border/60 text-left text-sm">
                <thead className="bg-secondary/30">
                  <tr>{["Strategy", "Mode", "Status", "Sharpe", "Fills"].map((column) => <th key={column} className="px-3 py-2">{column}</th>)}</tr>
                </thead>
                <tbody>
                  {vm.comparison.map((row) => (
                    <tr key={row.runId}>
                      <td className="px-3 py-2">{row.strategyName}</td>
                      <td className="px-3 py-2">{row.mode}</td>
                      <td className="px-3 py-2">{row.status}</td>
                      <td className="px-3 py-2 font-mono">{row.sharpeRatio?.toFixed(3) ?? "n/a"}</td>
                      <td className="px-3 py-2 font-mono">{row.fillCount}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </CardContent>
        </Card>
      )}

      {vm.runDiff && (
        <Card>
          <CardHeader>
            <CardTitle>Position & parameter diff</CardTitle>
            <CardDescription>{vm.runDiff.baseStrategyName} compared with {vm.runDiff.targetStrategyName}.</CardDescription>
          </CardHeader>
          <CardContent className="grid gap-4 md:grid-cols-2">
            <div className="rounded-lg border border-border/70 p-4">
              <div className="text-sm font-semibold">Position changes</div>
              <ul className="mt-3 space-y-2 text-sm">
                {[...vm.runDiff.addedPositions, ...vm.runDiff.removedPositions, ...vm.runDiff.modifiedPositions].map((item) => (
                  <li key={`${item.symbol}-${item.changeType}`} className="font-mono">{item.symbol} {item.changeType}</li>
                ))}
              </ul>
            </div>
            <div className="rounded-lg border border-border/70 p-4">
              <div className="text-sm font-semibold">Parameter changes</div>
              <ul className="mt-3 space-y-2 text-sm">
                {vm.runDiff.parameterChanges.map((item) => (
                  <li key={item.key} className="font-mono">
                    <span>{item.key}</span>
                    <span>: {item.baseValue ?? "n/a"} {"->"} {item.targetValue ?? "n/a"}</span>
                  </li>
                ))}
              </ul>
            </div>
          </CardContent>
        </Card>
      )}

      {vm.promotionHistory.length > 0 && (
        <Card>
          <CardHeader>
            <CardTitle>Promotion history</CardTitle>
            <CardDescription>Latest paper and live promotion decisions.</CardDescription>
          </CardHeader>
          <CardContent>
            <ul className="space-y-2 text-sm">
              {vm.promotionHistory.map((record) => (
                <li key={record.promotionId} className="rounded-lg border border-border/70 p-3">
                  <span className="font-semibold">{record.strategyName}</span>
                  <span className="ml-3 font-mono">{record.qualifyingSharpe.toFixed(3)}</span>
                </li>
              ))}
            </ul>
          </CardContent>
        </Card>
      )}

      {vm.selectedRun && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-background/70 p-4">
          <div role="dialog" aria-modal="true" className="w-full max-w-lg rounded-lg border border-border bg-card p-5 shadow-workstation">
            <div className="flex items-start justify-between gap-4">
              <div>
                <div className="eyebrow-label">Run detail</div>
                <h2 className="mt-1 text-lg font-semibold">{vm.selectedRun.strategyName}</h2>
              </div>
              <Button variant="ghost" size="sm" onClick={vm.closeRunDetail}>Close</Button>
            </div>
            <p className="mt-4 text-sm leading-6 text-muted-foreground">{vm.selectedRun.notes}</p>
          </div>
        </div>
      )}
    </div>
  );
}
