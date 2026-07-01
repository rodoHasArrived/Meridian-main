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
});
