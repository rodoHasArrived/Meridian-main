import { describe, expect, it } from "vitest";
import { formatDateTimeUtc, formatRelativeAge } from "@/lib/time";

describe("formatDateTimeUtc", () => {
  it("preserves an explicit UTC instant and distinguishes unavailable dates", () => {
    expect(formatDateTimeUtc("2026-11-11T02:30:00+02:00")).toBe("Nov 11, 2026, 00:30 UTC");
    expect(formatDateTimeUtc("invalid")).toBe("Timestamp unavailable");
    expect(formatDateTimeUtc(null)).toBe("Timestamp unavailable");
  });
});

describe("formatRelativeAge", () => {
  it("returns Never for missing or invalid timestamps", () => {
    expect(formatRelativeAge(null)).toBe("Never");
    expect(formatRelativeAge("bad")).toBe("Never");
  });

  it("formats second and minute ages", () => {
    const now = Date.parse("2026-05-09T01:00:00.000Z");
    expect(formatRelativeAge("2026-05-09T00:59:30.000Z", now)).toBe("30s ago");
    expect(formatRelativeAge("2026-05-09T00:40:00.000Z", now)).toBe("20m ago");
  });

  it("formats hour and day ages", () => {
    const now = Date.parse("2026-05-09T12:00:00.000Z");
    expect(formatRelativeAge("2026-05-09T07:00:00.000Z", now)).toBe("5h ago");
    expect(formatRelativeAge("2026-05-06T12:00:00.000Z", now)).toBe("3d ago");
  });

  it("falls back to an absolute label in the display zone for future timestamps", () => {
    // Previously the host-locale `toLocaleString()`, which varied by machine and used a 12-hour clock.
    const now = Date.parse("2026-05-09T01:00:00.000Z");
    const future = "2026-05-09T02:00:00.000Z";
    expect(formatRelativeAge(future, now)).toBe("May 9, 2026, 02:00 UTC");
  });
});
