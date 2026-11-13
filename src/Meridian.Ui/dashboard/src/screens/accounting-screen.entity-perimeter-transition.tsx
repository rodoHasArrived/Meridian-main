import { useState, type FormEvent } from "react";
import { Button } from "@/components/ui/button";
import { FormGrid } from "@/components/ui/form";
import { TechnicalDetails } from "@/components/ui/technical-details";
import { getEntityOperation } from "@/lib/api/entity-operations.api";
import { describeApiError } from "@/lib/api-errors";
import type { LedgerPeriod } from "@/types";
import type { ConsolidationTransitionPreview, ConsolidationTransitionReceipt, ConsolidationTransitionRequest, EntityOperationScope } from "@/types/entity-operations";
import { EntityOperationTable, MutationFeedback, newIdentity, OperationError, OperationField, today, useEntityData, useEntityMutation } from "./accounting-screen.entity-operations.shared";

const base = "/api/ledger/consolidation/transitions";
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function EntityPerimeterTransition({ scope }: { scope: EntityOperationScope }) {
  const [request, setRequest] = useState<ConsolidationTransitionRequest>(() => ({ transitionId: newIdentity(),
    organizationId: scope.graph?.organizations[0]?.organizationId ?? "", ownershipRootId: scope.book.fundStructureNodeKind === "Fund" ? scope.book.fundStructureNodeId : "",
    previousEliminationBookId: scope.book.fundStructureNodeKind === "Fund" ? scope.book.ledgerBookId : "", newEliminationBookId: "", newPeriodId: "", cutoverDate: today(), reason: "", evidenceReference: "" }));
  const [preview, setPreview] = useState<ConsolidationTransitionPreview | null>(null);
  const [loadId, setLoadId] = useState("");
  const [loadingPreview, setLoadingPreview] = useState(false);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [receipt, setReceipt] = useState<ConsolidationTransitionReceipt | null>(null);
  const mutation = useEntityMutation();
  const periods = useEntityData<LedgerPeriod[]>(request.newEliminationBookId ? `/api/ledger/periods?${new URLSearchParams({ ledgerBookId: request.newEliminationBookId })}` : null);
  const history = useEntityData<ConsolidationTransitionReceipt[]>(uuid.test(request.organizationId) && uuid.test(request.ownershipRootId)
    ? `${base}?${new URLSearchParams({ organizationId: request.organizationId, ownershipRootId: request.ownershipRootId })}` : null);
  const fundBooks = scope.books.filter((book) => book.fundStructureNodeKind === "Fund" && book.accountingBasis === "Primary");
  const name = (id: string) => scope.graph?.entities.find((entity) => entity.entityId === id)?.name ?? id;
  const bookName = (id: string) => scope.books.find((book) => book.ledgerBookId === id)?.displayName ?? id;
  function update(field: keyof ConsolidationTransitionRequest, value: string) {
    setRequest((current) => ({ ...current, [field]: value, ...(field === "newEliminationBookId" ? { newPeriodId: "" } : {}) }));
    setPreview(null); setReceipt(null);
  }
  async function prepare(event: FormEvent) {
    event.preventDefault();
    setPreview(null); setReceipt(null);
    const result = await mutation.run<ConsolidationTransitionPreview>(`${base}/preview`, request, "Cutover preview retained. An independent operator must reopen and approve this exact evidence before the book route changes.");
    if (result) { setPreview(result); setLoadId(result.previewId); }
  }
  async function loadRetained() {
    if (!uuid.test(loadId) || loadingPreview) return;
    setLoadingPreview(true); setLoadError(null); setPreview(null); setReceipt(null);
    try {
      const result = await getEntityOperation<ConsolidationTransitionPreview>(`${base}/previews/${encodeURIComponent(loadId)}`);
      setRequest(result.request); setLoadId(result.previewId); setPreview(result);
    } catch (error) { setLoadError(describeApiError(error, "The retained cutover preview could not be loaded.").summary); }
    finally { setLoadingPreview(false); }
  }
  async function activate() {
    if (!preview?.canApprove || preview.blockers.length > 0) return;
    const result = await mutation.run<ConsolidationTransitionReceipt>(`${base}/activate`, { previewId: preview.previewId, previewFingerprint: preview.fingerprint },
      "Independently approved perimeter cutover retained. Old-book history remains inspectable; the new dedicated book is authoritative from the cutover date.");
    if (result) { setReceipt(result); setPreview(null); history.refresh();
      setRequest((current) => ({ ...current, transitionId: newIdentity(), previousEliminationBookId: result.request.newEliminationBookId,
        newEliminationBookId: "", newPeriodId: "", reason: "", evidenceReference: "" })); }
  }
  if (!scope.capabilities.canManageLedger) return <p className="text-sm text-muted-foreground">Preparing or approving a consolidation perimeter cutover requires ledger management permission and authority over both books.</p>;
  return <section className="space-y-4"><h3 className="font-semibold">Reviewed consolidation perimeter cutover</h3>
    <p className="text-sm text-muted-foreground">Ownership changes can move consolidated reporting into a new, empty dedicated book. The new book rebuilds its elimination target from the new gross perimeter; prior-book overlays remain historical and are not deducted again. This does not novate legal agreements or rewrite source journals.</p>
    <form className="space-y-3 rounded border border-border p-4" onSubmit={(event) => void prepare(event)}><fieldset disabled={mutation.busy || loadingPreview} className="space-y-3"><FormGrid columns={3}>
      <OperationField label="Cutover organization ID" value={request.organizationId} onChange={(value) => update("organizationId", value)} required />
      <OperationField label="Cutover ownership root ID" value={request.ownershipRootId} onChange={(value) => update("ownershipRootId", value)} required />
      <OperationField label="Previous dedicated elimination book" value={request.previousEliminationBookId} onChange={(value) => update("previousEliminationBookId", value)} required><option value="">Select old dedicated book</option>{fundBooks.map((book) => <option key={book.ledgerBookId} value={book.ledgerBookId}>{book.displayName}</option>)}</OperationField>
      <OperationField label="New empty dedicated elimination book" value={request.newEliminationBookId} onChange={(value) => update("newEliminationBookId", value)} required><option value="">Select new dedicated book</option>{fundBooks.filter((book) => book.ledgerBookId !== request.previousEliminationBookId && ["w10-v2", "w10-v3", "w10-v4"].includes(book.accountingPolicyVersion)).map((book) => <option key={book.ledgerBookId} value={book.ledgerBookId}>{book.displayName} · {book.accountingPolicyVersion}</option>)}</OperationField>
      <OperationField label="New-book cutover period" value={request.newPeriodId} onChange={(value) => update("newPeriodId", value)} required><option value="">Select new-book period</option>{periods.data?.filter((period) => period.status === "Open").map((period) => <option key={period.periodId} value={period.periodId}>{period.label} · {period.startDate} → {period.endDate}</option>)}</OperationField>
      <OperationField label="Perimeter cutover date (UTC)" value={request.cutoverDate} onChange={(value) => update("cutoverDate", value)} type="date" required />
      <OperationField label="Perimeter cutover reason" value={request.reason} onChange={(value) => update("reason", value)} required />
      <OperationField label="Perimeter cutover evidence reference" value={request.evidenceReference} onChange={(value) => update("evidenceReference", value)} required />
    </FormGrid><Button type="submit" variant="outline">Preview and retain perimeter cutover</Button></fieldset></form>
    <OperationError error={periods.error} />
    <div className="space-y-3 rounded border border-border p-4"><h4 className="text-sm font-semibold">Reopen a prepared cutover for independent review</h4><p className="text-xs text-muted-foreground">The preparer shares the retained preview ID. Loading it preserves the original preparer and fingerprint; preparing another preview creates a new review.</p>
      <OperationField label="Retained perimeter preview ID" value={loadId} onChange={(value) => { setLoadId(value); setPreview(null); }} disabled={loadingPreview || mutation.busy} />
      <Button type="button" variant="outline" disabled={!uuid.test(loadId) || loadingPreview || mutation.busy} onClick={() => void loadRetained()}>Load retained perimeter preview</Button><OperationError error={loadError} />
    </div>
    {preview ? <section className="space-y-3 rounded border border-border p-4" aria-label="Retained perimeter cutover preview"><h4 className="font-semibold">Cutover {preview.request.cutoverDate} · root version {preview.rootVersion}</h4><p className="text-sm">{bookName(preview.request.previousEliminationBookId)} → {bookName(preview.request.newEliminationBookId)} · new rule {preview.newEvidence.ruleVersion}</p>
      <p className="text-sm">Prepared by {preview.preparedBy} · {preview.preparedAt}. Approval requires a different authorized operator.</p><p className="text-xs">Previous latest posted elimination: {preview.previousLatestPostedAsOf ?? "None"} · {preview.previousPostedJournalIds.length} retained overlays</p>
      <div className="grid gap-3 lg:grid-cols-2"><div><h5 className="text-sm font-semibold">Joined entities</h5><ul className="text-sm">{preview.joinedEntityIds.map((id) => <li key={id} title={id}>{name(id)}</li>)}</ul>{preview.joinedEntityIds.length === 0 ? <p className="text-sm text-muted-foreground">None</p> : null}</div>
        <div><h5 className="text-sm font-semibold">Removed entities</h5><ul className="text-sm">{preview.removedEntityIds.map((id) => <li key={id} title={id}>{name(id)}</li>)}</ul>{preview.removedEntityIds.length === 0 ? <p className="text-sm text-muted-foreground">None</p> : null}</div></div>
      {preview.blockers.length > 0 ? <div role="alert" className="rounded border border-warning/40 p-3 text-sm"><p className="font-semibold">Cutover approval is blocked</p><ul className="list-disc pl-5">{preview.blockers.map((blocker) => <li key={blocker}>{blocker}</li>)}</ul></div> : null}
      <TechnicalDetails label="Retained perimeter cutover evidence"><dl className="space-y-2 break-all text-xs"><div><dt>Preview ID</dt><dd className="font-mono">{preview.previewId}</dd></div><div><dt>Preview fingerprint</dt><dd className="font-mono">{preview.fingerprint}</dd></div><div><dt>Previous book evidence</dt><dd className="font-mono">{preview.previousBookEvidenceDigest}</dd></div><div><dt>New source fingerprint</dt><dd className="font-mono">{preview.newEvidence.sourceFingerprint}</dd></div><div><dt>New perimeter fingerprint</dt><dd className="font-mono">{preview.newEvidence.perimeterFingerprint}</dd></div><div><dt>Reason and evidence</dt><dd>{preview.request.reason} · {preview.request.evidenceReference}</dd></div></dl></TechnicalDetails>
      <Button type="button" disabled={!preview.canApprove || preview.blockers.length > 0 || mutation.busy || loadingPreview} onClick={() => void activate()}>Independently approve perimeter cutover</Button>
    </section> : null}
    <MutationFeedback state={mutation} />
    {receipt ? <p role="status" className="break-all text-sm">Approved by {receipt.approvedBy} · receipt {receipt.transitionId} · {receipt.approvedAt}</p> : null}
    <OperationError error={history.error} /><EntityOperationTable label="Approved perimeter cutovers" rows={history.data ?? []} getRowId={(row) => row.transitionId} emptyText="No approved cutovers in this root scope."
      columns={[
        { id: "date", label: "Cutover date", rowHeader: true, render: (row) => row.request.cutoverDate },
        { id: "books", label: "Book route", render: (row) => `${bookName(row.request.previousEliminationBookId)} → ${bookName(row.request.newEliminationBookId)}` },
        { id: "review", label: "Independent review", render: (row) => `${row.preparedBy} → ${row.approvedBy} · ${row.approvedAt}` },
        { id: "receipt", label: "Receipt evidence", render: (row) => <TechnicalDetails label="Approved cutover receipt"><p className="break-all text-xs">{row.transitionId}</p><p className="break-all font-mono text-xs">{row.contentHash}</p></TechnicalDetails> }
      ]} />
  </section>;
}
