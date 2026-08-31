import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import * as qualityApi from "@/lib/api/data-quality-watch.api";
import { DataQualityWatch } from "@/screens/data-quality-watch";

vi.mock("@/lib/api/data-quality-watch.api", () => ({
  getQualityHealth: vi.fn(),
  getUnhealthyQualitySymbols: vi.fn(),
  getQualityLatencyStatistics: vi.fn(),
  getHighLatencyQualitySymbols: vi.fn(),
  getTopErrorQualitySymbols: vi.fn(),
  getQualityCompletenessSummary: vi.fn(),
  getLowCompletenessQualitySymbols: vi.fn(),
  getUnacknowledgedQualityAnomalies: vi.fn(),
  getStaleQualitySymbols: vi.fn()
}));

const api = vi.mocked(qualityApi);

afterEach(() => {
  vi.resetAllMocks();
});

function primeReads(overrides: { stale?: string[] } = {}) {
  api.getQualityHealth.mockResolvedValue({
    status: "degraded",
    score: 0.812,
    activeSymbols: 42,
    symbolsWithIssues: 5,
    gapsLast5Min: 2,
    errorsLast5Min: 7,
    anomaliesLast5Min: 1,
    timestamp: "2026-08-26T16:00:00Z"
  });
  api.getUnhealthyQualitySymbols.mockResolvedValue([
    {
      symbol: "AAPL",
      state: 2,
      score: 0.41,
      lastEvent: "2026-08-26T15:52:00Z",
      timeSinceLastEvent: "00:08:00",
      activeIssues: ["Sequence gaps"]
    }
  ]);
  api.getQualityLatencyStatistics.mockResolvedValue({
    symbolsTracked: 42,
    totalSamples: 128_400,
    globalMeanMs: 18.4,
    globalP50Ms: 12,
    globalP90Ms: 44.5,
    globalP99Ms: 1240,
    fastestSymbol: "MSFT",
    slowestSymbol: "TSLA",
    distributionsBySymbol: {},
    calculatedAt: "2026-08-26T16:00:00Z"
  });
  api.getHighLatencyQualitySymbols.mockResolvedValue([{ symbol: "TSLA", p99Ms: 1240 }]);
  api.getTopErrorQualitySymbols.mockResolvedValue([{ symbol: "NVDA", errorCount: 1200 }]);
  api.getQualityCompletenessSummary.mockResolvedValue({
    totalSymbolDates: 210,
    averageScore: 0.94,
    minScore: 0.32,
    maxScore: 1,
    symbolsTracked: 42,
    datesTracked: 5,
    totalEvents: 900_000,
    totalExpectedEvents: 950_000,
    overallCoverage: 0.947,
    gradeDistribution: { A: 180, F: 8 },
    calculatedAt: "2026-08-26T16:00:00Z"
  });
  api.getLowCompletenessQualitySymbols.mockResolvedValue([
    {
      symbol: "TSLA",
      date: "2026-08-26",
      score: 0.42,
      expectedEvents: 20_000,
      actualEvents: 8_400,
      missingEvents: 11_600,
      tradingDuration: "06:30:00",
      coveredDuration: "02:44:00",
      coveragePercent: 42,
      calculatedAt: "2026-08-26T16:00:00Z",
      grade: "F"
    }
  ]);
  api.getUnacknowledgedQualityAnomalies.mockResolvedValue([
    {
      id: "anomaly-1",
      timestamp: "2026-08-26T15:40:00Z",
      symbol: "TSLA",
      type: 0,
      severity: 3,
      description: "Price moved 12 standard deviations in one tick.",
      expectedValue: 250,
      actualValue: 310,
      deviationPercent: 24,
      zScore: 12.4,
      provider: "polygon",
      isAcknowledged: false,
      detectedAt: "2026-08-26T15:40:01Z"
    }
  ]);
  api.getStaleQualitySymbols.mockResolvedValue(overrides.stale ?? []);
}

describe("DataQualityWatch", () => {
  it("shows which symbols are unhealthy, slow, noisy, incomplete, and anomalous", async () => {
    primeReads();
    render(<DataQualityWatch />);

    expect(await screen.findByText("Unhealthy")).toBeInTheDocument();
    expect(screen.getByText("Sequence gaps")).toBeInTheDocument();
    expect(screen.getByText("1,200 errors")).toBeInTheDocument();
    expect(screen.getByText("Price spike")).toBeInTheDocument();
    expect(screen.getByText("Critical")).toBeInTheDocument();
    expect(screen.getByText("11,600 of 20,000 missing")).toBeInTheDocument();
  });

  it("asks for the latency threshold and anomaly count it labels the sections with", async () => {
    primeReads();
    render(<DataQualityWatch />);

    await waitFor(() => expect(api.getHighLatencyQualitySymbols).toHaveBeenCalledWith(100));
    expect(api.getUnacknowledgedQualityAnomalies).toHaveBeenCalledWith(25);
    expect(api.getTopErrorQualitySymbols).toHaveBeenCalledWith(10);
    expect(await screen.findByText(/p99 over 100ms/)).toBeInTheDocument();
  });

  it("raises the stale-symbol notice only when symbols have gone silent", async () => {
    primeReads();
    const { unmount } = render(<DataQualityWatch />);
    await screen.findByText("Unhealthy");
    expect(screen.queryByText(/stopped reporting/)).not.toBeInTheDocument();
    unmount();

    vi.resetAllMocks();
    primeReads({ stale: ["AAPL", "TSLA"] });
    render(<DataQualityWatch />);

    expect(await screen.findByText("2 symbols have stopped reporting: AAPL, TSLA."))
      .toBeInTheDocument();
  });

  it("names the rollups that failed and calls it a gap while the headline survives", async () => {
    primeReads();
    api.getTopErrorQualitySymbols.mockRejectedValue(new Error("sequence tracker offline"));
    render(<DataQualityWatch />);

    expect(await screen.findByText("Quality watch loaded with gaps")).toBeInTheDocument();
    expect(screen.getByText(/Top error symbols: sequence tracker offline/)).toBeInTheDocument();
    expect(screen.queryByText(/^Health:/)).not.toBeInTheDocument();
  });

  it("calls the panel unavailable when the headline itself did not load", async () => {
    primeReads();
    api.getQualityHealth.mockRejectedValue(new Error("quality service offline"));
    render(<DataQualityWatch />);

    expect(await screen.findByText("Quality watch unavailable")).toBeInTheDocument();
    expect(screen.getByText(/Health: quality service offline/)).toBeInTheDocument();
  });

  it("refetches every rollup on refresh", async () => {
    primeReads();
    render(<DataQualityWatch />);
    await screen.findByText("Unhealthy");

    await userEvent.click(screen.getByRole("button", { name: /Refresh/ }));

    await waitFor(() => expect(api.getQualityHealth).toHaveBeenCalledTimes(2));
    expect(api.getStaleQualitySymbols).toHaveBeenCalledTimes(2);
  });
});
