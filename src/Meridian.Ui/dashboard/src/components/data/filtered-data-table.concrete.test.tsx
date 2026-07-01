import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { FilteredDataTable } from "./filtered-data-table";

interface Row extends Record<string, unknown> {
  symbol: string;
  sector: string;
}

const columns = [
  { key: "symbol", label: "Symbol" },
  { key: "sector", label: "Sector" },
];

const data: Row[] = [
  { symbol: "AAPL", sector: "Tech" },
  { symbol: "XOM", sector: "Energy" },
  { symbol: "MSFT", sector: "Tech" },
];

describe("FilteredDataTable (Concrete)", () => {
  it("renders a title, the result count, and all rows", () => {
    render(<FilteredDataTable title="Universe" data={data} columns={columns} />);
    expect(screen.getByText("Universe")).toBeInTheDocument();
    expect(screen.getByText("3 of 3")).toBeInTheDocument();
    expect(screen.getByText("AAPL")).toBeInTheDocument();
  });

  it("filters rows via the search box and updates the count", async () => {
    render(<FilteredDataTable data={data} columns={columns} />);
    await userEvent.type(screen.getByLabelText("Search all fields"), "energy");
    expect(screen.getByText("XOM")).toBeInTheDocument();
    expect(screen.queryByText("AAPL")).toBeNull();
    expect(screen.getByText("1 of 3")).toBeInTheDocument();
  });

  it("shows a no-results message when the search matches nothing", async () => {
    render(<FilteredDataTable data={data} columns={columns} />);
    await userEvent.type(screen.getByLabelText("Search all fields"), "zzz");
    expect(screen.getByText("No results match your search")).toBeInTheDocument();
  });

  it("sorts by a column header click", async () => {
    render(<FilteredDataTable data={data} columns={columns} />);
    await userEvent.click(screen.getByText("Symbol"));
    const cells = screen.getAllByRole("cell").map((c) => c.textContent);
    // Ascending: AAPL first.
    expect(cells[0]).toBe("AAPL");
  });
});
