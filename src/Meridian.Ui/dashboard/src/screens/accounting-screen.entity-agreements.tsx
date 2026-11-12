import { useState } from "react";
import { Link } from "react-router-dom";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { FormGrid } from "@/components/ui/form";
import { TechnicalDetails } from "@/components/ui/technical-details";
import { entityOperationEndpoints as endpoints } from "@/lib/api/entity-operations.api";
import type { AgreementHistory, EntityOperationScope, IntercompanyAgreement } from "@/types/entity-operations";
import { AgreementEditor } from "./accounting-screen.entity-agreement-editor";
import { agreementSignedEvidencePath } from "./accounting-screen.entity-signed-evidence";
import { effectiveUtc, EntityOperationTable, MutationFeedback, OperationError, OperationField, today, useEntityData, useEntityMutation } from "./accounting-screen.entity-operations.shared";

export function EntityAgreements({ scope }: { scope: EntityOperationScope }) {
  const agreements = useEntityData<IntercompanyAgreement[]>(scope.capabilities.canManageAgreements ? endpoints.agreements : null);
  const [selectedId, setSelectedId] = useState("");
  const [editing, setEditing] = useState(false);
  const [creating, setCreating] = useState(false);
  const [reason, setReason] = useState("");
  const [effectiveAt, setEffectiveAt] = useState(today());
  const [successor, setSuccessor] = useState("");
  const [terminalRevision, setTerminalRevision] = useState("");
  const mutation = useEntityMutation();
  const selected = agreements.data?.find((row) => row.agreementId === selectedId) ?? null;
  const history = useEntityData<AgreementHistory>(selectedId ? `${endpoints.agreements}/${selectedId}/history` : null);
  const revisionSnapshots = new Map<string, IntercompanyAgreement>();
  history.data?.events.forEach((event) => { const affected = event.targetRevisionSnapshot ?? event.snapshot; revisionSnapshots.set(affected.revisionId, affected); });
  const approvedRevisions = [...revisionSnapshots.values()].filter((revision) => revision.status === "Approved");
  const targetRevisionId = approvedRevisions.some((revision) => revision.revisionId === terminalRevision) ? terminalRevision : approvedRevisions[0]?.revisionId ?? selected?.revisionId ?? "";
  if (!scope.capabilities.canManageAgreements) return <p className="text-sm text-muted-foreground">Agreement management requires fund-structure permission for the organization and both entities. Ledger workflows remain available according to your access.</p>;
  async function apply(action: string) {
    if (!selected) return;
    const successorAgreement = agreements.data?.find((agreement) => agreement.agreementId === successor);
    const command = { agreementId: selected.agreementId, expectedVersion: selected.version, action,
      reason: reason.trim(), effectiveAt: effectiveUtc(effectiveAt), successorAgreementId: action === "Novate" ? successor : null,
      revisionId: action === "Terminate" || action === "Novate" ? targetRevisionId : selected.revisionId,
      successorRevisionId: action === "Novate" ? successorAgreement?.revisionId ?? null : null,
      expectedSuccessorVersion: action === "Novate" ? successorAgreement?.version ?? null : null };
    const result = await mutation.run<IntercompanyAgreement>(`${endpoints.agreements}/${selected.agreementId}/${action}`,
      { requestId: mutation.identity(command), expectedVersion: selected.version, reason: command.reason,
        effectiveAt: command.effectiveAt, successorAgreementId: command.successorAgreementId, revisionId: command.revisionId,
        successorRevisionId: command.successorRevisionId, expectedSuccessorVersion: command.expectedSuccessorVersion }, `Agreement ${action.toLowerCase()} retained in its audit history.`);
    if (result) { agreements.refresh(); history.refresh(); setEditing(false); }
  }
  return <div className="space-y-4">
    <div className="flex flex-wrap items-center justify-between gap-2"><h3 className="font-semibold">Intercompany agreement registry</h3><div className="flex gap-2">
      <Button type="button" variant="outline" onClick={agreements.refresh} disabled={agreements.loading}>Refresh agreements</Button>
      <Button type="button" onClick={() => { setCreating(true); setEditing(true); }}>New agreement</Button>
    </div></div>
    <OperationError error={agreements.error} />
    {agreements.loading ? <p role="status">Loading retained agreements…</p> : null}
    {agreements.data?.length === 0 ? <p className="text-sm text-muted-foreground">No agreements are retained in your authorized perimeter.</p> : null}
    <EntityOperationTable label="Agreement registry" rows={agreements.data ?? []} getRowId={(agreement) => agreement.agreementId} emptyText="No retained agreements in this perimeter."
      columns={[
        { id: "facility", label: "Facility", rowHeader: true, sortValue: (agreement) => agreement.facilityReference,
          render: (agreement) => <Button type="button" variant="outline" size="sm" aria-pressed={selectedId === agreement.agreementId} onClick={() => { setSelectedId(agreement.agreementId); setEditing(false); setCreating(false); setReason(""); setSuccessor(""); setTerminalRevision(""); }}>{agreement.facilityReference}</Button> },
        { id: "kind", label: "Kind and currency", render: (agreement) => `${agreement.kind} · ${agreement.currency}` },
        { id: "effective", label: "Effective window", render: (agreement) => `${agreement.effectiveFrom.slice(0, 10)} → ${agreement.effectiveTo?.slice(0, 10) ?? "Open"} (exclusive)` },
        { id: "status", label: "Approval", render: (agreement) => <><Badge variant="outline">{agreement.status}</Badge>{agreement.approvedBy ? <p className="mt-1 text-xs">{agreement.approvedBy}</p> : null}</> },
        { id: "revision", label: "Revision", render: (agreement) => `${agreement.revisionNumber} · version ${agreement.version}` }
      ]} />
    {selected && !creating ? <section className="space-y-3 rounded border border-border p-4" aria-label="Selected agreement">
      <h4 className="font-semibold">{selected.facilityReference} · {selected.partyOneRole} ↔ {selected.partyTwoRole}</h4>
      <p className="text-sm">Settlement {selected.settlementCadence} · {selected.currency} · Signed evidence {selected.signedDocument ? "retained" : "required before approval"}</p>
      <Link className="inline-block text-sm underline underline-offset-4" to={agreementSignedEvidencePath(selected.agreementId)}>Retain or review signed agreement evidence</Link>
      <p className="break-all text-xs text-muted-foreground">Agreement {selected.agreementId} · Terms revision {selected.revisionId}</p>
      <Button type="button" variant="outline" onClick={() => setEditing(true)} disabled={mutation.busy || ["Terminated", "Novated"].includes(selected.status)}>Revise agreement terms</Button>
      <FormGrid columns={3}>
        <OperationField label="Agreement action reason" value={reason} onChange={setReason} required />
        <OperationField label="Agreement action effective date" value={effectiveAt} onChange={setEffectiveAt} type="date" required />
        <OperationField label="Novation successor agreement" value={successor} onChange={setSuccessor}>
          <option value="">Select approved successor</option>{agreements.data?.filter((agreement) => agreement.status === "Approved" && agreement.agreementId !== selected.agreementId).map((agreement) => <option key={agreement.agreementId} value={agreement.agreementId}>{agreement.facilityReference}</option>)}
        </OperationField>
        {approvedRevisions.length > 0 ? <OperationField label="Approved terms revision for terminal action" value={targetRevisionId} onChange={setTerminalRevision}>{approvedRevisions.map((revision) => <option key={revision.revisionId} value={revision.revisionId}>Revision {revision.revisionNumber} · effective {revision.effectiveFrom.slice(0, 10)}</option>)}</OperationField> : null}
      </FormGrid>
      <div className="flex flex-wrap gap-2">
        {([...(selected.status === "Draft" || selected.status === "Rejected" ? ["Submit"] : []),
          ...(selected.status === "Submitted" ? ["Approve", "Reject"] : []), ...(selected.status === "Approved" || approvedRevisions.length > 0 ? ["Terminate", "Novate"] : [])]).map((action) => <Button
            type="button" variant="outline" key={action} disabled={mutation.busy || !reason.trim() || !effectiveAt || (action === "Novate" && !successor) || (action === "Approve" && !selected.signedDocument)}
            onClick={() => void apply(action)}>{action} agreement</Button>)}
      </div>
      <p className="text-xs text-muted-foreground">Agreement approval can be performed by the owner. Counterpart journals continue through their independent journal review and posting controls.</p>
      <MutationFeedback state={mutation} />
      <TechnicalDetails label="Signed evidence and agreement history">
        {selected.signedDocument ? <dl className="grid gap-2 break-all text-xs">{Object.entries(selected.signedDocument).map(([key, value]) => <div key={key}><dt className="font-semibold">{key}</dt><dd className="font-mono">{value}</dd></div>)}</dl> : <p>No signed evidence attached to this revision.</p>}
        <OperationError error={history.error} /><ol className="mt-3 space-y-2 text-xs">{history.data?.events.map((event) => <li key={event.requestId}>{event.recordedAt} · {event.action} · {event.actor} · {event.reason ?? "Initial terms"} · revision {(event.targetRevisionSnapshot ?? event.snapshot).revisionNumber}</li>)}</ol>
      </TechnicalDetails>
    </section> : null}
    {editing && (creating || selected) ? <AgreementEditor key={creating ? "new-agreement" : `${selected!.agreementId}:${selected!.version}`} scope={scope} agreement={creating ? null : selected}
      onCancel={() => { setEditing(false); setCreating(false); }} onSaved={(result) => { setSelectedId(result.agreementId); setEditing(false); setCreating(false); agreements.refresh(); }} /> : null}
  </div>;
}
