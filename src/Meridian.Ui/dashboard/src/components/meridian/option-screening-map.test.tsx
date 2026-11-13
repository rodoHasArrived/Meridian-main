import { fireEvent, render, screen } from "@testing-library/react";
import { axe } from "jest-axe";
import { describe, expect, it, vi } from "vitest";
import { OptionScreeningMap } from "./option-screening-map";
import type { CoveredCallChainPreview } from "@/types/covered-call.types";

const preview: CoveredCallChainPreview = {
  underlyingSymbol: "SPY", asOf: "2026-11-12", underlyingPrice: 520,
  totalContractsScanned: 3, filtersPassed: 1, coverageKnown: true,
  requestedExpirations: ["2026-12-12", "2027-01-12"], loadedExpirations: ["2026-12-12"],
  snapshotTimes: ["2026-11-01T12:00:00Z"], maxStrikesPerExpiration: 80,
  candidates: [
    { strike: 525, expiration: "2026-12-12", daysToExpiration: 30, bid: 2, ask: 2.05, delta: .3, impliedVolatility: .2, openInterest: 2000, volume: 200, meetsAllFilters: true, rejectReason: null },
    { strike: 530, expiration: "2026-12-12", daysToExpiration: 30, bid: 1, ask: 2, delta: .2, impliedVolatility: .2, openInterest: 20, volume: 10, meetsAllFilters: false, rejectReason: "Spread too wide" },
    { strike: 535, expiration: "2026-12-12", daysToExpiration: 30, bid: 1, ask: 2, delta: 0, deltaAvailable: false, impliedVolatility: null, openInterest: 20, volume: 10, meetsAllFilters: false, rejectReason: "Delta unavailable" }
  ]
};
describe("options screening map", () => {
  it("keeps rejected contracts selectable, excludes unknown delta, and reports partial stale snapshots", async () => {
    const select = vi.fn();
    const { container } = render(<OptionScreeningMap preview={preview} filters={{ minDte: 7, maxDte: 60, maxDelta: .35 }} selectedIndex={0} onSelect={select} />);
    const rejected = screen.getByRole("button", { name: /Select SPY 530 call.*Spread too wide/ });
    fireEvent.keyDown(rejected, { key: "Enter" });
    expect(select).toHaveBeenCalledWith(1);
    expect(screen.queryByRole("button", { name: /Select SPY 535/ })).not.toBeInTheDocument();
    expect(screen.getByText(/Partial coverage/)).toHaveTextContent(/Stale snapshot/);
    expect(screen.getByText(/1 candidates have unavailable/)).toBeInTheDocument();
    expect((await axe(container)).violations).toEqual([]);
  });
});
