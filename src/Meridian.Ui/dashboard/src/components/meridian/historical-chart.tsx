import { useCallback, useMemo, useRef, useState } from "react";
import { AlertCircle, LineChart, Loader2, RefreshCcw } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { cn } from "@/lib/utils";
import {
  HISTORICAL_CHART_TIMEFRAMES,
  buildCandlestickChartViewModel,
  computeChartStats,
  formatIntervalLabel,
  useHistoricalChartViewModel,
  type CandlestickBarViewModel,
  type CandlestickChartViewModel,
  type CandlestickHoverDetail,
  type ChartModeOption,
  type HistoricalChartSparklineViewModel,
  type HistoricalChartStatePanel,
  type HistoricalChartStatTile
} from "@/components/meridian/historical-chart.view-model";

export {
  HISTORICAL_CHART_TIMEFRAMES,
  buildCandlestickChartViewModel,
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
        <div className="mt-3 flex flex-wrap items-center gap-2">
          <div
            className="flex flex-wrap gap-1"
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
          <span aria-hidden="true" className="text-border/60 select-none">|</span>
          <ChartModeToggle options={vm.chartModeOptions} />
        </div>
      </CardHeader>
      <CardContent>
        {vm.statePanel ? (
          <HistoricalChartStatePanelView panel={vm.statePanel} onRetry={vm.retry} />
        ) : vm.activeChartMode === "candles" && vm.candlestickChart ? (
          <div className="space-y-3">
            <CandlestickChartView viewModel={vm.candlestickChart} />
            <div className="grid grid-cols-2 gap-2 sm:grid-cols-5">
              {vm.statTiles.map((stat) => <ChartStat key={stat.id} stat={stat} />)}
            </div>
          </div>
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

function ChartModeToggle({ options }: { options: ChartModeOption[] }) {
  return (
    <div
      role="group"
      aria-label="Select chart type"
      className="flex gap-1"
    >
      {options.map((opt) => (
        <Button
          key={opt.mode}
          size="sm"
          variant={opt.buttonVariant}
          onClick={opt.select}
          aria-pressed={opt.ariaPressed}
          aria-label={opt.ariaLabel}
          data-testid={`historical-chart-mode-${opt.mode}`}
        >
          {opt.label}
        </Button>
      ))}
    </div>
  );
}

function CandlestickChartView({ viewModel: vm }: { viewModel: CandlestickChartViewModel }) {
  const svgRef = useRef<SVGSVGElement | null>(null);
  const [hoverIndex, setHoverIndex] = useState<number | null>(null);

  const lastBarIndex = vm.bars.length - 1;
  const { padX, slotWidth, width } = vm.geometry;

  const resolveIndex = useCallback(
    (clientX: number): number | null => {
      const svg = svgRef.current;
      if (!svg || vm.bars.length === 0) return null;
      const rect = svg.getBoundingClientRect();
      if (rect.width <= 0) return null;
      const ratio = (clientX - rect.left) / rect.width;
      const svgX = ratio * width;
      const slotIndex = Math.floor((svgX - padX) / slotWidth);
      if (slotIndex < 0) return 0;
      if (slotIndex > lastBarIndex) return lastBarIndex;
      return slotIndex;
    },
    [lastBarIndex, padX, slotWidth, vm.bars.length, width]
  );

  const handlePointer = useCallback(
    (event: React.PointerEvent<SVGSVGElement>) => {
      const next = resolveIndex(event.clientX);
      if (next !== null) setHoverIndex(next);
    },
    [resolveIndex]
  );

  const clearHover = useCallback(() => setHoverIndex(null), []);

  const handleKeyDown = useCallback(
    (event: React.KeyboardEvent<SVGSVGElement>) => {
      if (vm.bars.length === 0) return;
      if (event.key === "ArrowRight") {
        event.preventDefault();
        setHoverIndex((prev) => {
          const base = prev ?? -1;
          return Math.min(lastBarIndex, base + 1);
        });
      } else if (event.key === "ArrowLeft") {
        event.preventDefault();
        setHoverIndex((prev) => {
          if (prev === null) return lastBarIndex;
          return Math.max(0, prev - 1);
        });
      } else if (event.key === "Home") {
        event.preventDefault();
        setHoverIndex(0);
      } else if (event.key === "End") {
        event.preventDefault();
        setHoverIndex(lastBarIndex);
      } else if (event.key === "Escape") {
        setHoverIndex(null);
      }
    },
    [lastBarIndex, vm.bars.length]
  );

  const hoveredBar = hoverIndex !== null ? vm.bars[hoverIndex] ?? null : null;
  const hoverDetail = hoveredBar?.hover ?? null;
  const liveAnnouncement = hoverDetail?.ariaLabel ?? "";
  const smaLegend = useMemo(
    () =>
      vm.smaOverlays.map((overlay) => ({
        key: `sma-${overlay.period}`,
        label: overlay.label,
        stroke: overlay.stroke
      })),
    [vm.smaOverlays]
  );

  return (
    <div className="relative">
      <svg
        ref={svgRef}
        viewBox={vm.viewBox}
        preserveAspectRatio="none"
        className="block h-64 w-full overflow-visible focus:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
        role="img"
        aria-label={vm.ariaLabel}
        tabIndex={0}
        onPointerMove={handlePointer}
        onPointerDown={handlePointer}
        onPointerLeave={clearHover}
        onPointerCancel={clearHover}
        onBlur={clearHover}
        onKeyDown={handleKeyDown}
      >
        {/* High guide line */}
        <line
          x1={vm.guideX1}
          x2={vm.guideX2}
          y1={vm.highGuideY}
          y2={vm.highGuideY}
          stroke="var(--chart-grid)"
          strokeOpacity="0.85"
          strokeDasharray="4 4"
        />
        {/* Low guide line */}
        <line
          x1={vm.guideX1}
          x2={vm.guideX2}
          y1={vm.lowGuideY}
          y2={vm.lowGuideY}
          stroke="var(--chart-grid)"
          strokeOpacity="0.85"
          strokeDasharray="4 4"
        />
        {/* High price label */}
        <text
          x={vm.highLabel.x}
          y={vm.highLabel.y}
          textAnchor="end"
          fontFamily="IBM Plex Mono, ui-monospace"
          fontSize="10"
          fill="currentColor"
          fillOpacity="0.55"
        >
          {vm.highLabel.value}
        </text>
        {/* Low price label */}
        <text
          x={vm.lowLabel.x}
          y={vm.lowLabel.y}
          textAnchor="end"
          fontFamily="IBM Plex Mono, ui-monospace"
          fontSize="10"
          fill="currentColor"
          fillOpacity="0.55"
        >
          {vm.lowLabel.value}
        </text>
        {/* Separator between price and volume areas */}
        <line
          x1={vm.guideX1}
          x2={vm.guideX2}
          y1={vm.volumeAreaTop - 2}
          y2={vm.volumeAreaTop - 2}
          stroke="var(--chart-grid)"
          strokeOpacity="0.3"
        />
        {/* Candlestick bars */}
        {vm.bars.map((bar, i) => (
          <CandlestickBar key={i} bar={bar} />
        ))}
        {/* Volume bars */}
        {vm.volumeBars.map((vb, i) => (
          <rect
            key={i}
            x={vb.x}
            y={vb.y}
            width={vb.width}
            height={vb.height}
            fill={vb.isBullish ? "var(--chart-up)" : "var(--chart-dn)"}
            fillOpacity="0.35"
          />
        ))}
        {/* Simple moving average overlays */}
        {vm.smaOverlays.map((overlay) => (
          <polyline
            key={overlay.period}
            fill="none"
            stroke={overlay.stroke}
            strokeOpacity="0.9"
            strokeWidth="1.5"
            strokeLinejoin="round"
            strokeLinecap="round"
            points={overlay.points}
            aria-label={overlay.ariaLabel}
            data-testid={`historical-chart-sma-${overlay.period}`}
          />
        ))}
        {/* Crosshair on hovered bar */}
        {hoveredBar ? (
          <line
            x1={hoveredBar.midX}
            x2={hoveredBar.midX}
            y1={0}
            y2={vm.geometry.height}
            stroke="var(--chart-grid-major, currentColor)"
            strokeOpacity="0.6"
            strokeDasharray="3 3"
            data-testid="historical-chart-crosshair"
            pointerEvents="none"
          />
        ) : null}
      </svg>
      {hoverDetail ? (
        <HistoricalChartHoverPanel detail={hoverDetail} />
      ) : (
        <p className="mt-2 text-xs text-muted-foreground">
          Hover, tap, or focus the chart and use ←/→ to inspect each bar.
        </p>
      )}
      {smaLegend.length > 0 ? (
        <ul
          aria-label="Moving average overlays"
          className="mt-2 flex flex-wrap gap-3 text-[11px] text-muted-foreground"
        >
          {smaLegend.map((entry) => (
            <li key={entry.key} className="flex items-center gap-1.5">
              <span
                aria-hidden="true"
                className="inline-block h-0.5 w-4 rounded"
                style={{ backgroundColor: entry.stroke }}
              />
              <span>{entry.label}</span>
            </li>
          ))}
        </ul>
      ) : null}
      <span aria-live="polite" className="sr-only">
        {liveAnnouncement}
      </span>
    </div>
  );
}

const hoverChangeToneClass = {
  positive: "text-success",
  negative: "text-danger",
  neutral: "text-foreground"
} as const;

function HistoricalChartHoverPanel({ detail }: { detail: CandlestickHoverDetail }) {
  return (
    <div
      data-testid="historical-chart-hover-panel"
      className="mt-2 rounded-md border border-border/60 bg-secondary/30 px-3 py-2 text-xs"
    >
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <span className="font-mono text-muted-foreground">{detail.timeLabel}</span>
        <span className={cn("font-mono", hoverChangeToneClass[detail.changeTone])}>
          {detail.changeLabel}
        </span>
      </div>
      <dl className="mt-1 grid grid-cols-2 gap-x-3 gap-y-0.5 font-mono sm:grid-cols-5">
        <HoverField label="O" value={detail.openLabel} />
        <HoverField label="H" value={detail.highLabel} />
        <HoverField label="L" value={detail.lowLabel} />
        <HoverField label="C" value={detail.closeLabel} />
        <HoverField label="Vol" value={detail.volumeLabel} />
      </dl>
    </div>
  );
}

function HoverField({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex items-baseline gap-1">
      <dt className="text-[10px] uppercase tracking-wide text-muted-foreground">{label}</dt>
      <dd className="text-foreground">{value}</dd>
    </div>
  );
}

function CandlestickBar({ bar }: { bar: CandlestickBarViewModel }) {
  const color = bar.isDoji
    ? "var(--chart-bench)"
    : bar.isBullish
      ? "var(--chart-up)"
      : "var(--chart-dn)";

  return (
    <g>
      <title>{bar.tooltipLabel}</title>
      {/* Wick */}
      <line
        x1={bar.midX}
        x2={bar.midX}
        y1={bar.wickY1}
        y2={bar.wickY2}
        stroke={color}
        strokeWidth="1"
        strokeOpacity="0.8"
      />
      {/* Body */}
      <rect
        x={bar.bodyX}
        y={bar.bodyY}
        width={bar.bodyWidth}
        height={bar.bodyHeight}
        fill={bar.isBullish ? color : "none"}
        fillOpacity={bar.isBullish ? 0.85 : 0}
        stroke={color}
        strokeWidth="1"
        strokeOpacity="0.9"
      />
    </g>
  );
}
