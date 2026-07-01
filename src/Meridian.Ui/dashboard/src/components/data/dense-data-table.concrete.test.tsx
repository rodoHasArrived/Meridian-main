import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { DenseDataTable, type DataTableColumn } from "./dense-data-table";

interface Position extends Record<string, unknown> {
  symbol: string;
  qty: number;
  pnl: string;
}

const columns: DataTableColumn<Position>[] = [
  { key: "symbol", label: "Symbol" },
  { key: "qty", label: "Qty", align: "right" },
  { key: "pnl", label: "P&L", align: "right", render: (r) => <span data-testid="pnl">{r.pnl}</span> },
];

const rows: Position[] = [
  { symbol: "AAPL", qty: 100, pnl: "+1,204.00" },
  { symbol: "MSFT", qty: 50, pnl: "-318.22" },
];

describe("DenseDataTable (Concrete)", () => {
  it("renders headers, rows, and custom cell renderers", () => {
    render(<DenseDataTable columns={columns} rows={rows} />);
    expect(screen.getByText("Symbol")).toBeInTheDocument();
    expect(screen.getByText("AAPL")).toBeInTheDocument();
    expect(screen.getAllByTestId("pnl")).toHaveLength(2);
  });

  it("fires onRowClick with the row and index", async () => {
    const onRowClick = vi.fn();
    render(<DenseDataTable columns={columns} rows={rows} onRowClick={onRowClick} />);
    await userEvent.click(screen.getByText("MSFT"));
    expect(onRowClick).toHaveBeenCalledWith(rows[1], 1);
  });

  it("activates clickable rows from the keyboard", async () => {
    const onRowClick = vi.fn();
    render(<DenseDataTable columns={columns} rows={rows} onRowClick={onRowClick} />);
    screen.getByText("MSFT").closest("tr")?.focus();
    await userEvent.keyboard("{Enter}");
    expect(onRowClick).toHaveBeenCalledWith(rows[1], 1);
  });

  it("fires onSort when a sortable header is clicked", async () => {
    const onSort = vi.fn();
    render(<DenseDataTable columns={columns} rows={rows} onSort={onSort} sortKey="qty" sortDir="desc" />);
    await userEvent.click(screen.getByText("Qty"));
    expect(onSort).toHaveBeenCalledWith("qty");
  });

  it("fires onSort when a sortable header is activated from the keyboard", async () => {
    const onSort = vi.fn();
    render(<DenseDataTable columns={columns} rows={rows} onSort={onSort} sortKey="qty" sortDir="desc" />);
    screen.getByRole("columnheader", { name: /Qty/ }).focus();
    await userEvent.keyboard(" ");
    expect(onSort).toHaveBeenCalledWith("qty");
  });

  it("renders a checkbox column and drives selection when selectable", async () => {
    const onSelectRow = vi.fn();
    render(
      <DenseDataTable
        columns={columns}
        rows={rows}
        selectable
        selectedRows={[0]}
        onSelectRow={onSelectRow}
      />,
    );
    const checkboxes = screen.getAllByRole("checkbox");
    // 1 select-all + 2 rows.
    expect(checkboxes).toHaveLength(3);
    await userEvent.click(screen.getByLabelText("Select row 2"));
    expect(onSelectRow).toHaveBeenCalledWith(rows[1], 1, true);
  });
});
