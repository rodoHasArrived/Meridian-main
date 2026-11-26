// Lets a screen offer "Choose account" (or any scope step) as its one next action by opening the
// shell's scope picker, instead of each screen growing its own selector. The shell provides the
// opener; outside the shell (isolated screen tests, pop-out panes) there is none and callers fall
// back to explanatory text.
import { createContext, useContext, type ReactNode } from "react";
import type { AppShellOperatingScopeInput } from "@/app-shell.operating-scope";
import type { ScopeFundAccountOption } from "@/lib/operating-scope/fund-accounts";
import type { ScopeStructureOptions } from "@/lib/operating-scope/structure-options";

const ScopePickerOpenerContext = createContext<(() => void) | null>(null);

export function ScopePickerOpenerProvider({ open, children }: { open: () => void; children: ReactNode }) {
  return <ScopePickerOpenerContext.Provider value={open}>{children}</ScopePickerOpenerContext.Provider>;
}

/** The shell's scope-picker opener, or null where no shell is mounted. */
export function useOpenScopePicker(): (() => void) | null {
  return useContext(ScopePickerOpenerContext);
}

/**
 * The shell's fund-account step for screens that need exactly one account: the known account
 * options and a setter that applies the account to the operating scope (and so to the URL),
 * keeping every other scope dimension. Used by the inline account picker.
 */
export interface FundAccountScopeSelection {
  options: ScopeFundAccountOption[];
  select: (fundAccountId: string) => void;
  /** Most recent account, offered as a draft only when it fits the current scope. */
  lastUsedAccountId?: string | null;
  /**
   * The organization graph, so the inline picker narrows `options` by the chosen entity and fund
   * exactly as the scope picker does. Omitted or unavailable: nothing is narrowed.
   */
  structure?: ScopeStructureOptions;
  /** True while the organization graph is loading. */
  structureLoading?: boolean;
  /** The current scope's entity, fund and fund account. */
  scope?: Pick<AppShellOperatingScopeInput, "entityId" | "fundId" | "fundAccountId">;
}

const FundAccountScopeSelectionContext = createContext<FundAccountScopeSelection | null>(null);

export function FundAccountScopeSelectionProvider({ value, children }: { value: FundAccountScopeSelection; children: ReactNode }) {
  return <FundAccountScopeSelectionContext.Provider value={value}>{children}</FundAccountScopeSelectionContext.Provider>;
}

/** The shell's fund-account setter, or null where no shell is mounted. */
export function useFundAccountScopeSelection(): FundAccountScopeSelection | null {
  return useContext(FundAccountScopeSelectionContext);
}
