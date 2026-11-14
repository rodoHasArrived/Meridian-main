import { PREPARER_ATTESTATION_DISCLOSURE } from "@/lib/single-principal-attestation-api";
import { CASH_DIVIDEND_ATTESTATION_POLICY } from "@/lib/cash-dividend-attestation-api";
import type { AccountingPreparerAttestationDisclosure, LedgerAmountProof } from "@/types/ledger-amount-proof";

const boundary = "This accounting decision is separate from ordinary recorded approval. It does not satisfy the ordinary-approval requirement for Verified Coverage.";

export function AccountingPreparerAttestation({ proof, sourceJournalEntryIds, inline = false }: {
  proof: LedgerAmountProof; sourceJournalEntryIds?: string[]; inline?: boolean;
}) {
  const entries = proof.accountingPreparerAttestations ?? [];
  if (!entries.length && proof.ordinaryRecordedApprovalSatisfied !== false) return null;
  const classified = entries.map(entry => ({ entry, known: matchesAmount(entry, proof, sourceJournalEntryIds) }));
  if (inline) return <span role="group" aria-label="Accounting preparer decision" className="mt-3 block space-y-1 border-t border-border/70 pt-2 text-sm">
    <span className="block font-semibold">Accounting review classification</span>
    {classified.some(item => item.known) ? <span className="block font-semibold">{PREPARER_ATTESTATION_DISCLOSURE}</span> : null}
    {!classified.length || classified.some(item => !item.known) ? <span className="block">Accounting review classification unavailable.</span> : null}
    <span className="block text-muted-foreground">{boundary}</span>
  </span>;
  return <section aria-label="Accounting preparer decision" className="mt-3 space-y-2 rounded-md border border-border/70 bg-secondary/10 p-3 text-sm">
    <h4 className="font-semibold">Accounting review classification</h4>
    <p className="text-muted-foreground">{boundary}</p>
    {!classified.length ? <p role="status">Accounting review classification unavailable. Ordinary recorded approval is not established.</p> : null}
    {classified.map(({ entry, known }, index) => <div key={`${entry.attestationId ?? "unknown"}-${index}`} className="space-y-2 rounded-md border border-border/60 p-2">
      <p className="font-semibold">{known ? PREPARER_ATTESTATION_DISCLOSURE : "Accounting review classification unavailable"}</p>
      {known ? <dl className="grid gap-2 text-xs">
        {Object.entries({ Attester: entry.actor!, Recorded: entry.recordedAt!, Attestation: entry.attestationId!,
          "Native case": entry.caseId ?? "Unavailable", Projection: entry.projectionId ?? "Unavailable",
          Declaration: `${entry.declarationId} / revision ${entry.declarationRevision}`,
          Policy: `${entry.policyVersion} / ${entry.policyHash}`, "Contributing journals": entry.journalEntryIds.join(", ") }).map(([label, value]) =>
          <div key={label}><dt className="font-semibold">{label}</dt><dd className="break-all font-mono">{value}</dd></div>)}
      </dl> : null}
      {entry.validationIssues?.length ? <ul className="list-disc space-y-1 pl-4" aria-label="Accounting decision validation issues">{entry.validationIssues.map((issue, issueIndex) => <li key={issueIndex}>{issue}</li>)}</ul> : null}
    </div>)}
  </section>;
}

function matchesAmount(entry: AccountingPreparerAttestationDisclosure, proof: LedgerAmountProof, sourceJournalEntryIds?: string[]) {
  const source = entry.sourceScope;
  const scope = proof.scope;
  const report = proof.subjectId.startsWith("report:");
  const knownJournals = new Set(sourceJournalEntryIds?.length ? sourceJournalEntryIds
    : report ? proof.evidence.flatMap(item => item.sourceSubjectId ? [item.sourceSubjectId.split(":")[0]!] : [])
      : [proof.subjectId.split(":")[0]!]);
  return entry.schemaVersion === 1 && entry.classification === "PreparerAttestation"
    && entry.disclosure === PREPARER_ATTESTATION_DISCLOSURE && entry.policyVersion === CASH_DIVIDEND_ATTESTATION_POLICY
    && Boolean(entry.attestationId && entry.caseId && entry.projectionId && entry.eventId
      && entry.declarationId && entry.actor?.trim() && entry.recordedAt)
    && Number.isFinite(Date.parse(entry.recordedAt!)) && Number.isInteger(entry.declarationRevision) && entry.declarationRevision! > 0
    && Number.isInteger(entry.eventVersion) && entry.eventVersion! > 0
    && Boolean(entry.policyHash && /^[a-f0-9]{64}$/.test(entry.policyHash))
    && Boolean(entry.draftedCandidateFingerprint && /^[a-f0-9]{64}$/.test(entry.draftedCandidateFingerprint))
    && Boolean(entry.postingIntentHash && /^[a-f0-9]{64}$/.test(entry.postingIntentHash))
    && source?.tenantId === scope.tenantId && source.companyId === scope.companyId
    && source.fundProfileId === scope.fundProfileId && source.ledgerBookId === scope.ledgerBookId
    && (report || source.periodId === scope.periodId)
    && Array.isArray(entry.journalEntryIds) && entry.journalEntryIds.length > 0
    && entry.journalEntryIds.every(id => knownJournals.has(id))
    && (entry.validationIssues?.length ?? 0) === 0;
}
