import { describe, expect, it } from "vitest";
import {
  buildCompletenessOverview,
  buildHighLatencyRow,
  buildLatencyOverview,
  buildLowCompletenessRow,
  buildQualityHeadline,
  buildStaleSymbolsViewModel,
  buildTopErrorRow,
  buildUnacknowledgedAnomalyRow,
  buildUnhealthySymbolRow
} from "@/screens/data-quality-watch.view-model";
import type {
  QualityAnomaly,
  QualityCompletenessScore,
  QualityCompletenessSummary,
  QualityHealthSnapshot,
  QualityLatencyStatistics,
  QualitySymbolHealth
} from "@/types/data-quality-watch.types";

const snapshot: QualityHealthSnapshot = {
  status: "degraded",
  score: 0.812,
  activeSymbols: 42,
  symbolsWithIssues: 5,
  gapsLast5Min: 2,
  errorsLast5Min: 7,
  anomaliesLast5Min: 1,
  timestamp: "2026-08-26T16:00:00Z"
};

const symbolHealth: QualitySymbolHealth = {
  symbol: "AAPL",
  state: 2,
  score: 0.41,
  lastEvent: "2026-08-26T15:52:00Z",
  timeSinceLastEvent: "00:08:00",
  activeIssues: ["Sequence gaps", "Stale quotes"]
};

const latency: QualityLatencyStatistics = {
  symbolsTracked: 42,
  totalSamples: 128_400,
  globalMeanMs: 18.4,
  globalP50Ms: 12,
  globalP90Ms: 44.5,
  globalP99Ms: 1240,
  fastestSymbol: "MSFT",
  slowestSymbol: "TSLA",
  distributionsBySymbol: { AAPL: 33 },
  calculatedAt: "2026-08-26T16:00:00Z"
};

const completeness: QualityCompletenessSummary = {
  totalSymbolDates: 210,
  averageScore: 0.94,
  minScore: 0.32,
  maxScore: 1,
  symbolsTracked: 42,
  datesTracked: 5,
  totalEvents: 900_000,
  totalExpectedEvents: 950_000,
  overallCoverage: 0.947,
  gradeDistribution: { A: 180, C: 22, F: 8 },
  calculatedAt: "2026-08-26T16:00:00Z"
};

const lowScore: QualityCompletenessScore = {
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
};

const anomaly: QualityAnomaly = {
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
};

describe("quality headline", () => {
  it("reports not-loaded rather than a healthy zero", () => {
    const view = buildQualityHeadline(null);

    expect(view.loaded).toBe(false);
    expect(view.statusLabel).toBe("Not loaded");
    expect(view.statusTone).toBe("default");
    expect(view.scoreLabel).toBe("—");
  });

  it("restates the server's status rather than deriving one from the score", () => {
    const view = buildQualityHeadline(snapshot);

    expect(view.statusLabel).toBe("degraded");
    expect(view.statusTone).toBe("warning");
    expect(view.scoreLabel).toBe("81.2%");
    expect(view.symbolsWithIssuesLabel).toBe("5 of 42");
    expect(view.recentActivityLabel).toBe("2 gaps, 7 errors, 1 anomalies in the last 5 min");
  });

  it("does not assume an unrecognized status is benign", () => {
    expect(buildQualityHeadline({ ...snapshot, status: "recovering" }).statusTone).toBe("default");
    expect(buildQualityHeadline({ ...snapshot, status: "unhealthy" }).statusTone).toBe("danger");
    expect(buildQualityHeadline({ ...snapshot, status: "healthy" }).statusTone).toBe("success");
  });
});

describe("unhealthy symbol rows", () => {
  it("resolves the health-state ordinal and keeps the issue list", () => {
    const row = buildUnhealthySymbolRow(symbolHealth);

    expect(row.stateLabel).toBe("Unhealthy");
    expect(row.stateTone).toBe("danger");
    expect(row.scoreLabel).toBe("41.0%");
    expect(row.issues).toBe("Sequence gaps; Stale quotes");
  });

  it("names an ordinal outside the transcribed map", () => {
    expect(buildUnhealthySymbolRow({ ...symbolHealth, state: 9 }).stateLabel)
      .toBe("Unrecognized health state 9");
  });

  it("says no issue detail was reported rather than rendering an empty cell", () => {
    expect(buildUnhealthySymbolRow({ ...symbolHealth, activeIssues: [] }).issues)
      .toBe("No issue detail reported");
  });

  it("shows the raw span beside the timestamp it was measured from", () => {
    expect(buildUnhealthySymbolRow(symbolHealth).silenceLabel).toBe("00:08:00 since 2026-08-26T15:52:00Z");
    expect(buildUnhealthySymbolRow({ ...symbolHealth, timeSinceLastEvent: "" }).silenceLabel)
      .toBe("Last event 2026-08-26T15:52:00Z");
  });
});

describe("latency overview", () => {
  it("switches to seconds once a percentile passes a second", () => {
    const view = buildLatencyOverview(latency);

    expect(view.headlineLabel).toBe("p99 1.24s");
    expect(view.spreadLabel).toBe("p50 12.0ms · p90 44.5ms · mean 18.4ms");
    expect(view.sampleLabel).toBe("128,400 samples across 42 symbols");
  });

  it("says an extreme is not ranked rather than leaving it blank", () => {
    const view = buildLatencyOverview({ ...latency, fastestSymbol: null, slowestSymbol: null });

    expect(view.extremesLabel).toBe("Fastest not ranked · slowest not ranked");
  });

  it("separates a symbol far past the threshold from one just over it", () => {
    expect(buildHighLatencyRow({ symbol: "A", p99Ms: 120 }, 100).tone).toBe("warning");
    expect(buildHighLatencyRow({ symbol: "B", p99Ms: 1200 }, 100).tone).toBe("danger");
  });

  it("pluralizes the error count", () => {
    expect(buildTopErrorRow({ symbol: "A", errorCount: 1 }).valueLabel).toBe("1 error");
    expect(buildTopErrorRow({ symbol: "B", errorCount: 1200 }).valueLabel).toBe("1,200 errors");
  });
});

describe("completeness", () => {
  it("summarizes coverage, spread, and the grade distribution", () => {
    const view = buildCompletenessOverview(completeness);

    expect(view.coverageLabel).toBe("94.7%");
    expect(view.spreadLabel).toBe("32.0% – 100.0%");
    expect(view.gradeLabel).toBe("A:180 · C:22 · F:8");
    expect(view.trackedLabel).toBe("42 symbols over 5 dates");
  });

  it("says no grades were recorded rather than showing an empty distribution", () => {
    expect(buildCompletenessOverview({ ...completeness, gradeDistribution: {} }).gradeLabel)
      .toBe("No grades recorded");
  });

  it("shows the server's grade alongside the score it was computed from", () => {
    const row = buildLowCompletenessRow(lowScore);

    expect(row.scoreLabel).toBe("42.0%");
    expect(row.grade).toBe("F");
    expect(row.tone).toBe("danger");
    expect(row.missingLabel).toBe("11,600 of 20,000 missing");
    expect(row.coverageLabel).toBe("42.0%");
  });

  it("tones a merely-low score differently from a collapsed one", () => {
    expect(buildLowCompletenessRow({ ...lowScore, score: 0.7 }).tone).toBe("warning");
  });
});

describe("anomalies and stale symbols", () => {
  it("resolves the type and severity ordinals", () => {
    const row = buildUnacknowledgedAnomalyRow(anomaly);

    expect(row.typeLabel).toBe("Price spike");
    expect(row.severityLabel).toBe("Critical");
    expect(row.severityTone).toBe("danger");
    expect(row.deviationLabel).toBe("+24.0% (z 12.40)");
  });

  it("names ordinals outside the transcribed maps", () => {
    const row = buildUnacknowledgedAnomalyRow({ ...anomaly, type: 99, severity: 42 });

    expect(row.typeLabel).toBe("Unrecognized anomaly type 99");
    expect(row.severityLabel).toBe("Unrecognized severity 42");
  });

  it("distinguishes not-loaded, none, and some stale symbols", () => {
    expect(buildStaleSymbolsViewModel(null).label).toBe("Not loaded");
    expect(buildStaleSymbolsViewModel(null).notice).toBeNull();

    const none = buildStaleSymbolsViewModel([]);
    expect(none.loaded).toBe(true);
    expect(none.label).toBe("None");
    expect(none.notice).toBeNull();

    const some = buildStaleSymbolsViewModel(["AAPL", "TSLA"]);
    expect(some.label).toBe("2");
    expect(some.notice).toBe("2 symbols have stopped reporting: AAPL, TSLA.");
  });
});
