import { afterEach, describe, expect, it, vi } from "vitest";
import {
  formatRelativeTime,
  formatTimestamp,
  formatUtcTimestamp,
  getDisplayTimeZone,
  isValidTimeZone,
  setDisplayTimeZone,
  subscribeDisplayTimeZone
} from "@/lib/format";
import { DISPLAY_TIME_ZONE_STORAGE_KEY, reloadDisplayTimeZone } from "@/lib/display-time-zone";

afterEach(() => {
  setDisplayTimeZone("UTC");
  localStorage.removeItem(DISPLAY_TIME_ZONE_STORAGE_KEY);
  reloadDisplayTimeZone();
});

describe("formatTimestamp", () => {
  it("defaults to UTC with a 24-hour clock and the year, byte-identical to formatUtcTimestamp", () => {
    expect(getDisplayTimeZone()).toBe("UTC");
    expect(formatTimestamp(new Date("2026-06-30T18:05:00Z"))).toBe("Jun 30, 2026, 18:05 UTC");
    expect(formatTimestamp("2025-01-01T00:00:00Z")).toBe("Jan 1, 2025, 00:00 UTC");
    expect(formatTimestamp(Date.parse("2026-12-31T23:59:59Z"))).toBe("Dec 31, 2026, 23:59 UTC");
    expect(formatTimestamp("not a time")).toBe("Invalid Date");
    expect(formatUtcTimestamp(new Date("2026-06-30T18:05:00Z"))).toBe("Jun 30, 2026, 18:05 UTC");
  });

  it("renders in the operator's display zone with its label", () => {
    setDisplayTimeZone("America/New_York");
    expect(formatTimestamp("2026-06-30T18:05:00Z")).toBe("Jun 30, 2026, 14:05 EDT");
    setDisplayTimeZone("Asia/Tokyo");
    expect(formatTimestamp("2026-06-30T18:05:00Z")).toBe("Jul 1, 2026, 03:05 GMT+9");
    // A UTC-defined label stays UTC whatever the preference.
    expect(formatUtcTimestamp(new Date("2026-06-30T18:05:00Z"))).toBe("Jun 30, 2026, 18:05 UTC");
    expect(formatTimestamp("2026-06-30T18:05:00Z", { timeZone: "UTC" })).toBe("Jun 30, 2026, 18:05 UTC");
  });

  it("adds seconds or milliseconds to the same layout with the precision option", () => {
    const instant = "2026-06-30T18:05:07.042Z";
    expect(formatTimestamp(instant)).toBe("Jun 30, 2026, 18:05 UTC");
    expect(formatTimestamp(instant, { precision: "minute" })).toBe("Jun 30, 2026, 18:05 UTC");
    expect(formatTimestamp(instant, { precision: "second" })).toBe("Jun 30, 2026, 18:05:07 UTC");
    expect(formatTimestamp(instant, { precision: "millisecond" })).toBe("Jun 30, 2026, 18:05:07.042 UTC");
    expect(formatTimestamp("2026-06-30T00:00:00.000Z", { precision: "millisecond" })).toBe("Jun 30, 2026, 00:00:00.000 UTC");
    expect(formatTimestamp("not a time", { precision: "second" })).toBe("Invalid Date");
    expect(formatUtcTimestamp(new Date(instant), { precision: "second" })).toBe("Jun 30, 2026, 18:05:07 UTC");

    setDisplayTimeZone("America/New_York");
    expect(formatTimestamp(instant, { precision: "second" })).toBe("Jun 30, 2026, 14:05:07 EDT");
    expect(formatTimestamp(instant, { precision: "millisecond" })).toBe("Jun 30, 2026, 14:05:07.042 EDT");
    expect(formatUtcTimestamp(new Date(instant), { precision: "millisecond" })).toBe("Jun 30, 2026, 18:05:07.042 UTC");
    setDisplayTimeZone("Asia/Kolkata");
    expect(formatTimestamp(instant, { precision: "second" })).toBe("Jun 30, 2026, 23:35:07 GMT+5:30");
  });

  it("follows a daylight-saving transition without a 24:00 or 12-hour reading", () => {
    setDisplayTimeZone("America/New_York");
    // US spring forward, 2026-03-08: 01:59 EST is followed by 03:00 EDT.
    expect(formatTimestamp("2026-03-08T06:59:00Z")).toBe("Mar 8, 2026, 01:59 EST");
    expect(formatTimestamp("2026-03-08T07:00:00Z")).toBe("Mar 8, 2026, 03:00 EDT");
    // US fall back, 2026-11-01: 01:30 occurs twice, told apart by the zone label.
    expect(formatTimestamp("2026-11-01T05:30:00Z")).toBe("Nov 1, 2026, 01:30 EDT");
    expect(formatTimestamp("2026-11-01T06:30:00Z")).toBe("Nov 1, 2026, 01:30 EST");
    expect(formatTimestamp("2026-11-01T04:00:00Z")).toBe("Nov 1, 2026, 00:00 EDT");
  });
});

describe("display time zone preference", () => {
  it("validates IANA zones, canonicalizes their spelling and persists the choice", () => {
    expect(isValidTimeZone("Europe/London")).toBe(true);
    expect(isValidTimeZone("Mars/Olympus_Mons")).toBe(false);
    expect(isValidTimeZone("")).toBe(false);

    expect(setDisplayTimeZone("Mars/Olympus_Mons")).toBe(false);
    expect(getDisplayTimeZone()).toBe("UTC");

    expect(setDisplayTimeZone("america/new_york")).toBe(true);
    expect(getDisplayTimeZone()).toBe("America/New_York");
    expect(localStorage.getItem(DISPLAY_TIME_ZONE_STORAGE_KEY)).toBe("America/New_York");

    setDisplayTimeZone("UTC");
    expect(localStorage.getItem(DISPLAY_TIME_ZONE_STORAGE_KEY)).toBeNull();
  });

  it("reads a stored preference and ignores a stored zone the runtime does not know", () => {
    localStorage.setItem(DISPLAY_TIME_ZONE_STORAGE_KEY, "Europe/London");
    reloadDisplayTimeZone();
    expect(getDisplayTimeZone()).toBe("Europe/London");

    localStorage.setItem(DISPLAY_TIME_ZONE_STORAGE_KEY, "Not/AZone");
    reloadDisplayTimeZone();
    expect(getDisplayTimeZone()).toBe("UTC");
  });

  it("keeps working when storage is blocked", () => {
    const setItem = vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => {
      throw new Error("blocked");
    });
    try {
      expect(setDisplayTimeZone("Asia/Tokyo")).toBe(true);
      expect(getDisplayTimeZone()).toBe("Asia/Tokyo");
    } finally {
      setItem.mockRestore();
    }
  });

  it("notifies subscribers on change only", () => {
    const listener = vi.fn();
    const unsubscribe = subscribeDisplayTimeZone(listener);
    setDisplayTimeZone("Europe/Paris");
    setDisplayTimeZone("Europe/Paris");
    unsubscribe();
    setDisplayTimeZone("UTC");
    expect(listener).toHaveBeenCalledTimes(1);
  });
});

describe("display time zone across tabs", () => {
  it("picks up another tab's change without any subscriber mounted", () => {
    // A formatter-only screen (no <TimeText>, no Settings control) has read the zone once.
    expect(formatTimestamp(new Date("2026-06-30T18:05:00Z"))).toBe("Jun 30, 2026, 18:05 UTC");

    // Another tab stores a new preference; this tab only sees the storage event.
    localStorage.setItem(DISPLAY_TIME_ZONE_STORAGE_KEY, "Asia/Tokyo");
    window.dispatchEvent(new StorageEvent("storage", { key: DISPLAY_TIME_ZONE_STORAGE_KEY, newValue: "Asia/Tokyo" }));

    expect(getDisplayTimeZone()).toBe("Asia/Tokyo");
    expect(formatTimestamp(new Date("2026-06-30T18:05:00Z"))).toBe("Jul 1, 2026, 03:05 GMT+9");
  });
});

describe("formatRelativeTime", () => {
  const now = Date.parse("2026-05-09T12:00:00.000Z");

  it("renders past instants as an age", () => {
    expect(formatRelativeTime("2026-05-09T11:59:30.000Z", now)).toBe("30s ago");
    expect(formatRelativeTime("2026-05-09T11:55:00.000Z", now)).toBe("5m ago");
    expect(formatRelativeTime("2026-05-09T07:00:00.000Z", now)).toBe("5h ago");
    expect(formatRelativeTime("2026-05-06T12:00:00.000Z", now)).toBe("3d ago");
    expect(formatRelativeTime(now, now)).toBe("0s ago");
  });

  it("renders future instants with 'in'", () => {
    expect(formatRelativeTime("2026-05-09T14:00:00.000Z", now)).toBe("in 2h");
    expect(formatRelativeTime("2026-05-09T12:00:45.000Z", now)).toBe("in 45s");
  });

  it("does not depend on the display zone, and rejects invalid input", () => {
    setDisplayTimeZone("Asia/Tokyo");
    expect(formatRelativeTime("2026-05-09T11:55:00.000Z", now)).toBe("5m ago");
    expect(formatRelativeTime("bad", now)).toBe("Invalid Date");
  });
});
