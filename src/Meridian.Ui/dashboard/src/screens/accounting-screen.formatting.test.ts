import { describe, expect, it } from "vitest";
import { formatCurrencyWithCode } from "./accounting-screen.formatting";

describe("formatCurrencyWithCode", () => {
  it.each([
    [100, "EUR", false, "€100 EUR"],
    [-100, "GBP", false, "-£100 GBP"],
    [100, " eur ", true, "+€100.00 EUR"],
    [-100, "EUR", true, "-€100.00 EUR"],
    [0, "EUR", true, "€0 EUR"],
    [100, "USD", false, "$100 USD"],
    [100, "", false, "$100"],
    [Number.NaN, "EUR", true, "— EUR"]
  ])("formats %s in %s (signed: %s)", (value, currency, signed, expected) => {
    expect(formatCurrencyWithCode(value, currency, signed)).toBe(expected);
  });
});
