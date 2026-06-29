import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { CoveragePassportDrillIn } from "@/components/meridian/coverage-passport-drill-in";
import type { SecurityMasterEntry } from "@/types";

function entry(securityId: string, displayName: string, assetClass: string, symbol: string): SecurityMasterEntry {
  return {
    securityId,
    displayName,
    status: "Active",
    classification: { assetClass, subType: null, primaryIdentifierKind: "Ticker", primaryIdentifierValue: symbol },
    economicDefinition: { currency: "USD" }
  } as unknown as SecurityMasterEntry;
}

const securities = [entry("s-1", "Acme Corp", "Equity", "ACME"), entry("s-3", "Gov 2032", "Bond", "G2032")];

describe("CoveragePassportDrillIn", () => {
  it("renders nothing when no securities match the asset class", () => {
    const { container } = render(<CoveragePassportDrillIn assetClass="Crypto" securities={securities} />);
    expect(container).toBeEmptyDOMElement();
  });

  it("expands to the asset-class securities and launches the editor for the chosen one", async () => {
    const user = userEvent.setup();
    const loadVersion = vi.fn().mockResolvedValue(7);
    render(<CoveragePassportDrillIn assetClass="Equity" securities={securities} loadVersion={loadVersion} />);

    // Only the Equity security is offered.
    await user.click(screen.getByRole("button", { name: /edit passports \(1\)/i }));
    await user.click(screen.getByRole("button", { name: /acme corp · acme/i }));

    await waitFor(() => expect(screen.getByTestId("security-passport-editor")).toBeInTheDocument());
    expect(loadVersion).toHaveBeenCalledWith("s-1");
    expect(screen.getByText("v7")).toBeInTheDocument();
  });

  it("lazily fetches the asset-class securities on first expand when no eager list is given", async () => {
    const user = userEvent.setup();
    const loadSecurities = vi.fn().mockResolvedValue(securities);
    render(<CoveragePassportDrillIn assetClass="Equity" loadSecurities={loadSecurities} />);

    // The toggle is offered before anything is loaded; nothing is fetched until the operator expands it.
    expect(loadSecurities).not.toHaveBeenCalled();
    await user.click(screen.getByRole("button", { name: /edit passports/i }));

    await waitFor(() => expect(loadSecurities).toHaveBeenCalledWith("Equity"));
    // Only the exact Equity match survives the asset-class filter.
    await waitFor(() => expect(screen.getByRole("button", { name: /acme corp · acme/i })).toBeInTheDocument());
    expect(screen.queryByRole("button", { name: /gov 2032/i })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: /edit passports \(1\)/i })).toBeInTheDocument();
  });

  it("shows an empty state when the lazy fetch returns no matching securities", async () => {
    const user = userEvent.setup();
    const loadSecurities = vi.fn().mockResolvedValue([]);
    render(<CoveragePassportDrillIn assetClass="Crypto" loadSecurities={loadSecurities} />);

    await user.click(screen.getByRole("button", { name: /edit passports/i }));

    await waitFor(() => expect(screen.getByText(/no crypto securities are available to edit/i)).toBeInTheDocument());
  });

  it("surfaces an error state when the lazy fetch fails", async () => {
    const user = userEvent.setup();
    const loadSecurities = vi.fn().mockRejectedValue(new Error("boom"));
    render(<CoveragePassportDrillIn assetClass="Equity" loadSecurities={loadSecurities} />);

    await user.click(screen.getByRole("button", { name: /edit passports/i }));

    await waitFor(() => expect(screen.getByText(/could not load equity securities/i)).toBeInTheDocument());
  });
});
