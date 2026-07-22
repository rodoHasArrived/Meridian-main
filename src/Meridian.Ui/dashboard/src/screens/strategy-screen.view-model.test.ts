import { act, renderHook, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import {
  buildComparisonTable,
  buildDiffPanel,
  buildStrategyLoadingState,
  buildPlotToolMomentsTable,
  buildPlotNotebookToolbarItems,
  buildPlotToolSampleTable,
  buildPlotStudyDetail,
  buildPlotStudyRows,
  buildPlotToolState,
  buildPlotToolTabs,
  buildPromotionCashForm,
  buildPromotionHistoryDetail,
  buildPromotionPanelState,
  buildPromotionHistoryTable,
  buildStrategyCommandStates,
  buildStrategyRunLibraryState,
  buildRunHistorySummary,
  buildRunDetail,
  buildRunTable,
  nextPlotToolViewForKey,
  plotToolTabIdForView,
  parsePromotionInitialCashInput,
  shouldCloseRunDetailForKey,
  toggleRunSelection,
  useStrategyRunLibraryViewModel,
  type StrategyRunLibraryServices
} from "@/screens/strategy-screen.view-model";
import type { MetricSnapshot, PaperSessionSummary, PromotionEvaluationResult, PromotionRecord, StrategyPlotToolPayload, StrategyRunRecord, RunDiff, RunComparisonRow } from "@/types";

const runs: StrategyRunRecord[] = [
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
    hasAuditTrail: false,
    compatibilityWarnings: ["Cross-engine comparison active (MeridianNative, Lean)."],
    artifactCompleteness: {
      hasPortfolio: true,
      hasLedger: false,
      hasCashFlow: true,
      hasFills: true,
      hasAuditTrail: false
    }
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
    targetTotalReturn: 0.052,
    finalEquityDelta: 2500,
    maxDrawdownDelta: -750,
    sharpeRatioDelta: 0.25,
    baseFinalEquity: 100000,
    targetFinalEquity: 102500,
    baseMaxDrawdown: 1800,
    targetMaxDrawdown: 1050,
    baseSharpeRatio: 1.41,
    targetSharpeRatio: 1.66
  },
  compatibilityWarnings: ["Fill-level evidence is incomplete for at least one run."],
  baseArtifactCompleteness: {
    hasPortfolio: true,
    hasLedger: true,
    hasCashFlow: true,
    hasFills: true,
    hasAuditTrail: true
  },
  targetArtifactCompleteness: {
    hasPortfolio: true,
    hasLedger: false,
    hasCashFlow: true,
    hasFills: false,
    hasAuditTrail: true
  },
  baseMode: "Backtest",
  targetMode: "Paper",
  baseEngine: "MeridianNative",
  targetEngine: "BrokerPaper",
  baseStrategyId: "strategy-alpha",
  targetStrategyId: "strategy-alpha",
  baseStrategyVersion: "v1.0.0",
  targetStrategyVersion: "v1.1.0",
  lineageRelation: "DirectChild",
  compatibilityLevel: "CrossEngine"
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

describe("strategy-screen view model", () => {
  it("derives the Strategy loading state outside the view", () => {
    expect(buildStrategyLoadingState()).toEqual({
      role: "status",
      ariaBusy: true,
      ariaLive: "polite",
      titleId: "strategy-loading-title",
      detailId: "strategy-loading-detail",
      title: "Loading Strategy",
      detail: "Waiting for run history, PlotTool state, and promotion evidence.",
      badgeLabel: "Loading",
      routeLabel: "Strategy"
    });
  });

  it("keeps run selection capped to the latest two ids and supports deselection", () => {
    expect(toggleRunSelection([], "run-1")).toEqual(["run-1"]);
    expect(toggleRunSelection(["run-1"], "run-2")).toEqual(["run-1", "run-2"]);
    expect(toggleRunSelection(["run-1", "run-2"], "run-3")).toEqual(["run-2", "run-3"]);
    expect(toggleRunSelection(["run-1", "run-2"], "run-1")).toEqual(["run-2"]);
  });

  it("derives pair readiness and operator copy from selected runs", () => {
    const state = buildStrategyRunLibraryState({
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
    expect(state.compareCommand).toMatchObject({
      label: "Compare 2 runs",
      ariaLabel: "Compare 2 runs: Mean Reversion FX and Index Momentum",
      disabled: false,
      disabledReason: null,
      busy: false
    });
    expect(state.diffCommand).toMatchObject({
      label: "Diff 2 runs",
      ariaLabel: "Diff 2 runs: Mean Reversion FX and Index Momentum",
      disabled: false,
      disabledReason: null,
      busy: false
    });
    expect(state.selectionText).toBe("Mean Reversion FX vs Index Momentum");
    expect(state.selectionDetail).toBe("Ready to compare or diff the selected run pair.");
    expect(state.evidenceAction).toEqual({
      label: "Evidence packet",
      href: "/reporting/evidence?subjectKind=strategy-run&subjectId=run-1",
      ariaLabel: "Open Mean Reversion FX evidence packet"
    });
    expect(state.runTable.rows[0].selectAriaLabel).toBe("Remove Mean Reversion FX for compare and diff");
    expect(state.inspectedRunId).toBe("run-1");
    expect(state.inspectedRunDetail).toMatchObject({
      id: "run-1",
      panelId: "strategy-run-library-selected-run-detail",
      ariaLabel: "Selected strategy run detail for Mean Reversion FX",
      title: "Mean Reversion FX"
    });
    expect(state.inspectedRunDetail?.fields).not.toContainEqual(expect.objectContaining({ label: "Run ID" }));
    expect(state.inspectedRunDetail?.technicalFields).toContainEqual({ id: "run-id", label: "Run ID", value: "run-1" });
    expect(state.runTable.rows[0]).toMatchObject({
      selectedForComparison: true,
      detailExpanded: true,
      detailPanelId: "strategy-run-library-selected-run-detail",
      rowSelectAriaLabel: "Inspect Mean Reversion FX run detail"
    });
  });

  it("derives default and requested run detail state for the selected-row panel", () => {
    const defaultState = buildStrategyRunLibraryState({
      runs,
      selectedIds: [],
      selectedRun: null,
      comparison: [],
      runDiff: null,
      promotionHistory: [],
      activeCommand: null,
      actionError: null
    });

    expect(defaultState.inspectedRunId).toBe("run-1");
    expect(defaultState.inspectedRunDetail?.title).toBe("Mean Reversion FX");
    expect(defaultState.runTable.rows[0].detailExpanded).toBe(true);
    expect(defaultState.runTable.rows[1].detailExpanded).toBe(false);

    const requestedState = buildStrategyRunLibraryState({
      runs,
      selectedIds: [],
      inspectedRunId: "run-2",
      selectedRun: null,
      comparison: [],
      runDiff: null,
      promotionHistory: [],
      activeCommand: null,
      actionError: null
    });

    expect(requestedState.inspectedRunId).toBe("run-2");
    expect(requestedState.inspectedRunDetail?.title).toBe("Index Momentum");
    expect(requestedState.runTable.rows[1]).toMatchObject({
      detailExpanded: true,
      rowAriaLabel: "Index Momentum: Completed, BACKTEST, P&L +1.9%, Sharpe 0.91."
    });
  });

  it("derives busy labels, errors, and result announcements", () => {
    const busy = buildStrategyRunLibraryState({
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
    expect(busy.compareCommand).toMatchObject({
      label: "Comparing...",
      disabled: true,
      disabledReason: "Wait for the current Strategy command to finish.",
      busy: true
    });
    expect(busy.diffCommand.disabledReason).toBe("Wait for the current Strategy command to finish.");
    expect(busy.statusAnnouncement).toBe("Comparing selected strategy runs.");

    const failed = buildStrategyRunLibraryState({
      runs,
      selectedIds: ["run-1", "run-2"],
      selectedRun: null,
      comparison: [],
      runDiff: null,
      promotionHistory: [],
      activeCommand: null,
      actionError: "Run comparison failed."
    });

    expect(failed.statusAnnouncement).toBe("Strategy command failed: Run comparison failed.");

    const diffBusy = buildStrategyRunLibraryState({
      runs,
      selectedIds: ["run-1", "run-2"],
      selectedRun: null,
      comparison: [],
      runDiff: null,
      promotionHistory: [],
      activeCommand: "diff",
      actionError: null
    });

    expect(diffBusy.statusAnnouncement).toBe("Diffing selected strategy runs.");

    const compared = buildStrategyRunLibraryState({
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

    const diffed = buildStrategyRunLibraryState({
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
    expect(comparisonTable.rows[0].artifactCompletenessText).toBe("Ready 3/5 (portfolio, cash-flow, fills); missing ledger, audit");
    expect(comparisonTable.rows[0].warningText).toBe("Cross-engine comparison active (MeridianNative, Lean).");
    expect(comparisonTable.rows[0].detailExpanded).toBe(true);
    expect(comparisonTable.rows[0].detailPanelId).toBe("strategy-run-comparison-selected-detail");
    expect(comparisonTable.rows[0].rowSelectAriaLabel).toBe("Inspect Mean Reversion FX comparison evidence");
    expect(comparisonTable.rows[0].ariaLabel).toContain("Mean Reversion FX: Running; net P&L +$3,200");
    expect(comparisonTable.rows[0].sharpeRatioText).toBe("Unavailable");
    expect(comparisonTable.rows[0].fillCountText).toBe("Unavailable");
    expect(buildComparisonTable([]).emptyText).toBe("No comparison rows returned for the selected pair.");

    const comparisonDetailState = buildStrategyRunLibraryState({
      runs,
      selectedIds: ["run-1", "run-2"],
      selectedRun: null,
      comparison: [
        comparison[0],
        { ...comparison[0], runId: "run-2", strategyName: "Index Momentum" }
      ],
      selectedComparisonRowId: "run-2",
      runDiff: null,
      promotionHistory: [],
      activeCommand: null,
      actionError: null
    });
    expect(comparisonDetailState.selectedComparisonRowId).toBe("run-2");
    expect(comparisonDetailState.selectedComparisonDetail).toMatchObject({
      panelId: "strategy-run-comparison-selected-detail",
      ariaLabel: "Selected comparison evidence for Index Momentum",
      title: "Index Momentum"
    });

    const emptyDiff = buildDiffPanel(diff);
    expect(emptyDiff.summaryLabel).toBe("Run diff metric summary");
    expect(emptyDiff.metrics[0]).toMatchObject({ label: "Net P&L delta", value: "+$1,200", tone: "success" });
    expect(emptyDiff.metrics[1]).toMatchObject({ label: "Return delta", value: "+1.00%", tone: "success" });
    expect(emptyDiff.metrics[2]).toMatchObject({ label: "Fill delta", value: "+5", tone: "success" });
    expect(emptyDiff.metrics[3]).toMatchObject({ label: "Final equity delta", value: "+$2,500", tone: "success" });
    expect(emptyDiff.metrics[4]).toMatchObject({ label: "Drawdown delta", value: "-$750", tone: "success" });
    expect(emptyDiff.metrics[5]).toMatchObject({ label: "Sharpe delta", value: "+0.250", tone: "success" });
    expect(emptyDiff.compatibilityWarnings).toEqual(["Fill-level evidence is incomplete for at least one run."]);
    expect(emptyDiff.metadataSummary).toBe(
      "Compatibility CrossEngine | Lineage DirectChild | Base strategy-alpha @ v1.0.0 | Target strategy-alpha @ v1.1.0"
    );
    expect(emptyDiff.compatibilityLevel).toBe("CrossEngine");
    expect(emptyDiff.lineageRelation).toBe("DirectChild");
    expect(emptyDiff.artifactCompletenessSummary).toBe(
      "Base Backtest / MeridianNative: Ready 5/5 (portfolio, ledger, cash-flow, fills, audit); missing none | Target Paper / BrokerPaper: Ready 3/5 (portfolio, cash-flow, audit); missing ledger, fills"
    );
    expect(emptyDiff.hasPositionChanges).toBe(false);
    expect(emptyDiff.hasParameterChanges).toBe(false);
    expect(emptyDiff.positionSectionLabel).toBe("0 position changes returned");
    expect(emptyDiff.parameterSectionLabel).toBe("0 parameter changes returned");
    expect(emptyDiff.positionTable.emptyText).toBe("No position changes returned for this diff.");
    expect(emptyDiff.parameterTable.emptyText).toBe("No parameter changes returned for this diff.");
    expect(emptyDiff.positionEmptyText).toBe("No position changes returned for this diff.");
    expect(emptyDiff.parameterEmptyText).toBe("No parameter changes returned for this diff.");

    const historyTable = buildPromotionHistoryTable(history);
    expect(historyTable.caption).toBe("Promotion history decisions returned for Strategy runs. Select a row to inspect gate evidence.");
    expect(historyTable.rows[0].routeText).toBe("backtest to paper");
    expect(historyTable.rows[0].qualifyingSharpeText).toBe("1.820");
    expect(historyTable.rows[0]).toMatchObject({
      detailPanelId: "strategy-promotion-history-selected-detail",
      detailExpanded: true,
      rowSelectAriaLabel: "Inspect Carry Pair FX promotion decision",
      decisionText: "Approved for paper",
      qualifyingMaxDrawdownText: "-3.2%",
      qualifyingTotalReturnText: "6.5%"
    });
    expect(historyTable.rows[0].ariaLabel).toContain("Carry Pair FX: backtest to paper; decision Approved for paper");
    expect(buildPromotionHistoryTable([]).emptyText).toBe("No promotion history records returned.");

    const selectedSecond = buildPromotionHistoryTable([
      history[0],
      { ...history[0], promotionId: "promo-2", strategyName: "Index Momentum", targetRunType: "live" }
    ], "promo-2");
    expect(selectedSecond.rows[0].detailExpanded).toBe(false);
    expect(selectedSecond.rows[1].detailExpanded).toBe(true);

    const detail = buildPromotionHistoryDetail({
      ...history[0],
      sourceRunId: "run-backtest-1",
      targetRunId: "run-paper-1",
      approvedBy: "risk-ops",
      auditReference: "audit-42",
      approvalReason: "Gate metrics passed."
    });
    expect(detail).toMatchObject({
      id: "promo-1",
      panelId: "strategy-promotion-history-selected-detail",
      ariaLabel: "Selected promotion decision detail for Carry Pair FX",
      title: "Carry Pair FX",
      statusLabel: "PAPER",
      statusVariant: "paper"
    });
    expect(detail.description).toBe("Approved for paper promotion decision with Gate metrics passed.");
    expect(detail.fields).toContainEqual({ label: "Source run", value: "run-backtest-1" });
    expect(detail.fields).toContainEqual({ label: "Target run", value: "run-paper-1" });
  });

  it("tracks empty command result panels after successful commands return no rows", () => {
    const compared = buildStrategyRunLibraryState({
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
    expect(compared.selectedPromotionHistoryId).toBe(null);
    expect(compared.selectedPromotionHistoryDetail).toBe(null);
    expect(compared.statusAnnouncement).toBe("No comparison rows returned for the selected pair.");
  });

  it("derives selected promotion-history detail state outside the view", () => {
    const state = buildStrategyRunLibraryState({
      runs,
      selectedIds: [],
      selectedRun: null,
      comparison: [],
      runDiff: null,
      promotionHistory: [
        history[0],
        { ...history[0], promotionId: "promo-2", strategyName: "Index Momentum", targetRunType: "live" }
      ],
      selectedPromotionHistoryId: "promo-2",
      promotionHistoryLoaded: true,
      activeCommand: null,
      actionError: null
    });

    expect(state.selectedPromotionHistoryId).toBe("promo-2");
    expect(state.selectedPromotionHistoryDetailPanelId).toBe("strategy-promotion-history-selected-detail");
    expect(state.selectedPromotionHistoryDetail).toMatchObject({
      id: "promo-2",
      ariaLabel: "Selected promotion decision detail for Index Momentum",
      statusLabel: "LIVE",
      statusVariant: "live"
    });
    expect(state.promotionHistoryTable.rows[1]).toMatchObject({
      detailExpanded: true,
      rowSelectAriaLabel: "Inspect Index Momentum promotion decision"
    });
  });

  it("derives modal detail copy with unavailable fallback", () => {
    const detail = buildRunDetail({ ...runs[0], notes: "  " });

    expect(detail.dialogTitleId).toBe("strategy-run-detail-run-1-title");
    expect(detail.dialogDescriptionId).toBe("strategy-run-detail-run-1-description");
    expect(detail.description).toBe("Mean Reversion FX is Running in PAPER mode.");
    expect(detail.modeBadgeLabel).toBe("PAPER");
    expect(detail.modeBadgeVariant).toBe("paper");
    expect(detail.summaryRows).not.toContainEqual(expect.objectContaining({ label: "Run ID" }));
    expect(detail.technicalRows).toContainEqual({ id: "run-id", label: "Run ID", value: "run-1" });
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
      baseQuantityText: "0",
      targetQuantityText: "100",
      quantityText: "Qty +100",
      basePnlText: "$0",
      targetPnlText: "$250",
      pnlText: "P&L +$250",
      badgeVariant: "success",
      ariaLabel: "AAPL Added. Qty +100. P&L +$250.",
      rowSelectAriaLabel: "Inspect AAPL added position diff",
      detailPanelId: "strategy-run-diff-selected-position-detail",
      detailExpanded: true
    });
    expect(panel.positionTable.caption).toContain("Select a row to inspect base and target exposure.");
    expect(panel.selectedPositionDetail).toMatchObject({
      ariaLabel: "Selected position diff detail for AAPL",
      title: "AAPL",
      statusLabel: "Added"
    });
    expect(panel.hasParameterChanges).toBe(true);
    expect(panel.parameterChanges[0]).toMatchObject({
      key: "lookback",
      valueText: "20 -> 30",
      ariaLabel: "lookback changed from 20 to 30.",
      rowSelectAriaLabel: "Inspect lookback parameter diff",
      detailPanelId: "strategy-run-diff-selected-parameter-detail",
      detailExpanded: true
    });
    expect(panel.parameterTable.caption).toContain("Select a row to inspect base and target values.");
    expect(panel.selectedParameterDetail).toMatchObject({
      ariaLabel: "Selected parameter diff detail for lookback",
      title: "lookback",
      statusLabel: "Changed"
    });

    const selectedSecond = buildDiffPanel({
      ...diff,
      addedPositions: [
        { symbol: "AAPL", baseQuantity: 0, targetQuantity: 100, basePnl: 0, targetPnl: 250, changeType: "Added" },
        { symbol: "MSFT", baseQuantity: 25, targetQuantity: 40, basePnl: 120, targetPnl: 180, changeType: "Modified" }
      ],
      parameterChanges: [
        { key: "lookback", baseValue: "20", targetValue: "30" },
        { key: "threshold", baseValue: "1.5", targetValue: "2.0" }
      ]
    }, {
      selectedPositionKey: "MSFT-Modified",
      selectedParameterKey: "threshold"
    });
    expect(selectedSecond.selectedPositionKey).toBe("MSFT-Modified");
    expect(selectedSecond.positionChanges[1]).toMatchObject({
      detailExpanded: true,
      quantityText: "Qty +15",
      pnlText: "P&L +$60"
    });
    expect(selectedSecond.selectedParameterKey).toBe("threshold");
    expect(selectedSecond.parameterChanges[1]).toMatchObject({
      detailExpanded: true,
      valueText: "1.5 -> 2.0"
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
    expect(plotTool.workspace.notebookToolbarItems).toEqual([
      { id: "count", label: "Notebook set", value: "3 retained" },
      { id: "selected", label: "Selected", value: "Mean Reversion FX", active: true },
      { id: "lane", label: "Lane", value: "Strategy" }
    ]);
    expect(plotTool.studies[0]).toMatchObject({
      id: "run-1",
      detailPanelId: "plottool-selected-study-detail",
      rowSelectAriaLabel: "Inspect Mean Reversion FX PlotTool study detail"
    });
    expect(plotTool.workspace.legendItems[1]).toMatchObject({ label: "Current", detail: "88.40 / 73.80", tone: "current" });
    expect(plotTool.workspace.focusPoint).toMatchObject({ label: "Current marker", xValueText: "88.40", yValueText: "73.80" });
    expect(plotTool.workspace.scatterChart).toMatchObject({
      ariaLabel: "Mean Reversion FX vs Index Momentum PlotTool scatter chart. Current marker 88.40, 73.80.",
      xAxisLabel: "Spread (bps)",
      yAxisLabel: "3m implied vol"
    });
    expect(plotTool.workspace.scatterChart.gridLines[0]).toMatchObject({ stroke: "var(--chart-grid)" });
    expect(plotTool.workspace.scatterChart.trendLine).toMatchObject({ stroke: "var(--chart-up)", strokeDasharray: "5 4" });
    expect(plotTool.workspace.scatterChart.marker).toMatchObject({
      fill: "var(--state-warn-fg)",
      labelText: "88.40, 73.80"
    });
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
    expect(plotTool.statistics.distributionChart.bars[0]).toMatchObject({
      id: "distribution-bar-0",
      heightPercent: 12,
      tone: "base"
    });
    expect(plotTool.statistics.distributionChart.bars[8]).toMatchObject({
      heightPercent: 94,
      tone: "selected"
    });
    expect(plotTool.statistics.summaryTiles[7]).toMatchObject({ label: "Sharpe (5d)", value: "1.41", tone: "success" });
    expect(plotTool.statistics.regression.detailItems[2]).toContain("position changes linked");
    expect(plotTool.statistics.sampleRows[0]).toMatchObject({ signalText: "Crowded vol", tone: "warning" });
    expect(plotTool.statistics.momentsTable).toMatchObject({
      hasRows: true,
      caption: "PlotTool moments for the active strategy pair.",
      emptyText: "No PlotTool moments are available for the active strategy context."
    });
    expect(plotTool.statistics.momentsTable.rows[0]).toMatchObject({ label: "Net P&L", benchmark: "Pair summary" });
    expect(plotTool.statistics.sampleTable).toMatchObject({
      hasRows: true,
      caption: "Recent PlotTool observations with spread, implied volatility, z-score, and signal.",
      emptyText: "No PlotTool observation rows are available for the active strategy context."
    });
    expect(plotTool.statistics.sampleTable.rows[0]).toMatchObject({ signalText: "Crowded vol", tone: "warning" });
  });

  it("uses Strategy as the PlotTool badge when no run is active", () => {
    const plotTool = buildPlotToolState({
      metrics,
      runs: [],
      selectedRuns: [],
      comparison: [],
      runDiff: null
    });

    expect(plotTool.workspace.statusBadgeLabel).toBe("STRATEGY");
    expect(plotTool.workspace.statusBadgeLabel).not.toBe("RESEARCH");
  });

  it("builds PlotTool table states with explicit empty copy", () => {
    expect(buildPlotToolMomentsTable([])).toEqual({
      rows: [],
      hasRows: false,
      caption: "PlotTool moments for the active strategy pair.",
      emptyText: "No PlotTool moments are available for the active strategy context."
    });
    expect(buildPlotToolSampleTable([])).toEqual({
      rows: [],
      hasRows: false,
      caption: "Recent PlotTool observations with spread, implied volatility, z-score, and signal.",
      emptyText: "No PlotTool observation rows are available for the active strategy context."
    });
  });

  it("derives PlotTool study row detail state outside the view", () => {
    const plotTool = buildPlotToolState({
      metrics,
      runs,
      selectedRuns: [runs[0], runs[1]],
      comparison,
      runDiff: diff
    });
    const rows = buildPlotStudyRows(plotTool.studies, "run-2");
    const detail = buildPlotStudyDetail(rows[1]);

    expect(rows[0]).toMatchObject({
      detailExpanded: false,
      detailPanelId: "plottool-selected-study-detail",
      rowSelectAriaLabel: "Inspect Mean Reversion FX PlotTool study detail"
    });
    expect(rows[1]).toMatchObject({
      detailExpanded: true,
      ariaLabel: "Index Momentum PlotTool study. Completed. +1.9% · Sharpe 0.91."
    });
    expect(detail).toMatchObject({
      id: "run-2",
      panelId: "plottool-selected-study-detail",
      ariaLabel: "Selected PlotTool study detail for Index Momentum",
      title: "Index Momentum",
      statusLabel: "BACKTEST",
      statusVariant: "research"
    });
    expect(detail.fields).toContainEqual({ label: "Notebook", value: "Retained notebook" });
    expect(buildPlotNotebookToolbarItems(rows, "run-2")).toEqual([
      { id: "count", label: "Notebook set", value: "3 retained" },
      { id: "selected", label: "Selected", value: "Index Momentum", active: true },
      { id: "lane", label: "Lane", value: "Strategy" }
    ]);

    const state = buildStrategyRunLibraryState({
      runs,
      plotToolFromApi: {
        ...plotTool,
        tabs: []
      },
      selectedIds: [],
      selectedRun: null,
      comparison: [],
      runDiff: null,
      promotionHistory: [],
      selectedPlotStudyId: "run-2",
      activeCommand: null,
      actionError: null
    });

    expect(state.selectedPlotStudyId).toBe("run-2");
    expect(state.selectedPlotStudyDetail?.title).toBe("Index Momentum");
    expect(state.plotTool.studies[1].detailExpanded).toBe(true);
    expect(state.plotTool.workspace.notebookToolbarItems[1]).toMatchObject({
      label: "Selected",
      value: "Index Momentum",
      active: true
    });
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
    expect(plotToolTabIdForView("workspace")).toBe("plottool-workspace-tab");
    expect(plotToolTabIdForView("statistics")).toBe("plottool-statistics-tab");
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
      promoteState: "evaluated",
      acknowledged: true
    });

    expect(valid.canSubmit).toBe(true);
    expect(valid.disabledReason).toBeNull();
    expect(valid.errorText).toBeNull();
    expect(valid.helpText).toBe("Minimum $1,000. Use whole-dollar paper capital.");
    expect(valid.inputHelpId).toBe("promote-initial-cash-help");
    expect(valid.inputDescribedBy).toBe("promote-initial-cash-help");
    expect(valid.inputDisabledReasonId).toBeNull();
    expect(valid.describedBy).toBe("promote-initial-cash-help");
    expect(valid.acknowledgementChecked).toBe(true);
    expect(valid.acknowledgementLabel).toBe("I reviewed the promotion gates and paper-capital impact.");
    expect(valid.acknowledgementDescribedBy).toBeUndefined();

    const unacknowledged = buildPromotionCashForm({
      input: "100000",
      eligible: true,
      promoteState: "evaluated",
      acknowledged: false
    });

    expect(unacknowledged.canSubmit).toBe(false);
    expect(unacknowledged.disabledReason).toBe(
      "Acknowledge the evaluated gates and paper-capital impact before starting a paper session."
    );
    expect(unacknowledged.submitAriaLabel).toContain("unavailable");

    const invalid = buildPromotionCashForm({
      input: "500",
      eligible: true,
      promoteState: "evaluated",
      acknowledged: true
    });

    expect(invalid.canSubmit).toBe(false);
    expect(invalid.disabledReason).toBe("Enter at least $1,000 in whole-dollar paper capital.");
    expect(invalid.errorText).toBe("Enter at least $1,000 in whole dollars.");
    expect(invalid.helpText).toBe("Enter at least $1,000 in whole dollars.");

    const empty = buildPromotionCashForm({
      input: "",
      eligible: true,
      promoteState: "evaluated",
      acknowledged: true
    });

    expect(empty.canSubmit).toBe(false);
    expect(empty.disabledReason).toBe("Enter initial paper capital of at least $1,000.");
    expect(empty.helpText).toBe("Enter initial paper capital of at least $1,000.");

    const creating = buildPromotionCashForm({
      input: "100000",
      eligible: true,
      promoteState: "creating",
      acknowledged: true
    });

    expect(creating.canSubmit).toBe(false);
    expect(creating.disabledReason).toBe("Paper-session creation is already running.");
    expect(creating.helpText).toBe("Paper-session creation is already running.");
    expect(creating.submitLabel).toBe("Starting paper session...");
    expect(creating.inputDisabled).toBe(true);
    expect(creating.inputDisabledReason).toBe("Paper-session creation is already running; wait before changing capital.");
    expect(creating.inputDisabledReasonId).toBe("promote-initial-cash-disabled-reason");
    expect(creating.inputDescribedBy).toBe("promote-initial-cash-help promote-initial-cash-disabled-reason");
    expect(creating.acknowledgementDisabled).toBe(true);
    expect(creating.acknowledgementDisabledReason).toBe(
      "Paper-session creation is already running; wait before changing acknowledgement."
    );
    expect(creating.acknowledgementDisabledReasonId).toBe(
      "promote-paper-session-acknowledgement-disabled-reason"
    );
    expect(creating.acknowledgementDescribedBy).toBe(
      "promote-paper-session-acknowledgement-disabled-reason"
    );
    expect(creating.cancelDisabled).toBe(true);
    expect(creating.cancelDisabledReason).toBe(
      "Paper-session creation is already running; wait for the session result before closing setup."
    );
    expect(creating.cancelAriaLabel).toBe(
      "Paper-session creation is already running; wait for the session result before closing setup."
    );
  });

  it("derives disabled command reasons for incomplete selections", () => {
    const noSelection = buildStrategyCommandStates({
      selectedRuns: [],
      hasTwoRuns: false,
      hasOneBacktestRun: false,
      busy: false,
      activeCommand: null,
      promoteState: "idle",
      promoteBusy: false
    });

    expect(noSelection.compare).toMatchObject({
      label: "Compare 2 runs",
      ariaLabel: "Compare 2 runs unavailable",
      disabled: true,
      disabledReason: "Select exactly two runs before using this command. 0 selected."
    });
    expect(noSelection.diff.disabledReason).toBe("Select exactly two runs before using this command. 0 selected.");
    expect(noSelection.promote.disabledReason).toBe("Select one completed backtest run before promoting to paper. 0 selected.");

    const oneCompletedBacktest = buildStrategyCommandStates({
      selectedRuns: [runs[1]],
      hasTwoRuns: false,
      hasOneBacktestRun: true,
      busy: false,
      activeCommand: null,
      promoteState: "idle",
      promoteBusy: false
    });

    expect(oneCompletedBacktest.promote).toMatchObject({
      label: "Promote to Paper",
      ariaLabel: "Promote to Paper: evaluate Index Momentum for paper trading",
      disabled: false,
      disabledReason: null
    });
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

    const creating = buildPromotionPanelState({
      promoteState: "creating",
      promotionEval: { ...promotionEvaluation, isEligible: true, reason: "", blockingReasons: [] },
      promotionSession: null
    });

    expect(creating.showCashForm).toBe(true);
    expect(creating.sessionCreated).toBeNull();

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
      actionAriaLabel: "Go to Trading cockpit for paper session sess-123",
      actionHref: "/trading"
    });
  });

  it("preserves canonical run-pair ordering across toggle churn for compare continuity", () => {
    let selected = toggleRunSelection([], "run-1");
    selected = toggleRunSelection(selected, "run-2");
    selected = toggleRunSelection(selected, "run-3");

    const state = buildStrategyRunLibraryState({
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

  it("summarizes shared run history across backtest, paper, and engines", () => {
    const summary = buildRunHistorySummary(runs);

    expect(summary).toMatchObject({
      normalizedResultText: "3 retained runs using the shared strategy-run result model.",
      modeCoverageText: "Backtest 2; Paper 1; Live 0",
      liveAdjacentText: "1 paper/live-adjacent run",
      engineCoverageText: "2 normalized engines: Lean, Meridian Native"
    });
    expect(summary.cards).toEqual([
      expect.objectContaining({
        id: "total-runs",
        value: "3",
        detail: "1 completed",
        badgeLabel: "Common model"
      }),
      expect.objectContaining({
        id: "mode-coverage",
        value: "2/1/0",
        badgeLabel: "Live-adjacent",
        badgeVariant: "paper"
      }),
      expect.objectContaining({
        id: "engine-coverage",
        value: "2",
        badgeLabel: "Cross-engine",
        badgeVariant: "warning"
      }),
      expect.objectContaining({
        id: "paper-live",
        value: "1",
        badgeLabel: "Operational",
        badgeVariant: "success"
      })
    ]);
  });

  it("keeps pair-selection continuity when compare returns empty rows", () => {
    const state = buildStrategyRunLibraryState({
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
    const apiPlotTool: StrategyPlotToolPayload = {
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

    const state = buildStrategyRunLibraryState({
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

  it("uses an honest empty PlotTool state when no API payload is provided", () => {
    const state = buildStrategyRunLibraryState({
      runs,
      selectedIds: [],
      selectedRun: null,
      comparison: [],
      runDiff: null,
      promotionHistory: [],
      activeCommand: null,
      actionError: null
    });

    expect(state.plotTool.studies).toEqual([]);
    expect(state.selectedPlotStudyId).toBeNull();
    expect(state.selectedPlotStudyDetail).toBeNull();
    expect(state.plotTool.workspace.title).toBe("PlotTool catalog not connected");
    expect(state.plotTool.workspace.studyTableEmptyText).toBe("No retained PlotTool studies are available. Connect a governed PlotTool catalog before selecting notebooks.");
    expect(state.plotTool.workspace.points).toEqual([]);
    expect(state.plotTool.workspace.scatterChart.points).toEqual([]);
    expect(state.plotTool.statistics.title).toBe("PlotTool statistics not connected");
    expect(state.plotTool.statistics.sampleTable.rows).toEqual([]);
  });

  it("ignores duplicate paper-session submit attempts while creation is unresolved", async () => {
    const pendingSession = createDeferred<PaperSessionSummary>();
    const services: StrategyRunLibraryServices = {
      compareRuns: vi.fn(),
      diffRuns: vi.fn(),
      getPromotionHistory: vi.fn(),
      evaluatePromotion: vi.fn().mockResolvedValue({
        ...promotionEvaluation,
        isEligible: true,
        ready: true,
        reason: "Promotion gates passed.",
        blockingReasons: []
      }),
      createPaperSession: vi.fn().mockReturnValue(pendingSession.promise)
    };

    const { result } = renderHook(() =>
      useStrategyRunLibraryViewModel({ metrics, runs }, services)
    );

    act(() => {
      result.current.toggleRun("run-2");
    });

    await act(async () => {
      await result.current.promoteSelectedRun();
    });

    act(() => {
      result.current.setPromotionInitialCash("150000");
    });

    act(() => {
      result.current.setPromotionAcknowledgement(true);
    });

    act(() => {
      void result.current.confirmPromotion();
      void result.current.confirmPromotion();
    });

    await waitFor(() => {
      expect(services.createPaperSession).toHaveBeenCalledTimes(1);
    });
    expect(services.createPaperSession).toHaveBeenCalledWith("run-2", "Index Momentum", 150000);

    act(() => {
      result.current.setPromotionInitialCash("900000");
      result.current.setPromotionAcknowledgement(false);
      result.current.cancelPromotion();
    });

    expect(result.current.showPromotePanel).toBe(true);
    expect(result.current.promoteState).toBe("creating");
    expect(result.current.promotionCashForm.value).toBe("150000");
    expect(result.current.promotionCashForm.acknowledgementChecked).toBe(true);
    expect(result.current.promotionCashForm.submitLabel).toBe("Starting paper session...");
    expect(result.current.promotionCashForm.inputDisabled).toBe(true);
    expect(result.current.promotionCashForm.acknowledgementDisabled).toBe(true);
    expect(result.current.promotionCashForm.cancelDisabled).toBe(true);

    await act(async () => {
      pendingSession.resolve({
        sessionId: "session-dup-guard",
        strategyId: "run-2",
        strategyName: "Index Momentum",
        initialCash: 100000,
        createdAt: "2026-05-14T00:00:00Z",
        closedAt: null,
        isActive: true
      });
      await pendingSession.promise;
    });

    expect(result.current.promoteState).toBe("done");
    expect(result.current.promotionSession?.sessionId).toBe("session-dup-guard");
  });
});

function createDeferred<T>() {
  let resolve!: (value: T | PromiseLike<T>) => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((promiseResolve, promiseReject) => {
    resolve = promiseResolve;
    reject = promiseReject;
  });

  return { promise, resolve, reject };
}
