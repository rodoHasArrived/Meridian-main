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

describe("zonedWallTimeToUtc across daylight-saving edges", () => {
  it("moves a wall time inside the spring-forward gap to the first valid time, as the server does", () => {
    // 8 March 2026 in New York: 02:00-03:00 does not exist. 02:30 becomes 03:00 EDT (07:00Z),
    // the same result as ReportingSchedulePeriodPlanner.DueAtUtc, not 01:30 or 03:30.
    expect(zonedWallTimeToUtc(2026, 2, 8, 2, 30, "America/New_York").toISOString()).toBe("2026-03-08T07:00:00.000Z");
    expect(zonedWallTimeToUtc(2026, 2, 8, 2, 0, "America/New_York").toISOString()).toBe("2026-03-08T07:00:00.000Z");
    // Europe/London, 29 March 2026: 01:00-02:00 does not exist. 01:30 becomes 02:00 BST (01:00Z).
    expect(zonedWallTimeToUtc(2026, 2, 29, 1, 30, "Europe/London").toISOString()).toBe("2026-03-29T01:00:00.000Z");
  });

  it("keeps the wall times on either side of the gap exact", () => {
    expect(zonedWallTimeToUtc(2026, 2, 8, 1, 59, "America/New_York").toISOString()).toBe("2026-03-08T06:59:00.000Z");
    expect(zonedWallTimeToUtc(2026, 2, 8, 3, 0, "America/New_York").toISOString()).toBe("2026-03-08T07:00:00.000Z");
  });

  it("resolves a wall time repeated on fall back to its second, standard-time occurrence", () => {
    // 1 November 2026 in New York: 01:30 happens at 05:30Z (EDT) and again at 06:30Z (EST).
    expect(zonedWallTimeToUtc(2026, 10, 1, 1, 30, "America/New_York").toISOString()).toBe("2026-11-01T06:30:00.000Z");
    // Europe/London, 25 October 2026: 01:30 happens at 00:30Z (BST) and again at 01:30Z (GMT).
    expect(zonedWallTimeToUtc(2026, 9, 25, 1, 30, "Europe/London").toISOString()).toBe("2026-10-25T01:30:00.000Z");
    // Times just outside the repeated hour are unaffected.
    expect(zonedWallTimeToUtc(2026, 10, 1, 0, 59, "America/New_York").toISOString()).toBe("2026-11-01T04:59:00.000Z");
    expect(zonedWallTimeToUtc(2026, 10, 1, 2, 0, "America/New_York").toISOString()).toBe("2026-11-01T07:00:00.000Z");
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
