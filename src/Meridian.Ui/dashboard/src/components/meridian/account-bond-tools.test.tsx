import { fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { AccountBondTools } from "./account-bond-tools";
import type { BondPortfolioRiskResult } from "@/types/bond-workflow.types";

const result: BondPortfolioRiskResult = { scope: { tenantId: "tenant", companyId: "company", accountId: "account", bookId: "book", baseCurrency: "USD" }, holdingsBatchId: "batch", holdingsContentHash: "hash", positions: [], buckets: [{ dimension: "Issuer", key: "Synthetic issuer", currency: "USD", holdings: 2, coveredHoldings: 1, knownPresentValue: "10000", knownDv01: "-1.5", knownCs01: "2.1" }], issues: ["One holding lacks discount inputs"], isComplete: false };
afterEach(() => { vi.unstubAllGlobals(); vi.useRealTimers(); });
describe("AccountBondTools", () => {
  it("sends an explicit UTC cutoff through the real API adapter and retains it for exact retries", async () => {
    vi.useFakeTimers({ toFake: ["Date"] });
    vi.setSystemTime(new Date("2026-11-12T10:15:30.000Z"));
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(new Response('{"detail":"Retained inputs temporarily unavailable"}', { status: 503 }))
      .mockImplementation(() => Promise.resolve(new Response(JSON.stringify(result), { status: 200 })));
    vi.stubGlobal("fetch", fetchMock);
    render(<AccountBondTools bookId="book" accountId="account" asOf="2026-11-12" />);
    const calculate = screen.getByRole("button", { name: "Calculate account rate and credit risk" });
    fireEvent.click(calculate);
    await screen.findByRole("alert");
    const firstRequest = JSON.parse(fetchMock.mock.calls[0][1].body);
    expect(fetchMock.mock.calls[0][0]).toBe("/api/ledger/books/book/accounts/account/bond-risk");
    expect(firstRequest.context).toEqual({ asOf: "2026-11-12T00:00:00Z", settlementDate: "2026-11-12", knownAt: "2026-11-12T10:15:30.000Z" });
    expect(firstRequest.runId).toMatch(/^[a-f\d-]{36}$/);

    vi.setSystemTime(new Date("2026-11-12T11:15:30.000Z"));
    fireEvent.click(calculate);
    await screen.findByRole("region", { name: "Account bond risk result" });
    expect(JSON.parse(fetchMock.mock.calls[1][1].body)).toEqual(firstRequest);
    fireEvent.click(calculate);
    await screen.findByRole("region", { name: "Account bond risk result" });
    expect(JSON.parse(fetchMock.mock.calls[2][1].body)).toEqual(firstRequest);

    fireEvent.change(screen.getByLabelText("Parallel rate shock (bp)"), { target: { value: "50" } });
    fireEvent.click(calculate);
    await screen.findByRole("region", { name: "Account bond risk result" });
    const changedRequest = JSON.parse(fetchMock.mock.calls[3][1].body);
    expect(changedRequest.context.knownAt).toBe("2026-11-12T11:15:30.000Z");
    expect(changedRequest.runId).not.toBe(firstRequest.runId);
  });
  it("reopens a retained risk scenario and preserves its recorded calculation context", async () => {
    const savedContext = { asOf: "2025-11-12T00:00:00Z", settlementDate: "2025-11-12", knownAt: "2025-11-12T12:00:00Z" };
    const saved = { ...result, context: savedContext, runId: "retained-risk" };
    const load = vi.fn(async () => ({ request: { context: savedContext, parallelShiftBps: "50", spreadWideningBps: "25", runId: "retained-risk" }, result: saved }));
    const calculate = vi.fn(async () => saved);
    render(<AccountBondTools bookId="book" accountId="account" asOf="2026-11-12" loadRisk={load} calculateRisk={calculate} />);
    fireEvent.change(screen.getByLabelText("Retained risk scenario ID"), { target: { value: "retained-risk" } });
    fireEvent.click(screen.getByRole("button", { name: "Reopen retained risk scenario" }));
    await screen.findByRole("region", { name: "Account bond risk result" });
    fireEvent.click(screen.getByRole("button", { name: "Calculate account rate and credit risk" }));
    await screen.findByRole("region", { name: "Account bond risk result" });
    expect(calculate).toHaveBeenCalledWith("book", "account", expect.objectContaining({ context: savedContext, runId: "retained-risk" }), expect.any(Object));
  });
  it("passes dated steepening pillars and labels incomplete exposure totals", async () => {
    const calculate = vi.fn(async () => result);
    render(<AccountBondTools bookId="book" accountId="account" asOf="2026-11-12" calculateRisk={calculate} />);
    fireEvent.change(screen.getByLabelText(/Optional curve-steepening pillars/), { target: { value: "2027-11-12, -50\n2036-11-12, 50" } });
    fireEvent.click(screen.getByRole("button", { name: "Calculate account rate and credit risk" }));
    await screen.findByRole("region", { name: "Account bond risk result" });
    expect(calculate.mock.calls[0]).toHaveLength(4);
    expect(calculate).toHaveBeenCalledWith("book", "account", expect.objectContaining({ steepeningPillars: [{ date: "2027-11-12", shiftBps: "-50" }, { date: "2036-11-12", shiftBps: "50" }] }), expect.objectContaining({ allowDevelopmentFallback: false }));
    expect(screen.getByText(/Partial calculation coverage/)).toBeInTheDocument(); expect(screen.getByText("-1.5")).toBeInTheDocument(); expect(screen.getByText("1 / 2")).toBeInTheDocument();
  });
  it("reuses a generated statement batch ID when retrying a reviewed import", async () => {
    const accept = vi.fn().mockRejectedValueOnce(new Error("Temporary unavailable")).mockResolvedValueOnce({ batchId: "accepted" });
    render(<AccountBondTools bookId="book" accountId="account" asOf="2026-11-12" acceptHoldings={accept} />);
    fireEvent.change(screen.getByLabelText("Reviewed connection ID"), { target: { value: "connection" } });
    fireEvent.change(screen.getByLabelText("Reviewed sync run ID"), { target: { value: "reviewed-run" } });
    fireEvent.click(screen.getByRole("button", { name: "Accept reviewed holding snapshot" })); await screen.findByRole("alert");
    const firstId = accept.mock.calls[0][2].batchId;
    fireEvent.click(screen.getByRole("button", { name: "Accept reviewed holding snapshot" })); await screen.findByRole("status");
    expect(accept.mock.calls[1][2].batchId).toBe(firstId); expect(firstId).toMatch(/^[a-f\d-]{36}$/);
  });
});
