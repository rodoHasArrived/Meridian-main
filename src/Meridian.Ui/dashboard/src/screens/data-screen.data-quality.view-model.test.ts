import { describe, expect, it } from "vitest";
import { buildDataQualityPanelModel, scoreTone } from "./data-screen.data-quality.view-model";
import type { QualityDashboardResponse } from "@/types";

function response(overrides: Partial<QualityDashboardResponse> = {}): QualityDashboardResponse {
  return {
    overallScore: 99.2,
    completenessScore: 99.5,
    freshnessScore: 98.9,
    anomalyRate: 0.05,
    symbols: [],
    recentGaps: [],
    recentAnomalies: [],
    ...overrides
  };
}

describe("scoreTone", () => {
  it("maps score bands onto tones", () => {
    expect(scoreTone(99)).toBe("success");
    expect(scoreTone(94.9)).toBe("warning");
    expect(scoreTone(84.9)).toBe("danger");
  });
});

describe("buildDataQualityPanelModel", () => {
  it("reports all-healthy when nothing needs attention", () => {
    const model = buildDataQualityPanelModel(
      response({
        symbols: [
          {
            symbol: "AAPL",
            completenessScore: 99.9,
            freshnessScore: 99.8,
            gapCount: 0,
            anomalyCount: 0,
            health: "Healthy"
          }
        ]
      })
    );

    expect(model.attentionSymbols).toHaveLength(0);
    expect(model.healthySymbolCount).toBe(1);
    expect(model.overallTone).toBe("success");
    expect(model.summary).toContain("All 1 tracked symbols healthy");
  });

  it("surfaces attention symbols worst-first with open gaps and unacknowledged anomalies", () => {
    const model = buildDataQualityPanelModel(
      response({
        overallScore: 91.4,
        symbols: [
          {
            symbol: "MSFT",
            completenessScore: 93.0,
            freshnessScore: 97.0,
            gapCount: 2,
            anomalyCount: 0,
            health: "Warning"
          },
          {
            symbol: "TSLA",
            completenessScore: 80.0,
            freshnessScore: 96.0,
            gapCount: 5,
            anomalyCount: 3,
            health: "Critical"
          },
          {
            symbol: "AAPL",
            completenessScore: 99.9,
            freshnessScore: 99.8,
            gapCount: 0,
            anomalyCount: 0,
            health: "Healthy"
          }
        ],
        recentGaps: [
          { symbol: "TSLA", provider: "stooq", from: "2026-07-01", to: "2026-07-02", estimatedBars: 2, status: "Open" },
          { symbol: "MSFT", provider: "stooq", from: "2026-06-28", to: "2026-06-29", estimatedBars: 1, status: "Resolved" }
        ],
        recentAnomalies: [
          {
            anomalyId: "a-1",
            symbol: "TSLA",
            anomalyType: "PriceSpike",
            message: "Price jumped 40% in one bar",
            detectedAt: "2026-07-03T14:00:00Z",
            acknowledged: false
          },
          {
            anomalyId: "a-2",
            symbol: "TSLA",
            anomalyType: "PriceSpike",
            message: "Acknowledged spike",
            detectedAt: "2026-07-02T14:00:00Z",
            acknowledged: true
          }
        ]
      })
    );

    expect(model.attentionSymbols.map((row) => row.symbol)).toEqual(["TSLA", "MSFT"]);
    expect(model.attentionSymbols[0].tone).toBe("danger");
    expect(model.openGaps).toHaveLength(1);
    expect(model.unacknowledgedAnomalies).toHaveLength(1);
    expect(model.overallTone).toBe("warning");
    expect(model.summary).toContain("2 symbols need attention");
    expect(model.summary).toContain("1 open gap");
    expect(model.summary).toContain("1 unacknowledged anomaly");
  });

  it("labels the anomaly-rate card from the events-per-minute rate", () => {
    const calm = buildDataQualityPanelModel(response({ anomalyRate: 0.05 }));
    const busy = buildDataQualityPanelModel(response({ anomalyRate: 1.5 }));

    expect(calm.scoreCards.find((card) => card.id === "anomaly-rate")?.tone).toBe("success");
    expect(busy.scoreCards.find((card) => card.id === "anomaly-rate")?.tone).toBe("danger");
  });
});
