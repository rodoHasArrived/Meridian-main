import { describe, expect, it } from "vitest";
import { indexFromPointerX, stepCrosshairIndex } from "@/components/charts/chart-interaction";

describe("indexFromPointerX", () => {
  it("returns null for empty data or zero-width overlays", () => {
    expect(indexFromPointerX(10, 0, 100, 0)).toBeNull();
    expect(indexFromPointerX(10, 0, 0, 5)).toBeNull();
  });

  it("maps the left and right edges to the first and last index", () => {
    expect(indexFromPointerX(0, 0, 100, 5)).toBe(0);
    expect(indexFromPointerX(100, 0, 100, 5)).toBe(4);
  });

  it("maps the midpoint to the middle index and clamps beyond the edges", () => {
    expect(indexFromPointerX(50, 0, 100, 5)).toBe(2);
    expect(indexFromPointerX(-40, 0, 100, 5)).toBe(0);
    expect(indexFromPointerX(400, 0, 100, 5)).toBe(4);
  });

  it("accounts for the overlay's left offset", () => {
    expect(indexFromPointerX(120, 20, 200, 5)).toBe(2); // (120-20)/200 = 0.5 -> index 2
  });
});

describe("stepCrosshairIndex", () => {
  it("starts at the near edge for the pressed direction when no index is set", () => {
    expect(stepCrosshairIndex(null, "ArrowRight", 5)).toBe(0);
    expect(stepCrosshairIndex(null, "ArrowLeft", 5)).toBe(4);
  });

  it("steps and clamps within bounds", () => {
    expect(stepCrosshairIndex(2, "ArrowRight", 5)).toBe(3);
    expect(stepCrosshairIndex(4, "ArrowRight", 5)).toBe(4);
    expect(stepCrosshairIndex(2, "ArrowLeft", 5)).toBe(1);
    expect(stepCrosshairIndex(0, "ArrowLeft", 5)).toBe(0);
  });

  it("jumps to the ends and ignores non-navigation keys", () => {
    expect(stepCrosshairIndex(2, "Home", 5)).toBe(0);
    expect(stepCrosshairIndex(2, "End", 5)).toBe(4);
    expect(stepCrosshairIndex(2, "a", 5)).toBeNull();
    expect(stepCrosshairIndex(2, "ArrowRight", 0)).toBeNull();
  });
});
