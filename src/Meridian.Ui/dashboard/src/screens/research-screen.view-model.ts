import { useCallback, useMemo, useState } from "react";
import * as workstationApi from "@/lib/api";
import type {
  PromotionRecord,
  ResearchRunRecord,
  ResearchWorkspaceResponse,
  RunComparisonRow,
  RunDiff
} from "@/types";

export type ResearchCommand = "compare" | "diff" | "history";

export interface ResearchRunLibraryServices {
  compareRuns: (runIds: string[]) => Promise<RunComparisonRow[]>;
  diffRuns: (baseRunId: string, targetRunId: string) => Promise<RunDiff>;
  getPromotionHistory: () => Promise<PromotionRecord[]>;
}

export interface ResearchRunLibraryState {
  runs: ResearchRunRecord[];
  runTable: ResearchResultTableState<ResearchRunTableRow>;
  selectedIds: string[];
  selectedRuns: ResearchRunRecord[];
  selectedRun: ResearchRunRecord | null;
  selectedRunDetail: ResearchRunDetailState | null;
  comparison: RunComparisonRow[];
  comparisonTable: ResearchResultTableState<ResearchComparisonTableRow>;
  runDiff: RunDiff | null;
  diffPanel: ResearchDiffPanelState;
  promotionHistory: PromotionRecord[];
  promotionHistoryTable: ResearchResultTableState<ResearchPromotionHistoryRow>;
  showComparisonPanel: boolean;
  showDiffPanel: boolean;
  showPromotionHistoryPanel: boolean;
  activeCommand: ResearchCommand | null;
  actionError: string | null;
  canCompare: boolean;
  canDiff: boolean;
  canLoadPromotionHistory: boolean;
  selectionText: string;
  selectionDetail: string;
  compareButtonLabel: string;
  diffButtonLabel: string;
  promotionHistoryButtonLabel: string;
  statusAnnouncement: string;
}

export interface ResearchResultTableState<T> {
  rows: T[];
  hasRows: boolean;
  caption: string;
  emptyText: string;
}

export interface ResearchRunTableRow {
  id: string;
  strategyName: string;
  engineText: string;
  mode: ResearchRunRecord["mode"];
  modeLabel: string;
  statusText: string;
  pnlText: string;
  sharpeText: string;
  lastUpdatedText: string;
  selectAriaLabel: string;
  openDetailLabel: string;
  raw: ResearchRunRecord;
}

export interface ResearchRunDetailState {
  dialogTitleId: string;
  dialogDescriptionId: string;
  description: string;
  eyebrow: string;
  title: string;
  subtitle: string;
  modeBadgeLabel: string;
  modeBadgeVariant: ResearchRunDetailBadgeVariant;
  summaryLabel: string;
  summaryRows: ResearchRunDetailSummaryRow[];
  notesLabel: string;
  notesText: string;
  closeButtonLabel: string;
  closeButtonAriaLabel: string;
}

export type ResearchRunDetailBadgeVariant = "research" | "paper" | "live";

export interface ResearchRunDetailSummaryRow {
  id: string;
  label: string;
  value: string;
}

export interface ResearchComparisonTableRow {
  runId: string;
  strategyName: string;
  modeText: string;
  statusText: string;
  sharpeRatioText: string;
  fillCountText: string;
}

export interface ResearchDiffChangeRow {
  key: string;
  text: string;
}

export interface ResearchParameterChangeRow {
  key: string;
  baseValueText: string;
  targetValueText: string;
}

export interface ResearchDiffPanelState {
  title: string;
  description: string;
  positionChanges: ResearchDiffChangeRow[];
  parameterChanges: ResearchParameterChangeRow[];
  positionEmptyText: string;
  parameterEmptyText: string;
}

export interface ResearchPromotionHistoryRow {
  promotionId: string;
  strategyName: string;
  qualifyingSharpeText: string;
  routeText: string;
  promotedAtText: string;
}

const defaultResearchServices: ResearchRunLibraryServices = {
  compareRuns: (runIds) => workstationApi.compareRuns(runIds),
  diffRuns: (baseRunId, targetRunId) => workstationApi.diffRuns(baseRunId, targetRunId),
  getPromotionHistory: () => workstationApi.getPromotionHistory()
};

export function useResearchRunLibraryViewModel(
  data: ResearchWorkspaceResponse | null,
  services: ResearchRunLibraryServices = defaultResearchServices
) {
  const [selectedRun, setSelectedRun] = useState<ResearchRunRecord | null>(null);
  const [selectedIds, setSelectedIds] = useState<string[]>([]);
  const [comparison, setComparison] = useState<RunComparisonRow[]>([]);
  const [runDiff, setRunDiff] = useState<RunDiff | null>(null);
  const [promotionHistory, setPromotionHistory] = useState<PromotionRecord[]>([]);
  const [comparisonLoaded, setComparisonLoaded] = useState(false);
  const [runDiffLoaded, setRunDiffLoaded] = useState(false);
  const [promotionHistoryLoaded, setPromotionHistoryLoaded] = useState(false);
  const [activeCommand, setActiveCommand] = useState<ResearchCommand | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);

  const runs = data?.runs ?? [];

  const state = useMemo(
    () => buildResearchRunLibraryState({
      runs,
      selectedIds,
      selectedRun,
      comparison,
      runDiff,
      promotionHistory,
      comparisonLoaded,
      runDiffLoaded,
      promotionHistoryLoaded,
      activeCommand,
      actionError
    }),
    [
      actionError,
      activeCommand,
      comparison,
      comparisonLoaded,
      promotionHistory,
      promotionHistoryLoaded,
      runDiff,
      runDiffLoaded,
      runs,
      selectedIds,
      selectedRun
    ]
  );

  const toggleRun = useCallback((runId: string) => {
    setSelectedIds((current) => toggleRunSelection(current, runId));
    setActionError(null);
  }, []);

  const openRunDetail = useCallback((run: ResearchRunRecord) => {
    setSelectedRun(run);
  }, []);

  const closeRunDetail = useCallback(() => {
    setSelectedRun(null);
  }, []);

  const closeRunDetailForKey = useCallback((key: string) => {
    if (!shouldCloseRunDetailForKey(key)) {
      return false;
    }

    setSelectedRun(null);
    return true;
  }, []);

  const loadPromotionHistory = useCallback(async () => {
    setActiveCommand("history");
    setActionError(null);

    try {
      const rows = await services.getPromotionHistory();
      setPromotionHistory(rows);
      setPromotionHistoryLoaded(true);
    } catch (err) {
      setActionError(err instanceof Error ? err.message : "Promotion history failed.");
    } finally {
      setActiveCommand(null);
    }
  }, [services]);

  const compareSelectedRuns = useCallback(async () => {
    if (selectedIds.length !== 2) {
      setActionError("Select exactly two runs before comparing.");
      return;
    }

    setActiveCommand("compare");
    setActionError(null);

    try {
      const rows = await services.compareRuns(selectedIds);
      setComparison(rows);
      setRunDiff(null);
      setComparisonLoaded(true);
      setRunDiffLoaded(false);
    } catch (err) {
      setActionError(err instanceof Error ? err.message : "Run comparison failed.");
    } finally {
      setActiveCommand(null);
    }
  }, [selectedIds, services]);

  const diffSelectedRuns = useCallback(async () => {
    if (selectedIds.length !== 2) {
      setActionError("Select exactly two runs before diffing.");
      return;
    }

    setActiveCommand("diff");
    setActionError(null);

    try {
      const result = await services.diffRuns(selectedIds[0], selectedIds[1]);
      setRunDiff(result);
      setComparison([]);
      setRunDiffLoaded(true);
      setComparisonLoaded(false);
    } catch (err) {
      setActionError(err instanceof Error ? err.message : "Run diff failed.");
    } finally {
      setActiveCommand(null);
    }
  }, [selectedIds, services]);

  return {
    ...state,
    toggleRun,
    openRunDetail,
    closeRunDetail,
    closeRunDetailForKey,
    loadPromotionHistory,
    compareSelectedRuns,
    diffSelectedRuns
  };
}

export function buildResearchRunLibraryState({
  runs,
  selectedIds,
  selectedRun,
  comparison,
  runDiff,
  promotionHistory,
  comparisonLoaded = false,
  runDiffLoaded = false,
  promotionHistoryLoaded = false,
  activeCommand,
  actionError
}: {
  runs: ResearchRunRecord[];
  selectedIds: string[];
  selectedRun: ResearchRunRecord | null;
  comparison: RunComparisonRow[];
  runDiff: RunDiff | null;
  promotionHistory: PromotionRecord[];
  comparisonLoaded?: boolean;
  runDiffLoaded?: boolean;
  promotionHistoryLoaded?: boolean;
  activeCommand: ResearchCommand | null;
  actionError: string | null;
}): ResearchRunLibraryState {
  const selectedRuns = selectedIds
    .map((id) => runs.find((run) => run.id === id))
    .filter((run): run is ResearchRunRecord => run !== undefined);
  const hasTwoRuns = selectedIds.length === 2;
  const busy = activeCommand !== null;
  const runTable = buildRunTable(runs);
  const comparisonTable = buildComparisonTable(comparison);
  const diffPanel = buildDiffPanel(runDiff);
  const promotionHistoryTable = buildPromotionHistoryTable(promotionHistory);

  return {
    runs,
    runTable,
    selectedIds,
    selectedRuns,
    selectedRun,
    selectedRunDetail: selectedRun ? buildRunDetail(selectedRun) : null,
    comparison,
    comparisonTable,
    runDiff,
    diffPanel,
    promotionHistory,
    promotionHistoryTable,
    showComparisonPanel: comparisonLoaded || comparison.length > 0,
    showDiffPanel: runDiffLoaded || runDiff !== null,
    showPromotionHistoryPanel: promotionHistoryLoaded || promotionHistory.length > 0,
    activeCommand,
    actionError,
    canCompare: hasTwoRuns && !busy,
    canDiff: hasTwoRuns && !busy,
    canLoadPromotionHistory: !busy,
    selectionText: buildSelectionText(selectedRuns),
    selectionDetail: hasTwoRuns
      ? "Ready to compare or diff the selected run pair."
      : "Select two runs to enable compare and diff commands.",
    compareButtonLabel: activeCommand === "compare" ? "Comparing..." : "Compare 2 runs",
    diffButtonLabel: activeCommand === "diff" ? "Diffing..." : "Diff 2 runs",
    promotionHistoryButtonLabel: activeCommand === "history" ? "Loading history..." : "Promotion history",
    statusAnnouncement: buildResearchStatusAnnouncement({
      activeCommand,
      actionError,
      comparison,
      runDiff,
      promotionHistory,
      comparisonLoaded,
      runDiffLoaded,
      promotionHistoryLoaded
    })
  };
}

export function buildRunTable(runs: ResearchRunRecord[]): ResearchResultTableState<ResearchRunTableRow> {
  return {
    rows: runs.map((run) => ({
      id: run.id,
      strategyName: formatText(run.strategyName),
      engineText: formatText(run.engine),
      mode: run.mode,
      modeLabel: formatText(run.mode).toUpperCase(),
      statusText: formatText(run.status),
      pnlText: formatText(run.pnl),
      sharpeText: formatText(run.sharpe),
      lastUpdatedText: formatText(run.lastUpdated),
      selectAriaLabel: `Select ${formatText(run.strategyName)}`,
      openDetailLabel: `Open ${formatText(run.strategyName)} run detail`,
      raw: run
    })),
    hasRows: runs.length > 0,
    caption: "Strategy runs available for compare, diff, and detail review.",
    emptyText: "No strategy runs available. Start a backtest or paper session, then refresh Strategy."
  };
}

export function buildComparisonTable(
  comparison: RunComparisonRow[]
): ResearchResultTableState<ResearchComparisonTableRow> {
  return {
    rows: comparison.map((row) => ({
      runId: row.runId,
      strategyName: formatText(row.strategyName),
      modeText: formatText(row.mode),
      statusText: formatText(row.status),
      sharpeRatioText: formatNullableNumber(row.sharpeRatio, 3),
      fillCountText: Number.isFinite(row.fillCount) ? row.fillCount.toLocaleString() : "Unavailable"
    })),
    hasRows: comparison.length > 0,
    caption: "Run comparison results returned by the workstation API.",
    emptyText: "No comparison rows returned for the selected pair."
  };
}

export function buildDiffPanel(runDiff: RunDiff | null): ResearchDiffPanelState {
  if (!runDiff) {
    return {
      title: "Position & parameter diff",
      description: "No run diff has been loaded for the selected pair.",
      positionChanges: [],
      parameterChanges: [],
      positionEmptyText: "No position diff result is available.",
      parameterEmptyText: "No parameter diff result is available."
    };
  }

  const positionChanges = [
    ...runDiff.addedPositions,
    ...runDiff.removedPositions,
    ...runDiff.modifiedPositions
  ].map((item) => ({
    key: `${item.symbol}-${item.changeType}`,
    text: `${formatText(item.symbol)} ${item.changeType}`
  }));

  return {
    title: "Position & parameter diff",
    description: `${runDiff.baseStrategyName} compared with ${runDiff.targetStrategyName}.`,
    positionChanges,
    parameterChanges: runDiff.parameterChanges.map((item) => ({
      key: item.key,
      baseValueText: formatText(item.baseValue),
      targetValueText: formatText(item.targetValue)
    })),
    positionEmptyText: "No position changes returned for this diff.",
    parameterEmptyText: "No parameter changes returned for this diff."
  };
}

export function buildPromotionHistoryTable(
  promotionHistory: PromotionRecord[]
): ResearchResultTableState<ResearchPromotionHistoryRow> {
  return {
    rows: promotionHistory.map((record) => ({
      promotionId: record.promotionId,
      strategyName: formatText(record.strategyName),
      qualifyingSharpeText: formatNullableNumber(record.qualifyingSharpe, 3),
      routeText: `${formatText(record.sourceRunType)} to ${formatText(record.targetRunType)}`,
      promotedAtText: formatText(record.promotedAt)
    })),
    hasRows: promotionHistory.length > 0,
    caption: "Promotion history decisions returned for Strategy runs.",
    emptyText: "No promotion history records returned."
  };
}

export function buildRunDetail(run: ResearchRunRecord): ResearchRunDetailState {
  const title = formatText(run.strategyName);
  const modeLabel = formatText(run.mode).toUpperCase();
  const statusText = formatText(run.status);

  return {
    dialogTitleId: `strategy-run-detail-${sanitizeDomId(run.id)}-title`,
    dialogDescriptionId: `strategy-run-detail-${sanitizeDomId(run.id)}-description`,
    description: `${title} is ${statusText} in ${modeLabel} mode.`,
    eyebrow: "Run detail",
    title,
    subtitle: `${formatText(run.engine)} - ${formatText(run.dataset)} - ${formatText(run.window)}`,
    modeBadgeLabel: modeLabel,
    modeBadgeVariant: modeBadgeVariantFor(run.mode),
    summaryLabel: "Selected strategy run evidence",
    summaryRows: [
      { id: "run-id", label: "Run ID", value: formatText(run.id) },
      { id: "status", label: "Status", value: statusText },
      { id: "pnl", label: "P&L", value: formatText(run.pnl) },
      { id: "sharpe", label: "Sharpe", value: formatText(run.sharpe) },
      { id: "updated", label: "Updated", value: formatText(run.lastUpdated) }
    ],
    notesLabel: "Operator notes",
    notesText: formatOptionalNotes(run.notes),
    closeButtonLabel: "Close",
    closeButtonAriaLabel: `Close ${title} run detail`
  };
}

export function toggleRunSelection(currentIds: string[], runId: string): string[] {
  if (currentIds.includes(runId)) {
    return currentIds.filter((id) => id !== runId);
  }

  return [...currentIds, runId].slice(-2);
}

export function shouldCloseRunDetailForKey(key: string): boolean {
  return key === "Escape" || key === "Esc";
}

function buildSelectionText(selectedRuns: ResearchRunRecord[]): string {
  if (selectedRuns.length === 0) {
    return "No runs selected";
  }

  if (selectedRuns.length === 1) {
    return `Selected ${selectedRuns[0].strategyName}`;
  }

  return `${selectedRuns[0].strategyName} vs ${selectedRuns[1].strategyName}`;
}

function formatText(value: string | null | undefined): string {
  const trimmed = value?.trim();
  return trimmed ? trimmed : "Unavailable";
}

function formatOptionalNotes(value: string | null | undefined): string {
  const trimmed = value?.trim();
  return trimmed ? trimmed : "No operator notes were recorded for this run.";
}

function formatNullableNumber(value: number | null | undefined, digits: number): string {
  return typeof value === "number" && Number.isFinite(value)
    ? value.toFixed(digits)
    : "Unavailable";
}

function modeBadgeVariantFor(mode: ResearchRunRecord["mode"]): ResearchRunDetailBadgeVariant {
  if (mode === "paper") {
    return "paper";
  }

  if (mode === "live") {
    return "live";
  }

  return "research";
}

function sanitizeDomId(value: string): string {
  const normalized = value.trim().toLowerCase().replace(/[^a-z0-9_-]+/g, "-").replace(/^-+|-+$/g, "");
  return normalized || "run";
}

function buildResearchStatusAnnouncement({
  activeCommand,
  actionError,
  comparison,
  runDiff,
  promotionHistory,
  comparisonLoaded = false,
  runDiffLoaded = false,
  promotionHistoryLoaded = false
}: {
  activeCommand: ResearchCommand | null;
  actionError: string | null;
  comparison: RunComparisonRow[];
  runDiff: RunDiff | null;
  promotionHistory: PromotionRecord[];
  comparisonLoaded?: boolean;
  runDiffLoaded?: boolean;
  promotionHistoryLoaded?: boolean;
}): string {
  if (activeCommand === "compare") {
    return "Comparing selected research runs.";
  }

  if (activeCommand === "diff") {
    return "Diffing selected research runs.";
  }

  if (activeCommand === "history") {
    return "Loading promotion history.";
  }

  if (actionError) {
    return `Research command failed: ${actionError}`;
  }

  if (runDiff) {
    return `Run diff ready for ${runDiff.baseStrategyName} and ${runDiff.targetStrategyName}.`;
  }

  if (runDiffLoaded) {
    return "No run diff returned for the selected pair.";
  }

  if (comparison.length > 0) {
    return `${comparison.length} comparison ${comparison.length === 1 ? "row" : "rows"} loaded.`;
  }

  if (comparisonLoaded) {
    return "No comparison rows returned for the selected pair.";
  }

  if (promotionHistory.length > 0) {
    return `${promotionHistory.length} promotion history ${promotionHistory.length === 1 ? "record" : "records"} loaded.`;
  }

  if (promotionHistoryLoaded) {
    return "No promotion history records returned.";
  }

  return "";
}
