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
});
