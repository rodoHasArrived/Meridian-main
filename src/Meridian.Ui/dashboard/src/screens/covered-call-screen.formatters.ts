export function formatPrice(value: number): string {
  if (!Number.isFinite(value)) return "—";
  return value.toFixed(2);
}

export function formatDecimal(value: number, digits: number): string {
  if (!Number.isFinite(value)) return "—";
  return value.toFixed(digits);
}

/**
 * Input is a fraction of 1 (0.425 -> "42.5%"), not percent units.
 *
 * Implied volatility and CAGR arrive from the covered-call API as fractions.
 * Kept local rather than delegating to `@/lib/format` because these render
 * ungrouped (`toFixed`); the shared helper adds thousands separators.
 */
export function formatRatioAsPercent(value: number): string {
  if (!Number.isFinite(value)) return "—";
  return `${(value * 100).toFixed(1)}%`;
}

export function formatCount(value: number): string {
  if (!Number.isFinite(value)) return "—";
  return Math.trunc(value).toLocaleString("en-US");
}

export function formatSignedMoney(value: number): string {
  if (!Number.isFinite(value)) return "—";
  const sign = value < 0 ? "-$" : "$";
  return `${sign}${Math.abs(value).toLocaleString("en-US", { maximumFractionDigits: 2 })}`;
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

  return `${UTC_MONTH_LABELS[date.getUTCMonth()]} ${date.getUTCDate()}, ${padUtc(date.getUTCHours())}:${padUtc(date.getUTCMinutes())} UTC`;
}

function padUtc(value: number): string {
  return String(value).padStart(2, "0");
}

const UTC_MONTH_LABELS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
