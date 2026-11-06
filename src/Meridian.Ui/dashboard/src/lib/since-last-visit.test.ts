import { beforeEach, describe, expect, it, vi } from "vitest";
import { applyCrossScreenCounts, applyOpenBreakDelta, selectCrossScreenCounts } from "@/lib/cross-screen-counts";
import { resolveDevFixture } from "@/lib/dev-fixtures";
import {
  BREAK_QUEUE_VISIT_SURFACE,
  classifySinceLastVisit,
  describeSinceLastVisit,
  loadSinceLastVisitBaseline,
  resetSinceLastVisitCache,
  sinceLastVisitDelta,
  summarizeSinceLastVisit
} from "@/lib/since-last-visit";
import { WORKSTATION_API_ENDPOINTS } from "@/lib/workstation-endpoints";
import type { AccountingWorkspaceResponse } from "@/types";

beforeEach(() => {
  resetSinceLastVisitCache();
  window.sessionStorage.clear();
});

const baseline = "2026-11-05T14:00:00Z";
const rows = [
  { detectedAt: "2026-11-04T09:00:00Z", lastUpdatedAt: "2026-11-04T09:00:00Z" },
  { detectedAt: "2026-11-05T15:30:00Z", lastUpdatedAt: "2026-11-05T15:30:00Z" },
  { detectedAt: "2026-11-01T09:00:00Z", lastUpdatedAt: "2026-11-05T16:00:00Z" },
  { detectedAt: "2026-11-06T08:00:00Z", lastUpdatedAt: null }
];

describe("since your last visit (C3)", () => {
  it("marks rows detected after the baseline as new and rows with later activity as updated", () => {
    expect(rows.map((row) => classifySinceLastVisit(row, baseline))).toEqual([null, "new", "updated", "new"]);
    expect(classifySinceLastVisit(rows[1]!, null)).toBeNull();
    expect(classifySinceLastVisit(rows[1]!, "not a time")).toBeNull();
    expect(summarizeSinceLastVisit(rows, baseline)).toEqual({ newCount: 2, updatedCount: 1 });
  });

  it("says what changed and the visit it is measured from", () => {
    const ready = { status: "ready" as const, baseline };
    expect(describeSinceLastVisit(ready, { newCount: 2, updatedCount: 1 })).toBe("2 new and 1 updated since your last visit (Nov 5, 2026, 14:00 UTC).");
    expect(describeSinceLastVisit(ready, { newCount: 0, updatedCount: 0 })).toBe("Nothing changed since your last visit (Nov 5, 2026, 14:00 UTC).");
    expect(describeSinceLastVisit({ status: "ready", baseline: null }, { newCount: 0, updatedCount: 0 })).toMatch(/first recorded visit/);
    expect(describeSinceLastVisit({ status: "unavailable", baseline: null }, { newCount: 3, updatedCount: 0 })).toBeNull();
    expect(sinceLastVisitDelta({ newCount: 2, updatedCount: 1 })).toBe("2 new since your last visit");
    expect(sinceLastVisitDelta({ newCount: 0, updatedCount: 4 })).toBe("");
  });

  it("records one visit per surface per tab and reuses its baseline", async () => {
    const visitedAt = "2026-11-06T09:00:00Z";
    const record = vi.fn().mockResolvedValue({ surface: BREAK_QUEUE_VISIT_SURFACE, previousVisitAt: baseline, visitedAt });
    const read = vi.fn().mockResolvedValue({ surface: BREAK_QUEUE_VISIT_SURFACE, previousVisitAt: visitedAt, visitedAt: null });
    const [first, second] = await Promise.all([
      loadSinceLastVisitBaseline(BREAK_QUEUE_VISIT_SURFACE, { record, read }),
      loadSinceLastVisitBaseline(BREAK_QUEUE_VISIT_SURFACE, { record, read })
    ]);
    expect(first).toBe(baseline);
    expect(second).toBe(baseline);
    // A reload of the tab keeps the baseline: the server's last visit is still the one this tab made.
    resetSinceLastVisitCache();
    expect(await loadSinceLastVisitBaseline(BREAK_QUEUE_VISIT_SURFACE, { record, read })).toBe(baseline);
    expect(record).toHaveBeenCalledOnce();
    expect(read).toHaveBeenCalledOnce();
  });

  it("gives another operator in the same tab their own baseline", async () => {
    const record = vi.fn()
      .mockResolvedValueOnce({ surface: BREAK_QUEUE_VISIT_SURFACE, previousVisitAt: baseline, visitedAt: "2026-11-06T09:00:00Z" })
      .mockResolvedValueOnce({ surface: BREAK_QUEUE_VISIT_SURFACE, previousVisitAt: "2026-11-02T08:00:00Z", visitedAt: "2026-11-06T09:30:00Z" });
    expect(await loadSinceLastVisitBaseline(BREAK_QUEUE_VISIT_SURFACE, { record })).toBe(baseline);

    // Sign-out and sign-in keep session storage; the next operator's last visit is their own.
    resetSinceLastVisitCache();
    const read = vi.fn().mockResolvedValue({ surface: BREAK_QUEUE_VISIT_SURFACE, previousVisitAt: "2026-11-02T08:00:00Z", visitedAt: null });
    expect(await loadSinceLastVisitBaseline(BREAK_QUEUE_VISIT_SURFACE, { record, read })).toBe("2026-11-02T08:00:00Z");
    expect(record).toHaveBeenCalledTimes(2);
  });

  it("tries again after a failed visit instead of caching the failure", async () => {
    const record = vi.fn()
      .mockRejectedValueOnce(new Error("offline"))
      .mockResolvedValueOnce({ surface: BREAK_QUEUE_VISIT_SURFACE, previousVisitAt: null, visitedAt: "2026-11-06T09:00:00Z" });
    await expect(loadSinceLastVisitBaseline(BREAK_QUEUE_VISIT_SURFACE, { record })).rejects.toThrow("offline");
    expect(await loadSinceLastVisitBaseline(BREAK_QUEUE_VISIT_SURFACE, { record })).toBeNull();
    expect(record).toHaveBeenCalledTimes(2);
  });

  it("gives the Accounting open-breaks card a delta with its basis", () => {
    const accounting = resolveDevFixture<AccountingWorkspaceResponse>(WORKSTATION_API_ENDPOINTS.accounting)!;
    const earliest = accounting.breakQueue
      .map((item) => Date.parse(item.detectedAt))
      .filter((time) => !Number.isNaN(time))
      .sort((a, b) => a - b)[0]!;
    const before = new Date(earliest - 1000).toISOString();
    const metrics = applyOpenBreakDelta(
      applyCrossScreenCounts(accounting.metrics, selectCrossScreenCounts({ breakQueue: accounting.breakQueue })),
      sinceLastVisitDelta(summarizeSinceLastVisit(accounting.breakQueue, before))
    );
    const breaks = metrics.find((metric) => metric.id === "breaks");
    expect(breaks?.delta).toBe(`${accounting.breakQueue.length} new since your last visit`);
  });
});
