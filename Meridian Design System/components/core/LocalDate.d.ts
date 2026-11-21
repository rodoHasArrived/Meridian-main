import type React from "react";
/**
 * LocalDate — the calendar-date authority on the namespace (`MeridianDesignSystem.LocalDate`),
 * the sibling of `Format`. A trade, settlement or accounting date is a DATE, not an instant:
 * never route one through `toISOString()` (it shifts the day for everyone west of Greenwich).
 *
 * @example
 * const { LocalDate } = window.MeridianDesignSystem_205d17;
 * LocalDate.dayOfInstant(Date.now())          // the operator's day, "2026-09-01"
 * LocalDate.toISO(LocalDate.addDays(d, 4))    // "2026-09-05"
 */
export declare const LocalDate: {
  /** Local Date → "YYYY-MM-DD", no timezone shift. */
  toISO(d: Date): string;
  /** "YYYY-MM-DD" → local midnight Date, or null when not a real date. */
  fromISO(s: string): Date | null;
  /** Local day bucket of an instant — the operator's day. */
  dayOfInstant(v: Date | number | string): string | null;
  /** UTC day bucket of an instant — only for sources defined on the UTC calendar; say so at the call site. */
  utcDayOfInstant(v: Date | number | string): string | null;
  addDays(d: Date, n: number): Date;
  /** Clamps the day — Jan 31 + 1 month is Feb 28/29. */
  addMonths(d: Date, n: number): Date;
  sameDay(a: Date | null, b: Date | null): boolean;
  clampDate(d: Date, min?: Date | null, max?: Date | null): Date;
  /** ISO, "26 Aug 2026", "8/26/2026" → local Date. */
  parseLoose(raw: string): Date | null;
  longDate(d: Date): string;
  monthTitle(d: Date): string;
  weeksFor(cursor: Date, weekStartsOn?: number): Date[][];
  rangePresets(today?: Date): Array<{ id: string; label: string; start: string; end: string }>;
  dowLabels(weekStartsOn?: number): string[];
  pad(n: number): string;
};

export interface LocalDateTextProps extends React.HTMLAttributes<HTMLElement> {
  /** "YYYY-MM-DD" or a local Date. */
  value: string | Date;
  /** @default "iso" */
  format?: "iso" | "long" | "month";
}
/** A calendar date as a `<time>` element. */
export declare function LocalDateText(props: LocalDateTextProps): React.JSX.Element;
