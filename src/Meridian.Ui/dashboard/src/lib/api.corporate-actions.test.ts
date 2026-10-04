import { beforeEach, describe, expect, it, vi } from "vitest";
import { acceptCorporateActionInboxProposal } from "@/lib/api";

describe("corporate-action API wiring", () => {
  const fetchMock = vi.fn();

  beforeEach(() => {
    fetchMock.mockReset();
    fetchMock.mockResolvedValue({
      ok: true,
      headers: { get: () => null },
      json: async () => ({}),
      text: async () => "{}"
    });
    vi.stubGlobal("fetch", fetchMock);
  });

  it("posts acceptance to the canonical proposal-identified route", async () => {
    const request = {
      proposalId: "proposal / 1",
      expectedVersion: 7,
      idempotencyKey: "test-canonical-route",
      scope: {
        tenantId: "tenant-meridian",
        companyId: "company-alpha"
      }
    };

    await acceptCorporateActionInboxProposal(request);

    expect(fetchMock).toHaveBeenCalledWith(
      "/api/security-master/corporate-actions/source-proposals/proposal%20%2F%201/accept",
      expect.objectContaining({
        method: "POST",
        body: JSON.stringify(request)
      })
    );
  });
});
