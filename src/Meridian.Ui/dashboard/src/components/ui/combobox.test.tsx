import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { Combobox } from "@/components/ui/combobox";

const options = [
  { value: "AAPL", label: "Apple Inc." },
  { value: "MSFT", label: "Microsoft Corp." },
  { value: "GOOG", label: "Alphabet Inc." }
];

describe("Combobox", () => {
  it("filters options by query and commits selection with Enter", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();

    render(<Combobox options={options} onChange={onChange} />);

    const input = screen.getByRole("combobox");
    await user.click(input);
    await user.type(input, "micro");

    expect(screen.getByRole("option", { name: /Microsoft/ })).toBeInTheDocument();
    expect(screen.queryByRole("option", { name: /Apple/ })).not.toBeInTheDocument();

    await user.keyboard("{Enter}");
    expect(onChange).toHaveBeenCalledWith("MSFT");
  });

  it("navigates options with arrow keys", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();

    render(<Combobox options={options} onChange={onChange} />);

    await user.click(screen.getByRole("combobox"));
    await user.keyboard("{ArrowDown}{Enter}");

    expect(onChange).toHaveBeenCalledWith("MSFT");
  });

  it("labels independent instances and exposes the active option while keyboard focus stays on the input", async () => {
    const user = userEvent.setup();
    render(<><Combobox label="First instrument" options={options} /><Combobox label="Second instrument" options={options} /></>);
    const first = screen.getByRole("combobox", { name: "First instrument" });
    const second = screen.getByRole("combobox", { name: "Second instrument" });
    expect(first.id).not.toBe(second.id);
    await user.click(first);
    const listId = first.getAttribute("aria-controls");
    expect(document.getElementById(listId!)).toHaveAttribute("role", "listbox");
    await user.keyboard("{End}");
    expect(document.getElementById(first.getAttribute("aria-activedescendant")!)).toHaveAccessibleName("Alphabet Inc.");
    expect(first).toHaveFocus();
    await user.keyboard("{Home}{ArrowDown}");
    expect(document.getElementById(first.getAttribute("aria-activedescendant")!)).toHaveAccessibleName("Microsoft Corp.");
  });

  it("keeps no-match selection inert and lets Enter submit a surrounding form after the menu closes", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    const onSubmit = vi.fn((event: React.FormEvent) => event.preventDefault());
    render(<form onSubmit={onSubmit}><Combobox label="Instrument" options={options} onChange={onChange} /><button type="submit">Save</button></form>);
    const input = screen.getByRole("combobox", { name: "Instrument" });
    await user.click(input);
    await user.type(input, "missing");
    await user.keyboard("{ArrowDown}{Enter}");
    expect(input).not.toHaveAttribute("aria-activedescendant");
    expect(onChange).not.toHaveBeenCalled();
    expect(onSubmit).not.toHaveBeenCalled();
    await user.keyboard("{Escape}{Enter}");
    expect(input).toHaveAttribute("aria-expanded", "false");
    expect(input).not.toHaveAttribute("aria-controls");
    expect(onChange).not.toHaveBeenCalled();
    expect(onSubmit).toHaveBeenCalledTimes(1);
  });

  it("closes on Tab and prevents selection when a control becomes disabled", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    const { rerender } = render(<><Combobox label="Instrument" options={options} onChange={onChange} /><button>Next</button></>);
    const input = screen.getByRole("combobox", { name: "Instrument" });
    await user.click(input);
    await user.tab();
    expect(screen.getByRole("button", { name: "Next" })).toHaveFocus();
    expect(screen.queryByRole("listbox")).not.toBeInTheDocument();
    await user.click(input);
    rerender(<><Combobox label="Instrument" options={options} onChange={onChange} disabled /><button>Next</button></>);
    expect(input).toBeDisabled();
    expect(input).toHaveAttribute("aria-expanded", "false");
    expect(screen.queryByRole("listbox")).not.toBeInTheDocument();
    expect(onChange).not.toHaveBeenCalled();
  });

  it("honors a disabled parent fieldset even when its suggestion popup was already open", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    const { rerender } = render(<fieldset><Combobox label="Instrument" options={options} onChange={onChange} /></fieldset>);
    await user.click(screen.getByRole("combobox", { name: "Instrument" }));
    rerender(<fieldset disabled><Combobox label="Instrument" options={options} onChange={onChange} /></fieldset>);
    expect(screen.getByRole("combobox", { name: "Instrument" })).toBeDisabled();
    await user.click(screen.getByRole("option", { name: "Microsoft Corp." }));
    expect(onChange).not.toHaveBeenCalled();
  });
});
