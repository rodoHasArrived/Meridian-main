import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { fireEvent, render, screen, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { DenseDataTable, sortRowsByValue, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";

interface PositionRow {
  id: string;
  symbol: string;
  quantity: number | null;
  marketValue: number;
}

const rows: PositionRow[] = [
  { id: "a", symbol: "MSFT", quantity: 120, marketValue: 48_000 },
  { id: "b", symbol: "AAPL", quantity: null, marketValue: 91_500 },
  { id: "c", symbol: "NVDA", quantity: 40, marketValue: 36_250 }
];

const columns: DenseDataTableColumn<PositionRow>[] = [
  { id: "symbol", label: "Symbol", sortValue: (row) => row.symbol, render: (row) => row.symbol },
  { id: "quantity", label: "Quantity", numeric: true, hideable: true, sortValue: (row) => row.quantity, render: (row) => row.quantity ?? "—" },
  { id: "marketValue", label: "Market value", numeric: true, hideable: true, sortValue: (row) => row.marketValue, render: (row) => row.marketValue },
  { id: "basis", label: "Cost basis source", defaultHidden: true, render: () => "Broker" }
];

function bodySymbols(): string[] {
  const table = screen.getByRole("table", { name: "Positions" });
  return within(table).getAllByRole("row").slice(1).map((row) => within(row).getAllByRole("cell")[0].textContent ?? "");
}

afterEach(() => {
  window.localStorage.clear();
  document.body.removeAttribute("data-theme-density");
});

describe("DenseDataTable table system", () => {
  it("sorts by a column's raw value and reports the order through aria-sort", () => {
    render(<DenseDataTable ariaLabel="Positions" emptyText="No positions" columns={columns} rows={rows} getRowId={(row) => row.id} />);

    const symbolHeader = screen.getByRole("columnheader", { name: /Symbol/ });
    expect(symbolHeader).toHaveAttribute("aria-sort", "none");

    fireEvent.click(within(symbolHeader).getByRole("button"));
    expect(symbolHeader).toHaveAttribute("aria-sort", "ascending");
    expect(bodySymbols()).toEqual(["AAPL", "MSFT", "NVDA"]);

    fireEvent.click(within(symbolHeader).getByRole("button"));
    expect(symbolHeader).toHaveAttribute("aria-sort", "descending");
    expect(bodySymbols()).toEqual(["NVDA", "MSFT", "AAPL"]);
  });

  it("starts numeric columns largest-first and keeps missing values last", () => {
    render(<DenseDataTable ariaLabel="Positions" emptyText="No positions" columns={columns} rows={rows} getRowId={(row) => row.id} />);

    const quantityHeader = screen.getByRole("columnheader", { name: /Quantity/ });
    fireEvent.click(within(quantityHeader).getByRole("button"));
    expect(quantityHeader).toHaveAttribute("aria-sort", "descending");
    expect(bodySymbols()).toEqual(["MSFT", "NVDA", "AAPL"]);

    fireEvent.click(within(quantityHeader).getByRole("button"));
    expect(bodySymbols()).toEqual(["NVDA", "MSFT", "AAPL"]);
  });

  it("honors a default sort and leaves caller-owned sorting alone", () => {
    const { unmount } = render(
      <DenseDataTable
        ariaLabel="Positions"
        emptyText="No positions"
        columns={columns}
        rows={rows}
        getRowId={(row) => row.id}
        defaultSort={{ columnId: "marketValue", direction: "desc" }}
      />
    );
    expect(bodySymbols()).toEqual(["AAPL", "MSFT", "NVDA"]);
    unmount();

    // With onToggleSort the caller owns order: rows render as given and only `sortable` columns sort.
    render(
      <DenseDataTable
        ariaLabel="Positions"
        emptyText="No positions"
        columns={columns}
        rows={rows}
        getRowId={(row) => row.id}
        sort={{ columnId: "marketValue", direction: "desc" }}
        onToggleSort={() => undefined}
      />
    );
    expect(bodySymbols()).toEqual(["MSFT", "AAPL", "NVDA"]);
    expect(screen.getByRole("columnheader", { name: /Market value/ })).not.toHaveAttribute("aria-sort");
  });

  it("right-aligns numeric columns with tabular figures", () => {
    render(<DenseDataTable ariaLabel="Positions" emptyText="No positions" columns={columns} rows={rows} getRowId={(row) => row.id} />);

    const header = screen.getByRole("columnheader", { name: /Market value/ });
    expect(header).toHaveClass("text-right", "dense-data-table-numeric");
    const firstValueCell = within(screen.getAllByRole("row")[1]).getAllByRole("cell")[2];
    expect(firstValueCell).toHaveClass("text-right", "dense-data-table-numeric");
  });

  it("lets operators hide and show columns, remembering the choice per table", () => {
    const { unmount } = render(
      <DenseDataTable tableId="positions-test" ariaLabel="Positions" emptyText="No positions" columns={columns} rows={rows} getRowId={(row) => row.id} />
    );

    // Default-hidden columns start off; hideable columns start on.
    expect(screen.queryByRole("columnheader", { name: /Cost basis source/ })).toBeNull();
    const menu = screen.getByRole("group", { name: "Show columns in Positions" });
    fireEvent.click(within(menu).getByRole("checkbox", { name: "Quantity" }));
    fireEvent.click(within(menu).getByRole("checkbox", { name: "Cost basis source" }));
    expect(screen.queryByRole("columnheader", { name: /Quantity/ })).toBeNull();
    expect(screen.getByRole("columnheader", { name: /Cost basis source/ })).toBeInTheDocument();
    unmount();

    render(
      <DenseDataTable tableId="positions-test" ariaLabel="Positions" emptyText="No positions" columns={columns} rows={rows} getRowId={(row) => row.id} />
    );
    expect(screen.queryByRole("columnheader", { name: /Quantity/ })).toBeNull();
    expect(screen.getByRole("columnheader", { name: /Cost basis source/ })).toBeInTheDocument();
  });

  it("lets the caller own hidden columns so a link or saved view can carry them (C4)", () => {
    const onHiddenColumnIdsChange = vi.fn();
    const { rerender } = render(
      <DenseDataTable
        tableId="owned-columns"
        ariaLabel="Positions"
        emptyText="No positions"
        columns={columns}
        rows={rows}
        getRowId={(row) => row.id}
        hiddenColumnIds={["quantity", "symbol"]}
        onHiddenColumnIdsChange={onHiddenColumnIdsChange}
      />
    );
    // Only hideable ids apply; the browser memory for this table id is not used.
    expect(screen.queryByRole("columnheader", { name: /Quantity/ })).toBeNull();
    expect(screen.getByRole("columnheader", { name: /Symbol/ })).toBeInTheDocument();
    expect(screen.getByRole("columnheader", { name: /Cost basis source/ })).toBeInTheDocument();

    fireEvent.click(within(screen.getByRole("group", { name: "Show columns in Positions" })).getByRole("checkbox", { name: "Market value" }));
    expect(onHiddenColumnIdsChange).toHaveBeenCalledWith(expect.arrayContaining(["quantity", "marketValue"]));
    expect(window.localStorage.getItem("meridian.workstation.table-columns.v1:owned-columns")).toBeNull();

    rerender(
      <DenseDataTable
        tableId="owned-columns"
        ariaLabel="Positions"
        emptyText="No positions"
        columns={columns}
        rows={rows}
        getRowId={(row) => row.id}
        hiddenColumnIds={[]}
        onHiddenColumnIdsChange={onHiddenColumnIdsChange}
      />
    );
    expect(screen.getByRole("columnheader", { name: /Quantity/ })).toBeInTheDocument();
  });

  it("renders no Columns menu when no column is hideable", () => {
    render(
      <DenseDataTable
        ariaLabel="Positions"
        emptyText="No positions"
        columns={columns.slice(0, 1)}
        rows={rows}
        getRowId={(row) => row.id}
      />
    );
    expect(screen.queryByText("Columns")).toBeNull();
  });

  it("pins a table's own density and otherwise follows the workstation density", () => {
    const { container, rerender } = render(
      <DenseDataTable ariaLabel="Positions" emptyText="No positions" columns={columns.slice(0, 1)} rows={rows} getRowId={(row) => row.id} />
    );
    expect(container.querySelector(".dense-data-table-wrap")).not.toHaveAttribute("data-density");

    rerender(
      <DenseDataTable density="compact" ariaLabel="Positions" emptyText="No positions" columns={columns.slice(0, 1)} rows={rows} getRowId={(row) => row.id} />
    );
    expect(container.querySelector(".dense-data-table-wrap")).toHaveAttribute("data-density", "compact");
  });

  it("keeps virtualized tables within their fixed row height when spacious density is chosen", () => {
    // Windowing assumes every row is exactly rowHeight tall; spacious padding must not apply there.
    const css = readFileSync(resolve(process.cwd(), "src/styles/ui-kit-primitives.css"), "utf8");
    const rule = css.match(/:where\(body\[data-theme-density="spacious"\]\) \.dense-data-table-wrap-virtual \.dense-data-table,[^{]*\{([^}]*)\}/);
    expect(rule?.[1]).toContain("--dense-cell-py: 0.4375rem");
  });

  it("keeps header caps for short labels and sentence case for longer ones", () => {
    render(
      <DenseDataTable
        tableId="caps-test"
        ariaLabel="Positions"
        emptyText="No positions"
        columns={columns.map((column) => ({ ...column, defaultHidden: false }))}
        rows={rows}
        getRowId={(row) => row.id}
      />
    );
    expect(screen.getByRole("columnheader", { name: /Market value/ })).not.toHaveClass("dense-data-table-th-sentence");
    expect(screen.getByRole("columnheader", { name: /Cost basis source/ })).toHaveClass("dense-data-table-th-sentence");
  });
});

describe("sortRowsByValue", () => {
  it("compares strings naturally and is stable for ties", () => {
    const items = [
      { id: "1", name: "Fund 10" },
      { id: "2", name: "Fund 2" },
      { id: "3", name: "fund 2" }
    ];
    expect(sortRowsByValue(items, (item) => item.name, "asc").map((item) => item.id)).toEqual(["2", "3", "1"]);
    expect(sortRowsByValue(items, (item) => item.name, "desc").map((item) => item.id)).toEqual(["1", "2", "3"]);
  });

  it("puts NaN, null and undefined last in both directions", () => {
    const items = [{ v: Number.NaN }, { v: 3 }, { v: null }, { v: 1 }];
    expect(sortRowsByValue(items, (item) => item.v, "asc").map((item) => item.v)).toEqual([1, 3, Number.NaN, null]);
    expect(sortRowsByValue(items, (item) => item.v, "desc").map((item) => item.v)).toEqual([3, 1, Number.NaN, null]);
  });
});
