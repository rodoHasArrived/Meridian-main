// Fund-account options for the operating-scope picker. There is no fund-account
// list endpoint; the household brokerage payload is the cleanest available source
// (id + display name), so we derive suggestions from it defensively.
import type { BrokerageHouseholdPortfolio } from "@/types";

export interface ScopeFundAccountOption {
  id: string;
  label: string;
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
