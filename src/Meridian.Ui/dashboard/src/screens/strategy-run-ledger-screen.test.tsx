import { fireEvent, screen, within } from "@testing-library/react";
import { useLocation } from "react-router-dom";
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

function RouteLocation() {
  const location = useLocation();
  return <output data-testid="route-location">{location.search}</output>;
}

async function renderRunLedger(initialEntry = "/strategy/run-ledger") {
  const result = renderWithRouter(<><StrategyRunLedgerScreen /><RouteLocation /></>, { initialEntries: [initialEntry] });
  await waitForAsyncEffects();
  return result;
}

describe("StrategyRunLedgerScreen", () => {
  it("keeps run and date choices before dependent summaries and results", async () => {
    await renderRunLedger("/strategy/run-ledger?runId=unknown-run&asOf=2026-06-30&scenario=retained");

    const run = screen.getByRole("combobox", { name: "Strategy run" });
    const date = screen.getByLabelText("As of date (UTC)");
    const summary = screen.getByLabelText("Explorer summary");
    expect(run).toHaveValue("unknown-run");
    expect(run.compareDocumentPosition(date) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(date.compareDocumentPosition(summary) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    fireEvent.focus(run);
    fireEvent.click(screen.getByRole("option", { name: "Select a run…" }));
    await waitForAsyncEffects();
    expect(date).toBeDisabled();
    expect(screen.getByTestId("route-location")).toHaveTextContent("?asOf=2026-06-30&scenario=retained");
  });

  it("keeps the run picker visible during loading and failed discovery", async () => {
    let rejectDiscovery: (reason: Error) => void = () => {};
    vi.mocked(api.getFinancialRecordExplorer).mockReturnValueOnce(new Promise((_resolve, reject) => { rejectDiscovery = reject; }));
    await renderRunLedger();
    expect(screen.getByRole("combobox", { name: "Strategy run" })).toBeInTheDocument();
    expect(screen.getByText("Loading available strategy runs…")).toBeInTheDocument();
    rejectDiscovery(new Error("offline"));
    await waitForAsyncEffects();
    expect(screen.getByRole("combobox", { name: "Strategy run" })).toBeInTheDocument();
    expect(screen.getByText("Run choices could not be loaded. Refresh this page to try again.")).toBeInTheDocument();
  });

  it("explains an empty run list beside the required picker", async () => {
    await renderRunLedger();
    expect(screen.getByRole("combobox", { name: "Strategy run" })).toBeInTheDocument();
    expect(screen.getByText("No retained strategy runs are available. Create a run in Strategy to inspect its ledger.")).toBeInTheDocument();
    expect(screen.getByText("No run selected")).toBeInTheDocument();
  });

  it("hides the outgoing explorer evidence until the selected run's response arrives", async () => {
    let finishDiscovery: (value: never) => void = () => {};
    vi.mocked(api.getFinancialRecordExplorer)
      .mockResolvedValueOnce({
        explorerId: "ledger", title: "Run A evidence", description: "", sourceState: "Ready",
        isBlocked: false, blockedReason: "", scopeItems: [], summaryItems: [], filters: [], columns: [],
        savedViews: ["run-a", "run-b"].map((id) => ({
          viewId: id, label: id, description: "", filters: [{ filterId: "run", label: "Run", value: id, operator: "equals", tone: "Info" }], columnIds: [], isActive: false
        })),
        rows: [], selectedRecord: null, proofActions: [], recordGraph: { nodes: [], edges: [] }
      } as never)
      .mockReturnValueOnce(new Promise((resolve) => { finishDiscovery = resolve; }));
    await renderRunLedger("/strategy/run-ledger?runId=run-a&scenario=retained");
    expect(screen.getByRole("heading", { name: "Run A evidence" })).toBeInTheDocument();
    fireEvent.focus(screen.getByRole("combobox", { name: "Strategy run" }));
    fireEvent.click(screen.getByRole("option", { name: "run-b" }));
    await waitForAsyncEffects();
    expect(screen.queryByRole("heading", { name: "Run A evidence" })).not.toBeInTheDocument();
    expect(screen.getByTestId("route-location")).toHaveTextContent("?runId=run-b&scenario=retained");
    finishDiscovery(null as never);
    await waitForAsyncEffects();
  });

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

  it("applies one inclusive UTC date to balances and journal and can restore run end", async () => {
    await renderRunLedger("/strategy/run-ledger?runId=run-42");
    fireEvent.change(screen.getByLabelText("As of date (UTC)"), { target: { value: "2026-06-30" } });
    await waitForAsyncEffects();

    const cut = { asOf: "2026-06-30T23:59:59.9999999Z" };
    expect(api.getRunTrialBalance).toHaveBeenLastCalledWith("run-42", cut);
    expect(api.getRunLedgerJournal).toHaveBeenLastCalledWith("run-42", cut);
    expect(screen.getByLabelText("As of date (UTC)")).toHaveValue("2026-06-30");

    fireEvent.click(screen.getByRole("button", { name: "Run end" }));
    await waitForAsyncEffects();
    expect(api.getRunTrialBalance).toHaveBeenLastCalledWith("run-42");
    expect(api.getRunLedgerJournal).toHaveBeenLastCalledWith("run-42");
    expect(screen.getByLabelText("As of date (UTC)")).toHaveValue("");
  });

  it("exports the selected run and date in the selected accounting format", async () => {
    await renderRunLedger("/strategy/run-ledger?runId=run-42&asOf=2026-03-01");
    fireEvent.change(screen.getByLabelText("Accounting export format"), { target: { value: "Parquet" } });
    expect(screen.getByRole("link", { name: "Download accounting package" })).toHaveAttribute(
      "href", "/api/workstation/runs/run-42/ledger/export?format=Parquet&asOf=2026-03-01T23%3A59%3A59.9999999Z"
    );
  });

  it("restores the economic date from a retained URL", async () => {
    await renderRunLedger("/strategy/run-ledger?runId=run-42&asOf=2026-03-01");
    expect(api.getRunTrialBalance).toHaveBeenCalledWith("run-42", { asOf: "2026-03-01T23:59:59.9999999Z" });
    expect(api.getRunLedgerJournal).toHaveBeenCalledWith("run-42", { asOf: "2026-03-01T23:59:59.9999999Z" });
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
  it("filters the run's accounts, which a several-hundred-row ledger needs", async () => {
    // The view state has always computed a filtered row count; the control that drives it was
    // never rendered when this screen moved out of Accounting, so the count could only ever read
    // as unfiltered.
    vi.mocked(api.getRunTrialBalance).mockResolvedValue(trialBalanceLines);
    await renderRunLedger("/strategy/run-ledger?runId=run-42");

    expect(screen.getAllByText("2 GL account rows").length).toBeGreaterThan(0);

    const filter = screen.getByLabelText(/filter/i, { selector: "input" });
    fireEvent.change(filter, { target: { value: "financing" } });
    await waitForAsyncEffects();

    expect(screen.getAllByText("1 of 2 GL account rows").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Financing payable").length).toBeGreaterThan(0);
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

    const selector = await screen.findByRole("combobox", { name: "Strategy run" });
    expect(selector).toBeInTheDocument();
    expect(screen.queryByRole("grid", { name: "Ledger records" })).not.toBeInTheDocument();
    expect(screen.getByText("No run selected")).toBeInTheDocument();

    fireEvent.focus(selector);
    const runOptions = within(screen.getByRole("listbox")).getAllByRole("option");
    expect(runOptions).toHaveLength(2);
    expect(screen.getByRole("option", { name: "Run 42" })).toBeInTheDocument();
    fireEvent.click(screen.getByRole("option", { name: "Run 43" }));
    await waitForAsyncEffects();
    // Selection uses the bare run id the APIs expect, never a composite ledger record id.
    expect(api.getRunTrialBalance).toHaveBeenCalledWith("run-43");
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

    const selector = screen.getByRole("combobox", { name: "Strategy run" });
    fireEvent.focus(selector);
    expect(within(screen.getByRole("listbox")).getAllByRole("option")).toHaveLength(2);
    fireEvent.change(selector, { target: { value: "2026-05" } });
    expect(within(screen.getByRole("listbox")).getAllByRole("option")).toHaveLength(1);
    fireEvent.keyDown(selector, { key: "Enter" });
    await waitForAsyncEffects();
    expect(api.getRunTrialBalance).toHaveBeenCalledWith("run-41");
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
