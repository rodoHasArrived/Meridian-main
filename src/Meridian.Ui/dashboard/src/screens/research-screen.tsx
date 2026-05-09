import { BarChart3, BookOpenText, ChartScatter, Network, Sigma, Sparkles } from "lucide-react";
import { Link, useNavigate } from "react-router-dom";
import { MetricCard } from "@/components/meridian/metric-card";
import { DenseDataTable, EntitySummary, ToolbarStrip, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { cn } from "@/lib/utils";
import { useResearchRunLibraryViewModel } from "@/screens/research-screen.view-model";
import type {
  ResearchPlotLegendItem,
  ResearchPlotSampleRow,
  ResearchPlotScatterPoint,
  ResearchPlotStatisticsState,
  ResearchPlotStudyItem,
  ResearchPlotWorkspaceState
} from "@/screens/research-screen.view-model";
import type { ResearchWorkspaceResponse } from "@/types";

interface ResearchScreenProps {
  data: ResearchWorkspaceResponse | null;
}

const comparisonValueToneClass = {
  success: "text-success",
  danger: "text-danger",
  muted: "text-muted-foreground"
} as const;

const diffMetricToneClass = {
  success: "text-success",
  danger: "text-danger",
  muted: "text-muted-foreground"
} as const;

const plotToneClass = {
  default: "text-foreground",
  success: "text-success",
  warning: "text-warning",
  danger: "text-danger"
} as const;

const plotLegendToneClass = {
  history: "bg-primary/75",
  current: "bg-warning",
  trend: "bg-danger",
  muted: "bg-muted-foreground/70"
} as const;

const promotionTitleToneClass = {
  success: "text-success",
  danger: "text-danger"
} as const;

const sampleToneBadgeVariant = {
  default: "outline",
  success: "success",
  warning: "warning",
  danger: "danger"
} as const;

export function ResearchScreen({ data }: ResearchScreenProps) {
  const vm = useResearchRunLibraryViewModel(data);
  const navigate = useNavigate();

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
          <div className="eyebrow-label">{vm.plotTool.workspace.eyebrow}</div>
          <div className="flex flex-col gap-4 xl:flex-row xl:items-start xl:justify-between">
            <div className="space-y-2">
              <CardTitle className="flex items-center gap-2">
                <ChartScatter className="h-5 w-5 text-primary" />
                PlotTool workstation
              </CardTitle>
              <CardDescription>{vm.plotTool.workspace.description}</CardDescription>
            </div>
            <div className="flex flex-wrap items-center justify-end gap-2">
              <div
                role="tablist"
                aria-label="PlotTool views"
                className="inline-flex rounded-md border border-border/70 bg-secondary/25 p-1"
                onKeyDown={(event) => {
                  if (vm.selectPlotToolViewForKey(event.key)) {
                    event.preventDefault();
                  }
                }}
              >
                {vm.plotToolTabs.map((tab) => (
                  <Button
                    key={tab.id}
                    type="button"
                    variant={tab.buttonVariant}
                    role="tab"
                    aria-selected={tab.selected}
                    aria-controls={tab.panelId}
                    aria-label={tab.ariaLabel}
                    tabIndex={tab.tabIndex}
                    id={tab.tabId}
                    onClick={() => vm.selectPlotToolView(tab.id)}
                  >
                    {tab.label}
                  </Button>
                ))}
              </div>
            </div>
          </div>
        </CardHeader>
        <CardContent className="space-y-4">
          <div className="flex flex-col gap-3 rounded-lg border border-border/70 bg-secondary/25 px-4 py-3 md:flex-row md:items-center md:justify-between">
            <div>
              <div className="eyebrow-label">Selection</div>
              <p className="mt-1 text-sm font-semibold">{vm.selectionText}</p>
              <p className="mt-1 text-xs text-muted-foreground">{vm.selectionDetail}</p>
            </div>
            <div className="flex flex-wrap gap-2">
              {vm.evidenceAction ? (
                <Button asChild variant="outline">
                  <Link to={vm.evidenceAction.href} aria-label={vm.evidenceAction.ariaLabel}>
                    <Network className="h-4 w-4" />
                    {vm.evidenceAction.label}
                  </Link>
                </Button>
              ) : null}
              <Button variant="secondary" onClick={() => void vm.loadPromotionHistory()} disabled={!vm.canLoadPromotionHistory}>
                {vm.promotionHistoryButtonLabel}
              </Button>
              <Button variant="outline" onClick={() => void vm.compareSelectedRuns()} disabled={!vm.canCompare}>
                {vm.compareButtonLabel}
              </Button>
              <Button variant="outline" onClick={() => void vm.diffSelectedRuns()} disabled={!vm.canDiff}>
                {vm.diffButtonLabel}
              </Button>
              <Button
                variant="default"
                onClick={() => void vm.promoteSelectedRun()}
                disabled={!vm.canPromote}
              >
                {vm.promoteButtonLabel}
              </Button>
            </div>
          </div>

          <span className="sr-only" aria-live="polite">{vm.statusAnnouncement}</span>
          {vm.actionError && (
            <div role="alert" className="rounded-lg border border-danger/40 bg-danger/10 px-4 py-3 text-sm text-danger">
              {vm.actionError}
            </div>
          )}

          {vm.showPromotePanel && (
            <div
              role={vm.promotionPanel.statusRole}
              aria-live={vm.promotionPanel.statusLive}
              aria-label={vm.promotionPanel.panelLabel}
              className="space-y-3 rounded-lg border border-border/70 bg-secondary/15 p-4"
            >
              <div className="eyebrow-label">{vm.promotionPanel.panelLabel}</div>
              {vm.promoteError && (
                <div role="alert" className="rounded-lg border border-danger/30 bg-danger/10 px-3 py-2 text-sm text-danger">
                  {vm.promoteError}
                </div>
              )}
              {vm.promotionPanel.evaluation && (
                <div className="space-y-2 text-sm">
                  <div className="flex items-center gap-2">
                    <span className={cn("font-semibold", promotionTitleToneClass[vm.promotionPanel.evaluation.titleTone])}>
                      {vm.promotionPanel.evaluation.title}
                    </span>
                    <span className="text-muted-foreground">·</span>
                    <span className="text-muted-foreground">{vm.promotionPanel.evaluation.reason}</span>
                  </div>
                  <div className="flex flex-wrap gap-4 font-mono text-xs text-muted-foreground">
                    {vm.promotionPanel.evaluation.metricRows.map((metric) => (
                      <span key={metric.id}>{metric.label} {metric.value}</span>
                    ))}
                  </div>
                  {vm.promotionPanel.evaluation.hasBlockingReasons && (
                    <ul
                      aria-label={vm.promotionPanel.evaluation.blockingListLabel}
                      className="list-inside list-disc space-y-1 text-xs text-danger"
                    >
                      {vm.promotionPanel.evaluation.blockingReasons.map((reason) => (
                        <li key={reason.id}>{reason.text}</li>
                      ))}
                    </ul>
                  )}
                </div>
              )}
              {vm.promotionPanel.sessionCreated && (
                <div className="space-y-2">
                  <div className="text-sm font-semibold text-success">{vm.promotionPanel.sessionCreated.title}</div>
                  <p className="font-mono text-xs text-muted-foreground">{vm.promotionPanel.sessionCreated.detail}</p>
                  <Button
                    size="sm"
                    aria-label={vm.promotionPanel.sessionCreated.actionAriaLabel}
                    onClick={() => { navigate("/trading"); }}
                  >
                    {vm.promotionPanel.sessionCreated.actionLabel}
                  </Button>
                </div>
              )}
              {vm.promotionPanel.showCashForm && (
                <form
                  className="flex flex-wrap items-end gap-3"
                  onSubmit={(e) => { e.preventDefault(); void vm.confirmPromotion(); }}
                  aria-label="Paper promotion session setup"
                  noValidate
                >
                  <div className="space-y-1">
                    <label htmlFor={vm.promotionCashForm.inputId} className="text-xs font-medium uppercase tracking-[0.14em] text-muted-foreground">
                      {vm.promotionCashForm.label}
                    </label>
                    <input
                      id={vm.promotionCashForm.inputId}
                      type="number"
                      min={vm.promotionCashForm.min}
                      step={vm.promotionCashForm.step}
                      value={vm.promotionCashForm.value}
                      onChange={(e) => vm.setPromotionInitialCash(e.target.value)}
                      aria-invalid={vm.promotionCashForm.errorText ? "true" : "false"}
                      aria-describedby={vm.promotionCashForm.describedBy}
                      className={cn(
                        "w-44 rounded-md border bg-background px-3 py-2 font-mono text-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40",
                        vm.promotionCashForm.errorText ? "border-danger/50 text-danger" : "border-border text-foreground"
                      )}
                    />
                    <p
                      id={vm.promotionCashForm.describedBy}
                      className={cn(
                        "max-w-56 text-[11px] leading-4",
                        vm.promotionCashForm.errorText ? "text-danger" : "text-muted-foreground"
                      )}
                    >
                      {vm.promotionCashForm.helpText}
                    </p>
                  </div>
                  <Button
                    type="submit"
                    size="sm"
                    disabled={!vm.promotionCashForm.canSubmit}
                    aria-label={vm.promotionCashForm.submitAriaLabel}
                  >
                    {vm.promotionCashForm.submitLabel}
                  </Button>
                  <Button type="button" size="sm" variant="ghost" onClick={vm.cancelPromotion}>Cancel</Button>
                </form>
              )}
              {vm.promotionPanel.showIneligibleDismiss && (
                <Button size="sm" variant="ghost" onClick={vm.cancelPromotion}>Dismiss</Button>
              )}
            </div>
          )}

          {vm.activePlotToolView === "workspace" ? (
            <PlotToolWorkspacePanel vm={vm.plotTool.workspace} studies={vm.plotTool.studies} />
          ) : (
            <PlotToolStatisticsPanel vm={vm.plotTool.statistics} />
          )}
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <div className="eyebrow-label">Strategy Lane</div>
          <CardTitle>Strategy run library</CardTitle>
          <CardDescription>Review retained runs, compare candidates, and open promotion history from the web workstation.</CardDescription>
        </CardHeader>
        <CardContent className="space-y-4">
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
                    <td className="px-3 py-2"><Badge variant={run.mode === "paper" ? "paper" : run.mode === "live" ? "live" : "research"}>{run.modeLabel}</Badge></td>
                    <td className="px-3 py-2">{run.engineText}</td>
                    <td className="px-3 py-2">{run.statusText}</td>
                    <td className="px-3 py-2 font-mono">{run.pnlText}</td>
                    <td className="px-3 py-2 font-mono">{run.sharpeText}</td>
                    <td className="px-3 py-2">{run.lastUpdatedText}</td>
                    <td className="px-3 py-2">
                      <Button
                        size="sm"
                        variant="outline"
                        aria-haspopup="dialog"
                        aria-label={run.openDetailLabel}
                        onClick={() => vm.openRunDetail(run.raw)}
                      >
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
            <CardDescription>Shared comparison evidence returned by the workstation API.</CardDescription>
          </CardHeader>
          <CardContent>
            <div className="overflow-x-auto rounded-lg border border-border/70">
              <table className="min-w-full divide-y divide-border/60 text-left text-sm" aria-label="Strategy run comparison evidence">
                <caption className="sr-only">{vm.comparisonTable.caption}</caption>
                <thead className="bg-secondary/30">
                  <tr>
                    {["Strategy", "Mode", "Status", "Net P&L", "Return", "Drawdown", "Sharpe", "Fills", "Evidence"].map((column) => (
                      <th key={column} className="px-3 py-2 text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">
                        {column}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody className="divide-y divide-border/50">
                  {vm.comparisonTable.hasRows ? vm.comparisonTable.rows.map((row) => (
                    <tr key={row.runId} aria-label={row.ariaLabel} className="align-top">
                      <td className="px-3 py-2">
                        <div className="font-semibold">{row.strategyName}</div>
                        <div className="mt-1 font-mono text-xs text-muted-foreground">{row.promotionStateText}</div>
                        <div className="mt-1 font-mono text-xs text-muted-foreground">{row.equityText}</div>
                      </td>
                      <td className="px-3 py-2"><Badge variant={row.modeBadgeVariant}>{row.modeText}</Badge></td>
                      <td className="px-3 py-2"><Badge variant={row.statusBadgeVariant} dot>{row.statusText}</Badge></td>
                      <td className={cn("px-3 py-2 font-mono", comparisonValueToneClass[row.netPnlTone])}>{row.netPnlText}</td>
                      <td className={cn("px-3 py-2 font-mono", comparisonValueToneClass[row.totalReturnTone])}>{row.totalReturnText}</td>
                      <td className={cn("px-3 py-2 font-mono", comparisonValueToneClass[row.maxDrawdownTone])}>{row.maxDrawdownText}</td>
                      <td className="px-3 py-2 font-mono">{row.sharpeRatioText}</td>
                      <td className="px-3 py-2 font-mono">{row.fillCountText}</td>
                      <td className="px-3 py-2 font-mono text-xs text-muted-foreground">{row.evidenceText}</td>
                    </tr>
                  )) : (
                    <tr>
                      <td colSpan={9} className="px-3 py-6 text-center text-muted-foreground">
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
        <Card role="region" aria-label={vm.diffPanel.ariaLabel}>
          <CardHeader>
            <CardTitle>{vm.diffPanel.title}</CardTitle>
            <CardDescription>{vm.diffPanel.description}</CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            {vm.diffPanel.metrics.length > 0 && (
              <section aria-label={vm.diffPanel.summaryLabel} className="grid gap-3 sm:grid-cols-3">
                {vm.diffPanel.metrics.map((metric) => (
                  <div key={metric.id} role="group" aria-label={metric.ariaLabel} className="rounded-md border border-border/70 bg-secondary/25 px-3 py-3">
                    <div className="eyebrow-label">{metric.label}</div>
                    <div className={cn("mt-2 font-mono text-sm font-semibold", diffMetricToneClass[metric.tone])}>{metric.value}</div>
                  </div>
                ))}
              </section>
            )}
            <div className="grid gap-4 md:grid-cols-2">
              <section aria-label={vm.diffPanel.positionSectionLabel} className="rounded-lg border border-border/70 bg-secondary/20 p-4">
                <div className="flex items-center justify-between gap-3">
                  <div className="text-sm font-semibold">Position changes</div>
                  <Badge variant={vm.diffPanel.hasPositionChanges ? "outline" : "warning"}>
                    {vm.diffPanel.positionChanges.length} rows
                  </Badge>
                </div>
                <ul aria-label={vm.diffPanel.positionListLabel} className="mt-3 space-y-2 text-sm">
                  {vm.diffPanel.hasPositionChanges ? vm.diffPanel.positionChanges.map((item) => (
                    <li key={item.key} aria-label={item.ariaLabel} className="rounded-md border border-border/60 bg-background/45 px-3 py-2">
                      <div className="flex flex-wrap items-center justify-between gap-2">
                        <span className="font-mono font-semibold">{item.symbolText}</span>
                        <Badge variant={item.badgeVariant}>{item.changeTypeText}</Badge>
                      </div>
                      <div className="mt-2 flex flex-wrap gap-x-3 gap-y-1 font-mono text-xs text-muted-foreground">
                        <span>{item.quantityText}</span>
                        <span>{item.pnlText}</span>
                      </div>
                    </li>
                  )) : (
                    <li className="rounded-md border border-dashed border-border/70 bg-background/35 px-3 py-3 text-muted-foreground">
                      {vm.diffPanel.positionEmptyText}
                    </li>
                  )}
                </ul>
              </section>
              <section aria-label={vm.diffPanel.parameterSectionLabel} className="rounded-lg border border-border/70 bg-secondary/20 p-4">
                <div className="flex items-center justify-between gap-3">
                  <div className="text-sm font-semibold">Parameter changes</div>
                  <Badge variant={vm.diffPanel.hasParameterChanges ? "outline" : "warning"}>
                    {vm.diffPanel.parameterChanges.length} rows
                  </Badge>
                </div>
                <ul aria-label={vm.diffPanel.parameterListLabel} className="mt-3 space-y-2 text-sm">
                  {vm.diffPanel.hasParameterChanges ? vm.diffPanel.parameterChanges.map((item) => (
                    <li key={item.key} aria-label={item.ariaLabel} className="rounded-md border border-border/60 bg-background/45 px-3 py-2 font-mono">
                      <div className="text-foreground">{item.key}</div>
                      <div className="mt-1 text-xs text-muted-foreground">{item.valueText}</div>
                    </li>
                  )) : (
                    <li className="rounded-md border border-dashed border-border/70 bg-background/35 px-3 py-3 text-muted-foreground">
                      {vm.diffPanel.parameterEmptyText}
                    </li>
                  )}
                </ul>
              </section>
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

      <Dialog open={Boolean(vm.selectedRunDetail)} onOpenChange={(open) => { if (!open) vm.closeRunDetail(); }}>
        {vm.selectedRunDetail && (
          <DialogContent
            aria-labelledby={vm.selectedRunDetail.dialogTitleId}
            aria-describedby={vm.selectedRunDetail.dialogDescriptionId}
            className="max-w-2xl"
          >
            <DialogHeader className="mb-5 flex flex-row items-start justify-between gap-4 space-y-0">
              <div className="min-w-0">
                <div className="eyebrow-label">{vm.selectedRunDetail.eyebrow}</div>
                <div className="mt-2 flex flex-wrap items-center gap-2">
                  <DialogTitle id={vm.selectedRunDetail.dialogTitleId}>
                    {vm.selectedRunDetail.title}
                  </DialogTitle>
                  <Badge variant={vm.selectedRunDetail.modeBadgeVariant} dot>
                    {vm.selectedRunDetail.modeBadgeLabel}
                  </Badge>
                </div>
                <DialogDescription id={vm.selectedRunDetail.dialogDescriptionId} className="mt-2">
                  {vm.selectedRunDetail.description}
                </DialogDescription>
                <p className="mt-1 text-xs font-mono text-muted-foreground">{vm.selectedRunDetail.subtitle}</p>
              </div>
              <Button
                variant="ghost"
                size="sm"
                autoFocus
                aria-label={vm.selectedRunDetail.closeButtonAriaLabel}
                onClick={vm.closeRunDetail}
              >
                {vm.selectedRunDetail.closeButtonLabel}
              </Button>
            </DialogHeader>

            <section aria-label={vm.selectedRunDetail.summaryLabel} className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
              {vm.selectedRunDetail.summaryRows.map((row) => (
                <div key={row.id} className="rounded-md border border-border/70 bg-secondary/25 px-3 py-3">
                  <div className="eyebrow-label">{row.label}</div>
                  <div className="mt-2 truncate font-mono text-sm text-foreground">{row.value}</div>
                </div>
              ))}
            </section>

            <section className="mt-4 rounded-md border border-border/70 bg-background/45 px-4 py-3">
              <div className="eyebrow-label">{vm.selectedRunDetail.notesLabel}</div>
              <p className="mt-2 text-sm leading-6 text-muted-foreground">{vm.selectedRunDetail.notesText}</p>
            </section>
          </DialogContent>
        )}
      </Dialog>
    </div>
  );
}

function PlotToolWorkspacePanel({ vm, studies }: { vm: ResearchPlotWorkspaceState; studies: ResearchPlotStudyItem[] }) {
  const studyColumns: DenseDataTableColumn<ResearchPlotStudyItem>[] = [
    {
      id: "study",
      label: "Study",
      render: (study) => (
        <div className="min-w-0">
          <div className="truncate font-semibold text-foreground">{study.title}</div>
          <div className="mt-1 text-xs text-muted-foreground">{study.subtitle}</div>
        </div>
      )
    },
    {
      id: "status",
      label: "Status",
      render: (study) => <Badge variant={study.statusBadgeVariant}>{study.statusBadgeLabel}</Badge>
    },
    {
      id: "metric",
      label: "Metric",
      render: (study) => <span className="font-mono text-xs text-foreground">{study.metricText}</span>
    },
    {
      id: "note",
      label: "Operator note",
      render: (study) => <span className="text-xs leading-5 text-muted-foreground">{study.noteText}</span>
    }
  ];

  return (
    <section
      id="plottool-workspace-panel"
      role="tabpanel"
      aria-labelledby="plottool-workspace-tab"
      className="grid gap-4 xl:grid-cols-[0.9fr_1.55fr_0.85fr]"
    >
      <div className="space-y-4">
        <EntitySummary
          eyebrow={vm.eyebrow}
          title={vm.title}
          subtitle={vm.metaItems.join(" · ")}
          description={vm.description}
          status={<Badge variant={vm.statusBadgeVariant}>{vm.statusBadgeLabel}</Badge>}
          fields={vm.studySummary.map((field) => ({ label: field.label, value: field.value }))}
          ariaLabel="PlotTool study brief"
        />

        <Card className="border-border/70 bg-background/35">
          <CardHeader className="pb-3">
            <CardTitle className="flex items-center gap-2 text-base">
              <BookOpenText className="h-4 w-4 text-primary" />
              Strategy notebooks
            </CardTitle>
            <CardDescription>Retained runs stay docked beside PlotTool instead of leaving the Strategy route.</CardDescription>
          </CardHeader>
          <CardContent className="space-y-3">
            <ToolbarStrip
              ariaLabel="Strategy notebook filters"
              items={[
                { id: "count", label: "Notebook set", value: `${studies.length} retained`, active: true },
                { id: "active", label: "Primary", value: studies.find((study) => study.isActive)?.title ?? "None" },
                { id: "lane", label: "Lane", value: "Strategy" }
              ]}
            />
            <DenseDataTable
              columns={studyColumns}
              rows={studies}
              getRowId={(study) => study.id}
              getRowAriaLabel={(study) => `${study.title}, ${study.statusText}, ${study.metricText}`}
              selectedRowId={studies.find((study) => study.isActive)?.id ?? null}
              emptyText="No retained PlotTool studies are available."
              ariaLabel="Strategy notebooks"
              caption="Retained strategy notebooks aligned to the active PlotTool workspace."
            />
          </CardContent>
        </Card>

        <Card className="border-border/70 bg-background/35">
          <CardHeader className="pb-3">
            <CardTitle className="flex items-center gap-2 text-base">
              <Sparkles className="h-4 w-4 text-primary" />
              {vm.consoleTitle}
            </CardTitle>
          </CardHeader>
          <CardContent className="space-y-3">
            <div className="rounded-md border border-border/70 bg-background/60 px-3 py-3 font-mono text-xs text-foreground">
              {vm.expression}
            </div>
            <p className="text-sm leading-6 text-muted-foreground">{vm.consoleBody}</p>
          </CardContent>
        </Card>
      </div>

      <Card className="border-border/70 bg-background/35">
        <CardHeader className="pb-3">
          <ToolbarStrip
            ariaLabel="PlotTool workspace controls"
            items={vm.toolbarPills.map((pill, index) => ({
              id: `plot-pill-${index}`,
              label: index === 0 ? "Window" : index === 1 ? "Sampling" : index === 2 ? "Overlay" : "Drift",
              value: pill,
              active: index === 0
            }))}
          />
          <div className="space-y-2">
            <div className="eyebrow-label">Scatter / residual view</div>
            <CardTitle>{vm.title}</CardTitle>
            <CardDescription className="font-mono text-xs">{vm.metaItems.join(" · ")}</CardDescription>
          </div>
        </CardHeader>
        <CardContent className="space-y-4">
          <div className="plottool-chart-shell">
            <PlotToolScatterChart
              points={vm.points}
              xTicks={vm.xTicks}
              yTicks={vm.yTicks}
              xAxisLabel={vm.xAxisLabel}
              yAxisLabel={vm.yAxisLabel}
              focusPoint={vm.focusPoint}
            />
          </div>
          <div className="plottool-chart-legend" aria-label="PlotTool chart legend">
            {vm.legendItems.map((item) => <PlotToolLegendItem key={item.id} item={item} />)}
          </div>
          <div className="flex flex-wrap items-center justify-between gap-3 rounded-lg border border-border/70 bg-secondary/15 px-3 py-3 text-xs text-muted-foreground">
            <div>
              <div className="eyebrow-label">{vm.focusPoint.label}</div>
              <div className="mt-1 font-mono text-sm text-foreground">
                {vm.focusPoint.xValueText}
                <span className="px-2 text-muted-foreground">/</span>
                {vm.focusPoint.yValueText}
              </div>
            </div>
            <div className="max-w-sm text-right leading-5">{vm.focusPoint.detail}</div>
          </div>
        </CardContent>
      </Card>

      <div className="space-y-4">
        <Card className="border-border/70 bg-background/35">
          <CardHeader className="pb-3">
            <CardTitle className="flex items-center gap-2 text-base">
              <Sigma className="h-4 w-4 text-primary" />
              Signal console
            </CardTitle>
          </CardHeader>
          <CardContent className="grid gap-3">
            <div className="rounded-lg border border-border/70 bg-background/70 px-3 py-3">
              <div className="eyebrow-label">{vm.focusPoint.label}</div>
              <div className="mt-2 flex items-baseline gap-2 font-mono text-lg text-foreground">
                <span>{vm.focusPoint.xValueText}</span>
                <span className="text-muted-foreground">/</span>
                <span>{vm.focusPoint.yValueText}</span>
              </div>
              <p className="mt-2 text-xs leading-5 text-muted-foreground">{vm.focusPoint.detail}</p>
            </div>
            {vm.signalCards.map((card) => (
              <div key={card.id} className="rounded-lg border border-border/70 bg-secondary/20 px-3 py-3">
                <div className="eyebrow-label">{card.label}</div>
                <div className={cn("mt-2 font-mono text-lg font-semibold", plotToneClass[card.tone])}>{card.value}</div>
                <p className="mt-1 text-xs leading-5 text-muted-foreground">{card.detail}</p>
              </div>
            ))}
          </CardContent>
        </Card>

        <Card className="border-border/70 bg-background/35">
          <CardHeader className="pb-3">
            <CardTitle className="flex items-center gap-2 text-base">
              <BarChart3 className="h-4 w-4 text-primary" />
              {vm.overlayTitle}
            </CardTitle>
          </CardHeader>
          <CardContent>
            <ul className="plottool-overlay-list space-y-3 text-sm text-muted-foreground">
              {vm.overlayItems.map((item) => (
                <li key={item} className="rounded-md border border-border/70 bg-secondary/20 px-3 py-3 leading-6">
                  {item}
                </li>
              ))}
            </ul>
          </CardContent>
        </Card>
      </div>
    </section>
  );
}

function PlotToolStatisticsPanel({ vm }: { vm: ResearchPlotStatisticsState }) {
  return (
    <section
      id="plottool-statistics-panel"
      role="tabpanel"
      aria-labelledby="plottool-statistics-tab"
      className="space-y-4"
    >
      <Card className="border-border/70 bg-background/35">
        <CardHeader className="pb-3">
          <div className="eyebrow-label">{vm.eyebrow}</div>
          <CardTitle className="flex items-center gap-2">
            <Sigma className="h-5 w-5 text-primary" />
            {vm.title}
          </CardTitle>
          <CardDescription>{vm.description}</CardDescription>
        </CardHeader>
        <CardContent className="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
          {vm.summaryTiles.map((tile) => (
            <div key={tile.id} className="rounded-lg border border-border/70 bg-secondary/20 px-3 py-3">
              <div className="eyebrow-label">{tile.label}</div>
              <div className={cn("mt-2 font-mono text-lg font-semibold", plotToneClass[tile.tone])}>{tile.value}</div>
              <div className="mt-1 text-xs text-muted-foreground">{tile.detail}</div>
            </div>
          ))}
        </CardContent>
      </Card>

      <div className="grid gap-4 xl:grid-cols-[1.1fr_0.9fr]">
        <Card className="border-border/70 bg-background/35">
          <CardHeader className="pb-3">
            <CardTitle className="text-base">Distribution profile</CardTitle>
            <CardDescription>Histogram snapshot for the active PlotTool scatter study.</CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="rounded-lg border border-border/70 bg-secondary/15 px-3 py-3 text-xs text-muted-foreground">
              <div className="font-mono uppercase tracking-[0.14em] text-foreground">Residual distribution</div>
              <p className="mt-2 leading-5">{vm.distributionSummary}</p>
            </div>
            <div className="flex h-56 items-end gap-1 rounded-lg border border-border/70 bg-[#05101B] px-3 py-4" aria-label="PlotTool distribution chart">
              {vm.distributionBars.map((bar, index) => (
                <div
                  key={`${bar}-${index}`}
                  className={cn("flex-1 rounded-t-sm", index >= 8 && index <= 14 ? "bg-primary" : "bg-primary/65")}
                  style={{ height: `${Math.max(bar, 4)}%` }}
                />
              ))}
            </div>
            <p className="text-xs leading-5 text-muted-foreground">{vm.distributionFootnote}</p>
          </CardContent>
        </Card>

        <Card className="border-border/70 bg-background/35">
          <CardHeader className="pb-3">
            <CardTitle className="text-base">Regression frame</CardTitle>
            <CardDescription>OLS summary anchored to run evidence and operator review posture.</CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="rounded-lg border border-border/70 bg-secondary/20 px-4 py-4 text-center font-mono text-lg text-foreground">
              {vm.regression.equation}
            </div>
            <ul className="space-y-2 text-sm text-muted-foreground">
              {vm.regression.detailItems.map((item) => (
                <li key={item} className="rounded-md border border-border/70 bg-secondary/20 px-3 py-3 leading-6">
                  {item}
                </li>
              ))}
            </ul>
          </CardContent>
        </Card>
      </div>

      <div className="grid gap-4 xl:grid-cols-[0.95fr_1.05fr]">
        <Card className="border-border/70 bg-background/35">
          <CardHeader className="pb-3">
            <CardTitle className="text-base">Moments</CardTitle>
            <CardDescription>Meridian-ready statistical readout with promotion and evidence cues.</CardDescription>
          </CardHeader>
          <CardContent>
            <div className="overflow-hidden rounded-lg border border-border/70">
              <table className="min-w-full divide-y divide-border/60 text-left text-sm">
                <thead className="bg-secondary/30">
                  <tr>
                    <th className="px-3 py-2 text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">Metric</th>
                    <th className="px-3 py-2 text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">Value</th>
                    <th className="px-3 py-2 text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">Benchmark</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-border/50">
                  {vm.moments.map((moment) => (
                    <tr key={moment.id}>
                      <td className="px-3 py-2 text-muted-foreground">{moment.label}</td>
                      <td className="px-3 py-2 font-mono text-foreground">{moment.value}</td>
                      <td className="px-3 py-2 text-muted-foreground">{moment.benchmark}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </CardContent>
        </Card>

        <Card className="border-border/70 bg-background/35">
          <CardHeader className="pb-3">
            <CardTitle className="text-base">Observation sheet</CardTitle>
            <CardDescription>Recent records packaged for analyst review without leaving the Strategy lane.</CardDescription>
          </CardHeader>
          <CardContent>
            <div className="overflow-hidden rounded-lg border border-border/70">
              <table className="min-w-full divide-y divide-border/60 text-left text-sm">
                <thead className="bg-secondary/30">
                  <tr>
                    {["Date", "Spread", "3m IV", "Z-score", "Signal"].map((column) => (
                      <th key={column} className="px-3 py-2 text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">
                        {column}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody className="divide-y divide-border/50">
                  {vm.sampleRows.map((row) => <PlotToolObservationRow key={row.id} row={row} />)}
                </tbody>
              </table>
            </div>
          </CardContent>
        </Card>
      </div>
    </section>
  );
}

function PlotToolScatterChart({
  points,
  xTicks,
  yTicks,
  xAxisLabel,
  yAxisLabel,
  focusPoint
}: {
  points: ResearchPlotScatterPoint[];
  xTicks: ResearchPlotWorkspaceState["xTicks"];
  yTicks: ResearchPlotWorkspaceState["yTicks"];
  xAxisLabel: string;
  yAxisLabel: string;
  focusPoint: ResearchPlotWorkspaceState["focusPoint"];
}) {
  const markerPoint = findLastPlotPoint(points, (point) => point.emphasis) ?? points[points.length - 1];

  return (
    <svg viewBox="0 0 640 320" className="h-[320px] w-full" role="img" aria-label="PlotTool scatter chart">
      <g stroke="#18283C" strokeWidth="1">
        {[40, 90, 140, 190, 240, 290].map((y) => <line key={`h-${y}`} x1="50" y1={y} x2="610" y2={y} />)}
        {[50, 130, 210, 290, 370, 450, 530, 610].map((x) => <line key={`v-${x}`} x1={x} y1="40" x2={x} y2="290" />)}
      </g>
      <g fill="#A8B5C4" fontFamily="IBM Plex Mono" fontSize="10">
        {yTicks.map((tick) => (
          <text key={`y-${tick.value}`} x="44" y={tick.value} textAnchor="end">
            {tick.label}
          </text>
        ))}
        {xTicks.map((tick) => (
          <text key={`x-${tick.value}`} x={tick.value + 20} y="304" textAnchor="middle">
            {tick.label}
          </text>
        ))}
        <text x="330" y="318" textAnchor="middle">{xAxisLabel}</text>
        <text x="16" y="170" textAnchor="middle" transform="rotate(-90 16 170)">{yAxisLabel}</text>
      </g>
      <polyline
        fill="none"
        stroke="#26BF86"
        strokeWidth="2"
        strokeDasharray="5 4"
        points="90,250 150,222 210,198 270,170 330,142 390,114 450,88 510,66 564,54"
      />
      <line x1={markerPoint.x} y1="40" x2={markerPoint.x} y2="290" stroke="#E6A93C" strokeDasharray="4 4" opacity="0.55" />
      <line x1="50" y1={markerPoint.y} x2="610" y2={markerPoint.y} stroke="#E6A93C" strokeDasharray="4 4" opacity="0.55" />
      {points.map((point, index) => (
        <circle
          key={`${point.x}-${point.y}-${index}`}
          cx={point.x}
          cy={point.y}
          r={point.emphasis ? 5 : 3.25}
          fill={point.emphasis ? "#26BF86" : "#2AB2D4"}
          fillOpacity={point.emphasis ? 0.95 : 0.65}
        />
      ))}
      <circle cx={markerPoint.x} cy={markerPoint.y} r="6" fill="#E6A93C" stroke="#05101B" strokeWidth="2" />
      <rect x={Math.min(markerPoint.x + 10, 512)} y={Math.max(markerPoint.y - 26, 52)} width="96" height="22" rx="4" fill="#0B1520" stroke="#E6A93C" />
      <text x={Math.min(markerPoint.x + 18, 520)} y={Math.max(markerPoint.y - 12, 66)} fill="#E6A93C" fontFamily="IBM Plex Mono" fontSize="10">
        {focusPoint.xValueText}, {focusPoint.yValueText}
      </text>
    </svg>
  );
}

function PlotToolLegendItem({ item }: { item: ResearchPlotLegendItem }) {
  return (
    <div className="plottool-legend-card">
      <div className="flex items-center gap-2 text-xs text-foreground">
        <span className={cn("h-2.5 w-2.5 rounded-full", plotLegendToneClass[item.tone])} aria-hidden="true" />
        <span className="font-mono uppercase tracking-[0.14em]">{item.label}</span>
      </div>
      <div className="mt-1 text-xs text-muted-foreground">{item.detail}</div>
    </div>
  );
}

function findLastPlotPoint<T>(items: T[], predicate: (item: T) => boolean): T | undefined {
  for (let index = items.length - 1; index >= 0; index -= 1) {
    if (predicate(items[index])) {
      return items[index];
    }
  }

  return undefined;
}

function PlotToolObservationRow({ row }: { row: ResearchPlotSampleRow }) {
  return (
    <tr>
      <td className="px-3 py-2 text-muted-foreground">{row.timestamp}</td>
      <td className="px-3 py-2 font-mono text-foreground">{row.spreadText}</td>
      <td className="px-3 py-2 font-mono text-foreground">{row.impliedVolText}</td>
      <td className={cn("px-3 py-2 font-mono", plotToneClass[row.tone])}>{row.zScoreText}</td>
      <td className="px-3 py-2">
        <Badge variant={sampleToneBadgeVariant[row.tone]}>{row.signalText}</Badge>
      </td>
    </tr>
  );
}
