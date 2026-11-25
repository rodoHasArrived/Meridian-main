import { afterEach, describe, expect, it, vi } from "vitest";
import { ApiError } from "@/lib/api-errors";
import {
  assignSecurityMasterExceptionCase,
  describeExceptionSla,
  describeReviewQueueFailure,
  discardSecurityMasterHeldAmendment,
  getSignedInUsername,
  listSecurityMasterExceptionCases,
  listSecurityMasterHeldAmendments,
  syncSecurityMasterExceptionCases,
  type SecurityMasterExceptionCaseQueueItem
} from "./security-master-review-queues.api";

const caseItem: SecurityMasterExceptionCaseQueueItem = {
  row: {
    caseId: "c1111111-1111-1111-1111-111111111111",
    caseKey: "k",
    category: "Conflict",
    ruleCode: "SM-CONFLICT",
    securityId: null,
    severity: "High",
    title: "Conflicting coupon",
    detail: "d",
    status: "Open",
    assignee: null,
    openedAt: "2026-11-23T06:00:00Z",
    lastSeenAt: "2026-11-24T06:00:00Z",
    slaDueAt: "2026-11-24T12:00:00-05:00",
    closedAt: null,
    closedReason: null
  },
  slaBreached: true,
  ageHours: 30
};

describe("Security Master review queues API", () => {
  afterEach(() => vi.unstubAllGlobals());

  it("uses the shared held-amendment and exception-case routes", async () => {
    const fetch = vi
      .fn()
      .mockResolvedValueOnce(new Response("[]", { status: 200 }))
      .mockResolvedValueOnce(new Response(null, { status: 204 }))
      .mockResolvedValueOnce(new Response(JSON.stringify([caseItem]), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ opened: 1, refreshed: 2, closed: 3 }), { status: 200 }))
      .mockResolvedValueOnce(new Response(null, { status: 204 }));
    vi.stubGlobal("fetch", fetch);

    await listSecurityMasterHeldAmendments({ securityId: "sec-a", status: "Held" });
    await discardSecurityMasterHeldAmendment("held-1");
    expect(await listSecurityMasterExceptionCases("Open")).toEqual([caseItem]);
    expect(await syncSecurityMasterExceptionCases()).toEqual({ opened: 1, refreshed: 2, closed: 3 });
    await assignSecurityMasterExceptionCase("case-1", " carol.ops ");

    expect(fetch.mock.calls.map((call) => [call[1]?.method ?? "GET", call[0]])).toEqual([
      ["GET", "/api/security-master/held-amendments?securityId=sec-a&status=Held"],
      ["POST", "/api/security-master/held-amendments/held-1/discard"],
      ["GET", "/api/security-master/exception-cases?status=Open"],
      ["POST", "/api/security-master/exception-cases/sync"],
      ["POST", "/api/security-master/exception-cases/case-1/assign"]
    ]);
    expect(JSON.parse(fetch.mock.calls[4][1].body)).toEqual({ assignee: "carol.ops" });
  });

  it("names the server's reason for a queue refusal", () => {
    const conflict = new ApiError({ path: "/x", status: 409, responseBody: '{"error":"Case \'c\' is not open."}' });
    expect(describeReviewQueueFailure(conflict, "assign cases")).toBe("Case 'c' is not open.");
    expect(describeReviewQueueFailure(new ApiError({ path: "/x", status: 403 }), "assign cases")).toBe(
      "You don't have permission to assign cases."
    );
    expect(describeReviewQueueFailure(new ApiError({ path: "/x", status: 409, responseBody: '{"error":"code-only"}' }), "x")).toBe(
      "The item changed since the queue was loaded. Refresh the queue."
    );
  });

  it("shows the server's SLA verdict with the due time in UTC", () => {
    expect(describeExceptionSla(caseItem)).toBe("Breached (due 2026-11-24 17:00 UTC)");
    expect(describeExceptionSla({ ...caseItem, slaBreached: false })).toBe("Due 2026-11-24 17:00 UTC");
  });

  it("reads the signed-in username from the auth profile and treats no session as no name", async () => {
    const fetch = vi
      .fn()
      .mockResolvedValueOnce(new Response(JSON.stringify({ username: " dana.ops ", role: "Operator" }), { status: 200 }))
      .mockResolvedValueOnce(new Response('{"error":"Not authenticated."}', { status: 401 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ role: "Operator" }), { status: 200 }))
      .mockResolvedValueOnce(new Response('{"detail":"down"}', { status: 500 }));
    vi.stubGlobal("fetch", fetch);

    expect(await getSignedInUsername()).toBe("dana.ops");
    expect(fetch.mock.calls[0][0]).toBe("/api/auth/me");
    expect(await getSignedInUsername()).toBeNull();
    expect(await getSignedInUsername()).toBeNull();
    await expect(getSignedInUsername()).rejects.toMatchObject({ status: 500 });
  });
});

