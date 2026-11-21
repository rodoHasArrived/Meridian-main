import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { dryRunReconciliationBreakBulkAction, executeReconciliationBreakBulkAction } from "@/lib/api";
import { describeApiError } from "@/lib/api-errors";
import { reconciliationBreakAuditEndpoint } from "@/lib/workstation-endpoints";
import type { ReconciliationBreakQueueItem, ReconciliationBulkCaseworkRequest, ReconciliationBulkCaseworkResult } from "@/types";

const defaultServices = { preview: dryRunReconciliationBreakBulkAction, execute: executeReconciliationBreakBulkAction };

/** Execution submits the exact reviewed versions and inputs, never a newly assembled current selection. */
export function ReconciliationBulkDecisions({ cases, onChanged, services = defaultServices }: {
  cases: ReconciliationBreakQueueItem[];
  onChanged?: (item: ReconciliationBreakQueueItem) => void;
  services?: typeof defaultServices;
}) {
  const [action, setAction] = useState<"Assign" | "Resolve">("Assign");
  const [reason, setReason] = useState("");
  const [assignee, setAssignee] = useState("");
  const [rootCause, setRootCause] = useState("");
  const [resolution, setResolution] = useState("");
  const [busy, setBusy] = useState(false);
  const [preview, setPreview] = useState<ReconciliationBulkCaseworkResult | null>(null);
  const [receipt, setReceipt] = useState<ReconciliationBulkCaseworkResult | null>(null);
  const [snapshot, setSnapshot] = useState<ReconciliationBulkCaseworkRequest | null>(null);
  const [error, setError] = useState<string | null>(null);
  const selectionKey = JSON.stringify(cases.map((item) => [item.breakId, item.version]));
  const [reviewedSelection, setReviewedSelection] = useState("");
  const currentPreview = reviewedSelection === selectionKey ? preview : null;
  const missingVersion = cases.some((item) => item.version == null);
  const permitted = cases.every((item) => item.permittedCommands?.some((entry) => entry.action === action && entry.enabled));
  function invalidate() { setSnapshot(null); setPreview(null); setReceipt(null); }
  async function dryRun() {
    if (!cases.length || missingVersion || busy || !reason.trim() || !permitted) return;
    setBusy(true); setError(null); setReceipt(null); setSnapshot(null);
    const previewId = crypto.randomUUID();
    const executionId = crypto.randomUUID();
    const base: ReconciliationBulkCaseworkRequest = { breakIds: cases.map((item) => item.breakId),
      expectedVersions: Object.fromEntries(cases.map((item) => [item.breakId, item.version!])),
      action, actor: "", commandId: previewId, correlationId: previewId, source: "browser-reconciliation-bulk",
      idempotencyKey: previewId, dryRun: true, allowPartialSuccess: false, reason: reason.trim(),
      note: reason.trim(), assignee: assignee.trim() || undefined,
      rootCauseCode: rootCause.trim() || undefined, resolutionCode: resolution.trim() || undefined };
    try {
      const result = await services.preview(base);
      const resultIds = result.results.map((entry) => entry.breakId);
      if (resultIds.length !== base.breakIds.length || new Set(resultIds).size !== resultIds.length
        || resultIds.some((id) => !base.breakIds.includes(id))) {
        setError("The preview does not match the selected cases. Preview the current selection again.");
        return;
      }
      setPreview(result); setReviewedSelection(selectionKey);
      setSnapshot({ ...base, commandId: executionId, idempotencyKey: executionId, dryRun: false });
    } catch (err) { setError(describeApiError(err, "Bulk preview failed.").summary); }
    finally { setBusy(false); }
  }
  async function execute() {
    if (!snapshot || !currentPreview || busy || !currentPreview.outcome.isSuccessful || currentPreview.failedCount || currentPreview.results.some((item) => !item.wouldSucceed)) return;
    setBusy(true); setError(null);
    try {
      const result = await services.execute(snapshot);
      setReceipt(result); setSnapshot(null);
      result.results.forEach((result) => { if (result.succeeded && result.item) onChanged?.(result.item); });
    } catch (err) { setError(describeApiError(err, "Bulk outcome could not be confirmed. Retry the same reviewed command to recover its retained receipt.").summary); }
    finally { setBusy(false); }
  }
  const results = receipt ?? currentPreview;
  return <section aria-label="Bulk case decisions" className="space-y-3 rounded border border-border p-3">
    <h3 className="text-sm font-semibold">Bulk decisions · {cases.length} selected</h3>
    <div className="grid gap-3 sm:grid-cols-2">
      <label className="text-sm">Decision<select className="block w-full rounded border border-border bg-background p-2" value={action} disabled={busy} onChange={(event) => { setAction(event.target.value as typeof action); invalidate(); }}><option value="Assign">Assign</option><option value="Resolve">Resolve</option></select></label>
      {action === "Assign" ? <label className="text-sm">Assignee<Input value={assignee} disabled={busy} onChange={(event) => { setAssignee(event.target.value); invalidate(); }} /></label> : <><label className="text-sm">Root cause code<Input value={rootCause} disabled={busy} onChange={(event) => { setRootCause(event.target.value); invalidate(); }} /></label><label className="text-sm">Resolution code<Input value={resolution} disabled={busy} onChange={(event) => { setResolution(event.target.value); invalidate(); }} /></label></>}
      <label className="text-sm">Decision rationale<Input value={reason} disabled={busy} onChange={(event) => { setReason(event.target.value); invalidate(); }} /></label>
    </div>
    {missingVersion ? <p role="status">Reload selected cases: a retained version is required for every decision.</p> : null}
    {!permitted && cases.length ? <p role="status">This decision is not permitted for every selected case. Inspect individual cases for policy and role requirements.</p> : null}
    <div className="flex flex-wrap gap-2"><Button type="button" size="sm" variant="outline" disabled={busy || !cases.length || missingVersion || !permitted || !reason.trim() || (action === "Assign" && !assignee.trim())} onClick={() => void dryRun()}>Preview selected decisions</Button>
      <Button type="button" size="sm" disabled={busy || !snapshot || !currentPreview || !currentPreview.outcome.isSuccessful || !!currentPreview.failedCount || currentPreview.results.some((result) => !result.wouldSucceed)} onClick={() => void execute()}>{busy ? "Evaluating…" : `Confirm ${cases.length} decisions`}</Button></div>
    {error ? <p role="alert">{error}</p> : null}
    {results ? <section aria-label={receipt ? "Bulk decision receipt" : "Bulk decision preview"} className="space-y-2"><p role="status">{receipt ? "Recorded outcomes" : "Preview outcomes"}: {results.succeededCount} successful · {results.failedCount} failed</p>
      <table className="w-full text-left text-sm"><thead><tr><th>Case</th><th>Outcome</th><th>Version</th><th>Audit</th></tr></thead><tbody>{[...results.results].sort((left, right) => Number(!!right.error) - Number(!!left.error)).map((result) => <tr key={result.breakId}><th className="break-all">{result.breakId}</th><td>{result.error ?? (receipt ? result.succeeded ? "Succeeded" : "Blocked" : result.wouldSucceed ? "Ready for confirmation" : "Blocked")}</td><td>{result.item?.version ?? "Not supplied"}</td><td><a className="underline" href={reconciliationBreakAuditEndpoint(result.breakId)}>Audit</a></td></tr>)}</tbody></table>
      {receipt ? <p className="text-xs">Receipt {receipt.outcome.operationId}</p> : null}
    </section> : null}
  </section>;
}
