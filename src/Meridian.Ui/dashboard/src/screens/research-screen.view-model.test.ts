import { describe, expect, it } from "vitest";
import {
  buildResearchRunLibraryState,
  toggleRunSelection
} from "@/screens/research-screen.view-model";
import type { PromotionRecord, ResearchRunRecord, RunComparisonRow, RunDiff } from "@/types";

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
});
