// The shell's entity/fund narrowing inputs for screens that filter fund-account rows client-side:
// the chosen entity and fund, the organization graph that places accounts under them, and the
// names the scope bar prints. Outside the shell (isolated tests, pop-out panes) there is none, and
// screens show every row, as they did before entity and fund narrowing existed.
import { createContext, useContext, type ReactNode } from "react";
import type { OperatingScopeValueLabels } from "@/app-shell.operating-scope";
import type { ScopeStructureOptions } from "@/lib/operating-scope/structure-options";
import {
  describeFundScopeNarrowing,
  narrowRowsToFundScope,
  type FundScopeRowNarrowing
} from "@/lib/operating-scope/fund-scope-narrowing";

export interface FundScopeNarrowingContextValue {
  structure: ScopeStructureOptions;
  /** True while the organization graph is loading. */
  loading: boolean;
  entityId: string | null;
  fundId: string | null;
  /** Display names (or ids) for the chosen entity and fund. */
  names: Pick<OperatingScopeValueLabels, "entityId" | "fundId">;
}

const FundScopeNarrowingContext = createContext<FundScopeNarrowingContextValue | null>(null);

export function FundScopeNarrowingProvider({ value, children }: { value: FundScopeNarrowingContextValue; children: ReactNode }) {
  return <FundScopeNarrowingContext.Provider value={value}>{children}</FundScopeNarrowingContext.Provider>;
}

/** The shell's entity/fund narrowing inputs, or null where no shell is mounted. */
export function useFundScopeNarrowingContext(): FundScopeNarrowingContextValue | null {
  return useContext(FundScopeNarrowingContext);
}

export type FundScopeRowsResult<T> = FundScopeRowNarrowing<T> & {
  /** What the narrowing did, to print next to the rows; null when no entity or fund is chosen. */
  note: string | null;
};

/** Narrows fund-account rows to the shell's chosen entity/fund (see `narrowRowsToFundScope`). */
export function narrowRowsWithFundScopeContext<T>(
  context: FundScopeNarrowingContextValue | null,
  rows: readonly T[],
  getFundAccountId: (row: T) => string | null | undefined,
  noun: string
): FundScopeRowsResult<T> {
  if (!context) {
    return { status: "inactive", rows: [...rows], totalCount: rows.length, outsideCount: 0, unknownCount: 0, dimension: null, note: null };
  }
  const narrowing = narrowRowsToFundScope(
    rows,
    getFundAccountId,
    context.structure,
    { entityId: context.entityId, fundId: context.fundId },
    context.loading
  );
  return { ...narrowing, note: describeFundScopeNarrowing(narrowing, noun, context.names) };
}
