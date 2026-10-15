import { act, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";
import { getPortfolioCashLadder } from "@/lib/api/portfolio-cash-ladder.api";
import { CashLadderScreen } from "./cash-ladder-screen";
import type { PortfolioCashLadder } from "@/types/portfolio-cash-ladder.types";

vi.mock("@/lib/api/portfolio-cash-ladder.api", () => ({ getPortfolioCashLadder: vi.fn() }));
const getLadder = vi.mocked(getPortfolioCashLadder);
afterEach(() => vi.resetAllMocks());

function ladder(account: string, minimumCashThreshold = 10000): PortfolioCashLadder {
  return {
    engineVersion: `engine-${account}`, asOfDate: "2026-07-05", horizonDays: 7, baseCurrency: "USD",
    scenarioId: "base", openingCash: 50000, minimumCashThreshold,
    buckets: [{ bucketStart: "2026-07-05", bucketEnd: "2026-07-11", label: `Week ${account}`,
      inflows: 3500, outflows: 0, netFlow: 3500, cumulativeCash: 53500,
      breachesMinimumBalance: false, sources: [{ source: "Coupons & interest", amount: 3500, flowCount: 1 }] }],
    contributions: [{ securityId: "bond", displayName: `Bond ${account}`, assetClass: "Bond",
      sourceDomain: "SecurityMaster", flowType: "Coupon", sourceLane: "Coupons & interest",
      dueDate: "2026-07-10", amount: 3500, currency: "USD", baseAmount: 3500 }],
    availableScenarios: [{ scenarioId: "base", displayName: "Base projection", description: `Assumptions ${account}`,
      modeledEffects: ["Contractual flows"], assumptions: ["Unchanged rates"] },
      { scenarioId: "early-call", displayName: "Early call", description: "Calls exercised", modeledEffects: [], assumptions: [] }],
    assumptions: [], warnings: [], generatedAt: "2026-07-05T12:00:00Z", securitiesEvaluated: 1, securitiesWithFlows: 1
  };
}

function view(account?: string) {
  return <MemoryRouter><CashLadderScreen fundAccountId={account} /></MemoryRouter>;
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason: unknown) => void;
  const promise = new Promise<T>((resolve_, reject_) => { resolve = resolve_; reject = reject_; });
  return { promise, resolve, reject };
}

describe("Cash Ladder request scope", () => {
  it("clears balances, threshold edits and drill selection during a delayed account switch", async () => {
    getLadder.mockResolvedValueOnce(ladder("A"));
    const next = deferred<PortfolioCashLadder>();
    getLadder.mockReturnValueOnce(next.promise);
    const user = userEvent.setup();
    const { rerender } = render(view("account-a"));
    await screen.findByText("engine-A");
    await user.click(screen.getByRole("button", { name: /Week A/ }));
    expect(screen.getByText("Bond A")).toBeInTheDocument();
    await user.clear(screen.getByLabelText("Minimum cash balance threshold"));
    await user.type(screen.getByLabelText("Minimum cash balance threshold"), "77777");

    rerender(view("account-b"));
    expect(screen.queryByText("engine-A")).not.toBeInTheDocument();
    expect(screen.queryByText("Bond A")).not.toBeInTheDocument();
    expect(screen.queryByText("Assumptions A")).not.toBeInTheDocument();
    expect(screen.queryByLabelText("Cash ladder bucket values")).not.toBeInTheDocument();
    expect(screen.getByText("Loading cash ladder…")).toBeInTheDocument();
    expect(screen.getByLabelText("Minimum cash balance threshold")).toHaveValue(null);
    expect(getLadder).toHaveBeenLastCalledWith({ scenario: "base", minimumCash: undefined, fundAccountId: "account-b" },
      expect.objectContaining({ signal: expect.any(AbortSignal) }));

    await act(async () => next.resolve(ladder("B", 20000)));
    expect(screen.getByText("engine-B")).toBeInTheDocument();
    expect(screen.getByLabelText("Minimum cash balance threshold")).toHaveValue(20000);
    expect(screen.queryByText("Bond B")).not.toBeInTheDocument();
    expect(within(screen.getByRole("table", { name: "Cash ladder contributing flows" }))
      .getByText("Select a bucket in the chart to inspect its contributing flows.")).toBeInTheDocument();
  });

  it("does not carry an applied threshold or scenario into another account", async () => {
    getLadder.mockImplementation(async (params) => ({ ...ladder(params?.fundAccountId ?? "all"),
      minimumCashThreshold: params?.minimumCash ?? 10000, scenarioId: params?.scenario ?? "base" }));
    const user = userEvent.setup();
    const { rerender } = render(view("account-a"));
    await screen.findByText("engine-account-a");
    await user.clear(screen.getByLabelText("Minimum cash balance threshold"));
    await user.type(screen.getByLabelText("Minimum cash balance threshold"), "77777");
    await user.click(screen.getByRole("button", { name: "Apply threshold" }));
    await user.selectOptions(screen.getByLabelText("Stress scenario"), "early-call");
    await waitFor(() => expect(getLadder).toHaveBeenLastCalledWith(
      { scenario: "early-call", minimumCash: 77777, fundAccountId: "account-a" }, expect.any(Object)));
    rerender(view("account-b"));
    await screen.findByText("engine-account-b");
    expect(getLadder).toHaveBeenLastCalledWith({ scenario: "base", minimumCash: undefined, fundAccountId: "account-b" }, expect.any(Object));
    expect(screen.getByLabelText("Minimum cash balance threshold")).toHaveValue(10000);
  });

  it("keeps the new account unavailable after failure and ignores a late old-account response", async () => {
    getLadder.mockResolvedValueOnce(ladder("A"));
    const previous = deferred<PortfolioCashLadder>();
    const next = deferred<PortfolioCashLadder>();
    getLadder.mockReturnValueOnce(previous.promise).mockReturnValueOnce(next.promise);
    const user = userEvent.setup();
    const { rerender } = render(view("account-a"));
    await screen.findByText("engine-A");
    await user.clear(screen.getByLabelText("Minimum cash balance threshold"));
    await user.type(screen.getByLabelText("Minimum cash balance threshold"), "77777");
    await user.click(screen.getByRole("button", { name: "Apply threshold" }));
    const previousSignal = getLadder.mock.calls[1][1]!.signal!;
    rerender(view("account-b"));
    expect(previousSignal.aborted).toBe(true);
    await act(async () => next.reject(new Error("Account B unavailable")));
    expect(screen.getByText("Cash ladder unavailable")).toBeInTheDocument();
    await act(async () => previous.resolve(ladder("LATE-A", 77777)));
    expect(screen.getByText("Cash ladder unavailable")).toBeInTheDocument();
    expect(screen.queryByText("engine-LATE-A")).not.toBeInTheDocument();
    expect(screen.queryByLabelText("Cash ladder bucket values")).not.toBeInTheDocument();
    expect(screen.getByLabelText("Minimum cash balance threshold")).toHaveValue(null);
  });

  it("invalidates evidence and drills on threshold changes, including failed requests", async () => {
    getLadder.mockResolvedValueOnce(ladder("A"));
    const changed = deferred<PortfolioCashLadder>();
    getLadder.mockReturnValueOnce(changed.promise);
    const user = userEvent.setup();
    render(view("account-a"));
    await screen.findByText("engine-A");
    await user.click(screen.getByRole("button", { name: /Week A/ }));
    await user.clear(screen.getByLabelText("Minimum cash balance threshold"));
    await user.type(screen.getByLabelText("Minimum cash balance threshold"), "88888");
    await user.click(screen.getByRole("button", { name: "Apply threshold" }));
    expect(screen.queryByText("Bond A")).not.toBeInTheDocument();
    expect(screen.queryByText("engine-A")).not.toBeInTheDocument();
    expect(screen.getByText("Loading cash ladder…")).toBeInTheDocument();
    await act(async () => changed.reject(new Error("Projection failed")));
    expect(screen.getByText("Cash ladder unavailable")).toBeInTheDocument();
    expect(screen.queryByLabelText("Cash ladder bucket values")).not.toBeInTheDocument();
  });

  it("clears account evidence when returning to the aggregate scope", async () => {
    getLadder.mockResolvedValueOnce(ladder("A"));
    const aggregate = deferred<PortfolioCashLadder>();
    getLadder.mockReturnValueOnce(aggregate.promise);
    const { rerender } = render(view("account-a"));
    await screen.findByText("engine-A");
    rerender(view());
    expect(screen.queryByText("engine-A")).not.toBeInTheDocument();
    expect(screen.getByLabelText("Minimum cash balance threshold")).toHaveValue(null);
    expect(getLadder).toHaveBeenLastCalledWith({ scenario: "base", minimumCash: undefined, fundAccountId: undefined }, expect.any(Object));
    await act(async () => aggregate.resolve(ladder("ALL", 30000)));
    expect(screen.getByText("engine-ALL")).toBeInTheDocument();
  });

  it("does not let a previous visit restore balances after an account round trip", async () => {
    const first = deferred<PortfolioCashLadder>();
    const current = deferred<PortfolioCashLadder>();
    getLadder.mockReturnValueOnce(first.promise).mockResolvedValueOnce(ladder("B")).mockReturnValueOnce(current.promise);
    const { rerender } = render(view("account-a"));
    rerender(view("account-b"));
    await screen.findByText("engine-B");
    rerender(view("account-a"));
    await act(async () => first.resolve(ladder("STALE-A", 99999)));
    expect(screen.getByText("Loading cash ladder…")).toBeInTheDocument();
    expect(screen.queryByText("engine-STALE-A")).not.toBeInTheDocument();
    expect(screen.getByLabelText("Minimum cash balance threshold")).toHaveValue(null);
    await act(async () => current.resolve(ladder("CURRENT-A", 30000)));
    expect(screen.getByText("engine-CURRENT-A")).toBeInTheDocument();
    expect(screen.getByLabelText("Minimum cash balance threshold")).toHaveValue(30000);
  });
});
