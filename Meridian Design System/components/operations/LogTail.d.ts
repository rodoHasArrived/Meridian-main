/**
 * LogTail — the evidence surface for raw process output (backfill jobs, quality scans,
 * strategy runs). Mono stream with UTC time-of-day per line, level-count filter chips, and a
 * follow-tail that pauses automatically when the operator scrolls up (and resumes at bottom).
 * Warn/error lines carry an alpha-10 wash. Never summarizes — the raw line is the evidence.
 *
 * @example
 * <LogTail title="backfill-7742 · Polygon daily" height={260} entries={[
 *   { ts: t0,        level: "info",  source: "fetch",  text: "XNAS window 2026-06-01..2026-06-30 · 21 sessions" },
 *   { ts: t0 + 900,  level: "warn",  source: "verify", text: "AAPL 2026-06-19 short session · 390 → 210 bars expected" },
 *   { ts: t0 + 2100, level: "error", source: "write",  text: "parquet flush failed · retrying (1/3)" },
 * ]} />
 */
export interface LogTailEntry {
  /** Date, epoch ms, or ISO string — rendered as UTC time-of-day. */
  ts: Date | number | string;
  /** @default "info" */
  level?: "error" | "warn" | "info" | "debug";
  /** Subsystem tag rendered before the message (e.g. "fetch", "verify"). */
  source?: string;
  text: string;
  /** Stable key for streaming appends. */
  id?: string | number;
}
export interface LogTailProps extends React.HTMLAttributes<HTMLDivElement> {
  entries?: LogTailEntry[];
  /** Small-caps header label. @default "Log" */
  title?: string;
  /** Fixed height (px or CSS string) — the body scrolls inside it. @default 240 */
  height?: number | string;
  /** Start following the tail. Scrolling up pauses; scrolling to bottom resumes. @default true */
  follow?: boolean;
  /** Soft-wrap long lines instead of horizontal scroll. @default false */
  wrap?: boolean;
  /** Show the per-level count filter chips. @default true */
  levelFilter?: boolean;
}
export declare function LogTail(props: LogTailProps): JSX.Element;
