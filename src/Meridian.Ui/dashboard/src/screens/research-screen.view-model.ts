import { useCallback, useMemo, useRef, useState } from "react";
import * as workstationApi from "@/lib/api";
import { evidenceWorkbenchPath, workspacePath } from "@/lib/workspace";
import type {
  MetricSnapshot,
  MetricsDiff,
  PaperSessionSummary,
  ParameterDiff,
  PositionDiffEntry,
  PromotionEvaluationResult,
  PromotionRecord,
  ResearchRunRecord,
  ResearchPlotToolPayload,
  ResearchWorkspaceResponse,
  RunComparisonRow,
  RunDiff
} from "@/types";

export type ResearchCommand = "compare" | "diff" | "history";
export type ResearchPlotToolView = "workspace" | "statistics";

export interface ResearchRunLibraryServices {
  compareRuns: (runIds: string[]) => Promise<RunComparisonRow[]>;
  diffRuns: (baseRunId: string, targetRunId: string) => Promise<RunDiff>;
  getPromotionHistory: () => Promise<PromotionRecord[]>;
  evaluatePromotion: (runId: string) => Promise<PromotionEvaluationResult>;
  createPaperSession: (strategyId: string, strategyName: string | null, initialCash: number) => Promise<PaperSessionSummary>;
}

export type PromoteState = "idle" | "evaluating" | "evaluated" | "creating" | "done";
export const RESEARCH_RUN_DETAIL_PANEL_ID = "strategy-run-library-selected-run-detail";
export const RESEARCH_COMPARISON_DETAIL_PANEL_ID = "strategy-run-comparison-selected-detail";
export const RESEARCH_PROMOTION_HISTORY_DETAIL_PANEL_ID = "strategy-promotion-history-selected-detail";
export const RESEARCH_PLOT_STUDY_DETAIL_PANEL_ID = "plottool-selected-study-detail";

export interface ResearchRunLibraryState {
  loadingState: ResearchLoadingState;
  runs: ResearchRunRecord[];
  runTable: ResearchResultTableState<ResearchRunTableRow>;
  plotTool: ResearchPlotToolState;
  activePlotToolView: ResearchPlotToolView;
  plotToolTabs: ResearchPlotToolTab[];
  selectedPlotStudyId: string | null;
  selectedPlotStudyDetailPanelId: string;
  selectedPlotStudyDetail: ResearchPlotStudyDetailState | null;
  selectedIds: string[];
  selectedRuns: ResearchRunRecord[];
  selectedRunDetailPanelId: string;
  inspectedRunId: string | null;
  inspectedRunDetail: ResearchRunInlineDetailState | null;
  selectedRun: ResearchRunRecord | null;
  selectedRunDetail: ResearchRunDetailState | null;
  comparison: RunComparisonRow[];
  comparisonTable: ResearchResultTableState<ResearchComparisonTableRow>;
  selectedComparisonRowId: string | null;
  selectedComparisonDetailPanelId: string;
  selectedComparisonDetail: ResearchComparisonDetailState | null;
  runDiff: RunDiff | null;
  diffPanel: ResearchDiffPanelState;
  promotionHistory: PromotionRecord[];
  promotionHistoryTable: ResearchResultTableState<ResearchPromotionHistoryRow>;
  selectedPromotionHistoryId: string | null;
  selectedPromotionHistoryDetailPanelId: string;
  selectedPromotionHistoryDetail: ResearchPromotionHistoryDetailState | null;
  showComparisonPanel: boolean;
  showDiffPanel: boolean;
  showPromotionHistoryPanel: boolean;
  activeCommand: ResearchCommand | null;
  actionError: string | null;
  canCompare: boolean;
  canDiff: boolean;
  canLoadPromotionHistory: boolean;
  canPromote: boolean;
  promotionHistoryCommand: ResearchCommandState;
  compareCommand: ResearchCommandState;
  diffCommand: ResearchCommandState;
  promoteCommand: ResearchCommandState;
  promoteState: PromoteState;
  promotionEval: PromotionEvaluationResult | null;
  promotionPanel: ResearchPromotionPanelState;
  promotionSession: PaperSessionSummary | null;
  showPromotePanel: boolean;
  promoteError: string | null;
  promotionCashForm: ResearchPromotionCashFormState;
  selectionText: string;
  selectionDetail: string;
  evidenceAction: ResearchEvidenceAction | null;
  compareButtonLabel: string;
  diffButtonLabel: string;
  promotionHistoryButtonLabel: string;
  promoteButtonLabel: string;
  statusAnnouncement: string;
  selectPlotStudy: (id: string) => void;
}

export interface ResearchLoadingState {
  role: "status";
  ariaBusy: boolean;
  ariaLive: "polite";
  titleId: string;
  detailId: string;
  title: string;
  detail: string;
  badgeLabel: string;
  routeLabel: string;
}

export interface ResearchCommandState {
  label: string;
  ariaLabel: string;
  disabled: boolean;
  disabledReason: string | null;
  busy: boolean;
}

export interface ResearchPromotionPanelState {
  panelLabel: string;
  statusRole: "status" | "alert";
  statusLive: "polite" | "assertive";
  evaluation: ResearchPromotionEvaluationState | null;
  sessionCreated: ResearchPromotionSessionState | null;
  showCashForm: boolean;
  showIneligibleDismiss: boolean;
}

export interface ResearchEvidenceAction {
  label: string;
  href: string;
  ariaLabel: string;
}

export interface ResearchPromotionEvaluationState {
  title: string;
  titleTone: "success" | "danger";
  reason: string;
  metricRows: ResearchPromotionMetricRow[];
  blockingReasons: ResearchPromotionBlockingReason[];
  hasBlockingReasons: boolean;
  blockingListLabel: string;
}

export interface ResearchPromotionMetricRow {
  id: string;
  label: string;
  value: string;
}

export interface ResearchPromotionBlockingReason {
  id: string;
  text: string;
}

export interface ResearchPromotionSessionState {
  title: string;
  sessionId: string;
  detail: string;
  actionLabel: string;
  actionAriaLabel: string;
  actionHref: string;
}

export interface ResearchPromotionCashFormState {
  inputId: string;
  label: string;
  value: string;
  min: number;
  step: number;
  helpText: string;
  errorText: string | null;
  describedBy: string;
  canSubmit: boolean;
  submitLabel: string;
  submitAriaLabel: string;
}

export interface ResearchPlotToolTab {
  id: ResearchPlotToolView;
  label: string;
  tabId: string;
  panelId: string;
  selected: boolean;
  buttonVariant: "secondary" | "ghost";
  tabIndex: 0 | -1;
  ariaLabel: string;
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
  modeBadgeVariant: ResearchRunDetailBadgeVariant;
  modeLabel: string;
  statusText: string;
  pnlText: string;
  sharpeText: string;
  lastUpdatedText: string;
  selectedForComparison: boolean;
  detailPanelId: string;
  detailExpanded: boolean;
  rowAriaLabel: string;
  rowSelectAriaLabel: string;
  selectAriaLabel: string;
  openDetailLabel: string;
  raw: ResearchRunRecord;
}

export interface ResearchRunInlineDetailState {
  id: string;
  panelId: string;
  ariaLabel: string;
  eyebrow: string;
  title: string;
  subtitle: string;
  description: string;
  statusLabel: string;
  statusVariant: ResearchRunDetailBadgeVariant;
  evidenceAction: ResearchEvidenceAction;
  openDetailLabel: string;
  fields: ResearchRunDetailSummaryRow[];
}

export interface ResearchRunDetailState {
  runId: string;
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
  equityText: string;
  modeText: string;
  modeBadgeVariant: ResearchComparisonBadgeVariant;
  statusText: string;
  statusBadgeVariant: ResearchComparisonBadgeVariant;
  netPnlText: string;
  netPnlTone: ResearchComparisonValueTone;
  totalReturnText: string;
  totalReturnTone: ResearchComparisonValueTone;
  maxDrawdownText: string;
  maxDrawdownTone: ResearchComparisonValueTone;
  sharpeRatioText: string;
  fillCountText: string;
  promotionStateText: string;
  evidenceText: string;
  detailPanelId: string;
  detailExpanded: boolean;
  rowSelectAriaLabel: string;
  ariaLabel: string;
}

export type ResearchComparisonBadgeVariant = "outline" | "success" | "warning" | "danger" | "paper" | "live" | "research";
export type ResearchComparisonValueTone = "success" | "danger" | "muted";

export interface ResearchComparisonDetailState {
  id: string;
  panelId: string;
  ariaLabel: string;
  eyebrow: string;
  title: string;
  subtitle: string;
  description: string;
  statusLabel: string;
  statusVariant: ResearchComparisonBadgeVariant;
  fields: Array<{ label: string; value: string }>;
}

export interface ResearchDiffChangeRow {
  key: string;
  symbolText: string;
  changeTypeText: string;
  quantityText: string;
  pnlText: string;
  text: string;
  badgeVariant: ResearchDiffBadgeVariant;
  ariaLabel: string;
}

export interface ResearchParameterChangeRow {
  key: string;
  baseValueText: string;
  targetValueText: string;
  valueText: string;
  ariaLabel: string;
}

export interface ResearchDiffMetricRow {
  id: string;
  label: string;
  value: string;
  tone: ResearchDiffMetricTone;
  ariaLabel: string;
}

export interface ResearchDiffPanelState {
  title: string;
  description: string;
  ariaLabel: string;
  summaryLabel: string;
  metrics: ResearchDiffMetricRow[];
  positionChanges: ResearchDiffChangeRow[];
  parameterChanges: ResearchParameterChangeRow[];
  positionSectionLabel: string;
  parameterSectionLabel: string;
  positionListLabel: string;
  parameterListLabel: string;
  hasPositionChanges: boolean;
  hasParameterChanges: boolean;
  positionEmptyText: string;
  parameterEmptyText: string;
}

export type ResearchDiffBadgeVariant = "outline" | "success" | "warning" | "danger";
export type ResearchDiffMetricTone = "success" | "danger" | "muted";

export interface ResearchPromotionHistoryRow {
  promotionId: string;
  strategyIdText: string;
  strategyName: string;
  qualifyingSharpeText: string;
  qualifyingMaxDrawdownText: string;
  qualifyingTotalReturnText: string;
  routeText: string;
  decisionText: string;
  promotedAtText: string;
  detailPanelId: string;
  detailExpanded: boolean;
  ariaLabel: string;
  rowSelectAriaLabel: string;
  raw: PromotionRecord;
}

export type ResearchPromotionHistoryBadgeVariant = "outline" | "success" | "warning" | "danger" | "paper" | "live" | "research";

export interface ResearchPromotionHistoryDetailState {
  id: string;
  panelId: string;
  ariaLabel: string;
  eyebrow: string;
  title: string;
  subtitle: string;
  description: string;
  statusLabel: string;
  statusVariant: ResearchPromotionHistoryBadgeVariant;
  fields: Array<{ label: string; value: string }>;
}

export interface ResearchPlotToolState {
  studies: ResearchPlotStudyItem[];
  workspace: ResearchPlotWorkspaceState;
  statistics: ResearchPlotStatisticsState;
}

export interface ResearchPlotStudyItem {
  id: string;
  title: string;
  subtitle: string;
  statusText: string;
  statusBadgeLabel: string;
  statusBadgeVariant: ResearchComparisonBadgeVariant;
  metricText: string;
  noteText: string;
  isActive: boolean;
  detailPanelId: string;
  detailExpanded: boolean;
  ariaLabel: string;
  rowSelectAriaLabel: string;
}

export interface ResearchPlotStudyDetailState {
  id: string;
  panelId: string;
  ariaLabel: string;
  eyebrow: string;
  title: string;
  subtitle: string;
  description: string;
  statusLabel: string;
  statusVariant: ResearchComparisonBadgeVariant;
  fields: Array<{ label: string; value: string }>;
}

export interface ResearchPlotWorkspaceState {
  eyebrow: string;
  title: string;
  description: string;
  statusBadgeLabel: string;
  statusBadgeVariant: ResearchComparisonBadgeVariant;
  expression: string;
  toolbarPills: string[];
  metaItems: string[];
  xAxisLabel: string;
  yAxisLabel: string;
  xTicks: ResearchPlotAxisTick[];
  yTicks: ResearchPlotAxisTick[];
  points: ResearchPlotScatterPoint[];
  scatterChart: ResearchPlotScatterChartState;
  studySummary: ResearchPlotWorkspaceField[];
  legendItems: ResearchPlotLegendItem[];
  focusPoint: ResearchPlotFocusPointState;
  signalCards: ResearchPlotSignalCard[];
  consoleTitle: string;
  consoleBody: string;
  overlayTitle: string;
  overlayItems: string[];
}

export interface ResearchPlotStatisticsState {
  eyebrow: string;
  title: string;
  description: string;
  summaryTiles: ResearchPlotSummaryTile[];
  distributionBars: number[];
  distributionChart: ResearchPlotDistributionChartState;
  distributionSummary: string;
  distributionFootnote: string;
  moments: ResearchPlotMomentRow[];
  momentsTable: ResearchResultTableState<ResearchPlotMomentRow>;
  regression: ResearchPlotRegressionState;
  sampleRows: ResearchPlotSampleRow[];
  sampleTable: ResearchResultTableState<ResearchPlotSampleRow>;
}

export interface ResearchPlotAxisTick {
  value: number;
  label: string;
}

export interface ResearchPlotScatterPoint {
  x: number;
  y: number;
  emphasis: boolean;
}

export interface ResearchPlotScatterChartState {
  ariaLabel: string;
  titleId: string;
  descriptionId: string;
  title: string;
  description: string;
  viewBox: string;
  gridLines: ResearchPlotChartLine[];
  xTicks: ResearchPlotAxisTick[];
  yTicks: ResearchPlotAxisTick[];
  xAxisLabel: string;
  yAxisLabel: string;
  trendLine: ResearchPlotTrendLineState;
  points: ResearchPlotScatterPointMark[];
  marker: ResearchPlotMarkerState;
}

export interface ResearchPlotChartLine {
  id: string;
  x1: number;
  y1: number;
  x2: number;
  y2: number;
  stroke: string;
  strokeWidth: number;
  opacity?: number;
  strokeDasharray?: string;
}

export interface ResearchPlotTrendLineState {
  points: string;
  stroke: string;
  strokeWidth: number;
  strokeDasharray: string;
}

export interface ResearchPlotScatterPointMark {
  id: string;
  x: number;
  y: number;
  radius: number;
  fill: string;
  fillOpacity: number;
  ariaLabel: string;
}

export interface ResearchPlotMarkerState {
  x: number;
  y: number;
  radius: number;
  fill: string;
  stroke: string;
  strokeWidth: number;
  labelX: number;
  labelY: number;
  labelTextX: number;
  labelTextY: number;
  labelWidth: number;
  labelHeight: number;
  labelRadius: number;
  labelFill: string;
  labelStroke: string;
  labelText: string;
  verticalGuide: ResearchPlotChartLine;
  horizontalGuide: ResearchPlotChartLine;
}

export interface ResearchPlotDistributionChartState {
  ariaLabel: string;
  bars: ResearchPlotDistributionBar[];
}

export interface ResearchPlotDistributionBar {
  id: string;
  heightPercent: number;
  tone: "selected" | "base";
  ariaLabel: string;
}

export interface ResearchPlotWorkspaceField {
  id: string;
  label: string;
  value: string;
}

export interface ResearchPlotLegendItem {
  id: string;
  label: string;
  detail: string;
  tone: "history" | "current" | "trend" | "muted";
}

export interface ResearchPlotFocusPointState {
  label: string;
  xValueText: string;
  yValueText: string;
  detail: string;
}

export interface ResearchPlotSignalCard {
  id: string;
  label: string;
  value: string;
  detail: string;
  tone: "default" | "success" | "warning" | "danger";
}

export interface ResearchPlotSummaryTile {
  id: string;
  label: string;
  value: string;
  detail: string;
  tone: "default" | "success" | "warning" | "danger";
}

export interface ResearchPlotMomentRow {
  id: string;
  label: string;
  value: string;
  benchmark: string;
}

export interface ResearchPlotRegressionState {
  equation: string;
  detailItems: string[];
}

export interface ResearchPlotSampleRow {
  id: string;
  timestamp: string;
  spreadText: string;
  impliedVolText: string;
  zScoreText: string;
  signalText: string;
  tone: "default" | "success" | "warning" | "danger";
}

const defaultResearchServices: ResearchRunLibraryServices = {
  compareRuns: (runIds) => workstationApi.compareRuns(runIds),
  diffRuns: (baseRunId, targetRunId) => workstationApi.diffRuns(baseRunId, targetRunId),
  getPromotionHistory: () => workstationApi.getPromotionHistory(),
  evaluatePromotion: (runId) => workstationApi.evaluatePromotion(runId),
  createPaperSession: (strategyId, strategyName, initialCash) => workstationApi.createPaperSession(strategyId, strategyName, initialCash)
};

const currencyFormatter = new Intl.NumberFormat("en-US", {
  style: "currency",
  currency: "USD",
  maximumFractionDigits: 0
});

export function useResearchRunLibraryViewModel(
  data: ResearchWorkspaceResponse | null,
  services: ResearchRunLibraryServices = defaultResearchServices
) {
  const [selectedRun, setSelectedRun] = useState<ResearchRunRecord | null>(null);
  const [inspectedRunId, setInspectedRunId] = useState<string | null>(null);
  const [selectedIds, setSelectedIds] = useState<string[]>([]);
  const [comparison, setComparison] = useState<RunComparisonRow[]>([]);
  const [selectedComparisonRowId, setSelectedComparisonRowId] = useState<string | null>(null);
  const [runDiff, setRunDiff] = useState<RunDiff | null>(null);
  const [promotionHistory, setPromotionHistory] = useState<PromotionRecord[]>([]);
  const [selectedPromotionHistoryId, setSelectedPromotionHistoryId] = useState<string | null>(null);
  const [comparisonLoaded, setComparisonLoaded] = useState(false);
  const [runDiffLoaded, setRunDiffLoaded] = useState(false);
  const [promotionHistoryLoaded, setPromotionHistoryLoaded] = useState(false);
  const [activeCommand, setActiveCommand] = useState<ResearchCommand | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const [activePlotToolView, setActivePlotToolView] = useState<ResearchPlotToolView>("workspace");
  const [selectedPlotStudyId, setSelectedPlotStudyId] = useState<string | null>(null);
  const [promoteState, setPromoteState] = useState<PromoteState>("idle");
  const [promotionEval, setPromotionEval] = useState<PromotionEvaluationResult | null>(null);
  const [promotionSession, setPromotionSession] = useState<PaperSessionSummary | null>(null);
  const [promoteError, setPromoteError] = useState<string | null>(null);
  const [promotionInitialCashInput, setPromotionInitialCashInput] = useState("100000");
  const runScopedCommandRequestId = useRef(0);
  const promotionRequestId = useRef(0);

  const metrics = data?.metrics ?? [];
  const runs = data?.runs ?? [];

  const state = useMemo(
    () => buildResearchRunLibraryState({
      metrics,
      runs,
      plotToolFromApi: data?.plotTool ?? null,
      selectedIds,
      inspectedRunId,
      selectedRun,
      comparison,
      selectedComparisonRowId,
      runDiff,
      promotionHistory,
      selectedPromotionHistoryId,
      comparisonLoaded,
      runDiffLoaded,
      promotionHistoryLoaded,
      activeCommand,
      actionError,
      activePlotToolView,
      selectedPlotStudyId,
      promoteState,
      promotionEval,
      promotionSession,
      promoteError,
      promotionInitialCashInput
    }),
    [
      actionError,
      activeCommand,
      comparison,
      selectedComparisonRowId,
      comparisonLoaded,
      promotionHistory,
      selectedPromotionHistoryId,
      promotionHistoryLoaded,
      runDiff,
      runDiffLoaded,
      metrics,
      data?.plotTool,
      runs,
      selectedIds,
      inspectedRunId,
      selectedRun,
      activePlotToolView,
      selectedPlotStudyId,
      promoteState,
      promotionEval,
      promotionSession,
      promoteError,
      promotionInitialCashInput
    ]
  );

  const toggleRun = useCallback((runId: string) => {
    runScopedCommandRequestId.current += 1;
    promotionRequestId.current += 1;
    setInspectedRunId(runId);
    setSelectedIds((current) => toggleRunSelection(current, runId));
    setComparison([]);
    setSelectedComparisonRowId(null);
    setRunDiff(null);
    setComparisonLoaded(false);
    setRunDiffLoaded(false);
    setPromoteState("idle");
    setPromotionEval(null);
    setPromotionSession(null);
    setPromoteError(null);
    setPromotionInitialCashInput("100000");
    setActionError(null);
    setActiveCommand((current) => current === "history" ? current : null);
  }, []);

  const openRunDetail = useCallback((run: ResearchRunRecord) => {
    setSelectedRun(run);
  }, []);

  const selectRunDetail = useCallback((runId: string) => {
    setInspectedRunId(runId);
  }, []);

  const openRunDetailById = useCallback((runId: string) => {
    const run = runs.find((candidate) => candidate.id === runId);
    if (run) {
      setSelectedRun(run);
    }
  }, [runs]);

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

  const selectPlotToolViewForKey = useCallback((key: string) => {
    const nextView = nextPlotToolViewForKey(activePlotToolView, key);
    if (!nextView) {
      return false;
    }

    setActivePlotToolView(nextView);
    return true;
  }, [activePlotToolView]);

  const loadPromotionHistory = useCallback(async () => {
    setActiveCommand("history");
    setActionError(null);

    try {
      const rows = await services.getPromotionHistory();
      setPromotionHistory(rows);
      setSelectedPromotionHistoryId((current) => rows.some((row) => row.promotionId === current)
        ? current
        : rows[0]?.promotionId ?? null);
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

    const requestId = runScopedCommandRequestId.current + 1;
    runScopedCommandRequestId.current = requestId;
    const requestedRunIds = [...selectedIds];
    setActiveCommand("compare");
    setActionError(null);

    try {
      const rows = await services.compareRuns(requestedRunIds);
      if (runScopedCommandRequestId.current !== requestId) {
        return;
      }

      setComparison(rows);
      setSelectedComparisonRowId(rows[0]?.runId ?? null);
      setRunDiff(null);
      setComparisonLoaded(true);
      setRunDiffLoaded(false);
    } catch (err) {
      if (runScopedCommandRequestId.current !== requestId) {
        return;
      }

      setActionError(err instanceof Error ? err.message : "Run comparison failed.");
    } finally {
      if (runScopedCommandRequestId.current === requestId) {
        setActiveCommand(null);
      }
    }
  }, [selectedIds, services]);

  const diffSelectedRuns = useCallback(async () => {
    if (selectedIds.length !== 2) {
      setActionError("Select exactly two runs before diffing.");
      return;
    }

    const requestId = runScopedCommandRequestId.current + 1;
    runScopedCommandRequestId.current = requestId;
    const requestedRunIds = [...selectedIds];
    setActiveCommand("diff");
    setActionError(null);

    try {
      const result = await services.diffRuns(requestedRunIds[0], requestedRunIds[1]);
      if (runScopedCommandRequestId.current !== requestId) {
        return;
      }

      setRunDiff(result);
      setComparison([]);
      setSelectedComparisonRowId(null);
      setRunDiffLoaded(true);
      setComparisonLoaded(false);
    } catch (err) {
      if (runScopedCommandRequestId.current !== requestId) {
        return;
      }

      setActionError(err instanceof Error ? err.message : "Run diff failed.");
    } finally {
      if (runScopedCommandRequestId.current === requestId) {
        setActiveCommand(null);
      }
    }
  }, [selectedIds, services]);

  const promoteSelectedRun = useCallback(async () => {
    const run = state.selectedRuns[0];
    if (!run) return;
    const requestId = promotionRequestId.current + 1;
    promotionRequestId.current = requestId;
    setPromoteState("evaluating");
    setPromoteError(null);
    setPromotionEval(null);
    setPromotionSession(null);
    setPromotionInitialCashInput("100000");
    try {
      const result = await services.evaluatePromotion(run.id);
      if (promotionRequestId.current !== requestId) {
        return;
      }

      setPromotionEval(result);
      setPromoteState("evaluated");
    } catch (err) {
      if (promotionRequestId.current !== requestId) {
        return;
      }

      setPromoteError(err instanceof Error ? err.message : "Promotion evaluation failed.");
      setPromoteState("idle");
    }
  }, [services, state.selectedRuns]);

  const confirmPromotion = useCallback(async () => {
    const run = state.selectedRuns[0];
    if (!run || !promotionEval?.isEligible) return;
    const initialCash = parsePromotionInitialCashInput(promotionInitialCashInput);
    if (initialCash === null) {
      setPromoteError("Enter initial cash of at least $1,000 before starting a paper session.");
      return;
    }

    const requestId = promotionRequestId.current + 1;
    promotionRequestId.current = requestId;
    setPromoteState("creating");
    setPromoteError(null);
    try {
      const session = await services.createPaperSession(run.id, run.strategyName, initialCash);
      if (promotionRequestId.current !== requestId) {
        return;
      }

      setPromotionSession(session);
      setPromoteState("done");
    } catch (err) {
      if (promotionRequestId.current !== requestId) {
        return;
      }

      setPromoteError(err instanceof Error ? err.message : "Paper session creation failed.");
      setPromoteState("evaluated");
    }
  }, [services, state.selectedRuns, promotionEval, promotionInitialCashInput]);

  const cancelPromotion = useCallback(() => {
    promotionRequestId.current += 1;
    setPromoteState("idle");
    setPromotionEval(null);
    setPromotionSession(null);
    setPromoteError(null);
    setPromotionInitialCashInput("100000");
  }, []);

  return {
    ...state,
    toggleRun,
    openRunDetail,
    closeRunDetail,
    closeRunDetailForKey,
    selectPlotToolView: setActivePlotToolView,
    selectPlotToolViewForKey,
    loadPromotionHistory,
    compareSelectedRuns,
    diffSelectedRuns,
    promoteSelectedRun,
    confirmPromotion,
    cancelPromotion,
    selectRunDetail,
    openRunDetailById,
    selectComparisonRow: setSelectedComparisonRowId,
    selectPromotionHistoryRecord: setSelectedPromotionHistoryId,
    selectPlotStudy: setSelectedPlotStudyId,
    setPromotionInitialCash: setPromotionInitialCashInput
  };
}

export function buildResearchRunLibraryState({
  metrics = [],
  runs,
  plotToolFromApi = null,
  selectedIds,
  inspectedRunId,
  selectedRun,
  comparison,
  selectedComparisonRowId = null,
  runDiff,
  promotionHistory,
  selectedPromotionHistoryId = null,
  comparisonLoaded = false,
  runDiffLoaded = false,
  promotionHistoryLoaded = false,
  activeCommand,
  actionError,
  activePlotToolView = "workspace",
  selectedPlotStudyId = null,
  promoteState = "idle",
  promotionEval = null,
  promotionSession = null,
  promoteError = null,
  promotionInitialCashInput = "100000"
}: {
  metrics?: MetricSnapshot[];
  runs: ResearchRunRecord[];
  plotToolFromApi?: ResearchPlotToolPayload | null;
  selectedIds: string[];
  inspectedRunId?: string | null;
  selectedRun: ResearchRunRecord | null;
  comparison: RunComparisonRow[];
  selectedComparisonRowId?: string | null;
  runDiff: RunDiff | null;
  promotionHistory: PromotionRecord[];
  selectedPromotionHistoryId?: string | null;
  comparisonLoaded?: boolean;
  runDiffLoaded?: boolean;
  promotionHistoryLoaded?: boolean;
  activeCommand: ResearchCommand | null;
  actionError: string | null;
  activePlotToolView?: ResearchPlotToolView;
  selectedPlotStudyId?: string | null;
  promoteState?: PromoteState;
  promotionEval?: PromotionEvaluationResult | null;
  promotionSession?: PaperSessionSummary | null;
  promoteError?: string | null;
  promotionInitialCashInput?: string;
}): ResearchRunLibraryState {
  const selectedRuns = selectedIds
    .map((id) => runs.find((run) => run.id === id))
    .filter((run): run is ResearchRunRecord => run !== undefined);
  const resolvedInspectedRunId = resolveInspectedRunId(runs, inspectedRunId ?? null, selectedRuns);
  const inspectedRun = resolvedInspectedRunId
    ? runs.find((run) => run.id === resolvedInspectedRunId) ?? null
    : null;
  const hasTwoRuns = selectedIds.length === 2;
  const hasOneBacktestRun = selectedIds.length === 1 &&
    selectedRuns[0]?.mode === "backtest" &&
    selectedRuns[0]?.status === "Completed";
  const busy = activeCommand !== null;
  const promoteBusy = promoteState === "evaluating" || promoteState === "creating";
  const promotionCashForm = buildPromotionCashForm({
    input: promotionInitialCashInput,
    eligible: promotionEval?.isEligible === true,
    promoteState
  });
  const promotionPanel = buildPromotionPanelState({
    promoteState,
    promotionEval,
    promotionSession
  });
  const runTable = buildRunTable(runs, {
    selectedIds,
    inspectedRunId: resolvedInspectedRunId,
    detailPanelId: RESEARCH_RUN_DETAIL_PANEL_ID
  });
  const resolvedComparisonRowId = resolveSelectedComparisonRunId(comparison, selectedComparisonRowId);
  const comparisonTable = buildComparisonTable(comparison, resolvedComparisonRowId);
  const selectedComparisonRow = resolvedComparisonRowId
    ? comparisonTable.rows.find((row) => row.runId === resolvedComparisonRowId) ?? null
    : null;
  const diffPanel = buildDiffPanel(runDiff);
  const resolvedPromotionHistoryId = resolveSelectedPromotionHistoryId(promotionHistory, selectedPromotionHistoryId);
  const selectedPromotionHistory = resolvedPromotionHistoryId
    ? promotionHistory.find((record) => record.promotionId === resolvedPromotionHistoryId) ?? null
    : null;
  const promotionHistoryTable = buildPromotionHistoryTable(promotionHistory, resolvedPromotionHistoryId);
  const basePlotTool = buildPlotToolStateFromApiOrFallback(
    plotToolFromApi,
    {
      metrics,
      runs,
      selectedRuns,
      comparison,
      runDiff
    }
  );
  const resolvedPlotStudyId = resolveSelectedPlotStudyId(basePlotTool.studies, selectedPlotStudyId);
  const plotTool: ResearchPlotToolState = {
    ...basePlotTool,
    studies: buildPlotStudyRows(basePlotTool.studies, resolvedPlotStudyId)
  };
  const selectedPlotStudy = resolvedPlotStudyId
    ? plotTool.studies.find((study) => study.id === resolvedPlotStudyId) ?? null
    : null;
  const commandStates = buildResearchCommandStates({
    selectedRuns,
    hasTwoRuns,
    hasOneBacktestRun,
    busy,
    activeCommand,
    promoteState,
    promoteBusy
  });

  return {
    loadingState: buildResearchLoadingState(),
    runs,
    runTable,
    plotTool,
    activePlotToolView,
    plotToolTabs: buildPlotToolTabs(activePlotToolView),
    selectedPlotStudyId: resolvedPlotStudyId,
    selectedPlotStudyDetailPanelId: RESEARCH_PLOT_STUDY_DETAIL_PANEL_ID,
    selectedPlotStudyDetail: selectedPlotStudy ? buildPlotStudyDetail(selectedPlotStudy) : null,
    selectedIds,
    selectedRuns,
    selectedRunDetailPanelId: RESEARCH_RUN_DETAIL_PANEL_ID,
    inspectedRunId: resolvedInspectedRunId,
    inspectedRunDetail: inspectedRun ? buildInlineRunDetail(inspectedRun, RESEARCH_RUN_DETAIL_PANEL_ID) : null,
    selectedRun,
    selectedRunDetail: selectedRun ? buildRunDetail(selectedRun) : null,
    comparison,
    comparisonTable,
    selectedComparisonRowId: resolvedComparisonRowId,
    selectedComparisonDetailPanelId: RESEARCH_COMPARISON_DETAIL_PANEL_ID,
    selectedComparisonDetail: selectedComparisonRow
      ? buildComparisonDetail(selectedComparisonRow, RESEARCH_COMPARISON_DETAIL_PANEL_ID)
      : null,
    runDiff,
    diffPanel,
    promotionHistory,
    promotionHistoryTable,
    selectedPromotionHistoryId: resolvedPromotionHistoryId,
    selectedPromotionHistoryDetailPanelId: RESEARCH_PROMOTION_HISTORY_DETAIL_PANEL_ID,
    selectedPromotionHistoryDetail: selectedPromotionHistory
      ? buildPromotionHistoryDetail(selectedPromotionHistory, RESEARCH_PROMOTION_HISTORY_DETAIL_PANEL_ID)
      : null,
    showComparisonPanel: comparisonLoaded || comparison.length > 0,
    showDiffPanel: runDiffLoaded || runDiff !== null,
    showPromotionHistoryPanel: promotionHistoryLoaded || promotionHistory.length > 0,
    activeCommand,
    actionError,
    canCompare: hasTwoRuns && !busy,
    canDiff: hasTwoRuns && !busy,
    canLoadPromotionHistory: !busy,
    canPromote: hasOneBacktestRun && !busy && !promoteBusy && promoteState !== "done",
    promotionHistoryCommand: commandStates.promotionHistory,
    compareCommand: commandStates.compare,
    diffCommand: commandStates.diff,
    promoteCommand: commandStates.promote,
    promoteState,
    promotionEval,
    promotionPanel,
    promotionSession,
    showPromotePanel: promoteState !== "idle",
    promoteError,
    promotionCashForm,
    selectionText: buildSelectionText(selectedRuns),
    selectionDetail: hasTwoRuns
      ? "Ready to compare or diff the selected run pair."
      : hasOneBacktestRun
        ? "Select Promote to Paper to evaluate this run for paper trading."
        : "Select two runs to enable compare and diff commands.",
    evidenceAction: buildResearchEvidenceAction(selectedRuns[0] ?? runs[0] ?? null),
    compareButtonLabel: commandStates.compare.label,
    diffButtonLabel: commandStates.diff.label,
    promotionHistoryButtonLabel: commandStates.promotionHistory.label,
    promoteButtonLabel: commandStates.promote.label,
    statusAnnouncement: buildResearchStatusAnnouncement({
      activeCommand,
      actionError,
      comparison,
      runDiff,
      promotionHistory,
      comparisonLoaded,
      runDiffLoaded,
      promotionHistoryLoaded
    }),
    selectPlotStudy: () => undefined
  };
}

export function buildResearchLoadingState(): ResearchLoadingState {
  return {
    role: "status",
    ariaBusy: true,
    ariaLive: "polite",
    titleId: "strategy-loading-title",
    detailId: "strategy-loading-detail",
    title: "Loading Strategy",
    detail: "Waiting for run history, PlotTool state, and promotion evidence.",
    badgeLabel: "Loading",
    routeLabel: "Strategy"
  };
}

export function buildPromotionPanelState({
  promoteState,
  promotionEval,
  promotionSession
}: {
  promoteState: PromoteState;
  promotionEval: PromotionEvaluationResult | null;
  promotionSession: PaperSessionSummary | null;
}): ResearchPromotionPanelState {
  const evaluation = promotionEval ? buildPromotionEvaluationState(promotionEval) : null;
  const sessionCreated = promoteState === "done" && promotionSession
    ? {
      title: `Paper session created - session ${promotionSession.sessionId}`,
      sessionId: promotionSession.sessionId,
      detail: `${promotionSession.strategyName ?? "Selected strategy"} is active with ${formatMoney(promotionSession.initialCash)} paper capital.`,
      actionLabel: "Go to Trading cockpit",
      actionAriaLabel: `Go to Trading cockpit for paper session ${promotionSession.sessionId}`,
      actionHref: workspacePath("trading")
    }
    : null;

  return {
    panelLabel: "Promotion evaluation",
    statusRole: evaluation?.titleTone === "danger" ? "alert" : "status",
    statusLive: evaluation?.titleTone === "danger" ? "assertive" : "polite",
    evaluation,
    sessionCreated,
    showCashForm: promoteState === "evaluated" && promotionEval?.isEligible === true,
    showIneligibleDismiss: promoteState === "evaluated" && promotionEval?.isEligible === false
  };
}

export function buildResearchCommandStates({
  selectedRuns,
  hasTwoRuns,
  hasOneBacktestRun,
  busy,
  activeCommand,
  promoteState,
  promoteBusy
}: {
  selectedRuns: ResearchRunRecord[];
  hasTwoRuns: boolean;
  hasOneBacktestRun: boolean;
  busy: boolean;
  activeCommand: ResearchCommand | null;
  promoteState: PromoteState;
  promoteBusy: boolean;
}): {
  promotionHistory: ResearchCommandState;
  compare: ResearchCommandState;
  diff: ResearchCommandState;
  promote: ResearchCommandState;
} {
  const pairLabel = selectedRuns.length === 2
    ? `${formatText(selectedRuns[0].strategyName)} and ${formatText(selectedRuns[1].strategyName)}`
    : null;
  const selectedRun = selectedRuns[0] ?? null;
  const pairDisabledReason = busy
    ? "Wait for the current Strategy command to finish."
    : hasTwoRuns
      ? null
      : `Select exactly two runs before using this command. ${selectedRuns.length} selected.`;
  const promoteLabel = promoteState === "evaluating"
    ? "Evaluating..."
    : promoteState === "creating"
      ? "Creating session..."
      : promoteState === "done"
        ? "Session created"
        : "Promote to Paper";
  const promoteDisabledReason = busy
    ? "Wait for the current Strategy command to finish."
    : promoteBusy
      ? "Paper-promotion workflow is already running."
      : promoteState === "done"
        ? "A paper session was already created for this promotion."
        : hasOneBacktestRun
          ? null
          : selectedRuns.length === 1
            ? "Select one completed backtest run before promoting to paper."
            : `Select one completed backtest run before promoting to paper. ${selectedRuns.length} selected.`;

  return {
    promotionHistory: {
      label: activeCommand === "history" ? "Loading history..." : "Promotion history",
      ariaLabel: activeCommand === "history" ? "Promotion history: loading" : "Promotion history",
      disabled: busy,
      disabledReason: busy ? "Wait for the current Strategy command to finish." : null,
      busy: activeCommand === "history"
    },
    compare: {
      label: activeCommand === "compare" ? "Comparing..." : "Compare 2 runs",
      ariaLabel: activeCommand === "compare"
        ? `Comparing selected runs: ${pairLabel ?? "current selection"}`
        : pairLabel
          ? `Compare 2 runs: ${pairLabel}`
          : "Compare 2 runs unavailable",
      disabled: !hasTwoRuns || busy,
      disabledReason: pairDisabledReason,
      busy: activeCommand === "compare"
    },
    diff: {
      label: activeCommand === "diff" ? "Diffing..." : "Diff 2 runs",
      ariaLabel: activeCommand === "diff"
        ? `Diffing selected runs: ${pairLabel ?? "current selection"}`
        : pairLabel
          ? `Diff 2 runs: ${pairLabel}`
          : "Diff 2 runs unavailable",
      disabled: !hasTwoRuns || busy,
      disabledReason: pairDisabledReason,
      busy: activeCommand === "diff"
    },
    promote: {
      label: promoteLabel,
      ariaLabel: selectedRun
        ? `${promoteLabel}: evaluate ${formatText(selectedRun.strategyName)} for paper trading`
        : `${promoteLabel} unavailable`,
      disabled: !hasOneBacktestRun || busy || promoteBusy || promoteState === "done",
      disabledReason: promoteDisabledReason,
      busy: promoteBusy
    }
  };
}

function formatDecimal(value: number | null | undefined, digits: number): string {
  if (typeof value !== "number" || !Number.isFinite(value)) {
    return "Unavailable";
  }

  return value.toFixed(digits);
}

function formatPercent(value: number | null | undefined, digits: number): string {
  if (typeof value !== "number" || !Number.isFinite(value)) {
    return "Unavailable";
  }

  return `${(value * 100).toFixed(digits)}%`;
}

export function buildPromotionEvaluationState(
  promotionEval: PromotionEvaluationResult
): ResearchPromotionEvaluationState {
  const title = promotionEval.isEligible ? "Eligible for paper trading" : "Not eligible";
  const blockingReasons = (promotionEval.blockingReasons ?? [])
    .filter((reason) => reason.trim().length > 0)
    .map((reason, index) => ({
      id: `${promotionEval.runId}-blocker-${index}`,
      text: reason
    }));

  return {
    title,
    titleTone: promotionEval.isEligible ? "success" : "danger",
    reason: promotionEval.reason.trim() || "Promotion evaluation returned no reason.",
    metricRows: [
      { id: "sharpe", label: "Sharpe", value: formatDecimal(promotionEval.sharpeRatio, 2) },
      { id: "drawdown", label: "Max DD", value: formatPercent(promotionEval.maxDrawdownPercent, 1) },
      { id: "return", label: "Return", value: formatPercent(promotionEval.totalReturn, 1) }
    ],
    blockingReasons,
    hasBlockingReasons: blockingReasons.length > 0,
    blockingListLabel: `${title} blocking reasons`
  };
}

export function buildPromotionCashForm({
  input,
  eligible,
  promoteState
}: {
  input: string;
  eligible: boolean;
  promoteState: PromoteState;
}): ResearchPromotionCashFormState {
  const normalizedInput = input.trim();
  const parsed = parsePromotionInitialCashInput(input);
  const isCreating = promoteState === "creating";
  const shouldValidate = eligible && promoteState === "evaluated";
  const errorText = shouldValidate && normalizedInput.length > 0 && parsed === null
    ? "Enter at least $1,000 in whole dollars."
    : null;
  const helpText = errorText ?? "Minimum $1,000. Use whole-dollar paper capital.";

  return {
    inputId: "promote-initial-cash",
    label: "Initial cash ($)",
    value: input,
    min: 1000,
    step: 1000,
    helpText,
    errorText,
    describedBy: "promote-initial-cash-help",
    canSubmit: eligible && promoteState === "evaluated" && parsed !== null && !isCreating,
    submitLabel: isCreating ? "Starting paper session..." : "Start paper session",
    submitAriaLabel: "Start paper session from selected strategy run"
  };
}

export function parsePromotionInitialCashInput(value: string): number | null {
  const trimmed = value.trim();
  if (!trimmed) {
    return null;
  }

  const parsed = Number(trimmed);
  if (!Number.isFinite(parsed) || parsed < 1000 || !Number.isInteger(parsed)) {
    return null;
  }

  return parsed;
}

export function buildPlotToolTabs(activeView: ResearchPlotToolView): ResearchPlotToolTab[] {
  const tabs: Array<Pick<ResearchPlotToolTab, "id" | "label" | "tabId" | "panelId">> = [
    {
      id: "workspace",
      label: "Workstation",
      tabId: "plottool-workspace-tab",
      panelId: "plottool-workspace-panel"
    },
    {
      id: "statistics",
      label: "Statistics",
      tabId: "plottool-statistics-tab",
      panelId: "plottool-statistics-panel"
    }
  ];

  return tabs.map((tab) => {
    const selected = tab.id === activeView;

    return {
      ...tab,
      selected,
      buttonVariant: selected ? "secondary" : "ghost",
      tabIndex: selected ? 0 : -1,
      ariaLabel: tab.label
    };
  });
}

export function nextPlotToolViewForKey(
  currentView: ResearchPlotToolView,
  key: string
): ResearchPlotToolView | null {
  if (key === "ArrowLeft" || key === "ArrowUp") {
    return currentView === "workspace" ? "statistics" : "workspace";
  }

  if (key === "ArrowRight" || key === "ArrowDown") {
    return currentView === "workspace" ? "statistics" : "workspace";
  }

  if (key === "Home") {
    return "workspace";
  }

  if (key === "End") {
    return "statistics";
  }

  return null;
}

export function buildRunTable(
  runs: ResearchRunRecord[],
  options: {
    selectedIds?: string[];
    inspectedRunId?: string | null;
    detailPanelId?: string;
  } = {}
): ResearchResultTableState<ResearchRunTableRow> {
  const selectedIds = options.selectedIds ?? [];
  const inspectedRunId = options.inspectedRunId ?? resolveInspectedRunId(runs, null, []);
  const detailPanelId = options.detailPanelId ?? RESEARCH_RUN_DETAIL_PANEL_ID;

  return {
    rows: runs.map((run) => {
      const strategyName = formatText(run.strategyName);
      const modeLabel = formatText(run.mode).toUpperCase();
      const statusText = formatText(run.status);
      const selectedForComparison = selectedIds.includes(run.id);
      const detailExpanded = run.id === inspectedRunId;

      return {
        id: run.id,
        strategyName,
        engineText: formatText(run.engine),
        mode: run.mode,
        modeBadgeVariant: modeBadgeVariantFor(run.mode),
        modeLabel,
        statusText,
        pnlText: formatText(run.pnl),
        sharpeText: formatText(run.sharpe),
        lastUpdatedText: formatText(run.lastUpdated),
        selectedForComparison,
        detailPanelId,
        detailExpanded,
        rowAriaLabel: `${strategyName}: ${statusText}, ${modeLabel}, P&L ${formatText(run.pnl)}, Sharpe ${formatText(run.sharpe)}.`,
        rowSelectAriaLabel: `Inspect ${strategyName} run detail`,
        selectAriaLabel: `${selectedForComparison ? "Remove" : "Select"} ${strategyName} for compare and diff`,
        openDetailLabel: `Open ${strategyName} run detail`,
        raw: run
      };
    }),
    hasRows: runs.length > 0,
    caption: "Strategy runs available for compare, diff, and detail review.",
    emptyText: "No strategy runs available. Start a backtest or paper session, then refresh Strategy."
  };
}

function buildPlotToolStateFromApiOrFallback(
  plotToolFromApi: ResearchPlotToolPayload | null | undefined,
  fallbackArgs: {
    metrics: MetricSnapshot[];
    runs: ResearchRunRecord[];
    selectedRuns: ResearchRunRecord[];
    comparison: RunComparisonRow[];
    runDiff: RunDiff | null;
  }
): ResearchPlotToolState {
  if (plotToolFromApi?.workspace && plotToolFromApi.statistics) {
    const workspace = plotToolFromApi.workspace as ResearchPlotWorkspaceState;
    const statistics = plotToolFromApi.statistics as ResearchPlotStatisticsState;

    return {
      studies: (plotToolFromApi.studies as ResearchPlotStudyItem[]) ?? [],
      workspace: ensurePlotToolWorkspaceChartState(workspace),
      statistics: ensurePlotToolStatisticsChartState(statistics)
    };
  }

  return buildPlotToolState(fallbackArgs);
}

export function buildPlotToolState({
  metrics,
  runs,
  selectedRuns,
  comparison,
  runDiff
}: {
  metrics: MetricSnapshot[];
  runs: ResearchRunRecord[];
  selectedRuns: ResearchRunRecord[];
  comparison: RunComparisonRow[];
  runDiff: RunDiff | null;
}): ResearchPlotToolState {
  const activeRun = selectedRuns[0] ?? runs[0] ?? null;
  const companionRun = selectedRuns[1] ?? runs[1] ?? null;
  const studyName = formatText(activeRun?.strategyName ?? "Meridian PlotTool");
  const companionName = companionRun ? formatText(companionRun.strategyName) : null;
  const datasetName = formatText(activeRun?.dataset ?? "Cross-asset sandbox");
  const windowName = formatText(activeRun?.window ?? "MAX");
  const observationCount = 2184 + (runs.length * 9);
  const correlation = 0.84;
  const rSquared = 0.71;
  const beta = 0.48;
  const meanX = 66.84;
  const meanY = 71.42;
  const parsedSharpe = parseDecimalToken(activeRun?.sharpe) ?? 1.24;
  const parsedReturn = activeRun?.totalReturn ?? parsePercentToken(activeRun?.pnl) ?? 0.042;
  const maxDrawdown = comparison[0]?.maxDrawdown ?? -0.148;
  const queuedMetric = metrics.find((metric) => /queue/i.test(metric.label))?.value ?? `${runs.filter((run) => run.status === "Queued").length}`;
  const reviewMetric = metrics.find((metric) => /review/i.test(metric.label))?.value ?? `${runs.filter((run) => /review/i.test(run.status)).length}`;
  const promotionCue = formatPromotionState(activeRun?.promotionState ?? comparison[0]?.promotionState ?? null);
  const evidenceCue = comparison[0] ? buildComparisonEvidenceText(comparison[0]) : "Compare a pair to attach ledger and audit evidence.";
  const diffCue = runDiff
    ? `${runDiff.parameterChanges.length} parameter changes and ${runDiff.addedPositions.length + runDiff.removedPositions.length + runDiff.modifiedPositions.length} position changes linked.`
    : "Load a diff to attach position and parameter drift.";
  const chartStudyLabel = companionName ? `${studyName} vs ${companionName}` : studyName;
  const latestObservation = plotToolSampleRows[0];
  const focusPoint = findLastPlotPoint(plotToolScatterPoints, (point) => point.emphasis) ?? plotToolScatterPoints[plotToolScatterPoints.length - 1];
  const xAxisLabel = "Spread (bps)";
  const yAxisLabel = "3m implied vol";
  const focusPointState: ResearchPlotFocusPointState = {
    label: "Current marker",
    xValueText: latestObservation.spreadText,
    yValueText: latestObservation.impliedVolText,
    detail: `${latestObservation.signalText} · highlighted at ${formatText(activeRun?.lastUpdated ?? "2m ago")} · x ${focusPoint.x}, y ${focusPoint.y}`
  };
  const momentRows: ResearchPlotMomentRow[] = [
    { id: "net-pnl", label: "Net P&L", value: formatMoney(comparison[0]?.netPnl ?? activeRun?.netPnl ?? 3200, true), benchmark: "Pair summary" },
    { id: "return", label: "Total return", value: formatSignedPercent(parsedReturn), benchmark: "Run linked" },
    { id: "sharpe", label: "Sharpe ratio", value: parsedSharpe.toFixed(3), benchmark: "Operator review" },
    { id: "drawdown", label: "Max drawdown", value: formatSignedPercent(maxDrawdown), benchmark: "Distribution tail" },
    { id: "promotion", label: "Promotion state", value: promotionCue, benchmark: "Strategy posture" },
    { id: "evidence", label: "Evidence pack", value: evidenceCue, benchmark: "Ledger / audit" }
  ];
  const sampleRows = plotToolSampleRows;

  return {
    studies: runs.map((run, index) => ({
      id: run.id,
      title: formatText(run.strategyName),
      subtitle: `${formatText(run.dataset)} · ${formatText(run.window)} · ${formatText(run.engine)}`,
      statusText: formatText(run.status),
      statusBadgeLabel: formatText(run.mode).toUpperCase(),
      statusBadgeVariant: badgeVariantForMode(run.mode),
      metricText: `${formatText(run.pnl)} · Sharpe ${formatText(run.sharpe)}`,
      noteText: formatOptionalNotes(run.notes),
      isActive: activeRun ? run.id === activeRun.id : index === 0,
      detailPanelId: RESEARCH_PLOT_STUDY_DETAIL_PANEL_ID,
      detailExpanded: false,
      ariaLabel: `${formatText(run.strategyName)} PlotTool study. ${formatText(run.status)} ${formatText(run.mode)}. ${formatText(run.pnl)} and Sharpe ${formatText(run.sharpe)}.`,
      rowSelectAriaLabel: `Inspect ${formatText(run.strategyName)} PlotTool study detail`
    })),
    workspace: {
      eyebrow: "Strategy Lane · PlotTool",
      title: `${chartStudyLabel} workstation`,
      description: "Meridian notebooks, evidence cues, and factor scatter analysis folded into the Strategy route.",
      statusBadgeLabel: formatText(activeRun?.mode ?? "research").toUpperCase(),
      statusBadgeVariant: badgeVariantForMode(activeRun?.mode ?? "research"),
      expression: buildPlotExpression(activeRun, companionRun),
      toolbarPills: [
        windowName,
        `Daily (${windowName})`,
        companionName ? "Pair overlay" : "Single study",
        runDiff ? "Diff linked" : "0d lag"
      ],
      metaItems: [
        datasetName,
        `${observationCount.toLocaleString()} obs`,
        `β ${beta.toFixed(2)}`,
        `R² ${rSquared.toFixed(2)}`,
        `ρ ${correlation.toFixed(2)}`
      ],
      xAxisLabel,
      yAxisLabel,
      xTicks: plotToolXTicks,
      yTicks: plotToolYTicks,
      points: plotToolScatterPoints,
      scatterChart: buildPlotToolScatterChartState({
        points: plotToolScatterPoints,
        xTicks: plotToolXTicks,
        yTicks: plotToolYTicks,
        xAxisLabel,
        yAxisLabel,
        markerPoint: focusPoint,
        focusPoint: focusPointState,
        chartStudyLabel
      }),
      studySummary: [
        { id: "primary", label: "Primary notebook", value: studyName },
        { id: "companion", label: "Pair target", value: companionName ?? "Select a second run" },
        { id: "promotion", label: "Promotion posture", value: promotionCue },
        { id: "evidence", label: "Evidence pack", value: evidenceCue },
        { id: "diff", label: "Diff posture", value: diffCue },
        { id: "refresh", label: "Last refresh", value: formatText(activeRun?.lastUpdated ?? latestObservation.timestamp) }
      ],
      legendItems: [
        { id: "history", label: "History", detail: `${observationCount.toLocaleString()} observations`, tone: "history" },
        { id: "current", label: "Current", detail: `${latestObservation.spreadText} / ${latestObservation.impliedVolText}`, tone: "current" },
        { id: "trend", label: "OLS fit", detail: "y = 0.48x + 39.31", tone: "trend" },
        { id: "refresh", label: "Refresh", detail: formatText(activeRun?.lastUpdated ?? "2m ago"), tone: "muted" }
      ],
      focusPoint: focusPointState,
      signalCards: [
        {
          id: "correlation",
          label: "Correlation",
          value: correlation.toFixed(2),
          detail: `${studyName} paired with ${datasetName}`,
          tone: "success"
        },
        {
          id: "regression",
          label: "Regression beta",
          value: beta.toFixed(2),
          detail: "OLS slope on the active notebook",
          tone: "default"
        },
        {
          id: "queued",
          label: "Queued studies",
          value: queuedMetric,
          detail: "Run-library backlog still attached to Strategy",
          tone: Number.parseInt(queuedMetric, 10) > 0 ? "warning" : "default"
        },
        {
          id: "review",
          label: "Review queue",
          value: reviewMetric,
          detail: promotionCue === "Unavailable" ? "Promotion posture pending." : promotionCue,
          tone: reviewMetric === "0" ? "success" : "warning"
        }
      ],
      consoleTitle: "Expression editor",
      consoleBody: companionName
        ? `Meridian is staging ${chartStudyLabel} for comparison. Pair evidence, diff results, and promotion posture stay aligned with the selected run pair.`
        : "Select a second run to promote this notebook into pair analysis. The Strategy lane keeps compare, diff, and promotion evidence attached to the PlotTool console.",
      overlayTitle: "Meridian overlays",
      overlayItems: [
        `Notebook coverage: ${runs.length} retained ${runs.length === 1 ? "study" : "studies"} in Strategy.`,
        `Evidence posture: ${evidenceCue}`,
        `Diff posture: ${diffCue}`
      ]
    },
    statistics: {
      eyebrow: "Statistics view",
      title: `${chartStudyLabel} analysis`,
      description: `${datasetName} · ${windowName} · refreshed ${formatText(activeRun?.lastUpdated ?? "2m ago")}`,
      summaryTiles: [
        {
          id: "observations",
          label: "N obs",
          value: observationCount.toLocaleString(),
          detail: "99.7% complete",
          tone: "default"
        },
        {
          id: "mean-y",
          label: "Mean (Y)",
          value: meanY.toFixed(2),
          detail: "σ̂ ±18.30",
          tone: "default"
        },
        {
          id: "mean-x",
          label: "Mean (X)",
          value: meanX.toFixed(2),
          detail: "σ̂ ±32.41",
          tone: "default"
        },
        {
          id: "correlation",
          label: "Correlation",
          value: correlation.toFixed(2),
          detail: "Pearson ρ",
          tone: "success"
        },
        {
          id: "r-squared",
          label: "R²",
          value: rSquared.toFixed(2),
          detail: "OLS fit",
          tone: "success"
        },
        {
          id: "beta",
          label: "β (slope)",
          value: beta.toFixed(2),
          detail: "SE 0.014",
          tone: "success"
        },
        {
          id: "hit-rate",
          label: "Hit rate",
          value: "62.3%",
          detail: "5d horizon",
          tone: "success"
        },
        {
          id: "sharpe",
          label: "Sharpe (5d)",
          value: parsedSharpe.toFixed(2),
          detail: "Run-linked",
          tone: parsedSharpe >= 1 ? "success" : "warning"
        },
        {
          id: "drawdown",
          label: "Max DD",
          value: formatSignedPercent(maxDrawdown),
          detail: "Recent window",
          tone: maxDrawdown < -0.1 ? "danger" : "warning"
        }
      ],
      distributionBars: plotToolDistributionBars,
      distributionChart: buildPlotToolDistributionChartState(plotToolDistributionBars),
      distributionSummary: `${observationCount.toLocaleString()} samples centered on spread ${meanX.toFixed(2)} and IV ${meanY.toFixed(2)}.`,
      distributionFootnote: `Latest observation ${latestObservation.timestamp} · ${latestObservation.signalText} · z-score ${latestObservation.zScoreText}.`,
      moments: momentRows,
      momentsTable: buildPlotToolMomentsTable(momentRows),
      regression: {
        equation: "y = 0.48x + 39.31",
        detailItems: [
          `${chartStudyLabel} stays inside Meridian's evidence-backed strategy lane.`,
          `Queued studies: ${queuedMetric} · Review required: ${reviewMetric}.`,
          diffCue
        ]
      },
      sampleRows,
      sampleTable: buildPlotToolSampleTable(sampleRows)
    }
  };
}

export function buildPlotStudyRows(
  studies: ResearchPlotStudyItem[],
  selectedStudyId: string | null = resolveSelectedPlotStudyId(studies, null)
): ResearchPlotStudyItem[] {
  return studies.map((study) => {
    const title = formatText(study.title);
    const statusText = formatText(study.statusText);
    const metricText = formatText(study.metricText);
    const detailExpanded = study.id === selectedStudyId;

    return {
      ...study,
      title,
      subtitle: formatText(study.subtitle),
      statusText,
      statusBadgeLabel: formatText(study.statusBadgeLabel).toUpperCase(),
      statusBadgeVariant: study.statusBadgeVariant ?? badgeVariantForMode(study.statusBadgeLabel),
      metricText,
      noteText: formatOptionalNotes(study.noteText),
      detailPanelId: RESEARCH_PLOT_STUDY_DETAIL_PANEL_ID,
      detailExpanded,
      ariaLabel: `${title} PlotTool study. ${statusText}. ${metricText}.`,
      rowSelectAriaLabel: `Inspect ${title} PlotTool study detail`
    };
  });
}

export function buildPlotStudyDetail(study: ResearchPlotStudyItem): ResearchPlotStudyDetailState {
  return {
    id: study.id,
    panelId: RESEARCH_PLOT_STUDY_DETAIL_PANEL_ID,
    ariaLabel: `Selected PlotTool study detail for ${study.title}`,
    eyebrow: "Selected notebook",
    title: study.title,
    subtitle: study.subtitle,
    description: `${study.statusText} study retained in the PlotTool workstation. ${study.noteText}`,
    statusLabel: study.statusBadgeLabel,
    statusVariant: study.statusBadgeVariant,
    fields: [
      { label: "Study ID", value: study.id },
      { label: "Status", value: study.statusText },
      { label: "Metric", value: study.metricText },
      { label: "Notebook", value: study.isActive ? "Active PlotTool notebook" : "Retained notebook" },
      { label: "Operator note", value: study.noteText }
    ]
  };
}

function resolveSelectedPlotStudyId(
  studies: ResearchPlotStudyItem[],
  selectedStudyId: string | null
): string | null {
  if (studies.length === 0) {
    return null;
  }

  if (selectedStudyId && studies.some((study) => study.id === selectedStudyId)) {
    return selectedStudyId;
  }

  return studies.find((study) => study.isActive)?.id ?? studies[0].id;
}

export function buildPlotToolMomentsTable(
  moments: ResearchPlotMomentRow[]
): ResearchResultTableState<ResearchPlotMomentRow> {
  return {
    rows: moments,
    hasRows: moments.length > 0,
    caption: "PlotTool moments for the active strategy pair.",
    emptyText: "No PlotTool moments are available for the active strategy context."
  };
}

export function buildPlotToolSampleTable(
  samples: ResearchPlotSampleRow[]
): ResearchResultTableState<ResearchPlotSampleRow> {
  return {
    rows: samples,
    hasRows: samples.length > 0,
    caption: "Recent PlotTool observations with spread, implied volatility, z-score, and signal.",
    emptyText: "No PlotTool observation rows are available for the active strategy context."
  };
}

export function buildComparisonTable(
  comparison: RunComparisonRow[],
  selectedRunId: string | null = null
): ResearchResultTableState<ResearchComparisonTableRow> {
  const resolvedSelectedRunId = resolveSelectedComparisonRunId(comparison, selectedRunId);
  return {
    rows: comparison.map((row) => buildComparisonRow(row, resolvedSelectedRunId, RESEARCH_COMPARISON_DETAIL_PANEL_ID)),
    hasRows: comparison.length > 0,
    caption: "Run comparison evidence returned by the workstation API.",
    emptyText: "No comparison rows returned for the selected pair."
  };
}

export function buildComparisonRow(
  row: RunComparisonRow,
  selectedRunId: string | null = null,
  detailPanelId: string = RESEARCH_COMPARISON_DETAIL_PANEL_ID
): ResearchComparisonTableRow {
  const strategyName = formatText(row.strategyName);
  const modeText = titleCase(formatText(row.mode));
  const statusText = formatText(row.status);
  const netPnlText = formatMoney(row.netPnl, true);
  const totalReturnText = formatSignedPercent(row.totalReturn);
  const maxDrawdownText = formatSignedPercent(row.maxDrawdown);
  const sharpeRatioText = formatNullableNumber(row.sharpeRatio, 3);
  const fillCountText = Number.isFinite(row.fillCount) ? row.fillCount.toLocaleString() : "Unavailable";
  const promotionStateText = formatPromotionState(row.promotionState);
  const evidenceText = buildComparisonEvidenceText(row);

  return {
    runId: row.runId,
    strategyName,
    equityText: `Equity ${formatMoney(row.finalEquity)}`,
    modeText,
    modeBadgeVariant: badgeVariantForMode(row.mode),
    statusText,
    statusBadgeVariant: badgeVariantForStatus(row.status),
    netPnlText,
    netPnlTone: toneForSignedValue(row.netPnl),
    totalReturnText,
    totalReturnTone: toneForSignedValue(row.totalReturn),
    maxDrawdownText,
    maxDrawdownTone: toneForDrawdown(row.maxDrawdown),
    sharpeRatioText,
    fillCountText,
    promotionStateText,
    evidenceText,
    detailPanelId,
    detailExpanded: selectedRunId === row.runId,
    rowSelectAriaLabel: `Inspect ${strategyName} comparison evidence`,
    ariaLabel: `${strategyName}: ${statusText}; net P&L ${netPnlText}; return ${totalReturnText}; promotion ${promotionStateText}; ${evidenceText}.`
  };
}

export function buildComparisonDetail(
  row: ResearchComparisonTableRow,
  panelId: string = RESEARCH_COMPARISON_DETAIL_PANEL_ID
): ResearchComparisonDetailState {
  return {
    id: row.runId,
    panelId,
    ariaLabel: `Selected comparison evidence for ${row.strategyName}`,
    eyebrow: "Comparison evidence",
    title: row.strategyName,
    subtitle: row.runId,
    description: `${row.statusText} ${row.modeText} run with ${row.promotionStateText.toLowerCase()} posture.`,
    statusLabel: row.statusText,
    statusVariant: row.statusBadgeVariant,
    fields: [
      { label: "Mode", value: row.modeText },
      { label: "Final equity", value: row.equityText.replace(/^Equity\s+/, "") },
      { label: "Net P&L", value: row.netPnlText },
      { label: "Return", value: row.totalReturnText },
      { label: "Max drawdown", value: row.maxDrawdownText },
      { label: "Sharpe", value: row.sharpeRatioText },
      { label: "Fills", value: row.fillCountText },
      { label: "Promotion", value: row.promotionStateText },
      { label: "Evidence", value: row.evidenceText }
    ]
  };
}

export function buildDiffPanel(runDiff: RunDiff | null): ResearchDiffPanelState {
  if (!runDiff) {
    return {
      title: "Position & parameter diff",
      description: "No run diff has been loaded for the selected pair.",
      ariaLabel: "Strategy run diff result is empty",
      summaryLabel: "Run diff metric summary",
      metrics: [],
      positionChanges: [],
      parameterChanges: [],
      positionSectionLabel: "Position changes",
      parameterSectionLabel: "Parameter changes",
      positionListLabel: "No position diff rows",
      parameterListLabel: "No parameter diff rows",
      hasPositionChanges: false,
      hasParameterChanges: false,
      positionEmptyText: "No position diff result is available.",
      parameterEmptyText: "No parameter diff result is available."
    };
  }

  const positionChanges = [
    ...runDiff.addedPositions,
    ...runDiff.removedPositions,
    ...runDiff.modifiedPositions
  ].map(buildPositionDiffRow);
  const parameterChanges = runDiff.parameterChanges.map(buildParameterDiffRow);

  return {
    title: "Position & parameter diff",
    description: `${runDiff.baseStrategyName} compared with ${runDiff.targetStrategyName}.`,
    ariaLabel: `Strategy run diff for ${runDiff.baseStrategyName} and ${runDiff.targetStrategyName}`,
    summaryLabel: "Run diff metric summary",
    metrics: buildDiffMetricRows(runDiff.metrics),
    positionChanges,
    parameterChanges,
    positionSectionLabel: `${positionChanges.length} position ${positionChanges.length === 1 ? "change" : "changes"} returned`,
    parameterSectionLabel: `${parameterChanges.length} parameter ${parameterChanges.length === 1 ? "change" : "changes"} returned`,
    positionListLabel: "Position diff rows",
    parameterListLabel: "Parameter diff rows",
    hasPositionChanges: positionChanges.length > 0,
    hasParameterChanges: parameterChanges.length > 0,
    positionEmptyText: "No position changes returned for this diff.",
    parameterEmptyText: "No parameter changes returned for this diff."
  };
}

function buildDiffMetricRows(metrics: MetricsDiff): ResearchDiffMetricRow[] {
  const netPnlValue = formatMoney(metrics.netPnlDelta, true);
  const returnValue = formatSignedPercent(metrics.totalReturnDelta);
  const fillValue = formatSignedCount(metrics.fillCountDelta);

  return [
    {
      id: "net-pnl-delta",
      label: "Net P&L delta",
      value: netPnlValue,
      tone: toneForSignedValue(metrics.netPnlDelta),
      ariaLabel: `Net P&L delta ${netPnlValue}. Base ${formatMoney(metrics.baseNetPnl)}. Target ${formatMoney(metrics.targetNetPnl)}.`
    },
    {
      id: "return-delta",
      label: "Return delta",
      value: returnValue,
      tone: toneForSignedValue(metrics.totalReturnDelta),
      ariaLabel: `Return delta ${returnValue}. Base ${formatSignedPercent(metrics.baseTotalReturn)}. Target ${formatSignedPercent(metrics.targetTotalReturn)}.`
    },
    {
      id: "fill-delta",
      label: "Fill delta",
      value: fillValue,
      tone: toneForSignedValue(metrics.fillCountDelta),
      ariaLabel: `Fill count delta ${fillValue}.`
    }
  ];
}

function buildPositionDiffRow(item: PositionDiffEntry): ResearchDiffChangeRow {
  const symbolText = formatText(item.symbol);
  const changeTypeText = formatText(item.changeType);
  const quantityDelta = item.targetQuantity - item.baseQuantity;
  const pnlDelta = item.targetPnl - item.basePnl;
  const quantityText = `Qty ${formatSignedCount(quantityDelta)}`;
  const pnlText = `P&L ${formatMoney(pnlDelta, true)}`;

  return {
    key: `${symbolText}-${changeTypeText}`,
    symbolText,
    changeTypeText,
    quantityText,
    pnlText,
    text: `${symbolText} ${changeTypeText}`,
    badgeVariant: badgeVariantForPositionChange(item.changeType),
    ariaLabel: `${symbolText} ${changeTypeText}. ${quantityText}. ${pnlText}.`
  };
}

function buildParameterDiffRow(item: ParameterDiff): ResearchParameterChangeRow {
  const key = formatText(item.key);
  const baseValueText = formatText(item.baseValue);
  const targetValueText = formatText(item.targetValue);
  const valueText = `${baseValueText} -> ${targetValueText}`;

  return {
    key,
    baseValueText,
    targetValueText,
    valueText,
    ariaLabel: `${key} changed from ${baseValueText} to ${targetValueText}.`
  };
}

function badgeVariantForPositionChange(changeType: PositionDiffEntry["changeType"]): ResearchDiffBadgeVariant {
  if (changeType === "Added") {
    return "success";
  }

  if (changeType === "Removed") {
    return "danger";
  }

  return "warning";
}

export function buildPromotionHistoryTable(
  promotionHistory: PromotionRecord[],
  selectedPromotionId = resolveSelectedPromotionHistoryId(promotionHistory, null)
): ResearchResultTableState<ResearchPromotionHistoryRow> {
  return {
    rows: promotionHistory.map((record) => buildPromotionHistoryRow(record, selectedPromotionId)),
    hasRows: promotionHistory.length > 0,
    caption: "Promotion history decisions returned for Strategy runs. Select a row to inspect gate evidence.",
    emptyText: "No promotion history records returned."
  };
}

function buildPromotionHistoryRow(
  record: PromotionRecord,
  selectedPromotionId: string | null
): ResearchPromotionHistoryRow {
  const strategyName = formatText(record.strategyName);
  const strategyIdText = formatText(record.strategyId);
  const source = formatText(record.sourceRunType);
  const target = formatText(record.targetRunType);
  const routeText = `${source} to ${target}`;
  const decisionText = formatPromotionDecision(record);
  const qualifyingSharpeText = formatNullableNumber(record.qualifyingSharpe, 3);
  const qualifyingMaxDrawdownText = formatPercent(record.qualifyingMaxDrawdownPercent, 1);
  const qualifyingTotalReturnText = formatPercent(record.qualifyingTotalReturn, 1);
  const promotedAtText = formatText(record.promotedAt);
  const detailExpanded = record.promotionId === selectedPromotionId;

  return {
    promotionId: record.promotionId,
    strategyIdText,
    strategyName,
    qualifyingSharpeText,
    qualifyingMaxDrawdownText,
    qualifyingTotalReturnText,
    routeText,
    decisionText,
    promotedAtText,
    detailPanelId: RESEARCH_PROMOTION_HISTORY_DETAIL_PANEL_ID,
    detailExpanded,
    ariaLabel: `${strategyName}: ${routeText}; decision ${decisionText}; Sharpe ${qualifyingSharpeText}; max drawdown ${qualifyingMaxDrawdownText}; return ${qualifyingTotalReturnText}; promoted ${promotedAtText}.`,
    rowSelectAriaLabel: `Inspect ${strategyName} promotion decision`,
    raw: record
  };
}

export function buildPromotionHistoryDetail(
  record: PromotionRecord,
  panelId = RESEARCH_PROMOTION_HISTORY_DETAIL_PANEL_ID
): ResearchPromotionHistoryDetailState {
  const strategyName = formatText(record.strategyName);
  const source = formatText(record.sourceRunType);
  const target = formatText(record.targetRunType);
  const decisionText = formatPromotionDecision(record);
  const approvedBy = formatText(record.approvedBy);
  const auditReference = formatText(record.auditReference);
  const reason = formatOptionalPromotionReason(record.approvalReason ?? record.reviewNotes ?? null);

  return {
    id: record.promotionId,
    panelId,
    ariaLabel: `Selected promotion decision detail for ${strategyName}`,
    eyebrow: "Selected promotion",
    title: strategyName,
    subtitle: `${source} to ${target} - ${formatText(record.promotedAt)}`,
    description: `${decisionText} promotion decision with ${reason}`,
    statusLabel: target.toUpperCase(),
    statusVariant: badgeVariantForPromotionTarget(record.targetRunType),
    fields: [
      { label: "Promotion ID", value: formatText(record.promotionId) },
      { label: "Strategy ID", value: formatText(record.strategyId) },
      { label: "Source run", value: formatText(record.sourceRunId ?? record.runId ?? null) },
      { label: "Target run", value: formatText(record.targetRunId ?? null) },
      { label: "Decision", value: decisionText },
      { label: "Sharpe", value: formatNullableNumber(record.qualifyingSharpe, 3) },
      { label: "Max drawdown", value: formatPercent(record.qualifyingMaxDrawdownPercent, 1) },
      { label: "Total return", value: formatPercent(record.qualifyingTotalReturn, 1) },
      { label: "Approved by", value: approvedBy },
      { label: "Audit ref", value: auditReference }
    ]
  };
}

function resolveSelectedPromotionHistoryId(
  promotionHistory: PromotionRecord[],
  selectedPromotionId: string | null
): string | null {
  if (promotionHistory.length === 0) {
    return null;
  }

  if (selectedPromotionId && promotionHistory.some((record) => record.promotionId === selectedPromotionId)) {
    return selectedPromotionId;
  }

  return promotionHistory[0].promotionId;
}

function resolveSelectedComparisonRunId(
  comparison: RunComparisonRow[],
  selectedRunId: string | null
): string | null {
  if (comparison.length === 0) {
    return null;
  }

  if (selectedRunId && comparison.some((row) => row.runId === selectedRunId)) {
    return selectedRunId;
  }

  return comparison[0].runId;
}

function formatPromotionDecision(record: PromotionRecord): string {
  const decision = record.decision?.trim();
  if (decision) {
    return decision;
  }

  const target = record.targetRunType.trim().toLowerCase();
  if (target === "live") {
    return "Approved for live";
  }

  if (target === "paper") {
    return "Approved for paper";
  }

  return "Promotion approved";
}

function badgeVariantForPromotionTarget(targetRunType: string): ResearchPromotionHistoryBadgeVariant {
  const target = targetRunType.trim().toLowerCase();
  if (target === "live") {
    return "live";
  }

  if (target === "paper") {
    return "paper";
  }

  if (target === "backtest" || target === "research") {
    return "research";
  }

  return "outline";
}

function formatOptionalPromotionReason(value: string | null | undefined): string {
  const trimmed = value?.trim();
  return trimmed && trimmed.length > 0 ? trimmed : "No approval reason was recorded.";
}

export function buildRunDetail(run: ResearchRunRecord): ResearchRunDetailState {
  const title = formatText(run.strategyName);
  const modeLabel = formatText(run.mode).toUpperCase();
  const statusText = formatText(run.status);

  return {
    runId: run.id,
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

export function buildInlineRunDetail(run: ResearchRunRecord, panelId = RESEARCH_RUN_DETAIL_PANEL_ID): ResearchRunInlineDetailState {
  const title = formatText(run.strategyName);
  const modeLabel = formatText(run.mode).toUpperCase();
  const statusText = formatText(run.status);

  return {
    id: run.id,
    panelId,
    ariaLabel: `Selected strategy run detail for ${title}`,
    eyebrow: "Selected run",
    title,
    subtitle: `${formatText(run.engine)} - ${formatText(run.dataset)} - ${formatText(run.window)}`,
    description: `${statusText} ${modeLabel} run retained for compare, diff, promotion, and evidence review.`,
    statusLabel: modeLabel,
    statusVariant: modeBadgeVariantFor(run.mode),
    evidenceAction: buildResearchEvidenceAction(run)!,
    openDetailLabel: `Open ${title} run detail dialog`,
    fields: [
      { id: "run-id", label: "Run ID", value: formatText(run.id) },
      { id: "status", label: "Status", value: statusText },
      { id: "pnl", label: "P&L", value: formatText(run.pnl) },
      { id: "sharpe", label: "Sharpe", value: formatText(run.sharpe) },
      { id: "updated", label: "Updated", value: formatText(run.lastUpdated) },
      { id: "notes", label: "Notes", value: formatOptionalNotes(run.notes) }
    ]
  };
}

function buildResearchEvidenceAction(run: ResearchRunRecord | null): ResearchEvidenceAction | null {
  if (!run) {
    return null;
  }

  return {
    label: "Evidence packet",
    href: evidenceWorkbenchPath("strategy-run", run.id),
    ariaLabel: `Open ${run.strategyName} evidence packet`
  };
}

function resolveInspectedRunId(
  runs: ResearchRunRecord[],
  requestedId: string | null,
  selectedRuns: ResearchRunRecord[]
): string | null {
  if (requestedId && runs.some((run) => run.id === requestedId)) {
    return requestedId;
  }

  return selectedRuns[0]?.id ?? runs[0]?.id ?? null;
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

function buildPlotExpression(
  activeRun: ResearchRunRecord | null,
  companionRun: ResearchRunRecord | null
): string {
  const primaryKey = sanitizeDomId(activeRun?.strategyName ?? "plottool").replace(/-/g, "_");
  const companionKey = companionRun
    ? sanitizeDomId(companionRun.strategyName).replace(/-/g, "_")
    : null;

  if (companionKey) {
    return `${primaryKey}.spread() vs ${companionKey}.implied_volatility(3m, forward, 100)`;
  }

  return `${primaryKey}.spread() vs ${primaryKey}.implied_volatility(3m, forward, 100)`;
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

function formatMoney(value: number | null | undefined, signed = false): string {
  if (typeof value !== "number" || !Number.isFinite(value)) {
    return "Unavailable";
  }

  const amount = currencyFormatter.format(Math.abs(value));

  if (!signed) {
    return value < 0 ? `-${amount}` : amount;
  }

  if (value > 0) {
    return `+${amount}`;
  }

  if (value < 0) {
    return `-${amount}`;
  }

  return amount;
}

function formatSignedCount(value: number | null | undefined): string {
  if (typeof value !== "number" || !Number.isFinite(value)) {
    return "Unavailable";
  }

  const sign = value > 0 ? "+" : "";
  return `${sign}${value.toLocaleString()}`;
}

function formatSignedPercent(value: number | null | undefined): string {
  if (typeof value !== "number" || !Number.isFinite(value)) {
    return "Unavailable";
  }

  const formatted = `${Math.abs(value * 100).toFixed(2)}%`;
  if (value > 0) {
    return `+${formatted}`;
  }

  if (value < 0) {
    return `-${formatted}`;
  }

  return formatted;
}

function formatPromotionState(value: string | null | undefined): string {
  const text = formatText(value);
  if (text === "Unavailable") {
    return text;
  }

  const normalized = text
    .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
    .replace(/[_-]+/g, " ")
    .replace(/\s+/g, " ")
    .trim();

  return normalized
    ? `${normalized.charAt(0).toUpperCase()}${normalized.slice(1).toLowerCase()}`
    : "Unavailable";
}

function parseDecimalToken(value: string | null | undefined): number | null {
  if (!value) {
    return null;
  }

  const normalized = value.replace(/[^0-9.+-]/g, "");
  if (!normalized) {
    return null;
  }

  const parsed = Number.parseFloat(normalized);
  return Number.isFinite(parsed) ? parsed : null;
}

function parsePercentToken(value: string | null | undefined): number | null {
  const parsed = parseDecimalToken(value);
  return parsed === null ? null : parsed / 100;
}

function buildComparisonEvidenceText(row: RunComparisonRow): string {
  const ledgerText = row.hasLedger ? "Ledger linked" : "Ledger missing";
  const auditText = row.hasAuditTrail ? "Audit linked" : "Audit missing";
  return `${ledgerText}; ${auditText}`;
}

function ensurePlotToolWorkspaceChartState(workspace: ResearchPlotWorkspaceState): ResearchPlotWorkspaceState {
  if (workspace.scatterChart) {
    return workspace;
  }

  const points = workspace.points?.length ? workspace.points : plotToolScatterPoints;
  const markerPoint = findLastPlotPoint(points, (point) => point.emphasis) ?? points[points.length - 1] ?? plotToolScatterPoints[0];
  const focusPoint = workspace.focusPoint ?? {
    label: "Current marker",
    xValueText: "Unavailable",
    yValueText: "Unavailable",
    detail: `Selected PlotTool marker at x ${markerPoint.x}, y ${markerPoint.y}.`
  };

  return {
    ...workspace,
    xTicks: workspace.xTicks ?? plotToolXTicks,
    yTicks: workspace.yTicks ?? plotToolYTicks,
    xAxisLabel: workspace.xAxisLabel ?? "Spread (bps)",
    yAxisLabel: workspace.yAxisLabel ?? "3m implied vol",
    points,
    focusPoint,
    scatterChart: buildPlotToolScatterChartState({
      points,
      xTicks: workspace.xTicks ?? plotToolXTicks,
      yTicks: workspace.yTicks ?? plotToolYTicks,
      xAxisLabel: workspace.xAxisLabel ?? "Spread (bps)",
      yAxisLabel: workspace.yAxisLabel ?? "3m implied vol",
      markerPoint,
      focusPoint,
      chartStudyLabel: workspace.title ?? "PlotTool"
    })
  };
}

function ensurePlotToolStatisticsChartState(statistics: ResearchPlotStatisticsState): ResearchPlotStatisticsState {
  if (statistics.distributionChart) {
    return statistics;
  }

  const distributionBars = statistics.distributionBars?.length ? statistics.distributionBars : plotToolDistributionBars;

  return {
    ...statistics,
    distributionBars,
    distributionChart: buildPlotToolDistributionChartState(distributionBars)
  };
}

function buildPlotToolScatterChartState({
  points,
  xTicks,
  yTicks,
  xAxisLabel,
  yAxisLabel,
  markerPoint,
  focusPoint,
  chartStudyLabel
}: {
  points: ResearchPlotScatterPoint[];
  xTicks: ResearchPlotAxisTick[];
  yTicks: ResearchPlotAxisTick[];
  xAxisLabel: string;
  yAxisLabel: string;
  markerPoint: ResearchPlotScatterPoint;
  focusPoint: ResearchPlotFocusPointState;
  chartStudyLabel: string;
}): ResearchPlotScatterChartState {
  const labelX = Math.min(markerPoint.x + 10, 512);
  const labelY = Math.max(markerPoint.y - 26, 52);
  const labelText = `${focusPoint.xValueText}, ${focusPoint.yValueText}`;

  return {
    ariaLabel: `${chartStudyLabel} PlotTool scatter chart. Current marker ${labelText}.`,
    titleId: "plottool-scatter-title",
    descriptionId: "plottool-scatter-description",
    title: `${chartStudyLabel} scatter`,
    description: `${xAxisLabel} against ${yAxisLabel}. ${focusPoint.detail}`,
    viewBox: "0 0 640 320",
    gridLines: buildPlotToolGridLines(),
    xTicks,
    yTicks,
    xAxisLabel,
    yAxisLabel,
    trendLine: {
      points: "90,250 150,222 210,198 270,170 330,142 390,114 450,88 510,66 564,54",
      stroke: "var(--chart-up)",
      strokeWidth: 2,
      strokeDasharray: "5 4"
    },
    points: points.map((point, index) => ({
      id: `plot-point-${index}`,
      x: point.x,
      y: point.y,
      radius: point.emphasis ? 5 : 3.25,
      fill: point.emphasis ? "var(--chart-up)" : "var(--chart-ma20)",
      fillOpacity: point.emphasis ? 0.95 : 0.65,
      ariaLabel: `${point.emphasis ? "Emphasized" : "History"} observation ${index + 1}, x ${point.x}, y ${point.y}`
    })),
    marker: {
      x: markerPoint.x,
      y: markerPoint.y,
      radius: 6,
      fill: "var(--state-warn-fg)",
      stroke: "var(--surface-topbar)",
      strokeWidth: 2,
      labelX,
      labelY,
      labelTextX: labelX + 8,
      labelTextY: labelY + 14,
      labelWidth: 96,
      labelHeight: 22,
      labelRadius: 4,
      labelFill: "var(--surface-input)",
      labelStroke: "var(--state-warn-fg)",
      labelText,
      verticalGuide: {
        id: "current-marker-x",
        x1: markerPoint.x,
        y1: 40,
        x2: markerPoint.x,
        y2: 290,
        stroke: "var(--state-warn-fg)",
        strokeWidth: 1,
        strokeDasharray: "4 4",
        opacity: 0.55
      },
      horizontalGuide: {
        id: "current-marker-y",
        x1: 50,
        y1: markerPoint.y,
        x2: 610,
        y2: markerPoint.y,
        stroke: "var(--state-warn-fg)",
        strokeWidth: 1,
        strokeDasharray: "4 4",
        opacity: 0.55
      }
    }
  };
}

function buildPlotToolGridLines(): ResearchPlotChartLine[] {
  const horizontal = [40, 90, 140, 190, 240, 290].map((y) => ({
    id: `h-${y}`,
    x1: 50,
    y1: y,
    x2: 610,
    y2: y,
    stroke: y === 290 ? "var(--chart-grid-major)" : "var(--chart-grid)",
    strokeWidth: 1
  }));
  const vertical = [50, 130, 210, 290, 370, 450, 530, 610].map((x) => ({
    id: `v-${x}`,
    x1: x,
    y1: 40,
    x2: x,
    y2: 290,
    stroke: x === 50 ? "var(--chart-grid-major)" : "var(--chart-grid)",
    strokeWidth: 1
  }));

  return [...horizontal, ...vertical];
}

function buildPlotToolDistributionChartState(bars: number[]): ResearchPlotDistributionChartState {
  return {
    ariaLabel: "PlotTool residual distribution chart",
    bars: bars.map((bar, index) => ({
      id: `distribution-bar-${index}`,
      heightPercent: Math.max(bar, 4),
      tone: index >= 8 && index <= 14 ? "selected" : "base",
      ariaLabel: `Distribution bucket ${index + 1}: ${bar}% density`
    }))
  };
}

const plotToolXTicks: ResearchPlotAxisTick[] = [
  { value: 40, label: "20" },
  { value: 120, label: "40" },
  { value: 200, label: "60" },
  { value: 280, label: "80" },
  { value: 360, label: "100" },
  { value: 440, label: "140" },
  { value: 520, label: "180" },
  { value: 600, label: "200" }
];

const plotToolYTicks: ResearchPlotAxisTick[] = [
  { value: 44, label: "120" },
  { value: 98, label: "100" },
  { value: 152, label: "80" },
  { value: 206, label: "60" },
  { value: 260, label: "40" }
];

const plotToolScatterPoints: ResearchPlotScatterPoint[] = [
  { x: 90, y: 250, emphasis: false },
  { x: 105, y: 244, emphasis: false },
  { x: 118, y: 238, emphasis: false },
  { x: 132, y: 232, emphasis: true },
  { x: 148, y: 224, emphasis: false },
  { x: 164, y: 216, emphasis: false },
  { x: 180, y: 208, emphasis: false },
  { x: 196, y: 200, emphasis: false },
  { x: 214, y: 192, emphasis: true },
  { x: 230, y: 184, emphasis: false },
  { x: 246, y: 176, emphasis: false },
  { x: 264, y: 168, emphasis: false },
  { x: 282, y: 160, emphasis: false },
  { x: 300, y: 152, emphasis: true },
  { x: 318, y: 144, emphasis: false },
  { x: 336, y: 136, emphasis: false },
  { x: 356, y: 128, emphasis: false },
  { x: 374, y: 120, emphasis: false },
  { x: 394, y: 112, emphasis: true },
  { x: 414, y: 104, emphasis: false },
  { x: 434, y: 96, emphasis: false },
  { x: 456, y: 88, emphasis: false },
  { x: 478, y: 80, emphasis: false },
  { x: 500, y: 74, emphasis: true },
  { x: 520, y: 68, emphasis: false },
  { x: 542, y: 62, emphasis: false },
  { x: 564, y: 56, emphasis: false }
];

const plotToolDistributionBars = [12, 20, 28, 39, 50, 64, 75, 86, 94, 100, 96, 89, 80, 69, 57, 46, 36, 28, 22, 18, 14, 11, 8, 5];

const plotToolSampleRows: ResearchPlotSampleRow[] = [
  {
    id: "row-1",
    timestamp: "2026-04-25",
    spreadText: "88.40",
    impliedVolText: "73.80",
    zScoreText: "+1.14",
    signalText: "Crowded vol",
    tone: "warning"
  },
  {
    id: "row-2",
    timestamp: "2026-04-24",
    spreadText: "82.10",
    impliedVolText: "69.20",
    zScoreText: "+0.74",
    signalText: "Carry stable",
    tone: "success"
  },
  {
    id: "row-3",
    timestamp: "2026-04-23",
    spreadText: "77.40",
    impliedVolText: "64.50",
    zScoreText: "+0.32",
    signalText: "Neutral",
    tone: "default"
  },
  {
    id: "row-4",
    timestamp: "2026-04-22",
    spreadText: "69.90",
    impliedVolText: "59.10",
    zScoreText: "-0.18",
    signalText: "Compression",
    tone: "default"
  },
  {
    id: "row-5",
    timestamp: "2026-04-21",
    spreadText: "62.80",
    impliedVolText: "53.40",
    zScoreText: "-0.74",
    signalText: "Mean reversion",
    tone: "success"
  }
];

function badgeVariantForMode(mode: string | null | undefined): ResearchComparisonBadgeVariant {
  const normalized = mode?.trim().toLowerCase();
  if (normalized === "paper") {
    return "paper";
  }

  if (normalized === "live") {
    return "live";
  }

  if (normalized === "backtest" || normalized === "research") {
    return "research";
  }

  return "outline";
}

function badgeVariantForStatus(status: string | null | undefined): ResearchComparisonBadgeVariant {
  const normalized = status?.trim().toLowerCase() ?? "";
  if (normalized.includes("complete") || normalized.includes("ready") || normalized.includes("approved")) {
    return "success";
  }

  if (normalized.includes("fail") || normalized.includes("block") || normalized.includes("reject") || normalized.includes("error")) {
    return "danger";
  }

  if (normalized.includes("review") || normalized.includes("queue") || normalized.includes("running") || normalized.includes("candidate")) {
    return "warning";
  }

  return "outline";
}

function toneForSignedValue(value: number | null | undefined): ResearchComparisonValueTone {
  if (typeof value !== "number" || !Number.isFinite(value) || value === 0) {
    return "muted";
  }

  return value > 0 ? "success" : "danger";
}

function toneForDrawdown(value: number | null | undefined): ResearchComparisonValueTone {
  if (typeof value !== "number" || !Number.isFinite(value) || value === 0) {
    return "muted";
  }

  return value < 0 ? "danger" : "success";
}

function titleCase(value: string): string {
  if (value === "Unavailable") {
    return value;
  }

  return value
    .replace(/[_-]+/g, " ")
    .replace(/\s+/g, " ")
    .trim()
    .replace(/\w\S*/g, (word) => `${word.charAt(0).toUpperCase()}${word.slice(1).toLowerCase()}`);
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

function findLastPlotPoint<T>(items: T[], predicate: (item: T) => boolean): T | undefined {
  for (let index = items.length - 1; index >= 0; index -= 1) {
    if (predicate(items[index])) {
      return items[index];
    }
  }

  return undefined;
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
