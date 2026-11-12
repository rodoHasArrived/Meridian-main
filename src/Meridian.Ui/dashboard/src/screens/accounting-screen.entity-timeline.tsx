import { useState, type FormEvent } from "react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { FormGrid } from "@/components/ui/form";
import { TechnicalDetails } from "@/components/ui/technical-details";
import { entityOperationEndpoints as endpoints } from "@/lib/api/entity-operations.api";
import type { EntityLifecycleRevision, EntityOperationScope, EntityTimeline } from "@/types/entity-operations";
import { effectiveUtc, EntityOperationTable, MutationFeedback, newIdentity, OperationError, OperationField, today, useEntityData, useEntityMutation } from "./accounting-screen.entity-operations.shared";

const statuses = ["Draft", "Forming", "Active", "AmendmentPending", "Restructuring", "WindingDown", "Dissolved", "Merged"];
const events = ["Formation", "LegalFormChange", "NameChange", "JurisdictionChange", "Amendment", "Restructure", "SpvRollup", "BeneficialOwnershipChange", "Dissolution", "Reactivation", "Other"];

export function EntityLifecycleTimeline({ scope }: { scope: EntityOperationScope }) {
  const [entityId, setEntityId] = useState(scope.graph?.entities.find((entity) => entity.entityId === scope.book.fundStructureNodeId)?.entityId ?? scope.graph?.entities[0]?.entityId ?? "");
  const [organizationId, setOrganizationId] = useState(scope.graph?.organizations[0]?.organizationId ?? "");
  const [asOf, setAsOf] = useState(today());
  const query = new URLSearchParams({ organizationId, asOf: `${asOf}T23:59:59.9999999Z` });
  const timeline = useEntityData<EntityTimeline>(entityId && organizationId && asOf ? `/api/fund-structure/entities/${entityId}/timeline?${query}` : null);
  const currentLifecycle = useEntityData<EntityLifecycleRevision>(entityId && scope.capabilities.canManageLifecycle ? endpoints.lifecycle(entityId) : null);
  return <section className="space-y-4"><h3 className="font-semibold">Entity timeline and legal perimeter</h3><FormGrid columns={3}>
    <OperationField label="Timeline organization" value={organizationId} onChange={setOrganizationId} required><option value="">Select organization</option>{scope.graph?.organizations.map((organization) => <option key={organization.organizationId} value={organization.organizationId}>{organization.name}</option>)}</OperationField>
    <OperationField label="Timeline entity" value={entityId} onChange={setEntityId} required><option value="">Select entity</option>{scope.graph?.entities.map((entity) => <option key={entity.entityId} value={entity.entityId}>{entity.name}</option>)}</OperationField>
    <OperationField label="Timeline as of (UTC)" value={asOf} onChange={setAsOf} type="date" required />
  </FormGrid><Button type="button" variant="outline" onClick={() => { timeline.refresh(); currentLifecycle.refresh(); }} disabled={timeline.loading}>Refresh entity timeline</Button>
    <OperationError error={timeline.error} />{timeline.loading ? <p role="status">Loading shared entity history…</p> : null}
    {timeline.data ? <div className="space-y-4"><p className="text-sm"><strong>{timeline.data.entity.name}</strong> · {timeline.data.entity.baseCurrency} · server as of {timeline.data.asOf}</p>
      {timeline.data.warnings.map((warning, index) => <p role="status" className="text-sm text-warning" key={index}>{warning}</p>)}
      <EntityOperationTable label="Entity lifecycle timeline" rows={timeline.data.lifecycleRevisions.flatMap((revision) => revision.timeline.map((state) => ({ revision, state })))} getRowId={({ revision, state }) => `${revision.revision}:${state.transitionId}`} emptyText="No lifecycle windows are retained."
        columns={[
          { id: "revision", label: "Revision", rowHeader: true, render: ({ revision }) => `${revision.revision}${revision.legacySnapshot ? " · legacy snapshot" : ""}` },
          { id: "effective", label: "Effective window", render: ({ state }) => `${state.effectiveFrom} → ${state.effectiveTo ?? "Open"} (exclusive)` },
          { id: "lifecycle", label: "Lifecycle", render: ({ state }) => <><Badge variant="outline">{state.status}</Badge><p className="mt-1 text-xs">{state.eventKind}</p>{state.successorEntityId ? <p className="mt-1 break-all text-xs">Successor {state.successorEntityId}</p> : null}</> },
          { id: "evidence", label: "Evidence", render: ({ revision, state }) => <>{revision.reason}<p className="mt-1 text-xs">{revision.recordedBy} · {revision.recordedAtUtc}</p><TechnicalDetails label={`Lifecycle evidence revision ${revision.revision}, transition ${state.eventKind}`}><p className="break-all text-xs">{revision.evidenceReference}</p><p className="mt-1 break-all font-mono text-xs">{revision.contentHash}</p></TechnicalDetails></> }
        ]} />
      <TechnicalDetails label="Ownership, agreements and books"><div className="grid gap-4 lg:grid-cols-3"><div><h4 className="mb-2 text-sm font-semibold">Ownership links</h4><ul className="space-y-2 text-xs">{timeline.data.ownershipLinks.map((link) => <li key={link.ownershipLinkId} className="break-all">{link.relationshipType} · {link.ownershipPercent ?? "Unspecified"}% · {link.effectiveFrom} → {link.effectiveTo ?? "Open"}<p>{link.parentNodeId} → {link.childNodeId}</p></li>)}</ul></div>
        <div><h4 className="mb-2 text-sm font-semibold">Agreements</h4><ul className="space-y-2 text-xs">{timeline.data.agreements.map((agreement) => <li key={agreement.agreementId}>{agreement.facilityReference} · {agreement.status} · {agreement.currency}<p>Revision {agreement.revisionNumber}</p></li>)}</ul></div>
        <div><h4 className="mb-2 text-sm font-semibold">Books</h4><ul className="space-y-2 text-xs">{timeline.data.books.map((book) => <li key={book.ledgerBookId}>{book.displayName} · {book.accountingBasis} · {book.baseCurrency}</li>)}</ul></div></div></TechnicalDetails>
    </div> : null}
    <OperationError error={currentLifecycle.error} />
    {scope.capabilities.canManageLifecycle && currentLifecycle.data ? <LifecycleTransitionEditor key={`${entityId}:${currentLifecycle.data.revision}`} scope={scope} current={currentLifecycle.data} onSaved={() => { timeline.refresh(); currentLifecycle.refresh(); }} />
      : !scope.capabilities.canManageLifecycle ? <p className="text-sm text-muted-foreground">Entity lifecycle transitions require fund-structure management permission. Historical posted balances remain inspectable.</p> : null}
  </section>;
}

function LifecycleTransitionEditor({ scope, current, onSaved }: { scope: EntityOperationScope; current: EntityLifecycleRevision; onSaved: () => void }) {
  const [status, setStatus] = useState("Active");
  const [eventKind, setEventKind] = useState("Amendment");
  const [effectiveFrom, setEffectiveFrom] = useState(today());
  const [successorEntityId, setSuccessorEntityId] = useState("");
  const [reason, setReason] = useState("");
  const [evidence, setEvidence] = useState("");
  const mutation = useEntityMutation();
  async function retain(event: FormEvent) {
    event.preventDefault();
    const date = effectiveUtc(effectiveFrom);
    const priorStates = current.timeline.map((state) => state.effectiveTo === null ? { ...state, effectiveTo: date } : state);
    const result = await mutation.run<EntityLifecycleRevision>(`${endpoints.lifecycle(current.entityId)}/revisions`, {
      entityId: current.entityId, expectedRevision: current.revision, reason: reason.trim(), evidenceReference: evidence.trim(),
      timeline: [...priorStates, { transitionId: newIdentity(), effectiveFrom: date, effectiveTo: null, status, eventKind,
        successorEntityId: successorEntityId || null }]
    }, "A lifecycle successor was retained. Posting eligibility is evaluated from its effective window and each book's control mode.");
    if (result) onSaved();
  }
  return <TechnicalDetails label="Retain an entity lifecycle transition"><form className="space-y-3" onSubmit={(event) => void retain(event)}><p className="text-sm text-muted-foreground">This closes the current open status window at the selected UTC date and appends a new window. A merger requires an explicit successor. Historical journals remain immutable.</p><fieldset disabled={mutation.busy} className="space-y-3"><FormGrid columns={3}>
    <OperationField label="Next entity status" value={status} onChange={setStatus}>{statuses.map((value) => <option key={value}>{value}</option>)}</OperationField>
    <OperationField label="Lifecycle event" value={eventKind} onChange={setEventKind}>{events.map((value) => <option key={value}>{value}</option>)}</OperationField>
    <OperationField label="Lifecycle transition effective from (UTC)" value={effectiveFrom} onChange={setEffectiveFrom} type="date" required />
    <OperationField label="Successor entity" value={successorEntityId} onChange={setSuccessorEntityId} required={status === "Merged" || eventKind === "SpvRollup"}><option value="">Select successor if applicable</option>{scope.graph?.entities.filter((entity) => entity.entityId !== current.entityId).map((entity) => <option key={entity.entityId} value={entity.entityId}>{entity.name}</option>)}</OperationField>
    <OperationField label="Lifecycle transition reason" value={reason} onChange={setReason} required />
    <OperationField label="Lifecycle transition evidence" value={evidence} onChange={setEvidence} required />
  </FormGrid><Button type="submit" variant="outline">Retain lifecycle successor</Button></fieldset><MutationFeedback state={mutation} /></form></TechnicalDetails>;
}
