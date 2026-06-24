/**
 * Pure period math for the reporting "as of" period switcher.
 *
 * All functions operate on ISO calendar dates (yyyy-mm-dd) in UTC so that period
 * shifts are deterministic and free of local-timezone drift. Month/quarter/year
 * shifts clamp the day to the target month length (e.g. Mar 31 - 1 month = Feb 28/29),
 * matching how finance users expect period-end as-of dates to behave.
 */

export type ReportingPeriodUnit = "day" | "month" | "quarter" | "year";

export interface ReportingPeriodOption {
  id: string;
  label: string;
  asOfDate: string;
  description: string;
}

const ISO_DATE_PATTERN = /^\d{4}-\d{2}-\d{2}$/;

const PERIOD_LABEL_FORMATTER = new Intl.DateTimeFormat("en-US", {
  year: "numeric",
  month: "short",
  day: "numeric",
  timeZone: "UTC"
});

function pad2(value: number): string {
  return value.toString().padStart(2, "0");
}

function pad4(value: number): string {
  return value.toString().padStart(4, "0");
}

function daysInMonth(year: number, month: number): number {
  // month is 1-based; day 0 of the next month is the last day of this month.
  return new Date(Date.UTC(year, month, 0)).getUTCDate();
}

function toIsoDate(year: number, month: number, day: number): string {
  return `${pad4(year)}-${pad2(month)}-${pad2(day)}`;
}

export interface ParsedIsoDate {
  year: number;
  month: number;
  day: number;
}

export function parseIsoDate(value: string): ParsedIsoDate | null {
  if (typeof value !== "string" || !ISO_DATE_PATTERN.test(value)) {
    return null;
  }

  const [year, month, day] = value.split("-").map((part) => Number.parseInt(part, 10));
  if (month < 1 || month > 12) {
    return null;
  }

  if (day < 1 || day > daysInMonth(year, month)) {
    return null;
  }

  return { year, month, day };
}

export function isIsoDate(value: string): boolean {
  return parseIsoDate(value) !== null;
}

export function shiftIsoDate(anchor: string, unit: ReportingPeriodUnit, amount: number): string {
  const parsed = parseIsoDate(anchor);
  if (!parsed) {
    throw new Error(`Invalid ISO date: ${anchor}`);
  }

  const { year, month, day } = parsed;
  if (unit === "day") {
    const shifted = new Date(Date.UTC(year, month - 1, day + amount));
    return toIsoDate(shifted.getUTCFullYear(), shifted.getUTCMonth() + 1, shifted.getUTCDate());
  }

  const monthDelta = unit === "month" ? amount : unit === "quarter" ? amount * 3 : amount * 12;
  const absoluteMonth = year * 12 + (month - 1) + monthDelta;
  const newYear = Math.floor(absoluteMonth / 12);
  const newMonth = (absoluteMonth % 12) + 1;
  const newDay = Math.min(day, daysInMonth(newYear, newMonth));
  return toIsoDate(newYear, newMonth, newDay);
}

export function formatReportingPeriodLabel(asOfDate: string): string {
  const parsed = parseIsoDate(asOfDate);
  if (!parsed) {
    return asOfDate;
  }

  return PERIOD_LABEL_FORMATTER.format(new Date(Date.UTC(parsed.year, parsed.month - 1, parsed.day)));
}

/**
 * Compares two ISO dates without timezone effects.
 * Returns a negative number when {@link left} is earlier, positive when later, 0 when equal.
 */
export function compareIsoDate(left: string, right: string): number {
  const a = parseIsoDate(left);
  const b = parseIsoDate(right);
  if (!a || !b) {
    return left.localeCompare(right);
  }

  return (
    a.year - b.year ||
    a.month - b.month ||
    a.day - b.day
  );
}

const PRESET_DEFINITIONS: ReadonlyArray<{ id: string; label: string; unit: ReportingPeriodUnit; amount: number }> = [
  { id: "prev-day", label: "Prev day", unit: "day", amount: -1 },
  { id: "prev-month", label: "Prev month", unit: "month", amount: -1 },
  { id: "prev-quarter", label: "Prev quarter", unit: "quarter", amount: -1 },
  { id: "prev-year", label: "Prev year", unit: "year", amount: -1 }
];

/**
 * Builds the quick-pick period options shown by the switcher, anchored on the current as-of date.
 * The first option is always the anchor itself ("Current"); the rest step backwards by day/month/quarter/year.
 */
export function buildReportingPeriodOptions(anchor: string): ReportingPeriodOption[] {
  if (!isIsoDate(anchor)) {
    return [];
  }

  const options: ReportingPeriodOption[] = [
    {
      id: "current",
      label: "Current",
      asOfDate: anchor,
      description: formatReportingPeriodLabel(anchor)
    }
  ];

  for (const preset of PRESET_DEFINITIONS) {
    const asOfDate = shiftIsoDate(anchor, preset.unit, preset.amount);
    options.push({
      id: preset.id,
      label: preset.label,
      asOfDate,
      description: formatReportingPeriodLabel(asOfDate)
    });
  }

  return options;
}

export function todayIsoDate(now: Date = new Date()): string {
  return toIsoDate(now.getUTCFullYear(), now.getUTCMonth() + 1, now.getUTCDate());
}
