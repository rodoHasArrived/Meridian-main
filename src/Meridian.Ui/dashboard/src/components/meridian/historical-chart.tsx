import { useCallback, useMemo, useRef, useState } from "react";
import { AlertCircle, LineChart, Loader2, RefreshCcw, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { cn } from "@/lib/utils";
import {
  HISTORICAL_CHART_TIMEFRAMES,
  buildCandlestickChartViewModel,
  computeChartStats,
  formatIntervalLabel,
  useHistoricalChartViewModel,
  type CandlestickBarViewModel,
  type CandlestickBollingerOverlay,
  type CandlestickChartViewModel,
  type CandlestickHoverDetail,
  type CandlestickRsiPanel,
  type ChartModeOption,
  type CompareChartViewModel,
  type CompareChipViewModel,
  type CompareSeriesViewModel,
  type HistoricalChartSparklineViewModel,
  type HistoricalChartStatePanel,
  type HistoricalChartStatTile,
  type IndicatorToggleOption
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
          <span aria-hidden="true" className="text-border/60 select-none">|</span>
          <IndicatorToggleGroup options={vm.indicatorOptions} />
        </div>
        <CompareControl
          inputId={vm.compareInputId}
          inputLabel={vm.compareInputLabel}
          inputPlaceholder={vm.compareInputPlaceholder}
          value={vm.compareInput}
          onChange={vm.setCompareInput}
          onSubmit={vm.submitCompareSymbol}
          submitDisabled={vm.compareSubmitDisabled}
          submitDisabledReason={vm.compareSubmitDisabledReason}
          submitAriaLabel={vm.compareSubmitAriaLabel}
          chips={vm.compareChips}
          errorMessage={vm.compareErrorMessage}
          onClearAll={vm.clearCompareSymbols}
        />
      </CardHeader>
      <CardContent>
        {vm.statePanel ? (
          <HistoricalChartStatePanelView panel={vm.statePanel} onRetry={vm.retry} />
        ) : vm.compareActive && vm.compareChart ? (
          <div className="space-y-3">
            <CompareChartView viewModel={vm.compareChart} />
          </div>
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

interface CompareControlProps {
  inputId: string;
  inputLabel: string;
  inputPlaceholder: string;
  value: string;
  onChange: (next: string) => void;
  onSubmit: (event?: { preventDefault?: () => void }) => void;
  submitDisabled: boolean;
  submitDisabledReason: string | null;
  submitAriaLabel: string;
  chips: CompareChipViewModel[];
  errorMessage: string | null;
  onClearAll: () => void;
}

function CompareControl({
  inputId,
  inputLabel,
  inputPlaceholder,
  value,
  onChange,
  onSubmit,
  submitDisabled,
  submitDisabledReason,
  submitAriaLabel,
  chips,
  errorMessage,
  onClearAll
}: CompareControlProps) {
  return (
    <div className="mt-3 flex flex-col gap-2" data-testid="historical-chart-compare-control">
      <form
        onSubmit={(event) => {
          event.preventDefault();
          onSubmit(event);
        }}
        className="flex flex-col gap-2 sm:flex-row sm:items-center"
        aria-label="Compare additional symbols"
      >
        <label htmlFor={inputId} className="sr-only">{inputLabel}</label>
        <Input
          id={inputId}
          placeholder={inputPlaceholder}
          value={value}
          onChange={(event) => onChange(event.target.value)}
          autoComplete="off"
          spellCheck={false}
          className="sm:max-w-[200px]"
        />
        <div className="flex items-center gap-2">
          <Button
            type="submit"
            size="sm"
            variant="outline"
            disabled={submitDisabled}
            disabledReason={submitDisabledReason}
            aria-label={submitAriaLabel}
            data-testid="historical-chart-compare-submit"
          >
            Compare
          </Button>
          {chips.length > 0 ? (
            <Button
              type="button"
              size="sm"
              variant="outline"
              onClick={onClearAll}
              aria-label="Clear all comparison symbols"
              data-testid="historical-chart-compare-clear"
            >
              Clear
            </Button>
          ) : null}
        </div>
      </form>
      {chips.length > 0 ? (
        <ul aria-label="Active comparison symbols" className="flex flex-wrap gap-1.5">
          {chips.map((chip) => (
            <CompareChip key={chip.symbol} chip={chip} />
          ))}
        </ul>
      ) : null}
      {errorMessage ? (
        <p role="alert" className="text-xs text-danger" data-testid="historical-chart-compare-error">
          {errorMessage}
        </p>
      ) : null}
    </div>
  );
}

function CompareChip({ chip }: { chip: CompareChipViewModel }) {
  const statusToneClass = chip.status === "error"
    ? "text-danger"
    : chip.status === "loading"
      ? "text-muted-foreground"
      : chip.status === "empty"
        ? "text-warning"
        : "text-foreground";
  return (
    <li
      aria-label={chip.ariaLabel}
      data-testid={`historical-chart-compare-chip-${chip.symbol}`}
      className="flex items-center gap-1.5 rounded-full border border-border/60 bg-secondary/30 px-2 py-0.5 text-xs"
    >
      <span
        aria-hidden="true"
        className="inline-block h-2 w-2 rounded-full"
        style={{ backgroundColor: chip.color }}
      />
      <span className="font-mono">{chip.symbol}</span>
      <span className={cn("text-[10px] uppercase tracking-wide", statusToneClass)}>{chip.statusLabel}</span>
      <button
        type="button"
        onClick={chip.remove}
        aria-label={chip.removeAriaLabel}
        className="ml-1 inline-flex h-4 w-4 items-center justify-center rounded-full hover:bg-secondary/60 focus:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
        data-testid={`historical-chart-compare-chip-${chip.symbol}-remove`}
      >
        <X className="h-3 w-3" aria-hidden="true" />
      </button>
    </li>
  );
}

function CompareChartView({ viewModel: vm }: { viewModel: CompareChartViewModel }) {
  return (
    <div className="relative">
      <svg
        viewBox={vm.viewBox}
        preserveAspectRatio="none"
        className="block h-64 w-full overflow-visible"
        role="img"
        aria-label={vm.ariaLabel}
      >
        <line
          x1={vm.guideX1}
          x2={vm.guideX2}
          y1={vm.zeroLineY}
          y2={vm.zeroLineY}
          stroke="var(--chart-grid-major, currentColor)"
          strokeOpacity="0.4"
          strokeDasharray="4 4"
        />
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
        {vm.series.map((series) =>
          series.points ? (
            <polyline
              key={series.symbol}
              fill="none"
              stroke={series.color}
              strokeOpacity={series.isBase ? 1 : 0.9}
              strokeWidth={series.isBase ? 2 : 1.5}
              strokeLinejoin="round"
              strokeLinecap="round"
              points={series.points}
              data-testid={`historical-chart-compare-series-${series.symbol}`}
            />
          ) : null
        )}
      </svg>
      <CompareLegend series={vm.series} />
    </div>
  );
}

const compareLegendToneClass = {
  positive: "text-success",
  negative: "text-danger",
  neutral: "text-foreground"
} as const;

function CompareLegend({ series }: { series: CompareSeriesViewModel[] }) {
  return (
    <ul
      aria-label="Comparison series legend"
      className="mt-2 flex flex-wrap gap-3 text-[11px] text-muted-foreground"
      data-testid="historical-chart-compare-legend"
    >
      {series.map((s) => (
        <li key={s.symbol} className="flex items-center gap-1.5">
          <span
            aria-hidden="true"
            className="inline-block h-0.5 w-4 rounded"
            style={{ backgroundColor: s.color }}
          />
          <span className="font-mono">{s.symbol}</span>
          {s.isBase ? <span className="text-[10px] uppercase tracking-wide">Base</span> : null}
          <span className={cn("font-mono", compareLegendToneClass[s.lastChangeTone])}>
            {s.lastChangeLabel}
          </span>
        </li>
      ))}
    </ul>
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

function IndicatorToggleGroup({ options }: { options: IndicatorToggleOption[] }) {
  return (
    <div
      role="group"
      aria-label="Toggle technical indicators"
      className="flex gap-1"
    >
      {options.map((opt) => (
        <Button
          key={opt.id}
          size="sm"
          variant={opt.buttonVariant}
          onClick={opt.toggle}
          aria-pressed={opt.ariaPressed}
          aria-label={opt.ariaLabel}
          data-testid={`historical-chart-indicator-${opt.id}`}
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
  const overlayLegend = useMemo(
    () => {
      const entries: Array<{ key: string; label: string; stroke: string }> = vm.smaOverlays.map((overlay) => ({
        key: `sma-${overlay.period}`,
        label: overlay.label,
        stroke: overlay.stroke
      }));
      if (vm.bollingerOverlay) {
        entries.push({
          key: "bollinger",
          label: vm.bollingerOverlay.label,
          stroke: vm.bollingerOverlay.stroke
        });
      }
      return entries;
    },
    [vm.smaOverlays, vm.bollingerOverlay]
  );

  const heightClass = vm.rsiPanel ? "h-[21rem]" : "h-64";

  return (
    <div className="relative">
      <svg
        ref={svgRef}
        viewBox={vm.viewBox}
        preserveAspectRatio="none"
        className={cn("block w-full overflow-visible focus:outline-none focus-visible:ring-2 focus-visible:ring-primary/40", heightClass)}
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
        {/* Bollinger Bands overlay (renders below SMA so SMA stays prominent) */}
        {vm.bollingerOverlay ? (
          <BollingerBandsOverlay overlay={vm.bollingerOverlay} />
        ) : null}
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
        {/* RSI sub-panel */}
        {vm.rsiPanel ? <RsiPanelView panel={vm.rsiPanel} /> : null}
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
      {overlayLegend.length > 0 ? (
        <ul
          aria-label="Indicator overlays"
          className="mt-2 flex flex-wrap gap-3 text-[11px] text-muted-foreground"
        >
          {overlayLegend.map((entry) => (
            <li key={entry.key} className="flex items-center gap-1.5">
              <span
                aria-hidden="true"
                className="inline-block h-0.5 w-4 rounded"
                style={{ backgroundColor: entry.stroke }}
              />
              <span>{entry.label}</span>
            </li>
          ))}
          {vm.rsiPanel ? (
            <li className="flex items-center gap-1.5" data-testid="historical-chart-rsi-legend">
              <span
                aria-hidden="true"
                className="inline-block h-0.5 w-4 rounded"
                style={{ backgroundColor: "var(--chart-rsi, #ec4899)" }}
              />
              <span>
                {vm.rsiPanel.label}
                <span className={cn("ml-1 font-mono", rsiToneClass[vm.rsiPanel.lastValueTone])}>
                  {vm.rsiPanel.lastValueLabel}
                </span>
              </span>
            </li>
          ) : null}
        </ul>
      ) : null}
      <span aria-live="polite" className="sr-only">
        {liveAnnouncement}
      </span>
    </div>
  );
}

const rsiToneClass = {
  overbought: "text-danger",
  oversold: "text-success",
  neutral: "text-foreground"
} as const;

function BollingerBandsOverlay({ overlay }: { overlay: CandlestickBollingerOverlay }) {
  return (
    <g aria-label={overlay.ariaLabel} data-testid="historical-chart-bollinger">
      <path d={overlay.bandPath} fill={overlay.stroke} fillOpacity="0.08" stroke="none" />
      <polyline
        fill="none"
        stroke={overlay.stroke}
        strokeOpacity="0.85"
        strokeWidth="1.25"
        strokeLinejoin="round"
        strokeLinecap="round"
        points={overlay.upperPoints}
        data-testid="historical-chart-bollinger-upper"
      />
      <polyline
        fill="none"
        stroke={overlay.stroke}
        strokeOpacity="0.85"
        strokeWidth="1.25"
        strokeLinejoin="round"
        strokeLinecap="round"
        points={overlay.lowerPoints}
        data-testid="historical-chart-bollinger-lower"
      />
      <polyline
        fill="none"
        stroke={overlay.stroke}
        strokeOpacity="0.55"
        strokeWidth="1"
        strokeDasharray="2 3"
        strokeLinejoin="round"
        strokeLinecap="round"
        points={overlay.middlePoints}
        data-testid="historical-chart-bollinger-middle"
      />
    </g>
  );
}

function RsiPanelView({ panel }: { panel: CandlestickRsiPanel }) {
  return (
    <g aria-label={panel.ariaLabel} data-testid="historical-chart-rsi">
      <line
        x1={panel.guideX1}
        x2={panel.guideX2}
        y1={panel.overboughtY}
        y2={panel.overboughtY}
        stroke="var(--chart-grid)"
        strokeOpacity="0.7"
        strokeDasharray="2 3"
      />
      <line
        x1={panel.guideX1}
        x2={panel.guideX2}
        y1={panel.oversoldY}
        y2={panel.oversoldY}
        stroke="var(--chart-grid)"
        strokeOpacity="0.7"
        strokeDasharray="2 3"
      />
      <line
        x1={panel.guideX1}
        x2={panel.guideX2}
        y1={panel.midlineY}
        y2={panel.midlineY}
        stroke="var(--chart-grid)"
        strokeOpacity="0.35"
      />
      <text
        x={panel.overboughtLabel.x}
        y={panel.overboughtLabel.y}
        textAnchor="end"
        fontFamily="IBM Plex Mono, ui-monospace"
        fontSize="9"
        fill="currentColor"
        fillOpacity="0.55"
      >
        {panel.overboughtLabel.value}
      </text>
      <text
        x={panel.oversoldLabel.x}
        y={panel.oversoldLabel.y}
        textAnchor="end"
        fontFamily="IBM Plex Mono, ui-monospace"
        fontSize="9"
        fill="currentColor"
        fillOpacity="0.55"
      >
        {panel.oversoldLabel.value}
      </text>
      <polyline
        fill="none"
        stroke="var(--chart-rsi, #ec4899)"
        strokeOpacity="0.95"
        strokeWidth="1.4"
        strokeLinejoin="round"
        strokeLinecap="round"
        points={panel.points}
      />
    </g>
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
