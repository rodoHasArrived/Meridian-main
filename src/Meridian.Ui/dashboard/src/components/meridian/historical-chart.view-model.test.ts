import { act, renderHook, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { getHistoricalBars } from "@/lib/api";
import {
  COMPARE_SYMBOL_LIMIT,
  HISTORICAL_CHART_TIMEFRAMES,
  buildCandlestickChartViewModel,
  buildCompareChartViewModel,
  buildHistoricalChartSparklineViewModel,
  buildHistoricalChartStatePanel,
  computeBollingerBands,
  computeChartStats,
  computePercentChangeSeries,
  computeRsi,
  computeSimpleMovingAverage,
  useHistoricalChartViewModel
} from "@/components/meridian/historical-chart.view-model";
import type { HistoricalBarPoint } from "@/types";

vi.mock("@/lib/api", () => ({
  getHistoricalBars: vi.fn()
}));

function bar(start: string, open: number, high: number, low: number, close: number, volume: number): HistoricalBarPoint {
  return { start, open, high, low, close, volume, vwap: close, tradeCount: 1 };
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((promiseResolve, promiseReject) => {
    resolve = promiseResolve;
    reject = promiseReject;
  });

  return { promise, resolve, reject };
}

afterEach(() => {
  vi.mocked(getHistoricalBars).mockReset();
});

describe("buildHistoricalChartStatePanel", () => {
  it("owns retry copy and alert semantics for failed historical-bar loads", () => {
    const panel = buildHistoricalChartStatePanel({
      status: "error",
      bars: [],
      symbol: "AAPL",
      activeTimeframe: HISTORICAL_CHART_TIMEFRAMES[0]!,
      errorMessage: "backend offline"
    });

    expect(panel).toMatchObject({
      kind: "error",
      role: "alert",
      ariaLive: "assertive",
      title: "Historical bars unavailable",
      detail: "backend offline",
      retryLabel: "Retry",
      retryAriaLabel: "Retry loading 1D historical bars for AAPL",
      retryDisabled: false
    });
  });

  it("owns the no-bar recovery copy for an otherwise successful response", () => {
    const panel = buildHistoricalChartStatePanel({
      status: "ready",
      bars: [],
      symbol: "MSFT",
      activeTimeframe: HISTORICAL_CHART_TIMEFRAMES[2]!,
      errorMessage: null
    });

    expect(panel?.kind).toBe("empty");
    expect(panel?.role).toBe("status");
    expect(panel?.detail).toContain("No stored trades found for MSFT over the last 30d");
    expect(panel?.retryLabel).toBe("Check again");
  });
});

describe("buildHistoricalChartSparklineViewModel", () => {
  it("prepares SVG geometry and chart-token stroke outside the React view", () => {
    const bars = [
      bar("2026-05-01T14:30:00Z", 100, 101, 99, 100.5, 1000),
      bar("2026-05-01T14:35:00Z", 100.5, 103, 100, 102.25, 1200)
    ];
    const vm = buildHistoricalChartSparklineViewModel({
      bars,
      stats: computeChartStats(bars),
      symbol: "AAPL",
      timeframe: "1D"
    });

    expect(vm?.viewBox).toBe("0 0 900 220");
    expect(vm?.points).toContain(",");
    expect(vm?.areaPath).toMatch(/^M /);
    expect(vm?.stroke).toBe("var(--chart-up)");
    expect(vm?.ariaLabel).toContain("AAPL 1D closing prices, 2 bars");
  });

  it("drops malformed timestamps before generating SVG geometry", () => {
    const bars = [
      bar("not-a-date", 99, 100, 98, 99.5, 750),
      bar("2026-05-01T14:30:00Z", 100, 101, 99, 100.5, 1000),
      bar("2026-05-01T14:35:00Z", 100.5, 103, 100, 102.25, 1200)
    ];
    const vm = buildHistoricalChartSparklineViewModel({
      bars,
      stats: computeChartStats(bars),
      symbol: "AAPL",
      timeframe: "1D"
    });

    expect(vm?.points).not.toContain("NaN");
    expect(vm?.areaPath).not.toContain("NaN");
    expect(vm?.ariaLabel).toContain("AAPL 1D closing prices, 2 bars");
  });
});

describe("computeChartStats", () => {
  it("uses chronological bars for open, last, and change when providers return newest first", () => {
    const bars = [
      bar("2026-05-01T14:40:00Z", 102, 103, 101, 102.5, 900),
      bar("2026-05-01T14:30:00Z", 100, 101, 99, 100.5, 1000),
      bar("2026-05-01T14:35:00Z", 100.5, 102, 100, 101.75, 1200)
    ];
    const stats = computeChartStats(bars);

    expect(stats.open).toBe(100);
    expect(stats.last).toBe(102.5);
    expect(stats.change).toBeCloseTo(2.5, 5);
    expect(stats.changePct).toBeCloseTo(2.5, 5);
    expect(stats.high).toBe(103);
    expect(stats.low).toBe(99);
  });

  it("drops malformed rows from headline stats so labels match chartable bars", () => {
    const bars = [
      bar("not-a-date", 1, 999, 1, 999, 1000),
      bar("2026-05-01T14:30:00Z", 100, 101, 99, 100.5, 1000),
      bar("2026-05-01T14:35:00Z", 100.5, 103, 100, 102.25, 1200)
    ];
    const stats = computeChartStats(bars);

    expect(stats.open).toBe(100);
    expect(stats.last).toBe(102.25);
    expect(stats.high).toBe(103);
    expect(stats.low).toBe(99);
    expect(stats.volume).toBe(2200);
  });
});

describe("buildCandlestickChartViewModel", () => {
  it("produces a bar for every valid OHLCV entry", () => {
    const bars = [
      bar("2026-05-01T14:30:00Z", 100, 102, 99, 101, 1000),
      bar("2026-05-01T14:35:00Z", 101, 105, 100, 104, 1500),
      bar("2026-05-01T14:40:00Z", 104, 105, 101, 102, 800)
    ];
    const vm = buildCandlestickChartViewModel({
      bars,
      stats: computeChartStats(bars),
      symbol: "AAPL",
      timeframe: "1D"
    });

    expect(vm).not.toBeNull();
    expect(vm?.bars).toHaveLength(3);
    expect(vm?.volumeBars).toHaveLength(3);
    expect(vm?.ariaLabel).toContain("AAPL 1D candlestick chart, 3 bars");
  });

  it("marks bullish and bearish bars correctly", () => {
    const bars = [
      bar("2026-05-01T14:30:00Z", 100, 105, 99, 103, 1000),
      bar("2026-05-01T14:35:00Z", 103, 104, 98, 99, 900)
    ];
    const vm = buildCandlestickChartViewModel({
      bars,
      stats: computeChartStats(bars),
      symbol: "MSFT",
      timeframe: "5D"
    });

    expect(vm?.bars[0]?.isBullish).toBe(true);
    expect(vm?.bars[1]?.isBullish).toBe(false);
  });

  it("returns null when there are no bars", () => {
    const vm = buildCandlestickChartViewModel({
      bars: [],
      stats: computeChartStats([]),
      symbol: "AAPL",
      timeframe: "1D"
    });

    expect(vm).toBeNull();
  });

  it("does not produce NaN coordinates for valid bars", () => {
    const bars = [
      bar("2026-05-01T14:30:00Z", 150, 152, 149, 151, 2000),
      bar("2026-05-01T14:35:00Z", 151, 153, 150, 152.5, 1800)
    ];
    const vm = buildCandlestickChartViewModel({
      bars,
      stats: computeChartStats(bars),
      symbol: "NVDA",
      timeframe: "1M"
    });

    for (const b of vm?.bars ?? []) {
      expect(Number.isFinite(b.midX)).toBe(true);
      expect(Number.isFinite(b.bodyY)).toBe(true);
      expect(b.bodyHeight).toBeGreaterThan(0);
      expect(Number.isFinite(b.wickY1)).toBe(true);
      expect(Number.isFinite(b.wickY2)).toBe(true);
    }
  });

  it("includes OHLC values in tooltip label for each bar", () => {
    const bars = [
      bar("2026-05-01T14:30:00Z", 100, 105, 99, 103, 1000)
    ];
    const vm = buildCandlestickChartViewModel({
      bars,
      stats: computeChartStats(bars),
      symbol: "SPY",
      timeframe: "1D"
    });

    expect(vm?.bars[0]?.tooltipLabel).toContain("103");
    expect(vm?.bars[0]?.tooltipLabel).toContain("105");
    expect(vm?.bars[0]?.tooltipLabel).toContain("99");
  });

  it("exposes per-bar hover detail with OHLC, volume, and change-from-prev", () => {
    const bars = [
      bar("2026-05-01T14:30:00Z", 100, 102, 99, 100, 1000),
      bar("2026-05-01T14:35:00Z", 100, 105, 100, 104, 1500)
    ];
    const vm = buildCandlestickChartViewModel({
      bars,
      stats: computeChartStats(bars),
      symbol: "AAPL",
      timeframe: "1D"
    });

    const firstHover = vm?.bars[0]?.hover;
    expect(firstHover?.index).toBe(0);
    expect(firstHover?.openLabel).toBe("100.00");
    expect(firstHover?.highLabel).toBe("102.00");
    expect(firstHover?.lowLabel).toBe("99.00");
    expect(firstHover?.closeLabel).toBe("100.00");
    expect(firstHover?.volumeLabel).toBe("1.0K");
    // First bar has no prior close, so change is "-"
    expect(firstHover?.changeLabel).toBe("-");
    expect(firstHover?.changeTone).toBe("neutral");
    expect(firstHover?.ariaLabel).toContain("Open 100.00");
    expect(firstHover?.ariaLabel).toContain("Volume 1.0K");

    const secondHover = vm?.bars[1]?.hover;
    expect(secondHover?.index).toBe(1);
    // Close moved from 100 -> 104, +4 (+4%)
    expect(secondHover?.changeLabel).toContain("+4");
    expect(secondHover?.changeLabel).toContain("%");
    expect(secondHover?.changeTone).toBe("positive");
  });

  it("exposes geometry needed for crosshair hit-testing", () => {
    const bars = [
      bar("2026-05-01T14:30:00Z", 100, 102, 99, 101, 1000),
      bar("2026-05-01T14:35:00Z", 101, 105, 100, 104, 1500)
    ];
    const vm = buildCandlestickChartViewModel({
      bars,
      stats: computeChartStats(bars),
      symbol: "AAPL",
      timeframe: "1D"
    });

    expect(vm?.geometry.width).toBeGreaterThan(0);
    expect(vm?.geometry.padX).toBeGreaterThanOrEqual(0);
    expect(vm?.geometry.slotWidth).toBeGreaterThan(0);
    // slotWidth * n should equal plot width (within rounding)
    const plotWidth = vm!.geometry.width - vm!.geometry.padX * 2;
    expect(vm!.geometry.slotWidth * 2).toBeCloseTo(plotWidth, 5);
  });

  it("emits SMA polyline overlays once enough bars are available", () => {
    const closes = Array.from({ length: 25 }, (_, i) => 100 + i);
    const bars = closes.map((close, i) =>
      bar(
        new Date(2026, 4, 1, 14, 30 + i).toISOString(),
        close - 0.5,
        close + 1,
        close - 1,
        close,
        1000 + i
      )
    );
    const vm = buildCandlestickChartViewModel({
      bars,
      stats: computeChartStats(bars),
      symbol: "AAPL",
      timeframe: "1D"
    });

    expect(vm?.smaOverlays.some((o) => o.period === 20)).toBe(true);
    const sma20 = vm?.smaOverlays.find((o) => o.period === 20);
    expect(sma20?.label).toBe("SMA 20");
    expect(sma20?.points.split(" ").length).toBe(closes.length - 19);
    // Not enough bars for SMA 50
    expect(vm?.smaOverlays.some((o) => o.period === 50)).toBe(false);
  });
});

describe("computeSimpleMovingAverage", () => {
  it("returns null for indices below the SMA window then the trailing mean", () => {
    const sma = computeSimpleMovingAverage([1, 2, 3, 4, 5], 3);
    expect(sma).toEqual([null, null, 2, 3, 4]);
  });

  it("returns all nulls when there are fewer values than the period", () => {
    const sma = computeSimpleMovingAverage([1, 2], 5);
    expect(sma).toEqual([null, null]);
  });

  it("handles period 1 as identity", () => {
    expect(computeSimpleMovingAverage([10, 20, 30], 1)).toEqual([10, 20, 30]);
  });

  it("guards against non-positive period without throwing", () => {
    expect(computeSimpleMovingAverage([1, 2, 3], 0)).toEqual([null, null, null]);
  });
});

describe("computeBollingerBands", () => {
  it("returns null until the period is reached, then a band per index", () => {
    const bands = computeBollingerBands([1, 2, 3, 4, 5], 3, 2);
    expect(bands[0]).toBeNull();
    expect(bands[1]).toBeNull();
    expect(bands[2]).not.toBeNull();
    expect(bands[2]!.middle).toBeCloseTo(2, 5);
    expect(bands[2]!.upper).toBeGreaterThan(bands[2]!.middle);
    expect(bands[2]!.lower).toBeLessThan(bands[2]!.middle);
  });

  it("collapses to middle when all values are identical", () => {
    const bands = computeBollingerBands([5, 5, 5, 5, 5], 3, 2);
    const last = bands[bands.length - 1]!;
    expect(last.middle).toBeCloseTo(5, 5);
    expect(last.upper).toBeCloseTo(5, 5);
    expect(last.lower).toBeCloseTo(5, 5);
  });

  it("returns all nulls when there are fewer values than the period", () => {
    expect(computeBollingerBands([1, 2], 5, 2)).toEqual([null, null]);
  });

  it("guards against non-positive period without throwing", () => {
    expect(computeBollingerBands([1, 2, 3], 0, 2)).toEqual([null, null, null]);
  });
});

describe("computeRsi", () => {
  it("returns 100 when every change is a gain (avgLoss zero)", () => {
    const closes = [1, 2, 3, 4, 5, 6];
    const rsi = computeRsi(closes, 3);
    expect(rsi[0]).toBeNull();
    expect(rsi[2]).toBeNull();
    expect(rsi[3]).toBe(100);
    expect(rsi[5]).toBe(100);
  });

  it("returns ~50 when alternating gains and losses cancel out", () => {
    const closes = [10, 11, 10, 11, 10, 11, 10, 11, 10, 11, 10, 11, 10, 11, 10];
    const rsi = computeRsi(closes, 14);
    expect(rsi[14]).not.toBeNull();
    expect(rsi[14]!).toBeGreaterThan(40);
    expect(rsi[14]!).toBeLessThan(60);
  });

  it("returns all nulls when there are not enough values", () => {
    expect(computeRsi([1, 2, 3], 5)).toEqual([null, null, null]);
  });

  it("guards against non-positive period without throwing", () => {
    expect(computeRsi([1, 2, 3], 0)).toEqual([null, null, null]);
  });
});

describe("candlestick indicators visibility", () => {
  function buildClimbingBars(count: number) {
    return Array.from({ length: count }, (_, i) =>
      bar(
        new Date(2026, 4, 1, 14, 30 + i).toISOString(),
        100 + i - 0.5,
        100 + i + 1,
        100 + i - 1,
        100 + i,
        1000 + i
      )
    );
  }

  it("hides SMA when sma visibility is false", () => {
    const bars = buildClimbingBars(25);
    const vm = buildCandlestickChartViewModel({
      bars,
      stats: computeChartStats(bars),
      symbol: "AAPL",
      timeframe: "1D",
      indicators: { sma: false, bollinger: false, rsi: false }
    });
    expect(vm?.smaOverlays).toHaveLength(0);
    expect(vm?.bollingerOverlay).toBeNull();
    expect(vm?.rsiPanel).toBeNull();
  });

  it("emits Bollinger overlay when bollinger visibility is true and enough bars exist", () => {
    const bars = buildClimbingBars(25);
    const vm = buildCandlestickChartViewModel({
      bars,
      stats: computeChartStats(bars),
      symbol: "AAPL",
      timeframe: "1D",
      indicators: { sma: false, bollinger: true, rsi: false }
    });
    expect(vm?.bollingerOverlay).not.toBeNull();
    expect(vm?.bollingerOverlay?.label).toContain("Bollinger");
    expect(vm?.bollingerOverlay?.upperPoints.length).toBeGreaterThan(0);
    expect(vm?.bollingerOverlay?.lowerPoints.length).toBeGreaterThan(0);
    expect(vm?.bollingerOverlay?.bandPath).toContain("Z");
  });

  it("emits RSI panel with reference lines when rsi visibility is true", () => {
    const bars = buildClimbingBars(25);
    const vm = buildCandlestickChartViewModel({
      bars,
      stats: computeChartStats(bars),
      symbol: "AAPL",
      timeframe: "1D",
      indicators: { sma: false, bollinger: false, rsi: true }
    });
    expect(vm?.rsiPanel).not.toBeNull();
    expect(vm?.rsiPanel?.period).toBe(14);
    expect(vm?.rsiPanel?.overboughtY).toBeLessThan(vm!.rsiPanel!.oversoldY);
    expect(vm?.rsiPanel?.lastValue).not.toBeNull();
    // Climbing series should reach overbought
    expect(vm?.rsiPanel?.lastValueTone).toBe("overbought");
    // Total chart viewBox grows when RSI is enabled
    const heightWithRsi = Number(vm!.viewBox.split(" ")[3]);
    const vmNoRsi = buildCandlestickChartViewModel({
      bars,
      stats: computeChartStats(bars),
      symbol: "AAPL",
      timeframe: "1D",
      indicators: { sma: false, bollinger: false, rsi: false }
    });
    const heightNoRsi = Number(vmNoRsi!.viewBox.split(" ")[3]);
    expect(heightWithRsi).toBeGreaterThan(heightNoRsi);
  });
});

describe("useHistoricalChartViewModel", () => {
  it("defaults to candles chart mode and exposes a working toggle", async () => {
    vi.mocked(getHistoricalBars).mockResolvedValue({
      success: true, message: null, symbol: "AAPL", intervalMinutes: 5,
      from: null, to: null, totalBars: 0, filesProcessed: 0, totalFiles: 0,
      queryTimeMs: 0, bars: []
    });

    const { result } = renderHook(() => useHistoricalChartViewModel("AAPL"));

    expect(result.current.activeChartMode).toBe("candles");
    expect(result.current.chartModeOptions).toHaveLength(2);

    const lineOption = result.current.chartModeOptions.find((o) => o.mode === "line");
    expect(lineOption?.ariaPressed).toBe(false);

    await act(async () => {
      lineOption?.select();
    });

    expect(result.current.activeChartMode).toBe("line");
    expect(result.current.chartModeOptions.find((o) => o.mode === "line")?.ariaPressed).toBe(true);
    expect(result.current.chartModeOptions.find((o) => o.mode === "candles")?.ariaPressed).toBe(false);
  });

  it("exposes indicator toggles defaulting to SMA on, Bollinger off, RSI off", async () => {
    vi.mocked(getHistoricalBars).mockResolvedValue({
      success: true, message: null, symbol: "AAPL", intervalMinutes: 5,
      from: null, to: null, totalBars: 0, filesProcessed: 0, totalFiles: 0,
      queryTimeMs: 0, bars: []
    });

    const { result } = renderHook(() => useHistoricalChartViewModel("AAPL"));

    expect(result.current.indicators).toEqual({ sma: true, bollinger: false, rsi: false });
    expect(result.current.indicatorOptions.map((o) => o.id)).toEqual(["sma", "bollinger", "rsi"]);

    const rsiOption = result.current.indicatorOptions.find((o) => o.id === "rsi");
    expect(rsiOption?.ariaPressed).toBe(false);

    await act(async () => {
      rsiOption?.toggle();
    });

    expect(result.current.indicators.rsi).toBe(true);
    expect(result.current.indicatorOptions.find((o) => o.id === "rsi")?.ariaPressed).toBe(true);
  });

  it("aborts superseded historical-bar requests", async () => {
    const first = deferred<Awaited<ReturnType<typeof getHistoricalBars>>>();
    const second = deferred<Awaited<ReturnType<typeof getHistoricalBars>>>();
    vi.mocked(getHistoricalBars)
      .mockReturnValueOnce(first.promise)
      .mockReturnValueOnce(second.promise);

    const { rerender, unmount } = renderHook(
      ({ symbol }: { symbol: string }) => useHistoricalChartViewModel(symbol),
      { initialProps: { symbol: "AAPL" } }
    );

    await waitFor(() => expect(getHistoricalBars).toHaveBeenCalledTimes(1));
    const firstSignal = vi.mocked(getHistoricalBars).mock.calls[0]?.[2]?.signal;
    expect(firstSignal?.aborted).toBe(false);

    rerender({ symbol: "MSFT" });

    await waitFor(() => expect(getHistoricalBars).toHaveBeenCalledTimes(2));
    const secondSignal = vi.mocked(getHistoricalBars).mock.calls[1]?.[2]?.signal;
    expect(firstSignal?.aborted).toBe(true);
    expect(secondSignal?.aborted).toBe(false);

    await act(async () => {
      second.resolve({
        success: true,
        message: null,
        symbol: "MSFT",
        intervalMinutes: 5,
        from: null,
        to: null,
        totalBars: 0,
        filesProcessed: 0,
        totalFiles: 0,
        queryTimeMs: 0,
        bars: []
      });
      await second.promise;
    });

    unmount();
    expect(secondSignal?.aborted).toBe(false);
  });

  it("manages compare symbols: add, dedupe, base-conflict, limit, remove, clear", async () => {
    vi.mocked(getHistoricalBars).mockResolvedValue({
      success: true, message: null, symbol: "AAPL", intervalMinutes: 5,
      from: null, to: null, totalBars: 0, filesProcessed: 0, totalFiles: 0,
      queryTimeMs: 0, bars: []
    });

    const { result } = renderHook(() => useHistoricalChartViewModel("AAPL"));
    expect(result.current.compareActive).toBe(false);
    expect(result.current.compareChips).toHaveLength(0);

    await act(async () => {
      result.current.addCompareSymbol("msft");
    });
    expect(result.current.compareChips.map((c) => c.symbol)).toEqual(["MSFT"]);
    expect(result.current.compareActive).toBe(true);

    // Dedupe: adding same symbol again is rejected
    await act(async () => {
      result.current.addCompareSymbol("MSFT");
    });
    expect(result.current.compareErrorMessage).toContain("already in the comparison");
    expect(result.current.compareChips).toHaveLength(1);

    // Base-conflict: adding the base symbol is rejected
    await act(async () => {
      result.current.addCompareSymbol("AAPL");
    });
    expect(result.current.compareErrorMessage).toContain("base symbol");

    // Fill up to the limit
    for (let i = 0; i < COMPARE_SYMBOL_LIMIT - 1; i++) {
      await act(async () => {
        result.current.addCompareSymbol(`SYM${i}`);
      });
    }
    expect(result.current.compareChips).toHaveLength(COMPARE_SYMBOL_LIMIT);

    await act(async () => {
      result.current.addCompareSymbol("EXTRA");
    });
    expect(result.current.compareErrorMessage).toContain(`limited to ${COMPARE_SYMBOL_LIMIT}`);

    // Remove one
    await act(async () => {
      result.current.removeCompareSymbol("MSFT");
    });
    expect(result.current.compareChips.map((c) => c.symbol)).not.toContain("MSFT");

    // Clear all
    await act(async () => {
      result.current.clearCompareSymbols();
    });
    expect(result.current.compareChips).toHaveLength(0);
    expect(result.current.compareActive).toBe(false);
  });

  it("assigns distinct colors to each compare symbol", async () => {
    vi.mocked(getHistoricalBars).mockResolvedValue({
      success: true, message: null, symbol: "AAPL", intervalMinutes: 5,
      from: null, to: null, totalBars: 0, filesProcessed: 0, totalFiles: 0,
      queryTimeMs: 0, bars: []
    });

    const { result } = renderHook(() => useHistoricalChartViewModel("AAPL"));

    await act(async () => {
      result.current.addCompareSymbol("MSFT");
      result.current.addCompareSymbol("GOOG");
    });

    const colors = result.current.compareChips.map((c) => c.color);
    expect(new Set(colors).size).toBe(colors.length);
  });
});

describe("computePercentChangeSeries", () => {
  it("returns an empty array when input bars are empty", () => {
    expect(computePercentChangeSeries([])).toEqual([]);
  });

  it("normalizes to percent change off the first bar's close", () => {
    const bars = [
      bar("2026-05-01T14:30:00Z", 100, 100, 100, 100, 1),
      bar("2026-05-01T14:31:00Z", 100, 100, 100, 110, 1),
      bar("2026-05-01T14:32:00Z", 100, 100, 100, 90, 1)
    ];
    const series = computePercentChangeSeries(bars);
    expect(series).toHaveLength(3);
    expect(series[0]!.changePct).toBeCloseTo(0, 5);
    expect(series[1]!.changePct).toBeCloseTo(10, 5);
    expect(series[2]!.changePct).toBeCloseTo(-10, 5);
  });

  it("guards against a zero or non-finite base close", () => {
    const bars = [bar("2026-05-01T14:30:00Z", 0, 0, 0, 0, 0)];
    expect(computePercentChangeSeries(bars)).toEqual([]);
  });
});

describe("buildCompareChartViewModel", () => {
  function makeBars(start: string, changes: number[]) {
    return changes.map((c, i) =>
      bar(
        new Date(new Date(start).getTime() + i * 60_000).toISOString(),
        100 + c - 0.1,
        100 + c + 0.1,
        100 + c - 0.2,
        100 + c,
        100
      )
    );
  }

  it("returns null when there are no points across any series", () => {
    const vm = buildCompareChartViewModel({
      baseSymbol: "AAPL",
      baseBars: [],
      compareSymbols: [],
      compareStates: {},
      timeframe: "1D"
    });
    expect(vm).toBeNull();
  });

  it("renders base + compare series, includes zero-line, and computes last percent change", () => {
    const baseBars = makeBars("2026-05-01T14:30:00Z", [0, 1, 2, 3]);
    const compareBars = makeBars("2026-05-01T14:30:00Z", [0, -1, 1, 5]);
    const vm = buildCompareChartViewModel({
      baseSymbol: "AAPL",
      baseBars,
      compareSymbols: ["MSFT"],
      compareStates: { MSFT: { status: "ready", bars: compareBars } },
      timeframe: "1D"
    });
    expect(vm).not.toBeNull();
    expect(vm!.series.map((s) => s.symbol)).toEqual(["AAPL", "MSFT"]);
    const base = vm!.series.find((s) => s.symbol === "AAPL")!;
    expect(base.isBase).toBe(true);
    expect(base.lastChangePct).toBeCloseTo(3, 1);
    expect(base.lastChangeTone).toBe("positive");

    const msft = vm!.series.find((s) => s.symbol === "MSFT")!;
    expect(msft.lastChangePct).toBeCloseTo(5, 1);
    expect(msft.points.split(" ")).toHaveLength(4);

    // Zero line is somewhere on the chart
    expect(vm!.zeroLineY).toBeGreaterThanOrEqual(0);
    expect(vm!.zeroLineY).toBeLessThanOrEqual(vm!.height);
  });

  it("surfaces loading/error/empty status on compare series with no points", () => {
    const baseBars = makeBars("2026-05-01T14:30:00Z", [0, 1, 2]);
    const vm = buildCompareChartViewModel({
      baseSymbol: "AAPL",
      baseBars,
      compareSymbols: ["MSFT", "GOOG"],
      compareStates: {
        MSFT: { status: "loading", bars: [] },
        GOOG: { status: "error", bars: [] }
      },
      timeframe: "1D"
    });

    const msft = vm!.series.find((s) => s.symbol === "MSFT")!;
    expect(msft.status).toBe("loading");
    expect(msft.points).toBe("");
    expect(msft.lastChangeLabel).toContain("Loading");

    const goog = vm!.series.find((s) => s.symbol === "GOOG")!;
    expect(goog.status).toBe("error");
    expect(goog.lastChangeLabel).toBe("Error");
  });
});
