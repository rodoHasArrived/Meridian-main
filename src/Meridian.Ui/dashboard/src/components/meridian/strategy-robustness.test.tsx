import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { renderWithRouter } from "@/test/render";
import type { StrategyRunSummary, StrategySweepResultGroup } from "@/types/workstation-6";
import { getRunSweeps } from "@/lib/api";
import { StrategyRobustness } from "./strategy-robustness";
import { buildRobustnessGrid, classifyRobustnessCell, fixedSlice, numericParameter } from "./strategy-robustness.model";
vi.mock("@/lib/api", () => ({ getRunSweeps: vi.fn() }));
const run = (id: string, x: string, y: string, overrides: Partial<StrategyRunSummary> = {}): StrategyRunSummary => ({
  runId: id, strategyId: "strategy-1", strategyName: "Momentum", mode: "Backtest", engine: "MeridianNative", status: "Completed",
  startedAt: "2026-11-01T10:00:00Z", completedAt: "2026-11-01T11:00:00Z", datasetReference: "dataset:retained-1", feedReference: null,
  portfolioId: null, ledgerReference: null, netPnl: 20, totalReturn: 0.2, finalEquity: 120, fillCount: 1,
  lastUpdatedAt: "2026-11-01T11:00:00Z", auditReference: null, parameters: { lookback: x, threshold: y, universe: "retained" }, ...overrides
});
const runs = [run("a", "10", "1"), run("b", "20", "1", { status: "Failed", totalReturn: null }), run("c", "10", "2", { totalReturn: -0.1 })];
const group: StrategySweepResultGroup = { sweepId: "sweep-1", sweepDefinitionHash: "sweep-v2:ABC", objective: "TotalReturn", startedAt: runs[0].startedAt, runCount: 3, runs, objectiveRankings: [], comparisonContextVerified: true };
beforeEach(() => vi.mocked(getRunSweeps).mockResolvedValue([group]));
describe("retained robustness grid", () => {
  it("preserves missing and failed cells instead of inventing a continuous surface", () => {
    const grid = buildRobustnessGrid(runs, "lookback", "threshold", fixedSlice(runs[0], "lookback", "threshold"));
    expect(grid.cells).toHaveLength(4);
    expect(classifyRobustnessCell(grid.cells[3].runs, "totalReturn", 0, "min")).toBe("Missing");
    expect(classifyRobustnessCell(grid.cells[1].runs, "totalReturn", 0, "min")).toBe("Failed");
    expect(classifyRobustnessCell(grid.cells[2].runs, "totalReturn", 0, "min")).toBe("Outside threshold");
  });
  it("requires repeated coordinates to be inspected rather than cherry-picking a winner", () => {
    expect(classifyRobustnessCell([runs[0], run("repeat", "10", "1")], "totalReturn", 0, "min")).toBe("Repeated");
  });
  it("caps Cartesian allocation before constructing a sparse oversized grid", () => {
    const many = Array.from({ length: 500 }, (_, index) => run(`run-${index}`, String(index), String(index)));
    const grid = buildRobustnessGrid(many, "lookback", "threshold", fixedSlice(many[0], "lookback", "threshold"));
    expect(grid.cellCount).toBe(250_000);
    expect(grid.cells).toEqual([]);
  });
  it("keeps other parameters fixed and excludes unknown numeric coordinates", () => {
    const different = run("other", "10", "1", { parameters: { lookback: "10", threshold: "1", universe: "different" } });
    const missing = run("unknown", "", "1");
    const grid = buildRobustnessGrid([...runs, different, missing], "lookback", "threshold", fixedSlice(runs[0], "lookback", "threshold"));
    expect(grid.unplaced.map(item => item.runId)).toEqual(["unknown"]);
    expect(grid.cells.flatMap(cell => cell.runs).map(item => item.runId)).not.toContain("other");
    expect(numericParameter("Infinity")).toBeNull();
    expect(numericParameter(" ")).toBeNull();
  });
  it("loads actual experiments and offers keyboard-accessible cell drilldown with scope preserved", async () => {
    const user = userEvent.setup();
    renderWithRouter(<StrategyRobustness />, { initialEntries: ["/strategy/promotions?companyId=company-a"] });
    await user.click(screen.getByRole("button", { name: "Load retained experiments" }));
    const cell = await screen.findByRole("button", { name: "lookback 10, threshold 1: Acceptable" });
    cell.focus(); await user.keyboard("{Enter}");
    const details = screen.getByRole("region", { name: "Selected parameter result" });
    expect(within(details).getByText(/Retained parameters/)).toBeInTheDocument();
    expect(within(details).getByRole("link").getAttribute("href")).toBe("/strategy/run-ledger?companyId=company-a&runId=a");
    await user.click(screen.getByRole("button", { name: "lookback 20, threshold 2: Missing" }));
    expect(screen.getByText("No retained result at this coordinate. No performance is inferred.")).toBeInTheDocument();
  });
  it("shows explicit empty and failed retrieval states", async () => {
    vi.mocked(getRunSweeps).mockResolvedValueOnce([]).mockRejectedValueOnce(new Error("Forbidden"));
    const user = userEvent.setup(); renderWithRouter(<StrategyRobustness />);
    await user.click(screen.getByRole("button", { name: "Load retained experiments" }));
    expect(await screen.findByText(/No retained sweep experiments/)).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Refresh retained experiments" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Forbidden");
    expect(screen.queryByRole("table")).not.toBeInTheDocument();
  });
  it("keeps same-ID legacy experiments with different mode, feed and objective independently selectable", async () => {
    const other: StrategySweepResultGroup = {
      ...group, objective: "NetPnl",
      runs: runs.map(item => ({ ...item, runId: `other-${item.runId}`, mode: "Paper", feedReference: "feed-b" }))
    };
    vi.mocked(getRunSweeps).mockResolvedValueOnce([group, other]);
    const user = userEvent.setup(); renderWithRouter(<StrategyRobustness />);
    await user.click(screen.getByRole("button", { name: "Load retained experiments" }));
    const experiments = await screen.findByRole("combobox", { name: "Experiment" });
    const options = within(experiments).getAllByRole("option") as HTMLOptionElement[];
    expect(options[0].value).not.toBe(options[1].value);
    await user.selectOptions(experiments, options[1]);
    await user.click(screen.getByRole("button", { name: "lookback 10, threshold 1: Acceptable" }));
    expect(within(screen.getByRole("region", { name: "Selected parameter result" })).getByText(/other-a/)).toBeInTheDocument();
  });
  it("keeps the populated grid and selected result accessible", async () => {
    const user = userEvent.setup();
    const { container } = renderWithRouter(<StrategyRobustness />);
    await user.click(screen.getByRole("button", { name: "Load retained experiments" }));
    await user.click(await screen.findByRole("button", { name: "lookback 10, threshold 1: Acceptable" }));
    expect((await axe(container)).violations).toEqual([]);
  });
});
