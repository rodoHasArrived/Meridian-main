// Strategy run comparison (UX Phase E). Split out of strategy-screen.view-model.ts as it was touched:
// the selection rule for compare and diff, and the overlay model for the selected runs' equity and
// drawdown. Pure functions only; the view model owns state and requests.
import type { ChartAccessibleData } from "@/components/charts/chart-a11y";
import type { EquityCurvePoint } from "@/types";

/** Compare holds at most this many runs. Diff stays two-run. */
export const RUN_COMPARE_MAX = 4;

/**
 * Adds or removes a run from the comparison selection. A run past the limit is refused, not swapped
 * in for the oldest: the selection only changes when the operator removes a run.
 */
export function toggleRunSelection(currentIds: readonly string[], runId: string, max = RUN_COMPARE_MAX): string[] {
  if (currentIds.includes(runId)) return currentIds.filter((id) => id !== runId);
  if (currentIds.length >= max) return [...currentIds];
  return [...currentIds, runId];
}

/** The message shown when a run is refused because the selection is full; null when it fits. */
export function describeRunSelectionRefusal(currentIds: readonly string[], runId: string, runLabel: string, max = RUN_COMPARE_MAX): string | null {
  if (currentIds.includes(runId) || currentIds.length < max) return null;
  return `Compare holds ${max} runs. Remove one from the selection to add ${runLabel}.`;
}

/** One selected run's loaded equity curve, or the reason it could not be loaded. */
export interface RunCurveInput {
  runId: string;
  label: string;
  points: readonly EquityCurvePoint[] | null;
  error: string | null;
}

export interface RunComparisonSeries {
  runId: string;
  label: string;
  points: number[];
}

export interface RunComparisonOverlay {
  /** Dates on the shared axis (YYYY-MM-DD). */
  labels: string[];
  /** Equity rebased to 100 at the start of the shared window. */
  equity: RunComparisonSeries[];
  /** Drawdown from each run's peak within the window, in percent (≤ 0). */
  drawdown: RunComparisonSeries[];
  window: { start: string; end: string };
  /** Runs left out of the charts, with the reason, e.g. "Run B: equity curve unavailable". */
  omitted: string[];
  equityTable: ChartAccessibleData;
  drawdownTable: ChartAccessibleData;
}

/** At most this many dates are drawn; longer windows are sampled evenly, always keeping both ends. */
export const RUN_OVERLAY_MAX_POINTS = 120;

function dayOf(value: string): string {
  return value.slice(0, 10);
}

/**
 * Aligns the selected runs on one date axis for overlay charts. Runs cover different periods, so
 * the charts show only the dates every drawn run shares (latest start to earliest end). Each run's
 * value on a date is its last recorded value on or before it; equity is rebased to 100 at the
 * window start and drawdown is measured from the run's peak inside the window. With fewer than two
 * drawable runs or no shared dates it returns the reason instead.
 */
export function buildRunComparisonOverlay(inputs: readonly RunCurveInput[]): RunComparisonOverlay | { unavailable: string; omitted: string[] } {
  const omitted: string[] = [];
  const runs = inputs.flatMap((input) => {
    const points = (input.points ?? [])
      .filter((point) => Number.isFinite(point.totalEquity) && Number.isFinite(Date.parse(point.date)))
      .map((point) => ({ day: dayOf(point.date), equity: point.totalEquity }))
      .sort((left, right) => left.day.localeCompare(right.day));
    if (input.error || points.length < 2) {
      omitted.push(`${input.label}: ${input.error ? "equity curve unavailable" : "fewer than two equity points"}`);
      return [];
    }
    return [{ ...input, points }];
  });
  if (runs.length < 2) return { unavailable: "Select at least two runs with retained equity curves to overlay them.", omitted };
  const start = runs.map((run) => run.points[0]!.day).sort().at(-1)!;
  const end = runs.map((run) => run.points.at(-1)!.day).sort()[0]!;
  if (start > end) return { unavailable: "The selected runs share no dates, so their curves cannot be overlaid.", omitted };

  const allDays = [...new Set(runs.flatMap((run) => run.points.map((point) => point.day)))].filter((day) => day >= start && day <= end).sort();
  const days = allDays.length <= RUN_OVERLAY_MAX_POINTS
    ? allDays
    : Array.from({ length: RUN_OVERLAY_MAX_POINTS }, (_, slot) => allDays[Math.round((slot * (allDays.length - 1)) / (RUN_OVERLAY_MAX_POINTS - 1))]!);

  const equity: RunComparisonSeries[] = [];
  const drawdown: RunComparisonSeries[] = [];
  for (const run of runs) {
    let cursor = 0;
    const values = days.map((day) => {
      while (cursor + 1 < run.points.length && run.points[cursor + 1]!.day <= day) cursor += 1;
      return run.points[cursor]!.equity;
    });
    const base = values[0]!;
    if (!(base > 0)) {
      omitted.push(`${run.label}: equity at the window start is not positive`);
      continue;
    }
    const rebased = values.map((value) => (value / base) * 100);
    let peak = Number.NEGATIVE_INFINITY;
    const dd = rebased.map((value) => { peak = Math.max(peak, value); return (value / peak - 1) * 100; });
    equity.push({ runId: run.runId, label: run.label, points: rebased });
    drawdown.push({ runId: run.runId, label: run.label, points: dd });
  }
  if (equity.length < 2) return { unavailable: "Select at least two runs with retained equity curves to overlay them.", omitted };

  const table = (series: RunComparisonSeries[], caption: string): ChartAccessibleData => ({
    caption,
    columns: ["Date", ...series.map((entry) => entry.label)],
    rows: days.map((day, index) => [day, ...series.map((entry) => Math.round(entry.points[index]! * 100) / 100)])
  });
  return {
    labels: days,
    equity,
    drawdown,
    window: { start, end },
    omitted,
    equityTable: table(equity, `Equity rebased to 100 on ${start}, ${start} to ${end}`),
    drawdownTable: table(drawdown, `Drawdown from each run's peak within ${start} to ${end}, percent`)
  };
}
