import { useEffect, useRef, useState, type FormEvent } from "react";
import { Link } from "react-router-dom";
import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { StatusBanner } from "@/components/ui/status-banner";
import { describeApiError } from "@/lib/api-errors";
import { discoverProviderBonds, stageBondDiscoveryCandidate, type BondDiscoveryCandidate, type BondDiscoveryResult } from "@/lib/api/bond-discovery.api";
import type { DataUploadPreviewResult } from "@/types";
import { WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";

export interface BondDiscoveryPanelProps {
  api?: { discover?: typeof discoverProviderBonds; stage?: typeof stageBondDiscoveryCandidate };
}

/** Lookup -> explicit candidate review -> retained import evidence. No golden-copy mutation. */
export function BondDiscoveryPanel({ api = {} }: BondDiscoveryPanelProps) {
  const discover = api.discover ?? discoverProviderBonds;
  const stage = api.stage ?? stageBondDiscoveryCandidate;
  const [query, setQuery] = useState("");
  const [result, setResult] = useState<BondDiscoveryResult | null>(null);
  const [selected, setSelected] = useState<BondDiscoveryCandidate | null>(null);
  const [reviewed, setReviewed] = useState(false);
  const [reason, setReason] = useState("");
  const [retained, setRetained] = useState<DataUploadPreviewResult | null>(null);
  const [busy, setBusy] = useState<"lookup" | "stage" | null>(null);
  const [error, setError] = useState<string | null>(null);
  const request = useRef<AbortController | null>(null);
  useEffect(() => () => request.current?.abort(), []);

  function clearReview() {
    setSelected(null); setReviewed(false); setReason(""); setRetained(null);
  }
  function editQuery(value: string) {
    request.current?.abort(); setBusy(null); setError(null); setResult(null); clearReview(); setQuery(value);
  }
  async function lookup(event: FormEvent) {
    event.preventDefault();
    request.current?.abort();
    const controller = new AbortController(); request.current = controller;
    setBusy("lookup"); setError(null); setResult(null); clearReview();
    try {
      const response = await discover(query.trim(), { signal: controller.signal });
      if (!controller.signal.aborted) setResult(response);
    } catch (failure) {
      if (!controller.signal.aborted) setError(describeApiError(failure, "Provider bond lookup is unavailable.").summary);
    } finally { if (!controller.signal.aborted) setBusy(null); }
  }
  async function retain() {
    if (!selected || !result?.canStage || !reviewed || !reason.trim() || busy) return;
    const controller = new AbortController(); request.current = controller;
    setBusy("stage"); setError(null);
    try {
      const response = await stage(selected, result.observedAtUtc, reason, { signal: controller.signal });
      if (!controller.signal.aborted) setRetained(response);
    } catch (failure) {
      if (!controller.signal.aborted) setError(`${describeApiError(failure, "Candidate staging could not be confirmed.").summary} Refresh and inspect retained source evidence before retrying an uncertain request.`);
    } finally { if (!controller.signal.aborted) setBusy(null); }
  }
  const columns: DenseDataTableColumn<BondDiscoveryCandidate>[] = [
    {
      id: "bond", label: "Bond", rowHeader: true,
      sortValue: candidate => candidate.name,
      render: candidate => <><span>{candidate.name}</span><div className="font-mono text-xs">{candidate.symbol}</div></>
    },
    {
      id: "provider", label: "Provider / venue",
      sortValue: candidate => candidate.source,
      render: candidate => <>{candidate.source ?? "Unavailable"} / {candidate.exchange ?? "Unavailable"}</>
    },
    { id: "currency", label: "Currency", sortValue: candidate => candidate.currency, render: candidate => candidate.currency ?? "Unavailable" },
    {
      id: "review", label: "Action",
      render: candidate => <Button size="sm" variant="outline" disabled={busy === "stage"} onClick={() => {
        clearReview(); setSelected(candidate); setError(null);
      }}>Review {candidate.symbol}</Button>
    }
  ];
  return <section aria-labelledby="bond-discovery-title" className="grid min-w-0 gap-4 rounded border border-border p-4">
    <div><h3 id="bond-discovery-title" className="font-semibold">Discover provider bonds</h3>
      <p className="text-sm text-muted-foreground">Look up Alpaca bond identities, review a candidate, then stage retained asset information for validation. Provider lookup does not supply approved coupon or maturity terms.</p></div>
    <form onSubmit={lookup} className="flex flex-wrap items-end gap-3">
      <label className="grid gap-1 text-sm">Bond symbol or issuer<Input value={query} maxLength={200} onChange={event => editQuery(event.target.value)} disabled={busy === "stage"} required /></label>
      <Button type="submit" disabled={!query.trim() || busy !== null}>{busy === "lookup" ? "Looking up bonds…" : "Look up provider bonds"}</Button>
    </form>
    {error ? <StatusBanner role="alert" tone="danger" title={<span className="text-foreground">{error}</span>} /> : null}
    {result ? <>
      <p className="text-xs text-muted-foreground">{result.providerLabel} · Observed {result.observedAtUtc}</p>
      {!result.available ? <StatusBanner tone="warning" title={<span className="text-foreground">Provider bond lookup unavailable</span>} detail={result.unavailableReason ?? "No configured bond discovery provider is available."} /> : null}
      {result.available && !result.candidates.length ? <p role="status">No bond candidates returned. Check the query or retry provider lookup.</p> : null}
      {!result.canStage ? <StatusBanner tone="warning" title={<span className="text-foreground">Candidate staging requires Modify Security Master permission.</span>} /> : null}
      {result.candidates.length ? <DenseDataTable
        rows={result.candidates}
        columns={columns}
        getRowId={candidate => `${candidate.source}:${candidate.symbol}`}
        selectedRowId={selected ? `${selected.source}:${selected.symbol}` : null}
        ariaLabel="Provider bond candidates"
        caption={null}
        emptyText="No provider bond candidates returned."
      /> : null}
    </> : null}
    {selected ? <div className="grid gap-3 border-t border-border pt-3" role="region" aria-label="Bond candidate review">
      <h4 className="font-semibold">Review {selected.name}</h4>
      <dl className="grid grid-cols-2 gap-2 text-sm"><dt>Symbol</dt><dd>{selected.symbol}</dd><dt>Provider</dt><dd>{selected.source ?? "Unavailable"}</dd><dt>FIGI</dt><dd>{selected.figi ?? "Unavailable"}</dd><dt>Composite FIGI</dt><dd>{selected.compositeFigi ?? "Unavailable"}</dd><dt>Country</dt><dd>{selected.country ?? "Unavailable"}</dd><dt>Coupon / maturity</dt><dd>Unavailable from discovery; terms review required</dd></dl>
      <label className="grid gap-1 text-sm">Review justification<Input value={reason} disabled={busy === "stage" || retained !== null} onChange={event => { setReason(event.target.value); setReviewed(false); }} /></label>
      <label className="flex items-center gap-2 text-sm"><input type="checkbox" checked={reviewed} disabled={busy === "stage" || retained !== null} onChange={event => setReviewed(event.target.checked)} />I reviewed this provider identity and its missing terms.</label>
      <Button className="justify-self-start" disabled={!result?.canStage || !selected.source || !reviewed || !reason.trim() || busy !== null || retained !== null} onClick={() => void retain()}>{busy === "stage" ? "Staging candidate…" : "Stage candidate import"}</Button>
      {retained ? <StatusBanner role="status" tone={retained.status === "ReadyForReview" ? "success" : "warning"} title={<span className="text-foreground">{retained.status === "ReadyForReview" ? "Candidate import staged for review" : "Candidate retained; schema repair required"}</span>} detail={<><div>Upload {retained.uploadId} · {retained.status} · {retained.retainedPath}</div><div>{retained.nextAction}</div>{retained.issues.map((issue, index) => <div key={index}>{issue.message}</div>)}<Link className="underline" to={WORKSTATION_ROUTE_CATALOG.dataImport}>Open Data intake</Link><div>Security Master terms have not been created or approved.</div></>} /> : null}
    </div> : null}
  </section>;
}
