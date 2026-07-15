import { fireEvent, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useState } from "react";
import { describe, expect, it, vi } from "vitest";
import { DENSE_ROW_DETAIL_KEYBOARD_INSTRUCTIONS, DenseRowDetailPanel } from "@/components/meridian/dense-row-detail-accessibility";
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
      <>
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
      <DenseRowDetailPanel id="symbol-detail" ariaLabel="Symbol detail">Symbol detail</DenseRowDetailPanel>
      </>
    );

    const selectedRow = screen.getByRole("row", { name: "Select AAPL" });
    expect(selectedRow).toHaveAttribute("aria-selected", "true");
    expect(selectedRow).toHaveAttribute("aria-controls", "symbol-detail");
    expect(selectedRow).toHaveAttribute("aria-expanded", "true");
    expect(selectedRow).toHaveAttribute("tabindex", "0");
    expect(screen.getByRole("row", { name: "Select MSFT" })).toHaveAttribute("aria-expanded", "false");
    expect(screen.getByRole("row", { name: "Select MSFT" })).toHaveAttribute("tabindex", "-1");
    expect(screen.getByRole("treegrid", { name: "Test table" })).toHaveAccessibleDescription(
      DENSE_ROW_DETAIL_KEYBOARD_INSTRUCTIONS
    );

    await user.click(screen.getByRole("row", { name: "Select MSFT" }));
    expect(onRowSelect).toHaveBeenLastCalledWith(rows[1]);

    screen.getByRole("row", { name: "Select AAPL" }).focus();
    await user.keyboard("{Enter}");
    expect(onRowSelect).toHaveBeenLastCalledWith(rows[0]);

    await user.keyboard(" ");
    expect(onRowSelect).toHaveBeenLastCalledWith(rows[0]);
  });

  it("moves master-detail row focus and selection with arrow, home, and end keys", async () => {
    const user = userEvent.setup();
    const onRowSelect = vi.fn();

    render(
      <DenseDataTable
        columns={columns}
        rows={rows}
        getRowId={(row) => row.id}
        getRowSelectAriaLabel={(row) => `Select ${row.symbol}`}
        onRowSelect={onRowSelect}
        selectedRowId="aapl"
        emptyText="No rows"
        ariaLabel="Keyboard table"
      />
    );

    const firstRow = screen.getByRole("row", { name: "Select AAPL" });
    const secondRow = screen.getByRole("row", { name: "Select MSFT" });

    firstRow.focus();
    await user.keyboard("{ArrowDown}");
    expect(onRowSelect).toHaveBeenLastCalledWith(rows[1]);
    expect(secondRow).toHaveFocus();

    await user.keyboard("{Home}");
    expect(onRowSelect).toHaveBeenLastCalledWith(rows[0]);
    expect(firstRow).toHaveFocus();

    await user.keyboard("{End}");
    expect(onRowSelect).toHaveBeenLastCalledWith(rows[1]);
    expect(secondRow).toHaveFocus();

    await user.keyboard("{ArrowUp}");
    expect(onRowSelect).toHaveBeenLastCalledWith(rows[0]);
    expect(firstRow).toHaveFocus();
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

    fireEvent.keyDown(screen.getByRole("button", { name: "Open AAPL" }), { key: "Enter" });

    expect(onRowSelect).not.toHaveBeenCalled();
  });

  it("activates rows when keyboard events bubble from non-interactive cell content", () => {
    const onRowSelect = vi.fn();

    render(
      <DenseDataTable
        columns={[
          {
            id: "symbol",
            label: "Symbol",
            render: (row) => <span data-testid={`symbol-${row.id}`}>{row.symbol}</span>
          },
          { id: "status", label: "Status", render: (row) => row.status }
        ]}
        rows={rows}
        getRowId={(row) => row.id}
        getRowSelectAriaLabel={(row) => `Select ${row.symbol}`}
        onRowSelect={onRowSelect}
        selectedRowId="aapl"
        emptyText="No rows"
        ariaLabel="Bubbling table"
      />
    );

    fireEvent.keyDown(screen.getByTestId("symbol-msft"), { key: "Enter" });

    expect(onRowSelect).toHaveBeenLastCalledWith(rows[1]);
  });

  it("renders sortable headers with aria-sort and toggle commands", async () => {
    const user = userEvent.setup();
    const onToggleSort = vi.fn();

    render(
      <DenseDataTable
        columns={[
          { id: "symbol", label: "Symbol", sortable: true, render: (row) => row.symbol },
          { id: "status", label: "Status", sortable: true, render: (row) => row.status }
        ]}
        rows={rows}
        getRowId={(row) => row.id}
        emptyText="No rows"
        ariaLabel="Sortable table"
        sort={{ columnId: "symbol", direction: "asc" }}
        onToggleSort={onToggleSort}
      />
    );

    expect(screen.getByRole("columnheader", { name: /symbol/i })).toHaveAttribute("aria-sort", "ascending");
    expect(screen.getByRole("columnheader", { name: /status/i })).toHaveAttribute("aria-sort", "none");
    expect(screen.getByRole("button", { name: "Symbol sorted ascending. Activate to change sort." })).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Sort by Status" }));

    expect(onToggleSort).toHaveBeenCalledWith("status");
  });

  it("applies view-model-owned row classes", () => {
    render(
      <DenseDataTable
        columns={columns}
        rows={rows}
        getRowId={(row) => row.id}
        getRowAriaLabel={(row) => `${row.symbol} ${row.status}`}
        getRowClassName={(row) => row.id === "msft" ? "state-disabled" : undefined}
        emptyText="No rows"
        ariaLabel="State table"
      />
    );

    expect(screen.getByRole("row", { name: "AAPL Active" })).not.toHaveClass("state-disabled");
    expect(screen.getByRole("row", { name: "MSFT Monitored" })).toHaveClass("state-disabled");
  });

  it("skips cell rendering when parent state changes but table props stay stable", async () => {
    const user = userEvent.setup();
    const renderSymbol = vi.fn((row: TestRow) => row.symbol);
    const stableColumns: DenseDataTableColumn<TestRow>[] = [
      { id: "symbol", label: "Symbol", render: renderSymbol }
    ];

    function StableTableHost() {
      const [tick, setTick] = useState(0);
      return (
        <>
          <button type="button" onClick={() => setTick((current) => current + 1)}>
            Parent tick {tick}
          </button>
          <DenseDataTable
            columns={stableColumns}
            rows={rows}
            getRowId={getTestRowId}
            emptyText="No rows"
            ariaLabel="Memoized table"
          />
        </>
      );
    }

    render(<StableTableHost />);

    expect(renderSymbol).toHaveBeenCalledTimes(rows.length);

    await user.click(screen.getByRole("button", { name: "Parent tick 0" }));

    expect(screen.getByRole("button", { name: "Parent tick 1" })).toBeInTheDocument();
    expect(renderSymbol).toHaveBeenCalledTimes(rows.length);
  });

  it("can cap initially visible rows without changing the default table behavior", async () => {
    const user = userEvent.setup();
    const extendedRows = [
      ...rows,
      { id: "nvda", symbol: "NVDA", status: "Watched" }
    ];

    render(
      <DenseDataTable
        columns={columns}
        rows={extendedRows}
        getRowId={(row) => row.id}
        getRowAriaLabel={(row) => `${row.symbol} ${row.status}`}
        emptyText="No rows"
        ariaLabel="Capped table"
        maxVisibleRows={2}
      />
    );

    expect(screen.getByRole("row", { name: "AAPL Active" })).toBeInTheDocument();
    expect(screen.getByRole("row", { name: "MSFT Monitored" })).toBeInTheDocument();
    expect(screen.queryByRole("row", { name: "NVDA Watched" })).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Show all 3 rows" }));

    expect(screen.getByRole("row", { name: "NVDA Watched" })).toBeInTheDocument();
  });

  it("guards the DOM with a default row cap when no explicit limit is given", async () => {
    const user = userEvent.setup();
    const manyRows = Array.from({ length: 150 }, (_, index) => ({
      id: `row-${index}`,
      symbol: `SYM${index}`,
      status: "Watched"
    }));

    render(
      <DenseDataTable
        columns={columns}
        rows={manyRows}
        getRowId={(row) => row.id}
        getRowAriaLabel={(row) => `${row.symbol} ${row.status}`}
        emptyText="No rows"
        ariaLabel="Unbounded table"
      />
    );

    // Only the first 100 rows are mounted by default; the rest stay behind the
    // progressive-disclosure control so a large set never floods the DOM.
    expect(screen.getByRole("row", { name: "SYM0 Watched" })).toBeInTheDocument();
    expect(screen.getByRole("row", { name: "SYM99 Watched" })).toBeInTheDocument();
    expect(screen.queryByRole("row", { name: "SYM100 Watched" })).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Show all 150 rows" }));
    expect(screen.getByRole("row", { name: "SYM149 Watched" })).toBeInTheDocument();
  });

  it("renders every row when the cap is explicitly disabled with null", () => {
    const manyRows = Array.from({ length: 130 }, (_, index) => ({
      id: `row-${index}`,
      symbol: `SYM${index}`,
      status: "Watched"
    }));

    render(
      <DenseDataTable
        columns={columns}
        rows={manyRows}
        getRowId={(row) => row.id}
        getRowAriaLabel={(row) => `${row.symbol} ${row.status}`}
        emptyText="No rows"
        ariaLabel="Uncapped table"
        maxVisibleRows={null}
      />
    );

    expect(screen.getByRole("row", { name: "SYM129 Watched" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Show all/ })).not.toBeInTheDocument();
  });

  it("invokes onRowContextMenu with the row for a right-click gesture", () => {
    const onRowContextMenu = vi.fn();

    render(
      <DenseDataTable
        columns={columns}
        rows={rows}
        getRowId={(row) => row.id}
        getRowAriaLabel={(row) => `${row.symbol} ${row.status}`}
        onRowContextMenu={onRowContextMenu}
        emptyText="No rows"
        ariaLabel="Context table"
      />
    );

    fireEvent.contextMenu(screen.getByRole("row", { name: "MSFT Monitored" }));

    expect(onRowContextMenu).toHaveBeenCalledTimes(1);
    expect(onRowContextMenu.mock.calls[0][0]).toEqual({ id: "msft", symbol: "MSFT", status: "Monitored" });
  });

  it("leaves rows without a context-menu handler when onRowContextMenu is omitted", () => {
    render(
      <DenseDataTable
        columns={columns}
        rows={rows}
        getRowId={(row) => row.id}
        getRowAriaLabel={(row) => `${row.symbol} ${row.status}`}
        emptyText="No rows"
        ariaLabel="No context table"
      />
    );

    // A right-click with no handler must not throw and must not select the row.
    fireEvent.contextMenu(screen.getByRole("row", { name: "AAPL Active" }));
    expect(screen.getByRole("row", { name: "AAPL Active" })).not.toHaveAttribute("aria-selected");
  });
});

function getTestRowId(row: TestRow) {
  return row.id;
}
