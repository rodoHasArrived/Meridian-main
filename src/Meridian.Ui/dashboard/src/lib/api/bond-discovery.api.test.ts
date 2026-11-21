import { afterEach, describe, expect, it, vi } from "vitest";
import { discoverProviderBonds, stageBondDiscoveryCandidate, type BondDiscoveryCandidate } from "./bond-discovery.api";

describe("bond discovery adapters", () => {
  afterEach(() => vi.unstubAllGlobals());
  it("encodes lookup input and preserves authoritative unavailable responses", async () => {
    const fetch = vi.fn().mockResolvedValue(new Response('{"error":"Provider unavailable"}', { status: 503 }));
    vi.stubGlobal("fetch", fetch);
    const controller = new AbortController();
    await expect(discoverProviderBonds("US / Treasury", { signal: controller.signal })).rejects.toMatchObject({ status: 503 });
    const [path, options] = fetch.mock.calls[0];
    expect(new URL(String(path), "https://meridian.invalid").searchParams.get("query")).toBe("US / Treasury");
    expect(options.signal).toBe(controller.signal);
  });
  it("stages only retained candidate identity and review evidence with no invented economics", async () => {
    const fetch = vi.fn().mockResolvedValue(new Response("{}", { status: 200 })); vi.stubGlobal("fetch", fetch);
    const candidate: BondDiscoveryCandidate = {
      symbol: "912TEST", name: '=HYPERLINK("unsafe")', source: "alpaca", assetType: "FixedIncome",
      country: "US", currency: "USD", exchange: "OTC", figi: "BBGTEST", compositeFigi: null
    };
    await stageBondDiscoveryCandidate(candidate, "2026-11-13T00:00:00Z", "  Reviewed source  ");
    const [path, options] = fetch.mock.calls[0];
    expect(path).toBe("/api/workstation/data/uploads/preview");
    expect(options.body.get("templateId")).toBe("asset-information");
    const file = options.body.get("file") as File;
    const content = await new Promise<string>((resolve, reject) => {
      const reader = new FileReader(); reader.onload = () => resolve(String(reader.result)); reader.onerror = reject; reader.readAsText(file);
    });
    expect(content).toContain("review_reason"); expect(content).toContain('"Reviewed source"');
    expect(content).toContain('"\'=HYPERLINK(""unsafe"")"');
    expect(content).toContain('"Bond","","USD","US","","","alpaca"');
  });
});
