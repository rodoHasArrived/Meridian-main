import { describe, expect, it } from "vitest";
import {
  compactOperatingScope,
  hasOperatingScopeValues,
  mergeOperatingScopes,
  parseStoredOperatingScope
} from "@/lib/operating-scope/persistence";

describe("operating-scope persistence", () => {
  it("accepts the legacy stored symbol string", () => {
    expect(parseStoredOperatingScope("MSFT")).toEqual({ symbol: "MSFT" });
  });

  it("validates object shape and strips unknown or non-string fields", () => {
    expect(parseStoredOperatingScope(JSON.stringify({
      symbol: "AAPL",
      fundAccountId: 42,
      provider: "Alpaca",
      unsafe: "ignored"
    }))).toEqual({ symbol: "AAPL", provider: "Alpaca" });
  });

  it("returns an empty scope for malformed persisted JSON", () => {
    expect(parseStoredOperatingScope("{not json")).toEqual({});
  });

  it("merges route scope over stored scope and compacts empty values", () => {
    expect(mergeOperatingScopes(
      { symbol: "MSFT", provider: "Alpaca" },
      { symbol: "NVDA", fundAccountId: "fund-1" }
    )).toEqual({ symbol: "NVDA", fundAccountId: "fund-1", provider: "Alpaca" });

    expect(compactOperatingScope({ symbol: "", provider: "Alpaca", date: null })).toEqual({ provider: "Alpaca" });
  });

  it("stores, merges and compacts the entity and fund dimensions", () => {
    expect(parseStoredOperatingScope(JSON.stringify({ entityId: "entity-1", fundId: "fund-1", fundAccountId: "acct-1" })))
      .toEqual({ entityId: "entity-1", fundId: "fund-1", fundAccountId: "acct-1" });
    expect(mergeOperatingScopes({ entityId: "entity-1", fundId: "fund-1" }, { fundId: "fund-2" }))
      .toEqual({ entityId: "entity-1", fundId: "fund-2" });
    expect(compactOperatingScope({ entityId: "", fundId: null })).toEqual({});
    expect(hasOperatingScopeValues({ fundId: "fund-1" })).toBe(true);
  });
});

describe("operating-scope persistence per tab (Phase 1)", () => {
  const key = "meridian.workstation.operatingContext.v1";

  it("reads this tab's scope before the last scope used in any tab", async () => {
    const { readStoredOperatingScope } = await import("@/lib/operating-scope/persistence");
    window.localStorage.clear(); window.sessionStorage.clear();
    window.localStorage.setItem(key, JSON.stringify({ fundId: "fund-last-used" }));
    expect(readStoredOperatingScope()).toEqual({ fundId: "fund-last-used" });
    window.sessionStorage.setItem(key, JSON.stringify({ fundId: "fund-this-tab" }));
    expect(readStoredOperatingScope()).toEqual({ fundId: "fund-this-tab" });
  });

  it("writes both stores, and a cleared tab stays cleared after another tab writes", async () => {
    const { readStoredOperatingScope, writeStoredOperatingScope } = await import("@/lib/operating-scope/persistence");
    window.localStorage.clear(); window.sessionStorage.clear();
    writeStoredOperatingScope({ fundId: "fund-a" });
    expect(JSON.parse(window.sessionStorage.getItem(key)!)).toEqual({ fundId: "fund-a" });
    expect(JSON.parse(window.localStorage.getItem(key)!)).toEqual({ fundId: "fund-a" });

    writeStoredOperatingScope({});
    expect(window.localStorage.getItem(key)).toBeNull();
    // Another tab writes its own scope as the last used.
    window.localStorage.setItem(key, JSON.stringify({ fundId: "fund-b" }));
    expect(readStoredOperatingScope()).toEqual({});
  });
});
