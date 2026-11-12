import { describe, expect, it } from "vitest";
import { rebaseToFirst, sampleEvenly } from "./compare-series";

describe("sampleEvenly", () => {
  it("keeps the first and last observation and spaces the rest evenly", () => {
    const points = Array.from({ length: 15 }, (_, index) => index);

    expect(sampleEvenly(points, 8)).toEqual([0, 2, 4, 6, 8, 10, 12, 14]);
  });

  it("only ever returns recorded observations, nearest to each slot", () => {
    const points = Array.from({ length: 10 }, (_, index) => index * 10);

    const sampled = sampleEvenly(points, 8);

    expect(sampled).toHaveLength(8);
    expect(sampled[0]).toBe(0);
    expect(sampled[7]).toBe(90);
    for (const value of sampled) {
      expect(points).toContain(value);
    }
  });

  it("repeats observations instead of interpolating when the series is shorter than the slot count", () => {
    expect(sampleEvenly([100, 110, 121], 8)).toEqual([100, 100, 110, 110, 110, 110, 121, 121]);
  });

  it("returns nothing when there is no path to sample", () => {
    expect(sampleEvenly([], 8)).toEqual([]);
    expect(sampleEvenly([100], 8)).toEqual([]);
    expect(sampleEvenly([100, 110], 1)).toEqual([]);
  });
});

describe("rebaseToFirst", () => {
  it("rebases a path to 100 at its own first observation", () => {
    expect(rebaseToFirst([200, 210, 190])).toEqual([100, 105, 95]);
  });

  it("makes paths with different starting levels commensurable", () => {
    const small = rebaseToFirst([1_000, 1_100]);
    const large = rebaseToFirst([1_000_000, 1_100_000]);

    expect(small).not.toBeNull();
    expect(large).not.toBeNull();
    expect(small![1]).toBeCloseTo(110, 10);
    expect(large![1]).toBeCloseTo(110, 10);
  });

  it("honours another base", () => {
    expect(rebaseToFirst([50, 75], 1)).toEqual([1, 1.5]);
  });

  it("refuses to rebase when the first observation is not a positive finite number", () => {
    expect(rebaseToFirst([])).toBeNull();
    expect(rebaseToFirst([0, 10])).toBeNull();
    expect(rebaseToFirst([-5, 10])).toBeNull();
    expect(rebaseToFirst([Number.NaN, 10])).toBeNull();
  });

  it("refuses to rebase a path that holds a non-finite observation", () => {
    expect(rebaseToFirst([100, Number.POSITIVE_INFINITY])).toBeNull();
  });
});
