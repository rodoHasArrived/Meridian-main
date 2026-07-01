import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { ExpandableDataTable } from "./expandable-data-table";

interface Txn extends Record<string, unknown> {
  date: string;
  amount: string;
  account: string;
}

const columns = [
  { key: "date", label: "Date" },
  { key: "amount", label: "Amount", align: "right" as const },
];

const rows: Txn[] = [
  { date: "2026-06-01", amount: "1,000.00", account: "4000" },
  { date: "2026-06-02", amount: "250.00", account: "5100" },
];

describe("ExpandableDataTable (Concrete)", () => {
  it("renders rows without an expand column when expandable is omitted", () => {
    const { container } = render(<ExpandableDataTable columns={columns} rows={rows} />);
    expect(container.querySelector(".edt--expand")).toBeNull();
    expect(screen.getByText("2026-06-01")).toBeInTheDocument();
  });

  it("toggles a detail panel via the expand chevron", async () => {
    render(
      <ExpandableDataTable
        columns={columns}
        rows={rows}
        expandable={(row) => <div data-testid="detail">Account {row.account}</div>}
      />,
    );
    expect(screen.queryByTestId("detail")).toBeNull();

    await userEvent.click(screen.getByRole("button", { name: "Expand row 1" }));
    expect(screen.getByTestId("detail")).toHaveTextContent("Account 4000");
  });

  it("toggles a detail panel from the keyboard", async () => {
    render(
      <ExpandableDataTable
        columns={columns}
        rows={rows}
        expandable={(row) => <div data-testid="detail">Account {row.account}</div>}
      />,
    );
    screen.getByRole("button", { name: "Expand row 1" }).focus();
    await userEvent.keyboard("{Enter}");
    expect(screen.getByTestId("detail")).toHaveTextContent("Account 4000");
  });

  it("activates clickable rows from the keyboard", async () => {
    const onRowClick = vi.fn();
    render(<ExpandableDataTable columns={columns} rows={rows} onRowClick={onRowClick} />);
    screen.getByText("2026-06-02").closest("tr")?.focus();
    await userEvent.keyboard(" ");
    expect(onRowClick).toHaveBeenCalledWith(rows[1], 1);
  });
});
