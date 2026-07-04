import { describe, expect, it } from "vitest";
import { collectScopeFundAccounts } from "@/lib/operating-scope/fund-accounts";
import type { BrokerageHouseholdPortfolio } from "@/types";

function portfolio(accounts: unknown): BrokerageHouseholdPortfolio {
  return { accounts } as unknown as BrokerageHouseholdPortfolio;
}

describe("collectScopeFundAccounts", () => {
  it("returns an empty list for missing or malformed payloads", () => {
    expect(collectScopeFundAccounts(null)).toEqual([]);
    expect(collectScopeFundAccounts(undefined)).toEqual([]);
    expect(collectScopeFundAccounts(portfolio("nope"))).toEqual([]);
  });

  it("derives distinct id/label options sorted by label", () => {
    const result = collectScopeFundAccounts(
      portfolio([
        { fundAccountId: "fund-b", displayName: "Beta Fund" },
        { fundAccountId: "fund-a", displayName: "Alpha Fund" },
        { fundAccountId: "fund-b", displayName: "Beta Fund (dup)" }
      ])
    );

    expect(result).toEqual([
      { id: "fund-a", label: "Alpha Fund" },
      { id: "fund-b", label: "Beta Fund" }
    ]);
  });

  it("falls back to the id when the display name is missing and skips invalid rows", () => {
    const result = collectScopeFundAccounts(
      portfolio([
        { fundAccountId: "fund-x", displayName: "  " },
        { fundAccountId: "  ", displayName: "ignored" },
        { displayName: "no id" },
        { fundAccountId: "fund-y" }
      ])
    );

    expect(result).toEqual([
      { id: "fund-x", label: "fund-x" },
      { id: "fund-y", label: "fund-y" }
    ]);
  });
});
