import { useEffect, useId, useRef, useState, type FormEvent } from "react";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { StatusBanner } from "@/components/ui/status-banner";
import { isApiError } from "@/lib/api-errors";
import { getJson, putJson } from "@/lib/api/http-client";

export interface ProviderValidationProfile {
  staleToleranceDays: number;
  futureSkewSeconds: number;
  staleThresholdSeconds: number | null;
  maxLatencyMs: number;
}

interface ProfileAudit {
  id: string;
  changedAt: string;
  actor: string;
  reason: string | null;
  before: ProviderValidationProfile;
  after: ProviderValidationProfile;
  resetToDefault: boolean;
}

export interface ProviderValidationResponse {
  provider: string;
  profile: ProviderValidationProfile;
  defaultProfile: ProviderValidationProfile;
  isOverride: boolean;
  audit: ProfileAudit[];
  canEdit: boolean;
  revision: string;
  supportsStreamingRules: boolean;
  startupStreamingProfile: ProviderValidationProfile | null;
  pendingRestart: boolean;
}

interface ProfileChange {
  profile: ProviderValidationProfile | null;
  reason: string | null;
  confirmLoosening: boolean;
  resetToDefault: boolean;
  expectedRevision: string;
}

const profilePath = (provider: string) => "/api/providers/" + encodeURIComponent(provider) + "/validation-profile";
const readProfile = (provider: string, signal?: AbortSignal) =>
  getJson<ProviderValidationResponse>(profilePath(provider), { signal, allowDevelopmentFallback: false });
const writeProfile = (provider: string, change: ProfileChange, signal?: AbortSignal) =>
  putJson<{ saved: boolean; snapshot: ProviderValidationResponse }>(profilePath(provider), change, { signal });

type RuleKey = keyof ProviderValidationProfile;
type DraftValues = Record<RuleKey, string>;
const rules = [
  { id: "staleToleranceDays", label: "Staleness tolerance", unit: "calendar days", maximum: 365,
    detail: "Flags near-current backfills whose newest bar falls short. Daily fallback can try another provider." },
  { id: "futureSkewSeconds", label: "Future clock skew", unit: "seconds", maximum: 86400,
    detail: "Rejects live events with a timestamp too far ahead of the host clock." },
  { id: "staleThresholdSeconds", label: "Stale event threshold", unit: "seconds", maximum: 31536000,
    detail: "Warns when a live event is too old. A blank threshold disables the warning." },
  { id: "maxLatencyMs", label: "Maximum event age", unit: "ms", maximum: 86400000,
    detail: "Rejects live events that arrive too long after their timestamp. Zero disables this check." }
] as const;
const auditDate = new Intl.DateTimeFormat(undefined, { dateStyle: "medium", timeStyle: "short" });
const number = new Intl.NumberFormat(undefined, { maximumFractionDigits: 10 });

export function profileLoosens(next: ProviderValidationProfile, previous: ProviderValidationProfile) {
  return next.staleToleranceDays > previous.staleToleranceDays || next.futureSkewSeconds > previous.futureSkewSeconds ||
    (previous.staleThresholdSeconds !== null && (next.staleThresholdSeconds === null || next.staleThresholdSeconds > previous.staleThresholdSeconds)) ||
    (previous.maxLatencyMs > 0 && (next.maxLatencyMs === 0 || next.maxLatencyMs > previous.maxLatencyMs));
}

export function ProviderValidationPanel({ providerId, providerName, fetchProfile = readProfile, saveProfile = writeProfile }: {
  providerId: string; providerName: string; fetchProfile?: typeof readProfile; saveProfile?: typeof writeProfile;
}) {
  const [snapshot, setSnapshot] = useState<ProviderValidationResponse | null>(null);
  const [draft, setDraft] = useState<{ values: DraftValues; reset: boolean; reason: string; reviewing: boolean } | null>(null);
  const [reloadCount, setReloadCount] = useState(0);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const [draftError, setDraftError] = useState("");
  const [notice, setNotice] = useState("");
  const write = useRef<AbortController | null>(null);
  const reasonInput = useRef<HTMLInputElement>(null);
  const reviewTitle = useRef<HTMLHeadingElement>(null);
  const id = useId();

  useEffect(() => {
    const controller = new AbortController();
    void fetchProfile(providerId, controller.signal).then(
      result => { if (!controller.signal.aborted) { setSnapshot(result); setLoading(false); } },
      failure => { if (!controller.signal.aborted) { setError(profileError(failure)); setLoading(false); } }
    );
    return () => controller.abort();
  }, [providerId, fetchProfile, reloadCount]);

  useEffect(() => () => write.current?.abort(), []);
  const reviewing = draft?.reviewing ?? false;
  useEffect(() => { if (reviewing) reviewTitle.current?.focus(); }, [reviewing]);

  const proposed = draft ? readDraft(draft.values) : null;
  const changes = snapshot && proposed ? changedRules(snapshot.profile, proposed) : [];
  const loosens = Boolean(snapshot && proposed && profileLoosens(proposed, snapshot.profile));
  const canEdit = snapshot?.canEdit && !loading && !error;
  const latest = snapshot?.audit[0];

  function openDraft(reset: boolean) {
    if (!snapshot) return;
    setNotice("");
    setDraftError("");
    setDraft({
      values: writeDraft(reset ? snapshot.defaultProfile : snapshot.profile),
      reset, reason: "", reviewing: false
    });
  }

  function reload() {
    setLoading(true);
    setError("");
    setNotice("");
    setReloadCount(value => value + 1);
  }

  async function save(event: FormEvent) {
    event.preventDefault();
    if (!snapshot || !draft || !proposed || !canEdit || write.current) return;
    const reason = draft.reason.trim();
    if (loosens && !reason) {
      setDraftError("Enter a reason to relax the validation rules.");
      reasonInput.current?.focus();
      return;
    }
    if (loosens && !reviewing) {
      setDraftError("");
      setDraft({ ...draft, reviewing: true });
      return;
    }
    const controller = new AbortController();
    write.current = controller;
    setSaving(true);
    setDraftError("");
    try {
      const result = await saveProfile(snapshot.provider, {
        profile: draft.reset ? null : proposed,
        reason: reason || null,
        resetToDefault: draft.reset,
        confirmLoosening: loosens && reviewing,
        expectedRevision: snapshot.revision
      }, controller.signal);
      if (controller.signal.aborted) return;
      if (!result.saved) throw new Error("The save could not be verified.");
      setSnapshot(result.snapshot);
      setDraft(null);
      setNotice(draft.reset ? "Deployment default restored in the saved profile." : "Validation profile saved.");
      window.dispatchEvent(new Event("meridian:configuration-saved"));
    } catch (failure) {
      if (controller.signal.aborted) return;
      if (isApiError(failure) && failure.status === 400) {
        setDraftError(profileError(failure));
        setDraft({ ...draft, reviewing: false });
      } else {
        setDraft(null);
        setError(profileError(failure, true));
      }
    } finally {
      write.current = null;
      if (!controller.signal.aborted) setSaving(false);
    }
  }

  return <section aria-label="Data quality rules" className="space-y-3">
    {loading ? <p role="status" className="text-sm text-muted-foreground">Loading validation profile…</p> : null}
    {error ? <div className="space-y-2">
      <StatusBanner tone="danger" title={error} role="alert" />
      <Button size="sm" variant="outline" onClick={reload}>Reload profile</Button>
    </div> : null}
    {notice ? <StatusBanner tone="success" title={notice} role="status" /> : null}
    {snapshot ? <>
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div>
          <h3 className="text-sm font-semibold">{error ? "Last verified validation profile" : "Effective validation profile"}</h3>
          <p className="text-xs text-muted-foreground">Saved profile source {snapshot.isOverride ? "Provider override" : "Deployment default"}</p>
        </div>
        {canEdit ? <div className="flex flex-wrap gap-2">
          <Button size="sm" variant="outline" onClick={() => openDraft(false)}>Edit profile</Button>
          {snapshot.isOverride ? <Button size="sm" variant="outline" onClick={() => openDraft(true)}>Reset to deployment default</Button> : null}
        </div> : null}
      </div>
      {!snapshot.canEdit ? <p className="text-xs text-muted-foreground">Read only. Editing requires provider management and configuration modification permissions.</p> : null}
      {snapshot.pendingRestart ? <StatusBanner tone="warning" title="Live validation changes pending host restart"
        detail="The effective live thresholds show the values loaded at startup." /> : null}
      <dl className="divide-y divide-border border-y border-border">
        {rules.map(rule => {
          const historical = rule.id === "staleToleranceDays";
          const active = historical ? snapshot.profile : snapshot.startupStreamingProfile;
          return <div key={rule.id} className="grid gap-2 py-2 text-xs sm:grid-cols-2">
            <dt>
              <span className="block font-medium">{rule.label}</span>
              <span className="block text-muted-foreground">{rule.detail}</span>
            </dt>
            <dd><dl className="grid grid-cols-3 gap-2 tabular-nums">
              <div><dt className="text-muted-foreground">Effective</dt><dd>{active
                ? formatRule(rule.id, active[rule.id])
                : snapshot.supportsStreamingRules ? "Unavailable" : "Not applicable"}</dd></div>
              <div><dt className="text-muted-foreground">Deployment default</dt><dd>{formatRule(rule.id, snapshot.defaultProfile[rule.id])}</dd></div>
              {!historical && snapshot.supportsStreamingRules && (!active || active[rule.id] !== snapshot.profile[rule.id])
                ? <div><dt className="text-muted-foreground">Saved</dt><dd>{formatRule(rule.id, snapshot.profile[rule.id])}</dd></div> : null}
            </dl></dd>
          </div>;
        })}
      </dl>
      <section aria-label="Last profile change" className="space-y-2">
        <h4 className="text-sm font-semibold">Last change</h4>
        {latest ? <AuditChange entry={latest} /> : <p className="text-xs text-muted-foreground">No provider changes recorded.</p>}
      </section>
      {snapshot.audit.length > 1 ? <details>
        <summary className="cursor-pointer text-sm">Earlier changes</summary>
        <ol className="grid gap-3 py-2">{snapshot.audit.slice(1).map(entry => <li key={entry.id}><AuditChange entry={entry} /></li>)}</ol>
      </details> : null}
    </> : null}
    {draft && snapshot ? <Dialog open onOpenChange={open => { if (!open && !saving) setDraft(null); }}>
      <DialogContent aria-labelledby={id + "-title"} aria-describedby={id + "-impact"}>
        <DialogHeader>
          <DialogTitle id={id + "-title"}>{reviewing ? "Confirm relaxation" : draft.reset ? "Reset to deployment default" : "Edit validation profile"}</DialogTitle>
          <DialogDescription id={id + "-impact"}>
            New workstation historical backfills and gap repair that use {providerName} read the saved daily tolerance.
            Queued backfill workers and applicable live ingestion use their startup profiles.
          </DialogDescription>
        </DialogHeader>
        <form onSubmit={event => void save(event)} className="space-y-3" aria-busy={saving}>
          {reviewing ? <h3 ref={reviewTitle} tabIndex={-1} className="text-sm font-semibold">Future jobs can accept older or later data</h3> : !draft.reset ?
            <fieldset disabled={saving} className="grid gap-2 sm:grid-cols-2">
              {rules.filter(rule => snapshot.supportsStreamingRules || rule.id === "staleToleranceDays").map(rule => <label key={rule.id} className="text-xs font-medium">
                {rule.label} ({rule.unit})
                <Input type="number" min={0} max={rule.maximum} step={rule.id === "maxLatencyMs" ? "any" : 1}
                  required={rule.id !== "staleThresholdSeconds"} data-dialog-autofocus={rule.id === "staleToleranceDays" ? true : undefined}
                  value={draft.values[rule.id]} onChange={event => {
                    setDraft({ ...draft, values: { ...draft.values, [rule.id]: event.target.value } });
                    setDraftError("");
                  }} />
              </label>)}
            </fieldset> : null}
          {proposed ? <ProfileDifference before={snapshot.profile} after={proposed} /> : null}
          {reviewing ? <p className="break-words text-sm"><strong>Recorded reason</strong> {draft.reason.trim()}</p> :
            <label className="block text-sm">Change reason {loosens ? "(required)" : "(optional)"}
              <Input ref={reasonInput} aria-required={loosens || undefined} value={draft.reason} maxLength={2000}
                disabled={saving} onChange={event => { setDraft({ ...draft, reason: event.target.value }); setDraftError(""); }} />
            </label>}
          {draftError ? <StatusBanner tone="danger" title={draftError} role="alert" /> : null}
          <div className="flex flex-wrap justify-end gap-2">
            <Button type="button" size="sm" variant="outline" disabled={saving} onClick={() => setDraft(null)}>Cancel</Button>
            {reviewing ? <Button type="button" size="sm" variant="outline" disabled={saving}
              onClick={() => setDraft({ ...draft, reviewing: false })}>Back</Button> : null}
            <Button type="submit" size="sm" busy={saving} disabled={saving || !proposed || (!draft.reset && changes.length === 0)}>
              {reviewing ? "Confirm relaxation" : loosens ? "Review relaxation" : draft.reset ? "Reset profile" : "Save changes"}
            </Button>
          </div>
        </form>
      </DialogContent>
    </Dialog> : null}
  </section>;
}

function writeDraft(profile: ProviderValidationProfile): DraftValues {
  return {
    staleToleranceDays: String(profile.staleToleranceDays), futureSkewSeconds: String(profile.futureSkewSeconds),
    staleThresholdSeconds: profile.staleThresholdSeconds === null ? "" : String(profile.staleThresholdSeconds),
    maxLatencyMs: String(profile.maxLatencyMs)
  };
}

function readDraft(values: DraftValues): ProviderValidationProfile | null {
  if (rules.some(rule => rule.id !== "staleThresholdSeconds" && !values[rule.id].trim())) return null;
  const profile = {
    staleToleranceDays: Number(values.staleToleranceDays), futureSkewSeconds: Number(values.futureSkewSeconds),
    staleThresholdSeconds: values.staleThresholdSeconds.trim() === "" ? null : Number(values.staleThresholdSeconds),
    maxLatencyMs: Number(values.maxLatencyMs)
  };
  return rules.every(rule => {
    const value = profile[rule.id];
    return value === null || (Number.isFinite(value) && value >= 0 && value <= rule.maximum &&
      (rule.id === "maxLatencyMs" || Number.isInteger(value)));
  }) ? profile : null;
}

function formatRule(key: RuleKey, value: number | null) {
  if (value === null || (key === "maxLatencyMs" && value === 0)) return "Disabled";
  return number.format(value) + " " + rules.find(rule => rule.id === key)?.unit;
}

function changedRules(before: ProviderValidationProfile, after: ProviderValidationProfile) {
  return rules.filter(rule => before[rule.id] !== after[rule.id]);
}

function ProfileDifference({ before, after }: { before: ProviderValidationProfile; after: ProviderValidationProfile }) {
  const changes = changedRules(before, after);
  return changes.length ? <div className="divide-y divide-border rounded-sm border border-border text-xs">
    {changes.map(rule => <div key={rule.id} className="px-3 py-2">
      <div className="font-medium">{rule.label}</div>
      <dl className="mt-1 grid grid-cols-2 gap-2 tabular-nums">
        <div><dt className="text-muted-foreground">Before</dt><dd>{formatRule(rule.id, before[rule.id])}</dd></div>
        <div><dt className="text-muted-foreground">After</dt><dd>{formatRule(rule.id, after[rule.id])}</dd></div>
      </dl>
    </div>)}
  </div> : null;
}

function AuditChange({ entry }: { entry: ProfileAudit }) {
  return <div className="space-y-2 text-xs">
    <dl className="grid gap-2 sm:grid-cols-3">
      <div><dt className="text-muted-foreground">Recorded at</dt><dd><time dateTime={entry.changedAt}>{auditDate.format(new Date(entry.changedAt))}</time></dd></div>
      <div><dt className="text-muted-foreground">Actor</dt><dd className="break-words">{entry.actor}</dd></div>
      <div><dt className="text-muted-foreground">Recorded reason</dt><dd className="break-words">{entry.reason ?? "None recorded"}</dd></div>
    </dl>
    {entry.resetToDefault ? <p>Reset to deployment default</p> : null}
    <ProfileDifference before={entry.before} after={entry.after} />
  </div>;
}

function profileError(error: unknown, saving = false) {
  if (isApiError(error)) {
    if (error.status === 401 || error.status === 403)
      return saving ? "No change was applied. Your session lacks permission to edit this profile." : "Your session lacks permission to view this profile.";
    if (error.status === 409) return "No change was applied. Reload the profile to review the latest saved values.";
    if (error.status === 400) return error.validationIssues[0]?.messages[0] ?? error.detail ?? "The proposed profile was refused.";
    if (error.status === 503 && saving) return "No change was applied. The host could not save the profile and its audit entry.";
  }
  return saving ? "The save could not be verified. Reload the profile to check the result." : "The validation profile could not be loaded.";
}
