import { useEffect, useRef, useState, type FormEvent } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { getJson, putJson } from "@/lib/api/http-client";

export interface ProviderValidationProfile {
  staleToleranceDays: number;
  futureSkewSeconds: number;
  staleThresholdSeconds: number | null;
  maxLatencyMs: number;
}
interface ProfileAudit {
  id: string; changedAt: string; actor: string; reason: string | null; loosened: boolean;
}
export interface ProviderValidationResponse {
  provider: string; profile: ProviderValidationProfile; audit: ProfileAudit[]; impact: string;
}
export interface ProviderValidationSaveResponse {
  saved: boolean; impact: string; pendingRestart: boolean; profile: ProviderValidationProfile; audit: ProfileAudit;
}
const profilePath = (provider: string) => `/api/providers/${encodeURIComponent(provider)}/validation-profile`;
const readProfile = (provider: string, signal?: AbortSignal) => getJson<ProviderValidationResponse>(profilePath(provider), { signal, allowDevelopmentFallback: false });
const writeProfile = (provider: string, profile: ProviderValidationProfile, reason: string, signal?: AbortSignal) =>
  putJson<ProviderValidationSaveResponse>(profilePath(provider), { profile, reason }, { signal });

export function profileLoosens(next: ProviderValidationProfile, previous: ProviderValidationProfile) {
  return next.staleToleranceDays > previous.staleToleranceDays || next.futureSkewSeconds > previous.futureSkewSeconds ||
    (previous.staleThresholdSeconds !== null && (next.staleThresholdSeconds === null || next.staleThresholdSeconds > previous.staleThresholdSeconds)) ||
    (previous.maxLatencyMs > 0 && (next.maxLatencyMs === 0 || next.maxLatencyMs > previous.maxLatencyMs));
}

export function ProviderValidationPanel({ fetchProfile = readProfile, saveProfile = writeProfile }: {
  fetchProfile?: typeof readProfile; saveProfile?: typeof writeProfile;
}) {
  const [provider, setProvider] = useState("polygon");
  const [snapshot, setSnapshot] = useState<ProviderValidationResponse | null>(null);
  const [draft, setDraft] = useState<ProviderValidationProfile | null>(null);
  const [reason, setReason] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);
  const operation = useRef(0);
  const activeRequest = useRef<AbortController | null>(null);
  useEffect(() => () => { operation.current++; activeRequest.current?.abort(); }, []);
  function beginRequest() { activeRequest.current?.abort(); const controller = new AbortController(); activeRequest.current = controller; return { controller, version: ++operation.current }; }
  const loosens = draft && snapshot ? profileLoosens(draft, snapshot.profile) : false;
  async function load(event: FormEvent) {
    event.preventDefault(); const { controller, version } = beginRequest();
    setBusy(true); setError(null); setSaved(false); setDraft(null); setSnapshot(null);
    try { const result = await fetchProfile(provider.trim(), controller.signal); if (version !== operation.current) return; setSnapshot(result); setDraft(result.profile); setReason(""); }
    catch (failure) { if (version === operation.current && !controller.signal.aborted) setError(failure instanceof Error ? failure.message : "Unable to load validation profile."); }
    finally { if (version === operation.current) setBusy(false); }
  }
  async function save(event: FormEvent) {
    event.preventDefault(); if (!draft || !snapshot) return;
    const { controller, version } = beginRequest();
    setBusy(true); setError(null); setSaved(false);
    try {
      const result = await saveProfile(snapshot.provider, draft, reason.trim(), controller.signal);
      if (version !== operation.current) return;
      if (!result.saved) throw new Error("The validation profile was not saved.");
      setSnapshot({ ...snapshot, profile: result.profile, audit: [result.audit, ...snapshot.audit] });
      setDraft(result.profile); setSaved(true); setReason("");
      window.dispatchEvent(new Event("meridian:configuration-saved"));
    } catch (failure) { if (version === operation.current && !controller.signal.aborted) setError(failure instanceof Error ? failure.message : "Unable to save validation profile."); }
    finally { if (version === operation.current) setBusy(false); }
  }
  return <details className="rounded-lg border bg-card p-4">
    <summary className="cursor-pointer font-semibold focus-visible:outline focus-visible:outline-2">Provider validation profiles</summary>
    <div className="mt-4 space-y-4">
      <p className="text-sm text-muted-foreground">Control stale-data tolerance, future clock skew and event latency per provider. Changes apply after a host restart. Loosening requires a recorded reason.</p>
      <form onSubmit={(event) => void load(event)} className="flex flex-wrap items-end gap-2">
        <label className="text-sm">Validation provider ID<Input value={provider} onChange={(event) => { setProvider(event.target.value); setDraft(null); setSnapshot(null); setSaved(false); }} required pattern={"[A-Za-z0-9_\\-]+"} disabled={busy} /></label>
        <Button type="submit" variant="outline" disabled={busy}>{busy ? "Working…" : "Load validation profile"}</Button>
      </form>
      {error ? <p role="alert" className="text-sm text-danger">{error}</p> : null}
      {saved ? <p role="status" className="text-sm text-warning">Saved — pending host restart. Running providers retain their current validation profile.</p> : null}
      {draft && snapshot ? <form onSubmit={(event) => void save(event)} className="space-y-3">
        <p className="text-sm font-medium">Editing {snapshot.provider}</p>
        <fieldset disabled={busy} className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
          <label className="text-sm">Stale tolerance (calendar days)<Input type="number" min={0} max={365} required value={draft.staleToleranceDays} onChange={(event) => setDraft({ ...draft, staleToleranceDays: Number(event.target.value) })} /></label>
          <label className="text-sm">Future skew (seconds)<Input type="number" min={0} max={86400} required value={draft.futureSkewSeconds} onChange={(event) => setDraft({ ...draft, futureSkewSeconds: Number(event.target.value) })} /></label>
          <label className="text-sm">Stale event threshold (seconds, blank disables)<Input type="number" min={0} max={31536000} value={draft.staleThresholdSeconds ?? ""} onChange={(event) => setDraft({ ...draft, staleThresholdSeconds: event.target.value === "" ? null : Number(event.target.value) })} /></label>
          <label className="text-sm">Maximum latency (ms, 0 disables)<Input type="number" min={0} max={86400000} required value={draft.maxLatencyMs} onChange={(event) => setDraft({ ...draft, maxLatencyMs: Number(event.target.value) })} /></label>
        </fieldset>
        {loosens ? <p className="text-sm text-warning">This change loosens validation. Explain why the additional tolerance is appropriate.</p> : null}
        <label className="block text-sm">Change reason{loosens ? " (required)" : " (optional)"}<Input required={Boolean(loosens)} maxLength={2000} value={reason} onChange={(event) => setReason(event.target.value)} disabled={busy} /></label>
        <div className="flex gap-2"><Button type="submit" disabled={busy || (loosens && reason.trim().length === 0)}>Save validation profile</Button><Button type="button" variant="outline" disabled={busy} onClick={() => { setDraft(snapshot.profile); setReason(""); }}>Discard changes</Button></div>
        <p className="text-xs text-muted-foreground">Future-skew and maximum-latency breaches reject new events. Stale-event thresholds issue warnings; daily recency influences provider fallback. Existing archives are not rewritten.</p>
        {snapshot.audit.length ? <details><summary className="cursor-pointer text-sm">Validation change history ({snapshot.audit.length})</summary><ul className="mt-2 space-y-2 text-xs">{snapshot.audit.map((entry) => <li key={entry.id}>{new Date(entry.changedAt).toISOString()} · {entry.actor} · {entry.loosened ? "Loosened" : "Tightened or unchanged"} · {entry.reason ?? "No reason required"}</li>)}</ul></details> : null}
      </form> : null}
    </div>
  </details>;
}
