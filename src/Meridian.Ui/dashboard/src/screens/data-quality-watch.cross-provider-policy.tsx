/**
 * Cross-provider tolerance policy editor for Data → data-quality watch.
 *
 * Lets an operator set how far two vendors may disagree before Meridian flags it (for example a
 * strict 5 bps close check, informational volume, or wider bands for illiquid preferreds), and
 * preview what a draft would have flagged over the last 30 days before saving it.
 */
import { useEffect, useRef, useState, type FormEvent } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import { StatusBanner } from "@/components/ui/status-banner";
import { describeConfigurationApply, type ConfigurationApplyMessage } from "@/lib/configuration-apply";
import { formatPercent, formatTimestamp } from "@/lib/format";
import {
  getCrossProviderTolerancePolicy,
  previewCrossProviderTolerancePolicy,
  saveCrossProviderTolerancePolicy
} from "@/lib/api/cross-provider-tolerance.api";
import type {
  CrossProviderToleranceOverride,
  CrossProviderTolerancePolicy,
  CrossProviderTolerancePolicyResponse,
  CrossProviderTolerancePreviewResponse,
  CrossProviderVolumeMode
} from "@/types/data-quality-watch.types";

const VOLUME_MODE_ORDER: Record<CrossProviderVolumeMode, number> = { Alert: 0, Informational: 1, Off: 2 };

interface ResolvedTolerances { price: number; quote: number; volume: number; mode: CrossProviderVolumeMode; close: number }

function resolve(policy: CrossProviderTolerancePolicy, assetClass: string | null): ResolvedTolerances {
  const key = assetClass ? Object.keys(policy.assetClassOverrides ?? {}).find(name => name.toLowerCase() === assetClass.toLowerCase()) : undefined;
  const o: CrossProviderToleranceOverride = key ? policy.assetClassOverrides![key] : {};
  return {
    price: o.priceTolerancePercent ?? policy.priceTolerancePercent,
    quote: o.quoteTolerancePercent ?? policy.quoteTolerancePercent,
    volume: o.volumeTolerancePercent ?? policy.volumeTolerancePercent,
    mode: o.volumeMode ?? policy.volumeMode,
    close: o.backfillCloseTolerancePercent ?? policy.backfillCloseTolerancePercent
  };
}

function looser(next: ResolvedTolerances, previous: ResolvedTolerances) {
  return next.price > previous.price || next.quote > previous.quote || next.close > previous.close ||
    VOLUME_MODE_ORDER[next.mode] > VOLUME_MODE_ORDER[previous.mode] ||
    (next.mode === previous.mode && next.volume > previous.volume);
}

/** Mirrors the server's CrossProviderTolerancePolicy.Loosens so the reason requirement is visible before saving. */
export function policyLoosens(next: CrossProviderTolerancePolicy, previous: CrossProviderTolerancePolicy) {
  if (looser(resolve(next, null), resolve(previous, null))) return true;
  if (next.backfillBarsToCompare !== null && (previous.backfillBarsToCompare === null || next.backfillBarsToCompare < previous.backfillBarsToCompare)) return true;
  const classes = new Set([...Object.keys(next.assetClassOverrides ?? {}), ...Object.keys(previous.assetClassOverrides ?? {})].map(name => name.toLowerCase()));
  return [...classes].some(name => looser(resolve(next, name), resolve(previous, name)));
}

const numberOrUndefined = (value: string) => value.trim() === "" ? undefined : Number(value);

export function CrossProviderTolerancePanel({
  fetchPolicy = getCrossProviderTolerancePolicy,
  savePolicy = saveCrossProviderTolerancePolicy,
  previewPolicy = previewCrossProviderTolerancePolicy
}: {
  fetchPolicy?: typeof getCrossProviderTolerancePolicy;
  savePolicy?: typeof saveCrossProviderTolerancePolicy;
  previewPolicy?: typeof previewCrossProviderTolerancePolicy;
}) {
  const [open, setOpen] = useState(false);
  const [snapshot, setSnapshot] = useState<CrossProviderTolerancePolicyResponse | null>(null);
  const [draft, setDraft] = useState<CrossProviderTolerancePolicy | null>(null);
  const [reason, setReason] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [applied, setApplied] = useState<ConfigurationApplyMessage | null>(null);
  const [newAssetClass, setNewAssetClass] = useState("");
  const [preview, setPreview] = useState<CrossProviderTolerancePreviewResponse | null>(null);
  const [previewInput, setPreviewInput] = useState({ symbol: "SPY", baseline: "", comparison: "", assetClass: "" });
  const operation = useRef(0);
  const activeRequest = useRef<AbortController | null>(null);

  function beginRequest() {
    activeRequest.current?.abort();
    const controller = new AbortController();
    activeRequest.current = controller;
    return { controller, version: ++operation.current };
  }

  useEffect(() => () => { operation.current++; activeRequest.current?.abort(); }, []);

  useEffect(() => {
    if (!open || snapshot) return;
    const { controller, version } = beginRequest();
    setBusy(true);
    fetchPolicy({ signal: controller.signal }).then(result => {
      if (version !== operation.current) return;
      setSnapshot(result);
      setDraft(result.policy);
      const [first = "", second = ""] = result.previewProviders;
      setPreviewInput(input => ({ ...input, baseline: input.baseline || first, comparison: input.comparison || second }));
    }).catch((failure: unknown) => {
      if (version === operation.current && !controller.signal.aborted)
        setError(failure instanceof Error ? failure.message : "Cross-provider tolerances could not be loaded.");
    }).finally(() => { if (version === operation.current) setBusy(false); });
    // Load once per open; later reloads come from a save.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);

  const loosens = draft && snapshot ? policyLoosens(draft, snapshot.policy) : false;
  const bounds = snapshot?.bounds;
  const overrides = Object.entries(draft?.assetClassOverrides ?? {});

  function setOverride(name: string, value: CrossProviderToleranceOverride | null) {
    if (!draft) return;
    const next = { ...(draft.assetClassOverrides ?? {}) };
    if (value) next[name] = value; else delete next[name];
    setDraft({ ...draft, assetClassOverrides: Object.keys(next).length ? next : null });
  }

  async function save(event: FormEvent) {
    event.preventDefault();
    if (!draft || !snapshot) return;
    const { controller, version } = beginRequest();
    setBusy(true); setError(null); setApplied(null);
    try {
      const result = await savePolicy(draft, reason.trim(), { signal: controller.signal });
      if (version !== operation.current) return;
      setSnapshot({ ...snapshot, policy: result.policy, audit: [result.audit, ...snapshot.audit] });
      setDraft(result.policy);
      setReason("");
      setApplied(describeConfigurationApply(result.changeImpact));
      window.dispatchEvent(new Event("meridian:configuration-saved"));
    } catch (failure) {
      if (version === operation.current && !controller.signal.aborted)
        setError(failure instanceof Error ? failure.message : "Cross-provider tolerances could not be saved.");
    } finally { if (version === operation.current) setBusy(false); }
  }

  async function runPreview() {
    if (!draft) return;
    const { controller, version } = beginRequest();
    setBusy(true); setError(null); setPreview(null);
    try {
      const result = await previewPolicy({
        symbol: previewInput.symbol.trim(),
        baselineProvider: previewInput.baseline,
        comparisonProvider: previewInput.comparison,
        days: bounds?.maxPreviewDays ?? 30,
        policy: draft,
        assetClass: previewInput.assetClass.trim() || null
      }, { signal: controller.signal });
      if (version === operation.current) setPreview(result);
    } catch (failure) {
      if (version === operation.current && !controller.signal.aborted)
        setError(failure instanceof Error ? failure.message : "The preview could not run.");
    } finally { if (version === operation.current) setBusy(false); }
  }

  return <details className="rounded-lg border bg-card p-4" open={open} onToggle={event => setOpen(event.currentTarget.open)}>
    <summary className="cursor-pointer font-semibold focus-visible:outline focus-visible:outline-2">Cross-provider tolerances</summary>
    <div className="mt-4 space-y-4">
      <p className="text-sm text-muted-foreground">
        How far two vendors may disagree before Meridian flags a discrepancy. Saved tolerances apply to the running host without a restart.
        Loosening any tolerance requires a recorded reason.
      </p>
      {error ? <StatusBanner tone="danger" title="Cross-provider tolerances unavailable" detail={error} role="alert" /> : null}
      {applied ? <StatusBanner tone={applied.tone} title={applied.title} role="status" detail={applied.detail} /> : null}
      {!draft || !snapshot ? <p role="status" className="text-sm text-muted-foreground">{busy ? "Loading tolerances…" : error ? "No saved tolerances loaded." : ""}</p> :
        <form onSubmit={event => void save(event)} className="space-y-4" aria-label="Cross-provider tolerance policy">
          <fieldset disabled={busy} className="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
            <label className="text-sm">Trade price tolerance (%)<Input type="number" min={0} max={bounds?.maxPriceTolerancePercent} step="any" required value={draft.priceTolerancePercent} onChange={event => setDraft({ ...draft, priceTolerancePercent: Number(event.target.value) })} /></label>
            <label className="text-sm">Quote tolerance (%)<Input type="number" min={0} max={bounds?.maxPriceTolerancePercent} step="any" required value={draft.quoteTolerancePercent} onChange={event => setDraft({ ...draft, quoteTolerancePercent: Number(event.target.value) })} /></label>
            <label className="text-sm">Volume tolerance (%)<Input type="number" min={0} max={bounds?.maxVolumeTolerancePercent} step="any" required value={draft.volumeTolerancePercent} onChange={event => setDraft({ ...draft, volumeTolerancePercent: Number(event.target.value) })} /></label>
            <label className="text-sm">Volume differences<Select value={draft.volumeMode} onChange={event => setDraft({ ...draft, volumeMode: event.target.value as CrossProviderVolumeMode })}>
              <option value="Alert">Alert</option><option value="Informational">Informational only</option><option value="Off">Not compared</option>
            </Select></label>
            <label className="text-sm">Daily close tolerance (%)<Input type="number" min={0} max={bounds?.maxPriceTolerancePercent} step="any" required value={draft.backfillCloseTolerancePercent} onChange={event => setDraft({ ...draft, backfillCloseTolerancePercent: Number(event.target.value) })} /></label>
            <label className="text-sm">Backfill bars to compare (blank checks every bar)<Input type="number" min={1} max={bounds?.maxBackfillBarsToCompare} value={draft.backfillBarsToCompare ?? ""} onChange={event => setDraft({ ...draft, backfillBarsToCompare: event.target.value === "" ? null : Number(event.target.value) })} /></label>
          </fieldset>
          <p className="text-xs text-muted-foreground">0.05% is 5 basis points. Price, quote and close tolerances are capped at {bounds?.maxPriceTolerancePercent}% and volume at {bounds?.maxVolumeTolerancePercent}% so data-integrity alarms cannot be switched off by a wide tolerance.</p>

          <fieldset disabled={busy} className="space-y-2" aria-label="Asset-class overrides">
            <legend className="text-sm font-medium">Asset-class overrides</legend>
            {overrides.length === 0 ? <p className="text-sm text-muted-foreground">Every asset class uses the tolerances above.</p> : overrides.map(([name, value]) =>
              <div key={name} className="grid items-end gap-2 rounded border p-2 md:grid-cols-[8rem_repeat(3,minmax(0,1fr))_auto]" role="group" aria-label={`Override for ${name}`}>
                <span className="text-sm font-medium">{name}</span>
                <label className="text-xs">Price (%)<Input type="number" min={0} max={bounds?.maxPriceTolerancePercent} step="any" placeholder="inherit" value={value.priceTolerancePercent ?? ""} onChange={event => setOverride(name, { ...value, priceTolerancePercent: numberOrUndefined(event.target.value) })} /></label>
                <label className="text-xs">Daily close (%)<Input type="number" min={0} max={bounds?.maxPriceTolerancePercent} step="any" placeholder="inherit" value={value.backfillCloseTolerancePercent ?? ""} onChange={event => setOverride(name, { ...value, backfillCloseTolerancePercent: numberOrUndefined(event.target.value) })} /></label>
                <label className="text-xs">Volume<Select value={value.volumeMode ?? ""} onChange={event => setOverride(name, { ...value, volumeMode: (event.target.value || undefined) as CrossProviderVolumeMode | undefined })}>
                  <option value="">Inherit</option><option value="Alert">Alert</option><option value="Informational">Informational only</option><option value="Off">Not compared</option>
                </Select></label>
                <Button type="button" variant="outline" size="sm" onClick={() => setOverride(name, null)}>Remove {name}</Button>
              </div>)}
            <div className="flex flex-wrap items-end gap-2">
              <label className="text-sm">New asset class<Input value={newAssetClass} placeholder="preferred" pattern={"[A-Za-z0-9_\\-]+"} maxLength={40} onChange={event => setNewAssetClass(event.target.value)} /></label>
              <Button type="button" variant="outline" disabled={!/^[A-Za-z0-9_-]{1,40}$/.test(newAssetClass.trim()) || overrides.some(([name]) => name.toLowerCase() === newAssetClass.trim().toLowerCase())}
                onClick={() => { setOverride(newAssetClass.trim(), {}); setNewAssetClass(""); }}>Add override</Button>
            </div>
          </fieldset>

          {loosens ? <p className="text-sm text-warning">This change flags fewer discrepancies. Explain why the wider tolerance is appropriate.</p> : null}
          <label className="block text-sm">Change reason{loosens ? " (required)" : " (optional)"}<Input required={loosens} maxLength={2000} value={reason} onChange={event => setReason(event.target.value)} disabled={busy} /></label>
          <div className="flex flex-wrap gap-2">
            <Button type="submit" disabled={busy || (loosens && reason.trim().length === 0)}>Save tolerances</Button>
            <Button type="button" variant="outline" disabled={busy} onClick={() => { setDraft(snapshot.policy); setReason(""); setPreview(null); }}>Discard changes</Button>
            <Button type="button" variant="outline" disabled={busy} onClick={() => setDraft(snapshot.defaults)}>Use built-in defaults</Button>
          </div>

          <fieldset disabled={busy} className="space-y-2 rounded border p-3" aria-label="Preview draft tolerances">
            <legend className="text-sm font-medium">Preview: what would this draft have flagged?</legend>
            {snapshot.previewProviders.length < 2 ? <p className="text-sm text-muted-foreground">Preview needs two configured historical providers.</p> : <>
              <div className="grid gap-2 md:grid-cols-4">
                <label className="text-sm">Preview symbol<Input required maxLength={32} value={previewInput.symbol} onChange={event => setPreviewInput({ ...previewInput, symbol: event.target.value })} /></label>
                <label className="text-sm">Baseline provider<Select value={previewInput.baseline} onChange={event => setPreviewInput({ ...previewInput, baseline: event.target.value })}>{snapshot.previewProviders.map(name => <option key={name} value={name}>{name}</option>)}</Select></label>
                <label className="text-sm">Comparison provider<Select value={previewInput.comparison} onChange={event => setPreviewInput({ ...previewInput, comparison: event.target.value })}>{snapshot.previewProviders.map(name => <option key={name} value={name}>{name}</option>)}</Select></label>
                <label className="text-sm">Asset class (optional)<Input value={previewInput.assetClass} placeholder="equity" onChange={event => setPreviewInput({ ...previewInput, assetClass: event.target.value })} /></label>
              </div>
              <p className="text-xs text-muted-foreground">Fetches the last {bounds?.maxPreviewDays ?? 30} days of daily bars from both providers: about {snapshot.providerCallsPerPreview} rate-limited provider calls per preview.</p>
              <Button type="button" variant="outline" disabled={busy || !previewInput.symbol.trim() || !previewInput.baseline || previewInput.baseline === previewInput.comparison} onClick={() => void runPreview()}>Preview last {bounds?.maxPreviewDays ?? 30} days</Button>
              {preview ? <div role="status" className="space-y-1 text-sm">
                <p className="font-medium">Would have flagged {preview.flaggedDays} of the last {bounds?.maxPreviewDays ?? 30} days for {preview.symbol} ({preview.baselineProvider} vs {preview.comparisonProvider}).</p>
                {preview.informationalDays > 0 ? <p className="text-muted-foreground">{preview.informationalDays} more day(s) differ only in volume and would be informational.</p> : null}
                {preview.missingSessions > 0 ? <p className="text-muted-foreground">{preview.missingSessions} session(s) were missing from one provider and were not compared.</p> : null}
                {preview.providerErrors.length > 0 ? <p className="text-warning">Provider errors: {preview.providerErrors.join("; ")}</p> : null}
                {preview.flagged.length > 0 ? <ul className="list-disc pl-5 text-xs">{preview.flagged.map(day => <li key={day.sessionDate}>{day.sessionDate}: {day.flaggedBy === "Price" ? `close differs ${formatPercent(day.closeDifferencePercent, { maximumFractionDigits: 3 })}` : `volume differs ${formatPercent(day.volumeDifferencePercent, { maximumFractionDigits: 1 })}`}</li>)}</ul> : null}
              </div> : null}
            </>}
          </fieldset>

          {snapshot.audit.length ? <details><summary className="cursor-pointer text-sm">Tolerance change history ({snapshot.audit.length})</summary>
            <ul className="mt-2 space-y-2 text-xs">{snapshot.audit.map(entry => <li key={entry.id}>{formatTimestamp(entry.changedAt)} · {entry.actor} · {entry.loosened ? "Loosened" : "Tightened or unchanged"} · {entry.reason ?? "No reason required"}</li>)}</ul>
          </details> : null}
        </form>}
    </div>
  </details>;
}
