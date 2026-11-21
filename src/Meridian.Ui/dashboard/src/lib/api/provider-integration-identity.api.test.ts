import { afterEach, describe, expect, it, vi } from "vitest";
import { resolveProviderIntegrationIdentity as resolveFromBarrel } from "@/lib/api";
import { resolveProviderIntegrationIdentity } from "./provider-integration-identity.api";
import type { ProviderIntegrationIdentityResolutionRequest } from "@/types";

const request: ProviderIntegrationIdentityResolutionRequest = {
  syncRunId: "retained-run",
  stagingRecordId: "retained-row",
  selectedSecurityId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
  evidenceHash: "retained-matching-evidence-hash",
  reason: "Reviewed the source identifiers."
};

afterEach(() => vi.unstubAllGlobals());

describe("provider identity decision API", () => {
  it("preserves the existing barrel export and sends the exact reviewed evidence", async () => {
    expect(resolveFromBarrel).toBe(resolveProviderIntegrationIdentity);
    const decision = { ...request, decisionId: "decision", reviewedBy: "server-actor" };
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify(decision), { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);
    const controller = new AbortController();

    await expect(resolveFromBarrel("connection / 1", request, { signal: controller.signal })).resolves.toEqual(decision);

    expect(fetchMock).toHaveBeenCalledWith(
      "/api/workstation/provider-integrations/connections/connection%20%2F%201/identity-resolution",
      expect.objectContaining({ method: "POST", body: JSON.stringify(request), signal: controller.signal })
    );
  });

  it("propagates a changed-evidence conflict without producing a synthetic decision", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(
      new Response('{"error":"Identifier matching evidence changed."}', { status: 409 })
    ));
    await expect(resolveProviderIntegrationIdentity("connection", request)).rejects.toMatchObject({ status: 409 });
  });
});
