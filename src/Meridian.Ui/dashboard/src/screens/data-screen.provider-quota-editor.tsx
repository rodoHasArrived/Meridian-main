import { useState, type FormEvent } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import { useRequestLifecycle } from "@/hooks/use-request-lifecycle";
import { getJson, putJson } from "@/lib/api/http-client";

type QuotaTier = {
  id: string;
  label: string;
  minuteCeiling: number;
  dailyCeiling: number;
  defaultDailyLimit: number | null;
};

type QuotaSettings = {
  tierId: string | null;
  unitsPerMinute: number;
  dailyLimit: number | null;
  requestsPerMinute: number;
};

export type ProviderQuotaResponse = {
  providerId: string;
  displayName: string;
  unit: string;
  dailyResetPolicy: string | null;
  canEdit: boolean;
  policy: string;
  saved: QuotaSettings | null;
  running: {
    maximumPerWindow: number;
    windowSeconds: number;
    dailyLimit: number | null;
    minimumDelaySeconds: number;
  } | null;
  pendingRestart: boolean;
  tiers: QuotaTier[];
};

type QuotaEdit = {
  tierId: string;
  unitsPerMinute: number;
  dailyLimit: number | null;
  requestsPerMinute: number;
};

const endpoint = (id: string) => `/api/providers/${encodeURIComponent(id)}/quota`;
const quotaServices = {
  read: (id: string, signal: AbortSignal) =>
    getJson<ProviderQuotaResponse>(endpoint(id), { signal, allowDevelopmentFallback: false }),
  save: (id: string, draft: QuotaEdit, signal: AbortSignal) =>
    putJson<ProviderQuotaResponse>(endpoint(id), draft, { signal })
};

export function ProviderQuotaEditor({ services = quotaServices }: { services?: typeof quotaServices }) {
  const [provider, setProvider] = useState("twelvedata");
  const [snapshot, setSnapshot] = useState<ProviderQuotaResponse | null>(null);
  const [draft, setDraft] = useState<QuotaEdit | null>(null);
  const { status, start, succeed, fail, finish } = useRequestLifecycle({
    operation: "provider-quota-editor",
    idleMessage: "",
    failureMessage: "Provider limits are unavailable."
  });
  const quota = snapshot?.providerId === provider ? snapshot : null;
  const tier = quota?.tiers.find((item) => item.id === draft?.tierId);
  const dailyReset = quota?.dailyResetPolicy === "utc-midnight"
    ? "midnight UTC"
    : quota?.dailyResetPolicy === "rolling-24-hours" ? "a rolling 24-hour window" : null;

  async function run(edit?: QuotaEdit) {
    const token = start({ busyMode: "drop", runningMessage: edit ? "Saving provider limits." : "Loading provider limits." });
    if (!token) return;
    try {
      const result = edit
        ? await services.save(provider, edit, token.signal)
        : await services.read(provider, token.signal);
      if (!token.isCurrent()) return;
      setSnapshot(result);
      const saved = result.saved;
      setDraft(saved?.tierId ? { ...saved, tierId: saved.tierId } : null);
      succeed(token, { message: edit ? "Provider limits saved." : "Provider limits loaded." });
    } catch (error) {
      if (!token.signal.aborted) fail(token, error);
    } finally {
      finish(token);
    }
  }

  function selectTier(id: string) {
    const selected = quota?.tiers.find((item) => item.id === id);
    setDraft(selected ? {
      tierId: selected.id,
      unitsPerMinute: selected.minuteCeiling,
      dailyLimit: selected.defaultDailyLimit,
      requestsPerMinute: selected.minuteCeiling
    } : null);
  }

  function save(event: FormEvent) {
    event.preventDefault();
    if (quota?.canEdit && tier && draft) void run(draft);
  }

  return (
    <details className="rounded-md border border-border p-3">
      <summary className="cursor-pointer text-sm font-semibold focus-visible:outline-2">Quota settings</summary>
      <div className="mt-3 space-y-3">
        <div className="flex flex-wrap items-end gap-2">
          <label className="grid gap-1 text-sm">Quota provider
            <Select value={provider} disabled={status.inFlight}
              onChange={(event) => setProvider(event.target.value)}>
              <option value="twelvedata">Twelve Data</option>
              <option value="alphavantage">Alpha Vantage</option>
              <option value="fred">FRED</option>
            </Select>
          </label>
          <Button type="button" variant="outline" disabled={status.inFlight} onClick={() => void run()}>
            Load limits
          </Button>
        </div>
        {status.error ? <p role="alert" className="text-sm text-danger">{status.error}</p>
          : status.message ? <p role="status" className="text-sm text-muted-foreground">{status.message}</p> : null}
        {quota && (
          <>
            <dl className="grid grid-cols-1 gap-3 text-sm md:grid-cols-2">
              {quota.saved && (
                <div>
                  <dt className="font-semibold">Saved limits</dt>
                  <dd>{quota.saved.unitsPerMinute} {quota.unit}/minute · {quota.saved.dailyLimit ?? "No cap declared"}{quota.saved.dailyLimit === null ? "" : ` ${quota.unit}/day`}</dd>
                  <dd>{quota.saved.requestsPerMinute} requests/minute pacing</dd>
                </div>
              )}
              <div>
                <dt className="font-semibold">Running limits</dt>
                <dd>{quota.running
                  ? `${quota.running.maximumPerWindow} ${quota.unit} per ${quota.running.windowSeconds}s · ${quota.running.dailyLimit === null ? "No daily cap reported" : `${quota.running.dailyLimit} ${quota.unit}/day`}`
                  : "No initialized adapter has reported a running budget."}</dd>
                {quota.running && <dd>At least {quota.running.minimumDelaySeconds}s between requests</dd>}
              </div>
            </dl>
            {quota.pendingRestart && <p role="status" className="text-sm">Saved changes take effect after host restart.</p>}
            {dailyReset && <p className="text-sm text-muted-foreground">Daily {quota.unit} use {dailyReset}.</p>}
            {quota.policy === "adapter-fixed" && <p className="text-sm">The adapter controls these limits.</p>}
            {quota.canEdit && (
              <form onSubmit={save}>
                <fieldset className="space-y-3" disabled={status.inFlight}>
                  <div className="grid grid-cols-1 gap-2 md:grid-cols-2">
                    <label className="grid gap-1 text-sm">Vendor tier
                      <Select value={draft?.tierId ?? ""}
                        onChange={(event) => selectTier(event.target.value)} required>
                        <option value="">Select tier</option>
                        {quota.tiers.map((item) => <option value={item.id} key={item.id}>{item.label}</option>)}
                      </Select>
                    </label>
                    {tier && draft && (
                      <>
                        <label className="text-sm">Minute allowance ({quota.unit})
                          <Input type="number" min={1} max={tier.minuteCeiling} value={draft.unitsPerMinute || ""} required
                            onChange={(event) => setDraft({ ...draft, unitsPerMinute: Number(event.target.value) })} />
                        </label>
                        <label className="text-sm">Daily limit ({quota.unit})
                          <Input type="number" min={1} max={tier.dailyCeiling} value={draft.dailyLimit ?? ""} required
                            onChange={(event) => setDraft({ ...draft, dailyLimit: event.target.value === "" ? null : Number(event.target.value) })} />
                        </label>
                        <label className="text-sm">Request pacing (requests/minute)
                          <Input type="number" min={1} max={tier.minuteCeiling} value={draft.requestsPerMinute || ""} required
                            onChange={(event) => setDraft({ ...draft, requestsPerMinute: Number(event.target.value) })} />
                        </label>
                      </>
                    )}
                  </div>
                  {tier?.defaultDailyLimit === null && <p className="text-xs text-muted-foreground">Set Meridian’s daily cap for this paid tier.</p>}
                  <Button type="submit" disabled={!tier}>Save limits</Button>
                </fieldset>
              </form>
            )}
          </>
        )}
      </div>
    </details>
  );
}
