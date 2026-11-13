import {
  formatCount as formatCountShared,
  formatDecimal as formatDecimalShared,
  formatRatioAsPercent as formatRatioAsPercentShared,
  formatUtcTimestamp
} from "@/lib/format";

export function formatPrice(value: number): string {
  return formatDecimalShared(value, 2);
}

export function formatDecimal(value: number, digits: number): string {
  return formatDecimalShared(value, digits);
}

/**
 * Input is a fraction of 1 (0.425 -> "42.5%"), not percent units.
 *
 * Implied volatility and CAGR arrive from the covered-call API as fractions.
 */
export function formatRatioAsPercent(value: number): string {
  return formatRatioAsPercentShared(value, { minimumFractionDigits: 1, maximumFractionDigits: 1 });
}

export function formatCount(value: number): string {
  if (!Number.isFinite(value)) return "—";
  return formatCountShared(Math.trunc(value));
}

export function formatExitReason(value: string): string {
  const normalized = value.trim();
  if (!normalized) return "Closed";
  return normalized
    .replace(/[_-]+/g, " ")
    .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
    .replace(/\s+/g, " ")
    .trim()
    .toLowerCase()
    .replace(/^\w/, (letter) => letter.toUpperCase());
}

export function formatUtcDateTime(value: string): string {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return "Unavailable";
  }

  return formatUtcTimestamp(date);
}

