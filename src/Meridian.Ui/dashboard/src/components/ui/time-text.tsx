import { useSyncExternalStore, type HTMLAttributes, type ReactNode } from "react";
import { formatRelativeTime, formatTimestamp, getDisplayTimeZone, subscribeDisplayTimeZone, type TimestampInput } from "@/lib/format";

/** The operator's display time zone, re-rendering the caller when it changes in Settings. */
export function useDisplayTimeZone(): string {
  return useSyncExternalStore(subscribeDisplayTimeZone, getDisplayTimeZone, getDisplayTimeZone);
}

export interface TimeTextProps extends Omit<HTMLAttributes<HTMLElement>, "children"> {
  /** An instant: a `Date`, epoch milliseconds, or an ISO datetime string. Not a calendar date. */
  value: TimestampInput | null | undefined;
  /** "absolute" shows "Jun 30, 2026, 18:05 UTC" with the age on hover; "relative" shows "5m ago" with the timestamp on hover. */
  display?: "absolute" | "relative";
  /** Reference instant for the relative form. Defaults to the time of render. */
  now?: TimestampInput;
  /** Rendered in a plain `<span>` when `value` is absent or not a valid instant. */
  fallback?: ReactNode;
}

/**
 * An instant as text in the operator's display time zone. A valid instant renders as
 * `<time dateTime>` (the ISO text as given, else `toISOString()`) with the other form (absolute or relative) in
 * `title`, so hovering a "5m ago" shows the exact time and hovering a timestamp shows its age.
 * Calendar dates (trade, settlement, period days) belong to `LocalDateText` instead.
 */
export function TimeText({ value, display = "absolute", now, fallback = "—", ...rest }: TimeTextProps) {
  const timeZone = useDisplayTimeZone();
  const date = value == null || value === "" ? null : value instanceof Date ? value : new Date(value);
  if (!date || Number.isNaN(date.getTime())) {
    return <span {...rest}>{fallback}</span>;
  }

  const absolute = formatTimestamp(date, { timeZone });
  const relative = formatRelativeTime(date, now ?? Date.now());
  return (
    <time title={display === "relative" ? absolute : relative} {...rest} dateTime={typeof value === "string" ? value.trim() : date.toISOString()}>
      {display === "relative" ? relative : absolute}
    </time>
  );
}
