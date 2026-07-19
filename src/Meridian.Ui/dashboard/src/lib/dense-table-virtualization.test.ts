import { describe, expect, it } from "vitest";
import {
  DENSE_VIRTUALIZATION_ROW_HEIGHT,
  DENSE_VIRTUALIZATION_THRESHOLD,
  DENSE_VIRTUALIZATION_VIEWPORT_ROWS,
  denseTableVirtualization
} from "./dense-table-virtualization";

describe("denseTableVirtualization", () => {
  it("stays off at or below the threshold so short tables render normally", () => {
    expect(denseTableVirtualization(0)).toBeNull();
    expect(denseTableVirtualization(DENSE_VIRTUALIZATION_THRESHOLD)).toBeNull();
  });

  it("windows once the row count exceeds the threshold", () => {
    expect(DENSE_VIRTUALIZATION_ROW_HEIGHT).toBe(32);
    const config = denseTableVirtualization(DENSE_VIRTUALIZATION_THRESHOLD + 1);
    expect(config).toEqual({
      rowHeight: DENSE_VIRTUALIZATION_ROW_HEIGHT,
      viewportRowCount: DENSE_VIRTUALIZATION_VIEWPORT_ROWS,
      overscan: undefined
    });
  });

  it("honors overrides for row height, viewport, overscan, and threshold", () => {
    expect(denseTableVirtualization(10, { threshold: 5, rowHeight: 24, viewportRowCount: 8, overscan: 6 })).toEqual({
      rowHeight: 24,
      viewportRowCount: 8,
      overscan: 6
    });
    // A raised threshold keeps a mid-size table on the simple render path.
    expect(denseTableVirtualization(30, { threshold: 100 })).toBeNull();
  });
});
