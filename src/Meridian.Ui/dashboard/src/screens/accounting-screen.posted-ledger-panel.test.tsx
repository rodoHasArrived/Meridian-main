import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import * as ledgerReportsApi from "@/lib/ledger-reports-api";
import { AccountingPostedLedgerSection } from "@/screens/accounting-screen.posted-ledger-panel";
import { renderWithRouter, waitForAsyncEffects } from "@/test/render";
import type { LedgerAccountRunningBalance, LedgerBook, LedgerPeriod, LedgerPeriodTrialBalanceLine } from "@/types";

/**
 * The canonical Accounting ledger surface.
 *
 * The posted ledger is book-scoped, so this panel has to name the book its figures belong to and,
 * where a deployment holds more than one, let an operator reach the others. It rendered neither
 * for a while: the shared view state carried `bookOptions` and the hook returned `selectBook`, but
 * this consumer used neither, leaving every book after the first unreachable here and the balances
 * on screen unattributed.
 */

vi.mock("@/lib/ledger-reports-api", () => ({
  getLedgerBooks: vi.fn(),
  getLedgerPeriods: vi.fn(),
  getLedgerPeriodTrialBalance: vi.fn(),
  getLedgerPeriodPnlSummary: vi.fn(),
  getLedgerPeriodJournalEntries: vi.fn(),
  getLedgerPeriodAccountRunningBalance: vi.fn()
}));

const MASTER_BOOK_ID = "00000000-0000-0000-0000-0000000000aa";
const FEEDER_BOOK_ID = "00000000-0000-0000-0000-0000000000cc";

function makeBook(overrides: Partial<LedgerBook> = {}): LedgerBook {
  return {
    ledgerBookId: MASTER_BOOK_ID,
    fundProfileId: "fund-alpha",
    fundStructureNodeId: "00000000-0000-0000-0000-0000000000bb",
    fundStructureNodeKind: "Fund",
    displayName: "Master Fund",
    baseCurrency: "USD",
    createdAt: "2026-01-01T00:00:00Z",
    updatedAt: "2026-01-01T00:00:00Z",
    accountingBasis: "Primary",
    accountingPolicyId: "legacy-v1",
    accountingPolicyVersion: "legacy-v1",
    ...overrides
  };
}

function makePeriod(overrides: Partial<LedgerPeriod> = {}): LedgerPeriod {
  return {
    periodId: "11111111-1111-1111-1111-111111111111",
    ledgerBookId: MASTER_BOOK_ID,
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

function makeLine(overrides: Partial<LedgerPeriodTrialBalanceLine> = {}): LedgerPeriodTrialBalanceLine {
  return {
    accountName: "Cash", accountType: "Asset", symbol: null, financialAccountId: null,
    debitTotal: 100, creditTotal: 0, balance: 100, entryCount: 1, accountingBasis: "Primary",
    dimensions: { fundId: "fund-alpha" }, ...overrides
  };
}

function makeRunningBalance(balance = 100): LedgerAccountRunningBalance {
  return {
    periodId: makePeriod().periodId, ledgerBookId: MASTER_BOOK_ID, accountName: "Cash", accountType: "Asset",
    symbol: null, financialAccountId: null, totalDebits: balance, totalCredits: 0, endingBalance: balance,
    points: [{ journalEntryId: "journal-cash", entryId: "entry-cash", globalSequence: 1,
      timestamp: "2026-07-02T00:00:00Z", description: "Retained cash posting", debit: balance, credit: 0, balance }]
  };
}

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(ledgerReportsApi.getLedgerPeriods).mockResolvedValue([makePeriod()]);
  vi.mocked(ledgerReportsApi.getLedgerPeriodTrialBalance).mockResolvedValue([]);
  vi.mocked(ledgerReportsApi.getLedgerPeriodPnlSummary).mockRejectedValue(new Error("no summary"));
  vi.mocked(ledgerReportsApi.getLedgerPeriodJournalEntries).mockResolvedValue([]);
  vi.mocked(ledgerReportsApi.getLedgerPeriodAccountRunningBalance).mockImplementation(async (_periodId, query) =>
    makeRunningBalance(query.accountingBasis === "Gaap" ? 40 : 100));
});

async function renderPanel() {
  const result = renderWithRouter(<AccountingPostedLedgerSection workstream="ledger" />, {
    initialEntries: ["/accounting/ledger"]
  });
  await waitForAsyncEffects();
  return result;
}

describe("AccountingPostedLedgerSection", () => {
  it("offers every ledger book when a deployment holds more than one", async () => {
    vi.mocked(ledgerReportsApi.getLedgerBooks).mockResolvedValue([
      makeBook(),
      makeBook({ ledgerBookId: FEEDER_BOOK_ID, displayName: "Feeder Fund", baseCurrency: "EUR" })
    ]);

    await renderPanel();

    const books = await screen.findByRole("group", { name: "Ledger book" });
    expect(books).toHaveTextContent("Master Fund");
    expect(books).toHaveTextContent("Feeder Fund");

    await userEvent.click(screen.getByRole("button", { name: /Feeder Fund, base currency EUR/ }));
    await waitForAsyncEffects();

    // Switching book re-scopes the period request; an unscoped list would span every book and land
    // on whichever owns the globally latest closed period.
    expect(ledgerReportsApi.getLedgerPeriods).toHaveBeenCalledWith({ ledgerBookId: FEEDER_BOOK_ID });
  });

  it("names the single book rather than leaving its balances unattributed", async () => {
    vi.mocked(ledgerReportsApi.getLedgerBooks).mockResolvedValue([makeBook()]);

    await renderPanel();

    expect(await screen.findByText("Master Fund")).toBeInTheDocument();
    expect(screen.queryByRole("group", { name: "Ledger book" })).not.toBeInTheDocument();
  });

  it("uses the selected row's basis and exact dimension scope for its running balance", async () => {
    vi.mocked(ledgerReportsApi.getLedgerBooks).mockResolvedValue([makeBook()]);
    vi.mocked(ledgerReportsApi.getLedgerPeriodTrialBalance).mockResolvedValue([
      makeLine(), makeLine({ accountingBasis: "Gaap", balance: 40, debitTotal: 40 })
    ]);
    await renderPanel();
    const postings = within(await screen.findByRole("region", { name: "Postings to Cash" }));
    expect(await postings.findByText("Retained cash posting")).toBeInTheDocument();
    expect(ledgerReportsApi.getLedgerPeriodAccountRunningBalance).toHaveBeenLastCalledWith(makePeriod().periodId, {
      account: "Cash", accountingBasis: "Primary", symbol: null, financialAccountId: null, dimensions: { fundId: "fund-alpha" }
    });
    await userEvent.click(screen.getByRole("button", { name: /^GAAP basis,/ }));
    const gaapPostings = within(await screen.findByRole("region", { name: "Postings to Cash" }));
    expect(await gaapPostings.findByText(/1 posting; ending balance/)).toHaveTextContent("$40");
    expect(ledgerReportsApi.getLedgerPeriodAccountRunningBalance).toHaveBeenLastCalledWith(makePeriod().periodId, {
      account: "Cash", accountingBasis: "Gaap", symbol: null, financialAccountId: null, dimensions: { fundId: "fund-alpha" }
    });
    expect(gaapPostings.queryByText("$100")).not.toBeInTheDocument();
  });

  it("keeps a missing book currency explicit in the running-balance amounts", async () => {
    vi.mocked(ledgerReportsApi.getLedgerBooks).mockResolvedValue([makeBook({ baseCurrency: "  " })]);
    vi.mocked(ledgerReportsApi.getLedgerPeriodTrialBalance).mockResolvedValue([makeLine()]);
    await renderPanel();
    const postings = await screen.findByRole("region", { name: "Postings to Cash" });
    expect(await within(postings).findByText(/1 posting; ending balance/)).toHaveTextContent("100 (currency unavailable)");
    expect(postings).not.toHaveTextContent("$");
    expect(postings).not.toHaveTextContent("USD");
  });
});
