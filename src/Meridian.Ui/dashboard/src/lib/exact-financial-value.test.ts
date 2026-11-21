import { currencyMinorUnits, equalExactDecimals, formatExactDecimal, parseExactDecimal, readLegacyFinancialText, sumExactDecimals } from "./exact-financial-value";

describe("exact financial values", () => {
  it("retains decimal digits beyond binary-number precision", () => {
    expect(formatExactDecimal("9007199254740993.125", { currency: "KWD" })).toBe("9,007,199,254,740,993.125");
    expect(sumExactDecimals(["9007199254740993.01", "0.02"])).toBe("9007199254740993.03");
    expect(sumExactDecimals(["0.1", "0.2", "-0.3"])).toBe("0.0");
  });

  it.each([["JPY", 0], ["KWD", 3], ["CLF", 4], ["USD", 2], ["jpy", 0]])("uses shared minor units for %s", (currency, digits) => {
    expect(currencyMinorUnits(currency)).toBe(digits);
  });

  it("rounds display half away from zero, with carries and explicit signs", () => {
    expect(formatExactDecimal("999.995", { currency: "USD" })).toBe("1,000.00");
    expect(formatExactDecimal("-1.005", { currency: "USD" })).toBe("−1.01");
    expect(formatExactDecimal("-0.001", { currency: "USD" })).toBe("0.00");
    expect(formatExactDecimal("1.5", { currency: "JPY", signed: true })).toBe("+2");
    expect(formatExactDecimal("-5", { parens: true })).toBe("(5.00)");
  });

  it.each([null, undefined, "", "—", "1a2", "12,34", "NaN", Infinity, "1e9999"])("keeps missing/malformed input %s out of financial arithmetic", (value) => {
    expect(parseExactDecimal(value)).toBeNull();
    expect(formatExactDecimal(value)).toBeNull();
  });

  it("handles legacy finite numbers and exponent notation without floating arithmetic", () => {
    expect(formatExactDecimal("1e-7", { decimals: 8 })).toBe("0.00000010");
    expect(formatExactDecimal(1e21)).toBe("1,000,000,000,000,000,000,000.00");
    expect(equalExactDecimals("25.000", 25)).toBe(true);
    expect(equalExactDecimals("9007199254740993", 9007199254740992)).toBe(false);
  });

  it("reads only deliberate legacy display syntax without assigning a currency code", () => {
    expect(readLegacyFinancialText("($1,000.25)")).toEqual({ value: "-1000.25", unit: "$" });
    expect(readLegacyFinancialText("-12.34")).toEqual({ value: "-12.34", unit: undefined });
    expect(readLegacyFinancialText("USD 123")).toEqual({ value: null });
  });
});
