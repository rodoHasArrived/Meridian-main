import { describe, expect, it } from "vitest";
import {
  accountKnownOutsideFundScope,
  describeAccountListNarrowing,
  describeFundScopeNarrowing,
  fundScopeNarrowingStatus,
  narrowHouseholdPortfolioToFundScope,
  narrowInlineAccountOptions,
  narrowRowsToFundScope
} from "@/lib/operating-scope/fund-scope-narrowing";
import { collectScopeStructureOptions, UNAVAILABLE_SCOPE_STRUCTURE } from "@/lib/operating-scope/structure-options";
import type { BrokerageHouseholdAccount, BrokerageHouseholdPortfolio, BrokerageHouseholdPosition } from "@/types";

// Synthetic organization graph: two funds under entity E-A, one under E-B, and an account the
// graph lists with no fund or entity.
const STRUCTURE = collectScopeStructureOptions({
  entities: [{ entityId: "E-A", name: "Entity A" }, { entityId: "E-B", name: "Entity B" }],
  funds: [
    { fundId: "F-1", name: "Fund One", entityIds: ["E-A"] },
    { fundId: "F-2", name: "Fund Two", entityIds: ["E-A"] },
    { fundId: "F-3", name: "Fund Three", entityIds: ["E-B"] }
  ],
  accounts: [
    { accountId: "A-1", displayName: "One brokerage", fundId: "F-1", entityId: "E-A" },
    { accountId: "A-2", displayName: "Two brokerage", fundId: "F-2", entityId: "E-A" },
    { accountId: "A-3", displayName: "Three custody", fundId: "F-3", entityId: "E-B" },
    { accountId: "A-4", displayName: "Unfiled", fundId: null, entityId: null }
  ]
});

const rows = [
  { id: "r1", fundAccountId: "a-1" },
  { id: "r2", fundAccountId: "A-2" },
  { id: "r3", fundAccountId: "A-3" },
  { id: "r4", fundAccountId: "A-4" },
  { id: "r5", fundAccountId: "H-9" },
  { id: "r6", fundAccountId: null }
];
const accountOf = (row: (typeof rows)[number]) => row.fundAccountId;

describe("narrowRowsToFundScope", () => {
  it("does nothing when no entity or fund is chosen", () => {
    const result = narrowRowsToFundScope(rows, accountOf, STRUCTURE, {});
    expect(result.status).toBe("inactive");
    expect(result.rows).toEqual(rows);
    expect(describeFundScopeNarrowing(result, "accounts")).toBeNull();
  });

  it("keeps the fund's rows and counts rows whose fund the graph does not record", () => {
    const result = narrowRowsToFundScope(rows, accountOf, STRUCTURE, { fundId: "f-1" });
    expect(result.status).toBe("applied");
    expect(result.rows.map((row) => row.id)).toEqual(["r1"]);
    expect(result.outsideCount).toBe(2);
    // A-4 (no fund), H-9 (not in the graph) and the row with no account.
    expect(result.unknownCount).toBe(3);
    expect(describeFundScopeNarrowing(result, "accounts", { fundId: "Fund One" })).toBe(
      "Narrowed to fund Fund One: showing 1 of 6 accounts. 3 with no recorded fund not shown."
    );
  });

  it("keeps an entity's rows through its funds", () => {
    const result = narrowRowsToFundScope(rows, accountOf, STRUCTURE, { entityId: "E-A" });
    expect(result.rows.map((row) => row.id)).toEqual(["r1", "r2"]);
    expect(result.dimension).toBe("entity");
    expect(result.unknownCount).toBe(3);
    expect(describeFundScopeNarrowing(result, "accounts")).toBe(
      "Narrowed to the chosen entity: showing 2 of 6 accounts. 3 with no recorded entity not shown."
    );
  });

  it("never narrows without the organization graph, and says why", () => {
    const unavailable = narrowRowsToFundScope(rows, accountOf, UNAVAILABLE_SCOPE_STRUCTURE, { fundId: "F-1" });
    expect(unavailable.status).toBe("unavailable");
    expect(unavailable.rows).toEqual(rows);
    expect(describeFundScopeNarrowing(unavailable, "close workflows")).toBe(
      "Not narrowed by entity or fund: the organization structure could not be loaded, so all authorized close workflows are shown."
    );

    const loading = narrowRowsToFundScope(rows, accountOf, UNAVAILABLE_SCOPE_STRUCTURE, { fundId: "F-1" }, true);
    expect(loading.status).toBe("loading");
    expect(loading.rows).toEqual(rows);
  });

  it("reports status from the selection and graph state", () => {
    expect(fundScopeNarrowingStatus(STRUCTURE, { entityId: " " })).toBe("inactive");
    expect(fundScopeNarrowingStatus(STRUCTURE, { fundId: "F-1" })).toBe("applied");
    expect(fundScopeNarrowingStatus(UNAVAILABLE_SCOPE_STRUCTURE, { fundId: "F-1" })).toBe("unavailable");
  });
});

describe("narrowHouseholdPortfolioToFundScope", () => {
  const account = (fundAccountId: string, equity: number, cash: number): BrokerageHouseholdAccount => ({
    fundAccountId,
    providerId: "alpaca",
    externalAccountId: `ext-${fundAccountId}`,
    displayName: fundAccountId,
    accountKind: "Brokerage" as BrokerageHouseholdAccount["accountKind"],
    health: "Healthy" as BrokerageHouseholdAccount["health"],
    cash,
    equity,
    buyingPower: cash * 2,
    currency: "USD",
    syncedAt: "2026-11-14T00:00:00Z",
    positionCount: 1,
    cashTransactionCount: 0,
    warnings: []
  });
  const position = (fundAccountId: string, symbol: string) =>
    ({ fundAccountId, symbol, quantity: 1 } as unknown as BrokerageHouseholdPosition);
  const household: BrokerageHouseholdPortfolio = {
    providerId: "alpaca",
    asOf: "2026-11-14T00:00:00Z",
    totalCash: 60,
    totalEquity: 600,
    totalBuyingPower: 120,
    currency: "USD",
    accounts: [account("A-1", 100, 10), account("A-3", 200, 20), account("H-9", 300, 30)],
    positions: [position("A-1", "AAPL"), position("A-3", "MSFT"), position("H-9", "TSLA")],
    warnings: []
  };

  it("narrows accounts and positions and re-sums totals over the accounts kept", () => {
    const { portfolio, accounts } = narrowHouseholdPortfolioToFundScope(household, STRUCTURE, { fundId: "F-1" });
    expect(accounts.status).toBe("applied");
    expect(accounts.unknownCount).toBe(1);
    expect(portfolio?.accounts.map((item) => item.fundAccountId)).toEqual(["A-1"]);
    expect(portfolio?.positions.map((item) => item.symbol)).toEqual(["AAPL"]);
    expect(portfolio).toMatchObject({ totalEquity: 100, totalCash: 10, totalBuyingPower: 20 });
  });

  it("returns the household unchanged without the graph", () => {
    const result = narrowHouseholdPortfolioToFundScope(household, UNAVAILABLE_SCOPE_STRUCTURE, { fundId: "F-1" });
    expect(result.portfolio).toBe(household);
    expect(result.accounts.status).toBe("unavailable");
  });
});

describe("inline account narrowing", () => {
  const loaded = [{ id: "a-1", label: "Household one" }, { id: "H-9", label: "Household only" }];

  it("narrows suggestions as the scope picker does and counts accounts with no recorded fund", () => {
    const result = narrowInlineAccountOptions(loaded, STRUCTURE, { fundId: "F-1" });
    expect(result.options.map((option) => option.id)).toEqual(["A-1"]);
    expect(result.help).toBe("Showing 1 account under the chosen fund. 2 with no recorded fund not shown.");
  });

  it("offers every loaded account when nothing is chosen or the graph is unavailable", () => {
    expect(narrowInlineAccountOptions(loaded, UNAVAILABLE_SCOPE_STRUCTURE, { fundId: "F-1" })).toMatchObject({
      options: loaded,
      help: null
    });
    expect(narrowInlineAccountOptions(loaded, STRUCTURE, {}).help).toBeNull();
  });

  it("flags only accounts the graph places outside the chosen scope", () => {
    expect(accountKnownOutsideFundScope("A-3", loaded, STRUCTURE, { fundId: "F-1" })).toBe(true);
    expect(accountKnownOutsideFundScope("A-1", loaded, STRUCTURE, { fundId: "F-1" })).toBe(false);
    expect(accountKnownOutsideFundScope("H-9", loaded, STRUCTURE, { fundId: "F-1" })).toBe(false);
    expect(accountKnownOutsideFundScope("A-3", loaded, UNAVAILABLE_SCOPE_STRUCTURE, { fundId: "F-1" })).toBe(false);
    expect(accountKnownOutsideFundScope("A-3", loaded, STRUCTURE, {})).toBe(false);
  });

  it("words the account-list help like the scope picker", () => {
    expect(describeAccountListNarrowing(
      { accounts: [], accountsConstrained: true, accountsWithoutKnownRelationship: 0 },
      { entityId: "E-B" }
    )).toBe("Showing 0 accounts under the chosen entity.");
  });
});
