/** Single date input with a calendar popover — click the field to open, click a day to select and close. */
export interface DatePickerProps {
  /** Small-caps label rendered above the field. */
  label?: string;
  /** Selected date as an ISO string (`YYYY-MM-DD`). */
  value?: string;
  /** Fires with the new ISO date string when a day is picked. */
  onChange?: (value: string) => void;
}
export declare function DatePicker(props: DatePickerProps): JSX.Element;
