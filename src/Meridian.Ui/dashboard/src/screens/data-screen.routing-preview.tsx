import { useEffect, useRef, useState, type FormEvent } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { apiPostJson } from "@/lib/api";
import { UI_API_ROUTES } from "@/lib/ui-api-routes.generated";
import type { ProviderRoutePreviewCandidate, ProviderRoutePreviewRequest, ProviderRoutePreviewResponse } from "@/types";

const ROUTE_COLUMNS: DenseDataTableColumn<ProviderRoutePreviewCandidate>[] = [
  { id: "provider", label: "Provider", rowHeader: true, render: (candidate) => candidate.providerFamilyId },
  { id: "priority", label: "Priority", numeric: true, render: (candidate) => candidate.priority },
  { id: "evidence", label: "Evidence", render: (candidate) => candidate.reasonCodes.join(" ") }
];

/** Deliberately lazy: no host configuration or credential reads until the operator requests a preview. */
export function DataRoutingPreviewPanel() {
  const [symbol, setSymbol] = useState("AAPL");
  const [from, setFrom] = useState("2019-01-01");
  const [to, setTo] = useState("2024-12-31");
  const [provider, setProvider] = useState("");
  const [priority, setPriority] = useState("");
  const [enabled, setEnabled] = useState("saved");
  const [busy, setBusy] = useState(false);
  const [result, setResult] = useState<ProviderRoutePreviewResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const active = useRef<AbortController | null>(null);
  const generation = useRef(0);
  useEffect(() => () => active.current?.abort(), []);

  function invalidate() {
    generation.current += 1;
    active.current?.abort();
    setResult(null);
    setError(null);
    setBusy(false);
  }

  async function preview(event: FormEvent) {
    event.preventDefault();
    const version = ++generation.current;
    active.current?.abort();
    const controller = new AbortController();
    active.current = controller;
    setBusy(true);
    setResult(null);
    setError(null);
    const request: ProviderRoutePreviewRequest = {
      capability: "HistoricalBars", previewBackfill: true, symbol: symbol.trim(), from, to, granularity: "daily",
      ...(provider.trim() ? { proposedConfig: { backfillProviders: [{
        providerId: provider.trim().toLowerCase(),
        ...(priority !== "" ? { priority: Number(priority) } : {}),
        ...(enabled !== "saved" ? { enabled: enabled === "enabled" } : {})
      }] } } : {})
    };
    try {
      const next = await apiPostJson<ProviderRoutePreviewResponse>(UI_API_ROUTES.ProviderRoutingPreview, request,
        { signal: controller.signal, allowDevelopmentFallback: false });
      if (version === generation.current) setResult(next);
    } catch (failure) {
      if (version === generation.current && !controller.signal.aborted)
        setError(failure instanceof Error ? failure.message : "Routing preview is unavailable.");
    } finally {
      if (version === generation.current) setBusy(false);
    }
  }

  return (
    <details className="rounded-md border border-border bg-card p-4">
      <summary className="cursor-pointer font-medium">Preview historical routing before saving</summary>
      <p className="mt-3 text-sm text-muted-foreground">Inspect the saved provider chain or a proposed priority and availability change. No data is fetched or settings saved.</p>
      <form className="mt-4 space-y-3" onSubmit={(event) => void preview(event)} onChange={invalidate}>
        <div className="grid gap-3 sm:grid-cols-3">
          <label className="text-sm">Symbol<Input aria-label="Routing symbol" value={symbol} onChange={(event) => setSymbol(event.target.value)} pattern="[A-Za-z0-9.\-]{1,20}" required /></label>
          <label className="text-sm">From<Input aria-label="Routing from date" type="date" value={from} onChange={(event) => setFrom(event.target.value)} required /></label>
          <label className="text-sm">Through<Input aria-label="Routing through date" type="date" value={to} min={from} onChange={(event) => setTo(event.target.value)} required /></label>
        </div>
        <fieldset className="grid gap-3 rounded border border-border p-3 sm:grid-cols-3">
          <legend className="px-1 text-sm">Optional proposed change</legend>
          <label className="text-sm">Provider ID<Input aria-label="Proposed routing provider" placeholder="e.g. tiingo" value={provider} onChange={(event) => setProvider(event.target.value)} /></label>
          <label className="text-sm">Priority<Input aria-label="Proposed routing priority" type="number" min={0} max={10000} step={1} placeholder="Use saved priority" value={priority} onChange={(event) => setPriority(event.target.value)} disabled={!provider.trim()} /></label>
          <label className="text-sm">Availability<select aria-label="Proposed routing availability" className="h-9 w-full rounded border border-input bg-background px-2" value={enabled} onChange={(event) => setEnabled(event.target.value)} disabled={!provider.trim()}><option value="saved">Use saved setting</option><option value="enabled">Enabled</option><option value="disabled">Disabled</option></select></label>
        </fieldset>
        <Button type="submit" size="sm" disabled={busy}>{busy ? "Previewing…" : "Preview route"}</Button>
      </form>
      {error ? <p role="alert" className="mt-3 text-sm text-destructive">{error}</p> : null}
      {result ? <div className="mt-4 space-y-3" role="status" aria-live="polite">
        <p className="text-sm font-medium">{result.isProposed ? "Proposed" : "Saved"} configuration: {result.selectedProviderFamilyId ? `first eligible provider ${result.selectedProviderFamilyId}` : "no eligible provider"}</p>
        {(result.warnings ?? []).map((warning, index) => <p key={index} className={warning.startsWith("FABRICATED") ? "text-sm font-medium text-destructive" : "text-sm text-muted-foreground"}>{warning}</p>)}
        <DenseDataTable columns={ROUTE_COLUMNS} rows={result.candidates} getRowId={(candidate) => candidate.connectionId}
          ariaLabel="Historical provider route candidates" emptyText="No eligible provider candidates." />
      </div> : null}
    </details>
  );
}
