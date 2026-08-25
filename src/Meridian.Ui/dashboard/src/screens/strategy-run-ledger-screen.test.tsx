import { screen } from "@testing-library/react";
import * as api from "@/lib/api";
import { StrategyRunLedgerScreen } from "@/screens/strategy-run-ledger-screen";
import { renderWithRouter, waitForAsyncEffects } from "@/test/render";
import type { LedgerJournalLine, LedgerTrialBalanceLine } from "@/types";

vi.mock("@/lib/api", async () => {
  const actual = await vi.importActual<typeof import("@/lib/api")>("@/lib/api");
  return {
    ...actual,
    getFinancialRecordExplorer: vi.fn(),
    getRunTrialBalance: vi.fn(),
    getRunLedgerJournal: vi.fn()
  };
});

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

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(api.getFinancialRecordExplorer).mockResolvedValue(null as never);
  vi.mocked(api.getRunTrialBalance).mockResolvedValue([]);
  vi.mocked(api.getRunLedgerJournal).mockResolvedValue([]);
});

async function renderRunLedger(initialEntry = "/strategy/run-ledger") {
  const result = renderWithRouter(<StrategyRunLedgerScreen />, { initialEntries: [initialEntry] });
  await waitForAsyncEffects();
  return result;
}

describe("StrategyRunLedgerScreen", () => {
  it("labels itself a simulation artifact rather than the fund's book", async () => {
    await renderRunLedger();

    expect(screen.getByRole("heading", { name: "Strategy Run Ledger Explorer" })).toBeInTheDocument();
    expect(screen.getByText(/Strategy run \(simulation\) — not the posted journal/)).toBeInTheDocument();
  });

  it("loads the run trial balance and journal for the runId query param", async () => {
    vi.mocked(api.getRunTrialBalance).mockResolvedValue(trialBalanceLines);
    vi.mocked(api.getRunLedgerJournal).mockResolvedValue(journalLines);

    await renderRunLedger("/strategy/run-ledger?runId=run-42");
    await waitForAsyncEffects();

    expect(api.getRunTrialBalance).toHaveBeenCalledWith("run-42");
    expect(api.getRunLedgerJournal).toHaveBeenCalledWith("run-42");
    expect(await screen.findByRole("region", { name: /trial balance lines for the selected ledger run/ })).toBeInTheDocument();
    expect(screen.getByRole("row", { name: /Cash Asset\. Primary basis/ })).toBeInTheDocument();
    expect(screen.getByText("Cash sweep")).toBeInTheDocument();
  });

  it("asks for a run instead of fabricating a ledger when none is selected", async () => {
    await renderRunLedger();

    expect(await screen.findByText("Select a run to load its simulated trial balance.")).toBeInTheDocument();
    expect(api.getRunTrialBalance).not.toHaveBeenCalled();
  });

  it("surfaces a failed run trial balance as an error rather than an empty book", async () => {
    vi.mocked(api.getRunTrialBalance).mockRejectedValue(new Error("offline"));

    await renderRunLedger("/strategy/run-ledger?runId=run-42");
    await waitForAsyncEffects();

    expect(await screen.findByRole("alert")).toBeInTheDocument();
  });
  it("offers the explorer's runs as a selector so the nav entry is not a dead end", async () => {
    // The Strategy nav links here with no runId. Without an in-screen selector the operator
    // would land on the "select a run" prompt with no way to select one.
    vi.mocked(api.getFinancialRecordExplorer).mockResolvedValue({
      explorerId: "ledger",
      title: "Ledger",
      description: "",
      sourceState: "Ready",
      isBlocked: false,
      blockedReason: "",
      scopeItems: [],
      savedViews: [],
      summaryItems: [],
      filters: [],
      columns: [],
      rows: [
        { recordId: "run-42", recordType: "LedgerRun", label: "Run 42", source: "", status: "", tone: "neutral", cells: [], detail: null },
        { recordId: "run-43", recordType: "LedgerRun", label: "Run 43", source: "", status: "", tone: "neutral", cells: [], detail: null }
      ],
      selectedRecord: null,
      proofActions: [],
      recordGraph: { nodes: [], edges: [] }
    } as never);

    await renderRunLedger();

    const selector = await screen.findByLabelText("Strategy run", { selector: "select" });
    expect(selector).toBeInTheDocument();
    expect(screen.getByRole("option", { name: "Run 42" })).toBeInTheDocument();
    expect(screen.getByRole("option", { name: "Run 43" })).toBeInTheDocument();
  });
});
