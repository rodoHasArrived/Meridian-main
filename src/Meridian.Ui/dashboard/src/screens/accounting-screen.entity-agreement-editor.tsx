import { useState, type FormEvent } from "react";
import { Button } from "@/components/ui/button";
import { FormGrid, FormRow } from "@/components/ui/form";
import type { AgreementKind, AgreementPartyMapping, EntityOperationScope, IntercompanyAgreement, SignedAgreementDocument } from "@/types/entity-operations";
import { entityOperationEndpoints as endpoints } from "@/lib/api/entity-operations.api";
import { effectiveUtc, MutationFeedback, newIdentity, OperationField, today, useEntityMutation } from "./accounting-screen.entity-operations.shared";
import { agreementSignedEvidencePath, SignedAgreementEvidencePicker } from "./accounting-screen.entity-signed-evidence";
import { AgreementPartyAccounts } from "./accounting-screen.entity-party-accounts";
import { Link } from "react-router-dom";
import { Input } from "@/components/ui/input";

const kinds: AgreementKind[] = ["Loan", "CostRecharge", "ManagementFee", "ExpenseShare", "Guarantee"];
function emptyParty(): AgreementPartyMapping {
  return { entityId: "", ledgerBookId: "", receivableAccountNodeId: "", payableAccountNodeId: "", settlementAccountNodeId: null, revenueAccountNodeId: null, expenseAccountNodeId: null };
}
function emptyDocument(): SignedAgreementDocument {
  return { vaultId: "", documentId: "", artifactId: "", contentHashSha256: "", scope: "" };
}

export function AgreementEditor({ scope, agreement, onSaved, onCancel }: {
  scope: EntityOperationScope; agreement: IntercompanyAgreement | null;
  onSaved: (agreement: IntercompanyAgreement) => void; onCancel: () => void;
}) {
  const [agreementId] = useState(() => agreement?.agreementId ?? newIdentity());
  const [organizationId, setOrganizationId] = useState(agreement?.organizationId ?? scope.graph?.organizations[0]?.organizationId ?? "");
  const [kind, setKind] = useState<AgreementKind>(agreement?.kind ?? "Loan");
  const [reference, setReference] = useState(agreement?.facilityReference ?? "");
  const [currency, setCurrency] = useState(agreement?.currency ?? scope.book.baseCurrency);
  const [cadence, setCadence] = useState(agreement?.settlementCadence ?? "Monthly");
  const [dueDays, setDueDays] = useState(String(agreement?.settlementDueDays ?? ""));
  const [from, setFrom] = useState(agreement?.effectiveFrom.slice(0, 10) ?? today());
  const [to, setTo] = useState(agreement?.effectiveTo?.slice(0, 10) ?? "");
  const [firstRole, setFirstRole] = useState(agreement?.partyOneRole ?? "Lender");
  const [secondRole, setSecondRole] = useState(agreement?.partyTwoRole ?? "Borrower");
  const [parties, setParties] = useState<AgreementPartyMapping[]>(() => agreement
    ? [agreement.partyOneEntityId, agreement.partyTwoEntityId].map((entityId) => ({ ...emptyParty(), ...agreement.partyMappings.find((mapping) => mapping.entityId === entityId), entityId }))
    : [emptyParty(), emptyParty()]);
  const [accountingMapping, setAccountingMapping] = useState(agreement ? agreement.partyMappings.length > 0 : true);
  const [document, setDocument] = useState<SignedAgreementDocument>(agreement?.signedDocument ?? emptyDocument());
  const [reason, setReason] = useState("");
  const mutation = useEntityMutation();
  function updateParty(index: number, change: Partial<AgreementPartyMapping>) {
    setParties((current) => current.map((party, partyIndex) => index === partyIndex ? { ...party, ...change } : party));
  }
  async function save(event: FormEvent) {
    event.preventDefault();
    const signedDocument = Object.values(document).some(Boolean) ? document : null;
    const terms = { facilityReference: reference.trim(), partyOneRole: firstRole.trim(), partyTwoRole: secondRole.trim(),
      currency: currency.trim().toUpperCase(), settlementCadence: cadence.trim(),
      effectiveFrom: agreement && from === agreement.effectiveFrom.slice(0, 10) ? agreement.effectiveFrom : effectiveUtc(from),
      effectiveTo: agreement?.effectiveTo && to === agreement.effectiveTo.slice(0, 10) ? agreement.effectiveTo : to ? effectiveUtc(to) : null,
      partyMappings: kind === "Guarantee" && !accountingMapping ? [] : parties, signedDocument, settlementDueDays: dueDays ? Number(dueDays) : null };
    const command = agreement ? { ...terms, expectedVersion: agreement.version, reason: reason.trim() }
      : { ...terms, agreementId, organizationId, kind, partyOneEntityId: parties[0].entityId, partyTwoEntityId: parties[1].entityId };
    const body = { ...command, requestId: mutation.identity(command) };
    const result = await mutation.run<IntercompanyAgreement>(agreement ? `${endpoints.agreements}/${agreement.agreementId}/revisions` : endpoints.agreements,
      body, agreement ? "A successor terms revision was retained for review." : "Agreement draft retained. Submit it for agreement approval when its signed evidence is ready.");
    if (result) onSaved(result);
  }
  const signing = Object.values(document).some(Boolean);
  return <form onSubmit={(event) => void save(event)} className="space-y-4 rounded border border-border p-4">
    <h4 className="font-semibold">{agreement ? `Revise ${agreement.facilityReference}` : "New intercompany agreement"}</h4>
    <p className="text-sm text-muted-foreground">Terms are effective from the start date inclusive to the end date exclusive. Changes create a retained revision and require agreement approval.</p>
    <FormRow label="Agreement record ID" labelFor="agreement-record-identity" hint="Use this stable identity when linking signed evidence. A new agreement can be saved before signed evidence is attached."><Input id="agreement-record-identity" readOnly value={agreementId} onFocus={(event) => event.target.select()} /></FormRow>
    <fieldset disabled={mutation.busy || !scope.capabilities.canManageAgreements} className="space-y-4">
      <FormGrid columns={3}>
        <OperationField label="Agreement organization" value={organizationId} onChange={setOrganizationId} required disabled={Boolean(agreement)}>
          <option value="">Select organization</option>{scope.graph?.organizations.map((organization) => <option key={organization.organizationId} value={organization.organizationId}>{organization.name}</option>)}
        </OperationField>
        <OperationField label="Facility reference" value={reference} onChange={setReference} required />
        <OperationField label="Agreement kind" value={kind} onChange={(value) => { setKind(value as AgreementKind); setAccountingMapping(value !== "Guarantee"); }} required disabled={Boolean(agreement)}>
          {kinds.map((value) => <option value={value} key={value}>{value}</option>)}
        </OperationField>
        <OperationField label="Agreement currency" value={currency} onChange={setCurrency} pattern="[A-Za-z]{3}" required />
        <OperationField label="Settlement cadence" value={cadence} onChange={setCadence} required />
        <OperationField label="Settlement due after calendar days" value={dueDays} onChange={setDueDays} type="number" hint="Optional governed aging basis measured from each source economic date." />
        <OperationField label="Effective from" value={from} onChange={setFrom} type="date" required />
        <OperationField label="Effective to (exclusive)" value={to} onChange={setTo} type="date" />
        <OperationField label="First party role" value={firstRole} onChange={setFirstRole} required />
        <OperationField label="Second party role" value={secondRole} onChange={setSecondRole} required />
      </FormGrid>
      {kind === "Guarantee" ? <div className="space-y-2"><label className="flex items-center gap-2 text-sm"><input type="checkbox" checked={accountingMapping} onChange={(event) => setAccountingMapping(event.target.checked)} />Include accounting book mappings for this guarantee</label><p className="text-xs text-muted-foreground">An informational guarantee retains the two parties and signed terms without accounting book mappings.</p></div> : null}
      {parties.map((party, index) => <fieldset key={index} className="rounded border border-border p-3">
        <legend className="px-1 text-sm font-semibold">{index === 0 ? "First" : "Second"} party · book and stable account nodes</legend>
        <FormGrid columns={3}>
          <OperationField label={`Party ${index + 1} entity`} value={party.entityId} onChange={(value) => updateParty(index, { entityId: value, ledgerBookId: "" })} required disabled={Boolean(agreement)}>
            <option value="">Select entity</option>{scope.graph?.entities.map((entity) => <option key={entity.entityId} value={entity.entityId}>{entity.name} · {entity.lifecycleStatus}</option>)}
          </OperationField>
          {kind !== "Guarantee" || accountingMapping ? <><OperationField label={`Party ${index + 1} book`} value={party.ledgerBookId} onChange={(value) => updateParty(index, { ledgerBookId: value })} required>
            <option value="">Select entity book</option>{scope.books.filter((book) => book.fundStructureNodeId === party.entityId && book.accountingBasis === "Primary").map((book) => <option key={book.ledgerBookId} value={book.ledgerBookId}>{book.displayName} · {book.baseCurrency}</option>)}
          </OperationField>
          <AgreementPartyAccounts party={party} index={index} asOf={from} onChange={(patch) => updateParty(index, patch)} /></> : null}
        </FormGrid>
      </fieldset>)}
      <fieldset className="rounded border border-border p-3">
        <legend className="px-1 text-sm font-semibold">Signed agreement in Evidence Vault</legend>
        <p className="mb-3 text-xs text-muted-foreground">Select retained document and artifact identities. Approval verifies the exact tenant-owned bytes and SHA-256 hash.</p>
        <SignedAgreementEvidencePicker agreementId={agreementId} onSelect={setDocument} />
        {agreement ? <Link className="mb-3 inline-block text-sm underline underline-offset-4" to={agreementSignedEvidencePath(agreementId)}>Open Evidence Vault for this agreement</Link>
          : <p className="mb-3 text-xs text-muted-foreground">Save this agreement draft first. Its retained record then provides the Evidence Vault link for signed-document intake and review.</p>}
        <FormGrid columns={3}>
          {([ ["vaultId", "Vault ID"], ["documentId", "Signed document ID"], ["artifactId", "Signed artifact ID"], ["contentHashSha256", "Signed content SHA-256"], ["scope", "Signed evidence scope"] ] as const).map(([key, label]) => <OperationField
            key={key} label={label} value={document[key]} onChange={(value) => setDocument((current) => ({ ...current, [key]: value.trim() }))}
            required={signing} pattern={key === "contentHashSha256" ? "[a-fA-F0-9]{64}" : undefined} />)}
        </FormGrid>
      </fieldset>
      {agreement ? <OperationField label="Terms revision reason" value={reason} onChange={setReason} required /> : null}
      <div className="flex flex-wrap gap-2"><Button type="submit">{agreement ? "Retain successor revision" : "Save agreement draft"}</Button><Button type="button" variant="outline" onClick={onCancel}>Cancel editing</Button></div>
    </fieldset>
    <MutationFeedback state={mutation} />
  </form>;
}
