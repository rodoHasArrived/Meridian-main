// Meridian shared time formatting. Relative-age text for freshness indicators and
// "last updated" labels; promoted from the watchlist view model so chips, screens,
// and view models share one age vocabulary. A thin layer over the timestamp formatters
// in `@/lib/format`.
import { formatRelativeTime, formatTimestamp } from "@/lib/format";

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
