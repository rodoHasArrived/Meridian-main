import { beforeEach, describe, expect, it, vi } from "vitest";
import { Blob as NodeBlob } from "node:buffer";
import * as api from "@/lib/api/accounting-onboarding.api";

describe("accounting onboarding API boundary", () => {
  const fetchMock = vi.fn();
  beforeEach(() => {
    fetchMock.mockReset();
    fetchMock.mockResolvedValue({ ok: true, json: async () => ({}), text: async () => "{}" });
    vi.stubGlobal("fetch", fetchMock);
  });
  it("encodes scoped identities, propagates abort, and never opts into development fixtures", async () => {
    const controller = new AbortController();
    await api.listOnboardingWorkspaces({ signal: controller.signal, allowDevelopmentFallback: true });
    await api.getOnboardingWorkspace("workspace / 1");
    await api.getOnboardingSources("workspace / 1");
    await api.assignOnboardingDifference("workspace / 1", "Balance/account:USD", { expectedVersion: 7, ownerId: "owner", notes: "Retained proof", evidenceIds: ["evidence-1"] });
    await api.reviewOnboardingWorkspace("workspace / 1", { expectedVersion: 8, dataRevision: 3, decision: "Approved", notes: "Reviewed", evidenceIds: ["evidence-1"] });
    await api.freezeOnboardingPacket("workspace / 1", 9);
    await api.getOnboardingPacket("workspace / 1", "packet / 2");
    await api.replayOnboardingComparison("workspace / 1", "comparison / 3");
    const base = "/api/accounting/onboarding/workspaces/workspace%20%2F%201";
    expect(fetchMock.mock.calls.map(call => call[0])).toEqual([
      "/api/accounting/onboarding/workspaces", base, `${base}/sources`, `${base}/differences/Balance%2Faccount%3AUSD/assignment`,
      `${base}/reviews`, `${base}/packets`, `${base}/packets/packet%20%2F%202`, `${base}/comparisons/comparison%20%2F%203/replay`
    ]);
    expect(fetchMock.mock.calls[0][1].signal).toBe(controller.signal);
    for (const [, options] of fetchMock.mock.calls) expect(options.headers).not.toHaveProperty("x-meridian-dev-fixture");
    expect(JSON.parse(fetchMock.mock.calls[4][1].body)).not.toHaveProperty("reviewerId");
  });
  it("keeps stale-version and unavailable-source failures visible", async () => {
    fetchMock.mockResolvedValue({ ok: false, status: 409, text: async () => JSON.stringify({ error: "Workspace version has changed." }) });
    await expect(api.freezeOnboardingPacket("workspace-1", 1)).rejects.toThrow("409");
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });
  it("downloads exact artifact bytes without parsing or rounding decimal source values", async () => {
    const canonical = '{"amount":123456789012345.1234567890123,"contentHash":"sha256-original"}';
    const blob = new NodeBlob([canonical], { type: "application/json" });
    const json = vi.fn();
    fetchMock.mockResolvedValue({ ok: true, blob: async () => blob, json });
    const result = await api.downloadOnboardingPacket("workspace-1", "packet / 2");
    expect(await result.text()).toBe(canonical);
    expect(json).not.toHaveBeenCalled();
    expect(fetchMock).toHaveBeenCalledWith("/api/accounting/onboarding/workspaces/workspace-1/packets/packet%20%2F%202/download", expect.anything());
  });
});
