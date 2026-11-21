import { useState, type FormEvent } from "react";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { FormGrid } from "@/components/ui/form";
import type { ConsolidationFxConfiguration, ConsolidationFxRateSet, EntityOperationScope, FxRateKind } from "@/types/entity-operations";
import { entityOperationEndpoints as endpoints } from "@/lib/api/entity-operations.api";
import { MutationFeedback, OperationField, today, useEntityMutation } from "./accounting-screen.entity-operations.shared";
import { fxRateKinds } from "./accounting-screen.entity-fx-rates";
import { currencyMinorUnitPrecision } from "@/lib/format";

type AccountRule = NonNullable<ConsolidationFxConfiguration["policy"]["accountRules"]>[number];
type HistoricalFact = NonNullable<ConsolidationFxConfiguration["historicalFacts"]>[number];
type ClosingDate = NonNullable<ConsolidationFxConfiguration["policy"]["closingRateDates"]>[number];

export function FxBookPolicyEditor({ scope, current, rateSets, onSaved, onCancel }: {
  scope: EntityOperationScope; current: ConsolidationFxConfiguration | null; rateSets: ConsolidationFxRateSet[]; onSaved: () => void; onCancel: () => void;
}) {
  const [selectedRate, setSelectedRate] = useState(current ? `${current.rateSetId}:${current.rateSetRevision}` : "");
  const [effectiveFrom, setEffectiveFrom] = useState(current?.effectiveFrom ?? today());
  const [effectiveTo, setEffectiveTo] = useState(current?.effectiveTo ?? "");
  const [policyId, setPolicyId] = useState(current?.policy.policyId ?? "group-translation");
  const [policyRevision, setPolicyRevision] = useState(current?.policy.revision ?? "v1");
  const [precision, setPrecision] = useState(String(current?.policy.decimalPlaces ?? currencyMinorUnitPrecision(scope.book.baseCurrency)));
  const [reserveAccount, setReserveAccount] = useState(current?.policy.translationReserveAccount ?? "Equity:Cumulative Translation Adjustment");
  const [rules, setRules] = useState<AccountRule[]>(current?.policy.accountRules ?? []);
  const [facts, setFacts] = useState<HistoricalFact[]>(current?.historicalFacts ?? []);
  const [closingDates, setClosingDates] = useState<ClosingDate[]>(current?.policy.closingRateDates ?? []);
  const mutation = useEntityMutation();
  function rule(index: number, change: Partial<AccountRule>) { setRules((rows) => rows.map((row, at) => at === index ? { ...row, ...change } : row)); }
  function fact(index: number, change: Partial<HistoricalFact>) { setFacts((rows) => rows.map((row, at) => at === index ? { ...row, ...change } : row)); }
  function closing(index: number, change: Partial<ClosingDate>) { setClosingDates((rows) => rows.map((row, at) => at === index ? { ...row, ...change } : row)); }
  async function approve(event: FormEvent) {
    event.preventDefault();
    const selected = rateSets.find((item) => `${item.rateSetId}:${item.revision}` === selectedRate);
    if (!selected) return;
    const result = await mutation.run<ConsolidationFxConfiguration>(`${endpoints.fxConfiguration(scope.book.ledgerBookId)}/approve`, {
      ledgerBookId: scope.book.ledgerBookId, revision: (current?.revision ?? 0) + 1, tenantId: "", companyId: "",
      effectiveFrom, effectiveTo: effectiveTo || null, rateSetId: selected.rateSetId, rateSetRevision: selected.revision,
      policy: { policyId, revision: policyRevision, reportingCurrency: scope.book.baseCurrency, decimalPlaces: Number(precision),
        translationReserveAccount: reserveAccount, accountRules: rules, closingRateDates: closingDates }, historicalFacts: facts,
      approvedBy: "", approvedAt: "0001-01-01T00:00:00Z", previousHash: current?.contentHash ?? null, contentHash: null
    }, "Approved book translation policy retained. Prepare a fresh consolidation preview against this exact authority.");
    if (result) onSaved();
  }
  return <form className="space-y-4 rounded border border-border p-4" onSubmit={(event) => void approve(event)}>
    <h4 className="font-semibold">Approve book FX configuration · revision {(current?.revision ?? 0) + 1}</h4>
    <p className="text-sm text-muted-foreground">Default treatment is closing rates for assets and liabilities, transaction-date rates for income and expenses, and evidenced historical rates for equity. Period averages require an explicit account rule. The whole trial balance is translated with a separate cumulative translation reserve.</p>
    <fieldset disabled={!scope.capabilities.canManageLedger || mutation.busy} className="space-y-4"><FormGrid columns={3}>
      <OperationField label="Approved FX rate-set revision" value={selectedRate} onChange={setSelectedRate} required><option value="">Select approved rate set</option>{rateSets.map((rateSet) => <option key={`${rateSet.rateSetId}:${rateSet.revision}`} value={`${rateSet.rateSetId}:${rateSet.revision}`}>{rateSet.sourceEvidence} · revision {rateSet.revision}</option>)}</OperationField>
      <OperationField label="FX configuration effective from" value={effectiveFrom} onChange={setEffectiveFrom} type="date" required />
      <OperationField label="FX configuration effective to (exclusive)" value={effectiveTo} onChange={setEffectiveTo} type="date" />
      <OperationField label="Translation policy ID" value={policyId} onChange={setPolicyId} required />
      <OperationField label="Translation policy revision" value={policyRevision} onChange={setPolicyRevision} required />
      <OperationField label="Reporting currency precision" value={precision} onChange={setPrecision} type="number" required hint={`The server validates currency minor units for ${scope.book.baseCurrency}.`} />
      <OperationField label="Translation reserve account" value={reserveAccount} onChange={setReserveAccount} required />
    </FormGrid>
      <fieldset className="space-y-3"><legend className="mb-2 text-sm font-semibold">Explicit account translation rules</legend>
        {rules.map((row, index) => <div className="space-y-2 rounded border border-border p-3" key={index}><FormGrid columns={3}>
          <OperationField label={`Treatment ${index + 1} account path`} value={row.accountPath} onChange={(value) => rule(index, { accountPath: value })} required />
          <OperationField label={`Treatment ${index + 1} method`} value={row.method} onChange={(value) => rule(index, { method: value as FxRateKind, periodStart: null, periodEnd: null })}>{fxRateKinds.map((kind) => <option key={kind}>{kind}</option>)}</OperationField>
          {row.method === "PeriodAverage" ? <><OperationField label={`Treatment ${index + 1} period start`} value={row.periodStart ?? ""} onChange={(value) => rule(index, { periodStart: value || null })} type="date" required />
            <OperationField label={`Treatment ${index + 1} period end`} value={row.periodEnd ?? ""} onChange={(value) => rule(index, { periodEnd: value || null })} type="date" required /></> : null}
        </FormGrid><Checkbox checked={row.requiresOpeningTranslatedBalance} onCheckedChange={(checked) => rule(index, { requiresOpeningTranslatedBalance: checked })} label={`Require retained opening translated balance for treatment ${index + 1}`} />
          <Button type="button" size="sm" variant="outline" onClick={() => setRules((currentRules) => currentRules.filter((_, at) => at !== index))}>Remove treatment {index + 1}</Button></div>)}
        <Button type="button" size="sm" variant="outline" onClick={() => setRules((currentRules) => [...currentRules, { accountPath: "", method: "PeriodAverage", periodStart: null, periodEnd: null, requiresOpeningTranslatedBalance: false }])}>Add account translation rule</Button>
      </fieldset>
      <fieldset className="space-y-3"><legend className="mb-2 text-sm font-semibold">Historical and opening-balance evidence</legend>
        {facts.map((row, index) => <div className="space-y-2 rounded border border-border p-3" key={index}><FormGrid columns={3}>
          <OperationField label={`Historical fact ${index + 1} source book`} value={row.ledgerBookId} onChange={(value) => fact(index, { ledgerBookId: value })} required><option value="">Select source book</option>{scope.books.map((book) => <option key={book.ledgerBookId} value={book.ledgerBookId}>{book.displayName}</option>)}</OperationField>
          <OperationField label={`Historical fact ${index + 1} journal ID`} value={row.journalEntryId} onChange={(value) => fact(index, { journalEntryId: value })} required />
          <OperationField label={`Historical fact ${index + 1} line ID`} value={row.lineId} onChange={(value) => fact(index, { lineId: value })} required />
          <OperationField label={`Historical fact ${index + 1} date`} value={row.historicalDate ?? ""} onChange={(value) => fact(index, { historicalDate: value || null })} type="date" />
          <OperationField label={`Historical fact ${index + 1} opening translated signed amount`} value={String(row.openingTranslatedSignedAmount ?? "")} onChange={(value) => fact(index, { openingTranslatedSignedAmount: value || null })} pattern="-?[0-9]+(\.[0-9]+)?" />
          <OperationField label={`Historical fact ${index + 1} evidence`} value={row.evidenceReference} onChange={(value) => fact(index, { evidenceReference: value })} required />
        </FormGrid><Button type="button" size="sm" variant="outline" onClick={() => setFacts((currentFacts) => currentFacts.filter((_, at) => at !== index))}>Remove historical fact {index + 1}</Button></div>)}
        <Button type="button" size="sm" variant="outline" onClick={() => setFacts((currentFacts) => [...currentFacts, { ledgerBookId: "", journalEntryId: "", lineId: "", historicalDate: null, openingTranslatedSignedAmount: null, evidenceReference: "" }])}>Add historical source fact</Button>
      </fieldset>
      <fieldset className="space-y-3"><legend className="mb-2 text-sm font-semibold">Evidenced closing-date selections</legend><p className="text-xs text-muted-foreground">Without a selection, the exact consolidation as-of date is required. Use a retained holiday/weekend decision to select a prior business date.</p>
        {closingDates.map((row, index) => <div className="space-y-2 rounded border border-border p-3" key={index}><FormGrid columns={3}>
          <OperationField label={`Closing selection ${index + 1} as of`} value={row.asOf} onChange={(value) => closing(index, { asOf: value })} type="date" required />
          <OperationField label={`Closing selection ${index + 1} rate date`} value={row.rateDate} onChange={(value) => closing(index, { rateDate: value })} type="date" required />
          <OperationField label={`Closing selection ${index + 1} evidence`} value={row.evidenceReference} onChange={(value) => closing(index, { evidenceReference: value })} required />
        </FormGrid><Button type="button" size="sm" variant="outline" onClick={() => setClosingDates((currentDates) => currentDates.filter((_, at) => at !== index))}>Remove closing selection {index + 1}</Button></div>)}
        <Button type="button" size="sm" variant="outline" onClick={() => setClosingDates((currentDates) => [...currentDates, { asOf: today(), rateDate: today(), evidenceReference: "" }])}>Add closing-date selection</Button>
      </fieldset>
      <Button type="submit" disabled={!selectedRate} disabledReason="Select an approved FX rate-set revision before retaining the policy.">Approve and retain book FX policy</Button><Button type="button" variant="outline" onClick={onCancel}>Cancel policy editing</Button>
    </fieldset><MutationFeedback state={mutation} />
  </form>;
}
