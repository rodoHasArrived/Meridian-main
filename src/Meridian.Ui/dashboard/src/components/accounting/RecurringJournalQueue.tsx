import { useEffect, useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import { getRecurringJournalQueue, type RecurringJournalQueue as Queue, type RecurringJournalScope } from "@/lib/api/recurring-journals.api";

interface Props {
  scope: RecurringJournalScope | null;
  onSelectDraft: (journalEntryId: string) => void;
  availableDraftIds: string[];
}

export function RecurringJournalQueue({ scope, onSelectDraft, availableDraftIds }: Props) {
  const [queue, setQueue] = useState<Queue | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [revision, setRevision] = useState(0);
  const generation = useRef(0);
  const fundProfileId = scope?.fundProfileId;
  const ledgerBookId = scope?.ledgerBookId;
  const entityId = scope?.entityId;

  useEffect(() => {
    const current = ++generation.current;
    const controller = new AbortController();
    setQueue(null);
    setError(null);
    if (!fundProfileId || !ledgerBookId || !entityId) {
      setLoading(false);
      return;
    }
    setLoading(true);
    void getRecurringJournalQueue({ fundProfileId, ledgerBookId, entityId }, controller.signal)
      .then((result) => { if (generation.current === current) setQueue(result); })
      .catch((reason: unknown) => {
        if (generation.current === current && !controller.signal.aborted) {
          setError(reason instanceof Error ? reason.message : "The retained occurrence state could not be read.");
        }
      })
      .finally(() => { if (generation.current === current) setLoading(false); });
    return () => { ++generation.current; controller.abort(); };
  }, [fundProfileId, ledgerBookId, entityId, revision]);

  // A scope change must hide previous rows during the render before the effect runs.
  const visibleQueue = queue?.fundProfileId === fundProfileId && queue?.ledgerBookId.toLowerCase() === ledgerBookId?.toLowerCase()
    && queue?.entityId === entityId ? queue : null;
  return (
    <section className="accounting-draft-rail" data-appearance="light" aria-label="Recurring journal occurrences">
      <div className="accounting-reference-heading">
        <div>
          <h4 className="accounting-reference-kicker">Recurring journals</h4>
          <p className="accounting-reference-subtitle">Retained drafts requiring human approval</p>
        </div>
      </div>
      {!scope ? <p role="status" className="text-sm text-muted-foreground">Select a fund, ledger book and entity to load recurring occurrences.</p> : null}
      {error ? <p role="alert" className="break-words text-sm text-danger">Recurring queue unavailable: {error}</p> : null}
      {loading ? <p role="status" className="text-sm text-muted-foreground">Loading retained occurrences…</p> : null}
      {visibleQueue?.occurrences.length === 0 ? <p role="status" className="text-sm text-muted-foreground">No retained recurring occurrences for this scope.</p> : null}
      <div className="space-y-3">
        {visibleQueue?.occurrences.map((row) => (
          <article key={row.occurrenceId} className="min-w-0 rounded border border-border p-3 text-xs">
            <p className="break-words font-semibold">{row.scheduleId} · v{row.scheduleVersion}</p>
            <p className="break-words text-muted-foreground">Template {row.templateId} · v{row.templateVersion}</p>
            <p>{row.effectiveDate} · {row.periodId ?? "Period unresolved"}</p>
            <p className="mt-2 font-semibold">{row.state} · Approval: {row.approvalStatus ?? "Not submitted"}</p>
            <p className="break-all text-muted-foreground">Occurrence {row.occurrenceId}</p>
            {row.journalEntryId ? <p className="break-all">Draft {row.journalEntryId}</p> : null}
            {row.blockers.length ? <ul className="mt-2 list-disc space-y-1 pl-4 text-danger">{row.blockers.map((blocker) => <li key={blocker}>{blocker}</li>)}</ul> : null}
            {row.periodLockOwner ? <p className="mt-2 break-words">Period lock owner: {row.periodLockOwner}</p> : null}
            {row.governedReopenPath ? <p className="break-words">Governed reopen: {row.governedReopenPath}</p> : null}
            <details className="mt-2">
              <summary className="cursor-pointer">Source evidence ({row.sourceEvidenceReferences.length})</summary>
              {row.sourceEvidenceReferences.length ? <ul className="mt-1 space-y-1 break-all">{row.sourceEvidenceReferences.map((reference) => <li key={reference}>{reference}</li>)}</ul>
                : <p>No retained source references.</p>}
            </details>
            {row.journalEntryId && availableDraftIds.includes(row.journalEntryId) ? (
              <Button size="sm" variant="outline" className="mt-2" onClick={() => onSelectDraft(row.journalEntryId!)}>Review retained draft</Button>
            ) : row.journalEntryId ? <p className="mt-2 text-muted-foreground">Refresh the journal workbench to load this retained draft.</p> : null}
          </article>
        ))}
      </div>
      {scope ? <Button size="sm" variant="outline" className="mt-3" disabled={loading} onClick={() => setRevision((value) => value + 1)}>{error ? "Retry recurring queue" : "Refresh recurring queue"}</Button> : null}
    </section>
  );
}
