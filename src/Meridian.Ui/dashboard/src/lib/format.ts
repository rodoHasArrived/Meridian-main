// Shared currency, number, and percent formatting for workstation screens.
// All helpers pin the en-US locale so operator machines render identical values
// regardless of host locale, and all accept null/undefined/non-finite input,
// rendering the configurable `fallback` instead.

const FORMAT_LOCALE = "en-US";
const DEFAULT_FALLBACK = "—";

export type NullableNumber = number | null | undefined;

export interface NumberFormatOptions {
  minimumFractionDigits?: number;
  maximumFractionDigits?: number;
  /** Rendered when the value is null, undefined, or not finite. */
  fallback?: string;
}

export interface CurrencyFormatOptions extends NumberFormatOptions {
  /** ISO 4217 code; blank or unknown codes render as "CODE 1,234.56". */
  currency?: string;
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
  const { minimumFractionDigits, maximumFractionDigits = 2, fallback = DEFAULT_FALLBACK } = options;
  if (!isRenderable(value)) {
    return fallback;
  }

  return value.toLocaleString(FORMAT_LOCALE, { minimumFractionDigits, maximumFractionDigits });
}

/** Percent display for values already expressed in percent units, e.g. 42.5 -> "42.5%". */
export function formatPercent(value: NullableNumber, options: NumberFormatOptions = {}): string {
  const { fallback = DEFAULT_FALLBACK } = options;
  if (!isRenderable(value)) {
    return fallback;
  }

  return `${formatNumber(value, options)}%`;
}

/** Symbol-style currency via Intl, e.g. "$1,234.56"; unknown codes render as "CODE 1,234.56". */
export function formatCurrency(value: NullableNumber, options: CurrencyFormatOptions = {}): string {
  const { currency = "USD", minimumFractionDigits, maximumFractionDigits = 2, fallback = DEFAULT_FALLBACK } = options;
  if (!isRenderable(value)) {
    return fallback;
  }

  const code = currency.trim() || "USD";
  try {
    return new Intl.NumberFormat(FORMAT_LOCALE, {
      style: "currency",
      currency: code,
      minimumFractionDigits,
      maximumFractionDigits
    }).format(value);
  } catch {
    return `${code} ${value.toLocaleString(FORMAT_LOCALE, { maximumFractionDigits: 2 })}`;
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
