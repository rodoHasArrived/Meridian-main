import { type AriaAttributes, type ReactNode } from "react";
import { CalendarField } from "@/components/ui/date-field";

export interface DateRange {
  start: string | null;
  end: string | null;
}

export interface DateRangePickerProps extends AriaAttributes {
  start?: string | null;
  end?: string | null;
  onChange?: (range: DateRange) => void;
  label?: ReactNode;
  disabled?: boolean;
  className?: string;
  id?: string;
}

/** ISO start/end field sharing the native civil-date calendar behavior. */
export function DateRangePicker(props: DateRangePickerProps) {
  return <CalendarField {...props} range />;
}
