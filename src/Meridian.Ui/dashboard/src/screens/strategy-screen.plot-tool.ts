import type { MetricSnapshot, RunComparisonRow, RunDiff, StrategyPlotToolPayload, StrategyRunRecord } from "@/types";
import { formatCount as formatCountValue, formatNumber } from "@/lib/format";
import {
  badgeVariantForMode,
  buildComparisonEvidenceText,
  formatMoney,
  formatNullableNumber,
  formatOptionalNotes,
  formatPromotionState,
  formatSignedPercent,
  formatText,
  parseDecimalToken,
  parsePercentToken,
  sanitizeDomId
} from "@/screens/strategy-screen.formatters";
import type {
  StrategyPlotAxisTick,
  StrategyPlotChartLine,
  StrategyPlotDistributionChartState,
  StrategyPlotFocusPointState,
  StrategyPlotMomentRow,
  StrategyPlotSampleRow,
  StrategyPlotScatterChartState,
  StrategyPlotScatterPoint,
  StrategyPlotStatisticsState,
  StrategyPlotStudyDetailState,
  StrategyPlotStudyItem,
  StrategyPlotToolState,
  StrategyPlotToolTab,
  StrategyPlotToolView,
  StrategyPlotWorkspaceState,
  StrategyResultTableState,
  StrategyToolbarItem
} from "@/screens/strategy-screen.view-model";

// The strategy screen's plot tool: tab navigation, the workspace/statistics state built from the
// API payload or the illustrative fallback, study rows and detail, and chart geometry. Split from
// strategy-screen.view-model.ts, which re-exports the public builders, so callers are unchanged.

// Exactly two decimals without grouping: the same text as toFixed(2) for every finite value.
const TWO_DECIMALS = { minimumFractionDigits: 2, maximumFractionDigits: 2, useGrouping: false } as const;

export const STRATEGY_PLOT_STUDY_DETAIL_PANEL_ID = "plottool-selected-study-detail";

export function buildPlotToolTabs(activeView: StrategyPlotToolView): StrategyPlotToolTab[] {
  const tabs: Array<Pick<StrategyPlotToolTab, "id" | "label" | "tabId" | "panelId">> = [
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

export function plotToolTabIdForView(view: StrategyPlotToolView): string {
  return `plottool-${view}-tab`;
}

export function nextPlotToolViewForKey(
  currentView: StrategyPlotToolView,
  key: string
): StrategyPlotToolView | null {
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

export function buildPlotToolStateFromApiOrFallback(
  plotToolFromApi: StrategyPlotToolPayload | null | undefined
): StrategyPlotToolState {
  if (plotToolFromApi?.workspace && plotToolFromApi.statistics) {
    const workspace = plotToolFromApi.workspace as StrategyPlotWorkspaceState;
    const statistics = plotToolFromApi.statistics as StrategyPlotStatisticsState;
    const status = plotToolFromApi.analyticsEvidenceStatus;
    if (status !== "Retained" && status !== "Illustrative") {
      const empty = buildEmptyPlotToolState();
      const detail = "No retained statistical observations are connected. Run metadata does not establish a scatter history, distribution or regression.";
      return {
        ...empty,
        analyticsEvidenceStatus: status === "Unavailable" ? "Unavailable" : "Unverified",
        studies: Array.isArray(plotToolFromApi.studies) ? plotToolFromApi.studies as StrategyPlotStudyItem[] : [],
        workspace: { ...empty.workspace, title: "Statistical observations unavailable", description: detail,
          studyTableEmptyText: detail, statusBadgeLabel: "ANALYTICS UNAVAILABLE", metaItems: ["Statistical evidence unavailable"] },
        statistics: { ...empty.statistics, title: "Statistical evidence unavailable", description: detail }
      };
    }

    return {
      analyticsEvidenceStatus: status,
      studies: (plotToolFromApi.studies as StrategyPlotStudyItem[]) ?? [],
      workspace: ensurePlotToolWorkspaceChartState(workspace),
      statistics: ensurePlotToolStatisticsChartState(statistics)
    };
  }

  return buildEmptyPlotToolState();
}

function buildEmptyPlotToolState(): StrategyPlotToolState {
  const xAxisLabel = "Spread";
  const yAxisLabel = "Implied volatility";
  const focusPoint: StrategyPlotFocusPointState = {
    label: "No marker",
    xValueText: "N/A",
    yValueText: "N/A",
    detail: "Connect strategy analytics before inspecting scatter observations."
  };

  return {
    analyticsEvidenceStatus: "Unavailable",
    studies: [],
    workspace: {
      eyebrow: "Strategy · PlotTool",
      title: "PlotTool catalog not connected",
      description: "Strategy analytics are not connected. Connect a strategy analytics source to inspect notebooks, scatter analysis, or statistics.",
      statusBadgeLabel: "NOT CONNECTED",
      statusBadgeVariant: "outline",
      expression: "Strategy analytics not connected",
      toolbarPills: ["No catalog", "No samples", "No overlay", "No diff"],
      notebookToolbarAriaLabel: "Strategy notebook filters",
      notebookToolbarItems: [],
      studyTableEmptyText: "No retained PlotTool studies are available. Connect a governed PlotTool catalog before selecting notebooks.",
      studyTableCaption: "PlotTool notebooks are available after the Strategy endpoint returns retained studies.",
      selectedStudyEmptyText: "No PlotTool study is selected.",
      metaItems: ["Catalog not connected", "0 obs"],
      xAxisLabel,
      yAxisLabel,
      xTicks: [],
      yTicks: [],
      points: [],
      scatterChart: buildPlotToolScatterChartState({
        points: [],
        xTicks: [],
        yTicks: [],
        xAxisLabel,
        yAxisLabel,
        markerPoint: { x: 0, y: 0, emphasis: false },
        focusPoint,
        chartStudyLabel: "Unconnected PlotTool"
      }),
      studySummary: [
        { id: "source", label: "Source", value: "Not connected" },
        { id: "catalog", label: "Formula catalog", value: "Unavailable" },
        { id: "samples", label: "Samples", value: "0" }
      ],
      legendItems: [
        { id: "history", label: "History", detail: "No observations", tone: "muted" },
        { id: "current", label: "Current", detail: "No marker", tone: "muted" },
        { id: "trend", label: "OLS fit", detail: "Unavailable", tone: "muted" }
      ],
      focusPoint,
      signalCards: [
        {
          id: "connection",
          label: "Connection",
          value: "Unavailable",
          detail: "Strategy analytics have not returned any data.",
          tone: "warning"
        }
      ],
      consoleTitle: "Expression editor unavailable",
      consoleBody: "Connect a governed formula catalog before authoring or previewing PlotTool expressions here.",
      overlayTitle: "Meridian overlays",
      overlayItems: [
        "Notebook coverage: no retained PlotTool studies.",
        "Evidence posture: unavailable until strategy analytics are connected.",
        "Diff posture: unavailable until strategy analytics are connected."
      ]
    },
    statistics: {
      eyebrow: "Statistics view",
      title: "PlotTool statistics not connected",
      description: "No PlotTool distribution, regression, or observation sheet is available from the Strategy endpoint.",
      summaryTiles: [],
      distributionBars: [],
      distributionChart: buildPlotToolDistributionChartState([]),
      distributionSummary: "Connect strategy analytics before reviewing residual distributions.",
      distributionFootnote: "No latest observation is available.",
      moments: [],
      momentsTable: buildPlotToolMomentsTable([]),
      regression: {
        equation: "Unavailable",
        detailItems: ["Connect strategy analytics before reviewing regression output."]
      },
      sampleRows: [],
      sampleTable: buildPlotToolSampleTable([])
    }
  };
}

export function buildPlotToolState({
  metrics,
  runs,
  selectedRuns,
  comparison,
  runDiff
}: {
  metrics: MetricSnapshot[];
  runs: StrategyRunRecord[];
  selectedRuns: StrategyRunRecord[];
  comparison: RunComparisonRow[];
  runDiff: RunDiff | null;
}): StrategyPlotToolState {
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
  // Run-linked metrics never fall back to invented literals: a missing value
  // renders "Unavailable" so the PlotTool never shows fabricated performance.
  const parsedSharpe = parseDecimalToken(activeRun?.sharpe);
  const parsedReturn = activeRun?.totalReturn ?? parsePercentToken(activeRun?.pnl);
  const maxDrawdown = comparison[0]?.maxDrawdown ?? null;
  const netPnl = comparison[0]?.netPnl ?? activeRun?.netPnl ?? null;
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
  const focusPointState: StrategyPlotFocusPointState = {
    label: "Current marker",
    xValueText: latestObservation.spreadText,
    yValueText: latestObservation.impliedVolText,
    detail: `${latestObservation.signalText} · highlighted at ${formatText(activeRun?.lastUpdated ?? "2m ago")} · x ${focusPoint.x}, y ${focusPoint.y}`
  };
  const momentRows: StrategyPlotMomentRow[] = [
    { id: "net-pnl", label: "Net P&L", value: formatMoney(netPnl, true), benchmark: "Pair summary" },
    { id: "return", label: "Total return", value: formatSignedPercent(parsedReturn), benchmark: "Run linked" },
    { id: "sharpe", label: "Sharpe ratio", value: formatNullableNumber(parsedSharpe, 3), benchmark: "Operator review" },
    { id: "drawdown", label: "Max drawdown", value: formatSignedPercent(maxDrawdown), benchmark: "Distribution tail" },
    { id: "promotion", label: "Promotion state", value: promotionCue, benchmark: "Strategy posture" },
    { id: "evidence", label: "Evidence pack", value: evidenceCue, benchmark: "Ledger / audit" }
  ];
  const sampleRows = plotToolSampleRows;
  const studyRows: StrategyPlotStudyItem[] = runs.map((run, index) => ({
    id: run.id,
    title: formatText(run.strategyName),
    subtitle: `${formatText(run.dataset)} · ${formatText(run.window)} · ${formatText(run.engine)}`,
    statusText: formatText(run.status),
    statusBadgeLabel: formatText(run.mode).toUpperCase(),
    statusBadgeVariant: badgeVariantForMode(run.mode),
    metricText: `${formatText(run.pnl)} · Sharpe ${formatText(run.sharpe)}`,
    noteText: formatOptionalNotes(run.notes),
    isActive: activeRun ? run.id === activeRun.id : index === 0,
    detailPanelId: STRATEGY_PLOT_STUDY_DETAIL_PANEL_ID,
    detailExpanded: false,
    ariaLabel: `${formatText(run.strategyName)} PlotTool study. ${formatText(run.status)} ${formatText(run.mode)}. ${formatText(run.pnl)} and Sharpe ${formatText(run.sharpe)}.`,
    rowSelectAriaLabel: `Inspect ${formatText(run.strategyName)} PlotTool study detail`
  }));

  return {
    analyticsEvidenceStatus: "Illustrative",
    studies: studyRows,
    workspace: {
      eyebrow: "Strategy · PlotTool",
      title: `${chartStudyLabel} workstation`,
      description: "Meridian notebooks, evidence cues, and factor scatter analysis folded into the Strategy route.",
      statusBadgeLabel: formatText(activeRun?.mode ?? "Strategy").toUpperCase(),
      statusBadgeVariant: badgeVariantForMode(activeRun?.mode ?? "Strategy"),
      expression: buildPlotExpression(activeRun, companionRun),
      toolbarPills: [
        windowName,
        `Daily (${windowName})`,
        companionName ? "Pair overlay" : "Single study",
        runDiff ? "Diff linked" : "0d lag"
      ],
      notebookToolbarAriaLabel: "Strategy notebook filters",
      notebookToolbarItems: buildPlotNotebookToolbarItems(studyRows, activeRun?.id ?? runs[0]?.id ?? null),
      studyTableEmptyText: "No retained PlotTool studies are available.",
      studyTableCaption: "Retained strategy notebooks aligned to the active PlotTool workspace. Select a row to inspect the notebook detail.",
      selectedStudyEmptyText: "No PlotTool study is selected.",
      metaItems: [
        datasetName,
        `${formatCountValue(observationCount)} obs`,
        `β ${formatNumber(beta, TWO_DECIMALS)}`,
        `R² ${formatNumber(rSquared, TWO_DECIMALS)}`,
        `ρ ${formatNumber(correlation, TWO_DECIMALS)}`
      ],
      xAxisLabel,
      yAxisLabel,
      xTicks: plotToolXTicks,
      yTicks: plotToolYTicks,
      points: plotToolScatterPoints,
      scatterChart: buildPlotToolScatterChartState({
        points: plotToolScatterPoints,
        illustrative: true,
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
        { id: "history", label: "History", detail: `${formatCountValue(observationCount)} observations`, tone: "history" },
        { id: "current", label: "Current", detail: `${latestObservation.spreadText} / ${latestObservation.impliedVolText}`, tone: "current" },
        { id: "trend", label: "OLS fit", detail: "y = 0.48x + 39.31", tone: "trend" },
        { id: "refresh", label: "Refresh", detail: formatText(activeRun?.lastUpdated ?? "2m ago"), tone: "muted" }
      ],
      focusPoint: focusPointState,
      signalCards: [
        {
          id: "correlation",
          label: "Correlation",
          value: formatNumber(correlation, TWO_DECIMALS),
          detail: `${studyName} paired with ${datasetName}`,
          tone: "success"
        },
        {
          id: "regression",
          label: "Regression beta",
          value: formatNumber(beta, TWO_DECIMALS),
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
        : "Select a second run to promote this notebook into pair analysis. Strategy keeps compare, diff, and promotion evidence attached to the PlotTool console.",
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
          value: formatCountValue(observationCount),
          detail: "99.7% complete",
          tone: "default"
        },
        {
          id: "mean-y",
          label: "Mean (Y)",
          value: formatNumber(meanY, TWO_DECIMALS),
          detail: "σ̂ ±18.30",
          tone: "default"
        },
        {
          id: "mean-x",
          label: "Mean (X)",
          value: formatNumber(meanX, TWO_DECIMALS),
          detail: "σ̂ ±32.41",
          tone: "default"
        },
        {
          id: "correlation",
          label: "Correlation",
          value: formatNumber(correlation, TWO_DECIMALS),
          detail: "Pearson ρ",
          tone: "success"
        },
        {
          id: "r-squared",
          label: "R²",
          value: formatNumber(rSquared, TWO_DECIMALS),
          detail: "OLS fit",
          tone: "success"
        },
        {
          id: "beta",
          label: "β (slope)",
          value: formatNumber(beta, TWO_DECIMALS),
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
          value: formatNullableNumber(parsedSharpe, 2),
          detail: "Run-linked",
          tone: parsedSharpe === null ? "default" : parsedSharpe >= 1 ? "success" : "warning"
        },
        {
          id: "drawdown",
          label: "Max DD",
          value: formatSignedPercent(maxDrawdown),
          detail: "Recent window",
          tone: maxDrawdown === null ? "default" : maxDrawdown < -0.1 ? "danger" : "warning"
        }
      ],
      distributionBars: plotToolDistributionBars,
      distributionChart: buildPlotToolDistributionChartState(plotToolDistributionBars),
      distributionSummary: `${formatCountValue(observationCount)} samples centered on spread ${formatNumber(meanX, TWO_DECIMALS)} and IV ${formatNumber(meanY, TWO_DECIMALS)}.`,
      distributionFootnote: `Latest observation ${latestObservation.timestamp} · ${latestObservation.signalText} · z-score ${latestObservation.zScoreText}.`,
      moments: momentRows,
      momentsTable: buildPlotToolMomentsTable(momentRows),
      regression: {
        equation: "y = 0.48x + 39.31",
        detailItems: [
          `${chartStudyLabel} stays inside Meridian's evidence-backed strategy workspace.`,
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
  studies: StrategyPlotStudyItem[],
  selectedStudyId: string | null = resolveSelectedPlotStudyId(studies, null)
): StrategyPlotStudyItem[] {
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
      detailPanelId: STRATEGY_PLOT_STUDY_DETAIL_PANEL_ID,
      detailExpanded,
      ariaLabel: `${title} PlotTool study. ${statusText}. ${metricText}.`,
      rowSelectAriaLabel: `Inspect ${title} PlotTool study detail`
    };
  });
}

export function buildPlotStudyDetail(study: StrategyPlotStudyItem): StrategyPlotStudyDetailState {
  return {
    id: study.id,
    panelId: STRATEGY_PLOT_STUDY_DETAIL_PANEL_ID,
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

export function buildPlotNotebookToolbarItems(
  studies: StrategyPlotStudyItem[],
  selectedStudyId: string | null = resolveSelectedPlotStudyId(studies, null)
): StrategyToolbarItem[] {
  const selectedStudy = selectedStudyId
    ? studies.find((study) => study.id === selectedStudyId) ?? null
    : null;

  return [
    { id: "count", label: "Notebook set", value: `${studies.length} retained` },
    { id: "selected", label: "Selected", value: selectedStudy ? formatText(selectedStudy.title) : "None", active: selectedStudy !== null },
    { id: "lane", label: "Workspace", value: "Strategy" }
  ];
}

export function resolveSelectedPlotStudyId(
  studies: StrategyPlotStudyItem[],
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
  moments: StrategyPlotMomentRow[]
): StrategyResultTableState<StrategyPlotMomentRow> {
  return {
    rows: moments,
    hasRows: moments.length > 0,
    caption: "PlotTool moments for the active strategy pair.",
    emptyText: "No PlotTool moments are available for the active strategy context."
  };
}

export function buildPlotToolSampleTable(
  samples: StrategyPlotSampleRow[]
): StrategyResultTableState<StrategyPlotSampleRow> {
  return {
    rows: samples,
    hasRows: samples.length > 0,
    caption: "Recent PlotTool observations with spread, implied volatility, z-score, and signal.",
    emptyText: "No PlotTool observation rows are available for the active strategy context."
  };
}

function buildPlotExpression(
  activeRun: StrategyRunRecord | null,
  companionRun: StrategyRunRecord | null
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

function ensurePlotToolWorkspaceChartState(workspace: StrategyPlotWorkspaceState): StrategyPlotWorkspaceState {
  if (workspace.scatterChart) {
    return workspace;
  }

  const points = workspace.points ?? [];
  const markerPoint = findLastPlotPoint(points, (point) => point.emphasis) ?? points[points.length - 1] ?? { x: 0, y: 0, emphasis: false };
  const focusPoint = workspace.focusPoint ?? {
    label: "Current marker",
    xValueText: "Unavailable",
    yValueText: "Unavailable",
    detail: `Selected PlotTool marker at x ${markerPoint.x}, y ${markerPoint.y}.`
  };

  return {
    ...workspace,
    xTicks: workspace.xTicks ?? [],
    yTicks: workspace.yTicks ?? [],
    xAxisLabel: workspace.xAxisLabel ?? "Spread (bps)",
    yAxisLabel: workspace.yAxisLabel ?? "3m implied vol",
    points,
    focusPoint,
    scatterChart: buildPlotToolScatterChartState({
      points,
      xTicks: workspace.xTicks ?? [],
      yTicks: workspace.yTicks ?? [],
      xAxisLabel: workspace.xAxisLabel ?? "Spread (bps)",
      yAxisLabel: workspace.yAxisLabel ?? "3m implied vol",
      markerPoint,
      focusPoint,
      chartStudyLabel: workspace.title ?? "PlotTool"
    })
  };
}

function ensurePlotToolStatisticsChartState(statistics: StrategyPlotStatisticsState): StrategyPlotStatisticsState {
  if (statistics.distributionChart) {
    return statistics;
  }

  const distributionBars = statistics.distributionBars ?? [];

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
  chartStudyLabel,
  illustrative = false
}: {
  points: StrategyPlotScatterPoint[];
  xTicks: StrategyPlotAxisTick[];
  yTicks: StrategyPlotAxisTick[];
  xAxisLabel: string;
  yAxisLabel: string;
  markerPoint: StrategyPlotScatterPoint;
  focusPoint: StrategyPlotFocusPointState;
  chartStudyLabel: string;
  illustrative?: boolean;
}): StrategyPlotScatterChartState {
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
      points: illustrative ? "90,250 150,222 210,198 270,170 330,142 390,114 450,88 510,66 564,54" : "",
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

function buildPlotToolGridLines(): StrategyPlotChartLine[] {
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

function buildPlotToolDistributionChartState(bars: number[]): StrategyPlotDistributionChartState {
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

const plotToolXTicks: StrategyPlotAxisTick[] = [
  { value: 40, label: "20" },
  { value: 120, label: "40" },
  { value: 200, label: "60" },
  { value: 280, label: "80" },
  { value: 360, label: "100" },
  { value: 440, label: "140" },
  { value: 520, label: "180" },
  { value: 600, label: "200" }
];

const plotToolYTicks: StrategyPlotAxisTick[] = [
  { value: 44, label: "120" },
  { value: 98, label: "100" },
  { value: 152, label: "80" },
  { value: 206, label: "60" },
  { value: 260, label: "40" }
];

const plotToolScatterPoints: StrategyPlotScatterPoint[] = [
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

const plotToolSampleRows: StrategyPlotSampleRow[] = [
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

function findLastPlotPoint<T>(items: T[], predicate: (item: T) => boolean): T | undefined {
  for (let index = items.length - 1; index >= 0; index -= 1) {
    if (predicate(items[index])) {
      return items[index];
    }
  }

  return undefined;
}
