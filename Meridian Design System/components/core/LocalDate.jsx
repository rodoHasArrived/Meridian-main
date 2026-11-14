// LocalDate — the calendar-date authority on the namespace, the sibling of `Format`.
// `core/localDate.js` has owned the rule since 4.1 (a trade, settlement or accounting date is a
// date, not a moment; never route it through toISOString()), but only components could import it.
// Templates had nothing to call, so C-02 kept finding the UTC-slice of an instant in screens.
// `const { LocalDate } = window.<Namespace>; LocalDate.dayOfInstant(ts)`.
import React from "react";
import * as L from "./localDate.js";

export const LocalDate = {
  toISO: L.toISO, fromISO: L.fromISO, dayOfInstant: L.dayOfInstant,
  addDays: L.addDays, addMonths: L.addMonths, sameDay: L.sameDay, clampDate: L.clampDate,
  parseLoose: L.parseLoose, longDate: L.longDate, monthTitle: L.monthTitle, weeksFor: L.weeksFor,
  rangePresets: L.rangePresets, dowLabels: L.dowLabels, pad: L.pad,
  /** UTC day bucket for an instant — for sources that are defined on the UTC calendar (exchange
   *  sessions stamped in UTC). Say so at the call site; the default is the operator's day. */
  utcDayOfInstant: (v) => { const d = v instanceof Date ? v : new Date(v); return isNaN(d.getTime()) ? null : `${d.getUTCFullYear()}-${L.pad(d.getUTCMonth() + 1)}-${L.pad(d.getUTCDate())}`; },
};

/** Renders a calendar date as text. `value` is "YYYY-MM-DD" (or a Date); never an instant. */
export function LocalDateText({ value, format = "iso", ...rest }) {
  const d = value instanceof Date ? value : L.fromISO(value);
  if (!d) return <span {...rest}>—</span>;
  const text = format === "long" ? L.longDate(d) : format === "month" ? L.monthTitle(d) : L.toISO(d);
  return <time dateTime={L.toISO(d)} {...rest}>{text}</time>;
}
