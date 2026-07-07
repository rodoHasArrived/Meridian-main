/**
 * Treemap — squarified area map of positive weights (exposure by position/sector, allocation
 * by book). Tile area ∝ `value`; tiles are alpha washes with hairline borders — never solid
 * fills. Colors by day-change `delta` (green/red, intensity ∝ magnitude vs the max), by
 * `group` (fixed wash palette), or flat. Labels hide progressively as tiles shrink (full
 * detail lives in the title tooltip). Div-based with measured layout so text stays crisp.
 */
import * as React from "react";

export interface TreemapItem {
  label: string;
  /** Tile area weight — must be > 0 (non-positive items are dropped). */
  value: number;
  /** Signed change (e.g. day %) — drives green/red wash intensity in delta mode. */
  delta?: number | null;
  /** Grouping key — drives the wash palette in group mode. */
  group?: string;
}

export interface TreemapProps {
  items: TreemapItem[];
  /** "auto" picks delta if any item has one, else group, else none. @default "auto" */
  colorBy?: "auto" | "delta" | "group" | "none";
  /** Format the tile value line. @default String */
  valueFmt?: (v: number) => string;
  /** Format the tile delta line. @default explicit-sign 2dp percent */
  deltaFmt?: (d: number) => string;
  /** Makes tiles clickable. */
  onSelect?: ((item: TreemapItem) => void) | null;
  /** Tile width (px) below which no label renders. @default 44 */
  minTileLabel?: number;
}
export declare function Treemap(props: TreemapProps): JSX.Element;
