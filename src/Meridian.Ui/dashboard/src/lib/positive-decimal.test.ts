import { describe, expect, it } from "vitest";
import { parsePositiveDecimal } from "@/lib/positive-decimal";

describe("parsePositiveDecimal", () => {
  it.each([
    ["100", 100],
    [" 25 ", 25],
    ["188.05", 188.05],
    ["10.", 10],
    [".5", 0.5],
    ["007", 7]
  ])("accepts plain decimal text %j", (text, expected) => {
    expect(parsePositiveDecimal(text)).toBe(expected);
  });

  it.each([
    "1e3", "1E3", "0x10", "0b11", "0o7", "+5", "-5", "1,000", "1_000", "1 000",
    "Infinity", "NaN", "", "   ", ".", "0", "0.00", "1.2.3", "12abc", "$5"
  ])("rejects malformed or non-positive text %j", (text) => {
    expect(parsePositiveDecimal(text)).toBeNull();
  });

  it("rejects missing values and digit runs that overflow to Infinity", () => {
    expect(parsePositiveDecimal(null)).toBeNull();
    expect(parsePositiveDecimal(undefined)).toBeNull();
    expect(parsePositiveDecimal("9".repeat(400))).toBeNull();
  });
});
