import { formatTaxAmount, formatTaxDecimal, isTaxDecimalText } from "@/lib/ledger-tax-results-format";

it("preserves large cents and every retained fractional digit when formatting amounts", () => {
  expect(formatTaxAmount("9007199254740993.01", "USD")).toBe("9,007,199,254,740,993.01 USD");
  expect(formatTaxAmount("-9007199254740993.01", "EUR")).toBe("-9,007,199,254,740,993.01 EUR");
  expect(formatTaxAmount("0.0000000000000000000000000001", "USD")).toBe("0.0000000000000000000000000001 USD");
  expect(formatTaxAmount("79228162514264337593543950335", "USD")).toBe("79,228,162,514,264,337,593,543,950,335.00 USD");
});

it("preserves fractional quantities beyond eight digits without padding or rounding", () => {
  expect(formatTaxDecimal("0.1234567890123456789012345678")).toBe("0.1234567890123456789012345678");
  expect(formatTaxDecimal("0.0000000000000000000000000001")).toBe("0.0000000000000000000000000001");
  expect(formatTaxDecimal("12345678901234567890.12345678")).toBe("12,345,678,901,234,567,890.12345678");
  expect(formatTaxDecimal("12.3400")).toBe("12.3400");
});

it("distinguishes absent evidence from exact zero and normalizes signed decimal zero", () => {
  expect(formatTaxAmount(null, "USD")).toBe("Missing evidence");
  expect(formatTaxAmount("0", "USD")).toBe("0.00 USD");
  expect(formatTaxAmount("-0.0000", "USD")).toBe("0.0000 USD");
  expect(formatTaxAmount("1.2", "")).toBe("1.20");
});

it.each([JSON.parse("9007199254740993.01"), 0, null, undefined, "NaN", "1e-28", "+1", "01", "1,234.56", ".5", "1.", " 1"])(
  "refuses noncanonical decimal evidence %j", (value) => expect(isTaxDecimalText(value)).toBe(false)
);
