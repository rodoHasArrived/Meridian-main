import { useEffect, useState } from "react";
import { getReconciliationBreakAudit } from "@/lib/api";
import { StatusBanner } from "@/components/ui/status-banner";
import { formatDateTimeLabel } from "./accounting-screen.formatting";
import { lifecycleStateLabel } from "./reconciliation-break-lifecycle";
import type { ReconciliationBreakQueueAuditEvent } from "@/types";

export type BreakAuditLoader = (breakId: string) => Promise<ReconciliationBreakQueueAuditEvent[]>;

/** Turns a server event type such as `RootCauseSet` into "Root cause set". */
export function auditEventLabel(eventType: string): string {
  const words = eventType.replace(/[_-]+/g, " ").replace(/([a-z])([A-Z])/g, "$1 $2").trim().toLowerCase();
  return words ? words.charAt(0).toUpperCase() + words.slice(1) : "Event";
}

/** Newest first; the endpoint returns the append-only history in sequence order. */
export function orderAuditEvents(events: readonly ReconciliationBreakQueueAuditEvent[]): ReconciliationBreakQueueAuditEvent[] {
  return [...events].sort((left, right) => {
    const bySequence = (right.sequence ?? 0) - (left.sequence ?? 0);
    return bySequence !== 0 ? bySequence : Date.parse(right.occurredAt) - Date.parse(left.occurredAt);
  });
}

/**
 * The break's retained audit history. It loads when opened and reloads whenever `refreshKey`
 * changes, so a lifecycle decision shows up here as soon as the server accepts it.
 */
export function BreakAuditTimeline({ breakId, refreshKey, load = getReconciliationBreakAudit }: {
  breakId: string;
  refreshKey?: string | number | null;
  load?: BreakAuditLoader;
}) {
  const [open, setOpen] = useState(false);
  const [events, setEvents] = useState<ReconciliationBreakQueueAuditEvent[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    if (!open) return;
    let cancelled = false;
    setLoading(true);
    setError(null);
    load(breakId)
      .then((loaded) => { if (!cancelled) setEvents(orderAuditEvents(loaded.filter((event) => event.breakId === breakId || !event.breakId))); })
      .catch((failure: unknown) => {
        if (!cancelled) setError(failure instanceof Error ? failure.message : "The audit history could not be loaded.");
      })
      .finally(() => { if (!cancelled) setLoading(false); });
    return () => { cancelled = true; };
  }, [breakId, load, open, refreshKey]);

  return (
    <details className="border-t border-border pt-3" open={open} onToggle={(event) => setOpen(event.currentTarget.open)}>
      <summary className="cursor-pointer text-sm font-semibold">Activity</summary>
      {open ? (
        <div className="mt-3 grid gap-2" aria-busy={loading}>
          {loading && !events ? <p role="status" className="text-sm text-muted-foreground">Loading audit history…</p> : null}
          {error ? <StatusBanner role="alert" tone="warning" title="Audit history unavailable" detail={error} /> : null}
          {events && events.length === 0 && !error ? <p className="text-sm text-muted-foreground">No history recorded yet.</p> : null}
          {events && events.length > 0 ? (
            <ol aria-label="Break audit history" className="grid gap-2">
              {events.map((event) => {
                const transition = event.newLifecycleState && event.previousLifecycleState !== event.newLifecycleState
                  ? `${lifecycleStateLabel(event.previousLifecycleState)} → ${lifecycleStateLabel(event.newLifecycleState)}`
                  : null;
                const detail = event.reason ?? event.note;
                const label = event.eventType === "ResolutionSet" && event.newLifecycleState === "Resolved" && transition
                  ? "Resolved"
                  : auditEventLabel(event.eventType);
                return (
                  <li key={event.eventId} className="border-b border-border/60 pb-2 text-sm">
                    <p className="font-medium">
                      {label}
                      {transition ? <span className="font-normal text-muted-foreground"> · {transition}</span> : null}
                    </p>
                    <p className="text-xs text-muted-foreground">
                      {event.actor || "Actor not recorded"} · <time dateTime={event.occurredAt} className="whitespace-nowrap">{formatDateTimeLabel(event.occurredAt)}</time>
                    </p>
                    {detail ? <p className="whitespace-pre-wrap text-xs">{detail}</p> : null}
                  </li>
                );
              })}
            </ol>
          ) : null}
        </div>
      ) : null}
    </details>
  );
}
