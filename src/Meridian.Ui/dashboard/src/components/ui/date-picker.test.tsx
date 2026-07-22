import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { DatePicker, monthGrid, toIsoDate } from "@/components/ui/date-picker";
import { DateRangePicker } from "@/components/ui/date-range-picker";

describe("date helpers", () => {
  it("formats a date as ISO YYYY-MM-DD in local time", () => {
    expect(toIsoDate(new Date(2026, 5, 9))).toBe("2026-06-09");
  });

  it("returns a full 6-week grid", () => {
    expect(monthGrid(new Date(2026, 5, 1))).toHaveLength(42);
  });
});

describe("DatePicker", () => {
  it("opens the calendar and emits an ISO date on selection", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();

    render(<DatePicker label="Trade date" value="2026-06-15" onChange={onChange} />);

    await user.click(screen.getByRole("textbox"));
    await user.click(screen.getByRole("button", { name: "20" }));

    expect(onChange).toHaveBeenCalledWith("2026-06-20");
  });
});

describe("DateRangePicker", () => {
  it("sets the start then the end date", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();

    const { rerender } = render(<DateRangePicker label="Window" start={null} end={null} onChange={onChange} />);

    await user.click(screen.getByRole("textbox"));
    await user.click(screen.getAllByRole("button", { name: "10" })[0]!);
    expect(onChange).toHaveBeenLastCalledWith({ start: expect.stringMatching(/-10$/), end: null });

    const start = onChange.mock.calls.at(-1)![0].start as string;
    rerender(<DateRangePicker label="Window" start={start} end={null} onChange={onChange} />);
    await user.click(screen.getAllByRole("button", { name: "20" })[0]!);
    expect(onChange).toHaveBeenLastCalledWith({ start, end: expect.stringMatching(/-20$/) });
  });
});
