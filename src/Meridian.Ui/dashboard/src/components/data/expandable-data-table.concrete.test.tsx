import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
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

    // First cell in the first row is the expand chevron.
    const chevrons = document.querySelectorAll(".edt--expand");
    await userEvent.click(chevrons[1] as Element); // [0] is the header cell
    expect(screen.getByTestId("detail")).toHaveTextContent("Account 4000");
  });
});
