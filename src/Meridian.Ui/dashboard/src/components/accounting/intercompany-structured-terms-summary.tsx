import { DenseDataTable } from "@/components/meridian/ui-kit-primitives";
import type { AgreementSchedulePreview, StructuredAgreementTerms } from "@/types/intercompany-agreements";

const display = (value: unknown) => value == null || value === "" ? "Not declared" : String(value);
export function StructuredTermsSummary({ terms }: { terms?: StructuredAgreementTerms | null }) {
  if (!terms) return <p className="text-sm text-muted-foreground">Terms format: Unstructured. No structured terms are inferred from descriptions.</p>;
  return <div className="space-y-2 rounded border border-border p-3 text-sm"><p className="font-semibold">Structured declarations v{terms.schemaVersion}</p><p className="text-muted-foreground">Declarations support date previews; they do not establish calculation or posting readiness.</p>
    <dl className="grid gap-2 md:grid-cols-2">{[["Rate basis", terms.rateBasis], ["Annual fixed rate (fraction)", terms.fixedAnnualRate], ["Floating index", terms.floatingIndexReference], ["Spread (basis points)", terms.spreadBps], ["Day count", terms.dayCount], ["Compounding", terms.compounding], ["Fixed fee amount", terms.fixedFeeAmount], ["Annual fee rate (fraction)", terms.annualFeeRate], ["Calculation base", terms.calculationBaseReference], ["Allocation driver", terms.allocationDriverReference]].filter(([, value]) => value != null).map(([label, value]) => <div key={String(label)}><dt className="text-muted-foreground">{label}</dt><dd className="break-all">{display(value)}</dd></div>)}</dl>
    {terms.schedule ? <p>{terms.schedule.startDate} → {terms.schedule.endDate} · {terms.schedule.frequency} · calendar {terms.schedule.calendarId} · {terms.schedule.businessDayConvention} · settlement {terms.schedule.settlementBusinessDays} business days</p> : null}
    {terms.allocations?.length ? <ul>{terms.allocations.map((item) => <li key={item.entityId} className="break-all">Allocation entity {item.entityId}: {item.percent}%</li>)}</ul> : null}
    {terms.guarantee ? <><p>Beneficiary: {terms.guarantee.beneficiaryReference} · limit {display(terms.guarantee.limitAmount)} in agreement currency</p><p className="text-muted-foreground">Guarantee disclosure only; no ordinary balance coverage or journal generation.</p></> : null}
  </div>;
}
export function ContractualDatePreview({ preview }: { preview: AgreementSchedulePreview }) {
  return <section className="space-y-3" aria-label="Contractual date preview"><h4 className="font-semibold">Contractual date preview · {preview.status}</h4><p className="text-sm">{preview.termsFormat} · {preview.kernelVersion} · calendar {preview.calendarId ?? "Unavailable"}</p>{preview.diagnostics.map((diagnostic, index) => <p key={index} role="status" className="text-sm text-warning">{diagnostic}</p>)}
    <DenseDataTable rows={preview.periods} getRowId={(item) => String(item.sequence)} ariaLabel="Server-generated contractual dates" caption="Date-only preview; no cash amounts or inferred journal due dates." emptyText="No contractual date periods are available." columns={[{ id: "period", label: "Period", rowHeader: true, render: (item) => item.sequence }, { id: "start", label: "Accrual start", render: (item) => item.accrualStart }, { id: "end", label: "Accrual end", render: (item) => item.accrualEnd }, { id: "payment", label: "Payment / settlement", render: (item) => item.paymentDate }]} />
    <p className="break-all text-sm text-muted-foreground">Subject {preview.subjectHash} · calendar hash {preview.calendarHash ?? "Unavailable"}</p>
  </section>;
}
