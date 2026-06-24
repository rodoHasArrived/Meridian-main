/**
 * Presentation helpers for rendering report-writer grids: conditional-formatting
 * cell styling and a dependency-free inline chart geometry. Pure and unit-tested
 * so the React table/chart components stay thin.
 */
import type {
  ReportWriterCellStyle,
  ReportWriterChartRender,
  ReportWriterGridRow
} from "@/types";

const CELL_STYLE_CLASSES: Record<Exclude<ReportWriterCellStyle, "None">, string> = {
  Success: "bg-success/10 text-success",
  Warning: "bg-warning/12 text-warning",
  Danger: "bg-danger/10 text-danger",
  // No dedicated "info" palette token in the dashboard theme; primary reads as informational.
  Info: "bg-primary/10 text-primary"
};

export function resolveCellStyle(
  row: Pick<ReportWriterGridRow, "styleHints">,
  columnKey: string
): ReportWriterCellStyle | null {
  const hint = row.styleHints?.[columnKey];
  return hint && hint !== "None" ? hint : null;
}

export function cellStyleClassName(style: ReportWriterCellStyle | null | undefined): string {
  if (!style || style === "None") {
    return "";
  }

  return CELL_STYLE_CLASSES[style];
}

export interface ChartBar {
  seriesKey: string;
  seriesLabel: string;
  value: number;
  /** Magnitude as a 0-100 percentage of the largest absolute value across the chart. */
  widthPct: number;
  isNegative: boolean;
}

export interface ChartCategory {
  category: string;
  bars: ChartBar[];
}

export interface ChartViz {
  categories: ChartCategory[];
  maxAbs: number;
  hasNegative: boolean;
}

/**
 * Normalizes a chart render into bar geometry: one group per category, one bar per
 * series, each sized relative to the largest absolute value in the chart. Works as a
 * compact magnitude view for every chart type the engine emits (bar, line, area, pie).
 */
export function buildChartViz(chart: ReportWriterChartRender): ChartViz {
  const categories = chart.categories ?? [];
  const series = chart.series ?? [];

  let maxAbs = 0;
  let hasNegative = false;
  for (const seriesItem of series) {
    for (const value of seriesItem.values ?? []) {
      const magnitude = Math.abs(value);
      if (magnitude > maxAbs) {
        maxAbs = magnitude;
      }

      if (value < 0) {
        hasNegative = true;
      }
    }
  }

  const built: ChartCategory[] = categories.map((category, index) => ({
    category: category.length > 0 ? category : "(blank)",
    bars: series.map((seriesItem) => {
      const value = seriesItem.values?.[index] ?? 0;
      return {
        seriesKey: seriesItem.key,
        seriesLabel: seriesItem.label || seriesItem.key,
        value,
        widthPct: maxAbs === 0 ? 0 : Math.round((Math.abs(value) / maxAbs) * 100),
        isNegative: value < 0
      };
    })
  }));

  return { categories: built, maxAbs, hasNegative };
}
