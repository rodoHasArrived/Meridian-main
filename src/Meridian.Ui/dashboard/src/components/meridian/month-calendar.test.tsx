import { fireEvent, screen, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { MonthCalendar, buildMonthWeeks } from "@/components/meridian/month-calendar";
import { renderWithRouter } from "@/test/render";

describe("MonthCalendar", () => {
  it("lays a month out in Monday-first weeks", () => {
    const weeks = buildMonthWeeks(2026, 6); // July 2026 starts on a Wednesday
    expect(weeks[0]).toEqual([null, null, "2026-07-01", "2026-07-02", "2026-07-03", "2026-07-04", "2026-07-05"]);
    expect(weeks.flat().filter(Boolean)).toHaveLength(31);
    expect(weeks.every((week) => week.length === 7)).toBe(true);
  });

  it("places items on their day, marks today and overdue work, and moves between months", () => {
    renderWithRouter(
      <MonthCalendar
        ariaLabel="Close calendar"
        initialMonth="2026-07"
        today="2026-07-10"
        items={[
          { id: "late", date: "2026-07-03", label: "June close", status: "In review", href: "/accounting" },
          { id: "done", date: "2026-07-04", label: "May close", status: "Ready", href: "/accounting" },
          { id: "next", date: "2026-07-20", label: "July close", status: "In review", href: "/accounting" },
          { id: "august", date: "2026-08-02", label: "August close", status: "In review", href: "/accounting" }
        ]}
        isDone={(item) => item.id === "done"}
      />
    );
    const grid = screen.getByRole("grid", { name: "July 2026" });
    expect(within(grid).getByRole("gridcell", { name: "10 July 2026, today: nothing due" })).toBeInTheDocument();
    expect(within(grid).getByRole("link", { name: /^June close, overdue\./ })).toBeInTheDocument();
    expect(within(grid).getByRole("link", { name: /^May close\. Ready/ })).toBeInTheDocument();
    expect(within(grid).getByRole("link", { name: /^July close\. In review/ })).toBeInTheDocument();
    expect(within(grid).queryByText("August close")).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Next month" }));
    expect(within(screen.getByRole("grid", { name: "August 2026" })).getByText("August close")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Today" }));
    expect(screen.getByRole("grid", { name: "July 2026" })).toBeInTheDocument();
  });
});
