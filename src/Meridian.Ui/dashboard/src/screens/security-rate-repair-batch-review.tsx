import { useId, useState } from "react";
import { Link } from "react-router-dom";
import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { SecurityImpactPreview } from "@/components/meridian/security-impact-preview";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { TimeText } from "@/components/ui/time-text";
import type { RateRepairBatch, RateRepairCommand, RateRepairCommandName, RateRepairItem } from "@/lib/api/security-rate-repair.api";

const commands: Array<{ name: RateRepairCommandName; label: string }> = [
  { name: "stage", label: "Stage per-record drafts" }, { name: "submit", label: "Submit for independent review" },
  { name: "approve", label: "Approve reviewed records" }, { name: "publish", label: "Publish approved records" }
];

export function RateRepairBatchReview(props: { batch: RateRepairBatch; busy: string | null; needsRefresh: boolean;
  onCommand: (command: RateRepairCommandName, input: RateRepairCommand) => void }) {
  return <RetainedBatchReview key={`${props.batch.batchId}:${props.batch.version}`} {...props} />;
}
function RetainedBatchReview({ batch, busy, needsRefresh, onCommand }: {
  batch: RateRepairBatch; busy: string | null; needsRefresh: boolean;
  onCommand: (command: RateRepairCommandName, input: RateRepairCommand) => void;
}) {
  const id = useId();
  const [reviewer, setReviewer] = useState("");
  const [rationale, setRationale] = useState("");
  const [confirmed, setConfirmed] = useState(false);
  const [receiptId, setReceiptId] = useState<string | null>(null);
  const [inspectionError, setInspectionError] = useState("");
  const [inspected, setInspected] = useState<string[]>([]);
  const requiredReceipts = batch.items.filter(item => ["submitted", "approved"].includes(item.state.toLowerCase()));
  const allInspected = requiredReceipts.length > 0 && requiredReceipts.every(item => item.impactReceiptId && inspected.includes(item.impactReceiptId));
  const published = batch.items.length > 0 && batch.items.every(item => item.state.toLowerCase() === "published");
  const counts = [...new Set(batch.items.map(item => item.state))].map(state => `${batch.items.filter(item => item.state === state).length} ${state.toLowerCase()}`);
  function reason(name: RateRepairCommandName): string | null {
    const action = batch.actions.find(row => row.command.toLowerCase() === name);
    if (busy) return "Wait for the active command and retained per-record results.";
    if (needsRefresh) return "Refresh the retained batch to resolve its current authority and command outcome.";
    if (!action?.enabled) return action?.disabledReason ?? "This command is not available for the current retained state and permissions.";
    if (name === "submit" && !reviewer.trim()) return "Name the independent reviewer before submitting.";
    if ((name === "approve" || name === "publish") && !allInspected) return "Inspect the current retained impact for every submitted or approved record.";
    if ((name === "approve" || name === "publish") && !confirmed) return "Confirm that you reviewed the frozen unit, evidence, scope and each current impact.";
    if (name === "approve" && !rationale.trim()) return "Enter the approval rationale.";
    return null;
  }
  const columns: DenseDataTableColumn<RateRepairItem>[] = [
    { id: "security", label: "Security / base", rowHeader: true, render: item => <><Link className="font-medium underline" to={`/accounting/security-master?securityId=${encodeURIComponent(item.securityId)}&tab=evidence`}>{item.displayName}</Link><span className="block text-xs text-muted-foreground">{item.assetClass} · base {item.baseVersion}</span></> },
    { id: "scalar", label: "Unchanged scalar", numeric: true, render: item => <span className="font-mono break-all">{item.rawValue ?? "Unavailable"}</span> },
    { id: "state", label: "Per-record outcome", render: item => <><strong>{item.state}</strong>{item.error ? <span className="block text-xs text-danger">{item.errorCode ? `${item.errorCode}: ` : ""}{item.error}</span> : null}</> },
    { id: "draft", label: "Draft / workflow", render: item => <span className="block max-w-64 break-all font-mono text-xs">{item.revisionId ?? "Not staged"}{item.workflowId ? <span className="mt-1 block">Workflow: {item.workflowId}</span> : null}</span> },
    { id: "impact", label: "Review impact", render: item => item.impactReceiptId ? <><Button size="sm" variant="outline" disabled={busy !== null} onClick={() => { setInspectionError(""); setReceiptId(item.impactReceiptId); }}>Inspect impact for {item.displayName}</Button><span className="mt-1 block text-xs">{item.state.toLowerCase() === "published" ? "Retained impact evidence" : inspected.includes(item.impactReceiptId) ? "Current retained impact inspected" : "Inspection required"}</span></> : "No impact receipt retained" },
    { id: "approval", label: "Approval receipt", render: item => <span className="block max-w-56 break-all font-mono text-xs">{item.approvalReceiptId ?? "Not approved"}</span> }
  ];
  return <section aria-labelledby={`${id}-title`} className="space-y-4 border-t border-border pt-4">
    <div><h3 id={`${id}-title`} className="font-semibold">Frozen repair batch</h3><p className="mt-1 text-sm">{counts.join(" · ")}. Each record keeps its own draft, independent decision and receipt.</p></div>
    <dl className="grid gap-3 rounded border border-border bg-secondary/10 p-3 text-sm lg:grid-cols-3">
      <div><dt className="text-xs text-muted-foreground">Batch / version</dt><dd className="break-all font-mono">{batch.batchId} · {batch.version}</dd></div>
      <div><dt className="text-xs text-muted-foreground">Maker / retained at</dt><dd>{batch.createdBy}<TimeText className="block text-xs" value={batch.createdAtUtc} /></dd></div>
      <div><dt className="text-xs text-muted-foreground">Declared unit / effective from</dt><dd>{batch.unit}<time className="block text-xs" dateTime={batch.effectiveFrom}>{batch.effectiveFrom}</time></dd></div>
      <div><dt className="text-xs text-muted-foreground">Source field / schema / role</dt><dd>{batch.cohort.sourceSystem} · {batch.cohort.sourceField} · {batch.cohort.sourceSchemaVersion} · {batch.cohort.rateRole}</dd></div>
      <div><dt className="text-xs text-muted-foreground">Accounting scope</dt><dd className="break-all">{batch.scope.fundProfileId} · {batch.scope.periodId}<span className="block font-mono text-xs">Account {batch.scope.fundAccountId}<br />Book {batch.scope.ledgerBookId}</span></dd></div>
      <div><dt className="text-xs text-muted-foreground">Retained source evidence</dt><dd className="break-all font-mono text-xs">{batch.evidenceId}<span className="block">SHA-256 {batch.evidenceSha256}</span></dd></div>
    </dl>
    <p className="text-sm"><strong>Justification:</strong> {batch.justification}</p>
    <DenseDataTable columns={columns} rows={batch.items} getRowId={item => item.securityId} ariaLabel="Frozen rate-repair record outcomes" emptyText="No records were retained in this batch. Commands remain subject to server authority." />
    {receiptId ? <section className="space-y-3 border border-border p-3" aria-label="Selected record impact evidence">
      <Button size="sm" variant="ghost" onClick={() => setReceiptId(null)}>Close impact evidence</Button>
      <SecurityImpactPreview key={receiptId} receiptId={receiptId} onInspected={receipt => {
        const item = batch.items.find(row => row.impactReceiptId === receiptId);
        if (!item || receipt.contentSha256 !== receiptId || receipt.securityId.toLowerCase() !== item.securityId.toLowerCase()
          || receipt.revisionId !== item.revisionId || receipt.canonicalVersion !== item.baseVersion) {
          setInspectionError("The impact receipt does not match this frozen security, revision and base version. Refresh retained authority before review."); return;
        }
        setInspectionError(""); setInspected(current => current.includes(receiptId) ? current : [...current, receiptId]);
      }} />
      {inspectionError ? <p role="alert" className="text-sm text-danger">{inspectionError}</p> : null}
    </section> : null}
    {published ? <p role="status" className="text-sm">All records are published. Their separate approval receipts and source evidence remain retained.</p> : <>
    <fieldset disabled={busy !== null || needsRefresh} className="space-y-3 border-t border-border pt-3">
      <legend className="px-1 text-sm font-semibold">Explicit governed commands</legend>
      <div className="grid gap-3 lg:grid-cols-2">
        <label className="grid gap-1 text-sm">Independent reviewer<Input value={reviewer} onChange={event => setReviewer(event.target.value)} placeholder="Authenticated reviewer identity" /></label>
        <label className="grid gap-1 text-sm">Review rationale<Input value={rationale} onChange={event => setRationale(event.target.value)} /></label>
      </div>
      <Checkbox checked={confirmed} onCheckedChange={setConfirmed} label="I reviewed the frozen unit declaration, evidence, accounting scope and each current impact."
        hint="The server enforces maker/submitter separation, current approvals, period authority and publication. A batch never approves itself." />
    </fieldset>
    <div className="grid gap-3 lg:grid-cols-2">
      {commands.map(command => { const disabledReason = reason(command.name); return <div key={command.name} className="space-y-1">
        <Button variant={command.name === "publish" ? "default" : "outline"} disabled={Boolean(disabledReason)} disabledReason={disabledReason}
          busy={busy === command.name} onClick={() => onCommand(command.name, { expectedVersion: batch.version,
            reviewer: command.name === "submit" ? reviewer.trim() : null, rationale: rationale.trim() || null })}>{command.label}</Button>
        {disabledReason ? <p className="text-xs text-muted-foreground">{disabledReason}</p> : null}
      </div>; })}
    </div>
    </>}
  </section>;
}
