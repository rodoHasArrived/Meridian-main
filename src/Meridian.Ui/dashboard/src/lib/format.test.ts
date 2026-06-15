import { describe, expect, it } from "vitest";

import { EMPTY_VALUE, formatCompactUsd, formatMoney, formatPercent, formatUsd } from "@/lib/format";

describe("formatUsd", () => {
  it("formats finite amounts with the currency style", () => {
    expect(formatUsd(1234.5, { maximumFractionDigits: 2 })).toBe("$1,234.50");
    expect(formatUsd(1000000, { maximumFractionDigits: 0 })).toBe("$1,000,000");
    expect(formatUsd(0, { maximumFractionDigits: 2 })).toBe("$0.00");
  });

  it("formats negative amounts", () => {
    expect(formatUsd(-2500, { maximumFractionDigits: 2 })).toBe("-$2,500.00");
  });

  it.each([NaN, Infinity, -Infinity, null, undefined])(
    "returns the sentinel for non-finite or missing value %p",
    (value) => {
      expect(formatUsd(value as number | null | undefined, { maximumFractionDigits: 2 })).toBe(EMPTY_VALUE);
    }
  );
});

describe("formatCompactUsd", () => {
  it("formats millions and thousands with magnitude suffixes", () => {
    expect(formatCompactUsd(10_000_000)).toBe("$10.0M");
    expect(formatCompactUsd(250_000)).toBe("$250.0K");
    expect(formatCompactUsd(1_500_000)).toBe("$1.5M");
  });

  it("formats sub-thousand amounts without a suffix", () => {
    expect(formatCompactUsd(420)).toBe("$420");
    expect(formatCompactUsd(0)).toBe("$0");
  });

  it("prefixes negative amounts and does not render negative zero", () => {
    expect(formatCompactUsd(-3_200_000)).toBe("-$3.2M");
    expect(formatCompactUsd(-0)).toBe("$0");
  });

  it.each([NaN, Infinity, -Infinity, null, undefined])(
    "returns the sentinel for non-finite or missing value %p",
    (value) => {
      expect(formatCompactUsd(value as number | null | undefined)).toBe(EMPTY_VALUE);
    }
  );
});

describe("formatMoney", () => {
  it("formats supported ISO currencies", () => {
    expect(formatMoney(1234.5, "USD", { maximumFractionDigits: 2, locale: "en-US" })).toBe("$1,234.50");
    expect(formatMoney(1000, "EUR", { maximumFractionDigits: 2, locale: "en-US" })).toBe("€1,000.00");
  });

  it("falls back to USD when the currency code is blank", () => {
    expect(formatMoney(500, "  ", { maximumFractionDigits: 2, locale: "en-US" })).toBe("$500.00");
  });

  it("degrades gracefully instead of throwing on a malformed currency code", () => {
    // An invalid ISO code makes Intl.NumberFormat throw a RangeError; the helper
    // must return a readable string rather than crash the calling surface.
    expect(formatMoney(2500, "BOGUS", { maximumFractionDigits: 2, locale: "en-US" })).toBe("BOGUS 2,500");
  });

  it.each([NaN, Infinity, -Infinity, null, undefined])(
    "returns the sentinel for non-finite or missing value %p",
    (value) => {
      expect(formatMoney(value as number | null | undefined, "USD")).toBe(EMPTY_VALUE);
    }
  );
});

describe("formatPercent", () => {
  it("renders whole percentages without a fractional digit", () => {
    expect(formatPercent(60)).toBe("60%");
    expect(formatPercent(0)).toBe("0%");
  });

  it("renders fractional percentages with one decimal", () => {
    expect(formatPercent(12.5)).toBe("12.5%");
    expect(formatPercent(33.337)).toBe("33.3%");
  });

  it.each([NaN, Infinity, -Infinity, null, undefined])(
    "returns the sentinel for non-finite or missing value %p",
    (value) => {
      expect(formatPercent(value as number | null | undefined)).toBe(EMPTY_VALUE);
    }
  );
});
