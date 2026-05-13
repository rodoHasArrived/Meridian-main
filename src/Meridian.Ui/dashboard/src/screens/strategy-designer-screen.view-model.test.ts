import { describe, expect, it } from "vitest";
import {
  buildLegFromPalette,
  buildDesignerSummaryMetrics,
  buildParticipationViewModel,
  buildPayoffChartViewModel,
  buildPayoffSeries,
  computeLegPayoff,
  computePortfolioPayoff,
  findBreakEvenPrices,
  getStrategyDesignerPalette,
  getStrategyDesignerSampleLegs,
  reorderLegs,
  type StrategyLeg
} from "@/screens/strategy-designer-screen.view-model";

const longCall100: StrategyLeg = {
  id: "leg-1",
  kind: "long-call",
  label: "Long Call 100",
  direction: "Long",
  instrument: "Call",
  quantity: 1,
  strike: 100,
  premium: 4
};

const shortCall110: StrategyLeg = {
  id: "leg-2",
  kind: "short-call",
  label: "Short Call 110",
  direction: "Short",
  instrument: "Call",
  quantity: 1,
  strike: 110,
  premium: 2
};

const longStock: StrategyLeg = {
  id: "stock-1",
  kind: "long-stock",
  label: "Long Stock",
  direction: "Long",
  instrument: "Stock",
  quantity: 100,
  strike: 100,
  premium: 0
};

describe("strategy designer view-model", () => {
  it("returns six palette entries with stable kinds", () => {
    const palette = getStrategyDesignerPalette();
    expect(palette).toHaveLength(6);
    expect(palette.map((entry) => entry.kind)).toEqual([
      "long-call",
      "short-call",
      "long-put",
      "short-put",
      "long-stock",
      "short-stock"
    ]);
  });

  it("computes long call payoff: intrinsic - premium scaled by quantity and direction", () => {
    expect(computeLegPayoff(longCall100, 90)).toBeCloseTo(-4, 6);
    expect(computeLegPayoff(longCall100, 100)).toBeCloseTo(-4, 6);
    expect(computeLegPayoff(longCall100, 104)).toBeCloseTo(0, 6);
    expect(computeLegPayoff(longCall100, 120)).toBeCloseTo(16, 6);
  });

  it("computes short call payoff as the negative of long call", () => {
    expect(computeLegPayoff(shortCall110, 110)).toBeCloseTo(2, 6);
    expect(computeLegPayoff(shortCall110, 115)).toBeCloseTo(-3, 6);
  });

  it("computes long stock payoff linearly", () => {
    expect(computeLegPayoff(longStock, 110)).toBeCloseTo(1000, 6);
    expect(computeLegPayoff(longStock, 90)).toBeCloseTo(-1000, 6);
  });

  it("aggregates portfolio payoff across legs (bull call spread)", () => {
    const legs = [longCall100, shortCall110];
    expect(computePortfolioPayoff(legs, 90)).toBeCloseTo(-4 + 2, 6);
    expect(computePortfolioPayoff(legs, 110)).toBeCloseTo(6 + 2, 6);
    expect(computePortfolioPayoff(legs, 120)).toBeCloseTo(16 + -8, 6);
  });

  it("builds payoff chart view-model with polyline points spanning leg strikes", () => {
    const payoff = buildPayoffChartViewModel([longCall100, shortCall110], 100);
    expect(payoff.isEmpty).toBe(false);
    expect(payoff.points.length).toBeGreaterThan(50);
    expect(payoff.points[0].x).toBeCloseTo(payoff.paddingLeft, 1);
    expect(payoff.points[payoff.points.length - 1].x).toBeCloseTo(
      payoff.width - payoff.paddingRight,
      1
    );
    expect(payoff.maxProfit).toBeGreaterThan(0);
    expect(payoff.maxLoss).toBeLessThan(0);
    expect(payoff.breakEvenPrices.length).toBeGreaterThanOrEqual(1);
  });

  it("marks empty payoff view-model when there are no legs", () => {
    const payoff = buildPayoffChartViewModel([], 100);
    expect(payoff.isEmpty).toBe(true);
    expect(payoff.points).toEqual([]);
    expect(payoff.breakEvenPrices).toEqual([]);
    expect(payoff.caption).toContain("Add a leg");
  });

  it("locates break-even price near the long-call strike + premium", () => {
    const series = buildPayoffSeries([longCall100], 100);
    const crossings = findBreakEvenPrices(series);
    expect(crossings.length).toBeGreaterThanOrEqual(1);
    expect(crossings[0]).toBeGreaterThan(103);
    expect(crossings[0]).toBeLessThan(105);
  });

  it("builds participation view-model with normalised notional shares", () => {
    const participation = buildParticipationViewModel([longCall100, shortCall110], 100);
    expect(participation.isEmpty).toBe(false);
    expect(participation.slices).toHaveLength(2);
    const total = participation.slices.reduce((sum, slice) => sum + slice.share, 0);
    expect(total).toBeCloseTo(1, 5);
    const long = participation.slices.find((s) => s.legId === "leg-1");
    const short = participation.slices.find((s) => s.legId === "leg-2");
    expect(long?.tone).toBe("long");
    expect(short?.tone).toBe("short");
  });

  it("computes net direction from notional weights", () => {
    const longBias = buildParticipationViewModel([longCall100, longStock], 100);
    expect(longBias.netDirection).toBe("Long");
    expect(longBias.netDirectionLabel).toContain("Long-biased");

    const balanced = buildParticipationViewModel(
      [
        { ...longCall100, id: "a" },
        { ...longCall100, id: "b", direction: "Short" }
      ],
      100
    );
    expect(balanced.netDirection).toBe("Flat");
  });

  it("summarizes designer metrics with tones derived from payoff numbers", () => {
    const payoff = buildPayoffChartViewModel([longCall100, shortCall110], 100);
    const participation = buildParticipationViewModel([longCall100, shortCall110], 100);
    const metrics = buildDesignerSummaryMetrics(payoff, participation);
    expect(metrics.map((m) => m.id)).toEqual([
      "max-profit",
      "max-loss",
      "net-debit",
      "net-direction"
    ]);
    expect(metrics[0].tone).toBe("success");
    expect(metrics[1].tone).toBe("danger");
  });

  it("falls back to default-toned placeholders when there are no legs", () => {
    const payoff = buildPayoffChartViewModel([], 100);
    const participation = buildParticipationViewModel([], 100);
    const metrics = buildDesignerSummaryMetrics(payoff, participation);
    expect(metrics.every((metric) => metric.tone === "default")).toBe(true);
    expect(metrics[0].value).toBe("—");
  });

  it("reorders legs by removing source and inserting at target position", () => {
    const legs = [longCall100, shortCall110, longStock];
    const reordered = reorderLegs(legs, longStock.id, longCall100.id);
    expect(reordered.map((leg) => leg.id)).toEqual([longStock.id, longCall100.id, shortCall110.id]);
  });

  it("returns input unchanged when reorder ids are equal or missing", () => {
    const legs = [longCall100, shortCall110];
    expect(reorderLegs(legs, longCall100.id, longCall100.id)).toBe(legs);
    expect(reorderLegs(legs, "missing", longCall100.id)).toBe(legs);
  });

  it("builds palette legs with unique ids and ordinal-aware labels", () => {
    const palette = getStrategyDesignerPalette();
    const entry = palette[0];
    const first = buildLegFromPalette(entry, []);
    const second = buildLegFromPalette(entry, [first]);
    expect(first.id).not.toBe(second.id);
    expect(first.kind).toBe(entry.kind);
    expect(second.label).toContain("(2)");
  });

  it("provides a sample strategy with two legs", () => {
    const sample = getStrategyDesignerSampleLegs();
    expect(sample).toHaveLength(2);
    expect(sample[0].direction).toBe("Long");
    expect(sample[1].direction).toBe("Short");
  });
});
