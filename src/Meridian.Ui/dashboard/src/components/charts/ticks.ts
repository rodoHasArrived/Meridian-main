// Shared axis-tick helper for the SVG chart primitives. Produces "nice" round tick values
// (1/2/5 × 10ⁿ steps) spanning [min, max] with roughly `count` divisions.

export function niceTicks(min: number, max: number, count: number): number[] {
  const span = max - min || 1;
  const raw = span / Math.max(1, count);
  const mag = Math.pow(10, Math.floor(Math.log10(raw)));
  const norm = raw / mag;
  const step = (norm >= 5 ? 5 : norm >= 2 ? 2 : 1) * mag;
  const start = Math.ceil(min / step) * step;
  const out: number[] = [];
  for (let v = start; v <= max + 1e-9; v += step) out.push(+v.toFixed(6));
  return out;
}
