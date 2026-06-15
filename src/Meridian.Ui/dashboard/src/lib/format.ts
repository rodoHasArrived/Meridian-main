/**
 * Shared display-formatting helpers for the operator workstation.
 *
 * Several screens independently reimplemented currency/percent rendering, and a
 * few omitted the guard for non-finite inputs — so values such as `NaN`,
 * `Infinity`, `null`, or `undefined` surfaced to operators as `"$NaN"`,
 * `"Infinity%"`, or `"-$0"`. These helpers centralize the workstation's
 * established convention of rendering the em-dash sentinel (`EMPTY_VALUE`) for
 * any value that cannot be meaningfully formatted, keeping fund-operations
 * surfaces defensive and consistent.
 *
 * For finite inputs the output matches the per-screen formatters these helpers
 * replace; only the non-finite / missing cases change (sentinel instead of a
 * broken numeric string).
 */

/** Em-dash sentinel rendered when a value cannot be meaningfully formatted. */
export const EMPTY_VALUE = "—";

function isFiniteNumber(value: number | null | undefined): value is number {
  return value !== null && value !== undefined && Number.isFinite(value);
}

/**
 * Formats a USD amount using the locale currency style, e.g. `"$1,234.50"`.
 * Returns {@link EMPTY_VALUE} for missing or non-finite values.
 */
export function formatUsd(
  value: number | null | undefined,
  options: { maximumFractionDigits?: number; minimumFractionDigits?: number } = {}
): string {
  if (!isFiniteNumber(value)) {
    return EMPTY_VALUE;
  }

  return value.toLocaleString(undefined, {
    style: "currency",
    currency: "USD",
    ...options
  });
}

/**
 * Formats a USD amount compactly with magnitude suffixes, e.g. `"$10.0M"`,
 * `"$250.0K"`, or `"$420"`. Negative amounts are prefixed with `"-"`.
 * Returns {@link EMPTY_VALUE} for missing or non-finite values.
 */
export function formatCompactUsd(value: number | null | undefined): string {
  if (!isFiniteNumber(value)) {
    return EMPTY_VALUE;
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

/**
 * Formats a percentage value already expressed in percent units (e.g. `60` ->
 * `"60%"`, `12.5` -> `"12.5%"`). Whole numbers omit the fractional digit.
 * Returns {@link EMPTY_VALUE} for missing or non-finite values.
 */
export function formatPercent(value: number | null | undefined): string {
  if (!isFiniteNumber(value)) {
    return EMPTY_VALUE;
  }

  return Number.isInteger(value) ? `${value}%` : `${value.toFixed(1)}%`;
}
