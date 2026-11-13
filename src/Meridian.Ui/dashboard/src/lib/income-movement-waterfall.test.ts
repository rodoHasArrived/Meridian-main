import { describe, expect, it } from "vitest";
import { buildIncomeWaterfall, formatIncomeAmount } from "@/lib/income-movement-waterfall";
import { buildIncomeComparison } from "@/test/reporting-income-comparison-fixtures";

describe("retained income waterfall", () => {
  it("bridges retained groups in order with a floating negative methodology change", () => {
    const model = buildIncomeWaterfall(buildIncomeComparison());
    expect(model.reason).toBeNull();
    expect(model.stages.map((item) => item.label)).toEqual(["Baseline", "Journal changes", "Population changes", "Methodology changes", "Unexplained difference", "Current"]);
    expect(model.stages.map((item) => item.amount)).toEqual(["1000", "125", "30", "-5", "0", "1150"]);
    expect(model.stages[3].endPct).toBeLessThan(model.stages[3].startPct);
    expect(model.stages[1].endPct).toBe(model.stages[2].startPct);
    expect(model.stages[4].endPct).toBe(model.stages[5].endPct);
    expect(model.zeroPct).toBe(0);
  });

  it("uses the residual once while preserving offsetting unexplained records separately", () => {
    const retained = buildIncomeComparison();
    const contributions = [
      ...retained.contributions,
      { ...retained.contributions[0], contributionId: "unsupported-positive", kind: "Unexplained", amount: "67" },
      { ...retained.contributions[0], contributionId: "unsupported-negative", kind: "Unexplained", amount: "-25" }
    ];
    const model = buildIncomeWaterfall(buildIncomeComparison({ contributions, currentAmount: "1192", movement: "192", residualAmount: "42", status: "Unexplained" }));
    expect(model.reason).toBeNull();
    expect(model.stages[4].amount).toBe("42");
    expect(model.stages[4].contributions.map((item) => item.amount)).toEqual(["67", "-25"]);
    expect(model.stages[4].endPct).toBe(model.stages[5].endPct);
  });

  it("verifies arithmetic beyond Number precision including fractional differences", () => {
    const base = buildIncomeComparison();
    const model = buildIncomeWaterfall(buildIncomeComparison({
      baselineAmount: "9007199254740993.000000000000000000000001",
      currentAmount: "9007199254740993.000000000000000000000003",
      movement: "0.000000000000000000000002",
      explainedAmount: "0.000000000000000000000002",
      contributions: [{ ...base.contributions[0], amount: "0.000000000000000000000002" }]
    }));
    expect(model.reason).toBeNull();
    expect(formatIncomeAmount(model.stages[1].amount, "USD", true)).toBe("+0.000000000000000000000002 USD");
    expect(formatIncomeAmount(model.stages[5].amount, "USD")).toBe("9,007,199,254,740,993.000000000000000000000003 USD");
  });

  it("rejects a discrepancy too small for floating-point equality", () => {
    const c = buildIncomeComparison({ baselineAmount: "9007199254740993", currentAmount: "9007199254741143.000000000000000000000001" });
    expect(buildIncomeWaterfall(c).reason).toMatch(/consistent bridge/);
  });

  it.each([
    { compatible: false },
    { current: { ...buildIncomeComparison().current, currency: "EUR" } },
    { residualAmount: null },
    { movement: "149" },
    { explainedAmount: "149" },
    { contributions: [] },
    { baselineAmount: "Infinity" }
  ])("withholds an incompatible, incomplete or inconsistent bridge: %j", (overrides) => {
    const model = buildIncomeWaterfall(buildIncomeComparison(overrides));
    expect(model.reason).not.toBeNull();
    expect(model.stages).toEqual([]);
  });

  it("supports negative income, zero crossing, and all-zero bridges without nonfinite geometry", () => {
    for (const [baseline, current, delta] of [["-100", "25", "125"], ["0", "0", "-0.00"]]) {
      const model = buildIncomeWaterfall(buildIncomeComparison({ baselineAmount: baseline, currentAmount: current, movement: delta, explainedAmount: "0", residualAmount: delta, contributions: [] }));
      expect(model.reason).toBeNull();
      for (const stage of model.stages) {
        expect(Number.isFinite(stage.startPct)).toBe(true);
        expect(Number.isFinite(stage.endPct)).toBe(true);
      }
      expect(model.zeroPct).toBeGreaterThanOrEqual(0);
      expect(model.zeroPct).toBeLessThanOrEqual(100);
    }
    expect(formatIncomeAmount("-0.00", "USD", true)).toBe("0.00 USD");
  });
});
