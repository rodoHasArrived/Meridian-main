import { useEffect, useId, useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { FormGrid, FormRow } from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import { StatusBanner } from "@/components/ui/status-banner";
import { describeApiError } from "@/lib/api-errors";
import { getRateObservations, type SecurityRateObservation } from "@/lib/api/security-rates.api";
import { getBondObservedFixings, retainBondObservedFixing, type BondObservedFixingReceipt } from "@/lib/api/bond-observed-fixings.api";

interface Props {
  securityId: string;
  securityVersion: number | null;
  selectedReceiptIds: readonly string[];
  onSelect: (receipt: BondObservedFixingReceipt) => void;
  onRemove: (receiptId: string) => void;
  loadObservations?: typeof getRateObservations;
  loadRetained?: typeof getBondObservedFixings;
  retain?: typeof retainBondObservedFixing;
}

/** Retains an actual already-reset fixing; it neither creates a forecast curve nor edits coupon conventions. */
export function BondObservedFixingPanel({ securityId, securityVersion, selectedReceiptIds, onSelect, onRemove,
  loadObservations = getRateObservations, loadRetained = getBondObservedFixings, retain = retainBondObservedFixing }: Props) {
  const id = useId();
  const [series, setSeries] = useState("SOFR");
  const [resetDate, setResetDate] = useState("");
  const [observations, setObservations] = useState<SecurityRateObservation[]>([]);
  const [selected, setSelected] = useState("");
  const [receipts, setReceipts] = useState<BondObservedFixingReceipt[]>([]);
  const [justification, setJustification] = useState("");
  const [reviewed, setReviewed] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const request = useRef<AbortController | null>(null);
  const observation = selected === "" ? undefined : observations[Number(selected)];

  useEffect(() => {
    request.current?.abort(); setObservations([]); setSelected(""); setReceipts([]); setReviewed(false); setError(null); setBusy(false);
    return () => request.current?.abort();
  }, [securityId, securityVersion]);

  function edit(change: () => void) {
    request.current?.abort(); setBusy(false); setReviewed(false); setError(null); change();
  }
  async function load() {
    request.current?.abort();
    const current = new AbortController(); request.current = current;
    setBusy(true); setError(null); setSelected(""); setReviewed(false);
    try {
      const [sourceResult, retainedResult] = await Promise.allSettled([
        loadObservations(series, resetDate, resetDate, { signal: current.signal }), loadRetained(securityId, { signal: current.signal })
      ]);
      if (current.signal.aborted) return;
      if (retainedResult.status === "fulfilled") {
        if (retainedResult.value.some(receipt => receipt.fixing.securityId.toLowerCase() !== securityId.toLowerCase())) throw new Error("Observed fixing receipts returned a different security.");
        setReceipts(retainedResult.value);
      }
      if (sourceResult.status === "fulfilled") setObservations(sourceResult.value.filter(row => row.kind === "ObservedFixing" && row.observationDate === resetDate));
      else setError(describeApiError(sourceResult.reason, "The provider is unavailable; retained fixing receipts can still be selected.").summary);
      if (retainedResult.status === "rejected") setError(describeApiError(retainedResult.reason, "Retained fixing receipts could not be loaded.").summary);
    } catch (failure) { if (!current.signal.aborted) setError(describeApiError(failure, "Observed fixing evidence could not be loaded.").summary); }
    finally { if (!current.signal.aborted) setBusy(false); }
  }
  async function save() {
    if (!observation || securityVersion === null || !reviewed) return;
    const current = new AbortController(); request.current = current;
    setBusy(true); setError(null);
    try {
      const receipt = await retain(securityId, { expectedSecurityVersion: securityVersion, resetDate, observation, justification }, { signal: current.signal });
      if (current.signal.aborted) return;
      if (receipt.fixing.securityId.toLowerCase() !== securityId.toLowerCase() || receipt.fixing.securityVersion !== securityVersion || receipt.fixing.resetDate !== resetDate)
        throw new Error("Retained fixing receipt does not match the selected security, version and reset.");
      setReceipts(rows => [receipt, ...rows.filter(row => row.receiptId !== receipt.receiptId)]); setReviewed(false); onSelect(receipt);
    } catch (failure) { if (!current.signal.aborted) setError(describeApiError(failure, "Observed fixing retention was not confirmed. Reload receipts before retrying.").summary); }
    finally { if (!current.signal.aborted) setBusy(false); }
  }

  return <Card id="bond-observed-fixings" tabIndex={-1} aria-label="Retained observed bond fixings">
    <CardHeader><CardTitle>Observed reset fixings</CardTitle><CardDescription>
      Repair an already-reset coupon using a verified observation for its exact contractual reset date. Forecast curves do not supply historical fixings.
    </CardDescription></CardHeader>
    <CardContent className="space-y-3">
      <FormGrid columns={2}>
        <FormRow label="Observed fixing series" labelFor={`${id}-series`}><Input id={`${id}-series`} value={series} onChange={event => edit(() => { setSeries(event.target.value); setObservations([]); setSelected(""); })} /></FormRow>
        <FormRow label="Contractual reset / fixing date" labelFor={`${id}-reset`} hint="Use the missing reset date shown by analytics. Fixing lags require a supported declared mapping; an older observation is never carried forward."><Input type="date" id={`${id}-reset`} value={resetDate} onChange={event => edit(() => { setResetDate(event.target.value); setObservations([]); setSelected(""); })} /></FormRow>
      </FormGrid>
      <Button type="button" variant="outline" disabled={busy || !series.trim() || !resetDate} onClick={() => void load()}>Load observed fixing evidence</Button>
      {error ? <StatusBanner tone="danger" title="Observed fixing needs attention" detail={error} role="alert" /> : null}
      <FormRow label="Verified observed fixing" labelFor={`${id}-observation`}><Select id={`${id}-observation`} value={selected} onChange={event => edit(() => setSelected(event.target.value))}>
        <option value="">Select an exact-date observed fixing</option>
        {observations.map((row, index) => <option key={`${row.seriesId}-${row.vintageDate}-${index}`} value={index}>{row.provider} · {row.seriesId} · {row.observationDate} · {row.originalValue} {row.sourceUnits} · vintage {row.vintageDate ?? "unknown"}</option>)}
      </Select></FormRow>
      {observation ? <p className="break-words text-xs">Observed {observation.observationDate}; original {observation.originalValue} {observation.sourceUnits}; currency {observation.currency}; publication {observation.publishedAt ?? "unavailable"}; retrieved {observation.retrievedAt}. Transformation {observation.transformation}.</p> : null}
      <FormRow label="Observed fixing review justification" labelFor={`${id}-reason`}><Input id={`${id}-reason`} value={justification} onChange={event => edit(() => setJustification(event.target.value))} /></FormRow>
      <label className="flex gap-2 text-xs"><input type="checkbox" checked={reviewed} onChange={event => setReviewed(event.target.checked)} />I reviewed the exact reset, source, units, vintage and current security version.</label>
      <Button type="button" disabled={busy || securityVersion === null || !observation || !reviewed || !justification.trim()} onClick={() => void save()}>Retain and select observed fixing</Button>
      <p className="text-xs text-muted-foreground">Selected immutable receipt IDs are sent explicitly with v3 calculations. Retention does not change a previous calculation; return and calculate again. A recorded cutoff earlier than the receipt remains blocked. Different sources for one reset require an explicit selection.</p>
      <ul className="space-y-2 text-xs" aria-label="Retained observed fixing receipts">{receipts.map(receipt => <li key={receipt.receiptId} className="break-words rounded border p-2">
        <p>{receipt.fixing.indexName} · reset {receipt.fixing.resetDate} · original {receipt.fixing.observation.originalValue} {receipt.fixing.observation.sourceUnits} · annual fraction {receipt.fixing.rateFraction} · {receipt.fixing.observation.provider}</p>
        <p>Security version {receipt.fixing.securityVersion}; recorded {receipt.recordedAt} by {receipt.fixing.recordedBy}; receipt {receipt.receiptId}; SHA-256 {receipt.contentSha256}</p>
        <Button type="button" size="sm" variant="outline" disabled={receipt.fixing.securityVersion !== securityVersion} onClick={() => selectedReceiptIds.includes(receipt.receiptId) ? onRemove(receipt.receiptId) : onSelect(receipt)}>{selectedReceiptIds.includes(receipt.receiptId) ? "Remove observed fixing selection" : "Use this observed fixing"}</Button>
      </li>)}</ul>
    </CardContent>
  </Card>;
}
