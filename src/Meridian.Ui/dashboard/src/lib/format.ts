// Shared currency, number, percent, count, quantity and date formatting for workstation screens.
// This is the one Intl-backed formatting module: screens call it instead of Intl.NumberFormat or
// toLocale*String directly (scripts/design-system-ratchets.mjs and the ESLint rule enforce that).
// All helpers pin the en-US locale so operator machines render identical values
// regardless of host locale, and all accept null/undefined/non-finite input,
// rendering the configurable `fallback` instead.
//
// MONEY: `formatMoney` below is the one money formatter. It reads numbers and decimal strings
// exactly through `@/lib/exact-decimal` (the single rounding, sign and grouping implementation)
// and spells them in one of two conventions: `screen` (Intl symbols, "-") for workstation screens
// and `statement` (the accounting symbol table, U+2212 "−", optional parentheses and zero-dash)
// for ledgers, statements and journal grids. `components/accounting/money.ts` is a thin
// statement-convention wrapper over it.
//
// STYLE GUIDE: the "Number style" and "Formatting" sections of the dashboard README are the
// operator-facing contract these helpers implement (signed P&L, the "P&L" spelling, currency
// codes, decimals, timestamps and the display time zone).

import { formatExactMagnitude, parseExactDecimal, type MoneyValue } from "@/lib/exact-decimal";

export type { MoneyValue } from "@/lib/exact-decimal";

const FORMAT_LOCALE = "en-US";
const DEFAULT_FALLBACK = "—";

export type NullableNumber = number | null | undefined;

export interface NumberFormatOptions {
  minimumFractionDigits?: number;
  maximumFractionDigits?: number;
  /** Scientific notation preserves tiny nonzero observations without rounding them to zero. */
  notation?: Intl.NumberFormatOptions["notation"];
  /** Thousands separators; false for a value typed back into an input, e.g. "1234.5". @default true */
  useGrouping?: boolean;
  /** Rendered when the value is null, undefined, or not finite. */
  fallback?: string;
}

export interface SignableNumberFormatOptions extends NumberFormatOptions {
  /** Explicit "+" on positives and "-" on negatives; zero (including -0) stays unsigned, e.g. "+1,234.5". */
  signed?: boolean;
}

/** "+" for a positive value, "-" for a negative one, "" for zero, so the sign follows the value, not its rounding. */
function signPrefix(value: number): string {
  return value > 0 ? "+" : value < 0 ? "-" : "";
}

export interface CurrencyFormatOptions extends NumberFormatOptions {
  /** ISO 4217 code; blank or unknown codes render as "CODE 1,234.56". */
  currency?: string;
  /** "compact" abbreviates large amounts for axes and chips, e.g. "$1.2M". @default "standard" */
  notation?: "standard" | "compact";
}

export interface SignedCurrencyFormatOptions extends NumberFormatOptions {
  /** Rendered for exactly zero when provided, e.g. "$0". */
  zeroLabel?: string;
}

function isRenderable(value: NullableNumber): value is number {
  return typeof value === "number" && Number.isFinite(value);
}

/** Grouped plain number, e.g. "1,234.56"; `signed` gives "+1,234.56" / "-1,234.56". */
export function formatNumber(value: NullableNumber, options: SignableNumberFormatOptions = {}): string {
  const { minimumFractionDigits, maximumFractionDigits = 2, notation, useGrouping, signed = false, fallback = DEFAULT_FALLBACK } = options;
  if (!isRenderable(value)) {
    return fallback;
  }

  const localeOptions = { minimumFractionDigits, maximumFractionDigits, notation, useGrouping };
  if (signed) {
    return `${signPrefix(value)}${Math.abs(value).toLocaleString(FORMAT_LOCALE, localeOptions)}`;
  }
  return value.toLocaleString(FORMAT_LOCALE, localeOptions);
}

/**
 * Display number with exactly `digits` fraction digits, grouped and locale-pinned, e.g.
 * `formatDecimal(1234.5, 2)` -> "1,234.50", `formatDecimal(0.84, 2)` -> "0.84". This is the
 * replacement for a displayed `value.toFixed(digits)`; `signed` gives "+0.84" / "-0.84".
 * Ties round on the shortest decimal form, so 0.35 at one digit reads "0.4" (toFixed gives "0.3").
 */
export function formatDecimal(
  value: NullableNumber,
  digits: number,
  options: Omit<SignableNumberFormatOptions, "minimumFractionDigits" | "maximumFractionDigits"> = {}
): string {
  // -0 reads "0.00" as it did under toFixed, not "-0.00".
  return formatNumber(value === 0 ? 0 : value, { ...options, minimumFractionDigits: digits, maximumFractionDigits: digits });
}

/**
 * Machine-readable fixed-point string for values a browser parses rather than an operator reads:
 * SVG path coordinates, polyline points and CSS lengths, e.g. `formatFixed(12.5, 2)` -> "12.50".
 * Never grouped and never localized, with exactly `Number#toFixed` digits (exact binary rounding,
 * "NaN" for NaN) so geometry stays byte-stable. Do not use it for a displayed figure: a number an
 * operator reads goes through `formatDecimal`, `formatNumber`, `formatPercent` or `formatMoney`.
 */
export function formatFixed(value: number, digits: number): string {
  return value.toFixed(digits);
}

// PERCENT UNITS: the workstation carries percent-like values in two different units,
// and picking the wrong helper is a silent 100x error in financial output. Read the
// producing field before choosing:
//   - `formatPercent`        takes PERCENT units  (42.5 -> "42.5%")
//   - `formatRatioAsPercent` takes FRACTION units (0.425 -> "42.5%")
// A `…Percent` suffix on a field name is NOT reliable evidence of its unit — see
// `maxDrawdownPercent`, which arrives as a fraction from the walk-forward path and
// as percent units from the live-metrics path.

/** Percent display for values already expressed in percent units, e.g. 42.5 -> "42.5%"; `signed` gives "+42.5%". */
export function formatPercent(value: NullableNumber, options: SignableNumberFormatOptions = {}): string {
  const { fallback = DEFAULT_FALLBACK } = options;
  if (!isRenderable(value)) {
    return fallback;
  }

  return `${formatNumber(value, options)}%`;
}

/** Percent display for values expressed as a fraction of 1, e.g. 0.425 -> "42.5%"; `signed` gives "+42.5%". */
export function formatRatioAsPercent(value: NullableNumber, options: SignableNumberFormatOptions = {}): string {
  const { fallback = DEFAULT_FALLBACK } = options;
  if (!isRenderable(value)) {
    return fallback;
  }

  return formatPercent(value * 100, options);
}

/** Symbol-style currency via Intl, e.g. "$1,234.56"; unknown codes render as "CODE 1,234.56". */
export function formatCurrency(value: NullableNumber, options: CurrencyFormatOptions = {}): string {
  const { currency = "USD", minimumFractionDigits, maximumFractionDigits = 2, notation, fallback = DEFAULT_FALLBACK } = options;
  if (!isRenderable(value)) {
    return fallback;
  }

  const code = currency.trim() || "USD";
  try {
    return new Intl.NumberFormat(FORMAT_LOCALE, {
      style: "currency",
      currency: code,
      notation,
      minimumFractionDigits,
      maximumFractionDigits
    }).format(value);
  } catch {
    // Unknown code: the amount after the code, still abbreviated when compact, e.g. "XYZ 1.2M".
    const amount = value.toLocaleString(
      FORMAT_LOCALE,
      notation === "compact" ? { notation, maximumFractionDigits } : { maximumFractionDigits: 2 }
    );
    return `${code} ${amount}`;
  }
}

/** Dollar amount with a hand-built prefix, e.g. "$1,234.56" / "-$1,234.56". */
export function formatPrefixedCurrency(value: NullableNumber, options: NumberFormatOptions = {}): string {
  const { fallback = DEFAULT_FALLBACK } = options;
  if (!isRenderable(value)) {
    return fallback;
  }

  const prefix = value < 0 ? "-$" : "$";
  return `${prefix}${formatNumber(Math.abs(value), { ...options, signed: false })}`;
}

/** Explicitly signed dollar amount, e.g. "+$1,234" / "-$1,234". */
export function formatSignedCurrency(value: NullableNumber, options: SignedCurrencyFormatOptions = {}): string {
  const { zeroLabel, fallback = DEFAULT_FALLBACK } = options;
  if (!isRenderable(value)) {
    return fallback;
  }

  if (value === 0 && zeroLabel !== undefined) {
    return zeroLabel;
  }

  const prefix = value < 0 ? "-$" : "+$";
  return `${prefix}${formatNumber(Math.abs(value), { ...options, signed: false })}`;
}

/** Compact dollar amount, e.g. "$1.2M", "$3.4K", "-$512". */
export function formatCompactCurrency(value: NullableNumber, options: Pick<NumberFormatOptions, "fallback"> = {}): string {
  const { fallback = DEFAULT_FALLBACK } = options;
  if (!isRenderable(value)) {
    return fallback;
  }

  const absoluteValue = Math.abs(value);
  const sign = value < 0 ? "-" : "";
  if (absoluteValue >= 1_000_000) {
    return `${sign}$${(absoluteValue / 1_000_000).toFixed(1)}M`;
  }

  if (absoluteValue >= 1_000) {
    return `${sign}$${(absoluteValue / 1_000).toFixed(1)}K`;
  }

  return `${sign}$${absoluteValue.toFixed(0)}`;
}

export interface PluralizeCountOptions {
  /** Explicit plural noun for irregular words, e.g. "entries", "anomalies". Defaults to `${singular}s`. */
  plural?: string;
  /** Group the count with the en-US locale separator, e.g. "1,234 rows". Defaults to `false`. */
  localizeCount?: boolean;
}

/**
 * Count followed by a singular/plural noun, e.g. "1 item" / "3 items".
 *
 * This is the single home for the `count === 1 ? …` decision that view-models and
 * screens previously reimplemented under a dozen different local helper names. Pass
 * `plural` for irregular nouns ("1 entry" / "2 entries") and `localizeCount` when the
 * count should carry thousands separators.
 */
export function pluralizeCount(count: number, singular: string, options: PluralizeCountOptions = {}): string {
  const { plural = `${singular}s`, localizeCount = false } = options;
  const renderedCount = localizeCount ? count.toLocaleString(FORMAT_LOCALE) : String(count);
  return `${renderedCount} ${count === 1 ? singular : plural}`;
}

/** Amount with an ISO code suffix and no symbol, e.g. "1,234.56 USD"; `signed` gives "+1,234.56 USD". */
export function formatAmountWithCode(
  value: NullableNumber,
  currency: string | null | undefined,
  options: SignableNumberFormatOptions = {}
): string {
  const { minimumFractionDigits = 2, maximumFractionDigits = 2, signed = false, fallback = DEFAULT_FALLBACK } = options;
  if (!isRenderable(value)) {
    return fallback;
  }

  const text = formatNumber(value, { minimumFractionDigits, maximumFractionDigits, signed });
  const code = currency?.trim();
  return code ? `${text} ${code}` : text;
}

export interface CurrencyWithCodeFormatOptions {
  /** Explicit "+" on positives and two fraction digits on every non-zero amount, e.g. "+€100.00 EUR". */
  signed?: boolean;
}

/**
 * Symbol-style currency that keeps the ISO code visible when the symbol alone is ambiguous,
 * e.g. "$100 USD", "-£100 GBP", "AED 100" (the code is not repeated when Intl already shows it).
 * Unsigned amounts drop trailing zero cents ("€100 EUR"); `signed` pins two decimals on non-zero
 * amounts ("+€100.00 EUR") and leaves zero, including -0, unsigned ("€0 EUR"). A blank code means USD.
 */
export function formatCurrencyWithCode(value: number, currency: string, options: CurrencyWithCodeFormatOptions = {}): string {
  const { signed = false } = options;
  const code = currency.trim().toUpperCase();
  const normalizedValue = value === 0 ? 0 : value;
  const amount = formatCurrency(normalizedValue, { currency: code || "USD", minimumFractionDigits: signed && normalizedValue !== 0 ? 2 : 0 });
  const prefix = signed && value > 0 && Number.isFinite(value) ? "+" : "";
  return code && !amount.includes(code) ? `${prefix}${amount} ${code}` : `${prefix}${amount}`;
}

/** Grouped count, e.g. 1234 -> "1,234"; the same output as `value.toLocaleString("en-US")`. `signed` gives "+1,234". */
export function formatCount(value: NullableNumber, options: Pick<SignableNumberFormatOptions, "fallback" | "signed"> = {}): string {
  const { signed = false, fallback = DEFAULT_FALLBACK } = options;
  if (!isRenderable(value)) {
    return fallback;
  }

  return signed ? `${signPrefix(value)}${Math.abs(value).toLocaleString(FORMAT_LOCALE)}` : value.toLocaleString(FORMAT_LOCALE);
}

/**
 * Where a money value is read, which sets its precision and sign convention:
 *  - `accounting`: the currency's minor units (2 for USD, 0 for JPY), negatives in parentheses.
 *  - `trading`: instrument precision (`decimals`, else the currency's minor units), minus sign.
 *  - `summary`: whole units for headline figures, minus sign.
 */
export type MoneyContext = "accounting" | "trading" | "summary";

/**
 * How a money figure is spelled, independent of its reading context:
 *  - `screen`: Intl en-US symbols ("$", "€", "CA$", "CHF "), "-" for negatives. The default.
 *  - `statement`: the accounting statement symbol table (`currencySymbol`: "$", "€", "CHF ",
 *    "CODE" + narrow no-break space for unmapped codes) and U+2212 "−" for negatives, as ledgers,
 *    statements and journal grids print them.
 */
export type MoneyConvention = "screen" | "statement";

export interface MoneyFormatOptions {
  /** ISO 4217 code; `null` (or "" under `statement`) renders a bare amount with no symbol. @default "USD" */
  currency?: string | null;
  /** @default "trading" */
  context?: MoneyContext;
  /** Fraction digits; overrides the context default (instrument precision for trading). */
  decimals?: number;
  /** Positives get an explicit "+". A value that rounds to zero is always unsigned. */
  signed?: boolean;
  /** Negatives in parentheses, e.g. "($1,234.00)". @default true for the accounting context, else false */
  parens?: boolean;
  /** An exact zero renders as an em dash. @default false */
  zeroDash?: boolean;
  /** @default "screen" */
  convention?: MoneyConvention;
  /** Rendered when the value is null, undefined, not finite, or a string that is not a number. */
  fallback?: string;
}

const STATEMENT_SYMBOLS: Record<string, string> = {
  USD: "$",
  EUR: "€",
  GBP: "£",
  JPY: "¥",
  CNY: "¥",
  CHF: "CHF ",
  CAD: "$",
  AUD: "$"
};

/**
 * Statement-convention currency symbol: code -> leading symbol. Unmapped codes render as "CODE"
 * plus a narrow no-break space (U+202F); a blank code renders no symbol.
 */
export function currencySymbol(code?: string | null): string {
  if (!code) return "";
  return STATEMENT_SYMBOLS[code] ?? code + " ";
}

interface ScreenCurrencyLayout {
  prefix: string;
  suffix: string;
  minorUnits: number;
}

const screenCurrencyLayouts = new Map<string, ScreenCurrencyLayout>();

/** Intl's en-US symbol placement and minor units for a code, read once per code from a sample. */
function screenCurrencyLayout(code: string): ScreenCurrencyLayout {
  let layout = screenCurrencyLayouts.get(code);
  if (layout) {
    return layout;
  }
  try {
    const formatter = new Intl.NumberFormat(FORMAT_LOCALE, { style: "currency", currency: code });
    const parts = formatter.formatToParts(1);
    const first = parts.findIndex((part) => part.type === "integer");
    const last = parts.length - 1 - [...parts].reverse().findIndex((part) => part.type === "integer" || part.type === "fraction");
    layout = {
      prefix: parts.slice(0, first).map((part) => part.value).join(""),
      suffix: parts.slice(last + 1).map((part) => part.value).join(""),
      minorUnits: formatter.resolvedOptions().maximumFractionDigits ?? 2
    };
  } catch {
    // A code Intl rejects outright renders after the amount's code, e.g. "US-D 1,234.56".
    layout = { prefix: `${code} `, suffix: "", minorUnits: 2 };
  }
  screenCurrencyLayouts.set(code, layout);
  return layout;
}

/**
 * The one money formatter. A decimal string is formatted exactly from its digits (never through a
 * float), and a number from its shortest decimal form; both round half away from zero. Examples:
 * trading "-$1,234.56", summary "$1,235", accounting "($1,234.00)",
 * `formatMoney("12345678901234.565")` -> "$12,345,678,901,234.57", and the statement convention
 * `formatMoney(-42.5, { convention: "statement", currency: null })` -> "−42.50".
 */
export function formatMoney(value: MoneyValue, options: MoneyFormatOptions = {}): string {
  const {
    currency,
    context = "trading",
    decimals,
    signed = false,
    parens = context === "accounting",
    zeroDash = false,
    convention = "screen",
    fallback = DEFAULT_FALLBACK
  } = options;
  const parsed = parseExactDecimal(value);
  if (!parsed) {
    return fallback;
  }
  if (zeroDash && parsed.units === 0n) {
    return "—";
  }

  const code = currency === null ? null : currency === undefined ? "USD" : currency.trim() || (convention === "statement" ? null : "USD");
  let prefix = "";
  let suffix = "";
  let minorUnits = 2;
  if (code !== null && convention === "statement") {
    prefix = currencySymbol(code);
  } else if (code !== null) {
    ({ prefix, suffix, minorUnits } = screenCurrencyLayout(code));
  }

  const digits = decimals ?? (context === "summary" ? 0 : minorUnits);
  const magnitude = formatExactMagnitude(parsed, digits);
  const amount = `${prefix}${magnitude.text}${suffix}`;
  // A value that rounds to zero at this precision is shown unsigned.
  if (magnitude.roundsToZero) {
    return amount;
  }
  if (parsed.negative) {
    return parens ? `(${amount})` : `${convention === "statement" ? "−" : "-"}${amount}`;
  }
  return signed ? `+${amount}` : amount;
}

/** Tone of a signed P&L figure: gain, loss, or flat/unknown. */
export type PnlTone = "success" | "danger" | "default";

/** Green for a gain, red for a loss, neutral for zero or a missing value. */
export function pnlToneForValue(value: NullableNumber): PnlTone {
  if (!isRenderable(value) || value === 0) {
    return "default";
  }
  return value > 0 ? "success" : "danger";
}

export interface PnlFormatOptions {
  /** ISO 4217 code. @default "USD" */
  currency?: string;
  /** Fraction digits. @default 2; pass 0 for a headline figure. */
  decimals?: number;
  fallback?: string;
}

/**
 * The one signed P&L style: an explicit sign on every non-zero amount, thousands separators and
 * fixed decimals, e.g. "+$1,234.56" / "-$1,234.56" / "$0.00". Pair it with `pnlToneForValue` so
 * the sign and the color always agree.
 */
export function formatPnl(value: NullableNumber, options: PnlFormatOptions = {}): string {
  const { currency, decimals = 2, fallback } = options;
  return formatMoney(value, { context: "trading", signed: true, decimals, currency, fallback });
}

const PROFIT_AND_LOSS_SPELLING = /\bP(?:n|N|\/|\s?&\s?|\sand\s)L\b/g;

/**
 * Operator-facing spelling of financial terms in a label. Profit and loss is always "P&L", never
 * "PnL", "P/L" or "P & L", so a label that arrives from a read model or a fixture reads the same
 * as one written in a screen. Shared label seams (stat strip, metric card, record explorer) apply
 * it; identifiers and field names are not labels and are left alone.
 */
export function normalizeFinancialLabel(label: string): string {
  return label.replace(PROFIT_AND_LOSS_SPELLING, "P&L");
}

export interface QuantityFormatOptions {
  /** Maximum fraction digits (instrument precision). @default 4 */
  maxDecimals?: number;
  /** Minimum fraction digits. @default 0 */
  minDecimals?: number;
  signed?: boolean;
  fallback?: string;
}

/** Quantity or price at instrument precision, e.g. "1,250", "0.0125", "+12.5". */
export function formatQuantity(value: NullableNumber, options: QuantityFormatOptions = {}): string {
  const { maxDecimals = 4, minDecimals = 0, signed = false, fallback = DEFAULT_FALLBACK } = options;
  if (!isRenderable(value)) {
    return fallback;
  }

  const text = value.toLocaleString(FORMAT_LOCALE, { minimumFractionDigits: minDecimals, maximumFractionDigits: maxDecimals });
  return signed && value > 0 ? `+${text}` : text;
}

const UTC_MONTH_LABELS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"] as const;

function padTwo(value: number): string {
  return String(value).padStart(2, "0");
}

/**
 * UTC timestamp label that always carries the year, e.g. "Jun 30, 2026, 18:05 UTC". The year
 * is never dropped: a ledger or evidence timestamp has to be unambiguous, and a label that
 * omitted it for the current year would read differently as the calendar turns. An invalid
 * date renders "Invalid Date", as `toLocaleString` does.
 */
export function formatUtcTimestamp(date: Date): string {
  if (Number.isNaN(date.getTime())) {
    return "Invalid Date";
  }
  return `${UTC_MONTH_LABELS[date.getUTCMonth()]} ${date.getUTCDate()}, ${date.getUTCFullYear()}, ${padTwo(date.getUTCHours())}:${padTwo(date.getUTCMinutes())} UTC`;
}

/** UTC calendar date with the year, e.g. "Jun 30, 2026". */
export function formatUtcDate(date: Date): string {
  return `${UTC_MONTH_LABELS[date.getUTCMonth()]} ${date.getUTCDate()}, ${date.getUTCFullYear()}`;
}

const MONTH_NAMES = ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"] as const;

/** UTC calendar month with the year, e.g. "June 2026", for a period label. */
export function formatUtcMonthYear(date: Date): string {
  return `${MONTH_NAMES[date.getUTCMonth()]} ${date.getUTCFullYear()}`;
}

const dateTimeFormatters = new Map<string, Intl.DateTimeFormat>();

/** Currency metadata for authoring a governed policy; the server validates its own precision. */
export function currencyMinorUnitPrecision(currency: string): number {
  const normalized = currency.trim().toUpperCase();
  if (!/^[A-Z]{3}$/.test(normalized)) return 2;
  return new Intl.NumberFormat(FORMAT_LOCALE, { style: "currency", currency: normalized }).resolvedOptions().maximumFractionDigits ?? 2;
}

/**
 * A date or time in a layout the fixed helpers above do not cover, through one pinned-locale
 * Intl.DateTimeFormat per option set, e.g. `formatDateTime(date, { month: "short", day: "numeric",
 * hour: "2-digit", minute: "2-digit", timeZone: "UTC" })`. Workstation times are shown in UTC, so
 * pass `timeZone: "UTC"`; without it the formatter falls back to the operator's local clock. An
 * invalid date renders "Invalid Date", as `toLocaleString` does.
 */
export function formatDateTime(date: Date, options: Intl.DateTimeFormatOptions): string {
  if (Number.isNaN(date.getTime())) {
    return "Invalid Date";
  }
  const key = JSON.stringify(options);
  let formatter = dateTimeFormatters.get(key);
  if (!formatter) {
    formatter = new Intl.DateTimeFormat(FORMAT_LOCALE, options);
    dateTimeFormatters.set(key, formatter);
  }
  return formatter.format(date);
}
