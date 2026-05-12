import { act, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ResearchScreen } from "@/screens/research-screen";
import * as api from "@/lib/api";
import { afterEach } from "vitest";
import { renderWithRouter } from "@/test/render";
import type { PromotionEvaluationResult, PromotionRecord, ResearchWorkspaceResponse, RunComparisonRow, RunDiff } from "@/types";

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
    restoreApiSpy(api.evaluatePromotion);
    restoreApiSpy(api.createPaperSession);
  });

  it("renders the Strategy loading state with pending semantics", () => {
    renderWithRouter(<ResearchScreen data={null} />);

    const loading = screen.getByRole("status", { name: "Loading Strategy" });
    expect(loading).toHaveAttribute("aria-busy", "true");
    expect(loading).toHaveAccessibleDescription("Waiting for run history, PlotTool state, and promotion evidence.");
    expect(loading).toHaveClass("border-[var(--state-pending-bd)]", "bg-[var(--state-pending-bg)]");
    expect(screen.getByText("Loading")).toBeInTheDocument();
    expect(screen.getByLabelText("Route Strategy")).toBeInTheDocument();
  });

  it("opens a detail dialog with run notes when the Open button is clicked", async () => {
    const user = userEvent.setup();
    renderWithRouter(<ResearchScreen data={twoRuns} />);

    await user.click(screen.getAllByRole("button", { name: /open/i })[0]);

    const dialog = screen.getByRole("dialog", { name: "Mean Reversion FX" });
    expect(dialog).toHaveAccessibleDescription("Mean Reversion FX is Running in PAPER mode.");
    expect(dialog).toHaveTextContent("Primary paper candidate.");
    expect(screen.getByLabelText("Selected strategy run evidence")).toBeInTheDocument();
    expect(screen.getAllByText("run-1").length).toBeGreaterThan(0);
    expect(screen.getByRole("button", { name: "Close Mean Reversion FX run detail" })).toBeInTheDocument();
  });

  it("closes the run detail dialog with Escape", async () => {
    const user = userEvent.setup();
    renderWithRouter(<ResearchScreen data={twoRuns} />);

    await user.click(screen.getAllByRole("button", { name: /open/i })[0]);

    const closeButton = screen.getByRole("button", { name: "Close Mean Reversion FX run detail" });
    closeButton.focus();
    await user.keyboard("{Escape}");

    expect(screen.queryByRole("dialog", { name: "Mean Reversion FX" })).not.toBeInTheDocument();
  });

  it("shows paper mode badge", () => {
    renderWithRouter(<ResearchScreen data={twoRuns} />);

    expect(screen.getAllByText("PAPER").length).toBeGreaterThan(0);
  });

  it("renders the PlotTool workstation view inside the Strategy lane by default", () => {
    renderWithRouter(<ResearchScreen data={twoRuns} />);

    expect(screen.getByText("PlotTool workstation")).toBeInTheDocument();
    expect(screen.getByLabelText("PlotTool study brief")).toBeInTheDocument();
    expect(screen.getByText("Strategy notebooks")).toBeInTheDocument();
    expect(screen.getByRole("img", { name: "Mean Reversion FX vs Index Momentum scatter" })).toBeInTheDocument();
    expect(screen.getByText(/Spread \(bps\) against 3m implied vol/)).toBeInTheDocument();
    expect(screen.getByLabelText("PlotTool chart legend")).toBeInTheDocument();
    expect(screen.getAllByText("Current marker").length).toBeGreaterThan(0);
    expect(screen.getByText("Meridian overlays")).toBeInTheDocument();
  });

  it("switches to the PlotTool statistics view", async () => {
    const user = userEvent.setup();
    renderWithRouter(<ResearchScreen data={twoRuns} />);

    await user.click(screen.getByRole("tab", { name: "Statistics" }));

    expect(screen.getByText("Distribution profile")).toBeInTheDocument();
    expect(screen.getByText("Residual distribution")).toBeInTheDocument();
    expect(screen.getByLabelText("PlotTool residual distribution chart")).toBeInTheDocument();
    expect(screen.getByText("Regression frame")).toBeInTheDocument();
    expect(screen.getByRole("table", { name: "PlotTool moments table" })).toBeInTheDocument();
    expect(screen.getByText("PlotTool moments for the active strategy pair.")).toBeInTheDocument();
    expect(screen.getByText("Observation sheet")).toBeInTheDocument();
    expect(screen.getByRole("table", { name: "PlotTool observation sheet" })).toBeInTheDocument();
    expect(screen.getByText("Recent PlotTool observations with spread, implied volatility, z-score, and signal.")).toBeInTheDocument();
  });

  it("switches PlotTool tabs from keyboard navigation", async () => {
    const user = userEvent.setup();
    renderWithRouter(<ResearchScreen data={twoRuns} />);

    screen.getByRole("tab", { name: "Workstation" }).focus();
    await user.keyboard("{ArrowRight}");

    expect(screen.getByRole("tab", { name: "Statistics" })).toHaveAttribute("aria-selected", "true");
    expect(screen.getByText("Distribution profile")).toBeInTheDocument();
  });

  it("renders an empty run-library row when no strategy runs are available", () => {
    renderWithRouter(<ResearchScreen data={{ ...twoRuns, runs: [] }} />);

    expect(screen.getAllByText("No strategy runs available. Start a backtest or paper session, then refresh Strategy.").length)
      .toBeGreaterThanOrEqual(2);
    expect(screen.getByText("Strategy runs available for compare, diff, and detail review.")).toBeInTheDocument();
  });

  it("links strategy run rows to a visible detail panel for click and keyboard inspection", async () => {
    const user = userEvent.setup();
    renderWithRouter(<ResearchScreen data={twoRuns} />);

    const firstRow = screen.getByRole("row", { name: "Inspect Mean Reversion FX run detail" });
    const secondRow = screen.getByRole("row", { name: "Inspect Index Momentum run detail" });
    expect(firstRow).toHaveAttribute("aria-controls", "strategy-run-library-selected-run-detail");
    expect(firstRow).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByRole("region", { name: "Selected strategy run detail for Mean Reversion FX" })).toBeInTheDocument();

    await user.click(secondRow);

    expect(secondRow).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByRole("region", { name: "Selected strategy run detail for Index Momentum" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Open Index Momentum evidence packet" })).toHaveAttribute(
      "href",
      "/reporting/evidence?subjectKind=strategy-run&subjectId=run-2"
    );

    firstRow.focus();
    await user.keyboard("{Enter}");

    expect(firstRow).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByRole("region", { name: "Selected strategy run detail for Mean Reversion FX" })).toBeInTheDocument();
  });

  it("keeps compare and diff disabled until two runs are checked", async () => {
    const user = userEvent.setup();
    renderWithRouter(<ResearchScreen data={twoRuns} />);

    const disabledCompare = screen.getByRole("button", { name: /compare 2 runs unavailable/i });
    const disabledDiff = screen.getByRole("button", { name: /diff 2 runs unavailable/i });
    expect(disabledCompare).toBeDisabled();
    expect(disabledCompare).toHaveAttribute("title", "Select exactly two runs before using this command. 0 selected.");
    expect(disabledDiff).toBeDisabled();
    expect(disabledDiff).toHaveAttribute("title", "Select exactly two runs before using this command. 0 selected.");
    expect(screen.getByText("No runs selected")).toBeInTheDocument();

    const checkboxes = screen.getAllByRole("checkbox");
    await user.click(checkboxes[0]);
    await user.click(checkboxes[1]);

    expect(screen.getByRole("button", { name: /compare 2 runs: mean reversion fx and index momentum/i })).toBeEnabled();
    expect(screen.getByRole("button", { name: /diff 2 runs: mean reversion fx and index momentum/i })).toBeEnabled();
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
    renderWithRouter(<ResearchScreen data={twoRuns} />);

    const checkboxes = screen.getAllByRole("checkbox");
    await user.click(checkboxes[0]);
    await user.click(checkboxes[1]);
    await user.click(screen.getByRole("button", { name: /compare 2 runs/i }));

    // Comparison result appears in a table cell (distinct from the runs table header)
    await waitFor(() => {
      const cells = screen.getAllByText("Carry Alpha");
      expect(cells.some((el) => el.closest("td") !== null)).toBe(true);
    });
    expect(screen.getByRole("table", { name: "Strategy run comparison evidence" })).toBeInTheDocument();
    expect(screen.getByRole("row", { name: /Carry Alpha: Running; net P&L \+\$3,200/ })).toBeInTheDocument();
    expect(screen.getByText("+4.20%")).toBeInTheDocument();
    expect(screen.getByText("-1.80%")).toBeInTheDocument();
    expect(screen.getAllByText("Ledger missing; Audit missing").length).toBeGreaterThanOrEqual(1);
    expect(api.compareRuns).toHaveBeenCalledOnce();
  });

  it("clears pending compare evidence when the selected run pair changes before the response", async () => {
    const pending = createDeferred<RunComparisonRow[]>();
    vi.spyOn(api, "compareRuns").mockReturnValueOnce(pending.promise);

    const user = userEvent.setup();
    renderWithRouter(<ResearchScreen data={twoRuns} />);

    const checkboxes = screen.getAllByRole("checkbox");
    await user.click(checkboxes[0]);
    await user.click(checkboxes[1]);
    await user.click(screen.getByRole("button", { name: /compare 2 runs/i }));

    expect(screen.getByRole("button", { name: /comparing/i })).toBeDisabled();

    await user.click(checkboxes[1]);

    await act(async () => {
      pending.resolve([
        {
          runId: "run-stale",
          strategyName: "Stale Carry Pair",
          mode: "paper",
          engine: "MeridianNative",
          status: "Running",
          netPnl: 4200,
          totalReturn: 0.052,
          finalEquity: 104200,
          maxDrawdown: -0.02,
          sharpeRatio: 1.5,
          fillCount: 31,
          lastUpdatedAt: "2026-03-26T10:00:00Z",
          promotionState: "CandidateForPaper",
          hasLedger: true,
          hasAuditTrail: true
        }
      ]);
      await pending.promise;
    });

    expect(screen.queryByRole("table", { name: "Strategy run comparison evidence" })).not.toBeInTheDocument();
    expect(screen.queryByText("Stale Carry Pair")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: /compare 2 runs/i })).toBeDisabled();
  });

  it("renders empty comparison guidance when compare returns no rows", async () => {
    vi.spyOn(api, "compareRuns").mockResolvedValue([]);

    const user = userEvent.setup();
    renderWithRouter(<ResearchScreen data={twoRuns} />);

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
    renderWithRouter(<ResearchScreen data={twoRuns} />);

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
    renderWithRouter(<ResearchScreen data={twoRuns} />);

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
    renderWithRouter(<ResearchScreen data={twoRuns} />);

    const checkboxes = screen.getAllByRole("checkbox");
    await user.click(checkboxes[0]);
    await user.click(checkboxes[1]);
    await user.click(screen.getByRole("button", { name: /diff 2 runs/i }));

    await waitFor(() => {
      expect(screen.getByText("Position & parameter diff")).toBeInTheDocument();
    });
    expect(screen.getByRole("region", { name: "Strategy run diff for Mean Reversion FX and Index Momentum" }))
      .toBeInTheDocument();
    expect(screen.getByLabelText("Run diff metric summary")).toBeInTheDocument();
    expect(screen.getByRole("group", { name: /Net P&L delta \+\$1,200/ })).toBeInTheDocument();
    expect(screen.getByLabelText("1 position change returned")).toBeInTheDocument();
    expect(screen.getByRole("listitem", { name: "AAPL Added. Qty +100. P&L +$250." })).toBeInTheDocument();
    expect(screen.getByText("Qty +100")).toBeInTheDocument();
    expect(screen.getByText("lookback")).toBeInTheDocument();
    expect(screen.getByRole("listitem", { name: "lookback changed from 20 to 30." })).toHaveTextContent("20 -> 30");
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
    renderWithRouter(<ResearchScreen data={twoRuns} />);

    const checkboxes = screen.getAllByRole("checkbox");
    await user.click(checkboxes[0]);
    await user.click(checkboxes[1]);
    await user.click(screen.getByRole("button", { name: /diff 2 runs/i }));

    await waitFor(() => {
      expect(screen.getByText("No position changes returned for this diff.")).toBeInTheDocument();
    });
    expect(screen.getByRole("listitem", { name: "lookback changed from Unavailable to Unavailable." }))
      .toHaveTextContent("Unavailable -> Unavailable");
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
      },
      {
        promotionId: "promo-2",
        strategyId: "strat-2",
        strategyName: "Index Momentum",
        sourceRunType: "paper",
        targetRunType: "live",
        sourceRunId: "run-paper-2",
        targetRunId: "run-live-2",
        decision: "Approved for live",
        approvedBy: "risk-ops",
        approvalReason: "Manual override evidence accepted.",
        auditReference: "audit-live-2",
        qualifyingSharpe: 1.31,
        qualifyingMaxDrawdownPercent: -0.044,
        qualifyingTotalReturn: 0.088,
        promotedAt: "2026-03-26T12:00:00Z"
      }
    ];
    vi.spyOn(api, "getPromotionHistory").mockResolvedValue(history);

    const user = userEvent.setup();
    renderWithRouter(<ResearchScreen data={twoRuns} />);

    await user.click(screen.getByRole("button", { name: /promotion history/i }));

    await waitFor(() => {
      expect(screen.getAllByText("Carry Pair FX").length).toBeGreaterThanOrEqual(1);
    });
    expect(screen.getAllByText("1.820").length).toBeGreaterThanOrEqual(1);
    const carryRow = screen.getByRole("row", { name: "Inspect Carry Pair FX promotion decision" });
    const momentumRow = screen.getByRole("row", { name: "Inspect Index Momentum promotion decision" });
    expect(carryRow).toHaveAttribute("aria-controls", "strategy-promotion-history-selected-detail");
    expect(carryRow).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByRole("region", { name: "Selected promotion decision detail for Carry Pair FX" }))
      .toHaveTextContent("Approved for paper promotion decision");

    await user.click(momentumRow);

    expect(momentumRow).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByRole("region", { name: "Selected promotion decision detail for Index Momentum" }))
      .toHaveTextContent("Manual override evidence accepted.");
    expect(screen.getByText("audit-live-2")).toBeInTheDocument();

    carryRow.focus();
    await user.keyboard("{Enter}");

    expect(carryRow).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByRole("region", { name: "Selected promotion decision detail for Carry Pair FX" }))
      .toBeInTheDocument();
    expect(api.getPromotionHistory).toHaveBeenCalledOnce();
  });

  it("renders empty promotion-history guidance when history returns no rows", async () => {
    vi.spyOn(api, "getPromotionHistory").mockResolvedValue([]);

    const user = userEvent.setup();
    renderWithRouter(<ResearchScreen data={twoRuns} />);

    await user.click(screen.getByRole("button", { name: /promotion history/i }));

    await waitFor(() => {
      expect(screen.getAllByText("No promotion history records returned.").length).toBeGreaterThanOrEqual(2);
    });
  });

  it("validates promotion initial cash before starting a paper session", async () => {
    vi.spyOn(api, "evaluatePromotion").mockResolvedValue({
      runId: "run-2",
      strategyId: "run-2",
      strategyName: "Index Momentum",
      sourceMode: "backtest",
      targetMode: "paper",
      isEligible: true,
      sharpeRatio: 1.25,
      maxDrawdownPercent: -0.04,
      totalReturn: 0.08,
      reason: "Promotion gates passed.",
      found: true,
      ready: true
    });
    vi.spyOn(api, "createPaperSession").mockResolvedValue({
      sessionId: "session-1",
      strategyId: "run-2",
      strategyName: "Index Momentum",
      initialCash: 100000,
      createdAt: "2026-05-09T00:00:00Z",
      closedAt: null,
      isActive: true
    });

    const user = userEvent.setup();
    renderWithRouter(<ResearchScreen data={twoRuns} />);

    await user.click(screen.getByRole("checkbox", { name: "Select Index Momentum for compare and diff" }));
    await user.click(screen.getByRole("button", { name: /promote to paper/i }));

    const cashInput = await screen.findByLabelText("Initial cash ($)");
    const startButton = screen.getByRole("button", { name: "Start paper session from selected strategy run" });
    expect(startButton).toBeEnabled();

    await user.clear(cashInput);
    await user.type(cashInput, "500");

    expect(cashInput).toHaveAttribute("aria-invalid", "true");
    expect(screen.getByText("Enter at least $1,000 in whole dollars.")).toBeInTheDocument();
    expect(startButton).toBeDisabled();

    await user.clear(cashInput);
    await user.type(cashInput, "125000");
    await user.click(startButton);

    await waitFor(() => {
      expect(api.createPaperSession).toHaveBeenCalledWith("run-2", "Index Momentum", 125000);
    });
    expect(screen.getByText("Paper session created - session session-1")).toBeInTheDocument();
  });

  it("discards pending promotion evaluation when the selected run changes", async () => {
    const pending = createDeferred<PromotionEvaluationResult>();
    vi.spyOn(api, "evaluatePromotion").mockReturnValueOnce(pending.promise);

    const user = userEvent.setup();
    renderWithRouter(<ResearchScreen data={twoRuns} />);

    await user.click(screen.getByRole("checkbox", { name: "Select Index Momentum for compare and diff" }));
    await user.click(screen.getByRole("button", { name: /promote to paper/i }));

    expect(screen.getByRole("button", { name: /evaluating/i })).toBeDisabled();

    await user.click(screen.getByRole("checkbox", { name: "Remove Index Momentum for compare and diff" }));

    await act(async () => {
      pending.resolve({
        runId: "run-2",
        strategyId: "run-2",
        strategyName: "Index Momentum",
        sourceMode: "backtest",
        targetMode: "paper",
        isEligible: true,
        sharpeRatio: 1.25,
        maxDrawdownPercent: -0.04,
        totalReturn: 0.08,
        reason: "Promotion gates passed.",
        found: true,
        ready: true
      });
      await pending.promise;
    });

    expect(screen.queryByText("Eligible for paper trading")).not.toBeInTheDocument();
    expect(screen.queryByLabelText("Initial cash ($)")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: /promote to paper/i })).toBeDisabled();
  });
});

function restoreApiSpy(fn: unknown) {
  const spy = fn as { mockRestore?: () => void };
  spy.mockRestore?.();
}

function createDeferred<T>() {
  let resolve!: (value: T | PromiseLike<T>) => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((promiseResolve, promiseReject) => {
    resolve = promiseResolve;
    reject = promiseReject;
  });

  return { promise, resolve, reject };
}
