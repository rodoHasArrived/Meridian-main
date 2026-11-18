/**
 * How urgent a due date is (UX Phase E). "Due Jul 14, 2026" reads the same whether it is tomorrow or
 * three days late; this turns a due instant or date into a relative chip ("Overdue 3 days", "Due today",
 * "Due in 2 days") and a sort rank that puts overdue work first.
 *
 * Days are counted in calendar days in UTC, so a date-only value ("2026-07-14") never shifts with the
 * viewer's time zone and an instant later today is "Due today", not "Due in 0d".
 */
export type DueState = "overdue" | "today" | "soon" | "later" | "none";

export interface DueUrgency {
  state: DueState;
  /** Chip text, e.g. "Overdue 3 days", "Due today", "Due in 2 days", "Due Jul 14". Empty when there is no due date. */
  label: string;
  /** Whole calendar days from today to the due day: negative when overdue, null with no due date. */
  days: number | null;
  /** The due instant in milliseconds, or null. */
  dueMs: number | null;
}

/** Within this many days, a due date reads as "Due in Nd" rather than a date. */
export const DUE_SOON_DAYS = 7;
const DAY_MS = 86_400_000;
const MONTHS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"] as const;

function parseDue(value: string | null | undefined): number | null {
  const trimmed = value?.trim();
  if (!trimmed) return null;
  // A bare date is that calendar day, read in UTC.
  const parsed = /^\d{4}-\d{2}-\d{2}$/.test(trimmed) ? Date.parse(`${trimmed}T00:00:00Z`) : Date.parse(trimmed);
  return Number.isFinite(parsed) ? parsed : null;
}

function utcDay(ms: number): number {
  return Math.floor(ms / DAY_MS);
}

/**
 * Describes a due date relative to `nowMs`. Work that is done (`done: true`) is never overdue: it
 * keeps its date, so a finished item does not shout.
 */
export function describeDue(dueAt: string | null | undefined, nowMs: number = Date.now(), { done = false }: { done?: boolean } = {}): DueUrgency {
  const dueMs = parseDue(dueAt);
  if (dueMs === null) return { state: "none", label: "", days: null, dueMs: null };
  const days = utcDay(dueMs) - utcDay(nowMs);
  const date = new Date(dueMs);
  const dateLabel = `${MONTHS[date.getUTCMonth()]} ${date.getUTCDate()}${date.getUTCFullYear() !== new Date(nowMs).getUTCFullYear() ? `, ${date.getUTCFullYear()}` : ""}`;
  if (done) return { state: "later", label: `Due ${dateLabel}`, days, dueMs };
  // Spelled out: status badges render in capitals, where "3D" would be ambiguous.
  if (days < 0) return { state: "overdue", label: `Overdue ${-days} ${-days === 1 ? "day" : "days"}`, days, dueMs };
  if (days === 0) return { state: "today", label: "Due today", days, dueMs };
  if (days <= DUE_SOON_DAYS) return { state: "soon", label: days === 1 ? "Due tomorrow" : `Due in ${days} days`, days, dueMs };
  return { state: "later", label: `Due ${dateLabel}`, days, dueMs };
}

const STATE_RANK: Record<DueState, number> = { overdue: 0, today: 1, soon: 2, later: 3, none: 4 };

/** Overdue first (most overdue at the top), then due today, then soonest; no due date last. Stable for ties. */
export function compareByUrgency(a: DueUrgency, b: DueUrgency): number {
  const rank = STATE_RANK[a.state] - STATE_RANK[b.state];
  if (rank !== 0) return rank;
  if (a.dueMs === null || b.dueMs === null) return 0;
  return a.dueMs - b.dueMs;
}

/** Sorts rows by the urgency of their due date, keeping the original order within equal urgency. */
export function sortByUrgency<T>(rows: readonly T[], getDue: (row: T) => DueUrgency): T[] {
  return rows
    .map((row, index) => ({ row, index, due: getDue(row) }))
    .sort((a, b) => compareByUrgency(a.due, b.due) || a.index - b.index)
    .map((entry) => entry.row);
}
