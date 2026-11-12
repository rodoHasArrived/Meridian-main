import { describe, expect, it } from "vitest";
import {
  dismissNotification,
  emptyNotificationReadState,
  markNotificationRead,
  setNotificationStatus
} from "./read-state";

describe("setNotificationStatus", () => {
  it("reverts a single mark-read", () => {
    const read = markNotificationRead(emptyNotificationReadState(), "a");
    const undone = setNotificationStatus(read, "a", { read: false, dismissed: false });

    expect(undone.readIds).toEqual([]);
    expect(undone.dismissedIds).toEqual([]);
  });

  it("reverts a dismissal and keeps the item read when it was read before", () => {
    const state = dismissNotification(markNotificationRead(emptyNotificationReadState(), "a"), "a");
    const undone = setNotificationStatus(state, "a", { read: true, dismissed: false });

    expect(undone.readIds).toEqual(["a"]);
    expect(undone.dismissedIds).toEqual([]);
  });

  it("leaves other ids untouched", () => {
    const state = markNotificationRead(markNotificationRead(emptyNotificationReadState(), "a"), "b");
    const undone = setNotificationStatus(state, "a", { read: false, dismissed: false });

    expect(undone.readIds).toEqual(["b"]);
  });

  it("returns the same reference when nothing changes", () => {
    const state = emptyNotificationReadState();
    expect(setNotificationStatus(state, "a", { read: false, dismissed: false })).toBe(state);
  });
});
