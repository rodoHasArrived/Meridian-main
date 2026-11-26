import { describe, expect, it } from "vitest";
import { maxDueDaysFor, parseDueDays, resolveReportingSchedulePeriodDueAtUtc } from "@/lib/reporting-schedule-due";

describe("reporting schedule due days", () => {
  it("applies per-frequency limits that mirror the server planner", () => {
    expect(maxDueDaysFor("Weekly")).toBe(6);
    expect(maxDueDaysFor("Daily")).toBe(1);
    expect(maxDueDaysFor("Monthly")).toBe(366);
    expect(maxDueDaysFor("")).toBe(366);
  });

  it("clamps parsed due days to the frequency range", () => {
    expect(parseDueDays("9", "Weekly")).toBe(6);
    expect(parseDueDays("3", "Daily")).toBe(1);
    expect(parseDueDays("400")).toBe(366);
    expect(parseDueDays("-2", "Weekly")).toBe(0);
    expect(parseDueDays("abc", "Daily")).toBe(0);
  });

  it("previews a daily close due the next day at the cadence time", () => {
    expect(resolveReportingSchedulePeriodDueAtUtc("2026-11-30", "0 8 * * *", "5", "fallback", "UTC", "Daily"))
      .toBe("2026-12-01T08:00:00Z");
  });
});
