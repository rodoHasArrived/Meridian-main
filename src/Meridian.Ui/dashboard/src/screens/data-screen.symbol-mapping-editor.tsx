import { useCallback, useEffect, useId, useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { StatusBanner } from "@/components/ui/status-banner";
import { useRequestLifecycle } from "@/hooks/use-request-lifecycle";
import { symbolMappingApi, type SymbolMapping, type SymbolMappingRollout } from "@/lib/api/symbol-mapping.api";

const emptyMapping: SymbolMapping = { canonicalSymbol: "", ibSymbol: null, alpacaSymbol: null, polygonSymbol: null, yahooSymbol: null, name: null, figi: null };
const providerFields = [["ibSymbol", "IB"], ["alpacaSymbol", "Alpaca"], ["polygonSymbol", "Polygon"], ["yahooSymbol", "Yahoo"]] as const;

export function SymbolMappingEditor({ api = symbolMappingApi, onChanged }: { api?: typeof symbolMappingApi; onChanged?: () => void }) {
  const id = useId();
  const [mappings, setMappings] = useState<SymbolMapping[]>([]);
  const [rollout, setRollout] = useState<SymbolMappingRollout | null>(null);
  const [draft, setDraft] = useState<SymbolMapping>(emptyMapping);
  const [provider, setProvider] = useState("yahoo");
  const [reason, setReason] = useState("");
  const [reviewed, setReviewed] = useState(false);
  const [archivesConfirmed, setArchivesConfirmed] = useState(false);
  const [seriesConfirmed, setSeriesConfirmed] = useState(false);
  const [deleteConfirmed, setDeleteConfirmed] = useState(false);
  const [file, setFile] = useState<File | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const { status, start, succeed, fail, finish } = useRequestLifecycle({ operation: "symbol-mapping-editor", failureMessage: "The symbol mapping operation failed. Refresh before retrying an uncertain write." });
  const resetReview = useCallback(() => { setReviewed(false); setArchivesConfirmed(false); setSeriesConfirmed(false); setReason(""); }, []);
  const refresh = useCallback(async () => {
    const request = start({ busyMode: "drop" });
    if (!request) return;
    try {
      const [nextMappings, nextRollout] = await Promise.all([api.list({ signal: request.signal }), api.rollout({ signal: request.signal })]);
      if (!request.isCurrent()) return;
      setMappings(nextMappings); setRollout(nextRollout); resetReview(); succeed(request);
    } catch (error) { if (request.isCurrent()) { setRollout(null); fail(request, error); } }
    finally { finish(request); }
  }, [api, start, succeed, fail, finish, resetReview]);
  useEffect(() => { void refresh(); }, [refresh]);

  async function mutate(operation: (signal: AbortSignal) => Promise<unknown>, success: string) {
    const request = start({ busyMode: "drop" });
    if (!request) return;
    setMessage(null);
    try {
      await operation(request.signal);
      window.dispatchEvent(new Event("meridian:configuration-saved"));
      if (!request.isCurrent()) return;
      const [nextMappings, nextRollout] = await Promise.all([api.list({ signal: request.signal }), api.rollout({ signal: request.signal })]);
      if (!request.isCurrent()) return;
      setMappings(nextMappings); setRollout(nextRollout); setMessage(success); resetReview(); setDeleteConfirmed(false); succeed(request); onChanged?.();
    } catch (error) { if (request.isCurrent()) { setRollout(null); fail(request, error); } }
    finally { finish(request); }
  }
  const selected = rollout?.providers.find(row => row.provider === provider);
  const busy = status.inFlight;
  const available = rollout !== null && !busy;
  const existing = mappings.some(row => row.canonicalSymbol === draft.canonicalSymbol.trim().toUpperCase());
  function selectMapping(symbol: string) {
    setDraft(mappings.find(row => row.canonicalSymbol === symbol) ?? { ...emptyMapping, canonicalSymbol: symbol });
    setDeleteConfirmed(false); setMessage(null); resetReview();
  }

  return <div className="grid gap-4 rounded-md border p-4" aria-label="Symbol mapping editor">
    <div className="flex flex-wrap items-center justify-between gap-2">
      <h3 className="font-semibold">Edit provider symbol mappings</h3>
      <Button type="button" variant="outline" size="sm" disabled={busy} onClick={() => void refresh()}>Refresh mapping evidence</Button>
    </div>
    <p className="text-sm text-muted-foreground">Mapping edits update registry aliases for future requests. Review comparison evidence before promoting each provider. Existing archives retain their original symbols and provenance.</p>
    {status.error ? <StatusBanner tone="danger" title="Mapping change unavailable" detail={status.error} /> : null}
    {message ? <StatusBanner tone="success" title="Mapping change saved" detail={message} /> : null}
    {!rollout && !status.error ? <p role="status">Loading mappings and rollout evidence…</p> : null}
    <form className="grid gap-3" onSubmit={event => { event.preventDefault(); void mutate(signal => api.save(draft, { signal }), "Registry mapping saved. Compare evidence must be reviewed again after edits."); }}>
      <fieldset disabled={!available} className="grid gap-3">
        <legend className="text-sm font-semibold">1. Correct aliases</legend>
        <label className="grid gap-1 text-sm" htmlFor={`${id}-existing`}>Existing mapping
          <select id={`${id}-existing`} className="rounded-md border bg-background p-2" value={existing ? draft.canonicalSymbol : ""} onChange={event => selectMapping(event.currentTarget.value)}>
            <option value="">New mapping</option>{mappings.map(row => <option key={row.canonicalSymbol} value={row.canonicalSymbol}>{row.canonicalSymbol}</option>)}
          </select>
        </label>
        <label className="grid gap-1 text-sm" htmlFor={`${id}-canonical`}>Canonical symbol
          <Input id={`${id}-canonical`} required value={draft.canonicalSymbol} onChange={event => selectMapping(event.currentTarget.value.toUpperCase())} />
        </label>
        <div className="grid gap-3 md:grid-cols-2">{providerFields.map(([field, name]) => <label key={field} className="grid gap-1 text-sm" htmlFor={`${id}-${field}`}>{name} symbol
          <Input id={`${id}-${field}`} value={draft[field] ?? ""} onChange={event => { const value = event.currentTarget.value; setDraft(current => ({ ...current, [field]: value || null })); resetReview(); }} />
        </label>)}</div>
        <p className="text-sm text-muted-foreground">Blank aliases preserve existing overrides. Removing overrides restores the registry fallback for all four providers.</p>
        <div><Button type="submit" disabled={!draft.canonicalSymbol.trim()}>Save symbol mapping</Button></div>
        {existing ? <div className="grid gap-2">
          <label className="flex items-start gap-2 text-sm"><input type="checkbox" checked={deleteConfirmed} onChange={event => setDeleteConfirmed(event.currentTarget.checked)} />Remove all operator overrides for {draft.canonicalSymbol}; retain canonical identity.</label>
          <div><Button type="button" variant="outline" disabled={!deleteConfirmed} onClick={() => void mutate(signal => api.remove(draft.canonicalSymbol, { signal }), "Operator overrides removed; canonical identity retained.")}>Remove provider overrides</Button></div>
        </div> : null}
      </fieldset>
    </form>
    <fieldset disabled={!available} className="grid gap-2 rounded-md border p-3">
      <legend className="text-sm font-semibold">Import CSV mappings</legend>
      <p className="text-sm text-muted-foreground">Columns: canonical (or symbol), ib, alpaca, polygon, yahoo, name, figi. Imports can update existing aliases.</p>
      <label className="grid gap-1 text-sm" htmlFor={`${id}-csv`}>Mapping CSV file<input id={`${id}-csv`} type="file" accept=".csv,text/csv" onChange={event => setFile(event.currentTarget.files?.[0] ?? null)} /></label>
      <div><Button type="button" variant="outline" disabled={!file} onClick={() => file && void mutate(signal => api.importCsv(file, { signal }), "CSV mappings imported. Refresh and review comparisons before promotion.")}>Import mappings</Button></div>
    </fieldset>
    <fieldset disabled={!available} className="grid gap-3 rounded-md border p-3">
      <legend className="text-sm font-semibold">2. Compare → Canonical rollout</legend>
      <label className="grid gap-1 text-sm" htmlFor={`${id}-provider`}>Rollout provider
        <select id={`${id}-provider`} className="rounded-md border bg-background p-2" value={provider} onChange={event => { setProvider(event.currentTarget.value); resetReview(); }}>
          {providerFields.map(([field, name]) => <option key={field} value={field.replace("Symbol", "")}>{name}</option>)}
        </select>
      </label>
      {selected ? <>
        <p className="text-sm">Active: <strong>{selected.activeMode ?? "Unavailable"}</strong> · Saved: <strong>{selected.savedMode}</strong>{selected.pendingRestart ? " · Pending host restart" : ""}</p>
        <p className="text-sm text-muted-foreground">Compare records disagreements while returning legacy output. Restart the host after saving a mode, then refresh to verify the active mode.</p>
        <div className="text-sm"><h4 className="font-semibold">Retained {provider} comparison mismatches</h4>
          {selected.recentMismatches.length ? <ul className="mt-2 grid gap-2">{selected.recentMismatches.map(row => <li key={`${row.observedAt}:${row.input}:${row.fromProvider}`} className="rounded-md border p-2"><span className="font-mono">{row.input}: {row.legacyResult ?? "∅"} → {row.canonicalResult ?? "∅"}</span><div className="text-muted-foreground">{row.fromProvider} → {row.toProvider} · {row.observedAt}</div></li>)}</ul> : <p>No mismatch retained for this provider. Absence of mismatches does not prove provider coverage.</p>}
        </div>
        {selected.savedMode !== "Compare" ? <div><Button type="button" variant="outline" disabled={!selected.activeMode} onClick={() => void mutate(signal => api.saveRollout(provider, { mode: "Compare", expectedMode: selected.savedMode, reviewedMismatchCount: rollout!.mismatchCount, evidenceReviewed: false, confirmArchivesNotRewritten: false, confirmSeriesRemainSeparate: false, reason: "Return provider to comparison" }, { signal }), "Compare mode saved. Restart the host, then refresh to verify it is active.")}>Save Compare mode</Button></div> : <>
          <label className="flex items-start gap-2 text-sm"><input type="checkbox" checked={reviewed} onChange={event => setReviewed(event.currentTarget.checked)} />I reviewed this provider’s comparison evidence and coverage.</label>
          <label className="flex items-start gap-2 text-sm"><input type="checkbox" checked={archivesConfirmed} onChange={event => setArchivesConfirmed(event.currentTarget.checked)} />I understand existing archives are not rewritten.</label>
          <label className="flex items-start gap-2 text-sm"><input type="checkbox" checked={seriesConfirmed} onChange={event => setSeriesConfirmed(event.currentTarget.checked)} />I will keep raw and adjusted series separate.</label>
          <label className="grid gap-1 text-sm" htmlFor={`${id}-reason`}>Promotion reason<Input id={`${id}-reason`} value={reason} onChange={event => setReason(event.currentTarget.value)} /></label>
          <div><Button type="button" disabled={!reviewed || !archivesConfirmed || !seriesConfirmed || !reason.trim() || selected.activeMode !== "Compare" || selected.pendingRestart} onClick={() => void mutate(signal => api.saveRollout(provider, { mode: "Canonical", expectedMode: selected.savedMode, reviewedMismatchCount: rollout!.mismatchCount, evidenceReviewed: reviewed, confirmArchivesNotRewritten: archivesConfirmed, confirmSeriesRemainSeparate: seriesConfirmed, reason: reason.trim() }, { signal }), "Canonical mode saved for this provider only. Pending host restart; refresh afterwards to verify the active mode.")}>Promote {provider} to Canonical</Button></div>
        </>}
      </> : <p className="text-sm">Rollout status is unavailable.</p>}
    </fieldset>
  </div>;
}
