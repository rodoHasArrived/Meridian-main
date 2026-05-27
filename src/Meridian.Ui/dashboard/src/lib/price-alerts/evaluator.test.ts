import { describe, expect, it } from "vitest";
import { describeCondition, shouldTrigger } from "./evaluator";

describe("shouldTrigger", () => {
  it("fires 'above' when current price is at or beyond threshold", () => {
    expect(shouldTrigger({ condition: "above", threshold: 100, previousPrice: 99, currentPrice: 100 })).toBe(true);
    expect(shouldTrigger({ condition: "above", threshold: 100, previousPrice: 99, currentPrice: 100.5 })).toBe(true);
    expect(shouldTrigger({ condition: "above", threshold: 100, previousPrice: null, currentPrice: 101 })).toBe(true);
  });

  it("does not fire 'above' below threshold", () => {
    expect(shouldTrigger({ condition: "above", threshold: 100, previousPrice: 90, currentPrice: 99.99 })).toBe(false);
  });

  it("fires 'below' when current price is at or beyond threshold", () => {
    expect(shouldTrigger({ condition: "below", threshold: 50, previousPrice: 51, currentPrice: 50 })).toBe(true);
    expect(shouldTrigger({ condition: "below", threshold: 50, previousPrice: 51, currentPrice: 49 })).toBe(true);
  });

  it("does not fire 'below' above threshold", () => {
    expect(shouldTrigger({ condition: "below", threshold: 50, previousPrice: 60, currentPrice: 50.01 })).toBe(false);
  });

  it("fires 'crosses-up' only when previous was strictly below and current is at/above threshold", () => {
    expect(shouldTrigger({ condition: "crosses-up", threshold: 200, previousPrice: 199.5, currentPrice: 200 })).toBe(true);
    expect(shouldTrigger({ condition: "crosses-up", threshold: 200, previousPrice: 199.5, currentPrice: 200.5 })).toBe(true);
  });

  it("does not fire 'crosses-up' on first observation (no previous price)", () => {
    expect(shouldTrigger({ condition: "crosses-up", threshold: 200, previousPrice: null, currentPrice: 250 })).toBe(false);
  });

  it("does not fire 'crosses-up' when staying above threshold", () => {
    expect(shouldTrigger({ condition: "crosses-up", threshold: 200, previousPrice: 205, currentPrice: 210 })).toBe(false);
  });

  it("fires 'crosses-down' only when previous was strictly above and current is at/below threshold", () => {
    expect(shouldTrigger({ condition: "crosses-down", threshold: 100, previousPrice: 100.5, currentPrice: 100 })).toBe(true);
    expect(shouldTrigger({ condition: "crosses-down", threshold: 100, previousPrice: 100.5, currentPrice: 99 })).toBe(true);
  });

  it("does not fire 'crosses-down' on first observation", () => {
    expect(shouldTrigger({ condition: "crosses-down", threshold: 100, previousPrice: null, currentPrice: 50 })).toBe(false);
  });

  it("ignores non-finite prices and thresholds", () => {
    expect(shouldTrigger({ condition: "above", threshold: 100, previousPrice: 99, currentPrice: Number.NaN })).toBe(false);
    expect(shouldTrigger({ condition: "above", threshold: Number.POSITIVE_INFINITY, previousPrice: 99, currentPrice: 100 })).toBe(false);
  });
});

describe("describeCondition", () => {
  it("describes 'above' condition with last field", () => {
    expect(describeCondition("above", 100, "last")).toContain("≥");
  });

  it("describes 'crosses-up' with helpful phrasing", () => {
    expect(describeCondition("crosses-up", 200, "last")).toContain("crosses up through");
  });

  it("describes 'crosses-down' with helpful phrasing", () => {
    expect(describeCondition("crosses-down", 200, "last")).toContain("crosses down through");
  });
});
