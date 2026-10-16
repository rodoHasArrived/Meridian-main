import { act, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { axe } from "jest-axe";
import { afterEach, describe, expect, it, vi } from "vitest";
import { getPortfolioCashLadder } from "@/lib/api/portfolio-cash-ladder.api";
import { CashLadderScreen } from "@/screens/cash-ladder-screen";
import type { PortfolioCashLadder } from "@/types/portfolio-cash-ladder.types";

vi.mock("@/lib/api/portfolio-cash-ladder.api", () => ({ getPortfolioCashLadder: vi.fn() }));
const getLadder = vi.mocked(getPortfolioCashLadder);

function ladder(account: string, minimumCashThreshold = 10000): PortfolioCashLadder {
  return {
    engineVersion: `${account}-engine`, asOfDate: "2026-07-05", horizonDays: 7,
    baseCurrency: "USD", scenarioId: "base", openingCash: 50000, minimumCashThreshold,
    buckets: [{ bucketStart: "2026-07-05", bucketEnd: "2026-07-11", label: "Jul 5 – Jul 11",
      inflows: 3500, outflows: 0, netFlow: 3500, cumulativeCash: 53500, breachesMinimumBalance: false,
      sources: [{ source: "Coupons & interest", amount: 3500, flowCount: 1 }] }],
    contributions: [{ securityId: "sec-1", displayName: `${account} bond`, assetClass: "Bond",
      projectionRunId: `${account}-projection`, projectionEngineVersion: "projection-v1", termsVersionId: `${account}-terms`,
      sourceDomain: "SecurityMaster", sourceEntityId: "sec-1", flowType: "Coupon", sourceLane: "Coupons & interest",
      dueDate: "2026-07-10", amount: 3500, currency: "USD", baseAmount: 3500 }],
    availableScenarios: [
      { scenarioId: "base", displayName: "Base projection", description: `${account} base scenario`, modeledEffects: [], assumptions: [] },
      { scenarioId: "early-call", displayName: "Early call", description: "Calls exercised", modeledEffects: [], assumptions: [] }
    ],
    assumptions: [`${account} assumption`], warnings: [], generatedAt: "2026-07-05T09:00:00Z",
    securitiesEvaluated: 1, securitiesWithFlows: 1
  };
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (error: Error) => void;
  const promise = new Promise<T>((resolve_, reject_) => { resolve = resolve_; reject = reject_; });
  return { promise, resolve, reject };
}

function screenFor(account: string) {
  return <MemoryRouter><CashLadderScreen fundAccountId={account} /></MemoryRouter>;
}

afterEach(() => vi.resetAllMocks());

describe("Cash Ladder scoped evidence", () => {
  it("clears balances, thresholds, scenario, and drill selection during a delayed account switch", async () => {
    const next = deferred<PortfolioCashLadder>();
    getLadder.mockResolvedValueOnce(ladder("account-a"));
    const user = userEvent.setup();
    const { rerender, container } = render(screenFor("account-a"));
    await screen.findByText("account-a-engine");
    await user.click(screen.getByRole("button", { name: /Jul 5.*net/i }));
    expect(screen.getByText("account-a bond")).toBeInTheDocument();
    const input = screen.getByLabelText("Minimum cash balance threshold");
    await user.clear(input);
    await user.type(input, "15000");
    getLadder.mockResolvedValueOnce(ladder("account-a", 15000));
    await user.click(screen.getByRole("button", { name: "Apply threshold" }));
    await waitFor(() => expect(getLadder).toHaveBeenLastCalledWith(
      { fundAccountId: "account-a", scenario: "base", minimumCash: 15000 }, expect.anything()));
    await screen.findByText("account-a-engine");
    getLadder.mockResolvedValueOnce({ ...ladder("account-a", 15000), scenarioId: "early-call" });
    await user.selectOptions(screen.getByLabelText("Stress scenario"), "early-call");
    await screen.findByText("account-a-engine");
    await user.click(screen.getByRole("button", { name: /Jul 5.*net/i }));
    getLadder.mockReturnValueOnce(next.promise);

    rerender(screenFor("account-b"));
    expect(screen.getByText("Loading cash ladder…")).toBeInTheDocument();
    expect(screen.queryByText("Opening cash")).not.toBeInTheDocument();
    expect(screen.queryByText("account-a-engine")).not.toBeInTheDocument();
    expect(screen.queryByText("account-a bond")).not.toBeInTheDocument();
    expect(screen.queryByText("account-a assumption")).not.toBeInTheDocument();
    expect(screen.getByLabelText("Minimum cash balance threshold")).toHaveValue(null);
    expect(screen.getByLabelText("Stress scenario")).toHaveValue("base");
    expect(getLadder).toHaveBeenLastCalledWith({ fundAccountId: "account-b", scenario: "base", minimumCash: undefined }, expect.anything());

    await act(async () => next.resolve(ladder("account-b", 2500)));
    expect(screen.getByText("account-b-engine")).toBeInTheDocument();
    expect(screen.getByLabelText("Minimum cash balance threshold")).toHaveValue(2500);
    expect(screen.queryByText("account-b bond")).not.toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: /Jul 5.*net/i }));
    expect(screen.getByText("account-b bond")).toBeInTheDocument();
    expect((await axe(container)).violations).toEqual([]);
  });

  it("keeps prior evidence hidden when the new account request fails", async () => {
    const next = deferred<PortfolioCashLadder>();
    getLadder.mockResolvedValueOnce(ladder("account-a")).mockReturnValueOnce(next.promise);
    const { rerender } = render(screenFor("account-a"));
    await screen.findByText("account-a-engine");
    rerender(screenFor("account-b"));
    await act(async () => next.reject(new Error("account unavailable")));
    expect(screen.getByText("Cash ladder unavailable")).toBeInTheDocument();
    expect(screen.queryByText("account-a-engine")).not.toBeInTheDocument();
    expect(screen.queryByText("Opening cash")).not.toBeInTheDocument();
    expect(screen.getByLabelText("Minimum cash balance threshold")).toHaveValue(null);
  });

  it("ignores late responses after switching accounts, including a return to the original account", async () => {
    const first = deferred<PortfolioCashLadder>();
    const second = deferred<PortfolioCashLadder>();
    const third = deferred<PortfolioCashLadder>();
    getLadder.mockReturnValueOnce(first.promise).mockReturnValueOnce(second.promise).mockReturnValueOnce(third.promise);
    const { rerender } = render(screenFor("account-a"));
    const firstSignal = getLadder.mock.calls[0][1]?.signal;
    rerender(screenFor("account-b"));
    rerender(screenFor("account-a"));
    expect(firstSignal?.aborted).toBe(true);
    await act(async () => third.resolve(ladder("latest-a", 4000)));
    await act(async () => {
      first.resolve(ladder("old-a", 10000));
      second.resolve(ladder("old-b", 20000));
    });
    expect(screen.getByText("latest-a-engine")).toBeInTheDocument();
    expect(screen.queryByText("old-a-engine")).not.toBeInTheDocument();
    expect(screen.queryByText("old-b-engine")).not.toBeInTheDocument();
    expect(screen.getByLabelText("Minimum cash balance threshold")).toHaveValue(4000);
  });

  it("hides prior evidence and drill selection while applying a threshold and after a failed refresh", async () => {
    const next = deferred<PortfolioCashLadder>();
    getLadder.mockResolvedValueOnce(ladder("account-a")).mockReturnValueOnce(next.promise);
    const user = userEvent.setup();
    render(screenFor("account-a"));
    await screen.findByText("account-a-engine");
    await user.click(screen.getByRole("button", { name: /Jul 5.*net/i }));
    await user.clear(screen.getByLabelText("Minimum cash balance threshold"));
    await user.type(screen.getByLabelText("Minimum cash balance threshold"), "75000");
    await user.click(screen.getByRole("button", { name: "Apply threshold" }));
    expect(screen.queryByText("Opening cash")).not.toBeInTheDocument();
    expect(screen.queryByText("account-a bond")).not.toBeInTheDocument();
    await act(async () => next.reject(new Error("projection failed")));
    expect(screen.getByText("Cash ladder unavailable")).toBeInTheDocument();
    expect(screen.queryByText("account-a-engine")).not.toBeInTheDocument();
    expect(screen.queryByText("Opening cash")).not.toBeInTheDocument();
  });
});
