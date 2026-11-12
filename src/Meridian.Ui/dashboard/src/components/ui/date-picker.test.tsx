import { useState } from "react";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { DatePicker, monthGrid, toIsoDate } from "@/components/ui/date-picker";
import { DateRangePicker } from "@/components/ui/date-range-picker";
import { calendarDate, dateOnly, parseDateOnly } from "@/components/ui/date-field";
import { Dialog, DialogContent, DialogTitle } from "@/components/ui/dialog";

afterEach(() => vi.unstubAllEnvs());

describe("date helpers", () => {
  it("preserves the exported local-date helpers", () => {
    expect(toIsoDate(new Date(2026, 5, 9))).toBe("2026-06-09");
    expect(monthGrid(new Date(2026, 5, 1))).toHaveLength(42);
  });
  it("validates leap days and rejects rollovers rather than displaying another financial date", () => {
    expect(dateOnly(parseDateOnly("2024-02-29")!)).toBe("2024-02-29");
    for (const invalid of ["2026-02-29", "2026-02-31", "2026-13-01", "2026-06-00", "2026-6-9", "invalid"]) expect(parseDateOnly(invalid)).toBeNull();
    expect(dateOnly(calendarDate(99, 0, 1))).toBe("0099-01-01");
  });
});

describe("DatePicker", () => {
  it("associates an automatically generated label, selects an ISO date and restores focus", async () => {
    const user = userEvent.setup(); const onChange = vi.fn();
    render(<DatePicker label="Trade date" value="2026-06-15" onChange={onChange} />);
    const trigger = screen.getByRole("button", { name: /Trade date/ });
    expect(screen.getByLabelText(/Trade date/)).toBe(trigger);
    expect(trigger).toHaveAttribute("aria-expanded", "false");
    await user.click(trigger);
    expect(trigger).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByRole("button", { name: "Monday, June 15, 2026" })).toHaveFocus();
    await user.click(screen.getByRole("button", { name: "Saturday, June 20, 2026" }));
    expect(onChange).toHaveBeenCalledWith("2026-06-20");
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument(); expect(trigger).toHaveFocus();
  });

  it("opens from the keyboard without submitting its form and restores focus on Escape", async () => {
    const user = userEvent.setup(); const submitted = vi.fn();
    render(<form onSubmit={submitted}><label htmlFor="trade-date">Trade date</label><DatePicker id="trade-date" value="2026-06-09" aria-describedby="trade-help" /><span id="trade-help">Use the retained transaction date.</span></form>);
    const trigger = screen.getByRole("button", { name: "Trade date" });
    expect(trigger).toHaveAccessibleDescription("Use the retained transaction date. Jun 9, 2026");
    trigger.focus(); await user.keyboard("{Enter}");
    expect(screen.getByRole("dialog", { name: "Date calendar" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Tuesday, June 9, 2026" })).toHaveFocus();
    expect(submitted).not.toHaveBeenCalled();
    await user.keyboard("{Escape}"); expect(screen.queryByRole("dialog")).not.toBeInTheDocument(); expect(trigger).toHaveFocus();
  });

  it("moves actual roving focus across month boundaries and clamps PageDown to February", async () => {
    const user = userEvent.setup(); const changed = vi.fn();
    function Controlled() { const [value, setValue] = useState("2026-01-31"); return <DatePicker label="Valuation date" value={value} onChange={(next) => { changed(next); setValue(next); }} />; }
    render(<Controlled />); const trigger = screen.getByRole("button", { name: /Valuation date/ });
    trigger.focus(); await user.keyboard("{ArrowDown}{PageDown}");
    expect(screen.getByRole("button", { name: "Saturday, February 28, 2026" })).toHaveFocus();
    expect(within(screen.getByRole("grid")).getAllByRole("button").filter((button) => button.tabIndex === 0)).toHaveLength(1);
    await user.keyboard("{ArrowRight}{Enter}");
    expect(changed).toHaveBeenLastCalledWith("2026-03-01"); expect(trigger).toHaveFocus(); expect(trigger).toHaveTextContent("Mar 1, 2026");
  });

  it("supports week boundaries and year navigation without selecting a date", async () => {
    const user = userEvent.setup(); const changed = vi.fn();
    render(<DatePicker label="Book date" value="2026-06-09" onChange={changed} />);
    await user.click(screen.getByRole("button", { name: /Book date/ }));
    await user.keyboard("{Home}"); expect(screen.getByRole("button", { name: "Sunday, June 7, 2026" })).toHaveFocus();
    await user.keyboard("{End}"); expect(screen.getByRole("button", { name: "Saturday, June 13, 2026" })).toHaveFocus();
    await user.keyboard("{Shift>}{PageUp}{/Shift}"); expect(screen.getByRole("button", { name: "Friday, June 13, 2025" })).toHaveFocus();
    expect(changed).not.toHaveBeenCalled();
  });

  it("uses the first Escape for a calendar and the second for its parent dialog", async () => {
    const user = userEvent.setup(); const closed = vi.fn();
    render(<Dialog open onOpenChange={closed}><DialogContent aria-labelledby="record-title"><DialogTitle id="record-title">Review record</DialogTitle><DatePicker label="Effective date" value="2026-06-09" /></DialogContent></Dialog>);
    await user.click(screen.getByRole("button", { name: /Effective date/ })); await user.keyboard("{Escape}");
    expect(screen.queryByRole("dialog", { name: "Effective date calendar" })).not.toBeInTheDocument(); expect(closed).not.toHaveBeenCalled();
    await user.keyboard("{Escape}"); expect(closed).toHaveBeenCalledExactlyOnceWith(false);
  });

  it("lets Tab leave the nonmodal calendar and closes its popover", async () => {
    const user = userEvent.setup();
    render(<><DatePicker label="Trade date" value="2026-06-09" /><button type="button">Next field</button></>);
    await user.click(screen.getByRole("button", { name: /Trade date/ }));
    expect(screen.getByRole("button", { name: "Tuesday, June 9, 2026" })).toHaveFocus();
    await user.tab(); expect(screen.getByRole("button", { name: "Next field" })).toHaveFocus();
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("keeps disabled fields closed, custom placeholders intact, and invalid dates explicit", async () => {
    const user = userEvent.setup(); const view = render(<DatePicker label="Effective date" disabled value="2026-06-09" />);
    await user.click(screen.getByRole("button", { name: /Effective date/ })); expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    view.rerender(<DatePicker label="Effective date" value="2026-02-31" />); expect(screen.getByRole("button", { name: /Effective date/ })).toHaveTextContent("Invalid date");
    view.rerender(<DatePicker label="Effective date" value={null} placeholder="Choose a book date" />); expect(screen.getByRole("button", { name: /Effective date/ })).toHaveTextContent("Choose a book date");
  });

  it("retains validation semantics and feedback on the labelled trigger", () => {
    render(<><DatePicker label="Settlement date" value="2026-06-09" aria-invalid="true" aria-errormessage="settlement-error" aria-describedby="settlement-help" /><span id="settlement-help">Required for posting.</span><span id="settlement-error">Choose an open accounting period.</span></>);
    const trigger = screen.getByRole("button", { name: /Settlement date/ });
    expect(trigger).toHaveAttribute("aria-invalid", "true");
    expect(trigger).toHaveAttribute("aria-errormessage", "settlement-error"); expect(trigger).toHaveAccessibleDescription("Required for posting.");
  });

  it.each(["America/Los_Angeles", "Pacific/Auckland"])("preserves date-only display under %s", (timezone) => {
    vi.stubEnv("TZ", timezone); render(<DatePicker label="Trade date" value="2026-06-09" />);
    expect(screen.getByRole("button", { name: /Trade date/ })).toHaveTextContent("Jun 9, 2026");
  });
});

describe("DateRangePicker", () => {
  it("supports an external label and retained validation feedback", () => {
    render(<><label htmlFor="reporting-dates">Reporting dates</label><DateRangePicker id="reporting-dates" start="2026-06-09" end="2026-06-15" aria-invalid="true" aria-errormessage="reporting-error" /><span id="reporting-error">The interval must be open.</span></>);
    const trigger = screen.getByRole("button", { name: "Reporting dates" });
    expect(trigger).toHaveAccessibleDescription("Jun 9 — Jun 15"); expect(trigger).toHaveAttribute("aria-invalid", "true"); expect(trigger).toHaveAttribute("aria-errormessage", "reporting-error");
  });
  it("sets a new start then an end while maintaining one roving day across the two months", async () => {
    const user = userEvent.setup(); const changed = vi.fn();
    function Controlled() { const [range, setRange] = useState({ start: "2026-06-09" as string | null, end: "2026-06-15" as string | null }); return <DateRangePicker label="Reporting period" {...range} onChange={(next) => { changed(next); setRange(next); }} />; }
    render(<Controlled />); const trigger = screen.getByRole("button", { name: /Reporting period/ });
    await user.click(trigger); const dialog = screen.getByRole("dialog", { name: "Reporting period calendar" });
    expect(within(dialog).getAllByRole("grid")).toHaveLength(2);
    expect(within(dialog).getAllByRole("button").filter((button) => button.hasAttribute("data-date") && button.tabIndex === 0)).toHaveLength(1);
    await user.click(screen.getByRole("button", { name: "Wednesday, June 10, 2026" }));
    expect(changed).toHaveBeenLastCalledWith({ start: "2026-06-10", end: null });
    await user.click(screen.getByRole("button", { name: "Saturday, June 20, 2026" }));
    expect(changed).toHaveBeenLastCalledWith({ start: "2026-06-10", end: "2026-06-20" });
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument(); expect(trigger).toHaveFocus();
  });

  it("completes an earlier second pick in date order and restores focus", async () => {
    const user = userEvent.setup(); const changed = vi.fn();
    function Controlled() { const [range, setRange] = useState({ start: "2026-06-09" as string | null, end: null as string | null }); return <DateRangePicker label="Reporting period" {...range} onChange={(next) => { changed(next); setRange(next); }} />; }
    render(<Controlled />); const trigger = screen.getByRole("button", { name: /Reporting period/ }); await user.click(trigger);
    await user.keyboard("{ArrowLeft}{Enter}"); expect(changed).toHaveBeenLastCalledWith({ start: "2026-06-08", end: "2026-06-09" });
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument(); expect(trigger).toHaveFocus();
  });
});
