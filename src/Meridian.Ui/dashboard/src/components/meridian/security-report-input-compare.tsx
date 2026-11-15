import { useEffect, useId, useState } from "react";
import { Button } from "@/components/ui/button";
import { StatusBanner } from "@/components/ui/status-banner";
import { describeApiError } from "@/lib/api-errors";
import { getSecurityReportInputs, type SecurityReportInputCompare as CompareResponse, type SecurityReportRateValue } from "@/lib/api/security-report-inputs.api";
import { useFundAccountScopeSelection } from "@/lib/operating-scope/scope-picker-context";

function Rate({ value }: { value?: SecurityReportRateValue }) {
  return value ? <div className="space-y-1"><span className="font-mono">{value.rawValue}</span><span className="block text-xs text-muted-foreground">{value.unit ?? "Unit unavailable"}</span>
    <span className="block text-xs text-muted-foreground">{value.normalizedFraction === null ? value.reason : `Fraction ${value.normalizedFraction}`}</span></div> : <span className="text-muted-foreground">Unavailable</span>;
}

/** A report's frozen calculation input and today's canonical definition stay visibly distinct. */
export function SecurityReportInputCompare({ securityId, runId }: { securityId: string; runId?: string | null }) {
  const heading = useId();
  const operatingScope = useFundAccountScopeSelection();
  const [opened, setOpened] = useState(!!runId);
  const [retry, setRetry] = useState(0);
  const [result, setResult] = useState<{ key: string; data?: CompareResponse; error?: string } | null>(null);
  const key = JSON.stringify([securityId, runId, retry, operatingScope?.scope]);
  useEffect(() => {
    if (!opened) return;
    const controller = new AbortController();
    getSecurityReportInputs(securityId, runId, controller.signal).then(data => {
      if (!controller.signal.aborted) setResult(data.securityId === securityId ? { key, data } : { key, error: "Returned report input identity does not match this passport." });
    }).catch(reason => {
      if (!controller.signal.aborted) setResult({ key, error: describeApiError(reason, "Exact retained report inputs are unavailable for the current access.").summary });
    });
    return () => controller.abort();
  }, [key, opened, runId, securityId]);
  const current = result?.key === key ? result : null;
  const data = current?.data;
  return <section className="space-y-3 rounded-md border border-border/70 p-4" aria-labelledby={heading}>
    <div className="flex flex-wrap items-start justify-between gap-3"><div><h3 id={heading} className="text-sm font-semibold">What the report used</h3>
      <p className="mt-1 text-sm text-muted-foreground">Compare exact retained calculation inputs with current canonical terms. Historical timestamp eligibility alone does not establish what a report used.</p></div>
      <Button type="button" size="sm" variant="outline" onClick={() => { setOpened(true); setRetry(value => value + 1); }}>{opened ? "Refresh report inputs" : "Load retained report inputs"}</Button>
    </div>
    {opened && !current ? <p role="status" className="text-sm text-muted-foreground">Loading authorized released-report inputs…</p> : null}
    {current?.error ? <StatusBanner role="alert" tone="warning" title="Exact report inputs unavailable" detail={current.error} /> : null}
    {data ? <>
      <div className="rounded border border-border/60 p-3 text-sm"><span className="font-semibold">Current canonical definition: </span>{data.currentCanonical
        ? `${data.currentCanonical.displayName} · v${data.currentCanonical.version} · effective ${data.currentCanonical.effectiveFrom}` : "Unavailable"}</div>
      {data.gaps.length > 0 ? <StatusBanner tone="warning" title="Provenance gaps" detail={data.gaps.join(" ")} /> : null}
      {data.reports.length === 0 ? <p className="text-sm text-muted-foreground">No matching retained inputs were found among accessible released reports. Any provenance gaps above remain unresolved.</p> : null}
      {data.reports.map(report => <article key={report.runId} className="space-y-3 border-t border-border pt-3">
        <h4 className="text-sm font-semibold">Report {report.runId} · {report.reportAsOf}</h4>
        <p className="text-xs text-muted-foreground">Ledger cutoff {report.sourceCutoffUtc} · released {report.releasedAtUtc}</p>
        {report.gaps.length > 0 ? <StatusBanner tone="warning" title="Exact input evidence incomplete" detail={report.gaps.join(" ")} /> : null}
        {report.inputs.map(input => {
          const fields = [...new Set([...input.rates, ...data.currentRates].map(value => value.field))];
          return <div key={`${input.journalEntryId}:${input.receiptSha256}`} className="space-y-3 rounded border border-border/60 p-3">
            <div className="grid gap-2 text-sm md:grid-cols-2"><p><strong>Report input:</strong> {input.security.displayName} v{input.security.version}</p><p><strong>Current definition:</strong> v{data.currentCanonical?.version ?? "unavailable"}</p></div>
            <p className="break-all text-xs text-muted-foreground">Model {input.modelVersion} · journal {input.journalEntryId}</p>
            <p className="text-xs text-muted-foreground">Recorded timestamp eligibility cutoff: {String(input.calculationContext.KnownAt ?? "not retained")}. This is separate from ledger cutoff and report release.</p>
            {fields.length > 0 ? <div className="overflow-x-auto"><table className="w-full text-left text-sm" aria-label={`Retained versus current rates for ${report.runId}`}>
              <thead><tr className="border-b border-border text-xs text-muted-foreground"><th scope="col" className="p-2">Field</th><th scope="col" className="p-2">Report input · raw / unit / fraction</th><th scope="col" className="p-2">Current · raw / unit / fraction</th></tr></thead>
              <tbody>{fields.map(field => <tr key={field} className="border-b border-border/50"><th scope="row" className="p-2 font-medium">{field}</th><td className="p-2"><Rate value={input.rates.find(value => value.field === field)} /></td><td className="p-2"><Rate value={data.currentRates.find(value => value.field === field)} /></td></tr>)}</tbody>
            </table></div> : <p className="text-sm text-muted-foreground">No comparable scalar rate values were retained. Inspect the exact terms below.</p>}
            <p className="text-sm text-muted-foreground">{input.changeProvenance}</p>
            {input.attribution ? <div className="space-y-2 text-sm" aria-label="Exact-version change attribution">
              <p>Immutable event v{input.attribution.securityVersion}: {input.attribution.eventActor ?? "actor unavailable"} · {input.attribution.eventRecordedAt ?? "recorded time unavailable"}</p>
              {input.attribution.revisionId ? <>
                <p>Revision {input.attribution.revisionId} · drafted by {input.attribution.draftedBy ?? "unavailable"} · submitted by {input.attribution.submittedBy ?? "unavailable"}</p>
                <p>Approved by {input.attribution.approvedBy ?? "unavailable"} · {input.attribution.approvedAt ?? "decision time unavailable"} · decision {input.attribution.approvalId ?? "unavailable"}</p>
                {input.attribution.evidenceRoute ? <a className="underline underline-offset-2" href={input.attribution.evidenceRoute} target="_blank" rel="noreferrer">Open retained approval evidence</a> : null}
              </> : null}
              {input.attribution.gaps.length > 0 ? <StatusBanner tone="warning" title="Change attribution incomplete" detail={input.attribution.gaps.join(" ")} /> : null}
            </div> : <p className="text-sm text-muted-foreground">Exact-version maker and approval attribution is unavailable.</p>}
            <details><summary className="cursor-pointer text-sm font-medium">Exact terms and calculation evidence</summary><div className="mt-2 grid gap-3 md:grid-cols-2">
              <div><h5 className="text-xs font-semibold">Retained terms</h5><pre className="max-h-64 overflow-auto whitespace-pre-wrap break-all text-xs">{JSON.stringify(input.security.assetSpecificTerms, null, 2)}</pre></div>
              <div><h5 className="text-xs font-semibold">Current canonical terms</h5><pre className="max-h-64 overflow-auto whitespace-pre-wrap break-all text-xs">{JSON.stringify(data.currentCanonical?.assetSpecificTerms ?? null, null, 2)}</pre></div>
              <div className="md:col-span-2"><p className="break-all text-xs">Receipt SHA-256 {input.receiptSha256}</p><pre className="max-h-64 overflow-auto whitespace-pre-wrap break-all text-xs">{JSON.stringify({ context: input.calculationContext, source: input.source }, null, 2)}</pre></div>
            </div></details>
          </div>;
        })}
      </article>)}
    </> : null}
  </section>;
}
