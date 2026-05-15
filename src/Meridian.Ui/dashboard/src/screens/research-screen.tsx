import { useMemo, useRef } from "react";
import { BarChart3, BookOpenText, ChartScatter, Network, Sigma, Sparkles } from "lucide-react";
import { Link, useNavigate } from "react-router-dom";
import { MetricCard } from "@/components/meridian/metric-card";
import { QuantNotebook } from "@/components/meridian/quant-notebook";
import { useQuantNotebookViewModel } from "@/components/meridian/quant-notebook.view-model";
import { DenseDataTable, EntitySummary, ToolbarStrip, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { cn } from "@/lib/utils";
import { useResearchRunLibraryViewModel } from "@/screens/research-screen.view-model";
import type {
  ResearchComparisonTableRow,
  ResearchDiffChangeRow,
  ResearchDiffDetailState,
  ResearchParameterChangeRow,
  ResearchPlotLegendItem,
  ResearchPlotMomentRow,
  ResearchPlotSampleRow,
  ResearchPlotScatterChartState,
  ResearchPlotStatisticsState,
  ResearchPlotStudyDetailState,
  ResearchPlotStudyItem,
  ResearchPlotWorkspaceState,
  ResearchPromotionHistoryRow,
  ResearchRunTableRow
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

const distributionBarToneClass = {
  selected: "bg-primary",
  base: "bg-primary/65"
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

const plotToolMomentColumns: DenseDataTableColumn<ResearchPlotMomentRow>[] = [
  {
    id: "metric",
    label: "Metric",
    className: "text-muted-foreground",
    render: (moment) => moment.label
  },
  {
    id: "value",
    label: "Value",
    className: "font-mono",
    render: (moment) => moment.value
  },
  {
    id: "benchmark",
    label: "Benchmark",
    className: "text-muted-foreground",
    render: (moment) => moment.benchmark
  }
];

const diffPositionColumns: DenseDataTableColumn<ResearchDiffChangeRow>[] = [
  {
    id: "symbol",
    label: "Symbol",
    className: "font-mono font-semibold text-foreground",
    render: (row) => row.symbolText
  },
  {
    id: "change",
    label: "Change",
    render: (row) => <Badge variant={row.badgeVariant}>{row.changeTypeText}</Badge>
  },
  {
    id: "quantity",
    label: "Qty delta",
    align: "right",
    className: "font-mono",
    render: (row) => row.quantityText
  },
  {
    id: "pnl",
    label: "P&L delta",
    align: "right",
    className: "font-mono",
    render: (row) => row.pnlText
  }
];

const diffParameterColumns: DenseDataTableColumn<ResearchParameterChangeRow>[] = [
  {
    id: "parameter",
    label: "Parameter",
    className: "font-mono font-semibold text-foreground",
    render: (row) => row.key
  },
  {
    id: "base",
    label: "Base",
    className: "font-mono text-muted-foreground",
    render: (row) => row.baseValueText
  },
  {
    id: "target",
    label: "Target",
    className: "font-mono",
    render: (row) => row.targetValueText
  }
];

const plotToolObservationColumns: DenseDataTableColumn<ResearchPlotSampleRow>[] = [
  {
    id: "date",
    label: "Date",
    className: "text-muted-foreground",
    render: (row) => row.timestamp
  },
  {
    id: "spread",
    label: "Spread",
    className: "font-mono",
    render: (row) => row.spreadText
  },
  {
    id: "implied-vol",
    label: "3m IV",
    className: "font-mono",
    render: (row) => row.impliedVolText
  },
  {
    id: "z-score",
    label: "Z-score",
    className: "font-mono",
    render: (row) => <span className={plotToneClass[row.tone]}>{row.zScoreText}</span>
  },
  {
    id: "signal",
    label: "Signal",
    render: (row) => <Badge variant={sampleToneBadgeVariant[row.tone]}>{row.signalText}</Badge>
  }
];

export function ResearchScreen({ data }: ResearchScreenProps) {
  const vm = useResearchRunLibraryViewModel(data);
  const navigate = useNavigate();
  const plotToolTabRefs = useRef<Record<string, HTMLButtonElement | null>>({});

  if (!data) {
    return (
      <Card
        role={vm.loadingState.role}
        aria-busy={vm.loadingState.ariaBusy}
        aria-live={vm.loadingState.ariaLive}
        aria-labelledby={vm.loadingState.titleId}
        aria-describedby={vm.loadingState.detailId}
        className="panel-surface border-[var(--state-pending-bd)] bg-[var(--state-pending-bg)]"
      >
        <CardHeader className="space-y-3">
          <div className="flex flex-wrap items-center gap-2">
            <Badge
              variant="outline"
              className="border-[var(--state-pending-bd)] bg-[var(--state-pending-bg)] text-[var(--state-pending-fg)]"
              dot
            >
              {vm.loadingState.badgeLabel}
            </Badge>
            <span className="toolbar-chip" aria-label={`Route ${vm.loadingState.routeLabel}`}>
              <span className="text-muted-foreground">Route</span>
              <b>{vm.loadingState.routeLabel}</b>
            </span>
          </div>
          <CardTitle id={vm.loadingState.titleId}>{vm.loadingState.title}</CardTitle>
          <CardDescription id={vm.loadingState.detailId}>{vm.loadingState.detail}</CardDescription>
        </CardHeader>
      </Card>
    );
  }

  const runColumns = useMemo<DenseDataTableColumn<ResearchRunTableRow>[]>(() => [
    {
      id: "compare",
      label: "",
      render: (run) => (
        <input
          type="checkbox"
          aria-label={run.selectAriaLabel}
          checked={run.selectedForComparison}
          onChange={() => vm.toggleRun(run.id)}
          className="h-4 w-4 rounded border-border bg-background text-primary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
        />
      )
    },
    {
      id: "strategy",
      label: "Strategy",
      render: (run) => <span className="font-semibold text-foreground">{run.strategyName}</span>
    },
    {
      id: "mode",
      label: "Mode",
      render: (run) => <Badge variant={run.modeBadgeVariant}>{run.modeLabel}</Badge>
    },
    {
      id: "engine",
      label: "Engine",
      render: (run) => run.engineText
    },
    {
      id: "status",
      label: "Status",
      render: (run) => run.statusText
    },
    {
      id: "pnl",
      label: "P&L",
      align: "right",
      className: "font-mono",
      render: (run) => run.pnlText
    },
    {
      id: "sharpe",
      label: "Sharpe",
      align: "right",
      className: "font-mono",
      render: (run) => run.sharpeText
    },
    {
      id: "updated",
      label: "Updated",
      render: (run) => <span className="font-mono text-xs text-muted-foreground">{run.lastUpdatedText}</span>
    }
  ], [vm]);

  const promotionHistoryColumns = useMemo<DenseDataTableColumn<ResearchPromotionHistoryRow>[]>(() => [
    {
      id: "strategy",
      label: "Strategy",
      render: (record) => (
        <span className="font-semibold text-foreground">{record.strategyName}</span>
      )
    },
    {
      id: "route",
      label: "Route",
      render: (record) => record.routeText
    },
    {
      id: "decision",
      label: "Decision",
      render: (record) => record.decisionText
    },
    {
      id: "sharpe",
      label: "Sharpe",
      align: "right",
      render: (record) => record.qualifyingSharpeText
    },
    {
      id: "promoted",
      label: "Promoted",
      render: (record) => <span className="font-mono text-xs">{record.promotedAtText}</span>
    }
  ], []);

  const comparisonColumns = useMemo<DenseDataTableColumn<ResearchComparisonTableRow>[]>(() => [
    {
      id: "strategy",
      label: "Strategy",
      render: (row) => (
        <span className="font-semibold text-foreground">{row.strategyName}</span>
      )
    },
    {
      id: "mode",
      label: "Mode",
      render: (row) => <Badge variant={row.modeBadgeVariant}>{row.modeText}</Badge>
    },
    {
      id: "status",
      label: "Status",
      render: (row) => <Badge variant={row.statusBadgeVariant} dot>{row.statusText}</Badge>
    },
    {
      id: "net-pnl",
      label: "Net P&L",
      align: "right",
      className: "font-mono",
      render: (row) => <span className={comparisonValueToneClass[row.netPnlTone]}>{row.netPnlText}</span>
    },
    {
      id: "return",
      label: "Return",
      align: "right",
      className: "font-mono",
      render: (row) => <span className={comparisonValueToneClass[row.totalReturnTone]}>{row.totalReturnText}</span>
    },
    {
      id: "drawdown",
      label: "Drawdown",
      align: "right",
      className: "font-mono",
      render: (row) => <span className={comparisonValueToneClass[row.maxDrawdownTone]}>{row.maxDrawdownText}</span>
    },
    {
      id: "sharpe",
      label: "Sharpe",
      align: "right",
      className: "font-mono",
      render: (row) => row.sharpeRatioText
    },
    {
      id: "fills",
      label: "Fills",
      align: "right",
      className: "font-mono",
      render: (row) => row.fillCountText
    }
  ], []);

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
                  const focusTargetTabId = vm.selectPlotToolViewForKey(event.key);
                  if (focusTargetTabId) {
                    event.preventDefault();
                    plotToolTabRefs.current[focusTargetTabId]?.focus();
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
                    ref={(node) => {
                      plotToolTabRefs.current[tab.tabId] = node;
                    }}
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
              <Button
                variant="secondary"
                onClick={() => void vm.loadPromotionHistory()}
                disabled={vm.promotionHistoryCommand.disabled}
                disabledReason={vm.promotionHistoryCommand.disabledReason}
                busy={vm.promotionHistoryCommand.busy}
                busyLabel={vm.promotionHistoryCommand.label}
                aria-label={vm.promotionHistoryCommand.ariaLabel}
              >
                {vm.promotionHistoryCommand.label}
              </Button>
              <Button
                variant="outline"
                onClick={() => void vm.compareSelectedRuns()}
                disabled={vm.compareCommand.disabled}
                disabledReason={vm.compareCommand.disabledReason}
                busy={vm.compareCommand.busy}
                busyLabel={vm.compareCommand.label}
                aria-label={vm.compareCommand.ariaLabel}
              >
                {vm.compareCommand.label}
              </Button>
              <Button
                variant="outline"
                onClick={() => void vm.diffSelectedRuns()}
                disabled={vm.diffCommand.disabled}
                disabledReason={vm.diffCommand.disabledReason}
                busy={vm.diffCommand.busy}
                busyLabel={vm.diffCommand.label}
                aria-label={vm.diffCommand.ariaLabel}
              >
                {vm.diffCommand.label}
              </Button>
              <Button
                variant="default"
                onClick={() => void vm.promoteSelectedRun()}
                disabled={vm.promoteCommand.disabled}
                disabledReason={vm.promoteCommand.disabledReason}
                busy={vm.promoteCommand.busy}
                busyLabel={vm.promoteCommand.label}
                aria-label={vm.promoteCommand.ariaLabel}
              >
                {vm.promoteCommand.label}
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
                    onClick={() => { navigate(vm.promotionPanel.sessionCreated.actionHref); }}
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
                      disabled={vm.promotionCashForm.inputDisabled}
                      title={vm.promotionCashForm.inputDisabledReason ?? undefined}
                      aria-invalid={vm.promotionCashForm.errorText ? "true" : "false"}
                      aria-describedby={vm.promotionCashForm.inputDescribedBy}
                      className={cn(
                        "w-44 rounded-md border bg-background px-3 py-2 font-mono text-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40",
                        vm.promotionCashForm.errorText ? "border-danger/50 text-danger" : "border-border text-foreground"
                      )}
                    />
                    <p
                      id={vm.promotionCashForm.inputHelpId}
                      className={cn(
                        "max-w-56 text-[11px] leading-4",
                        vm.promotionCashForm.errorText ? "text-danger" : "text-muted-foreground"
                      )}
                    >
                      {vm.promotionCashForm.helpText}
                    </p>
                    {vm.promotionCashForm.inputDisabledReason ? (
                      <p
                        id={vm.promotionCashForm.inputDisabledReasonId ?? undefined}
                        className="max-w-56 rounded-sm border border-warning/30 bg-warning/10 px-2 py-1 text-[11px] leading-4 text-warning"
                      >
                        {vm.promotionCashForm.inputDisabledReason}
                      </p>
                    ) : null}
                  </div>
                  <label
                    htmlFor={vm.promotionCashForm.acknowledgementId}
                    className="flex max-w-sm items-start gap-2 rounded-md border border-border/70 bg-background/45 px-3 py-2 text-xs leading-5 text-muted-foreground"
                  >
                    <input
                      id={vm.promotionCashForm.acknowledgementId}
                      type="checkbox"
                      checked={vm.promotionCashForm.acknowledgementChecked}
                      onChange={(e) => vm.setPromotionAcknowledgement(e.target.checked)}
                      disabled={vm.promotionCashForm.acknowledgementDisabled}
                      title={vm.promotionCashForm.acknowledgementDisabledReason ?? undefined}
                      aria-describedby={vm.promotionCashForm.acknowledgementDescribedBy}
                      className="mt-0.5 h-4 w-4 rounded border-border bg-background text-primary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
                    />
                    <span className="grid gap-1">
                      <span>{vm.promotionCashForm.acknowledgementLabel}</span>
                      {vm.promotionCashForm.acknowledgementDisabledReason ? (
                        <span
                          id={vm.promotionCashForm.acknowledgementDisabledReasonId ?? undefined}
                          className="rounded-sm border border-warning/30 bg-warning/10 px-2 py-1 text-[11px] leading-4 text-warning"
                        >
                          {vm.promotionCashForm.acknowledgementDisabledReason}
                        </span>
                      ) : null}
                    </span>
                  </label>
                  <Button
                    type="submit"
                    size="sm"
                    disabled={!vm.promotionCashForm.canSubmit}
                    disabledReason={vm.promotionCashForm.disabledReason}
                    aria-label={vm.promotionCashForm.submitAriaLabel}
                  >
                    {vm.promotionCashForm.submitLabel}
                  </Button>
                  <Button
                    type="button"
                    size="sm"
                    variant="ghost"
                    onClick={vm.cancelPromotion}
                    disabled={vm.promotionCashForm.cancelDisabled}
                    disabledReason={vm.promotionCashForm.cancelDisabledReason}
                    aria-label={vm.promotionCashForm.cancelAriaLabel}
                  >
                    {vm.promotionCashForm.cancelLabel}
                  </Button>
                </form>
              )}
              {vm.promotionPanel.showIneligibleDismiss && (
                <Button size="sm" variant="ghost" onClick={vm.cancelPromotion}>Dismiss</Button>
              )}
            </div>
          )}

          {vm.activePlotToolView === "workspace" ? (
            <PlotToolWorkspacePanel
              vm={vm.plotTool.workspace}
              studies={vm.plotTool.studies}
              selectedStudyId={vm.selectedPlotStudyId}
              selectedStudyDetail={vm.selectedPlotStudyDetail}
              studyDetailPanelId={vm.selectedPlotStudyDetailPanelId}
              onStudySelect={vm.selectPlotStudy}
            />
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
          <div className="grid gap-4 xl:grid-cols-[minmax(0,1fr)_minmax(280px,360px)]">
            <DenseDataTable
              columns={runColumns}
              rows={vm.runTable.rows}
              getRowId={(run) => run.id}
              getRowAriaLabel={(run) => run.rowAriaLabel}
              getRowSelectAriaLabel={(run) => run.rowSelectAriaLabel}
              getRowAriaControls={(run) => run.detailPanelId}
              getRowAriaExpanded={(run) => run.detailExpanded}
              selectedRowId={vm.inspectedRunId}
              onRowSelect={(run) => vm.selectRunDetail(run.id)}
              emptyText={vm.runTable.emptyText}
              ariaLabel="Strategy run library"
              caption={vm.runTable.caption}
            />
            {vm.inspectedRunDetail ? (
              <div id={vm.inspectedRunDetail.panelId} className="min-w-0">
                <EntitySummary
                  eyebrow={vm.inspectedRunDetail.eyebrow}
                  title={vm.inspectedRunDetail.title}
                  subtitle={vm.inspectedRunDetail.subtitle}
                  description={vm.inspectedRunDetail.description}
                  fields={vm.inspectedRunDetail.fields}
                  ariaLabel={vm.inspectedRunDetail.ariaLabel}
                  status={<Badge variant={vm.inspectedRunDetail.statusVariant}>{vm.inspectedRunDetail.statusLabel}</Badge>}
                  actions={(
                    <>
                      <Button
                        size="sm"
                        variant="outline"
                        aria-haspopup="dialog"
                        aria-label={vm.inspectedRunDetail.openDetailLabel}
                        onClick={() => vm.openRunDetailById(vm.inspectedRunDetail!.id)}
                      >
                        Open
                      </Button>
                      <Button asChild size="sm" variant="ghost">
                        <Link to={vm.inspectedRunDetail.evidenceAction.href} aria-label={vm.inspectedRunDetail.evidenceAction.ariaLabel}>
                          {vm.inspectedRunDetail.evidenceAction.label}
                        </Link>
                      </Button>
                    </>
                  )}
                />
              </div>
            ) : (
              <div
                id={vm.selectedRunDetailPanelId}
                role="status"
                className="row-detail-panel h-fit min-w-0 border-dashed text-sm text-muted-foreground"
              >
                {vm.runTable.emptyText}
              </div>
            )}
          </div>
        </CardContent>
      </Card>

      {vm.showComparisonPanel && (
        <Card>
          <CardHeader>
            <CardTitle>Run comparison</CardTitle>
            <CardDescription>Shared comparison evidence returned by the workstation API.</CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="grid gap-4 xl:grid-cols-[minmax(0,1fr)_minmax(280px,360px)]">
              <DenseDataTable
                columns={comparisonColumns}
                rows={vm.comparisonTable.rows}
                getRowId={(row) => row.runId}
                getRowAriaLabel={(row) => row.ariaLabel}
                getRowSelectAriaLabel={(row) => row.rowSelectAriaLabel}
                getRowAriaControls={(row) => row.detailPanelId}
                getRowAriaExpanded={(row) => row.detailExpanded}
                selectedRowId={vm.selectedComparisonRowId}
                onRowSelect={(row) => vm.selectComparisonRow(row.runId)}
                emptyText={vm.comparisonTable.emptyText}
                ariaLabel="Strategy run comparison evidence"
                caption={vm.comparisonTable.caption}
              />
              {vm.selectedComparisonDetail ? (
                <div id={vm.selectedComparisonDetail.panelId} className="min-w-0">
                  <EntitySummary
                    eyebrow={vm.selectedComparisonDetail.eyebrow}
                    title={vm.selectedComparisonDetail.title}
                    subtitle={vm.selectedComparisonDetail.subtitle}
                    description={vm.selectedComparisonDetail.description}
                    fields={vm.selectedComparisonDetail.fields}
                    ariaLabel={vm.selectedComparisonDetail.ariaLabel}
                    status={<Badge variant={vm.selectedComparisonDetail.statusVariant} dot>{vm.selectedComparisonDetail.statusLabel}</Badge>}
                  />
                </div>
              ) : (
                <div
                  id={vm.selectedComparisonDetailPanelId}
                  role="status"
                  className="row-detail-panel h-fit min-w-0 border-dashed text-sm text-muted-foreground"
                >
                  {vm.comparisonTable.emptyText}
                </div>
              )}
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
            <div className="grid gap-4 2xl:grid-cols-2">
              <section aria-label={vm.diffPanel.positionSectionLabel} className="rounded-lg border border-border/70 bg-secondary/20 p-4">
                <div className="flex items-center justify-between gap-3">
                  <div className="text-sm font-semibold">Position changes</div>
                  <Badge variant={vm.diffPanel.hasPositionChanges ? "outline" : "warning"}>
                    {vm.diffPanel.positionChanges.length} rows
                  </Badge>
                </div>
                <div className="mt-3 grid gap-3 xl:grid-cols-[minmax(0,1fr)_minmax(260px,0.42fr)]">
                  <DenseDataTable
                    columns={diffPositionColumns}
                    rows={vm.diffPanel.positionTable.rows}
                    getRowId={(row) => row.key}
                    getRowAriaLabel={(row) => row.ariaLabel}
                    getRowSelectAriaLabel={(row) => row.rowSelectAriaLabel}
                    getRowAriaControls={(row) => row.detailPanelId}
                    getRowAriaExpanded={(row) => row.detailExpanded}
                    selectedRowId={vm.diffPanel.selectedPositionKey}
                    onRowSelect={(row) => vm.selectDiffPositionChange(row.key)}
                    emptyText={vm.diffPanel.positionTable.emptyText}
                    ariaLabel={vm.diffPanel.positionListLabel}
                    caption={vm.diffPanel.positionTable.caption}
                  />
                  <ResearchDiffDetailPanel
                    id={vm.diffPanel.selectedPositionDetailPanelId}
                    detail={vm.diffPanel.selectedPositionDetail}
                    emptyText={vm.diffPanel.positionEmptyText}
                  />
                </div>
              </section>
              <section aria-label={vm.diffPanel.parameterSectionLabel} className="rounded-lg border border-border/70 bg-secondary/20 p-4">
                <div className="flex items-center justify-between gap-3">
                  <div className="text-sm font-semibold">Parameter changes</div>
                  <Badge variant={vm.diffPanel.hasParameterChanges ? "outline" : "warning"}>
                    {vm.diffPanel.parameterChanges.length} rows
                  </Badge>
                </div>
                <div className="mt-3 grid gap-3 xl:grid-cols-[minmax(0,1fr)_minmax(260px,0.42fr)]">
                  <DenseDataTable
                    columns={diffParameterColumns}
                    rows={vm.diffPanel.parameterTable.rows}
                    getRowId={(row) => row.key}
                    getRowAriaLabel={(row) => row.ariaLabel}
                    getRowSelectAriaLabel={(row) => row.rowSelectAriaLabel}
                    getRowAriaControls={(row) => row.detailPanelId}
                    getRowAriaExpanded={(row) => row.detailExpanded}
                    selectedRowId={vm.diffPanel.selectedParameterKey}
                    onRowSelect={(row) => vm.selectDiffParameterChange(row.key)}
                    emptyText={vm.diffPanel.parameterTable.emptyText}
                    ariaLabel={vm.diffPanel.parameterListLabel}
                    caption={vm.diffPanel.parameterTable.caption}
                  />
                  <ResearchDiffDetailPanel
                    id={vm.diffPanel.selectedParameterDetailPanelId}
                    detail={vm.diffPanel.selectedParameterDetail}
                    emptyText={vm.diffPanel.parameterEmptyText}
                  />
                </div>
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
          <CardContent className="space-y-4">
            <div className="grid gap-4 xl:grid-cols-[minmax(0,1fr)_minmax(280px,360px)]">
              <DenseDataTable
                columns={promotionHistoryColumns}
                rows={vm.promotionHistoryTable.rows}
                getRowId={(record) => record.promotionId}
                getRowAriaLabel={(record) => record.ariaLabel}
                getRowSelectAriaLabel={(record) => record.rowSelectAriaLabel}
                getRowAriaControls={(record) => record.detailPanelId}
                getRowAriaExpanded={(record) => record.detailExpanded}
                selectedRowId={vm.selectedPromotionHistoryId}
                onRowSelect={(record) => vm.selectPromotionHistoryRecord(record.promotionId)}
                emptyText={vm.promotionHistoryTable.emptyText}
                ariaLabel="Promotion history decisions"
                caption={vm.promotionHistoryTable.caption}
              />
              {vm.selectedPromotionHistoryDetail ? (
                <div id={vm.selectedPromotionHistoryDetail.panelId} className="min-w-0">
                  <EntitySummary
                    eyebrow={vm.selectedPromotionHistoryDetail.eyebrow}
                    title={vm.selectedPromotionHistoryDetail.title}
                    subtitle={vm.selectedPromotionHistoryDetail.subtitle}
                    description={vm.selectedPromotionHistoryDetail.description}
                    fields={vm.selectedPromotionHistoryDetail.fields}
                    ariaLabel={vm.selectedPromotionHistoryDetail.ariaLabel}
                    status={(
                      <Badge variant={vm.selectedPromotionHistoryDetail.statusVariant}>
                        {vm.selectedPromotionHistoryDetail.statusLabel}
                      </Badge>
                    )}
                  />
                </div>
              ) : (
                <div
                  id={vm.selectedPromotionHistoryDetailPanelId}
                  role="status"
                  className="row-detail-panel h-fit min-w-0 border-dashed text-sm text-muted-foreground"
                >
                  {vm.promotionHistoryTable.emptyText}
                </div>
              )}
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

function ResearchDiffDetailPanel({
  id,
  detail,
  emptyText
}: {
  id: string;
  detail: ResearchDiffDetailState | null;
  emptyText: string;
}) {
  if (!detail) {
    return (
      <div
        id={id}
        role="status"
        className="row-detail-panel h-fit min-w-0 border-dashed text-sm text-muted-foreground"
      >
        {emptyText}
      </div>
    );
  }

  return (
    <div id={detail.panelId} className="min-w-0">
      <EntitySummary
        eyebrow={detail.eyebrow}
        title={detail.title}
        subtitle={detail.subtitle}
        description={detail.description}
        fields={detail.fields}
        ariaLabel={detail.ariaLabel}
        status={<Badge variant={detail.statusVariant}>{detail.statusLabel}</Badge>}
      />
    </div>
  );
}

function PlotToolWorkspacePanel({
  vm,
  studies,
  selectedStudyId,
  selectedStudyDetail,
  studyDetailPanelId,
  onStudySelect
}: {
  vm: ResearchPlotWorkspaceState;
  studies: ResearchPlotStudyItem[];
  selectedStudyId: string | null;
  selectedStudyDetail: ResearchPlotStudyDetailState | null;
  studyDetailPanelId: string;
  onStudySelect: (id: string) => void;
}) {
  const notebookVm = useQuantNotebookViewModel();
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
            <div className="grid gap-3 lg:grid-cols-[minmax(0,1fr)_minmax(220px,0.85fr)]">
              <DenseDataTable
                columns={studyColumns}
                rows={studies}
                getRowId={(study) => study.id}
                getRowAriaLabel={(study) => study.ariaLabel}
                getRowSelectAriaLabel={(study) => study.rowSelectAriaLabel}
                getRowAriaControls={() => studyDetailPanelId}
                getRowAriaExpanded={(study) => study.detailExpanded}
                onRowSelect={(study) => onStudySelect(study.id)}
                selectedRowId={selectedStudyId}
                emptyText="No retained PlotTool studies are available."
                ariaLabel="Strategy notebooks"
                caption="Retained strategy notebooks aligned to the active PlotTool workspace. Select a row to inspect the notebook detail."
              />
              <SelectedPlotStudyDetail
                id={studyDetailPanelId}
                detail={selectedStudyDetail}
                emptyText="No PlotTool study is selected."
              />
            </div>
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

      <div className="space-y-4">
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
                chart={vm.scatterChart}
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

        <QuantNotebook vm={notebookVm} studyChips={vm.toolbarPills} />
      </div>

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

function SelectedPlotStudyDetail({
  id,
  detail,
  emptyText
}: {
  id: string;
  detail: ResearchPlotStudyDetailState | null;
  emptyText: string;
}) {
  if (!detail) {
    return (
      <aside
        id={id}
        role="status"
        aria-live="polite"
        className="row-detail-panel h-fit min-w-0 border-dashed text-sm text-muted-foreground"
      >
        {emptyText}
      </aside>
    );
  }

  return (
    <aside
      id={id}
      role="region"
      aria-label={detail.ariaLabel}
      aria-live="polite"
      className="row-detail-panel h-fit min-w-0"
    >
      <div className="head flex items-center justify-between gap-3">
        <span>{detail.eyebrow}</span>
        <Badge variant={detail.statusVariant}>{detail.statusLabel}</Badge>
      </div>
      <div className="body">
        <h3 className="text-sm font-semibold text-foreground">{detail.title}</h3>
        <p className="mt-1 break-words font-mono text-[11px] text-muted-foreground">{detail.subtitle}</p>
        <p className="mt-2 text-xs leading-5 text-foreground/80">{detail.description}</p>
        <dl className="mt-3 grid gap-2">
          {detail.fields.map((field) => (
            <div
              key={field.label}
              className="grid grid-cols-[minmax(0,0.4fr)_minmax(0,0.6fr)] gap-3 rounded-sm border border-border/60 bg-background/25 px-2.5 py-2"
            >
              <dt className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">{field.label}</dt>
              <dd className="break-words text-right font-mono text-xs text-foreground">{field.value}</dd>
            </div>
          ))}
        </dl>
      </div>
    </aside>
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
            <div className="plottool-distribution-chart" aria-label={vm.distributionChart.ariaLabel}>
              {vm.distributionChart.bars.map((bar) => (
                <div
                  key={bar.id}
                  className={cn("flex-1 rounded-t-sm", distributionBarToneClass[bar.tone])}
                  style={{ height: `${bar.heightPercent}%` }}
                  aria-label={bar.ariaLabel}
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
            <DenseDataTable
              columns={plotToolMomentColumns}
              rows={vm.momentsTable.rows}
              getRowId={(moment) => moment.id}
              getRowAriaLabel={(moment) => `${moment.label}: ${moment.value}. Benchmark ${moment.benchmark}.`}
              emptyText={vm.momentsTable.emptyText}
              ariaLabel="PlotTool moments table"
              caption={vm.momentsTable.caption}
            />
          </CardContent>
        </Card>

        <Card className="border-border/70 bg-background/35">
          <CardHeader className="pb-3">
            <CardTitle className="text-base">Observation sheet</CardTitle>
            <CardDescription>Recent records packaged for analyst review without leaving the Strategy lane.</CardDescription>
          </CardHeader>
          <CardContent>
            <DenseDataTable
              columns={plotToolObservationColumns}
              rows={vm.sampleTable.rows}
              getRowId={(row) => row.id}
              getRowAriaLabel={(row) => `${row.timestamp}: spread ${row.spreadText}, implied volatility ${row.impliedVolText}, z-score ${row.zScoreText}, signal ${row.signalText}.`}
              emptyText={vm.sampleTable.emptyText}
              ariaLabel="PlotTool observation sheet"
              caption={vm.sampleTable.caption}
            />
          </CardContent>
        </Card>
      </div>
    </section>
  );
}

function PlotToolScatterChart({
  chart
}: {
  chart: ResearchPlotScatterChartState;
}) {
  return (
    <svg
      viewBox={chart.viewBox}
      className="h-[320px] w-full"
      role="img"
      aria-labelledby={chart.titleId}
      aria-describedby={chart.descriptionId}
    >
      <title id={chart.titleId}>{chart.title}</title>
      <desc id={chart.descriptionId}>{chart.description}</desc>
      <g>
        {chart.gridLines.map((line) => (
          <line
            key={line.id}
            x1={line.x1}
            y1={line.y1}
            x2={line.x2}
            y2={line.y2}
            stroke={line.stroke}
            strokeWidth={line.strokeWidth}
            strokeDasharray={line.strokeDasharray}
            opacity={line.opacity}
          />
        ))}
      </g>
      <g fill="var(--fg-muted)" fontFamily="IBM Plex Mono" fontSize="10">
        {chart.yTicks.map((tick) => (
          <text key={`y-${tick.value}`} x="44" y={tick.value} textAnchor="end">
            {tick.label}
          </text>
        ))}
        {chart.xTicks.map((tick) => (
          <text key={`x-${tick.value}`} x={tick.value + 20} y="304" textAnchor="middle">
            {tick.label}
          </text>
        ))}
        <text x="330" y="318" textAnchor="middle">{chart.xAxisLabel}</text>
        <text x="16" y="170" textAnchor="middle" transform="rotate(-90 16 170)">{chart.yAxisLabel}</text>
      </g>
      <polyline
        fill="none"
        stroke={chart.trendLine.stroke}
        strokeWidth={chart.trendLine.strokeWidth}
        strokeDasharray={chart.trendLine.strokeDasharray}
        points={chart.trendLine.points}
      />
      <line
        x1={chart.marker.verticalGuide.x1}
        y1={chart.marker.verticalGuide.y1}
        x2={chart.marker.verticalGuide.x2}
        y2={chart.marker.verticalGuide.y2}
        stroke={chart.marker.verticalGuide.stroke}
        strokeWidth={chart.marker.verticalGuide.strokeWidth}
        strokeDasharray={chart.marker.verticalGuide.strokeDasharray}
        opacity={chart.marker.verticalGuide.opacity}
      />
      <line
        x1={chart.marker.horizontalGuide.x1}
        y1={chart.marker.horizontalGuide.y1}
        x2={chart.marker.horizontalGuide.x2}
        y2={chart.marker.horizontalGuide.y2}
        stroke={chart.marker.horizontalGuide.stroke}
        strokeWidth={chart.marker.horizontalGuide.strokeWidth}
        strokeDasharray={chart.marker.horizontalGuide.strokeDasharray}
        opacity={chart.marker.horizontalGuide.opacity}
      />
      {chart.points.map((point) => (
        <circle
          key={point.id}
          cx={point.x}
          cy={point.y}
          r={point.radius}
          fill={point.fill}
          fillOpacity={point.fillOpacity}
        />
      ))}
      <circle
        cx={chart.marker.x}
        cy={chart.marker.y}
        r={chart.marker.radius}
        fill={chart.marker.fill}
        stroke={chart.marker.stroke}
        strokeWidth={chart.marker.strokeWidth}
      />
      <rect
        x={chart.marker.labelX}
        y={chart.marker.labelY}
        width={chart.marker.labelWidth}
        height={chart.marker.labelHeight}
        rx={chart.marker.labelRadius}
        fill={chart.marker.labelFill}
        stroke={chart.marker.labelStroke}
      />
      <text x={chart.marker.labelTextX} y={chart.marker.labelTextY} fill={chart.marker.labelStroke} fontFamily="IBM Plex Mono" fontSize="10">
        {chart.marker.labelText}
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
