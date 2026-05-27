import type { QuantPlot, QuantPlotBar, QuantPlotPoint, QuantPlotSeries } from "@/types";

export const QUANT_PLOT_WIDTH = 480;
export const QUANT_PLOT_HEIGHT = 220;

const PADDING_LEFT = 44;
const PADDING_RIGHT = 12;
const PADDING_TOP = 12;
const PADDING_BOTTOM = 28;
const PLOT_WIDTH = QUANT_PLOT_WIDTH - PADDING_LEFT - PADDING_RIGHT;
const PLOT_HEIGHT = QUANT_PLOT_HEIGHT - PADDING_TOP - PADDING_BOTTOM;
const TICK_COUNT = 4;

const SERIES_COLORS = [
  "var(--chart-ma20)",
  "var(--chart-up)",
  "var(--chart-ma50)",
  "var(--chart-bench)",
  "var(--state-pending-fg)",
  "var(--chart-crosshair)"
] as const;

const GRID_STROKE = "var(--chart-grid)";
const AXIS_TEXT_FILL = "var(--fg-muted)";
const ZERO_LINE_STROKE = "var(--chart-grid-major)";
const NEGATIVE_FILL = "var(--chart-dn)";
const HEATMAP_STOPS = ["var(--chart-bench)", "var(--fg)", "var(--chart-dn)"] as const;

export interface Bounds {
  min: number;
  max: number;
}

export interface QuantPlotLegendItemViewModel {
  label: string;
  color: string;
}

export interface QuantPlotAxisTickViewModel {
  id: string;
  label: string;
  y: number;
  gridStroke: string;
  textFill: string;
}

export interface QuantPlotFrameViewModel {
  title: string;
  descriptionId: string;
  ariaLabel: string;
  description: string;
  viewBox: string;
  legend: QuantPlotLegendItemViewModel[];
}

export interface QuantPlotUnsupportedViewModel {
  kind: "unsupported";
  title: string;
  message: string;
  toneClassName: string;
}

export interface QuantPlotLineSeriesViewModel {
  id: string;
  label: string;
  color: string;
  linePath: string;
  areaPath: string | null;
  fillOpacity: number;
}

export interface QuantPlotLineViewModel {
  kind: "line";
  frame: QuantPlotFrameViewModel;
  axisTicks: QuantPlotAxisTickViewModel[];
  zeroLine: QuantPlotZeroLineViewModel;
  series: QuantPlotLineSeriesViewModel[];
}

export interface QuantPlotZeroLineViewModel {
  x1: number;
  x2: number;
  y1: number;
  y2: number;
  stroke: string;
  strokeWidth: number;
  strokeDasharray: string;
}

export interface QuantPlotBarRectViewModel {
  id: string;
  x: number;
  y: number;
  width: number;
  height: number;
  fill: string;
  fillOpacity: number;
}

export interface QuantPlotBarViewModel {
  kind: "bar";
  frame: QuantPlotFrameViewModel;
  axisTicks: QuantPlotAxisTickViewModel[];
  bars: QuantPlotBarRectViewModel[];
}

export interface QuantPlotScatterPointViewModel {
  id: string;
  cx: number;
  cy: number;
  r: number;
  fill: string;
  fillOpacity: number;
}

export interface QuantPlotScatterViewModel {
  kind: "scatter";
  frame: QuantPlotFrameViewModel;
  axisTicks: QuantPlotAxisTickViewModel[];
  points: QuantPlotScatterPointViewModel[];
}

export interface QuantPlotHistogramBarViewModel extends QuantPlotBarRectViewModel {
  title: string;
}

export interface QuantPlotHistogramViewModel {
  kind: "histogram";
  frame: QuantPlotFrameViewModel;
  axisTicks: QuantPlotAxisTickViewModel[];
  bars: QuantPlotHistogramBarViewModel[];
}

export interface QuantPlotCandlestickViewModel {
  kind: "candlestick";
  frame: QuantPlotFrameViewModel;
  axisTicks: QuantPlotAxisTickViewModel[];
  candles: QuantPlotCandleViewModel[];
}

export interface QuantPlotCandleViewModel {
  id: string;
  title: string;
  wick: {
    x1: number;
    y1: number;
    x2: number;
    y2: number;
    stroke: string;
    strokeWidth: number;
    opacity: number;
  };
  body: {
    x: number;
    y: number;
    width: number;
    height: number;
    fill: string;
    fillOpacity: number;
    stroke: string;
    strokeWidth: number;
  };
}

export interface QuantPlotHeatmapViewModel {
  kind: "heatmap";
  frame: QuantPlotFrameViewModel;
  cells: QuantPlotHeatmapCellViewModel[];
  xLabels: QuantPlotHeatmapLabelViewModel[];
  yLabels: QuantPlotHeatmapLabelViewModel[];
  scale: {
    gradientId: string;
    x: number;
    y: number;
    width: number;
    height: number;
    rx: number;
    stops: readonly string[];
  };
}

export interface QuantPlotHeatmapCellViewModel {
  id: string;
  title: string;
  x: number;
  y: number;
  width: number;
  height: number;
  fill: string;
  fillOpacity: number;
}

export interface QuantPlotHeatmapLabelViewModel {
  id: string;
  label: string;
  x: number;
  y: number;
  textAnchor: "middle" | "end";
  fontSize: number;
  fill: string;
}

export type QuantPlotViewModel =
  | QuantPlotUnsupportedViewModel
  | QuantPlotLineViewModel
  | QuantPlotBarViewModel
  | QuantPlotScatterViewModel
  | QuantPlotHistogramViewModel
  | QuantPlotCandlestickViewModel
  | QuantPlotHeatmapViewModel;

interface PreparedLine {
  kind: "line";
  series: QuantPlotSeries[];
  bounds: Bounds;
  fillBaseline: boolean;
}

interface PreparedBar {
  kind: "bar";
  series: QuantPlotPoint[];
  bounds: Bounds;
}

interface PreparedScatter {
  kind: "scatter";
  points: QuantPlotPoint[];
  bounds: Bounds;
}

interface PreparedHistogram {
  kind: "histogram";
  bins: { lo: number; hi: number; count: number }[];
  bounds: Bounds;
}

interface PreparedCandlestick {
  kind: "candlestick";
  bars: QuantPlotBar[];
  bounds: Bounds;
}

interface PreparedHeatmap {
  kind: "heatmap";
  data: number[][];
  labels: string[];
  bounds: Bounds;
}

export type PreparedPlot =
  | PreparedLine
  | PreparedBar
  | PreparedScatter
  | PreparedHistogram
  | PreparedCandlestick
  | PreparedHeatmap
  | { kind: "unsupported" };

export function buildQuantPlotViewModel(plot: QuantPlot): QuantPlotViewModel {
  const prepared = prepareSeries(plot);

  if (prepared.kind === "unsupported") {
    return {
      kind: "unsupported",
      title: plot.title,
      message: `Plot type "${plot.type}" is not yet renderable in the browser. Data remains available in the API response.`,
      toneClassName: "border-warning/30 bg-warning/10 text-warning"
    };
  }

  const frame = buildFrame(plot.title, prepared.kind === "line" ? buildLegend(prepared.series) : []);
  const zeroLine = buildZeroLine(prepared.bounds);

  switch (prepared.kind) {
    case "line": {
      const baselineY = prepared.fillBaseline ? chartYFor(Math.max(prepared.bounds.min, 0), prepared.bounds) : null;
      return {
        kind: "line",
        frame,
        axisTicks: buildAxisTicks(prepared.bounds),
        zeroLine,
        series: prepared.series.map((series, index) => {
          const points = series.values.map((point, pointIndex) => ({
            x: chartXFor(pointIndex, series.values.length),
            y: chartYFor(point.value, prepared.bounds)
          }));
          const color = colorForSeries(index);
          return {
            id: `${series.label}-${index}`,
            label: series.label,
            color,
            linePath: buildLinePath(points),
            areaPath: baselineY === null ? null : buildAreaPath(points, baselineY),
            fillOpacity: 0.18
          };
        })
      };
    }
    case "bar":
      return {
        kind: "bar",
        frame,
        axisTicks: buildAxisTicks(prepared.bounds),
        bars: buildBarRects(prepared.series, prepared.bounds)
      };
    case "scatter":
      return {
        kind: "scatter",
        frame,
        axisTicks: buildAxisTicks(prepared.bounds),
        points: prepared.points.map((point, index) => ({
          id: `${point.date}-${index}`,
          cx: chartXFor(index, prepared.points.length),
          cy: chartYFor(point.value, prepared.bounds),
          r: 2.4,
          fill: "var(--chart-bench)",
          fillOpacity: 0.85
        }))
      };
    case "histogram":
      return {
        kind: "histogram",
        frame,
        axisTicks: buildAxisTicks(prepared.bounds, (value) => Math.round(value).toString()),
        bars: buildHistogramRects(prepared.bins, prepared.bounds)
      };
    case "candlestick":
      return {
        kind: "candlestick",
        frame,
        axisTicks: buildAxisTicks(prepared.bounds),
        candles: buildCandles(prepared.bars, prepared.bounds)
      };
    case "heatmap":
      return buildHeatmapViewModel(frame, prepared.data, prepared.labels, prepared.bounds, plot.title);
  }
}

export function prepareSeries(plot: QuantPlot): PreparedPlot {
  switch (plot.type) {
    case "Line":
    case "MultiLine":
    case "CumulativeReturn":
    case "Drawdown": {
      const series = plot.multiSeries
        ?? (plot.series ? [{ label: plot.title || "Series", values: plot.series }] : []);
      if (series.length === 0 || series.every((item) => item.values.length === 0)) {
        return { kind: "unsupported" };
      }

      const transformed = series.map((item) => {
        const values = item.values.map((point) => point.value);
        const dates = item.values.map((point) => point.date);
        let outputValues = values;
        if (plot.type === "CumulativeReturn") {
          outputValues = cumulativeReturn(values);
        } else if (plot.type === "Drawdown") {
          outputValues = drawdown(values);
        }

        return { label: item.label, values: outputValues.map((value, index) => ({ date: dates[index], value })) };
      });

      return {
        kind: "line",
        series: transformed,
        bounds: safeBounds(transformed.flatMap((item) => item.values.map((point) => point.value))),
        fillBaseline: plot.type === "Drawdown" || plot.type === "CumulativeReturn"
      };
    }
    case "Bar": {
      const points = plot.series ?? [];
      if (points.length === 0) return { kind: "unsupported" };
      return { kind: "bar", series: points, bounds: safeBounds(points.map((point) => point.value)) };
    }
    case "Scatter": {
      const points = plot.series ?? [];
      if (points.length === 0) return { kind: "unsupported" };
      return { kind: "scatter", points, bounds: safeBounds(points.map((point) => point.value)) };
    }
    case "Histogram": {
      const points = plot.series ?? [];
      const values = points.map((point) => point.value).filter((value) => Number.isFinite(value));
      if (values.length === 0) return { kind: "unsupported" };
      const bins = histogramBins(values, 12);
      return { kind: "histogram", bins, bounds: safeBounds(bins.map((bin) => bin.count)) };
    }
    case "Candlestick": {
      const bars = plot.candlestick ?? [];
      if (bars.length === 0) return { kind: "unsupported" };
      const prices = bars.flatMap((bar) => [bar.open, bar.high, bar.low, bar.close]).filter(Number.isFinite);
      return { kind: "candlestick", bars, bounds: safeBounds(prices) };
    }
    case "Heatmap": {
      const data = plot.heatmapData ?? [];
      if (data.length === 0) return { kind: "unsupported" };
      const allValues = data.flat().filter(Number.isFinite);
      return { kind: "heatmap", data, labels: plot.heatmapLabels ?? [], bounds: safeBounds(allValues) };
    }
    default:
      return { kind: "unsupported" };
  }
}

export function histogramBins(values: number[], count: number): { lo: number; hi: number; count: number }[] {
  const filtered = values.filter((value) => Number.isFinite(value));
  if (filtered.length === 0) return [];

  let min = Math.min(...filtered);
  let max = Math.max(...filtered);
  if (min === max) {
    min -= 0.5;
    max += 0.5;
  }

  const width = (max - min) / count;
  const bins: { lo: number; hi: number; count: number }[] = [];
  for (let index = 0; index < count; index++) {
    bins.push({ lo: min + index * width, hi: min + (index + 1) * width, count: 0 });
  }

  for (const value of filtered) {
    const index = Math.min(count - 1, Math.max(0, Math.floor((value - min) / width)));
    bins[index].count += 1;
  }

  return bins;
}

function buildFrame(title: string, legend: QuantPlotLegendItemViewModel[]): QuantPlotFrameViewModel {
  const legendText = legend.length > 0 ? ` Series: ${legend.map((entry) => entry.label).join(", ")}.` : "";
  return {
    title,
    descriptionId: `quant-plot-${toPlotDomId(title)}-summary`,
    ariaLabel: title,
    description: `${title} chart rendered from Meridian run data.${legendText}`,
    viewBox: `0 0 ${QUANT_PLOT_WIDTH} ${QUANT_PLOT_HEIGHT}`,
    legend: legend.length > 1 ? legend : []
  };
}

function buildLegend(series: QuantPlotSeries[]): QuantPlotLegendItemViewModel[] {
  return series.map((item, index) => ({ label: item.label, color: colorForSeries(index) }));
}

function buildAxisTicks(bounds: Bounds, format = formatTickValue): QuantPlotAxisTickViewModel[] {
  const ticks: QuantPlotAxisTickViewModel[] = [];
  for (let index = 0; index <= TICK_COUNT; index++) {
    const value = bounds.min + ((bounds.max - bounds.min) * index) / TICK_COUNT;
    ticks.push({
      id: `tick-${index}`,
      label: format(value),
      y: chartYFor(value, bounds),
      gridStroke: GRID_STROKE,
      textFill: AXIS_TEXT_FILL
    });
  }
  return ticks;
}

function buildZeroLine(bounds: Bounds): QuantPlotZeroLineViewModel {
  const y = chartYFor(0, bounds);
  return {
    x1: PADDING_LEFT,
    x2: QUANT_PLOT_WIDTH - PADDING_RIGHT,
    y1: y,
    y2: y,
    stroke: ZERO_LINE_STROKE,
    strokeWidth: 0.75,
    strokeDasharray: "2 3"
  };
}

function buildBarRects(series: QuantPlotPoint[], bounds: Bounds): QuantPlotBarRectViewModel[] {
  const barWidth = Math.max(2, PLOT_WIDTH / Math.max(1, series.length) - 2);
  return series.map((point, index) => {
    const y = chartYFor(Math.max(0, point.value), bounds);
    const baseY = chartYFor(0, bounds);
    return {
      id: `${point.date}-${index}`,
      x: chartXFor(index, series.length) - barWidth / 2,
      y: Math.min(y, baseY),
      width: barWidth,
      height: Math.max(1, Math.abs(baseY - chartYFor(point.value, bounds))),
      fill: point.value >= 0 ? "var(--chart-bench)" : NEGATIVE_FILL,
      fillOpacity: 0.7
    };
  });
}

function buildHistogramRects(
  bins: { lo: number; hi: number; count: number }[],
  bounds: Bounds
): QuantPlotHistogramBarViewModel[] {
  const barWidth = Math.max(2, PLOT_WIDTH / Math.max(1, bins.length) - 2);
  const baseY = chartYFor(bounds.min, bounds);
  return bins.map((bin, index) => {
    const y = chartYFor(bin.count, bounds);
    return {
      id: `bin-${index}`,
      title: `${bin.lo.toFixed(2)} - ${bin.hi.toFixed(2)}: ${bin.count}`,
      x: PADDING_LEFT + (PLOT_WIDTH * index) / bins.length + 1,
      y,
      width: barWidth,
      height: Math.max(1, baseY - y),
      fill: "var(--state-pending-fg)",
      fillOpacity: 0.75
    };
  });
}

function buildCandles(bars: QuantPlotBar[], bounds: Bounds): QuantPlotCandleViewModel[] {
  const slotWidth = bars.length > 0 ? PLOT_WIDTH / bars.length : PLOT_WIDTH;
  const bodyWidth = Math.max(2, slotWidth * 0.6);

  return bars.map((bar, index) => {
    const cx = chartXFor(index, bars.length);
    const highY = chartYFor(bar.high, bounds);
    const lowY = chartYFor(bar.low, bounds);
    const openY = chartYFor(bar.open, bounds);
    const closeY = chartYFor(bar.close, bounds);
    const bullish = bar.close >= bar.open;
    const color = bullish ? "var(--chart-up)" : NEGATIVE_FILL;
    return {
      id: `${bar.date}-${index}`,
      title: `${bar.date}  O ${bar.open.toFixed(2)}  H ${bar.high.toFixed(2)}  L ${bar.low.toFixed(2)}  C ${bar.close.toFixed(2)}`,
      wick: { x1: cx, y1: highY, x2: cx, y2: lowY, stroke: color, strokeWidth: 1, opacity: 0.7 },
      body: {
        x: cx - bodyWidth / 2,
        y: Math.min(openY, closeY),
        width: bodyWidth,
        height: Math.max(1, Math.abs(closeY - openY)),
        fill: color,
        fillOpacity: bullish ? 0.85 : 0.75,
        stroke: color,
        strokeWidth: 0.5
      }
    };
  });
}

function buildHeatmapViewModel(
  frame: QuantPlotFrameViewModel,
  data: number[][],
  labels: string[],
  bounds: Bounds,
  title: string
): QuantPlotHeatmapViewModel {
  const rows = data.length;
  const cols = data[0]?.length ?? 0;
  const cellWidth = cols > 0 ? PLOT_WIDTH / cols : PLOT_WIDTH;
  const cellHeight = rows > 0 ? PLOT_HEIGHT / rows : PLOT_HEIGHT;
  const range = bounds.max - bounds.min;
  const xLabels = labels.length === cols ? labels : [];
  const yLabels = labels.length === rows ? labels : [];

  return {
    kind: "heatmap",
    frame,
    cells: data.flatMap((row, rowIndex) =>
      row.map((value, columnIndex) => {
        const normalized = range === 0 ? 0.5 : (value - bounds.min) / range;
        return {
          id: `${rowIndex}-${columnIndex}`,
          title: value.toFixed(4),
          x: PADDING_LEFT + columnIndex * cellWidth,
          y: PADDING_TOP + rowIndex * cellHeight,
          width: Math.max(1, cellWidth - 0.5),
          height: Math.max(1, cellHeight - 0.5),
          fill: heatmapToken(normalized),
          fillOpacity: heatmapOpacity(normalized)
        };
      })
    ),
    xLabels: xLabels.map((label, index) => ({
      id: `xl-${index}`,
      label: truncateHeatmapLabel(label),
      x: PADDING_LEFT + index * cellWidth + cellWidth / 2,
      y: QUANT_PLOT_HEIGHT - 6,
      fontSize: 9,
      fill: AXIS_TEXT_FILL,
      textAnchor: "middle"
    })),
    yLabels: yLabels.map((label, index) => ({
      id: `yl-${index}`,
      label: truncateHeatmapLabel(label),
      x: PADDING_LEFT - 4,
      y: PADDING_TOP + index * cellHeight + cellHeight / 2 + 3,
      fontSize: 9,
      fill: AXIS_TEXT_FILL,
      textAnchor: "end"
    })),
    scale: {
      gradientId: `heatmap-scale-${toPlotDomId(title)}`,
      x: PADDING_LEFT,
      y: QUANT_PLOT_HEIGHT - 5,
      width: PLOT_WIDTH,
      height: 3,
      rx: 1,
      stops: HEATMAP_STOPS
    }
  };
}

function safeBounds(values: number[]): Bounds {
  if (values.length === 0) return { min: 0, max: 1 };
  let min = Number.POSITIVE_INFINITY;
  let max = Number.NEGATIVE_INFINITY;
  for (const value of values) {
    if (!Number.isFinite(value)) continue;
    if (value < min) min = value;
    if (value > max) max = value;
  }
  if (!Number.isFinite(min) || !Number.isFinite(max)) return { min: 0, max: 1 };
  if (min === max) {
    const pad = Math.abs(min) > 0 ? Math.abs(min) * 0.1 : 1;
    return { min: min - pad, max: max + pad };
  }
  return { min, max };
}

function formatTickValue(value: number): string {
  if (!Number.isFinite(value)) return "-";
  const abs = Math.abs(value);
  if (abs >= 1000) return value.toLocaleString(undefined, { maximumFractionDigits: 0 });
  if (abs >= 1) return value.toFixed(2);
  return value.toFixed(3);
}

function buildLinePath(points: { x: number; y: number }[]): string {
  if (points.length === 0) return "";
  let path = `M ${points[0].x.toFixed(2)} ${points[0].y.toFixed(2)}`;
  for (let index = 1; index < points.length; index++) {
    path += ` L ${points[index].x.toFixed(2)} ${points[index].y.toFixed(2)}`;
  }
  return path;
}

function buildAreaPath(points: { x: number; y: number }[], baselineY: number): string {
  if (points.length === 0) return "";
  let path = `M ${points[0].x.toFixed(2)} ${baselineY.toFixed(2)}`;
  for (const point of points) {
    path += ` L ${point.x.toFixed(2)} ${point.y.toFixed(2)}`;
  }
  path += ` L ${points[points.length - 1].x.toFixed(2)} ${baselineY.toFixed(2)} Z`;
  return path;
}

function cumulativeReturn(values: number[]): number[] {
  if (values.length === 0) return [];
  const base = values[0];
  if (!Number.isFinite(base) || base === 0) return values.map(() => 0);
  return values.map((value) => value / base - 1);
}

function drawdown(values: number[]): number[] {
  const output: number[] = [];
  let peak = Number.NEGATIVE_INFINITY;
  for (const value of values) {
    if (value > peak) peak = value;
    output.push(peak === 0 ? 0 : value / peak - 1);
  }
  return output;
}

function chartXFor(index: number, total: number): number {
  if (total <= 1) return PADDING_LEFT + PLOT_WIDTH / 2;
  return PADDING_LEFT + (PLOT_WIDTH * index) / (total - 1);
}

function chartYFor(value: number, bounds: Bounds): number {
  const range = bounds.max - bounds.min;
  if (range === 0) return PADDING_TOP + PLOT_HEIGHT / 2;
  const ratio = (value - bounds.min) / range;
  return PADDING_TOP + PLOT_HEIGHT - ratio * PLOT_HEIGHT;
}

function colorForSeries(index: number): string {
  return SERIES_COLORS[index % SERIES_COLORS.length];
}

function heatmapToken(normalized: number): string {
  const value = Math.max(0, Math.min(1, normalized));
  if (value < 0.34) return "var(--chart-bench)";
  if (value > 0.66) return "var(--chart-dn)";
  return "var(--fg)";
}

function heatmapOpacity(normalized: number): number {
  const distanceFromMidpoint = Math.abs(Math.max(0, Math.min(1, normalized)) - 0.5) * 2;
  return 0.45 + distanceFromMidpoint * 0.4;
}

function truncateHeatmapLabel(label: string): string {
  return label.length > 6 ? `${label.slice(0, 5)}...` : label;
}

function toPlotDomId(value: string): string {
  const normalized = value.toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "");
  return normalized || "chart";
}
