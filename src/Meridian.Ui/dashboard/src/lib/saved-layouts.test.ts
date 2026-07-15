import { describe, expect, it } from "vitest";
import {
  MAX_SAVED_LAYOUTS,
  SAVED_LAYOUTS_STORAGE_KEY,
  buildLayoutRestorePlan,
  captureLayoutState,
  createUserLayout,
  decodeLayoutStateToken,
  encodeLayoutStateToken,
  listTeamLayouts,
  normalizeLayoutState,
  readUserLayouts,
  removeUserLayout,
  upsertUserLayout,
  type SavedLayout,
  type SavedLayoutState
} from "@/lib/saved-layouts";

function memoryStorage(seed: Record<string, string> = {}): Storage {
  const map = new Map(Object.entries(seed));
  return {
    get length() {
      return map.size;
    },
    clear: () => map.clear(),
    getItem: (key: string) => map.get(key) ?? null,
    key: (index: number) => [...map.keys()][index] ?? null,
    removeItem: (key: string) => void map.delete(key),
    setItem: (key: string, value: string) => void map.set(key, value)
  } satisfies Storage;
}

const sampleState: SavedLayoutState = {
  route: "/accounting/reconciliation?fundAccountId=fund-a",
  operatingScope: { fundAccountId: "fund-a", symbol: "AAPL" },
  density: "compact",
  panes: ["live-quotes", "watchlist"]
};

function userLayout(overrides: Partial<SavedLayout> = {}): SavedLayout {
  return {
    id: "layout-1",
    name: "Month-End Close",
    description: "Close-day arrangement",
    source: "user",
    state: sampleState,
    ...overrides
  };
}

describe("saved-layouts serialization", () => {
  it("round-trips a layout state through the view-state envelope", () => {
    const token = encodeLayoutStateToken(sampleState);
    expect(token).toBeTruthy();
    expect(decodeLayoutStateToken(token)).toEqual(sampleState);
  });

  it("rejects a foreign or malformed token", () => {
    expect(decodeLayoutStateToken(null)).toBeNull();
    expect(decodeLayoutStateToken("not-a-token")).toBeNull();
  });

  it("captures and normalizes raw inputs", () => {
    const state = captureLayoutState({
      route: "  /trading/readiness  ",
      operatingScope: { symbol: "  AAPL  ", provider: "" },
      density: "spacious",
      panes: ["watchlist", "watchlist", "unknown-pane"]
    });
    expect(state.route).toBe("/trading/readiness");
    expect(state.operatingScope).toEqual({ symbol: "AAPL" });
    expect(state.panes).toEqual(["watchlist"]);
    expect(state.density).toBe("spacious");
  });
});

describe("saved-layouts normalization", () => {
  it("drops off-site or protocol-relative routes", () => {
    expect(normalizeLayoutState({ route: "https://evil.example" })).toBeNull();
    expect(normalizeLayoutState({ route: "//evil.example" })).toBeNull();
    expect(normalizeLayoutState({ route: "not-a-route" })).toBeNull();
  });

  it("falls back to default density and filters unknown panes", () => {
    const state = normalizeLayoutState({
      route: "/data/quotes",
      density: "ultra",
      panes: ["live-quotes", 42, "bogus"]
    });
    expect(state).toEqual({
      route: "/data/quotes",
      operatingScope: {},
      density: "default",
      panes: ["live-quotes"]
    });
  });
});

describe("saved-layouts store", () => {
  it("persists user layouts as envelope tokens and re-hydrates them", () => {
    const storage = memoryStorage();
    upsertUserLayout(userLayout(), storage);

    const raw = storage.getItem(SAVED_LAYOUTS_STORAGE_KEY);
    expect(raw).toContain("token");
    expect(raw).not.toContain("/accounting/reconciliation"); // opaque token, not plain state

    expect(readUserLayouts(storage)).toEqual([userLayout()]);
  });

  it("overwrites a same-name layout instead of duplicating it", () => {
    const storage = memoryStorage();
    upsertUserLayout(userLayout({ id: "a" }), storage);
    const next = upsertUserLayout(
      userLayout({ id: "b", name: "month-end close", description: "updated" }),
      storage
    );
    expect(next).toHaveLength(1);
    expect(next[0].id).toBe("b");
    expect(next[0].description).toBe("updated");
  });

  it("removes a layout by id", () => {
    const storage = memoryStorage();
    upsertUserLayout(userLayout({ id: "a", name: "A" }), storage);
    upsertUserLayout(userLayout({ id: "b", name: "B" }), storage);
    const next = removeUserLayout("a", storage);
    expect(next.map((layout) => layout.id)).toEqual(["b"]);
  });

  it("caps the number of stored layouts", () => {
    const storage = memoryStorage();
    for (let index = 0; index < MAX_SAVED_LAYOUTS + 5; index += 1) {
      upsertUserLayout(userLayout({ id: `id-${index}`, name: `Layout ${index}` }), storage);
    }
    expect(readUserLayouts(storage)).toHaveLength(MAX_SAVED_LAYOUTS);
  });

  it("ignores corrupt persisted blobs", () => {
    expect(readUserLayouts(memoryStorage({ [SAVED_LAYOUTS_STORAGE_KEY]: "{" }))).toEqual([]);
  });
});

describe("createUserLayout", () => {
  it("builds a user layout and trims the name", () => {
    const layout = createUserLayout({ id: "x", name: "  My Layout  ", state: sampleState });
    expect(layout?.name).toBe("My Layout");
    expect(layout?.source).toBe("user");
  });

  it("rejects an empty name", () => {
    expect(createUserLayout({ id: "x", name: "   ", state: sampleState })).toBeNull();
  });
});

describe("team presets and restore", () => {
  it("exposes read-only team seeds as fresh copies", () => {
    const first = listTeamLayouts();
    first[0].state.panes.push("watchlist");
    expect(listTeamLayouts()[0].state.panes).not.toContain("watchlist");
    expect(first.every((layout) => layout.source === "team")).toBe(true);
  });

  it("builds a restore plan mirroring the layout state", () => {
    expect(buildLayoutRestorePlan(userLayout())).toEqual({
      route: sampleState.route,
      density: "compact",
      operatingScope: { fundAccountId: "fund-a", symbol: "AAPL" },
      panes: ["live-quotes", "watchlist"]
    });
  });
});
