import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter } from "react-router-dom";
import * as coveredCallApi from "@/lib/api/covered-call";
import { CoveredCallScreen } from "@/screens/covered-call-screen";
import { COVERED_CALL_CHAIN_DETAIL_PANEL_ID } from "@/screens/covered-call-screen.view-model";
import type { CoveredCallChainPreview, CoveredCallRunHandle, CoveredCallRunResult } from "@/types/covered-call";

vi.mock("@/lib/api/covered-call", () => ({
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
    expect(screen.getByRole("button", { name: "3. Results" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "3. Results" })).toHaveAttribute(
      "aria-describedby",
      "covered-call-stage-navigation-feedback"
    );
    expect(screen.getByText("Wait until the strategy engine accepts the backtest request before leaving run progress.")).toBeInTheDocument();
    expect(screen.getByText("Submitting backtest")).toBeInTheDocument();
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
});
