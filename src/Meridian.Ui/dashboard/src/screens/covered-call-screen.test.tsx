import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import * as coveredCallApi from "@/lib/api/covered-call";
import { CoveredCallScreen } from "@/screens/covered-call-screen";
import { COVERED_CALL_CHAIN_DETAIL_PANEL_ID } from "@/screens/covered-call-screen.view-model";
import type { CoveredCallChainPreview, CoveredCallRunResult } from "@/types/covered-call";

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

const completedResult: CoveredCallRunResult = {
  runId: "history-1",
  underlyingSymbol: "SPY",
  from: "2024-01-01",
  to: "2024-06-30",
  label: "Income sleeve",
  metrics: {
    cagr: 0.12,
    annualizedVolatility: 0.16,
    sharpeRatio: 1.1,
    sortinoRatio: 1.4,
    calmarRatio: 2.3,
    maxDrawdownPct: -0.05,
    winRate: 0.7,
    assignmentRate: 0.04,
    averageHoldingDays: 18,
    totalOptionTrades: 4,
    assignedTrades: 0,
    totalPremiumCollected: 1200,
    totalOptionPnl: 840,
    upCapture: 0.6,
    downCapture: 0.9,
    monthlyVar1Pct: -0.08,
    monthlyVar5Pct: -0.05,
    monthlyCVar5Pct: -0.06,
    returnSkewness: 0,
    returnKurtosis: 3,
    annualizedTurnover: 6
  },
  equityCurve: [],
  trades: [],
  openPositionsAtEnd: []
};

describe("CoveredCallScreen", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(coveredCallApi.previewCoveredCallChain).mockResolvedValue(chainPreview);
    vi.mocked(coveredCallApi.listCoveredCallRuns).mockResolvedValue([]);
    vi.mocked(coveredCallApi.getCoveredCallRunResult).mockResolvedValue(completedResult);
  });

  it("renders chain preview rows with keyboard selection and a linked detail panel", async () => {
    render(<CoveredCallScreen />);

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

  it("renders previous runs with UTC labels and opens history rows by keyboard", async () => {
    vi.mocked(coveredCallApi.listCoveredCallRuns).mockResolvedValueOnce([
      {
        runId: "history-1",
        underlyingSymbol: "SPY",
        from: "2024-01-01",
        to: "2024-06-30",
        label: "Income sleeve",
        status: "Completed",
        startedAt: "2024-06-30T14:05:30Z",
        endedAt: "2024-06-30T14:08:00Z",
        cagr: 0.12,
        sharpeRatio: 1.1,
        winRate: 0.7
      }
    ]);

    render(<CoveredCallScreen />);

    const row = await screen.findByRole("row", {
      name: "Open covered-call run history-1. SPY. Completed. Started Jun 30, 2024 14:05 UTC."
    });
    expect(screen.getByText("Jun 30, 2024 14:05 UTC")).toBeInTheDocument();
    expect(screen.getByText("70.00%")).toBeInTheDocument();

    row.focus();
    fireEvent.keyDown(row, { key: "Enter" });

    await waitFor(() => {
      expect(coveredCallApi.getCoveredCallRunResult).toHaveBeenCalledWith("history-1");
    });
    expect(await screen.findByText("Metrics")).toBeInTheDocument();
  });

  it("surfaces history load failures with a retry action", async () => {
    const retry = deferred<Awaited<ReturnType<typeof coveredCallApi.listCoveredCallRuns>>>();
    vi.mocked(coveredCallApi.listCoveredCallRuns)
      .mockRejectedValueOnce(new Error("HTTP 503"))
      .mockReturnValueOnce(retry.promise);

    render(<CoveredCallScreen />);

    expect(await screen.findByRole("alert")).toHaveTextContent("Run history failed to load: HTTP 503");

    fireEvent.click(screen.getByRole("button", { name: "Retry covered-call run history" }));
    expect(screen.getByRole("button", { name: "Loading covered-call run history" })).toBeDisabled();
    expect(screen.getByRole("status")).toHaveTextContent("Loading saved covered-call evidence...");

    retry.resolve([]);

    await waitFor(() => {
      expect(coveredCallApi.listCoveredCallRuns).toHaveBeenCalledTimes(2);
    });
  });
});

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((res, rej) => {
    resolve = res;
    reject = rej;
  });
  return { promise, resolve, reject };
}
