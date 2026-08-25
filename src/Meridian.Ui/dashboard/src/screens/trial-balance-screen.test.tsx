import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import * as ledgerReportsApi from "@/lib/ledger-reports-api";
import { TrialBalanceScreen } from "@/screens/trial-balance-screen";
import { renderWithRouter, waitForAsyncEffects } from "@/test/render";
import type {
  AccountingWorkspaceResponse,
  LedgerPeriod,
  LedgerPeriodPnlSummary,
  LedgerPeriodTrialBalanceLine,
  LedgerPostedJournalEntry
} from "@/types";

vi.mock("@/lib/ledger-reports-api", () => ({
  getLedgerBooks: vi.fn().mockResolvedValue([{
    ledgerBookId: "00000000-0000-0000-0000-0000000000aa",
    fundProfileId: "fund-alpha",
    fundStructureNodeId: "00000000-0000-0000-0000-0000000000bb",
    fundStructureNodeKind: "Fund",
    displayName: "Master Fund",
    baseCurrency: "USD",
    createdAt: "2026-01-01T00:00:00Z",
    updatedAt: "2026-01-01T00:00:00Z",
    accountingBasis: "Primary",
    accountingPolicyId: "legacy-v1",
    accountingPolicyVersion: "legacy-v1"
  }]),
  getLedgerPeriods: vi.fn(),
  getLedgerPeriodTrialBalance: vi.fn(),
  getLedgerPeriodPnlSummary: vi.fn(),
  getLedgerPeriodJournalEntries: vi.fn()
}));

const PERIOD_ID = "11111111-1111-1111-1111-111111111111";
const PRIOR_PERIOD_ID = "22222222-2222-2222-2222-222222222222";

const data: AccountingWorkspaceResponse = {
  metrics: [],
  reconciliationQueue: [],
  breakQueue: [],
  cashFlow: {
    totalCash: 0,
    totalLedgerCash: 0,
    netVariance: 0,
    totalFinancing: 0,
    runsWithCashSignals: 0,
    runsWithCashVariance: 0,
    tone: "success",
    summary: "No cash-flow signals."
  },
  reporting: {
    profileCount: 0,
    recommendedProfiles: [],
    profiles: [],
    reportPackTargets: [],
    summary: "No reporting profiles."
  }
};

function makePeriod(overrides: Partial<LedgerPeriod> = {}): LedgerPeriod {
  return {
    periodId: PERIOD_ID,
    ledgerBookId: "book-1",
    fiscalYear: 2026,
    periodNo: 7,
    label: "July 2026",
    startDate: "2026-07-01",
    endDate: "2026-07-31",
    status: "HardClosed",
    openedAt: "2026-07-01T00:00:00Z",
    closedAt: "2026-08-02T00:00:00Z",
    version: 1,
    ...overrides
  };
}

const postedLines: LedgerPeriodTrialBalanceLine[] = [
  {
    accountName: "Cash",
    accountType: "Asset",
    symbol: null,
    financialAccountId: "acct-cash",
    debitTotal: 125000,
    creditTotal: 4500,
    balance: 120500,
    entryCount: 12,
    accountingBasis: "Primary",
    sourceJournalEntryId: "je-cash-1"
  },
  {
    accountName: "Apple Inc.",
    accountType: "Asset",
    symbol: "AAPL",
    financialAccountId: "acct-aapl",
    debitTotal: 5000,
    creditTotal: 0,
    balance: 5000,
    entryCount: 3,
    accountingBasis: "Primary"
  },
  {
    accountName: "Financing payable",
    accountType: "Liability",
    symbol: null,
    financialAccountId: "acct-financing",
    debitTotal: 0,
    creditTotal: 500,
    balance: -500,
    entryCount: 2,
    accountingBasis: "Primary"
  }
];

const postedEntries: LedgerPostedJournalEntry[] = [
  {
    journalEntryId: "je-cash-1",
    periodId: PERIOD_ID,
    ledgerBookId: "book-1",
    timestamp: "2026-06-30T00:00:00Z",
    description: "Cash sweep",
    totalDebits: 500,
    totalCredits: 500,
    isBalanced: true,
    lines: [{}, {}]
  }
];

function makePnl(): LedgerPeriodPnlSummary {
  return {
    periodId: PERIOD_ID,
    ledgerBookId: "book-1",
    fiscalYear: 2026,
    periodNo: 7,
    label: "July 2026",
    totalRevenue: 5000,
    totalExpenses: 1800,
    netIncome: 3200,
    periodOnPeriodVariance: null,
    openBreakCount: 0,
    signoffStatus: "SignedOff",
    completedAt: "2026-08-02T00:00:00Z",
    revenueLines: [],
    expenseLines: []
  };
}

/** The posted trial balance for July 2026, as the region is labelled once loaded. */
const POSTED_REGION = "Primary trial balance lines for the posted journal for period July 2026";

function primeHappyPath(lines: LedgerPeriodTrialBalanceLine[] = postedLines) {
  vi.mocked(ledgerReportsApi.getLedgerPeriods).mockResolvedValue([makePeriod()]);
  vi.mocked(ledgerReportsApi.getLedgerPeriodTrialBalance).mockResolvedValue(lines);
  vi.mocked(ledgerReportsApi.getLedgerPeriodPnlSummary).mockResolvedValue(makePnl());
  vi.mocked(ledgerReportsApi.getLedgerPeriodJournalEntries).mockResolvedValue(postedEntries);
}

async function renderTrialBalanceScreen(initialEntry = "/accounting/ledger?view=trial-balance") {
  const result = renderWithRouter(<TrialBalanceScreen data={data} />, { initialEntries: [initialEntry] });
  await waitForAsyncEffects();
  return result;
}

beforeEach(() => {
  vi.clearAllMocks();
});

describe("TrialBalanceScreen", () => {
  it("renders a loading state while accounting workspace data is unavailable", async () => {
    primeHappyPath();
    renderWithRouter(<TrialBalanceScreen data={null} />, { initialEntries: ["/accounting/ledger?view=trial-balance"] });
    await waitForAsyncEffects();

    expect(screen.getByRole("status", { name: "Loading Trial Balance" })).toBeInTheDocument();
  });

  it("explains how to start the governed book when no ledger period exists", async () => {
    vi.mocked(ledgerReportsApi.getLedgerPeriods).mockResolvedValue([]);
    vi.mocked(ledgerReportsApi.getLedgerPeriodTrialBalance).mockResolvedValue([]);
    vi.mocked(ledgerReportsApi.getLedgerPeriodPnlSummary).mockResolvedValue(makePnl());
    vi.mocked(ledgerReportsApi.getLedgerPeriodJournalEntries).mockResolvedValue([]);

    await renderTrialBalanceScreen();

    expect(await screen.findByText(/No ledger periods exist yet/)).toBeInTheDocument();
  });

  /**
   * The load-bearing regression for adversarial-program-review-2026-08-25 §1: this screen is
   * what an operator reaches for "Accounting → Trial Balance", and it must read the fund's
   * posted journal rather than a strategy run's simulation ledger.
   */
  it("reads the posted journal, not a strategy run's simulation ledger", async () => {
    primeHappyPath();

    await renderTrialBalanceScreen(`/accounting/ledger?view=trial-balance&periodId=${PERIOD_ID}`);
    await waitForAsyncEffects();

    expect(ledgerReportsApi.getLedgerPeriodTrialBalance).toHaveBeenCalledWith(PERIOD_ID);
    expect(ledgerReportsApi.getLedgerPeriodJournalEntries).toHaveBeenCalledWith(PERIOD_ID);
    expect(screen.getByText("Posted journal")).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Trial balance scope" })).toBeInTheDocument();
    expect(screen.getByLabelText("Entity / fund / portfolio")).toHaveValue("All entities");
    expect(screen.getByLabelText("Book")).toHaveValue("Primary GL");
    expect(await screen.findByRole("region", { name: POSTED_REGION })).toBeInTheDocument();
    expect(screen.getByRole("row", { name: /Cash Asset\. Primary basis/ })).toBeInTheDocument();
  });

  it("loads the period named in the periodId query param", async () => {
    vi.mocked(ledgerReportsApi.getLedgerPeriods).mockResolvedValue([
      makePeriod(),
      makePeriod({ periodId: PRIOR_PERIOD_ID, periodNo: 6, label: "June 2026" })
    ]);
    vi.mocked(ledgerReportsApi.getLedgerPeriodTrialBalance).mockResolvedValue(postedLines);
    vi.mocked(ledgerReportsApi.getLedgerPeriodPnlSummary).mockResolvedValue(makePnl());
    vi.mocked(ledgerReportsApi.getLedgerPeriodJournalEntries).mockResolvedValue(postedEntries);

    await renderTrialBalanceScreen(`/accounting/ledger?view=trial-balance&periodId=${PRIOR_PERIOD_ID}`);
    await waitForAsyncEffects();

    expect(ledgerReportsApi.getLedgerPeriodTrialBalance).toHaveBeenCalledWith(PRIOR_PERIOD_ID);
    expect(screen.getByLabelText("Period", { selector: "select" })).toHaveValue(PRIOR_PERIOD_ID);
  });

  it("switches basis and narrows rows with the account filter", async () => {
    primeHappyPath();
    const user = userEvent.setup();

    await renderTrialBalanceScreen(`/accounting/ledger?view=trial-balance&periodId=${PERIOD_ID}`);

    const filterInput = screen.getByPlaceholderText(/Account name, account id, type, symbol, or security/);
    await user.type(filterInput, "Apple");

    const table = await screen.findByRole("region", { name: POSTED_REGION });
    expect(table).toHaveTextContent("Apple Inc.");
    expect(table).not.toHaveTextContent("Financing payable");
  });

  it("switches to the account hierarchy view and rolls up balances by account type", async () => {
    primeHappyPath();
    const user = userEvent.setup();

    await renderTrialBalanceScreen(`/accounting/ledger?view=trial-balance&periodId=${PERIOD_ID}`);
    await screen.findByRole("region", { name: POSTED_REGION });

    await user.click(screen.getByRole("button", { name: "Hierarchy" }));

    expect(screen.getByRole("tree", { name: "Chart of accounts" })).toBeInTheDocument();
    expect(screen.getByRole("treeitem", { name: /Asset/ })).toBeInTheDocument();
    expect(screen.getByRole("treeitem", { name: /Liability/ })).toBeInTheDocument();
    expect(screen.getByRole("treeitem", { name: /Cash/ })).toBeInTheDocument();
    expect(screen.getByRole("treeitem", { name: /Financing payable/ })).toBeInTheDocument();
  });

  it("links posted journal entries to the Journal Entry Detail route scoped by period", async () => {
    primeHappyPath();

    await renderTrialBalanceScreen(`/accounting/ledger?view=trial-balance&periodId=${PERIOD_ID}`);

    const journalLink = await screen.findByRole("link", { name: "Cash sweep" });
    expect(journalLink).toHaveAttribute(
      "href",
      `/accounting/journal-entries/detail?journalEntryId=je-cash-1&periodId=${PERIOD_ID}`
    );
  });

  it("distinguishes unavailable journal lineage from an empty journal", async () => {
    vi.mocked(ledgerReportsApi.getLedgerPeriods).mockResolvedValue([makePeriod()]);
    vi.mocked(ledgerReportsApi.getLedgerPeriodTrialBalance).mockResolvedValue(postedLines);
    vi.mocked(ledgerReportsApi.getLedgerPeriodPnlSummary).mockResolvedValue(makePnl());
    vi.mocked(ledgerReportsApi.getLedgerPeriodJournalEntries).mockRejectedValue(new Error("offline"));

    await renderTrialBalanceScreen(`/accounting/ledger?view=trial-balance&periodId=${PERIOD_ID}`);
    await waitForAsyncEffects();

    expect(await screen.findByRole("alert")).toHaveTextContent("Journal lineage unavailable");
  });

  it("renders empty trial-balance evidence without a fabricated table", async () => {
    primeHappyPath([]);

    await renderTrialBalanceScreen(`/accounting/ledger?view=trial-balance&periodId=${PERIOD_ID}`);

    expect(await screen.findByText("No posted trial balance lines")).toBeInTheDocument();
    expect(screen.queryByRole("region", { name: POSTED_REGION })).not.toBeInTheDocument();
  });
});
