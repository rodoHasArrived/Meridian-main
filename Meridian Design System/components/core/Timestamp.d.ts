/**
 * Timestamp — the time twin of `AmountCell`. Enforces the content rules for time: UTC, mono,
 * tabular, explicit. Renders a semantic `<time>`; the full UTC form is always on `title`.
 *
 * @example
 * <Timestamp value={run.completedAt} />                  // 2026-06-09 20:00:00Z
 * <Timestamp value={quote.ts} format="time" />           // 20:00:04Z
 * <Timestamp value={lastSeen} format="relative" />       // 6m ago (auto-ticks)
 */
export interface TimestampProps extends React.TimeHTMLAttributes<HTMLTimeElement> {
  /** Date, epoch ms, or ISO string. Invalid values render an em dash. */
  value: Date | number | string;
  /** "utc" full stamp · "time" time-of-day · "relative" auto-ticking "6m ago". @default "utc" */
  format?: "utc" | "time" | "relative";
  /** Re-render interval for relative format, ms. @default 1000 */
  tickMs?: number;
}
export declare function Timestamp(props: TimestampProps): JSX.Element;
