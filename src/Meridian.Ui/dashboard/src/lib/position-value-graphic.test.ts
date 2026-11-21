import { describe, expect, it } from "vitest";
import { measureDisplayedPositionAmounts, parseDisplayedPositionAmount } from "@/lib/position-value-graphic";

describe("displayed position amount graphics", () => {
  it("recognizes demonstrated invariant grouped and abbreviated formats without inferring a currency code", () => {
    expect(parseDisplayedPositionAmount("$18,900")).toEqual({ units: 1_890_000n, denomination: "$" });
    expect(parseDisplayedPositionAmount("-$1.25K")).toEqual({ units: -125_000n, denomination: "$" });
    expect(parseDisplayedPositionAmount("+$2.1M")).toEqual({ units: 210_000_000n, denomination: "$" });
    expect(parseDisplayedPositionAmount("-$0")).toEqual({ units: 0n, denomination: "$" });
  });

  it("withholds missing, malformed, ambiguous locale, and other denomination values", () => {
    for (const value of ["", "—", "NaN", "$NaN", "Infinity", "18,900", "USD 100", "€1,000", "$1,2", "$1.234,56", "$100 trailing", " $100", "$01", "$0.001"]) {
      expect(parseDisplayedPositionAmount(value), value).toBeNull();
    }
  });

  it("uses separate magnitudes and retains loss direction, zero and missingness", () => {
    expect(measureDisplayedPositionAmounts(["+$100", "-$200", "$0", "—"])).toEqual([
      { magnitudePct: 50, direction: "positive", isLargest: false, comparableCount: 3, denomination: "$" },
      { magnitudePct: 100, direction: "negative", isLargest: true, comparableCount: 3, denomination: "$" },
      { magnitudePct: 0, direction: "zero", isLargest: false, comparableCount: 3, denomination: "$" },
      null
    ]);
    expect(measureDisplayedPositionAmounts(["$0", "-$0"])[0]).toMatchObject({ magnitudePct: 0, isLargest: false });
  });

  it("keeps explicit source currencies and unknown currency in separate scale groups", () => {
    const measured = measureDisplayedPositionAmounts(
      ["$100", "$200", "$1,000", "$50", "$500", "$250"],
      ["USD", "USD", "CAD", "CAD", null, undefined]
    );
    expect(measured.map((item) => item?.magnitudePct)).toEqual([50, 100, 100, 5, 100, 50]);
    expect(measured.map((item) => item?.currency)).toEqual(["USD", "USD", "CAD", "CAD", null, null]);
    expect(measured.every((item) => item?.comparableCount === 2)).toBe(true);
  });

  it("scales huge displayed values exactly before converting only the bounded ratio", () => {
    const measured = measureDisplayedPositionAmounts(["$9007199254740992", "$18014398509481984"]);
    expect(measured[0]?.magnitudePct).toBe(50);
    expect(measured[1]?.magnitudePct).toBe(100);
  });
});
