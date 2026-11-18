import { useMemo, useState } from "react";
import { ChevronLeft, ChevronRight } from "lucide-react";
import { Link } from "react-router-dom";
import { Button } from "@/components/ui/button";
import { StatusBadge } from "@/design-system/status";
import { cn } from "@/lib/utils";

export interface MonthCalendarItem {
  id: string;
  /** The day the item is due, as YYYY-MM-DD (calendar day, no time). */
  date: string;
  label: string;
  /** A short second line, e.g. "Owner Ops Desk · 2 blockers". */
  detail?: string;
  /** Status word for the badge, e.g. "Blocked", "Ready", "In review". */
  status: string;
  href: string;
}

const WEEKDAYS = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"] as const;
const MONTHS = ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"] as const;

function isoDay(year: number, month: number, day: number): string {
  return `${year}-${String(month + 1).padStart(2, "0")}-${String(day).padStart(2, "0")}`;
}

/** The month's days in Monday-first weeks; days outside the month are null. */
export function buildMonthWeeks(year: number, month: number): (string | null)[][] {
  const firstWeekday = (new Date(Date.UTC(year, month, 1)).getUTCDay() + 6) % 7;
  const daysInMonth = new Date(Date.UTC(year, month + 1, 0)).getUTCDate();
  const cells: (string | null)[] = [
    ...Array.from({ length: firstWeekday }, () => null),
    ...Array.from({ length: daysInMonth }, (_, index) => isoDay(year, month, index + 1))
  ];
  while (cells.length % 7 !== 0) cells.push(null);
  return Array.from({ length: cells.length / 7 }, (_, week) => cells.slice(week * 7, week * 7 + 7));
}

/**
 * A month grid of dated work (UX Phase D): each day lists what is due that day, today is marked,
 * and an item due before today that is not ready reads as overdue. Monday-first weeks in calendar
 * days, so a due date never shifts with the viewer's time zone.
 */
export function MonthCalendar({
  items,
  initialMonth,
  today,
  ariaLabel,
  isDone
}: {
  items: readonly MonthCalendarItem[];
  /** YYYY-MM of the month shown first. */
  initialMonth: string;
  /** Today's calendar day, YYYY-MM-DD. */
  today: string;
  ariaLabel: string;
  /** Whether an item no longer counts as overdue once its date has passed. */
  isDone?: (item: MonthCalendarItem) => boolean;
}) {
  const [month, setMonth] = useState(initialMonth);
  const [year, monthIndex] = month.split("-").map(Number) as [number, number];
  const weeks = useMemo(() => buildMonthWeeks(year, monthIndex - 1), [year, monthIndex]);
  const byDay = useMemo(() => {
    const map = new Map<string, MonthCalendarItem[]>();
    for (const item of items) map.set(item.date, [...(map.get(item.date) ?? []), item]);
    return map;
  }, [items]);
  const shift = (delta: number) => {
    const next = new Date(Date.UTC(year, monthIndex - 1 + delta, 1));
    setMonth(`${next.getUTCFullYear()}-${String(next.getUTCMonth() + 1).padStart(2, "0")}`);
  };
  const title = `${MONTHS[monthIndex - 1]} ${year}`;
  const inMonth = items.filter((item) => item.date.startsWith(month)).length;

  return (
    <section aria-label={ariaLabel} className="month-calendar space-y-2" data-month={month}>
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h3 className="text-sm font-semibold" aria-live="polite">{title} <span className="font-normal text-muted-foreground">· {inMonth} due</span></h3>
        <div className="flex items-center gap-1">
          <Button type="button" size="sm" variant="outline" onClick={() => shift(-1)} aria-label="Previous month"><ChevronLeft className="size-4" aria-hidden="true" /></Button>
          <Button type="button" size="sm" variant="outline" onClick={() => setMonth(today.slice(0, 7))}>Today</Button>
          <Button type="button" size="sm" variant="outline" onClick={() => shift(1)} aria-label="Next month"><ChevronRight className="size-4" aria-hidden="true" /></Button>
        </div>
      </div>
      <div role="grid" aria-label={title} className="grid grid-cols-7 overflow-hidden rounded-md border border-border text-xs">
        <div role="row" className="contents">
          {WEEKDAYS.map((weekday) => (
            <div key={weekday} role="columnheader" className="border-b border-border bg-secondary/30 px-2 py-1 font-semibold text-muted-foreground">{weekday}</div>
          ))}
        </div>
        {weeks.map((week, weekIndex) => (
          <div key={weekIndex} role="row" className="contents">
            {week.map((day, dayIndex) => {
              const dayItems = day ? byDay.get(day) ?? [] : [];
              const isToday = day === today;
              return (
                <div
                  key={day ?? `blank-${weekIndex}-${dayIndex}`}
                  role="gridcell"
                  aria-label={day ? `${Number(day.slice(8))} ${title}${isToday ? ", today" : ""}: ${dayItems.length === 0 ? "nothing due" : `${dayItems.length} due`}` : undefined}
                  className={cn(
                    "min-h-[5.5rem] border-b border-r border-border p-1.5",
                    !day && "bg-secondary/15",
                    isToday && "bg-[var(--interaction-selected-surface)]"
                  )}
                >
                  {day ? (
                    <>
                      <div className={cn("mb-1 tabular-nums text-muted-foreground", isToday && "font-semibold text-foreground")}>{Number(day.slice(8))}</div>
                      <ul className="space-y-1">
                        {dayItems.map((item) => {
                          const overdue = day < today && !(isDone?.(item) ?? false);
                          return (
                            <li key={item.id} data-overdue={overdue || undefined}>
                              <Link to={item.href} className="block rounded-sm border border-border/70 bg-card px-1.5 py-1 hover:border-border focus-visible:outline-hidden focus-visible:ring-2 focus-visible:ring-[var(--interaction-focus)]"
                                aria-label={`${item.label}${overdue ? ", overdue" : ""}. ${item.status}.${item.detail ? ` ${item.detail}.` : ""}`}>
                                <span className="block truncate font-medium text-foreground">{item.label}</span>
                                <span className="mt-0.5 flex flex-wrap items-center gap-1">
                                  <StatusBadge status={overdue ? "blocked" : item.status} label={overdue ? "Overdue" : item.status} />
                                </span>
                                {item.detail ? <span className="mt-0.5 block truncate text-muted-foreground">{item.detail}</span> : null}
                              </Link>
                            </li>
                          );
                        })}
                      </ul>
                    </>
                  ) : null}
                </div>
              );
            })}
          </div>
        ))}
      </div>
    </section>
  );
}
