/**
 * Case queue — operator triage list for reconciliation cases and workflow break
 * cases. Field names mirror the server case models (`ReconciliationCaseSummaryDto`,
 * `OperationsBreakCaseDto`): id · summary · category/reason · status · priority ·
 * assignee · SLA. Priority renders as a 3px left rail (Critical red, High amber),
 * status as a `SeverityBadge`, SLA as an `SlaChip`; a missing assignee reads
 * "Unassigned" in amber italics. Controlled selection: listbox semantics with click
 * and ArrowUp/Down; the queue owns no state.
 *
 * @example
 * <CaseQueue
 *   items={[
 *     { id: "CASE-4181", summary: "Custody cash break exceeds tolerance",
 *       category: "QuantityMismatch", status: "Open", priority: "Critical",
 *       assignee: "D. Chen", openedAtUtc: "2026-07-04 06:10Z",
 *       sla: { state: "Breached", dueAtUtc: "2026-07-05T09:00:00Z" } },
 *     { id: "CASE-4184", summary: "Dividend accrual unmatched — MSFT",
 *       category: "MissingCounterpart", status: "InProgress", priority: "Normal",
 *       sla: { state: "OnTrack", dueAtUtc: "2026-07-06T09:00:00Z" } },
 *   ]}
 *   selectedId={selected} onSelect={setSelected}
 *   now="2026-07-05T14:32:00Z"
 * />
 */
export interface CaseQueueItem {
  /** Case id (mono, e.g. "CASE-4181"). */
  id: string;
  /** One-line operator summary. */
  summary: React.ReactNode;
  /** Break category / classification — sub-line. */
  category?: string;
  /** Match-failure reason — sub-line. */
  reason?: string;
  /** Case status (Open, InProgress, Resolved, SignedOff, Reopened, …). */
  status?: string;
  /** Priority (Low | Normal | High | Critical) — drives the left rail. */
  priority?: string;
  /** Assigned operator; falsy renders "Unassigned" in amber. */
  assignee?: string;
  /** Opened timestamp (display string) — sub-line. */
  openedAtUtc?: string;
  /** SLA posture, passed through to `SlaChip`. */
  sla?: { state?: string; dueAtUtc?: string; ageBand?: string; businessAgeHours?: number };
  [key: string]: unknown;
}
export interface CaseQueueProps extends React.HTMLAttributes<HTMLElement> {
  items?: CaseQueueItem[];
  /** Controlled selected case id. */
  selectedId?: string;
  onSelect?: (id: string, item: CaseQueueItem) => void;
  /** Clock reference forwarded to every row's `SlaChip` (ISO or epoch ms). */
  now?: string | number;
  /** @default "No open cases" */
  emptyLabel?: React.ReactNode;
}
export declare function CaseQueue(props: CaseQueueProps): JSX.Element;
