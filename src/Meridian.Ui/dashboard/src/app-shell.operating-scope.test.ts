import { describe, expect, it } from "vitest";
import { operatingScopeDimensionsForRoute } from "@/app-shell.operating-scope";

describe("operatingScopeDimensionsForRoute", () => {
  it("returns the dimensions each workspace actually filters on", () => {
    expect(operatingScopeDimensionsForRoute("/data/quotes")).toEqual(["symbol", "provider", "window"]);
    expect(operatingScopeDimensionsForRoute("/accounting/ledger")).toEqual([
      "symbol",
      "fundAccountId",
      "runId",
      "provider",
      "window"
    ]);
    expect(operatingScopeDimensionsForRoute("/settings")).toEqual(["fundAccountId", "provider"]);
  });
});
