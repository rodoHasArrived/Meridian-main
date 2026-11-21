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
  /**
   * The session's verdict, before any payload's own reload outcome: unverified when the data
   * source is unconfirmed or nothing ever loaded, null while the first load is still in flight
   * (nothing can be claimed yet), fresh otherwise. Staleness is never decided here.
   */
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
  /**
   * The fund account each account-scoped payload on screen was loaded for (null for every
   * account). The operator's selected account can change before, or without, that payload
   * reloading, so a figure's scope is the account its payload was loaded for, not the selection.
   */
  sourceScope?: Partial<Record<FigureSourceKey, string | null>>;
}

const DATA_LABELS: Record<DataProvenanceKind, string | null> = {
  real: null,
  simulated: "simulated data",
  seeded: "seeded demo data",
  sample: "sample data",
  unknown: "data source unverified"
};

/**
 * A session whose data source is unconfirmed, or that never loaded, is unverified. Staleness is
 * decided per payload, from the data hook's record of each payload's own reload outcome
 * (`figureSourcesStale`, `figureSourceLoadedAt`): a payload whose reload failed keeps its retained
 * value and is stale until it reloads, while the ones that did reload stay fresh. The refresh
 * lifecycle's own outcome is not used for that, because it describes one refresh, not a payload:
 * a failed refresh says nothing about a payload a later poll reloaded, and a successful one says
 * nothing about a payload it did not touch. Nor is the shell's error map, which is cleared
 * whenever a refresh starts. A time budget is deliberately not applied here: workspaces that poll
 * (Trading, Portfolio) judge their own freshness.
 */
export function deriveWorkspaceFigureContext({
  status,
  provenance,
  staleSources = [],
  sourceLoadedAt = {},
  sourceScope = {}
}: {
  status: Pick<RequestLifecycleStatus, "phase" | "inFlight" | "lastSucceededAt" | "settledAt">;
  provenance: DataProvenanceKind;
  /** Payloads whose latest reload failed and that have not reloaded since. */
  staleSources?: readonly FigureSourceKey[];
  /** When each payload last loaded. */
  sourceLoadedAt?: Partial<Record<FigureSourceKey, string>>;
  /** The fund account each account-scoped payload was loaded for. */
  sourceScope?: Partial<Record<FigureSourceKey, string | null>>;
}): WorkspaceFigureContextValue {
  // A partial refresh did load the payloads without an error, so it counts as a load.
  const hasLoaded = Boolean(status.lastSucceededAt || (status.phase === "partial" && status.settledAt));
  const freshness: MetricFreshness | null = provenance === "unknown"
    ? "unverified"
    : hasLoaded
      ? "fresh"
      : status.inFlight ? null : "unverified";

  return {
    freshness,
    dataLabel: DATA_LABELS[provenance],
    staleSources,
    sourceLoadedAt,
    sourceScope
  };
}

/**
 * The fund account a payload on screen was loaded for: an id, null when it was loaded for every
 * account, or undefined when no scope was recorded (outside the shell, or a payload that is not
 * account-scoped).
 */
export function resolveFigureScope(
  context: WorkspaceFigureContextValue | null,
  source: FigureSourceKey
): string | null | undefined {
  return context?.sourceScope?.[source];
}

/**
 * The as-of time and freshness of the figures one payload supplies. The as-of time is when that
 * payload was last loaded, whatever has been refreshed since, and is not stated when no payload
 * is named or its load was never recorded. A payload whose latest reload failed is stale as of
 * that load, whatever a refresh of something else did; one that has loaded and not failed since
 * is fresh, whatever the latest refresh's outcome was. An unconfirmed session keeps every figure
 * unverified.
 */
export function resolveFigureEvidence(
  context: WorkspaceFigureContextValue | null,
  source?: FigureSourceKey | null
): { asOf: string | null; freshness: MetricFreshness | null } {
  if (!context) {
    return { asOf: null, freshness: null };
  }
  if (!source) {
    return { asOf: null, freshness: context.freshness };
  }

  const asOf = context.sourceLoadedAt[source] ?? null;
  const freshness = context.staleSources.includes(source)
    ? "stale"
    : context.freshness === "unverified" || !asOf
      ? context.freshness
      : "fresh";
  return { asOf, freshness };
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
