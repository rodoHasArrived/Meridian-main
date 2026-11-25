import { afterEach, describe, expect, it, vi } from "vitest";
import { getPricingHierarchy, savePricingHierarchy } from "./pricing-hierarchy.api";

describe("Pricing hierarchy API", () => {
  afterEach(() => vi.unstubAllGlobals());
  it("encodes exact account/security scope, cancellation and server-derived actor", async () => {
    const fetch = vi.fn().mockResolvedValueOnce(new Response(JSON.stringify({ securityId: "sec /a", accountId: "account/a" }), { status: 200 }))
      .mockResolvedValueOnce(new Response(null, { status: 204 }));
    vi.stubGlobal("fetch", fetch);
    const controller = new AbortController();
    await getPricingHierarchy("sec /a", "account/a", { signal: controller.signal });
    expect(fetch.mock.calls[0][0]).toBe("/api/security-master/sec%20%2Fa/pricing-hierarchy?accountId=account%2Fa");
    expect(fetch.mock.calls[0][1].signal).toBe(controller.signal);
    const request = { securityId: "sec /a", accountId: "account/a", asOf: "2026-11-01T00:00:00Z", entries: [{ priority: 1, sourceId: "vendor", sourceDisplayName: "Vendor", maxDaysStale: 2 }] };
    await savePricingHierarchy(request);
    expect(fetch.mock.calls[1][1].method).toBe("PUT");
    expect(JSON.parse(fetch.mock.calls[1][1].body)).toEqual(request);
    expect(JSON.parse(fetch.mock.calls[1][1].body)).not.toHaveProperty("updatedBy");
  });
  it("rejects foreign returned scope and propagates service failures without fixture fallback", async () => {
    const fetch = vi.fn().mockResolvedValueOnce(new Response(JSON.stringify({ securityId: "sec-a", accountId: "other" }), { status: 200 }))
      .mockResolvedValueOnce(new Response('{"detail":"No pricing service"}', { status: 503 }));
    vi.stubGlobal("fetch", fetch);
    await expect(getPricingHierarchy("sec-a", "account-a")).rejects.toThrow("another security or account");
    await expect(getPricingHierarchy("sec-a", "account-a")).rejects.toMatchObject({ status: 503 });
  });
});
