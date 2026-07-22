import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { NumberInput } from "@/components/ui/number-input";
import { TextArea } from "@/components/ui/text-area";

describe("NumberInput", () => {
  it("increments and decrements by step, emitting the value", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();

    render(<NumberInput aria-label="Qty" defaultValue={5} step={2} onChange={onChange} />);

    await user.click(screen.getByRole("button", { name: "Increment" }));
    expect(onChange).toHaveBeenLastCalledWith(7);

    await user.click(screen.getByRole("button", { name: "Decrement" }));
    expect(onChange).toHaveBeenLastCalledWith(5);
  });

  it("clamps to min and disables the decrement button at the floor", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();

    render(<NumberInput aria-label="Qty" value={0} min={0} onChange={onChange} />);

    expect(screen.getByRole("button", { name: "Decrement" })).toBeDisabled();
    await user.click(screen.getByRole("button", { name: "Increment" }));
    expect(onChange).toHaveBeenCalledWith(1);
  });
});

describe("TextArea", () => {
  it("renders a labelled control and surfaces error state", () => {
    render(<TextArea label="Notes" error="Required" defaultValue="draft" />);
    const control = screen.getByRole("textbox");
    expect(control).toHaveAttribute("aria-invalid", "true");
    expect(screen.getByText("Notes")).toBeInTheDocument();
    expect(screen.getByText("Required")).toBeInTheDocument();
  });
});
