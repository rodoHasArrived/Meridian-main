import { formatCount as formatCountValue, type MoneyFormatOptions, type SignableNumberFormatOptions } from "@/lib/format";

/** Strategy-run money for `formatMoney` from `@/lib/format`: whole dollars, "Unavailable" when missing, e.g. "-$1,235". */
export const STRATEGY_MONEY: MoneyFormatOptions = { context: "summary", fallback: "Unavailable" };

/** `STRATEGY_MONEY` with an explicit sign on gains, e.g. "+$1,235" / "-$1,235" / "$0". */
export const STRATEGY_SIGNED_MONEY: MoneyFormatOptions = { ...STRATEGY_MONEY, signed: true };

/** Signed two-decimal percent for `formatRatioAsPercent` (fraction units), e.g. 0.425 -> "+42.50%". */
export const STRATEGY_SIGNED_RETURN: SignableNumberFormatOptions = { signed: true, minimumFractionDigits: 2, maximumFractionDigits: 2, fallback: "Unavailable" };

export function formatText(value: string | null | undefined): string {
  const trimmed = value?.trim();
  return trimmed ? trimmed : "Unavailable";
}

export function formatOptionalNotes(value: string | null | undefined): string {
  const trimmed = value?.trim();
  return trimmed ? trimmed : "No operator notes were recorded for this run.";
}

export function formatNullableNumber(value: number | null | undefined, digits: number): string {
  return typeof value === "number" && Number.isFinite(value)
    ? value.toFixed(digits)
    : "Unavailable";
}

export function countBy<T>(items: T[], selector: (item: T) => string): Map<string, number> {
  const counts = new Map<string, number>();
  for (const item of items) {
    const key = selector(item);
    counts.set(key, (counts.get(key) ?? 0) + 1);
  }

  return counts;
}

export function distinctFormattedValues(values: Array<string | null | undefined>): string[] {
  return [...new Set(values.map((value) => formatText(value)).filter((value) => value !== "Unavailable"))].sort();
}

export function isLiveAdjacentMode(mode: string | null | undefined): boolean {
  return mode?.toLowerCase() === "paper" || mode?.toLowerCase() === "live";
}

export function formatCount(value: number | null | undefined): string {
  if (typeof value !== "number" || !Number.isFinite(value)) {
    return "Unavailable";
  }

  return formatCountValue(value);
}

export function formatPromotionState(value: string | null | undefined): string {
  const text = formatText(value);
  if (text === "Unavailable") {
    return text;
  }

  const normalized = text
    .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
    .replace(/[_-]+/g, " ")
    .replace(/\s+/g, " ")
    .trim();

  return normalized
    ? `${normalized.charAt(0).toUpperCase()}${normalized.slice(1).toLowerCase()}`
    : "Unavailable";
}

export function parseDecimalToken(value: string | null | undefined): number | null {
  if (!value) {
    return null;
  }

  const normalized = value.replace(/[^0-9.+-]/g, "");
  if (!normalized) {
    return null;
  }

  const parsed = Number.parseFloat(normalized);
  return Number.isFinite(parsed) ? parsed : null;
}

export function parsePercentToken(value: string | null | undefined): number | null {
  const parsed = parseDecimalToken(value);
  return parsed === null ? null : parsed / 100;
}
