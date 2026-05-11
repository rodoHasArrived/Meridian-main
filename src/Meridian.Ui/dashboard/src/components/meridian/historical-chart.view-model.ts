import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { getHistoricalBars } from "@/lib/api";
import type { HistoricalBarPoint } from "@/types";

export type ChartMode = "line" | "candles";

export interface HistoricalChartTimeframe {
  readonly id: string;
  readonly label: string;
  readonly intervalMinutes: number;
  readonly lookbackDays: number;
}

export const HISTORICAL_CHART_TIMEFRAMES: readonly HistoricalChartTimeframe[] = [
  { id: "1D", label: "1D", intervalMinutes: 5, lookbackDays: 1 },
  { id: "5D", label: "5D", intervalMinutes: 15, lookbackDays: 5 },
  { id: "1M", label: "1M", intervalMinutes: 60, lookbackDays: 30 },
  { id: "3M", label: "3M", intervalMinutes: 240, lookbackDays: 90 },
  { id: "1Y", label: "1Y", intervalMinutes: 1440, lookbackDays: 365 }
];

type FetchStatus = "idle" | "loading" | "ready" | "error";

interface FetchState {
  status: FetchStatus;
  bars: HistoricalBarPoint[];
  errorMessage: string | null;
  appliedTimeframeId: string | null;
}

const initialFetchState: FetchState = {
  status: "idle",
  bars: [],
  errorMessage: null,
  appliedTimeframeId: null
};

export interface HistoricalChartStats {
  open: number | null;
  high: number | null;
  low: number | null;
  last: number | null;
  vwap: number | null;
  volume: number;
  change: number | null;
  changePct: number | null;
}

export interface HistoricalChartTimeframeOption {
  id: string;
  label: string;
  selected: boolean;
  buttonVariant: "default" | "outline";
  ariaPressed: boolean;
  ariaLabel: string;
  testId: string;
  select: () => void;
}

export interface HistoricalChartStatePanel {
  kind: "loading" | "error" | "empty" | "idle";
  role: "status" | "alert";
  ariaLive: "polite" | "assertive";
  title: string;
  detail: string;
  retryLabel: string | null;
  retryAriaLabel: string | null;
  retryBusy: boolean;
  retryDisabled: boolean;
}

export interface HistoricalChartSparklineViewModel {
  width: number;
  height: number;
  viewBox: string;
  ariaLabel: string;
  highGuideY: number;
  lowGuideY: number;
  guideX1: number;
  guideX2: number;
  areaPath: string;
  points: string;
  lastPoint: { x: number; y: number };
  highLabel: { value: string; x: number; y: number };
  lowLabel: { value: string; x: number; y: number };
  stroke: string;
}

export interface HistoricalChartStatTile {
  id: string;
  label: string;
  value: string;
}

export interface CandlestickBarViewModel {
  midX: number;
  bodyX: number;
  bodyY: number;
  bodyHeight: number;
  bodyWidth: number;
  wickY1: number;
  wickY2: number;
  isBullish: boolean;
  isDoji: boolean;
  tooltipLabel: string;
  hover: CandlestickHoverDetail;
}

export type CandlestickChangeTone = "positive" | "negative" | "neutral";

export interface CandlestickHoverDetail {
  index: number;
  timeLabel: string;
  openLabel: string;
  highLabel: string;
  lowLabel: string;
  closeLabel: string;
  volumeLabel: string;
  changeLabel: string;
  changeTone: CandlestickChangeTone;
  ariaLabel: string;
}

export interface CandlestickSmaOverlay {
  period: number;
  label: string;
  ariaLabel: string;
  stroke: string;
  points: string;
}

export interface CandlestickBollingerOverlay {
  period: number;
  multiplier: number;
  label: string;
  ariaLabel: string;
  stroke: string;
  upperPoints: string;
  lowerPoints: string;
  middlePoints: string;
  bandPath: string;
}

export interface CandlestickRsiPanel {
  period: number;
  label: string;
  ariaLabel: string;
  areaTop: number;
  areaHeight: number;
  points: string;
  overboughtY: number;
  oversoldY: number;
  midlineY: number;
  guideX1: number;
  guideX2: number;
  overboughtLabel: { value: string; x: number; y: number };
  oversoldLabel: { value: string; x: number; y: number };
  lastValue: number | null;
  lastValueLabel: string;
  lastValueTone: "overbought" | "oversold" | "neutral";
  lastValueY: number | null;
}

export interface CandlestickChartGeometry {
  width: number;
  height: number;
  padX: number;
  slotWidth: number;
  priceAreaHeight: number;
}

export interface CandlestickIndicatorVisibility {
  sma: boolean;
  bollinger: boolean;
  rsi: boolean;
}

export interface CandlestickChartViewModel {
  viewBox: string;
  ariaLabel: string;
  bars: CandlestickBarViewModel[];
  highGuideY: number;
  lowGuideY: number;
  guideX1: number;
  guideX2: number;
  highLabel: { value: string; x: number; y: number };
  lowLabel: { value: string; x: number; y: number };
  volumeBars: { x: number; y: number; width: number; height: number; isBullish: boolean }[];
  priceAreaHeight: number;
  volumeAreaTop: number;
  geometry: CandlestickChartGeometry;
  smaOverlays: CandlestickSmaOverlay[];
  bollingerOverlay: CandlestickBollingerOverlay | null;
  rsiPanel: CandlestickRsiPanel | null;
}

export interface ChartModeOption {
  mode: ChartMode;
  label: string;
  ariaLabel: string;
  ariaPressed: boolean;
  buttonVariant: "default" | "outline";
  select: () => void;
}

export type IndicatorId = "sma" | "bollinger" | "rsi";

export interface IndicatorToggleOption {
  id: IndicatorId;
  label: string;
  ariaLabel: string;
  ariaPressed: boolean;
  buttonVariant: "default" | "outline";
  toggle: () => void;
}

export interface HistoricalChartViewModel {
  eyebrow: string;
  title: string;
  description: string;
  activeTimeframeLabel: string;
  timeframeOptions: HistoricalChartTimeframeOption[];
  chartModeOptions: ChartModeOption[];
  activeChartMode: ChartMode;
  indicatorOptions: IndicatorToggleOption[];
  indicators: CandlestickIndicatorVisibility;
  lastPriceText: string;
  changeText: string;
  changeToneClass: string;
  statePanel: HistoricalChartStatePanel | null;
  chart: HistoricalChartSparklineViewModel | null;
  candlestickChart: CandlestickChartViewModel | null;
  statTiles: HistoricalChartStatTile[];
  retry: () => void;
}

const DEFAULT_INDICATOR_VISIBILITY: CandlestickIndicatorVisibility = {
  sma: true,
  bollinger: false,
  rsi: false
};

export function useHistoricalChartViewModel(symbol: string): HistoricalChartViewModel {
  const [activeTimeframe, setActiveTimeframe] = useState<HistoricalChartTimeframe>(
    HISTORICAL_CHART_TIMEFRAMES[0]!
  );
  const [state, setState] = useState<FetchState>(initialFetchState);
  const [chartMode, setChartMode] = useState<ChartMode>("candles");
  const [indicators, setIndicators] = useState<CandlestickIndicatorVisibility>(DEFAULT_INDICATOR_VISIBILITY);
  const requestRevision = useRef(0);
  const mounted = useRef(false);
  const requestAbortRef = useRef<AbortController | null>(null);

  const fetchBars = useCallback(async (sym: string, tf: HistoricalChartTimeframe) => {
    const revision = requestRevision.current + 1;
    requestRevision.current = revision;
    requestAbortRef.current?.abort();

    if (!sym) {
      requestAbortRef.current = null;
      setState(initialFetchState);
      return;
    }

    const controller = new AbortController();
    requestAbortRef.current = controller;
    setState((prev) => ({ ...prev, status: "loading", errorMessage: null }));

    const today = new Date();
    const fromDate = new Date(today);
    fromDate.setDate(fromDate.getDate() - tf.lookbackDays);

    try {
      const response = await getHistoricalBars(sym, {
        intervalMinutes: tf.intervalMinutes,
        from: formatDateForRequest(fromDate),
        to: formatDateForRequest(today),
        maxBars: 1500
      }, { signal: controller.signal });

      if (!mounted.current || requestRevision.current !== revision) {
        return;
      }

      setState({
        status: "ready",
        bars: response.bars ?? [],
        errorMessage: null,
        appliedTimeframeId: tf.id
      });
    } catch (err) {
      if (!mounted.current || requestRevision.current !== revision) {
        return;
      }

      setState({
        status: "error",
        bars: [],
        errorMessage: (err as Error)?.message ?? "Failed to load historical bars",
        appliedTimeframeId: tf.id
      });
    } finally {
      if (mounted.current && requestRevision.current === revision && requestAbortRef.current === controller) {
        requestAbortRef.current = null;
      }
    }
  }, []);

  useEffect(() => {
    mounted.current = true;
    return () => {
      mounted.current = false;
      requestRevision.current += 1;
      requestAbortRef.current?.abort();
    };
  }, []);

  useEffect(() => {
    void fetchBars(symbol, activeTimeframe);
  }, [symbol, activeTimeframe, fetchBars]);

  const stats = useMemo(() => computeChartStats(state.bars), [state.bars]);
  const retry = useCallback(() => {
    void fetchBars(symbol, activeTimeframe);
  }, [activeTimeframe, fetchBars, symbol]);

  const timeframeOptions = useMemo(
    () => HISTORICAL_CHART_TIMEFRAMES.map((timeframe) => ({
      id: timeframe.id,
      label: timeframe.label,
      selected: timeframe.id === activeTimeframe.id,
      buttonVariant: timeframe.id === activeTimeframe.id ? "default" as const : "outline" as const,
      ariaPressed: timeframe.id === activeTimeframe.id,
      ariaLabel: `Show ${timeframe.label} historical bars for ${symbol || "selected symbol"}`,
      testId: `historical-chart-timeframe-${timeframe.id}`,
      select: () => setActiveTimeframe(timeframe)
    })),
    [activeTimeframe.id, symbol]
  );

  const chartModeOptions = useMemo<ChartModeOption[]>(() => [
    {
      mode: "candles",
      label: "Candles",
      ariaLabel: `Switch to candlestick chart for ${symbol || "selected symbol"}`,
      ariaPressed: chartMode === "candles",
      buttonVariant: chartMode === "candles" ? "default" : "outline",
      select: () => setChartMode("candles")
    },
    {
      mode: "line",
      label: "Line",
      ariaLabel: `Switch to line chart for ${symbol || "selected symbol"}`,
      ariaPressed: chartMode === "line",
      buttonVariant: chartMode === "line" ? "default" : "outline",
      select: () => setChartMode("line")
    }
  ], [chartMode, symbol]);

  const toggleIndicator = useCallback((id: IndicatorId) => {
    setIndicators((prev) => ({ ...prev, [id]: !prev[id] }));
  }, []);

  const indicatorOptions = useMemo<IndicatorToggleOption[]>(() => [
    {
      id: "sma",
      label: "SMA",
      ariaLabel: `Toggle simple moving average overlays for ${symbol || "selected symbol"}`,
      ariaPressed: indicators.sma,
      buttonVariant: indicators.sma ? "default" : "outline",
      toggle: () => toggleIndicator("sma")
    },
    {
      id: "bollinger",
      label: "Bollinger",
      ariaLabel: `Toggle Bollinger Bands overlay for ${symbol || "selected symbol"}`,
      ariaPressed: indicators.bollinger,
      buttonVariant: indicators.bollinger ? "default" : "outline",
      toggle: () => toggleIndicator("bollinger")
    },
    {
      id: "rsi",
      label: "RSI",
      ariaLabel: `Toggle RSI sub-panel for ${symbol || "selected symbol"}`,
      ariaPressed: indicators.rsi,
      buttonVariant: indicators.rsi ? "default" : "outline",
      toggle: () => toggleIndicator("rsi")
    }
  ], [indicators.sma, indicators.bollinger, indicators.rsi, symbol, toggleIndicator]);

  const statePanel = buildHistoricalChartStatePanel({
    status: state.status,
    bars: state.bars,
    symbol,
    activeTimeframe,
    errorMessage: state.errorMessage
  });

  return {
    eyebrow: "Historical price",
    title: `${symbol || "Symbol"} · ${activeTimeframe.label}`,
    description: `OHLCV bars aggregated from stored trade events. Each bar covers ${formatIntervalLabel(activeTimeframe.intervalMinutes)}.`,
    activeTimeframeLabel: activeTimeframe.label,
    timeframeOptions,
    chartModeOptions,
    activeChartMode: chartMode,
    indicatorOptions,
    indicators,
    lastPriceText: formatPrice(stats.last),
    changeText: `${formatChange(stats.change)} (${formatChangePct(stats.changePct)})`,
    changeToneClass: chartChangeToneClass(stats.change),
    statePanel,
    chart: buildHistoricalChartSparklineViewModel({
      bars: state.bars,
      stats,
      symbol,
      timeframe: activeTimeframe.label
    }),
    candlestickChart: buildCandlestickChartViewModel({
      bars: state.bars,
      stats,
      symbol,
      timeframe: activeTimeframe.label,
      indicators
    }),
    statTiles: [
      { id: "open", label: "Open", value: formatPrice(stats.open) },
      { id: "high", label: "High", value: formatPrice(stats.high) },
      { id: "low", label: "Low", value: formatPrice(stats.low) },
      { id: "vwap", label: "VWAP", value: formatPrice(stats.vwap) },
      { id: "volume", label: "Volume", value: formatVolume(stats.volume) }
    ],
    retry
  };
}

export function buildCandlestickChartViewModel({
  bars,
  stats,
  symbol,
  timeframe,
  indicators
}: {
  bars: readonly HistoricalBarPoint[];
  stats: HistoricalChartStats;
  symbol: string;
  timeframe: string;
  indicators?: CandlestickIndicatorVisibility;
}): CandlestickChartViewModel | null {
  const visibility: CandlestickIndicatorVisibility = indicators ?? {
    sma: true,
    bollinger: false,
    rsi: false
  };
  const width = 900;
  const priceAreaHeight = 175;
  const volumeGap = 8;
  const volumeAreaHeight = 35;
  const rsiGap = visibility.rsi ? 10 : 0;
  const rsiAreaHeight = visibility.rsi ? 60 : 0;
  const totalHeight = priceAreaHeight + volumeGap + volumeAreaHeight + rsiGap + rsiAreaHeight;
  const padX = 12;
  const padY = 14;
  const volumeAreaTop = priceAreaHeight + volumeGap;
  const rsiAreaTop = volumeAreaTop + volumeAreaHeight + rsiGap;

  if (bars.length === 0 || stats.high === null || stats.low === null) {
    return null;
  }

  const chartBars = toChronologicalChartBars(bars);
  if (chartBars.length === 0) return null;

  const n = chartBars.length;
  const slotWidth = (width - padX * 2) / n;
  const bodyWidth = Math.max(1.5, slotWidth * 0.65);

  const closes = chartBars.map(({ bar }) => bar.close);

  const bollingerExtremes = visibility.bollinger
    ? computeBollingerBands(closes, 20, 2).reduce(
        (acc, band) => {
          if (band === null) return acc;
          return {
            min: Math.min(acc.min, band.lower),
            max: Math.max(acc.max, band.upper)
          };
        },
        { min: Infinity, max: -Infinity }
      )
    : { min: Infinity, max: -Infinity };

  const lowerBound = Number.isFinite(bollingerExtremes.min)
    ? Math.min(stats.low, bollingerExtremes.min)
    : stats.low;
  const upperBound = Number.isFinite(bollingerExtremes.max)
    ? Math.max(stats.high, bollingerExtremes.max)
    : stats.high;
  const priceSpan = Math.max(upperBound - lowerBound, Math.max(upperBound * 0.001, 0.01));
  const plotH = priceAreaHeight - padY * 2;
  const yForPrice = (price: number): number =>
    padY + (1 - (price - lowerBound) / priceSpan) * plotH;

  const maxVolume = chartBars.reduce((m, { bar }) => Math.max(m, bar.volume), 0);

  const candleBars: CandlestickBarViewModel[] = chartBars.map(({ bar }, i) => {
    const midX = padX + (i + 0.5) * slotWidth;
    const openY = yForPrice(bar.open);
    const closeY = yForPrice(bar.close);
    const highY = yForPrice(bar.high);
    const lowY = yForPrice(bar.low);
    const isBullish = bar.close >= bar.open;
    const isDoji = Math.abs(bar.close - bar.open) < priceSpan * 0.002;
    const bodyTop = Math.min(openY, closeY);
    const bodyH = Math.max(1.5, Math.abs(closeY - openY));
    const prevClose = i > 0 ? chartBars[i - 1]!.bar.close : null;
    const changeFromPrev = prevClose !== null ? bar.close - prevClose : null;
    const changeFromPrevPct = prevClose !== null && prevClose !== 0
      ? (changeFromPrev! / prevClose) * 100
      : null;
    const timeLabel = formatBarTimeLabel(bar.start);
    const tooltipLabel = `${timeLabel} · O ${formatPriceRaw(bar.open)} H ${formatPriceRaw(bar.high)} L ${formatPriceRaw(bar.low)} C ${formatPriceRaw(bar.close)}`;

    const hover: CandlestickHoverDetail = {
      index: i,
      timeLabel,
      openLabel: formatPrice(bar.open),
      highLabel: formatPrice(bar.high),
      lowLabel: formatPrice(bar.low),
      closeLabel: formatPrice(bar.close),
      volumeLabel: formatVolume(bar.volume),
      changeLabel: formatChangeWithPct(changeFromPrev, changeFromPrevPct),
      changeTone: candleChangeTone(changeFromPrev),
      ariaLabel: `${timeLabel}. Open ${formatPrice(bar.open)}, high ${formatPrice(bar.high)}, low ${formatPrice(bar.low)}, close ${formatPrice(bar.close)}. Volume ${formatVolume(bar.volume)}.`
    };

    return {
      midX,
      bodyX: midX - bodyWidth / 2,
      bodyY: bodyTop,
      bodyHeight: bodyH,
      bodyWidth,
      wickY1: highY,
      wickY2: lowY,
      isBullish,
      isDoji,
      tooltipLabel,
      hover
    };
  });

  const volumeBars = chartBars.map(({ bar }, i) => {
    const midX = padX + (i + 0.5) * slotWidth;
    const ratio = maxVolume > 0 ? bar.volume / maxVolume : 0;
    const barH = Math.max(1, ratio * volumeAreaHeight);
    return {
      x: midX - bodyWidth / 2,
      y: volumeAreaTop + volumeAreaHeight - barH,
      width: bodyWidth,
      height: barH,
      isBullish: bar.close >= bar.open
    };
  });

  const midXs = candleBars.map((b) => b.midX);

  const smaOverlays = visibility.sma
    ? buildSmaOverlays({ closes, midXs, yForPrice })
    : [];

  const bollingerOverlay = visibility.bollinger
    ? buildBollingerOverlay({ closes, midXs, yForPrice })
    : null;

  const rsiPanel = visibility.rsi
    ? buildRsiPanel({
        closes,
        midXs,
        areaTop: rsiAreaTop,
        areaHeight: rsiAreaHeight,
        padX,
        width
      })
    : null;

  return {
    viewBox: `0 0 ${width} ${totalHeight}`,
    ariaLabel: `${symbol} ${timeframe} candlestick chart, ${n} bars from ${formatPrice(stats.low)} to ${formatPrice(stats.high)}.`,
    bars: candleBars,
    highGuideY: yForPrice(stats.high),
    lowGuideY: yForPrice(stats.low),
    guideX1: padX,
    guideX2: width - padX,
    highLabel: {
      value: formatPrice(stats.high),
      x: width - padX,
      y: Math.max(yForPrice(stats.high) - 4, 10)
    },
    lowLabel: {
      value: formatPrice(stats.low),
      x: width - padX,
      y: Math.min(yForPrice(stats.low) + 12, priceAreaHeight - 4)
    },
    volumeBars,
    priceAreaHeight,
    volumeAreaTop,
    geometry: {
      width,
      height: totalHeight,
      padX,
      slotWidth,
      priceAreaHeight
    },
    smaOverlays,
    bollingerOverlay,
    rsiPanel
  };
}

interface BuildSmaOverlaysInput {
  closes: number[];
  midXs: number[];
  yForPrice: (price: number) => number;
}

const SMA_OVERLAY_DEFINITIONS: ReadonlyArray<{
  period: number;
  label: string;
  stroke: string;
}> = [
  { period: 20, label: "SMA 20", stroke: "var(--chart-sma-20, #f5a524)" },
  { period: 50, label: "SMA 50", stroke: "var(--chart-sma-50, #7c5cff)" }
];

function buildSmaOverlays({ closes, midXs, yForPrice }: BuildSmaOverlaysInput): CandlestickSmaOverlay[] {
  const overlays: CandlestickSmaOverlay[] = [];
  for (const def of SMA_OVERLAY_DEFINITIONS) {
    const values = computeSimpleMovingAverage(closes, def.period);
    const points = values
      .map((value, i) =>
        value === null
          ? null
          : `${midXs[i]!.toFixed(2)},${yForPrice(value).toFixed(2)}`
      )
      .filter((p): p is string => p !== null)
      .join(" ");

    if (!points) continue;

    overlays.push({
      period: def.period,
      label: def.label,
      ariaLabel: `${def.period}-period simple moving average`,
      stroke: def.stroke,
      points
    });
  }
  return overlays;
}

export function computeSimpleMovingAverage(
  values: readonly number[],
  period: number
): Array<number | null> {
  if (period <= 0) return values.map(() => null);
  const result: Array<number | null> = new Array(values.length).fill(null);
  if (values.length < period) return result;

  let sum = 0;
  for (let i = 0; i < period; i++) sum += values[i]!;
  result[period - 1] = sum / period;
  for (let i = period; i < values.length; i++) {
    sum += values[i]! - values[i - period]!;
    result[i] = sum / period;
  }
  return result;
}

export interface BollingerBand {
  middle: number;
  upper: number;
  lower: number;
}

export function computeBollingerBands(
  values: readonly number[],
  period: number,
  multiplier: number
): Array<BollingerBand | null> {
  const result: Array<BollingerBand | null> = new Array(values.length).fill(null);
  if (period <= 0 || values.length < period) return result;

  let sum = 0;
  let sumSq = 0;
  for (let i = 0; i < period; i++) {
    const v = values[i]!;
    sum += v;
    sumSq += v * v;
  }
  for (let i = period - 1; i < values.length; i++) {
    if (i >= period) {
      const out = values[i - period]!;
      const incoming = values[i]!;
      sum += incoming - out;
      sumSq += incoming * incoming - out * out;
    }
    const mean = sum / period;
    const variance = Math.max(0, sumSq / period - mean * mean);
    const stddev = Math.sqrt(variance);
    result[i] = {
      middle: mean,
      upper: mean + multiplier * stddev,
      lower: mean - multiplier * stddev
    };
  }
  return result;
}

export function computeRsi(values: readonly number[], period: number): Array<number | null> {
  const result: Array<number | null> = new Array(values.length).fill(null);
  if (period <= 0 || values.length <= period) return result;

  let avgGain = 0;
  let avgLoss = 0;
  for (let i = 1; i <= period; i++) {
    const change = values[i]! - values[i - 1]!;
    if (change > 0) avgGain += change;
    else avgLoss += -change;
  }
  avgGain /= period;
  avgLoss /= period;
  result[period] = computeRsiFromAverages(avgGain, avgLoss);

  for (let i = period + 1; i < values.length; i++) {
    const change = values[i]! - values[i - 1]!;
    const gain = change > 0 ? change : 0;
    const loss = change < 0 ? -change : 0;
    avgGain = (avgGain * (period - 1) + gain) / period;
    avgLoss = (avgLoss * (period - 1) + loss) / period;
    result[i] = computeRsiFromAverages(avgGain, avgLoss);
  }
  return result;
}

function computeRsiFromAverages(avgGain: number, avgLoss: number): number {
  if (avgLoss === 0) return 100;
  const rs = avgGain / avgLoss;
  return 100 - 100 / (1 + rs);
}

interface BuildBollingerOverlayInput {
  closes: number[];
  midXs: number[];
  yForPrice: (price: number) => number;
}

function buildBollingerOverlay({ closes, midXs, yForPrice }: BuildBollingerOverlayInput): CandlestickBollingerOverlay | null {
  const period = 20;
  const multiplier = 2;
  const bands = computeBollingerBands(closes, period, multiplier);

  const upperPts: string[] = [];
  const lowerPts: string[] = [];
  const middlePts: string[] = [];
  const upperPathSegments: string[] = [];
  const lowerPathSegmentsReversed: string[] = [];

  for (let i = 0; i < bands.length; i++) {
    const band = bands[i];
    if (!band) continue;
    const x = midXs[i]!.toFixed(2);
    const upperY = yForPrice(band.upper).toFixed(2);
    const lowerY = yForPrice(band.lower).toFixed(2);
    const middleY = yForPrice(band.middle).toFixed(2);
    upperPts.push(`${x},${upperY}`);
    lowerPts.push(`${x},${lowerY}`);
    middlePts.push(`${x},${middleY}`);
    upperPathSegments.push(`${upperPathSegments.length === 0 ? "M" : "L"} ${x} ${upperY}`);
    lowerPathSegmentsReversed.unshift(`L ${x} ${lowerY}`);
  }

  if (upperPts.length === 0) return null;

  const bandPath = `${upperPathSegments.join(" ")} ${lowerPathSegmentsReversed.join(" ")} Z`;

  return {
    period,
    multiplier,
    label: `Bollinger (${period}, ${multiplier})`,
    ariaLabel: `${period}-period Bollinger Bands with ${multiplier} standard deviations`,
    stroke: "var(--chart-bollinger, #38bdf8)",
    upperPoints: upperPts.join(" "),
    lowerPoints: lowerPts.join(" "),
    middlePoints: middlePts.join(" "),
    bandPath
  };
}

interface BuildRsiPanelInput {
  closes: number[];
  midXs: number[];
  areaTop: number;
  areaHeight: number;
  padX: number;
  width: number;
}

function buildRsiPanel({
  closes,
  midXs,
  areaTop,
  areaHeight,
  padX,
  width
}: BuildRsiPanelInput): CandlestickRsiPanel | null {
  const period = 14;
  if (areaHeight <= 0) return null;

  const values = computeRsi(closes, period);
  const yForRsi = (rsi: number): number => areaTop + (1 - rsi / 100) * areaHeight;

  const pts: string[] = [];
  let lastValue: number | null = null;
  for (let i = 0; i < values.length; i++) {
    const v = values[i];
    if (v === null || !Number.isFinite(v)) continue;
    pts.push(`${midXs[i]!.toFixed(2)},${yForRsi(v).toFixed(2)}`);
    lastValue = v;
  }

  if (pts.length === 0) return null;

  const overboughtY = yForRsi(70);
  const oversoldY = yForRsi(30);
  const midlineY = yForRsi(50);
  const guideX1 = padX;
  const guideX2 = width - padX;
  const lastValueTone: CandlestickRsiPanel["lastValueTone"] =
    lastValue === null
      ? "neutral"
      : lastValue >= 70
        ? "overbought"
        : lastValue <= 30
          ? "oversold"
          : "neutral";

  return {
    period,
    label: `RSI ${period}`,
    ariaLabel: `${period}-period Relative Strength Index sub-panel`,
    areaTop,
    areaHeight,
    points: pts.join(" "),
    overboughtY,
    oversoldY,
    midlineY,
    guideX1,
    guideX2,
    overboughtLabel: { value: "70", x: width - padX, y: Math.max(overboughtY - 2, areaTop + 9) },
    oversoldLabel: { value: "30", x: width - padX, y: Math.min(oversoldY + 9, areaTop + areaHeight - 2) },
    lastValue,
    lastValueLabel: lastValue === null ? "-" : lastValue.toFixed(1),
    lastValueTone,
    lastValueY: lastValue === null ? null : yForRsi(lastValue)
  };
}

function formatBarTimeLabel(start: string): string {
  const date = new Date(start);
  if (Number.isNaN(date.getTime())) return start;
  return date.toLocaleString();
}

function formatChangeWithPct(change: number | null, pct: number | null): string {
  if (change === null || !Number.isFinite(change)) return "-";
  const sign = change > 0 ? "+" : change < 0 ? "" : "";
  const changeText = `${sign}${change.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 4 })}`;
  if (pct === null || !Number.isFinite(pct)) return changeText;
  const pctSign = pct > 0 ? "+" : pct < 0 ? "" : "";
  return `${changeText} (${pctSign}${pct.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}%)`;
}

function candleChangeTone(change: number | null): CandlestickChangeTone {
  if (change === null || !Number.isFinite(change) || change === 0) return "neutral";
  return change > 0 ? "positive" : "negative";
}

function formatPriceRaw(value: number): string {
  return value.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 4 });
}

export function buildHistoricalChartStatePanel({
  status,
  bars,
  symbol,
  activeTimeframe,
  errorMessage
}: {
  status: FetchStatus;
  bars: readonly HistoricalBarPoint[];
  symbol: string;
  activeTimeframe: HistoricalChartTimeframe;
  errorMessage: string | null;
}): HistoricalChartStatePanel | null {
  if (!symbol) {
    return {
      kind: "idle",
      role: "status",
      ariaLive: "polite",
      title: "Select a symbol",
      detail: "Choose a live quote symbol to load stored OHLCV bars.",
      retryLabel: null,
      retryAriaLabel: null,
      retryBusy: false,
      retryDisabled: true
    };
  }

  if (status === "loading") {
    return {
      kind: "loading",
      role: "status",
      ariaLive: "polite",
      title: `Loading ${activeTimeframe.label} bars`,
      detail: `Loading ${activeTimeframe.label} bars for ${symbol}.`,
      retryLabel: null,
      retryAriaLabel: null,
      retryBusy: true,
      retryDisabled: true
    };
  }

  if (status === "error") {
    return {
      kind: "error",
      role: "alert",
      ariaLive: "assertive",
      title: "Historical bars unavailable",
      detail: errorMessage ?? "Failed to load historical bars.",
      retryLabel: "Retry",
      retryAriaLabel: `Retry loading ${activeTimeframe.label} historical bars for ${symbol}`,
      retryBusy: false,
      retryDisabled: false
    };
  }

  if (status === "ready" && bars.length === 0) {
    return {
      kind: "empty",
      role: "status",
      ariaLive: "polite",
      title: "No stored bars",
      detail: `No stored trades found for ${symbol} over the last ${activeTimeframe.lookbackDays}d. Backfill historical data or stream live trades to populate the chart.`,
      retryLabel: "Check again",
      retryAriaLabel: `Check again for ${activeTimeframe.label} historical bars for ${symbol}`,
      retryBusy: false,
      retryDisabled: false
    };
  }

  return null;
}

export function buildHistoricalChartSparklineViewModel({
  bars,
  stats,
  symbol,
  timeframe
}: {
  bars: readonly HistoricalBarPoint[];
  stats: HistoricalChartStats;
  symbol: string;
  timeframe: string;
}): HistoricalChartSparklineViewModel | null {
  const width = 900;
  const height = 220;
  const padX = 12;
  const padY = 16;

  if (bars.length === 0 || stats.high === null || stats.low === null) {
    return null;
  }

  const chartBars = toChronologicalChartBars(bars);

  if (chartBars.length === 0) {
    return null;
  }

  const startMs = chartBars[0]!.timestamp;
  const endMs = chartBars[chartBars.length - 1]!.timestamp;
  const tsSpan = Math.max(1, endMs - startMs);
  const priceSpan = Math.max(stats.high - stats.low, Math.max(stats.high * 0.0005, 0.01));
  const xFor = (ms: number) => padX + ((ms - startMs) / tsSpan) * (width - padX * 2);
  const yFor = (price: number) => padY + (1 - (price - stats.low!) / priceSpan) * (height - padY * 2);

  const points = chartBars.map(({ bar, timestamp }) => `${xFor(timestamp).toFixed(2)},${yFor(bar.close).toFixed(2)}`).join(" ");
  const last = chartBars[chartBars.length - 1]!;
  const lastPoint = {
    x: xFor(last.timestamp),
    y: yFor(last.bar.close)
  };
  const baseY = (height - padY).toFixed(2);
  const areaSegments = [`M ${xFor(chartBars[0]!.timestamp).toFixed(2)} ${baseY}`];

  for (const { bar, timestamp } of chartBars) {
    areaSegments.push(`L ${xFor(timestamp).toFixed(2)} ${yFor(bar.close).toFixed(2)}`);
  }

  areaSegments.push(`L ${lastPoint.x.toFixed(2)} ${baseY} Z`);

  return {
    width,
    height,
    viewBox: `0 0 ${width} ${height}`,
    ariaLabel: `${symbol} ${timeframe} closing prices, ${chartBars.length} bars from ${formatPrice(stats.low)} to ${formatPrice(stats.high)}.`,
    highGuideY: yFor(stats.high),
    lowGuideY: yFor(stats.low),
    guideX1: padX,
    guideX2: width - padX,
    areaPath: areaSegments.join(" "),
    points,
    lastPoint,
    highLabel: {
      value: formatPrice(stats.high),
      x: width - padX,
      y: Math.max(yFor(stats.high) - 4, 12)
    },
    lowLabel: {
      value: formatPrice(stats.low),
      x: width - padX,
      y: Math.min(yFor(stats.low) + 12, height - 4)
    },
    stroke: chartStroke(stats.change)
  };
}

export function computeChartStats(bars: readonly HistoricalBarPoint[]): HistoricalChartStats {
  const chartBars = toChronologicalChartBars(bars);

  if (chartBars.length === 0) {
    return {
      open: null,
      high: null,
      low: null,
      last: null,
      vwap: null,
      volume: 0,
      change: null,
      changePct: null
    };
  }

  const open = chartBars[0]!.bar.open;
  const last = chartBars[chartBars.length - 1]!.bar.close;
  let high = -Infinity;
  let low = Infinity;
  let volume = 0;
  let pxVolume = 0;

  for (const { bar } of chartBars) {
    if (bar.high > high) high = bar.high;
    if (bar.low < low) low = bar.low;
    if (Number.isFinite(bar.volume) && bar.volume > 0) {
      volume += bar.volume;
      const vw = Number.isFinite(bar.vwap) && bar.vwap > 0 ? bar.vwap : bar.close;
      pxVolume += vw * bar.volume;
    }
  }

  const change = last - open;
  const changePct = open !== 0 ? (change / open) * 100 : null;
  const vwap = volume > 0 ? pxVolume / volume : null;

  return {
    open,
    high: Number.isFinite(high) ? high : null,
    low: Number.isFinite(low) ? low : null,
    last,
    vwap,
    volume,
    change,
    changePct
  };
}

function toChronologicalChartBars(bars: readonly HistoricalBarPoint[]) {
  return bars
    .map((bar) => ({ bar, timestamp: new Date(bar.start).getTime() }))
    .filter((entry) =>
      Number.isFinite(entry.timestamp) &&
      Number.isFinite(entry.bar.open) &&
      Number.isFinite(entry.bar.high) &&
      Number.isFinite(entry.bar.low) &&
      Number.isFinite(entry.bar.close)
    )
    .sort((left, right) => left.timestamp - right.timestamp);
}

export function formatIntervalLabel(intervalMinutes: number): string {
  if (intervalMinutes < 60) return `${intervalMinutes}m`;
  if (intervalMinutes === 60) return "1h";
  if (intervalMinutes < 1440) return `${intervalMinutes / 60}h`;
  if (intervalMinutes === 1440) return "1d";
  return `${(intervalMinutes / 1440).toFixed(1)}d`;
}

function formatDateForRequest(date: Date): string {
  return date.toISOString().slice(0, 10);
}

function chartStroke(change: number | null): string {
  if (change === null) return "var(--chart-bench)";
  if (change > 0) return "var(--chart-up)";
  if (change < 0) return "var(--chart-dn)";
  return "var(--chart-bench)";
}

function chartChangeToneClass(change: number | null): string {
  if (change === null) return "text-foreground";
  if (change > 0) return "text-success";
  if (change < 0) return "text-danger";
  return "text-foreground";
}

function formatPrice(value: number | null | undefined): string {
  if (value === null || value === undefined || !Number.isFinite(value)) return "-";
  return value.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 4 });
}

function formatChange(value: number | null): string {
  if (value === null || !Number.isFinite(value)) return "-";
  const sign = value > 0 ? "+" : "";
  return `${sign}${value.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 4 })}`;
}

function formatChangePct(value: number | null): string {
  if (value === null || !Number.isFinite(value)) return "-";
  const sign = value > 0 ? "+" : "";
  return `${sign}${value.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}%`;
}

function formatVolume(value: number): string {
  if (!Number.isFinite(value) || value <= 0) return "-";
  if (value >= 1_000_000) return `${(value / 1_000_000).toFixed(2)}M`;
  if (value >= 1_000) return `${(value / 1_000).toFixed(1)}K`;
  return value.toLocaleString();
}
