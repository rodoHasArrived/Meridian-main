/**
 * LocalDate — the calendar-date carrier for the workstation, the dashboard-native port of the
 * Meridian Design System `LocalDate` authority (`components/core/LocalDate.jsx` + `localDate.js`).
 *
 * The rule it owns: a trade, settlement, posting, valuation, effective or accounting date is a
 * DATE, not an instant. It is carried as a "YYYY-MM-DD" string (or a `Date` at LOCAL midnight while
 * arithmetic is being done on it) and is never produced by serialising an instant to UTC text and
 * cutting the day off the front — that moves the day for every operator off Greenwich.
 *
 * Two day buckets exist for a genuine instant, and the call site picks one on purpose:
 * - `dayOfInstant` is the operator's own calendar day, and is the default;
 * - `utcDayOfInstant` is the UTC calendar day, only for sources defined on the UTC calendar. Say
 *   so where it is called. `utcToday` below is the named form of that choice for "today" defaults.
 *
 * Coexistence: `@/lib/reporting-periods` keeps the zone-free string math for the reporting period
 * switcher (`parseIsoDate`, `shiftIsoDate`, `compareIsoDate`, `todayIsoDate`), and `@/lib/format`
 * stays the only module that calls Intl — the two display helpers here go through it. Instants are
 * not this module's job: format those with `formatUtcTimestamp` / `formatUtcDate`.
 */

import { formatDateTime } from "@/lib/format";

/** A genuine instant: a `Date`, epoch milliseconds, or an ISO datetime string. */
export type LocalDateInstant = Date | number | string;

/** One desk range as an inclusive pair of "YYYY-MM-DD" days. */
export interface LocalDateRangePreset {
  id: string;
  label: string;
  start: string;
  end: string;
}

const ISO_DAY_PATTERN = /^(\d{4})-(\d{2})-(\d{2})$/;

/** Two-digit zero padding for a month or day number. */
export function pad(n: number): string {
  return String(n).padStart(2, "0");
}

function padYear(year: number): string {
  // The package leaves the year unpadded; padding to four digits is identical for every year from
  // 1000 on and keeps the output a valid `dateTime` value for the first millennium as well.
  return String(year).padStart(4, "0");
}

/** Local calendar date → "YYYY-MM-DD", read from the local fields so no timezone shift can occur. */
export function toISO(d: Date): string {
  return `${padYear(d.getFullYear())}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

/**
 * "YYYY-MM-DD" → `Date` at LOCAL midnight, or null when the text is not exactly that shape or is
 * not a real calendar date (2026-02-30). Never falls back to `new Date(text)`, which would read a
 * date-only string as UTC midnight.
 */
export function fromISO(s: string | null | undefined): Date | null {
  if (typeof s !== "string") {
    return null;
  }
  const match = ISO_DAY_PATTERN.exec(s.trim());
  if (!match) {
    return null;
  }
  const year = Number(match[1]);
  const month = Number(match[2]);
  const day = Number(match[3]);
  const date = new Date(year, month - 1, day);
  return date.getFullYear() === year && date.getMonth() === month - 1 && date.getDate() === day ? date : null;
}

/** The date part an ISO date or datetime string opens with: "2026-06-30", "2026-06-30T02:30:00Z". */
const ISO_LEADING_DAY_PATTERN = /^(\d{4})-(\d{2})-(\d{2})(?:$|[T\s])/;

/** Whether year-month-day names a day on the calendar. Plain arithmetic: no `Date`, so no zone. */
function isCalendarDay(year: number, month: number, day: number): boolean {
  if (month < 1 || month > 12 || day < 1) {
    return false;
  }
  const leapYear = (year % 4 === 0 && year % 100 !== 0) || year % 400 === 0;
  const daysInMonth = month === 2 ? (leapYear ? 29 : 28) : [4, 6, 9, 11].includes(month) ? 30 : 31;
  return day <= daysInMonth;
}

function toInstant(v: LocalDateInstant): Date | null {
  if (typeof v === "string") {
    // The engine parser accepts any day up to 31 and rolls the surplus into the next month
    // ("2026-02-30T10:00:00Z" reads as 2 March). A date part that is not on the calendar is not an
    // instant — the same rule fromISO and parseLoose hold: null, never a different day.
    const leadingDay = ISO_LEADING_DAY_PATTERN.exec(v.trim());
    if (leadingDay && !isCalendarDay(Number(leadingDay[1]), Number(leadingDay[2]), Number(leadingDay[3]))) {
      return null;
    }
  }
  const date = v instanceof Date ? v : new Date(v);
  return Number.isNaN(date.getTime()) ? null : date;
}

/**
 * Local day bucket of an instant — the operator's day, not the UTC day. Null when the value is not
 * a valid instant.
 *
 * A string that is already a calendar date ("YYYY-MM-DD") is that day or nothing: it is returned
 * as the same day when it is a real one and is null when it is not (2026-02-30). It never reaches
 * `new Date`, which would read it as UTC midnight and re-bucket it — a day early west of
 * Greenwich — and would roll an impossible day into the next month. The package passes such a
 * string straight to `new Date`; this is the safe superset. An ISO datetime whose date part is
 * impossible is null as well.
 */
export function dayOfInstant(v: LocalDateInstant): string | null {
  if (typeof v === "string" && ISO_DAY_PATTERN.test(v.trim())) {
    const calendarDate = fromISO(v);
    return calendarDate ? toISO(calendarDate) : null;
  }
  const date = toInstant(v);
  return date ? toISO(date) : null;
}

/**
 * UTC day bucket of an instant — only for sources defined on the UTC calendar (provider sessions
 * stamped in UTC, server-evaluated effective windows). State that at the call site; the default
 * bucket is the operator's day (`dayOfInstant`). Null when the value is not a valid instant,
 * which includes an ISO date or datetime string whose date part is not on the calendar.
 */
export function utcDayOfInstant(v: LocalDateInstant): string | null {
  const date = toInstant(v);
  return date ? `${padYear(date.getUTCFullYear())}-${pad(date.getUTCMonth() + 1)}-${pad(date.getUTCDate())}` : null;
}

/** Calendar-day arithmetic (`setDate`), so a daylight-saving change never costs or gains a day. */
export function addDays(d: Date, n: number): Date {
  const next = new Date(d);
  next.setDate(next.getDate() + n);
  return next;
}

/** Month arithmetic that clamps the day — Jan 31 + 1 month is Feb 28/29, not Mar 3. */
export function addMonths(d: Date, n: number): Date {
  const next = new Date(d.getFullYear(), d.getMonth() + n, 1);
  next.setDate(Math.min(d.getDate(), new Date(next.getFullYear(), next.getMonth() + 1, 0).getDate()));
  return next;
}

/** Whether two dates fall on the same local calendar day; false when either is absent. */
export function sameDay(a: Date | null | undefined, b: Date | null | undefined): boolean {
  return Boolean(a) && Boolean(b) && toISO(a as Date) === toISO(b as Date);
}

/** Clamps a date into an optional [min, max] window. */
export function clampDate(d: Date, min?: Date | null, max?: Date | null): Date {
  if (min && d < min) {
    return min;
  }
  if (max && d > max) {
    return max;
  }
  return d;
}

/**
 * Operators paste dates as often as they pick them: ISO, "26 Aug 2026", "8/26/2026" (month first).
 * An invalid date is never rolled over into a different day — it is null.
 */
export function parseLoose(raw: string | null | undefined): Date | null {
  const text = String(raw ?? "").trim();
  if (!text) {
    return null;
  }
  const iso = fromISO(text);
  if (iso) {
    return iso;
  }
  // An ISO-shaped value that fromISO refused (2026-02-30) is invalid — never let the looser
  // parsers below roll it over into a different day.
  if (/^\d{4}-\d{1,2}-\d{1,2}/.test(text)) {
    return null;
  }
  const slash = /^(\d{1,2})[/.-](\d{1,2})[/.-](\d{4})$/.exec(text);
  if (slash) {
    const year = Number(slash[3]);
    const month = Number(slash[1]);
    const day = Number(slash[2]);
    const date = new Date(year, month - 1, day);
    // Round-trip check: 2/30/2026 or 13/01/2026 must not silently become a different date.
    return date.getFullYear() === year && date.getMonth() === month - 1 && date.getDate() === day ? date : null;
  }
  const any = new Date(text);
  if (Number.isNaN(any.getTime())) {
    return null;
  }
  // "30 Feb 2026" parses with overflow; the day the operator typed must survive the parse.
  const typedDay = /(?:^|\D)(\d{1,2})(?!\d)/.exec(text);
  if (typedDay && Number(typedDay[1]) !== any.getDate()) {
    return null;
  }
  return new Date(any.getFullYear(), any.getMonth(), any.getDate());
}

/** Two-letter weekday headers, Sunday first. */
export const DOW: readonly string[] = ["Su", "Mo", "Tu", "We", "Th", "Fr", "Sa"];

/** Weekday headers rotated so the week starts on `weekStartsOn` (0 = Sunday). */
export function dowLabels(weekStartsOn = 0): string[] {
  return DOW.slice(weekStartsOn).concat(DOW.slice(0, weekStartsOn));
}

/**
 * The local calendar fields of `d` restated as that same day at UTC midnight, for the display
 * formatter only. A formatter is bound to one zone when it is built and `formatDateTime` caches
 * them, so formatting the local-midnight carrier directly would depend on which zone the cached
 * formatter happened to capture. Copying year, month and day across and formatting in UTC makes
 * the printed day a function of the calendar fields alone.
 */
function calendarFieldsAtUtcMidnight(d: Date): Date {
  const utc = new Date(0);
  utc.setUTCFullYear(d.getFullYear(), d.getMonth(), d.getDate());
  return utc;
}

/**
 * A local calendar date spelled out with its weekday, e.g. "Tuesday, June 30, 2026". The package
 * formats in the browser's own locale; the workstation pins one locale for every operator, so
 * this goes through `formatDateTime`.
 */
export function longDate(d: Date): string {
  return formatDateTime(calendarFieldsAtUtcMidnight(d), { weekday: "long", year: "numeric", month: "long", day: "numeric", timeZone: "UTC" });
}

/** A calendar month title, e.g. "June 2026". A month on the calendar, not a reporting-period label. */
export function monthTitle(d: Date): string {
  return formatDateTime(calendarFieldsAtUtcMidnight(d), { year: "numeric", month: "long", timeZone: "UTC" });
}

/**
 * Whole weeks covering the month of `cursor` — four to six rows of seven days, not a blind 42 cells.
 *
 * The grid is laid out by counting calendar days, and each cell is built from its own year, month
 * and day. The package walks one `Date` forward and stops when that instant passes the last day's
 * local midnight; where the daylight-saving gap falls AT local midnight (Havana, Cairo, Santiago)
 * the walking date picks up the 01:00 wall time on the changeover day and keeps it, so a last day
 * of the month that opens a week row compared as "after" its own midnight and the row was dropped.
 * Counting days has no instant to compare, and building each cell afresh leaves every day at its
 * local midnight wherever that exists.
 */
export function weeksFor(cursor: Date, weekStartsOn = 0): Date[][] {
  const year = cursor.getFullYear();
  const month = cursor.getMonth();
  const leadingDays = (new Date(year, month, 1).getDay() - weekStartsOn + 7) % 7;
  const daysInMonth = new Date(year, month + 1, 0).getDate();
  const weekCount = Math.ceil((leadingDays + daysInMonth) / 7);
  const weeks: Date[][] = [];
  for (let week = 0; week < weekCount; week += 1) {
    const row: Date[] = [];
    for (let index = 0; index < 7; index += 1) {
      row.push(new Date(year, month, 1 - leadingDays + week * 7 + index));
    }
    weeks.push(row);
  }
  return weeks;
}

/** Common desk ranges as inclusive ISO day pairs, on the operator's calendar. `today` is injectable. */
export function rangePresets(today: Date = new Date()): LocalDateRangePreset[] {
  const day = new Date(today.getFullYear(), today.getMonth(), today.getDate());
  const quarterStartMonth = Math.floor(day.getMonth() / 3) * 3;
  const end = toISO(day);
  return [
    { id: "today", label: "Today", start: end, end },
    { id: "7d", label: "Last 7d", start: toISO(addDays(day, -6)), end },
    { id: "30d", label: "Last 30d", start: toISO(addDays(day, -29)), end },
    { id: "mtd", label: "MTD", start: toISO(new Date(day.getFullYear(), day.getMonth(), 1)), end },
    { id: "qtd", label: "QTD", start: toISO(new Date(day.getFullYear(), quarterStartMonth, 1)), end },
    { id: "ytd", label: "YTD", start: toISO(new Date(day.getFullYear(), 0, 1)), end }
  ];
}

/**
 * The calendar-date authority as one object, mirroring the design-system `LocalDate` surface
 * member for member: `LocalDate.toISO(LocalDate.addDays(LocalDate.fromISO(day), 3))`.
 */
export const LocalDate = {
  toISO,
  fromISO,
  dayOfInstant,
  utcDayOfInstant,
  addDays,
  addMonths,
  sameDay,
  clampDate,
  parseLoose,
  longDate,
  monthTitle,
  weeksFor,
  rangePresets,
  dowLabels,
  pad
} as const;

/**
 * Today on the UTC calendar, as "YYYY-MM-DD" — a workstation addition, not part of the package
 * surface. It names the one deliberate use the workstation has for `utcDayOfInstant`: defaults and
 * windows the server evaluates on its UTC date (report as-of defaults, asset-profile effective
 * dates). Using it is the statement that the day is UTC; an operator-entered "today" belongs to
 * `dayOfInstant(Date.now())` instead. Same day as `todayIsoDate` in `@/lib/reporting-periods`.
 *
 * Throws a RangeError for an invalid `now`, as serialising an invalid Date always has.
 */
export function utcToday(now: Date = new Date()): string {
  const day = utcDayOfInstant(now);
  if (day === null) {
    throw new RangeError("Invalid time value");
  }
  return day;
}
