import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { vi } from "vitest";
import { AgingTable, type AgingTableRow } from "./AgingTable";

const rows: AgingTableRow[] = [
  { id: "alpha", name: "Alpha Capital", ref: "INV-1", amounts: [100, 0, 25, 50, 0] },
  { id: "beta", name: "Beta Fund", ref: "INV-2", amounts: [0, 10, 0, 0, 90] }
];

describe("AgingTable", () => {
  it("renders bucket totals, shares, warning buckets, and selected row", () => {
    render(<AgingTable rows={rows} selectedRowId="beta" />);

    expect(screen.getByText("Alpha Capital")).toBeInTheDocument();
    const alphaRow = screen.getByText("Alpha Capital").closest("tr") as HTMLTableRowElement;
    const betaRow = screen.getByText("Beta Fund").closest("tr") as HTMLTableRowElement;
    expect(betaRow).toHaveAttribute("aria-selected", "true");
    expect(screen.getAllByText("$100.00").length).toBeGreaterThan(0);
    expect(within(betaRow).getByText("$90.00").closest("td")).toHaveClass("agt--late");
    expect(within(alphaRow).getByText("$50.00").closest("td")).toHaveClass("agt--warn");
    expect(screen.getByText("36.4%")).toBeInTheDocument();
  });

  it("renders zero amounts as dashes and handles empty totals", () => {
    render(<AgingTable rows={[{ id: "empty", name: "Empty", amounts: [0, null, undefined] }]} buckets={["Current", "Late", "Older"]} />);

    expect(screen.getByText("Empty")).toBeInTheDocument();
    expect(screen.getAllByText("—").length).toBeGreaterThanOrEqual(3);
    expect(screen.queryByText("%")).toBeNull();
  });

  it("selects rows by click and keyboard", async () => {
    const onRowSelect = vi.fn();
    render(<AgingTable rows={rows} onRowSelect={onRowSelect} />);

    const alphaRow = screen.getByText("Alpha Capital").closest("tr") as HTMLTableRowElement;
    await userEvent.click(alphaRow);
    expect(onRowSelect).toHaveBeenCalledWith(rows[0]);
    alphaRow.focus();
    await userEvent.keyboard(" ");
    expect(onRowSelect).toHaveBeenCalledTimes(2);
  });
});
