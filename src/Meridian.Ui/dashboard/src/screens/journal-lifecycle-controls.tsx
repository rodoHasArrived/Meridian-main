import { useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { DenseDataTable } from "@/components/meridian/ui-kit-primitives";
import type { ManualJournalEntryWorkbenchViewModel } from "./accounting-screen.view-model";
import type { JournalEntryLifecycleAction } from "@/types";

const consequential = new Set<JournalEntryLifecycleAction>(["Approve", "Post", "Reverse", "Rebook", "LockAfterClose"]);
const reasonRequired = new Set<JournalEntryLifecycleAction>(["Reverse", "LockAfterClose"]);

/** Confirms the exact loaded draft; a refreshed or edited draft needs a new review. */
export function JournalLifecycleControls({ view }: { view: ManualJournalEntryWorkbenchViewModel }) {
  const [review, setReview] = useState<{ action: JournalEntryLifecycleAction; snapshot: string } | null>(null);
  const [reason, setReason] = useState("");
  const sending = useRef<string | null>(null);
  const snapshot = JSON.stringify(view.draft);
  const command = review ? view.lifecycleCommands.find((item) => item.action === review.action) : null;
  const stale = review !== null && review.snapshot !== snapshot;
  const needsReason = review !== null && reasonRequired.has(review.action);
  const blocked = stale ? "The journal changed. Cancel and review the latest draft."
    : command?.disabledReason ?? (needsReason && !reason.trim() ? "Enter a reason before confirming." : null);
  const close = () => { setReview(null); setReason(""); };
  const confirm = () => {
    if (!review || !command || blocked || sending.current === snapshot) return;
    const requestedSnapshot = snapshot;
    sending.current = requestedSnapshot;
    const action = review.action;
    const notes = reason.trim() ? reason : undefined;
    close();
    void view.applyLifecycleAction(action, notes).finally(() => { if (sending.current === requestedSnapshot) sending.current = null; });
  };
  return <>
    {view.lifecycleConflict ? <section className="mt-3 rounded border border-warning/50 p-3" aria-label="Journal changed since loaded" aria-live="polite">
      <h3 className="font-semibold">Changed since you loaded it</h3>
      <p className="mt-1 text-sm">{view.lifecycleConflict.action} was not retried. Review latest replaces the current editor, including unsaved edits, with the latest retained draft. The submitted snapshot and exact decision notes remain in this comparison.</p>
      {view.lifecycleConflict.loading ? <p role="status" className="mt-2 text-sm">Reading the latest journal once…</p> : null}
      {view.lifecycleConflict.unavailableReason ? <p role="alert" className="mt-2 text-sm">{view.lifecycleConflict.unavailableReason}</p> : null}
      {view.lifecycleConflict.latest ? <div className="mt-2"><DenseDataTable rows={view.lifecycleConflict.changes}
        getRowId={(change) => change.label} maxVisibleRows={null} ariaLabel="Journal conflict field comparison"
        emptyText="No differences were found in the displayed fields. Review the latest retained journal before acting."
        columns={[
          { id: "field", label: "Field", render: (change) => change.label },
          { id: "before", label: "Submitted snapshot", render: (change) => <span className="block whitespace-pre-wrap break-words">{change.before}</span> },
          { id: "after", label: "Latest retained", render: (change) => <span className="block whitespace-pre-wrap break-words">{change.after}</span> }
        ]} /></div> : null}
      <p className="mt-2 text-sm font-medium">Retained decision notes</p><p className="whitespace-pre-wrap break-words text-sm">{view.lifecycleConflict.notes || "Not recorded"}</p>
      {view.lifecycleConflict.reviewedLatest ? <p role="status" className="mt-2 text-sm">Latest journal loaded. Validate again and make a fresh decision; the submitted snapshot and exact notes remain in this comparison.</p> : null}
      <div className="mt-3 flex flex-wrap gap-2"><Button size="sm" disabled={!view.lifecycleConflict.latest || view.lifecycleConflict.loading || view.lifecycleConflict.reviewedLatest}
        onClick={() => { close(); view.reviewLatestLifecycleDraft?.(); }}>Review latest</Button>
        <Button size="sm" variant="outline" onClick={() => { close(); view.discardLifecycleConflict?.(); }}>Discard decision</Button></div>
    </section> : null}
    <div className="mt-3 grid gap-2 sm:grid-cols-2 xl:grid-cols-3">
      {view.lifecycleCommands.map((item) => <Button key={item.action} size="sm" variant="outline"
        busy={item.busy} disabled={item.disabledReason !== null} disabledReason={item.disabledReason}
        onClick={() => {
          if (consequential.has(item.action)) {
            setReason(view.lifecycleConflict?.reviewedLatest && view.lifecycleConflict.action === item.action ? view.lifecycleConflict.notes : "");
            setReview({ action: item.action, snapshot });
          }
          else void view.applyLifecycleAction(item.action);
        }}>{item.label}</Button>)}
    </div>
    <Dialog open={review !== null} onOpenChange={(open) => { if (!open) close(); }}>
      <DialogContent aria-labelledby="journal-decision-title" aria-describedby="journal-decision-description" className="max-h-[85vh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle id="journal-decision-title">Confirm {command?.label.toLowerCase() ?? "journal action"}</DialogTitle>
          <DialogDescription id="journal-decision-description">Review the journal scope and balance impact before continuing. Existing approval and period controls still apply.</DialogDescription>
        </DialogHeader>
        <dl className="grid grid-cols-2 gap-2 text-sm">
          <dt>Journal</dt><dd className="break-all font-mono">{view.draft.journalEntryId} · v{view.draft.version}</dd>
          <dt>Entity</dt><dd>{view.draft.entityId || "Not recorded"}</dd>
          <dt>Book</dt><dd>{view.draft.ledgerBookId || "Not recorded"}</dd>
          <dt>Period</dt><dd>{view.draft.periodId || "Not recorded"}</dd>
          <dt>Currency</dt><dd>{view.draft.currency || "Not recorded"}</dd>
        </dl>
        <p className="mt-3 text-sm">{review?.action === "Reverse" || review?.action === "Rebook"
          ? "This creates a separate correction draft. It does not change the original posted lines. The figures below describe the original journal; the correction must be reviewed before posting."
          : review?.action === "LockAfterClose" ? "This locks the journal after close. It does not post new ledger lines."
          : review?.action === "Approve" ? "Approval does not post the journal. Posting remains a separate governed decision."
          : "Posting makes the approved journal immutable. Corrections require reversal or rebook."}</p>
        <div className="mt-3 overflow-x-auto" role="region" aria-label="Confirmation balance impact" tabIndex={0}>
          <DenseDataTable rows={view.balanceImpactRows} getRowId={(row) => row.id} maxVisibleRows={null}
            ariaLabel="Journal confirmation balance impact" emptyText="Balance impact unavailable. Review the journal lines."
            columns={[
              { id: "account", label: "Account", render: (row) => row.accountName },
              { id: "debit", label: "Debit", align: "right", render: (row) => <span className="font-mono">{row.debitLabel}</span> },
              { id: "credit", label: "Credit", align: "right", render: (row) => <span className="font-mono">{row.creditLabel}</span> },
              { id: "effect", label: "Effect", render: (row) => row.netEffectLabel }
            ]} />
        </div>
        <label className="mt-4 block text-sm" htmlFor="journal-decision-reason">Reason {needsReason ? "(required)" : "(optional)"}</label>
        <textarea id="journal-decision-reason" className="mt-1 w-full rounded border border-border bg-background p-2" value={reason} onChange={(event) => setReason(event.target.value)} required={needsReason} />
        {blocked ? <p role="status" className="mt-2 text-sm">{blocked}</p> : null}
        <div className="mt-4 flex justify-end gap-2"><Button variant="outline" onClick={close}>Cancel</Button><Button disabled={Boolean(blocked) || !command} disabledReason={blocked} onClick={confirm}>Confirm {command?.label.toLowerCase() ?? "action"}</Button></div>
      </DialogContent>
    </Dialog>
  </>;
}
