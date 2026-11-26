import { useEffect, useState } from "react";
import { useOnGlobalRefresh } from "@/lib/global-refresh";
import { Button } from "@/components/ui/button";
import { StatusBanner } from "@/components/ui/status-banner";
import { apiGetJson } from "@/lib/api/http-client";
import { describeConfigurationApply } from "@/lib/configuration-apply";
import { formatTimestamp } from "@/lib/format";

export type ConfigurationChangeImpact = "Live" | "ReconnectProvider" | "RestartHost";
export interface ConfigurationFieldChange { path: string; impact: ConfigurationChangeImpact }
export type ConfigurationApplyKind = "AppliedLive" | "ProviderReconfigured" | "NoRuntimeEffect" | "Deferred" | "Failed";
/** What the running host did with one saved setting; detail never carries setting values. */
export interface ConfigurationApplyEntry { path: string; kind: ConfigurationApplyKind; detail?: string | null }
export interface ConfigurationApplySummary { entries: ConfigurationApplyEntry[]; appliedAtUtc: string; anyDeferred?: boolean }
export interface ConfigurationRevision { id: string; contentHash: string }
export interface ConfigurationRuntimeAcknowledgement extends ConfigurationApplySummary {
  revision: ConfigurationRevision;
  collectorInstanceId: string;
}
export interface ConfigurationChangeStatus {
  impact: ConfigurationChangeImpact;
  changes: ConfigurationFieldChange[];
  pendingChanges: ConfigurationFieldChange[];
  savedAtUtc: string | null;
  requiresRestart: boolean;
  requiresReconnect: boolean;
  canReconnectInProcess?: boolean;
  lastApply?: ConfigurationApplySummary | null;
  revision?: ConfigurationRevision | null;
  runtimeState?: "Saved" | "AppliedLive" | "Deferred" | "Failed" | "Unknown" | null;
  collectorAcknowledgement?: ConfigurationRuntimeAcknowledgement | null;
}

const applyLabels: Record<ConfigurationApplyKind, string> = {
  AppliedLive: "Applied live",
  ProviderReconfigured: "Provider reconfigured",
  NoRuntimeEffect: "No runtime effect",
  Deferred: "Deferred",
  Failed: "Failed"
};

function pendingLabel(change: ConfigurationFieldChange, status: ConfigurationChangeStatus | null): string {
  if ((status?.runtimeState === "Unknown" || status?.runtimeState === "Saved")
      && (change.path === "dataSources" || change.path.startsWith("dataSources."))) {
    return "Awaiting collector confirmation";
  }
  if (change.impact === "RestartHost") return "Host restart";
  return status?.canReconnectInProcess ? "Provider reconnect" : "Provider reconnect · host restart required";
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

  // Activation status reloads with the header Refresh; a local Retry appears only after a failure.
  useOnGlobalRefresh(() => setRefresh(value => value + 1));

  const pending = status?.pendingChanges ?? [];
  const runtime = !error && status?.runtimeState ? describeConfigurationApply(status) : null;
  const acknowledgement = runtime ? status?.collectorAcknowledgement : null;
  if (pending.length === 0 && !error && !runtime) return null;
  return (
    // With nothing pending, an unavailable status is a footnote: it sits below the page's content
    // (the page lays its sections out in a flex column), not above the setup checklist.
    <section aria-label="Setting activation status" aria-live="polite" className={pending.length > 0 || runtime ? "space-y-2" : "order-last flex flex-wrap items-center gap-2"}>
      {runtime || pending.length > 0 ? (
        <StatusBanner
          tone={runtime?.tone ?? "warning"}
          title={runtime?.title ?? (status?.requiresRestart ? "Saved changes · pending restart" : "Saved changes · provider reconnect required")}
          detail={
            <>
              <p>{runtime?.detail ?? (status?.requiresRestart
                ? "Restart the host to rebuild the affected services. Saving has not confirmed these changes in the running host."
                : "Reconnect the affected provider through the host workflow. Saving has not confirmed that it is using the new settings.")}</p>
              {pending.length > 0 ? <details className="mt-2">
                <summary className="cursor-pointer">Inspect {pending.length} pending {pending.length === 1 ? "setting" : "settings"}</summary>
                <ul className="mt-2 space-y-1">
                  {pending.map(change => <li key={change.path}>
                    <code>{change.path}</code> · {pendingLabel(change, status)}
                  </li>)}
                </ul>
              </details> : null}
              {status?.revision && runtime ? <details className="mt-2 break-words">
                <summary className="cursor-pointer">Inspect configuration revision</summary>
                <p>Saved revision <code>{status.revision.id}</code></p>
                <p>SHA-256 <code>{status.revision.contentHash}</code></p>
                {acknowledgement ? <>
                  <p>Collector <code>{acknowledgement.collectorInstanceId}</code> acknowledged at{" "}
                    <time dateTime={acknowledgement.appliedAtUtc}>{formatTimestamp(acknowledgement.appliedAtUtc, { precision: "second" })}</time>
                  </p>
                  <ul className="mt-2 space-y-1">
                    {acknowledgement.entries.map(entry => <li key={entry.path}><code>{entry.path}</code> · {applyLabels[entry.kind]}</li>)}
                  </ul>
                </> : null}
              </details> : null}
            </>
          }
        />
      ) : null}
      {error ? <p className="text-xs text-muted-foreground" role="status">Setting activation status is unavailable.{pending.length > 0 ? " The last confirmed pending changes remain shown." : ""}</p> : null}
      {error ? <Button size="sm" variant="outline" disabled={busy} onClick={() => setRefresh(value => value + 1)}>
        {busy ? "Checking activation…" : "Retry"}
      </Button> : null}
    </section>
  );
}
