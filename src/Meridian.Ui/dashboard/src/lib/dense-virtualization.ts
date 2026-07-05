/**
 * Pure windowing helpers for {@link DenseDataTable}'s optional row virtualization.
 *
 * The table stays a semantic `<table>`: windowing is expressed with spacer rows
 * (top/bottom padding) rather than absolutely-positioned children, so row
 * addressing (`aria-rowindex`) and keyboard traversal keep working across the
 * full data set even though only a window of rows is mounted. Row heights are
 * fixed (`rowHeight`) which is what makes the offset math exact.
 */

export interface RowWindowInput {
  /** Current scroll offset of the viewport, in pixels. */
  scrollTop: number;
  /** Fixed height of every row, in pixels. */
  rowHeight: number;
  /** Total number of rows in the full data set. */
  rowCount: number;
  /** Height of the scroll viewport, in pixels. */
  viewportHeight: number;
  /** Extra rows rendered above and below the viewport to smooth fast scrolls. */
  overscan: number;
}

export interface RowWindow {
  /** Absolute index of the first mounted row (inclusive). */
  startIndex: number;
  /** Absolute index one past the last mounted row (exclusive). */
  endIndex: number;
  /** Height of the leading spacer row, in pixels. */
  topPad: number;
  /** Height of the trailing spacer row, in pixels. */
  bottomPad: number;
}

/**
 * Resolve the slice of rows to mount for a given scroll offset, plus the
 * spacer heights that keep the scrollbar sized to the full data set.
 */
export function computeRowWindow(input: RowWindowInput): RowWindow {
  const { scrollTop, rowHeight, rowCount, viewportHeight, overscan } = input;
  if (rowCount <= 0 || rowHeight <= 0) {
    return { startIndex: 0, endIndex: 0, topPad: 0, bottomPad: 0 };
  }

  const safeScroll = Math.max(0, scrollTop);
  const safeOverscan = Math.max(0, Math.floor(overscan));
  const firstVisible = Math.floor(safeScroll / rowHeight);
  const visibleCount = Math.max(1, Math.ceil(Math.max(0, viewportHeight) / rowHeight));

  // Compute endIndex first, then clamp startIndex to it. Overscrolling past the
  // bottom (elastic scroll, resize) can push firstVisible beyond rowCount; without
  // the clamp startIndex would exceed endIndex, yielding an empty slice with an
  // oversized topPad and an unstable scrollbar.
  const endIndex = Math.min(rowCount, firstVisible + visibleCount + safeOverscan);
  const startIndex = Math.max(0, Math.min(endIndex, firstVisible - safeOverscan));

  return {
    startIndex,
    endIndex,
    topPad: startIndex * rowHeight,
    bottomPad: Math.max(0, (rowCount - endIndex) * rowHeight)
  };
}

export interface RevealScrollInput {
  /** Absolute index of the row to bring into view. */
  index: number;
  /** Fixed height of every row, in pixels. */
  rowHeight: number;
  /** Current scroll offset of the viewport, in pixels. */
  scrollTop: number;
  /** Height of the scroll viewport, in pixels. */
  viewportHeight: number;
}

/**
 * The scroll offset that brings `index` fully into the viewport. Rows above the
 * window align to the top; rows below align to the bottom; already-visible rows
 * leave the offset unchanged (no scroll jitter on in-window navigation).
 */
export function computeRevealScrollTop(input: RevealScrollInput): number {
  const { index, rowHeight, scrollTop, viewportHeight } = input;
  if (rowHeight <= 0 || index < 0) {
    return Math.max(0, scrollTop);
  }

  const rowTop = index * rowHeight;
  const rowBottom = rowTop + rowHeight;
  const safeScroll = Math.max(0, scrollTop);

  if (rowTop < safeScroll) {
    return rowTop;
  }
  if (rowBottom > safeScroll + viewportHeight) {
    return Math.max(0, rowBottom - viewportHeight);
  }
  return safeScroll;
}

export interface TypeaheadInput {
  /** Per-row match text, aligned to the full data set by absolute index. */
  labels: string[];
  /** Accumulated type-ahead buffer. */
  buffer: string;
  /** Index the search starts from (the currently focused row). */
  fromIndex: number;
  /**
   * When true (a continuation of an existing buffer) the current row is a valid
   * match; when false (a fresh keystroke) the search starts at the next row so
   * repeated keys cycle through matches.
   */
  inclusive: boolean;
}

/**
 * Resolve the absolute index of the next row whose label starts with the
 * type-ahead buffer, wrapping around the full data set. Returns null when
 * nothing matches.
 */
export function resolveTypeaheadIndex(input: TypeaheadInput): number | null {
  const needle = input.buffer.trim().toLowerCase();
  const count = input.labels.length;
  if (!needle || count === 0) {
    return null;
  }

  const from = ((Math.trunc(input.fromIndex) % count) + count) % count;
  const start = input.inclusive ? 0 : 1;
  const end = input.inclusive ? count - 1 : count;

  for (let step = start; step <= end; step++) {
    const index = (from + step) % count;
    if ((input.labels[index] ?? "").toLowerCase().startsWith(needle)) {
      return index;
    }
  }
  return null;
}
