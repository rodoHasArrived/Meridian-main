import { describe, expect, it, vi } from "vitest";
import {
  accountConflictsWithScope,
  collectScopeStructureOptions,
  constrainScopeOptions,
  fundConflictsWithEntity,
  getScopeStructureGraph,
  mergeScopeAccountOptions,
  SCOPE_STRUCTURE_GRAPH_ENDPOINT,
  scopeValueLabels,
  UNAVAILABLE_SCOPE_STRUCTURE
} from "@/lib/operating-scope/structure-options";

const apiGetJson = vi.hoisted(() => vi.fn());
vi.mock("@/lib/api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/lib/api")>()),
  apiGetJson
}));

// The shape of OrganizationStructureGraphDto the picker reads (other members omitted).
const GRAPH = {
  entities: [
    { entityId: "E-B", name: "Entity B" },
    { entityId: "E-A", name: "Entity A" },
    { entityId: "", name: "no id" }
  ],
  funds: [
    { fundId: "F-1", name: "Fund One", entityIds: ["E-A"] },
    { fundId: "F-2", name: "Fund Two", entityIds: [] },
    { fundId: "F-3", name: "  ", entityIds: ["E-B", 42] }
  ],
  accounts: [
    { accountId: "A-1", displayName: "One brokerage", fundId: "F-1", entityId: "E-A" },
    { accountId: "A-2", displayName: "Two bank", fundId: "F-2", entityId: "E-A" },
    { accountId: "A-3", displayName: "Three custody", fundId: "F-3", entityId: null },
    { accountId: "A-4", displayName: "Unfiled", fundId: null, entityId: null }
  ]
};

describe("collectScopeStructureOptions", () => {
  it("reads entities, funds and accounts with their relationships, sorted by label", () => {
    const structure = collectScopeStructureOptions(GRAPH);

    expect(structure.status).toBe("ready");
    expect(structure.entities).toEqual([{ id: "E-A", label: "Entity A" }, { id: "E-B", label: "Entity B" }]);
    expect(structure.funds).toEqual([
      { id: "F-3", label: "F-3", entityIds: ["E-B"] },
      { id: "F-1", label: "Fund One", entityIds: ["E-A"] },
      { id: "F-2", label: "Fund Two", entityIds: [] }
    ]);
    expect(structure.accounts.find((account) => account.id === "A-1")).toEqual({
      id: "A-1",
      label: "One brokerage",
      fundId: "F-1",
      entityId: "E-A"
    });
  });

  it("is unavailable for a missing or unrelated payload", () => {
    expect(collectScopeStructureOptions(null)).toBe(UNAVAILABLE_SCOPE_STRUCTURE);
    expect(collectScopeStructureOptions({ unrelated: true })).toBe(UNAVAILABLE_SCOPE_STRUCTURE);
    expect(collectScopeStructureOptions([])).toBe(UNAVAILABLE_SCOPE_STRUCTURE);
  });
});

describe("constrainScopeOptions", () => {
  const structure = collectScopeStructureOptions(GRAPH);
  const accounts = mergeScopeAccountOptions(structure, [
    { id: "a-1", label: "Household duplicate (case differs)" },
    { id: "H-9", label: "Household only" }
  ]);

  it("merges household accounts the graph does not list, without a relationship", () => {
    expect(accounts.map((account) => account.id)).toEqual(["H-9", "A-1", "A-3", "A-2", "A-4"]);
    expect(accounts.find((account) => account.id === "H-9")).toEqual({ id: "H-9", label: "Household only" });
  });

  it("narrows nothing without a selection or without the graph", () => {
    expect(constrainScopeOptions(structure, accounts, {})).toMatchObject({ accountsConstrained: false, fundsConstrained: false });
    expect(constrainScopeOptions(UNAVAILABLE_SCOPE_STRUCTURE, accounts, { entityId: "E-A", fundId: "F-1" }).accounts).toHaveLength(5);
  });

  it("keeps the funds holding an entity, including through an account booked to it", () => {
    const result = constrainScopeOptions(structure, accounts, { entityId: "e-a" });

    // F-1 holds E-A directly; F-2 owns account A-2 booked to E-A.
    expect(result.funds.map((fund) => fund.id)).toEqual(["F-1", "F-2"]);
    expect(result.accounts.map((account) => account.id)).toEqual(["A-1", "A-2"]);
    expect(result.accountsWithoutKnownRelationship).toBe(2);
  });

  it("keeps an account without its own entity when its fund holds the entity", () => {
    const result = constrainScopeOptions(structure, accounts, { entityId: "E-B" });
    expect(result.funds.map((fund) => fund.id)).toEqual(["F-3"]);
    expect(result.accounts.map((account) => account.id)).toEqual(["A-3"]);
  });

  it("keeps a fund's own accounts and counts the ones with no recorded fund", () => {
    const result = constrainScopeOptions(structure, accounts, { fundId: "F-1" });
    expect(result.funds).toHaveLength(3);
    expect(result.accounts.map((account) => account.id)).toEqual(["A-1"]);
    expect(result.accountsWithoutKnownRelationship).toBe(2);
  });

  it("detects a fund or account known to sit outside the selection, and never guesses", () => {
    expect(fundConflictsWithEntity(structure, accounts, "F-3", "E-A")).toBe(true);
    expect(fundConflictsWithEntity(structure, accounts, "F-2", "E-A")).toBe(false);
    expect(fundConflictsWithEntity(structure, accounts, "unknown-fund", "E-A")).toBe(false);
    expect(fundConflictsWithEntity(UNAVAILABLE_SCOPE_STRUCTURE, accounts, "F-3", "E-A")).toBe(false);

    expect(accountConflictsWithScope(structure, accounts, "A-1", { fundId: "F-2" })).toBe(true);
    expect(accountConflictsWithScope(structure, accounts, "A-1", { fundId: "F-1", entityId: "E-A" })).toBe(false);
    expect(accountConflictsWithScope(structure, accounts, "H-9", { fundId: "F-2" })).toBe(false);
    expect(accountConflictsWithScope(structure, accounts, "typed-id", { fundId: "F-2" })).toBe(false);
  });
});

describe("scopeValueLabels", () => {
  it("names the ids the graph knows and leaves the rest to print as ids", () => {
    const structure = collectScopeStructureOptions(GRAPH);
    expect(scopeValueLabels({ entityId: "e-a", fundId: "F-3", fundAccountId: "A-1" }, structure, structure.accounts)).toEqual({
      entityId: "Entity A",
      fundId: undefined,
      fundAccountId: "One brokerage"
    });
    expect(scopeValueLabels({ entityId: "E-A" }, UNAVAILABLE_SCOPE_STRUCTURE, [])).toEqual({
      entityId: undefined,
      fundId: undefined,
      fundAccountId: undefined
    });
  });
});

describe("getScopeStructureGraph", () => {
  it("reads active nodes from the organization graph and refuses development fixtures", async () => {
    apiGetJson.mockResolvedValue(GRAPH);
    const controller = new AbortController();
    await expect(getScopeStructureGraph({ signal: controller.signal })).resolves.toBe(GRAPH);
    expect(SCOPE_STRUCTURE_GRAPH_ENDPOINT).toBe("/api/fund-structure/graph?activeOnly=true");
    expect(apiGetJson).toHaveBeenCalledWith(SCOPE_STRUCTURE_GRAPH_ENDPOINT, {
      signal: controller.signal,
      allowDevelopmentFallback: false
    });
  });
});
