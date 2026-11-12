/**
 * DotMatrix — a **magnitude** grid: rows × columns where each cell encodes a number.
 *
 * Two encodings, one component. `shape="dot"` sizes a marker by magnitude — the summary
 * matrix, legible at small cell sizes and easy to scan for outliers. `shape="square"` tints
 * the whole cell — the exposure mini-map, a dense heat block you read as terrain.
 *
 * Boundary: **CoverageMatrix** owns *status* grids (full / partial / gap / pending / none —
 * five categorical states with a hover readout). DotMatrix owns *continuous values*. If your
 * cells are states, you want CoverageMatrix.
 *
 * Use `diverging` when sign is the point (long/short, over/under budget): negatives take the
 * loss hue, positives the gain hue. Otherwise every cell is one hue at varying strength.
 *
 * @example
 * <DotMatrix
 *   ariaLabel="Desk exposure by sector"
 *   shape="square" diverging
 *   columns={["Energy","Fins","Tech","Health","Utils"]}
 *   rows={[
 *     { label:"Macro",    sub:"desk 01", cells:[ 12.4, -3.1,  22.0,  4.2, -1.0] },
 *     { label:"Credit",   sub:"desk 02", cells:[ -8.0, 18.6,   1.2, null, 6.4] },
 *   ]}
 *   fmt={(v) => v.toFixed(1) + "%"} />
 */
import * as React from "react";

export interface DotMatrixCell {
  value: number | null;
  /** Evidence string appended to the cell tooltip. */
  note?: string;
}
export interface DotMatrixRow {
  id?: string;
  label: string;
  /** Secondary mono line under the row label — book code, entity id. */
  sub?: string;
  /** One entry per column; `null` renders the empty mark, never a zero. */
  cells: Array<number | null | DotMatrixCell>;
}
export interface DotMatrixColumn { id?: string; label: string; }

export interface DotMatrixProps {
  rows: DotMatrixRow[];
  columns: Array<string | DotMatrixColumn>;
  /** "dot" = size-encoded marker · "square" = tint-encoded cell. @default "dot" */
  shape?: "dot" | "square";
  /** Sign-aware colouring (loss/gain hues). @default false */
  diverging?: boolean;
  /** Magnitude that saturates the scale. Defaults to the largest |value| present. */
  domainMax?: number | null;
  /** Cell edge in px. @default 26 */
  cellSize?: number;
  /** @default 3 */
  gap?: number;
  /** @default 128 */
  rowLabelWidth?: number;
  /** @default true */
  colLabels?: boolean;
  /** @default true */
  legend?: boolean;
  fmt?: (v: number) => string;
  /** Glyph for a null cell — an explicit absence, never a blank. @default "–" */
  emptyMark?: string;
  /** Makes cells real buttons (drill into the population behind the number). */
  onCellClick?: (row: DotMatrixRow, col: DotMatrixColumn, value: number | null) => void;
  /** Names the grid and emits a visually-hidden data table. */
  ariaLabel?: string;
  caption?: string;
}
export declare function DotMatrix(props: DotMatrixProps): React.JSX.Element;
