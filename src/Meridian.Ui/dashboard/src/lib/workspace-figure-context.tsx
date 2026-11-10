// What the shell knows about the evidence behind headline figures: when each payload that
// supplies figures was last loaded, whether its latest reload failed, and whether the session
// runs on real data. Evidence is kept per payload, not per workspace or per refresh, because one
// strip can show figures from more than one payload and one refresh reloads only some of them.
// The shell provides it once; StatStrip and the screens read it so every figure states the
// evidence of the payload that actually supplies its value.
import { createContext, useContext, type ReactNode } from "react";
import type { DataProvenanceKind } from "@/app-shell.data-provenance-badge";
import type { RequestLifecycleStatus } from "@/hooks/use-request-lifecycle";
import type { MetricFreshness } from "@/types";

/** The workstation payloads that supply headline figures, by their refresh key. */
export const FIGURE_SOURCE_KEYS = [
  "strategy",
  "trading",
  "portfolio",
  "data",
  "accounting",
  "reporting",
  "overview",
  "crossScreenCounts"
] as const;
export type FigureSourceKey = (typeof FIGURE_SOURCE_KEYS)[number];

export interface WorkspaceFigureContextValue {
  /** Null while the first load is still in flight: nothing can be claimed yet. */
  freshness: MetricFreshness | null;
  /** Qualifier appended to a figure's source when the session is not on real data. */
  dataLabel: string | null;
  /**
   * Payloads that failed their latest reload. Figures they supply are the retained ones from an
   * earlier load, so they are stale whatever the overall state says, and stay stale until that
   * payload reloads: a later refresh of something else does not clear them.
   */
  staleSources: readonly FigureSourceKey[];
  /**
   * When each payload last loaded (ISO). It moves only when that payload is actually reloaded,
   * so a refresh of another workspace, a refresh that finds nothing to reload, and a failed
   * reload all leave it alone. This is the only as-of time a figure may state: the time of the
   * latest refresh says nothing about a payload it did not reload.
   */
  sourceLoadedAt: Partial<Record<FigureSourceKey, string>>;
}

const DATA_LABELS: Record<DataProvenanceKind, string | null> = {
  real: null,
  simulated: "simulated data",
  seeded: "seeded demo data",
  sample: "sample data",
  unknown: "data source unverified"
};

/**
 * A failed refresh leaves the last good figures on screen, so they are stale; a session whose
 * data source is unconfirmed, or that never loaded, is unverified. A refresh can also fail for
 * some payloads only: those keep their retained value and are listed as stale, while the ones
 * that did reload stay fresh. The stale list and load times come from the data hook's record of
 * each payload's own reload outcome (`figureSourcesStale`, `figureSourceLoadedAt`), not from the
 * shell's error map, which is cleared whenever a refresh starts. A time budget is deliberately
 * not applied here: workspaces that poll (Trading, Portfolio) judge their own freshness.
 */
export function deriveWorkspaceFigureContext({
  status,
  provenance,
  staleSources = [],
  sourceLoadedAt = {}
}: {
  status: Pick<RequestLifecycleStatus, "phase" | "inFlight" | "lastSucceededAt" | "settledAt">;
  provenance: DataProvenanceKind;
  /** Payloads whose latest reload failed and that have not reloaded since. */
  staleSources?: readonly FigureSourceKey[];
  /** When each payload last loaded. */
  sourceLoadedAt?: Partial<Record<FigureSourceKey, string>>;
}): WorkspaceFigureContextValue {
  // A partial refresh did load the payloads without an error, so it counts as a load.
  const hasLoaded = Boolean(status.lastSucceededAt || (status.phase === "partial" && status.settledAt));
  const freshness: MetricFreshness | null = status.phase === "failed"
    ? "stale"
    : provenance === "unknown"
      ? "unverified"
      : hasLoaded
        ? "fresh"
        : status.inFlight ? null : "unverified";

  return {
    freshness,
    dataLabel: DATA_LABELS[provenance],
    staleSources,
    sourceLoadedAt
  };
}

/**
 * The as-of time and freshness of the figures one payload supplies. The as-of time is when that
 * payload was last loaded, whatever has been refreshed since, and is not stated when no payload
 * is named or its load was never recorded. A payload whose latest reload failed is stale as of
 * that load, even when the overall refresh is reported as partial or a later refresh of something
 * else succeeded.
 */
export function resolveFigureEvidence(
  context: WorkspaceFigureContextValue | null,
  source?: FigureSourceKey | null
): { asOf: string | null; freshness: MetricFreshness | null } {
  if (!context) {
    return { asOf: null, freshness: null };
  }
  return {
    asOf: source ? context.sourceLoadedAt[source] ?? null : null,
    freshness: source && context.staleSources.includes(source) ? "stale" : context.freshness
  };
}

const WorkspaceFigureContext = createContext<WorkspaceFigureContextValue | null>(null);

export function WorkspaceFigureContextProvider({
  value,
  children
}: {
  value: WorkspaceFigureContextValue | null;
  children: ReactNode;
}) {
  return <WorkspaceFigureContext.Provider value={value}>{children}</WorkspaceFigureContext.Provider>;
}

/** The shell's figure context, or null outside the shell (isolated screens and tests). */
export function useWorkspaceFigureContext(): WorkspaceFigureContextValue | null {
  return useContext(WorkspaceFigureContext);
}
