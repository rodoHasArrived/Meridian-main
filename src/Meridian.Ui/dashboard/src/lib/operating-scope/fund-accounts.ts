// Fund-account options for the operating-scope picker. The household brokerage
// payload is available to every trading operator (id + display name), so we derive
// suggestions from it defensively. Fund and entity relationships come from the
// organization graph instead (see `structure-options.ts`), when the operator may read it.
import type { BrokerageHouseholdPortfolio } from "@/types";

export interface ScopeFundAccountOption {
  id: string;
  label: string;
  /** The fund the account belongs to, when a loaded read model states it. */
  fundId?: string | null;
  /** The legal entity the account is booked to, when a loaded read model states it. */
  entityId?: string | null;
}

/**
 * Distinct fund accounts discovered from the loaded brokerage household payload,
 * sorted by label. Returns an empty list when nothing is available — the picker's
 * fund-account field still accepts a typed id in that case.
 */
export function collectScopeFundAccounts(
  brokeragePortfolio: BrokerageHouseholdPortfolio | null | undefined
): ScopeFundAccountOption[] {
  const accounts = brokeragePortfolio?.accounts;
  if (!Array.isArray(accounts)) {
    return [];
  }

  const byId = new Map<string, ScopeFundAccountOption>();
  for (const account of accounts) {
    const id = typeof account?.fundAccountId === "string" ? account.fundAccountId.trim() : "";
    if (!id || byId.has(id)) {
      continue;
    }
    const label = typeof account?.displayName === "string" && account.displayName.trim()
      ? account.displayName.trim()
      : id;
    byId.set(id, { id, label });
  }

  return [...byId.values()].sort((a, b) => a.label.localeCompare(b.label));
}
