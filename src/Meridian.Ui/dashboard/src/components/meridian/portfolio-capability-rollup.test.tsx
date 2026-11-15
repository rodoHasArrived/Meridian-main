import { render, screen, waitFor } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { describe, expect, it, vi } from "vitest";
import type { BrokerageHouseholdPortfolio, BrokerageHouseholdPosition, InstrumentCapabilities, InstrumentCapabilityBatch } from "@/types";
import { buildObservedAccrualRollup, PortfolioCapabilityRollup } from "./portfolio-capability-rollup";

const securityId = "a070d0d4-42d2-4e22-9b2c-e983c1967fe1";
const asOf = "2026-11-14T00:00:00Z";
const blocked: InstrumentCapabilities = { securityId, securityVersion: 42, asOfUtc: asOf, capabilities: [{
  capability: "accrual", label: "Accrual", operation: "RecurringFixedIncomeAccrual", applicability: "Applicable", status: "Blocked", evaluationState: "Complete",
  securityVersion: 42, asOfUtc: asOf, context: "Selected definition.", reasons: [{ code: "RateUnitUndeclared", reason: "Coupon unit is undeclared.", remediationPath: "/accounting/security-master?rateRepair=queue" }]
}] };
const position = (accountId: string, id: string | null = securityId, symbol = "SYNTH") => ({
  fundAccountId: accountId, quantity: 100, symbol, positionId: null, security: id ? { securityId: id } : null
}) as BrokerageHouseholdPosition;
const portfolio = (positions: BrokerageHouseholdPosition[]): BrokerageHouseholdPortfolio => ({ providerId: "synthetic", asOf, totalCash: 0, totalEquity: 100,
  totalBuyingPower: 0, currency: "USD", accounts: [], positions, warnings: [] });

describe("PortfolioCapabilityRollup", () => {
  it("does not turn an unavailable holdings source into a zero-count assessment", () => {
    render(<MemoryRouter><PortfolioCapabilityRollup portfolio={null} accountId="all" scopeLabel="Loaded household" /></MemoryRouter>);
    expect(screen.getByText("Snapshot unavailable")).toBeInTheDocument();
    expect(screen.getByText(/holding eligibility has not been evaluated/)).toBeInTheDocument();
    expect(screen.queryByText(/0 holdings have known accrual blockers/)).not.toBeInTheDocument();
  });
  it("counts reason groups per holding and keeps unresolved identities under review", () => {
    const result = buildObservedAccrualRollup([
      { key: "a", securityId, symbol: "SYNTH", accountId: "a" },
      { key: "b", securityId, symbol: "SYNTH", accountId: "b" },
      { key: "c", securityId: null, symbol: "UNKNOWN", accountId: "a" }
    ], [blocked]);
    expect(result.blocked).toHaveLength(2);
    expect(result.reasons[0].count).toBe(2);
    expect(result.review).toBe(1);
    expect(result.unresolved).toBe(1);
  });

  it("deduplicates account/security observations and honors the selected account", async () => {
    const service = vi.fn().mockResolvedValue({ asOfUtc: asOf, securities: [blocked] });
    render(<MemoryRouter><PortfolioCapabilityRollup portfolio={portfolio([position("a"), position("a"), position("b")])} accountId="a" scopeLabel="Account A" service={service} /></MemoryRouter>);
    expect(await screen.findByText(/1 holding has known accrual blockers; 0 need/)).toBeInTheDocument();
    expect(screen.getByText("1 observed holding")).toBeInTheDocument();
    expect(service).toHaveBeenCalledOnce();
    expect(service.mock.calls[0][0]).toEqual([securityId]);
    expect(screen.getByText(/Loaded brokerage observations · Account A/)).toBeInTheDocument();
  });

  it("never turns fetch failure into Ready or zero known work", async () => {
    const service = vi.fn().mockRejectedValue(new Error("Synthetic outage"));
    render(<MemoryRouter><PortfolioCapabilityRollup portfolio={portfolio([position("a"), position("a", null, "UNRESOLVED")])} accountId="all" scopeLabel="Loaded household" service={service} /></MemoryRouter>);
    expect(await screen.findByText(/Some definitions could not be evaluated/)).toBeInTheDocument();
    expect(screen.getByText(/0 holdings have known accrual blockers; 2 need book and lot review/)).toBeInTheDocument();
    expect(screen.getByText(/1 holding has an unresolved Security Master identity/)).toBeInTheDocument();
    expect(screen.queryByText("Ready")).not.toBeInTheDocument();
  });

  it("aborts the old scope and ignores responses arriving after account selection changes", async () => {
    let finish!: (value: InstrumentCapabilityBatch) => void;
    const service = vi.fn().mockImplementationOnce(() => new Promise<InstrumentCapabilityBatch>((resolve) => { finish = resolve; }))
      .mockResolvedValue({ asOfUtc: asOf, securities: [] });
    const data = portfolio([position("a"), position("b", null, "UNRESOLVED")]);
    const { rerender } = render(<MemoryRouter><PortfolioCapabilityRollup portfolio={data} accountId="a" scopeLabel="Account A" service={service} /></MemoryRouter>);
    await waitFor(() => expect(service).toHaveBeenCalledOnce());
    const signal = service.mock.calls[0][1] as AbortSignal;
    rerender(<MemoryRouter><PortfolioCapabilityRollup portfolio={data} accountId="b" scopeLabel="Account B" service={service} /></MemoryRouter>);
    expect(signal.aborted).toBe(true);
    finish({ asOfUtc: asOf, securities: [blocked] });
    await waitFor(() => expect(screen.getByText(/0 holdings have known accrual blockers; 1 need/)).toBeInTheDocument());
  });
});
