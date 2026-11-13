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
  // Keep the unfilled warning presentation previously emitted by Tailwind 3.
  Warning: "text-warning",
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
  /** Missing, non-numeric and non-finite observations stay unavailable. */
  value: number | null;
  /** Width and offset on a symmetric scale with zero at 50%. */
  widthPct: number;
  leftPct: number;
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

/** Builds a signed value summary, never an imitation of the configured chart type. */
export function buildChartViz(chart: ReportWriterChartRender): ChartViz {
  const categories = chart.categories ?? [];
  const series = chart.series ?? [];
  // Only observations with a displayed category participate in the common scale.
  let maxAbs = 0;
  let hasNegative = false;
  for (const seriesItem of series) {
    for (let index = 0; index < categories.length; index += 1) {
      const value = seriesItem.values?.[index];
      if (typeof value !== "number" || !Number.isFinite(value)) continue;
      maxAbs = Math.max(maxAbs, Math.abs(value));
      hasNegative ||= value < 0;
    }
  }

  const built: ChartCategory[] = categories.map((category, index) => ({
    category: category.length > 0 ? category : "(blank)",
    bars: series.map((seriesItem) => {
      const observation = seriesItem.values?.[index];
      const value = typeof observation === "number" && Number.isFinite(observation) ? observation : null;
      const widthPct = value === null || maxAbs === 0 ? 0 : (Math.abs(value) / maxAbs) * 50;
      const isNegative = value !== null && value < 0;
      return {
        seriesKey: seriesItem.key,
        seriesLabel: seriesItem.label || seriesItem.key,
        value,
        widthPct,
        leftPct: isNegative ? 50 - widthPct : 50,
        isNegative
      };
    })
  }));

  return { categories: built, maxAbs, hasNegative };
}
