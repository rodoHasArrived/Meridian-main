import { useState, type FormEvent } from "react";
import { Button } from "@/components/ui/button";
import { FormGrid } from "@/components/ui/form";
import { TechnicalDetails } from "@/components/ui/technical-details";
import { entityOperationEndpoints as endpoints } from "@/lib/api/entity-operations.api";
import type { EntityOperationScope, IntercompanyAccountingArrangement, IntercompanyAccountBinding } from "@/types/entity-operations";
import type { LegacyAttribution, LegacyAttributionCandidate } from "@/types/intercompany-obligations";
import { EntityOperationTable, MutationFeedback, OperationError, OperationField, useEntityData, useEntityMutation } from "./accounting-screen.entity-operations.shared";

/** Retains reviewed native sidecar evidence; source amounts and legal facts are never re-keyed. */
export function LegacyIntercompanyCoverageReview({ scope, asOf, onSaved }: { scope: EntityOperationScope; asOf: string; onSaved: () => void }) {
  const base = `/api/ledger/intercompany/legacy-attributions`;
  const bookQuery = new URLSearchParams({ ledgerBookId: scope.book.ledgerBookId });
  const candidates = useEntityData<LegacyAttributionCandidate[]>(`${base}/candidates?${bookQuery}`);
  const history = useEntityData<LegacyAttribution[]>(`${base}?${bookQuery}`);
  const [sourceKey, setSourceKey] = useState("");
  const [arrangementId, setArrangementId] = useState("");
  const [bindingId, setBindingId] = useState("");
  const [transactionId, setTransactionId] = useState("");
  const [preparedBy, setPreparedBy] = useState("");
  const [evidence, setEvidence] = useState("");
  const [rationale, setRationale] = useState("");
  const [operatorReviewed, setOperatorReviewed] = useState(false);
  const [observedAt] = useState(() => new Date().toISOString());
  const mutation = useEntityMutation();
  const keyOf = (source: LegacyAttributionCandidate) => `${source.journalEntryId}:${source.entryId}`;
  const source = candidates.data?.find((item) => keyOf(item) === sourceKey);
  const sourceDate = source?.effectiveDate ?? asOf;
  const profiles = useEntityData<IntercompanyAccountingArrangement[]>(source ? `${endpoints.accountingArrangements}?${new URLSearchParams({ bookId: scope.book.ledgerBookId, asOf: sourceDate })}` : null);
  const bindings = useEntityData<IntercompanyAccountBinding[]>(source ? endpoints.bindings(scope.book.ledgerBookId, sourceDate) : null);
  const approvedProfiles = profiles.data?.filter((item) => item.status === "Approved" && item.partyMappings.some((mapping) => mapping.ledgerBookId === scope.book.ledgerBookId)) ?? [];
  const arrangement = approvedProfiles.find((item) => item.revisionId === arrangementId);
  const account = bindings.data?.find((item) => item.revisionId === bindingId);
  const previous = source ? history.data?.filter((item) => item.journalEntryId === source.journalEntryId && item.lineId === source.entryId).sort((a, b) => b.revision - a.revision)[0] : undefined;
  async function retain(event: FormEvent) {
    event.preventDefault(); if (!source || !arrangement || !account) return;
    const payload = { revision: (previous?.revision ?? 0) + 1, ledgerBookId: scope.book.ledgerBookId, journalEntryId: source.journalEntryId,
      lineId: source.entryId, sourceLineDigest: source.sourceLineDigest, accountingArrangementId: arrangement.arrangementId,
      accountingArrangementRevisionId: arrangement.revisionId, accountBindingRevisionId: account.revisionId, chartAccountNodeId: account.chartAccountNodeId,
      intercompanyTransactionId: transactionId.trim(), preparedBy: operatorReviewed ? "" : preparedBy.trim(), approvedBy: "", approvedAt: observedAt,
      evidenceReference: evidence.trim(), tenantId: "", companyId: null, previousAttributionId: previous?.attributionId ?? null,
      intercompanyAgreementId: arrangement.canonicalAgreementId, intercompanyAgreementVersion: arrangement.canonicalAgreementVersion,
      intercompanyAgreementPayloadHash: arrangement.canonicalPayloadHash, reviewMode: operatorReviewed ? "OperatorReviewed" : "Independent", reviewRationale: rationale.trim() };
    const result = await mutation.run<LegacyAttribution>(base, { attribution: { ...payload, attributionId: mutation.identity(payload) }, expectedPreviousAttributionId: previous?.attributionId ?? null }, "Reviewed coverage evidence retained. Posted journals remain unchanged; rerun obligation monitoring.");
    if (result) { history.refresh(); onSaved(); }
  }
  async function revoke() {
    if (!previous || previous.revoked || !rationale.trim() || !evidence.trim()) return;
    const payload = { ...previous, attributionId: undefined, revision: previous.revision + 1, previousAttributionId: previous.attributionId,
      revoked: true, preparedBy: operatorReviewed ? "" : preparedBy.trim() || previous.preparedBy, approvedBy: "", approvedAt: observedAt,
      evidenceReference: evidence.trim(), reviewRationale: rationale.trim(), reviewMode: operatorReviewed ? "OperatorReviewed" : "Independent" };
    const result = await mutation.run<LegacyAttribution>(base, { attribution: { ...payload, attributionId: mutation.identity(payload) }, expectedPreviousAttributionId: previous.attributionId },
      "Coverage revoked through retained successor evidence. Posted journals and earlier review history remain unchanged.");
    if (result) { history.refresh(); onSaved(); }
  }
  return <TechnicalDetails label="Review legacy posted-line agreement coverage"><div className="space-y-4"><p className="text-sm text-muted-foreground">Native accounting profiles and account-role revisions bind an exact immutable legacy line to approved legal terms. Corrections append successors; they never alter posted journals.</p>
    <OperationError error={candidates.error} /><OperationError error={profiles.error} /><OperationError error={bindings.error} /><OperationError error={history.error} />
    {candidates.loading || profiles.loading || bindings.loading || history.loading ? <p role="status">Loading retained source and coverage authority…</p> : null}
    <form onSubmit={(event) => void retain(event)} className="space-y-3"><fieldset disabled={mutation.busy} className="space-y-3"><FormGrid columns={3}>
      <OperationField label="Legacy posted source line" value={sourceKey} onChange={(value) => { setSourceKey(value); setArrangementId(""); setBindingId(""); setTransactionId(""); setEvidence(""); setRationale(""); }} required><option value="">Select retained posted line</option>{candidates.data?.map((item) => <option key={keyOf(item)} value={keyOf(item)}>{item.effectiveDate} · {item.accountName} · Dr {item.debit} / Cr {item.credit} · {item.entryId}</option>)}</OperationField>
      <OperationField label="Approved coverage accounting profile" value={arrangementId} onChange={setArrangementId} required><option value="">Select approved accounting revision</option>{approvedProfiles.map((item) => <option key={item.revisionId} value={item.revisionId}>{item.arrangementReference} · revision {item.revisionNumber} · legal v{item.canonicalAgreementVersion}</option>)}</OperationField>
      <OperationField label="Retained coverage account role" value={bindingId} onChange={setBindingId} required><option value="">Select retained account revision</option>{bindings.data?.filter((item) => !source || item.accountName === source.accountName || item.historicalNames.includes(source.accountName)).map((item) => <option key={item.revisionId} value={item.revisionId}>{item.accountPath} · {item.role} · revision {item.revisionNumber}</option>)}</OperationField>
      <OperationField label="Retained intercompany transaction reference" value={transactionId} onChange={setTransactionId} required />
      {!operatorReviewed ? <OperationField label="Original preparing operator" value={preparedBy} onChange={setPreparedBy} required hint="Independent review requires a different authenticated approving operator." /> : null}
      <OperationField label="Coverage review evidence reference" value={evidence} onChange={setEvidence} required />
      <OperationField label="Coverage review rationale" value={rationale} onChange={setRationale} required />
    </FormGrid><label className="flex items-center gap-2 text-sm"><input type="checkbox" checked={operatorReviewed} onChange={(event) => setOperatorReviewed(event.target.checked)} />Operator-reviewed coverage: the authenticated preparer may also review</label>
    <p className="text-sm text-muted-foreground">{operatorReviewed ? "This decision is recorded as operator-reviewed, with your authenticated identity and retained rationale and evidence." : "Independent approval remains the default. The server supplies authenticated approving identity and tenant/company scope."}</p>
    {source ? <p className="break-all text-sm text-muted-foreground">Exact posted source {source.journalEntryId} / {source.entryId} · SHA-256 {source.sourceLineDigest} · successor revision {(previous?.revision ?? 0) + 1}</p> : null}
    <Button type="submit" variant="outline" disabled={!source || !arrangement || !account || !history.data || !rationale.trim() || !evidence.trim() || !transactionId.trim() || (!operatorReviewed && !preparedBy.trim())}>Retain reviewed coverage successor</Button> <Button type="button" variant="outline" disabled={!previous || previous.revoked || !rationale.trim() || !evidence.trim()} onClick={() => void revoke()}>Revoke selected coverage</Button></fieldset><MutationFeedback state={mutation} /></form>
    <EntityOperationTable label="Retained legacy coverage history" rows={history.data ?? []} getRowId={(item) => item.attributionId} emptyText="No legacy attribution evidence is retained for this book." columns={[
      { id: "source", label: "Posted line", rowHeader: true, render: (item) => item.lineId },
      { id: "revision", label: "Coverage revision", render: (item) => `${item.revision}${item.revoked ? " · Revoked" : ""}` },
      { id: "legal", label: "Legal agreement version", render: (item) => item.intercompanyAgreementVersion },
      { id: "review", label: "Review provenance", render: (item) => `${item.reviewMode === "OperatorReviewed" ? "Operator-reviewed" : "Independent"} · ${item.approvedBy} · ${item.approvedAt}` },
      { id: "evidence", label: "Retained rationale and evidence", render: (item) => <>{item.reviewRationale ?? "Legacy independent review"}<p className="break-all">{item.evidenceReference}</p></> }
    ]} />
  </div></TechnicalDetails>;
}
