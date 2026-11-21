// Strict positive-decimal parsing for operator-typed order quantities and prices.
//
// `Number()` accepts exponent ("1e3"), hex ("0x10"), signed, and whitespace-only text, so a typo in
// an order field could be sent as a very different quantity. Only plain decimal text is accepted
// here, mirroring the design-system OrderTicket's `positiveDecimal` helper.

const PLAIN_DECIMAL_PATTERN = /^(?:\d+(?:\.\d*)?|\.\d+)$/;

/**
 * Returns a finite number greater than zero when `value` is plain decimal text (after trimming):
 * digits with an optional fractional part. Exponents, hex/octal/binary prefixes, signs, grouping
 * separators, `Infinity`, `NaN`, empty text, and zero all return `null`.
 */
export function parsePositiveDecimal(value: string | number | null | undefined): number | null {
  const text = String(value ?? "").trim();
  if (!PLAIN_DECIMAL_PATTERN.test(text)) {
    return null;
  }

  const parsed = Number(text);
  return Number.isFinite(parsed) && parsed > 0 ? parsed : null;
}
