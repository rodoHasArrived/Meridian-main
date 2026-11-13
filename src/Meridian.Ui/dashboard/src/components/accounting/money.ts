// Meridian accounting — shared money helpers. No React, no DOM. Used by AmountCell and the
// ledger/statement primitives so every amount formats identically: mono tabular, fixed decimals,
// accounting parentheses for negatives, zero-as-dash, explicit signs on deltas.
//
// SCOPE: this module is the deliberate ACCOUNTING exception — statement convention with
// parenthesized negatives and U+2212 minus. All non-accounting screens must use the
// Intl-based formatters in `lib/format.ts` instead. A rounding/negative-display policy
// change must be applied to whichever of the two conventions it targets, knowingly.

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
 * An exact decimal: `units / 10^scale`, sign held separately so "-0.00" stays a zero. Money
 * arrives from the server as decimal strings; parsing them to a binary float first would round
 * values such as "12345678901234.565" before they are displayed or summed (UX consolidation P3).
 */
export interface ExactDecimal {
  negative: boolean;
  units: bigint;
  scale: number;
}

// The first unsigned number in the text, with an optional exponent that must directly follow a
// digit ("1.5e-3"). Matching a token rather than stripping letters keeps a currency suffix such as
// "EUR" from being read as an exponent.
const DECIMAL_TOKEN = /(\d+)?(?:\.(\d+)?)?(?:(?<=\d)[eE]([+-]?\d+))?/g;
// No money value needs more than this many digits of exponent; beyond it the text is not an amount
// and expanding it would exhaust memory.
const MAX_EXPONENT = 400;

/**
 * Read a money value (number or money-ish string like `"(1,234.00)"`, `"1,234.00-"`, `"−45.5"`)
 * as an exact decimal. Returns `null` when the input is blank or unparseable. Accounting
 * parentheses and trailing minus signs are read as negative.
 */
export function parseExactDecimal(value: number | string | null | undefined): ExactDecimal | null {
  if (value == null || value === "") return null;
  if (typeof value === "number" && !Number.isFinite(value)) return null;
  // A number is written out as plain decimal digits (no exponent) before it is read.
  const s = typeof value === "number"
    ? value.toLocaleString("en-US", { useGrouping: false, maximumFractionDigits: 20 })
    : String(value).trim();
  // Statement negatives: "(1,234.00)", optionally followed by an ISO code ("(1,234.00) EUR").
  const parenthesized = /^\(.*\)(?:\s*[A-Z]{3})?$/i.test(s);
  const trailingMinus = /[-−]$/.test(s);
  const leadingMinus = /^[(\s$€£¥A-Z]*[-−]/i.test(s) && !trailingMinus;
  // Signs were read above; the amount is the first number token once group separators are gone.
  const cleaned = s.replace(/,/g, "");
  const match = [...cleaned.matchAll(DECIMAL_TOKEN)].find((candidate) => candidate[1] || candidate[2]);
  if (!match) return null;
  const [, whole = "", fraction = "", exponentText] = match;
  const exponent = exponentText ? Number(exponentText) : 0;
  if (!Number.isSafeInteger(exponent) || Math.abs(exponent) > MAX_EXPONENT) return null;
  let digits = `${whole}${fraction}` || "0";
  let scale = fraction.length - exponent;
  if (scale < 0) {
    digits += "0".repeat(-scale);
    scale = 0;
  }
  return { negative: parenthesized || trailingMinus || leadingMinus, units: BigInt(digits), scale };
}

function rescale(value: ExactDecimal, scale: number): bigint {
  return value.units * 10n ** BigInt(scale - value.scale);
}

/** Round half away from zero to `decimals` places, then render the magnitude with grouping. */
function formatMagnitude(value: ExactDecimal, decimals: number): string {
  let units = value.units;
  if (value.scale > decimals) {
    const divisor = 10n ** BigInt(value.scale - decimals);
    const remainder = units % divisor;
    units /= divisor;
    if (remainder * 2n >= divisor) units += 1n;
  } else {
    units *= 10n ** BigInt(decimals - value.scale);
  }
  const text = units.toString().padStart(decimals + 1, "0");
  const whole = text.slice(0, text.length - decimals).replace(/\B(?=(\d{3})+(?!\d))/g, ",");
  return decimals > 0 ? `${whole}.${text.slice(text.length - decimals)}` : whole;
}

function decimalToNumber(value: ExactDecimal): number {
  const text = value.units.toString().padStart(value.scale + 1, "0");
  const magnitude = Number(value.scale > 0 ? `${text.slice(0, -value.scale)}.${text.slice(-value.scale)}` : text);
  return value.negative ? -magnitude : magnitude;
}

/**
 * Coerce a money value to a Number, for comparisons and chart geometry only; display goes
 * through {@link formatMoney}, which keeps the exact decimal. Returns `NaN` when the input is
 * blank or unparseable.
 */
export function toNumber(value: number | string | null | undefined): number {
  if (typeof value === "number") return value;
  const parsed = parseExactDecimal(value);
  return parsed ? decimalToNumber(parsed) : NaN;
}

/**
 * Format a money value the Meridian way — tabular, fixed decimals, optional currency symbol.
 * A decimal string is formatted from its digits, never through a float.
 *  - `parens`   → negatives as `(1,234.00)` instead of `−1,234.00` (statement convention)
 *  - `zeroDash` → exact zero renders as an em dash
 *  - `signed`   → positives get an explicit leading `+`
 *
 * Unparseable input passes through: `null`/`undefined` → `""`, everything else → its string form.
 */
export function formatMoney(value: number | string | null | undefined, opts: FormatMoneyOptions = {}): string {
  const { currency, decimals = 2, parens = false, zeroDash = false, signed = false } = opts;
  const parsed = parseExactDecimal(value);
  if (!parsed) return value == null ? "" : String(value);
  const isZero = parsed.units === 0n;
  if (zeroDash && isZero) return "—";
  const sym = currencySymbol(currency);
  const mag = formatMagnitude(parsed, decimals);
  // A value that rounds to zero at this precision is shown unsigned, as toLocaleString did.
  const shownZero = /^[0,.]*$/.test(mag);
  const neg = parsed.negative && !isZero && !shownZero;
  if (neg && parens) return `(${sym}${mag})`;
  const sign = neg ? "−" : signed && !parsed.negative && !isZero && !shownZero ? "+" : "";
  return `${sign}${sym}${mag}`;
}

/** Sum a list of money-ish values exactly, ignoring blanks / unparseable entries. */
export function sumAmounts(values: Array<number | string | null | undefined>): number {
  const parsed = values.map(parseExactDecimal).filter((value): value is ExactDecimal => value !== null);
  if (parsed.length === 0) return 0;
  const scale = Math.max(...parsed.map((value) => value.scale));
  const total = parsed.reduce((acc, value) => acc + (value.negative ? -1n : 1n) * rescale(value, scale), 0n);
  return decimalToNumber({ negative: total < 0n, units: total < 0n ? -total : total, scale });
}
