import { useEffect, useState } from "react";
import { Button } from "@/components/ui/button";
import { StatusBanner } from "@/components/ui/status-banner";
import { apiGetJson } from "@/lib/api/http-client";

export type ConfigurationChangeImpact = "Live" | "ReconnectProvider" | "RestartHost";
export interface ConfigurationFieldChange { path: string; impact: ConfigurationChangeImpact }
export interface ConfigurationChangeStatus {
  impact: ConfigurationChangeImpact;
  changes: ConfigurationFieldChange[];
  pendingChanges: ConfigurationFieldChange[];
  savedAtUtc: string | null;
  requiresRestart: boolean;
  requiresReconnect: boolean;
  canReconnectInProcess?: boolean;
}

const loadConfigurationChanges = (signal: AbortSignal) =>
  apiGetJson<ConfigurationChangeStatus>("/api/config/changes", { signal, allowDevelopmentFallback: false });

/** Shared saved-versus-applied evidence. Only the host can confirm activation; this banner cannot clear it. */
export function SettingsChangeImpactBanner({ loadStatus = loadConfigurationChanges }: {
  loadStatus?: (signal: AbortSignal) => Promise<ConfigurationChangeStatus>;
}) {
  const [status, setStatus] = useState<ConfigurationChangeStatus | null>(null);
  const [error, setError] = useState(false);
  const [refresh, setRefresh] = useState(0);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    const controller = new AbortController();
    let reading = false;
    async function read() {
      if (reading || controller.signal.aborted) return;
      reading = true;
      setBusy(true);
      try {
        const next = await loadStatus(controller.signal);
        if (!controller.signal.aborted) { setStatus(next); setError(false); }
      } catch (failure) {
        if (controller.signal.aborted) return;
        const denied = typeof failure === "object" && failure !== null && "status" in failure
          && (failure.status === 401 || failure.status === 403);
        if (denied) { setStatus(null); setError(false); }
        else setError(true);
      } finally {
        reading = false;
        if (!controller.signal.aborted) setBusy(false);
      }
    }
    const reload = () => { void read(); };
    void read();
    const interval = window.setInterval(reload, 15_000);
    window.addEventListener("focus", reload);
    window.addEventListener("meridian:configuration-saved", reload);
    return () => {
      controller.abort();
      window.clearInterval(interval);
      window.removeEventListener("focus", reload);
      window.removeEventListener("meridian:configuration-saved", reload);
    };
  }, [loadStatus, refresh]);

  const pending = status?.pendingChanges ?? [];
  if (pending.length === 0 && !error) return null;
  return (
    <section aria-label="Setting activation status" aria-live="polite" className="space-y-2">
      {pending.length > 0 ? (
        <StatusBanner
          tone="warning"
          title={status?.requiresRestart ? "Saved changes · pending restart" : "Saved changes · provider reconnect required"}
          detail={
            <>
              <p>{status?.requiresRestart
                ? "Restart the host to rebuild the affected services. Saving has not confirmed these changes in the running host."
                : "Reconnect the affected provider through the host workflow. Saving has not confirmed that it is using the new settings."}</p>
              <details className="mt-2">
                <summary className="cursor-pointer">Inspect {pending.length} pending {pending.length === 1 ? "setting" : "settings"}</summary>
                <ul className="mt-2 space-y-1">
                  {pending.map(change => <li key={change.path}><code>{change.path}</code> · {change.impact === "RestartHost" ? "Host restart" : status?.canReconnectInProcess ? "Provider reconnect" : "Provider reconnect · host restart required"}</li>)}
                </ul>
              </details>
            </>
          }
        />
      ) : null}
      {error ? <p className="text-xs text-muted-foreground" role="status">Setting activation status is unavailable.{pending.length > 0 ? " The last confirmed pending changes remain shown." : ""}</p> : null}
      <Button size="sm" variant="outline" disabled={busy} onClick={() => setRefresh(value => value + 1)}>
        {busy ? "Checking activation…" : "Refresh activation status"}
      </Button>
    </section>
  );
}
