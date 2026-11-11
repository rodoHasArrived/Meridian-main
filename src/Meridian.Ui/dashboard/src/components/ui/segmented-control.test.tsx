import { useState } from "react";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
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

  it("uses one tab stop, skips disabled tabs, and links the selected tab to its panel", async () => {
    const user = userEvent.setup();
    function Views() {
      const [value, setValue] = useState("table");
      return <SegmentedControl value={value} onChange={setValue} aria-label="View" options={[
        { value: "table", label: "Table", panelId: "table-panel" },
        { value: "disabled", label: "Unavailable", disabled: true },
        { value: "chart", label: "Chart", panelId: "chart-panel" }
      ]} />;
    }
    render(<Views />);
    const table = screen.getByRole("tab", { name: "Table" });
    const chart = screen.getByRole("tab", { name: "Chart" });
    await user.tab();
    expect(table).toHaveFocus();
    await user.keyboard("{ArrowRight}");
    expect(chart).toHaveFocus();
    expect(chart).toHaveAttribute("aria-selected", "true");
    expect(chart).toHaveAttribute("aria-controls", "chart-panel");
    expect(table).toHaveAttribute("tabindex", "-1");
    await user.keyboard("{ArrowRight}");
    expect(table).toHaveFocus();
    await user.keyboard("{End}");
    expect(chart).toHaveFocus();
    await user.keyboard("{Home}");
    expect(table).toHaveFocus();
  });

  it("can represent a parameter choice without claiming tab-panel semantics", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    render(<SegmentedControl semantics="radio" aria-label="Resolution" value="day" onChange={onChange} options={["day", "week"]} />);
    expect(screen.getByRole("radiogroup", { name: "Resolution" })).toBeInTheDocument();
    screen.getByRole("radio", { name: "day" }).focus();
    await user.keyboard("{ArrowLeft}");
    expect(screen.getByRole("radio", { name: "week" })).toHaveFocus();
    expect(onChange).toHaveBeenCalledWith("week");
  });
});
