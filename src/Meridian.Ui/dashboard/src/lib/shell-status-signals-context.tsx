// The shell's source-of-truth signals (provider posture, data provenance, workspace load, host
// posture), shared with every page status row. A page row folds in the ones that are issues, so
// it can never read "All sources current" while the masthead is warning about the same sources.
import { createContext, useContext, type ReactNode } from "react";
import type { AppShellAttentionSignal } from "@/app-shell.attention";
import type { AppShellOperatingScopeState } from "@/app-shell.operating-scope";

export interface ShellStatusSignals {
  /** Source-kind attention signals, ready ones included. */
  sourceSignals: AppShellAttentionSignal[];
  /** Carried onto recovery links so a fix opens in the operator's current scope. */
  operatingScope: AppShellOperatingScopeState | null;
}

const EMPTY: ShellStatusSignals = { sourceSignals: [], operatingScope: null };

const ShellStatusSignalsContext = createContext<ShellStatusSignals>(EMPTY);

export function ShellStatusSignalsProvider({
  value,
  children
}: {
  value: ShellStatusSignals;
  children: ReactNode;
}) {
  return <ShellStatusSignalsContext.Provider value={value}>{children}</ShellStatusSignalsContext.Provider>;
}

/** The shell's source signals; empty outside the shell (pop-out panes, isolated tests). */
export function useShellStatusSignals(): ShellStatusSignals {
  return useContext(ShellStatusSignalsContext);
}
