import { useId, type InputHTMLAttributes } from "react";
import { Button } from "@/components/ui/button";
import { FormGrid, FormRow } from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import type { AgreementType, StructuredAgreementTerms } from "@/types/intercompany-agreements";

type Schedule = NonNullable<StructuredAgreementTerms["schedule"]>;
function Field({ label, hint, ...props }: InputHTMLAttributes<HTMLInputElement> & { label: string; hint?: string }) {
  const id = useId();
  return <FormRow label={label} labelFor={id} hint={hint}><Input id={id} autoComplete="off" {...props} /></FormRow>;
}
const decimal = { inputMode: "decimal" as const, pattern: "-?(?:[0-9]+(?:\\.[0-9]*)?|\\.[0-9]+)" };
export function initialStructuredTerms(type: AgreementType, start: string, end?: string | null): StructuredAgreementTerms {
  if (type === "Guarantee") return { schemaVersion: 1, guarantee: { beneficiaryReference: "", limitAmount: null } };
  return { schemaVersion: 1, schedule: { startDate: start, endDate: end ?? "", frequency: "Monthly", calendarId: "", businessDayConvention: "Unadjusted", settlementBusinessDays: 0 }, ...(type === "Loan" ? { rateBasis: "Fixed" as const } : {}), ...(type === "CostShare" ? { allocations: [] } : {}) };
}
export function IntercompanyStructuredTermsEditor({ type, value, onChange, partyOneId, partyTwoId, busy = false }: {
  type: AgreementType; value: StructuredAgreementTerms; onChange: (value: StructuredAgreementTerms) => void; partyOneId: string; partyTwoId: string; busy?: boolean;
}) {
  const id = useId();
  const set = (next: Partial<StructuredAgreementTerms>) => onChange({ ...value, ...next });
  const schedule = value.schedule;
  const setSchedule = (next: Partial<Schedule>) => set({ schedule: { ...schedule!, ...next } });
  return <fieldset className="space-y-4 rounded border border-border p-3" disabled={busy}><legend className="px-1 font-semibold">Structured legal declarations v1</legend>
    <p className="text-sm text-muted-foreground">Capture the signed contract. Structured declarations support date previews; they do not authorize accrual, posting, settlement, or fetching rate fixings.</p>
    {schedule ? <FormGrid columns={3}>
      <Field label="Accrual schedule start" type="date" required value={schedule.startDate} onChange={(e) => setSchedule({ startDate: e.target.value })} />
      <Field label="Accrual schedule end" type="date" required value={schedule.endDate} onChange={(e) => setSchedule({ endDate: e.target.value })} />
      <FormRow label="Payment frequency" labelFor={`${id}-frequency`}><Select id={`${id}-frequency`} value={schedule.frequency} onChange={(e) => setSchedule({ frequency: e.target.value as Schedule["frequency"] })}>{["Monthly", "Quarterly", "SemiAnnual", "Annual", "AtMaturity"].map((item) => <option key={item}>{item}</option>)}</Select></FormRow>
      <Field label="Declared business calendar" required value={schedule.calendarId} onChange={(e) => setSchedule({ calendarId: e.target.value })} hint="Use the signed calendar ID. WEEKENDS must be selected explicitly when applicable." />
      <FormRow label="Business-day convention" labelFor={`${id}-convention`}><Select id={`${id}-convention`} value={schedule.businessDayConvention} onChange={(e) => setSchedule({ businessDayConvention: e.target.value as Schedule["businessDayConvention"] })}>{["Unadjusted", "Following", "ModifiedFollowing", "Preceding", "ModifiedPreceding"].map((item) => <option key={item}>{item}</option>)}</Select></FormRow>
      <Field label="Settlement business days" type="number" min="0" max="3660" step="1" required value={schedule.settlementBusinessDays} onChange={(e) => setSchedule({ settlementBusinessDays: Number(e.target.value) })} />
    </FormGrid> : null}
    {type === "Loan" ? <FormGrid columns={3}>
      <FormRow label="Declared rate basis" labelFor={`${id}-rate`}><Select id={`${id}-rate`} value={value.rateBasis ?? "Fixed"} onChange={(e) => set({ rateBasis: e.target.value as "Fixed" | "Floating", fixedAnnualRate: null, floatingIndexReference: null, spreadBps: null })}><option>Fixed</option><option>Floating</option></Select></FormRow>
      {value.rateBasis !== "Floating" ? <Field label="Structured annual rate (fraction)" {...decimal} required value={value.fixedAnnualRate ?? ""} onChange={(e) => set({ fixedAnnualRate: e.target.value || null })} hint="Exact decimal: 0.05 means 5%." /> : <><Field label="Floating index reference" required value={value.floatingIndexReference ?? ""} onChange={(e) => set({ floatingIndexReference: e.target.value || null })} /><Field label="Declared spread (basis points)" {...decimal} required value={value.spreadBps ?? ""} onChange={(e) => set({ spreadBps: e.target.value || null })} /></>}
      <FormRow label="Declared day count" labelFor={`${id}-daycount`}><Select id={`${id}-daycount`} required value={value.dayCount ?? ""} onChange={(e) => set({ dayCount: e.target.value as StructuredAgreementTerms["dayCount"] })}><option value="">Select convention</option>{["Actual360", "Actual365", "Thirty360", "Business252", "ActualActualIsda", "ThirtyE360", "ThirtyE360Isda", "ActualActualIcma", "Nl365", "Actual36525"].map((item) => <option key={item}>{item}</option>)}</Select></FormRow>
      <FormRow label="Declared compounding" labelFor={`${id}-compounding`}><Select id={`${id}-compounding`} required value={value.compounding ?? ""} onChange={(e) => set({ compounding: e.target.value as StructuredAgreementTerms["compounding"] })}><option value="">Select convention</option>{["Simple", "Monthly", "Quarterly", "SemiAnnual", "Annual", "Daily"].map((item) => <option key={item}>{item}</option>)}</Select></FormRow>
    </FormGrid> : null}
    {type === "ManagementFee" ? <FormGrid columns={3}>
      <FormRow label="Fee calculation basis" labelFor={`${id}-fee`}><Select id={`${id}-fee`} value={value.annualFeeRate != null || value.calculationBaseReference != null ? "Rate" : "Fixed"} onChange={(e) => set({ fixedFeeAmount: e.target.value === "Fixed" ? "" : null, annualFeeRate: e.target.value === "Rate" ? "" : null, calculationBaseReference: e.target.value === "Rate" ? "" : null })}><option value="Fixed">Fixed monetary amount</option><option value="Rate">Annual percentage of named base</option></Select></FormRow>
      {value.annualFeeRate != null || value.calculationBaseReference != null ? <><Field label="Structured annual fee rate (fraction)" {...decimal} required value={value.annualFeeRate ?? ""} onChange={(e) => set({ annualFeeRate: e.target.value || null })} /><Field label="Fee calculation base reference" required value={value.calculationBaseReference ?? ""} onChange={(e) => set({ calculationBaseReference: e.target.value })} /></> : <Field label="Fixed fee amount in agreement currency" {...decimal} required value={value.fixedFeeAmount ?? ""} onChange={(e) => set({ fixedFeeAmount: e.target.value || null })} />}
    </FormGrid> : null}
    {type === "CostShare" ? <div className="space-y-3"><FormRow label="Allocation method" labelFor={`${id}-allocation`}><Select id={`${id}-allocation`} value={value.allocationDriverReference != null ? "Driver" : "Fixed"} onChange={(e) => set({ allocations: e.target.value === "Fixed" ? [] : null, allocationDriverReference: e.target.value === "Driver" ? "" : null })}><option value="Fixed">Fixed percentages</option><option value="Driver">Referenced driver</option></Select></FormRow>
      {value.allocationDriverReference != null ? <Field label="Allocation driver reference" required value={value.allocationDriverReference} onChange={(e) => set({ allocationDriverReference: e.target.value })} /> : <>{(value.allocations ?? []).map((item, index) => <FormGrid key={index} columns={3}><FormRow label={`Allocation entity ${index + 1}`} labelFor={`${id}-entity-${index}`}><Select id={`${id}-entity-${index}`} required value={item.entityId} onChange={(e) => set({ allocations: value.allocations!.map((row, i) => i === index ? { ...row, entityId: e.target.value } : row) })}><option value="">Select legal party</option>{partyOneId ? <option value={partyOneId}>First legal party · {partyOneId}</option> : null}{partyTwoId ? <option value={partyTwoId}>Second legal party · {partyTwoId}</option> : null}</Select></FormRow><Field label={`Allocation percentage ${index + 1}`} {...decimal} required value={item.percent} onChange={(e) => set({ allocations: value.allocations!.map((row, i) => i === index ? { ...row, percent: e.target.value } : row) })} /><Button type="button" variant="outline" className="self-end" onClick={() => set({ allocations: value.allocations!.filter((_, i) => i !== index) })}>Remove allocation {index + 1}</Button></FormGrid>)}<Button type="button" variant="outline" disabled={(value.allocations?.length ?? 0) >= 2} onClick={() => set({ allocations: [...value.allocations ?? [], { entityId: "", percent: "" }] })}>Add allocation party</Button><p className="text-sm text-muted-foreground">Fixed allocations name the two native legal parties and must total exactly 100%. Driver values remain outside this declaration.</p></>}
    </div> : null}
    {type === "Guarantee" && value.guarantee ? <FormGrid columns={2}><Field label="Guarantee beneficiary reference" required value={value.guarantee.beneficiaryReference} onChange={(e) => set({ guarantee: { ...value.guarantee!, beneficiaryReference: e.target.value } })} /><Field label="Guarantee limit in agreement currency" {...decimal} value={value.guarantee.limitAmount ?? ""} onChange={(e) => set({ guarantee: { ...value.guarantee!, limitAmount: e.target.value || null } })} hint="An absent limit is unknown. Disclosure only; no journals or ordinary balance coverage." /></FormGrid> : null}
  </fieldset>;
}
