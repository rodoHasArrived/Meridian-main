import { useId, useState, type FormEvent, type InputHTMLAttributes } from "react";
import { Button } from "@/components/ui/button";
import { FormGrid, FormRow } from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import type { AgreementDraft, AgreementType } from "@/types/intercompany-agreements";

export const agreementTypes: readonly AgreementType[] = ["Loan", "ManagementFee", "CostShare", "CashSweep", "Guarantee", "Other"];
export const typeLabel = (value: string) => value.replace(/([a-z])([A-Z])/g, "$1 $2");
const roles: Record<AgreementType, readonly [string, string]> = {
  Loan: ["Lender", "Borrower"], ManagementFee: ["ServiceProvider", "Recipient"], CostShare: ["Participant", "Participant"],
  CashSweep: ["SweepSource", "Destination"], Guarantee: ["Guarantor", "SupportedEntity"], Other: ["PartyOne", "PartyTwo"]
};
export function newAgreementDraft(entityId: string, asOf: string): AgreementDraft {
  return { code: "", name: "", partyOneId: entityId, partyOneRole: "Lender", partyTwoId: "", partyTwoRole: "Borrower",
    agreementType: "Loan", currency: "", effectiveFrom: asOf, action: "Activate", terms: {}, evidence: [] };
}
function Field({ label, hint, ...props }: InputHTMLAttributes<HTMLInputElement> & { label: string; hint?: string }) {
  const id = useId();
  return <FormRow label={label} labelFor={id} hint={hint}><Input id={id} {...props} /></FormRow>;
}

interface Props {
  initial: AgreementDraft; fixedIdentity: boolean; busy: boolean;
  onSave: (draft: AgreementDraft, rationale: string) => void; onCancel: () => void;
}
/** FormData keeps unsaved decimal/date text intact when a request fails. */
export function IntercompanyAgreementEditor({ initial, fixedIdentity, busy, onSave, onCancel }: Props) {
  const id = useId();
  const [selectedType, setSelectedType] = useState(initial.agreementType);
  const [validationError, setValidationError] = useState<string | null>(null);
  function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    const text = (name: string) => String(data.get(name) ?? "").trim();
    const rate = text("fixedAnnualRate");
    const settlement = text("settlementDays");
    if (initial.action !== "Terminate" && rate && !/^-?(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+)$/.test(rate)) {
      setValidationError("Enter the annual rate as an exact decimal fraction."); return;
    }
    if (initial.action !== "Terminate" && settlement && (!Number.isSafeInteger(Number(settlement)) || Number(settlement) < 0 || Number(settlement) > 2147483647)) {
      setValidationError("Settlement days must be a nonnegative whole number within the supported range."); return;
    }
    setValidationError(null);
    const type = (fixedIdentity ? initial.agreementType : text("agreementType")) as AgreementType;
    const next: AgreementDraft = initial.action === "Terminate" ? { ...initial, terminationEffectiveDate: text("terminationEffectiveDate"),
      evidence: [...(text("evidenceReference") ? [{ reference: text("evidenceReference"), artifactId: text("artifactId") || null }] : []), ...initial.evidence?.slice(1) ?? []] }
      : { ...initial, code: fixedIdentity ? initial.code : text("code"), name: text("name"),
        partyOneId: fixedIdentity ? initial.partyOneId : text("partyOneId"),
        partyTwoId: fixedIdentity ? initial.partyTwoId : text("partyTwoId"), agreementType: type,
        partyOneRole: fixedIdentity ? initial.partyOneRole : type === "Other" ? text("partyOneRole") || roles[type][0] : roles[type][0],
        partyTwoRole: fixedIdentity ? initial.partyTwoRole : type === "Other" ? text("partyTwoRole") || roles[type][1] : roles[type][1],
        currency: fixedIdentity ? initial.currency : text("currency").toUpperCase(), effectiveFrom: text("effectiveFrom"), effectiveTo: text("effectiveTo") || null,
        purpose: text("purpose"), terms: { fixedAnnualRate: rate || null, rateDescription: text("rateDescription"),
          scheduleDescription: text("scheduleDescription"), allocationDescription: text("allocationDescription"),
          settlementDays: settlement ? Number(settlement) : null, notes: text("notes") },
        evidence: [...(text("evidenceReference") ? [{ reference: text("evidenceReference"), artifactId: text("artifactId") || null }] : []), ...initial.evidence?.slice(1) ?? []] };
    onSave(next, text("rationale"));
  }
  const evidence = initial.evidence?.[0];
  return <form onSubmit={save} aria-label={`${initial.action === "Activate" ? "Create" : typeLabel(initial.action)} agreement draft`} className="space-y-4 rounded-md border border-border p-4">
    <h3 className="font-semibold">{initial.action === "Activate" ? "Agreement draft" : `${typeLabel(initial.action)} agreement`}</h3>
    {validationError ? <p role="alert" className="text-sm text-danger">{validationError}</p> : null}
    {initial.action === "Terminate" ? <Field label="Termination effective date" name="terminationEffectiveDate" type="date" required defaultValue={initial.terminationEffectiveDate ?? initial.effectiveFrom} /> : <>
      <FormGrid columns={3}>
        <Field label="Agreement code" name="code" required defaultValue={initial.code} disabled={fixedIdentity} />
        <Field label="Agreement name" name="name" required defaultValue={initial.name} />
        <FormRow label="Agreement type" labelFor={`${id}-type`}><Select id={`${id}-type`} name="agreementType" value={selectedType} onChange={event => setSelectedType(event.target.value as AgreementType)} disabled={fixedIdentity}>{agreementTypes.map(type => <option key={type} value={type}>{typeLabel(type)}</option>)}</Select></FormRow>
        <Field label="First legal entity" name="partyOneId" required defaultValue={initial.partyOneId} disabled={fixedIdentity} />
        <Field label="Second legal entity" name="partyTwoId" required defaultValue={initial.partyTwoId} disabled={fixedIdentity} />
        <Field label="Agreement currency" name="currency" maxLength={3} required defaultValue={initial.currency} disabled={fixedIdentity} hint="Declared three-letter transaction currency." />
        {selectedType === "Other" ? <><Field label="First party role" name="partyOneRole" required defaultValue={initial.partyOneRole} disabled={fixedIdentity} />
          <Field label="Second party role" name="partyTwoRole" required defaultValue={initial.partyTwoRole} disabled={fixedIdentity} /></> : null}
        <Field label="Effective from" name="effectiveFrom" type="date" required readOnly={initial.action === "Amend"} defaultValue={initial.effectiveFrom} hint={initial.action === "Amend" ? "Choose the intended effective date in the as-of filter before drafting an amendment." : undefined} />
        <Field label="Effective to (exclusive)" name="effectiveTo" type="date" defaultValue={initial.effectiveTo ?? ""} />
        <Field label="Annual fixed rate (fraction)" name="fixedAnnualRate" inputMode="decimal" pattern="-?(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+)" defaultValue={initial.terms?.fixedAnnualRate ?? ""} hint="0.05 means 5%. Descriptive terms do not accrue journals." />
        <Field label="Settlement days" name="settlementDays" type="number" min="0" step="1" defaultValue={initial.terms?.settlementDays ?? ""} hint="Calendar days." />
        <Field label="Rate description" name="rateDescription" defaultValue={initial.terms?.rateDescription ?? ""} />
        <Field label="Schedule description" name="scheduleDescription" defaultValue={initial.terms?.scheduleDescription ?? ""} />
        <Field label="Allocation description" name="allocationDescription" defaultValue={initial.terms?.allocationDescription ?? ""} />
        <Field label="Purpose" name="purpose" defaultValue={initial.purpose ?? ""} />
        <Field label="Terms notes" name="notes" defaultValue={initial.terms?.notes ?? ""} />
      </FormGrid>
      <p className="text-sm text-muted-foreground">Party roles: {fixedIdentity ? `${initial.partyOneRole} and ${initial.partyTwoRole}` : roles[selectedType].map(typeLabel).join(" and ")}. {fixedIdentity ? "Approved party identities, roles, type and currency remain fixed." : "These roles follow the selected agreement type."}</p>
    </>}
    <FormGrid columns={3}>
      <Field label="Retained evidence reference" name="evidenceReference" defaultValue={evidence?.reference ?? ""} hint="Select the canonical evidence://evidence-vault/ reference from retained evidence." />
      <Field label="Retained artifact identifier" name="artifactId" defaultValue={evidence?.artifactId ?? ""} />
      <Field label="Change rationale" name="rationale" required hint="Required for retrospective changes and review decisions." />
    </FormGrid>
    <p className="text-sm text-muted-foreground">Saving retains a draft. Submission and independent approval are separate actions.</p>
    <div className="flex flex-wrap gap-2"><Button type="submit" disabled={busy}>Save agreement draft</Button><Button type="button" variant="outline" disabled={busy} onClick={onCancel}>Discard local changes</Button></div>
  </form>;
}
