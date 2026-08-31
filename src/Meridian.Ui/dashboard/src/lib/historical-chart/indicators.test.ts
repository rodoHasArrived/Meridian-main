import { describe, expect, it } from "vitest";
import {
  CANDLESTICK_RSI_PERIOD,
  CANDLESTICK_SMA_PERIODS,
  computeCandlestickIndicators,
  computeSimpleMovingAverage,
  smaSeriesForPeriod
} from "./indicators";
import {
  buildCandlestickChartViewModel,
  computeChartStats,
  toChronologicalCloses
} from "@/components/meridian/historical-chart.view-model";
import type { HistoricalBarPoint } from "@/types";

function syntheticBars(count: number): HistoricalBarPoint[] {
  return Array.from({ length: count }, (_, i) => {
    const close = 100 + Math.sin(i / 3) * 8 + i * 0.4;
    const open = close - 0.6;
    return {
      start: new Date(Date.UTC(2026, 0, 1, 0, i)).toISOString(),
      open,
      high: close + 1.2,
      low: open - 1.2,
      close,
      volume: 1000 + i * 5,
      vwap: close,
      tradeCount: 10
    };
  });
}

describe("computeCandlestickIndicators", () => {
  it("computes SMA series for each configured period, plus bollinger and rsi", () => {
    const closes = syntheticBars(60).map((b) => b.close);
    const set = computeCandlestickIndicators(closes);

    expect(set.sma.map((s) => s.period)).toEqual([...CANDLESTICK_SMA_PERIODS]);
    expect(set.bollinger).toHaveLength(closes.length);
    expect(set.rsi).toHaveLength(closes.length);
    // SMA20 leads with 19 nulls then a running mean.
    expect(smaSeriesForPeriod(set, 20)).toEqual(computeSimpleMovingAverage(closes, 20));
    // RSI is null until the period warms up.
    expect(set.rsi.slice(0, CANDLESTICK_RSI_PERIOD).every((v) => v === null)).toBe(true);
  });

  it("smaSeriesForPeriod returns undefined for an unconfigured period", () => {
    const set = computeCandlestickIndicators([1, 2, 3, 4]);
    expect(smaSeriesForPeriod(set, 200)).toBeUndefined();
  });
});

describe("precomputed indicators are behaviorally identical to inline computation", () => {
  it("produces the same candlestick view-model with or without precomputed indicators", () => {
    const bars = syntheticBars(80);
    const stats = computeChartStats(bars);
    const indicators = { sma: true, bollinger: true, rsi: true };
    const precomputedIndicators = computeCandlestickIndicators(toChronologicalCloses(bars));

    const inline = buildCandlestickChartViewModel({ bars, stats, symbol: "AAPL", timeframe: "1m", indicators });
    const offThread = buildCandlestickChartViewModel({
      bars,
      stats,
      symbol: "AAPL",
      timeframe: "1m",
      indicators,
      precomputedIndicators
    });

    // The off-thread path must not change a single rendered pixel of geometry.
    expect(offThread).toEqual(inline);
    expect(offThread?.smaOverlays.length).toBeGreaterThan(0);
    expect(offThread?.bollingerOverlay).not.toBeNull();
    expect(offThread?.rsiPanel).not.toBeNull();
  });

  it("ignores stale precomputed series when the current bar set is shorter", () => {
    const priorBars = syntheticBars(80);
    const currentBars = syntheticBars(5);
    const stats = computeChartStats(currentBars);
    const indicators = { sma: true, bollinger: true, rsi: true };
    const staleIndicators = computeCandlestickIndicators(toChronologicalCloses(priorBars));

    const inline = buildCandlestickChartViewModel({
      bars: currentBars,
      stats,
      symbol: "AAPL",
      timeframe: "1m",
      indicators
    });
    const withStaleIndicators = buildCandlestickChartViewModel({
      bars: currentBars,
      stats,
      symbol: "AAPL",
      timeframe: "1m",
      indicators,
      precomputedIndicators: staleIndicators
    });

    expect(withStaleIndicators).toEqual(inline);
  });
});
