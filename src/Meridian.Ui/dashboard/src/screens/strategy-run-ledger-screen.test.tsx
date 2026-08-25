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

  it("renders the basis bridge that moved with this screen", async () => {
    // The trial balance shows one basis at a time, so the per-account Primary-to-GAAP difference
    // is only readable here. Moving the run ledger out of Accounting removed the bridge's only
    // renderer while leaving the view model still building it, so it silently disappeared.
    vi.mocked(api.getRunTrialBalance).mockResolvedValue([
      { ...trialBalanceLines[0], accountingBasis: "Primary", balance: 120500 },
      { ...trialBalanceLines[0], accountingBasis: "Gaap", balance: 118000 }
    ]);

    await renderRunLedger("/strategy/run-ledger?runId=run-42");
    await waitForAsyncEffects();

    const bridge = await screen.findByRole("region", { name: /GAAP to Primary basis bridge/i });
    expect(bridge).toBeInTheDocument();
    expect(bridge).toHaveTextContent("Cash");
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
      // Real shape: rows are ledger *accounts*, one per account per run, with composite record
      // ids. Two rows below share a run — the selector must dedupe by sourceRunId and must never
      // offer a record id as if it were a run id.
      rows: [
        { recordId: "ledger:run-42:0", recordType: "ledger", label: "Cash", source: "Run 42", status: "Asset", tone: "neutral", cells: [], detail: null, sourceRunId: "run-42" },
        { recordId: "ledger:run-42:1", recordType: "ledger", label: "Payable", source: "Run 42", status: "Liability", tone: "neutral", cells: [], detail: null, sourceRunId: "run-42" },
        { recordId: "ledger:run-43:0", recordType: "ledger", label: "Cash", source: "Run 43", status: "Asset", tone: "neutral", cells: [], detail: null, sourceRunId: "run-43" }
      ],
      selectedRecord: null,
      proofActions: [],
      recordGraph: { nodes: [], edges: [] }
    } as never);

    await renderRunLedger();

    const selector = await screen.findByLabelText("Strategy run", { selector: "select" });
    expect(selector).toBeInTheDocument();

    const runOptions = screen.getAllByRole("option").filter((option) => (option as HTMLOptionElement).value !== "");
    // Deduped to one option per run, valued by the bare run id the run APIs expect.
    expect(runOptions.map((option) => (option as HTMLOptionElement).value)).toEqual(["run-42", "run-43"]);
    expect(screen.getByRole("option", { name: "Run 42" })).toBeInTheDocument();
    expect(screen.getByRole("option", { name: "Run 43" })).toBeInTheDocument();
  });
});
