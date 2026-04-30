import { useCallback, useMemo, useState } from "react";
import * as workstationApi from "@/lib/api";
import type {
  MetricsDiff,
  ParameterDiff,
  PositionDiffEntry,
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
  ariaLabel: string;
}

export type ResearchComparisonBadgeVariant = "outline" | "success" | "warning" | "danger" | "paper" | "live" | "research";
export type ResearchComparisonValueTone = "success" | "danger" | "muted";

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
    rows: comparison.map(buildComparisonRow),
    hasRows: comparison.length > 0,
    caption: "Run comparison evidence returned by the workstation API.",
    emptyText: "No comparison rows returned for the selected pair."
  };
}

export function buildComparisonRow(row: RunComparisonRow): ResearchComparisonTableRow {
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
    ariaLabel: `${strategyName}: ${statusText}; net P&L ${netPnlText}; return ${totalReturnText}; promotion ${promotionStateText}; ${evidenceText}.`
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

function buildComparisonEvidenceText(row: RunComparisonRow): string {
  const ledgerText = row.hasLedger ? "Ledger linked" : "Ledger missing";
  const auditText = row.hasAuditTrail ? "Audit linked" : "Audit missing";
  return `${ledgerText}; ${auditText}`;
}

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
