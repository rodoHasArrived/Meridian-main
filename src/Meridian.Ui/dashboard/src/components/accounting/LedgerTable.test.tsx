import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { vi } from "vitest";
import { LedgerTable, type LedgerRow } from "./LedgerTable";

const rows: LedgerRow[] = [
  { date: "2026-01-02", ref: "JE-1", memo: "Opening buy", debit: 1000, credit: 0 },
  { date: "2026-01-03", ref: "JE-2", memo: "Fee", debit: 0, credit: 250 }
];

describe("LedgerTable", () => {
  it("renders debit/credit totals in the footer", () => {
    render(<LedgerTable rows={rows} currency="USD" opening={0} />);
    const footer = screen.getByText("Totals").closest("tr")!;
    expect(within(footer).getByText("$1,000.00")).toBeInTheDocument();
    expect(within(footer).getByText("$250.00")).toBeInTheDocument();
  });

  it("computes a running balance from the opening value", () => {
    render(<LedgerTable rows={rows} currency="USD" opening={0} />);
    // opening 0 → +1000 → +1000 running, then -250 → 750 running balance in the last data row.
    const bodyRows = screen.getAllByRole("row").filter((r) => r.closest("tbody"));
    const lastRow = bodyRows[bodyRows.length - 1];
    expect(within(lastRow).getByText("$750.00")).toBeInTheDocument();
  });

  it("flags an imbalance in the footer when debits and credits disagree", () => {
    render(<LedgerTable rows={[{ date: "2026-01-04", debit: 100, credit: 0 }]} currency="USD" />);
    expect(screen.getByLabelText("Ledger imbalance")).toBeInTheDocument();
  });

  it("marks a balanced ledger", () => {
    render(<LedgerTable rows={[{ date: "2026-01-05", debit: 100, credit: 100 }]} currency="USD" />);
    expect(screen.getByLabelText("Ledger balanced")).toBeInTheDocument();
  });

  it("does not backfill running balances before the first explicit seed", () => {
    render(
      <LedgerTable
        rows={[
          { date: "2026-01-02", ref: "JE-1", memo: "Unknown seed", debit: 100, credit: 0 },
          { date: "2026-01-03", ref: "JE-2", memo: "Seeded", debit: 0, credit: 0, balance: 500 },
        ]}
        currency="USD"
      />,
    );
    const bodyRows = screen.getAllByRole("row").filter((r) => r.closest("tbody"));
    // The balance is the last cell; scope to it (zero credit also renders "—").
    const balanceCell = (row: HTMLElement) => within(row).getAllByRole("cell").at(-1)!;
    expect(balanceCell(bodyRows[0])).toHaveTextContent("—");
    expect(balanceCell(bodyRows[1])).toHaveTextContent("$500.00");
  });

  it("sorts only when an onSort handler is supplied and supports keyboard activation", async () => {
    const onSort = vi.fn();
    render(<LedgerTable rows={rows} currency="USD" onSort={onSort} />);
    screen.getByRole("columnheader", { name: "Date" }).focus();
    await userEvent.keyboard("{Enter}");
    expect(onSort).toHaveBeenCalledWith("date", 1);
  });
});
