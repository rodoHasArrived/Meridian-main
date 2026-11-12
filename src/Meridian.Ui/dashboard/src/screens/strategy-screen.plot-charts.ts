import type {
  StrategyPlotFocusPointState,
  StrategyPlotSampleRow,
  StrategyPlotStatisticsState,
  StrategyPlotWorkspaceState
} from "./strategy-screen.view-model";

/** Pure PlotTool chart geometry, accessibility descriptions and illustrative fixture ownership. */
export interface StrategyPlotAxisTick {
  value: number;
  label: string;
}

export interface StrategyPlotScatterPoint {
  x: number;
  y: number;
  emphasis: boolean;
}

export interface StrategyPlotScatterChartState {
  ariaLabel: string;
  titleId: string;
  descriptionId: string;
  title: string;
  description: string;
  viewBox: string;
  gridLines: StrategyPlotChartLine[];
  xTicks: StrategyPlotAxisTick[];
  yTicks: StrategyPlotAxisTick[];
  xAxisLabel: string;
  yAxisLabel: string;
  trendLine: StrategyPlotTrendLineState;
  points: StrategyPlotScatterPointMark[];
  marker: StrategyPlotMarkerState;
}

export interface StrategyPlotChartLine {
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

export interface StrategyPlotTrendLineState {
  points: string;
  stroke: string;
  strokeWidth: number;
  strokeDasharray: string;
}

export interface StrategyPlotScatterPointMark {
  id: string;
  x: number;
  y: number;
  radius: number;
  fill: string;
  fillOpacity: number;
  ariaLabel: string;
}

export interface StrategyPlotMarkerState {
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
  verticalGuide: StrategyPlotChartLine;
  horizontalGuide: StrategyPlotChartLine;
}

export interface StrategyPlotDistributionChartState {
  ariaLabel: string;
  bars: StrategyPlotDistributionBar[];
}

export interface StrategyPlotDistributionBar {
  id: string;
  heightPercent: number;
  tone: "selected" | "base";
  ariaLabel: string;
}

export function ensurePlotToolWorkspaceChartState(workspace: StrategyPlotWorkspaceState): StrategyPlotWorkspaceState {
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

export function ensurePlotToolStatisticsChartState(statistics: StrategyPlotStatisticsState): StrategyPlotStatisticsState {
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

export function buildPlotToolScatterChartState({
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

export function buildPlotToolDistributionChartState(bars: number[]): StrategyPlotDistributionChartState {
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

export const plotToolXTicks: StrategyPlotAxisTick[] = [
  { value: 40, label: "20" },
  { value: 120, label: "40" },
  { value: 200, label: "60" },
  { value: 280, label: "80" },
  { value: 360, label: "100" },
  { value: 440, label: "140" },
  { value: 520, label: "180" },
  { value: 600, label: "200" }
];

export const plotToolYTicks: StrategyPlotAxisTick[] = [
  { value: 44, label: "120" },
  { value: 98, label: "100" },
  { value: 152, label: "80" },
  { value: 206, label: "60" },
  { value: 260, label: "40" }
];

export const plotToolScatterPoints: StrategyPlotScatterPoint[] = [
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

export const plotToolDistributionBars = [12, 20, 28, 39, 50, 64, 75, 86, 94, 100, 96, 89, 80, 69, 57, 46, 36, 28, 22, 18, 14, 11, 8, 5];

export const plotToolSampleRows: StrategyPlotSampleRow[] = [
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

export function findLastPlotPoint<T>(items: T[], predicate: (item: T) => boolean): T | undefined {
  for (let index = items.length - 1; index >= 0; index -= 1) {
    if (predicate(items[index])) {
      return items[index];
    }
  }

  return undefined;
}
