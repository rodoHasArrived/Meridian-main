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
});
