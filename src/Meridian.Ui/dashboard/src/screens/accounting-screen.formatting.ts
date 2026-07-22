import {
  formatCurrency as formatCurrencyAmount,
  formatPrefixedCurrency,
  formatSignedCurrency as formatSignedCurrencyAmount,
  pluralizeCount
} from "@/lib/format";

export function formatCount(count: number, singular: string): string {
  return pluralizeCount(count, singular);
}

export function formatBytes(value: number): string {
  if (!Number.isFinite(value) || value <= 0) {
    return "0 B";
  }

  const units = ["B", "KB", "MB", "GB"];
  let size = value;
  let unitIndex = 0;
  while (size >= 1024 && unitIndex < units.length - 1) {
    size /= 1024;
    unitIndex += 1;
  }

  const formatted = size >= 10 || unitIndex === 0
    ? size.toFixed(0)
    : size.toFixed(1).replace(/\.0$/, "");
  return `${formatted} ${units[unitIndex]}`;
}

export function formatCurrency(value: number) {
  return formatPrefixedCurrency(value);
}

export function formatCurrencyWithCode(value: number, currency: string, signed = false): string {
  const amount = signed ? formatSignedCurrency(value) : formatCurrency(value);
  const code = currency.trim();
  return code ? `${amount} ${code}` : amount;
}

export function formatCurrencyForCode(value: number, currency: string): string {
  return formatCurrencyAmount(value, { currency, minimumFractionDigits: 0 });
}

export function formatSignedCurrency(value: number): string {
  return formatSignedCurrencyAmount(value, { minimumFractionDigits: 2, zeroLabel: "$0" });
}

export function formatDateTimeLabel(value: string | null | undefined): string {
  if (!value) {
    return "Not recorded";
  }

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return value;
  }

  return `${UTC_MONTH_LABELS[date.getUTCMonth()]} ${date.getUTCDate()}, ${padUtc(date.getUTCHours())}:${padUtc(date.getUTCMinutes())} UTC`;
}

export function formatDateOnly(value: string): string {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toISOString().slice(0, 10);
}

function padUtc(value: number): string {
  return String(value).padStart(2, "0");
}

const UTC_MONTH_LABELS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

export function toDomId(value: string): string {
  const normalized = value.trim().toLowerCase().replace(/[^a-z0-9_-]+/g, "-").replace(/^-+|-+$/g, "");
  return normalized || "profile";
}
