import { act, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";
import { axe } from "jest-axe";
import { getPortfolioCashLadder } from "@/lib/api/portfolio-cash-ladder.api";
import { getAssetOperations } from "@/lib/api";
import { CashLadderScreen } from "./cash-ladder-screen";
import type { AssetOperationsDetail } from "@/types";
import type { PortfolioCashLadder } from "@/types/portfolio-cash-ladder.types";

vi.mock("@/lib/api/portfolio-cash-ladder.api", () => ({ getPortfolioCashLadder: vi.fn() }));
vi.mock("@/lib/api", () => ({ getAssetOperations: vi.fn() }));
const getLadder = vi.mocked(getPortfolioCashLadder);
const getOperations = vi.mocked(getAssetOperations);
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
  it("links an instrument and its source evidence and inspects the exact Asset Operations run", async () => {
    const projection = ladder("A");
    Object.assign(projection.contributions[0], { projectionRunId: "run-original", projectionEngineVersion: "engine-original",
      termsVersionId: "terms-original", termsHash: "hash-original", sourceEntityId: "bond" });
    getLadder.mockResolvedValue(projection);
    getOperations.mockResolvedValue(operations());
    const user = userEvent.setup();
    const { container } = render(view("account-a"));
    await user.click(await screen.findByRole("button", { name: /Week A/ }));
    const table = within(screen.getByRole("table", { name: "Cash ladder contributing flows" }));
    expect(table.getByRole("link", { name: "Bond A" })).toHaveAttribute("href", "/portfolio/asset-detail?securityId=bond");
    expect(table.getByRole("link", { name: "Security Master evidence" })).toHaveAttribute("href", "/accounting/security-master?securityId=bond&tab=evidence");
    await user.click(table.getByRole("button", { name: "Inspect projection run for Bond A" }));
    const evidence = within(screen.getByRole("region", { name: "Projection evidence for Bond A" }));
    await evidence.findByText("Matched terms evidence");
    expect(evidence.getByText("run-original")).toBeInTheDocument();
    expect(evidence.getByText("Retained original bond terms")).toBeInTheDocument();
    expect(evidence.queryByText("Unrelated current terms")).not.toBeInTheDocument();
    expect(getOperations).toHaveBeenCalledWith("bond", expect.objectContaining({ allowDevelopmentFallback: false, signal: expect.any(AbortSignal) }));
    expect((await axe(container)).violations).toEqual([]);
    await user.click(evidence.getByRole("button", { name: "Close projection evidence" }));
    expect(screen.queryByRole("region", { name: "Projection evidence for Bond A" })).not.toBeInTheDocument();
  });

  it("withholds a newer run and reports that the contribution's exact run is unavailable", async () => {
    const projection = ladder("A");
    Object.assign(projection.contributions[0], { projectionRunId: "missing-run", projectionEngineVersion: "engine-original" });
    getLadder.mockResolvedValue(projection);
    getOperations.mockResolvedValue(operations());
    const user = userEvent.setup();
    render(view("account-a"));
    await user.click(await screen.findByRole("button", { name: /Week A/ }));
    await user.click(screen.getByRole("button", { name: "Inspect projection run for Bond A" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Exact projection evidence unavailable");
    expect(screen.getByRole("alert")).toHaveTextContent("a newer run cannot substitute");
    expect(screen.queryByText("Matched terms evidence")).not.toBeInTheDocument();
  });

  it("does not link capital activity to an invented instrument or projection", async () => {
    const projection = ladder("A");
    Object.assign(projection.contributions[0], { securityId: "investor-reference", assetClass: "CapitalActivity", sourceDomain: "PartnershipLedger", sourceEntityId: "investor-reference", projectionRunId: null });
    getLadder.mockResolvedValue(projection);
    const user = userEvent.setup();
    render(view("account-a"));
    await user.click(await screen.findByRole("button", { name: /Week A/ }));
    const table = within(screen.getByRole("table", { name: "Cash ladder contributing flows" }));
    expect(table.queryByRole("link")).not.toBeInTheDocument();
    expect(table.queryByRole("button")).not.toBeInTheDocument();
    expect(table.getByText("PartnershipLedger")).toBeInTheDocument();
    expect(table.getByText("investor-reference")).toBeInTheDocument();
  });

  it("preserves the exact run but does not substitute a current terms version", async () => {
    const projection = ladder("A");
    Object.assign(projection.contributions[0], { projectionRunId: "run-original", projectionEngineVersion: "engine-original", termsVersionId: "terms-missing", termsHash: "hash-missing" });
    getLadder.mockResolvedValue(projection);
    getOperations.mockResolvedValue(operations());
    const user = userEvent.setup();
    render(view("account-a"));
    await user.click(await screen.findByRole("button", { name: /Week A/ }));
    await user.click(screen.getByRole("button", { name: "Inspect projection run for Bond A" }));
    await screen.findByText("Exact terms evidence unavailable");
    expect(screen.getByText("Run status")).toBeInTheDocument();
    expect(screen.queryByText("Matched terms evidence")).not.toBeInTheDocument();
    expect(screen.queryByText("Unrelated current terms")).not.toBeInTheDocument();
  });

  it("retains the source's blocker explanation on an exact blocked projection run", async () => {
    const projection = ladder("A");
    Object.assign(projection.contributions[0], { projectionRunId: "run-original", projectionEngineVersion: "engine-original" });
    const source = operations();
    Object.assign(source.cashFlowProjectionRuns[0], { status: "Blocked", blocker: { code: "CurveUnavailable", reason: "The projection requires the retained curve version." } });
    getLadder.mockResolvedValue(projection);
    getOperations.mockResolvedValue(source);
    const user = userEvent.setup();
    render(view("account-a"));
    await user.click(await screen.findByRole("button", { name: /Week A/ }));
    await user.click(screen.getByRole("button", { name: "Inspect projection run for Bond A" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("CurveUnavailable");
    expect(screen.getByRole("alert")).toHaveTextContent("The projection requires the retained curve version.");
  });

  it("aborts projection evidence on account change and ignores its late response", async () => {
    const projection = ladder("A");
    Object.assign(projection.contributions[0], { projectionRunId: "run-original", projectionEngineVersion: "engine-original" });
    getLadder.mockResolvedValueOnce(projection).mockResolvedValueOnce(ladder("B"));
    const pending = deferred<AssetOperationsDetail>();
    getOperations.mockReturnValue(pending.promise);
    const user = userEvent.setup();
    const { rerender } = render(view("account-a"));
    await user.click(await screen.findByRole("button", { name: /Week A/ }));
    await user.click(screen.getByRole("button", { name: "Inspect projection run for Bond A" }));
    const signal = getOperations.mock.calls[0][1]!.signal!;
    rerender(view("account-b"));
    expect(signal.aborted).toBe(true);
    await act(async () => pending.resolve(operations()));
    expect(screen.queryByRole("region", { name: "Projection evidence for Bond A" })).not.toBeInTheDocument();
    expect(screen.queryByText("Retained original bond terms")).not.toBeInTheDocument();
  });
  it("exposes keyboard bucket selection within a named chart group", async () => {
    getLadder.mockResolvedValue(ladder("A"));
    const user = userEvent.setup();
    render(view("account-a"));
    const chart = await screen.findByRole("group", { name: /Portfolio cash ladder: stacked/ });
    const bucket = within(chart).getByRole("button", { name: /Week A/ });
    expect(bucket).toHaveAttribute("aria-pressed", "false");
    bucket.focus();
    await user.keyboard("{Enter}");
    expect(bucket).toHaveAttribute("aria-pressed", "true");
    expect(screen.getByText("Bond A")).toBeInTheDocument();
    await user.keyboard(" ");
    expect(bucket).toHaveAttribute("aria-pressed", "false");
    expect(screen.queryByText("Bond A")).not.toBeInTheDocument();
  });

  it("retains scenario choices through a delayed and failed refresh without retaining balances", async () => {
    const pending = deferred<PortfolioCashLadder>();
    getLadder.mockResolvedValueOnce(ladder("A")).mockReturnValueOnce(pending.promise)
      .mockResolvedValue(ladder("RECOVERED"));
    const user = userEvent.setup();
    render(view("account-a"));
    await screen.findByText("engine-A");
    await user.selectOptions(screen.getByLabelText("Stress scenario"), "early-call");
    expect(screen.getByLabelText("Stress scenario")).toHaveValue("early-call");
    expect(screen.getByRole("option", { name: "Early call" })).toBeInTheDocument();
    expect(screen.queryByText("engine-A")).not.toBeInTheDocument();
    await act(async () => pending.reject(new Error("Projection unavailable")));
    expect(screen.getByLabelText("Stress scenario")).toHaveValue("early-call");
    expect(screen.getByRole("option", { name: "Early call" })).toBeInTheDocument();
    expect(screen.queryByText("engine-A")).not.toBeInTheDocument();
    await user.selectOptions(screen.getByLabelText("Stress scenario"), "base");
    await screen.findByText("engine-RECOVERED");
    expect(getLadder).toHaveBeenLastCalledWith({ scenario: "base", minimumCash: undefined, fundAccountId: "account-a" }, expect.any(Object));
  });

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

function operations(): AssetOperationsDetail {
  return {
    subject: { securityId: "bond", assetClass: "Bond", displayName: "Bond A", primaryIdentifier: null, operationalProfile: [] },
    termsHistory: [
      { securityId: "bond", termsVersionId: "terms-original", termsHash: "hash-original", versionNumber: 1, effectiveDate: "2026-01-01", recordedAt: "2026-01-01T12:00:00Z", sourceDomain: "SecurityMaster", sourceEntityId: "bond", summary: "Retained original bond terms" },
      { securityId: "bond", termsVersionId: "terms-current", termsHash: "hash-current", versionNumber: 2, effectiveDate: "2026-02-01", recordedAt: "2026-02-01T12:00:00Z", sourceDomain: "SecurityMaster", sourceEntityId: "bond", summary: "Unrelated current terms" }
    ],
    cashFlowProjectionRuns: [{ projectionRunId: "run-original", securityId: "bond", projectionAsOf: "2026-07-05", engineVersion: "engine-original", status: "Completed", generatedAt: "2026-07-05T12:00:00Z", sourceDomain: "SecurityMaster", sourceEntityId: "bond" }],
    lifecycleEvents: [], projectedCashFlows: [], actualActivity: [], reconciliationRuns: [], reconciliationResults: [], ledgerProjections: [], workflowAudit: [],
    readiness: { securityId: "bond", status: "ReviewRequired", capabilities: [], readyCapabilities: [], missingCapabilities: [], blockers: [], evaluatedAt: "2026-07-05T12:00:00Z", sourceDomain: "SecurityMaster", sourceEntityId: "bond" }
  };
}
