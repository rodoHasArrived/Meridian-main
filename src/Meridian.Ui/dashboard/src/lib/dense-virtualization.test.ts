import { describe, expect, it } from "vitest";
import { computeRevealScrollTop, computeRowWindow, resolveTypeaheadIndex } from "@/lib/dense-virtualization";

describe("computeRowWindow", () => {
  it("returns an empty window for an empty or zero-height data set", () => {
    expect(computeRowWindow({ scrollTop: 0, rowHeight: 32, rowCount: 0, viewportHeight: 320, overscan: 6 })).toEqual({
      startIndex: 0,
      endIndex: 0,
      topPad: 0,
      bottomPad: 0
    });
    expect(computeRowWindow({ scrollTop: 100, rowHeight: 0, rowCount: 50, viewportHeight: 320, overscan: 6 })).toEqual({
      startIndex: 0,
      endIndex: 0,
      topPad: 0,
      bottomPad: 0
    });
  });

  it("windows around the scroll offset with overscan and exact spacer padding", () => {
    // 1000 rows * 20px = 20000px total. Scrolled to 4000px => firstVisible = 200.
    const window = computeRowWindow({ scrollTop: 4000, rowHeight: 20, rowCount: 1000, viewportHeight: 200, overscan: 5 });
    expect(window.startIndex).toBe(195); // 200 - 5 overscan
    // firstVisible(200) + ceil(200/20)=10 visible + 5 overscan = 215
    expect(window.endIndex).toBe(215);
    expect(window.topPad).toBe(195 * 20);
    expect(window.bottomPad).toBe((1000 - 215) * 20);
    // Spacers + rendered rows always sum to the full virtual height.
    const renderedHeight = (window.endIndex - window.startIndex) * 20;
    expect(window.topPad + renderedHeight + window.bottomPad).toBe(1000 * 20);
  });

  it("clamps at the top and bottom edges", () => {
    const top = computeRowWindow({ scrollTop: -50, rowHeight: 32, rowCount: 40, viewportHeight: 320, overscan: 4 });
    expect(top.startIndex).toBe(0);
    expect(top.topPad).toBe(0);

    const bottom = computeRowWindow({ scrollTop: 100000, rowHeight: 32, rowCount: 40, viewportHeight: 320, overscan: 4 });
    expect(bottom.endIndex).toBe(40);
    expect(bottom.bottomPad).toBe(0);
    // Overscroll past the bottom clamps startIndex to endIndex (never beyond it),
    // so the slice stays valid and topPad never exceeds the full virtual height.
    expect(bottom.startIndex).toBeLessThanOrEqual(bottom.endIndex);
    expect(bottom.startIndex).toBe(40);
    expect(bottom.topPad).toBe(40 * 32);
  });
});

describe("computeRevealScrollTop", () => {
  const base = { rowHeight: 20, viewportHeight: 200 };

  it("aligns a row above the window to the top", () => {
    expect(computeRevealScrollTop({ ...base, index: 10, scrollTop: 400 })).toBe(200);
  });

  it("aligns a row below the window to the bottom", () => {
    // index 50 => rowBottom 1020; viewport 200 => 1020 - 200 = 820.
    expect(computeRevealScrollTop({ ...base, index: 50, scrollTop: 100 })).toBe(820);
  });

  it("leaves the offset untouched when the row is already visible", () => {
    expect(computeRevealScrollTop({ ...base, index: 12, scrollTop: 200 })).toBe(200);
  });
});

describe("resolveTypeaheadIndex", () => {
  const labels = ["Alpha", "Bravo", "Charlie", "Bravado", "Delta"];

  it("finds the next matching row after the current one for a fresh keystroke", () => {
    expect(resolveTypeaheadIndex({ labels, buffer: "b", fromIndex: 0, inclusive: false })).toBe(1);
  });

  it("cycles through matches when the same key repeats", () => {
    expect(resolveTypeaheadIndex({ labels, buffer: "b", fromIndex: 1, inclusive: false })).toBe(3);
    // wraps back around to the first match past the end
    expect(resolveTypeaheadIndex({ labels, buffer: "b", fromIndex: 3, inclusive: false })).toBe(1);
  });

  it("keeps the current row when a continuation buffer still matches it", () => {
    expect(resolveTypeaheadIndex({ labels, buffer: "brav", fromIndex: 1, inclusive: true })).toBe(1);
  });

  it("returns null when nothing matches or the buffer is blank", () => {
    expect(resolveTypeaheadIndex({ labels, buffer: "z", fromIndex: 0, inclusive: false })).toBeNull();
    expect(resolveTypeaheadIndex({ labels, buffer: "   ", fromIndex: 0, inclusive: false })).toBeNull();
    expect(resolveTypeaheadIndex({ labels: [], buffer: "a", fromIndex: 0, inclusive: false })).toBeNull();
  });
});
