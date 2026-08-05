import { formatCurrency as formatCurrencyAmount } from "@/lib/format";

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

export function formatSignedNullableNumber(value: number | null | undefined, digits: number): string {
  if (typeof value !== "number" || !Number.isFinite(value)) {
    return "Unavailable";
  }

  const formatted = Math.abs(value).toFixed(digits);
  return value > 0 ? `+${formatted}` : value < 0 ? `-${formatted}` : formatted;
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

export function formatMoney(value: number | null | undefined, signed = false): string {
  if (typeof value !== "number" || !Number.isFinite(value)) {
    return "Unavailable";
  }

  const amount = formatCurrencyAmount(Math.abs(value), { maximumFractionDigits: 0 });

  if (!signed) {
    return value < 0 ? `-${amount}` : amount;
  }

  if (value > 0) {
    return `+${amount}`;
  }

  if (value < 0) {
    return `-${amount}`;
  }

  return amount;
}

export function formatSignedCount(value: number | null | undefined): string {
  if (typeof value !== "number" || !Number.isFinite(value)) {
    return "Unavailable";
  }

  const sign = value > 0 ? "+" : "";
  return `${sign}${value.toLocaleString()}`;
}

export function formatCount(value: number | null | undefined): string {
  if (typeof value !== "number" || !Number.isFinite(value)) {
    return "Unavailable";
  }

  return value.toLocaleString();
}

export function formatSignedPercent(value: number | null | undefined): string {
  if (typeof value !== "number" || !Number.isFinite(value)) {
    return "Unavailable";
  }

  const formatted = `${Math.abs(value * 100).toFixed(2)}%`;
  if (value > 0) {
    return `+${formatted}`;
  }

  if (value < 0) {
    return `-${formatted}`;
  }

  return formatted;
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
