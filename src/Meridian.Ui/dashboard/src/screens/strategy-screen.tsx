import { useEffect, useMemo, useRef } from "react";
import { BarChart3, BookOpenText, ChartScatter, Network, Sigma, Sparkles } from "lucide-react";
import { Link, useLocation, useNavigate } from "react-router-dom";
import { BiasDisclosurePanel } from "@/components/meridian/bias-disclosure-panel";
import { StatStrip } from "@/components/meridian/stat-strip";
import { WorkspaceTabStrip } from "@/components/meridian/workspace-primitives";
import { QuantNotebook } from "@/components/meridian/quant-notebook";
import { useQuantNotebookViewModel } from "@/components/meridian/quant-notebook.view-model";
import { DenseDataTable, EntitySummary, ToolbarStrip, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { Histogram } from "@/components/charts";
import { SeverityBadge } from "@/components/operations";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { TechnicalDetails } from "@/components/ui/technical-details";
import { cn } from "@/lib/utils";
import { categoricalVariantToSeverityStatus } from "@/lib/shared-tone-mappings";
import { useStrategyRunLibraryViewModel } from "@/screens/strategy-screen.view-model";
import type {
  StrategyComparisonTableRow,
  StrategyDiffChangeRow,
  StrategyDiffDetailState,
  StrategyParameterChangeRow,
  StrategyPlotLegendItem,
  StrategyPlotMomentRow,
  StrategyPlotSampleRow,
  StrategyPlotScatterChartState,
  StrategyPlotStatisticsState,
  StrategyPlotStudyDetailState,
  StrategyPlotStudyItem,
  StrategyPlotWorkspaceState,
  StrategyPromotionHistoryRow,
  StrategyRunLibraryState,
  StrategyRunTableRow
} from "@/screens/strategy-screen.view-model";
import type { StrategyWorkspaceResponse } from "@/types";

interface StrategyScreenProps {
  data: StrategyWorkspaceResponse | null;
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

/** Map a raw run status string onto a Concrete operator severity. Covers the full
 * `StrategyRunRecord.status` union (`Running` · `Queued` · `Needs Review` · `Completed`) plus
 * common backend variants so no valid status silently falls through to the neutral `info` gray:
 * Completed→ready, Needs Review→review, Running→action, failure states→blocked, Queued/unknown→info. */
function strategyRunSeverityStatus(status: string): string {
  const key = status.trim().toLowerCase();
  if (key === "completed" || key === "complete" || key === "done" || key === "passed") return "ready";
  if (key === "needs review" || key === "needsreview" || key === "review" || key === "review required" || key === "reviewrequired") return "review";
  if (key === "failed" || key === "cancelled" || key === "canceled" || key === "error" || key === "blocked") return "blocked";
  if (key === "running" || key === "inprogress" || key === "in progress") return "action";
  return "info";
}

const plotToolMomentColumns: DenseDataTableColumn<StrategyPlotMomentRow>[] = [
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

const diffPositionColumns: DenseDataTableColumn<StrategyDiffChangeRow>[] = [
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

const diffParameterColumns: DenseDataTableColumn<StrategyParameterChangeRow>[] = [
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

const plotToolObservationColumns: DenseDataTableColumn<StrategyPlotSampleRow>[] = [
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

type StrategyRouteViewId = "overview" | "promotions" | "lab";

/**
 * Route-scoped tabs: the catchall Strategy sub-routes share the sidebar
 * taxonomy. Designer, Formula Workbench, Covered call, and Quant Lab are
 * separate screens and stay sidebar-only.
 */
const strategyRouteTabs: { id: StrategyRouteViewId; label: string; route: string }[] = [
  { id: "overview", label: "Overview", route: "/strategy" },
  { id: "promotions", label: "Promotions", route: "/strategy/promotions" },
  { id: "lab", label: "Strategy Lab", route: "/strategy/lab" }
];

export function resolveStrategyRouteView(pathname: string): StrategyRouteViewId {
  // Match the segment right after /strategy so a dynamic parameter deeper in
  // the path can never collide with a view keyword.
  const segments = pathname.split("/").filter(Boolean);
  if (segments[1] === "promotions") {
    return "promotions";
  }

  if (segments[1] === "lab") {
    return "lab";
  }

  return "overview";
}

const strategyRouteViewCopy: Record<StrategyRouteViewId, { title: string; description: string }> = {
  overview: {
    title: "Strategy overview",
    description: "Review strategy posture, then open the focused builder, lab, run, and promotion workspaces."
  },
  promotions: {
    title: "Promotions",
    description: "Retained runs, comparison evidence, and paper-promotion review."
  },
  lab: {
    title: "Strategy Lab",
    description: "PlotTool workstation: scatter analysis, notebooks, and statistics."
  }
};

export function StrategyScreen({ data }: StrategyScreenProps) {
  const vm = useStrategyRunLibraryViewModel(data);
  const navigate = useNavigate();
  const { pathname, search } = useLocation();
  const routeView = resolveStrategyRouteView(pathname);
  const routeCopy = strategyRouteViewCopy[routeView];
  const showLab = routeView === "lab";
  const showRuns = routeView === "promotions";
  const requestedRunId = new URLSearchParams(search).get("runId")?.trim() ?? "";
  const routeTabs = strategyRouteTabs.map((tab) => ({
    id: tab.id,
    label: tab.label,
    selected: tab.id === routeView
  }));
  const plotToolTabRefs = useRef<Record<string, HTMLButtonElement | null>>({});
  const openedDeepLinkRunId = useRef<string | null>(null);

  useEffect(() => {
    if (!showRuns || !requestedRunId) {
      openedDeepLinkRunId.current = null;
      return;
    }

    if (openedDeepLinkRunId.current === requestedRunId ||
        !vm.runs.some((run) => run.id === requestedRunId)) {
      return;
    }

    openedDeepLinkRunId.current = requestedRunId;
    vm.openRunDetailById(requestedRunId);
  }, [requestedRunId, showRuns, vm.openRunDetailById, vm.runs]);

  const runColumns = useMemo<DenseDataTableColumn<StrategyRunTableRow>[]>(() => [
    {
      id: "compare",
      label: "",
      srLabel: "Select for comparison",
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
      render: (run) => <SeverityBadge status={strategyRunSeverityStatus(run.statusText)} label={run.statusText} />
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

  const promotionHistoryColumns = useMemo<DenseDataTableColumn<StrategyPromotionHistoryRow>[]>(() => [
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

  const comparisonColumns = useMemo<DenseDataTableColumn<StrategyComparisonTableRow>[]>(() => [
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
      render: (row) => <SeverityBadge status={categoricalVariantToSeverityStatus(row.statusBadgeVariant)} label={row.statusText} />
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

  // Keep this after every hook call: an early return before the useMemo columns would
  // change the hook order when the strategy slice arrives and crash the mounted screen.
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

  return (
    <div className="space-y-5">
      <StatStrip metrics={data.metrics} label="Strategy headline metrics" />

      <section
        role="region"
        aria-label="Strategy workspace context"
        className="flex flex-wrap items-end justify-between gap-3"
      >
        <div className="min-w-0">
          <h2 className="font-display text-lg font-semibold leading-tight text-foreground">
            {routeCopy.title}
          </h2>
          <p className="mt-0.5 max-w-3xl text-xs leading-5 text-muted-foreground">
            {routeCopy.description}
          </p>
        </div>
        <WorkspaceTabStrip
          label="Strategy routes"
          tabs={routeTabs}
          onSelect={(id) => {
            const tab = strategyRouteTabs.find((candidate) => candidate.id === id);
            if (tab) {
              // Preserve the querystring: the operating scope is threaded
              // through search params across the shell.
              navigate({ pathname: tab.route, search });
            }
          }}
        />
      </section>

      {routeView === "overview" ? <StrategyOverviewHub vm={vm} /> : null}

      {showLab || showRuns ? (
      <Card>
        {showLab ? (
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
        ) : (
          <CardHeader>
            <div className="eyebrow-label">Run decisions</div>
            <CardTitle>Comparison and promotion controls</CardTitle>
            <CardDescription>Select retained runs below, then compare, diff, or evaluate a paper promotion.</CardDescription>
          </CardHeader>
        )}
        <CardContent className="space-y-4">
          {showRuns ? (
          <>
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
              {vm.promotionPanel.approval && (
                <div className="space-y-2">
                  <div className="text-sm font-semibold text-success">{vm.promotionPanel.approval.title}</div>
                  <p className="font-mono text-xs text-muted-foreground">{vm.promotionPanel.approval.detail}</p>
                  <Button
                    size="sm"
                    aria-label={vm.promotionPanel.approval.actionAriaLabel}
                    onClick={() => {
                      if (vm.promotionPanel.approval) {
                        navigate(vm.promotionPanel.approval.actionHref);
                      }
                    }}
                  >
                    {vm.promotionPanel.approval.actionLabel}
                  </Button>
                </div>
              )}
              {vm.promotionPanel.showApprovalForm && (
                <form
                  className="flex flex-wrap items-center gap-3"
                  onSubmit={(e) => { e.preventDefault(); void vm.confirmPromotion(); }}
                  aria-label="Governed Paper promotion approval"
                  noValidate
                >
                  <label
                    htmlFor={vm.promotionApprovalForm.acknowledgementId}
                    className="flex max-w-sm items-start gap-2 rounded-md border border-border/70 bg-background/45 px-3 py-2 text-xs leading-5 text-muted-foreground"
                  >
                    <input
                      id={vm.promotionApprovalForm.acknowledgementId}
                      type="checkbox"
                      checked={vm.promotionApprovalForm.acknowledgementChecked}
                      onChange={(e) => vm.setPromotionAcknowledgement(e.target.checked)}
                      disabled={vm.promotionApprovalForm.acknowledgementDisabled}
                      title={vm.promotionApprovalForm.acknowledgementDisabledReason ?? undefined}
                      aria-describedby={vm.promotionApprovalForm.acknowledgementDescribedBy}
                      className="mt-0.5 h-4 w-4 rounded border-border bg-background text-primary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
                    />
                    <span className="grid gap-1">
                      <span>{vm.promotionApprovalForm.acknowledgementLabel}</span>
                      {vm.promotionApprovalForm.acknowledgementDisabledReason ? (
                        <span
                          id={vm.promotionApprovalForm.acknowledgementDisabledReasonId ?? undefined}
                          className="rounded-sm border border-warning/30 bg-warning/10 px-2 py-1 text-[11px] leading-4 text-warning"
                        >
                          {vm.promotionApprovalForm.acknowledgementDisabledReason}
                        </span>
                      ) : null}
                    </span>
                  </label>
                  <Button
                    type="submit"
                    size="sm"
                    disabled={!vm.promotionApprovalForm.canSubmit}
                    disabledReason={vm.promotionApprovalForm.disabledReason}
                    aria-label={vm.promotionApprovalForm.submitAriaLabel}
                  >
                    {vm.promotionApprovalForm.submitLabel}
                  </Button>
                  <Button
                    type="button"
                    size="sm"
                    variant="ghost"
                    onClick={vm.cancelPromotion}
                    disabled={vm.promotionApprovalForm.cancelDisabled}
                    disabledReason={vm.promotionApprovalForm.cancelDisabledReason}
                    aria-label={vm.promotionApprovalForm.cancelAriaLabel}
                  >
                    {vm.promotionApprovalForm.cancelLabel}
                  </Button>
                </form>
              )}
              {vm.promotionPanel.showIneligibleDismiss && (
                <Button size="sm" variant="ghost" onClick={vm.cancelPromotion}>Dismiss</Button>
              )}
            </div>
          )}
          </>
          ) : null}

          {showLab ? (vm.activePlotToolView === "workspace" ? (
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
          )) : null}
        </CardContent>
      </Card>
      ) : null}

      {showRuns ? (
      <>
      <Card>
        <CardHeader>
          <CardTitle>Strategy run library</CardTitle>
          <CardDescription>Review retained runs, compare candidates, and open promotion history from the web workstation.</CardDescription>
        </CardHeader>
        <CardContent className="space-y-4">
          <section aria-label={vm.runHistorySummary.ariaLabel} className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
            {vm.runHistorySummary.cards.map((card) => (
              <div key={card.id} className="rounded-md border border-border/70 bg-secondary/20 px-3 py-3">
                <div className="flex items-start justify-between gap-3">
                  <div className="min-w-0">
                    <div className="eyebrow-label">{card.label}</div>
                    <div className="mt-2 font-mono text-lg font-semibold text-foreground">{card.value}</div>
                  </div>
                  <Badge variant={card.badgeVariant}>{card.badgeLabel}</Badge>
                </div>
                <p className="mt-2 text-xs leading-5 text-muted-foreground">{card.detail}</p>
              </div>
            ))}
            <p className="sr-only">{vm.runHistorySummary.normalizedResultText}</p>
            <p className="sr-only">{vm.runHistorySummary.modeCoverageText}</p>
            <p className="sr-only">{vm.runHistorySummary.engineCoverageText}</p>
            <p className="sr-only">{vm.runHistorySummary.liveAdjacentText}</p>
          </section>
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
              <div id={vm.inspectedRunDetail.panelId} className="min-w-0 space-y-3">
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
                <TechnicalDetails
                  label="Run references"
                  description="Stable identifiers for support, audit, and API tracing."
                >
                  <dl className="grid gap-2">
                    {vm.inspectedRunDetail.technicalFields.map((field) => (
                      <div key={field.id} className="grid gap-1 sm:grid-cols-[7rem_minmax(0,1fr)] sm:items-baseline">
                        <dt className="text-xs font-medium text-muted-foreground">{field.label}</dt>
                        <dd className="break-all font-mono text-xs text-foreground">{field.value}</dd>
                      </div>
                    ))}
                  </dl>
                </TechnicalDetails>
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
                    status={<SeverityBadge status={categoricalVariantToSeverityStatus(vm.selectedComparisonDetail.statusVariant)} label={vm.selectedComparisonDetail.statusLabel} />}
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
              <section aria-label={vm.diffPanel.summaryLabel} className="grid gap-3 sm:grid-cols-3 xl:grid-cols-6">
                {vm.diffPanel.metrics.map((metric) => (
                  <div key={metric.id} role="group" aria-label={metric.ariaLabel} className="rounded-md border border-border/70 bg-secondary/25 px-3 py-3">
                    <div className="eyebrow-label">{metric.label}</div>
                    <div className={cn("mt-2 font-mono text-sm font-semibold", diffMetricToneClass[metric.tone])}>{metric.value}</div>
                  </div>
                ))}
              </section>
            )}
            <section aria-label="Run diff artifact completeness" className="rounded-lg border border-border/70 bg-secondary/20 px-3 py-3">
              <div className="eyebrow-label">Version, engine, and artifact context</div>
              <p className="mt-2 text-sm leading-6 text-muted-foreground">{vm.diffPanel.metadataSummary}</p>
              <p className="mt-2 text-sm leading-6 text-muted-foreground">{vm.diffPanel.artifactCompletenessSummary}</p>
              {vm.diffPanel.compatibilityWarnings.length > 0 ? (
                <ul className="mt-2 list-disc space-y-1 pl-5 text-xs leading-5 text-warning" aria-label="Run diff compatibility warnings">
                  {vm.diffPanel.compatibilityWarnings.map((warning) => (
                    <li key={warning}>{warning}</li>
                  ))}
                </ul>
              ) : (
                <p className="mt-2 text-xs text-muted-foreground">No compatibility warnings.</p>
              )}
            </section>
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
                  <StrategyDiffDetailPanel
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
                  <StrategyDiffDetailPanel
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
      </>
      ) : null}

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

            <section className="mt-4 rounded-md border border-border/70 bg-secondary/20 px-4 py-3">
              <div className="eyebrow-label">{vm.selectedRunDetail.acceptanceCriteriaLabel}</div>
              {vm.selectedRunDetail.acceptanceCriteriaStatus === "ready" ? (
                <>
                  <p className="mt-2 text-xs text-muted-foreground">
                    {vm.selectedRunDetail.acceptanceCriteriaMessage}
                  </p>
                  <ul
                    aria-label={vm.selectedRunDetail.acceptanceCriteriaLabel}
                    className="mt-3 space-y-2"
                  >
                    {vm.selectedRunDetail.acceptanceCriteria.map((criterion, index) => (
                      <li key={`${criterion}-${index}`} className="flex items-start gap-2 text-sm leading-5 text-foreground">
                        <span aria-hidden="true" className="mt-0.5 shrink-0 text-muted-foreground">&bull;</span>
                        <span>{criterion}</span>
                      </li>
                    ))}
                  </ul>
                </>
              ) : (
                <p role="status" aria-live="polite" className="mt-2 text-sm text-muted-foreground">
                  {vm.selectedRunDetail.acceptanceCriteriaMessage}
                </p>
              )}
            </section>

            <section className="mt-4 rounded-md border border-border/70 bg-secondary/20 px-4 py-3">
              <div className="flex flex-wrap items-start justify-between gap-3">
                <div>
                  <div className="eyebrow-label">{vm.selectedRunDetail.acceptanceChecklistLabel}</div>
                  <p id={`${vm.selectedRunDetail.dialogDescriptionId}-checklist`} className="mt-2 text-xs text-muted-foreground">
                    {vm.selectedRunDetail.acceptanceChecklistMessage}
                  </p>
                </div>
                <Button asChild variant="outline" size="sm">
                  <Link to={vm.selectedRunDetail.evidenceAction.href} aria-label={vm.selectedRunDetail.evidenceAction.ariaLabel}>
                    {vm.selectedRunDetail.evidenceAction.label}
                  </Link>
                </Button>
              </div>
              {vm.selectedRunDetail.acceptanceChecklistStatus === "ready" ? (
                <fieldset
                  disabled
                  aria-label={vm.selectedRunDetail.acceptanceChecklistLabel}
                  aria-describedby={`${vm.selectedRunDetail.dialogDescriptionId}-checklist`}
                  className="mt-3 space-y-2"
                >
                  {vm.selectedRunDetail.acceptanceChecklist.map((item) => (
                    <label key={item.checklistId} className="block rounded-md border border-border/60 bg-background/45 px-3 py-2">
                      <span className="flex items-start gap-2">
                        <input
                          type="checkbox"
                          checked={item.status === "Ready"}
                          readOnly
                          className="mt-1 h-4 w-4 shrink-0"
                        />
                        <span className="min-w-0">
                          <span className="block text-sm font-medium text-foreground">{item.label}</span>
                          <span className="block font-mono text-[11px] text-muted-foreground">{item.checklistId}</span>
                          <span className="mt-1 block text-xs text-muted-foreground">
                            {item.status === "Ready"
                              ? `Ready - decided by ${item.decidedBy} at ${item.decidedAt}.`
                              : item.status === "Rejected"
                                ? `Rejected - ${item.blocker ?? "the durable promotion decision blocked this requirement."}`
                                : item.blocker ?? "Review is required before this item can be ready."}
                          </span>
                          {item.evidenceReference ? (
                            <span className="mt-1 block break-all font-mono text-[11px] text-muted-foreground">
                              Evidence: {item.evidenceReference}
                            </span>
                          ) : null}
                          {item.auditReference ? (
                            <span className="block break-all font-mono text-[11px] text-muted-foreground">
                              Audit: {item.auditReference}
                            </span>
                          ) : null}
                        </span>
                      </span>
                    </label>
                  ))}
                </fieldset>
              ) : (
                <p role="status" aria-live="polite" className="mt-3 text-sm text-muted-foreground">
                  {vm.selectedRunDetail.acceptanceChecklistMessage}
                </p>
              )}
            </section>

            <TechnicalDetails
              label="Run references"
              description="Stable identifiers for support, audit, and API tracing."
              className="mt-4"
            >
              <dl className="grid gap-2">
                {vm.selectedRunDetail.technicalRows.map((row) => (
                  <div key={row.id} className="grid gap-1 sm:grid-cols-[7rem_minmax(0,1fr)] sm:items-baseline">
                    <dt className="text-xs font-medium text-muted-foreground">{row.label}</dt>
                    <dd className="break-all font-mono text-xs text-foreground">{row.value}</dd>
                  </div>
                ))}
              </dl>
            </TechnicalDetails>

            <section className="mt-4 rounded-md border border-border/70 bg-background/45 px-4 py-3">
              <div className="eyebrow-label">{vm.selectedRunDetail.notesLabel}</div>
              <p className="mt-2 text-sm leading-6 text-muted-foreground">{vm.selectedRunDetail.notesText}</p>
            </section>

            <BiasDisclosurePanel disclosure={vm.selectedRunDetail.biasDisclosure} className="mt-4" />
          </DialogContent>
        )}
      </Dialog>
    </div>
  );
}

function StrategyDiffDetailPanel({
  id,
  detail,
  emptyText
}: {
  id: string;
  detail: StrategyDiffDetailState | null;
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
  vm: StrategyPlotWorkspaceState;
  studies: StrategyPlotStudyItem[];
  selectedStudyId: string | null;
  selectedStudyDetail: StrategyPlotStudyDetailState | null;
  studyDetailPanelId: string;
  onStudySelect: (id: string) => void;
}) {
  const notebookVm = useQuantNotebookViewModel();
  const hasPlotToolPayload = studies.length > 0 || vm.scatterChart.points.length > 0;

  if (!hasPlotToolPayload) {
    return (
      <section
        id="plottool-workspace-panel"
        role="tabpanel"
        aria-labelledby="plottool-workspace-tab"
        className="space-y-4"
      >
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
          <CardContent className="py-6">
            <div
              className="flex flex-col items-center justify-center rounded-md border border-dashed border-border/70 bg-secondary/15 px-6 py-10 text-center"
              role="status"
              aria-label="PlotTool scatter observations unavailable"
            >
              <ChartScatter className="h-8 w-8 text-muted-foreground" aria-hidden="true" />
              <div className="mt-3 font-semibold text-foreground">No PlotTool observations yet</div>
              <p className="mt-2 max-w-2xl text-sm leading-6 text-muted-foreground">{vm.studyTableEmptyText}</p>
              <Button asChild variant="outline" size="sm" className="mt-4">
                <Link to="/settings/providers" aria-label="Review provider connections for PlotTool">
                  Review provider connections
                </Link>
              </Button>
            </div>
          </CardContent>
        </Card>
      </section>
    );
  }

  const studyColumns: DenseDataTableColumn<StrategyPlotStudyItem>[] = [
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
      // Mode-derived chip (LIVE/PAPER/BACKTEST) — a categorical environment badge, not an
      // operator severity, so it stays a plain Badge.
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
              ariaLabel={vm.notebookToolbarAriaLabel}
              items={vm.notebookToolbarItems}
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
                emptyText={vm.studyTableEmptyText}
                ariaLabel="Strategy notebooks"
                caption={vm.studyTableCaption}
              />
              <SelectedPlotStudyDetail
                id={studyDetailPanelId}
                detail={selectedStudyDetail}
                emptyText={vm.selectedStudyEmptyText}
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
            {vm.scatterChart.points.length > 0 ? (
              <details className="rounded-md border border-border/70 bg-secondary/15 px-3 py-2">
                <summary className="cursor-pointer text-sm font-semibold text-foreground">
                  View observation table
                </summary>
                <div className="mt-3 overflow-x-auto">
                  <table className="w-full text-left text-xs" aria-label="PlotTool scatter observations">
                    <thead>
                      <tr className="border-b border-border/60 text-muted-foreground">
                        <th scope="col" className="px-2 py-2">Observation</th>
                        <th scope="col" className="px-2 py-2">X position</th>
                        <th scope="col" className="px-2 py-2">Y position</th>
                      </tr>
                    </thead>
                    <tbody>
                      {vm.scatterChart.points.map((point, index) => (
                        <tr key={point.id} className="border-b border-border/40 last:border-0" aria-label={point.ariaLabel}>
                          <th scope="row" className="px-2 py-2 font-medium">{index + 1}</th>
                          <td className="px-2 py-2 font-mono">{point.x}</td>
                          <td className="px-2 py-2 font-mono">{point.y}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </details>
            ) : null}
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
  detail: StrategyPlotStudyDetailState | null;
  emptyText: string;
}) {
  if (!detail) {
    return (
      <div
        id={id}
        role="status"
        aria-live="polite"
        className="row-detail-panel h-fit min-w-0 border-dashed text-sm text-muted-foreground"
      >
        {emptyText}
      </div>
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
        {/* Mode-derived chip (LIVE/PAPER/BACKTEST), not a severity — stays a plain Badge. */}
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

function PlotToolStatisticsPanel({ vm }: { vm: StrategyPlotStatisticsState }) {
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
            <div className="h-[180px] w-full" role="img" aria-label={vm.distributionChart.ariaLabel}>
              <Histogram
                bins={vm.distributionChart.bars.map((bar, index) => ({
                  x0: index,
                  x1: index + 1,
                  count: bar.heightPercent
                }))}
                signed={false}
                showMean={false}
                valueFmt={(value) => String(Math.round(value))}
                countFmt={(count) => `${Math.round(count)}%`}
              />
            </div>
            <ul className="sr-only">
              {vm.distributionChart.bars.map((bar) => (
                <li key={bar.id}>{bar.ariaLabel}</li>
              ))}
            </ul>
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
  chart: StrategyPlotScatterChartState;
}) {
  const hasObservations = chart.points.length > 0;

  if (!hasObservations) {
    return (
      <div
        className="flex min-h-[320px] flex-col items-center justify-center rounded-md border border-dashed border-border/70 bg-secondary/15 px-6 text-center"
        role="status"
        aria-label="PlotTool scatter observations unavailable"
      >
        <ChartScatter className="h-8 w-8 text-muted-foreground" aria-hidden="true" />
        <div className="mt-3 font-semibold text-foreground">No PlotTool observations yet</div>
        <p className="mt-2 max-w-lg text-sm leading-6 text-muted-foreground">
          Connect a governed Strategy analytics source to load retained notebooks, scatter observations, and statistical evidence.
        </p>
        <Button asChild variant="outline" size="sm" className="mt-4">
          <Link to="/settings/providers" aria-label="Review provider connections for PlotTool">
            Review provider connections
          </Link>
        </Button>
      </div>
    );
  }

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
      <g fill="var(--fg-muted)" fontFamily="Cascadia Mono" fontSize="10">
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
      {hasObservations ? (
        <>
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
        </>
      ) : null}
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
      {hasObservations ? (
        <>
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
          <text x={chart.marker.labelTextX} y={chart.marker.labelTextY} fill={chart.marker.labelStroke} fontFamily="Cascadia Mono" fontSize="10">
            {chart.marker.labelText}
          </text>
        </>
      ) : null}
    </svg>
  );
}

const strategyHubRoutes = [
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

function StrategyOverviewHub({ vm }: { vm: StrategyRunLibraryState }) {
  const latestRun = vm.runs[0] ?? null;
  const plotToolConnected = vm.plotTool.workspace.statusBadgeLabel !== "NOT CONNECTED";

  return (
    <section aria-labelledby="strategy-overview-hub-title" className="space-y-4">
      <Card className="border-border/80 bg-secondary/15">
        <CardHeader>
          <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
            <div>
              <div className="eyebrow-label">Strategy command center</div>
              <CardTitle id="strategy-overview-hub-title" className="mt-2">Choose the next strategy task</CardTitle>
              <CardDescription className="mt-2 max-w-3xl">
                The overview summarizes run and analytics posture. Detailed authoring, analysis, comparison, and promotion evidence stay in focused workspaces.
              </CardDescription>
            </div>
            <div className="rounded-md border border-border/70 bg-background/45 px-3 py-3 text-sm lg:max-w-sm">
              <div className="flex items-center justify-between gap-3">
                <span className="font-semibold text-foreground">PlotTool analytics</span>
                <Badge variant={vm.plotTool.workspace.statusBadgeVariant}>{vm.plotTool.workspace.statusBadgeLabel}</Badge>
              </div>
              {plotToolConnected ? (
                <p className="mt-2 text-xs leading-5 text-muted-foreground">Retained PlotTool studies are available in Strategy Lab.</p>
              ) : (
                <div className="mt-2 space-y-2">
                  <p className="text-xs leading-5 text-muted-foreground">
                    Run history remains available, but PlotTool analytics needs a governed provider connection.
                  </p>
                  <Link
                    to="/settings/providers"
                    className="inline-flex text-xs font-semibold text-primary underline-offset-4 hover:underline"
                    aria-label="Review provider connections for PlotTool analytics"
                  >
                    Review provider connections
                  </Link>
                </div>
              )}
            </div>
          </div>
        </CardHeader>
        <CardContent className="space-y-4">
          <section aria-label={vm.runHistorySummary.ariaLabel} className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
            {vm.runHistorySummary.cards.map((card) => (
              <div key={card.id} className="rounded-md border border-border/70 bg-background/45 px-3 py-3">
                <div className="flex items-start justify-between gap-3">
                  <div>
                    <div className="eyebrow-label">{card.label}</div>
                    <div className="mt-2 font-mono text-lg font-semibold text-foreground">{card.value}</div>
                  </div>
                  <Badge variant={card.badgeVariant}>{card.badgeLabel}</Badge>
                </div>
                <p className="mt-2 text-xs leading-5 text-muted-foreground">{card.detail}</p>
              </div>
            ))}
          </section>
          <div className="rounded-md border border-border/70 bg-background/35 px-3 py-3 text-sm">
            <div className="eyebrow-label">Latest retained run</div>
            {latestRun ? (
              <div className="mt-2 flex flex-wrap items-center justify-between gap-3">
                <div>
                  <div className="font-semibold text-foreground">{latestRun.strategyName}</div>
                  <div className="mt-1 text-xs text-muted-foreground">
                    {latestRun.mode} · {latestRun.status} · updated {latestRun.lastUpdated}
                  </div>
                </div>
                <Button asChild variant="outline" size="sm">
                  <Link to="/strategy/promotions">Review run library</Link>
                </Button>
              </div>
            ) : (
              <p className="mt-2 text-muted-foreground">No retained runs are available yet. Open a focused lab to create or inspect strategy evidence.</p>
            )}
          </div>
        </CardContent>
      </Card>

      <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-3" aria-label="Strategy task routes">
        {strategyHubRoutes.map((route) => (
          <Card key={route.id} className="h-full border-border/70">
            <CardHeader>
              <CardTitle className="text-base">{route.title}</CardTitle>
              <CardDescription>{route.description}</CardDescription>
            </CardHeader>
            <CardContent>
              <Button asChild variant="outline" size="sm">
                <Link to={route.href}>{route.action}</Link>
              </Button>
            </CardContent>
          </Card>
        ))}
      </div>
    </section>
  );
}

function PlotToolLegendItem({ item }: { item: StrategyPlotLegendItem }) {
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
