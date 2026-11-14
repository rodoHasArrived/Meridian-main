import { useCallback, useEffect, useRef, useState } from "react";
import { apiGetJson, apiPutJson } from "@/lib/api";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { StatusBanner } from "@/components/ui/status-banner";

interface VendorTier {
  id: string; label: string; rateLimitPerMinute: number | null; rateLimitPerHour: number | null;
}
interface ProviderOptions {
  enabled: boolean; priority: number; tierId: string;
  rateLimitPerMinute: number | null; rateLimitPerHour: number | null;
}
interface ProviderStatus {
  metadata: { providerId: string; displayName: string; tiers: VendorTier[] };
  options: ProviderOptions;
}

/** The host owns tier defaults and validates caps again on save. No credential fields are accepted. */
export function BackfillProviderEditorRegion() {
  const [open, setOpen] = useState(false);
  const [providers, setProviders] = useState<ProviderStatus[]>([]);
  const [selected, setSelected] = useState<string | null>(null);
  const [draft, setDraft] = useState<ProviderOptions | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [dragged, setDragged] = useState<string | null>(null);
  const requests = useRef(new Set<AbortController>());
  const mounted = useRef(true);
  useEffect(() => { mounted.current = true; const activeRequests = requests.current; return () => { mounted.current = false; activeRequests.forEach(controller => controller.abort()); }; }, []);
  const provider = providers.find(p => p.metadata.providerId === selected);
  const dirty = !!provider && JSON.stringify(provider.options) !== JSON.stringify(draft);
  const tier = provider?.metadata.tiers.find(t => t.id === draft?.tierId);
  const refresh = useCallback(async (signal?: AbortSignal) => {
    setLoading(true); setError(null);
    try {
      const data = await apiGetJson<ProviderStatus[]>("/api/backfill/providers/statuses", { signal, allowDevelopmentFallback: false });
      if (mounted.current && !signal?.aborted) setProviders(data);
    } catch (cause) {
      if (mounted.current && !signal?.aborted) setError(cause instanceof Error ? cause.message : "Provider configuration is unavailable.");
    } finally { if (mounted.current && !signal?.aborted) setLoading(false); }
  }, []);
  useEffect(() => { if (!open) return; const controller = new AbortController(); const activeRequests = requests.current; activeRequests.add(controller); void refresh(controller.signal); return () => { controller.abort(); activeRequests.delete(controller); }; }, [open, refresh]);

  function select(item: ProviderStatus, priority?: number) {
    if (dirty) {
      setError("Save or discard the current provider changes before editing another provider."); return;
    }
    setSelected(item.metadata.providerId);
    setDraft({ ...item.options, ...(priority !== undefined ? { priority } : {}) });
    setError(null); setNotice(null);
  }
  async function save() {
    if (!draft || !selected) return;
    setSaving(true); setError(null); setNotice(null);
    const controller = new AbortController(); requests.current.add(controller);
    try {
      const result = await apiPutJson<{ changeImpact?: { impact?: string; requiresRestart?: boolean; requiresReconnect?: boolean } }>(`/api/backfill/providers/${encodeURIComponent(selected)}`, draft, { signal: controller.signal });
      if (!mounted.current || controller.signal.aborted) return;
      setProviders(current => current.map(p => p.metadata.providerId === selected ? { ...p, options: draft } : p));
      const impact = result.changeImpact;
      setNotice(impact?.requiresRestart ? "Saved. Pending host restart; active requests keep their existing limits." : impact?.requiresReconnect ? "Saved. Pending provider reconnect; active requests keep their existing limits." : `Saved. Change impact: ${impact?.impact ?? "awaiting host status"}.`);
      window.dispatchEvent(new Event("meridian:configuration-saved"));
    } catch (cause) { if (mounted.current && !controller.signal.aborted) setError(cause instanceof Error ? cause.message : "Provider settings were not saved."); }
    finally { requests.current.delete(controller); if (mounted.current && !controller.signal.aborted) setSaving(false); }
  }
  const ordered = [...providers].sort((a, b) => a.options.priority - b.options.priority);
  const invalid = !!draft && (!Number.isInteger(draft.priority) || draft.priority < 0 || draft.priority > 10000 ||
    (draft.rateLimitPerMinute !== null && (!Number.isInteger(draft.rateLimitPerMinute) || draft.rateLimitPerMinute < 1 || draft.rateLimitPerMinute > (tier?.rateLimitPerMinute ?? 0))) ||
    (draft.rateLimitPerHour !== null && (!Number.isInteger(draft.rateLimitPerHour) || draft.rateLimitPerHour < 1 || draft.rateLimitPerHour > (tier?.rateLimitPerHour ?? 0))));
  return <section className="workspace-region" aria-labelledby="backfill-provider-editor-title">
    <Card>
      <CardHeader>
        <CardTitle id="backfill-provider-editor-title">Backfill provider priority and limits</CardTitle>
        <CardDescription>Select your subscribed vendor tier, then lower its limits if needed. Drag a provider before another, or edit its numeric priority. Lower numbers run first.</CardDescription>
        <Button type="button" variant="outline" size="sm" aria-expanded={open} disabled={saving || dirty} onClick={() => setOpen(value => !value)}>{open ? "Close provider editor" : "Edit provider priorities and limits"}</Button>
      </CardHeader>
      {open ? <CardContent className="space-y-4">
        {error ? <StatusBanner tone="danger" title="Provider settings unavailable" detail={error} /> : null}
        {notice ? <StatusBanner role="status" tone="warning" title="Provider settings saved" detail={notice} /> : null}
        {loading ? <p role="status">Loading provider configuration…</p> : null}
        {!loading && providers.length === 0 ? <p>No provider configuration is available.</p> : null}
        <div className="grid gap-4 lg:grid-cols-2">
          <ol className="space-y-1" aria-label="Configured provider priority">
            {ordered.map((item, index) => <li key={item.metadata.providerId} draggable={!saving && !dirty}
              onDragStart={() => setDragged(item.metadata.providerId)} onDragEnd={() => setDragged(null)}
              onDragOver={event => event.preventDefault()} onDrop={event => {
                event.preventDefault();
                const moved = providers.find(p => p.metadata.providerId === dragged);
                if (moved && moved !== item && !dirty) select(moved, Math.max(0, item.options.priority - 1));
                setDragged(null);
              }} className="flex flex-wrap items-center gap-2 rounded border p-2">
              <Button type="button" variant={selected === item.metadata.providerId ? "secondary" : "ghost"} size="sm"
                onClick={() => select(item)} disabled={saving} aria-pressed={selected === item.metadata.providerId}>
                {item.metadata.displayName}
              </Button>
              <span className="font-mono text-xs">Priority {item.options.priority} · {item.options.enabled ? "Enabled" : "Disabled"}</span>
              {index > 0 && item.metadata.providerId !== "openfigi" ? <Button type="button" size="sm" variant="ghost"
                disabled={saving || dirty} aria-label={`Move ${item.metadata.displayName} before ${ordered[index - 1].metadata.displayName}`}
                onClick={() => select(item, Math.max(0, ordered[index - 1].options.priority - 1))}>Move up</Button> : null}
            </li>)}
          </ol>
          {draft && provider ? <form onSubmit={event => { event.preventDefault(); void save(); }} className="space-y-3 rounded border p-4" aria-label={`${provider.metadata.displayName} options`}>
            <h4 className="font-semibold">{provider.metadata.displayName}</h4>
            <label className="flex items-center gap-2"><input type="checkbox" checked={draft.enabled} disabled={saving}
              onChange={event => setDraft({ ...draft, enabled: event.target.checked })} />Enabled</label>
            <label className="block text-sm">Vendor tier
              <select className="mt-1 block w-full rounded border bg-background p-2" value={draft.tierId} disabled={saving}
                onChange={event => { const chosen = provider.metadata.tiers.find(t => t.id === event.target.value); if (chosen) setDraft({ ...draft, tierId: chosen.id, rateLimitPerMinute: chosen.rateLimitPerMinute, rateLimitPerHour: chosen.rateLimitPerHour }); }}>
                {provider.metadata.tiers.map(t => <option key={t.id} value={t.id}>{t.label}</option>)}
              </select>
            </label>
            {selected !== "openfigi" ? <label className="block text-sm">Priority<Input type="number" min={0} max={10000} step={1} value={draft.priority} disabled={saving}
              onChange={event => setDraft({ ...draft, priority: Number(event.target.value) })} /></label> : <p className="text-xs text-muted-foreground">Identifier resolution only; OpenFIGI is outside the historical fallback chain.</p>}
            {tier?.rateLimitPerMinute != null ? <label className="block text-sm">Requests per minute (ceiling {tier.rateLimitPerMinute})<Input type="number" min={1} max={tier.rateLimitPerMinute} step={1}
              value={draft.rateLimitPerMinute ?? ""} disabled={saving} onChange={event => setDraft({ ...draft, rateLimitPerMinute: Number(event.target.value) })} /></label> : null}
            {tier?.rateLimitPerHour != null ? <label className="block text-sm">Requests per hour (ceiling {tier.rateLimitPerHour})<Input type="number" min={1} max={tier.rateLimitPerHour} step={1}
              value={draft.rateLimitPerHour ?? ""} disabled={saving} onChange={event => setDraft({ ...draft, rateLimitPerHour: Number(event.target.value) })} /></label> : null}
            <p className="text-xs text-muted-foreground">Changing a tier declares your subscription; it does not purchase or verify entitlement. Credentials and IB pacing are managed separately.</p>
            {selected === "openfigi" ? <p className="text-xs text-muted-foreground">OpenFIGI with runtime-managed credentials keeps an effective cap of at most 25 requests/minute, including the authenticated plan. The host retains this conservative cap when credentials can rotate or disappear.</p> : null}
            {invalid ? <p role="alert">Use an integer priority and positive limits within the selected tier ceiling.</p> : null}
            <div className="flex flex-wrap gap-2"><Button type="submit" disabled={saving || !dirty || invalid}>{saving ? "Saving…" : "Save provider"}</Button>
              <Button type="button" variant="outline" disabled={saving || !dirty} onClick={() => { setDraft({ ...provider.options }); setError(null); }}>Discard changes</Button></div>
          </form> : <p className="text-sm text-muted-foreground">Select a provider to edit its tier, priority and limits.</p>}
        </div>
        <Button type="button" variant="outline" size="sm" disabled={loading || saving || dirty} onClick={() => { const controller = new AbortController(); requests.current.add(controller); void refresh(controller.signal).finally(() => requests.current.delete(controller)); }}>Refresh provider settings</Button>
      </CardContent> : null}
    </Card>
  </section>;
}
