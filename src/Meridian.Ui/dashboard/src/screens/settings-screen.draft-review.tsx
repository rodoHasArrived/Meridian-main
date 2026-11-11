import { useEffect, useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import { StatusBanner } from "@/components/ui/status-banner";

type DraftFields = Record<string, string | number | boolean>;
interface DraftCacheOptions { actorKey: string | null; scopeKey: string }
interface RetainedDraft { draft: DraftFields; baseline: DraftFields; sourceIdentity: string; scopeKey: string }
const retainedDrafts = new Map<string, RetainedDraft>();
let retainedActor: string | null = null;

/** Presentation drafts only. Actor labels are isolation hints, never authenticated authority. */
export function synchronizeSettingsDraftActor(actorKey: string | null) {
  if (retainedActor !== actorKey) { retainedDrafts.clear(); retainedActor = actorKey; }
}

export function settingsDraftSeed<T extends DraftFields>(actorKey: string | null, allowedScopes: string[]): Partial<T> {
  synchronizeSettingsDraftActor(actorKey);
  if (!actorKey) return {};
  return [...retainedDrafts.values()].reverse().find((entry) => allowedScopes.includes(entry.scopeKey))?.draft as Partial<T> ?? {};
}

/** Browser draft protection; the service remains responsible for authority and concurrency. */
export function useSettingsDraftReview<T extends DraftFields>(draft: T, source: T, apply: (fields: T) => void, revision = "", cache?: DraftCacheOptions) {
  const [baseline, setBaseline] = useState(source);
  const [conflict, setConflict] = useState(false);
  const previousSource = useRef<string | null>(null);
  const previousContext = useRef<string | null>(null);
  const hydration = useRef<string | null>(null);
  const baselineSource = useRef("");
  const sourceSnapshot = JSON.stringify(source);
  const draftSnapshot = JSON.stringify(draft);
  const baselineSnapshot = JSON.stringify(baseline);
  const sourceIdentity = `${revision}:${sourceSnapshot}`;
  const actorKey = cache?.actorKey ?? null;
  const scopeKey = cache?.scopeKey ?? "";
  const cacheKey = actorKey && scopeKey ? JSON.stringify([actorKey, scopeKey]) : null;
  useEffect(() => {
    if (cache) synchronizeSettingsDraftActor(actorKey);
    if (previousContext.current !== (cacheKey ?? "transient")) {
      previousContext.current = cacheKey ?? "transient";
      previousSource.current = null;
      const retained = cacheKey ? retainedDrafts.get(cacheKey) : undefined;
      if (retained) {
        previousSource.current = sourceIdentity;
        baselineSource.current = retained.sourceIdentity;
        hydration.current = JSON.stringify(retained.draft);
        setBaseline(retained.baseline as T);
        apply(retained.draft as T);
        setConflict(retained.sourceIdentity !== sourceIdentity);
        return;
      }
    }
    if (previousSource.current === sourceIdentity) return;
    const initial = previousSource.current === null;
    previousSource.current = sourceIdentity;
    if (!initial && draftSnapshot !== baselineSnapshot) {
      setConflict(true);
      return;
    }
    const fields = JSON.parse(sourceSnapshot) as T;
    baselineSource.current = sourceIdentity;
    hydration.current = sourceSnapshot;
    setBaseline(fields);
    apply(fields);
    setConflict(false);
  }, [sourceIdentity, sourceSnapshot, draftSnapshot, baselineSnapshot, apply, cacheKey, actorKey, cache]);
  const changes = Object.entries(draft).filter(([field, value]) => value !== baseline[field])
    .map(([field, value]) => ({ field, before: baseline[field], after: value }));
  const dirty = changes.length > 0;
  useEffect(() => {
    if (!cacheKey || actorKey !== retainedActor) return;
    if (hydration.current !== null && hydration.current !== draftSnapshot) return;
    hydration.current = null;
    if (!dirty && !conflict) { retainedDrafts.delete(cacheKey); return; }
    retainedDrafts.delete(cacheKey);
    retainedDrafts.set(cacheKey, { draft: JSON.parse(draftSnapshot), baseline: JSON.parse(baselineSnapshot), sourceIdentity: baselineSource.current, scopeKey });
    while (retainedDrafts.size > 20) retainedDrafts.delete(retainedDrafts.keys().next().value!);
  }, [cacheKey, actorKey, scopeKey, dirty, conflict, draftSnapshot, baselineSnapshot]);
  useEffect(() => {
    if (!dirty) return;
    const preventLoss = (event: BeforeUnloadEvent) => { event.preventDefault(); event.returnValue = ""; };
    window.addEventListener("beforeunload", preventLoss);
    return () => window.removeEventListener("beforeunload", preventLoss);
  }, [dirty]);
  return {
    dirty, conflict, changes, retained: Boolean(cacheKey),
    discard: () => { if (cacheKey) retainedDrafts.delete(cacheKey); baselineSource.current = sourceIdentity; setBaseline(source); apply(source); setConflict(false); },
    load: (fields: T) => { if (cacheKey) retainedDrafts.delete(cacheKey); setBaseline(fields); apply(fields); setConflict(false); },
    markSaved: () => { if (cacheKey) retainedDrafts.delete(cacheKey); setBaseline(draft); setConflict(false); }
  };
}

const fieldLabel = (field: string) => field.replace(/([A-Z])/g, " $1").replace(/^./, (character) => character.toUpperCase());
const display = (value: string | number | boolean | undefined) => typeof value === "boolean" ? value ? "Required" : "Not required" : value === "" || value === undefined ? "Empty" : String(value);

export function SettingsDraftReview({ label, review, busy }: {
  label: string; busy: boolean;
  review: ReturnType<typeof useSettingsDraftReview>;
}) {
  return <section aria-label={`${label} draft review`} className="space-y-2 rounded-md border border-border/70 bg-secondary/15 px-3 py-2">
    <div className="flex flex-wrap items-center justify-between gap-2">
      <p className="text-xs font-semibold" role="status">{review.dirty ? `${review.changes.length} unsaved change${review.changes.length === 1 ? "" : "s"}` : "No unsaved changes"}</p>
      {review.dirty || review.conflict ? <Button type="button" size="sm" variant="outline" disabled={busy} onClick={review.discard}>Discard {label.toLowerCase()} changes</Button> : null}
    </div>
    {review.dirty ? <p className="text-xs text-muted-foreground">{review.retained ? "This draft is retained while you move between workspaces in this browser session. It has not been saved to the service. Reloading the browser clears the draft." : "This unsaved draft remains in the open editor. Sign in before relying on draft recovery."}</p> : null}
    {review.conflict ? <StatusBanner tone="warning" role="alert" title="Source changed while editing"
      detail="Your draft is retained. Discard these changes to load the current authoritative values, then review and reapply your changes before saving." /> : null}
    {review.changes.length > 0 ? <dl className="grid gap-2 text-xs" aria-label={`${label} changed fields`}>
      {review.changes.map((change) => <div key={change.field} className="min-w-0">
        <dt className="font-medium">{fieldLabel(change.field)}</dt>
        <dd className="break-words text-muted-foreground">{display(change.before)} → <strong className="text-foreground">{display(change.after)}</strong></dd>
      </div>)}
    </dl> : null}
  </section>;
}
