import { describe, expect, it } from "vitest";
import {
  dismissNotification,
  emptyNotificationReadState,
  loadNotificationReadState,
  markNotificationRead,
  markNotificationsRead,
  NOTIFICATION_READ_STATE_KEY,
  NOTIFICATION_READ_STATE_LIMIT,
  saveNotificationReadState,
  type StorageLike
} from "@/lib/notification-center/read-state";

function memoryStorage(seed: Record<string, string> = {}): StorageLike {
  const map = new Map(Object.entries(seed));
  return {
    getItem: (key) => map.get(key) ?? null,
    setItem: (key, value) => void map.set(key, value),
    removeItem: (key) => void map.delete(key)
  };
}

describe("notification read-state", () => {
  it("returns empty state when nothing is stored or the blob is malformed", () => {
    expect(loadNotificationReadState(memoryStorage())).toEqual(emptyNotificationReadState());
    expect(loadNotificationReadState(memoryStorage({ [NOTIFICATION_READ_STATE_KEY]: "not json" }))).toEqual(
      emptyNotificationReadState()
    );
  });

  it("round-trips read and dismissed ids, dropping non-string entries", () => {
    const storage = memoryStorage();
    saveNotificationReadState(
      { version: 1, readIds: ["inbox:a", "", "inbox:b"], dismissedIds: ["system:x"] } as never,
      storage
    );

    const loaded = loadNotificationReadState(storage);
    expect(loaded.readIds).toEqual(["inbox:a", "inbox:b"]);
    expect(loaded.dismissedIds).toEqual(["system:x"]);
  });

  it("marks ids read idempotently without duplication or churn", () => {
    let state = markNotificationRead(emptyNotificationReadState(), "inbox:a");
    state = markNotificationRead(state, "inbox:b");
    const beforeRepeat = state;
    state = markNotificationRead(state, "inbox:a");
    expect(state.readIds).toEqual(["inbox:a", "inbox:b"]);
    // Re-marking an already-read id is a no-op (same reference, no re-render churn).
    expect(state).toBe(beforeRepeat);
  });

  it("marks many ids read in one pass", () => {
    const state = markNotificationsRead(emptyNotificationReadState(), ["inbox:a", "system:b"]);
    expect(state.readIds).toEqual(["inbox:a", "system:b"]);
  });

  it("dismissing also marks the id read", () => {
    const state = dismissNotification(emptyNotificationReadState(), "inbox:a");
    expect(state.dismissedIds).toContain("inbox:a");
    expect(state.readIds).toContain("inbox:a");
  });

  it("caps stored id lists to the FIFO limit", () => {
    let state = emptyNotificationReadState();
    for (let index = 0; index < NOTIFICATION_READ_STATE_LIMIT + 25; index += 1) {
      state = markNotificationRead(state, `inbox:${index}`);
    }
    expect(state.readIds).toHaveLength(NOTIFICATION_READ_STATE_LIMIT);
    // Oldest ids trimmed; newest retained.
    expect(state.readIds[state.readIds.length - 1]).toBe(`inbox:${NOTIFICATION_READ_STATE_LIMIT + 24}`);
    expect(state.readIds[0]).toBe("inbox:25");
  });
});
