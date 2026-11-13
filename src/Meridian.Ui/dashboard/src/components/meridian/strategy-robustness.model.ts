import type { StrategyRunSummary } from "@/types/workstation-6";

export type RobustnessMetric = "totalReturn" | "netPnl" | "finalEquity";
export function numericParameter(value: string | undefined): number | null {
  if (value == null || value.trim() === "") return null;
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : null;
}
export function numericAxes(runs: StrategyRunSummary[]): string[] {
  const keys = new Set(runs.flatMap(run => Object.keys(run.parameters ?? {})));
  return [...keys].filter(key => runs.some(run => numericParameter(run.parameters?.[key]) != null))
    .filter(key => !/tenant|company|account|scope/i.test(key)).sort();
}
export function fixedSlice(run: StrategyRunSummary, x: string, y: string): string {
  return JSON.stringify(Object.entries(run.parameters ?? {}).filter(([key]) => key !== x && key !== y).sort(([a], [b]) => a.localeCompare(b)));
}
export function buildRobustnessGrid(runs: StrategyRunSummary[], x: string, y: string, slice: string) {
  const filtered = runs.filter(run => fixedSlice(run, x, y) === slice);
  const values = (key: string) => [...new Set(filtered.map(run => numericParameter(run.parameters?.[key])).filter((v): v is number => v != null))].sort((a, b) => a - b);
  const xs = values(x), ys = values(y);
  const unplaced = filtered.filter(run => numericParameter(run.parameters?.[x]) == null || numericParameter(run.parameters?.[y]) == null);
  const indexed = new Map<string, StrategyRunSummary[]>();
  for (const run of filtered) {
    const xValue = numericParameter(run.parameters?.[x]), yValue = numericParameter(run.parameters?.[y]);
    if (xValue == null || yValue == null) continue;
    const key = `${xValue}|${yValue}`;
    const entries = indexed.get(key) ?? [];
    entries.push(run); indexed.set(key, entries);
  }
  const cellCount = xs.length * ys.length;
  return {
    xs, ys, cellCount, unplaced,
    cells: cellCount > 2500 ? [] : ys.flatMap(yValue => xs.map(xValue => ({
      x: xValue, y: yValue,
      runs: indexed.get(`${xValue}|${yValue}`) ?? []
    })))
  };
}
export function classifyRobustnessCell(runs: StrategyRunSummary[], metric: RobustnessMetric, threshold: number | null, direction: "min" | "max") {
  if (!runs.length) return "Missing";
  if (runs.length > 1) return "Repeated";
  const run = runs[0];
  if (run.status !== "Completed") return run.status;
  const value = run[metric];
  if (value == null || !Number.isFinite(Number(value))) return "Unavailable";
  if (threshold == null) return "Completed";
  return (direction === "min" ? value >= threshold : value <= threshold) ? "Acceptable" : "Outside threshold";
}
