import { act, renderHook, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import * as api from "@/lib/api";
import { ApiError } from "@/lib/api-errors";
import type { CompareItem } from "@/components/charts/SelectionCompare";
import {
  STRATEGY_EQUITY_PATH_CATEGORIES,
  STRATEGY_EQUITY_PATH_CATEGORY_LABELS,
  STRATEGY_EQUITY_PATH_COMPARE_MAX,
  STRATEGY_EQUITY_PATH_POINT_COUNT,
  buildStrategyEquityPathCompareItem,
  buildStrategyEquityPathView,
  describeStrategyEquityPathFailure,
  describeStrategyEquityPathFailureCause,
  formatStrategyEquityPathIndex,
  formatStrategyEquityPathTotalReturn,
  strategyEquityPathIndexFractionDigits,
  toggleRunSelection,
  useStrategyEquityPathCompare
} from "@/screens/strategy-screen.view-model";
import type { EquityCurveSummary, StrategyRunRecord } from "@/types";

function run(id: string, overrides: Partial<StrategyRunRecord> = {}): StrategyRunRecord {
  return {
    id,
    strategyName: `Strategy ${id}`,
    engine: "Meridian Native",
    mode: "backtest",
    status: "Completed",
    dataset: "US Equities",
    window: "90d",
    pnl: "+1.0%",
    sharpe: "1.00",
    lastUpdated: "1m ago",
    notes: "",
    ...overrides
  };
}

function curve(runId: string, equities: number[]): EquityCurveSummary {
  return {
    runId,
    initialEquity: equities[0] ?? 0,
    finalEquity: equities[equities.length - 1] ?? 0,
    maxDrawdown: 0,
    maxDrawdownPercent: 0,
    maxDrawdownRecoveryDays: 0,
    sharpeRatio: 0,
    sortinoRatio: 0,
    points: equities.map((totalEquity, index) => ({
      date: `2026-05-${String(index + 1).padStart(2, "0")}`,
      totalEquity,
      cash: 0,
      dailyReturn: 0,
      drawdownFromPeak: 0,
      drawdownFromPeakPercent: 0
    }))
  };
}

function createDeferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((promiseResolve, promiseReject) => {
    resolve = promiseResolve;
    reject = promiseReject;
  });
  return { promise, resolve, reject };
}

const item = (id: string): CompareItem => ({ id, title: `Strategy ${id}` });
const runs = [run("a"), run("b"), run("c", { status: "Running", mode: "paper" })];

describe("equity-path compare builders", () => {
  it("keeps the template's eight-point start-to-end axis and the six-run cap", () => {
    expect(STRATEGY_EQUITY_PATH_COMPARE_MAX).toBe(6);
    expect(STRATEGY_EQUITY_PATH_POINT_COUNT).toBe(8);
    expect(STRATEGY_EQUITY_PATH_CATEGORIES).toEqual(["start", "", "", "", "", "", "", "end"]);
    expect(STRATEGY_EQUITY_PATH_CATEGORY_LABELS).toHaveLength(STRATEGY_EQUITY_PATH_POINT_COUNT);
    expect(STRATEGY_EQUITY_PATH_CATEGORY_LABELS.every((label) => label.trim().length > 0)).toBe(true);
  });

  it("builds the compare-set entry from the run record", () => {
    expect(buildStrategyEquityPathCompareItem(runs[2], runs)).toEqual({
      id: "c",
      title: "Strategy c",
      tag: "PAPER"
    });
  });

  it("tells runs that share a strategy name apart by run id", () => {
    const twins = [run("run-1", { strategyName: "Carry" }), run("run-2", { strategyName: "Carry" })];

    expect(buildStrategyEquityPathCompareItem(twins[0], twins).title).toBe("Carry (run-1)");
    expect(buildStrategyEquityPathCompareItem(twins[1], twins).title).toBe("Carry (run-2)");
  });

  it("reports the total return exactly as the run record carries it", () => {
    expect(formatStrategyEquityPathTotalReturn(run("a", { totalReturn: 0.042 }))).toBe("+4.20%");
    expect(formatStrategyEquityPathTotalReturn(run("a", { totalReturn: -0.018 }))).toBe("-1.80%");
    expect(formatStrategyEquityPathTotalReturn(run("a", { totalReturn: null }))).toBe("Not reported");
    expect(formatStrategyEquityPathTotalReturn(run("a"))).toBe("Not reported");
    expect(formatStrategyEquityPathTotalReturn(undefined)).toBe("Not reported");
  });

  it("formats index values on the rebased scale", () => {
    expect(formatStrategyEquityPathIndex(100)).toBe("100");
    expect(formatStrategyEquityPathIndex(104.24)).toBe("104.2");
  });

  it("keeps index precision at or below the tick step of the shared scale", () => {
    expect(strategyEquityPathIndexFractionDigits(5)).toBe(1);
    expect(strategyEquityPathIndexFractionDigits(1)).toBe(1);
    expect(strategyEquityPathIndexFractionDigits(0.5)).toBe(1);
    expect(strategyEquityPathIndexFractionDigits(0.1)).toBe(1);
    expect(strategyEquityPathIndexFractionDigits(0.05)).toBe(2);
    expect(strategyEquityPathIndexFractionDigits(0.02)).toBe(2);
    expect(strategyEquityPathIndexFractionDigits(0.01)).toBe(2);
    expect(strategyEquityPathIndexFractionDigits(0.002)).toBe(3);
    expect(strategyEquityPathIndexFractionDigits(1e-12)).toBe(6);
    expect(strategyEquityPathIndexFractionDigits(null)).toBe(1);
    expect(strategyEquityPathIndexFractionDigits(undefined)).toBe(1);
    expect(strategyEquityPathIndexFractionDigits(Number.NaN)).toBe(1);

    // Ticks 0.05 apart no longer collapse onto one label, and values read at the same precision.
    expect([100, 100.05, 100.1, 100.15].map((tick) => formatStrategyEquityPathIndex(tick, { step: 0.05 })))
      .toEqual(["100", "100.05", "100.1", "100.15"]);
    expect(formatStrategyEquityPathIndex(100.0234, { step: 0.01 })).toBe("100.02");
    expect(formatStrategyEquityPathIndex(104.24, { step: 5 })).toBe("104.2");
    expect(formatStrategyEquityPathIndex(104.24, { step: null })).toBe("104.2");
  });

  it("describes the cause of a failed read without the run status", () => {
    const notFound = new ApiError({ path: "/api/workstation/runs/c/equity-curve", status: 404 });

    expect(describeStrategyEquityPathFailureCause(notFound)).toBe("No equity curve is published for this run.");
    expect(describeStrategyEquityPathFailureCause(new Error("Curve service unavailable"))).toBe("Curve service unavailable.");
  });

  it("quotes the run's status from the record it is given when the view is built", () => {
    const load = { status: "unavailable", reason: "No equity curve is published for this run." } as const;

    expect(buildStrategyEquityPathView(load, run("c", { status: "Running" })).series).toEqual({
      status: "unavailable",
      reason: "No equity curve is published for this run. Run status: Running."
    });
    expect(buildStrategyEquityPathView(load, run("c", { status: "Completed" })).series).toEqual({
      status: "unavailable",
      reason: "No equity curve is published for this run. Run status: Completed."
    });
  });

  it("explains a missing curve with the run status", () => {
    const notFound = new ApiError({ path: "/api/workstation/runs/c/equity-curve", status: 404 });

    expect(describeStrategyEquityPathFailure(notFound, runs[2]))
      .toBe("No equity curve is published for this run. Run status: Running.");
    expect(describeStrategyEquityPathFailure(notFound, undefined))
      .toBe("No equity curve is published for this run.");
  });

  it("explains any other failure in the service's words", () => {
    expect(describeStrategyEquityPathFailure(new Error("Curve service unavailable"), runs[0]))
      .toBe("Curve service unavailable. Run status: Completed.");
    expect(describeStrategyEquityPathFailure("boom", runs[0]))
      .toBe("The equity curve request failed. Run status: Completed.");
  });

  it("shows a curve that is still loading or failed in words", () => {
    expect(buildStrategyEquityPathView(undefined).series).toEqual({ status: "loading", text: "Loading equity curve…" });
    expect(buildStrategyEquityPathView({ status: "loading" }).series.status).toBe("loading");
    expect(buildStrategyEquityPathView({ status: "unavailable", reason: "No curve." })).toEqual({
      series: { status: "unavailable", reason: "No curve." },
      curveDatesText: "—",
      curvePointsText: "—",
      initialEquityText: "—",
      finalEquityText: "—"
    });
  });

  it("rebases each path to 100 at its own first point so different starting equity shares one scale", () => {
    const small = buildStrategyEquityPathView({
      status: "ready",
      curve: curve("a", [1_000, 1_010, 1_020, 1_030, 1_040, 1_050, 1_060, 1_100])
    });
    const large = buildStrategyEquityPathView({
      status: "ready",
      curve: curve("b", [5_000_000, 5_050_000, 5_100_000, 5_150_000, 5_200_000, 5_250_000, 5_300_000, 5_500_000])
    });

    expect(small.series.status).toBe("ready");
    expect(large.series.status).toBe("ready");
    if (small.series.status !== "ready" || large.series.status !== "ready") return;
    expect(small.series.values).toHaveLength(8);
    expect(small.series.values[0]).toBe(100);
    expect(large.series.values[0]).toBe(100);
    expect(small.series.values[7]).toBeCloseTo(110, 10);
    expect(large.series.values[7]).toBeCloseTo(110, 10);
  });

  it("samples eight recorded points from a longer curve and reports the server's own figures", () => {
    const equities = Array.from({ length: 15 }, (_, index) => 100_000 + index * 1_000);
    const view = buildStrategyEquityPathView({ status: "ready", curve: curve("a", equities) });

    expect(view.curveDatesText).toBe("2026-05-01 to 2026-05-15");
    expect(view.curvePointsText).toBe("15");
    expect(view.initialEquityText).toBe("$100,000");
    expect(view.finalEquityText).toBe("$114,000");
    expect(view.series.status).toBe("ready");
    if (view.series.status !== "ready") return;
    expect(view.series.values.map((value) => Math.round((value ?? 0) * 100) / 100))
      .toEqual([100, 102, 104, 106, 108, 110, 112, 114]);
    // Footer figures are not derived here: the view carries no return of its own.
    expect(view.series.footerValue).toBeUndefined();
  });

  it("refuses to draw a curve with fewer than two points or a non-positive first point", () => {
    const single = buildStrategyEquityPathView({ status: "ready", curve: curve("a", [100_000]) });
    expect(single.series).toEqual({
      status: "unavailable",
      reason: "The published equity curve has fewer than two points, so there is no path to draw."
    });
    expect(single.curvePointsText).toBe("1");

    const zero = buildStrategyEquityPathView({ status: "ready", curve: curve("a", [0, 10, 20]) });
    expect(zero.series.status).toBe("unavailable");
    if (zero.series.status !== "unavailable") return;
    expect(zero.series.reason).toMatch(/cannot be rebased to 100/);
  });
});

describe("two-run compare selection stays as it was", () => {
  it("still keeps only the last two runs", () => {
    expect(toggleRunSelection(["a", "b"], "c")).toEqual(["b", "c"]);
    expect(toggleRunSelection(["a", "b"], "a")).toEqual(["b"]);
  });
});

describe("useStrategyEquityPathCompare", () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("requests nothing until the chart is open with two or more runs", () => {
    const getRunEquityCurve = vi.fn().mockResolvedValue(curve("a", [100, 110]));
    const { result } = renderHook(() => useStrategyEquityPathCompare(runs, { getRunEquityCurve }));

    act(() => result.current.onCompareSetChange([item("a"), item("b")]));
    expect(getRunEquityCurve).not.toHaveBeenCalled();

    act(() => result.current.onCompareSetChange([item("a")]));
    act(() => result.current.onSheetOpenChange(true));
    expect(getRunEquityCurve).not.toHaveBeenCalled();
    expect(result.current.seriesFor(item("a")).status).toBe("loading");
  });

  it("reads every selected run's curve when the chart opens and settles them together", async () => {
    const getRunEquityCurve = vi.fn((runId: string) => runId === "c"
      ? Promise.reject(new ApiError({ path: "/equity-curve", status: 404 }))
      : Promise.resolve(curve(runId, [100, 105, 110, 115, 120, 125, 130, 135])));
    const { result } = renderHook(() => useStrategyEquityPathCompare(runs, { getRunEquityCurve }));

    act(() => result.current.onCompareSetChange([item("a"), item("b"), item("c")]));
    act(() => result.current.onSheetOpenChange(true));

    expect(getRunEquityCurve.mock.calls.map(([runId]) => runId)).toEqual(["a", "b", "c"]);
    expect(result.current.seriesFor(item("a")).status).toBe("loading");

    await waitFor(() => expect(result.current.seriesFor(item("a")).status).toBe("ready"));
    expect(result.current.seriesFor(item("b")).status).toBe("ready");
    expect(result.current.seriesFor(item("c"))).toEqual({
      status: "unavailable",
      reason: "No equity curve is published for this run. Run status: Running."
    });
    expect(result.current.viewFor("a").initialEquityText).toBe("$100");
  });

  it("treats a client that throws synchronously as an unavailable curve", async () => {
    const getRunEquityCurve = vi.fn((runId: string): Promise<EquityCurveSummary> => {
      if (runId === "b") {
        throw new Error("Client not ready");
      }
      return Promise.resolve(curve(runId, [100, 110]));
    });
    const { result } = renderHook(() => useStrategyEquityPathCompare(runs, { getRunEquityCurve }));

    act(() => result.current.onCompareSetChange([item("a"), item("b")]));
    act(() => result.current.onSheetOpenChange(true));

    await waitFor(() => expect(result.current.seriesFor(item("b")).status).toBe("unavailable"));
    expect(result.current.seriesFor(item("b"))).toEqual({
      status: "unavailable",
      reason: "Client not ready. Run status: Completed."
    });
    expect(result.current.seriesFor(item("a")).status).toBe("ready");
  });

  it("discards a superseded request instead of applying its late result", async () => {
    const first = createDeferred<EquityCurveSummary>();
    const getRunEquityCurve = vi.fn()
      .mockImplementationOnce(() => first.promise)
      .mockImplementation((runId: string) => Promise.resolve(curve(runId, [200, 210])));
    const { result } = renderHook(() => useStrategyEquityPathCompare(runs, { getRunEquityCurve }));

    act(() => result.current.onCompareSetChange([item("a"), item("b")]));
    act(() => result.current.onSheetOpenChange(true));
    await waitFor(() => expect(result.current.seriesFor(item("b")).status).toBe("loading"));

    // The operator closes the chart while run a is still in flight.
    act(() => result.current.onSheetOpenChange(false));
    await act(async () => {
      first.resolve(curve("a", [100, 999]));
      await first.promise;
    });

    expect(result.current.viewFor("a").finalEquityText).toBe("—");
    expect(result.current.seriesFor(item("a")).status).toBe("loading");

    // Opening again starts from fresh curves.
    act(() => result.current.onSheetOpenChange(true));
    await waitFor(() => expect(result.current.seriesFor(item("a")).status).toBe("ready"));
    expect(result.current.viewFor("a").finalEquityText).toBe("$210");
    expect(getRunEquityCurve).toHaveBeenCalledTimes(4);
  });

  it("does not let an older request overwrite a newer one when the set changes mid-flight", async () => {
    const slow = createDeferred<EquityCurveSummary>();
    const calls: string[] = [];
    const getRunEquityCurve = vi.fn((runId: string) => {
      calls.push(runId);
      // Only the very first read of run b is slow; every later read answers at once.
      if (runId === "b" && calls.filter((id) => id === "b").length === 1) {
        return slow.promise;
      }
      return Promise.resolve(curve(runId, [100, 120]));
    });
    const { result } = renderHook(() => useStrategyEquityPathCompare(runs, { getRunEquityCurve }));

    act(() => result.current.onCompareSetChange([item("a"), item("b")]));
    act(() => result.current.onSheetOpenChange(true));
    // Run c joins before the first request settled: a new request supersedes it.
    act(() => result.current.onCompareSetChange([item("a"), item("b"), item("c")]));

    await waitFor(() => expect(result.current.seriesFor(item("b")).status).toBe("ready"));
    expect(result.current.viewFor("b").finalEquityText).toBe("$120");

    await act(async () => {
      slow.resolve(curve("b", [100, 999]));
      await slow.promise;
    });

    expect(result.current.viewFor("b").finalEquityText).toBe("$120");
    expect(result.current.seriesFor(item("c")).status).toBe("ready");
  });

  it("reads only the newly added run while the chart stays open and drops a removed one", async () => {
    const getRunEquityCurve = vi.fn((runId: string) => Promise.resolve(curve(runId, [100, 110])));
    const { result } = renderHook(() => useStrategyEquityPathCompare(runs, { getRunEquityCurve }));

    act(() => result.current.onCompareSetChange([item("a"), item("b")]));
    act(() => result.current.onSheetOpenChange(true));
    await waitFor(() => expect(result.current.seriesFor(item("b")).status).toBe("ready"));
    expect(getRunEquityCurve).toHaveBeenCalledTimes(2);

    act(() => result.current.onCompareSetChange([item("a"), item("b"), item("c")]));
    expect(result.current.seriesFor(item("a")).status).toBe("ready");
    expect(result.current.seriesFor(item("c")).status).toBe("loading");
    await waitFor(() => expect(result.current.seriesFor(item("c")).status).toBe("ready"));
    expect(getRunEquityCurve.mock.calls.map(([runId]) => runId)).toEqual(["a", "b", "c"]);

    act(() => result.current.onCompareSetChange([item("a"), item("c")]));
    expect(getRunEquityCurve).toHaveBeenCalledTimes(3);
    expect(result.current.viewFor("b").curvePointsText).toBe("—");
  });

  it("forgets the curves when the set falls below two and reads them again when it recovers", async () => {
    const getRunEquityCurve = vi.fn((runId: string) => Promise.resolve(curve(runId, [100, 110])));
    const { result } = renderHook(() => useStrategyEquityPathCompare(runs, { getRunEquityCurve }));

    act(() => result.current.onCompareSetChange([item("a"), item("b")]));
    act(() => result.current.onSheetOpenChange(true));
    await waitFor(() => expect(result.current.seriesFor(item("a")).status).toBe("ready"));

    act(() => result.current.onCompareSetChange([item("a")]));
    expect(result.current.seriesFor(item("a")).status).toBe("loading");

    act(() => result.current.onCompareSetChange([item("a"), item("c")]));
    await waitFor(() => expect(result.current.seriesFor(item("c")).status).toBe("ready"));
    expect(getRunEquityCurve.mock.calls.map(([runId]) => runId)).toEqual(["a", "b", "a", "c"]);
  });

  it("quotes the run's current status beside a failed read and reads the curve again when it changes", async () => {
    const notFound = new ApiError({ path: "/equity-curve", status: 404 });
    let published = false;
    const getRunEquityCurve = vi.fn((runId: string) => runId === "c" && !published
      ? Promise.reject(notFound)
      : Promise.resolve(curve(runId, [100, 110])));
    const { result, rerender } = renderHook(
      ({ library }: { library: StrategyRunRecord[] }) => useStrategyEquityPathCompare(library, { getRunEquityCurve }),
      { initialProps: { library: runs } }
    );

    act(() => result.current.onCompareSetChange([item("a"), item("c")]));
    act(() => result.current.onSheetOpenChange(true));
    await waitFor(() => expect(result.current.seriesFor(item("c")).status).toBe("unavailable"));
    expect(result.current.seriesFor(item("c"))).toEqual({
      status: "unavailable",
      reason: "No equity curve is published for this run. Run status: Running."
    });
    expect(getRunEquityCurve).toHaveBeenCalledTimes(2);

    // A workspace refresh: run c finished and still has no curve.
    rerender({ library: [runs[0], runs[1], run("c", { status: "Completed", mode: "paper" })] });

    expect(getRunEquityCurve.mock.calls.map(([runId]) => runId)).toEqual(["a", "c", "c"]);
    expect(result.current.seriesFor(item("a")).status).toBe("ready");
    await waitFor(() => expect(result.current.seriesFor(item("c")).status).toBe("unavailable"));
    expect(result.current.seriesFor(item("c"))).toEqual({
      status: "unavailable",
      reason: "No equity curve is published for this run. Run status: Completed."
    });

    // Another refresh: the status changes again and the curve is now published.
    published = true;
    rerender({ library: [runs[0], runs[1], run("c", { status: "Needs Review", mode: "paper" })] });

    await waitFor(() => expect(result.current.seriesFor(item("c")).status).toBe("ready"));
    expect(getRunEquityCurve).toHaveBeenCalledTimes(4);
  });

  it("does not read curves again when a refresh leaves the charted runs' status as it was", async () => {
    const getRunEquityCurve = vi.fn((runId: string) => Promise.resolve(curve(runId, [100, 110])));
    const { result, rerender } = renderHook(
      ({ library }: { library: StrategyRunRecord[] }) => useStrategyEquityPathCompare(library, { getRunEquityCurve }),
      { initialProps: { library: runs } }
    );

    act(() => result.current.onCompareSetChange([item("a"), item("b")]));
    act(() => result.current.onSheetOpenChange(true));
    await waitFor(() => expect(result.current.seriesFor(item("b")).status).toBe("ready"));

    // New records, same status for a and b; run c (not charted) changes status; a's return changes.
    rerender({ library: [run("a", { totalReturn: 0.05 }), run("b"), run("c", { status: "Completed" })] });

    expect(getRunEquityCurve).toHaveBeenCalledTimes(2);
    expect(result.current.seriesFor(item("a")).status).toBe("ready");
    expect(result.current.totalReturnFor(item("a"))).toBe("+5.00%");

    // A status change while the chart is closed reads nothing either.
    act(() => result.current.onSheetOpenChange(false));
    rerender({ library: [run("a", { status: "Needs Review" }), run("b"), run("c")] });
    expect(getRunEquityCurve).toHaveBeenCalledTimes(2);
  });

  it("reports total return from the run record, not from the curve", () => {
    const withReturn = [run("a", { totalReturn: 0.042 }), run("b")];
    const { result } = renderHook(() => useStrategyEquityPathCompare(withReturn, { getRunEquityCurve: vi.fn() }));

    expect(result.current.totalReturnFor(item("a"))).toBe("+4.20%");
    expect(result.current.totalReturnFor(item("b"))).toBe("Not reported");
    expect(result.current.totalReturnFor(item("gone"))).toBe("Not reported");
  });

  it("uses the existing workstation equity-curve client by default", async () => {
    const spy = vi.spyOn(api, "getRunEquityCurve").mockImplementation((runId) => Promise.resolve(curve(runId, [100, 110])));
    const { result } = renderHook(() => useStrategyEquityPathCompare(runs));

    act(() => result.current.onCompareSetChange([item("a"), item("b")]));
    act(() => result.current.onSheetOpenChange(true));

    await waitFor(() => expect(result.current.seriesFor(item("a")).status).toBe("ready"));
    expect(spy.mock.calls.map(([runId]) => runId)).toEqual(["a", "b"]);
  });

  it("falls back to the workstation client when injected services carry no curve reader", async () => {
    const spy = vi.spyOn(api, "getRunEquityCurve").mockImplementation((runId) => Promise.resolve(curve(runId, [100, 110])));
    const { result } = renderHook(() => useStrategyEquityPathCompare(runs, {}));

    act(() => result.current.onCompareSetChange([item("a"), item("b")]));
    act(() => result.current.onSheetOpenChange(true));

    await waitFor(() => expect(result.current.seriesFor(item("b")).status).toBe("ready"));
    expect(spy).toHaveBeenCalledTimes(2);
  });
});
