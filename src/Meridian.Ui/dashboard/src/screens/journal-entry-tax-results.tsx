import { useEffect, useState } from "react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { StatusBanner } from "@/components/ui/status-banner";
import { formatCurrency, formatNumber } from "@/lib/format";
import { getLedgerJournalEntryTaxResults, type LedgerTaxResultScope } from "@/lib/ledger-tax-results-api";
import type { LedgerDisposalTaxResult, LedgerJournalTaxResults, LedgerDisposalTaxParcel } from "@/types/ledger-tax-results";

const characterLabels = { ShortTerm: "Short term", LongTerm: "Long term", Mixed: "Mixed parcels" };
const stateLabels = { Settled: "Settled", Provisional: "Provisional", MissingEvidence: "Missing evidence" };

export function JournalEntryTaxResults({ ledgerBookId, periodId, journalEntryId }: LedgerTaxResultScope) {
  const [result, setResult] = useState<LedgerJournalTaxResults | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [refreshVersion, setRefreshVersion] = useState(0);

  useEffect(() => {
    const controller = new AbortController();
    setResult(null);
    setLoading(true);
    setError(null);
    getLedgerJournalEntryTaxResults({ ledgerBookId, periodId, journalEntryId }, { signal: controller.signal })
      .then((response) => { if (!controller.signal.aborted) setResult(response); })
      .catch(() => {
        if (!controller.signal.aborted) setError("Retained tax results could not be loaded. Tax character and amounts are unavailable until the source responds.");
      })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [journalEntryId, ledgerBookId, periodId, refreshVersion]);

  // Scope changes must hide outgoing evidence on the first render, before effect cleanup runs.
  const current = result?.journalEntryId.toLowerCase() === journalEntryId.toLowerCase()
    && result?.periodId.toLowerCase() === periodId.toLowerCase()
    && result?.ledgerBookId.toLowerCase() === ledgerBookId.toLowerCase() ? result : null;

  return (
    <Card className="panel-surface" aria-labelledby="journal-tax-results-title">
      <CardHeader>
        <div className="flex flex-wrap items-center justify-between gap-3">
          <CardTitle id="journal-tax-results-title">Disposal tax results</CardTitle>
          <Button size="sm" variant="outline" disabled={loading} onClick={() => setRefreshVersion((value) => value + 1)}>
            {error ? "Retry tax results" : "Refresh retained results"}
          </Button>
        </div>
        <CardDescription>Retained server results for this posting. Refresh reads the latest evidence; it does not re-evaluate or change a disposal.</CardDescription>
      </CardHeader>
      <CardContent className="space-y-4" aria-busy={loading}>
        {loading ? <p role="status" className="text-sm text-muted-foreground">Loading retained tax results…</p> : error ? (
          <StatusBanner role="alert" tone="danger" title="Tax results unavailable" detail={error} />
        ) : current ? (
          <>
            {current.evidenceState === "MissingEvidence" ? (
              <StatusBanner role="status" tone="warning" title="Missing evidence" detail={current.message} />
            ) : current.disposals.length === 0 ? (
              <p role="status" className="text-sm text-muted-foreground">{current.message || "No retained disposal tax result for this journal entry."}</p>
            ) : null}
            <p className="text-xs text-muted-foreground">Evidence checked {retainedTime(current.evaluatedAt)} · Amounts in {current.functionalCurrency || "unrecorded currency"}</p>
            {current.disposals.map((disposal) => (
              <DisposalResult key={disposal.mutationBatchId} disposal={disposal} currency={current.functionalCurrency} />
            ))}
          </>
        ) : null}
      </CardContent>
    </Card>
  );
}

function DisposalResult({ disposal, currency }: { disposal: LedgerDisposalTaxResult; currency: string }) {
  return (
    <section aria-label={`${disposal.symbol ?? disposal.accountName} disposal on ${disposal.saleDate}`} className="space-y-3 rounded-md border border-border p-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h3 className="text-sm font-semibold">{disposal.symbol ?? "Disposal"} · {disposal.accountName} · {disposal.saleDate}</h3>
        <Badge variant={disposal.state === "Settled" ? "success" : "warning"}>{stateLabels[disposal.state]}</Badge>
      </div>
      <StatusBanner
        tone={disposal.state === "Settled" ? "success" : "warning"}
        title={disposal.state === "MissingEvidence" ? "Tax result needs evidence" : disposal.canChange ? "This result can still change" : "Settled under the retained policy"}
        detail={disposal.stateReason}
      />
      <dl className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
        <TaxFact label="Tax character" value={disposal.character ? characterLabels[disposal.character] : "Missing evidence"} />
        <TaxFact label="Economic gain / loss" value={amount(disposal.economicGainOrLoss, currency)} />
        <TaxFact label="Recognized gain / loss" value={amount(disposal.recognizedGainOrLoss, currency)} />
        <TaxFact label="Deferred loss" value={amount(disposal.deferredLoss, currency)} />
        <TaxFact label="Exact policy revision" value={disposal.policyRevision ?? "Missing evidence"} />
        <TaxFact label="Retained at" value={disposal.recordedAt ? retainedTime(disposal.recordedAt) : "Missing evidence"} />
        <TaxFact label="Replacement window ends" value={disposal.replacementWindowEnd ?? (disposal.state === "MissingEvidence" ? "Cannot determine — missing evidence" : "Not applicable")} />
        <TaxFact label="Re-evaluation" value={disposal.reEvaluationRequired ? "Required — awaiting retained evidence" : disposal.state === "MissingEvidence" ? "Cannot determine — missing evidence" : "Not required by current evidence"} />
        <TaxFact label="Relief method" value={disposal.reliefMethod} />
        <TaxFact label="Disposal evidence" value={disposal.mutationBatchId} />
        <TaxFact label="Can still change" value={disposal.canChange ? "Yes" : "No"} />
      </dl>
      {disposal.parcels.length > 0 ? (
        <div className="space-y-2">
          <h4 className="text-sm font-semibold">Relieved parcels</h4>
          {disposal.parcels.map((parcel, index) => (
            <ParcelResult key={`${parcel.lotId}-${index}`} parcel={parcel} currency={currency} />
          ))}
        </div>
      ) : <p className="text-sm text-muted-foreground">Parcel holding-period evidence is unavailable.</p>}
    </section>
  );
}

function ParcelResult({ parcel, currency }: { parcel: LedgerDisposalTaxParcel; currency: string }) {
  return (
    <details className="rounded-md border border-border/70 bg-secondary/15 px-3 py-2">
      <summary className="cursor-pointer text-sm leading-6 focus-visible:outline focus-visible:outline-2 focus-visible:outline-primary">
        <span className="break-all font-mono">{parcel.lotId}</span> · {characterLabels[parcel.character]} · Holding start {parcel.holdingPeriodStart}
        {parcel.holdingPeriodCarried ? " · Carried holding period" : ""}
      </summary>
      <dl className="mt-3 grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
        <TaxFact label="Acquired" value={parcel.acquiredDate} />
        <TaxFact label="Holding-period start" value={parcel.holdingPeriodStart} />
        <TaxFact label="Holding-period days" value={formatNumber(parcel.holdingPeriodDays, { maximumFractionDigits: 0 })} />
        <TaxFact label="Holding period carried" value={parcel.holdingPeriodCarried ? "Yes" : "No"} />
        <TaxFact label="Tax character" value={characterLabels[parcel.character]} />
        <TaxFact label="Quantity relieved" value={formatNumber(parcel.quantity, { maximumFractionDigits: 8 })} />
        <TaxFact label="Proceeds" value={amount(parcel.proceeds, currency)} />
        <TaxFact label="Cost basis" value={amount(parcel.costBasis, currency)} />
        <TaxFact label="Economic gain / loss" value={amount(parcel.economicGainOrLoss, currency)} />
        <TaxFact label="Recognized gain / loss" value={amount(parcel.recognizedGainOrLoss, currency)} />
        <TaxFact label="Deferred loss" value={amount(parcel.deferredLoss, currency)} />
      </dl>
    </details>
  );
}

function TaxFact({ label, value }: { label: string; value: string }) {
  return <div className="min-w-0"><dt className="text-xs text-muted-foreground">{label}</dt><dd className="mt-1 break-words font-mono text-sm">{value}</dd></div>;
}

function amount(value: number | null, currency: string) {
  return value === null ? "Missing evidence" : currency
    ? formatCurrency(value, { currency })
    : formatNumber(value, { minimumFractionDigits: 2 });
}

function retainedTime(value: string) {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : `${date.toLocaleString("en-US", {
    year: "numeric", month: "short", day: "numeric", hour: "2-digit", minute: "2-digit", second: "2-digit", hour12: false, timeZone: "UTC"
  })} UTC`;
}
