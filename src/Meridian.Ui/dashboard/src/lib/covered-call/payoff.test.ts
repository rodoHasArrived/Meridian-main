import { describe, expect, it } from "vitest";
import {
  buildCoveredCallPayoffCurves,
  coveredCallBreakEven,
  coveredCallPayoff,
  shortCallPayoff,
  underlyingPayoff
} from "@/lib/covered-call/payoff";

describe("payoff math", () => {
  const inputs = {
    strike: 100,
    entryCredit: 2,
    contracts: 1,
    multiplier: 100,
    underlyingShares: 100,
    underlyingCostBasis: 90
  };

  it("shortCallPayoff = credit when spot below strike (out-of-the-money)", () => {
    expect(shortCallPayoff(95, inputs)).toBe(200); // credit * 1 * 100
  });

  it("shortCallPayoff loses 1:1 with spot above strike", () => {
    // at spot = 105: credit 2 minus intrinsic 5 = -3 per share * 100 = -300
    expect(shortCallPayoff(105, inputs)).toBe(-300);
  });

  it("underlyingPayoff equals (spot - basis) * shares", () => {
    expect(underlyingPayoff(95, inputs)).toBe(500);
    expect(underlyingPayoff(85, inputs)).toBe(-500);
  });

  it("coveredCallPayoff caps at strike + credit", () => {
    const atStrike = coveredCallPayoff(100, inputs);
    const above = coveredCallPayoff(120, inputs);
    expect(atStrike).toBe(above); // capped — above-strike spots add 0 net
    // expected = (100 - 90) * 100 + 2 * 1 * 100 = 1000 + 200 = 1200
    expect(atStrike).toBe(1200);
  });

  it("coveredCallBreakEven returns basis minus premium per share", () => {
    // basis 90, premium per share = 2 * 1 * 100 / 100 = 2
    expect(coveredCallBreakEven(inputs)).toBe(88);
  });

  it("buildCoveredCallPayoffCurves returns aligned series", () => {
    const curves = buildCoveredCallPayoffCurves(inputs, 80, 120, 5);
    expect(curves.spots).toHaveLength(5);
    expect(curves.underlying).toHaveLength(5);
    expect(curves.shortCall).toHaveLength(5);
    expect(curves.covered).toHaveLength(5);

    // First spot is min, last is max
    expect(curves.spots[0]).toBe(80);
    expect(curves.spots[4]).toBe(120);

    // Combined series equals underlying + shortCall point-wise
    for (let i = 0; i < curves.spots.length; i++) {
      const combined = curves.underlying[i].payoff + curves.shortCall[i].payoff;
      expect(curves.covered[i].payoff).toBeCloseTo(combined, 6);
    }
  });

  it("buildCoveredCallPayoffCurves returns empty for invalid ranges", () => {
    const curves = buildCoveredCallPayoffCurves(inputs, 100, 100);
    expect(curves.spots).toHaveLength(0);
  });
});
