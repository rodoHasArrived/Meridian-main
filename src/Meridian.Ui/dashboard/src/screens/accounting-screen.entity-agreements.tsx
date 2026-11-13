import { useState } from "react";
import { Link } from "react-router-dom";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { FormGrid } from "@/components/ui/form";
import { TechnicalDetails } from "@/components/ui/technical-details";
import { entityOperationEndpoints as endpoints } from "@/lib/api/entity-operations.api";
import type { AccountingArrangementHistory, AccountingArrangementRecord, EntityOperationScope, IntercompanyAccountingArrangement } from "@/types/entity-operations";
import { AccountingArrangementEditor, canonicalAgreementPath } from "./accounting-screen.entity-agreement-editor";
import { effectiveUtc, EntityOperationTable, MutationFeedback, OperationError, OperationField, today, useEntityData, useEntityMutation } from "./accounting-screen.entity-operations.shared";

export function EntityAccountingArrangements({ scope }: { scope: EntityOperationScope }) {
  const [organizationSelection, setOrganizationSelection] = useState("");
  const organizationId = organizationSelection || scope.graph?.organizations[0]?.organizationId || "";
  const arrangements = useEntityData<IntercompanyAccountingArrangement[]>(scope.capabilities.canManageAgreements && organizationId ? `${endpoints.accountingArrangements}?${new URLSearchParams({ organizationId })}` : null);
  const [selectedId, setSelectedId] = useState("");
  const [editing, setEditing] = useState(false);
  const [creating, setCreating] = useState(false);
  const [reason, setReason] = useState("");
  const [effectiveAt, setEffectiveAt] = useState(today());
  const [retiringRevision, setRetiringRevision] = useState("");
  const mutation = useEntityMutation();
  const selected = arrangements.data?.find((row) => row.arrangementId === selectedId) ?? null;
  const history = useEntityData<AccountingArrangementHistory>(selected ? `${endpoints.accountingArrangements}/${selected.arrangementId}/history` : null);
  const revisionSnapshots = new Map<string, AccountingArrangementRecord>();
  history.data?.events.forEach((event) => { const affected = event.targetRevisionSnapshot ?? event.snapshot; revisionSnapshots.set(affected.revisionId, affected); });
  const approvedRevisions = [...revisionSnapshots.values()].filter((revision) => revision.status === "Approved");
  const targetRevisionId = approvedRevisions.some((revision) => revision.revisionId === retiringRevision) ? retiringRevision : approvedRevisions[0]?.revisionId ?? selected?.revisionId ?? "";
  if (!scope.capabilities.canManageAgreements) return <p className="text-sm text-muted-foreground">Accounting profile management requires fund-structure permission for the organization and both entities. Authorized journal workflows retain their separate book access.</p>;

  async function apply(action: "submit" | "approve" | "reject" | "retire") {
    if (!selected || editing || mutation.busy || !reason.trim()) return;
    const command = { arrangementId: selected.arrangementId, expectedVersion: selected.version, action,
      reason: reason.trim(), effectiveAt: action === "retire" ? effectiveUtc(effectiveAt) : null,
      revisionId: action === "retire" ? targetRevisionId : selected.revisionId };
    const result = await mutation.run<AccountingArrangementRecord>(`${endpoints.accountingArrangements}/${selected.arrangementId}/${action}`,
      { requestId: mutation.identity(command), expectedVersion: command.expectedVersion, reason: command.reason, effectiveAt: command.effectiveAt, revisionId: command.revisionId },
      `Accounting profile ${action} retained in its mapping history.`);
    if (result) { arrangements.refresh(); history.refresh(); setEditing(false); }
  }
  return <div className="space-y-4"><div className="flex flex-wrap items-center justify-between gap-2"><h3 className="font-semibold">Intercompany accounting profiles</h3><div className="flex flex-wrap gap-2">
    <Button type="button" variant="outline" onClick={arrangements.refresh} disabled={arrangements.loading || !organizationId || editing || mutation.busy} disabledReason={mutation.busy ? "Wait for the current accounting profile change to finish." : editing ? "Save or cancel the current accounting profile edit." : !organizationId ? "Select an accounting profile organization." : "Wait for accounting profiles to load."}>Refresh accounting profiles</Button>
    <Button type="button" disabled={!organizationId || editing || mutation.busy} disabledReason={mutation.busy ? "Wait for the current accounting profile change to finish." : editing ? "Save or cancel the current accounting profile edit." : "Select an accounting profile organization."} onClick={() => { setCreating(true); setEditing(true); }}>New accounting profile</Button>
  </div></div>
  <p className="text-sm text-muted-foreground">Profiles govern book mappings and counterpart accounting against an exact approved legal agreement version. Legal terms and their independent review remain in the canonical agreement registry.</p>
  <Button asChild variant="ghost" size="sm" className="underline underline-offset-4"><Link to={canonicalAgreementPath(undefined, scope.book.ledgerBookId)}>Open canonical legal agreement registry</Link></Button>
  <OperationField label="Accounting profile perimeter" value={organizationId} disabled={editing || mutation.busy} onChange={(value) => { setOrganizationSelection(value); setSelectedId(""); setCreating(false); setEditing(false); }}><option value="">Select organization</option>{scope.graph?.organizations.map((organization) => <option key={organization.organizationId} value={organization.organizationId}>{organization.name}</option>)}</OperationField>
  <OperationError error={arrangements.error} />{arrangements.loading ? <p role="status">Loading retained accounting profiles…</p> : null}
  {!organizationId ? <p className="text-sm text-muted-foreground">Select an authorized organization to inspect its accounting profiles.</p> : null}
  {arrangements.data ? <EntityOperationTable label="Intercompany accounting profiles" rows={arrangements.data} getRowId={(arrangement) => arrangement.arrangementId} emptyText="No retained accounting profiles in this perimeter."
    columns={[
      { id: "reference", label: "Accounting profile", rowHeader: true, render: (arrangement) => <Button type="button" variant="outline" size="sm" disabled={editing || mutation.busy} disabledReason={mutation.busy ? "Wait for the current accounting profile change to finish." : "Save or cancel the current accounting profile edit."} aria-pressed={selectedId === arrangement.arrangementId} onClick={() => { setSelectedId(arrangement.arrangementId); setEditing(false); setCreating(false); setReason(""); setRetiringRevision(""); }}>{arrangement.arrangementReference}</Button> },
      { id: "template", label: "Template and legal currency", render: (arrangement) => `${arrangement.templateKind} · ${arrangement.currency}` },
      { id: "effective", label: "Mapping window", render: (arrangement) => `${arrangement.effectiveFrom.slice(0, 10)} → ${arrangement.effectiveTo?.slice(0, 10) ?? "Open"} (exclusive)` },
      { id: "status", label: "Mapping review", render: (arrangement) => <><Badge variant="outline">{arrangement.status}</Badge>{arrangement.arrangement.approvedBy ? <p className="mt-1 text-xs">{arrangement.arrangement.approvedBy}</p> : null}</> },
      { id: "revision", label: "Accounting / legal versions", render: (arrangement) => <>Mapping revision {arrangement.revisionNumber} · version {arrangement.version}<p>Legal version {arrangement.canonicalAgreementVersion}</p></> }
    ]} /> : null}
  {selected && !creating ? <section className="space-y-3 rounded border border-border p-4" aria-label="Selected accounting profile"><h4 className="font-semibold">{selected.arrangementReference} · {selected.partyOneRole} ↔ {selected.partyTwoRole}</h4>
    <p className="text-sm">Canonical {selected.legalRevision.terms.code} · legal version {selected.canonicalAgreementVersion} · {selected.currency} · {selected.settlementCadence || "No legal settlement cadence specified"}</p>
    <Button asChild variant="ghost" size="sm" className="underline underline-offset-4"><Link to={canonicalAgreementPath(selected.canonicalAgreementId, scope.book.ledgerBookId)}>Inspect approved legal terms and signed evidence</Link></Button>
    <p className="break-all text-xs text-muted-foreground">Accounting profile {selected.arrangementId} · mapping revision {selected.revisionId}</p>
    <Button type="button" variant="outline" onClick={() => setEditing(true)} disabled={editing || mutation.busy || selected.status === "Retired"} disabledReason={mutation.busy ? "Wait for the current accounting profile change to finish." : editing ? "Save or cancel the current accounting profile edit." : "Retired accounting mappings cannot be revised."}>Revise accounting mappings</Button>
    <FormGrid columns={3}><OperationField label="Accounting profile action reason" value={reason} onChange={setReason} required />
      {selected.status === "Approved" || approvedRevisions.length > 0 ? <OperationField label="Mapping retirement effective date" value={effectiveAt} onChange={setEffectiveAt} type="date" required /> : null}
      {approvedRevisions.length > 0 ? <OperationField label="Approved mapping revision to retire" value={targetRevisionId} onChange={setRetiringRevision}>{approvedRevisions.map((revision) => <option key={revision.revisionId} value={revision.revisionId}>Mapping revision {revision.revisionNumber} · legal version {revision.canonicalAgreementVersion}</option>)}</OperationField> : null}
    </FormGrid>
    <div className="flex flex-wrap gap-2">{(selected.status === "Draft" || selected.status === "Rejected" ? ["submit"] as const : selected.status === "Submitted" ? ["approve", "reject"] as const : []).map((action) => <Button type="button" variant="outline" key={action} disabled={editing || mutation.busy || !reason.trim()} disabledReason={mutation.busy ? "Wait for the current accounting profile change to finish." : editing ? "Save or cancel the current accounting profile edit." : "Enter a reason for this accounting profile decision."} onClick={() => void apply(action)}>{action === "submit" ? "Submit accounting profile" : action === "approve" ? "Approve accounting mappings" : "Reject accounting mappings"}</Button>)}
      {selected.status === "Approved" || approvedRevisions.length > 0 ? <Button type="button" variant="outline" disabled={editing || mutation.busy || !reason.trim() || !effectiveAt} disabledReason={mutation.busy ? "Wait for the current accounting profile change to finish." : editing ? "Save or cancel the current accounting profile edit." : !reason.trim() ? "Enter a reason for retiring these mappings." : "Select the mapping retirement effective date."} onClick={() => void apply("retire")}>Retire accounting mappings</Button> : null}
    </div>
    <p className="text-xs text-muted-foreground">The owner may approve accounting mappings. This records no legal approval. Counterpart journals retain their independent journal review and posting controls.</p>
    <MutationFeedback state={mutation} />
    <TechnicalDetails label="Canonical legal authority and accounting history"><dl className="space-y-2 break-all text-xs"><div><dt>Canonical legal agreement</dt><dd>{selected.canonicalAgreementId} · version {selected.canonicalAgreementVersion}</dd></div><div><dt>Approved legal payload hash</dt><dd className="font-mono">{selected.canonicalPayloadHash}</dd></div><div><dt>Legal effective window</dt><dd>{selected.legalRevision.terms.effectiveFrom} → {selected.legalRevision.terms.effectiveTo ?? "Open"} (exclusive)</dd></div><div><dt>Canonical legal approval</dt><dd>{selected.legalRevision.approver} · {selected.legalRevision.approvedAtUtc}</dd></div></dl>
      <ul className="mt-3 space-y-2 break-all text-xs">{selected.canonicalEvidence.map((evidence, index) => <li key={index}>{evidence.reference}<p className="font-mono">{evidence.artifactId} · {evidence.contentHash}</p></li>)}</ul>
      <OperationError error={history.error} /><ol className="mt-3 space-y-2 text-xs">{history.data?.events.map((event) => <li key={event.requestId}>{event.recordedAt} · {event.action} · {event.actor} · {event.reason ?? "Initial mapping"} · mapping revision {(event.targetRevisionSnapshot ?? event.snapshot).revisionNumber}</li>)}</ol>
    </TechnicalDetails>
  </section> : null}
  {editing && (creating || selected) ? <AccountingArrangementEditor key={creating ? `new-arrangement:${organizationId}` : `${selected!.arrangementId}:${selected!.version}`} scope={scope} arrangement={creating ? null : selected} initialOrganizationId={organizationId}
    onCancel={() => { setEditing(false); setCreating(false); }} onSaved={(result) => { setSelectedId(result.arrangementId); setEditing(false); setCreating(false); arrangements.refresh(); }} /> : null}
  </div>;
}
