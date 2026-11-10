import { describe, expect, it } from "vitest";
import { listTimeZoneOptions, zonedWallTimeToUtc } from "@/lib/time-zone";

describe("zonedWallTimeToUtc", () => {
  it("returns the wall time unchanged for UTC, blank and unknown zones", () => {
    for (const zone of ["UTC", "", "Mars/Olympus_Mons"]) {
      expect(zonedWallTimeToUtc(2026, 6, 1, 8, 0, zone).toISOString()).toBe("2026-07-01T08:00:00.000Z");
    }
  });

  it("applies standard and daylight offsets for the same zone", () => {
    expect(zonedWallTimeToUtc(2026, 0, 15, 8, 0, "America/New_York").toISOString()).toBe("2026-01-15T13:00:00.000Z");
    expect(zonedWallTimeToUtc(2026, 6, 1, 8, 0, "America/New_York").toISOString()).toBe("2026-07-01T12:00:00.000Z");
  });

  it("handles zones ahead of UTC, where the UTC instant falls on the previous day", () => {
    expect(zonedWallTimeToUtc(2026, 6, 1, 8, 0, "Asia/Tokyo").toISOString()).toBe("2026-06-30T23:00:00.000Z");
  });

  it("settles on the correct offset on the day daylight time starts", () => {
    // 8 March 2026: New York springs forward at 02:00, so 08:00 local is already EDT.
    expect(zonedWallTimeToUtc(2026, 2, 8, 8, 0, "America/New_York").toISOString()).toBe("2026-03-08T12:00:00.000Z");
  });
});

describe("listTimeZoneOptions", () => {
  it("lists UTC first, without duplicates, and always includes the current value", () => {
    const options = listTimeZoneOptions("Custom/Zone");

    expect(options[0]).toBe("Custom/Zone");
    expect(options[1]).toBe("UTC");
    expect(options.filter((zone) => zone === "UTC")).toHaveLength(1);
    expect(listTimeZoneOptions("UTC")[0]).toBe("UTC");
  });
});
