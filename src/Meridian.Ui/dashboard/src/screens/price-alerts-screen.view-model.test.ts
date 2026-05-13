import { describe, expect, it } from "vitest";
import {
  DEFAULT_PRICE_ALERT_FORM,
  formatPriceAlertPrice,
  formatPriceAlertTimestamp,
  parseThreshold,
  priceAlertDraftFromForm,
  validatePriceAlertForm
} from "./price-alerts-screen.view-model";

describe("validatePriceAlertForm", () => {
  it("flags an empty symbol", () => {
    const v = validatePriceAlertForm({ ...DEFAULT_PRICE_ALERT_FORM, threshold: "10" });
    expect(v.symbolError).not.toBeNull();
    expect(v.canSubmit).toBe(false);
  });

  it("flags an invalid symbol", () => {
    const v = validatePriceAlertForm({ ...DEFAULT_PRICE_ALERT_FORM, symbol: "bad symbol!", threshold: "10" });
    expect(v.symbolError).not.toBeNull();
  });

  it("accepts valid symbol with dot/dash characters", () => {
    const v = validatePriceAlertForm({ ...DEFAULT_PRICE_ALERT_FORM, symbol: "BRK.B", threshold: "300" });
    expect(v.symbolError).toBeNull();
    expect(v.thresholdError).toBeNull();
    expect(v.canSubmit).toBe(true);
  });

  it("flags a missing threshold", () => {
    const v = validatePriceAlertForm({ ...DEFAULT_PRICE_ALERT_FORM, symbol: "AAPL" });
    expect(v.thresholdError).not.toBeNull();
  });

  it("flags a non-positive threshold", () => {
    const v = validatePriceAlertForm({ ...DEFAULT_PRICE_ALERT_FORM, symbol: "AAPL", threshold: "-1" });
    expect(v.thresholdError).not.toBeNull();
  });

  it("flags a non-numeric threshold", () => {
    const v = validatePriceAlertForm({ ...DEFAULT_PRICE_ALERT_FORM, symbol: "AAPL", threshold: "abc" });
    expect(v.thresholdError).not.toBeNull();
  });
});

describe("priceAlertDraftFromForm", () => {
  it("returns null for invalid form", () => {
    expect(priceAlertDraftFromForm(DEFAULT_PRICE_ALERT_FORM)).toBeNull();
  });

  it("normalizes symbol to upper-case and trims note", () => {
    const draft = priceAlertDraftFromForm({
      symbol: "  aapl  ",
      condition: "crosses-up",
      field: "mid",
      threshold: "200.50",
      note: "  earnings prep  "
    });
    expect(draft).not.toBeNull();
    expect(draft?.symbol).toBe("AAPL");
    expect(draft?.threshold).toBe(200.5);
    expect(draft?.note).toBe("earnings prep");
    expect(draft?.condition).toBe("crosses-up");
    expect(draft?.field).toBe("mid");
  });

  it("accepts thresholds with comma separators", () => {
    const draft = priceAlertDraftFromForm({
      ...DEFAULT_PRICE_ALERT_FORM,
      symbol: "BRK.A",
      threshold: "1,000,000"
    });
    expect(draft?.threshold).toBe(1_000_000);
  });

  it("returns null note when only whitespace was entered", () => {
    const draft = priceAlertDraftFromForm({
      symbol: "MSFT",
      condition: "below",
      field: "last",
      threshold: "300",
      note: "   "
    });
    expect(draft?.note).toBeNull();
  });
});

describe("parseThreshold", () => {
  it("handles empty input", () => {
    expect(parseThreshold("")).toBeNull();
    expect(parseThreshold("   ")).toBeNull();
  });
  it("parses decimal", () => {
    expect(parseThreshold("1.25")).toBe(1.25);
  });
  it("rejects non-numeric", () => {
    expect(parseThreshold("abc")).toBeNull();
  });
});

describe("formatters", () => {
  it("formats null prices as em-dash", () => {
    expect(formatPriceAlertPrice(null)).toBe("—");
    expect(formatPriceAlertPrice(undefined)).toBe("—");
    expect(formatPriceAlertPrice(Number.NaN)).toBe("—");
  });
  it("formats numeric prices with locale separators", () => {
    expect(formatPriceAlertPrice(1234.5)).toMatch(/1[,.]234/);
  });
  it("formats null timestamps as em-dash", () => {
    expect(formatPriceAlertTimestamp(null)).toBe("—");
    expect(formatPriceAlertTimestamp(undefined)).toBe("—");
    expect(formatPriceAlertTimestamp("not-a-date")).toBe("—");
  });
});
