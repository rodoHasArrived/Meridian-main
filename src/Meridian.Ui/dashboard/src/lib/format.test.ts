import { describe, expect, it } from "vitest";
import {
  formatAmountWithCode,
  formatCompactCurrency,
  formatCurrency,
  formatNumber,
  formatPercent,
  formatPrefixedCurrency,
  formatRatioAsPercent,
  formatSignedCurrency,
  pluralizeCount
} from "@/lib/format";

describe("formatNumber", () => {
  it("groups thousands and caps fraction digits at two by default", () => {
    expect(formatNumber(1234567.891)).toBe("1,234,567.89");
    expect(formatNumber(1234.5)).toBe("1,234.5");
  });

  it("honours explicit fraction digit bounds", () => {
    expect(formatNumber(12, { minimumFractionDigits: 2 })).toBe("12.00");
    expect(formatNumber(0.123456, { maximumFractionDigits: 4 })).toBe("0.1235");
  });

  it("renders the fallback for null, undefined, and non-finite values", () => {
    expect(formatNumber(null)).toBe("—");
    expect(formatNumber(undefined)).toBe("—");
    expect(formatNumber(Number.NaN, { fallback: "0" })).toBe("0");
  });
});

describe("formatPercent", () => {
  it("appends a percent sign to values already in percent units", () => {
    expect(formatPercent(42.5)).toBe("42.5%");
    expect(formatPercent(0)).toBe("0%");
  });

  it("renders the fallback for non-finite values", () => {
    expect(formatPercent(null, { fallback: "Not measured" })).toBe("Not measured");
  });
});

describe("formatRatioAsPercent", () => {
  it("scales fraction-unit values into percent display", () => {
    expect(formatRatioAsPercent(0.425)).toBe("42.5%");
    expect(formatRatioAsPercent(1)).toBe("100%");
    expect(formatRatioAsPercent(0)).toBe("0%");
  });

  it("differs from formatPercent by exactly 100x for the same input", () => {
    expect(formatPercent(0.425)).toBe("0.43%");
    expect(formatRatioAsPercent(0.425)).toBe("42.5%");
  });

  it("honours fraction digit bounds and the fallback", () => {
    expect(formatRatioAsPercent(0.12345, { maximumFractionDigits: 1 })).toBe("12.3%");
    expect(formatRatioAsPercent(null, { fallback: "Unavailable" })).toBe("Unavailable");
    expect(formatRatioAsPercent(Number.NaN, { fallback: "Unavailable" })).toBe("Unavailable");
  });
});

describe("formatCurrency", () => {
  it("formats USD with symbol and cents by default", () => {
    expect(formatCurrency(1234.5)).toBe("$1,234.50");
    expect(formatCurrency(-1234.5)).toBe("-$1,234.50");
  });

  it("formats other ISO codes", () => {
    expect(formatCurrency(1234.5, { currency: "EUR" })).toBe("€1,234.50");
  });

  it("supports whole-dollar rendering", () => {
    expect(formatCurrency(1234.56, { maximumFractionDigits: 0 })).toBe("$1,235");
  });

  it("supports optional cents", () => {
    expect(formatCurrency(1200, { minimumFractionDigits: 0 })).toBe("$1,200");
    expect(formatCurrency(1200.5, { minimumFractionDigits: 0 })).toBe("$1,200.5");
  });

  it("falls back to a code-prefixed rendering for unknown codes", () => {
    expect(formatCurrency(1234.56, { currency: "??" })).toBe("?? 1,234.56");
  });

  it("treats blank codes as USD", () => {
    expect(formatCurrency(5, { currency: "" })).toBe("$5.00");
  });

  it("renders the fallback for non-finite values", () => {
    expect(formatCurrency(null)).toBe("—");
    expect(formatCurrency(Number.NaN, { fallback: "$0.00" })).toBe("$0.00");
  });
});

describe("formatPrefixedCurrency", () => {
  it("prefixes the sign ahead of the dollar symbol", () => {
    expect(formatPrefixedCurrency(1234.567)).toBe("$1,234.57");
    expect(formatPrefixedCurrency(-1234.567)).toBe("-$1,234.57");
  });

  it("supports whole-dollar and fixed-cent renderings", () => {
    expect(formatPrefixedCurrency(1234.56, { maximumFractionDigits: 0 })).toBe("$1,235");
    expect(formatPrefixedCurrency(5, { minimumFractionDigits: 2 })).toBe("$5.00");
  });

  it("renders the fallback for non-finite values", () => {
    expect(formatPrefixedCurrency(Number.POSITIVE_INFINITY)).toBe("—");
  });
});

describe("formatSignedCurrency", () => {
  it("always renders an explicit sign", () => {
    expect(formatSignedCurrency(1234.5, { minimumFractionDigits: 2 })).toBe("+$1,234.50");
    expect(formatSignedCurrency(-1234.5, { minimumFractionDigits: 2 })).toBe("-$1,234.50");
  });

  it("treats zero as positive unless a zero label is provided", () => {
    expect(formatSignedCurrency(0, { maximumFractionDigits: 0 })).toBe("+$0");
    expect(formatSignedCurrency(0, { zeroLabel: "$0" })).toBe("$0");
  });

  it("renders the fallback for non-finite values", () => {
    expect(formatSignedCurrency(null)).toBe("—");
  });
});

describe("formatCompactCurrency", () => {
  it("abbreviates thousands and millions", () => {
    expect(formatCompactCurrency(1_234_567)).toBe("$1.2M");
    expect(formatCompactCurrency(3_450)).toBe("$3.5K");
    expect(formatCompactCurrency(512.4)).toBe("$512");
    expect(formatCompactCurrency(-2_500_000)).toBe("-$2.5M");
  });

  it("renders the fallback for non-finite values", () => {
    expect(formatCompactCurrency(undefined)).toBe("—");
  });
});

describe("formatAmountWithCode", () => {
  it("suffixes the currency code when present", () => {
    expect(formatAmountWithCode(1234.5, "USD")).toBe("1,234.50 USD");
    expect(formatAmountWithCode(1234.5, null)).toBe("1,234.50");
    expect(formatAmountWithCode(1234.5, "  ")).toBe("1,234.50");
  });

  it("renders the fallback for non-finite values", () => {
    expect(formatAmountWithCode(Number.NaN, "USD", { fallback: "-" })).toBe("-");
  });
});

describe("pluralizeCount", () => {
  it("uses the singular noun for exactly one and appends 's' otherwise", () => {
    expect(pluralizeCount(1, "item")).toBe("1 item");
    expect(pluralizeCount(0, "item")).toBe("0 items");
    expect(pluralizeCount(3, "item")).toBe("3 items");
  });

  it("honours an explicit plural for irregular nouns", () => {
    expect(pluralizeCount(1, "entry", { plural: "entries" })).toBe("1 entry");
    expect(pluralizeCount(2, "entry", { plural: "entries" })).toBe("2 entries");
    expect(pluralizeCount(2, "anomaly", { plural: "anomalies" })).toBe("2 anomalies");
  });

  it("localizes the count when requested", () => {
    expect(pluralizeCount(1234, "row", { localizeCount: true })).toBe("1,234 rows");
    expect(pluralizeCount(1234, "row")).toBe("1234 rows");
  });
});
