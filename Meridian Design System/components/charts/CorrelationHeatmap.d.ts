/**
 * CorrelationHeatmap — a square matrix of pairwise correlations (-1..1). Positive correlation
 * washes green, negative red, intensity by magnitude; the diagonal reads 1.00. A token-driven
 * grid (not SVG) so values stay legible and cells stay square at any size.
 */
import * as React from "react";

export interface CorrelationHeatmapProps {
  /** Axis labels (tickers/series), length N. Used for both rows and columns. */
  labels: string[];
  /** N×N correlation values in -1..1; `matrix[r][c]`. */
  matrix: number[][];
  /** Cell value formatter. @default v => v.toFixed(2) */
  valueFmt?: (v: number) => string;
  /** Square cell edge in px. @default 46 */
  cellSize?: number;
  /** Row-header column width in px. @default 56 */
  headerSize?: number;
  /** Print the numeric value inside each cell. @default true */
  showValues?: boolean;
  /** Hover callback with { row, col, value }. */
  onCellHover?: ((cell: { row: number; col: number; value: number }) => void) | null;
}
export declare function CorrelationHeatmap(props: CorrelationHeatmapProps): JSX.Element;
