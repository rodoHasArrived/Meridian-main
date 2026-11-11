// "Since your last visit" (Phase 3, C3).
//
// The server keeps each operator's last visit to a surface (`/api/workstation/operator/visits`).
// Opening the surface records a visit and returns the one it replaced, which becomes the baseline:
// a row detected after it is new, and a row whose activity (`lastUpdatedAt`) moved after it is
// updated. The baseline is fetched once per surface per browser tab and reused, so it does not
// move while the operator works or when a screen re-mounts.
//
// The tab cache keeps the baseline with the visit this tab recorded. Session storage outlives a
// sign-out, so a later page load reuses the cache only when the server's last visit for whoever is
// now signed in is still that one; another operator in the same tab gets their own baseline.
import { useEffect, useState } from "react";
import { getOperatorSurfaceVisit, recordOperatorSurfaceVisit } from "@/lib/api/operator-workstation.api";
import { formatUtcTimestamp } from "@/lib/format";
import type { OperatorSurfaceVisit } from "@/types/operator-surface-visit";

export const BREAK_QUEUE_VISIT_SURFACE = "reconciliation-breaks";
const SESSION_KEY_PREFIX = "meridian.workstation.surface-visit.v1:";

export type SinceLastVisitMark = "new" | "updated";
export type RecordSurfaceVisit = (surface: string) => Promise<OperatorSurfaceVisit>;

/** `record` records a visit and returns the one it replaced; `read` returns the last one only. */
export interface SurfaceVisitServices {
  record: RecordSurfaceVisit;
  read: RecordSurfaceVisit;
}

export interface SinceLastVisitState {
  status: "loading" | "ready" | "unavailable";
  /** Null on the operator's first visit, or while loading or unavailable. */
  baseline: string | null;
}

const inFlight = new Map<string, Promise<string | null>>();

function sessionStore(): Storage | null {
  try {
    return typeof window === "undefined" ? null : window.sessionStorage;
  } catch {
    return null;
  }
}

interface CachedVisit {
  baseline: string | null;
  visitedAt: string;
}

function readCachedVisit(storage: Storage | null, key: string): CachedVisit | null {
  try {
    const raw = storage?.getItem(key);
    if (!raw) return null;
    const parsed = JSON.parse(raw) as { baseline?: unknown; visitedAt?: unknown };
    const { baseline, visitedAt } = parsed;
    if (typeof visitedAt !== "string") return null;
    if (baseline === null) return { baseline: null, visitedAt };
    return typeof baseline === "string" ? { baseline, visitedAt } : null;
  } catch {
    // An unreadable cache falls through to the server.
    return null;
  }
}

function sameInstant(left: string | null | undefined, right: string | null | undefined): boolean {
  const a = parseTime(left);
  return a !== null && a === parseTime(right);
}

export interface SinceLastVisitOptions {
  /**
   * False for a screen that shows the baseline without being the surface itself, such as the
   * Accounting open-breaks card: it reads the operator's last visit and never records one, so only
   * opening the break queue advances it. Defaults to true.
   */
  record?: boolean;
}

/** The surface's baseline for this tab: the visit the first load of the tab replaced. */
export function loadSinceLastVisitBaseline(
  surface: string,
  services: Partial<SurfaceVisitServices> = {},
  storage: Storage | null = sessionStore(),
  options: SinceLastVisitOptions = {}
): Promise<string | null> {
  const key = `${SESSION_KEY_PREFIX}${surface}`;
  const recordKey = `${surface}:record`;
  if (options.record === false) return readSinceLastVisitBaseline(surface, services, storage);
  const pending = inFlight.get(recordKey);
  if (pending) return pending;
  // Resolved inside the chain, not as defaults, so a missing API fails into "unavailable".
  const record = () => (services.record ?? recordOperatorSurfaceVisit)(surface);
  const read = () => (services.read ?? getOperatorSurfaceVisit)(surface);
  const recordAndCache = () => record().then((visit) => {
    const baseline = visit.previousVisitAt ?? null;
    try {
      if (visit.visitedAt) storage?.setItem(key, JSON.stringify({ baseline, visitedAt: visit.visitedAt }));
    } catch {
      // Without session storage the in-memory promise still holds the baseline for this page.
    }
    return baseline;
  });
  const cached = readCachedVisit(storage, key);
  const request = Promise.resolve().then(() => cached
    ? read().then((last) => sameInstant(last.previousVisitAt, cached.visitedAt) ? cached.baseline : recordAndCache())
    : recordAndCache());
  inFlight.set(recordKey, request);
  request.catch(() => inFlight.delete(recordKey));
  return request;
}

/**
 * The baseline without recording a visit: this tab's baseline when the tab already recorded one
 * (and the server's last visit is still it), otherwise the operator's last recorded visit. A
 * recording already in flight on this page wins, so a card beside the queue shows its baseline.
 */
function readSinceLastVisitBaseline(
  surface: string,
  services: Partial<SurfaceVisitServices>,
  storage: Storage | null
): Promise<string | null> {
  const recording = inFlight.get(`${surface}:record`);
  if (recording) return recording;
  const readKey = `${surface}:read`;
  const pending = inFlight.get(readKey);
  if (pending) return pending;
  const cached = readCachedVisit(storage, `${SESSION_KEY_PREFIX}${surface}`);
  const request = Promise.resolve()
    .then(() => (services.read ?? getOperatorSurfaceVisit)(surface))
    .then((last) => cached && sameInstant(last.previousVisitAt, cached.visitedAt) ? cached.baseline : last.previousVisitAt ?? null);
  inFlight.set(readKey, request);
  request.catch(() => inFlight.delete(readKey));
  return request;
}

/** Test seam: forget the in-memory baselines. */
export function resetSinceLastVisitCache() {
  inFlight.clear();
}

export function useSinceLastVisit(
  surface: string,
  services?: Partial<SurfaceVisitServices>,
  options: SinceLastVisitOptions = {}
): SinceLastVisitState {
  const [state, setState] = useState<SinceLastVisitState>({ status: "loading", baseline: null });
  const record = services?.record;
  const read = services?.read;
  const recordsVisit = options.record !== false;
  useEffect(() => {
    let active = true;
    loadSinceLastVisitBaseline(surface, { record, read }, undefined, { record: recordsVisit })
      .then((baseline) => { if (active) setState({ status: "ready", baseline }); })
      .catch(() => { if (active) setState({ status: "unavailable", baseline: null }); });
    return () => { active = false; };
  }, [read, record, recordsVisit, surface]);
  return state;
}

function parseTime(value: string | null | undefined): number | null {
  if (!value) return null;
  const time = Date.parse(value);
  return Number.isNaN(time) ? null : time;
}

export function classifySinceLastVisit(
  item: { detectedAt?: string | null; lastUpdatedAt?: string | null },
  baseline: string | null
): SinceLastVisitMark | null {
  const since = parseTime(baseline);
  if (since === null) return null;
  const detected = parseTime(item.detectedAt);
  if (detected !== null && detected > since) return "new";
  const updated = parseTime(item.lastUpdatedAt);
  return updated !== null && updated > since ? "updated" : null;
}

export interface SinceLastVisitSummary {
  newCount: number;
  updatedCount: number;
}

export function summarizeSinceLastVisit(
  items: ReadonlyArray<{ detectedAt?: string | null; lastUpdatedAt?: string | null }>,
  baseline: string | null
): SinceLastVisitSummary {
  let newCount = 0;
  let updatedCount = 0;
  for (const item of items) {
    const mark = classifySinceLastVisit(item, baseline);
    if (mark === "new") newCount += 1;
    else if (mark === "updated") updatedCount += 1;
  }
  return { newCount, updatedCount };
}

/** One line for the queue: what changed, and the visit it is measured from. */
export function describeSinceLastVisit(state: SinceLastVisitState, summary: SinceLastVisitSummary): string | null {
  if (state.status !== "ready") return null;
  const since = parseTime(state.baseline);
  if (since === null) return "This is your first recorded visit. Breaks that change from now on are marked next time.";
  const parts = [
    summary.newCount > 0 ? `${summary.newCount} new` : null,
    summary.updatedCount > 0 ? `${summary.updatedCount} updated` : null
  ].filter(Boolean);
  const at = formatUtcTimestamp(new Date(since));
  return parts.length === 0
    ? `Nothing changed since your last visit (${at}).`
    : `${parts.join(" and ")} since your last visit (${at}).`;
}

/** KPI delta with its basis, e.g. "2 new since your last visit"; empty when nothing is new. */
export function sinceLastVisitDelta(summary: SinceLastVisitSummary): string {
  return summary.newCount > 0 ? `${summary.newCount} new since your last visit` : "";
}
