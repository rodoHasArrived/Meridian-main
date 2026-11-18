import { formatUtcDate } from "@/lib/format";
import type { MetricHistoryPoint } from "@/types";

/**
 * The latest run of established points, oldest first, or null when fewer than two remain. A gap
 * (null value) ends the run: the sparkline never draws a line across a day the source could not
 * establish, and never fills one in.
 */
export function trailingKnownRun(history: readonly MetricHistoryPoint[] | null | undefined): { date: string; value: number }[] | null {
  if (!history?.length) return null;
  const run: { date: string; value: number }[] = [];
  for (let index = history.length - 1; index >= 0; index--) {
    const point = history[index];
    if (point.value === null || !Number.isFinite(point.value)) break;
    run.unshift({ date: point.date, value: point.value });
  }
  return run.length >= 2 ? run : null;
}

/** Screen-reader and tooltip text for a sparkline: the span it covers and where it starts and ends. */
export function describeMetricHistory(label: string, run: readonly { date: string; value: number }[], totalPoints: number): string {
  const first = run[0];
  const last = run[run.length - 1];
  const gap = run.length < totalPoints ? " Earlier days could not be established from the source." : "";
  const day = (date: string) => formatUtcDate(new Date(`${date}T00:00:00Z`));
  return `${label} over ${run.length} days: ${first.value} on ${day(first.date)}, ${last.value} on ${day(last.date)}.${gap}`;
}
