import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { useState } from "react";
import { SegmentedControl } from "@/components/ui/segmented-control";

describe("SegmentedControl", () => {
  it("marks the active option and emits the raw value on change", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();

    render(
      <SegmentedControl
        aria-label="View"
        value="table"
        onChange={onChange}
        options={[
          { value: "table", label: "Table" },
          { value: "chart", label: "Chart" }
        ]}
      />
    );

    expect(screen.getByRole("tab", { name: "Table" })).toHaveAttribute("aria-selected", "true");

    await user.click(screen.getByRole("tab", { name: "Chart" }));
    expect(onChange).toHaveBeenCalledWith("chart");
  });

  it("does not emit for disabled options and accepts string options", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();

    render(
      <SegmentedControl
        value="day"
        onChange={onChange}
        options={["day", { value: "week", label: "Week", disabled: true }]}
      />
    );

    await user.click(screen.getByRole("tab", { name: "Week" }));
    expect(onChange).not.toHaveBeenCalled();
    expect(screen.getByRole("tab", { name: "day" })).toBeInTheDocument();
  });

  it("uses one tab stop and moves selection and focus past disabled options", async () => {
    const user = userEvent.setup();
    function Fixture() {
      const [value, setValue] = useState("table");
      return <SegmentedControl aria-label="View" value={value} onChange={setValue}
        options={["table", { value: "unavailable", label: "Unavailable", disabled: true }, "chart", "details"]} />;
    }
    render(<Fixture />);
    const table = screen.getByRole("tab", { name: "table" });
    const chart = screen.getByRole("tab", { name: "chart" });
    const details = screen.getByRole("tab", { name: "details" });
    expect(table).toHaveAttribute("tabindex", "0");
    expect(chart).toHaveAttribute("tabindex", "-1");
    table.focus();
    await user.keyboard("{ArrowRight}");
    expect(chart).toHaveFocus();
    expect(chart).toHaveAttribute("aria-selected", "true");
    expect(table).toHaveAttribute("tabindex", "-1");
    await user.keyboard("{End}");
    expect(details).toHaveFocus();
    await user.keyboard("{ArrowRight}");
    expect(table).toHaveFocus();
    await user.keyboard("{Home}");
    expect(table).toHaveAttribute("aria-selected", "true");
  });
});
