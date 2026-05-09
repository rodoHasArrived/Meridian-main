import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { AlertCircle, LineChart, Loader2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { getHistoricalBars } from "@/lib/api";
import type { HistoricalBarPoint } from "@/types";
import { cn } from "@/lib/utils";

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

interface HistoricalChartCardProps {
  symbol: string;
  className?: string;
}

interface FetchState {
  status: "idle" | "loading" | "ready" | "error";
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

export function HistoricalChartCard({ symbol, className }: HistoricalChartCardProps) {
  const [activeTimeframe, setActiveTimeframe] = useState<HistoricalChartTimeframe>(
    HISTORICAL_CHART_TIMEFRAMES[0]!
  );
  const [state, setState] = useState<FetchState>(initialFetchState);
  const inFlight = useRef<{ symbol: string; timeframeId: string } | null>(null);

  const fetchBars = useCallback(async (sym: string, tf: HistoricalChartTimeframe) => {
    inFlight.current = { symbol: sym, timeframeId: tf.id };
    setState((prev) => ({ ...prev, status: "loading", errorMessage: null }));

    const today = new Date();
    const fromDate = new Date(today);
    fromDate.setDate(fromDate.getDate() - tf.lookbackDays);

    const fmt = (d: Date) => d.toISOString().slice(0, 10);

    try {
      const response = await getHistoricalBars(sym, {
        intervalMinutes: tf.intervalMinutes,
        from: fmt(fromDate),
        to: fmt(today),
        maxBars: 1500
      });

      if (
        inFlight.current?.symbol !== sym ||
        inFlight.current?.timeframeId !== tf.id
      ) {
        // A newer request superseded this one.
        return;
      }

      setState({
        status: "ready",
        bars: response.bars ?? [],
        errorMessage: null,
        appliedTimeframeId: tf.id
      });
    } catch (err) {
      if (
        inFlight.current?.symbol !== sym ||
        inFlight.current?.timeframeId !== tf.id
      ) {
        return;
      }
      setState({
        status: "error",
        bars: [],
        errorMessage: (err as Error)?.message ?? "Failed to load historical bars",
        appliedTimeframeId: tf.id
      });
    }
  }, []);

  useEffect(() => {
    if (!symbol) return;
    fetchBars(symbol, activeTimeframe);
  }, [symbol, activeTimeframe, fetchBars]);

  const stats = useMemo(() => computeChartStats(state.bars), [state.bars]);
  const stroke = stats.change === null
    ? "var(--meridian-chart-stroke, #94a3b8)"
    : stats.change > 0
      ? "var(--meridian-chart-positive, #10b981)"
      : stats.change < 0
        ? "var(--meridian-chart-danger, #ef4444)"
        : "var(--meridian-chart-stroke, #94a3b8)";
  const tone = stats.change === null
    ? "text-foreground"
    : stats.change > 0
      ? "text-positive"
      : stats.change < 0
        ? "text-danger"
        : "text-foreground";

  return (
    <Card className={className}>
      <CardHeader>
        <div className="flex flex-col gap-3 sm:flex-row sm:items-end sm:justify-between">
          <div>
            <div className="eyebrow-label">Historical price</div>
            <CardTitle className="flex items-center gap-2 text-base">
              <LineChart className="h-4 w-4 text-primary" aria-hidden="true" />
              {symbol} · {activeTimeframe.label}
            </CardTitle>
            <CardDescription>
              OHLCV bars aggregated from stored trade events. Each bar covers{" "}
              {formatIntervalLabel(activeTimeframe.intervalMinutes)}.
            </CardDescription>
          </div>
          <div className="flex flex-col items-start gap-1 sm:items-end">
            <span className="font-mono text-2xl text-foreground">
              {formatPrice(stats.last)}
            </span>
            <span className={cn("font-mono text-xs", tone)}>
              {formatChange(stats.change)} ({formatChangePct(stats.changePct)})
            </span>
          </div>
        </div>
        <div
          className="mt-3 flex flex-wrap gap-1"
          role="group"
          aria-label="Select chart timeframe"
        >
          {HISTORICAL_CHART_TIMEFRAMES.map((tf) => (
            <Button
              key={tf.id}
              size="sm"
              variant={tf.id === activeTimeframe.id ? "default" : "outline"}
              onClick={() => setActiveTimeframe(tf)}
              aria-pressed={tf.id === activeTimeframe.id}
              data-testid={`historical-chart-timeframe-${tf.id}`}
            >
              {tf.label}
            </Button>
          ))}
        </div>
      </CardHeader>
      <CardContent>
        {state.status === "loading" ? (
          <div className="flex items-center gap-2 text-sm text-muted-foreground">
            <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" />
            Loading {activeTimeframe.label} bars for {symbol}…
          </div>
        ) : state.status === "error" ? (
          <div className="flex items-start gap-2 rounded-md border border-danger/40 bg-danger/5 px-3 py-2 text-sm text-danger">
            <AlertCircle className="mt-0.5 h-4 w-4" aria-hidden="true" />
            <span>{state.errorMessage}</span>
          </div>
        ) : state.bars.length === 0 ? (
          <p className="text-sm text-muted-foreground">
            No stored trades found for {symbol} over the last{" "}
            {activeTimeframe.lookbackDays}d. Backfill historical data or stream live trades to populate the chart.
          </p>
        ) : (
          <div className="space-y-3">
            <BarChartSparkline
              bars={state.bars}
              stroke={stroke}
              high={stats.high}
              low={stats.low}
              symbol={symbol}
              timeframe={activeTimeframe.label}
            />
            <div className="grid grid-cols-2 gap-2 sm:grid-cols-5">
              <ChartStat label="Open" value={formatPrice(stats.open)} />
              <ChartStat label="High" value={formatPrice(stats.high)} />
              <ChartStat label="Low" value={formatPrice(stats.low)} />
              <ChartStat label="VWAP" value={formatPrice(stats.vwap)} />
              <ChartStat label="Volume" value={formatVolume(stats.volume)} />
            </div>
          </div>
        )}
      </CardContent>
    </Card>
  );
}

interface BarChartSparklineProps {
  bars: HistoricalBarPoint[];
  stroke: string;
  high: number | null;
  low: number | null;
  symbol: string;
  timeframe: string;
}

function BarChartSparkline({ bars, stroke, high, low, symbol, timeframe }: BarChartSparklineProps) {
  const width = 900;
  const height = 220;
  const padX = 12;
  const padY = 16;

  if (bars.length === 0 || high === null || low === null) {
    return null;
  }

  const startMs = new Date(bars[0]!.start).getTime();
  const endMs = new Date(bars[bars.length - 1]!.start).getTime();
  const tsSpan = Math.max(1, endMs - startMs);
  const priceSpan = Math.max(high - low, Math.max(high * 0.0005, 0.01));
  const xFor = (ms: number) => padX + ((ms - startMs) / tsSpan) * (width - padX * 2);
  const yFor = (price: number) => padY + (1 - (price - low) / priceSpan) * (height - padY * 2);

  const points = bars.map((b) => `${xFor(new Date(b.start).getTime()).toFixed(2)},${yFor(b.close).toFixed(2)}`).join(" ");
  const last = bars[bars.length - 1]!;
  const lastX = xFor(new Date(last.start).getTime());
  const lastY = yFor(last.close);
  const baseY = (height - padY).toFixed(2);

  const areaSegments = [`M ${xFor(new Date(bars[0]!.start).getTime()).toFixed(2)} ${baseY}`];
  for (const bar of bars) {
    areaSegments.push(`L ${xFor(new Date(bar.start).getTime()).toFixed(2)} ${yFor(bar.close).toFixed(2)}`);
  }
  areaSegments.push(`L ${lastX.toFixed(2)} ${baseY} Z`);
  const areaPath = areaSegments.join(" ");

  return (
    <svg
      viewBox={`0 0 ${width} ${height}`}
      preserveAspectRatio="none"
      className="block h-56 w-full overflow-visible"
      role="img"
      aria-label={`${symbol} ${timeframe} closing prices, ${bars.length} bars from ${formatPrice(low)} to ${formatPrice(high)}.`}
    >
      <line
        x1={padX}
        x2={width - padX}
        y1={yFor(high)}
        y2={yFor(high)}
        stroke="currentColor"
        strokeOpacity="0.15"
        strokeDasharray="4 4"
      />
      <line
        x1={padX}
        x2={width - padX}
        y1={yFor(low)}
        y2={yFor(low)}
        stroke="currentColor"
        strokeOpacity="0.15"
        strokeDasharray="4 4"
      />
      <path d={areaPath} fill={stroke} fillOpacity="0.12" stroke="none" />
      <polyline
        fill="none"
        stroke={stroke}
        strokeWidth="1.75"
        strokeLinejoin="round"
        strokeLinecap="round"
        points={points}
      />
      <circle cx={lastX} cy={lastY} r="3.25" fill={stroke} stroke="currentColor" strokeOpacity="0.4" strokeWidth="1" />
      <text
        x={width - padX}
        y={Math.max(yFor(high) - 4, 12)}
        textAnchor="end"
        fontFamily="IBM Plex Mono, ui-monospace"
        fontSize="10"
        fill="currentColor"
        fillOpacity="0.55"
      >
        {formatPrice(high)}
      </text>
      <text
        x={width - padX}
        y={Math.min(yFor(low) + 12, height - 4)}
        textAnchor="end"
        fontFamily="IBM Plex Mono, ui-monospace"
        fontSize="10"
        fill="currentColor"
        fillOpacity="0.55"
      >
        {formatPrice(low)}
      </text>
    </svg>
  );
}

function ChartStat({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-md border border-border/60 bg-secondary/25 px-2.5 py-1.5">
      <div className="text-[10px] uppercase tracking-wide text-muted-foreground">{label}</div>
      <div className="mt-0.5 font-mono text-sm text-foreground">{value}</div>
    </div>
  );
}

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

export function computeChartStats(bars: readonly HistoricalBarPoint[]): HistoricalChartStats {
  if (bars.length === 0) {
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

  const open = bars[0]!.open;
  const last = bars[bars.length - 1]!.close;
  let high = -Infinity;
  let low = Infinity;
  let volume = 0;
  let pxVolume = 0;

  for (const bar of bars) {
    if (bar.high > high) high = bar.high;
    if (bar.low < low) low = bar.low;
    if (bar.volume > 0) {
      volume += bar.volume;
      // Use bar VWAP if present, otherwise close price
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

function formatPrice(value: number | null | undefined): string {
  if (value === null || value === undefined || !Number.isFinite(value)) return "—";
  return value.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 4 });
}

function formatChange(value: number | null): string {
  if (value === null || !Number.isFinite(value)) return "—";
  const sign = value > 0 ? "+" : "";
  return `${sign}${value.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 4 })}`;
}

function formatChangePct(value: number | null): string {
  if (value === null || !Number.isFinite(value)) return "—";
  const sign = value > 0 ? "+" : "";
  return `${sign}${value.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}%`;
}

function formatVolume(value: number): string {
  if (!Number.isFinite(value) || value <= 0) return "—";
  if (value >= 1_000_000) return `${(value / 1_000_000).toFixed(2)}M`;
  if (value >= 1_000) return `${(value / 1_000).toFixed(1)}K`;
  return value.toLocaleString();
}

export function formatIntervalLabel(intervalMinutes: number): string {
  if (intervalMinutes < 60) return `${intervalMinutes}m`;
  if (intervalMinutes === 60) return `1h`;
  if (intervalMinutes < 1440) return `${intervalMinutes / 60}h`;
  if (intervalMinutes === 1440) return `1d`;
  return `${(intervalMinutes / 1440).toFixed(1)}d`;
}
