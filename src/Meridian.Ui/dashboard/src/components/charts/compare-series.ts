// Caller-side normalisation for SelectionCompare. The compare sheet draws whatever it is given on
// one shared domain, so series of different lengths and levels must be made commensurable BEFORE
// they enter it — and the basis used must be stated next to the chart. These helpers do the two
// steps the design-system strategy-runs template applies to equity paths: pick a fixed number of
// evenly spaced observations, and rebase each path to its own first observation.
//
// Neither helper invents a value: a sampled point is always a recorded observation, and a rebased
// value is that observation divided by the path's own first observation.

/**
 * Pick `count` evenly spaced observations, always including the first and the last. Each slot takes
 * the nearest recorded observation, so a series shorter than `count` repeats observations rather
 * than interpolating between them. Returns `[]` when there are fewer than two observations.
 */
export function sampleEvenly<T>(points: readonly T[], count: number): T[] {
  const slots = Math.floor(count);
  if (points.length < 2 || slots < 2) return [];
  const sampled: T[] = [];
  for (let slot = 0; slot < slots; slot += 1) {
    sampled.push(points[Math.round((slot * (points.length - 1)) / (slots - 1))]);
  }
  return sampled;
}

/**
 * Rebase a path so its first observation reads `base` (100 by default) and every later one reads
 * relative to it. Returns `null` when the first observation is not a positive finite number (or any
 * later observation is not finite), because no honest rebasing exists then.
 */
export function rebaseToFirst(values: readonly number[], base = 100): number[] | null {
  const first = values[0];
  if (typeof first !== "number" || !Number.isFinite(first) || first <= 0) return null;
  if (values.some((value) => typeof value !== "number" || !Number.isFinite(value))) return null;
  return values.map((value) => (value / first) * base);
}
