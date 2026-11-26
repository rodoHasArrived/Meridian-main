import { describe, expect, it } from "vitest";
import { candidateLeg, comparableBasket, emptyAssessment, normalizeOptionDecimal, optionCoincidentGroups } from "./options-screen.view-model";
import type { OptionScreenCandidate } from "@/types/option-visualization.types";

export const optionCandidate: OptionScreenCandidate = { id: "call-a", symbol: "XYZ-C100", underlyingSymbol: "XYZ", right: "Call",
  expiration: "2027-01-15", daysToExpiration: 64, strike: "100", delta: "0.3", bid: "1", ask: "1.1", mid: "1.05",
  spreadFraction: "0.095238", openInterest: 1000, volume: 200, multiplier: "100", currency: "USD", exchange: "SMART",
  style: "American", source: "synthetic", observedAt: "2026-11-12T12:00:00Z", passesScreen: true, rejectionReasons: [] };

describe("general options evidence semantics", () => {
  it("preserves signed delta, exact decimal identity and unknown deliverables", () => {
    expect(normalizeOptionDecimal("0001.2300")).toBe("1.23");
    expect(normalizeOptionDecimal("-0.000")).toBe("0");
    expect(normalizeOptionDecimal("123.1234567890123456789")).toBe("123.1234567890123456789");
    expect(candidateLeg(optionCandidate, "a").deliverable).toBe("");
  });
  it("matches formatting equivalence but treats side, intent, ratio and deliverable as exact basket terms", () => {
    const basket = { quantity: "1", orderType: "Limit" as const, priceEffect: "Credit" as const, limitPrice: "2", currency: "USD", legs: [candidateLeg(optionCandidate, "a")] };
    expect(comparableBasket(basket)).toBe(comparableBasket({ ...basket, quantity: "01.00", limitPrice: "2.00" }));
    for (const replacement of [{ side: "Sell" as const }, { positionIntent: "Close" as const }, { ratio: "2" }, { deliverable: "adjusted" }])
      expect(comparableBasket(basket)).not.toBe(comparableBasket({ ...basket, legs: [{ ...basket.legs[0], ...replacement }] }));
    expect(emptyAssessment("account", basket).outcome).toBe("NotReported");
  });
  it("groups true coincident points without jitter and retains absent delta for tabular investigation", () => {
    const groups = optionCoincidentGroups([optionCandidate, { ...optionCandidate, id: "call-b", delta: "0.300", strike: "101" }, { ...optionCandidate, id: "missing", delta: null }]);
    expect(groups).toHaveLength(1); expect(groups[0]).toHaveLength(2);
  });
});
