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
          <CardTitle>Loading Strategy</CardTitle>
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
          <div className="eyebrow-label">Strategy Lane</div>
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
            <table className="min-w-full divide-y divide-border/60 text-left text-sm" aria-label="Strategy run library">
              <caption className="sr-only">{vm.runTable.caption}</caption>
              <thead className="bg-secondary/30">
                <tr>
                  {["", "Strategy", "Mode", "Engine", "Status", "P&L", "Sharpe", "Updated", ""].map((column, index) => (
                    <th key={`${column || "action"}-${index}`} className="px-3 py-2 text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">{column}</th>
                  ))}
                </tr>
              </thead>
              <tbody className="divide-y divide-border/50">
                {vm.runTable.hasRows ? vm.runTable.rows.map((run) => (
                  <tr key={run.id}>
                    <td className="px-3 py-2">
                      <input
                        type="checkbox"
                        aria-label={run.selectAriaLabel}
                        checked={vm.selectedIds.includes(run.id)}
                        onChange={() => vm.toggleRun(run.id)}
                      />
                    </td>
                    <td className="px-3 py-2 font-semibold">{run.strategyName}</td>
                    <td className="px-3 py-2"><Badge variant={run.mode === "paper" ? "paper" : "outline"}>{run.modeLabel}</Badge></td>
                    <td className="px-3 py-2">{run.engineText}</td>
                    <td className="px-3 py-2">{run.statusText}</td>
                    <td className="px-3 py-2 font-mono">{run.pnlText}</td>
                    <td className="px-3 py-2 font-mono">{run.sharpeText}</td>
                    <td className="px-3 py-2">{run.lastUpdatedText}</td>
                    <td className="px-3 py-2">
                      <Button size="sm" variant="outline" aria-label={run.openDetailLabel} onClick={() => vm.openRunDetail(run.raw)}>
                        Open
                      </Button>
                    </td>
                  </tr>
                )) : (
                  <tr>
                    <td colSpan={9} className="px-3 py-6 text-center text-muted-foreground">
                      {vm.runTable.emptyText}
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
        </CardContent>
      </Card>

      {vm.showComparisonPanel && (
        <Card>
          <CardHeader>
            <CardTitle>Run comparison</CardTitle>
            <CardDescription>Shared comparison rows returned by the workstation API.</CardDescription>
          </CardHeader>
          <CardContent>
            <div className="overflow-x-auto rounded-lg border border-border/70">
              <table className="min-w-full divide-y divide-border/60 text-left text-sm">
                <caption className="sr-only">{vm.comparisonTable.caption}</caption>
                <thead className="bg-secondary/30">
                  <tr>{["Strategy", "Mode", "Status", "Sharpe", "Fills"].map((column) => <th key={column} className="px-3 py-2">{column}</th>)}</tr>
                </thead>
                <tbody>
                  {vm.comparisonTable.hasRows ? vm.comparisonTable.rows.map((row) => (
                    <tr key={row.runId}>
                      <td className="px-3 py-2">{row.strategyName}</td>
                      <td className="px-3 py-2">{row.modeText}</td>
                      <td className="px-3 py-2">{row.statusText}</td>
                      <td className="px-3 py-2 font-mono">{row.sharpeRatioText}</td>
                      <td className="px-3 py-2 font-mono">{row.fillCountText}</td>
                    </tr>
                  )) : (
                    <tr>
                      <td colSpan={5} className="px-3 py-6 text-center text-muted-foreground">
                        {vm.comparisonTable.emptyText}
                      </td>
                    </tr>
                  )}
                </tbody>
              </table>
            </div>
          </CardContent>
        </Card>
      )}

      {vm.showDiffPanel && (
        <Card>
          <CardHeader>
            <CardTitle>{vm.diffPanel.title}</CardTitle>
            <CardDescription>{vm.diffPanel.description}</CardDescription>
          </CardHeader>
          <CardContent className="grid gap-4 md:grid-cols-2">
            <div className="rounded-lg border border-border/70 p-4">
              <div className="text-sm font-semibold">Position changes</div>
              <ul className="mt-3 space-y-2 text-sm">
                {vm.diffPanel.positionChanges.length > 0 ? vm.diffPanel.positionChanges.map((item) => (
                  <li key={item.key} className="font-mono">{item.text}</li>
                )) : (
                  <li className="text-muted-foreground">{vm.diffPanel.positionEmptyText}</li>
                )}
              </ul>
            </div>
            <div className="rounded-lg border border-border/70 p-4">
              <div className="text-sm font-semibold">Parameter changes</div>
              <ul className="mt-3 space-y-2 text-sm">
                {vm.diffPanel.parameterChanges.length > 0 ? vm.diffPanel.parameterChanges.map((item) => (
                  <li key={item.key} className="font-mono">
                    <span>{item.key}</span>
                    <span>: {item.baseValueText} {"->"} {item.targetValueText}</span>
                  </li>
                )) : (
                  <li className="text-muted-foreground">{vm.diffPanel.parameterEmptyText}</li>
                )}
              </ul>
            </div>
          </CardContent>
        </Card>
      )}

      {vm.showPromotionHistoryPanel && (
        <Card>
          <CardHeader>
            <CardTitle>Promotion history</CardTitle>
            <CardDescription>Latest paper and live promotion decisions.</CardDescription>
          </CardHeader>
          <CardContent>
            <div className="overflow-x-auto rounded-lg border border-border/70">
              <table className="min-w-full divide-y divide-border/60 text-left text-sm">
                <caption className="sr-only">{vm.promotionHistoryTable.caption}</caption>
                <thead className="bg-secondary/30">
                  <tr>{["Strategy", "Route", "Sharpe", "Promoted"].map((column) => <th key={column} className="px-3 py-2">{column}</th>)}</tr>
                </thead>
                <tbody>
                  {vm.promotionHistoryTable.hasRows ? vm.promotionHistoryTable.rows.map((record) => (
                    <tr key={record.promotionId}>
                      <td className="px-3 py-2 font-semibold">{record.strategyName}</td>
                      <td className="px-3 py-2">{record.routeText}</td>
                      <td className="px-3 py-2 font-mono">{record.qualifyingSharpeText}</td>
                      <td className="px-3 py-2">{record.promotedAtText}</td>
                    </tr>
                  )) : (
                    <tr>
                      <td colSpan={4} className="px-3 py-6 text-center text-muted-foreground">
                        {vm.promotionHistoryTable.emptyText}
                      </td>
                    </tr>
                  )}
                </tbody>
              </table>
            </div>
          </CardContent>
        </Card>
      )}

      {vm.selectedRunDetail && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-background/70 p-4">
          <div role="dialog" aria-modal="true" className="w-full max-w-lg rounded-lg border border-border bg-card p-5 shadow-workstation">
            <div className="flex items-start justify-between gap-4">
              <div>
                <div className="eyebrow-label">Run detail</div>
                <h2 className="mt-1 text-lg font-semibold">{vm.selectedRunDetail.title}</h2>
              </div>
              <Button variant="ghost" size="sm" onClick={vm.closeRunDetail}>Close</Button>
            </div>
            <p className="mt-4 text-sm leading-6 text-muted-foreground">{vm.selectedRunDetail.notesText}</p>
          </div>
        </div>
      )}
    </div>
  );
}
