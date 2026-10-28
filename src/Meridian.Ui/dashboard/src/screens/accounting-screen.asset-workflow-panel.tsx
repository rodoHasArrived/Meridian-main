import { useId, useMemo, useState } from "react";
import { Link } from "react-router-dom";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import type { AssetAccountingWorkflowViewModel } from "./accounting-screen.asset-workflow";

export function AssetAccountingWorkflowPanel({ view }: { view: AssetAccountingWorkflowViewModel }) {
  const id = useId();
  const approval = view.approval;
  const posted = view.postedJournal;
  const [copyStatus, setCopyStatus] = useState<{ hash: string; text: string } | null>(null);
  const receiptBytes = approval?.canonicalContentBase64;
  const rawReceipt = useMemo(() => receiptBytes ? new TextDecoder().decode(Uint8Array.from(atob(receiptBytes), char => char.charCodeAt(0))) : "", [receiptBytes]);
  async function copyReceipt() {
    if (!approval) return;
    try { await navigator.clipboard.writeText(rawReceipt); setCopyStatus({ hash: approval.contentHashSha256, text: "Copied exact retained receipt JSON." }); }
    catch { setCopyStatus({ hash: approval.contentHashSha256, text: "Copy is unavailable. Select the retained text below." }); }
  }
  return <section className="space-y-3 rounded border border-border bg-secondary/15 p-3" aria-label="Governed asset approval and posting">
    <div className="flex flex-wrap items-start justify-between gap-3"><div><h4 className="text-sm font-semibold">Governed asset workflow</h4>
      <p className="text-xs text-muted-foreground">Source fields remain immutable. Available decisions come from your authenticated workflow authority. Reversal, rebook and close-lock actions remain unavailable here.</p></div>
      <Button type="button" size="sm" variant="outline" disabled={view.loading || !!view.busyAction} onClick={() => void view.reload()}>{view.loading ? "Loading workflow…" : "Reload retained workflow"}</Button>
    </div>
    {view.capability ? <p className="break-all text-xs">Authenticated operator: {view.capability.authenticatedActor}. Reviewed candidate: <span className="font-mono">{view.capability.candidateFingerprint}</span></p> : <p className="text-sm">Approval and posting remain unavailable until a matching supported workflow is loaded.</p>}
    {approval ? <div className="space-y-2 rounded border border-border p-3" aria-label="Retained asset approval receipt"><h5 className="text-sm font-semibold">Retained approval</h5>
      <dl className="grid gap-2 text-xs sm:grid-cols-2"><div><dt>Approval identity</dt><dd className="break-all font-mono">{approval.approvalId}</dd></div><div><dt>Authenticated approver</dt><dd>{approval.approver}</dd></div><div><dt>Approved at</dt><dd>{approval.approvedAtUtc}</dd></div><div><dt>Evidence subject</dt><dd className="break-all font-mono">{approval.subjectId}</dd></div><div className="sm:col-span-2"><dt>Fund / book / period</dt><dd className="break-all font-mono">{view.capability?.fundProfileId} / {view.capability?.ledgerBookId} / {view.capability?.periodId}</dd></div><div className="sm:col-span-2"><dt>Approval rationale</dt><dd>{view.capability?.approvalDecision?.notes || "No rationale retained."}</dd></div><div className="sm:col-span-2"><dt>Receipt SHA-256</dt><dd className="break-all font-mono">{approval.contentHashSha256}</dd></div></dl>
      <details className="text-xs"><summary className="cursor-pointer font-semibold">Inspect retained receipt bytes</summary>
        <div className="mt-2 space-y-2"><textarea aria-label="Exact retained approval receipt JSON" className="min-h-24 w-full rounded border border-border bg-background p-2 font-mono" readOnly value={rawReceipt} />
          <div className="flex flex-wrap items-center gap-3"><Button type="button" size="sm" variant="outline" onClick={() => void copyReceipt()}>Copy exact receipt</Button>
          <a className="text-foreground underline" href={`data:application/json;base64,${approval.canonicalContentBase64}`} download={`approval-${approval.approvalId.replace(/[^a-zA-Z0-9-]/g, "_")}.json`}>Download retained approval receipt</a></div>
          {copyStatus?.hash === approval.contentHashSha256 ? <p role="status">{copyStatus.text}</p> : null}</div>
      </details>
    </div> : null}
    {view.pendingCommandId ? <p role="status" className="break-all text-sm">Unresolved {view.pendingAction?.toLowerCase()} command: <span className="font-mono">{view.pendingCommandId}</span>. Resume uses this exact retained request.</p> : null}
    {view.hasPendingPost && !view.pendingCommandId ? <p role="status" className="text-sm">Posting recovery is pending. Its original command is available only to the authorized approver.</p> : null}
    {!posted ? <fieldset className="space-y-2" disabled={!view.sessionReady || !view.capability || !!view.busyAction || (!view.capability.canApprove && !view.capability.canPost)}>
      <label htmlFor={`${id}-note`} className="block text-xs font-semibold">Review rationale</label><Input id={`${id}-note`} value={view.reviewNote} readOnly={!!view.pendingAction} onChange={event => view.setReviewNote(event.target.value)} />
      <label className="flex items-start gap-2 text-sm"><input type="checkbox" checked={view.confirmed} onChange={event => view.setConfirmed(event.target.checked)} />I reviewed this retained candidate and the approval evidence for the available decision.</label>
    </fieldset> : null}
    {view.errorText ? <p role="alert" className="text-sm text-danger">{view.errorText}</p> : null}
    {view.statusText ? <p role="status" className="text-sm">{view.statusText}</p> : null}
    {posted ? <div className="space-y-1 text-sm" aria-label="Canonical asset posting receipt"><p>Canonical posted journal: <span className="font-mono">{posted.journalEntryId}</span></p><p className="break-all text-xs">Command {posted.commandId ?? "retained by ledger"} · {posted.postedAtUtc ?? "Posting time retained in journal"}</p>
      <Link className="text-foreground underline" to={`/accounting/journal-entries/detail?${new URLSearchParams({ journalEntryId: posted.journalEntryId, periodId: posted.periodId, ledgerBookId: posted.ledgerBookId })}`}>Open canonical posted journal</Link></div> : null}
  </section>;
}
