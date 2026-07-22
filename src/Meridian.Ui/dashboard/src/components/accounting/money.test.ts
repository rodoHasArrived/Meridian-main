import { currencySymbol, formatMoney, sumAmounts, toNumber } from "./money";

// Currency codes without a mapped glyph render as "CODE" + a narrow no-break space (U+202F),
// matching the Concrete thin-separator number convention.
const NNBSP = " ";

describe("money.toNumber", () => {
  it("passes through finite numbers", () => {
    expect(toNumber(1234.5)).toBe(1234.5);
    expect(toNumber(0)).toBe(0);
  });

  it("reads accounting parentheses as negative", () => {
    expect(toNumber("(1,234.00)")).toBe(-1234);
    expect(toNumber("($921.00)")).toBe(-921);
  });

  it("parses signed and separated strings", () => {
    expect(toNumber("-921.00")).toBe(-921);
    expect(toNumber("12,000.25")).toBe(12000.25);
    expect(toNumber("−45.5")).toBe(-45.5); // unicode minus
  });

  it("returns NaN for blank / unparseable input", () => {
    expect(Number.isNaN(toNumber(""))).toBe(true);
    expect(Number.isNaN(toNumber(null))).toBe(true);
    expect(Number.isNaN(toNumber(undefined))).toBe(true);
  });
});

describe("money.formatMoney", () => {
  it("renders negatives with parentheses when parens is set", () => {
    expect(formatMoney(-1234, { parens: true })).toBe("(1,234.00)");
    expect(formatMoney(-1234, { currency: "USD", parens: true })).toBe("($1,234.00)");
  });

  it("renders negatives with a unicode minus by default", () => {
    expect(formatMoney(-42.5)).toBe("−42.50");
  });

  it("renders exact zero as an em dash when zeroDash is set", () => {
    expect(formatMoney(0, { zeroDash: true })).toBe("—");
    expect(formatMoney(0)).toBe("0.00");
  });

  it("adds an explicit plus on positive deltas when signed", () => {
    expect(formatMoney(18.4, { signed: true })).toBe("+18.40");
    expect(formatMoney(-18.4, { signed: true })).toBe("−18.40");
  });

  it("honours the decimals option (prices use 4dp)", () => {
    expect(formatMoney(101.2, { decimals: 4 })).toBe("101.2000");
  });

  it("prefixes the currency symbol (unknown codes use a narrow no-break space)", () => {
    expect(formatMoney(5, { currency: "EUR" })).toBe("€5.00");
    expect(formatMoney(5, { currency: "ZZZ" })).toBe(`ZZZ${NNBSP}5.00`);
  });
});

describe("money.sumAmounts", () => {
  it("sums money-ish values and ignores blanks/NaN", () => {
    expect(sumAmounts([100, "(50.00)", "", null, "25.50"])).toBeCloseTo(75.5, 6);
  });
});

describe("money.currencySymbol", () => {
  it("maps known codes and falls back to a spaced prefix", () => {
    expect(currencySymbol("USD")).toBe("$");
    expect(currencySymbol("GBP")).toBe("£");
    expect(currencySymbol()).toBe("");
    expect(currencySymbol("XYZ")).toBe(`XYZ${NNBSP}`);
  });
});
