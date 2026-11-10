// Shared currency, number, percent, count, quantity and date formatting for workstation screens.
// This is the one Intl-backed formatting module: screens call it instead of Intl.NumberFormat or
// toLocale*String directly (scripts/design-system-ratchets.mjs and the ESLint rule enforce that).
// All helpers pin the en-US locale so operator machines render identical values
// regardless of host locale, and all accept null/undefined/non-finite input,
// rendering the configurable `fallback` instead.
//
// SCOPE: this is the default formatting convention for the workstation. Accounting
// surfaces (ledger, statements, journal grids) intentionally use the statement
// convention in `components/accounting/money.ts` (parenthesized negatives, zero-dash);
// the two conventions differ by design — see the note in that file.

const FORMAT_LOCALE = "en-US";
const DEFAULT_FALLBACK = "—";

export type NullableNumber = number | null | undefined;

export interface NumberFormatOptions {
  minimumFractionDigits?: number;
  maximumFractionDigits?: number;
  /** Thousands separators; false for a value typed back into an input, e.g. "1234.5". @default true */
  useGrouping?: boolean;
  /** Rendered when the value is null, undefined, or not finite. */
  fallback?: string;
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

/** Grouped plain number, e.g. "1,234.56". */
export function formatNumber(value: NullableNumber, options: NumberFormatOptions = {}): string {
  const { minimumFractionDigits, maximumFractionDigits = 2, useGrouping, fallback = DEFAULT_FALLBACK } = options;
  if (!isRenderable(value)) {
    return fallback;
  }

  return value.toLocaleString(FORMAT_LOCALE, { minimumFractionDigits, maximumFractionDigits, useGrouping });
}

// PERCENT UNITS: the workstation carries percent-like values in two different units,
// and picking the wrong helper is a silent 100x error in financial output. Read the
// producing field before choosing:
//   - `formatPercent`        takes PERCENT units  (42.5 -> "42.5%")
//   - `formatRatioAsPercent` takes FRACTION units (0.425 -> "42.5%")
// A `…Percent` suffix on a field name is NOT reliable evidence of its unit — see
// `maxDrawdownPercent`, which arrives as a fraction from the walk-forward path and
// as percent units from the live-metrics path.

/** Percent display for values already expressed in percent units, e.g. 42.5 -> "42.5%". */
export function formatPercent(value: NullableNumber, options: NumberFormatOptions = {}): string {
  const { fallback = DEFAULT_FALLBACK } = options;
  if (!isRenderable(value)) {
    return fallback;
  }

  return `${formatNumber(value, options)}%`;
}

/** Percent display for values expressed as a fraction of 1, e.g. 0.425 -> "42.5%". */
export function formatRatioAsPercent(value: NullableNumber, options: NumberFormatOptions = {}): string {
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
  return `${prefix}${formatNumber(Math.abs(value), options)}`;
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
  return `${prefix}${formatNumber(Math.abs(value), options)}`;
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

/** Amount with an ISO code suffix and no symbol, e.g. "1,234.56 USD". */
export function formatAmountWithCode(
  value: NullableNumber,
  currency: string | null | undefined,
  options: NumberFormatOptions = {}
): string {
  const { minimumFractionDigits = 2, maximumFractionDigits = 2, fallback = DEFAULT_FALLBACK } = options;
  if (!isRenderable(value)) {
    return fallback;
  }

  const text = value.toLocaleString(FORMAT_LOCALE, { minimumFractionDigits, maximumFractionDigits });
  const code = currency?.trim();
  return code ? `${text} ${code}` : text;
}

/** Grouped count, e.g. 1234 -> "1,234"; the same output as `value.toLocaleString("en-US")`. */
export function formatCount(value: NullableNumber, options: Pick<NumberFormatOptions, "fallback"> = {}): string {
  const { fallback = DEFAULT_FALLBACK } = options;
  if (!isRenderable(value)) {
    return fallback;
  }

  return value.toLocaleString(FORMAT_LOCALE);
}

/**
 * Where a money value is read, which sets its precision and sign convention:
 *  - `accounting`: the currency's minor units (2 for USD, 0 for JPY), negatives in parentheses.
 *  - `trading`: instrument precision (`decimals`, else the currency's minor units), minus sign.
 *  - `summary`: whole units for headline figures, minus sign.
 */
export type MoneyContext = "accounting" | "trading" | "summary";

export interface MoneyFormatOptions {
  /** ISO 4217 code; `null` renders a bare amount with no symbol. @default "USD" */
  currency?: string | null;
  /** @default "trading" */
  context?: MoneyContext;
  /** Fraction digits; overrides the context default (instrument precision for trading). */
  decimals?: number;
  /** Positives get an explicit "+". */
  signed?: boolean;
  fallback?: string;
}

/** Money for its reading context, e.g. accounting "(1,234.00)", trading "-$1,234.56", summary "$1,235". */
export function formatMoney(value: NullableNumber, options: MoneyFormatOptions = {}): string {
  const { currency, context = "trading", decimals, signed = false, fallback = DEFAULT_FALLBACK } = options;
  if (!isRenderable(value)) {
    return fallback;
  }

  const digits = decimals ?? (context === "summary" ? 0 : undefined);
  const code = currency === null ? null : currency?.trim() || "USD";
  let magnitude: string;
  if (code === null) {
    magnitude = Math.abs(value).toLocaleString(FORMAT_LOCALE, { minimumFractionDigits: digits ?? 2, maximumFractionDigits: digits ?? 2 });
  } else try {
    magnitude = new Intl.NumberFormat(FORMAT_LOCALE, {
      style: "currency",
      currency: code,
      ...(digits === undefined ? {} : { minimumFractionDigits: digits, maximumFractionDigits: digits })
    }).format(Math.abs(value));
  } catch {
    magnitude = `${code} ${Math.abs(value).toLocaleString(FORMAT_LOCALE, { minimumFractionDigits: digits ?? 2, maximumFractionDigits: digits ?? 2 })}`;
  }

  if (value < 0) {
    return context === "accounting" ? `(${magnitude})` : `-${magnitude}`;
  }
  return signed && value > 0 ? `+${magnitude}` : magnitude;
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
