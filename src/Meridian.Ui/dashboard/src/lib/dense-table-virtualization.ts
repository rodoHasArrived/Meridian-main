import type { DenseDataTableVirtualization } from "@/components/meridian/ui-kit-primitives";

// Centralized "when and how to window" policy for dense grids. Below the
// threshold a table renders normally (and keeps its soft row cap); above it the
// table windows rows so a 50k-row tape scrolls at 60fps instead of mounting
// every node. Standardizing this here means every dense grid virtualizes the
// same way instead of each screen re-deriving row heights and thresholds.

export const DENSE_VIRTUALIZATION_ROW_HEIGHT = 32;
export const DENSE_VIRTUALIZATION_VIEWPORT_ROWS = 14;
export const DENSE_VIRTUALIZATION_THRESHOLD = 40;

export interface DenseVirtualizationOptions {
  /** Fixed row height used by the windowing math. Defaults to the compact 32px dense-row token. */
  rowHeight?: number;
  /** How many rows are visible in the scroll viewport. */
  viewportRowCount?: number;
  /** Extra rows rendered above/below the viewport to smooth fast scrolling. */
  overscan?: number;
  /** Row count above which windowing activates. */
  threshold?: number;
}

/**
 * Returns a {@link DenseDataTableVirtualization} descriptor when `rowCount`
 * exceeds the threshold, and `null` below it. Feed the result straight into a
 * `DenseDataTable`'s `virtualization` prop — `null` leaves the simple render path
 * (and the soft row cap) untouched for short tables.
 */
export function denseTableVirtualization(
  rowCount: number,
  options: DenseVirtualizationOptions = {}
): DenseDataTableVirtualization | null {
  const threshold = options.threshold ?? DENSE_VIRTUALIZATION_THRESHOLD;
  if (rowCount <= threshold) {
    return null;
  }
  return {
    rowHeight: options.rowHeight ?? DENSE_VIRTUALIZATION_ROW_HEIGHT,
    viewportRowCount: options.viewportRowCount ?? DENSE_VIRTUALIZATION_VIEWPORT_ROWS,
    overscan: options.overscan
  };
}
