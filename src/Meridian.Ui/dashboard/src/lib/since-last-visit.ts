// "Since your last visit" (Phase 3, C3).
//
// The server keeps each operator's last visit to a surface (`/api/workstation/operator/visits`).
// Opening the surface records a visit and returns the one it replaced, which becomes the baseline:
// a row detected after it is new, and a row whose activity (`lastUpdatedAt`) moved after it is
// updated. The baseline is fetched once per surface per browser tab and reused, so it does not
// move while the operator works or when a screen re-mounts.
import { useEffect, useState } from "react";
import { recordOperatorSurfaceVisit } from "@/lib/api";
import { formatUtcTimestamp } from "@/lib/format";
import type { OperatorSurfaceVisit } from "@/types/operator-surface-visit";

export const BREAK_QUEUE_VISIT_SURFACE = "reconciliation-breaks";
const SESSION_KEY_PREFIX = "meridian.workstation.surface-visit.v1:";

export type SinceLastVisitMark = "new" | "updated";
export type RecordSurfaceVisit = (surface: string) => Promise<OperatorSurfaceVisit>;

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

/** The surface's baseline for this tab: the visit the first load of the tab replaced. */
export function loadSinceLastVisitBaseline(
  surface: string,
  record?: RecordSurfaceVisit,
  storage: Storage | null = sessionStore()
): Promise<string | null> {
  const key = `${SESSION_KEY_PREFIX}${surface}`;
  try {
    const cached = storage?.getItem(key);
    if (cached) {
      const baseline = (JSON.parse(cached) as { baseline?: unknown }).baseline;
      if (baseline === null) return Promise.resolve(null);
      if (typeof baseline === "string") return Promise.resolve(baseline);
    }
  } catch {
    // An unreadable cache falls through to the server.
  }
  const pending = inFlight.get(surface);
  if (pending) return pending;
  const request = Promise.resolve()
    // Resolved here, not as a default parameter, so a missing API fails into "unavailable".
    .then(() => (record ?? recordOperatorSurfaceVisit)(surface))
    .then((visit) => {
      const baseline = visit.previousVisitAt ?? null;
      try {
        storage?.setItem(key, JSON.stringify({ baseline }));
      } catch {
        // Without session storage the in-memory promise still holds the baseline for this page.
      }
      return baseline;
    });
  inFlight.set(surface, request);
  request.catch(() => inFlight.delete(surface));
  return request;
}

/** Test seam: forget the in-memory baselines. */
export function resetSinceLastVisitCache() {
  inFlight.clear();
}

export function useSinceLastVisit(surface: string, record?: RecordSurfaceVisit): SinceLastVisitState {
  const [state, setState] = useState<SinceLastVisitState>({ status: "loading", baseline: null });
  useEffect(() => {
    let active = true;
    loadSinceLastVisitBaseline(surface, record)
      .then((baseline) => { if (active) setState({ status: "ready", baseline }); })
      .catch(() => { if (active) setState({ status: "unavailable", baseline: null }); });
    return () => { active = false; };
  }, [record, surface]);
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
