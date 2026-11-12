import { act, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { afterEach, beforeEach, describe, expect, it, vi, type MockInstance } from "vitest";
import * as api from "@/lib/api";
import { ApiError } from "@/lib/api-errors";
import { StrategyScreen } from "@/screens/strategy-screen";
import { TestMemoryRouter } from "@/test/render";
import type { EquityCurveSummary, StrategyRunRecord, StrategyWorkspaceResponse } from "@/types";

const metrics: StrategyWorkspaceResponse["metrics"] = [
  { id: "1", label: "Runs", value: "24", delta: "+8%", tone: "success" }
];

function run(id: string, strategyName: string, overrides: Partial<StrategyRunRecord> = {}): StrategyRunRecord {
  return {
    id,
    strategyName,
    engine: "Meridian Native",
    mode: "backtest",
    status: "Completed",
    dataset: "US Equities",
    window: "90d",
    pnl: "+1.9%",
    sharpe: "0.91",
    lastUpdated: "5m ago",
    notes: "",
    ...overrides
  };
}

const twoRuns: StrategyWorkspaceResponse = {
  metrics,
  runs: [
    run("run-1", "Mean Reversion FX", { mode: "paper", status: "Running", pnl: "+4.2%", totalReturn: 0.042 }),
    run("run-2", "Index Momentum")
  ]
};

const sevenRuns: StrategyWorkspaceResponse = {
  metrics,
  runs: Array.from({ length: 7 }, (_, index) => run(`run-${index + 1}`, `Strategy ${index + 1}`))
};

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

// Different starting equity on purpose: 100k against 2m.
const curves: Record<string, EquityCurveSummary> = {
  "run-1": curve("run-1", [100_000, 101_000, 102_000, 103_000, 104_000, 105_000, 106_000, 107_000]),
  "run-2": curve("run-2", [2_000_000, 1_990_000, 1_980_000, 1_970_000, 1_960_000, 1_950_000, 1_940_000, 1_900_000])
};

function createDeferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((promiseResolve) => {
    resolve = promiseResolve;
  });
  return { promise, resolve };
}

function renderScreen(data: StrategyWorkspaceResponse) {
  return render(
    <TestMemoryRouter initialEntries={["/strategy/promotions"]}>
      <StrategyScreen data={data} />
    </TestMemoryRouter>
  );
}

function chartToggle(name: string) {
  return screen.getByRole("button", { name: `Chart equity path for ${name}` });
}

function tray() {
  return screen.getByRole("region", { name: "Equity path comparison set" });
}

describe("StrategyScreen equity paths on one scale", () => {
  let getRunEquityCurve: MockInstance<typeof api.getRunEquityCurve>;

  beforeEach(() => {
    getRunEquityCurve = vi.spyOn(api, "getRunEquityCurve").mockImplementation((runId) => {
      const found = curves[runId];
      return found
        ? Promise.resolve(found)
        : Promise.resolve(curve(runId, [100_000, 101_000, 102_000, 103_000, 104_000, 105_000, 106_000, 107_000]));
    });
    vi.spyOn(api, "getRunReviewPacket").mockRejectedValue(new Error("not needed"));
    vi.spyOn(api, "compareRuns").mockResolvedValue([]);
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("offers a separate, empty equity-path set above the run library without reading any curve", () => {
    renderScreen(twoRuns);

    const region = tray();
    expect(region).toHaveTextContent("0 of 6");
    expect(region).toHaveTextContent("Tick ▢ Chart on two or more runs to compare their equity paths on one scale (up to 6).");
    expect(region).toHaveTextContent("separate from the two-run selection used by Compare, Diff and Promote");
    const chart = within(region).getByRole("button", { name: "Chart equity paths" });
    expect(chart).toBeDisabled();
    expect(chart).toHaveAttribute("title", "Tick ▢ Chart on at least two runs to chart their equity paths.");

    const table = screen.getByRole("treegrid", { name: "Strategy run library" });
    expect(within(table).getByRole("columnheader", { name: "Equity path" })).toBeInTheDocument();
    expect(getRunEquityCurve).not.toHaveBeenCalled();
    expect(screen.queryByRole("region", { name: "Equity paths on one scale" })).not.toBeInTheDocument();
  });

  it("adds a pressed-state button per run and no checkbox", async () => {
    const user = userEvent.setup();
    renderScreen(twoRuns);

    expect(screen.getAllByRole("checkbox", { name: /^(Select|Remove) .+ for compare and diff$/ })).toHaveLength(2);
    const toggle = chartToggle("Mean Reversion FX");
    expect(toggle).toHaveAttribute("aria-pressed", "false");
    expect(toggle).toHaveTextContent("▢Chart");

    await user.click(toggle);

    // The visible word is the same in both states and is part of the accessible name (label in
    // name); the glyph and aria-pressed carry the state.
    const pressed = chartToggle("Mean Reversion FX");
    expect(pressed).toHaveAttribute("aria-pressed", "true");
    expect(pressed).toHaveTextContent(/^▣Chart$/);
    for (const candidate of [pressed, chartToggle("Index Momentum")]) {
      const visibleWord = (candidate.textContent ?? "").replace(/[▢▣]/g, "").trim();
      expect(visibleWord).toBe("Chart");
      expect(candidate.getAttribute("aria-label")).toContain(visibleWord);
    }
    expect(screen.getAllByRole("checkbox", { name: /^(Select|Remove) .+ for compare and diff$/ })).toHaveLength(2);
    expect(tray()).toHaveTextContent("1 of 6");
    expect(tray()).toHaveTextContent("runs to compare");
    expect(within(tray()).getByText("PAPER")).toBeInTheDocument();
  });

  it("leaves the two-run compare and diff selection untouched", async () => {
    const user = userEvent.setup();
    renderScreen(twoRuns);
    const secondRow = screen.getByRole("row", { name: "Inspect Index Momentum run detail" });

    await user.click(chartToggle("Mean Reversion FX"));
    await user.click(chartToggle("Index Momentum"));

    // Charting a run neither ticks its compare checkbox nor selects its row.
    for (const checkbox of screen.getAllByRole("checkbox", { name: /^(Select|Remove) .+ for compare and diff$/ })) {
      expect(checkbox).not.toBeChecked();
    }
    expect(secondRow).not.toHaveAttribute("aria-selected", "true");
    expect(screen.getByText("No runs selected")).toBeInTheDocument();
    const disabledCompare = screen.getByRole("button", { name: /compare 2 runs unavailable/i });
    expect(disabledCompare).toHaveAttribute("title", "Select exactly two runs before using this command. 0 selected.");

    // And the two-run selection does not move the equity-path set.
    const checkboxes = screen.getAllByRole("checkbox", { name: /^(Select|Remove) .+ for compare and diff$/ });
    await user.click(checkboxes[0]);
    await user.click(checkboxes[1]);

    expect(screen.getByRole("button", { name: /compare 2 runs/i })).toBeEnabled();
    expect(tray()).toHaveTextContent("2 of 6");
    expect(chartToggle("Mean Reversion FX")).toHaveAttribute("aria-pressed", "true");

    await user.click(within(tray()).getByRole("button", { name: "Clear" }));

    expect(tray()).toHaveTextContent("0 of 6");
    expect(screen.getByRole("button", { name: /compare 2 runs: mean reversion fx and index momentum/i })).toBeEnabled();
    expect(api.compareRuns).not.toHaveBeenCalled();
  });

  it("keeps the existing open and compare commands unambiguous", async () => {
    const user = userEvent.setup();
    renderScreen(twoRuns);

    await user.click(chartToggle("Mean Reversion FX"));
    await user.click(chartToggle("Index Momentum"));
    await user.click(screen.getByRole("button", { name: "Chart 2 equity paths" }));
    await screen.findByRole("img", { name: /^Index Momentum/ });

    expect(screen.getAllByRole("button", { name: /compare 2 runs/i })).toHaveLength(1);
    expect(screen.getAllByRole("button", { name: /open/i })[0]).toHaveAttribute("aria-haspopup", "dialog");
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  it("reads the curves only when the chart is opened and draws them on one stated scale", async () => {
    const user = userEvent.setup();
    renderScreen(twoRuns);

    await user.click(chartToggle("Mean Reversion FX"));
    await user.click(chartToggle("Index Momentum"));
    expect(getRunEquityCurve).not.toHaveBeenCalled();

    await user.click(screen.getByRole("button", { name: "Chart 2 equity paths" }));

    expect(getRunEquityCurve.mock.calls.map(([runId]) => runId)).toEqual(["run-1", "run-2"]);
    const sheet = screen.getByRole("region", { name: "Equity paths on one scale" });
    expect(sheet).toHaveFocus();

    // Footer figures are the run record's, never recomputed from the path (run-2 fell 5%).
    const first = await within(sheet).findByRole("img", {
      name: "Mean Reversion FX — Total return +4.20%; first 100, last 107"
    });
    const second = within(sheet).getByRole("img", {
      name: "Index Momentum — Total return Not reported; first 100, last 95"
    });

    const ticksOf = (figure: HTMLElement) =>
      Array.from(figure.querySelectorAll("text[x='0']")).map((node) => node.textContent);
    expect(ticksOf(first)).toEqual(ticksOf(second));
    expect(ticksOf(first)).toEqual(["95", "100", "105"]);
    expect(first.getAttribute("viewBox")).toBe(second.getAttribute("viewBox"));
    expect(within(first).getByText("start")).toBeInTheDocument();
    expect(within(first).getByText("end")).toBeInTheDocument();

    expect(sheet).toHaveTextContent("2 plates · one shared domain · 8 categories");
    expect(sheet).toHaveTextContent("Y-axis: each path is rebased to 100 at that run's first curve point");
    expect(sheet).toHaveTextContent("X-axis: position within each run's own recorded span, start to end");
    expect(sheet).toHaveTextContent("Total return is the figure reported on the run record. It is not recalculated from the plotted path.");
    expect(sheet).toHaveTextContent("Each curve is as read when the chart was opened or the run joined it, and is read again when the run's status changes.");
    expect(sheet).toHaveTextContent("Plates share plot size, domain and ticks; a visible difference is a real difference.");
  });

  it("carries the plotted values and the reported figures as a table", async () => {
    const user = userEvent.setup();
    renderScreen(twoRuns);

    await user.click(chartToggle("Mean Reversion FX"));
    await user.click(chartToggle("Index Momentum"));
    await user.click(screen.getByRole("button", { name: "Chart 2 equity paths" }));
    await screen.findByRole("img", { name: /^Index Momentum/ });
    await user.click(screen.getByText("View plotted values"));

    const table = screen.getByRole("table", { name: "Equity paths on one scale: plotted values" });
    expect(within(table).getAllByRole("columnheader").map((cell) => cell.textContent)).toEqual([
      "Run",
      "Curve dates",
      "Curve points",
      "Initial equity (reported)",
      "Final equity (reported)",
      "Total return",
      "Start",
      "Sample 2",
      "Sample 3",
      "Sample 4",
      "Sample 5",
      "Sample 6",
      "Sample 7",
      "End"
    ]);
    const momentum = within(table).getByRole("row", { name: /^Index Momentum/ });
    expect(within(momentum).getAllByRole("cell").map((cell) => cell.textContent)).toEqual([
      "2026-05-01 to 2026-05-08",
      "8",
      "$2,000,000",
      "$1,900,000",
      "Not reported",
      "100",
      "99.5",
      "99",
      "98.5",
      "98",
      "97.5",
      "97",
      "95"
    ]);
    const fx = within(table).getByRole("row", { name: /^Mean Reversion FX/ });
    expect(within(fx).getAllByRole("cell")[4]).toHaveTextContent("+4.20%");
    expect(within(fx).getAllByRole("cell")[2]).toHaveTextContent("$100,000");
  });

  it("shows a run whose curve is unavailable in place, with the reason, instead of dropping it", async () => {
    getRunEquityCurve.mockImplementation((runId) => runId === "run-1"
      ? Promise.reject(new ApiError({ path: "/api/workstation/runs/run-1/equity-curve", status: 404 }))
      : Promise.resolve(curves[runId]));
    const user = userEvent.setup();
    renderScreen(twoRuns);

    await user.click(chartToggle("Mean Reversion FX"));
    await user.click(chartToggle("Index Momentum"));
    await user.click(screen.getByRole("button", { name: "Chart 2 equity paths" }));

    const sheet = screen.getByRole("region", { name: "Equity paths on one scale" });
    await within(sheet).findByRole("img", { name: /^Index Momentum/ });

    const reason = "Unavailable. No equity curve is published for this run. Run status: Running.";
    expect(within(sheet).getAllByText(reason)).toHaveLength(2);
    expect(within(sheet).getAllByRole("img")).toHaveLength(1);
    expect(sheet).toHaveTextContent("1 of 2 plates drawn · 1 unavailable · one shared domain · 8 categories");
    expect(within(tray()).getByText("Mean Reversion FX")).toBeInTheDocument();
    expect(within(sheet).getByRole("row", { name: /^Mean Reversion FX/ })).toHaveTextContent(reason);
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  it("reports a failed curve request in the service's words", async () => {
    getRunEquityCurve.mockImplementation((runId) => runId === "run-2"
      ? Promise.reject(new Error("Curve service unavailable"))
      : Promise.resolve(curves[runId]));
    const user = userEvent.setup();
    renderScreen(twoRuns);

    await user.click(chartToggle("Mean Reversion FX"));
    await user.click(chartToggle("Index Momentum"));
    await user.click(screen.getByRole("button", { name: "Chart 2 equity paths" }));

    const sheet = screen.getByRole("region", { name: "Equity paths on one scale" });
    expect((await within(sheet).findAllByText("Unavailable. Curve service unavailable. Run status: Completed.")).length)
      .toBeGreaterThan(0);
  });

  it("says a curve is loading and ignores a response that arrives after the chart was closed", async () => {
    const pending = createDeferred<EquityCurveSummary>();
    getRunEquityCurve.mockImplementation((runId) => runId === "run-1" ? pending.promise : Promise.resolve(curves[runId]));
    const user = userEvent.setup();
    renderScreen(twoRuns);

    await user.click(chartToggle("Mean Reversion FX"));
    await user.click(chartToggle("Index Momentum"));
    await user.click(screen.getByRole("button", { name: "Chart 2 equity paths" }));

    const sheet = screen.getByRole("region", { name: "Equity paths on one scale" });
    expect(within(sheet).getAllByText("Loading equity curve…").length).toBeGreaterThan(0);
    expect(sheet).toHaveTextContent("0 of 2 plates drawn · 2 loading");

    await user.click(within(sheet).getByRole("button", { name: "Close equity path chart" }));
    expect(screen.queryByRole("region", { name: "Equity paths on one scale" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Chart 2 equity paths" })).toHaveFocus();

    await act(async () => {
      pending.resolve(curves["run-1"]);
      await pending.promise;
    });
    expect(screen.queryByRole("img", { name: /^Mean Reversion FX —/ })).not.toBeInTheDocument();

    // Opening again reads fresh curves rather than reusing the late one.
    getRunEquityCurve.mockImplementation((runId) => Promise.resolve(curves[runId]));
    await user.click(screen.getByRole("button", { name: "Chart 2 equity paths" }));
    await screen.findByRole("img", { name: /^Mean Reversion FX —/ });
    expect(getRunEquityCurve).toHaveBeenCalledTimes(4);
  });

  it("labels a near-flat set at the precision of its ticks, in the picture and in the table", async () => {
    // +0.15% against +0.10%: the shared ticks step by 0.05 index points.
    getRunEquityCurve.mockImplementation((runId) => Promise.resolve(runId === "run-1"
      ? curve(runId, [100_000, 100_020, 100_040, 100_060, 100_080, 100_100, 100_120, 100_150])
      : curve(runId, [1_000_000, 1_000_100, 1_000_200, 1_000_300, 1_000_400, 1_000_600, 1_000_800, 1_001_000])));
    const user = userEvent.setup();
    renderScreen(twoRuns);

    await user.click(chartToggle("Mean Reversion FX"));
    await user.click(chartToggle("Index Momentum"));
    await user.click(screen.getByRole("button", { name: "Chart 2 equity paths" }));

    const sheet = screen.getByRole("region", { name: "Equity paths on one scale" });
    const first = await within(sheet).findByRole("img", {
      name: "Mean Reversion FX — Total return +4.20%; first 100, last 100.15"
    });
    const second = within(sheet).getByRole("img", {
      name: "Index Momentum — Total return Not reported; first 100, last 100.1"
    });
    const ticksOf = (figure: HTMLElement) =>
      Array.from(figure.querySelectorAll("text[x='0']")).map((node) => node.textContent);

    // No two gridlines read the same, and both plates still carry one tick set.
    expect(ticksOf(first)).toEqual(["100", "100.05", "100.1", "100.15"]);
    expect(ticksOf(second)).toEqual(ticksOf(first));
    // Both plots start to the right of the widest tick label and at the same x.
    const plotStart = (figure: HTMLElement) => Number(figure.querySelector("line")?.getAttribute("x1"));
    expect(plotStart(first)).toBeGreaterThanOrEqual(6 * 7.2);
    expect(plotStart(second)).toBe(plotStart(first));

    await user.click(screen.getByText("View plotted values"));
    const table = screen.getByRole("table", { name: "Equity paths on one scale: plotted values" });
    const plotted = (name: RegExp) =>
      within(within(table).getByRole("row", { name })).getAllByRole("cell").slice(5).map((cell) => cell.textContent);
    expect(plotted(/^Mean Reversion FX/)).toEqual(["100", "100.02", "100.04", "100.06", "100.08", "100.1", "100.12", "100.15"]);
    expect(plotted(/^Index Momentum/)).toEqual(["100", "100.01", "100.02", "100.03", "100.04", "100.06", "100.08", "100.1"]);
  });

  it("keeps distinct tick labels when every charted run moved by a few basis points", async () => {
    getRunEquityCurve.mockImplementation((runId) => Promise.resolve(runId === "run-1"
      ? curve(runId, [100_000, 100_020])
      : curve(runId, [1_000_000, 1_000_100])));
    const user = userEvent.setup();
    renderScreen(twoRuns);

    await user.click(chartToggle("Mean Reversion FX"));
    await user.click(chartToggle("Index Momentum"));
    await user.click(screen.getByRole("button", { name: "Chart 2 equity paths" }));

    const first = await screen.findByRole("img", {
      name: "Mean Reversion FX — Total return +4.20%; first 100, last 100.02"
    });
    const labels = Array.from(first.querySelectorAll("text[x='0']")).map((node) => node.textContent);
    expect(labels).toEqual(["100", "100.01", "100.02"]);
    expect(screen.getByRole("img", { name: "Index Momentum — Total return Not reported; first 100, last 100.01" }))
      .toBeInTheDocument();
  });

  it("follows a workspace refresh: the status beside an unavailable curve is current and the curve is read again", async () => {
    let published = false;
    getRunEquityCurve.mockImplementation((runId) => runId === "run-1" && !published
      ? Promise.reject(new ApiError({ path: "/api/workstation/runs/run-1/equity-curve", status: 404 }))
      : Promise.resolve(curves[runId]));
    const user = userEvent.setup();
    const { rerender } = renderScreen(twoRuns);

    await user.click(chartToggle("Mean Reversion FX"));
    await user.click(chartToggle("Index Momentum"));
    await user.click(screen.getByRole("button", { name: "Chart 2 equity paths" }));
    const sheet = screen.getByRole("region", { name: "Equity paths on one scale" });
    await within(sheet).findByRole("img", { name: /^Index Momentum/ });
    expect(within(sheet).getAllByText("Unavailable. No equity curve is published for this run. Run status: Running."))
      .toHaveLength(2);
    expect(getRunEquityCurve).toHaveBeenCalledTimes(2);

    const refreshed = (status: StrategyRunRecord["status"]): StrategyWorkspaceResponse => ({
      metrics,
      runs: [{ ...twoRuns.runs[0], status, totalReturn: 0.1 }, twoRuns.runs[1]]
    });

    // The run finished but still has no curve: the sentence quotes the new status.
    rerender(
      <TestMemoryRouter initialEntries={["/strategy/promotions"]}>
        <StrategyScreen data={refreshed("Completed")} />
      </TestMemoryRouter>
    );

    await waitFor(() => expect(
      within(sheet).getAllByText("Unavailable. No equity curve is published for this run. Run status: Completed.")
    ).toHaveLength(2));
    expect(within(sheet).queryByText(/Run status: Running/)).not.toBeInTheDocument();
    expect(getRunEquityCurve.mock.calls.map(([runId]) => runId)).toEqual(["run-1", "run-2", "run-1"]);
    // The run that did not change keeps its plate; its curve is not read again.
    expect(within(sheet).getByRole("img", { name: /^Index Momentum/ })).toBeInTheDocument();

    // A later status change finds the curve published.
    published = true;
    rerender(
      <TestMemoryRouter initialEntries={["/strategy/promotions"]}>
        <StrategyScreen data={refreshed("Needs Review")} />
      </TestMemoryRouter>
    );

    expect(await within(sheet).findByRole("img", {
      name: "Mean Reversion FX — Total return +10.00%; first 100, last 107"
    })).toBeInTheDocument();
    expect(within(sheet).queryByText(/^Unavailable\./)).not.toBeInTheDocument();
    expect(getRunEquityCurve).toHaveBeenCalledTimes(4);
  });

  it("keeps keyboard focus in the tray when a chip is removed or the set is cleared", async () => {
    const user = userEvent.setup();
    renderScreen(sevenRuns);

    for (const name of ["Strategy 1", "Strategy 2", "Strategy 3"]) {
      await user.click(chartToggle(name));
    }

    within(tray()).getByRole("button", { name: "Remove Strategy 1 from the equity path set" }).focus();
    await user.keyboard("{Enter}");
    expect(within(tray()).getByRole("button", { name: "Remove Strategy 2 from the equity path set" })).toHaveFocus();

    within(tray()).getByRole("button", { name: "Clear" }).focus();
    await user.keyboard("{Enter}");
    expect(tray()).toHaveTextContent("0 of 6");
    expect(tray()).toHaveFocus();

    // Removing the only chip also lands on the tray rather than on the page body.
    await user.click(chartToggle("Strategy 4"));
    within(tray()).getByRole("button", { name: "Remove Strategy 4 from the equity path set" }).focus();
    await user.keyboard("{Enter}");
    expect(tray()).toHaveFocus();
  });

  it("does not reopen the chart or read curves when a new set is built after clearing", async () => {
    const user = userEvent.setup();
    renderScreen(sevenRuns);

    await user.click(chartToggle("Strategy 1"));
    await user.click(chartToggle("Strategy 2"));
    await user.click(screen.getByRole("button", { name: "Chart 2 equity paths" }));
    await screen.findByRole("img", { name: /^Strategy 2/ });
    expect(getRunEquityCurve).toHaveBeenCalledTimes(2);

    await user.click(screen.getByRole("button", { name: "Clear set" }));
    expect(screen.queryByRole("region", { name: "Equity paths on one scale" })).not.toBeInTheDocument();
    expect(tray()).toHaveFocus();

    await user.click(chartToggle("Strategy 1"));
    await user.click(chartToggle("Strategy 3"));

    expect(screen.queryByRole("region", { name: "Equity paths on one scale" })).not.toBeInTheDocument();
    expect(getRunEquityCurve).toHaveBeenCalledTimes(2);

    // Falling below two from the tray closes the chart too; only the tray action opens it.
    await user.click(screen.getByRole("button", { name: "Chart 2 equity paths" }));
    await screen.findByRole("img", { name: /^Strategy 3/ });
    expect(getRunEquityCurve).toHaveBeenCalledTimes(4);
    await user.click(within(tray()).getByRole("button", { name: "Remove Strategy 3 from the equity path set" }));
    expect(screen.queryByRole("region", { name: "Equity paths on one scale" })).not.toBeInTheDocument();
    await user.click(chartToggle("Strategy 5"));
    expect(screen.queryByRole("region", { name: "Equity paths on one scale" })).not.toBeInTheDocument();
    expect(getRunEquityCurve).toHaveBeenCalledTimes(4);
  });

  it("refuses a seventh run in words instead of degrading the chart", async () => {
    const user = userEvent.setup();
    renderScreen(sevenRuns);

    for (let index = 1; index <= 6; index += 1) {
      await user.click(chartToggle(`Strategy ${index}`));
    }

    const refused = chartToggle("Strategy 7");
    expect(refused).toBeDisabled();
    expect(refused).toHaveAttribute("title", "Equity path set is full (6). Remove a run to chart another.");
    expect(tray()).toHaveTextContent("6 of 6");
    expect(tray()).toHaveTextContent("Set is full (6). Remove a run to chart another.");
    expect(within(tray()).getByRole("button", { name: "Chart 6 equity paths" })).toBeEnabled();

    await user.click(within(tray()).getByRole("button", { name: "Remove Strategy 1 from the equity path set" }));

    expect(chartToggle("Strategy 7")).toBeEnabled();
    expect(chartToggle("Strategy 1")).toHaveAttribute("aria-pressed", "false");
  });

  it("drops a run from the set when the run library no longer lists it", async () => {
    const user = userEvent.setup();
    const { rerender } = renderScreen(twoRuns);

    await user.click(chartToggle("Mean Reversion FX"));
    await user.click(chartToggle("Index Momentum"));
    expect(tray()).toHaveTextContent("2 of 6");

    rerender(
      <TestMemoryRouter initialEntries={["/strategy/promotions"]}>
        <StrategyScreen data={{ metrics, runs: [twoRuns.runs[0]] }} />
      </TestMemoryRouter>
    );

    await waitFor(() => expect(tray()).toHaveTextContent("1 of 6"));
    expect(within(tray()).queryByText("Index Momentum")).not.toBeInTheDocument();
  });

  it("has no basic accessibility violations with the chart open, one run unavailable", async () => {
    getRunEquityCurve.mockImplementation((runId) => runId === "run-1"
      ? Promise.reject(new ApiError({ path: "/api/workstation/runs/run-1/equity-curve", status: 404 }))
      : Promise.resolve(curves[runId]));
    const user = userEvent.setup();
    const { container } = renderScreen(twoRuns);

    await user.click(chartToggle("Mean Reversion FX"));
    await user.click(chartToggle("Index Momentum"));
    await user.click(screen.getByRole("button", { name: "Chart 2 equity paths" }));
    await screen.findByRole("img", { name: /^Index Momentum/ });
    await user.click(screen.getByText("View plotted values"));

    const results = await axe(container);
    expect(results.violations.map((violation) => ({
      id: violation.id,
      targets: violation.nodes.map((node) => node.target)
    }))).toEqual([]);
  });
});
