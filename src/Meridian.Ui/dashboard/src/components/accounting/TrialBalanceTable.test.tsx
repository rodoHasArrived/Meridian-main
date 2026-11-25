import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { vi } from "vitest";
import { TrialBalanceTable } from "./TrialBalanceTable";
import type { AccountingTrialBalanceRowViewModel } from "@/screens/accounting-screen.view-model";
import type { LedgerAmountSelection } from "@/types/ledger-amount-proof";

vi.mock("@/components/meridian/proof-drawer", () => ({
  LedgerAmountProofDrawer: ({ selection }: { selection: LedgerAmountSelection | null }) => selection
    ? <div role="dialog" aria-label="Amount passport">{selection.subjectId} · {selection.amount}</div> : null
}));

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
  it("states one shared unknown condition for all balances and totals while keeping the source passport accessible", async () => {
    const cash = { ...row("cash", "Cash", "Asset", 100), accountName: "Cash", balanceProofSubjectId: "trial-balance:cash",
      balanceProofScope: { tenantId: "tenant", companyId: "company", fundProfileId: "fund", ledgerBookId: "book", periodId: "period" },
      balanceProofCurrency: "USD", journalEntryIds: ["journal-cash"], ledgerEntryIds: ["line-cash"] };
    render(<TrialBalanceTable rows={[cash, row("receivables", "Receivables", "Asset", 25), row("payable", "Payables", "Liability", 125)]} />);
    const notice = screen.getByText(/Data condition unavailable\. A reconciliation data condition/);
    expect(notice).toBeVisible();
    expect(screen.getAllByText(/Data condition unavailable\./)).toHaveLength(1);
    expect(screen.queryByText("Data condition unavailable", { exact: true })).not.toBeInTheDocument();
    for (const value of screen.getAllByRole("group", { name: /Data condition unavailable/ })) {
      expect(value.getAttribute("aria-describedby")?.split(" ")).toContain(notice.id);
      expect(value).toHaveAccessibleDescription(/A reconciliation data condition has not been supplied/);
    }
    expect(screen.getAllByText("125.00 USD").length).toBeGreaterThan(1);
    const control = screen.getByRole("button", { name: "Open Amount passport for Cash net balance: 100.00 USD · Data condition unavailable" });
    expect(control).toHaveAccessibleDescription(/source has not supplied a reconciled data condition/);
    control.focus();
    await userEvent.keyboard("{Enter}");
    expect(screen.getByRole("dialog", { name: "Amount passport" })).toHaveTextContent("trial-balance:cash");
  });
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
    expect(screen.getAllByText("100.00 USD").length).toBeGreaterThan(0);
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

  it("makes the scroll region keyboard-focusable with an accessible name", () => {
    render(<TrialBalanceTable rows={[row("cash", "Cash", "Asset", 100)]} />);
    expect(screen.getByRole("region", { name: "Trial balance" })).toHaveAttribute("tabindex", "0");
  });
});
