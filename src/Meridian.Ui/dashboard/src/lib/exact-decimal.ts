// Exact decimal arithmetic for money values (UX consolidation P3). Money arrives from the server
// as decimal strings; parsing them to a binary float first would round values such as
// "12345678901234.565" before they are displayed or summed. This module is the one place that
// reads a money value, rounds it and groups its digits: `formatMoney` in `@/lib/format` renders
// through `formatExactMagnitude`, and `components/accounting/money.ts` re-exports the readers.
// No Intl and no `parseFloat`: numbers are written out with `String(value)` (the shortest form
// that round-trips) and read back digit by digit.

/** An exact decimal: `units / 10^scale`, sign held separately so "-0.00" stays a zero. */
export interface ExactDecimal {
  negative: boolean;
  units: bigint;
  scale: number;
}

/** A money value as it reaches a formatter: a number, a decimal string, or nothing. */
export type MoneyValue = number | string | null | undefined;

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
export function parseExactDecimal(value: MoneyValue): ExactDecimal | null {
  if (value == null || value === "") return null;
  if (typeof value === "number" && !Number.isFinite(value)) return null;
  // A number is written in its shortest round-trip form ("1.005", "1e-7", "1e+21"); the exponent
  // is expanded below, so the digits are exactly the ones an Intl formatter would round.
  const s = typeof value === "number" ? String(value) : String(value).trim();
  // Statement negatives: "(1,234.00)", optionally followed by an ISO code ("(1,234.00) EUR").
  const parenthesized = /^\(.*\)(?:\s*[A-Z]{3})?$/i.test(s);
  const trailingMinus = /[-−]$/.test(s);
  // Commas and any whitespace (space, no-break and narrow no-break space) are group separators.
  const cleaned = s.replace(/[,\s]/g, "");
  const match = [...cleaned.matchAll(DECIMAL_TOKEN)].find((candidate) => candidate[1] || candidate[2]);
  if (!match) return null;
  // A minus anywhere before the number is its sign, whatever currency symbol or code precedes it
  // ("-$5", "$-5", "₹-1,234.56", "CHF -5").
  const leadingMinus = /[-−]/.test(cleaned.slice(0, match.index)) && !trailingMinus;
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

/** The magnitude of `value` rounded half away from zero to `decimals` places, as integer units. */
function roundedUnits(value: ExactDecimal, decimals: number): bigint {
  if (value.scale <= decimals) {
    return value.units * 10n ** BigInt(decimals - value.scale);
  }
  const divisor = 10n ** BigInt(value.scale - decimals);
  const remainder = value.units % divisor;
  const units = value.units / divisor;
  return remainder * 2n >= divisor ? units + 1n : units;
}

/** A rounded, grouped magnitude ("1,234.57") and whether it shows as zero at that precision. */
export interface ExactMagnitude {
  text: string;
  roundsToZero: boolean;
}

/**
 * Round half away from zero to `decimals` places, then render the magnitude with en-US grouping,
 * e.g. 1234.565 at 2 -> "1,234.57". The sign is the caller's: see `formatMoney` in `@/lib/format`.
 */
export function formatExactMagnitude(value: ExactDecimal, decimals: number): ExactMagnitude {
  const places = Math.max(0, Math.trunc(decimals));
  const units = roundedUnits(value, places);
  const text = units.toString().padStart(places + 1, "0");
  const whole = text.slice(0, text.length - places).replace(/\B(?=(\d{3})+(?!\d))/g, ",");
  return {
    text: places > 0 ? `${whole}.${text.slice(text.length - places)}` : whole,
    roundsToZero: units === 0n
  };
}

function decimalToNumber(value: ExactDecimal): number {
  const text = value.units.toString().padStart(value.scale + 1, "0");
  const magnitude = Number(value.scale > 0 ? `${text.slice(0, -value.scale)}.${text.slice(-value.scale)}` : text);
  return value.negative ? -magnitude : magnitude;
}

/**
 * Coerce a money value to a Number, for comparisons and chart geometry only; display goes
 * through `formatMoney`, which keeps the exact decimal. Returns `NaN` when the input is
 * blank or unparseable.
 */
export function toNumber(value: MoneyValue): number {
  if (typeof value === "number") return value;
  const parsed = parseExactDecimal(value);
  return parsed ? decimalToNumber(parsed) : NaN;
}

/** Sum a list of money-ish values exactly, ignoring blanks / unparseable entries. */
export function sumAmounts(values: MoneyValue[]): number {
  const parsed = values.map(parseExactDecimal).filter((value): value is ExactDecimal => value !== null);
  if (parsed.length === 0) return 0;
  const scale = Math.max(...parsed.map((value) => value.scale));
  const total = parsed.reduce((acc, value) => acc + (value.negative ? -1n : 1n) * rescale(value, scale), 0n);
  return decimalToNumber({ negative: total < 0n, units: total < 0n ? -total : total, scale });
}
