import { act, render, renderHook, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import {
  ACTIVITY_LOG_LIMIT,
  ACTIVITY_LOG_STORAGE_KEY,
  loadActivityLog,
  saveActivityLog,
  type StorageLike
} from "./storage";
import { ActivityLogProvider, useActivityLog, useActivityLogService } from "./store";
import type { ActivityEntry } from "./types";
import { ActivityCenter } from "@/components/meridian/activity-center";

function memoryStorage(seed: Record<string, string> = {}): StorageLike {
  const map = new Map<string, string>(Object.entries(seed));
  return {
    getItem: (key) => map.get(key) ?? null,
    setItem: (key, value) => {
      map.set(key, value);
    },
    removeItem: (key) => {
      map.delete(key);
    }
  };
}

function entry(overrides: Partial<ActivityEntry> = {}): ActivityEntry {
  return {
    id: "e1",
    kind: "symbol.add",
    title: "Added SPY",
    tone: "success",
    timestamp: "2026-07-06T12:00:00.000Z",
    undoStatus: "idle",
    ...overrides
  };
}

describe("activity-log storage", () => {
  it("uses session storage by default so history does not outlive the browser session", () => {
    window.localStorage.removeItem(ACTIVITY_LOG_STORAGE_KEY);
    window.sessionStorage.removeItem(ACTIVITY_LOG_STORAGE_KEY);

    window.localStorage.setItem(
      ACTIVITY_LOG_STORAGE_KEY,
      JSON.stringify({ version: 1, entries: [entry({ id: "legacy-local" })] })
    );
    expect(loadActivityLog()).toEqual([]);

    saveActivityLog([entry({ id: "session-only" })]);

    expect(loadActivityLog()).toMatchObject([{ id: "session-only", title: "Added SPY" }]);
    expect(window.localStorage.getItem(ACTIVITY_LOG_STORAGE_KEY)).toContain("legacy-local");

    window.localStorage.removeItem(ACTIVITY_LOG_STORAGE_KEY);
    window.sessionStorage.removeItem(ACTIVITY_LOG_STORAGE_KEY);
  });

  it("round-trips entries through save/load", () => {
    const storage = memoryStorage();
    saveActivityLog([entry({ id: "a", undoStatus: "undone" })], storage);
    const loaded = loadActivityLog(storage);
    expect(loaded).toHaveLength(1);
    expect(loaded[0]).toMatchObject({ id: "a", title: "Added SPY", undoStatus: "undone" });
  });

  it("downgrades reversible-but-idle entries to expired on reload (thunks are not persisted)", () => {
    const storage = memoryStorage();
    saveActivityLog([entry({ id: "idle", undoStatus: "idle" }), entry({ id: "run", undoStatus: "running" })], storage);
    const loaded = loadActivityLog(storage);
    expect(loaded.find((e) => e.id === "idle")?.undoStatus).toBe("expired");
    expect(loaded.find((e) => e.id === "run")?.undoStatus).toBe("expired");
  });

  it("preserves settled statuses across reload", () => {
    const storage = memoryStorage();
    saveActivityLog([entry({ id: "done", undoStatus: "undone" }), entry({ id: "n", undoStatus: "none" })], storage);
    const loaded = loadActivityLog(storage);
    expect(loaded.find((e) => e.id === "done")?.undoStatus).toBe("undone");
    expect(loaded.find((e) => e.id === "n")?.undoStatus).toBe("none");
  });

  it("caps persisted entries and tolerates corrupt payloads", () => {
    const storage = memoryStorage();
    const many = Array.from({ length: ACTIVITY_LOG_LIMIT + 25 }, (_, i) => entry({ id: `e${i}` }));
    saveActivityLog(many, storage);
    expect(loadActivityLog(storage)).toHaveLength(ACTIVITY_LOG_LIMIT);

    const corrupt = memoryStorage({ [ACTIVITY_LOG_STORAGE_KEY]: "{not json" });
    expect(loadActivityLog(corrupt)).toEqual([]);
  });
});

describe("useActivityLogService", () => {
  it("records entries newest-first and increments the unseen count", () => {
    const storage = memoryStorage();
    const { result } = renderHook(() => useActivityLogService({ storage, now: () => 1000 }));

    act(() => {
      result.current.record({ kind: "symbol.add", title: "Added SPY" });
      result.current.record({ kind: "symbol.remove", title: "Removed MSFT" });
    });

    expect(result.current.entries.map((e) => e.title)).toEqual(["Removed MSFT", "Added SPY"]);
    expect(result.current.unseenCount).toBe(2);
    act(() => result.current.markAllSeen());
    expect(result.current.unseenCount).toBe(0);
  });

  it("runs a real compensating action on undo and marks the entry undone", async () => {
    const storage = memoryStorage();
    const compensate = vi.fn().mockResolvedValue(undefined);
    const { result } = renderHook(() => useActivityLogService({ storage }));

    let id = "";
    act(() => {
      id = result.current.record({ kind: "symbol.remove", title: "Removed MSFT", undo: { run: compensate } });
    });
    expect(result.current.entries[0].undoStatus).toBe("idle");

    await act(async () => {
      await result.current.undo(id);
    });

    expect(compensate).toHaveBeenCalledTimes(1);
    expect(result.current.entries[0].undoStatus).toBe("undone");
  });

  it("marks undo failed and allows a retry", async () => {
    const compensate = vi
      .fn()
      .mockRejectedValueOnce(new Error("network down"))
      .mockResolvedValueOnce(undefined);
    const { result } = renderHook(() => useActivityLogService({ storage: memoryStorage() }));

    let id = "";
    act(() => {
      id = result.current.record({ kind: "symbol.remove", title: "Removed MSFT", undo: { run: compensate } });
    });

    await act(async () => {
      await result.current.undo(id);
    });
    expect(result.current.entries[0].undoStatus).toBe("failed");
    expect(result.current.entries[0].undoError).toBe("network down");

    await act(async () => {
      await result.current.undo(id);
    });
    expect(result.current.entries[0].undoStatus).toBe("undone");
    expect(compensate).toHaveBeenCalledTimes(2);
  });

  it("does nothing when undoing an entry that has no live handler (e.g. reloaded)", async () => {
    const storage = memoryStorage();
    saveActivityLog([entry({ id: "old", undoStatus: "idle" })], storage);
    const { result } = renderHook(() => useActivityLogService({ storage }));
    // Loaded entry is history-only; its status was downgraded to expired.
    expect(result.current.entries[0].undoStatus).toBe("expired");
    await act(async () => {
      await result.current.undo("old");
    });
    expect(result.current.entries[0].undoStatus).toBe("expired");
  });

  it("persists across a fresh service instance sharing storage", () => {
    const storage = memoryStorage();
    const first = renderHook(() => useActivityLogService({ storage }));
    act(() => {
      first.result.current.record({ kind: "symbol.add", title: "Added SPY", undo: { run: async () => undefined } });
    });
    const second = renderHook(() => useActivityLogService({ storage }));
    expect(second.result.current.entries[0].title).toBe("Added SPY");
    // The undo thunk did not survive; the reloaded entry is expired.
    expect(second.result.current.entries[0].undoStatus).toBe("expired");
  });
});

describe("ActivityCenter drawer", () => {
  function Harness({ storage }: { storage: StorageLike }) {
    return (
      <ActivityLogProvider storage={storage}>
        <ActivityCenter />
        <Recorder />
      </ActivityLogProvider>
    );
  }

  function Recorder() {
    const log = useActivityLog();
    return (
      <button
        type="button"
        onClick={() =>
          log.record({
            kind: "symbol.remove",
            title: "Removed MSFT from the watchlist",
            undo: { run: async () => undefined }
          })
        }
      >
        record-remove
      </button>
    );
  }

  it("surfaces a recorded action and undoes it from the drawer", async () => {
    const user = userEvent.setup();
    render(<Harness storage={memoryStorage()} />);

    await user.click(screen.getByRole("button", { name: "record-remove" }));
    // Badge reflects the unseen action.
    expect(screen.getByRole("button", { name: /Activity — 1 new/ })).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: /Activity/ }));
    expect(await screen.findByText("Removed MSFT from the watchlist")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Undo" }));
    await waitFor(() => expect(screen.getByText("Undone")).toBeInTheDocument());
  });

  it("shows the empty state with no activity", async () => {
    const user = userEvent.setup();
    render(<Harness storage={memoryStorage()} />);
    await user.click(screen.getByRole("button", { name: /Activity/ }));
    expect(await screen.findByText(/No activity yet/)).toBeInTheDocument();
  });
});
