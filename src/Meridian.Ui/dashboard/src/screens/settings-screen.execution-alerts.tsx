import { useEffect, useRef, useState } from "react";
import { getExecutionAlertSettings, saveExecutionAlertSettings, type ExecutionAlertSettings } from "@/lib/api/execution-workflows.api";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";

export function ExecutionAlertSettingsPanel({ accountId }: { accountId?: string | null }) {
  const activeAccount = useRef(accountId); activeAccount.current = accountId;
  const [settings, setSettings] = useState<ExecutionAlertSettings | null>(null);
  const [error, setError] = useState<string | null>(null); const [notice, setNotice] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  useEffect(() => {
    setSettings(null); setError(null); setNotice(null);
    if (!accountId) return;
    const controller = new AbortController();
    void getExecutionAlertSettings(accountId, { signal: controller.signal }).then((value) => {
      if (!controller.signal.aborted) setSettings(value);
    }).catch((reason: unknown) => { if (!controller.signal.aborted) setError(reason instanceof Error ? reason.message : "Alert settings are unavailable."); });
    return () => controller.abort();
  }, [accountId]);
  return <section aria-label="Execution alert settings" className="panel-surface space-y-3 rounded-lg border border-border p-4">
    <h3 className="font-semibold">Execution alerts</h3><p className="text-xs text-muted-foreground">Account-scoped daily equity loss includes realized and unrealized changes, adjusted for external cash flows. Acknowledgement never clears execution controls.</p>
    {!accountId ? <p>Select an execution account before configuring alerts.</p> : null}
    {error ? <p role="alert">{error}</p> : null}{notice ? <p role="status">{notice}</p> : null}
    {settings && settings.accountId.toLowerCase() === accountId?.toLowerCase() ? <form className="grid gap-3 text-sm md:grid-cols-2" onSubmit={(event) => {
      event.preventDefault(); setSaving(true); setError(null); setNotice(null);
      void saveExecutionAlertSettings(settings).then(() => { if (activeAccount.current === settings.accountId) setNotice("Alert settings saved. External delivery occurs only when enabled."); })
        .catch((reason: unknown) => { if (activeAccount.current === settings.accountId) setError(reason instanceof Error ? reason.message : "Settings could not be saved."); })
        .finally(() => setSaving(false));
    }}>
      <label>Daily loss limit (USD; blank disables)<Input type="number" min="0.01" step="0.01" value={settings.dailyLossLimitUsd ?? ""} onChange={(event) => setSettings({ ...settings, dailyLossLimitUsd: event.target.value ? Number(event.target.value) : null })} /></label>
      <label>Stale order threshold (eligible market minutes)<Input type="number" min="1" max="1440" value={settings.staleOrderMinutes} onChange={(event) => setSettings({ ...settings, staleOrderMinutes: Number(event.target.value) })} /></label>
      <label className="flex items-center gap-2"><input type="checkbox" checked={settings.webhookEnabled} onChange={(event) => setSettings({ ...settings, webhookEnabled: event.target.checked })} />Enable external webhook delivery</label>
      <label>Webhook HTTPS URL<Input type="url" value={settings.webhookUrl ?? ""} onChange={(event) => setSettings({ ...settings, webhookUrl: event.target.value || null })} /></label>
      <Button type="submit" disabled={saving}>{saving ? "Saving…" : "Save execution alert settings"}</Button>
    </form> : null}
  </section>;
}
