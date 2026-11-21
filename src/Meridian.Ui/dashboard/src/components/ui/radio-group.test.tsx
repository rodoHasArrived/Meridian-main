import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { useState } from "react";
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

  it("moves actual focus so repeated arrows advance and skip disabled choices", async () => {
    const user = userEvent.setup();
    function Fixture() {
      const [value, setValue] = useState("live");
      return <RadioGroup aria-label="Environment" value={value} onChange={setValue}
        options={[...options, { value: "blocked", label: "Blocked", disabled: true }]} />;
    }
    render(<Fixture />);
    screen.getByRole("radio", { name: "Live" }).focus();
    await user.keyboard("{ArrowRight}");
    expect(screen.getByRole("radio", { name: "Paper" })).toHaveFocus();
    expect(screen.getByRole("radio", { name: "Paper" })).toBeChecked();
    await user.keyboard("{ArrowRight}");
    expect(screen.getByRole("radio", { name: "Fixture" })).toHaveFocus();
    await user.keyboard("{ArrowRight}");
    expect(screen.getByRole("radio", { name: "Live" })).toHaveFocus();
    expect(screen.getByRole("radio", { name: "Live" })).toBeChecked();
  });

  it("keeps an enabled option reachable when the retained value is disabled", () => {
    render(<RadioGroup aria-label="Environment" value="live" options={[
      { value: "live", label: "Live", disabled: true }, { value: "paper", label: "Paper" }
    ]} />);
    expect(screen.getByRole("radio", { name: "Paper" })).toHaveAttribute("tabindex", "0");
  });
});
