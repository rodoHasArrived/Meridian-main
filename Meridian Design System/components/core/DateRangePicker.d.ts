import type React from "react";
export interface DateRangeValue { start: string | null; end: string | null; }
/** Accessible two-month calendar. First selection starts a new range; the second
 * completes an ordered range and returns focus to the trigger. */
export interface DateRangePickerProps extends Omit<React.ButtonHTMLAttributes<HTMLButtonElement>, "value" | "onChange" | "children"> {
  label?: React.ReactNode;
  start?: string | null;
  end?: string | null;
  onChange: (range: DateRangeValue) => void;
}
export declare function DateRangePicker(props: DateRangePickerProps): React.ReactElement;
