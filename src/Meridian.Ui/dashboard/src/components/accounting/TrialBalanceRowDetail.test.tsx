import { render, screen, within } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { AccountingTrialBalanceSelectedDetailPanel } from "./TrialBalanceRowDetail";
import type { AccountingTrialBalanceDetailViewState } from "@/screens/accounting-screen.view-model";

function detail(): AccountingTrialBalanceDetailViewState {
  return {
    eyebrow: "Trial-balance detail",
    title: "Cash",
    subtitle: "Asset · Accrual basis",
    description: "Cash contributes $100.00 across one retained ledger entry.",
    statusLabel: "Debit / asset",
    statusVariant: "success",
    ariaLabel: "Trial-balance detail for Cash",
    fields: [
      { label: "Account type", value: "Asset" },
      { label: "Basis", value: "Accrual basis" },
      { label: "Policy", value: "legacy-v1/legacy-v1" },
      { label: "Balance", value: "$100.00" },
      { label: "Entries", value: "1" },
      { label: "Financial account", value: "acct-cash" },
      { label: "Dimensions", value: "Fund I" },
      { label: "Security", value: "No linked security" },
      { label: "Journal entries", value: "journal-42" },
      { label: "Source events", value: "source-event-42" },
      { label: "Approvals", value: "approval-42" },
      { label: "Run", value: "run-42" }
    ],
    auditDrillThroughLabel: "Open source evidence",
    auditDrillThroughHref: null,
    approvalDrillThroughHref: null,
    ledgerLinesTitle: "Ledger lines for selected account",
    ledgerLinesDescription: "Journal support linked to Cash.",
    ledgerLines: [
      {
        rowId: "cash-journal-42",
        journalEntryId: "journal-42",
        description: "Cash posting",
        debitLabel: "$100.00",
        creditLabel: "$0.00",
        balanceLabel: "$100.00",
        evidenceLabel: "No source-event drill-through available",
        evidenceHref: null,
        approvalHref: null,
        ariaLabel: "Cash posting retained journal line"
      }
    ],
    ledgerLinesEmptyText: "No ledger line support is attached to this account row yet.",
    supportingDocumentsTitle: "Supporting documentation",
    supportingDocuments: [],
    supportingDocumentsEmptyText: "No supporting documentation is attached to this account row yet."
  };
}

describe("AccountingTrialBalanceSelectedDetailPanel", () => {
  it("keeps policy and record identifiers in collapsed technical details", () => {
    render(
      <MemoryRouter>
        <AccountingTrialBalanceSelectedDetailPanel panelId="cash-detail" detail={detail()} />
      </MemoryRouter>
    );

    const summary = screen.getByRole("region", { name: "Trial-balance detail for Cash" });
    expect(within(summary).getByText("Account type")).toBeInTheDocument();
    expect(within(summary).getByText("Fund I")).toBeInTheDocument();
    expect(within(summary).queryByText("Policy")).not.toBeInTheDocument();
    expect(within(summary).queryByText("run-42")).not.toBeInTheDocument();

    const recordIdentifiers = screen.getByText("Record identifiers").closest("details");
    expect(recordIdentifiers).not.toHaveAttribute("open");
    expect(within(recordIdentifiers!).getByText("legacy-v1/legacy-v1")).toBeInTheDocument();
    expect(within(recordIdentifiers!).getByText("acct-cash")).toBeInTheDocument();
    expect(within(recordIdentifiers!).getByText("run-42")).toBeInTheDocument();
    expect(within(recordIdentifiers!).getByText("source-event-42")).toBeInTheDocument();
    expect(within(recordIdentifiers!).getByText("approval-42")).toBeInTheDocument();
  });

  it("keeps the retained posting identifier behind collapsed disclosure", () => {
    render(
      <MemoryRouter>
        <AccountingTrialBalanceSelectedDetailPanel panelId="cash-detail" detail={detail()} />
      </MemoryRouter>
    );

    expect(screen.getByText("Retained journal posting")).toBeInTheDocument();
    const postingReference = screen.getByText("Posting reference").closest("details");
    expect(postingReference).not.toHaveAttribute("open");
    expect(within(postingReference!).getByText("journal-42")).toBeInTheDocument();
  });
});
