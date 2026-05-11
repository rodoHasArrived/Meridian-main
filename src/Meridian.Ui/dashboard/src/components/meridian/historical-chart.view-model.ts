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
}

export interface ChartModeOption {
  mode: ChartMode;
  label: string;
  ariaLabel: string;
  ariaPressed: boolean;
  buttonVariant: "default" | "outline";
  select: () => void;
}

export interface HistoricalChartViewModel {
  eyebrow: string;
  title: string;
  description: string;
  activeTimeframeLabel: string;
  timeframeOptions: HistoricalChartTimeframeOption[];
  chartModeOptions: ChartModeOption[];
  activeChartMode: ChartMode;
  lastPriceText: string;
  changeText: string;
  changeToneClass: string;
  statePanel: HistoricalChartStatePanel | null;
  chart: HistoricalChartSparklineViewModel | null;
  candlestickChart: CandlestickChartViewModel | null;
  statTiles: HistoricalChartStatTile[];
  retry: () => void;
}

export function useHistoricalChartViewModel(symbol: string): HistoricalChartViewModel {
  const [activeTimeframe, setActiveTimeframe] = useState<HistoricalChartTimeframe>(
    HISTORICAL_CHART_TIMEFRAMES[0]!
  );
  const [state, setState] = useState<FetchState>(initialFetchState);
  const [chartMode, setChartMode] = useState<ChartMode>("candles");
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
      timeframe: activeTimeframe.label
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
  timeframe
}: {
  bars: readonly HistoricalBarPoint[];
  stats: HistoricalChartStats;
  symbol: string;
  timeframe: string;
}): CandlestickChartViewModel | null {
  const width = 900;
  const priceAreaHeight = 175;
  const volumeGap = 8;
  const volumeAreaHeight = 35;
  const totalHeight = priceAreaHeight + volumeGap + volumeAreaHeight;
  const padX = 12;
  const padY = 14;
  const volumeAreaTop = priceAreaHeight + volumeGap;

  if (bars.length === 0 || stats.high === null || stats.low === null) {
    return null;
  }

  const chartBars = toChronologicalChartBars(bars);
  if (chartBars.length === 0) return null;

  const n = chartBars.length;
  const slotWidth = (width - padX * 2) / n;
  const bodyWidth = Math.max(1.5, slotWidth * 0.65);

  const priceSpan = Math.max(stats.high - stats.low, Math.max(stats.high * 0.001, 0.01));
  const plotH = priceAreaHeight - padY * 2;
  const yForPrice = (price: number): number =>
    padY + (1 - (price - stats.low!) / priceSpan) * plotH;

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
      tooltipLabel: `${new Date(bar.start).toLocaleString()} · O ${formatPriceRaw(bar.open)} H ${formatPriceRaw(bar.high)} L ${formatPriceRaw(bar.low)} C ${formatPriceRaw(bar.close)}`
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
    volumeAreaTop
  };
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
