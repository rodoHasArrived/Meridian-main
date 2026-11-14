// Meridian shared time formatting. Relative-age text for freshness indicators and
// "last updated" labels; promoted from the watchlist view model so chips, screens,
// and view models share one age vocabulary. Both helpers are thin layers over the
// timestamp formatters in `@/lib/format`.
import { formatRelativeTime, formatTimestamp, formatUtcTimestamp } from "@/lib/format";

/** A UTC-clock timestamp from ISO text, e.g. "Nov 11, 2026, 00:30 UTC"; "Timestamp unavailable" when absent. */
export function formatDateTimeUtc(value: string | null | undefined): string {
  const timestamp = Date.parse(value ?? "");
  if (!Number.isFinite(timestamp)) return "Timestamp unavailable";
  return formatUtcTimestamp(new Date(timestamp));
}

/**
 * Age of a past instant, e.g. "30s ago", "5h ago"; "Never" when absent or invalid. A future
 * instant (clock skew) renders as the absolute timestamp in the display zone instead.
 */
export function formatRelativeAge(iso: string | null, now = Date.now()): string {
  if (!iso) {
    return "Never";
  }

  const timestamp = new Date(iso).getTime();
  if (Number.isNaN(timestamp)) {
    return "Never";
  }

  return now - timestamp < 0 ? formatTimestamp(timestamp) : formatRelativeTime(timestamp, now);
}
