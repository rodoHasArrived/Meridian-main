import { useEffect, useRef, useState } from "react";
import { BackfillAttribution } from "@/screens/data-screen.backfill-attribution";
import { loadEffectiveBackfillOrder, type EffectiveBackfillProvider } from "@/screens/data-screen.backfill-routing";
import { apiGetJson, apiPostJson, apiPutJson } from "@/lib/api";
import { formatDateTime } from "@/lib/format";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { PanelReload } from "@/components/ui/panel-reload";
import { Select } from "@/components/ui/select";
import { StatusBanner } from "@/components/ui/status-banner";
import type { BackfillRoutingAttempt } from "@/types";

const schedulesPath = "/api/backfill/schedules";
const schedulePath = (id: string) => `${schedulesPath}/${encodeURIComponent(id)}`;
const providerKey = (id: string) => id.trim().toLowerCase();
const initialSchedule = { name: "", cronExpression: "0 2 * * *", symbols: "", lookbackDays: 30, enabled: true };

interface BackfillSchedule {
  scheduleId: string;
  name: string;
  enabled: boolean;
  cronExpression: string;
  timeZoneId: string;
  preferredProviders: string[] | null;
}

interface ScheduleExecution {
  executionId: string;
  status: string | number;
  startedAt: string | null;
  scheduledAt: string;
  symbolResults?: Record<string, {
    symbol: string;
    servedBy?: { providerId: string | null; bars: number }[] | null;
    routingAttempts?: BackfillRoutingAttempt[] | null;
  }> | null;
}

const executionStatuses = ["Pending", "Running", "Completed", "Partial success", "Failed", "Cancelled", "Skipped"];
const utcOptions = { dateStyle: "medium", timeStyle: "short", timeZone: "UTC" } as const;

function ScheduleHistory({ scheduleId, revision }: { scheduleId: string; revision: number }) {
  const [executions, setExecutions] = useState<ScheduleExecution[] | null>(null);
  const [failed, setFailed] = useState(false);
  useEffect(() => {
    const controller = new AbortController();
    void apiGetJson<{ executions: ScheduleExecution[] }>(`${schedulePath(scheduleId)}/history?limit=10`, {
      signal: controller.signal, allowDevelopmentFallback: false
    }).then(result => {
      if (!controller.signal.aborted) {
        setExecutions(result.executions);
        setFailed(false);
      }
    }).catch(() => {
      if (!controller.signal.aborted) setFailed(true);
    });
    return () => controller.abort();
  }, [scheduleId, revision]);

  return <section aria-label="Schedule execution history" className="space-y-2">
    <h4 className="text-sm font-semibold">Recent executions</h4>
    {failed ? <p role="status" className="text-sm text-muted-foreground">Execution history is unavailable.</p> : null}
    {!executions && !failed ? <p role="status" className="text-sm text-muted-foreground">Loading executions…</p> : null}
    {executions?.length === 0 ? <p className="text-sm text-muted-foreground">This schedule has no retained executions.</p> : null}
    {executions?.map(execution => {
      const results = Object.values(execution.symbolResults ?? {});
      const sources = results.flatMap(result => (result.servedBy ?? []).map(source => ({ ...source, symbol: result.symbol })));
      const attempts = results.flatMap(result => result.routingAttempts ?? []);
      const status = typeof execution.status === "number" ? executionStatuses[execution.status] ?? "Unknown" : execution.status;
      return <details key={execution.executionId} className="rounded border border-border px-3 py-2">
        <summary className="cursor-pointer text-sm">{formatDateTime(new Date(execution.startedAt ?? execution.scheduledAt), utcOptions)} UTC · {status}</summary>
        <div className="mt-2">
          {sources.length || attempts.length ? <BackfillAttribution sources={sources} attempts={attempts} /> : (
            <p className="text-sm text-muted-foreground">No provider evidence was retained for this execution.</p>
          )}
        </div>
      </details>;
    })}
  </section>;
}

function PreferredProviders({ providers, order, onChange }: {
  providers: EffectiveBackfillProvider[];
  order: string[];
  onChange: (providers: string[]) => void;
}) {
  const included = new Set(order.map(providerKey));
  const available = providers.filter(provider => !provider.isSimulated && !included.has(providerKey(provider.providerId)));
  function move(index: number, target: number) {
    const next = [...order];
    const [provider] = next.splice(index, 1);
    if (provider === undefined) return;
    next.splice(target, 0, provider);
    onChange(next);
  }

  return <fieldset className="space-y-2">
    <legend className="text-sm font-semibold">Preferred providers</legend>
    {order.length > 0 ? <ol className="list-decimal space-y-1 pl-5">
      {order.map((id, index) => {
        const provider = providers.find(provider => providerKey(provider.providerId) === providerKey(id) && !provider.isSimulated);
        const label = provider?.displayName ?? id;
        return <li key={id}>
          <div className="flex flex-wrap items-center gap-2 text-sm">
            <span className="min-w-24 font-medium">{label}</span>
            {!provider ? <span className="text-xs text-muted-foreground">Unavailable</span> : null}
            <Button type="button" size="sm" variant="ghost" disabled={index === 0} aria-label={`Move ${label} earlier`}
              onClick={() => move(index, index - 1)}>Earlier</Button>
            <Button type="button" size="sm" variant="ghost" disabled={index === order.length - 1} aria-label={`Move ${label} later`}
              onClick={() => move(index, index + 1)}>Later</Button>
            <Button type="button" size="sm" variant="ghost" aria-label={`Remove ${label}`} onClick={() => onChange(order.filter(value => value !== id))}>
              Remove
            </Button>
          </div>
        </li>;
      })}
    </ol> : <p className="text-sm text-muted-foreground">Use global order</p>}
    <label className="block max-w-sm text-sm">Add a provider
      <Select className="mt-1" value="" disabled={available.length === 0} onChange={event => {
        if (event.target.value) onChange([...order, event.target.value]);
      }}>
        <option value="">{available.length ? "Select a provider" : "No additional providers available"}</option>
        {available.map(provider => <option key={provider.providerId} value={provider.providerId}>{provider.displayName}</option>)}
      </Select>
    </label>
  </fieldset>;
}

export function BackfillSchedulesRegion() {
  const [open, setOpen] = useState(false);
  const [revision, setRevision] = useState(0);
  const [schedules, setSchedules] = useState<BackfillSchedule[]>([]);
  const [providers, setProviders] = useState<EffectiveBackfillProvider[]>([]);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [runtimeAvailable, setRuntimeAvailable] = useState<boolean | undefined>();
  const [orderUnavailable, setOrderUnavailable] = useState(false);
  const [loading, setLoading] = useState(true);
  const [draft, setDraft] = useState(initialSchedule);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [creating, setCreating] = useState(false);
  const [draftOrder, setDraftOrder] = useState<string[] | null>(null);
  const [saving, setSaving] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const pendingSave = useRef<AbortController | null>(null);
  const selected = schedules.find(schedule => schedule.scheduleId === selectedId);
  const savedOrder = selected?.preferredProviders ?? [];
  const order = draftOrder ?? savedOrder;
  const changed = draftOrder !== null;
  const editing = creating || changed;
  const validOrder = order.every(id => providers.some(provider => providerKey(provider.providerId) === providerKey(id) && !provider.isSimulated));

  useEffect(() => () => pendingSave.current?.abort(), []);
  useEffect(() => {
    if (!open) return;
    const controller = new AbortController();
    const signal = controller.signal;
    async function load() {
      setLoading(true);
      const [scheduleResult, orderResult] = await Promise.allSettled([
        apiGetJson<{ schedules: BackfillSchedule[]; runtimeAvailable?: boolean }>(schedulesPath, { signal, allowDevelopmentFallback: false }),
        loadEffectiveBackfillOrder(signal)
      ]);
      if (signal.aborted) return;
      if (scheduleResult.status === "fulfilled") {
        setSchedules(scheduleResult.value.schedules);
        setRuntimeAvailable(scheduleResult.value.runtimeAvailable);
        setLoadError(null);
      } else {
        setLoadError(scheduleResult.reason instanceof Error ? scheduleResult.reason.message : "Backfill schedules are unavailable.");
      }
      setProviders(orderResult.status === "fulfilled" ? orderResult.value.availableProviders ?? orderResult.value.providers : []);
      setOrderUnavailable(orderResult.status === "rejected");
      setLoading(false);
    }
    void load();
    return () => controller.abort();
  }, [open, revision]);

  function select(schedule?: BackfillSchedule) {
    setSelectedId(schedule?.scheduleId ?? null);
    setDraftOrder(null);
    setCreating(false);
    setSaveError(null);
    setNotice(null);
  }

  function changeOrder(next: string[]) {
    const matchesSavedOrder = next.length === savedOrder.length && next.every((id, index) => id === savedOrder[index]);
    setDraftOrder(matchesSavedOrder ? null : next);
  }

  async function save() {
    const editingSchedule = creating ? null : selected;
    if (!validOrder || (!creating && !editingSchedule) || pendingSave.current) return;
    const controller = new AbortController();
    pendingSave.current = controller;
    setSaving(true);
    setSaveError(null);
    setNotice(null);
    try {
      const preferences = { preferredProviders: order };
      const saved = editingSchedule
        ? await apiPutJson<BackfillSchedule>(`${schedulePath(editingSchedule.scheduleId)}/preferred-providers`, preferences, { signal: controller.signal })
        : await apiPostJson<BackfillSchedule>(schedulesPath, {
            ...preferences, name: draft.name.trim(), cronExpression: draft.cronExpression.trim(), timeZoneId: "UTC",
            symbols: [...new Set(draft.symbols.split(/[\s,]+/).filter(Boolean).map(symbol => symbol.toUpperCase()))],
            lookbackDays: draft.lookbackDays, enabled: draft.enabled
          }, { signal: controller.signal });
      if (controller.signal.aborted) return;
      setSchedules(current => [...current.filter(schedule => schedule.scheduleId !== saved.scheduleId), saved]);
      select(saved);
      setNotice(creating ? "Schedule created." : "Saved. Provider preferences apply to the next job.");
    } catch (error) {
      if (!controller.signal.aborted) setSaveError(error instanceof Error ? error.message : "The schedule could not be saved.");
    } finally {
      pendingSave.current = null;
      if (!controller.signal.aborted) setSaving(false);
    }
  }

  return <Card role="region" aria-labelledby="backfill-schedules-title" className="workspace-region">
    <CardHeader>
      <CardTitle id="backfill-schedules-title">Backfill schedules</CardTitle>
      <CardDescription>Review scheduled runs and set their provider order.</CardDescription>
      <Button type="button" size="sm" variant="outline" aria-expanded={open} disabled={saving || editing}
        onClick={() => setOpen(value => !value)}>{open ? "Close schedules" : "Manage schedules"}</Button>
    </CardHeader>
    {open ? <CardContent className="space-y-4">
      {loading ? <p role="status" className="text-sm text-muted-foreground">Loading schedules…</p> : null}
      {loadError ? <StatusBanner tone="danger" title="Schedules unavailable" detail={loadError} /> : null}
      {!loadError && runtimeAvailable === false ? (
        <p className="text-sm text-muted-foreground" role="status">Schedules can be saved. Automatic execution is disabled on this host.</p>
      ) : null}
      {orderUnavailable ? <p role="status" className="text-sm text-muted-foreground">The host’s effective provider order is unavailable.</p> : null}
      <PanelReload what="schedules" failed={!!loadError || orderUnavailable} busy={saving || loading || editing}
        reload={() => { if (!saving && !loading && !editing) setRevision(value => value + 1); }} />
      {loadError ? null : <div className="grid gap-4 lg:grid-cols-[minmax(12rem,0.65fr)_minmax(0,1fr)]">
        <div className="space-y-2">
          {!loading && schedules.length === 0 ? <p className="text-sm text-muted-foreground">No backfill schedules.</p> : null}
          {schedules.length > 0 ? <ul className="space-y-1" aria-label="Backfill schedules">
            {[...schedules].sort((a, b) => a.name.localeCompare(b.name)).map(schedule => <li key={schedule.scheduleId}>
              <Button type="button" size="sm" variant={selectedId === schedule.scheduleId ? "secondary" : "ghost"}
                aria-pressed={selectedId === schedule.scheduleId} disabled={loading || saving || editing} onClick={() => select(schedule)}>
                {schedule.name}
              </Button>
              <span className="ml-2 text-xs text-muted-foreground">{schedule.enabled ? "Enabled" : "Disabled"}</span>
            </li>)}
          </ul> : null}
          <Button type="button" size="sm" variant="outline" disabled={loading || saving || editing} onClick={() => {
            select(); setDraft(initialSchedule); setCreating(true);
          }}>New gap-fill schedule</Button>
        </div>
        {creating || selected ? <div className="min-w-0 space-y-3 rounded border border-border p-3">
          <form className="space-y-3" onSubmit={event => { event.preventDefault(); void save(); }}>
            <fieldset className="space-y-3" disabled={loading || saving}>
              <legend className="font-semibold">{creating ? "New gap-fill schedule" : selected?.name}</legend>
              {creating ? <>
                <label className="block text-sm">Schedule name<Input required value={draft.name} onChange={event => setDraft({ ...draft, name: event.target.value })} /></label>
                <label className="block text-sm">Cron expression (UTC)<Input required value={draft.cronExpression} onChange={event => setDraft({ ...draft, cronExpression: event.target.value })} /></label>
                <label className="block text-sm">Symbols (optional)<Input placeholder="All configured symbols" value={draft.symbols} onChange={event => setDraft({ ...draft, symbols: event.target.value })} /></label>
                <label className="block text-sm">Lookback days<Input type="number" min={1} required value={draft.lookbackDays} onChange={event => setDraft({ ...draft, lookbackDays: Number(event.target.value) })} /></label>
                <label className="flex items-center gap-2 text-sm"><input type="checkbox" checked={draft.enabled}
                  onChange={event => setDraft({ ...draft, enabled: event.target.checked })} />Enable schedule</label>
              </> : <p className="text-sm text-muted-foreground"><code>{selected?.cronExpression}</code> · {selected?.timeZoneId}</p>}
              <PreferredProviders providers={providers} order={order} onChange={changeOrder} />
              {!validOrder ? <p className="text-sm text-muted-foreground">Remove unavailable providers to save this order.</p> : null}
              <div className="flex flex-wrap gap-2">
                <Button type="submit" size="sm" disabled={!validOrder || (creating ? !draft.name.trim() || !draft.cronExpression.trim() : !changed)}>
                  {saving ? "Saving…" : creating ? "Create schedule" : "Save preferences"}
                </Button>
                {editing ? <Button type="button" size="sm" variant="ghost" onClick={() => select(selected)}>Discard changes</Button> : null}
              </div>
            </fieldset>
            {saveError ? <StatusBanner tone="danger" title="Schedule could not be saved" detail={saveError} /> : null}
            {notice ? <p role="status" className="text-sm">{notice}</p> : null}
          </form>
          {selected ? <ScheduleHistory key={selected.scheduleId} scheduleId={selected.scheduleId} revision={revision} /> : null}
        </div> : null}
      </div>}
    </CardContent> : null}
  </Card>;
}
