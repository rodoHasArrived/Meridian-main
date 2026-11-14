import { formatNumber, pluralizeCount, formatTimestamp } from "@/lib/format";

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

export function formatDateOnly(value: string): string {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toISOString().slice(0, 10);
}

export function toDomId(value: string): string {
  const normalized = value.trim().toLowerCase().replace(/[^a-z0-9_-]+/g, "-").replace(/^-+|-+$/g, "");
  return normalized || "profile";
}
