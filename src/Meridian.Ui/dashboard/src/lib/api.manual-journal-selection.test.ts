import { afterEach, describe, expect, it, vi } from "vitest";
import { getManualJournalEntryWorkbench } from "./api";

afterEach(() => vi.unstubAllGlobals());
describe("Scoped manual journal selection API", () => {
  it("keeps draft and period selection local while fetching the authorized fund/book workbench", async () => {
    const fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify({ fundProfileId: "fund-alpha", ledgerBookId: "book-alpha", drafts: [] }), { status: 200 }));
    vi.stubGlobal("fetch", fetch);
    await getManualJournalEntryWorkbench({ fundProfileId: "fund-alpha", ledgerBookId: "book-alpha", journalEntryId: "draft-1", periodId: "period-1" });
    expect(fetch).toHaveBeenCalledWith(expect.stringMatching(/fundProfileId=fund-alpha&ledgerBookId=book-alpha$/), expect.any(Object));
    expect(fetch.mock.calls[0][0]).not.toContain("journalEntryId");
    expect(fetch.mock.calls[0][0]).not.toContain("periodId");
  });
  it("refuses fallback data when an exact linked draft cannot be read", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response('{"error":"workbench unavailable"}', { status: 503 })));
    await expect(getManualJournalEntryWorkbench({ fundProfileId: "fund-alpha", ledgerBookId: "book-alpha", journalEntryId: "draft-1", periodId: "period-1" }, { allowDevelopmentFallback: true }))
      .rejects.toThrow("workbench unavailable");
  });
});
