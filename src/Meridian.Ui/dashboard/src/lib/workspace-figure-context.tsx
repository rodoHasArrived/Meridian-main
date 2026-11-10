// What the shell knows about every workspace's headline figures: when the workstation data was
// last loaded, whether that load is still good, and whether the session runs on real data. The
// shell provides it once; StatStrip reads it so every workspace strip states the same as-of time
// and degrades the same way without each screen re-deriving it.
import { createContext, useContext, type ReactNode } from "react";
import type { DataProvenanceKind } from "@/app-shell.data-provenance-badge";
import type { RequestLifecycleStatus } from "@/hooks/use-request-lifecycle";
import type { MetricFreshness } from "@/types";

export interface WorkspaceFigureContextValue {
  /** ISO timestamp of the last successful workstation load. */
  asOf: string | null;
  /** Null while the first load is still in flight: nothing can be claimed yet. */
  freshness: MetricFreshness | null;
  /** Qualifier appended to a figure's source when the session is not on real data. */
  dataLabel: string | null;
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
 * data source is unconfirmed, or that never loaded, is unverified. A time budget is deliberately
 * not applied here: workspaces that poll (Trading, Portfolio) judge their own freshness.
 */
export function deriveWorkspaceFigureContext({
  status,
  provenance
}: {
  status: Pick<RequestLifecycleStatus, "phase" | "inFlight" | "lastSucceededAt">;
  provenance: DataProvenanceKind;
}): WorkspaceFigureContextValue {
  const freshness: MetricFreshness | null = status.phase === "failed"
    ? "stale"
    : provenance === "unknown"
      ? "unverified"
      : status.lastSucceededAt
        ? "fresh"
        : status.inFlight ? null : "unverified";

  return { asOf: status.lastSucceededAt, freshness, dataLabel: DATA_LABELS[provenance] };
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
