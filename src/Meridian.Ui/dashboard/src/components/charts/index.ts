// Meridian "Concrete" chart primitives — SVG-based, responsive, token-driven (with hex fallbacks).
// Charts flex-fill their container: give the wrapper a definite height (see ChartCard).
export { ChartCard } from "./ChartCard";
export type { ChartCardProps, ChartReadoutItem } from "./ChartCard";

export { CandleChart } from "./CandleChart";
export type { CandleChartProps, Candle, CandleOverlay } from "./CandleChart";

export { EquityCurve } from "./EquityCurve";
export type { EquityCurveProps, EquitySeries } from "./EquityCurve";

export { DrawdownChart } from "./DrawdownChart";
export type { DrawdownChartProps } from "./DrawdownChart";

export { Histogram } from "./Histogram";
export type { HistogramProps, HistogramBin } from "./Histogram";

export { DepthChart } from "./DepthChart";
export type { DepthChartProps, DepthLevel } from "./DepthChart";

export { CorrelationHeatmap } from "./CorrelationHeatmap";
export type { CorrelationHeatmapProps } from "./CorrelationHeatmap";

export { Sparkline } from "./Sparkline";
export type { SparklineProps } from "./Sparkline";
