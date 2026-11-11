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
import { MemoryRouter } from "react-router-dom";

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

  it("isolates stored history by shell identity and treats reloaded pending commands as unconfirmed", () => {
    const storage = memoryStorage();
    saveActivityLog([entry({ undoStatus: "none", operation: { status: "pending", actor: "reviewer", scope: "Book A / May", reference: "command-42", updatedAt: "2026-07-06T12:00:00Z" } })], storage, "session-A");
    expect(loadActivityLog(storage, "session-B")).toEqual([]);
    expect(loadActivityLog(storage, null)).toEqual([]);
    expect(loadActivityLog(storage, "session-A")[0].operation).toMatchObject({ status: "uncertain", reference: "command-42", scope: "Book A / May" });
    saveActivityLog([], storage, null);
    expect(storage.getItem(ACTIVITY_LOG_STORAGE_KEY)).toBeNull();
  });

  it("expires failed undo history when its real compensating handler no longer exists", () => {
    const storage = memoryStorage();
    saveActivityLog([entry({ undoStatus: "failed" })], storage);
    expect(loadActivityLog(storage)[0].undoStatus).toBe("expired");
  });
});

describe("useActivityLogService", () => {
  it("tracks one operation across pending, failed, retry-in-workflow, completed and uncertain outcomes", () => {
    const storage = memoryStorage();
    const { result } = renderHook(() => useActivityLogService({ storage, sessionIdentity: "session-A", now: () => 1000 }));
    let id = "";
    act(() => { id = result.current.beginOperation({ kind: "report.generate", title: "Generate Board Pack", actor: "reporter", scope: "Fund A / May", reference: "request-1", route: "/reporting/report-packs" }); });
    expect(result.current.entries[0]).toMatchObject({ undoStatus: "none", operation: { status: "pending", actor: "reporter" } });
    act(() => result.current.updateOperation(id, { status: "failed", detail: "Request rejected." }));
    expect(result.current.entries[0].operation?.status).toBe("failed");
    act(() => result.current.updateOperation(id, { status: "pending" }));
    act(() => result.current.updateOperation(id, { status: "completed", reference: "retained-run-1" }));
    expect(result.current.entries).toHaveLength(1);
    expect(result.current.unseenCount).toBe(1);
    expect(result.current.entries[0]).toMatchObject({ tone: "success", operation: { status: "completed", scope: "Fund A / May", reference: "retained-run-1" } });
    act(() => result.current.updateOperation(id, { status: "uncertain", detail: "Inspect retained state." }));
    expect(result.current.entries[0]).toMatchObject({ tone: "warning", undoStatus: "none", operation: { status: "uncertain" } });
  });

  it("clears another identity's history and ignores delayed updates from its callbacks", () => {
    const compensate = vi.fn().mockResolvedValue(undefined);
    const { result, rerender } = renderHook(({ identity }) => useActivityLogService({ storage: memoryStorage(), sessionIdentity: identity }), { initialProps: { identity: "A" } });
    let operationId = "";
    act(() => {
      result.current.record({ id: "undo-A", kind: "symbol.remove", title: "Removed AAPL", undo: { run: compensate } });
      operationId = result.current.beginOperation({ kind: "journal.post", title: "Post journal", actor: "A" });
    });
    const oldUpdate = result.current.updateOperation;
    const oldRecord = result.current.record;
    rerender({ identity: "B" });
    expect(result.current.entries).toEqual([]);
    act(() => {
      oldUpdate(operationId, { status: "completed" });
      oldRecord({ kind: "late-A", title: "Old callback" });
    });
    expect(result.current.entries).toEqual([]);
    expect(compensate).not.toHaveBeenCalled();
  });

  it("does not reactivate captured callbacks or flash prior history after A-to-B-to-A identity changes", async () => {
    const storage = memoryStorage();
    const observations: Array<{ identity: string; count: number }> = [];
    const { result, rerender } = renderHook(({ identity }) => {
      const api = useActivityLogService({ storage, sessionIdentity: identity });
      observations.push({ identity, count: api.entries.length });
      return api;
    }, { initialProps: { identity: "A" } });
    const priorUndo = vi.fn().mockResolvedValue(undefined);
    act(() => {
      result.current.record({ id: "shared-undo", kind: "symbol.remove", title: "Prior A action", undo: { run: priorUndo } });
      result.current.beginOperation({ id: "shared-operation", kind: "journal.post", title: "Prior A operation" });
    });
    const prior = result.current;
    rerender({ identity: "B" });
    const beforeReturn = observations.length;
    rerender({ identity: "A" });
    expect(observations.slice(beforeReturn).every((item) => item.count === 0)).toBe(true);
    const currentUndo = vi.fn().mockResolvedValue(undefined);
    act(() => {
      result.current.record({ id: "shared-undo", kind: "symbol.remove", title: "New A action", undo: { run: currentUndo } });
      result.current.beginOperation({ id: "shared-operation", kind: "journal.post", title: "New A operation" });
    });
    await act(async () => {
      expect(prior.record({ kind: "late-A", title: "Stale callback" })).toBe("");
      expect(prior.beginOperation({ kind: "late-A-operation", title: "Stale operation" })).toBe("");
      prior.updateOperation("shared-operation", { status: "completed" });
      await prior.undo("shared-undo");
      prior.remove("shared-operation");
      prior.clear();
      prior.markAllSeen();
    });
    expect(result.current.entries).toHaveLength(2);
    expect(result.current.entries.find((item) => item.id === "shared-operation")?.operation?.status).toBe("pending");
    expect(result.current.unseenCount).toBe(2);
    expect(priorUndo).not.toHaveBeenCalled();
    expect(currentUndo).not.toHaveBeenCalled();
    await act(async () => { await result.current.undo("shared-undo"); });
    expect(currentUndo).toHaveBeenCalledTimes(1);
  });

  it.each(["resolved", "rejected"])("ignores an old epoch's %s Undo completion after A-to-B-to-A while a new Undo is running", async (outcome) => {
    const storage = memoryStorage();
    let resolveOld!: () => void;
    let rejectOld!: (error: Error) => void;
    let resolveNew!: () => void;
    const oldCompensate = vi.fn(() => new Promise<void>((resolve, reject) => { resolveOld = resolve; rejectOld = reject; }));
    const newCompensate = vi.fn(() => new Promise<void>((resolve) => { resolveNew = resolve; }));
    const { result, rerender } = renderHook(({ identity }) => useActivityLogService({ storage, sessionIdentity: identity }), { initialProps: { identity: "A" } });
    act(() => { result.current.record({ id: "shared-undo", kind: "symbol.remove", title: "Prior A action", undo: { run: oldCompensate } }); });
    let oldPending!: Promise<void>;
    act(() => { oldPending = result.current.undo("shared-undo"); });
    rerender({ identity: "B" });
    rerender({ identity: "A" });
    act(() => { result.current.record({ id: "shared-undo", kind: "symbol.remove", title: "New A action", undo: { run: newCompensate } }); });
    let newPending!: Promise<void>;
    act(() => { newPending = result.current.undo("shared-undo"); });
    await act(async () => {
      if (outcome === "resolved") resolveOld();
      else rejectOld(new Error("Old A operation failed."));
      await oldPending;
    });
    expect(result.current.entries[0]).toMatchObject({ title: "New A action", undoStatus: "running" });
    expect(result.current.entries[0].undoError).toBeUndefined();
    await act(async () => { await result.current.undo("shared-undo"); });
    expect(newCompensate).toHaveBeenCalledTimes(1);
    await act(async () => { resolveNew(); await newPending; });
    expect(result.current.entries[0].undoStatus).toBe("undone");
  });

  it("retains same-owner history while session identity is initially unresolved", () => {
    const storage = memoryStorage();
    saveActivityLog([entry({ undoStatus: "none" })], storage, "A");
    const { result, rerender } = renderHook(({ identity }) => useActivityLogService({ storage, sessionIdentity: identity }), { initialProps: { identity: null as string | null } });
    expect(result.current.entries).toEqual([]);
    expect(storage.getItem(ACTIVITY_LOG_STORAGE_KEY)).not.toBeNull();
    rerender({ identity: "A" });
    expect(result.current.entries[0]?.title).toBe("Added SPY");
    rerender({ identity: null });
    expect(result.current.entries).toEqual([]);
    expect(storage.getItem(ACTIVITY_LOG_STORAGE_KEY)).toBeNull();
  });

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

  it("prevents duplicate compensating mutations while Undo is in flight", async () => {
    let finish!: () => void;
    const compensate = vi.fn(() => new Promise<void>((resolve) => { finish = resolve; }));
    const { result } = renderHook(() => useActivityLogService({ storage: memoryStorage() }));
    act(() => { result.current.record({ id: "undo-once", kind: "symbol.remove", title: "Removed MSFT", undo: { run: compensate } }); });
    let first!: Promise<void>;
    act(() => { first = result.current.undo("undo-once"); });
    await act(async () => { await result.current.undo("undo-once"); });
    expect(compensate).toHaveBeenCalledTimes(1);
    await act(async () => { finish(); await first; });
    expect(result.current.entries[0].undoStatus).toBe("undone");
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
  it("shows an uncertain operation with its scope and retained-record link without a retry command", async () => {
    const storage = memoryStorage();
    saveActivityLog([entry({ undoStatus: "none", title: "Post asset journal", route: "/accounting/journal-entries?journalEntryId=J1", routeLabel: "Inspect retained workflow", operation: { status: "uncertain", actor: "reviewer-A", scope: "Book A / May", reference: "command-42", updatedAt: "2026-07-06T12:00:00Z" } })], storage);
    const user = userEvent.setup();
    render(<MemoryRouter><Harness storage={storage} /></MemoryRouter>);
    await user.click(screen.getByRole("button", { name: /Activity/ }));
    expect(await screen.findByRole("heading", { name: "Recent operations" })).toBeInTheDocument();
    expect(screen.getByText("Outcome unconfirmed")).toBeInTheDocument();
    expect(screen.getByText("Book A / May")).toBeInTheDocument();
    expect(screen.getByText("reviewer-A")).toBeInTheDocument();
    expect(screen.getByText("command-42")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Open Post asset journal" })).toHaveAttribute("href", "/accounting/journal-entries?journalEntryId=J1");
    expect(screen.queryByRole("button", { name: /retry|undo/i })).not.toBeInTheDocument();
    expect(screen.getByText(/Local history is a navigation aid/)).toBeInTheDocument();
  });

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
