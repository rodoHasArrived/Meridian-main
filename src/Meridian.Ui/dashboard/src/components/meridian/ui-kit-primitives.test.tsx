import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";

interface TestRow {
  id: string;
  symbol: string;
  status: string;
}

const rows: TestRow[] = [
  { id: "aapl", symbol: "AAPL", status: "Active" },
  { id: "msft", symbol: "MSFT", status: "Monitored" }
];

const columns: DenseDataTableColumn<TestRow>[] = [
  { id: "symbol", label: "Symbol", render: (row) => row.symbol },
  { id: "status", label: "Status", render: (row) => row.status }
];

describe("DenseDataTable", () => {
  it("selects rows with click and keyboard commands", async () => {
    const user = userEvent.setup();
    const onRowSelect = vi.fn();

    render(
      <DenseDataTable
        columns={columns}
        rows={rows}
        getRowId={(row) => row.id}
        getRowAriaLabel={(row) => `${row.symbol} ${row.status}`}
        getRowSelectAriaLabel={(row) => `Select ${row.symbol}`}
        getRowAriaControls={() => "symbol-detail"}
        getRowAriaExpanded={(row) => row.id === "aapl"}
        onRowSelect={onRowSelect}
        selectedRowId="aapl"
        emptyText="No rows"
        ariaLabel="Test table"
      />
    );

    const selectedRow = screen.getByRole("row", { name: "Select AAPL" });
    expect(selectedRow).toHaveAttribute("aria-selected", "true");
    expect(selectedRow).toHaveAttribute("aria-controls", "symbol-detail");
    expect(selectedRow).toHaveAttribute("aria-expanded", "true");
    expect(selectedRow).toHaveAttribute("tabindex", "0");
    expect(screen.getByRole("row", { name: "Select MSFT" })).toHaveAttribute("aria-expanded", "false");

    await user.click(screen.getByRole("row", { name: "Select MSFT" }));
    expect(onRowSelect).toHaveBeenLastCalledWith(rows[1]);

    screen.getByRole("row", { name: "Select AAPL" }).focus();
    await user.keyboard("{Enter}");
    expect(onRowSelect).toHaveBeenLastCalledWith(rows[0]);

    await user.keyboard(" ");
    expect(onRowSelect).toHaveBeenLastCalledWith(rows[0]);
  });

  it("does not hijack interactive controls inside selectable rows", async () => {
    const user = userEvent.setup();
    const onRowSelect = vi.fn();
    const onButtonClick = vi.fn();

    render(
      <DenseDataTable
        columns={[
          { id: "symbol", label: "Symbol", render: (row) => row.symbol },
          {
            id: "action",
            label: "Action",
            render: (row) => (
              <button type="button" onClick={onButtonClick}>
                Open {row.symbol}
              </button>
            )
          }
        ]}
        rows={rows}
        getRowId={(row) => row.id}
        getRowAriaLabel={(row) => `${row.symbol} ${row.status}`}
        onRowSelect={onRowSelect}
        selectedRowId={null}
        emptyText="No rows"
        ariaLabel="Action table"
      />
    );

    await user.click(screen.getByRole("button", { name: "Open AAPL" }));

    expect(onButtonClick).toHaveBeenCalledTimes(1);
    expect(onRowSelect).not.toHaveBeenCalled();
  });
});
