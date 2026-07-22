/** Start/end date range picker — two-month calendar popover; first click sets the start (and clears
 * any prior end), second click sets the end and closes. Dates between start and end wash blue. */
export interface DateRangeValue {
  start: string | null;
  end: string | null;
}
export interface DateRangePickerProps {
  /** Small-caps label rendered above the field. */
  label?: string;
  /** Selected range start, ISO string (`YYYY-MM-DD`) or null. */
  start?: string | null;
  /** Selected range end, ISO string (`YYYY-MM-DD`) or null. */
  end?: string | null;
  /** Fires with `{ start, end }` on every pick. */
  onChange: (range: DateRangeValue) => void;
}
export declare function DateRangePicker(props: DateRangePickerProps): JSX.Element;
