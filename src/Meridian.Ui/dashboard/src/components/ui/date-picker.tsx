import { type AriaAttributes, type ReactNode } from "react";
import { CalendarField } from "@/components/ui/date-field";

export interface DatePickerProps extends AriaAttributes {
  value?: string | null;
  onChange?: (value: string) => void;
  label?: ReactNode;
  placeholder?: string;
  disabled?: boolean;
  className?: string;
  id?: string;
}

export function toIsoDate(date: Date): string {
  const year = date.getFullYear();
  const month = String(date.getMonth() + 1).padStart(2, "0");
  const day = String(date.getDate()).padStart(2, "0");
  return `${year}-${month}-${day}`;
}

export function monthGrid(month: Date): Date[] {
  const first = new Date(month.getFullYear(), month.getMonth(), 1);
  const start = new Date(first);
  start.setDate(start.getDate() - first.getDay());
  const days: Date[] = [];
  for (let i = 0; i < 42; i += 1) {
    const day = new Date(start);
    day.setDate(day.getDate() + i);
    days.push(day);
  }
  return days;
}

/** Date-only field with a keyboard-operable, labelled calendar popover. */
export function DatePicker(props: DatePickerProps) {
  return <CalendarField {...props} />;
}
