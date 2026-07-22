/**
 * EventTimeline — vertical audit trail. Where `DiffView` shows one change, the timeline shows
 * the sequence: who did what, when, with what evidence. Severity-dotted rail, mono UTC
 * time-of-day, optional day grouping. Pair with `DiffView` inside `detail` renders for
 * field-level before → after.
 *
 * @example
 * <EventTimeline events={[
 *   { ts: t2, action: "Rule threshold raised", actor: "r.alvarez", severity: "warning",
 *     detail: "Drawdown breach -5.0% → -8.0% · window 15m",
 *     evidence: { label: "change-4118.json", status: "Ready", onOpen: open } },
 *   { ts: t1, action: "Backfill complete", actor: "scheduler", severity: "success",
 *     detail: "412,008 bars · 0 gaps" },
 * ]} />
 */
export interface TimelineEvent {
  /** Date, epoch ms, or ISO string. Events render in the order given (newest first by convention). */
  ts: Date | number | string;
  /** What happened — terse, declarative ("Backfill complete", not "The backfill finished!"). */
  action: string;
  /** Who/what did it — user id or system name, rendered as a mono chip. */
  actor?: string;
  /** Evidence line under the action. */
  detail?: React.ReactNode;
  /** Dot color. Omit for neutral. */
  severity?: "success" | "warning" | "danger" | "accent";
  /** Renders an `EvidenceLink` under the event (label + status + route / href / onOpen). */
  evidence?: { label: string; href?: string; onOpen?: () => void; status?: string; route?: React.ReactNode };
  /** Stable key. */
  id?: string | number;
}
export interface EventTimelineProps extends React.HTMLAttributes<HTMLDivElement> {
  events?: TimelineEvent[];
  /** Insert a mono day header ("2026-07-02") whenever the date changes. @default true */
  groupByDay?: boolean;
  /** Tighter row spacing for rail placements. @default false */
  dense?: boolean;
}
export declare function EventTimeline(props: EventTimelineProps): JSX.Element;
