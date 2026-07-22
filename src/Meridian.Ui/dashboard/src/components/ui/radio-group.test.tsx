import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { RadioGroup } from "@/components/ui/radio-group";

const options = [
  { value: "live", label: "Live" },
  { value: "paper", label: "Paper" },
  { value: "fixture", label: "Fixture" }
];

describe("RadioGroup", () => {
  it("selects an option on click and emits the value", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();

    render(<RadioGroup aria-label="Environment" options={options} value="live" onChange={onChange} />);

    await user.click(screen.getByRole("radio", { name: "Paper" }));
    expect(onChange).toHaveBeenCalledWith("paper");
    expect(screen.getByRole("radio", { name: "Live" })).toBeChecked();
  });

  it("moves selection with arrow keys and wraps", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();

    render(<RadioGroup aria-label="Environment" options={options} value="fixture" onChange={onChange} />);

    screen.getByRole("radio", { name: "Fixture" }).focus();
    await user.keyboard("{ArrowDown}");
    expect(onChange).toHaveBeenCalledWith("live");
  });
});
