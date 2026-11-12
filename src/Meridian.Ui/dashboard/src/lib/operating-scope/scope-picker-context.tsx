// Lets a screen offer "Choose account" (or any scope step) as its one next action by opening the
// shell's scope picker, instead of each screen growing its own selector. The shell provides the
// opener; outside the shell (isolated screen tests, pop-out panes) there is none and callers fall
// back to explanatory text.
import { createContext, useContext, type ReactNode } from "react";

const ScopePickerOpenerContext = createContext<(() => void) | null>(null);

export function ScopePickerOpenerProvider({ open, children }: { open: () => void; children: ReactNode }) {
  return <ScopePickerOpenerContext.Provider value={open}>{children}</ScopePickerOpenerContext.Provider>;
}

/** The shell's scope-picker opener, or null where no shell is mounted. */
export function useOpenScopePicker(): (() => void) | null {
  return useContext(ScopePickerOpenerContext);
}
