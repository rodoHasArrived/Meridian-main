import { describe, expect, it } from "vitest";
import {
  buildReportingPeriodOptions,
  compareIsoDate,
  formatReportingPeriodLabel,
  hasRetainedReportingAsOfDateValue,
  isIsoDate,
  parseIsoDate,
  reportingRunRequiresPeriodConfirmation,
  shiftIsoDate,
  todayIsoDate
} from "@/lib/reporting-periods";

describe("reporting period math", () => {
  it("validates ISO calendar dates including month-length bounds", () => {
    expect(isIsoDate("2026-06-24")).toBe(true);
    expect(isIsoDate("2024-02-29")).toBe(true);
    expect(isIsoDate("2026-02-29")).toBe(false);
    expect(isIsoDate("2026-13-01")).toBe(false);
    expect(isIsoDate("2026-06-31")).toBe(false);
    expect(isIsoDate("not-a-date")).toBe(false);
    expect(parseIsoDate("2026-06-24")).toEqual({ year: 2026, month: 6, day: 24 });
  });

  it("shifts by calendar days across month and year boundaries", () => {
    expect(shiftIsoDate("2026-01-01", "day", -1)).toBe("2025-12-31");
    expect(shiftIsoDate("2026-06-24", "day", 7)).toBe("2026-07-01");
  });

  it("clamps the day when shifting by month, quarter, and year", () => {
    expect(shiftIsoDate("2026-03-31", "month", -1)).toBe("2026-02-28");
    expect(shiftIsoDate("2024-03-31", "month", -1)).toBe("2024-02-29");
    expect(shiftIsoDate("2026-06-30", "month", -1)).toBe("2026-05-30");
    expect(shiftIsoDate("2026-12-31", "quarter", -1)).toBe("2026-09-30");
    expect(shiftIsoDate("2024-02-29", "year", -1)).toBe("2023-02-28");
  });

  it("throws on an invalid anchor", () => {
    expect(() => shiftIsoDate("2026-02-30", "month", -1)).toThrow();
  });

  it("formats period labels in UTC", () => {
    expect(formatReportingPeriodLabel("2026-05-31")).toBe("May 31, 2026");
    expect(formatReportingPeriodLabel("invalid")).toBe("invalid");
  });

  it("fails terminal run presentation closed when the reporting period is missing", () => {
    expect(hasRetainedReportingAsOfDateValue("2026-06-30")).toBe(true);
    expect(hasRetainedReportingAsOfDateValue("As-of date unavailable")).toBe(false);
    expect(reportingRunRequiresPeriodConfirmation("Published", "As-of date unavailable")).toBe(true);
    expect(reportingRunRequiresPeriodConfirmation("Released", null)).toBe(true);
    expect(reportingRunRequiresPeriodConfirmation("AwaitingApproval", null)).toBe(false);
    expect(reportingRunRequiresPeriodConfirmation("Published", "2026-06-30")).toBe(false);
  });

  it("compares ISO dates without timezone effects", () => {
    expect(compareIsoDate("2026-05-31", "2026-06-01")).toBeLessThan(0);
    expect(compareIsoDate("2026-06-01", "2026-05-31")).toBeGreaterThan(0);
    expect(compareIsoDate("2026-06-01", "2026-06-01")).toBe(0);
  });

  it("builds quick-pick options anchored on the current as-of date", () => {
    const options = buildReportingPeriodOptions("2026-06-30");
    expect(options.map((option) => option.id)).toEqual([
      "current",
      "prev-day",
      "prev-month",
      "prev-quarter",
      "prev-year"
    ]);
    expect(options[0].asOfDate).toBe("2026-06-30");
    expect(options.find((option) => option.id === "prev-month")?.asOfDate).toBe("2026-05-30");
    expect(options.find((option) => option.id === "prev-quarter")?.asOfDate).toBe("2026-03-30");
    expect(options.find((option) => option.id === "prev-year")?.asOfDate).toBe("2025-06-30");
    expect(options.find((option) => option.id === "prev-month")?.description).toBe("May 30, 2026");
  });

  it("returns no options for an invalid anchor", () => {
    expect(buildReportingPeriodOptions("nonsense")).toEqual([]);
  });

  it("derives today's ISO date in UTC from a fixed clock", () => {
    expect(todayIsoDate(new Date("2026-06-24T23:30:00Z"))).toBe("2026-06-24");
  });
});
