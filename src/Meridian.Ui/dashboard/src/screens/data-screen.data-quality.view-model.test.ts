import { act, renderHook, waitFor } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { buildDataQualityPanelModel, scoreTone, useDataQualityPanel } from "./data-screen.data-quality.view-model";
import type {
  QualityCompositeDashboardResponse,
  QualityCompositeGapResponse,
  QualityCompositeSymbolResponse,
  QualityDashboardResponse
} from "@/types";

const gap: QualityCompositeGapResponse = {
  gapId: "111111111111111111111111",
  symbol: "TSLA",
  provider: "polygon",
  eventType: "Trade",
  from: "2026-07-01T14:00:00Z",
  to: "2026-07-01T14:07:00Z",
  estimatedMissingEvents: 11,
  severity: "Significant",
  status: "Open",
  canBackfill: true,
  disabledReason: null
};

function symbol(overrides: Partial<QualityCompositeSymbolResponse> = {}): QualityCompositeSymbolResponse {
  return {
    symbol: "AAPL",
    compositeScore: 96,
    status: "Green",
    isPartial: false,
    coverageWeight: 1,
    expectedEvents: 100,
    observedEvents: 100,
    anomalyCount: 0,
    components: [
      {
        kind: "StoredCompleteness",
        label: "Stored completeness",
        weight: 0.4,
        score: 98,
        availability: "Measured",
        observedAt: "2026-07-03T14:00:00Z",
        issueCount: 0,
        detail: "Stored artifacts measured."
      },
      {
        kind: "StreamingFreshness",
        label: "Streaming freshness",
        weight: 0.35,
        score: 96,
        availability: "Measured",
        observedAt: "2026-07-03T14:00:00Z",
        issueCount: 0,
        detail: "Streaming evidence measured."
      },
      {
        kind: "AdapterGapIntegrity",
        label: "Adapter gap integrity",
        weight: 0.25,
        score: 92,
        availability: "Measured",
        observedAt: "2026-07-03T14:00:00Z",
        issueCount: 0,
        detail: "Adapter evidence measured."
      }
    ],
    openGaps: [],
    providerFreshness: [],
    issues: [],
    ...overrides
  };
}

function response(
  compositeOverrides: Partial<QualityCompositeDashboardResponse> = {},
  responseOverrides: Partial<QualityDashboardResponse> = {}
): QualityDashboardResponse {
  const composite: QualityCompositeDashboardResponse = {
    version: "quality-v1",
    observedAt: "2026-07-03T14:00:00Z",
    compositeScore: 96,
    status: "Green",
    isPartial: false,
    coverageWeight: 1,
    components: symbol().components,
    symbols: [symbol()],
    openGaps: [],
    anomalyCount: 0,
    ...compositeOverrides
  };

  return {
    timestamp: "2026-07-03T14:00:00Z",
    composite,
    recentGaps: [],
    recentAnomalies: [],
    ...responseOverrides
  };
}

describe("scoreTone", () => {
  it("maps the canonical 80/60 score bands onto tones", () => {
    expect(scoreTone(80)).toBe("success");
    expect(scoreTone(79.9)).toBe("warning");
    expect(scoreTone(59.9)).toBe("danger");
  });
});

describe("buildDataQualityPanelModel", () => {
  it("keeps unavailable composite evidence explicit instead of inventing demo scores", () => {
    const model = buildDataQualityPanelModel({
      timestamp: "2026-07-03T14:00:00Z",
      composite: null,
      recentGaps: [],
      recentAnomalies: []
    });

    expect(model.overallStatus).toBe("Unavailable");
    expect(model.overallLabel).toBe("Unavailable");
    expect(model.symbols).toEqual([]);
    expect(model.scoreCards).toEqual([]);
  });

  it("projects every collected symbol worst-first with source evidence and exact gaps", () => {
    const partial = symbol({
      symbol: "TSLA",
      compositeScore: 55,
      status: "Red",
      isPartial: true,
      coverageWeight: 0.75,
      expectedEvents: 100,
      observedEvents: 89,
      anomalyCount: 2,
      openGaps: [gap],
      providerFreshness: [{
        provider: "polygon",
        lastEventAt: "2026-07-03T13:30:00Z",
        ageMilliseconds: 1_800_000,
        status: "Stale",
        completenessScore: 89,
        gapCount: 1
      }],
      issues: ["Open trade gap."]
    });
    const model = buildDataQualityPanelModel(
      response({
        compositeScore: 74,
        status: "Amber",
        isPartial: true,
        coverageWeight: 0.75,
        symbols: [symbol(), partial],
        openGaps: [gap],
        anomalyCount: 2
      })
    );

    expect(model.symbols.map((row) => row.symbol)).toEqual(["TSLA", "AAPL"]);
    expect(model.symbols[0]).toMatchObject({
      status: "Red",
      isPartial: true,
      coverageLabel: "75% evidence coverage",
      expectedEventsLabel: "89 / 100 expected events",
      gapCount: 1,
      anomalyCount: 2
    });
    expect(model.openGaps).toEqual([gap]);
    expect(model.overallStatus).toBe("Amber");
    expect(model.summary).toContain("2 collected symbols");
    expect(model.summary).toContain("1 open gap");
  });

  it("filters acknowledged legacy anomalies while using composite score cards", () => {
    const anomaly = {
      id: "a-1",
      timestamp: "2026-07-03T14:00:00Z",
      symbol: "AAPL",
      type: 1,
      severity: 2,
      description: "Price spike",
      expectedValue: 100,
      actualValue: 150,
      deviationPercent: 50,
      zScore: 4,
      provider: "polygon",
      isAcknowledged: false,
      detectedAt: "2026-07-03T14:00:00Z"
    };
    const model = buildDataQualityPanelModel(response({}, {
      recentAnomalies: [anomaly, { ...anomaly, id: "a-2", isAcknowledged: true }]
    }));

    expect(model.scoreCards.map((card) => card.id)).toEqual([
      "Composite",
      "StoredCompleteness",
      "StreamingFreshness",
      "AdapterGapIntegrity"
    ]);
    expect(model.unacknowledgedAnomalies.map((entry) => entry.id)).toEqual(["a-1"]);
  });
});

describe("useDataQualityPanel", () => {
  it("reads as loading until the first fetch settles, then exposes the model", async () => {
    let resolveFetch: ((value: QualityDashboardResponse) => void) | null = null;
    const fetchDashboard = () => new Promise<QualityDashboardResponse>((resolve) => {
      resolveFetch = resolve;
    });
    const { result } = renderHook(() => useDataQualityPanel(fetchDashboard));

    expect(result.current.loading).toBe(true);
    await act(async () => resolveFetch?.(response()));

    await waitFor(() => expect(result.current.loading).toBe(false));
    expect(result.current.error).toBeNull();
    expect(result.current.model?.overallLabel).toBe("96.0");
  });

  it("discards a stale response when a newer refresh supersedes it", async () => {
    const resolvers: Array<(value: QualityDashboardResponse) => void> = [];
    const fetchDashboard = () => new Promise<QualityDashboardResponse>((resolve) => {
      resolvers.push(resolve);
    });
    const { result } = renderHook(() => useDataQualityPanel(fetchDashboard));
    await act(async () => void result.current.refresh());

    await act(async () => {
      resolvers[1]?.(response({ compositeScore: 88 }));
      resolvers[0]?.(response({ compositeScore: 12 }));
    });

    await waitFor(() => expect(result.current.loading).toBe(false));
    expect(result.current.model?.overallLabel).toBe("88.0");
  });
});
