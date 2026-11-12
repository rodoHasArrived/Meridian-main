import type { HTMLAttributes, ReactNode } from "react";
import { LocalDate } from "@/lib/local-date";

export type LocalDateTextFormat = "iso" | "long" | "month";

export interface LocalDateTextProps extends Omit<HTMLAttributes<HTMLElement>, "children"> {
  /** A calendar date: "YYYY-MM-DD" or a `Date` at local midnight. Never an instant. */
  value: string | Date | null | undefined;
  /** "iso" renders 2026-06-30, "long" Tuesday, June 30, 2026, "month" June 2026. Defaults to "iso". */
  format?: LocalDateTextFormat;
  /**
   * What to render when `value` is not a real calendar date. Defaults to an em dash, as the design
   * system does. A screen that used to print a server label verbatim passes that label here, so a
   * sentinel such as "Not supplied" keeps its exact text.
   */
  fallback?: ReactNode;
}

const ABSENT_DATE = "—";

/**
 * A calendar date as text. A real date renders as `<time dateTime="YYYY-MM-DD">`, so the machine
 * day is exposed whatever the visible format; anything else renders the fallback in a plain
 * `<span>` with no `dateTime`. The component names no role — the label or column header beside it
 * says whether this is an ex-date, a period end or a due date.
 *
 * Dashboard-native port of the design-system `LocalDateText`. Use it for days; an instant
 * (`…AtUtc`) is not a date and belongs to the UTC timestamp formatters in `@/lib/format`.
 */
export function LocalDateText({ value, format = "iso", fallback = ABSENT_DATE, ...rest }: LocalDateTextProps) {
  const date = value instanceof Date ? (Number.isNaN(value.getTime()) ? null : value) : LocalDate.fromISO(value);
  if (!date) {
    return <span {...rest}>{fallback}</span>;
  }

  const isoDay = LocalDate.toISO(date);
  const text = format === "long" ? LocalDate.longDate(date) : format === "month" ? LocalDate.monthTitle(date) : isoDay;
  return <time {...rest} dateTime={isoDay}>{text}</time>;
}
