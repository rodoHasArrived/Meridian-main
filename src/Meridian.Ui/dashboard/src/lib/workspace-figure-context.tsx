// What the shell knows about every workspace's headline figures: when each workspace's payload
// was last loaded, whether the latest refresh is still good, and whether the session runs on real
// data. The shell provides it once; StatStrip reads it so every workspace strip states its own
// as-of time and degrades the same way without each screen re-deriving it.
import { createContext, useContext, type ReactNode } from "react";
import type { DataProvenanceKind } from "@/app-shell.data-provenance-badge";
import type { RequestLifecycleStatus } from "@/hooks/use-request-lifecycle";
import type { MetricFreshness, WorkspaceKey } from "@/types";

export interface WorkspaceFigureContextValue {
  /** Null while the first load is still in flight: nothing can be claimed yet. */
  freshness: MetricFreshness | null;
  /** Qualifier appended to a figure's source when the session is not on real data. */
  dataLabel: string | null;
  /**
   * Workspaces whose latest refresh failed while others succeeded. Their figures on screen are the
   * retained ones from an earlier load, so they are stale whatever the overall state says.
   */
  staleWorkspaces: readonly WorkspaceKey[];
  /**
   * When the payload behind each workspace's headline figures last loaded (ISO). It moves only
   * when that payload is actually reloaded, so refreshing another workspace, a refresh that
   * finds nothing to reload, and a failed reload all leave it alone. This is the only as-of time
   * a figure may state: the time of the latest refresh says nothing about a workspace it did not
   * reload.
   */
  workspaceLoadedAt: Partial<Record<WorkspaceKey, string>>;
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
 * some workspaces only: those workspaces keep their retained payload and are listed as stale,
 * while the ones that did refresh stay fresh. A time budget is deliberately not applied here:
 * workspaces that poll (Trading, Portfolio) judge their own freshness.
 */
export function deriveWorkspaceFigureContext({
  status,
  provenance,
  workspaceErrors = {},
  workspaceLoadedAt = {}
}: {
  status: Pick<RequestLifecycleStatus, "phase" | "inFlight" | "lastSucceededAt" | "settledAt">;
  provenance: DataProvenanceKind;
  /** Per-workspace refresh errors; a workspace with one is showing a retained payload. */
  workspaceErrors?: Partial<Record<WorkspaceKey, string | null | undefined>>;
  /** Per-workspace load times of the payloads behind the headline figures. */
  workspaceLoadedAt?: Partial<Record<WorkspaceKey, string>>;
}): WorkspaceFigureContextValue {
  // A partial refresh did load the workspaces without an error, so it counts as a load.
  const hasLoaded = Boolean(status.lastSucceededAt || (status.phase === "partial" && status.settledAt));
  const freshness: MetricFreshness | null = status.phase === "failed"
    ? "stale"
    : provenance === "unknown"
      ? "unverified"
      : hasLoaded
        ? "fresh"
        : status.inFlight ? null : "unverified";
  const staleWorkspaces = (Object.keys(workspaceErrors) as WorkspaceKey[]).filter((key) => Boolean(workspaceErrors[key]));

  return {
    freshness,
    dataLabel: DATA_LABELS[provenance],
    staleWorkspaces,
    workspaceLoadedAt
  };
}

/**
 * The as-of time and freshness for one workspace's figures. The as-of time is when that
 * workspace's payload was last loaded, whatever has been refreshed since, and is not stated for a
 * strip that names no workspace or whose load was never recorded. A workspace whose latest
 * refresh failed is stale as of that load, even when the overall refresh is reported as partial or
 * a later refresh of another workspace succeeded.
 */
export function resolveWorkspaceFigures(
  context: WorkspaceFigureContextValue | null,
  workspace?: WorkspaceKey | null
): { asOf: string | null; freshness: MetricFreshness | null } {
  if (!context) {
    return { asOf: null, freshness: null };
  }
  const asOf = workspace ? context.workspaceLoadedAt[workspace] ?? null : null;
  return {
    asOf,
    freshness: workspace && context.staleWorkspaces.includes(workspace) ? "stale" : context.freshness
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
