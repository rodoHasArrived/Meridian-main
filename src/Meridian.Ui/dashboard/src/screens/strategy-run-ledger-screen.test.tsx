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
  it("falls back to the rows when the explorer publishes no run views", async () => {
    // A fallback, not the main path: BuildLedgerExplorerAsync composes its rows from exactly one
    // run, so rows can only ever name that run. Two runs' rows are constructed here to prove the
    // dedupe, and the run-views test below covers what the server actually sends.
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

  it("offers every run the explorer publishes, not only the one whose rows it returned", async () => {
    // The selector used to read run ids out of the rows, and the explorer builds its rows from a
    // single run — so it offered exactly one option and every older run was unreachable from this
    // screen. The runs now arrive as system views, one per run the caller may read.
    vi.mocked(api.getFinancialRecordExplorer).mockResolvedValue({
      explorerId: "ledger",
      title: "Ledger",
      description: "",
      sourceState: "Ready",
      isBlocked: false,
      blockedReason: "",
      scopeItems: [],
      savedViews: [
        {
          viewId: "system-ledger-run-run-42",
          label: "Momentum · Backtest · 2026-06-30 09:00",
          description: "",
          isSystem: true,
          isActive: true,
          filters: [{ filterId: "run", label: "Run", value: "run-42", operator: "equals", tone: "Info" }],
          searchText: "",
          columnIds: []
        },
        {
          viewId: "system-ledger-run-run-41",
          label: "Momentum · Backtest · 2026-05-31 09:00",
          description: "",
          isSystem: true,
          isActive: false,
          filters: [{ filterId: "run", label: "Run", value: "run-41", operator: "equals", tone: "Info" }],
          searchText: "",
          columnIds: []
        }
      ],
      summaryItems: [],
      filters: [],
      columns: [],
      // Rows from the active run alone, exactly as the server composes them.
      rows: [
        { recordId: "ledger:run-42:0", recordType: "ledger", label: "Cash", source: "Run 42", status: "Asset", tone: "neutral", cells: [], detail: null, sourceRunId: "run-42" }
      ],
      selectedRecord: null,
      proofActions: [],
      recordGraph: { nodes: [], edges: [] }
    } as never);

    await renderRunLedger();

    const runOptions = screen.getAllByRole("option").filter((option) => (option as HTMLOptionElement).value !== "");
    expect(runOptions.map((option) => (option as HTMLOptionElement).value)).toEqual(["run-42", "run-41"]);
    expect(screen.getByRole("option", { name: "Momentum · Backtest · 2026-05-31 09:00" })).toBeInTheDocument();
  });

  it("scopes the explorer request to the run the panels are reading", async () => {
    // Unscoped, the explorer answered for whichever run was newest, so an older run's rows sat
    // under the newest run's header, proof links and scope.
    await renderRunLedger("/strategy/run-ledger?runId=run-41");

    expect(api.getFinancialRecordExplorer).toHaveBeenCalledWith(
      "ledger",
      {},
      [{ filterId: "run", value: "run-41" }]
    );
  });

  it("asks the explorer for nothing in particular when no run is selected", async () => {
    await renderRunLedger();

    expect(api.getFinancialRecordExplorer).toHaveBeenCalledWith("ledger", {}, []);
  });
});
