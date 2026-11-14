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

  it("preserves trailing statement minus signs", () => {
    expect(toNumber("1,234.00-")).toBe(-1234);
    expect(toNumber(" $1,234.00− ")).toBe(-1234);
    expect(toNumber("921.00 -")).toBe(-921);
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

  it("balances a credit with a trailing minus against its debit", () => {
    expect(sumAmounts(["1,234.00", "1,234.00-"])).toBe(0);
    expect(formatMoney("1,234.00-", { parens: true })).toBe("(1,234.00)");
  });
});

describe("money exact decimals", () => {
  it("formats a decimal string from its digits, not through a float", () => {
    // 12345678901234.565 has no exact binary float; parseFloat would round it to ...234.56.
    expect(formatMoney("12345678901234.565")).toBe("12,345,678,901,234.57");
    expect(formatMoney("-0.005", { parens: true })).toBe("(0.01)");
    expect(formatMoney("1.005")).toBe("1.01");
  });

  it("keeps tiny and exponent values exact and shows a value rounded to zero unsigned", () => {
    expect(formatMoney(1e-7, { decimals: 8 })).toBe("0.00000010");
    expect(formatMoney("1.5e-3", { decimals: 4 })).toBe("0.0015");
    expect(formatMoney(-0.001)).toBe("0.00");
    expect(formatMoney("0.00", { signed: true })).toBe("0.00");
  });

  it("sums decimal strings exactly", () => {
    expect(sumAmounts(["0.1", "0.2"])).toBe(0.3);
    expect(sumAmounts(["1,000,000.01", "(1,000,000.01)"])).toBe(0);
  });

  it("reads an amount with a currency suffix whose code contains E", () => {
    expect(toNumber("1,234.56 EUR")).toBe(1234.56);
    expect(toNumber("(1,234.56) EUR")).toBe(-1234.56);
    expect(formatMoney("1,234.56 EUR", { parens: true })).toBe("1,234.56");
    expect(sumAmounts(["100.00 EUR", "50.25 EUR"])).toBe(150.25);
  });

  it("reads whitespace group separators and a minus after any currency symbol", () => {
    expect(toNumber("1 234.56")).toBe(1234.56);
    expect(toNumber("1\u00a0234.56")).toBe(1234.56);
    expect(toNumber("1\u202f234.56")).toBe(1234.56);
    expect(formatMoney("1 234.56")).toBe("1,234.56");
    expect(toNumber("₹-1,234.56")).toBe(-1234.56);
    expect(toNumber("CHF -5")).toBe(-5);
    expect(toNumber("$-5")).toBe(-5);
    expect(toNumber("-$5")).toBe(-5);
  });

  it("treats an exponent beyond any money value as invalid instead of throwing", () => {
    expect(Number.isNaN(toNumber("1e9999999999"))).toBe(true);
    expect(formatMoney("1e-9999999999")).toBe("1e-9999999999");
    expect(toNumber("2.5e3")).toBe(2500);
  });

  it("rejects text that is not a number", () => {
    expect(Number.isNaN(toNumber("n/a"))).toBe(true);
    expect(formatMoney("Not supplied")).toBe("Not supplied");
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
