import { useMemo } from "react";
import type { QuantPlot, QuantPlotPoint, QuantPlotSeries } from "@/types";

const WIDTH = 480;
const HEIGHT = 220;
const PADDING_LEFT = 44;
const PADDING_RIGHT = 12;
const PADDING_TOP = 12;
const PADDING_BOTTOM = 28;
const PLOT_WIDTH = WIDTH - PADDING_LEFT - PADDING_RIGHT;
const PLOT_HEIGHT = HEIGHT - PADDING_TOP - PADDING_BOTTOM;

const SERIES_COLORS = ["#60a5fa", "#34d399", "#f59e0b", "#f472b6", "#a78bfa", "#22d3ee"] as const;

interface Bounds {
  min: number;
  max: number;
}

function safeBounds(values: number[]): Bounds {
  if (values.length === 0) return { min: 0, max: 1 };
  let min = Number.POSITIVE_INFINITY;
  let max = Number.NEGATIVE_INFINITY;
  for (const v of values) {
    if (!Number.isFinite(v)) continue;
    if (v < min) min = v;
    if (v > max) max = v;
  }
  if (!Number.isFinite(min) || !Number.isFinite(max)) return { min: 0, max: 1 };
  if (min === max) {
    const pad = Math.abs(min) > 0 ? Math.abs(min) * 0.1 : 1;
    return { min: min - pad, max: max + pad };
  }
  return { min, max };
}

function formatTickValue(value: number): string {
  if (!Number.isFinite(value)) return "—";
  const abs = Math.abs(value);
  if (abs >= 1000) return value.toLocaleString(undefined, { maximumFractionDigits: 0 });
  if (abs >= 1) return value.toFixed(2);
  return value.toFixed(3);
}

function buildLinePath(points: { x: number; y: number }[]): string {
  if (points.length === 0) return "";
  let path = `M ${points[0].x.toFixed(2)} ${points[0].y.toFixed(2)}`;
  for (let i = 1; i < points.length; i++) {
    path += ` L ${points[i].x.toFixed(2)} ${points[i].y.toFixed(2)}`;
  }
  return path;
}

function buildAreaPath(points: { x: number; y: number }[], baselineY: number): string {
  if (points.length === 0) return "";
  let path = `M ${points[0].x.toFixed(2)} ${baselineY.toFixed(2)}`;
  for (const p of points) {
    path += ` L ${p.x.toFixed(2)} ${p.y.toFixed(2)}`;
  }
  path += ` L ${points[points.length - 1].x.toFixed(2)} ${baselineY.toFixed(2)} Z`;
  return path;
}

function cumulativeReturn(values: number[]): number[] {
  if (values.length === 0) return [];
  const base = values[0];
  if (!Number.isFinite(base) || base === 0) return values.map(() => 0);
  return values.map((v) => v / base - 1);
}

function drawdown(values: number[]): number[] {
  const out: number[] = [];
  let peak = Number.NEGATIVE_INFINITY;
  for (const v of values) {
    if (v > peak) peak = v;
    out.push(peak === 0 ? 0 : v / peak - 1);
  }
  return out;
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

interface QuantPlotChartProps {
  plot: QuantPlot;
}

export function QuantPlotChart({ plot }: QuantPlotChartProps) {
  const renderable = useMemo(() => prepareSeries(plot), [plot]);

  if (renderable.kind === "unsupported") {
    return (
      <div className="rounded-md border border-border/60 bg-secondary/20 px-3 py-3 text-xs text-muted-foreground">
        <div className="font-semibold text-foreground">{plot.title}</div>
        <div className="mt-1">Plot type "{plot.type}" not yet rendered in browser. Data is available via the API response.</div>
      </div>
    );
  }

  if (renderable.kind === "histogram") {
    return <HistogramChart plot={plot} bins={renderable.bins} domain={renderable.domain} bounds={renderable.bounds} />;
  }

  if (renderable.kind === "bar") {
    return <BarChart plot={plot} series={renderable.series} bounds={renderable.bounds} />;
  }

  if (renderable.kind === "scatter") {
    return <ScatterChart plot={plot} points={renderable.points} bounds={renderable.bounds} />;
  }

  return <LineChart plot={plot} series={renderable.series} bounds={renderable.bounds} fillBaselineZero={renderable.fillBaseline} />;
}

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
  domain: Bounds;
  bounds: Bounds;
}

type PreparedPlot =
  | PreparedLine
  | PreparedBar
  | PreparedScatter
  | PreparedHistogram
  | { kind: "unsupported" };

export function prepareSeries(plot: QuantPlot): PreparedPlot {
  switch (plot.type) {
    case "Line":
    case "MultiLine":
    case "CumulativeReturn":
    case "Drawdown": {
      const series = plot.multiSeries
        ?? (plot.series ? [{ label: plot.title || "Series", values: plot.series }] : []);
      if (series.length === 0 || series.every((s) => s.values.length === 0)) {
        return { kind: "unsupported" };
      }

      const transformed = series.map((s) => {
        const values = s.values.map((p) => p.value);
        const xs = s.values.map((p) => p.date);
        let ys = values;
        if (plot.type === "CumulativeReturn") {
          ys = cumulativeReturn(values);
        } else if (plot.type === "Drawdown") {
          ys = drawdown(values);
        }
        return { label: s.label, values: ys.map((y, i) => ({ date: xs[i], value: y })) };
      });

      const allValues = transformed.flatMap((s) => s.values.map((p) => p.value));
      return {
        kind: "line",
        series: transformed,
        bounds: safeBounds(allValues),
        fillBaseline: plot.type === "Drawdown" || plot.type === "CumulativeReturn"
      };
    }
    case "Bar": {
      const points = plot.series ?? [];
      if (points.length === 0) return { kind: "unsupported" };
      return { kind: "bar", series: points, bounds: safeBounds(points.map((p) => p.value)) };
    }
    case "Scatter": {
      const points = plot.series ?? [];
      if (points.length === 0) return { kind: "unsupported" };
      return { kind: "scatter", points, bounds: safeBounds(points.map((p) => p.value)) };
    }
    case "Histogram": {
      const points = plot.series ?? [];
      const values = points.map((p) => p.value).filter((v) => Number.isFinite(v));
      if (values.length === 0) return { kind: "unsupported" };
      const bins = histogramBins(values, 12);
      return {
        kind: "histogram",
        bins,
        domain: safeBounds(values),
        bounds: safeBounds(bins.map((b) => b.count))
      };
    }
    default:
      return { kind: "unsupported" };
  }
}

export function histogramBins(values: number[], count: number): { lo: number; hi: number; count: number }[] {
  const filtered = values.filter((v) => Number.isFinite(v));
  if (filtered.length === 0) return [];
  let min = Math.min(...filtered);
  let max = Math.max(...filtered);
  if (min === max) {
    min -= 0.5;
    max += 0.5;
  }
  const width = (max - min) / count;
  const bins: { lo: number; hi: number; count: number }[] = [];
  for (let i = 0; i < count; i++) {
    bins.push({ lo: min + i * width, hi: min + (i + 1) * width, count: 0 });
  }
  for (const v of filtered) {
    const idx = Math.min(count - 1, Math.max(0, Math.floor((v - min) / width)));
    bins[idx].count += 1;
  }
  return bins;
}

interface ChartFrameProps {
  title: string;
  legend?: { label: string; color: string }[];
  children: React.ReactNode;
}

function ChartFrame({ title, legend, children }: ChartFrameProps) {
  return (
    <figure className="rounded-md border border-border/60 bg-secondary/15 p-3">
      <figcaption className="mb-2 flex flex-wrap items-baseline justify-between gap-2 text-xs">
        <span className="font-semibold text-foreground">{title}</span>
        {legend && legend.length > 0 ? (
          <div className="flex flex-wrap gap-x-3 gap-y-1 text-muted-foreground">
            {legend.map((entry) => (
              <span key={entry.label} className="inline-flex items-center gap-1.5">
                <span aria-hidden="true" className="inline-block h-2 w-2 rounded-sm" style={{ backgroundColor: entry.color }} />
                {entry.label}
              </span>
            ))}
          </div>
        ) : null}
      </figcaption>
      <svg
        role="img"
        aria-label={title}
        viewBox={`0 0 ${WIDTH} ${HEIGHT}`}
        className="h-auto w-full"
        preserveAspectRatio="xMidYMid meet"
      >
        {children}
      </svg>
    </figure>
  );
}

function YAxis({ bounds, format = formatTickValue, ticks = 4 }: { bounds: Bounds; format?: (v: number) => string; ticks?: number }) {
  const lines: React.ReactNode[] = [];
  for (let i = 0; i <= ticks; i++) {
    const value = bounds.min + ((bounds.max - bounds.min) * i) / ticks;
    const y = chartYFor(value, bounds);
    lines.push(
      <g key={i}>
        <line x1={PADDING_LEFT} x2={WIDTH - PADDING_RIGHT} y1={y} y2={y} stroke="rgba(148,163,184,0.18)" strokeWidth={1} />
        <text x={PADDING_LEFT - 6} y={y + 3} fontSize={10} fill="rgba(148,163,184,0.85)" textAnchor="end">{format(value)}</text>
      </g>
    );
  }
  return <>{lines}</>;
}

function LineChart({ plot, series, bounds, fillBaselineZero }: { plot: QuantPlot; series: QuantPlotSeries[]; bounds: Bounds; fillBaselineZero: boolean }) {
  const legend = series.map((s, idx) => ({ label: s.label, color: SERIES_COLORS[idx % SERIES_COLORS.length] }));
  const baselineY = fillBaselineZero
    ? chartYFor(Math.max(bounds.min, 0), bounds)
    : null;

  return (
    <ChartFrame title={plot.title} legend={legend.length > 1 ? legend : undefined}>
      <YAxis bounds={bounds} />
      {series.map((s, idx) => {
        const points = s.values.map((p, i) => ({
          x: chartXFor(i, s.values.length),
          y: chartYFor(p.value, bounds)
        }));
        const color = SERIES_COLORS[idx % SERIES_COLORS.length];
        return (
          <g key={s.label}>
            {baselineY !== null ? (
              <path d={buildAreaPath(points, baselineY)} fill={color} fillOpacity={0.18} />
            ) : null}
            <path d={buildLinePath(points)} fill="none" stroke={color} strokeWidth={1.5} strokeLinejoin="round" strokeLinecap="round" />
          </g>
        );
      })}
      <line
        x1={PADDING_LEFT}
        x2={WIDTH - PADDING_RIGHT}
        y1={chartYFor(0, bounds)}
        y2={chartYFor(0, bounds)}
        stroke="rgba(148,163,184,0.45)"
        strokeWidth={0.75}
        strokeDasharray="2 3"
      />
    </ChartFrame>
  );
}

function BarChart({ plot, series, bounds }: { plot: QuantPlot; series: QuantPlotPoint[]; bounds: Bounds }) {
  const barWidth = Math.max(2, PLOT_WIDTH / Math.max(1, series.length) - 2);
  return (
    <ChartFrame title={plot.title}>
      <YAxis bounds={bounds} />
      {series.map((point, i) => {
        const x = chartXFor(i, series.length) - barWidth / 2;
        const y = chartYFor(Math.max(0, point.value), bounds);
        const baseY = chartYFor(0, bounds);
        const top = Math.min(y, baseY);
        const height = Math.abs(baseY - chartYFor(point.value, bounds));
        return (
          <rect
            key={`${point.date}-${i}`}
            x={x}
            y={top}
            width={barWidth}
            height={Math.max(1, height)}
            fill={point.value >= 0 ? "#60a5fa" : "#f87171"}
            fillOpacity={0.7}
          />
        );
      })}
    </ChartFrame>
  );
}

function ScatterChart({ plot, points, bounds }: { plot: QuantPlot; points: QuantPlotPoint[]; bounds: Bounds }) {
  return (
    <ChartFrame title={plot.title}>
      <YAxis bounds={bounds} />
      {points.map((p, i) => (
        <circle
          key={`${p.date}-${i}`}
          cx={chartXFor(i, points.length)}
          cy={chartYFor(p.value, bounds)}
          r={2.4}
          fill="#60a5fa"
          fillOpacity={0.85}
        />
      ))}
    </ChartFrame>
  );
}

function HistogramChart({
  plot,
  bins,
  bounds
}: {
  plot: QuantPlot;
  bins: { lo: number; hi: number; count: number }[];
  domain: Bounds;
  bounds: Bounds;
}) {
  const barWidth = Math.max(2, PLOT_WIDTH / Math.max(1, bins.length) - 2);
  const baseY = chartYFor(bounds.min, bounds);
  return (
    <ChartFrame title={plot.title}>
      <YAxis bounds={bounds} format={(v) => Math.round(v).toString()} />
      {bins.map((bin, i) => {
        const x = PADDING_LEFT + (PLOT_WIDTH * i) / bins.length + 1;
        const y = chartYFor(bin.count, bounds);
        return (
          <g key={i}>
            <rect
              x={x}
              y={y}
              width={barWidth}
              height={Math.max(1, baseY - y)}
              fill="#a78bfa"
              fillOpacity={0.75}
            />
            <title>{`${bin.lo.toFixed(2)} - ${bin.hi.toFixed(2)}: ${bin.count}`}</title>
          </g>
        );
      })}
    </ChartFrame>
  );
}
