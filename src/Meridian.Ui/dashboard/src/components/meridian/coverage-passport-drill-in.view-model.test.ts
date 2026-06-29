import { describe, expect, it } from "vitest";
import { buildCoverageSecurityDrillIns } from "@/components/meridian/coverage-passport-drill-in.view-model";
import type { SecurityMasterEntry } from "@/types";

function entry(securityId: string, displayName: string, assetClass: string, symbol: string): SecurityMasterEntry {
  return {
    securityId,
    displayName,
    status: "Active",
    classification: {
      assetClass,
      subType: null,
      primaryIdentifierKind: "Ticker",
      primaryIdentifierValue: symbol
    },
    economicDefinition: { currency: "USD" }
  } as unknown as SecurityMasterEntry;
}

describe("buildCoverageSecurityDrillIns", () => {
  const securities = [
    entry("s-2", "Zeta Corp", "Equity", "ZETA"),
    entry("s-1", "Acme Corp", "Equity", "ACME"),
    entry("s-3", "Gov 2032", "Bond", "G2032")
  ];

  it("returns only the securities of the requested asset class, sorted by display name", () => {
    const rows = buildCoverageSecurityDrillIns(securities, "Equity");
    expect(rows.map((r) => r.securityId)).toEqual(["s-1", "s-2"]);
    expect(rows[0]).toMatchObject({ displayName: "Acme Corp", symbol: "ACME", assetClass: "Equity" });
  });

  it("matches asset class case-insensitively", () => {
    expect(buildCoverageSecurityDrillIns(securities, "equity")).toHaveLength(2);
  });

  it("returns empty for an unknown asset class or empty inputs", () => {
    expect(buildCoverageSecurityDrillIns(securities, "Crypto")).toEqual([]);
    expect(buildCoverageSecurityDrillIns([], "Equity")).toEqual([]);
    expect(buildCoverageSecurityDrillIns(securities, "")).toEqual([]);
  });
});
