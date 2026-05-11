import { act, renderHook, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { getHistoricalBars } from "@/lib/api";
import {
  HISTORICAL_CHART_TIMEFRAMES,
  buildCandlestickChartViewModel,
  buildHistoricalChartSparklineViewModel,
  buildHistoricalChartStatePanel,
  computeChartStats,
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
});
