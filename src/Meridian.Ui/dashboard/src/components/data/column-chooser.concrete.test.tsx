import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { ColumnChooser } from "./column-chooser";

const columns = [
  { key: "symbol", label: "Symbol" },
  { key: "qty", label: "Qty" },
  { key: "pnl", label: "P&L" },
];

// The checked "✓" glyph lives inside each menu item and contributes to its accessible
// name, so column items are matched by substring rather than an exact name.

describe("ColumnChooser (Concrete)", () => {
  it("opens the menu and lists all columns", async () => {
    render(<ColumnChooser columns={columns} onChange={vi.fn()} />);
    await userEvent.click(screen.getByRole("button", { name: /columns/i }));
    expect(screen.getByText("Show / hide columns")).toBeInTheDocument();
    expect(screen.getByRole("menuitemcheckbox", { name: /Symbol/ })).toBeInTheDocument();
  });

  it("emits the remaining visible keys when a column is hidden", async () => {
    const onChange = vi.fn();
    render(<ColumnChooser columns={columns} onChange={onChange} />);
    await userEvent.click(screen.getByRole("button", { name: /columns/i }));
    await userEvent.click(screen.getByRole("menuitemcheckbox", { name: /Qty/ }));
    expect(onChange).toHaveBeenCalledWith(["symbol", "pnl"]);
  });

  it("never hides the last remaining column", async () => {
    const onChange = vi.fn();
    render(<ColumnChooser columns={columns} visible={["symbol"]} onChange={onChange} />);
    await userEvent.click(screen.getByRole("button", { name: /columns/i }));
    await userEvent.click(screen.getByRole("menuitemcheckbox", { name: /Symbol/ }));
    expect(onChange).not.toHaveBeenCalled();
  });
});
