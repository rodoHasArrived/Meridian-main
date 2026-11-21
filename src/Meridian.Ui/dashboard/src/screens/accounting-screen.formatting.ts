import { formatNumber, pluralizeCount, formatTimestamp, formatUtcDate } from "@/lib/format";

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

  const formatted = formatNumber(size, { maximumFractionDigits: size >= 10 || unitIndex === 0 ? 0 : 1 });
  return `${formatted} ${units[unitIndex]}`;
}

export function formatDateTimeLabel(value: string | null | undefined): string {
  if (!value) {
    return "Not recorded";
  }

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return value;
  }

  return formatTimestamp(date);
}

const DATE_ONLY = /^\d{4}-\d{2}-\d{2}$/;
const MIDNIGHT_UTC = /^\d{4}-\d{2}-\d{2}T00:00(?::00(?:\.0+)?)?(?:Z|\+00:00)$/;

/**
 * An accounting date as a date. Effective, posting and period dates arrive date-only or as UTC
 * midnight; printing those with "00:00 UTC" invents a time nobody recorded. Anything carrying a
 * real time keeps the timestamp format.
 */
export function formatAccountingDate(value: string | null | undefined): string {
  if (!value) {
    return "Not recorded";
  }
  const trimmed = value.trim();
  if (DATE_ONLY.test(trimmed) || MIDNIGHT_UTC.test(trimmed)) {
    const date = new Date(`${trimmed.slice(0, 10)}T00:00:00Z`);
    return Number.isNaN(date.getTime()) ? value : formatUtcDate(date);
  }
  return formatDateTimeLabel(value);
}

export function formatDateOnly(value: string): string {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toISOString().slice(0, 10);
}

export function toDomId(value: string): string {
  const normalized = value.trim().toLowerCase().replace(/[^a-z0-9_-]+/g, "-").replace(/^-+|-+$/g, "");
  return normalized || "profile";
}
