import { describe, expect, it } from "vitest";
import {
  OPEN_PANES_STORAGE_KEY,
  clearOpenCompanionPaneIds,
  readOpenCompanionPaneIds,
  recordCompanionPaneOpened,
  setOpenCompanionPaneIds
} from "@/lib/companion-pane/open-registry";

function memoryStorage(seed: Record<string, string> = {}): Storage {
  const map = new Map(Object.entries(seed));
  return {
    get length() {
      return map.size;
    },
    clear: () => map.clear(),
    getItem: (key: string) => map.get(key) ?? null,
    key: (index: number) => [...map.keys()][index] ?? null,
    removeItem: (key: string) => void map.delete(key),
    setItem: (key: string, value: string) => void map.set(key, value)
  } satisfies Storage;
}

describe("companion-pane open registry", () => {
  it("records known panes without duplicates, preserving order", () => {
    const storage = memoryStorage();
    recordCompanionPaneOpened("watchlist", storage);
    recordCompanionPaneOpened("live-quotes", storage);
    recordCompanionPaneOpened("watchlist", storage);
    expect(readOpenCompanionPaneIds(storage)).toEqual(["watchlist", "live-quotes"]);
  });

  it("ignores unknown pane ids", () => {
    const storage = memoryStorage();
    recordCompanionPaneOpened("bogus", storage);
    expect(readOpenCompanionPaneIds(storage)).toEqual([]);
  });

  it("filters unknown ids and dedupes on read and set", () => {
    const storage = memoryStorage({
      [OPEN_PANES_STORAGE_KEY]: JSON.stringify(["live-quotes", "live-quotes", "nope"])
    });
    expect(readOpenCompanionPaneIds(storage)).toEqual(["live-quotes"]);

    setOpenCompanionPaneIds(["watchlist", "nope", "watchlist"], storage);
    expect(readOpenCompanionPaneIds(storage)).toEqual(["watchlist"]);
  });

  it("clears the registry and tolerates corrupt blobs", () => {
    const storage = memoryStorage({ [OPEN_PANES_STORAGE_KEY]: "{" });
    expect(readOpenCompanionPaneIds(storage)).toEqual([]);

    setOpenCompanionPaneIds(["watchlist"], storage);
    clearOpenCompanionPaneIds(storage);
    expect(readOpenCompanionPaneIds(storage)).toEqual([]);
  });

  it("removes the key when set to an empty list", () => {
    const storage = memoryStorage();
    setOpenCompanionPaneIds(["watchlist"], storage);
    setOpenCompanionPaneIds([], storage);
    expect(storage.getItem(OPEN_PANES_STORAGE_KEY)).toBeNull();
  });
});
