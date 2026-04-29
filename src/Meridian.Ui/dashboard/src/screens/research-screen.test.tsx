import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ResearchScreen } from "@/screens/research-screen";
import * as api from "@/lib/api";
import { afterEach } from "vitest";
import type { PromotionRecord, ResearchWorkspaceResponse, RunComparisonRow, RunDiff } from "@/types";

const twoRuns: ResearchWorkspaceResponse = {
  metrics: [
    { id: "1", label: "Runs", value: "24", delta: "+8%", tone: "success" },
    { id: "2", label: "Queued", value: "3", delta: "0%", tone: "default" },
    { id: "3", label: "Needs Review", value: "2", delta: "-1%", tone: "warning" },
    { id: "4", label: "Promotions", value: "5", delta: "+2%", tone: "default" }
  ],
  runs: [
    {
      id: "run-1",
      strategyName: "Mean Reversion FX",
      engine: "Meridian Native",
      mode: "paper",
      status: "Running",
      dataset: "FX Majors",
      window: "90d",
      pnl: "+4.2%",
      sharpe: "1.41",
      lastUpdated: "2m ago",
      notes: "Primary paper candidate."
    },
    {
      id: "run-2",
      strategyName: "Index Momentum",
      engine: "Lean",
      mode: "backtest",
      status: "Completed",
      dataset: "US Equities",
      window: "180d",
      pnl: "+1.9%",
      sharpe: "0.91",
      lastUpdated: "5m ago",
      notes: "Completed backtest run."
    }
  ]
};

describe("ResearchScreen", () => {
  afterEach(() => {
    restoreApiSpy(api.compareRuns);
    restoreApiSpy(api.diffRuns);
    restoreApiSpy(api.getPromotionHistory);
  });

  it("opens a detail dialog with run notes when the Open button is clicked", async () => {
    const user = userEvent.setup();
    render(<ResearchScreen data={twoRuns} />);

    // Both runs are rendered; click the button next to the paper run
    await user.click(screen.getAllByRole("button", { name: /open/i })[0]);

    expect(screen.getByRole("dialog")).toBeInTheDocument();
    // The dialog notes for whichever row is first in sorted order should be visible
    expect(
      screen.queryByText("Primary paper candidate.") ??
      screen.queryByText("Completed backtest run.")
    ).toBeTruthy();
  });

  it("shows paper mode badge", () => {
    render(<ResearchScreen data={twoRuns} />);

    expect(screen.getByText("PAPER")).toBeInTheDocument();
  });

  it("renders an empty run-library row when no strategy runs are available", () => {
    render(<ResearchScreen data={{ ...twoRuns, runs: [] }} />);

    expect(screen.getByText("No strategy runs available. Start a backtest or paper session, then refresh Strategy."))
      .toBeInTheDocument();
    expect(screen.getByText("Strategy runs available for compare, diff, and detail review.")).toBeInTheDocument();
  });

  it("keeps compare and diff disabled until two runs are checked", async () => {
    const user = userEvent.setup();
    render(<ResearchScreen data={twoRuns} />);

    expect(screen.getByRole("button", { name: /compare 2 runs/i })).toBeDisabled();
    expect(screen.getByRole("button", { name: /diff 2 runs/i })).toBeDisabled();
    expect(screen.getByText("No runs selected")).toBeInTheDocument();

    const checkboxes = screen.getAllByRole("checkbox");
    await user.click(checkboxes[0]);
    await user.click(checkboxes[1]);

    expect(screen.getByRole("button", { name: /compare 2 runs/i })).toBeEnabled();
    expect(screen.getByRole("button", { name: /diff 2 runs/i })).toBeEnabled();
    expect(screen.getByText("Mean Reversion FX vs Index Momentum")).toBeInTheDocument();
  });

  it("calls compareRuns API and renders a comparison table cell", async () => {
    const comparisonRows: RunComparisonRow[] = [
      {
        runId: "run-1",
        strategyName: "Carry Alpha",
        mode: "paper",
        engine: "MeridianNative",
        status: "Running",
        netPnl: 3200,
        totalReturn: 0.042,
        finalEquity: null,
        maxDrawdown: -0.018,
        sharpeRatio: 1.41,
        fillCount: 27,
        lastUpdatedAt: "2026-03-26T10:00:00Z",
        promotionState: "CandidateForPaper",
        hasLedger: false,
        hasAuditTrail: false
      }
    ];
    vi.spyOn(api, "compareRuns").mockResolvedValue(comparisonRows);

    const user = userEvent.setup();
    render(<ResearchScreen data={twoRuns} />);

    const checkboxes = screen.getAllByRole("checkbox");
    await user.click(checkboxes[0]);
    await user.click(checkboxes[1]);
    await user.click(screen.getByRole("button", { name: /compare 2 runs/i }));

    // Comparison result appears in a table cell (distinct from the runs table header)
    await waitFor(() => {
      const cells = screen.getAllByText("Carry Alpha");
      expect(cells.some((el) => el.closest("td") !== null)).toBe(true);
    });
    expect(api.compareRuns).toHaveBeenCalledOnce();
  });

  it("renders empty comparison guidance when compare returns no rows", async () => {
    vi.spyOn(api, "compareRuns").mockResolvedValue([]);

    const user = userEvent.setup();
    render(<ResearchScreen data={twoRuns} />);

    const checkboxes = screen.getAllByRole("checkbox");
    await user.click(checkboxes[0]);
    await user.click(checkboxes[1]);
    await user.click(screen.getByRole("button", { name: /compare 2 runs/i }));

    await waitFor(() => {
      expect(screen.getAllByText("No comparison rows returned for the selected pair.").length).toBeGreaterThanOrEqual(2);
    });
  });

  it("renders unavailable placeholders for missing comparison values", async () => {
    const comparisonRows: RunComparisonRow[] = [
      {
        runId: "run-1",
        strategyName: "Carry Alpha",
        mode: "paper",
        engine: "MeridianNative",
        status: "Running",
        netPnl: 3200,
        totalReturn: 0.042,
        finalEquity: null,
        maxDrawdown: -0.018,
        sharpeRatio: null,
        fillCount: Number.NaN,
        lastUpdatedAt: "2026-03-26T10:00:00Z",
        promotionState: "CandidateForPaper",
        hasLedger: false,
        hasAuditTrail: false
      }
    ];
    vi.spyOn(api, "compareRuns").mockResolvedValue(comparisonRows);

    const user = userEvent.setup();
    render(<ResearchScreen data={twoRuns} />);

    const checkboxes = screen.getAllByRole("checkbox");
    await user.click(checkboxes[0]);
    await user.click(checkboxes[1]);
    await user.click(screen.getByRole("button", { name: /compare 2 runs/i }));

    await waitFor(() => {
      expect(screen.getAllByText("Unavailable").length).toBeGreaterThanOrEqual(2);
    });
  });

  it("shows an error banner when compare fails", async () => {
    vi.spyOn(api, "compareRuns").mockRejectedValue(new Error("Compare service unavailable"));

    const user = userEvent.setup();
    render(<ResearchScreen data={twoRuns} />);

    const checkboxes = screen.getAllByRole("checkbox");
    await user.click(checkboxes[0]);
    await user.click(checkboxes[1]);
    await user.click(screen.getByRole("button", { name: /compare 2 runs/i }));

    await waitFor(() => {
      expect(screen.getByRole("alert")).toHaveTextContent("Compare service unavailable");
    });
  });

  it("loads and displays run diff panel when Diff is clicked", async () => {
    const diff: RunDiff = {
      baseRunId: "run-1",
      targetRunId: "run-2",
      baseStrategyName: "Mean Reversion FX",
      targetStrategyName: "Index Momentum",
      addedPositions: [
        { symbol: "AAPL", baseQuantity: 0, targetQuantity: 100, basePnl: 0, targetPnl: 250, changeType: "Added" }
      ],
      removedPositions: [],
      modifiedPositions: [],
      parameterChanges: [{ key: "lookback", baseValue: "20", targetValue: "30" }],
      metrics: {
        netPnlDelta: 1200,
        totalReturnDelta: 0.01,
        fillCountDelta: 5,
        baseNetPnl: 3200,
        targetNetPnl: 4400,
        baseTotalReturn: 0.042,
        targetTotalReturn: 0.052
      }
    };
    vi.spyOn(api, "diffRuns").mockResolvedValue(diff);

    const user = userEvent.setup();
    render(<ResearchScreen data={twoRuns} />);

    const checkboxes = screen.getAllByRole("checkbox");
    await user.click(checkboxes[0]);
    await user.click(checkboxes[1]);
    await user.click(screen.getByRole("button", { name: /diff 2 runs/i }));

    await waitFor(() => {
      expect(screen.getByText("Position & parameter diff")).toBeInTheDocument();
    });
    expect(screen.getByText("lookback")).toBeInTheDocument();
    expect(api.diffRuns).toHaveBeenCalledOnce();
  });

  it("renders empty diff and unavailable parameter states", async () => {
    const diff: RunDiff = {
      baseRunId: "run-1",
      targetRunId: "run-2",
      baseStrategyName: "Mean Reversion FX",
      targetStrategyName: "Index Momentum",
      addedPositions: [],
      removedPositions: [],
      modifiedPositions: [],
      parameterChanges: [{ key: "lookback", baseValue: null, targetValue: null }],
      metrics: {
        netPnlDelta: 0,
        totalReturnDelta: 0,
        fillCountDelta: 0,
        baseNetPnl: null,
        targetNetPnl: null,
        baseTotalReturn: null,
        targetTotalReturn: null
      }
    };
    vi.spyOn(api, "diffRuns").mockResolvedValue(diff);

    const user = userEvent.setup();
    render(<ResearchScreen data={twoRuns} />);

    const checkboxes = screen.getAllByRole("checkbox");
    await user.click(checkboxes[0]);
    await user.click(checkboxes[1]);
    await user.click(screen.getByRole("button", { name: /diff 2 runs/i }));

    await waitFor(() => {
      expect(screen.getByText("No position changes returned for this diff.")).toBeInTheDocument();
    });
    expect(screen.getAllByText((_, element) =>
      element?.tagName.toLowerCase() === "li" &&
      element.textContent === "lookback: Unavailable -> Unavailable"
    ).length).toBe(1);
  });

  it("loads and displays promotion history when history button is clicked", async () => {
    const history: PromotionRecord[] = [
      {
        promotionId: "promo-1",
        strategyId: "strat-1",
        strategyName: "Carry Pair FX",
        sourceRunType: "backtest",
        targetRunType: "paper",
        qualifyingSharpe: 1.82,
        qualifyingMaxDrawdownPercent: -0.032,
        qualifyingTotalReturn: 0.065,
        promotedAt: "2026-03-25T12:00:00Z"
      }
    ];
    vi.spyOn(api, "getPromotionHistory").mockResolvedValue(history);

    const user = userEvent.setup();
    render(<ResearchScreen data={twoRuns} />);

    await user.click(screen.getByRole("button", { name: /promotion history/i }));

    await waitFor(() => {
      expect(screen.getByText("Carry Pair FX")).toBeInTheDocument();
    });
    expect(screen.getByText("1.820")).toBeInTheDocument();
    expect(api.getPromotionHistory).toHaveBeenCalledOnce();
  });

  it("renders empty promotion-history guidance when history returns no rows", async () => {
    vi.spyOn(api, "getPromotionHistory").mockResolvedValue([]);

    const user = userEvent.setup();
    render(<ResearchScreen data={twoRuns} />);

    await user.click(screen.getByRole("button", { name: /promotion history/i }));

    await waitFor(() => {
      expect(screen.getAllByText("No promotion history records returned.").length).toBeGreaterThanOrEqual(2);
    });
  });
});

function restoreApiSpy(fn: unknown) {
  const spy = fn as { mockRestore?: () => void };
  spy.mockRestore?.();
}
