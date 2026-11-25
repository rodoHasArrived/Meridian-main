import { AlertCircle, LineChart, Loader2, RefreshCcw, X } from "lucide-react";
import { ChartEvidenceMark, ChartEvidenceView } from "@/components/charts/chart-evidence";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { StretchedChartLabels } from "@/components/charts/stretched-chart-labels";
import { cn } from "@/lib/utils";
import {
  useHistoricalChartViewModel,
  type CandlestickBarViewModel,
  type CandlestickBollingerOverlay,
  type CandlestickChartViewModel,
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
import { eyebrowClassName, usesTrackedCaps } from "@/design-system/label-case";

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
            <div className={eyebrowClassName(vm.eyebrow)}>{vm.eyebrow}</div>
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
          inputDescribedBy={vm.compareInputDescribedBy}
          inputInvalid={vm.compareInputInvalid}
          value={vm.compareInput}
          onChange={vm.setCompareInput}
          onSubmit={vm.submitCompareSymbol}
          submitDisabled={vm.compareSubmitDisabled}
          submitDisabledReason={vm.compareSubmitDisabledReason}
          submitAriaLabel={vm.compareSubmitAriaLabel}
          formLabel={vm.compareFormLabel}
          chipsListLabel={vm.compareChipsListLabel}
          clearAriaLabel={vm.compareClearAriaLabel}
          chips={vm.compareChips}
          feedbackId={vm.compareFeedbackId}
          feedbackMessage={vm.compareFeedbackMessage}
          feedbackRole={vm.compareFeedbackRole}
          feedbackAriaLive={vm.compareFeedbackAriaLive}
          hasError={vm.compareErrorMessage !== null}
          onClearAll={vm.clearCompareSymbols}
        />
      </CardHeader>
      <CardContent>
        {vm.compareFailureMessages.length > 0 ? (
          <div role="alert" className="mb-3 rounded border border-danger/40 bg-danger/10 p-3 text-sm text-danger">
            <p className="font-medium">Historical comparison incomplete</p>
            {vm.compareFailureMessages.map((message) => <p key={message}>{message}</p>)}
          </div>
        ) : null}
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
  inputDescribedBy: string;
  inputInvalid: boolean;
  value: string;
  onChange: (next: string) => void;
  onSubmit: (event?: { preventDefault?: () => void }) => void;
  submitDisabled: boolean;
  submitDisabledReason: string | null;
  submitAriaLabel: string;
  formLabel: string;
  chipsListLabel: string;
  clearAriaLabel: string;
  chips: CompareChipViewModel[];
  feedbackId: string;
  feedbackMessage: string;
  feedbackRole: "alert" | undefined;
  feedbackAriaLive: "assertive" | undefined;
  hasError: boolean;
  onClearAll: () => void;
}

function CompareControl({
  inputId,
  inputLabel,
  inputPlaceholder,
  inputDescribedBy,
  inputInvalid,
  value,
  onChange,
  onSubmit,
  submitDisabled,
  submitDisabledReason,
  submitAriaLabel,
  formLabel,
  chipsListLabel,
  clearAriaLabel,
  chips,
  feedbackId,
  feedbackMessage,
  feedbackRole,
  feedbackAriaLive,
  hasError,
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
        aria-label={formLabel}
      >
        <label htmlFor={inputId} className="sr-only">{inputLabel}</label>
        <Input
          id={inputId}
          placeholder={inputPlaceholder}
          value={value}
          onChange={(event) => onChange(event.target.value)}
          autoComplete="off"
          spellCheck={false}
          aria-invalid={inputInvalid ? "true" : undefined}
          aria-describedby={inputDescribedBy}
          aria-errormessage={inputInvalid ? feedbackId : undefined}
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
              aria-label={clearAriaLabel}
              data-testid="historical-chart-compare-clear"
            >
              Clear
            </Button>
          ) : null}
        </div>
      </form>
      <p
        id={feedbackId}
        role={feedbackRole}
        aria-live={feedbackAriaLive}
        className={cn(hasError ? "text-xs text-danger" : "sr-only")}
        data-testid="historical-chart-compare-feedback"
      >
        {feedbackMessage}
      </p>
      {chips.length > 0 ? (
        <ul aria-label={chipsListLabel} className="flex flex-wrap gap-1.5">
          {chips.map((chip) => (
            <CompareChip key={chip.symbol} chip={chip} />
          ))}
        </ul>
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
      <span className={cn("text-xs uppercase tracking-wide", statusToneClass)}>{chip.statusLabel}</span>
      <button
        type="button"
        onClick={chip.remove}
        aria-label={chip.removeAriaLabel}
        className="ml-1 inline-flex h-4 w-4 items-center justify-center rounded-full hover:bg-secondary/60 focus:outline-hidden focus-visible:ring-2 focus-visible:ring-primary/40"
        data-testid={`historical-chart-compare-chip-${chip.symbol}-remove`}
      >
        <X className="h-3 w-3" aria-hidden="true" />
      </button>
    </li>
  );
}

function CompareChartView({ viewModel: vm }: { viewModel: CompareChartViewModel }) {
  const observations = vm.series.flatMap((series) => series.observations);
  return (
    <div className="relative">
      <ChartEvidenceView label="Historical comparison" evidence={observations}>
        {(inspection) => (
          <div className="relative">
            <svg
              viewBox={vm.viewBox}
              preserveAspectRatio="none"
              className="block h-64 w-full overflow-visible"
              role="group"
              aria-label={vm.ariaLabel}
              aria-describedby={inspection.hintId}
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
              {observations.map((point, index) => (
                <ChartEvidenceMark key={point.id} inspection={inspection} index={index} geometry={point} />
              ))}
            </svg>
            <StretchedChartLabels viewBox={vm.viewBox} labels={[vm.highLabel, vm.lowLabel]} fontSize={12} />
          </div>
        )}
      </ChartEvidenceView>
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
      className="mt-2 flex flex-wrap gap-3 text-xs text-muted-foreground"
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
          {s.isBase ? <span className="text-xs uppercase tracking-wide">Base</span> : null}
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
    <ChartEvidenceView label="Historical closing prices" evidence={viewModel.observations}>
      {(inspection) => (
        <div className="relative">
          <svg
            viewBox={viewModel.viewBox}
            preserveAspectRatio="none"
            className="block h-56 w-full overflow-visible"
            role="group"
            aria-label={viewModel.ariaLabel}
            aria-describedby={inspection.hintId}
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
            {viewModel.observations.map((point, index) => (
              <ChartEvidenceMark key={point.id} inspection={inspection} index={index} geometry={point} />
            ))}
          </svg>
          <StretchedChartLabels viewBox={viewModel.viewBox} labels={[viewModel.highLabel, viewModel.lowLabel]} fontSize={12} />
        </div>
      )}
    </ChartEvidenceView>
  );
}

function ChartStat({ stat }: { stat: HistoricalChartStatTile }) {
  return (
    <div className="rounded-md border border-border/60 bg-secondary/25 px-2.5 py-1.5">
      <div className={cn("text-xs text-muted-foreground", usesTrackedCaps(stat.label) && "uppercase tracking-wide")}>{stat.label}</div>
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
  const observations = [...vm.bars.map((bar) => bar.evidence), ...vm.references];
  const overlayLegend = [
    ...vm.smaOverlays.map((overlay) => ({ key: `sma-${overlay.period}`, label: overlay.label, stroke: overlay.stroke })),
    ...(vm.bollingerOverlay ? [{ key: "bollinger", label: vm.bollingerOverlay.label, stroke: vm.bollingerOverlay.stroke }] : [])
  ];

  const heightClass = vm.rsiPanel ? "h-[21rem]" : "h-64";

  return (
    <div className="relative">
      <ChartEvidenceView label={vm.ariaLabel} evidence={observations} initialIndex={vm.bars.length - 1}>
        {(inspection) => {
          const inspectedBar = vm.bars[inspection.activeIndex];
          return (
            <div className="relative">
              <svg
                viewBox={vm.viewBox}
                preserveAspectRatio="none"
                className={cn("block w-full overflow-visible", heightClass)}
                role="group"
                aria-label={vm.ariaLabel}
                aria-describedby={inspection.hintId}
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
                {inspectedBar ? (
                  <line
                    x1={inspectedBar.midX}
                    x2={inspectedBar.midX}
                    y1={0}
                    y2={vm.geometry.height}
                    stroke="var(--chart-crosshair)"
                    strokeOpacity="0.6"
                    strokeDasharray="3 3"
                    data-testid="historical-chart-crosshair"
                    pointerEvents="none"
                  />
                ) : null}
                {vm.bars.map((bar, index) => (
                  <ChartEvidenceMark
                    key={bar.evidence.id}
                    inspection={inspection}
                    index={index}
                    geometry={{ x: bar.midX - vm.geometry.slotWidth / 2, y: 0, width: vm.geometry.slotWidth, height: vm.geometry.height }}
                  />
                ))}
                {vm.references.map((reference, index) => (
                  <ChartEvidenceMark key={reference.id} inspection={inspection} index={vm.bars.length + index} geometry={reference} />
                ))}
              </svg>
              <StretchedChartLabels viewBox={vm.viewBox} labels={[vm.highLabel, vm.lowLabel, ...(vm.rsiPanel ? [vm.rsiPanel.overboughtLabel, vm.rsiPanel.oversoldLabel] : [])]} fontSize={12} />
            </div>
          );
        }}
      </ChartEvidenceView>
      {overlayLegend.length > 0 || vm.rsiPanel ? (
        <ul
          aria-label="Indicator overlays"
          className="mt-2 flex flex-wrap gap-3 text-xs text-muted-foreground"
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
                style={{ backgroundColor: "var(--chart-rsi, #2E4B6E)" }}
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
      <polyline
        fill="none"
        stroke="var(--chart-rsi, #2E4B6E)"
        strokeOpacity="0.95"
        strokeWidth="1.4"
        strokeLinejoin="round"
        strokeLinecap="round"
        points={panel.points}
      />
    </g>
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
