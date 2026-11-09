import { describe, expect, it } from "vitest";
import {
  describeOperatingScope,
  readRecentScopes,
  RECENT_SCOPES_LIMIT,
  RECENT_SCOPES_STORAGE_KEY,
  rememberRecentScope
} from "@/lib/operating-scope/recent-scopes";

function memoryStorage(): Storage {
  const values = new Map<string, string>();
  return {
    get length() { return values.size; },
    clear: () => values.clear(),
    getItem: (key) => values.get(key) ?? null,
    key: (index) => [...values.keys()][index] ?? null,
    removeItem: (key) => { values.delete(key); },
    setItem: (key, value) => { values.set(key, value); }
  };
}

describe("recent operating scopes (C1)", () => {
  it("keeps the newest first, remembers a scope once, and caps the list", () => {
    const storage = memoryStorage();
    let recents = rememberRecentScope({ symbol: "AAPL" }, [], storage);
    recents = rememberRecentScope({ symbol: "MSFT", fundAccountId: "fund-a" }, recents, storage);
    recents = rememberRecentScope({ symbol: "AAPL" }, recents, storage);
    expect(recents.map((scope) => scope.symbol)).toEqual(["AAPL", "MSFT"]);

    for (const symbol of ["A", "B", "C", "D", "E", "F"]) {
      recents = rememberRecentScope({ symbol }, recents, storage);
    }
    expect(recents).toHaveLength(RECENT_SCOPES_LIMIT);
    expect(recents[0]?.symbol).toBe("F");
    expect(readRecentScopes(storage)).toEqual(recents);
  });

  it("ignores an empty scope and unreadable storage", () => {
    const storage = memoryStorage();
    expect(rememberRecentScope({}, [], storage)).toEqual([]);
    storage.setItem(RECENT_SCOPES_STORAGE_KEY, "not json");
    expect(readRecentScopes(storage)).toEqual([]);
  });

  it("describes a scope for the picker in a few words", () => {
    expect(describeOperatingScope({
      symbol: "AAPL",
      fundAccountId: "11111111-1111-1111-1111-111111111111",
      provider: "alpaca",
      from: "2026-06-01",
      to: "2026-06-30"
    })).toBe("AAPL · fund 1111…1111 · alpaca · 2026-06-01 to 2026-06-30");
    expect(describeOperatingScope({})).toBe("No scope");
  });
});
