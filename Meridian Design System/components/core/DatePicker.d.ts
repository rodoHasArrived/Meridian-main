import type React from "react";
/** Civil-date calendar. Enter/Space/ArrowDown opens; arrows, Home/End and PageUp/PageDown
 * navigate dates; Escape closes the calendar and restores its named trigger. */
export interface DatePickerProps extends Omit<React.ButtonHTMLAttributes<HTMLButtonElement>, "value" | "onChange" | "children"> {
  label?: React.ReactNode;
  /** Selected civil date (`YYYY-MM-DD`); never converted through a local timestamp. */
  value?: string;
  onChange?: (value: string) => void;
}
export declare function DatePicker(props: DatePickerProps): React.ReactElement;
