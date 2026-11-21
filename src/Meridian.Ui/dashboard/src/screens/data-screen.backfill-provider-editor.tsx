import { useCallback, useEffect, useRef, useState } from "react";
import { Select } from "@/components/ui/select";
import { apiGetJson, apiPutJson } from "@/lib/api";
import { Button } from "@/components/ui/button";
import { PanelReload } from "@/components/ui/panel-reload";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { StatusBanner } from "@/components/ui/status-banner";
import { loadEffectiveBackfillOrder, type EffectiveBackfillOrder } from "@/screens/data-screen.backfill-routing";
import type { ConfigurationChangeStatus } from "@/screens/settings-change-impact";

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

function describeProviderSave(status: ConfigurationChangeStatus | undefined): string {
  const entries = status?.lastApply?.entries ?? [];
  const live = entries.some(entry => entry.kind === "AppliedLive");
  const deferred = entries.find(entry => entry.kind === "Deferred");
  const restart = deferred || status?.changes?.some(change => change.impact === "RestartHost") ||
    (!live && status?.requiresRestart);
  if (live && !restart) return "Saved. Routing applies to the next job.";
  if (restart) {
    const activation = live ? "Routing applies to the next job. " : "";
    return `Saved. ${activation}Pending host restart. ${deferred?.detail ?? "Running jobs keep their current settings."}`;
  }
  if (status?.requiresReconnect) return "Saved. Pending provider reconnect. Running jobs keep their current settings.";
  return `Saved. ${entries.find(entry => entry.kind === "NoRuntimeEffect")?.detail ?? "Awaiting host activation status."}`;
}

/** The host owns tier defaults and validates caps again on save. No credential fields are accepted. */
export function BackfillProviderEditorRegion() {
  const [open, setOpen] = useState(false);
  const [providers, setProviders] = useState<ProviderStatus[]>([]);
  const [effectiveOrder, setEffectiveOrder] = useState<EffectiveBackfillOrder | null>(null);
  const [orderLoadFailed, setOrderLoadFailed] = useState(false);
  const [selected, setSelected] = useState<string | null>(null);
  const [draft, setDraft] = useState<ProviderOptions | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [loadFailed, setLoadFailed] = useState(false);
  const [saving, setSaving] = useState(false);
  const [dragged, setDragged] = useState<string | null>(null);
  const requests = useRef(new Set<AbortController>());
  const mounted = useRef(true);
  useEffect(() => { mounted.current = true; const activeRequests = requests.current; return () => { mounted.current = false; activeRequests.forEach(controller => controller.abort()); }; }, []);
  const provider = providers.find(p => p.metadata.providerId === selected);
  const dirty = !!provider && JSON.stringify(provider.options) !== JSON.stringify(draft);
  const tier = provider?.metadata.tiers.find(t => t.id === draft?.tierId);
  const refreshOrder = useCallback(async (signal?: AbortSignal) => {
    try {
      const order = await loadEffectiveBackfillOrder(signal);
      if (mounted.current && !signal?.aborted) {
        setEffectiveOrder(order);
        setOrderLoadFailed(false);
      }
    } catch {
      if (mounted.current && !signal?.aborted) {
        setEffectiveOrder(null);
        setOrderLoadFailed(true);
      }
    }
  }, []);
  const refresh = useCallback(async (signal?: AbortSignal) => {
    setLoading(true); setError(null); setLoadFailed(false);
    const settings = apiGetJson<ProviderStatus[]>("/api/backfill/providers/statuses", { signal, allowDevelopmentFallback: false });
    const order = refreshOrder(signal);
    try {
      const data = await settings;
      if (mounted.current && !signal?.aborted) setProviders(data);
    } catch (cause) {
      if (mounted.current && !signal?.aborted) { setLoadFailed(true); setError(cause instanceof Error ? cause.message : "Provider configuration is unavailable."); }
    } finally {
      await order;
      if (mounted.current && !signal?.aborted) setLoading(false);
    }
  }, [refreshOrder]);
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
      const result = await apiPutJson<{ options?: ProviderOptions; changeImpact?: ConfigurationChangeStatus }>(
        `/api/backfill/providers/${encodeURIComponent(selected)}`, draft, { signal: controller.signal });
      if (!mounted.current || controller.signal.aborted) return;
      const saved = result.options ?? draft;
      setProviders(current => current.map(p => p.metadata.providerId === selected ? { ...p, options: saved } : p));
      setDraft(saved);
      setNotice(describeProviderSave(result.changeImpact));
      window.dispatchEvent(new Event("meridian:configuration-saved"));
      await refreshOrder(controller.signal);
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
        {!loading ? <section aria-label="Effective backfill order" className="rounded border border-border p-3 text-sm">
          <h4 className="font-semibold">Effective order for new jobs</h4>
          {effectiveOrder?.providers.length ? <>
            <ol className="mt-1 flex list-decimal flex-wrap gap-x-8 gap-y-1 pl-5">
              {effectiveOrder.providers.map(item => <li key={item.providerId}>
                {item.displayName}{item.isSimulated ? " (simulated)" : ""}
              </li>)}
            </ol>
            <p className="mt-2 text-xs text-muted-foreground">
              {effectiveOrder.fallbackEnabled ? "Fallback enabled." : "Fallback disabled."}{" "}
              {effectiveOrder.rotationEnabled ? "Live quota state may change the attempt order." : "Quota rotation is disabled."}
            </p>
          </> : <p className="mt-1 text-muted-foreground">{effectiveOrder ? "No providers are available for new jobs." : "Running order is unavailable."}</p>}
        </section> : null}
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
              <Select className="mt-1" value={draft.tierId} disabled={saving}
                onChange={event => { const chosen = provider.metadata.tiers.find(t => t.id === event.target.value); if (chosen) setDraft({ ...draft, tierId: chosen.id, rateLimitPerMinute: chosen.rateLimitPerMinute, rateLimitPerHour: chosen.rateLimitPerHour }); }}>
                {provider.metadata.tiers.map(t => <option key={t.id} value={t.id}>{t.label}</option>)}
              </Select>
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
        <PanelReload what="provider settings" failed={loadFailed || orderLoadFailed} busy={loading || saving || dirty} reload={() => { if (loading || saving || dirty) return; const controller = new AbortController(); requests.current.add(controller); void refresh(controller.signal).finally(() => requests.current.delete(controller)); }} />
      </CardContent> : null}
    </Card>
  </section>;
}
