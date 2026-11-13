import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { BondMarketInputsPanel } from "./bond-market-inputs-panel";
import type { BondMarketObservation } from "@/types/bond-workflow.types";

const quote: BondMarketObservation = { observationId: "quote-source-revision", recordedAtUtc: "2026-11-12T12:00:00Z", contentSha256: "hash", input: { observationKey: "quote", revision: 2, kind: "Quote", providerId: "synthetic", seriesKey: "bond", economicDate: "2026-11-12", unit: "PercentOfPar", value: "99.123456789", sourceName: "Synthetic pricing evidence", securityId: "bond", currency: "USD", quote: { category: "IndicativeQuote", priceBasis: "Clean", settlementDate: "2026-11-16", providerYield: "0.045", providerYieldConvention: "NominalPeriodic" } } };
describe("BondMarketInputsPanel", () => {
  it("shows quote category and provider yield, and selects an exact retained observation", async () => {
    const selected = vi.fn(); const query = vi.fn(async () => [quote]); render(<BondMarketInputsPanel securityId="bond" onQuoteSelected={selected} query={query} />);
    fireEvent.click(screen.getByRole("button", { name: "Load retained market inputs" })); await screen.findByText(/IndicativeQuote/);
    expect(screen.getByText(/Provider yield 0.045/)).toBeInTheDocument(); fireEvent.click(screen.getByRole("button", { name: "Use retained quote" }));
    expect(selected).toHaveBeenCalledWith(quote);
  });
  it("blocks malformed evidence before sending an import", async () => {
    const importer = vi.fn(); render(<BondMarketInputsPanel securityId="bond" onQuoteSelected={vi.fn()} importObservations={importer} />);
    fireEvent.change(screen.getByLabelText(/JSON observations with provider record/), { target: { value: '{"observations":[]}' } });
    fireEvent.click(screen.getByRole("button", { name: "Import retained market observations" })); await screen.findByRole("alert"); expect(importer).not.toHaveBeenCalled();
  });
});
