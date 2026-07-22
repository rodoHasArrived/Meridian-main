import { describe, expect, it } from "vitest";
import {
  buildCanonicalSymbolRegistryPanelModel,
  filterCanonicalSymbols
} from "@/screens/data-screen.canonical-symbols.view-model";
import type { CanonicalSymbolRegistryResponse } from "@/types";

function response(): CanonicalSymbolRegistryResponse {
  return {
    registryVersion: "1.0.0",
    resolutionMode: "Compare",
    compareModeReturnsLegacy: true,
    totalMismatchCount: 3,
    lastMismatchAt: "2026-07-13T17:00:00Z",
    recentMismatches: [{
      input: "BRK-B",
      fromProvider: "yahoo",
      toProvider: "ib",
      legacyResult: "BRK B",
      canonicalResult: "BRK.B",
      securityId: "11111111-1111-1111-1111-111111111111",
      observedAt: "2026-07-13T17:00:00Z"
    }],
    migrations: [{
      migrationId: "legacy-symbol-mappings-v1",
      sourceFingerprint: "sha256:abc123"
    }],
    symbols: [{
      securityId: "11111111-1111-1111-1111-111111111111",
      canonicalTicker: "BRK.B",
      displayName: "Berkshire Hathaway Class B",
      assetClass: "equity",
      exchange: "NYSE",
      currency: "USD",
      identifiers: {
        isin: "US0846707026",
        figi: "BBG000BLNNH6",
        compositeFigi: null,
        cusip: null,
        sedol: null
      },
      aliases: [{
        alias: "BRK-B",
        source: "security-master",
        provider: "yahoo",
        validFrom: null,
        validTo: null,
        isActive: true
      }],
      providerAliases: [{
        provider: "interactive-brokers",
        symbol: "BRK B",
        source: "operator",
        isOverride: true,
        updatedAt: "2026-07-13T16:00:00Z"
      }],
      provenanceSources: ["operator", "security-master"],
      hasRecentMismatch: true
    }, {
      securityId: "22222222-2222-2222-2222-222222222222",
      canonicalTicker: "AAPL",
      displayName: "Apple Inc.",
      assetClass: "equity",
      exchange: "NASDAQ",
      currency: "USD",
      identifiers: {
        isin: "US0378331005",
        figi: null,
        compositeFigi: null,
        cusip: null,
        sedol: null
      },
      aliases: [],
      providerAliases: [{
        provider: "polygon",
        symbol: "AAPL",
        source: "openfigi",
        isOverride: false,
        updatedAt: null
      }],
      provenanceSources: ["openfigi"],
      hasRecentMismatch: false
    }]
  };
}

describe("buildCanonicalSymbolRegistryPanelModel", () => {
  it("keeps Compare-mode behavior, mismatch evidence, migrations, and arbitrary provider aliases explicit", () => {
    const model = buildCanonicalSymbolRegistryPanelModel(response());

    expect(model.modeTitle).toBe("Compare mode preserves legacy output");
    expect(model.compareModeReturnsLegacy).toBe(true);
    expect(model.totalMismatchCount).toBe(3);
    expect(model.migrations[0]?.migrationId).toBe("legacy-symbol-mappings-v1");
    expect(model.providerAliasCount).toBe(2);
    expect(model.symbols[1]?.providerAliases[0]?.provider).toBe("interactive-brokers");
  });

  it("searches canonical ticker, SecurityId, identifiers, aliases, providers, and provenance", () => {
    const symbols = buildCanonicalSymbolRegistryPanelModel(response()).symbols;

    expect(filterCanonicalSymbols(symbols, "BRK-B").map((symbol) => symbol.canonicalTicker)).toEqual(["BRK.B"]);
    expect(filterCanonicalSymbols(symbols, "US0378331005").map((symbol) => symbol.canonicalTicker)).toEqual(["AAPL"]);
    expect(filterCanonicalSymbols(symbols, "11111111 operator").map((symbol) => symbol.canonicalTicker)).toEqual(["BRK.B"]);
    expect(filterCanonicalSymbols(symbols, "polygon").map((symbol) => symbol.canonicalTicker)).toEqual(["AAPL"]);
  });

  it("presents Canonical mode as active behavior instead of comparison-only evidence", () => {
    const model = buildCanonicalSymbolRegistryPanelModel({
      ...response(),
      resolutionMode: "Canonical",
      compareModeReturnsLegacy: false
    });

    expect(model.modeTone).toBe("success");
    expect(model.modeTitle).toBe("Canonical resolution active");
    expect(model.compareModeReturnsLegacy).toBe(false);
  });
});
