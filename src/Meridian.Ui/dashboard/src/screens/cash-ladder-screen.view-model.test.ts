import { describe, expect, it } from "vitest";
import {
  buildCashLadderChart,
  selectActiveScenario,
  selectBucketContributions,
  selectScenarioOptions,
  selectSummaryCards,
  sourcePaletteIndex,
  CASH_LADDER_SOURCE_ORDER
} from "@/screens/cash-ladder-screen.view-model";
import type { PortfolioCashLadder } from "@/types";

function buildLadder(overrides: Partial<PortfolioCashLadder> = {}): PortfolioCashLadder {
  return {
    engineVersion: "portfolio-cash-ladder-v1",
    asOfDate: "2026-07-05",
    horizonDays: 14,
    baseCurrency: "USD",
    scenarioId: "base",
    openingCash: 50000,
    minimumCashThreshold: 10000,
    buckets: [
      {
        bucketStart: "2026-07-05",
        bucketEnd: "2026-07-11",
        label: "Jul 5 – Jul 11",
        inflows: 3500,
        outflows: -1000,
        netFlow: 2500,
        cumulativeCash: 52500,
        breachesMinimumBalance: false,
        sources: [
          { source: "Coupons & interest", amount: 3500, flowCount: 2 },
          { source: "Capital activity", amount: -1000, flowCount: 1 }
        ]
      },
      {
        bucketStart: "2026-07-12",
        bucketEnd: "2026-07-18",
        label: "Jul 12 – Jul 18",
        inflows: 0,
        outflows: -48000,
        netFlow: -48000,
        cumulativeCash: 4500,
        breachesMinimumBalance: true,
        sources: [{ source: "Capital activity", amount: -48000, flowCount: 1 }]
      }
    ],
    contributions: [
      {
        securityId: "sec-1",
        displayName: "Meridian 5.875% 2031",
        assetClass: "Bond",
        projectionRunId: "11111111-2222-3333-4444-555555555555",
        projectionEngineVersion: "asset-obligation-projection-v1",
        termsVersionId: "66666666-7777-8888-9999-000000000000",
        termsHash: "security-master:3",
        sourceDomain: "SecurityMaster",
        sourceEntityId: "sec-1",
        flowType: "Coupon",
        sourceLane: "Coupons & interest",
        dueDate: "2026-07-10",
        amount: 3500,
        currency: "USD",
        baseAmount: 3500,
        scenarioAdjustment: null
      },
      {
        securityId: "00000000-0000-0000-0000-000000000000",
        displayName: "Investor redemption",
        assetClass: "CapitalActivity",
        projectionRunId: null,
        projectionEngineVersion: null,
        termsVersionId: null,
        termsHash: null,
        sourceDomain: "PartnershipLedger",
        sourceEntityId: "inv-1",
        flowType: "Redemption",
        sourceLane: "Capital activity",
        dueDate: "2026-07-14",
        amount: -48000,
        currency: "USD",
        baseAmount: -48000,
        scenarioAdjustment: "Adverse move applied."
      }
    ],
    availableScenarios: [
      {
        scenarioId: "base",
        displayName: "Base projection",
        description: "No stress.",
        modeledEffects: ["Contractual flows"],
        assumptions: ["1:1 FX"]
      },
      {
        scenarioId: "early-call",
        displayName: "Early call",
        description: "Calls exercised.",
        modeledEffects: ["Coupons removed"],
        assumptions: ["Par call"]
      }
    ],
    assumptions: ["Quantity basis assumption"],
    warnings: [],
    generatedAt: "2026-07-05T12:00:00Z",
    securitiesEvaluated: 3,
    securitiesWithFlows: 1,
    ...overrides
  };
}

describe("cash-ladder view model", () => {
  it("returns null chart geometry when the ladder is missing or empty", () => {
    expect(buildCashLadderChart(null)).toBeNull();
    expect(buildCashLadderChart(buildLadder({ buckets: [] }))).toBeNull();
  });

  it("stacks inflow segments above the zero line and outflows below", () => {
    const chart = buildCashLadderChart(buildLadder());

    expect(chart).not.toBeNull();
    expect(chart!.bars).toHaveLength(2);

    const firstBar = chart!.bars[0];
    const inflowSegment = firstBar.segments.find((segment) => segment.source === "Coupons & interest");
    const outflowSegment = firstBar.segments.find((segment) => segment.source === "Capital activity");
    expect(inflowSegment).toBeDefined();
    expect(outflowSegment).toBeDefined();
    expect(inflowSegment!.y + inflowSegment!.height).toBeLessThanOrEqual(chart!.zeroLineY + 0.5);
    expect(outflowSegment!.y).toBeGreaterThanOrEqual(chart!.zeroLineY - 0.5);
  });

  it("renders the minimum-balance threshold band and flags breaching buckets", () => {
    const chart = buildCashLadderChart(buildLadder());

    expect(chart!.thresholdY).not.toBeNull();
    expect(chart!.thresholdBand).not.toBeNull();
    expect(chart!.bars[0].breachesMinimumBalance).toBe(false);
    expect(chart!.bars[1].breachesMinimumBalance).toBe(true);
    expect(chart!.cumulativeMarkers[1].breaches).toBe(true);
  });

  it("keeps the cumulative line monotone with bucket cumulative values", () => {
    const chart = buildCashLadderChart(buildLadder());

    // Bucket 1 cumulative (52,500) is higher than bucket 2 (4,500), so its marker sits higher (smaller y).
    expect(chart!.cumulativeMarkers[0].y).toBeLessThan(chart!.cumulativeMarkers[1].y);
    expect(chart!.cumulativePoints.split(" ")).toHaveLength(2);
  });

  it("drills bucket contributions by inclusive date range with traceability fields", () => {
    const ladder = buildLadder();

    const firstBucketRows = selectBucketContributions(ladder, 0);
    expect(firstBucketRows).toHaveLength(1);
    expect(firstBucketRows[0].instrument).toBe("Meridian 5.875% 2031");
    expect(firstBucketRows[0].projectionRunId).toBe("11111111");
    expect(firstBucketRows[0].projectionEngineVersion).toBe("asset-obligation-projection-v1");
    expect(firstBucketRows[0].termsVersion).toBe("security-master:3");

    const secondBucketRows = selectBucketContributions(ladder, 1);
    expect(secondBucketRows).toHaveLength(1);
    expect(secondBucketRows[0].amountTone).toBe("danger");
    expect(secondBucketRows[0].scenarioAdjustment).toBe("Adverse move applied.");

    expect(selectBucketContributions(ladder, null)).toHaveLength(0);
    expect(selectBucketContributions(ladder, 99)).toHaveLength(0);
  });

  it("summarizes opening cash, flow totals, and breach status", () => {
    const cards = selectSummaryCards(buildLadder());

    expect(cards.map((card) => card.id)).toEqual([
      "opening-cash",
      "projected-inflows",
      "projected-outflows",
      "ending-cash"
    ]);
    expect(cards[0].value).toBe("$50,000");
    expect(cards[1].value).toBe("$3,500");
    expect(cards[2].value).toBe("$49,000");
    expect(cards[3].tone).toBe("danger");
    expect(cards[3].detail).toContain("1 bucket below minimum balance");
  });

  it("falls back to a base scenario option when the ladder has not loaded", () => {
    expect(selectScenarioOptions(null)).toEqual([{ scenarioId: "base", displayName: "Base projection" }]);
    expect(selectActiveScenario(null)).toBeNull();

    const ladder = buildLadder({ scenarioId: "early-call" });
    expect(selectActiveScenario(ladder)?.displayName).toBe("Early call");
    expect(selectScenarioOptions(ladder)).toHaveLength(2);
  });

  it("maps every known source lane to a stable palette slot and unknown lanes to the last slot", () => {
    const indexes = CASH_LADDER_SOURCE_ORDER.map((source) => sourcePaletteIndex(source));
    expect(new Set(indexes).size).toBe(CASH_LADDER_SOURCE_ORDER.length);
    expect(sourcePaletteIndex("Something new")).toBe(CASH_LADDER_SOURCE_ORDER.length - 1);
  });
});
