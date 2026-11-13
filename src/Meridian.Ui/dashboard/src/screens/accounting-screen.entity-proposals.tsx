import { useState, type FormEvent } from "react";
import { Link } from "react-router-dom";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { FormGrid } from "@/components/ui/form";
import { TechnicalDetails } from "@/components/ui/technical-details";
import { entityOperationEndpoints as endpoints } from "@/lib/api/entity-operations.api";
import type { EntityOperationScope, IntercompanyBreak, IntercompanyProposal } from "@/types/entity-operations";
import { EntityOperationTable, MutationFeedback, OperationError, OperationField, useEntityData, useEntityMutation } from "./accounting-screen.entity-operations.shared";
import { CounterpartRepricing } from "./accounting-screen.entity-counterpart-repricing";

function resolvedTarget(proposal: IntercompanyProposal) {
  return { bookId: proposal.resolvedTargetBookId ?? proposal.latestLink?.targetBookId ?? proposal.targetBookId,
    journalId: proposal.resolvedTargetJournalId ?? proposal.latestLink?.targetJournalId ?? proposal.targetJournalId };
}
const hasResolution = (proposal: IntercompanyProposal) => Boolean(proposal.latestLink) || proposal.status === "Linked";

export function EntityCounterpartProposals({ scope }: { scope: EntityOperationScope }) {
  const proposals = useEntityData<IntercompanyProposal[]>(endpoints.proposals(scope.book.ledgerBookId));
  const breaks = useEntityData<IntercompanyBreak[]>(endpoints.breaks(scope.book.ledgerBookId));
  const [selectedId, setSelectedId] = useState("");
  const [targetBookId, setTargetBookId] = useState("");
  const [targetJournalId, setTargetJournalId] = useState("");
  const [reason, setReason] = useState("");
  const [retryReason, setRetryReason] = useState("");
  const [notice, setNotice] = useState("");
  const mutation = useEntityMutation();
  const selected = proposals.data?.find((item) => item.source.proposalId === selectedId);
  const selectedResolved = selected ? hasResolution(selected) : false;
  async function deliver() {
    if (!selected || selectedResolved) return;
    if (!retryReason.trim()) return;
    const command = { proposalId: selected.source.proposalId, reason: retryReason.trim() };
    const result = await mutation.run<IntercompanyProposal>(`/api/ledger/intercompany/proposals/${selected.source.proposalId}/retry`, { requestId: mutation.identity(command), reason: command.reason }, "A retry was scheduled against the retained source identity. Background delivery retains the target draft or repair reason.");
    if (result) { proposals.refresh(); breaks.refresh(); }
  }
  async function link(event: FormEvent) {
    event.preventDefault();
    if (!selected || selectedResolved) return;
    const command = { proposalId: selected.source.proposalId, targetBookId, targetJournalId, reason: reason.trim() };
    const result = await mutation.run<IntercompanyProposal>(`/api/ledger/intercompany/proposals/${selected.source.proposalId}/link`,
      { requestId: mutation.identity(command), targetBookId, targetJournalId, reason: reason.trim() }, "Existing counterpart journal linked after source and target validation.");
    if (result) { proposals.refresh(); breaks.refresh(); }
  }
  return <section className="space-y-4"><div className="flex flex-wrap items-center justify-between gap-2"><h3 className="font-semibold">Counterpart proposals and intercompany breaks</h3>
    <Button type="button" variant="outline" onClick={() => { proposals.refresh(); breaks.refresh(); }}>Refresh counterpart evidence</Button></div>
    <p className="text-sm text-muted-foreground">Source posting retains a delivery record. The other entity reviews its own draft; source preparers gain no target-book edit rights. Link an existing counterpart only after checking its journal identity.</p>
    <OperationError error={proposals.error} /><OperationError error={breaks.error} />
    {proposals.loading ? <p role="status">Loading counterpart delivery records…</p> : null}
    {proposals.data?.length === 0 ? <p className="text-sm text-muted-foreground">No counterpart proposals originate in this book.</p> : null}
    <EntityOperationTable label="Counterpart delivery records" rows={proposals.data ?? []} getRowId={(proposal) => proposal.source.proposalId} emptyText="No counterpart delivery records originate in this book."
      columns={[
        { id: "source", label: "Source transaction", rowHeader: true, render: (proposal) => <><Button type="button" variant="outline" size="sm" aria-pressed={selectedId === proposal.source.proposalId} onClick={() => setSelectedId(proposal.source.proposalId)}>{proposal.source.transactionId}</Button><p className="mt-1 text-xs">{proposal.source.effectiveDate}</p></> },
        { id: "delivery", label: "Delivery", render: (proposal) => <><Badge variant={!hasResolution(proposal) && proposal.status === "NeedsFix" ? "warning" : "outline"}>{proposal.latestLink ? "Resolved" : proposal.status}</Badge><p className="mt-1 text-xs">{proposal.attempts} delivery attempts</p>{proposal.latestLink ? <p className="mt-1 text-xs">Original delivery receipt: {proposal.status}</p> : proposal.lastError ? <p className="mt-1 text-xs text-warning">{proposal.lastError}</p> : null}</> },
        { id: "target", label: "Target", className: "break-all", render: (proposal) => {
          const target = resolvedTarget(proposal);
          return target.journalId ? <Button asChild variant="ghost" size="sm" className="underline underline-offset-4"><Link to={`/accounting/journal-entries?${new URLSearchParams({ ledgerBookId: target.bookId ?? "", journalEntryId: target.journalId })}`}>{hasResolution(proposal) ? "Open resolved counterpart journal" : "Open target journal"}</Link></Button> : "Awaiting counterpart draft";
        } },
        { id: "evidence", label: "Evidence", render: (proposal) => <TechnicalDetails label={`Provenance for ${proposal.source.transactionId}`}><dl className="space-y-2 break-all text-xs"><div><dt>Source journal</dt><dd>{proposal.source.sourceJournalId}</dd></div>
          <div><dt>Accounting arrangement</dt><dd>{proposal.source.accountingArrangementId}</dd></div><div><dt>Accounting mapping revision</dt><dd>{proposal.source.accountingArrangementRevisionId}</dd></div>
          <div><dt>Canonical legal agreement</dt><dd>{proposal.source.intercompanyAgreementId || "Not retained"}</dd></div><div><dt>Approved legal agreement version</dt><dd>{proposal.source.intercompanyAgreementVersion && proposal.source.intercompanyAgreementVersion > 0 ? proposal.source.intercompanyAgreementVersion : "Not retained"}</dd></div>
          <div><dt>Legal agreement payload hash</dt><dd>{proposal.source.intercompanyAgreementPayloadHash || "Not retained"}</dd></div><div><dt>Source digest</dt><dd>{proposal.sourceDigest}</dd></div>
          <div><dt>Original delivered book</dt><dd>{proposal.targetBookId ?? "No original delivery receipt"}</dd></div><div><dt>Original delivered journal</dt><dd>{proposal.targetJournalId ?? "No original delivery receipt"}</dd></div>
          {proposal.latestLink ? <><div><dt>Resolved posted counterpart</dt><dd>{proposal.latestLink.targetBookId} · {proposal.latestLink.targetJournalId}</dd></div><div><dt>Resolution actor and time</dt><dd>{proposal.latestLink.actor} · {proposal.latestLink.recordedAt}</dd></div><div><dt>Resolution reason</dt><dd>{proposal.latestLink.reason}</dd></div>{proposal.latestLink.rejectedDraftId ? <div><dt>Rejected generated draft</dt><dd>{proposal.latestLink.rejectedDraftId} · version {proposal.latestLink.rejectedDraftVersion}</dd></div> : null}</> : null}
        </dl></TechnicalDetails> }
      ]} />
    <h4 className="text-sm font-semibold">Booking breaks</h4><p className="text-xs text-muted-foreground">Aging uses the retained basis below. FX availability, translation and rounding differences are reported separately in consolidation.</p>
    <ul className="space-y-2">{breaks.data?.map((item) => <li key={`${item.proposalId}:${item.classification}`} className="rounded border border-warning/30 p-3 text-sm"><p className="font-semibold">{item.classification} · {item.ageDays} days</p><p>{item.detail}</p><p className="mt-1 text-xs text-muted-foreground">{item.agingBasis} from {item.ageFrom} · transaction {item.transactionId}</p><p className="mt-1 break-all text-xs text-muted-foreground">Accounting profile {item.accountingArrangementId}</p></li>)}</ul>
    {breaks.data?.length === 0 ? <p className="text-sm text-muted-foreground">No reported counterpart booking breaks.</p> : null}
    {selected ? <section aria-label="Selected counterpart proposal" className="space-y-3 rounded border border-border p-4"><h4 className="font-semibold">{selectedResolved ? "Resolved" : "Resolve"} {selected.source.transactionId}</h4>
      {selectedResolved ? <p className="text-sm text-muted-foreground">An explicit counterpart resolution is retained. Open the resolved journal above; the original delivery receipt remains in provenance. Delivery retries, repricing and replacement links are closed.</p> : null}
      {!selectedResolved && (selected.status === "Pending" || selected.status === "NeedsFix") ? <div className="space-y-3"><OperationField label="Delivery retry reason" value={retryReason} onChange={setRetryReason} required />
        <Button type="button" variant="outline" disabled={!scope.capabilities.canManageLedger || mutation.busy || !retryReason.trim()} disabledReason={!scope.capabilities.canManageLedger ? "Ledger management permission is required to retry counterpart delivery." : mutation.busy ? "Wait for the current counterpart change to finish." : "Enter a reason before retrying counterpart delivery."} onClick={() => void deliver()}>Retry counterpart delivery</Button></div> : null}
      {!selectedResolved && selected.status === "Drafted" && selected.targetJournalId ? <CounterpartRepricing key={selected.source.proposalId} proposal={selected} canManage={scope.capabilities.canManageLedger} onApplied={() => { setNotice("Counterpart repricing retained. Its previous review was cleared; obtain fresh independent journal approval before posting."); proposals.refresh(); breaks.refresh(); }} /> : null}
      {!selectedResolved && selected.status === "Drafted" ? <p className="text-xs text-muted-foreground">To resolve a rejected generated draft with an existing posted counterpart, retain the generated draft's rejection first. Linking verifies that rejection and the posted journal's exact reciprocal economics.</p> : null}
      {!selectedResolved ? <form className="space-y-3" onSubmit={(event) => void link(event)}><fieldset disabled={!scope.capabilities.canManageLedger || mutation.busy} className="space-y-3"><FormGrid columns={3}>
        <OperationField label="Existing counterpart book" value={targetBookId} onChange={setTargetBookId} required><option value="">Select target book</option>{scope.books.filter((book) => book.fundStructureNodeId === selected.source.counterpartyEntityId).map((book) => <option key={book.ledgerBookId} value={book.ledgerBookId}>{book.displayName}</option>)}</OperationField>
        <OperationField label="Existing counterpart journal ID" value={targetJournalId} onChange={setTargetJournalId} required />
        <OperationField label="Counterpart linking reason" value={reason} onChange={setReason} required />
      </FormGrid><Button type="submit" variant="outline">Validate and link existing counterpart</Button></fieldset></form> : null}
      {!scope.capabilities.canManageLedger ? <p className="text-xs text-muted-foreground">Ledger management permission is required to deliver or link a counterpart. Target-book authorization is checked separately.</p> : null}
    </section> : null}
    <MutationFeedback state={mutation} />
    <p role="status" aria-live="polite" className="text-sm text-muted-foreground">{notice}</p>
  </section>;
}
