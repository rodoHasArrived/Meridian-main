import { type ReactNode, useEffect, useRef, useState } from "react";
import { monthGrid, toIsoDate } from "@/components/ui/date-picker";
import { cn } from "@/lib/utils";

export interface DateRange {
  start: string | null;
  end: string | null;
}

export interface DateRangePickerProps {
  start?: string | null;
  end?: string | null;
  onChange?: (range: DateRange) => void;
  label?: ReactNode;
  disabled?: boolean;
  className?: string;
}

const DOW = ["Su", "Mo", "Tu", "We", "Th", "Fr", "Sa"];

const dayButtonClass =
  "flex h-8 w-8 items-center justify-center border border-transparent font-mono text-xs text-foreground transition-colors hover:bg-[#EAEEF3] focus:outline-none focus-visible:ring-2 focus-visible:ring-primary/40 [outline-offset:-2px]";

function shortLabel(value: string | null | undefined, fallback: string): string {
  return value ? new Date(value).toLocaleDateString("en-US", { month: "short", day: "numeric" }) : fallback;
}

/**
 * Start/end date range selection across two months. Concrete: hairline field, menu-shadowed
 * dual calendar, accent endpoints with an alpha-10 in-range wash. Emits `{ start, end }` ISO
 * dates via `onChange`.
 */
export function DateRangePicker({ start, end, onChange, label, disabled = false, className }: DateRangePickerProps) {
  const [open, setOpen] = useState(false);
  const [month, setMonth] = useState<Date>(start ? new Date(start) : new Date());
  const rootRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (typeof document === "undefined") {
      return undefined;
    }
    const handler = (event: MouseEvent) => {
      if (rootRef.current && !rootRef.current.contains(event.target as Node)) {
        setOpen(false);
      }
    };
    document.addEventListener("mousedown", handler);
    return () => document.removeEventListener("mousedown", handler);
  }, []);

  const handleDayClick = (day: Date) => {
    const iso = toIsoDate(day);
    if (!start || (start && end)) {
      onChange?.({ start: iso, end: null });
    } else if (iso < start) {
      onChange?.({ start: iso, end: start });
    } else {
      onChange?.({ start, end: iso });
      setOpen(false);
    }
  };

  const inRange = (day: Date) => {
    const iso = toIsoDate(day);
    return Boolean(start && end && iso >= start && iso <= end);
  };

  const renderMonth = (base: Date, offset: number) => {
    const monthDate = new Date(base.getFullYear(), base.getMonth() + offset, 1);
    return (
      <div className="w-[16rem]">
        <div className="mb-3 flex items-center justify-between text-xs font-semibold text-foreground">
          {offset === 0 ? (
            <button
              type="button"
              aria-label="Previous month"
              onClick={() => setMonth(new Date(base.getFullYear(), base.getMonth() - 1))}
              className="rounded-[2px] border border-border bg-card px-2 py-1 transition-colors hover:bg-[#EAEEF3]"
            >
              ←
            </button>
          ) : (
            <span />
          )}
          <span>{monthDate.toLocaleDateString("en-US", { year: "numeric", month: "long" })}</span>
          {offset === 1 ? (
            <button
              type="button"
              aria-label="Next month"
              onClick={() => setMonth(new Date(base.getFullYear(), base.getMonth() + 1))}
              className="rounded-[2px] border border-border bg-card px-2 py-1 transition-colors hover:bg-[#EAEEF3]"
            >
              →
            </button>
          ) : (
            <span />
          )}
        </div>
        <div className="grid grid-cols-7 gap-0.5">
          {DOW.map((day) => (
            <div key={day} className="text-center font-mono text-[10px] font-semibold text-muted-foreground">
              {day}
            </div>
          ))}
          {monthGrid(monthDate).map((day, index) => {
            const iso = toIsoDate(day);
            const isStart = start === iso;
            const isEnd = end === iso;
            const within = inRange(day);
            return (
              <button
                key={index}
                type="button"
                onClick={() => handleDayClick(day)}
                className={cn(
                  dayButtonClass,
                  "rounded-[2px]",
                  within && "bg-primary/10",
                  (isStart || isEnd) && "border-primary bg-primary text-primary-foreground hover:bg-primary"
                )}
              >
                {day.getDate()}
              </button>
            );
          })}
        </div>
      </div>
    );
  };

  return (
    <div ref={rootRef} className={cn("relative w-full", className)}>
      {label ? (
        <span className="mb-1.5 block font-mono text-[10px] font-semibold uppercase tracking-[0.14em] text-muted-foreground">
          {label}
        </span>
      ) : null}
      <input
        type="text"
        readOnly
        disabled={disabled}
        value={`${shortLabel(start, "Start")} — ${shortLabel(end, "End")}`}
        onClick={() => !disabled && setOpen((current) => !current)}
        className={cn(
          "h-9 w-full cursor-pointer rounded-[2px] border border-border bg-[#F3F6F9] px-2.5 font-mono text-sm text-foreground",
          "hover:border-[#ADB8C4] focus-visible:border-primary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40",
          disabled && "cursor-not-allowed opacity-55"
        )}
      />
      {open ? (
        <div className="absolute left-0 top-[calc(100%+4px)] z-[100] flex gap-4 rounded-[2px] border border-border bg-popover p-3 shadow-[0_2px_6px_rgba(0,0,0,0.18)]">
          {renderMonth(month, 0)}
          {renderMonth(month, 1)}
        </div>
      ) : null}
    </div>
  );
}
