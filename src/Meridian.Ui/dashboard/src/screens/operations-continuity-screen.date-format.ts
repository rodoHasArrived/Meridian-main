import { formatUtcDate, formatUtcTimestamp } from "@/lib/format";

/** Operations Continuity's retained UTC timestamp and due-date display formats. */
export function formatDate(value: string | null | undefined): string {
  if (!value) {
    return "Not recorded";
  }

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return value;
  }

  return formatUtcTimestamp(date);
}

export function formatDateOnly(value: string | null | undefined): string {
  if (!value) {
    return "No due date";
  }

  const date = new Date(/^\d{4}-\d{2}-\d{2}$/.test(value) ? `${value}T00:00:00Z` : value);
  if (Number.isNaN(date.getTime())) {
    return value;
  }

  return formatUtcDate(date);
}
