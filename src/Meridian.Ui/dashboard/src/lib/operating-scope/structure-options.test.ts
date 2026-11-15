import { describe, expect, it, vi } from "vitest";
import type { ScopeStructureOptions } from "@/lib/operating-scope/structure-options";
import {
  accountConflictsWithScope,
  collectScopeStructureOptions,
  constrainScopeOptions,
  fundConflictsWithEntity,
  getScopeOptions,
  getScopeStructureGraph,
  loadScopeStructure,
  mergeScopeAccountOptions,
  SCOPE_OPTIONS_ENDPOINT,
  SCOPE_STRUCTURE_GRAPH_ENDPOINT,
  scopeStructureHelp,
  scopeValueLabels,
  UNAVAILABLE_SCOPE_STRUCTURE
} from "@/lib/operating-scope/structure-options";
import { ApiError } from "@/lib/api-errors";

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

  it("reads withheld fund and entity flags defensively, only beside a null relationship", () => {
    const structure = collectScopeStructureOptions({
      accounts: [
        { accountId: "W-1", displayName: "Withheld", fundId: null, entityId: null, fundWithheld: true, entityWithheld: true },
        { accountId: "W-2", displayName: "Visible fund", fundId: "F-1", entityId: null, fundWithheld: true, entityWithheld: "yes" },
        { accountId: "W-3", displayName: "No flags", fundId: null, entityId: null }
      ]
    });

    expect(structure.accounts.find((account) => account.id === "W-1")).toMatchObject({ fundWithheld: true, entityWithheld: true });
    // A stated fund wins over a stray flag, and only a literal true counts.
    expect(structure.accounts.find((account) => account.id === "W-2")).toEqual({
      id: "W-2",
      label: "Visible fund",
      fundId: "F-1",
      entityId: null
    });
    // The organization graph fallback sends no flags: nothing is withheld.
    expect(structure.accounts.find((account) => account.id === "W-3")).not.toHaveProperty("fundWithheld");
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

  it("does not place an account under an entity through its fund when its own entity is withheld", () => {
    const structure = collectScopeStructureOptions({
      entities: [{ entityId: "E-A", name: "Entity A" }],
      funds: [{ fundId: "F-1", name: "Fund One", entityIds: ["E-A"] }],
      accounts: [
        { accountId: "A-1", displayName: "Held, no entity", fundId: "F-1", entityId: null },
        { accountId: "A-2", displayName: "Booked to a hidden entity", fundId: "F-1", entityId: null, entityWithheld: true }
      ]
    });

    const byEntity = constrainScopeOptions(structure, structure.accounts, { entityId: "E-A" });
    expect(byEntity.accounts.map((account) => account.id)).toEqual(["A-1"]);
    expect(byEntity.accountsWithWithheldRelationship).toBe(1);
    // A chosen fund alone still keeps it: the fund is visible and is its own.
    expect(constrainScopeOptions(structure, structure.accounts, { fundId: "F-1" }).accounts.map((account) => account.id))
      .toEqual(["A-2", "A-1"]);
  });

  it("counts accounts whose fund or entity is withheld apart from ones with none, and never calls them conflicts", () => {
    const withheld = collectScopeStructureOptions({
      entities: [{ entityId: "E-A", name: "Entity A" }],
      funds: [{ fundId: "F-1", name: "Fund One", entityIds: ["E-A"] }],
      accounts: [
        { accountId: "A-1", displayName: "In fund", fundId: "F-1", entityId: "E-A" },
        { accountId: "W-1", displayName: "Fund withheld", fundId: null, entityId: "E-A", fundWithheld: true },
        { accountId: "W-2", displayName: "Both withheld", fundId: null, entityId: null, fundWithheld: true, entityWithheld: true },
        { accountId: "U-1", displayName: "Unfiled", fundId: null, entityId: null }
      ]
    });

    const byFund = constrainScopeOptions(withheld, withheld.accounts, { fundId: "F-1" });
    expect(byFund.accounts.map((account) => account.id)).toEqual(["A-1"]);
    expect(byFund.accountsWithWithheldRelationship).toBe(2);
    expect(byFund.accountsWithoutKnownRelationship).toBe(1);

    const byEntity = constrainScopeOptions(withheld, withheld.accounts, { entityId: "E-A" });
    // W-1 is booked to E-A itself, so it is kept; W-2 cannot be placed.
    expect(byEntity.accounts.map((account) => account.id)).toEqual(["W-1", "A-1"]);
    expect(byEntity.accountsWithWithheldRelationship).toBe(1);
    expect(byEntity.accountsWithoutKnownRelationship).toBe(1);

    expect(accountConflictsWithScope(withheld, withheld.accounts, "W-1", { fundId: "F-1" })).toBe(false);
    expect(accountConflictsWithScope(withheld, withheld.accounts, "W-2", { fundId: "F-1" })).toBe(false);
    expect(constrainScopeOptions(withheld, withheld.accounts, {}).accountsWithWithheldRelationship).toBe(0);
  });

  it("keeps an account the picker offers when its fund's entity comes from another account", () => {
    // A-1 links fund F-1 to entity E-A; A-2 is in F-1 with no entity of its own.
    const linked: ScopeStructureOptions = {
      status: "ready",
      entities: [{ id: "E-A", label: "Entity A" }],
      funds: [{ id: "F-1", label: "Fund 1", entityIds: [] }],
      reason: null
    } as unknown as ScopeStructureOptions;
    const linkedAccounts = [
      { id: "A-1", label: "Account 1", fundId: "F-1", entityId: "E-A" },
      { id: "A-2", label: "Account 2", fundId: "F-1", entityId: null }
    ] as unknown as Parameters<typeof constrainScopeOptions>[1];
    const offered = constrainScopeOptions(linked, linkedAccounts, { entityId: "E-A" }).accounts.map((account) => account.id);
    for (const id of offered) {
      expect(accountConflictsWithScope(linked, linkedAccounts, id, { entityId: "E-A" })).toBe(false);
    }
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

describe("getScopeOptions", () => {
  it("reads the caller's scope options and refuses development fixtures", async () => {
    const payload = { entities: [], funds: [], accounts: [] };
    apiGetJson.mockResolvedValue(payload);
    const controller = new AbortController();
    await expect(getScopeOptions({ signal: controller.signal })).resolves.toBe(payload);
    expect(SCOPE_OPTIONS_ENDPOINT).toBe("/api/workstation/scope-options");
    expect(apiGetJson).toHaveBeenCalledWith(SCOPE_OPTIONS_ENDPOINT, {
      signal: controller.signal,
      allowDevelopmentFallback: false
    });
  });
});

describe("loadScopeStructure", () => {
  const notFound = () => new ApiError({ path: "/api/workstation/scope-options", status: 404 });

  it("uses the scope options when the host serves them, without reading the graph", async () => {
    const payload = { entities: [], funds: [], accounts: [] };
    const scopeOptions = vi.fn().mockResolvedValue(payload);
    const graph = vi.fn();
    await expect(loadScopeStructure({}, { scopeOptions, graph })).resolves.toBe(payload);
    expect(graph).not.toHaveBeenCalled();
  });

  it("falls back to the organization graph only when the host has no scope-options route", async () => {
    const controller = new AbortController();
    const scopeOptions = vi.fn().mockRejectedValue(notFound());
    const graph = vi.fn().mockResolvedValue(GRAPH);
    await expect(loadScopeStructure({ signal: controller.signal }, { scopeOptions, graph })).resolves.toBe(GRAPH);
    expect(graph).toHaveBeenCalledWith({ signal: controller.signal });
  });

  it("passes a refusal or any other failure on rather than retrying against the graph", async () => {
    const graph = vi.fn().mockResolvedValue(GRAPH);
    const forbidden = new ApiError({ path: "/api/workstation/scope-options", status: 403 });
    await expect(loadScopeStructure({}, { scopeOptions: vi.fn().mockRejectedValue(forbidden), graph })).rejects.toBe(forbidden);
    const unavailable = new ApiError({ path: "/api/workstation/scope-options", status: 503 });
    await expect(loadScopeStructure({}, { scopeOptions: vi.fn().mockRejectedValue(unavailable), graph })).rejects.toBe(unavailable);
    const network = new TypeError("Failed to fetch");
    await expect(loadScopeStructure({}, { scopeOptions: vi.fn().mockRejectedValue(network), graph })).rejects.toBe(network);
    expect(graph).not.toHaveBeenCalled();
  });

  it("does not start the fallback read once the request was aborted", async () => {
    const controller = new AbortController();
    controller.abort();
    const missing = notFound();
    const graph = vi.fn();
    await expect(
      loadScopeStructure({ signal: controller.signal }, { scopeOptions: vi.fn().mockRejectedValue(missing), graph })
    ).rejects.toBe(missing);
    expect(graph).not.toHaveBeenCalled();
  });
});

describe("scope-options payload", () => {
  it("reads the permission-limited shape: no entities or funds, accounts with withheld relationships", () => {
    const structure = collectScopeStructureOptions({
      entities: [],
      funds: [],
      accounts: [{ accountId: "A-1", displayName: "One brokerage", fundId: null, entityId: null }]
    });
    expect(structure).toEqual({
      status: "ready",
      entities: [],
      funds: [],
      accounts: [{ id: "A-1", label: "One brokerage", fundId: null, entityId: null }]
    });
  });

  it("reads the empty shape as ready with nothing to choose, not as a failed load", () => {
    expect(collectScopeStructureOptions({ entities: [], funds: [], accounts: [] })).toEqual({
      status: "ready",
      entities: [],
      funds: [],
      accounts: []
    });
  });
});

describe("scopeStructureHelp", () => {
  const ready = collectScopeStructureOptions(GRAPH);

  it("says why a list is missing, and nothing when both lists are there", () => {
    expect(scopeStructureHelp(ready, true)).toBe("Loading entities and funds…");
    expect(scopeStructureHelp(ready, false)).toBeNull();
    expect(scopeStructureHelp(UNAVAILABLE_SCOPE_STRUCTURE, false)).toBe(
      "Entity and fund lists could not be loaded, so enter an id. Accounts are not narrowed by entity or fund."
    );
    expect(scopeStructureHelp({ ...ready, entities: [], funds: [] }, false)).toBe(
      "No entities or funds are visible to you, so enter an id."
    );
    expect(scopeStructureHelp({ ...ready, entities: [] }, false)).toBe("No entities are visible to you, so enter an entity id.");
    expect(scopeStructureHelp({ ...ready, funds: [] }, false)).toBe("No funds are visible to you, so enter a fund id.");
  });
});
