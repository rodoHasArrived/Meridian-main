import { describe, expect, it } from "vitest";
import { computeVariance, roundDecimal, varianceMagnitude } from "./variance";

describe("computeVariance", () => {
  it("is current minus prior, exactly, in decimal strings", () => {
    expect(computeVariance("120500.10", "100000.05")).toEqual({ change: "20500.05", percent: "20.50" });
    expect(computeVariance("0.3", "0.1")).toEqual({ change: "0.2", percent: "200.00" });
    expect(computeVariance(150, 200)).toEqual({ change: "-50", percent: "-25.00" });
  });

  it("divides by the absolute prior, so the sign follows the variance", () => {
    // A loss that shrinks is an improvement: positive variance, positive percent.
    expect(computeVariance("-50", "-200")).toEqual({ change: "150", percent: "75.00" });
    // A gain that turns to a loss: negative variance, negative percent.
    expect(computeVariance("-100", "100")).toEqual({ change: "-200", percent: "-200.00" });
  });

  it("has no percentage when the prior is zero", () => {
    expect(computeVariance("25", "0")).toEqual({ change: "25", percent: null });
    expect(computeVariance("0", "0.00")).toEqual({ change: "0.00", percent: null });
  });

  it("rounds the percentage half away from zero to two places", () => {
    expect(computeVariance("1", "3")).toEqual({ change: "-2", percent: "-66.67" });
    expect(computeVariance("2", "3")).toEqual({ change: "-1", percent: "-33.33" });
    expect(computeVariance("1.00005", "1")).toMatchObject({ percent: "0.01" });
    expect(computeVariance("0.99995", "1")).toMatchObject({ percent: "-0.01" });
  });

  it("returns nothing for a missing or malformed figure", () => {
    expect(computeVariance(null, "1")).toEqual({ change: null, percent: null });
    expect(computeVariance("1", undefined)).toEqual({ change: null, percent: null });
    expect(computeVariance("1,000", "1")).toEqual({ change: null, percent: null });
    expect(computeVariance(Number.NaN, 1)).toEqual({ change: null, percent: null });
  });

  it("reads numbers that print in exponent form", () => {
    expect(computeVariance(1e-7, 0)).toEqual({ change: "0.0000001", percent: null });
    expect(computeVariance(2e21, 1e21)).toEqual({ change: "1000000000000000000000", percent: "100.00" });
  });
});

describe("varianceMagnitude", () => {
  it("is the absolute value for threshold tests", () => {
    expect(varianceMagnitude("-150000.25")).toBe(150000.25);
    expect(varianceMagnitude(null)).toBeNull();
  });
});

describe("roundDecimal", () => {
  it("rounds half away from zero without floating point", () => {
    expect(roundDecimal("31.818181818", 2)).toBe("31.82");
    expect(roundDecimal("-0.005", 2)).toBe("-0.01");
    expect(roundDecimal("2.5", 0)).toBe("3");
    expect(roundDecimal("7", 2)).toBe("7.00");
    expect(roundDecimal("n/a", 2)).toBeNull();
  });
});
