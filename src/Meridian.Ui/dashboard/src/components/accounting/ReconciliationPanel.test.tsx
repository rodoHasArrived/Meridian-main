import { fireEvent, render, screen, within } from "@testing-library/react";
import { vi } from "vitest";
import { ReconciliationPanel, type ReconciliationSide } from "./ReconciliationPanel";

const statement: ReconciliationSide = {
  title: "Statement",
  items: [
    { id: "s1", date: "2026-06-01", ref: "TXN-1", memo: "Wire in", amount: 1000, matched: true },
    { id: "s2", date: "2026-06-02", ref: "TXN-2", memo: "Fee", amount: -50, matched: false }
  ]
};

const ledger: ReconciliationSide = {
  title: "Ledger",
  items: [{ id: "l1", date: "2026-06-01", ref: "JE-1", memo: "Deposit", amount: 1000, matched: true }]
};

describe("ReconciliationPanel", () => {
  it("flags 'Out by …' when the two sides disagree", () => {
    render(<ReconciliationPanel left={statement} right={ledger} currency="USD" />);
    // statement 950 vs ledger 1000 → out by 50
    expect(screen.getByText(/Out by/)).toBeInTheDocument();
  });

  it("marks reconciled when balances match within tolerance", () => {
    const balanced: ReconciliationSide = { title: "Ledger", items: [{ id: "l1", amount: 950, matched: true }] };
    render(<ReconciliationPanel left={statement} right={balanced} currency="USD" />);
    expect(screen.getByText("Reconciled")).toBeInTheDocument();
  });

  it("fires onToggleItem when a match checkbox changes", () => {
    const onToggleItem = vi.fn();
    render(<ReconciliationPanel left={statement} right={ledger} currency="USD" onToggleItem={onToggleItem} />);
    const box = screen.getByLabelText("Match Statement item TXN-2");
    fireEvent.click(box);
    expect(onToggleItem).toHaveBeenCalledWith("s2", true);
  });

  it("filters to open items", () => {
    render(<ReconciliationPanel left={statement} right={ledger} currency="USD" />);
    fireEvent.click(screen.getByRole("button", { name: "Open" }));
    // the statement side should still show its open Fee row
    expect(screen.getByText("Fee")).toBeInTheDocument();
    // the ledger side (all matched) should now be empty
    expect(screen.getAllByText("No matching items").length).toBeGreaterThan(0);
  });

  it("totals each side over the full data set", () => {
    render(<ReconciliationPanel left={statement} right={ledger} currency="USD" />);
    const summary = screen.getByText("Statement balance").closest(".rec__sumcell") as HTMLElement;
    expect(within(summary).getByText("$950.00")).toBeInTheDocument();
  });
});
