import { BarChart3, BookOpenText, ChartScatter, Sigma, Sparkles } from "lucide-react";
import { Link } from "react-router-dom";
import { DetailFields } from "@/components/meridian/detail-pane";
import { QuantNotebook } from "@/components/meridian/quant-notebook";
import { useQuantNotebookViewModel } from "@/components/meridian/quant-notebook.view-model";
import { EntitySummary, ToolbarStrip, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { TableWorkbench, tableColumnsWithExport } from "@/components/meridian/table-workbench";
import { Histogram } from "@/components/charts";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { cn } from "@/lib/utils";
import type {
  StrategyPlotLegendItem,
  StrategyPlotMomentRow,
  StrategyPlotSampleRow,
  StrategyPlotScatterChartState,
  StrategyPlotStatisticsState,
  StrategyPlotStudyDetailState,
  StrategyPlotStudyItem,
  StrategyPlotWorkspaceState
} from "@/screens/strategy-screen.view-model";
import { eyebrowClassName, usesTrackedCaps } from "@/design-system/label-case";

// The strategy screen's plot-tool panels: the study workspace, the statistics view and the scatter
// chart, with the tables and tone maps only they use. Split from strategy-screen.tsx.

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

const sampleToneBadgeVariant = {
  default: "outline",
  success: "success",
  warning: "warning",
  danger: "danger"
} as const;

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

export function PlotToolWorkspacePanel({
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
              <TableWorkbench workbenchId="strategy-plot-studies"
                columns={tableColumnsWithExport(studyColumns, { study: row => row.title, status: row => row.statusText, metric: row => row.metricText, note: row => row.noteText })}
                sourceDescription="Retained study catalog in the current PlotTool snapshot."
                completePopulation
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
              <div className="eyebrow-label eyebrow-label--sentence">Scatter / residual view</div>
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
                  <TableWorkbench workbenchId="strategy-scatter-mark-coordinates"
                    rows={vm.scatterChart.points.map((point, index) => ({ ...point, ordinal: index + 1 }))}
                    columns={[
                      { id: "observation", label: "Observation", rowHeader: true, exportValue: point => point.ordinal, render: point => point.ordinal },
                      { id: "x", label: "X position", exportValue: point => point.x, render: point => point.x },
                      { id: "y", label: "Y position", exportValue: point => point.y, render: point => point.y }
                    ]} getRowId={point => point.id} getRowAriaLabel={point => point.ariaLabel}
                    getSearchText={point => `${point.ordinal} ${point.x} ${point.y}`} completePopulation
                    sourceDescription="Displayed scatter marks in chart coordinates; positions are not underlying financial observations."
                    emptyText="No scatter marks are available." ariaLabel="PlotTool scatter observations" />
                </div>
              </details>
            ) : null}
            <div className="plottool-chart-legend" aria-label="PlotTool chart legend">
              {vm.legendItems.map((item) => <PlotToolLegendItem key={item.id} item={item} />)}
            </div>
            <div className="flex flex-wrap items-center justify-between gap-3 rounded-lg border border-border/70 bg-secondary/15 px-3 py-3 text-xs text-muted-foreground">
              <div>
                <div className={eyebrowClassName(vm.focusPoint.label)}>{vm.focusPoint.label}</div>
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
              <div className={eyebrowClassName(vm.focusPoint.label)}>{vm.focusPoint.label}</div>
              <div className="mt-2 flex items-baseline gap-2 font-mono text-lg text-foreground">
                <span>{vm.focusPoint.xValueText}</span>
                <span className="text-muted-foreground">/</span>
                <span>{vm.focusPoint.yValueText}</span>
              </div>
              <p className="mt-2 text-xs leading-5 text-muted-foreground">{vm.focusPoint.detail}</p>
            </div>
            {vm.signalCards.map((card) => (
              <div key={card.id} className="rounded-lg border border-border/70 bg-secondary/20 px-3 py-3">
                <div className={eyebrowClassName(card.label)}>{card.label}</div>
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
        <p className="mt-1 break-words font-mono text-xs text-muted-foreground">{detail.subtitle}</p>
        <p className="mt-2 text-xs leading-5 text-foreground/80">{detail.description}</p>
        <DetailFields fields={detail.fields} className="mt-3" />
      </div>
    </aside>
  );
}

export function PlotToolStatisticsPanel({ vm }: { vm: StrategyPlotStatisticsState }) {
  return (
    <section
      id="plottool-statistics-panel"
      role="tabpanel"
      aria-labelledby="plottool-statistics-tab"
      className="space-y-4"
    >
      <Card className="border-border/70 bg-background/35">
        <CardHeader className="pb-3">
          <div className={eyebrowClassName(vm.eyebrow)}>{vm.eyebrow}</div>
          <CardTitle className="flex items-center gap-2">
            <Sigma className="h-5 w-5 text-primary" />
            {vm.title}
          </CardTitle>
          <CardDescription>{vm.description}</CardDescription>
        </CardHeader>
        <CardContent className="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
          {vm.summaryTiles.map((tile) => (
            <div key={tile.id} className="rounded-lg border border-border/70 bg-secondary/20 px-3 py-3">
              <div className={eyebrowClassName(tile.label)}>{tile.label}</div>
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
            <TableWorkbench workbenchId="strategy-plot-moments"
                columns={tableColumnsWithExport(plotToolMomentColumns, { metric: row => row.label, value: row => row.value, benchmark: row => row.benchmark })}
                sourceDescription="Complete statistical moment set for the selected PlotTool study; source display units retained."
                completePopulation
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
            <CardDescription>Recent records packaged for analyst review without leaving Strategy.</CardDescription>
          </CardHeader>
          <CardContent>
            <TableWorkbench workbenchId="strategy-plot-observations"
                columns={tableColumnsWithExport(plotToolObservationColumns, { date: row => row.timestamp, spread: row => row.spreadText, "implied-vol": row => row.impliedVolText, "z-score": row => row.zScoreText, signal: row => row.signalText })}
                sourceDescription="Current recent-observation sample supplied by PlotTool; not complete market history."
                completePopulation
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
      <g fill="var(--fg-muted)" fontFamily="var(--font-data, IBM Plex Mono, ui-monospace, Consolas, monospace)" fontSize="10">
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
          <text x={chart.marker.labelTextX} y={chart.marker.labelTextY} fill={chart.marker.labelStroke} fontFamily="var(--font-data, IBM Plex Mono, ui-monospace, Consolas, monospace)" fontSize="10">
            {chart.marker.labelText}
          </text>
        </>
      ) : null}
    </svg>
  );
}

function PlotToolLegendItem({ item }: { item: StrategyPlotLegendItem }) {
  return (
    <div className="plottool-legend-card">
      <div className="flex items-center gap-2 text-xs text-foreground">
        <span className={cn("h-2.5 w-2.5 rounded-full", plotLegendToneClass[item.tone])} aria-hidden="true" />
        <span className={cn("", usesTrackedCaps(item.label) && "font-mono uppercase tracking-[0.14em]")}>{item.label}</span>
      </div>
      <div className="mt-1 text-xs text-muted-foreground">{item.detail}</div>
    </div>
  );
}
