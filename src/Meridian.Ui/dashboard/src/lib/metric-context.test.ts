import { describe, expect, it } from "vitest";
import { hasDeclaredMetricBasis, metricBasisLabel, metricBasisValue, presentMetricChange } from "./metric-context";

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
    expect(metricBasisLabel({})).toBe("Basis not declared by source");
    expect(metricBasisLabel({ basis: "Posted GAAP book", asOf: "2026-11-10" })).toBe("Posted GAAP book · as of 2026-11-10");
  });
  it("states an undeclared basis in non-failure words", () => {
    expect(metricBasisValue({})).toBe("Not declared by source");
    expect(metricBasisValue({ asOf: "2026-11-10" })).toBe("Not declared by source · as of 2026-11-10");
    expect(metricBasisValue({ basis: " Posted GAAP book ", asOf: "2026-11-10" })).toBe("Posted GAAP book · as of 2026-11-10");
    expect(metricBasisLabel({ basis: "  ", asOf: "2026-11-10" })).toBe("Basis not declared by source · as of 2026-11-10");
    for (const basis of [undefined, null, "", "  "]) expect(hasDeclaredMetricBasis({ basis })).toBe(false);
    expect(hasDeclaredMetricBasis({ basis: "Posted GAAP book" })).toBe(true);
  });
});
