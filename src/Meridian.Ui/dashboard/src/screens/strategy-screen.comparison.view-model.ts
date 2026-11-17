// Strategy run comparison (UX Phase E). Split out of strategy-screen.view-model.ts as it was touched:
// the selection rule for compare and diff, and the overlay model for the selected runs' equity and
// drawdown. Pure functions only; the view model owns state and requests.
import type { ChartAccessibleData } from "@/components/charts/chart-a11y";
import { formatText } from "@/screens/strategy-screen.formatters";
import type { PromoteState, StrategyCommand, StrategyCommandState } from "@/screens/strategy-screen.view-model";
import type { EquityCurvePoint, StrategyRunRecord } from "@/types";

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

/** The selection as the controls name it, e.g. "A vs B vs C". */
export function buildSelectionText(selectedRuns: StrategyRunRecord[]): string {
  if (selectedRuns.length === 0) {
    return "No runs selected";
  }

  if (selectedRuns.length === 1) {
    return `Selected ${selectedRuns[0].strategyName}`;
  }

  return selectedRuns.map((run) => run.strategyName).join(" vs ");
}

/** Compare, diff, promote and history command states for the current selection. */
export function buildStrategyCommandStates({
  selectedRuns,
  hasTwoRuns,
  hasOneBacktestRun,
  busy,
  activeCommand,
  promoteState,
  promoteBusy
}: {
  selectedRuns: StrategyRunRecord[];
  hasTwoRuns: boolean;
  hasOneBacktestRun: boolean;
  busy: boolean;
  activeCommand: StrategyCommand | null;
  promoteState: PromoteState;
  promoteBusy: boolean;
}): {
  promotionHistory: StrategyCommandState;
  compare: StrategyCommandState;
  diff: StrategyCommandState;
  promote: StrategyCommandState;
} {
  const pairLabel = selectedRuns.length === 2
    ? `${formatText(selectedRuns[0].strategyName)} and ${formatText(selectedRuns[1].strategyName)}`
    : null;
  const compareCount = selectedRuns.length;
  const canCompareSelection = compareCount >= 2 && compareCount <= RUN_COMPARE_MAX;
  const compareNames = canCompareSelection ? selectedRuns.map((run) => formatText(run.strategyName)).join(", ") : null;
  const compareDisabledReason = busy
    ? "Wait for the current Strategy command to finish."
    : canCompareSelection
      ? null
      : `Select two to ${RUN_COMPARE_MAX} runs before comparing. ${compareCount} selected.`;
  const compareLabel = `Compare ${canCompareSelection ? compareCount : 2} runs`;
  const selectedRun = selectedRuns[0] ?? null;
  const pairDisabledReason = busy
    ? "Wait for the current Strategy command to finish."
    : hasTwoRuns
      ? null
      : `Select exactly two runs before using this command. ${selectedRuns.length} selected.`;
  const promoteLabel = promoteState === "evaluating"
    ? "Evaluating..."
    : promoteState === "approving"
      ? "Approving promotion..."
      : promoteState === "done"
        ? "Promotion approved"
        : "Promote to Paper";
  const promoteDisabledReason = busy
    ? "Wait for the current Strategy command to finish."
    : promoteBusy
      ? "Paper-promotion workflow is already running."
      : promoteState === "done"
        ? "A durable Paper promotion was already approved for this run."
        : hasOneBacktestRun
          ? null
          : selectedRuns.length === 1
            ? "Select one completed backtest run before promoting to paper."
            : `Select one completed backtest run before promoting to paper. ${selectedRuns.length} selected.`;

  return {
    promotionHistory: {
      label: activeCommand === "history" ? "Loading history..." : "Promotion history",
      ariaLabel: activeCommand === "history" ? "Promotion history: loading" : "Promotion history",
      disabled: busy,
      disabledReason: busy ? "Wait for the current Strategy command to finish." : null,
      busy: activeCommand === "history"
    },
    compare: {
      label: activeCommand === "compare" ? "Comparing..." : compareLabel,
      ariaLabel: activeCommand === "compare"
        ? `Comparing selected runs: ${compareNames ?? "current selection"}`
        : compareNames
          ? `${compareLabel}: ${compareNames}`
          : `${compareLabel} unavailable`,
      disabled: !canCompareSelection || busy,
      disabledReason: compareDisabledReason,
      busy: activeCommand === "compare"
    },
    diff: {
      label: activeCommand === "diff" ? "Diffing..." : "Diff 2 runs",
      ariaLabel: activeCommand === "diff"
        ? `Diffing selected runs: ${pairLabel ?? "current selection"}`
        : pairLabel
          ? `Diff 2 runs: ${pairLabel}`
          : "Diff 2 runs unavailable",
      disabled: !hasTwoRuns || busy,
      disabledReason: pairDisabledReason,
      busy: activeCommand === "diff"
    },
    promote: {
      label: promoteLabel,
      ariaLabel: selectedRun
        ? `${promoteLabel}: evaluate ${formatText(selectedRun.strategyName)} for paper trading`
        : `${promoteLabel} unavailable`,
      disabled: !hasOneBacktestRun || busy || promoteBusy || promoteState === "done",
      disabledReason: promoteDisabledReason,
      busy: promoteBusy
    }
  };
}
