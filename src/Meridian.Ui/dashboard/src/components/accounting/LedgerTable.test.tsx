import { render, screen, within } from "@testing-library/react";
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
});
