// The operating scope the current route applies, as one short label ("Account fund-1" or
// "All authorized scopes"). The shell provides it once so every figures strip can print the scope
// it was counted over without per-screen wiring; a screen with a narrower or different scope
// passes its own label instead. Outside the shell (isolated screen tests, pop-out panes) there is
// none and the strip states only what its figures themselves carry.
import { createContext, useContext, type ReactNode } from "react";

const OperatingScopeLabelContext = createContext<string | null>(null);

export function OperatingScopeLabelProvider({ value, children }: { value: string; children: ReactNode }) {
  return <OperatingScopeLabelContext.Provider value={value}>{children}</OperatingScopeLabelContext.Provider>;
}

/** The shell's route-applied scope label, or null where no shell is mounted. */
export function useOperatingScopeLabel(): string | null {
  return useContext(OperatingScopeLabelContext);
}
