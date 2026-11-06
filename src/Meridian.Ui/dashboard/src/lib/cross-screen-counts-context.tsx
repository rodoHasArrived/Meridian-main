// The shell's cross-screen counts (A3), loaded on every route and read by screens that do not hold
// the source lists themselves (Settings, the shell chrome). Screens that do hold a list count it with
// lib/cross-screen-counts.ts and fall back to these.
import { createContext, useContext, type ReactNode } from "react";
import type { WorkstationCrossScreenCounts } from "@/types/cross-screen-counts";

const CrossScreenCountsContext = createContext<WorkstationCrossScreenCounts | null>(null);

export function CrossScreenCountsProvider({
  counts,
  children
}: {
  counts: WorkstationCrossScreenCounts | null;
  children: ReactNode;
}) {
  return <CrossScreenCountsContext.Provider value={counts}>{children}</CrossScreenCountsContext.Provider>;
}

/** The server's cross-screen counts, or null before they load or outside the shell. */
export function useSharedCrossScreenCounts(): WorkstationCrossScreenCounts | null {
  return useContext(CrossScreenCountsContext);
}
