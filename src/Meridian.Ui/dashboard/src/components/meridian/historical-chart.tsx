import { AlertCircle, LineChart, Loader2, RefreshCcw } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { cn } from "@/lib/utils";
import {
  HISTORICAL_CHART_TIMEFRAMES,
  computeChartStats,
  formatIntervalLabel,
  useHistoricalChartViewModel,
  type HistoricalChartSparklineViewModel,
  type HistoricalChartStatePanel,
  type HistoricalChartStatTile
} from "@/components/meridian/historical-chart.view-model";

export {
  HISTORICAL_CHART_TIMEFRAMES,
  computeChartStats,
  formatIntervalLabel
} from "@/components/meridian/historical-chart.view-model";

interface HistoricalChartCardProps {
  symbol: string;
  className?: string;
}

const statePanelClass = {
  idle: "border-border/70 bg-secondary/20 text-muted-foreground",
  loading: "border-[var(--state-pending-bd)] bg-[var(--state-pending-bg)] text-[var(--state-pending-fg)]",
  error: "border-danger/40 bg-danger/10 text-danger",
  empty: "border-warning/35 bg-warning/10 text-warning"
} as const;

export function HistoricalChartCard({ symbol, className }: HistoricalChartCardProps) {
  const vm = useHistoricalChartViewModel(symbol);

  return (
    <Card className={className}>
      <CardHeader>
        <div className="flex flex-col gap-3 sm:flex-row sm:items-end sm:justify-between">
          <div>
            <div className="eyebrow-label">{vm.eyebrow}</div>
            <CardTitle className="flex items-center gap-2 text-base">
              <LineChart className="h-4 w-4 text-primary" aria-hidden="true" />
              {vm.title}
            </CardTitle>
            <CardDescription>{vm.description}</CardDescription>
          </div>
          <div className="flex flex-col items-start gap-1 sm:items-end">
            <span className="font-mono text-2xl text-foreground">
              {vm.lastPriceText}
            </span>
            <span className={cn("font-mono text-xs", vm.changeToneClass)}>
              {vm.changeText}
            </span>
          </div>
        </div>
        <div
          className="mt-3 flex flex-wrap gap-1"
          role="group"
          aria-label="Select chart timeframe"
        >
          {vm.timeframeOptions.map((timeframe) => (
            <Button
              key={timeframe.id}
              size="sm"
              variant={timeframe.buttonVariant}
              onClick={timeframe.select}
              aria-pressed={timeframe.ariaPressed}
              aria-label={timeframe.ariaLabel}
              data-testid={timeframe.testId}
            >
              {timeframe.label}
            </Button>
          ))}
        </div>
      </CardHeader>
      <CardContent>
        {vm.statePanel ? (
          <HistoricalChartStatePanelView panel={vm.statePanel} onRetry={vm.retry} />
        ) : vm.chart ? (
          <div className="space-y-3">
            <BarChartSparkline viewModel={vm.chart} />
            <div className="grid grid-cols-2 gap-2 sm:grid-cols-5">
              {vm.statTiles.map((stat) => <ChartStat key={stat.id} stat={stat} />)}
            </div>
          </div>
        ) : null}
      </CardContent>
    </Card>
  );
}

function HistoricalChartStatePanelView({
  panel,
  onRetry
}: {
  panel: HistoricalChartStatePanel;
  onRetry: () => void;
}) {
  const Icon = panel.kind === "loading" ? Loader2 : AlertCircle;

  return (
    <div
      role={panel.role}
      aria-live={panel.ariaLive}
      className={cn(
        "flex flex-col gap-3 rounded-md border px-3 py-3 text-sm sm:flex-row sm:items-start sm:justify-between",
        statePanelClass[panel.kind]
      )}
    >
      <div className="flex min-w-0 items-start gap-2">
        <Icon
          className={cn("mt-0.5 h-4 w-4 shrink-0", panel.kind === "loading" ? "animate-spin" : "")}
          aria-hidden="true"
        />
        <div className="min-w-0">
          <div className="font-semibold text-foreground">{panel.title}</div>
          <div className="mt-1 text-muted-foreground">{panel.detail}</div>
        </div>
      </div>
      {panel.retryLabel ? (
        <Button
          type="button"
          size="sm"
          variant="outline"
          onClick={onRetry}
          disabled={panel.retryDisabled}
          busy={panel.retryBusy}
          busyLabel={panel.retryLabel}
          aria-label={panel.retryAriaLabel ?? panel.retryLabel}
          className="self-start"
        >
          <RefreshCcw className="h-3.5 w-3.5" aria-hidden="true" />
          {panel.retryLabel}
        </Button>
      ) : null}
    </div>
  );
}

function BarChartSparkline({ viewModel }: { viewModel: HistoricalChartSparklineViewModel }) {
  return (
    <svg
      viewBox={viewModel.viewBox}
      preserveAspectRatio="none"
      className="block h-56 w-full overflow-visible"
      role="img"
      aria-label={viewModel.ariaLabel}
    >
      <line
        x1={viewModel.guideX1}
        x2={viewModel.guideX2}
        y1={viewModel.highGuideY}
        y2={viewModel.highGuideY}
        stroke="var(--chart-grid)"
        strokeOpacity="0.85"
        strokeDasharray="4 4"
      />
      <line
        x1={viewModel.guideX1}
        x2={viewModel.guideX2}
        y1={viewModel.lowGuideY}
        y2={viewModel.lowGuideY}
        stroke="var(--chart-grid)"
        strokeOpacity="0.85"
        strokeDasharray="4 4"
      />
      <path d={viewModel.areaPath} fill={viewModel.stroke} fillOpacity="0.12" stroke="none" />
      <polyline
        fill="none"
        stroke={viewModel.stroke}
        strokeWidth="1.75"
        strokeLinejoin="round"
        strokeLinecap="round"
        points={viewModel.points}
      />
      <circle
        cx={viewModel.lastPoint.x}
        cy={viewModel.lastPoint.y}
        r="3.25"
        fill={viewModel.stroke}
        stroke="var(--chart-grid-major)"
        strokeOpacity="0.8"
        strokeWidth="1"
      />
      <text
        x={viewModel.highLabel.x}
        y={viewModel.highLabel.y}
        textAnchor="end"
        fontFamily="IBM Plex Mono, ui-monospace"
        fontSize="10"
        fill="currentColor"
        fillOpacity="0.55"
      >
        {viewModel.highLabel.value}
      </text>
      <text
        x={viewModel.lowLabel.x}
        y={viewModel.lowLabel.y}
        textAnchor="end"
        fontFamily="IBM Plex Mono, ui-monospace"
        fontSize="10"
        fill="currentColor"
        fillOpacity="0.55"
      >
        {viewModel.lowLabel.value}
      </text>
    </svg>
  );
}

function ChartStat({ stat }: { stat: HistoricalChartStatTile }) {
  return (
    <div className="rounded-md border border-border/60 bg-secondary/25 px-2.5 py-1.5">
      <div className="text-[10px] uppercase tracking-wide text-muted-foreground">{stat.label}</div>
      <div className="mt-0.5 font-mono text-sm text-foreground">{stat.value}</div>
    </div>
  );
}
