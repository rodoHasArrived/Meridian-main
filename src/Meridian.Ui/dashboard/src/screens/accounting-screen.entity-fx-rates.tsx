import { useState, type FormEvent } from "react";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { FormGrid } from "@/components/ui/form";
import type { ConsolidationFxRate, ConsolidationFxRateSet, EntityOperationScope, FxRateKind } from "@/types/entity-operations";
import { entityOperationEndpoints as endpoints } from "@/lib/api/entity-operations.api";
import { MutationFeedback, newIdentity, OperationField, today, useEntityMutation } from "./accounting-screen.entity-operations.shared";

export const fxRateKinds: FxRateKind[] = ["Closing", "TransactionDate", "Historical", "PeriodAverage"];
function newQuote(currency: string): ConsolidationFxRate {
  return { rateId: newIdentity(), fromCurrency: "", toCurrency: currency, rateDate: today(), rate: "", kind: "Closing",
    sourceEvidence: "", allowInverse: false, periodStart: null, periodEnd: null };
}

export function FxRateSetEditor({ scope, previous, onSaved, onCancel }: { scope: EntityOperationScope;
  previous: ConsolidationFxRateSet | null; onSaved: () => void; onCancel: () => void }) {
  const [rateSetId] = useState(() => previous?.rateSetId ?? newIdentity());
  const [sourceEvidence, setSourceEvidence] = useState(previous?.sourceEvidence ?? "");
  const [rates, setRates] = useState<ConsolidationFxRate[]>(() => previous?.rates.map((rate) => ({ ...rate })) ?? [newQuote(scope.book.baseCurrency)]);
  const mutation = useEntityMutation();
  function update(index: number, change: Partial<ConsolidationFxRate>) { setRates((current) => current.map((rate, row) => row === index ? { ...rate, ...change } : rate)); }
  async function approve(event: FormEvent) {
    event.preventDefault();
    const result = await mutation.run<ConsolidationFxRateSet>(`${endpoints.rates}/approve`, {
      rateSetId, revision: (previous?.revision ?? 0) + 1, tenantId: "", companyId: "", rates,
      sourceEvidence: sourceEvidence.trim(), preparedBy: "", approvedBy: "", approvedAt: "0001-01-01T00:00:00Z",
      previousHash: previous?.contentHash ?? null, contentHash: null
    }, "Approved rate-set revision retained. Existing draft reviews must be refreshed when their rate authority changes.");
    if (result) onSaved();
  }
  return <form className="space-y-4 rounded border border-border p-4" onSubmit={(event) => void approve(event)}>
    <h4 className="font-semibold">{previous ? `Approve rate-set successor · revision ${previous.revision + 1}` : "Approve a new FX rate set"}</h4>
    <p className="text-sm text-muted-foreground">Rates are direct multipliers: one unit of the source currency buys the quoted amount of the destination currency. Approval pins exact evidence; dates and periods are explicit.</p>
    <fieldset disabled={mutation.busy || !scope.capabilities.canManageLedger} className="space-y-4">
      <OperationField label="Rate-set source evidence" value={sourceEvidence} onChange={setSourceEvidence} required />
      {rates.map((rate, index) => <fieldset className="rounded border border-border p-3" key={rate.rateId}><legend className="px-1 text-sm font-semibold">Quote {index + 1}</legend><FormGrid columns={3}>
        <OperationField label={`Quote ${index + 1} source currency`} value={rate.fromCurrency} onChange={(value) => update(index, { fromCurrency: value.toUpperCase() })} pattern="[A-Z]{3}" required />
        <OperationField label={`Quote ${index + 1} destination currency`} value={rate.toCurrency} onChange={(value) => update(index, { toCurrency: value.toUpperCase() })} pattern="[A-Z]{3}" required />
        <OperationField label={`Quote ${index + 1} exact rate`} value={String(rate.rate)} onChange={(value) => update(index, { rate: value })} pattern="[0-9]+(\.[0-9]+)?" required />
        <OperationField label={`Quote ${index + 1} method`} value={rate.kind} onChange={(value) => update(index, { kind: value as FxRateKind, periodStart: null, periodEnd: null })}>{fxRateKinds.map((kind) => <option key={kind}>{kind}</option>)}</OperationField>
        <OperationField label={`Quote ${index + 1} date`} value={rate.rateDate} onChange={(value) => update(index, { rateDate: value })} type="date" required />
        <OperationField label={`Quote ${index + 1} evidence`} value={rate.sourceEvidence} onChange={(value) => update(index, { sourceEvidence: value })} required />
        {rate.kind === "PeriodAverage" ? <><OperationField label={`Quote ${index + 1} averaging period start`} value={rate.periodStart ?? ""} onChange={(value) => update(index, { periodStart: value || null })} type="date" required />
          <OperationField label={`Quote ${index + 1} averaging period end`} value={rate.periodEnd ?? ""} onChange={(value) => update(index, { periodEnd: value || null })} type="date" required /></> : null}
      </FormGrid><Checkbox className="mt-3" checked={rate.allowInverse} onCheckedChange={(checked) => update(index, { allowInverse: checked })} label={`Allow explicit inversion of quote ${index + 1}`} />
        <Button className="mt-3" type="button" size="sm" variant="outline" disabled={rates.length === 1} disabledReason="A rate set must retain at least one quote." onClick={() => setRates((current) => current.filter((_, row) => row !== index))}>Remove quote {index + 1}</Button>
      </fieldset>)}
      <div className="flex flex-wrap gap-2"><Button type="button" variant="outline" onClick={() => setRates((current) => [...current, newQuote(scope.book.baseCurrency)])}>Add FX quote</Button>
        <Button type="submit">Approve and retain rate set</Button><Button type="button" variant="outline" onClick={onCancel}>Cancel rate editing</Button></div>
    </fieldset><MutationFeedback state={mutation} />
  </form>;
}
