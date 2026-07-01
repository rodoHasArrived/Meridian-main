// Meridian accounting — shared money helpers. No React, no DOM. Used by AmountCell and the
// ledger/statement primitives so every amount formats identically: mono tabular, fixed decimals,
// accounting parentheses for negatives, zero-as-dash, explicit signs on deltas.

/** Options accepted by {@link formatMoney}. */
export interface FormatMoneyOptions {
  /** Currency code (USD, EUR, GBP, JPY, CHF…). Omit for a bare number. */
  currency?: string;
  /** Decimal places. @default 2 */
  decimals?: number;
  /** Render negatives as `(1,234.00)` instead of `−1,234.00` (statement convention). @default false */
  parens?: boolean;
  /** Exact zero renders as an em dash. @default false */
  zeroDash?: boolean;
  /** Positives get an explicit leading `+`. @default false */
  signed?: boolean;
}

const SYMBOLS: Record<string, string> = {
  USD: "$",
  EUR: "€",
  GBP: "£",
  JPY: "¥",
  CNY: "¥",
  CHF: "CHF ",
  CAD: "$",
  AUD: "$"
};

/** Currency code → leading symbol. Unknown codes render as a `"CODE "` prefix. */
export function currencySymbol(code?: string | null): string {
  if (!code) return "";
  return SYMBOLS[code] ?? code + " ";
}

/**
 * Coerce a value (number or money-ish string like `"(1,234.00)"`) to a Number.
 * Returns `NaN` when the input is blank or unparseable. Accounting parentheses are read as
 * negative so a round-tripped statement string parses back to its signed value.
 */
export function toNumber(value: number | string | null | undefined): number {
  if (value == null || value === "") return NaN;
  if (typeof value === "number") return value;
  const s = String(value).trim();
  const neg = /^\(.*\)$/.test(s); // accounting parentheses = negative
  const cleaned = s.replace(/[(),\s]/g, "").replace(/[^0-9.−-]/g, "").replace(/−/g, "-");
  const n = parseFloat(cleaned);
  if (Number.isNaN(n)) return NaN;
  return neg ? -Math.abs(n) : n;
}

/**
 * Format a money value the Meridian way — tabular, fixed decimals, optional currency symbol.
 *  - `parens`   → negatives as `(1,234.00)` instead of `−1,234.00` (statement convention)
 *  - `zeroDash` → exact zero renders as an em dash
 *  - `signed`   → positives get an explicit leading `+`
 *
 * Non-finite input passes through: `null`/`undefined` → `""`, everything else → its string form.
 */
export function formatMoney(value: number | string | null | undefined, opts: FormatMoneyOptions = {}): string {
  const { currency, decimals = 2, parens = false, zeroDash = false, signed = false } = opts;
  const num = toNumber(value);
  if (!Number.isFinite(num)) return value == null ? "" : String(value);
  if (zeroDash && num === 0) return "—";
  const sym = currencySymbol(currency);
  const mag = Math.abs(num).toLocaleString("en-US", {
    minimumFractionDigits: decimals,
    maximumFractionDigits: decimals
  });
  const neg = num < 0;
  if (neg && parens) return `(${sym}${mag})`;
  const sign = neg ? "−" : signed && num > 0 ? "+" : "";
  return `${sign}${sym}${mag}`;
}

/** Sum a list of money-ish values, ignoring blanks / NaN. */
export function sumAmounts(values: Array<number | string | null | undefined>): number {
  return values.reduce<number>((acc, v) => {
    const n = toNumber(v);
    return acc + (Number.isFinite(n) ? n : 0);
  }, 0);
}
