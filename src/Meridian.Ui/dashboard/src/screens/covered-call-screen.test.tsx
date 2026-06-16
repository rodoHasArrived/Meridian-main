import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter } from "react-router-dom";
import { ApiError } from "@/lib/api-errors";
import * as coveredCallApi from "@/lib/api/covered-call.api";
import { CoveredCallScreen } from "@/screens/covered-call-screen";
import { COVERED_CALL_CHAIN_DETAIL_PANEL_ID } from "@/screens/covered-call-screen.view-model";
import type { CoveredCallChainPreview, CoveredCallRunHandle, CoveredCallRunResult, CoveredCallRunSummary } from "@/lib/covered-call";

vi.mock("@/lib/api/covered-call.api", () => ({
  startCoveredCallBacktest: vi.fn(),
  getCoveredCallRunStatus: vi.fn(),
  getCoveredCallRunResult: vi.fn(),
  cancelCoveredCallRun: vi.fn(),
  previewCoveredCallChain: vi.fn(),
  listCoveredCallRuns: vi.fn()
}));

const chainPreview: CoveredCallChainPreview = {
  underlyingSymbol: "SPY",
  asOf: "2024-01-01",
  underlyingPrice: 500,
  totalContractsScanned: 2,
  filtersPassed: 1,
  candidates: [
    {
      strike: 505,
      expiration: "2024-02-16",
      daysToExpiration: 32,
      bid: 2.41,
      ask: 2.58,
      delta: 0.31,
      impliedVolatility: 0.22,
      openInterest: 1040,
      volume: 122,
      meetsAllFilters: true,
      rejectReason: null
    },
    {
      strike: 510,
      expiration: "2024-02-16",
      daysToExpiration: 32,
      bid: 1.71,
      ask: 1.95,
      delta: 0.42,
      impliedVolatility: null,
      openInterest: 84,
      volume: 12,
      meetsAllFilters: false,
      rejectReason: "Open interest below minimum"
    }
  ]
};

const completedRunResult: CoveredCallRunResult = {
  runId: "run-1",
  underlyingSymbol: "SPY",
  from: "2024-01-01",
  to: "2024-06-30",
  label: null,
  metrics: {
    cagr: 0.1,
    annualizedVolatility: 0.15,
    sharpeRatio: 0.7,
    sortinoRatio: 0.9,
    calmarRatio: 1.8,
    maxDrawdownPct: -0.05,
    winRate: 0.7,
    assignmentRate: 0.05,
    averageHoldingDays: 20,
    totalOptionTrades: 5,
    assignedTrades: 0,
    totalPremiumCollected: 1500,
    totalOptionPnl: 800,
    upCapture: 0.6,
    downCapture: 0.9,
    monthlyVar1Pct: -0.08,
    monthlyVar5Pct: -0.05,
    monthlyCVar5Pct: -0.06,
    returnSkewness: 0,
    returnKurtosis: 3,
    annualizedTurnover: 8
  },
  equityCurve: [],
  trades: [],
  openPositionsAtEnd: []
};

const completedRunWithTrades: CoveredCallRunResult = {
  ...completedRunResult,
  trades: [
    {
      strike: 505,
      expiration: "2024-02-16",
      contracts: 2,
      multiplier: 100,
      entryDate: "2024-01-10",
      entryCredit: 2.35,
      exitDate: "2024-01-24",
      exitDebit: 0.75,
      exitReason: "TakeProfit",
      entryImpliedVolatility: 0.22,
      netPnlPerContract: 160,
      totalNetPnl: 320,
      holdingDays: 14,
      isWin: true,
      wasAssigned: false
    },
    {
      strike: 510,
      expiration: "2024-03-15",
      contracts: 1,
      multiplier: 100,
      entryDate: "2024-02-01",
      entryCredit: 1.9,
      exitDate: "2024-02-12",
      exitDebit: 3.3,
      exitReason: "Assigned",
      entryImpliedVolatility: null,
      netPnlPerContract: -140,
      totalNetPnl: -140,
      holdingDays: 11,
      isWin: false,
      wasAssigned: true
    }
  ]
};

const completedRunWithOpenPositions: CoveredCallRunResult = {
  ...completedRunResult,
  openPositionsAtEnd: [
    {
      positionId: "pos-505",
      strike: 505,
      expiration: "2024-02-16",
      contracts: 2,
      multiplier: 100,
      entryDate: "2024-01-10",
      entryCredit: 2.35,
      markToClose: 0.75,
      currentDelta: 0.31,
      currentDte: 21,
      unrealisedPnl: 320,
      premiumCaptured: 0.68
    },
    {
      positionId: "pos-510",
      strike: 510,
      expiration: "2024-03-15",
      contracts: 1,
      multiplier: 100,
      entryDate: "2024-02-01",
      entryCredit: 1.9,
      markToClose: 3.3,
      currentDelta: 0.42,
      currentDte: 44,
      unrealisedPnl: -140,
      premiumCaptured: 0.42
    }
  ]
};

const historicalRun: CoveredCallRunSummary = {
  runId: "run-history-1",
  underlyingSymbol: "SPY",
  from: "2024-01-01",
  to: "2024-06-30",
  label: "Income sleeve",
  status: "Completed",
  startedAt: "2024-07-01T14:05:00Z",
  endedAt: "2024-07-01T14:06:00Z",
  cagr: 0.1234,
  sharpeRatio: 1.42,
  winRate: 0.73
};

function renderCoveredCallScreen() {
  return render(
    <MemoryRouter>
      <CoveredCallScreen />
    </MemoryRouter>
  );
}

describe("CoveredCallScreen", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(coveredCallApi.previewCoveredCallChain).mockResolvedValue(chainPreview);
    vi.mocked(coveredCallApi.listCoveredCallRuns).mockResolvedValue([]);
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("renders chain preview rows with keyboard selection and a linked detail panel", async () => {
    renderCoveredCallScreen();

    fireEvent.change(screen.getByLabelText(/Min strike/i), { target: { value: "500" } });

    await waitFor(() => {
      expect(coveredCallApi.previewCoveredCallChain).toHaveBeenCalled();
    });

    const passingRow = await screen.findByRole("row", {
      name: "Inspect SPY 505.00 call expiring 2024-02-16. Status Pass."
    });
    const rejectedRow = await screen.findByRole("row", {
      name: "Inspect SPY 510.00 call expiring 2024-02-16. Status Open interest below minimum."
    });

    expect(passingRow).toHaveAttribute("aria-controls", COVERED_CALL_CHAIN_DETAIL_PANEL_ID);
    expect(passingRow).toHaveAttribute("aria-selected", "true");
    expect(rejectedRow).toHaveAttribute("aria-controls", COVERED_CALL_CHAIN_DETAIL_PANEL_ID);
    expect(rejectedRow).toHaveAttribute("aria-expanded", "false");

    rejectedRow.focus();
    fireEvent.keyDown(rejectedRow, { key: "Enter" });

    expect(rejectedRow).toHaveAttribute("aria-selected", "true");
    expect(rejectedRow).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByRole("region", {
      name: "Selected covered-call candidate: SPY 510.00 call expiring 2024-02-16"
    })).toBeInTheDocument();
    expect(screen.getByText("This contract is excluded by the current filter set: Open interest below minimum.")).toBeInTheDocument();
  });

  it("keeps the run action disabled until required parameters are valid", async () => {
    renderCoveredCallScreen();

    const runButton = screen.getByRole("button", { name: "Run covered-call backtest" });
    expect(runButton).toBeDisabled();
    expect(runButton).toHaveAttribute("title", "Minimum strike must be greater than zero.");
    expect(runButton).toHaveAttribute("aria-describedby", "covered-call-run-command-feedback");
    expect(screen.getByText("Cannot run yet: Minimum strike must be greater than zero.")).toBeInTheDocument();

    await act(async () => {
      fireEvent.change(screen.getByLabelText(/Min strike/i), { target: { value: "500" } });
    });

    await waitFor(() => {
      expect(runButton).not.toBeDisabled();
      expect(runButton).not.toHaveAttribute("title", "Minimum strike must be greater than zero.");
      expect(runButton).not.toHaveAttribute("aria-describedby");
    });
    expect(screen.queryByText("Cannot run yet: Minimum strike must be greater than zero.")).not.toBeInTheDocument();

    await waitFor(() => {
      expect(coveredCallApi.previewCoveredCallChain).toHaveBeenCalled();
    });
  });

  it("renders structured chain preview failure details", async () => {
    vi.mocked(coveredCallApi.previewCoveredCallChain).mockRejectedValueOnce(
      new ApiError({
        path: "/api/covered-call/preview",
        status: 503,
        detail: "Preview service unavailable",
        validationIssues: [
          {
            field: "underlyingSymbol",
            label: "underlyingSymbol",
            messages: ["Underlying symbol is not routable for option-chain preview."]
          }
        ]
      })
    );

    renderCoveredCallScreen();

    fireEvent.change(screen.getByLabelText(/Min strike/i), { target: { value: "500" } });

    await waitFor(() => {
      expect(screen.getByText("Chain preview failed: Preview service unavailable")).toBeInTheDocument();
    });

    const detailPanel = screen.getByLabelText("Covered-call candidate detail unavailable");
    expect(within(detailPanel).getByText("Preview service unavailable")).toBeInTheDocument();
    expect(within(detailPanel).getByText("Endpoint returned 503 for /api/covered-call/preview.")).toBeInTheDocument();
    expect(within(detailPanel).getByText("underlyingSymbol: Underlying symbol is not routable for option-chain preview.")).toBeInTheDocument();
  });

  it("renders VM-owned scoring controls and includes them in the backtest request", async () => {
    vi.mocked(coveredCallApi.startCoveredCallBacktest).mockResolvedValue({
      runId: "run-scoring",
      queuedAt: "2024-07-01T00:00:00Z"
    });

    renderCoveredCallScreen();

    const scoringMode = screen.getByLabelText("Scoring mode");
    const depthBonusWeight = screen.getByLabelText("Depth bonus weight");

    expect(scoringMode).toHaveAttribute("id", "cc-scoringMode");
    expect(scoringMode).toHaveAttribute("aria-describedby", "cc-scoringMode-help");
    expect(screen.getByText("Relative ranks candidates by liquidity, depth, and premium quality; Basic keeps the plain filter score.")).toBeInTheDocument();
    expect(depthBonusWeight).toHaveAttribute("id", "cc-depthBonusWeight");
    expect(depthBonusWeight).toHaveAttribute("aria-describedby", "cc-depthBonusWeight-help");

    await act(async () => {
      fireEvent.change(screen.getByLabelText(/Min strike/i), { target: { value: "500" } });
      fireEvent.change(scoringMode, { target: { value: "Basic" } });
      fireEvent.change(depthBonusWeight, { target: { value: "0.12" } });
    });
    await waitFor(() => expect(screen.getByRole("button", { name: "Run covered-call backtest" })).not.toBeDisabled());

    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: "Run covered-call backtest" }));
    });

    expect(coveredCallApi.startCoveredCallBacktest).toHaveBeenCalledWith(expect.objectContaining({
      minStrike: 500,
      scoringMode: "Basic",
      depthBonusWeight: 0.12
    }));
  });

  it("renders previous runs through dense-table rows and reloads a run from keyboard selection", async () => {
    vi.mocked(coveredCallApi.listCoveredCallRuns).mockResolvedValue([historicalRun]);
    vi.mocked(coveredCallApi.getCoveredCallRunResult).mockResolvedValue({
      ...completedRunResult,
      runId: historicalRun.runId,
      underlyingSymbol: historicalRun.underlyingSymbol
    });

    renderCoveredCallScreen();

    const historyTable = await screen.findByRole("table", { name: "Previous covered-call runs" });
    const historyRow = await screen.findByRole("row", {
      name: "Reload covered-call run run-history-1 for SPY"
    });

    expect(historyTable).toBeInTheDocument();
    expect(historyRow).toHaveAttribute("tabindex", "0");
    expect(screen.getByText("Jul 1, 14:05 UTC")).toBeInTheDocument();
    expect(screen.getByText("12.3%")).toBeInTheDocument();
    expect(screen.getByText("1.42")).toBeInTheDocument();

    historyRow.focus();
    fireEvent.keyDown(historyRow, { key: "Enter" });

    await waitFor(() => {
      expect(coveredCallApi.getCoveredCallRunResult).toHaveBeenCalledWith("run-history-1");
    });
    await screen.findByRole("navigation", { name: "Covered-call results next workflow" });
    expect(historyRow).toHaveAttribute("aria-selected", "true");
  });

  it("renders structured history-load diagnostics when previous runs fail to load", async () => {
    vi.mocked(coveredCallApi.listCoveredCallRuns).mockRejectedValue(new ApiError({
      path: "/api/covered-call/runs?limit=50",
      status: 503,
      title: "History store unavailable",
      detail: "Covered-call run history is temporarily offline."
    }));

    renderCoveredCallScreen();

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("Previous covered-call runs failed to load: Covered-call run history is temporarily offline.");
    expect(within(alert).getByText("Endpoint returned 503 for /api/covered-call/runs?limit=50.")).toBeInTheDocument();
    expect(within(alert).getByText("History store unavailable")).toBeInTheDocument();
  });

  it("renders submitting progress and a disabled cancel reason while the engine accepts the run", async () => {
    vi.mocked(coveredCallApi.startCoveredCallBacktest).mockReturnValue(new Promise<CoveredCallRunHandle>(() => {}));
    renderCoveredCallScreen();

    await act(async () => {
      fireEvent.change(screen.getByLabelText(/Min strike/i), { target: { value: "500" } });
    });
    await waitFor(() => expect(screen.getByRole("button", { name: "Run covered-call backtest" })).not.toBeDisabled());
    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: "Run covered-call backtest" }));
    });

    expect(await screen.findByText("Submitting backtest")).toBeInTheDocument();
    expect(screen.getByText("Submitting covered-call run request to the strategy engine.")).toBeInTheDocument();
    expect(screen.getByRole("progressbar")).toHaveAttribute("aria-valuetext", "Submitting covered-call run request.");
    expect(screen.getByRole("progressbar")).toHaveAttribute("aria-busy", "true");

    const cancelButton = screen.getByRole("button", { name: "Cancel covered-call backtest run" });
    expect(cancelButton).toBeDisabled();
    expect(cancelButton).toHaveAttribute("title", "Run ID is not available until the engine accepts the request.");
    expect(cancelButton).toHaveAttribute("aria-describedby", "covered-call-cancel-command-feedback");
    expect(screen.getByText("Run ID is not available until the engine accepts the request.")).toBeInTheDocument();

    const backButton = screen.getByRole("button", { name: "Back" });
    expect(backButton).toBeDisabled();
    expect(backButton).toHaveAttribute(
      "title",
      "Wait until the strategy engine accepts the backtest request before leaving run progress."
    );
    expect(screen.getByRole("button", { name: "1. Configure" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "1. Configure" })).toHaveAttribute(
      "aria-describedby",
      "covered-call-stage-navigation-feedback"
    );
    expect(screen.getByRole("button", { name: "2. Run" })).toHaveAttribute("aria-current", "step");
    expect(screen.getByRole("button", { name: "3. Results" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "3. Results" })).toHaveAttribute(
      "aria-describedby",
      "covered-call-stage-navigation-feedback"
    );
    expect(screen.getByText("Wait until the strategy engine accepts the backtest request before leaving run progress.")).toBeInTheDocument();
    expect(screen.getByText("Submitting backtest")).toBeInTheDocument();
  });

  it("requires confirmation before cancelling an accepted covered-call run", async () => {
    vi.mocked(coveredCallApi.startCoveredCallBacktest).mockResolvedValue({
      runId: "run-1",
      queuedAt: "2024-07-01T00:00:00Z"
    });
    vi.mocked(coveredCallApi.cancelCoveredCallRun).mockResolvedValue({
      runId: "run-1",
      phase: "Cancelled",
      percentComplete: 0,
      currentBacktestDate: null,
      failureMessage: null
    });

    renderCoveredCallScreen();

    await act(async () => {
      fireEvent.change(screen.getByLabelText(/Min strike/i), { target: { value: "500" } });
    });
    await waitFor(() => expect(screen.getByRole("button", { name: "Run covered-call backtest" })).not.toBeDisabled());
    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: "Run covered-call backtest" }));
    });

    const cancelButton = await screen.findByRole("button", { name: "Cancel covered-call backtest run" });
    expect(cancelButton).not.toBeDisabled();

    await act(async () => {
      fireEvent.click(cancelButton);
    });

    expect(coveredCallApi.cancelCoveredCallRun).not.toHaveBeenCalled();
    expect(screen.getByRole("button", {
      name: "Confirm cancel covered-call backtest run run-1. This stops the active backtest request."
    })).toBeInTheDocument();
    expect(screen.getByText("Cancel confirmation pending. Confirm cancel stops this covered-call backtest run.")).toBeInTheDocument();

    await act(async () => {
      fireEvent.click(screen.getByRole("button", {
        name: "Confirm cancel covered-call backtest run run-1. This stops the active backtest request."
      }));
    });

    await waitFor(() => {
      expect(coveredCallApi.cancelCoveredCallRun).toHaveBeenCalledWith("run-1");
    });
  });

  it("renders results workflow handoffs after a completed run", async () => {
    vi.mocked(coveredCallApi.startCoveredCallBacktest).mockResolvedValue({
      runId: "run-1",
      queuedAt: "2024-07-01T00:00:00Z"
    });
    vi.mocked(coveredCallApi.getCoveredCallRunStatus).mockResolvedValue({
      runId: "run-1",
      phase: "Completed",
      percentComplete: 1,
      currentBacktestDate: null,
      failureMessage: null
    });
    vi.mocked(coveredCallApi.getCoveredCallRunResult).mockResolvedValue(completedRunResult);

    renderCoveredCallScreen();

    await act(async () => {
      fireEvent.change(screen.getByLabelText(/Min strike/i), { target: { value: "500" } });
    });
    await waitFor(() => expect(screen.getByRole("button", { name: "Run covered-call backtest" })).not.toBeDisabled());
    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: "Run covered-call backtest" }));
    });

    await act(async () => {
      await Promise.resolve();
    });
    expect(coveredCallApi.startCoveredCallBacktest).toHaveBeenCalledTimes(1);

    expect(await screen.findByRole("navigation", { name: "Covered-call results next workflow" }, { timeout: 3000 })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Validate live quote evidence for SPY" })).toHaveAttribute(
      "href",
      "/data/quotes?symbol=SPY"
    );
    expect(screen.getByRole("link", { name: "Open Strategy Designer to refine covered-call payoff" })).toHaveAttribute(
      "href",
      "/strategy/designer"
    );
    expect(screen.getByRole("link", { name: "Open report packs to package covered-call run evidence" })).toHaveAttribute(
      "href",
      "/reporting/report-packs"
    );
  });

  it("renders completed trade timeline as selectable dense rows with a linked detail inspector", async () => {
    vi.mocked(coveredCallApi.listCoveredCallRuns).mockResolvedValue([historicalRun]);
    vi.mocked(coveredCallApi.getCoveredCallRunResult).mockResolvedValue(completedRunWithTrades);

    renderCoveredCallScreen();

    const historyRow = await screen.findByRole("row", {
      name: "Reload covered-call run run-history-1 for SPY"
    });
    fireEvent.click(historyRow);

    const tradeTable = await screen.findByRole("table", { name: "SPY covered-call trade timeline" });
    const firstTrade = await screen.findByRole("row", {
      name: "Inspect SPY trade 1, entry 2024-01-10, exit 2024-01-24, strike 505.00, PnL $320, status Closed gain."
    });
    const assignedTrade = await screen.findByRole("row", {
      name: "Inspect SPY trade 2, entry 2024-02-01, exit 2024-02-12, strike 510.00, PnL -$140, status Assigned."
    });

    expect(tradeTable).toBeInTheDocument();
    expect(within(tradeTable).getByRole("columnheader", { name: "Status" })).toBeInTheDocument();
    expect(within(tradeTable).getByText("Closed gain")).toBeInTheDocument();
    expect(firstTrade).toHaveAttribute("aria-selected", "true");
    expect(firstTrade).toHaveAttribute("aria-controls", "covered-call-trade-detail");
    expect(screen.getByRole("region", {
      name: "Selected covered-call trade 1: SPY 505.00 call"
    })).toBeInTheDocument();

    assignedTrade.focus();
    fireEvent.keyDown(assignedTrade, { key: "Enter" });

    expect(assignedTrade).toHaveAttribute("aria-selected", "true");
    expect(assignedTrade).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByRole("region", {
      name: "Selected covered-call trade 2: SPY 510.00 call"
    })).toBeInTheDocument();
    expect(screen.getByText("Take profit")).toBeInTheDocument();
    expect(screen.getByText("Assigned; exit reason Assigned; -$140 total net PnL.")).toBeInTheDocument();
  });

  it("renders payoff position controls and updates the selected short-call diagram", async () => {
    vi.mocked(coveredCallApi.listCoveredCallRuns).mockResolvedValue([historicalRun]);
    vi.mocked(coveredCallApi.getCoveredCallRunResult).mockResolvedValue(completedRunWithOpenPositions);

    renderCoveredCallScreen();

    fireEvent.click(await screen.findByRole("row", {
      name: "Reload covered-call run run-history-1 for SPY"
    }));

    const firstPosition = await screen.findByRole("button", {
      name: "Selected SPY 505.00 call expiring 2024-02-16 payoff diagram"
    });
    const secondPosition = screen.getByRole("button", {
      name: "Select SPY 510.00 call expiring 2024-03-15 payoff diagram"
    });

    expect(firstPosition).toHaveAttribute("aria-pressed", "true");
    expect(screen.getByRole("img", { name: "SPY 505.00 short-call payoff diagram" })).toBeInTheDocument();
    expect(screen.getByText("2 x 505.00 call expiring 2024-02-16 - short-call break-even about $507.35")).toBeInTheDocument();

    fireEvent.click(secondPosition);

    expect(await screen.findByRole("button", {
      name: "Selected SPY 510.00 call expiring 2024-03-15 payoff diagram"
    })).toHaveAttribute("aria-pressed", "true");
    expect(screen.getByRole("img", { name: "SPY 510.00 short-call payoff diagram" })).toBeInTheDocument();
    expect(screen.getByText("1 x 510.00 call expiring 2024-03-15 - short-call break-even about $511.90")).toBeInTheDocument();
  });

  it("renders structured run-reload diagnostics when a cached result cannot be reopened", async () => {
    vi.mocked(coveredCallApi.listCoveredCallRuns).mockResolvedValue([historicalRun]);
    vi.mocked(coveredCallApi.getCoveredCallRunResult).mockRejectedValue(new ApiError({
      path: "/api/covered-call/runs/run-history-1/result",
      status: 410,
      title: "Cached result expired",
      detail: "Run completed but the cached result has expired."
    }));

    renderCoveredCallScreen();

    fireEvent.click(await screen.findByRole("row", {
      name: "Reload covered-call run run-history-1 for SPY"
    }));

    const alert = await screen.findByText("Backtest issue");
    const banner = alert.closest("div")?.parentElement;
    expect(screen.getByText("Run completed but the cached result has expired.")).toBeInTheDocument();
    expect(screen.getByText("Endpoint returned 410 for /api/covered-call/runs/run-history-1/result.")).toBeInTheDocument();
    expect(screen.getByText("Cached result expired")).toBeInTheDocument();
    expect(banner).toBeInTheDocument();
  });
});
