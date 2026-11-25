import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { RECENT_PAGE_LIMIT, isPinnedPage, recordRecentPage, resetSavedPagesCache, togglePinnedPage } from "@/lib/recent-pages";
import { buildCommandPaletteViewModel } from "@/components/meridian/command-palette.view-model";

describe("recent and pinned pages", () => {
  beforeEach(() => { localStorage.clear(); resetSavedPagesCache(); });
  afterEach(() => { vi.restoreAllMocks(); localStorage.clear(); resetSavedPagesCache(); });

  it("keeps the newest visits first, one per route, up to the limit", () => {
    for (let index = 0; index < RECENT_PAGE_LIMIT + 3; index += 1) {
      recordRecentPage({ route: `/accounting/page-${index}`, label: `Page ${index}` });
    }
    recordRecentPage({ route: "/accounting/page-5", label: "Page 5" });
    resetSavedPagesCache();
    const stored = JSON.parse(localStorage.getItem("meridian.workstation.recent-pages.v1") ?? "[]") as { route: string }[];
    expect(stored).toHaveLength(RECENT_PAGE_LIMIT);
    expect(stored[0]?.route).toBe("/accounting/page-5");
    expect(new Set(stored.map((page) => page.route)).size).toBe(RECENT_PAGE_LIMIT);
  });

  it("pins and unpins a page", () => {
    expect(togglePinnedPage({ route: "/accounting/ledger", label: "Ledger Explorer" })).toBe(true);
    expect(isPinnedPage("/accounting/ledger")).toBe(true);
    expect(togglePinnedPage({ route: "/accounting/ledger", label: "Ledger Explorer" })).toBe(false);
    expect(isPinnedPage("/accounting/ledger")).toBe(false);
  });

  it("keeps working when storage throws", () => {
    vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => { throw new Error("quota"); });
    vi.spyOn(Storage.prototype, "getItem").mockImplementation(() => { throw new Error("blocked"); });
    resetSavedPagesCache();
    expect(() => recordRecentPage({ route: "/trading", label: "Trading" })).not.toThrow();
    expect(() => togglePinnedPage({ route: "/trading", label: "Trading" })).not.toThrow();
  });

  it("lists pinned pages first, then recent ones, without the current page or duplicates", () => {
    const viewModel = buildCommandPaletteViewModel("/trading", undefined, {
      pinnedPages: [{ route: "/accounting/ledger", label: "Ledger Explorer" }],
      recentPages: [
        { route: "/trading", label: "Trading" },
        { route: "/accounting/ledger", label: "Ledger Explorer" },
        { route: "/data/providers", label: "Providers" }
      ]
    });
    const groups = viewModel.commandGroups;
    expect(groups[0]?.label).toBe("Pinned pages");
    expect(groups[0]?.items.map((item) => item.route)).toEqual(["/accounting/ledger"]);
    expect(groups[1]?.label).toBe("Recent pages");
    expect(groups[1]?.items.map((item) => item.route)).toEqual(["/data/providers"]);
  });
});
