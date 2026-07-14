import { describe, expect, it } from "vitest";
import {
  compactOperatingScope,
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
});
