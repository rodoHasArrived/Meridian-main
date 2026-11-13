import { useState, type FormEvent } from "react";
import { Link } from "react-router-dom";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { FormGrid, FormRow } from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import { WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";
import { entityOperationEndpoints as endpoints } from "@/lib/api/entity-operations.api";
import { intercompanyAgreementsEndpoint } from "@/lib/api/intercompany-agreements.api";
import type { AgreementDetail, AgreementList, AgreementRevision } from "@/types/intercompany-agreements";
import type { AccountingArrangementPartyMapping, AccountingArrangementRecord, AccountingArrangementTemplate, EntityOperationScope, IntercompanyAccountingArrangement } from "@/types/entity-operations";
import { effectiveUtc, MutationFeedback, newIdentity, OperationError, OperationField, today, useEntityData, useEntityMutation } from "./accounting-screen.entity-operations.shared";
import { AgreementPartyAccounts } from "./accounting-screen.entity-party-accounts";

const templates: AccountingArrangementTemplate[] = ["Loan", "CostRecharge", "ManagementFee", "ExpenseShare", "CashSweep", "Guarantee", "Manual"];
const emptyParty = (entityId = ""): AccountingArrangementPartyMapping => ({ entityId, ledgerBookId: "", receivableAccountNodeId: "", payableAccountNodeId: "", settlementAccountNodeId: null, revenueAccountNodeId: null, expenseAccountNodeId: null });
export const canonicalAgreementPath = (agreementId?: string, bookId?: string) => `${WORKSTATION_ROUTE_CATALOG.accountingConfigure}?${new URLSearchParams({ ...(agreementId ? { agreementId } : {}), ...(bookId ? { ledgerBookId: bookId } : {}) })}#configure-section-agreements`;

export function AccountingArrangementEditor({ scope, arrangement, initialOrganizationId, onSaved, onCancel }: {
  scope: EntityOperationScope; arrangement: IntercompanyAccountingArrangement | null;
  initialOrganizationId?: string;
  onSaved: (arrangement: AccountingArrangementRecord) => void; onCancel: () => void;
}) {
  const [arrangementId] = useState(() => arrangement?.arrangementId ?? newIdentity());
  const [organizationId, setOrganizationId] = useState(arrangement?.organizationId ?? initialOrganizationId ?? scope.graph?.organizations[0]?.organizationId ?? "");
  const [template, setTemplate] = useState<AccountingArrangementTemplate>(arrangement?.templateKind ?? "Loan");
  const [reference, setReference] = useState(arrangement?.arrangementReference ?? "");
  const [canonicalId, setCanonicalId] = useState(arrangement?.canonicalAgreementId ?? "");
  const [canonicalVersion, setCanonicalVersion] = useState(arrangement ? String(arrangement.canonicalAgreementVersion) : "");
  const [cursor, setCursor] = useState("");
  const [from, setFrom] = useState(arrangement?.effectiveFrom.slice(0, 10) ?? today());
  const [to, setTo] = useState(arrangement?.effectiveTo?.slice(0, 10) ?? "");
  const [parties, setParties] = useState<AccountingArrangementPartyMapping[]>(arrangement?.partyMappings.length ? arrangement.partyMappings : [emptyParty(), emptyParty()]);
  const [accountingMapping, setAccountingMapping] = useState(arrangement ? arrangement.partyMappings.length > 0 : true);
  const [reason, setReason] = useState("");
  const mutation = useEntityMutation();
  const query = new URLSearchParams({ asOf: from || today(), ...(cursor ? { cursor } : {}) });
  const legalList = useEntityData<AgreementList>(`${intercompanyAgreementsEndpoint}?${query}`);
  const legal = useEntityData<AgreementDetail>(canonicalId ? `${intercompanyAgreementsEndpoint}/${encodeURIComponent(canonicalId)}?${new URLSearchParams({ asOf: from || today() })}` : null);
  const revisions = legal.data?.revisions.filter((revision) => revision.action !== "Terminate") ?? [];
  const chosen = revisions.find((revision) => revision.agreementVersion === Number(canonicalVersion));
  const entityName = (id: string) => scope.graph?.entities.find((entity) => entity.entityId === id)?.name ?? id;

  function chooseRevision(value: string) {
    setCanonicalVersion(value);
    const revision = revisions.find((item) => item.agreementVersion === Number(value));
    if (!revision) return;
    setParties((current) => [revision.terms.partyOneId, revision.terms.partyTwoId].map((entityId) => current.find((party) => party.entityId === entityId) ?? emptyParty(entityId)));
    if (!arrangement) { setFrom(revision.terms.effectiveFrom); setTo(revision.terms.effectiveTo ?? ""); }
  }
  function updateParty(entityId: string, change: Partial<AccountingArrangementPartyMapping>) {
    setParties((current) => current.some((party) => party.entityId === entityId) ? current.map((party) => party.entityId === entityId ? { ...party, ...change } : party) : [...current.filter((party) => party.entityId), { ...emptyParty(entityId), ...change }]);
  }
  async function save(event: FormEvent) {
    event.preventDefault();
    if (!chosen || (arrangement && !reason.trim())) return;
    const mappings = template === "Guarantee" && !accountingMapping ? [] : [chosen.terms.partyOneId, chosen.terms.partyTwoId].map((entityId) => parties.find((party) => party.entityId === entityId) ?? emptyParty(entityId));
    const configuration = { arrangementReference: reference.trim(), templateKind: template,
      canonicalAgreementVersion: chosen.agreementVersion, canonicalPayloadHash: chosen.payloadHash,
      effectiveFrom: arrangement && from === arrangement.effectiveFrom.slice(0, 10) ? arrangement.effectiveFrom : effectiveUtc(from),
      effectiveTo: arrangement?.effectiveTo && to === arrangement.effectiveTo.slice(0, 10) ? arrangement.effectiveTo : to ? effectiveUtc(to) : null,
      partyMappings: mappings };
    const command = arrangement ? { ...configuration, expectedVersion: arrangement.version, reason: reason.trim() }
      : { ...configuration, arrangementId, organizationId, canonicalAgreementId: chosen.agreementId };
    const result = await mutation.run<AccountingArrangementRecord>(arrangement ? `${endpoints.accountingArrangements}/${arrangement.arrangementId}/revisions` : endpoints.accountingArrangements,
      { ...command, requestId: mutation.identity(command) }, arrangement ? "A successor accounting profile was retained for mapping review." : "Accounting profile draft retained against the selected approved legal version.");
    if (result) onSaved(result);
  }

  return <form onSubmit={(event) => void save(event)} className="space-y-4 rounded border border-border p-4" aria-label="Accounting profile editor">
    <h4 className="font-semibold">{arrangement ? `Revise ${arrangement.arrangementReference}` : "New intercompany accounting profile"}</h4>
    <p className="text-sm text-muted-foreground">Select an approved legal agreement version, then configure its books, accounts and counterpart template. Legal parties, currency, dates and signed evidence come from that exact retained version.</p>
    <Button asChild variant="ghost" size="sm" className="underline underline-offset-4"><Link to={canonicalAgreementPath(canonicalId, scope.book.ledgerBookId)}>Open canonical legal agreement registry</Link></Button>
    <FormRow label="Accounting profile ID" labelFor="accounting-profile-identity"><Input id="accounting-profile-identity" readOnly value={arrangementId} onFocus={(event) => event.target.select()} /></FormRow>
    <fieldset disabled={mutation.busy || !scope.capabilities.canManageAgreements} className="space-y-4"><FormGrid columns={3}>
      <OperationField label="Accounting profile organization" value={organizationId} onChange={setOrganizationId} required disabled={Boolean(arrangement)}><option value="">Select organization</option>{scope.graph?.organizations.map((organization) => <option key={organization.organizationId} value={organization.organizationId}>{organization.name}</option>)}</OperationField>
      <OperationField label="Accounting profile reference" value={reference} onChange={setReference} required />
      <OperationField label="Counterpart accounting template" value={template} onChange={(value) => { setTemplate(value as AccountingArrangementTemplate); setAccountingMapping(value !== "Guarantee"); }} required>{templates.map((value) => <option key={value}>{value}</option>)}</OperationField>
      <OperationField label="Canonical legal agreement" value={canonicalId} onChange={(value) => { setCanonicalId(value); setCanonicalVersion(""); setParties([emptyParty(), emptyParty()]); }} required disabled={Boolean(arrangement)}><option value="">Select approved legal agreement</option>
        {canonicalId && !legalList.data?.items.some((item) => item.agreementId === canonicalId) ? <option value={canonicalId}>{legal.data?.code ?? canonicalId} · retained selection</option> : null}
        {legalList.data?.items.filter((item) => item.revisions.some((revision) => revision.action !== "Terminate")).map((item) => <option key={item.agreementId} value={item.agreementId}>{item.code} · {item.name} · {item.effectiveStatus}</option>)}
      </OperationField>
      <OperationField label="Approved legal version" value={canonicalVersion} onChange={chooseRevision} required disabled={!canonicalId || legal.loading}><option value="">Select exact approved version</option>{revisions.map((revision) => <option key={revision.agreementVersion} value={revision.agreementVersion}>Version {revision.agreementVersion} · {revision.terms.effectiveFrom} · {revision.approver}</option>)}</OperationField>
      <OperationField label="Mapping effective from" value={from} onChange={setFrom} type="date" required />
      <OperationField label="Mapping effective to (exclusive)" value={to} onChange={setTo} type="date" />
    </FormGrid>
    <OperationError error={legalList.error} /><OperationError error={legal.error} />
    {legal.loading || legalList.loading ? <p role="status" className="text-sm">Loading approved canonical legal authority…</p> : null}
    {legalList.data?.nextCursor ? <Button type="button" variant="outline" onClick={() => setCursor(legalList.data!.nextCursor!)}>Next legal agreement page</Button> : null}
    {cursor ? <Button type="button" variant="outline" onClick={() => setCursor("")}>First legal agreement page</Button> : null}
    {chosen ? <CanonicalTerms revision={chosen} entityName={entityName} /> : <p role="status" className="text-sm text-muted-foreground">An exact approved legal version is required before this accounting profile can be saved.</p>}
    {template === "Guarantee" ? <Checkbox checked={accountingMapping} onCheckedChange={setAccountingMapping} label="Include accounting book mappings for this guarantee" /> : null}
    {chosen && (template !== "Guarantee" || accountingMapping) ? [chosen.terms.partyOneId, chosen.terms.partyTwoId].map((entityId, index) => {
      const party = parties.find((item) => item.entityId === entityId) ?? emptyParty(entityId);
      return <fieldset key={entityId} className="rounded border border-border p-3"><legend className="px-1 text-sm font-semibold">{entityName(entityId)} · {index === 0 ? chosen.terms.partyOneRole : chosen.terms.partyTwoRole}</legend>
        <FormGrid columns={3}><OperationField label={`Party ${index + 1} book`} value={party.ledgerBookId} onChange={(value) => updateParty(entityId, { ledgerBookId: value })} required><option value="">Select entity book</option>{scope.books.filter((book) => book.fundStructureNodeKind === "Entity" && book.fundStructureNodeId === entityId).map((book) => <option key={book.ledgerBookId} value={book.ledgerBookId}>{book.displayName} · {book.accountingBasis} · {book.baseCurrency}</option>)}</OperationField>
          <AgreementPartyAccounts party={party} index={index} asOf={from} onChange={(change) => updateParty(entityId, change)} />
        </FormGrid></fieldset>;
    }) : null}
    {arrangement ? <OperationField label="Accounting profile revision reason" value={reason} onChange={setReason} required /> : null}
    <div className="flex flex-wrap gap-2"><Button type="submit" disabled={!chosen || legal.loading || (Boolean(arrangement) && !reason.trim())} disabledReason={legal.loading ? "Wait for the approved legal version to load." : !chosen ? "Select an exact approved legal agreement version." : "Enter a reason for this accounting profile revision."}>Save accounting profile draft</Button><Button type="button" variant="outline" onClick={onCancel}>Cancel profile editing</Button></div>
    </fieldset><MutationFeedback state={mutation} />
  </form>;
}

function CanonicalTerms({ revision, entityName }: { revision: AgreementRevision; entityName: (id: string) => string }) {
  const terms = revision.terms;
  return <section className="space-y-2 rounded border border-border bg-muted/20 p-3" aria-label="Pinned legal terms"><h5 className="text-sm font-semibold">Approved legal version {revision.agreementVersion} · {terms.code}</h5>
    <p className="text-sm">{entityName(terms.partyOneId)} ({terms.partyOneRole}) ↔ {entityName(terms.partyTwoId)} ({terms.partyTwoRole}) · {terms.currency}</p>
    <p className="text-xs">Legal window {terms.effectiveFrom} → {terms.effectiveTo ?? "Open"} (exclusive) · {terms.terms?.scheduleDescription ?? "No settlement cadence specified"} · {terms.terms?.settlementDays == null ? "No due-day policy specified" : `${terms.terms.settlementDays} calendar days`}</p>
    <p className="break-all font-mono text-xs">Approved payload {revision.payloadHash}</p><p className="text-xs">Approved by {revision.approver} · {revision.approvedAtUtc} · {terms.evidence?.length ?? 0} canonical evidence references</p>
  </section>;
}
