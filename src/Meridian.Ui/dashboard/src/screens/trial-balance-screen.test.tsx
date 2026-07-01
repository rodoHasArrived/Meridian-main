import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import * as api from "@/lib/api";
import { TrialBalanceScreen } from "@/screens/trial-balance-screen";
import { renderWithRouter, waitForAsyncEffects } from "@/test/render";
import type { AccountingWorkspaceResponse, LedgerJournalLine, LedgerTrialBalanceLine } from "@/types";

vi.mock("@/lib/api", async () => {
  const actual = await vi.importActual<typeof import("@/lib/api")>("@/lib/api");
  return {
    ...actual,
    getRunTrialBalance: vi.fn(),
    getRunLedgerJournal: vi.fn()
  };
});

const data: AccountingWorkspaceResponse = {
  metrics: [],
  reconciliationQueue: [
    {
      runId: "run-42",
      strategyName: "Paper Index Mean Reversion",
      mode: "paper",
      status: "Running",
      lastUpdated: "3m ago",
      breakCount: 2,
      openBreakCount: 1,
      reconciliationStatus: "BreaksOpen"
    },
    {
      runId: "run-57",
      strategyName: "Intraday Vol Carry",
      mode: "paper",
      status: "Paused",
      lastUpdated: "7m ago",
      breakCount: 1,
      openBreakCount: 0,
      reconciliationStatus: "Resolved"
    }
  ],
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

const trialBalanceLines: LedgerTrialBalanceLine[] = [
  {
    accountName: "Cash",
    accountType: "Asset",
    symbol: null,
    financialAccountId: "acct-cash",
    balance: 120500,
    entryCount: 12,
    security: null,
    sourceJournalEntryId: "je-cash-1"
  },
  {
    accountName: "Apple Inc.",
    accountType: "Asset",
    symbol: "AAPL",
    financialAccountId: "acct-aapl",
    balance: 5000,
    entryCount: 3,
    security: {
      securityId: "sec-aapl",
      displayName: "Apple Inc.",
      assetClass: "Equity",
      currency: "USD",
      status: "Active",
      primaryIdentifier: "AAPL",
      subType: null
    }
  },
  {
    accountName: "Financing payable",
    accountType: "Liability",
    symbol: null,
    financialAccountId: "acct-financing",
    balance: -500,
    entryCount: 2,
    security: null
  }
];

const journalLines: LedgerJournalLine[] = [
  {
    journalEntryId: "je-cash-1",
    timestamp: "2026-06-30T00:00:00Z",
    description: "Cash sweep",
    totalDebits: 500,
    totalCredits: 500,
    lineCount: 2
  }
];

async function renderTrialBalanceScreen(initialEntry = "/accounting/trial-balance") {
  const result = renderWithRouter(<TrialBalanceScreen data={data} />, { initialEntries: [initialEntry] });
  await waitForAsyncEffects();
  return result;
}

describe("TrialBalanceScreen", () => {
  it("renders a loading state while accounting workspace data is unavailable", async () => {
    renderWithRouter(<TrialBalanceScreen data={null} />, { initialEntries: ["/accounting/trial-balance"] });
    await waitForAsyncEffects();

    expect(screen.getByRole("status", { name: "Loading Trial Balance" })).toBeInTheDocument();
  });

  it("renders an empty state when there are no reconciliation runs", async () => {
    renderWithRouter(<TrialBalanceScreen data={{ ...data, reconciliationQueue: [] }} />, {
      initialEntries: ["/accounting/trial-balance"]
    });
    await waitForAsyncEffects();

    expect(screen.getByText("No reconciliation runs are available for this accounting scope.")).toBeInTheDocument();
  });

  it("loads the trial balance for the run named in the runId query param", async () => {
    vi.mocked(api.getRunTrialBalance).mockResolvedValueOnce(trialBalanceLines);
    vi.mocked(api.getRunLedgerJournal).mockResolvedValueOnce(journalLines);

    await renderTrialBalanceScreen("/accounting/trial-balance?runId=run-42");
    await waitForAsyncEffects();

    expect(api.getRunTrialBalance).toHaveBeenCalledWith("run-42");
    expect(screen.getByRole("heading", { name: "Trial balance scope" })).toBeInTheDocument();
    expect(screen.getByLabelText("Entity / fund / portfolio")).toHaveValue("All entities");
    expect(screen.getByLabelText("Book")).toHaveValue("Primary GL");
    expect(screen.getByLabelText("Period")).toHaveValue("Current period");
    expect(screen.getByRole("button", { name: "Compare prior period" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Export" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Jump to report preview" })).toHaveAttribute("href", "/reporting/preview");
    expect(await screen.findByRole("treegrid", { name: "Primary trial balance lines for run-42" })).toBeInTheDocument();
    expect(screen.getByRole("row", { name: "Inspect trial-balance account Cash for Asset" })).toBeInTheDocument();
  });

  it("switches basis and narrows rows with the account filter", async () => {
    vi.mocked(api.getRunTrialBalance).mockResolvedValueOnce(trialBalanceLines);
    vi.mocked(api.getRunLedgerJournal).mockResolvedValueOnce(journalLines);
    const user = userEvent.setup();

    await renderTrialBalanceScreen("/accounting/trial-balance?runId=run-42");

    const filterInput = screen.getByPlaceholderText(/Account name, account id, type, symbol, or security/);
    await user.type(filterInput, "Apple");

    const table = await screen.findByRole("treegrid", { name: "Primary trial balance lines for run-42" });
    expect(table).toHaveTextContent("Apple Inc.");
    expect(table).not.toHaveTextContent("Financing payable");
  });

  it("switches to the account hierarchy view and rolls up balances by account type", async () => {
    vi.mocked(api.getRunTrialBalance).mockResolvedValueOnce(trialBalanceLines);
    vi.mocked(api.getRunLedgerJournal).mockResolvedValueOnce(journalLines);
    const user = userEvent.setup();

    await renderTrialBalanceScreen("/accounting/trial-balance?runId=run-42");
    await screen.findByRole("treegrid", { name: "Primary trial balance lines for run-42" });

    await user.click(screen.getByRole("button", { name: "Hierarchy" }));

    expect(screen.getByRole("tree", { name: "Chart of accounts" })).toBeInTheDocument();
    expect(screen.getByRole("treeitem", { name: /Asset/ })).toBeInTheDocument();
    expect(screen.getByRole("treeitem", { name: /Liability/ })).toBeInTheDocument();
    expect(screen.getByRole("treeitem", { name: /Cash/ })).toBeInTheDocument();
    expect(screen.getByRole("treeitem", { name: /Financing payable/ })).toBeInTheDocument();
  });

  it("links journal entries for the selected run to the Journal Entry Detail route", async () => {
    vi.mocked(api.getRunTrialBalance).mockResolvedValueOnce(trialBalanceLines);
    vi.mocked(api.getRunLedgerJournal).mockResolvedValueOnce(journalLines);

    await renderTrialBalanceScreen("/accounting/trial-balance?runId=run-42");

    const journalLink = await screen.findByRole("link", { name: "Cash sweep" });
    expect(journalLink).toHaveAttribute(
      "href",
      "/accounting/journal-entries/detail?journalEntryId=je-cash-1&runId=run-42"
    );
  });

  it("links related securities to the Asset Detail route", async () => {
    vi.mocked(api.getRunTrialBalance).mockResolvedValueOnce(trialBalanceLines);
    vi.mocked(api.getRunLedgerJournal).mockResolvedValueOnce(journalLines);

    await renderTrialBalanceScreen("/accounting/trial-balance?runId=run-42");

    const securityLink = await screen.findByRole("link", { name: "Apple Inc." });
    expect(securityLink).toHaveAttribute("href", "/accounting/security-master/detail?securityId=sec-aapl");
  });

  it("renders empty trial-balance evidence without a fabricated table", async () => {
    vi.mocked(api.getRunTrialBalance).mockResolvedValueOnce([]);
    vi.mocked(api.getRunLedgerJournal).mockResolvedValueOnce([]);

    await renderTrialBalanceScreen("/accounting/trial-balance?runId=run-42");

    expect(await screen.findByText("No trial balance lines")).toBeInTheDocument();
    expect(screen.queryByRole("treegrid", { name: "Primary trial balance lines for run-42" })).not.toBeInTheDocument();
  });
});
