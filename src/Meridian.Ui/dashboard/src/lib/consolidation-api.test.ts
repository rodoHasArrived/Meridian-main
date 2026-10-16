import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { createConsolidationDrafts, previewConsolidation } from "@/lib/consolidation-api";
import type { ConsolidationRequest } from "@/types/consolidation";

const request: ConsolidationRequest = {
  organizationId: "00000000-0000-0000-0000-000000000001",
  ownershipRootId: "00000000-0000-0000-0000-000000000002",
  eliminationBookId: "00000000-0000-0000-0000-000000000003",
  periodId: "00000000-0000-0000-0000-000000000004",
  asOf: "2026-09-30"
};
const fetchMock = vi.fn();

beforeEach(() => {
  fetchMock.mockReset();
  fetchMock.mockResolvedValue({ ok: true, json: async () => ({}), text: async () => "{}" });
  vi.stubGlobal("fetch", fetchMock);
  document.cookie = "mdc-csrf=consolidation-test-token; path=/";
});

afterEach(() => {
  document.cookie = "mdc-csrf=; Max-Age=0; path=/";
  vi.unstubAllGlobals();
});

describe("consolidation transport and authorization boundaries", () => {
  it("previews with a cancellable GET carrying all five scope fields in the query and no mutation body", async () => {
    const signal = new AbortController().signal;
    await previewConsolidation(request, { signal, allowDevelopmentFallback: false });
    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [path, options] = fetchMock.mock.calls[0];
    const url = new URL(path, "https://meridian.test");
    expect(url.pathname).toBe("/api/ledger/consolidation/preview");
    expect(Object.fromEntries(url.searchParams)).toEqual(request);
    expect(options.method ?? "GET").toBe("GET");
    expect(options.body).toBeUndefined();
    expect(options.signal).toBe(signal);
    expect(options.headers).toEqual({ Accept: "application/json" });
  });

  it("creates review drafts with POST, the complete request body, cancellation, and mutation CSRF protection", async () => {
    const signal = new AbortController().signal;
    await createConsolidationDrafts(request, { signal });
    expect(fetchMock).toHaveBeenCalledExactlyOnceWith("/api/ledger/consolidation/drafts", {
      method: "POST", body: JSON.stringify(request), signal,
      headers: { Accept: "application/json", "Content-Type": "application/json", "X-CSRF-Token": "consolidation-test-token" }
    });
  });

  it("allows a read preview while retaining a separate server permission denial for draft creation", async () => {
    await expect(previewConsolidation(request)).resolves.toEqual({});
    fetchMock.mockResolvedValueOnce({
      ok: false, status: 403, text: async () => JSON.stringify({ detail: "Ledger write permission is required." })
    });
    await expect(createConsolidationDrafts(request)).rejects.toMatchObject({ status: 403 });
    expect(fetchMock).toHaveBeenCalledTimes(2);
    expect(fetchMock.mock.calls[1][0]).toBe("/api/ledger/consolidation/drafts");
    expect(fetchMock.mock.calls[1][1].method).toBe("POST");
  });
});
