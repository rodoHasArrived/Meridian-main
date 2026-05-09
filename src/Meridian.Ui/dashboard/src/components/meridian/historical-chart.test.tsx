import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

import {
  HISTORICAL_CHART_TIMEFRAMES,
  HistoricalChartCard,
  computeChartStats,
  formatIntervalLabel
} from "@/components/meridian/historical-chart";
import * as api from "@/lib/api";
import type { HistoricalBarPoint, HistoricalBarsResponse } from "@/types";

function bar(start: string, open: number, high: number, low: number, close: number, volume: number, vwap?: number): HistoricalBarPoint {
  return { start, open, high, low, close, volume, vwap: vwap ?? close, tradeCount: 1 };
}

function response(bars: HistoricalBarPoint[], intervalMinutes = 5): HistoricalBarsResponse {
  return {
    success: true,
    message: null,
    symbol: "AAPL",
    intervalMinutes,
    from: null,
    to: null,
    totalBars: bars.length,
    filesProcessed: 1,
    totalFiles: 1,
    queryTimeMs: 1,
    bars
  };
}

describe("computeChartStats", () => {
  it("returns nulls for empty input", () => {
    const stats = computeChartStats([]);
    expect(stats.open).toBeNull();
    expect(stats.high).toBeNull();
    expect(stats.low).toBeNull();
    expect(stats.last).toBeNull();
    expect(stats.change).toBeNull();
    expect(stats.changePct).toBeNull();
    expect(stats.volume).toBe(0);
  });

  it("aggregates open from first bar and last from final close", () => {
    const stats = computeChartStats([
      bar("2024-06-03T14:30:00Z", 100, 101, 99.5, 100.5, 1000, 100.25),
      bar("2024-06-03T14:35:00Z", 100.5, 102, 100.4, 101.7, 800, 101.2)
    ]);

    expect(stats.open).toBe(100);
    expect(stats.last).toBe(101.7);
    expect(stats.high).toBe(102);
    expect(stats.low).toBe(99.5);
    expect(stats.change).toBeCloseTo(1.7, 5);
    expect(stats.changePct).toBeCloseTo(1.7, 5);
    expect(stats.volume).toBe(1800);
    // VWAP = (100.25*1000 + 101.2*800) / 1800
    expect(stats.vwap).toBeCloseTo(100.6722, 3);
  });

  it("falls back to close when bar vwap is missing or non-positive", () => {
    const stats = computeChartStats([
      bar("2024-06-03T14:30:00Z", 100, 100, 100, 100, 100, 0),
    ]);
    expect(stats.vwap).toBe(100);
  });
});

describe("formatIntervalLabel", () => {
  it("formats sub-hour intervals as minutes", () => {
    expect(formatIntervalLabel(5)).toBe("5m");
    expect(formatIntervalLabel(30)).toBe("30m");
  });

  it("formats hourly and daily intervals", () => {
    expect(formatIntervalLabel(60)).toBe("1h");
    expect(formatIntervalLabel(240)).toBe("4h");
    expect(formatIntervalLabel(1440)).toBe("1d");
  });
});

describe("HistoricalChartCard", () => {
  let getBarsSpy: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    getBarsSpy = vi.spyOn(api, "getHistoricalBars").mockResolvedValue(response([]));
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("renders all timeframe options", async () => {
    render(<HistoricalChartCard symbol="AAPL" />);

    expect(screen.getByRole("status")).toHaveTextContent(/Loading 1D bars for AAPL/);

    for (const tf of HISTORICAL_CHART_TIMEFRAMES) {
      const button = await screen.findByTestId(`historical-chart-timeframe-${tf.id}`);
      expect(button).toBeInTheDocument();
      expect(button).toHaveTextContent(tf.label);
    }
  });

  it("fetches bars for the active timeframe on mount", async () => {
    render(<HistoricalChartCard symbol="AAPL" />);

    await waitFor(() => expect(getBarsSpy).toHaveBeenCalled());
    const callArg = getBarsSpy.mock.calls[0]?.[1] as api.HistoricalBarsRequest | undefined;
    expect(callArg?.intervalMinutes).toBe(HISTORICAL_CHART_TIMEFRAMES[0]!.intervalMinutes);
    expect(callArg?.from).toMatch(/^\d{4}-\d{2}-\d{2}$/);
    expect(callArg?.to).toMatch(/^\d{4}-\d{2}-\d{2}$/);
  });

  it("refetches when a different timeframe is selected", async () => {
    const user = userEvent.setup();
    render(<HistoricalChartCard symbol="AAPL" />);

    await waitFor(() => expect(getBarsSpy).toHaveBeenCalledTimes(1));

    await user.click(screen.getByTestId("historical-chart-timeframe-1M"));

    await waitFor(() => expect(getBarsSpy).toHaveBeenCalledTimes(2));
    const secondCallArg = getBarsSpy.mock.calls[1]?.[1] as api.HistoricalBarsRequest | undefined;
    expect(secondCallArg?.intervalMinutes).toBe(60);
  });

  it("renders bars and computed stats once data resolves", async () => {
    getBarsSpy.mockResolvedValueOnce(response([
      bar("2024-06-03T14:30:00Z", 100, 101, 99.5, 100.5, 1000, 100.25),
      bar("2024-06-03T14:35:00Z", 100.5, 102, 100.4, 101.7, 800, 101.2)
    ]));

    render(<HistoricalChartCard symbol="AAPL" />);

    await waitFor(() => {
      expect(screen.getByText("Open")).toBeInTheDocument();
    });

    // Last close is 101.70 which should appear as a price somewhere in the card.
    expect(screen.getAllByText(/101\.70/).length).toBeGreaterThan(0);
  });

  it("shows an empty state when no bars are returned", async () => {
    getBarsSpy.mockResolvedValueOnce(response([]));

    render(<HistoricalChartCard symbol="AAPL" />);

    await waitFor(() => {
      expect(
        screen.getByText(/No stored trades found for AAPL/)
      ).toBeInTheDocument();
    });
  });

  it("surfaces an error message when the fetch fails", async () => {
    getBarsSpy.mockRejectedValueOnce(new Error("boom"));

    render(<HistoricalChartCard symbol="AAPL" />);

    await waitFor(() => {
      expect(screen.getByRole("alert")).toHaveTextContent(/boom/);
    });
  });
});
