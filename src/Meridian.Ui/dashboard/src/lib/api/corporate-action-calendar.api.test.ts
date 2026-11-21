import { afterEach, describe, expect, it, vi } from "vitest";
import { apiGetJson, apiPostJson } from "@/lib/api";
import { getCorporateActionCalendar, recordCorporateActionDeadline } from "@/lib/api/corporate-action-calendar.api";
import type { CorporateActionCalendarPage, RecordCorporateActionDeadlineRequest } from "@/types/corporate-action-calendar.types";

vi.mock("@/lib/api", () => ({ apiGetJson: vi.fn(), apiPostJson: vi.fn() }));
const get = vi.mocked(apiGetJson);
const page = (items: CorporateActionCalendarPage["items"] = []): CorporateActionCalendarPage => ({ from: "2026-11-12", to: "2026-11-18", generatedAtUtc: "2026-11-12T12:00:00Z", revision: "v1", items, totalCount: items.length, coverage: "Retained source events only." });
afterEach(() => vi.resetAllMocks());
describe("corporate action calendar API", () => {
  it("loads every page and URL-encodes opaque cursors without development fallback", async () => {
    const first = { eventId: "event-1" } as CorporateActionCalendarPage["items"][number];
    const second = { eventId: "event-2" } as CorporateActionCalendarPage["items"][number];
    get.mockResolvedValueOnce({ ...page([first]), totalCount: 2, nextCursor: "opaque+/=" }).mockResolvedValueOnce({ ...page([second]), totalCount: 2 });
    const result = await getCorporateActionCalendar({ from: "2026-11-12", to: "2026-11-18", financialAccountId: "account/one" });
    expect(result.items.map((event) => event.eventId)).toEqual(["event-1", "event-2"]);
    const url = new URL(get.mock.calls[1][0], "http://localhost");
    expect(url.searchParams.get("cursor")).toBe("opaque+/=");
    expect(url.searchParams.get("financialAccountId")).toBe("account/one");
    expect(get.mock.calls.every((call) => call[1]?.allowDevelopmentFallback === false)).toBe(true);
  });
  it("rejects changed revisions and repeated cursors instead of returning partial coverage", async () => {
    get.mockResolvedValueOnce({ ...page(), totalCount: 2, nextCursor: "cursor" }).mockResolvedValueOnce({ ...page(), totalCount: 2, revision: "v2" });
    await expect(getCorporateActionCalendar({ from: "2026-11-12", to: "2026-11-18" })).rejects.toThrow(/revision changed/);
    get.mockReset();
    get.mockResolvedValue({ ...page(), totalCount: 2, nextCursor: "cursor" });
    await expect(getCorporateActionCalendar({ from: "2026-11-12", to: "2026-11-18" })).rejects.toThrow(/repeated a cursor/);
  });
  it("refuses a missing final page population", async () => {
    get.mockResolvedValue({ ...page(), totalCount: 2 });
    await expect(getCorporateActionCalendar({ from: "2026-11-12", to: "2026-11-18" })).rejects.toThrow(/coverage is incomplete/);
  });
  it("sends the original manual command unchanged to its exact case", async () => {
    const request = { caseId: "case-1", idempotencyKey: "same-key", deadline: { sourceText: "Original notice text", at: "2026-11-12T17:00:00-07:00" } } as RecordCorporateActionDeadlineRequest;
    await recordCorporateActionDeadline(request);
    expect(apiPostJson).toHaveBeenCalledWith("/api/security-master/corporate-actions/cases/case-1/deadlines", request, expect.objectContaining({ allowDevelopmentFallback: false }));
  });
});
