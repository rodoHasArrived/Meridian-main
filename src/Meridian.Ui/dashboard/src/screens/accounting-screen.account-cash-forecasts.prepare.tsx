import { useId, useState, type FormEvent } from "react";
import { Button } from "@/components/ui/button";
import { FormGrid, FormRow } from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import { StatusBanner } from "@/components/ui/status-banner";
import type { AccountCashForecastRequest, AccountCashForecastSnapshot } from "@/types/account-oversight";

interface RetainedForecastPreparationProps {
  source: AccountCashForecastSnapshot;
  history: AccountCashForecastSnapshot[];
  onPrepare: (request: AccountCashForecastRequest) => void;
  onCancel: () => void;
}

/** Reuse exact reviewed inputs, including original dates and source identities. Never synthesize evidence. */
export function RetainedForecastPreparation({ source, history, onPrepare, onCancel }: RetainedForecastPreparationProps) {
  const id = useId();
  const input = source.inputs;
  const [periodStart, setPeriodStart] = useState(input.periodStart);
  const [periodEnd, setPeriodEnd] = useState(input.periodEnd);
  const [threshold, setThreshold] = useState(input.minimumCashThreshold);
  const [bucketDays, setBucketDays] = useState(String(input.bucketDays));
  const [rationale, setRationale] = useState("");
  const [includeOpening, setIncludeOpening] = useState(Boolean(input.openingCash));
  const [includeClosing, setIncludeClosing] = useState(Boolean(input.closingCash));
  const [flows, setFlows] = useState(() => input.flows.map(() => true));
  const [fx, setFx] = useState(() => input.fxConversions.map(() => true));
  const [error, setError] = useState<string | null>(null);
  const latest = history.filter(item => item.inputs.periodStart === periodStart && item.inputs.periodEnd === periodEnd).at(-1);

  function prepare(event: FormEvent) {
    event.preventDefault();
    const days = Number(bucketDays);
    if (!periodStart || !periodEnd || periodEnd < periodStart) {
      setError("Choose a cash period ending on or after its start date.");
      return;
    }
    if (!/^-?\d+(?:\.\d+)?$/.test(threshold)) {
      setError("Enter the minimum cash threshold as an exact decimal amount.");
      return;
    }
    if (!Number.isSafeInteger(days) || days < 1 || days > 3660) {
      setError("Bucket days must be a whole number between 1 and 3660.");
      return;
    }
    if (latest && !rationale.trim()) {
      setError("Explain the correction to this retained cash period before reviewing the prepared inputs.");
      return;
    }
    const openingCash = includeOpening ? input.openingCash : null;
    const closingCash = includeClosing ? input.closingCash : null;
    if (openingCash && openingCash.asOfDate !== periodStart || closingCash && closingCash.asOfDate !== periodEnd) {
      setError("Selected settled observations must match the opening and closing dates. Retained dates cannot be moved; deselect an unavailable observation or restore its date.");
      return;
    }
    onPrepare({
      ...input, runId: `cash-forecast-${crypto.randomUUID()}`, recordedAtUtc: new Date().toISOString(),
      periodStart, periodEnd, openingCash, closingCash,
      flows: input.flows.filter((_, index) => flows[index]),
      fxConversions: input.fxConversions.filter((_, index) => fx[index]),
      minimumCashThreshold: threshold, bucketDays: days,
      previousSnapshotId: latest?.snapshotId ?? null,
      correctionRationale: latest ? rationale.trim() : null
    });
  }

  return <form onSubmit={prepare} aria-label="Prepare cash forecast from retained inputs" className="space-y-4 rounded-md border border-border p-4">
    <div>
      <h3 className="text-sm font-semibold">Prepare from retained inputs</h3>
      <p className="mt-1 text-sm text-muted-foreground">Reuse the selected forecast's reviewed observations, cash flows and dated FX. Their amounts, dates and evidence remain unchanged. The server rechecks the sources before capture.</p>
      <p className="mt-1 text-xs text-muted-foreground">New observations or revised source data still require a reviewed source handoff. Missing closing cash can support a forecast but prevents cash reconciliation certification.</p>
    </div>
    <FormGrid columns={2}>
      <FormRow label="Cash period start" labelFor={`${id}-start`}><Input id={`${id}-start`} type="date" required value={periodStart} onChange={event => setPeriodStart(event.target.value)} /></FormRow>
      <FormRow label="Cash period end" labelFor={`${id}-end`}><Input id={`${id}-end`} type="date" required value={periodEnd} onChange={event => setPeriodEnd(event.target.value)} /></FormRow>
      <FormRow label={`Minimum cash threshold (${input.scope.baseCurrency})`} labelFor={`${id}-threshold`}><Input id={`${id}-threshold`} required inputMode="decimal" value={threshold} onChange={event => setThreshold(event.target.value)} /></FormRow>
      <FormRow label="Cash bucket days" labelFor={`${id}-bucket`}><Input id={`${id}-bucket`} type="number" min={1} max={3660} step={1} required value={bucketDays} onChange={event => setBucketDays(event.target.value)} /></FormRow>
    </FormGrid>
    <fieldset className="space-y-2 text-sm">
      <legend className="mb-2 font-semibold">Retained settled observations</legend>
      {input.openingCash ? <label className="flex items-start gap-2"><input type="checkbox" checked={includeOpening} onChange={event => setIncludeOpening(event.target.checked)} /><span>Opening: {input.openingCash.amount} {input.openingCash.currency} · {input.openingCash.asOfDate} · {input.openingCash.provenance.evidence.sourceReference}</span></label> : <p className="text-muted-foreground">No retained opening observation available.</p>}
      {input.closingCash ? <label className="flex items-start gap-2"><input type="checkbox" checked={includeClosing} onChange={event => setIncludeClosing(event.target.checked)} /><span>Closing: {input.closingCash.amount} {input.closingCash.currency} · {input.closingCash.asOfDate} · {input.closingCash.provenance.evidence.sourceReference}</span></label> : <p className="text-muted-foreground">No retained closing observation available.</p>}
    </fieldset>
    <fieldset className="space-y-2 text-sm">
      <legend className="mb-2 font-semibold">Retained cash flows</legend>
      <p className="text-xs text-muted-foreground">Include the complete source population for the period. Omitting an input changes the forecast and must be explained in a correction.</p>
      {input.flows.map((flow, index) => <label key={`${flow.versionId}:${index}`} className="flex items-start gap-2">
        <input type="checkbox" checked={flows[index]} onChange={event => setFlows(current => current.map((value, position) => position === index ? event.target.checked : value))} />
        <span>{flow.flowType} · {flow.status === 1 ? "Actual receipt" : "Expected"} · {flow.amount} {flow.currency} · Receipt {flow.cashReceiptDate || "Missing"}<span className="block text-xs text-muted-foreground">{flow.provenance.evidence.sourceReference} · Version {flow.versionId}</span></span>
      </label>)}
      {input.flows.length === 0 ? <p className="text-muted-foreground">No retained cash flows available.</p> : null}
    </fieldset>
    <fieldset className="space-y-2 text-sm">
      <legend className="mb-2 font-semibold">Retained dated FX</legend>
      <p className="text-xs text-muted-foreground">Rates state base units per native unit for one exact date. Missing foreign-currency declarations remain blockers.</p>
      {input.fxConversions.map((rate, index) => <label key={`${rate.currency}:${rate.baseCurrency}:${rate.conversionDate}:${index}`} className="flex items-start gap-2">
        <input type="checkbox" checked={fx[index]} onChange={event => setFx(current => current.map((value, position) => position === index ? event.target.checked : value))} />
        <span>{rate.currency} → {rate.baseCurrency} · {rate.conversionDate} · {rate.baseUnitsPerCurrencyUnit}<span className="block text-xs text-muted-foreground">{rate.provenance.evidence.sourceReference}</span></span>
      </label>)}
      {input.fxConversions.length === 0 ? <p className="text-muted-foreground">No retained dated FX declarations available.</p> : null}
    </fieldset>
    {latest ? <FormRow label="Correction rationale" labelFor={`${id}-rationale`} hint="This period already has a retained forecast. The new capture links its latest snapshot and preserves prior results.">
      <Input id={`${id}-rationale`} required value={rationale} onChange={event => setRationale(event.target.value)} />
    </FormRow> : <p className="text-sm text-muted-foreground">No forecast is retained for this cash period. Capture will retain a new period using the selected source dates.</p>}
    {error ? <StatusBanner role="alert" tone="warning" title={<span className="text-foreground">Prepared inputs need attention</span>} detail={error} /> : null}
    <div className="flex flex-wrap gap-2">
      <Button type="submit">Review prepared inputs</Button>
      <Button type="button" variant="outline" onClick={onCancel}>Cancel preparation</Button>
    </div>
  </form>;
}
