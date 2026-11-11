import React from "react";

export interface DateRangeValue {
  /** `YYYY-MM-DD`, or null when cleared */
  start: string | null;
  /** `YYYY-MM-DD`, or null while only the start has been picked */
  end: string | null;
}

export interface DateRangePreset {
  id: string;
  label: string;
  start: string;
  end: string;
}

export interface DateRangePickerProps extends Omit<React.HTMLAttributes<HTMLDivElement>, "onChange" | "start" | "end"> {
  /** Small-caps label above the field */
  label?: string;
  /** Range start, `YYYY-MM-DD` */
  start?: string | null;
  /** Range end, `YYYY-MM-DD` */
  end?: string | null;
  /** Fires with the whole range on every change; `end` is null between the two clicks */
  onChange?: (range: DateRangeValue) => void;
  min?: string;
  max?: string;
  /** Block individual dates — weekends, holidays, closed periods */
  disabledDate?: (iso: string, date: Date) => boolean;
  /** Replace the built-in presets (Today, Last 7d/30d, MTD, QTD, YTD) */
  presets?: DateRangePreset[];
  /** @default true */
  showPresets?: boolean;
  /** Months shown side by side. @default 2 */
  months?: number;
  /** 0 = Sunday, 1 = Monday. @default 0 */
  weekStartsOn?: 0 | 1;
  disabled?: boolean;
  error?: React.ReactNode;
  /** @default true */
  clearable?: boolean;
  id?: string;
}

/**
 * Start/end date selection over two months, with preset ranges beside the calendar. **Local
 * calendar dates throughout** — ISO `YYYY-MM-DD` in and out, no timezone round-trip.
 *
 * Two clicks make a range; the span shades live from the anchor as the pointer (or keyboard) moves,
 * and clicking a completed range starts a new one. Full ARIA grid keyboard model: arrows, PageUp/
 * PageDown for months, Shift+PageUp/PageDown for years, Home/End, Enter to pick, Escape to close.
 *
 * @example
 * <DateRangePicker
 *   label="Period"
 *   start={range.start}
 *   end={range.end}
 *   onChange={setRange}
 *   max={today}
 *   disabledDate={(iso, d) => d.getDay() === 0 || d.getDay() === 6}
 * />
 */
export declare function DateRangePicker(props: DateRangePickerProps): JSX.Element;
