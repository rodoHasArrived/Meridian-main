import { type ReactNode, useEffect, useRef, useState } from "react";
import { cn } from "@/lib/utils";

export interface DatePickerProps {
  value?: string | null;
  onChange?: (value: string) => void;
  label?: ReactNode;
  placeholder?: string;
  disabled?: boolean;
  className?: string;
  id?: string;
}

const DOW = ["Su", "Mo", "Tu", "We", "Th", "Fr", "Sa"];

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

const dayButtonClass =
  "flex h-8 w-8 items-center justify-center rounded-[2px] border border-transparent font-mono text-xs text-foreground transition-colors hover:bg-[var(--ws-row-hover)] focus:outline-none focus-visible:ring-2 focus-visible:ring-primary/40 [outline-offset:-2px]";

/**
 * Single-date input with a flat calendar popover. Concrete: hairline field, menu shadow on the
 * calendar, 2px cells, one accent for the selected day. Emits ISO `YYYY-MM-DD` via `onChange`.
 */
export function DatePicker({ value, onChange, label, placeholder = "Select date…", disabled = false, className, id }: DatePickerProps) {
  const [open, setOpen] = useState(false);
  const [month, setMonth] = useState<Date>(value ? new Date(value) : new Date());
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

  const days = monthGrid(month);
  const display = value
    ? new Date(value).toLocaleDateString("en-US", { year: "numeric", month: "short", day: "numeric" })
    : placeholder;
  const today = toIsoDate(new Date());

  return (
    <div ref={rootRef} className={cn("relative w-full", className)}>
      {label ? (
        <label htmlFor={id} className="mb-1.5 block font-sans text-[10px] font-semibold uppercase tracking-[0.14em] text-muted-foreground">
          {label}
        </label>
      ) : null}
      <input
        id={id}
        type="text"
        readOnly
        disabled={disabled}
        value={display}
        onClick={() => !disabled && setOpen((current) => !current)}
        className={cn(
          "h-9 w-full cursor-pointer rounded-[2px] border border-border bg-[var(--surface-input)] px-2.5 font-mono text-sm text-foreground",
          "hover:border-[var(--border-hover)] focus-visible:border-primary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40",
          disabled && "cursor-not-allowed opacity-55"
        )}
      />
      {open ? (
        <div className="absolute left-0 top-[calc(100%+4px)] z-[100] rounded-[2px] border border-border bg-popover p-3 shadow-[var(--shadow-menu)]">
          <div className="mb-3 flex items-center justify-between text-xs font-semibold text-foreground">
            <button
              type="button"
              aria-label="Previous month"
              onClick={() => setMonth(new Date(month.getFullYear(), month.getMonth() - 1))}
              className="rounded-[2px] border border-border bg-card px-2 py-1 transition-colors hover:bg-[var(--ws-row-hover)]"
            >
              ←
            </button>
            <span>{month.toLocaleDateString("en-US", { year: "numeric", month: "long" })}</span>
            <button
              type="button"
              aria-label="Next month"
              onClick={() => setMonth(new Date(month.getFullYear(), month.getMonth() + 1))}
              className="rounded-[2px] border border-border bg-card px-2 py-1 transition-colors hover:bg-[var(--ws-row-hover)]"
            >
              →
            </button>
          </div>
          <div className="grid grid-cols-7 gap-0.5">
            {DOW.map((day) => (
              <div key={day} className="text-center font-mono text-[10px] font-semibold text-muted-foreground">
                {day}
              </div>
            ))}
            {days.map((day, index) => {
              const iso = toIsoDate(day);
              const other = day.getMonth() !== month.getMonth();
              const selected = value === iso;
              const isToday = iso === today;
              return (
                <button
                  key={index}
                  type="button"
                  onClick={() => {
                    onChange?.(iso);
                    setOpen(false);
                  }}
                  className={cn(
                    dayButtonClass,
                    other && "text-muted-foreground",
                    isToday && !selected && "border-primary",
                    selected && "border-primary bg-primary text-primary-foreground hover:bg-primary"
                  )}
                >
                  {day.getDate()}
                </button>
              );
            })}
          </div>
        </div>
      ) : null}
    </div>
  );
}
