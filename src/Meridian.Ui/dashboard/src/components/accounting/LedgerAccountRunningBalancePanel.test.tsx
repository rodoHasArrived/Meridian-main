import { render, screen } from "@testing-library/react";
import * as ledgerReportsApi from "@/lib/ledger-reports-api";
import { LedgerAccountRunningBalancePanel } from "@/components/accounting/LedgerAccountRunningBalancePanel";
import type { LedgerAccountRunningBalance } from "@/types";

vi.mock("@/lib/ledger-reports-api", () => ({
  getLedgerPeriodAccountRunningBalance: vi.fn()
}));

const PERIOD_ID = "11111111-1111-1111-1111-111111111111";

function makeRunningBalance(overrides: Partial<LedgerAccountRunningBalance> = {}): LedgerAccountRunningBalance {
  return {
    periodId: PERIOD_ID,
    ledgerBookId: "00000000-0000-0000-0000-0000000000aa",
    accountName: "Management fees",
    accountType: "Revenue",
    symbol: null,
    financialAccountId: null,
    totalDebits: 0,
    totalCredits: 300,
    endingBalance: 300,
    points: [
      {
        journalEntryId: "j-1",
        entryId: "e-1",
        globalSequence: 1,
        timestamp: "2026-06-30T21:00:00Z",
        description: "June management fee",
        debit: 0,
        credit: 100,
        balance: 100
      },
      {
        journalEntryId: "j-2",
        entryId: "e-2",
        globalSequence: 2,
        timestamp: "2026-06-30T22:00:00Z",
        description: "Parallel fund fee",
        debit: 0,
        credit: 200,
        balance: 300
      }
    ],
    ...overrides
  };
}

const formatAmount = (value: number) => `$${value.toFixed(2)}`;

describe("LedgerAccountRunningBalancePanel", () => {
  beforeEach(() => {
    vi.mocked(ledgerReportsApi.getLedgerPeriodAccountRunningBalance).mockReset();
  });

  it("requests the selected account scope and lists each posting with its running balance", async () => {
    vi.mocked(ledgerReportsApi.getLedgerPeriodAccountRunningBalance).mockResolvedValue(makeRunningBalance());

    render(
      <LedgerAccountRunningBalancePanel
        periodId={PERIOD_ID}
        accountName="Management fees"
        symbol="AAPL"
        formatAmount={formatAmount}
      />
    );

    expect(await screen.findByText("June management fee")).toBeInTheDocument();
    expect(ledgerReportsApi.getLedgerPeriodAccountRunningBalance).toHaveBeenCalledWith(PERIOD_ID, {
      account: "Management fees",
      symbol: "AAPL",
      financialAccountId: null
    });
    expect(screen.getByRole("region", { name: "Postings to Management fees" })).toBeInTheDocument();
    expect(screen.getAllByRole("row")).toHaveLength(3);
    expect(screen.getByText(/2 postings; ending balance/)).toHaveTextContent("$300.00");
  });

  it("states when the postings combine several trial-balance rows of the account", async () => {
    vi.mocked(ledgerReportsApi.getLedgerPeriodAccountRunningBalance).mockResolvedValue(makeRunningBalance());

    render(
      <LedgerAccountRunningBalancePanel
        periodId={PERIOD_ID}
        accountName="Management fees"
        combinedAcrossScopes
        formatAmount={formatAmount}
      />
    );

    expect(await screen.findByText(/cover all\s+of its dimension scopes/)).toBeInTheDocument();
  });

  it("says so when the account has no postings in the period", async () => {
    vi.mocked(ledgerReportsApi.getLedgerPeriodAccountRunningBalance).mockResolvedValue(
      makeRunningBalance({ points: [], totalCredits: 0, endingBalance: 0 })
    );

    render(
      <LedgerAccountRunningBalancePanel periodId={PERIOD_ID} accountName="Management fees" formatAmount={formatAmount} />
    );

    expect(await screen.findByText("No postings to this account in the selected period.")).toBeInTheDocument();
  });

  it("reports a load failure instead of an empty history", async () => {
    vi.mocked(ledgerReportsApi.getLedgerPeriodAccountRunningBalance).mockRejectedValue(new Error("Ledger store offline"));

    render(
      <LedgerAccountRunningBalancePanel periodId={PERIOD_ID} accountName="Management fees" formatAmount={formatAmount} />
    );

    expect(await screen.findByRole("alert")).toBeInTheDocument();
    expect(screen.queryByText("No postings to this account in the selected period.")).not.toBeInTheDocument();
  });
});
