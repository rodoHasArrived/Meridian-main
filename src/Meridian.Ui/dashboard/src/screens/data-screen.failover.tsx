import { useEffect, useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import { StatusBanner } from "@/components/ui/status-banner";
import { failoverApi, type FailoverRule, type FailoverRuleDraft, type FailoverSettings } from "@/lib/failover-api";

type FailoverApi = typeof failoverApi;
const newRule = (): FailoverRuleDraft => ({ id: "", primaryProviderId: "", backupProviderIds: [], failoverThreshold: 3, recoveryThreshold: 5, dataQualityThreshold: 0, maxLatencyMs: 0 });

function FailoverEditor({ api }: { api: FailoverApi }) {
  const [settings, setSettings] = useState<FailoverSettings | null>(null);
  const [rule, setRule] = useState<FailoverRuleDraft | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [pending, setPending] = useState(false);
  const [deleteId, setDeleteId] = useState<string | null>(null);
  const [deleteReason, setDeleteReason] = useState("");
  const [targets, setTargets] = useState<Record<string, string>>({});
  const request = useRef<AbortController | null>(null);
  const previous = settings?.rules.find(existing => existing.id === rule?.id);
  const loosens = Boolean(rule && previous && (rule.dataQualityThreshold < previous.dataQualityThreshold ||
    (previous.maxLatencyMs > 0 && (rule.maxLatencyMs === 0 || rule.maxLatencyMs > previous.maxLatencyMs)) ||
    rule.failoverThreshold > previous.failoverThreshold || rule.recoveryThreshold < previous.recoveryThreshold));

  useEffect(() => {
    const controller = new AbortController(); request.current = controller;
    api.get({ signal: controller.signal }).then(setSettings).catch((cause: unknown) => {
      if (!controller.signal.aborted) setError(cause instanceof Error ? cause.message : "Failover settings could not be loaded.");
    });
    return () => { controller.abort(); request.current?.abort(); };
  }, [api]);

  async function perform(action: (signal: AbortSignal) => Promise<unknown>, message: string, saved = false) {
    request.current?.abort(); const controller = new AbortController(); request.current = controller;
    setBusy(true); setError(null); setNotice(null);
    try {
      await action(controller.signal);
      if (controller.signal.aborted) return;
      if (saved) { setPending(true); window.dispatchEvent(new Event("meridian:configuration-saved")); }
      setNotice(message);
      const refreshed = await api.get({ signal: controller.signal });
      if (!controller.signal.aborted) setSettings(refreshed);
    } catch (cause) {
      if (!controller.signal.aborted) setError(cause instanceof Error ? cause.message : "The operation failed. Refresh to inspect current state before retrying.");
    } finally { if (!controller.signal.aborted) setBusy(false); }
  }

  function editRule(existing: FailoverRule) { setRule({ ...existing, reason: "" }); }

  return <div className="grid gap-4">
    {error ? <StatusBanner tone="danger" title="Failover operation unavailable" detail={error} role="alert" /> : null}
    {notice ? <StatusBanner tone="success" title={notice} role="status" /> : null}
    {pending ? <StatusBanner tone="warning" title="Pending host restart" detail="Saved configuration applies after the host restarts and rebuilds streaming providers. The active provider shown below is live runtime evidence; saving does not switch it." /> : null}
    <Button type="button" variant="outline" disabled={busy} onClick={() => void perform(async signal => {
      const fresh = await api.get({ signal }); if (!signal.aborted) setSettings(fresh);
    }, "Failover state refreshed.")}>Refresh failover state</Button>
    {!settings ? <p role="status" className="text-sm text-muted-foreground">{error ? "No authoritative settings loaded." : "Loading failover configuration…"}</p> : <>
      {!settings.runtimeAvailable ? <StatusBanner tone="warning" title="Streaming runtime unavailable" detail="You can edit saved configuration. Provider switch controls require a running streaming failover service." /> : null}
      <form className="grid gap-3 rounded border p-3" onSubmit={event => {
        event.preventDefault();
        void perform(signal => api.saveSettings({ enableFailover: settings.enableFailover,
          healthCheckIntervalSeconds: settings.healthCheckIntervalSeconds, autoRecover: settings.autoRecover,
          failoverTimeoutSeconds: settings.failoverTimeoutSeconds }, { signal }), "Failover settings saved.", true);
      }}>
        <fieldset disabled={busy} className="grid gap-3 lg:grid-cols-2">
          <Checkbox label="Automatic failover" checked={settings.enableFailover} onCheckedChange={value => setSettings({ ...settings, enableFailover: value })} />
          <Checkbox label="Automatically recover to primary" checked={settings.autoRecover} onCheckedChange={value => setSettings({ ...settings, autoRecover: value })} />
          <label className="grid gap-1 text-sm">Health check interval (seconds)<Input type="number" min="1" required value={settings.healthCheckIntervalSeconds} onChange={event => setSettings({ ...settings, healthCheckIntervalSeconds: Number(event.target.value) })} /></label>
          <label className="grid gap-1 text-sm">Failover timeout (seconds)<Input type="number" min="1" required value={settings.failoverTimeoutSeconds} onChange={event => setSettings({ ...settings, failoverTimeoutSeconds: Number(event.target.value) })} /></label>
        </fieldset>
        <Button type="submit" className="justify-self-start" disabled={busy}>Save failover settings</Button>
      </form>
      <div className="grid gap-3" aria-label="Failover rules">
        {settings.rules.length === 0 ? <p className="text-sm">No failover rules configured.</p> : settings.rules.map(existing => <article key={existing.id} className="grid gap-2 rounded border p-3" aria-label={`Failover rule ${existing.id}`}>
          <div className="flex flex-wrap justify-between gap-2"><h4 className="font-semibold">{existing.id}</h4><span className="text-sm">Active: <strong>{existing.currentActiveProviderId ?? "Unavailable"}</strong></span></div>
          <p className="text-sm text-muted-foreground">Primary {existing.primaryProviderId} → backups {existing.backupProviderIds.join(" → ")} · failures {existing.failoverThreshold} · recovery {existing.recoveryThreshold} · quality ≥ {existing.dataQualityThreshold} · latency ≤ {existing.maxLatencyMs || "disabled"} ms</p>
          <div className="flex flex-wrap items-end gap-2">
            <label className="grid min-w-48 gap-1 text-sm">Switch target for {existing.id}<Select value={targets[existing.id] ?? ""} disabled={busy || !existing.currentActiveProviderId} onChange={event => setTargets({ ...targets, [existing.id]: event.target.value })}><option value="">Choose provider…</option>{[existing.primaryProviderId, ...existing.backupProviderIds].map(id => <option key={id} value={id} disabled={id === existing.currentActiveProviderId}>{id}</option>)}</Select></label>
            <Button type="button" disabled={busy || !existing.currentActiveProviderId || !targets[existing.id] || targets[existing.id] === existing.currentActiveProviderId} onClick={() => void perform(signal => api.force(existing.id, targets[existing.id], { signal }), "Live provider hand-off completed.")}>Switch provider</Button>
            <Button type="button" variant="outline" disabled={busy || !existing.currentActiveProviderId || existing.currentActiveProviderId === existing.primaryProviderId} onClick={() => void perform(signal => api.reset(existing.id, { signal }), "Primary provider restored.")}>Reset to primary</Button>
            <Button type="button" variant="outline" disabled={busy} onClick={() => editRule(existing)}>Edit {existing.id}</Button>
            <Button type="button" variant="outline" disabled={busy} onClick={() => { setDeleteId(existing.id); setDeleteReason(""); }}>Delete {existing.id}</Button>
          </div>
          {deleteId === existing.id ? <div className="grid gap-2 rounded border p-2" role="group" aria-label={`Confirm deletion of ${existing.id}`}>
            <p className="text-sm">Delete this saved rule? Running routing continues until the host restarts.</p>
            <label className="grid gap-1 text-sm">Reason for deleting {existing.id}<Input required maxLength={2000} value={deleteReason} disabled={busy} onChange={event => setDeleteReason(event.target.value)} /></label>
            <p className="text-xs text-muted-foreground">Removal disables this rule's validation protections. Your reason and the previous thresholds are retained in the audit history.</p>
            <div className="flex gap-2"><Button type="button" disabled={busy || !deleteReason.trim()} onClick={() => void perform(async signal => {
              await api.deleteRule(existing.id, deleteReason.trim(), { signal }); if (!signal.aborted) setDeleteId(null);
            }, "Failover rule deleted.", true)}>Confirm delete</Button><Button type="button" variant="outline" disabled={busy} onClick={() => setDeleteId(null)}>Cancel deletion</Button></div>
          </div> : null}
        </article>)}
      </div>
      <Button type="button" variant="outline" disabled={busy} className="justify-self-start" onClick={() => setRule(newRule())}>Add failover rule</Button>
      {rule ? <form className="grid gap-3 rounded border p-3" aria-label="Edit failover rule" onSubmit={event => {
        event.preventDefault(); void perform(async signal => {
          await api.saveRule(rule, { signal }); if (!signal.aborted) setRule(null);
        }, "Failover rule saved.", true);
      }}>
        <fieldset disabled={busy} className="grid gap-3 lg:grid-cols-2">
          <label className="grid gap-1 text-sm">Primary provider ID<Input required value={rule.primaryProviderId} onChange={event => setRule({ ...rule, primaryProviderId: event.target.value })} /></label>
          <label className="grid gap-1 text-sm">Ordered backup provider IDs<Input required value={rule.backupProviderIds.join(", ")} placeholder="ib, polygon" onChange={event => setRule({ ...rule, backupProviderIds: event.target.value.split(",").map(value => value.trim()) })} /></label>
          <label className="grid gap-1 text-sm">Consecutive failures<Input type="number" min="1" required value={rule.failoverThreshold} onChange={event => setRule({ ...rule, failoverThreshold: Number(event.target.value) })} /></label>
          <label className="grid gap-1 text-sm">Recovery successes<Input type="number" min="1" required value={rule.recoveryThreshold} onChange={event => setRule({ ...rule, recoveryThreshold: Number(event.target.value) })} /></label>
          <label className="grid gap-1 text-sm">Minimum quality (0 disables)<Input type="number" min="0" max="100" step="any" value={rule.dataQualityThreshold} onChange={event => setRule({ ...rule, dataQualityThreshold: Number(event.target.value) })} /></label>
          <label className="grid gap-1 text-sm">Maximum latency in ms (0 disables)<Input type="number" min="0" step="any" value={rule.maxLatencyMs} onChange={event => setRule({ ...rule, maxLatencyMs: Number(event.target.value) })} /></label>
          <label className="grid gap-1 text-sm lg:col-span-2">Reason for loosening thresholds<Input required={loosens} maxLength={2000} value={rule.reason ?? ""} onChange={event => setRule({ ...rule, reason: event.target.value })} /></label>
        </fieldset>
        <p className="text-xs text-muted-foreground">Loosening an existing threshold requires a recorded reason and creates an audit entry. Changes require a host restart.</p>
        <div className="flex gap-2"><Button type="submit" disabled={busy || (loosens && !rule.reason?.trim())}>Save rule</Button><Button type="button" variant="outline" disabled={busy} onClick={() => setRule(null)}>Discard rule edit</Button></div>
      </form> : null}
    </>}
  </div>;
}

export function FailoverRegion({ api = failoverApi }: { api?: FailoverApi }) {
  const [open, setOpen] = useState(false);
  return <section className="workspace-region" aria-labelledby="failover-title"><Card>
    <CardHeader><CardTitle id="failover-title">Streaming failover</CardTitle><CardDescription>Inspect the active provider, switch live routing, and edit automatic failover rules.</CardDescription></CardHeader>
    <CardContent><details open={open} onToggle={event => setOpen(event.currentTarget.open)}><summary className="cursor-pointer text-sm font-semibold focus-visible:outline focus-visible:outline-2">Configure streaming failover</summary>{open ? <div className="mt-4"><FailoverEditor api={api} /></div> : null}</details></CardContent>
  </Card></section>;
}
