import { afterAll, beforeAll, describe, expect, it } from "vitest";
import {
  DOW,
  LocalDate,
  addDays,
  addMonths,
  clampDate,
  dayOfInstant,
  dowLabels,
  fromISO,
  longDate,
  monthTitle,
  pad,
  parseLoose,
  rangePresets,
  sameDay,
  toISO,
  utcDayOfInstant,
  utcToday,
  weeksFor
} from "@/lib/local-date";
import { todayIsoDate } from "@/lib/reporting-periods";

/**
 * Every zone-sensitive rule is exercised in three zones: one west of Greenwich, one east of it and
 * UTC itself. In UTC alone the local day and the UTC day always coincide, so a UTC-only run could
 * not tell a correct calendar date from one that had been shifted.
 */
const ZONES = [
  { zone: "America/New_York", januaryOffsetMinutes: 300 },
  { zone: "Asia/Tokyo", januaryOffsetMinutes: -540 },
  { zone: "UTC", januaryOffsetMinutes: 0 }
] as const;

const originalTimeZone = process.env.TZ;

function useTimeZone(zone: string): void {
  beforeAll(() => {
    process.env.TZ = zone;
  });
  afterAll(() => {
    if (originalTimeZone === undefined) {
      delete process.env.TZ;
    } else {
      process.env.TZ = originalTimeZone;
    }
  });
}

describe.each(ZONES)("LocalDate in $zone", ({ zone, januaryOffsetMinutes }) => {
  useTimeZone(zone);

  it("runs with the zone actually pinned", () => {
    expect(new Date("2026-01-15T12:00:00Z").getTimezoneOffset()).toBe(januaryOffsetMinutes);
  });

  it("round-trips a calendar date without moving the day", () => {
    for (const day of ["2026-01-01", "2026-03-08", "2026-06-30", "2026-11-01", "2026-12-31", "2024-02-29"]) {
      const date = fromISO(day);
      expect(date).not.toBeNull();
      expect(toISO(date as Date)).toBe(day);
      expect((date as Date).getHours()).toBe(0);
    }
  });

  it("reads the local fields of a date, never its UTC fields", () => {
    expect(toISO(new Date(2026, 5, 30, 23, 59, 59))).toBe("2026-06-30");
    expect(toISO(new Date(2026, 5, 30, 0, 0, 0))).toBe("2026-06-30");
    expect(toISO(new Date(2026, 0, 5))).toBe("2026-01-05");
  });

  it("keeps a calendar-date string on its own day when asked for its day bucket", () => {
    expect(dayOfInstant("2026-06-30")).toBe("2026-06-30");
    expect(dayOfInstant("2026-01-01")).toBe("2026-01-01");
  });

  it("adds days on the calendar across both daylight-saving changes", () => {
    expect(toISO(addDays(fromISO("2026-03-07") as Date, 1))).toBe("2026-03-08");
    expect(toISO(addDays(fromISO("2026-03-08") as Date, 1))).toBe("2026-03-09");
    expect(toISO(addDays(fromISO("2026-10-31") as Date, 1))).toBe("2026-11-01");
    expect(toISO(addDays(fromISO("2026-11-01") as Date, 1))).toBe("2026-11-02");
    expect(toISO(addDays(fromISO("2026-01-01") as Date, -1))).toBe("2025-12-31");
    expect(toISO(addDays(fromISO("2026-03-01") as Date, 30))).toBe("2026-03-31");
  });

  it("does not mutate the date it is given", () => {
    const start = fromISO("2026-06-30") as Date;
    addDays(start, 5);
    addMonths(start, 2);
    expect(toISO(start)).toBe("2026-06-30");
  });

  it("builds desk ranges on the operator's calendar from an injected today", () => {
    expect(rangePresets(new Date(2026, 7, 26, 23, 30))).toEqual([
      { id: "today", label: "Today", start: "2026-08-26", end: "2026-08-26" },
      { id: "7d", label: "Last 7d", start: "2026-08-20", end: "2026-08-26" },
      { id: "30d", label: "Last 30d", start: "2026-07-28", end: "2026-08-26" },
      { id: "mtd", label: "MTD", start: "2026-08-01", end: "2026-08-26" },
      { id: "qtd", label: "QTD", start: "2026-07-01", end: "2026-08-26" },
      { id: "ytd", label: "YTD", start: "2026-01-01", end: "2026-08-26" }
    ]);
  });

  it("spells a date out on its own day in the pinned workstation locale", () => {
    expect(longDate(fromISO("2026-06-30") as Date)).toBe("Tuesday, June 30, 2026");
    expect(monthTitle(fromISO("2026-06-30") as Date)).toBe("June 2026");
    expect(monthTitle(fromISO("2026-01-01") as Date)).toBe("January 2026");
    expect(longDate(fromISO("2026-01-01") as Date)).toBe("Thursday, January 1, 2026");
  });

  it("names the UTC day of now the same way the reporting period module does", () => {
    const now = new Date("2026-07-01T02:30:00Z");
    expect(utcToday(now)).toBe("2026-07-01");
    expect(utcToday(now)).toBe(todayIsoDate(now));
    expect(utcToday(now)).toBe(utcDayOfInstant(now));
  });
});

describe("LocalDate day buckets west of Greenwich", () => {
  useTimeZone("America/New_York");

  it("buckets an evening instant on the operator's day, and on the next day only when UTC is asked for", () => {
    const eveningInNewYork = "2026-07-01T02:30:00Z";
    expect(dayOfInstant(eveningInNewYork)).toBe("2026-06-30");
    expect(utcDayOfInstant(eveningInNewYork)).toBe("2026-07-01");
    expect(dayOfInstant(new Date(eveningInNewYork))).toBe("2026-06-30");
    expect(dayOfInstant(Date.parse(eveningInNewYork))).toBe("2026-06-30");
    expect(utcDayOfInstant(Date.parse(eveningInNewYork))).toBe("2026-07-01");
  });

  it("keeps local midnight on its own day where a UTC serialisation would not disagree, and late evening where it would", () => {
    const lateEvening = new Date(2026, 5, 30, 21, 0, 0);
    expect(toISO(lateEvening)).toBe("2026-06-30");
    expect(utcDayOfInstant(lateEvening)).toBe("2026-07-01");
  });
});

describe("LocalDate day buckets east of Greenwich", () => {
  useTimeZone("Asia/Tokyo");

  it("buckets a morning instant on the operator's day, and on the previous day only when UTC is asked for", () => {
    const morningInTokyo = "2026-06-29T22:30:00Z";
    expect(dayOfInstant(morningInTokyo)).toBe("2026-06-30");
    expect(utcDayOfInstant(morningInTokyo)).toBe("2026-06-29");
  });

  it("keeps local midnight on its own day although its UTC day is the day before", () => {
    const localMidnight = fromISO("2026-06-30") as Date;
    expect(toISO(localMidnight)).toBe("2026-06-30");
    expect(utcDayOfInstant(localMidnight)).toBe("2026-06-29");
  });
});

describe("LocalDate.fromISO", () => {
  it("accepts exactly YYYY-MM-DD, tolerating surrounding whitespace", () => {
    expect(toISO(fromISO("2026-06-30") as Date)).toBe("2026-06-30");
    expect(toISO(fromISO("  2026-06-30 ") as Date)).toBe("2026-06-30");
  });

  it("refuses dates that do not exist instead of rolling them over", () => {
    expect(fromISO("2026-02-30")).toBeNull();
    expect(fromISO("2026-02-29")).toBeNull();
    expect(fromISO("2026-13-01")).toBeNull();
    expect(fromISO("2026-00-10")).toBeNull();
    expect(fromISO("2026-04-31")).toBeNull();
    expect(fromISO("2024-02-29")).not.toBeNull();
  });

  it("refuses anything that is not a date-only string", () => {
    expect(fromISO("2026-6-15")).toBeNull();
    expect(fromISO("2026-06-30T00:00:00Z")).toBeNull();
    expect(fromISO("06/30/2026")).toBeNull();
    expect(fromISO("Not supplied")).toBeNull();
    expect(fromISO("")).toBeNull();
    expect(fromISO(null)).toBeNull();
    expect(fromISO(undefined)).toBeNull();
    expect(fromISO(20260630 as unknown as string)).toBeNull();
  });

  it("refuses two-digit-era years rather than reading them as the 1900s", () => {
    expect(fromISO("0050-01-01")).toBeNull();
  });

  it("keeps a four-digit year for the first millennium", () => {
    expect(toISO(fromISO("0999-12-31") as Date)).toBe("0999-12-31");
  });
});

describe("LocalDate instants", () => {
  it("returns null for a value that is not an instant", () => {
    expect(dayOfInstant("not a date")).toBeNull();
    expect(dayOfInstant(Number.NaN)).toBeNull();
    expect(dayOfInstant(new Date(Number.NaN))).toBeNull();
    expect(utcDayOfInstant("not a date")).toBeNull();
    expect(utcDayOfInstant(new Date(Number.NaN))).toBeNull();
  });

  it("reads the UTC day of an instant", () => {
    expect(utcDayOfInstant("2026-06-30T23:59:59Z")).toBe("2026-06-30");
    expect(utcDayOfInstant("2026-07-01T00:00:00Z")).toBe("2026-07-01");
    expect(utcDayOfInstant(Date.UTC(2026, 0, 5))).toBe("2026-01-05");
    expect(utcDayOfInstant("2026-06-30")).toBe("2026-06-30");
  });

  it("gives the same UTC today as the text every screen used to cut out of the instant", () => {
    for (const instant of ["2026-06-30T23:59:59.999Z", "2026-07-01T00:00:00.000Z", "2026-12-31T23:30:00-05:00", "2026-01-01T08:15:00+09:00"]) {
      const now = new Date(instant);
      expect(utcToday(now)).toBe(now.toISOString().substring(0, 10));
    }
    expect(utcToday()).toBe(todayIsoDate());
  });

  it("throws for an invalid now, as serialising an invalid Date does", () => {
    expect(() => utcToday(new Date(Number.NaN))).toThrow(RangeError);
  });
});

describe("LocalDate arithmetic", () => {
  it("clamps the day when a month is shorter", () => {
    expect(toISO(addMonths(fromISO("2026-01-31") as Date, 1))).toBe("2026-02-28");
    expect(toISO(addMonths(fromISO("2024-01-31") as Date, 1))).toBe("2024-02-29");
    expect(toISO(addMonths(fromISO("2026-03-31") as Date, -1))).toBe("2026-02-28");
    expect(toISO(addMonths(fromISO("2026-08-31") as Date, 1))).toBe("2026-09-30");
    expect(toISO(addMonths(fromISO("2026-11-15") as Date, 3))).toBe("2027-02-15");
    expect(toISO(addMonths(fromISO("2026-01-15") as Date, -2))).toBe("2025-11-15");
  });

  it("compares calendar days, not instants", () => {
    expect(sameDay(new Date(2026, 5, 30, 1), new Date(2026, 5, 30, 23))).toBe(true);
    expect(sameDay(new Date(2026, 5, 30), new Date(2026, 6, 1))).toBe(false);
    expect(sameDay(null, new Date(2026, 5, 30))).toBe(false);
    expect(sameDay(new Date(2026, 5, 30), null)).toBe(false);
    expect(sameDay(null, null)).toBe(false);
  });

  it("clamps into an optional window", () => {
    const min = fromISO("2026-06-10") as Date;
    const max = fromISO("2026-06-20") as Date;
    expect(clampDate(fromISO("2026-06-01") as Date, min, max)).toBe(min);
    expect(clampDate(fromISO("2026-06-30") as Date, min, max)).toBe(max);
    const inside = fromISO("2026-06-15") as Date;
    expect(clampDate(inside, min, max)).toBe(inside);
    expect(clampDate(inside)).toBe(inside);
    expect(clampDate(inside, null, null)).toBe(inside);
  });

  it("pads to two digits", () => {
    expect(pad(3)).toBe("03");
    expect(pad(12)).toBe("12");
  });
});

describe("LocalDate.parseLoose", () => {
  it("accepts ISO, month-first slash dates and spelled-out dates", () => {
    expect(toISO(parseLoose("2026-08-26") as Date)).toBe("2026-08-26");
    expect(toISO(parseLoose("8/26/2026") as Date)).toBe("2026-08-26");
    expect(toISO(parseLoose("08.26.2026") as Date)).toBe("2026-08-26");
    expect(toISO(parseLoose("8-26-2026") as Date)).toBe("2026-08-26");
    expect(toISO(parseLoose("26 Aug 2026") as Date)).toBe("2026-08-26");
    expect(toISO(parseLoose("  26 Aug 2026 ") as Date)).toBe("2026-08-26");
  });

  it("never rolls an impossible date over into another day", () => {
    expect(parseLoose("2026-02-30")).toBeNull();
    expect(parseLoose("2026-2-30")).toBeNull();
    expect(parseLoose("2/30/2026")).toBeNull();
    expect(parseLoose("13/01/2026")).toBeNull();
    expect(parseLoose("30 Feb 2026")).toBeNull();
  });

  it("returns null for empty or unreadable text", () => {
    expect(parseLoose("")).toBeNull();
    expect(parseLoose("   ")).toBeNull();
    expect(parseLoose(null)).toBeNull();
    expect(parseLoose(undefined)).toBeNull();
    expect(parseLoose("Not supplied")).toBeNull();
  });
});

describe("LocalDate calendar grid", () => {
  it("rotates weekday headers to the first day of the week", () => {
    expect(dowLabels()).toEqual(["Su", "Mo", "Tu", "We", "Th", "Fr", "Sa"]);
    expect(dowLabels(1)).toEqual(["Mo", "Tu", "We", "Th", "Fr", "Sa", "Su"]);
    expect(DOW).toHaveLength(7);
  });

  it("covers a month in whole weeks", () => {
    const june = weeksFor(fromISO("2026-06-15") as Date);
    expect(june).toHaveLength(5);
    expect(june.every((week) => week.length === 7)).toBe(true);
    expect(toISO(june[0][0])).toBe("2026-05-31");
    expect(toISO(june[4][6])).toBe("2026-07-04");

    const february = weeksFor(fromISO("2026-02-10") as Date);
    expect(february).toHaveLength(4);
    expect(toISO(february[0][0])).toBe("2026-02-01");
    expect(toISO(february[3][6])).toBe("2026-02-28");

    const august = weeksFor(fromISO("2026-08-01") as Date);
    expect(august).toHaveLength(6);
    expect(toISO(august[0][0])).toBe("2026-07-26");
    expect(toISO(august[5][6])).toBe("2026-09-05");
  });

  it("starts weeks on the requested weekday", () => {
    const june = weeksFor(fromISO("2026-06-15") as Date, 1);
    expect(june[0][0].getDay()).toBe(1);
    expect(toISO(june[0][0])).toBe("2026-06-01");
    expect(toISO(june[june.length - 1][6])).toBe("2026-07-05");
  });
});

/**
 * Zones whose daylight-saving gap falls at local midnight: on the changeover day 00:00 does not
 * exist, so that day starts at 01:00. `month` contains such a changeover and its LAST day opens a
 * week row. A grid walked forward from the changeover carries the 01:00 wall time with it, and a
 * comparison of that instant against the last day's local midnight used to drop the final row.
 */
const MIDNIGHT_GAP_MONTHS = [
  { zone: "America/Havana", month: "2024-03", lastDay: "2024-03-31", rows: 6 },
  { zone: "Africa/Cairo", month: "2028-04", lastDay: "2028-04-30", rows: 6 },
  { zone: "America/Santiago", month: "2029-09", lastDay: "2029-09-30", rows: 6 },
  { zone: "America/Sao_Paulo", month: "2010-10", lastDay: "2010-10-31", rows: 6 }
] as const;

describe.each(MIDNIGHT_GAP_MONTHS)("LocalDate calendar grid across a midnight gap in $zone", ({ zone, month, lastDay, rows }) => {
  useTimeZone(zone);

  it("runs in a zone where one day of the month has no local midnight", () => {
    const [year, monthNumber, daysInMonth] = lastDay.split("-").map(Number);
    const startHours = Array.from({ length: daysInMonth }, (_, index) => new Date(year, monthNumber - 1, index + 1).getHours());
    expect(startHours.filter((hour) => hour !== 0)).toEqual([1]);
  });

  it("keeps the last day of the month when it opens the final week", () => {
    const weeks = weeksFor(fromISO(`${month}-15`) as Date);
    expect(weeks).toHaveLength(rows);
    expect(weeks.every((week) => week.length === 7)).toBe(true);
    expect(toISO(weeks[rows - 1][0])).toBe(lastDay);
    expect(weeks.flat().map(toISO)).toContain(lastDay);
  });

  it("lays every month of the year out as consecutive calendar days, each in its weekday column", () => {
    const year = Number(month.slice(0, 4));
    for (const weekStartsOn of [0, 1]) {
      for (let monthIndex = 0; monthIndex < 12; monthIndex += 1) {
        const cells = weeksFor(new Date(year, monthIndex, 15), weekStartsOn).flat();
        const days = cells.map(toISO);
        expect(cells.length % 7).toBe(0);
        expect(new Set(days).size).toBe(days.length);
        expect([...days].sort()).toEqual(days);
        cells.forEach((cell, index) => {
          expect(cell.getDay()).toBe((weekStartsOn + index) % 7);
        });
        const daysInMonth = new Date(year, monthIndex + 1, 0).getDate();
        for (let day = 1; day <= daysInMonth; day += 1) {
          expect(days).toContain(`${year}-${pad(monthIndex + 1)}-${pad(day)}`);
        }
        // Whole weeks covering the month and nothing more: the first and last rows both touch it.
        expect(cells[6].getMonth()).toBe(monthIndex);
        expect(cells[cells.length - 7].getMonth()).toBe(monthIndex);
      }
    }
  });
});

describe.each([{ zone: "America/Havana" }, { zone: "Asia/Tokyo" }, { zone: "UTC" }])("LocalDate impossible days as instants in $zone", ({ zone }) => {
  useTimeZone(zone);

  it("refuses an ISO-shaped day that does not exist instead of rolling it into another day", () => {
    expect(dayOfInstant("2026-02-30")).toBeNull();
    expect(dayOfInstant(" 2026-02-30 ")).toBeNull();
    expect(dayOfInstant("2026-02-29")).toBeNull();
    expect(dayOfInstant("2026-04-31")).toBeNull();
    expect(dayOfInstant("2026-13-01")).toBeNull();
    expect(dayOfInstant("2026-00-10")).toBeNull();
    expect(utcDayOfInstant("2026-02-30")).toBeNull();
    expect(utcDayOfInstant("2026-04-31")).toBeNull();
  });

  it("refuses an ISO datetime whose date part does not exist", () => {
    expect(dayOfInstant("2026-02-30T10:00:00Z")).toBeNull();
    expect(utcDayOfInstant("2026-02-30T10:00:00Z")).toBeNull();
    expect(dayOfInstant("2026-04-31T23:59:59.999-05:00")).toBeNull();
    expect(utcDayOfInstant("2026-04-31 08:00:00")).toBeNull();
  });

  it("still buckets real days, leap days included", () => {
    expect(dayOfInstant("2024-02-29")).toBe("2024-02-29");
    expect(utcDayOfInstant("2024-02-29")).toBe("2024-02-29");
    expect(utcDayOfInstant("2024-02-29T12:00:00Z")).toBe("2024-02-29");
    expect(dayOfInstant("2026-12-31T12:00:00Z")).toBe("2026-12-31");
    expect(utcDayOfInstant("2000-02-29T00:00:00Z")).toBe("2000-02-29");
    expect(utcDayOfInstant("1900-02-29T00:00:00Z")).toBeNull();
  });
});

describe("LocalDate namespace", () => {
  it("exposes the design-system surface member for member", () => {
    expect(Object.keys(LocalDate).sort()).toEqual([
      "addDays",
      "addMonths",
      "clampDate",
      "dayOfInstant",
      "dowLabels",
      "fromISO",
      "longDate",
      "monthTitle",
      "pad",
      "parseLoose",
      "rangePresets",
      "sameDay",
      "toISO",
      "utcDayOfInstant",
      "weeksFor"
    ]);
    expect(LocalDate.toISO(LocalDate.addDays(LocalDate.fromISO("2026-09-01") as Date, 4))).toBe("2026-09-05");
  });
});
