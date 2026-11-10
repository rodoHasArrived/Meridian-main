// What the shell knows about every workspace's headline figures: when the workstation data was
// last loaded, whether that load is still good, and whether the session runs on real data. The
// shell provides it once; StatStrip reads it so every workspace strip states the same as-of time
// and degrades the same way without each screen re-deriving it.
import { createContext, useContext, type ReactNode } from "react";
import type { DataProvenanceKind } from "@/app-shell.data-provenance-badge";
import type { RequestLifecycleStatus } from "@/hooks/use-request-lifecycle";
import type { MetricFreshness, WorkspaceKey } from "@/types";

export interface WorkspaceFigureContextValue {
  /** ISO timestamp of the load behind the figures of workspaces that refreshed cleanly. */
  asOf: string | null;
  /** Null while the first load is still in flight: nothing can be claimed yet. */
  freshness: MetricFreshness | null;
  /** Qualifier appended to a figure's source when the session is not on real data. */
  dataLabel: string | null;
  /**
   * Workspaces whose latest refresh failed while others succeeded. Their figures on screen are the
   * retained ones from an earlier load, so they are stale whatever the overall state says.
   */
  staleWorkspaces: readonly WorkspaceKey[];
  /** When everything last loaded together; the best as-of time for a stale workspace's figures. */
  staleAsOf: string | null;
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
 * some workspaces only (`partial`): those workspaces keep their retained payload and are listed
 * as stale, while the ones that did refresh stay fresh as of that settle. A time budget is
 * deliberately not applied here: workspaces that poll (Trading, Portfolio) judge their own
 * freshness.
 */
export function deriveWorkspaceFigureContext({
  status,
  provenance,
  workspaceErrors = {}
}: {
  status: Pick<RequestLifecycleStatus, "phase" | "inFlight" | "lastSucceededAt" | "settledAt">;
  provenance: DataProvenanceKind;
  /** Per-workspace refresh errors; a workspace with one is showing a retained payload. */
  workspaceErrors?: Partial<Record<WorkspaceKey, string | null | undefined>>;
}): WorkspaceFigureContextValue {
  // After a partial refresh the workspaces without an error were loaded at that settle; the
  // last *full* success is older and belongs to the ones that failed.
  const loadedAt = status.phase === "partial" ? status.settledAt ?? status.lastSucceededAt : status.lastSucceededAt;
  const freshness: MetricFreshness | null = status.phase === "failed"
    ? "stale"
    : provenance === "unknown"
      ? "unverified"
      : loadedAt
        ? "fresh"
        : status.inFlight ? null : "unverified";
  const staleWorkspaces = (Object.keys(workspaceErrors) as WorkspaceKey[]).filter((key) => Boolean(workspaceErrors[key]));

  return {
    asOf: loadedAt,
    freshness,
    dataLabel: DATA_LABELS[provenance],
    staleWorkspaces,
    staleAsOf: status.lastSucceededAt
  };
}

/**
 * The as-of time and freshness for one workspace's figures. A workspace whose latest refresh
 * failed is stale as of the last full load, even when the overall refresh is reported as partial
 * or a later refresh of another workspace succeeded.
 */
export function resolveWorkspaceFigures(
  context: WorkspaceFigureContextValue | null,
  workspace?: WorkspaceKey | null
): { asOf: string | null; freshness: MetricFreshness | null } {
  if (!context) {
    return { asOf: null, freshness: null };
  }
  if (workspace && context.staleWorkspaces.includes(workspace)) {
    return { asOf: context.staleAsOf, freshness: "stale" };
  }
  return { asOf: context.asOf, freshness: context.freshness };
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
