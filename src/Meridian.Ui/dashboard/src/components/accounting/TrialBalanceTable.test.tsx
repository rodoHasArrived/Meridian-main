import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { vi } from "vitest";
import { TrialBalanceTable } from "./TrialBalanceTable";
import type { AccountingTrialBalanceRowViewModel } from "@/screens/accounting-screen.view-model";

function row(
  rowId: string,
  accountLabel: string,
  accountTypeLabel: string,
  balance: number,
  entryCount = 1
): AccountingTrialBalanceRowViewModel {
  return {
    rowId,
    accountLabel,
    accountTypeLabel,
    accountType: accountTypeLabel,
    basisLabel: "Accrual",
    basisTone: "default",
    policyLabel: "GAAP",
    dimensionLabel: "Fund I",
    dimensionDetailLabel: "Primary dimension",
    balance,
    balanceLabel: `$${balance}`,
    balanceTone: "default",
    entryCount,
    entryCountLabel: String(entryCount),
    ariaLabel: `${accountLabel} ${accountTypeLabel}`,
    selectAriaLabel: `Select ${accountLabel}`,
    detailPanelId: `${rowId}-detail`,
    isExpanded: false,
    financialAccountId: `acct-${rowId}`,
    symbol: null,
    security: null
  } as AccountingTrialBalanceRowViewModel;
}

describe("TrialBalanceTable", () => {
  it("renders grouped sections, debit and credit totals, and selected row", () => {
    render(
      <TrialBalanceTable
        rows={[
          row("cash", "Cash", "Asset", 100),
          row("payable", "Payables", "Liability", 40),
          row("equity", "Equity", "Equity", 60)
        ]}
        selectedRowId="payable"
      />
    );

    expect(screen.getByText("Assets")).toBeInTheDocument();
    expect(screen.getByText("Liabilities")).toBeInTheDocument();
    expect(screen.getAllByText("Equity").length).toBeGreaterThan(0);
    expect(screen.getByText("Payables").closest("tr")).toHaveAttribute("aria-selected", "true");
    expect(screen.getAllByText("$100.00").length).toBeGreaterThan(0);
  });

  it("flags out-of-balance totals", () => {
    render(<TrialBalanceTable rows={[row("cash", "Cash", "Asset", 125), row("payable", "Payables", "Liability", 40)]} />);

    expect(screen.getByText(/out of balance by/i)).toBeInTheDocument();
  });

  it("selects rows by click and keyboard", async () => {
    const onRowSelect = vi.fn();
    render(<TrialBalanceTable rows={[row("cash", "Cash", "Asset", 100)]} onRowSelect={onRowSelect} />);

    const cashRow = screen.getByRole("row", { name: "Cash Asset" });
    await userEvent.click(cashRow);
    expect(onRowSelect).toHaveBeenCalledWith(expect.objectContaining({ rowId: "cash" }));
    cashRow.focus();
    await userEvent.keyboard("{Enter}");
    expect(onRowSelect).toHaveBeenCalledTimes(2);
  });

  it("can render ungrouped rows", () => {
    render(<TrialBalanceTable rows={[row("cash", "Cash", "Asset", 100)]} grouped={false} />);
    const table = screen.getByRole("region", { name: "Trial balance" });
    expect(within(table).queryByText("Assets")).toBeNull();
  });

  it("keeps the financial account identifier out of the default table reading path", () => {
    const cashRow = row("cash", "Cash", "Asset", 100);
    cashRow.financialAccountId = "acct-cash-internal";

    render(<TrialBalanceTable rows={[cashRow]} />);

    const table = screen.getByRole("region", { name: "Trial balance" });
    expect(within(table).getByText("Cash")).toBeInTheDocument();
    expect(within(table).getByText("Fund I")).toBeInTheDocument();
    expect(within(table).queryByText("acct-cash-internal")).not.toBeInTheDocument();
  });
});
