import { describe, expect, it } from "vitest";
import { metricBasisLabel, presentMetricChange } from "./metric-context";

describe("declared metric context", () => {
  it("withholds legacy changes without an interval", () => {
    for (const delta of ["+8%", "0%", "+3", "", " "]) expect(presentMetricChange({ delta })).toBeNull();
  });
  it("preserves declared legacy periods and structured periods", () => {
    expect(presentMetricChange({ delta: "▲ +0.42% · 1D" })).toBe("▲ +0.42% · 1D");
    expect(presentMetricChange({ delta: "+8%", comparisonPeriod: "vs prior completed fiscal month" })).toBe("+8% · vs prior completed fiscal month");
    expect(presentMetricChange({ delta: "+3% vs 7d" })).toBe("+3% vs 7d");
  });
  it("does not invent a basis or as-of date", () => {
    expect(metricBasisLabel({})).toBe("Basis unavailable");
    expect(metricBasisLabel({ basis: "Posted GAAP book", asOf: "2026-11-10" })).toBe("Posted GAAP book · as of 2026-11-10");
  });
});
