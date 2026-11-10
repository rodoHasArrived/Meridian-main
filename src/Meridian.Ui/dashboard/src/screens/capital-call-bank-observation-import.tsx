import { useId, useState } from "react";
import { Link } from "react-router-dom";
import { Button } from "@/components/ui/button";
import { FormGrid, FormRow } from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import { importPrivateCapitalBankObservation } from "@/lib/api/private-capital.api";
import { WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";
import { capitalAmount, capitalIsPositive, type CapitalMutation } from "@/screens/private-capital-presenters";
import type { PrivateCapitalBankObservationImport, PrivateCapitalScope } from "@/types/private-capital";

export function CapitalBankObservationImport({ scope, locked, mutate, onImported }: {
  scope: PrivateCapitalScope; locked: boolean; mutate: CapitalMutation;
  onImported: (value: PrivateCapitalBankObservationImport) => void;
}) {
  const id = useId();
  const [evidenceLink, setEvidenceLink] = useState("");
  const [documentId, setDocumentId] = useState("");
  const [imported, setImported] = useState<PrivateCapitalBankObservationImport | null>(null);
  function importObservation() {
    const request = { evidenceLink: evidenceLink.trim(), documentId: documentId.trim() };
    if (!request.evidenceLink || !request.documentId) return;
    setImported(null);
    void mutate("Reviewed bank observation import", async signal => {
      const result = await importPrivateCapitalBankObservation(scope.ledgerBookId, request, { signal });
      const bank = result.bankTransaction;
      if (bank.entityId.toLowerCase() !== scope.entityId.toLowerCase() || bank.currency !== scope.currency
        || bank.transactionType !== "CapitalCallFunding" || bank.isVoided || !capitalIsPositive(bank.amount)) {
        throw new Error("The imported bank observation does not match this entity, currency or funding purpose. Inspect the retained source before using it.");
      }
      if (!signal.aborted) { setImported(result); onImported(result); }
      return result;
    }, { refresh: false });
  }
  return <section className="space-y-2 rounded border border-border p-3" aria-label="Import reviewed bank observation">
    <h4 className="text-sm font-semibold">Import reviewed bank observation</h4>
    <p className="text-xs text-muted-foreground">Retain the bank observation JSON as BankEvidence or BankStatement in <Link className="text-primary underline" to={WORKSTATION_ROUTE_CATALOG.reportingEvidence}>Evidence Vault</Link> and accept its review first. Import reads that source; it creates no journal and moves no cash.</p>
    <details className="text-xs"><summary>Reviewed source JSON format</summary>
      <pre className="mt-2 overflow-x-auto rounded bg-secondary/30 p-2">{JSON.stringify({ bankTransactionId: "<bank transaction GUID>", entityId: scope.entityId,
        amount: "<exact positive decimal>", currency: scope.currency, effectiveDate: "YYYY-MM-DD", transactionDate: "YYYY-MM-DD", settlementDate: "YYYY-MM-DD",
        direction: "Credit", statementReference: "<bank statement reference>", observedAtUtc: "YYYY-MM-DDTHH:mm:ssZ" }, null, 2)}</pre>
      <p className="mt-1">Use these fields only. Dates and amounts come from the reviewed bank source. Import preserves its identity and hash; conflicting source content requires review.</p>
      <p className="mt-1">Effective and settlement dates must match; transaction date cannot follow settlement. Amounts support up to four decimal places. Settlement cannot be in the future; the UTC observation must follow settlement and must not exceed the retained source timestamp.</p>
    </details>
    <form onSubmit={event => { event.preventDefault(); importObservation(); }}><fieldset disabled={locked} className="space-y-2"><legend className="sr-only">Accepted bank source identity</legend>
      <FormGrid><FormRow label="Bank observation Evidence Vault reference" labelFor={`${id}-evidence`}><Input id={`${id}-evidence`} value={evidenceLink}
        placeholder="evidence://evidence-vault/ev-…" onChange={event => { setEvidenceLink(event.target.value); setImported(null); }} /></FormRow>
        <FormRow label="Bank observation document ID" labelFor={`${id}-document`}><Input id={`${id}-document`} value={documentId}
          onChange={event => { setDocumentId(event.target.value); setImported(null); }} /></FormRow></FormGrid>
      <Button type="submit" variant="outline" disabled={!evidenceLink.trim() || !documentId.trim()}>Import reviewed bank observation</Button>
    </fieldset></form>
    {imported ? <div role="status" className="space-y-1 text-xs"><p>{imported.isReplay ? "Existing bank observation recovered." : "Bank observation retained."} Review the receipt match again before queueing any funding draft.</p>
      <p>Bank line {imported.bankTransaction.bankTransactionId} · {capitalAmount(imported.bankTransaction.amount, imported.bankTransaction.currency)} · Effective {imported.bankTransaction.effectiveDate}</p>
      <p className="break-all">Source SHA-256 {imported.sourceHashSha256}</p>
    </div> : null}
  </section>;
}
