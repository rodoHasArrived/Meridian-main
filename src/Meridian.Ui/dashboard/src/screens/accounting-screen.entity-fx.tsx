import { useState } from "react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { TechnicalDetails } from "@/components/ui/technical-details";
import { entityOperationEndpoints as endpoints } from "@/lib/api/entity-operations.api";
import type { ConsolidationFxConfiguration, ConsolidationFxRateSet, EntityOperationScope } from "@/types/entity-operations";
import { FxRateSetEditor } from "./accounting-screen.entity-fx-rates";
import { FxBookPolicyEditor } from "./accounting-screen.entity-fx-policy";
import { EntityOperationTable, OperationError, OperationField, today, useEntityData } from "./accounting-screen.entity-operations.shared";

export function EntityFxAuthority({ scope }: { scope: EntityOperationScope }) {
  const rates = useEntityData<ConsolidationFxRateSet[]>(endpoints.rates);
  const [asOf, setAsOf] = useState(today());
  const configuration = useEntityData<ConsolidationFxConfiguration>(`${endpoints.fxConfiguration(scope.book.ledgerBookId)}?${new URLSearchParams({ asOf })}`, true);
  const [editingRates, setEditingRates] = useState(false);
  const [editingPolicy, setEditingPolicy] = useState(false);
  const [previousRates, setPreviousRates] = useState<ConsolidationFxRateSet | null>(null);
  const editing = editingRates || editingPolicy;
  const editingReason = "Save or cancel the current FX authority edit before changing its scope.";
  const currentSets = rates.data?.filter((set) => !rates.data?.some((other) => other.rateSetId === set.rateSetId && other.revision > set.revision)) ?? [];
  return <section className="space-y-4"><h3 className="font-semibold">Governed FX translation authority</h3>
    <p className="text-sm text-muted-foreground">Consolidation translates the whole trial balance under the selected book policy. Booking breaks, translation differences and rounding are distinct. Missing or superseded rate authority blocks draft creation.</p>
    <OperationError error={rates.error} /><OperationError error={configuration.error} />
    <div className="flex flex-wrap items-end gap-3"><OperationField label="FX configuration as of" disabled={editing} value={asOf} onChange={(value) => { setAsOf(value); setEditingPolicy(false); }} type="date" required />
      <Button type="button" variant="outline" disabled={editing} disabledReason={editingReason} onClick={() => { rates.refresh(); configuration.refresh(); }}>Refresh FX authority</Button>
      <Button type="button" disabled={editing || !scope.capabilities.canManageLedger} disabledReason={editing ? editingReason : "Ledger management permission is required to approve FX rate sets."} onClick={() => { setPreviousRates(null); setEditingRates(true); }}>New approved rate set</Button>
      <Button type="button" variant="outline" disabled={editing || !scope.capabilities.canManageLedger || rates.loading || configuration.loading || currentSets.length === 0} disabledReason={editing ? editingReason : !scope.capabilities.canManageLedger ? "Ledger management permission is required to configure book translation." : rates.loading || configuration.loading ? "Wait for the current FX authority to load." : "Retain an approved FX rate set before configuring book translation."} onClick={() => setEditingPolicy(true)}>Configure book translation</Button>
    </div>
    {rates.loading ? <p role="status">Loading approved rate evidence…</p> : null}
    {rates.data?.length === 0 ? <p className="text-sm text-muted-foreground">No approved FX rate sets are retained. Retain quotes and evidence before configuring a cross-currency consolidation.</p> : null}
    <EntityOperationTable label="Approved FX rate sets" rows={rates.data ?? []} getRowId={(set) => `${set.rateSetId}:${set.revision}`} emptyText="No approved FX rate sets are retained."
      columns={[
        { id: "set", label: "Rate set and revision", rowHeader: true, className: "break-all", render: (set) => <><span className="font-mono text-xs">{set.rateSetId}</span><p>Revision {set.revision} {currentSets.includes(set) ? <Badge variant="outline">Latest</Badge> : <Badge variant="warning">Superseded</Badge>}</p>
          {currentSets.includes(set) ? <Button className="mt-2" type="button" size="sm" variant="outline" disabled={editing || !scope.capabilities.canManageLedger} disabledReason={editing ? editingReason : "Ledger management permission is required to approve FX rate sets."} onClick={() => { setPreviousRates(set); setEditingRates(true); }}>Revise rate set {set.revision}</Button> : null}</> },
        { id: "source", label: "Source", render: (set) => <>{set.sourceEvidence}<p className="mt-1 break-all font-mono text-xs">{set.contentHash}</p></> },
        { id: "approval", label: "Approval", render: (set) => <>{set.approvedBy}<p className="mt-1 text-xs">{set.approvedAt}</p></> },
        { id: "quotes", label: "Quotes", render: (set) => <TechnicalDetails label={`${set.rates.length} retained quotes`}><ul className="space-y-2 text-xs">{set.rates.map((rate) => <li key={rate.rateId}><p className="font-semibold">{rate.fromCurrency}/{rate.toCurrency} · {String(rate.rate)} · {rate.kind} · {rate.rateDate}</p><p>{rate.sourceEvidence}</p>{rate.periodStart ? <p>Period {rate.periodStart} → {rate.periodEnd}</p> : null}<p className="break-all font-mono">{rate.rateId} · inversion {rate.allowInverse ? "explicitly allowed" : "disabled"}</p></li>)}</ul></TechnicalDetails> }
      ]} />
    {configuration.data ? <div className="space-y-2 rounded border border-border p-3"><p className="text-sm font-semibold">Effective policy {configuration.data.policy.policyId} · {configuration.data.policy.revision}</p>
      <p className="text-sm">Configuration {configuration.data.revision} · {configuration.data.policy.reportingCurrency} · {configuration.data.policy.decimalPlaces} decimal places · reserve {configuration.data.policy.translationReserveAccount}</p>
      <TechnicalDetails label="Effective FX policy provenance"><dl className="space-y-2 break-all text-xs"><div><dt>Rate set and revision</dt><dd>{configuration.data.rateSetId} · {configuration.data.rateSetRevision}</dd></div><div><dt>Approved by</dt><dd>{configuration.data.approvedBy} · {configuration.data.approvedAt}</dd></div><div><dt>Configuration hash</dt><dd className="font-mono">{configuration.data.contentHash}</dd></div><div><dt>Effective window</dt><dd>{configuration.data.effectiveFrom} → {configuration.data.effectiveTo ?? "Open"} (exclusive)</dd></div></dl></TechnicalDetails>
    </div> : !configuration.loading && !configuration.error ? <p className="text-sm text-warning">No approved book FX configuration is effective on {asOf}.</p> : null}
    {editingRates ? <FxRateSetEditor key={previousRates ? `${previousRates.rateSetId}:${previousRates.revision}` : "new-rates"} scope={scope} previous={previousRates} onCancel={() => setEditingRates(false)} onSaved={() => { setEditingRates(false); rates.refresh(); }} /> : null}
    {editingPolicy ? <FxBookPolicyEditor key={`${scope.book.ledgerBookId}:${configuration.data?.revision ?? 0}`} scope={scope} current={configuration.data} rateSets={currentSets} onCancel={() => setEditingPolicy(false)} onSaved={() => { setEditingPolicy(false); configuration.refresh(); }} /> : null}
    {!scope.capabilities.canManageLedger ? <p className="text-sm text-muted-foreground">You can inspect approved FX authority. Ledger management permission is required to approve rates or a book policy.</p> : null}
  </section>;
}
