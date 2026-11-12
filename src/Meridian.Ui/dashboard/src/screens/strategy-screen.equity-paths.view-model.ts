import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import * as workstationApi from "@/lib/api";
import { describeApiError, isApiError } from "@/lib/api-errors";
import { rebaseToFirst, sampleEvenly } from "@/components/charts/compare-series";
import type { CompareItem, CompareSeriesState } from "@/components/charts/SelectionCompare";
import { formatCount as formatCountValue, formatNumber } from "@/lib/format";
import { formatMoney, formatSignedPercent, formatText } from "@/screens/strategy-screen.formatters";
import type { StrategyRunLibraryServices } from "@/screens/strategy-screen.view-model";
import type { EquityCurveSummary, StrategyRunRecord } from "@/types";

// The existing equity-curve client (already used by the Portfolio drill-in); no new endpoint.
export function defaultGetRunEquityCurve(runId: string): Promise<EquityCurveSummary> {
  return workstationApi.getRunEquityCurve(runId);
}

const defaultEquityPathServices = { getRunEquityCurve: defaultGetRunEquityCurve };

// --- Equity paths on one scale (SelectionCompare) ---
//
// A visual-only set of up to six runs whose published equity curves are drawn on one shared
// scale. It is deliberately SEPARATE from `selectedIds`, the two-run selection that drives
// Compare, Diff and Promote: it never reads or changes that selection, and nothing in it is sent
// to or derived from the comparison API. Curves are read from the existing equity-curve endpoint
// only when the operator opens the chart.

export const STRATEGY_EQUITY_PATH_COMPARE_MAX = 6;
export const STRATEGY_EQUITY_PATH_POINT_COUNT = 8;
/** Plot categories: the ends are named, the six samples between them are positional. */
export const STRATEGY_EQUITY_PATH_CATEGORIES: readonly string[] = ["start", "", "", "", "", "", "", "end"];
/** Column headers for the plotted-values table, where every sample needs a name. */
export const STRATEGY_EQUITY_PATH_CATEGORY_LABELS: readonly string[] = [
  "Start",
  "Sample 2",
  "Sample 3",
  "Sample 4",
  "Sample 5",
  "Sample 6",
  "Sample 7",
  "End"
];
export const STRATEGY_EQUITY_PATH_BASIS_TEXT = {
  value: "Y-axis: each path is rebased to 100 at that run's first curve point, so runs that started with different equity share one scale.",
  category: "X-axis: position within each run's own recorded span, start to end, as eight evenly spaced curve points (the nearest recorded point for each). Runs cover different dates; each run's dates are in the plotted values table.",
  footer: "Total return is the figure reported on the run record. It is not recalculated from the plotted path.",
  asOf: "Each curve is as read when the chart was opened or the run joined it, and is read again when the run's status changes. Close and reopen the chart to read every curve again."
} as const;

export type StrategyEquityPathLoadState =
  | { status: "loading" }
  | { status: "ready"; curve: EquityCurveSummary }
  /** `reason` is the cause only. The run's status is added when the view is built, from the current record. */
  | { status: "unavailable"; reason: string };

export interface StrategyEquityPathView {
  /** What the compare sheet draws, or the words it shows instead. */
  series: CompareSeriesState;
  curveDatesText: string;
  curvePointsText: string;
  initialEquityText: string;
  finalEquityText: string;
}

const EQUITY_PATH_NO_DETAIL = "—";

/** The compare-set entry for one run. Runs that share a strategy name are told apart by run id. */
export function buildStrategyEquityPathCompareItem(run: StrategyRunRecord, runs: readonly StrategyRunRecord[]): CompareItem {
  const strategyName = formatText(run.strategyName);
  const sharesName = runs.some((candidate) => candidate.id !== run.id && formatText(candidate.strategyName) === strategyName);
  return {
    id: run.id,
    title: sharesName ? `${strategyName} (${run.id})` : strategyName,
    tag: formatText(run.mode).toUpperCase()
  };
}

/** Why a run's equity curve could not be read, in operator words. Says nothing about the run itself. */
export function describeStrategyEquityPathFailureCause(error: unknown): string {
  if (isApiError(error) && error.status === 404) {
    return "No equity curve is published for this run.";
  }

  const summary = describeApiError(error, "The equity curve request failed.").summary.trim();
  return /[.!?]$/.test(summary) ? summary : `${summary}.`;
}

/** A reason a curve is not drawn, with the run's status alongside when the run record is known. */
export function withStrategyEquityPathRunStatus(reason: string, run: StrategyRunRecord | undefined): string {
  return run ? `${reason} Run status: ${formatText(run.status)}.` : reason;
}

/** Why a run's equity curve could not be read, in operator words, with the run's status alongside. */
export function describeStrategyEquityPathFailure(error: unknown, run: StrategyRunRecord | undefined): string {
  return withStrategyEquityPathRunStatus(describeStrategyEquityPathFailureCause(error), run);
}

const EQUITY_PATH_INDEX_MIN_FRACTION_DIGITS = 1;
const EQUITY_PATH_INDEX_MAX_FRACTION_DIGITS = 6;

/**
 * Fraction digits that tell adjacent ticks of the shared scale apart: one by default, more when
 * the ticks step by less than a tenth of an index point (runs that barely moved).
 */
export function strategyEquityPathIndexFractionDigits(step: number | null | undefined): number {
  if (typeof step !== "number" || !Number.isFinite(step) || step <= 0) {
    return EQUITY_PATH_INDEX_MIN_FRACTION_DIGITS;
  }

  // 0.5 -> 1, 0.05 -> 2, 0.01 -> 2, 0.002 -> 3. The epsilon keeps exact powers of ten exact.
  const needed = Math.ceil(-Math.log10(step) - 1e-9);
  return Math.min(EQUITY_PATH_INDEX_MAX_FRACTION_DIGITS, Math.max(EQUITY_PATH_INDEX_MIN_FRACTION_DIGITS, needed));
}

/**
 * Index value on the rebased scale ("100", "104.2"). Given the chart's shared scale, precision
 * follows its tick step ("100.02" on a 0.01 scale) so two gridlines never carry the same label and
 * the plotted-values table reads the same move the picture shows.
 */
export function formatStrategyEquityPathIndex(value: number, scale?: { step: number | null }): string {
  return formatNumber(value, { maximumFractionDigits: strategyEquityPathIndexFractionDigits(scale?.step) });
}

/** Total return exactly as the run record reports it; never derived from the plotted path. */
export function formatStrategyEquityPathTotalReturn(run: StrategyRunRecord | undefined): string {
  return typeof run?.totalReturn === "number" && Number.isFinite(run.totalReturn)
    ? formatSignedPercent(run.totalReturn)
    : "Not reported";
}

/**
 * What the sheet shows for one run. `run` is the CURRENT run record: the status quoted beside a
 * failed read comes from it at build time, so it follows a workspace refresh instead of freezing
 * the status the run had when the request settled.
 */
export function buildStrategyEquityPathView(
  load: StrategyEquityPathLoadState | undefined,
  run?: StrategyRunRecord
): StrategyEquityPathView {
  const noDetail = {
    curveDatesText: EQUITY_PATH_NO_DETAIL,
    curvePointsText: EQUITY_PATH_NO_DETAIL,
    initialEquityText: EQUITY_PATH_NO_DETAIL,
    finalEquityText: EQUITY_PATH_NO_DETAIL
  };
  if (!load || load.status === "loading") {
    return { series: { status: "loading", text: "Loading equity curve…" }, ...noDetail };
  }

  if (load.status === "unavailable") {
    return { series: { status: "unavailable", reason: withStrategyEquityPathRunStatus(load.reason, run) }, ...noDetail };
  }

  const points = Array.isArray(load.curve.points) ? load.curve.points : [];
  const detail = {
    curveDatesText: points.length > 0 ? `${points[0].date} to ${points[points.length - 1].date}` : EQUITY_PATH_NO_DETAIL,
    curvePointsText: formatCountValue(points.length),
    initialEquityText: formatMoney(load.curve.initialEquity),
    finalEquityText: formatMoney(load.curve.finalEquity)
  };
  const sampled = sampleEvenly(points, STRATEGY_EQUITY_PATH_POINT_COUNT).map((point) => point.totalEquity);
  if (sampled.length === 0) {
    return {
      series: { status: "unavailable", reason: "The published equity curve has fewer than two points, so there is no path to draw." },
      ...detail
    };
  }

  const rebased = rebaseToFirst(sampled);
  if (!rebased) {
    return {
      series: { status: "unavailable", reason: "The published equity curve cannot be rebased to 100: its first point is not a positive equity value, or a sampled point is not a number." },
      ...detail
    };
  }

  return { series: { status: "ready", values: rebased }, ...detail };
}

/**
 * State for the Strategy run library's equity-path chart. The compare set itself lives in the
 * SelectionCompare provider; this hook only follows it (`onCompareSetChange`, `onSheetOpenChange`)
 * and must be mounted and unmounted together with that provider. It reads the curves the chart
 * needs:
 *  - nothing is requested until the chart is showing (open, with two or more runs);
 *  - every showing starts from fresh curves; while it stays showing only newly added runs are
 *    read, plus any run whose record status changed since its curve was read (a run that finished
 *    may have published or extended its curve);
 *  - requests settle together (`Promise.allSettled`) and a superseded request is discarded;
 *  - a run whose curve cannot be read keeps its place with the reason and its CURRENT status.
 */
export function useStrategyEquityPathCompare(
  runs: readonly StrategyRunRecord[],
  services: Pick<StrategyRunLibraryServices, "getRunEquityCurve"> = defaultEquityPathServices
) {
  const [loads, setLoads] = useState<Record<string, StrategyEquityPathLoadState>>({});
  const loadsRef = useRef(loads);
  const runsRef = useRef(runs);
  runsRef.current = runs;
  const getRunEquityCurve = services.getRunEquityCurve ?? defaultGetRunEquityCurve;
  const getRunEquityCurveRef = useRef(getRunEquityCurve);
  getRunEquityCurveRef.current = getRunEquityCurve;
  const compareIdsRef = useRef<string[]>([]);
  const sheetOpenRef = useRef(false);
  const showingRef = useRef(false);
  const requestIdRef = useRef(0);
  // Record status each run had when its curve was last requested, to notice a later change.
  const requestedStatusRef = useRef<Record<string, string | undefined>>({});

  const commitLoads = useCallback((next: Record<string, StrategyEquityPathLoadState>) => {
    loadsRef.current = next;
    setLoads(next);
  }, []);

  const syncEquityPaths = useCallback((rereadRunIds: readonly string[] = []) => {
    const runIds = compareIdsRef.current;
    const showing = sheetOpenRef.current && runIds.length >= 2;
    const wasShowing = showingRef.current;
    showingRef.current = showing;
    // Any change supersedes whatever is in flight.
    const requestId = requestIdRef.current + 1;
    requestIdRef.current = requestId;

    if (!showing) {
      requestedStatusRef.current = {};
      if (Object.keys(loadsRef.current).length > 0) {
        commitLoads({});
      }
      return;
    }

    const current = loadsRef.current;
    const pendingRunIds = runIds.filter((runId) => {
      const known = wasShowing ? current[runId] : undefined;
      return !known || known.status === "loading" || rereadRunIds.includes(runId);
    });
    const next: Record<string, StrategyEquityPathLoadState> = {};
    const requestedStatus: Record<string, string | undefined> = {};
    for (const runId of runIds) {
      const pending = pendingRunIds.includes(runId);
      next[runId] = pending ? { status: "loading" } : current[runId];
      requestedStatus[runId] = pending
        ? runsRef.current.find((candidate) => candidate.id === runId)?.status
        : requestedStatusRef.current[runId];
    }
    requestedStatusRef.current = requestedStatus;
    commitLoads(next);
    if (pendingRunIds.length === 0) {
      return;
    }

    void Promise.allSettled(
      pendingRunIds.map((runId) => {
        try {
          return Promise.resolve(getRunEquityCurveRef.current(runId));
        } catch (error) {
          // A client that throws before returning a promise is one more unavailable curve.
          return Promise.reject(error);
        }
      })
    ).then((results) => {
      if (requestIdRef.current !== requestId) {
        return;
      }

      const resolved = { ...loadsRef.current };
      results.forEach((result, index) => {
        const runId = pendingRunIds[index];
        resolved[runId] = result.status === "fulfilled"
          ? { status: "ready", curve: result.value }
          : { status: "unavailable", reason: describeStrategyEquityPathFailureCause(result.reason) };
      });
      commitLoads(resolved);
    });
  }, [commitLoads]);

  const onCompareSetChange = useCallback((items: readonly CompareItem[]) => {
    compareIdsRef.current = items.map((item) => item.id);
    syncEquityPaths();
  }, [syncEquityPaths]);

  const onSheetOpenChange = useCallback((open: boolean) => {
    sheetOpenRef.current = open;
    syncEquityPaths();
  }, [syncEquityPaths]);

  // A workspace refresh can change a charted run's status (Running -> Completed) while the chart
  // stays open. The curve read under the old status may be missing or shorter than what is now
  // published, so it is read again rather than left standing beside the new status.
  useEffect(() => {
    if (!showingRef.current) {
      return;
    }

    // A run that left the library is not re-read here: the screen drops it from the set.
    const changedRunIds = compareIdsRef.current.filter((runId) => {
      const run = runs.find((candidate) => candidate.id === runId);
      return run !== undefined
        && runId in requestedStatusRef.current
        && requestedStatusRef.current[runId] !== run.status;
    });
    if (changedRunIds.length > 0) {
      syncEquityPaths(changedRunIds);
    }
  }, [runs, syncEquityPaths]);

  const views = useMemo(() => {
    const byRunId: Record<string, StrategyEquityPathView> = {};
    for (const [runId, load] of Object.entries(loads)) {
      byRunId[runId] = buildStrategyEquityPathView(load, runs.find((candidate) => candidate.id === runId));
    }
    return byRunId;
  }, [loads, runs]);

  const viewFor = useCallback(
    (runId: string) => views[runId] ?? buildStrategyEquityPathView(undefined),
    [views]
  );

  const seriesFor = useCallback((item: CompareItem) => viewFor(item.id).series, [viewFor]);

  const totalReturnFor = useCallback(
    (item: CompareItem) => formatStrategyEquityPathTotalReturn(runs.find((candidate) => candidate.id === item.id)),
    [runs]
  );

  return {
    maxRuns: STRATEGY_EQUITY_PATH_COMPARE_MAX,
    categories: STRATEGY_EQUITY_PATH_CATEGORIES,
    categoryLabels: STRATEGY_EQUITY_PATH_CATEGORY_LABELS,
    basisText: STRATEGY_EQUITY_PATH_BASIS_TEXT,
    formatIndex: formatStrategyEquityPathIndex,
    seriesFor,
    viewFor,
    totalReturnFor,
    onCompareSetChange,
    onSheetOpenChange
  };
}

