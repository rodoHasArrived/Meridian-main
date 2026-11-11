import React from "react";

export interface DatePickerProps extends Omit<React.HTMLAttributes<HTMLDivElement>, "onChange"> {
  /** Small-caps label above the field; associated with the input */
  label?: string;
  /** Selected date as a local calendar date, `YYYY-MM-DD` */
  value?: string;
  /** Fires with the new `YYYY-MM-DD`, or `""` when cleared */
  onChange?: (value: string) => void;
  /** Earliest selectable date, `YYYY-MM-DD` */
  min?: string;
  /** Latest selectable date, `YYYY-MM-DD` */
  max?: string;
  /** Block individual dates — holidays, non-business days, closed periods */
  disabledDate?: (iso: string, date: Date) => boolean;
  disabled?: boolean;
  required?: boolean;
  /** Error message — renders below, sets `aria-invalid`, reddens the border */
  error?: React.ReactNode;
  /** Show the ✕ and the Clear button. @default true */
  clearable?: boolean;
  /** Show the Today button. @default true */
  showToday?: boolean;
  /** 0 = Sunday, 1 = Monday. @default 0 */
  weekStartsOn?: 0 | 1;
  /** @default "YYYY-MM-DD" */
  placeholder?: string;
  id?: string;
  name?: string;
}

/**
 * Single date field with a calendar popover. **Dates are local calendar dates, never instants** —
 * an ISO `YYYY-MM-DD` goes in and the same string comes out with no timezone round-trip.
 *
 * The field is typeable (ISO, `26 Aug 2026`, or `8/26/2026`; an unparseable entry snaps back to the
 * last good value). The calendar is an ARIA grid: arrows move a day, PageUp/PageDown a month,
 * Shift+PageUp/PageDown a year, Home/End the week, Enter/Space select, Escape closes and returns
 * focus to the field. The popover flips when it would run off the viewport.
 *
 * @example
 * <DatePicker
 *   label="Settlement date"
 *   value={settle}
 *   onChange={setSettle}
 *   min={tradeDate}
 *   disabledDate={(iso, d) => d.getDay() === 0 || d.getDay() === 6 || holidays.has(iso)}
 *   required
 * />
 */
export declare function DatePicker(props: DatePickerProps): JSX.Element;
