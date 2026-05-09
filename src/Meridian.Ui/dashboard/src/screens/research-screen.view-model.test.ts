import { describe, expect, it } from "vitest";
import {
  buildComparisonTable,
  buildDiffPanel,
  buildPlotToolState,
  buildPlotToolTabs,
  buildPromotionCashForm,
  buildPromotionPanelState,
  buildPromotionHistoryTable,
  buildResearchRunLibraryState,
  buildRunDetail,
  buildRunTable,
  nextPlotToolViewForKey,
  parsePromotionInitialCashInput,
  shouldCloseRunDetailForKey,
  toggleRunSelection
} from "@/screens/research-screen.view-model";
import type { MetricSnapshot, PromotionEvaluationResult, PromotionRecord, ResearchPlotToolPayload, ResearchRunRecord, RunDiff, RunComparisonRow } from "@/types";

const runs: ResearchRunRecord[] = [
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
  },
  {
    id: "run-3",
    strategyName: "Carry Alpha",
    engine: "Meridian Native",
    mode: "backtest",
    status: "Queued",
    dataset: "FX Majors",
    window: "30d",
    pnl: "+0.7%",
    sharpe: "0.41",
    lastUpdated: "8m ago",
    notes: "Queued candidate."
  }
];

const comparison: RunComparisonRow[] = [
  {
    runId: "run-1",
    strategyName: "Mean Reversion FX",
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

const diff: RunDiff = {
  baseRunId: "run-1",
  targetRunId: "run-2",
  baseStrategyName: "Mean Reversion FX",
  targetStrategyName: "Index Momentum",
  addedPositions: [],
  removedPositions: [],
  modifiedPositions: [],
  parameterChanges: [],
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

const promotionEvaluation: PromotionEvaluationResult = {
  runId: "run-2",
  strategyId: "strat-2",
  strategyName: "Index Momentum",
  sourceMode: "backtest",
  targetMode: "paper",
  isEligible: false,
  sharpeRatio: 0.91,
  maxDrawdownPercent: -0.124,
  totalReturn: 0.019,
  reason: "Risk review failed.",
  found: true,
  ready: false,
  blockingReasons: ["Sharpe ratio below promotion threshold.", "Drawdown exceeds paper gate."]
};

const metrics: MetricSnapshot[] = [
  { id: "runs", label: "Runs", value: "24", delta: "+8%", tone: "success" },
  { id: "queued", label: "Queued", value: "3", delta: "0%", tone: "default" },
  { id: "review", label: "Needs Review", value: "2", delta: "-1%", tone: "warning" }
];

describe("research-screen view model", () => {
  it("keeps run selection capped to the latest two ids and supports deselection", () => {
    expect(toggleRunSelection([], "run-1")).toEqual(["run-1"]);
    expect(toggleRunSelection(["run-1"], "run-2")).toEqual(["run-1", "run-2"]);
    expect(toggleRunSelection(["run-1", "run-2"], "run-3")).toEqual(["run-2", "run-3"]);
    expect(toggleRunSelection(["run-1", "run-2"], "run-1")).toEqual(["run-2"]);
  });

  it("derives pair readiness and operator copy from selected runs", () => {
    const state = buildResearchRunLibraryState({
      runs,
      selectedIds: ["run-1", "run-2"],
      selectedRun: null,
      comparison: [],
      runDiff: null,
      promotionHistory: [],
      activeCommand: null,
      actionError: null
    });

    expect(state.canCompare).toBe(true);
    expect(state.canDiff).toBe(true);
    expect(state.selectionText).toBe("Mean Reversion FX vs Index Momentum");
    expect(state.selectionDetail).toBe("Ready to compare or diff the selected run pair.");
    expect(state.runTable.rows[0].selectAriaLabel).toBe("Select Mean Reversion FX");
  });

  it("derives busy labels, errors, and result announcements", () => {
    const busy = buildResearchRunLibraryState({
      runs,
      selectedIds: ["run-1", "run-2"],
      selectedRun: null,
      comparison: [],
      runDiff: null,
      promotionHistory: [],
      activeCommand: "compare",
      actionError: null
    });

    expect(busy.canCompare).toBe(false);
    expect(busy.compareButtonLabel).toBe("Comparing...");
    expect(busy.statusAnnouncement).toBe("Comparing selected research runs.");

    const failed = buildResearchRunLibraryState({
      runs,
      selectedIds: ["run-1", "run-2"],
      selectedRun: null,
      comparison: [],
      runDiff: null,
      promotionHistory: [],
      activeCommand: null,
      actionError: "Run comparison failed."
    });

    expect(failed.statusAnnouncement).toBe("Research command failed: Run comparison failed.");

    const compared = buildResearchRunLibraryState({
      runs,
      selectedIds: ["run-1", "run-2"],
      selectedRun: null,
      comparison,
      runDiff: null,
      promotionHistory: history,
      activeCommand: null,
      actionError: null
    });

    expect(compared.statusAnnouncement).toBe("1 comparison row loaded.");

    const diffed = buildResearchRunLibraryState({
      runs,
      selectedIds: ["run-1", "run-2"],
      selectedRun: null,
      comparison: [],
      runDiff: diff,
      promotionHistory: [],
      activeCommand: null,
      actionError: null
    });

    expect(diffed.statusAnnouncement).toBe("Run diff ready for Mean Reversion FX and Index Momentum.");
  });

  it("derives result table captions, empty copy, and unavailable placeholders", () => {
    const runTable = buildRunTable([]);
    expect(runTable.caption).toBe("Strategy runs available for compare, diff, and detail review.");
    expect(runTable.emptyText).toContain("No strategy runs available");

    const comparisonTable = buildComparisonTable([
      {
        ...comparison[0],
        sharpeRatio: null,
        fillCount: Number.NaN
      }
    ]);
    expect(comparisonTable.caption).toBe("Run comparison evidence returned by the workstation API.");
    expect(comparisonTable.rows[0].modeBadgeVariant).toBe("paper");
    expect(comparisonTable.rows[0].statusBadgeVariant).toBe("warning");
    expect(comparisonTable.rows[0].netPnlText).toBe("+$3,200");
    expect(comparisonTable.rows[0].netPnlTone).toBe("success");
    expect(comparisonTable.rows[0].totalReturnText).toBe("+4.20%");
    expect(comparisonTable.rows[0].totalReturnTone).toBe("success");
    expect(comparisonTable.rows[0].maxDrawdownText).toBe("-1.80%");
    expect(comparisonTable.rows[0].maxDrawdownTone).toBe("danger");
    expect(comparisonTable.rows[0].equityText).toBe("Equity Unavailable");
    expect(comparisonTable.rows[0].promotionStateText).toBe("Candidate for paper");
    expect(comparisonTable.rows[0].evidenceText).toBe("Ledger missing; Audit missing");
    expect(comparisonTable.rows[0].ariaLabel).toContain("Mean Reversion FX: Running; net P&L +$3,200");
    expect(comparisonTable.rows[0].sharpeRatioText).toBe("Unavailable");
    expect(comparisonTable.rows[0].fillCountText).toBe("Unavailable");
    expect(buildComparisonTable([]).emptyText).toBe("No comparison rows returned for the selected pair.");

    const emptyDiff = buildDiffPanel(diff);
    expect(emptyDiff.summaryLabel).toBe("Run diff metric summary");
    expect(emptyDiff.metrics[0]).toMatchObject({ label: "Net P&L delta", value: "+$1,200", tone: "success" });
    expect(emptyDiff.metrics[1]).toMatchObject({ label: "Return delta", value: "+1.00%", tone: "success" });
    expect(emptyDiff.metrics[2]).toMatchObject({ label: "Fill delta", value: "+5", tone: "success" });
    expect(emptyDiff.hasPositionChanges).toBe(false);
    expect(emptyDiff.hasParameterChanges).toBe(false);
    expect(emptyDiff.positionSectionLabel).toBe("0 position changes returned");
    expect(emptyDiff.parameterSectionLabel).toBe("0 parameter changes returned");
    expect(emptyDiff.positionEmptyText).toBe("No position changes returned for this diff.");
    expect(emptyDiff.parameterEmptyText).toBe("No parameter changes returned for this diff.");

    const historyTable = buildPromotionHistoryTable(history);
    expect(historyTable.rows[0].routeText).toBe("backtest to paper");
    expect(historyTable.rows[0].qualifyingSharpeText).toBe("1.820");
    expect(buildPromotionHistoryTable([]).emptyText).toBe("No promotion history records returned.");
  });

  it("tracks empty command result panels after successful commands return no rows", () => {
    const compared = buildResearchRunLibraryState({
      runs,
      selectedIds: ["run-1", "run-2"],
      selectedRun: null,
      comparison: [],
      runDiff: null,
      promotionHistory: [],
      comparisonLoaded: true,
      runDiffLoaded: false,
      promotionHistoryLoaded: true,
      activeCommand: null,
      actionError: null
    });

    expect(compared.showComparisonPanel).toBe(true);
    expect(compared.comparisonTable.hasRows).toBe(false);
    expect(compared.showPromotionHistoryPanel).toBe(true);
    expect(compared.promotionHistoryTable.emptyText).toBe("No promotion history records returned.");
    expect(compared.statusAnnouncement).toBe("No comparison rows returned for the selected pair.");
  });

  it("derives modal detail copy with unavailable fallback", () => {
    const detail = buildRunDetail({ ...runs[0], notes: "  " });

    expect(detail.dialogTitleId).toBe("strategy-run-detail-run-1-title");
    expect(detail.dialogDescriptionId).toBe("strategy-run-detail-run-1-description");
    expect(detail.description).toBe("Mean Reversion FX is Running in PAPER mode.");
    expect(detail.modeBadgeLabel).toBe("PAPER");
    expect(detail.modeBadgeVariant).toBe("paper");
    expect(detail.summaryRows).toContainEqual({ id: "run-id", label: "Run ID", value: "run-1" });
    expect(detail.notesText).toBe("No operator notes were recorded for this run.");
    expect(detail.closeButtonAriaLabel).toBe("Close Mean Reversion FX run detail");
  });

  it("derives accessible diff rows for position and parameter changes", () => {
    const panel = buildDiffPanel({
      ...diff,
      addedPositions: [
        { symbol: "AAPL", baseQuantity: 0, targetQuantity: 100, basePnl: 0, targetPnl: 250, changeType: "Added" }
      ],
      parameterChanges: [{ key: "lookback", baseValue: "20", targetValue: "30" }]
    });

    expect(panel.ariaLabel).toBe("Strategy run diff for Mean Reversion FX and Index Momentum");
    expect(panel.hasPositionChanges).toBe(true);
    expect(panel.positionChanges[0]).toMatchObject({
      symbolText: "AAPL",
      changeTypeText: "Added",
      quantityText: "Qty +100",
      pnlText: "P&L +$250",
      badgeVariant: "success",
      ariaLabel: "AAPL Added. Qty +100. P&L +$250."
    });
    expect(panel.hasParameterChanges).toBe(true);
    expect(panel.parameterChanges[0]).toMatchObject({
      key: "lookback",
      valueText: "20 -> 30",
      ariaLabel: "lookback changed from 20 to 30."
    });
  });

  it("builds plottool workstation and statistics state from strategy runs", () => {
    const plotTool = buildPlotToolState({
      metrics,
      runs,
      selectedRuns: [runs[0], runs[1]],
      comparison,
      runDiff: diff
    });

    expect(plotTool.workspace.title).toBe("Mean Reversion FX vs Index Momentum workstation");
    expect(plotTool.workspace.statusBadgeLabel).toBe("PAPER");
    expect(plotTool.workspace.expression).toContain("mean_reversion_fx.spread()");
    expect(plotTool.workspace.studySummary[0]).toMatchObject({ label: "Primary notebook", value: "Mean Reversion FX" });
    expect(plotTool.workspace.legendItems[1]).toMatchObject({ label: "Current", detail: "88.40 / 73.80", tone: "current" });
    expect(plotTool.workspace.focusPoint).toMatchObject({ label: "Current marker", xValueText: "88.40", yValueText: "73.80" });
    expect(plotTool.workspace.signalCards[2]).toMatchObject({
      label: "Queued studies",
      value: "3",
      tone: "warning"
    });
    expect(plotTool.workspace.overlayItems[1]).toContain("Ledger missing; Audit missing");
    expect(plotTool.statistics.title).toBe("Mean Reversion FX vs Index Momentum analysis");
    expect(plotTool.statistics.summaryTiles).toHaveLength(9);
    expect(plotTool.statistics.distributionSummary).toContain("2,211 samples");
    expect(plotTool.statistics.distributionFootnote).toContain("Latest observation 2026-04-25");
    expect(plotTool.statistics.summaryTiles[7]).toMatchObject({ label: "Sharpe (5d)", value: "1.41", tone: "success" });
    expect(plotTool.statistics.regression.detailItems[2]).toContain("position changes linked");
    expect(plotTool.statistics.sampleRows[0]).toMatchObject({ signalText: "Crowded vol", tone: "warning" });
  });

  it("derives PlotTool tab selection and keyboard transitions outside the view", () => {
    const tabs = buildPlotToolTabs("statistics");

    expect(tabs).toEqual([
      expect.objectContaining({
        id: "workspace",
        panelId: "plottool-workspace-panel",
        selected: false,
        buttonVariant: "ghost",
        tabIndex: -1,
        ariaLabel: "Workstation"
      }),
      expect.objectContaining({
        id: "statistics",
        panelId: "plottool-statistics-panel",
        selected: true,
        buttonVariant: "secondary",
        tabIndex: 0,
        ariaLabel: "Statistics"
      })
    ]);
    expect(nextPlotToolViewForKey("workspace", "ArrowRight")).toBe("statistics");
    expect(nextPlotToolViewForKey("statistics", "ArrowLeft")).toBe("workspace");
    expect(nextPlotToolViewForKey("statistics", "Home")).toBe("workspace");
    expect(nextPlotToolViewForKey("workspace", "End")).toBe("statistics");
    expect(nextPlotToolViewForKey("workspace", "Enter")).toBeNull();
  });

  it("keeps run detail keyboard-close decisions testable outside the view", () => {
    expect(shouldCloseRunDetailForKey("Escape")).toBe(true);
    expect(shouldCloseRunDetailForKey("Esc")).toBe(true);
    expect(shouldCloseRunDetailForKey("Enter")).toBe(false);
  });

  it("derives paper-promotion cash validation and disabled command state", () => {
    expect(parsePromotionInitialCashInput("100000")).toBe(100000);
    expect(parsePromotionInitialCashInput("999")).toBeNull();
    expect(parsePromotionInitialCashInput("1000.50")).toBeNull();
    expect(parsePromotionInitialCashInput("")).toBeNull();

    const valid = buildPromotionCashForm({
      input: "100000",
      eligible: true,
      promoteState: "evaluated"
    });

    expect(valid.canSubmit).toBe(true);
    expect(valid.errorText).toBeNull();
    expect(valid.helpText).toBe("Minimum $1,000. Use whole-dollar paper capital.");
    expect(valid.describedBy).toBe("promote-initial-cash-help");

    const invalid = buildPromotionCashForm({
      input: "500",
      eligible: true,
      promoteState: "evaluated"
    });

    expect(invalid.canSubmit).toBe(false);
    expect(invalid.errorText).toBe("Enter at least $1,000 in whole dollars.");

    const creating = buildPromotionCashForm({
      input: "100000",
      eligible: true,
      promoteState: "creating"
    });

    expect(creating.canSubmit).toBe(false);
    expect(creating.submitLabel).toBe("Starting paper session...");
  });

  it("derives paper-promotion evaluation panel state outside the view", () => {
    const blocked = buildPromotionPanelState({
      promoteState: "evaluated",
      promotionEval: promotionEvaluation,
      promotionSession: null
    });

    expect(blocked.statusRole).toBe("alert");
    expect(blocked.statusLive).toBe("assertive");
    expect(blocked.evaluation).toMatchObject({
      title: "Not eligible",
      titleTone: "danger",
      reason: "Risk review failed.",
      hasBlockingReasons: true,
      blockingListLabel: "Not eligible blocking reasons"
    });
    expect(blocked.evaluation?.metricRows).toEqual([
      { id: "sharpe", label: "Sharpe", value: "0.91" },
      { id: "drawdown", label: "Max DD", value: "-12.4%" },
      { id: "return", label: "Return", value: "1.9%" }
    ]);
    expect(blocked.evaluation?.blockingReasons[0]).toEqual({
      id: "run-2-blocker-0",
      text: "Sharpe ratio below promotion threshold."
    });
    expect(blocked.showCashForm).toBe(false);
    expect(blocked.showIneligibleDismiss).toBe(true);

    const eligible = buildPromotionPanelState({
      promoteState: "evaluated",
      promotionEval: { ...promotionEvaluation, isEligible: true, reason: "", blockingReasons: [] },
      promotionSession: null
    });

    expect(eligible.statusRole).toBe("status");
    expect(eligible.evaluation?.title).toBe("Eligible for paper trading");
    expect(eligible.evaluation?.reason).toBe("Promotion evaluation returned no reason.");
    expect(eligible.showCashForm).toBe(true);
    expect(eligible.showIneligibleDismiss).toBe(false);

    const done = buildPromotionPanelState({
      promoteState: "done",
      promotionEval: eligible.evaluation ? { ...promotionEvaluation, isEligible: true } : null,
      promotionSession: {
        sessionId: "sess-123",
        strategyId: "strat-2",
        strategyName: "Index Momentum",
        initialCash: 100000,
        createdAt: "2026-05-09T00:00:00Z",
        closedAt: null,
        isActive: true
      }
    });

    expect(done.sessionCreated).toMatchObject({
      title: "Paper session created - session sess-123",
      detail: "Index Momentum is active with $100,000 paper capital.",
      actionAriaLabel: "Go to Trading cockpit for paper session sess-123"
    });
  });

  it("preserves canonical run-pair ordering across toggle churn for compare continuity", () => {
    let selected = toggleRunSelection([], "run-1");
    selected = toggleRunSelection(selected, "run-2");
    selected = toggleRunSelection(selected, "run-3");

    const state = buildResearchRunLibraryState({
      runs,
      selectedIds: selected,
      selectedRun: null,
      comparison: [],
      runDiff: null,
      promotionHistory: [],
      activeCommand: null,
      actionError: null
    });

    expect(selected).toEqual(["run-2", "run-3"]);
    expect(state.selectedRuns.map((run) => run.id)).toEqual(["run-2", "run-3"]);
    expect(state.canCompare).toBe(true);
    expect(state.canDiff).toBe(true);
    expect(state.selectionText).toBe("Index Momentum vs Carry Alpha");
  });

  it("keeps pair-selection continuity when compare returns empty rows", () => {
    const state = buildResearchRunLibraryState({
      runs,
      selectedIds: ["run-1", "run-2"],
      selectedRun: null,
      comparison: [],
      runDiff: null,
      promotionHistory: [],
      comparisonLoaded: true,
      runDiffLoaded: false,
      promotionHistoryLoaded: false,
      activeCommand: null,
      actionError: null
    });

    expect(state.showComparisonPanel).toBe(true);
    expect(state.selectedRuns.map((run) => run.id)).toEqual(["run-1", "run-2"]);
    expect(state.selectionText).toBe("Mean Reversion FX vs Index Momentum");
    expect(state.statusAnnouncement).toBe("No comparison rows returned for the selected pair.");
  });

  it("prefers API-backed PlotTool payload when provided", () => {
    const apiPlotTool: ResearchPlotToolPayload = {
      activeView: "workspace",
      tabs: [],
      studies: [],
      workspace: {
        eyebrow: "API",
        title: "API workspace",
        description: "From backend",
        statusBadgeLabel: "PAPER",
        statusBadgeVariant: "paper",
        expression: "api.expr()",
        toolbarPills: [],
        metaItems: [],
        xAxisLabel: "X",
        yAxisLabel: "Y",
        xTicks: [],
        yTicks: [],
        points: [],
        studySummary: [],
        legendItems: [],
        focusPoint: { label: "focus", xValueText: "1", yValueText: "2", detail: "api" },
        signalCards: [],
        consoleTitle: "console",
        consoleBody: "body",
        overlayTitle: "overlay",
        overlayItems: []
      },
      statistics: {
        eyebrow: "API",
        title: "API stats",
        description: "From backend",
        summaryTiles: [],
        distributionBars: [],
        distributionSummary: "summary",
        distributionFootnote: "footnote",
        moments: [],
        regression: { equation: "y=x", detailItems: [] },
        sampleRows: []
      }
    };

    const state = buildResearchRunLibraryState({
      runs,
      metrics,
      plotToolFromApi: apiPlotTool,
      selectedIds: [],
      selectedRun: null,
      comparison: [],
      runDiff: null,
      promotionHistory: [],
      activeCommand: null,
      actionError: null
    });

    expect(state.plotTool.workspace.title).toBe("API workspace");
    expect(state.plotTool.statistics.title).toBe("API stats");
  });
});
