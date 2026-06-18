// Meridian accounting — shared money helpers. No React, no DOM. Used by AmountCell and the
// ledger/statement primitives so every amount formats identically (tabular, fixed decimals,
// accounting parentheses for negatives, zero-as-dash).

const SYMBOLS = { USD: "$", EUR: "\u20AC", GBP: "\u00A3", JPY: "\u00A5", CNY: "\u00A5", CHF: "CHF\u202F", CAD: "$", AUD: "$" };

/** Currency code → leading symbol. Unknown codes render as "CODE " prefix. */
export function currencySymbol(code) {
  if (!code) return "";
  return SYMBOLS[code] || code + "\u202F";
}

/** Coerce a value (number or money-ish string like "(1,234.00)") to a Number. NaN if unparseable. */
export function toNumber(value) {
  if (value == null || value === "") return NaN;
  if (typeof value === "number") return value;
  const s = String(value).trim();
  const neg = /^\(.*\)$/.test(s); // accounting parentheses = negative
  const n = parseFloat(s.replace(/[(),\s]/g, "").replace(/[^0-9.\-]/g, ""));
  return neg ? -Math.abs(n) : n;
}

/**
 * Format a money value the Meridian way.
 * opts: { currency, decimals=2, parens=false, zeroDash=false, signed=false }
 *  - parens   → negatives as (1,234.00) instead of -1,234.00 (statement convention)
 *  - zeroDash → exact zero renders as an em dash
 *  - signed   → positives get an explicit leading +
 */
export function formatMoney(value, opts = {}) {
  const { currency, decimals = 2, parens = false, zeroDash = false, signed = false } = opts;
  const num = toNumber(value);
  if (!isFinite(num)) return value == null ? "" : String(value);
  if (zeroDash && num === 0) return "\u2014";
  const sym = currencySymbol(currency);
  const mag = Math.abs(num).toLocaleString("en-US", {
    minimumFractionDigits: decimals, maximumFractionDigits: decimals,
  });
  const neg = num < 0;
  if (neg && parens) return `(${sym}${mag})`;
  const sign = neg ? "\u2212" : signed && num > 0 ? "+" : "";
  return `${sign}${sym}${mag}`;
}

/** Sum a list of money-ish values, ignoring blanks/NaN. */
export function sumAmounts(values) {
  return values.reduce((a, v) => {
    const n = toNumber(v);
    return a + (isFinite(n) ? n : 0);
  }, 0);
}
