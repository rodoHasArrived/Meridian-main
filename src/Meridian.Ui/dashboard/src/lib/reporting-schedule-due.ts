import { UTC_TIME_ZONE, zonedWallTimeToUtc } from "@/lib/time-zone";

// Client-side preview of a reporting schedule's first due instant. The server validates the
// schedule and owns every later advance; this only keeps the draft form coherent.

/** Whole days between period end and due date, clamped to the range the server accepts. */
export function parseDueDays(value: string): number {
  const parsed = Number.parseInt(value, 10);
  return Number.isFinite(parsed) ? Math.min(366, Math.max(0, parsed)) : 0;
}

export function resolveReportingScheduleDueAtUtc(
  nextAsOfDate: string,
  cronExpression: string,
  currentDueAtUtc: string,
  timeZoneId: string = UTC_TIME_ZONE
): string {
  const dateMatch = /^(\d{4})-(\d{2})-(\d{2})$/.exec(nextAsOfDate.trim());
  const cronFields = cronExpression.trim().split(/\s+/);
  const minute = Number.parseInt(cronFields[0] ?? "", 10);
  const hour = Number.parseInt(cronFields[1] ?? "", 10);
  if (!dateMatch || !Number.isInteger(minute) || minute < 0 || minute > 59 || !Number.isInteger(hour) || hour < 0 || hour > 23) {
    return currentDueAtUtc;
  }

  const year = Number.parseInt(dateMatch[1], 10);
  const monthIndex = Number.parseInt(dateMatch[2], 10) - 1;
  const day = Number.parseInt(dateMatch[3], 10);
  // Calendar arithmetic runs on a UTC-anchored date that stands for the schedule's local
  // calendar day; the zone is applied once, when the wall time becomes a UTC instant.
  const dueDate = new Date(Date.UTC(year, monthIndex, day));
  if (
    dueDate.getUTCFullYear() !== year
    || dueDate.getUTCMonth() !== monthIndex
    || dueDate.getUTCDate() !== day
  ) {
    return currentDueAtUtc;
  }

  // The first day on or after the period end that the cadence preset runs on. A custom
  // cron keeps the period-end day; the server validates and advances it from there.
  const matchesPresetDay = reportingSchedulePresetDayMatchers[cronFields.join(" ")];
  if (matchesPresetDay) {
    for (let guard = 0; guard < 400 && !matchesPresetDay(dueDate); guard += 1) {
      dueDate.setUTCDate(dueDate.getUTCDate() + 1);
    }
  }

  const dueInstant = zonedWallTimeToUtc(
    dueDate.getUTCFullYear(),
    dueDate.getUTCMonth(),
    dueDate.getUTCDate(),
    hour,
    minute,
    timeZoneId
  );
  return dueInstant.toISOString().replace(/\.\d{3}Z$/, "Z");
}

/**
 * Due instant for a period-close schedule: the period end plus the due-day offset, at the
 * cadence's time of day in the schedule's time zone.
 */
export function resolveReportingSchedulePeriodDueAtUtc(
  nextAsOfDate: string,
  cronExpression: string,
  dueDaysAfterPeriodEnd: string,
  currentDueAtUtc: string,
  timeZoneId: string = UTC_TIME_ZONE
): string {
  const dateMatch = /^(\d{4})-(\d{2})-(\d{2})$/.exec(nextAsOfDate.trim());
  const cronFields = cronExpression.trim().split(/\s+/);
  const minute = Number.parseInt(cronFields[0] ?? "", 10);
  const hour = Number.parseInt(cronFields[1] ?? "", 10);
  if (!dateMatch || !Number.isInteger(minute) || minute < 0 || minute > 59 || !Number.isInteger(hour) || hour < 0 || hour > 23) {
    return currentDueAtUtc;
  }

  const dueDay = new Date(Date.UTC(
    Number.parseInt(dateMatch[1], 10),
    Number.parseInt(dateMatch[2], 10) - 1,
    Number.parseInt(dateMatch[3], 10) + parseDueDays(dueDaysAfterPeriodEnd)
  ));
  return zonedWallTimeToUtc(
    dueDay.getUTCFullYear(),
    dueDay.getUTCMonth(),
    dueDay.getUTCDate(),
    hour,
    minute,
    timeZoneId
  ).toISOString().replace(/\.\d{3}Z$/, "Z");
}

const reportingSchedulePresetDayMatchers: Record<string, (localDay: Date) => boolean> = {
  "0 8 * * *": () => true,
  "0 8 * * 1-5": (localDay) => localDay.getUTCDay() !== 0 && localDay.getUTCDay() !== 6,
  "0 8 * * 1": (localDay) => localDay.getUTCDay() === 1,
  "0 8 1,15 * *": (localDay) => localDay.getUTCDate() === 1 || localDay.getUTCDate() === 15,
  "0 8 1 * *": (localDay) => localDay.getUTCDate() === 1,
  "0 8 L * *": (localDay) =>
    localDay.getUTCDate() === new Date(Date.UTC(localDay.getUTCFullYear(), localDay.getUTCMonth() + 1, 0)).getUTCDate(),
  "0 8 1 1,4,7,10 *": (localDay) => localDay.getUTCDate() === 1 && localDay.getUTCMonth() % 3 === 0,
  "0 8 1 1 *": (localDay) => localDay.getUTCDate() === 1 && localDay.getUTCMonth() === 0
};
