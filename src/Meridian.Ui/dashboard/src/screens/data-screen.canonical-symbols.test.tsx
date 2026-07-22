import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { CanonicalSymbolRegistryRegion } from "@/screens/data-screen.canonical-symbols";
import { renderWithRouter } from "@/test/render";
import type { CanonicalSymbolRegistryResponse } from "@/types";

const snapshot: CanonicalSymbolRegistryResponse = {
  registryVersion: "1.0.0",
  resolutionMode: "Compare",
  compareModeReturnsLegacy: true,
  totalMismatchCount: 1,
  lastMismatchAt: "2026-07-13T17:00:00Z",
  recentMismatches: [{
    input: "BRK-B",
    fromProvider: "yahoo",
    toProvider: "interactive-brokers",
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
      updatedAt: null
    }],
    provenanceSources: ["operator", "security-master"],
    hasRecentMismatch: true
  }, {
    securityId: null,
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
    providerAliases: [],
    provenanceSources: ["registry"],
    hasRecentMismatch: false
  }]
};

describe("CanonicalSymbolRegistryRegion", () => {
  it("shows registry version, compare behavior, mismatch evidence, and migration receipts", async () => {
    const fetchRegistry = vi.fn().mockResolvedValue(snapshot);
    renderWithRouter(<CanonicalSymbolRegistryRegion fetchRegistry={fetchRegistry} />);

    expect(await screen.findByText("Compare mode preserves legacy output")).toBeInTheDocument();
    expect(screen.getByText("1.0.0")).toBeInTheDocument();
    expect(screen.getByText("legacy-symbol-mappings-v1")).toBeInTheDocument();
    expect(screen.getByText("legacy BRK B · canonical BRK.B")).toBeInTheDocument();
    expect(fetchRegistry).toHaveBeenCalledTimes(1);
  });

  it("filters rows by a non-canonical provider alias and reveals arbitrary provider provenance", async () => {
    const user = userEvent.setup();
    renderWithRouter(<CanonicalSymbolRegistryRegion fetchRegistry={vi.fn().mockResolvedValue(snapshot)} />);
    await screen.findByText("Compare mode preserves legacy output");

    await user.type(screen.getByRole("textbox", { name: "Search registry" }), "BRK-B");
    expect(screen.getByText("BRK.B")).toBeInTheDocument();
    expect(screen.queryByText("AAPL")).not.toBeInTheDocument();

    await user.click(screen.getByText("BRK.B"));
    expect(screen.getByText("interactive-brokers")).toBeInTheDocument();
    expect(screen.getByText("BRK B")).toBeInTheDocument();
    expect(screen.getByText("Sources: operator, security-master")).toBeInTheDocument();
  });
});
