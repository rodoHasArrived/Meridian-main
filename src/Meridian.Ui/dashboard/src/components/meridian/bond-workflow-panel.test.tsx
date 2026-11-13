import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { BondWorkflowPanel } from "./bond-workflow-panel";
import type { BondWorkflowRequest, BondWorkflowResult } from "@/types/bond-workflow.types";

function response(request: BondWorkflowRequest): BondWorkflowResult {
  return { securityId: request.securityId, context: { ...request.context, asOf: request.context.asOf.replace("Z", "+00:00") }, currency: "USD", modelVersion: "bond-workflow-v1", originalFace: request.originalFace, currentFace: "10000", accruedInterest: "-1.125", cleanConsideration: "9900", dirtyConsideration: "9898.875", fees: "0", totalCash: "9898.875", presentValue: "9901.123456789", yield: null, calibratedSpread: null, dv01: "1.25", cs01: "1.20", duration: "2.1", cashFlows: [{ paymentDate: "2027-01-01", accrualStart: "2026-07-01", accrualEnd: "2027-01-01", interest: "-5.25", principal: "10000", outstandingPrincipal: "10000", indexRatio: "1", isForecast: true, observations: [{ accrualDate: "2026-12-31", observationDate: "2026-12-29", days: 1, rate: "-0.001", isObserved: false, source: "Retained forward curve" }] }], inputs: [{ kind: "SecurityTerms", key: request.securityId, version: 7, contentSha256: "source-hash" }], issues: [{ operation: "Yield", code: "YieldUniquenessNotEstablished", reason: "Yield uniqueness could not be established.", blocksCalculation: false }], metrics: [{ name: "Yield", available: false, reason: "Multiple sign changes" }], isBlocked: false };
}
describe("BondWorkflowPanel", () => {
  afterEach(() => vi.unstubAllGlobals());
  it("keeps screening mounted when its economic date is cleared", () => {
    render(<BondWorkflowPanel securityId="bond-a" calculate={vi.fn()} />);
    fireEvent.change(screen.getByLabelText("Economic date and time (UTC)"), { target: { value: "" } });
    expect(screen.getByText("Find and screen comparison bonds")).toBeInTheDocument();
    expect(screen.getByLabelText("Economic date and time (UTC)")).toHaveValue("");
  });
  it("uses an explicit intraday UTC cutoff for retained quotes and preserves it on retry", async () => {
    const quote = { observationId: "quote-at-ten", recordedAtUtc: "2026-11-12T10:05:00Z", contentSha256: "quote-hash", input: {
      observationKey: "intraday-quote", revision: 1, kind: "Quote", providerId: "synthetic", seriesKey: "bond-a",
      economicDate: "2026-11-12", unit: "PercentOfPar", value: "99", sourceName: "Synthetic quote at 10:00 UTC",
      securityId: "bond-a", currency: "USD", quote: { category: "IndicativeQuote", priceBasis: "Clean", settlementDate: "2026-11-16", observedAtUtc: "2026-11-12T10:00:00Z" }
    } };
    const requests: BondWorkflowRequest[] = []; let screenRequest: { asOf: string } | undefined;
    vi.stubGlobal("fetch", vi.fn(async (url: RequestInfo | URL, init?: RequestInit) => {
      if (String(url).includes("market-observations")) return new Response(JSON.stringify([quote]));
      if (String(url).endsWith("/screen")) {
        screenRequest = JSON.parse(String(init?.body));
        return new Response(JSON.stringify({ rows: [], nextCursor: null, gaps: [], sourcePagesRead: 1, referenceKnowledgeTimeVerified: true, limitations: [] }));
      }
      const request = JSON.parse(String(init?.body)) as BondWorkflowRequest; requests.push(request);
      return requests.length === 1 ? new Response('{"detail":"Temporary retained input failure"}', { status: 503 })
        : new Response(JSON.stringify(response(request)));
    }));
    render(<BondWorkflowPanel securityId="bond-a" />);
    const economic = screen.getByLabelText("Economic date and time (UTC)");
    expect(economic).toHaveAttribute("type", "datetime-local");
    expect(economic).toHaveAccessibleDescription(/Enter UTC/);
    fireEvent.change(economic, { target: { value: "2026-11-12T11:15:30" } });
    fireEvent.change(screen.getByLabelText("Knowledge cutoff (UTC, optional)"), { target: { value: "2026-11-13T12:00" } });
    fireEvent.change(screen.getByLabelText("Comparison settlement"), { target: { value: "2026-11-16" } });
    fireEvent.click(screen.getByRole("button", { name: "Load retained market inputs" }));
    fireEvent.click(await screen.findByRole("button", { name: "Use retained quote" }));
    const calculate = screen.getByRole("button", { name: "Calculate purchase and comparison" });
    fireEvent.click(calculate); await screen.findByText("Temporary retained input failure");
    fireEvent.click(calculate); await screen.findByRole("region", { name: "Bond workflow result" });
    expect(requests[0]).toMatchObject({ context: { asOf: "2026-11-12T11:15:30Z", settlementDate: "2026-11-16", knownAt: "2026-11-13T12:00:00Z" }, quoteObservationId: "quote-at-ten", cleanPricePer100: null, quoteSettlementDate: "2026-11-16" });
    expect(Date.parse(requests[0].context.asOf)).toBeGreaterThan(Date.parse(quote.input.quote.observedAtUtc));
    expect(requests[1]).toEqual(requests[0]);
    fireEvent.click(screen.getByRole("button", { name: "Screen comparison bonds" }));
    await waitFor(() => expect(screenRequest?.asOf).toBe("2026-11-12"));
  });
  it("does not advance economic or knowledge cutoffs when a future retained quote is selected", async () => {
    const quote = { observationId: "future-quote", recordedAtUtc: "2026-11-13T10:05:00Z", contentSha256: "future-hash", input: {
      observationKey: "future-quote", revision: 1, kind: "Quote", providerId: "synthetic", seriesKey: "bond-a",
      economicDate: "2026-11-13", unit: "PercentOfPar", value: "99", sourceName: "Next-day quote",
      securityId: "bond-a", currency: "USD", quote: { category: "IndicativeQuote", priceBasis: "Clean", settlementDate: "2026-11-16", observedAtUtc: "2026-11-13T10:00:00Z" }
    } };
    let submitted: BondWorkflowRequest | undefined;
    vi.stubGlobal("fetch", vi.fn(async (url: RequestInfo | URL, init?: RequestInit) => {
      if (String(url).includes("market-observations")) return new Response(JSON.stringify([quote]));
      submitted = JSON.parse(String(init?.body)) as BondWorkflowRequest;
      return new Response(JSON.stringify({ ...response(submitted), isBlocked: true, issues: [{ operation: "Quote", code: "RetainedQuoteUnavailable", reason: "The selected quote is outside the economic cutoff.", blocksCalculation: true }] }));
    }));
    render(<BondWorkflowPanel securityId="bond-a" />);
    const economic = screen.getByLabelText("Economic date and time (UTC)");
    const knowledge = screen.getByLabelText("Knowledge cutoff (UTC, optional)");
    fireEvent.change(economic, { target: { value: "2026-11-12T11:15:30" } });
    fireEvent.change(knowledge, { target: { value: "2026-11-14T12:00" } });
    fireEvent.click(screen.getByRole("button", { name: "Load retained market inputs" }));
    fireEvent.click(await screen.findByRole("button", { name: "Use retained quote" }));
    fireEvent.click(screen.getByRole("button", { name: "Calculate purchase and comparison" }));
    await screen.findByText("Calculation blocked");
    expect(submitted?.context).toMatchObject({ asOf: "2026-11-12T11:15:30Z", knownAt: "2026-11-14T12:00:00Z" });
    expect(economic).toHaveValue("2026-11-12T11:15:30");
    expect(knowledge).toHaveValue("2026-11-14T12:00");
    expect(screen.getByText(/The selected quote is outside the economic cutoff/)).toBeInTheDocument();
  });
  it("submits exact face and price through keyboard and retains partial signed results", async () => {
    const user = userEvent.setup(); const calculate = vi.fn(async (request: BondWorkflowRequest) => response(request));
    render(<BondWorkflowPanel securityId="bond-a" calculate={calculate} />);
    fireEvent.change(screen.getByLabelText("Clean quote / 100 current face"), { target: { value: "99.123456789012345678" } });
    screen.getByRole("button", { name: "Calculate purchase and comparison" }).focus(); await user.keyboard("{Enter}");
    await screen.findByRole("region", { name: "Bond workflow result" });
    expect(calculate.mock.calls[0][0]).toMatchObject({ originalFace: "10000", cleanPricePer100: "99.123456789012345678" });
    expect(screen.getByText("9,901.123456789")).toBeInTheDocument(); expect(screen.getByText("-5.25")).toBeInTheDocument();
    expect(screen.getByText(/Yield uniqueness could not be established/)).toBeInTheDocument();
    expect(screen.getByText("Includes forecasts")).toBeInTheDocument();
  });
  it("aborts and clears stale calculations when any reviewed input changes", async () => {
    let resolve!: (result: BondWorkflowResult) => void;
    const calculate = vi.fn((request: BondWorkflowRequest) => new Promise<BondWorkflowResult>((done) => { resolve = () => done(response(request)); }));
    render(<BondWorkflowPanel securityId="bond-a" calculate={calculate} />);
    fireEvent.click(screen.getByRole("button", { name: "Calculate purchase and comparison" }));
    await waitFor(() => expect(calculate).toHaveBeenCalledOnce());
    fireEvent.change(screen.getByLabelText("Original face amount"), { target: { value: "20000" } });
    await act(async () => resolve(response(calculate.mock.calls[0][0])));
    expect(screen.queryByRole("region", { name: "Bond workflow result" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Calculate purchase and comparison" })).toBeEnabled();
  });
  it("keeps failed alternative rows visible without discarding successful calculations", async () => {
    const calculate = vi.fn(async (request: BondWorkflowRequest) => { if (request.securityId === "bond-b") throw new Error("Missing fixing"); return response(request); });
    render(<BondWorkflowPanel securityId="bond-a" calculate={calculate} />);
    fireEvent.change(screen.getByLabelText(/One security ID and clean price/), { target: { value: "bond-b, 101" } });
    fireEvent.click(screen.getByRole("button", { name: "Calculate purchase and comparison" }));
    await screen.findByText("Missing fixing");
    expect(calculate).toHaveBeenCalledTimes(2); expect(screen.getByRole("region", { name: "Bond workflow result" })).toBeInTheDocument();
    expect(within(screen.getByRole("table", { name: /Bond comparison/ })).getByText("bond-b")).toBeInTheDocument();
  });
  it("rejects a response for a different security", async () => {
    const calculate = vi.fn(async (request: BondWorkflowRequest) => ({ ...response(request), securityId: "different-bond" }));
    render(<BondWorkflowPanel securityId="bond-a" calculate={calculate} />);
    fireEvent.click(screen.getByRole("button", { name: "Calculate purchase and comparison" }));
    await screen.findByRole("alert"); expect(screen.getByText(/Response security does not match/)).toBeInTheDocument();
    expect(screen.queryByRole("region", { name: "Bond workflow result" })).not.toBeInTheDocument();
  });
});
