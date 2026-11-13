import { afterEach, describe, expect, it, vi } from "vitest";
import { listIntercompanyAgreements, mutateIntercompanyAgreement } from "./intercompany-agreements.api";
import type { AgreementCommand } from "@/types/intercompany-agreements";

describe("agreement API authority", () => {
  afterEach(() => vi.unstubAllGlobals());
  it("retains dated entity filters and cancellation", async () => {
    const fetch = vi.fn().mockResolvedValue(new Response('{"items":[],"canCreate":false}'));
    vi.stubGlobal("fetch", fetch); const controller = new AbortController();
    await listIntercompanyAgreements({ entityId: "entity-a", asOf: "2026-11-12", agreementType: "CostShare" }, { signal: controller.signal });
    const [path, options] = fetch.mock.calls[0];
    expect(path).toContain("/api/fund-structure/intercompany-agreements?");
    expect(new URL(path, "https://meridian.invalid").searchParams.get("entityId")).toBe("entity-a");
    expect(options.signal).toBe(controller.signal);
  });
  it("uses the exact revision/proposal command for draft saving and surfaces authoritative failures", async () => {
    const fetch = vi.fn().mockResolvedValue(new Response('{"error":"Retained version changed"}', { status: 409 })); vi.stubGlobal("fetch", fetch);
    const command: AgreementCommand = { commandId: "command", agreementId: "agreement/a", proposalId: "proposal/a", expectedAggregateVersion: 5, submittedHash: "retained-hash" };
    await expect(mutateIntercompanyAgreement("save", command)).rejects.toThrow("Retained version changed");
    expect(fetch.mock.calls[0][0]).toBe("/api/fund-structure/intercompany-agreements/agreement%2Fa/proposals/proposal%2Fa");
    expect(fetch.mock.calls[0][1].method).toBe("PUT");
    expect(JSON.parse(fetch.mock.calls[0][1].body)).toEqual(command);
  });
});
