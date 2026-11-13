import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { BondProviderIntakePanel } from "./bond-provider-intake-panel";
describe("BondProviderIntakePanel", () => {
  it("retains ambiguous debt candidates without asserting an identity write", async () => {
    const discover = vi.fn(async () => ({ identifier: "US0000000001", identifierType: "ISIN", isAmbiguous: true, resolvedFigi: null, evidenceStatus: "LiveUntested", candidates: [{ figi: "FIGI-A", compositeFigi: null, securityType: "Bond", marketSector: "Corp", ticker: null, name: "Synthetic bond A", exchangeCode: null }, { figi: "FIGI-B", compositeFigi: null, securityType: "Bond", marketSector: "Corp", ticker: null, name: "Synthetic bond B", exchangeCode: null }] }));
    render(<BondProviderIntakePanel discover={discover} />); fireEvent.change(screen.getByLabelText("Bond identifier"), { target: { value: "US0000000001" } });
    fireEvent.click(screen.getByRole("button", { name: "Discover bond identifier candidates" })); await screen.findByRole("region", { name: "Bond identifier candidates" });
    expect(screen.getByText("Multiple bond candidates require source selection")).toBeInTheDocument(); expect(screen.getByText(/No identity was written/)).toBeInTheDocument();
    expect(discover).toHaveBeenCalledWith({ identifier: "US0000000001", identifierType: "ISIN", marketSector: null }, expect.objectContaining({ allowDevelopmentFallback: false }));
  });
  it("imports FRED dates using only the reviewed binding and preserves live-untested evidence", async () => {
    const importer = vi.fn(async () => ({ retainedObservations: { observations: [], appendedCount: 0 }, missingDates: ["2026-11-12"], responseSha256: "response-hash", bindingEvidenceSha256: "binding-hash", evidenceStatus: "LiveUntested" }));
    render(<BondProviderIntakePanel importFred={importer} />);
    fireEvent.change(screen.getByLabelText("Reviewed FRED binding evidence URI"), { target: { value: "evidence://fred-binding" } });
    fireEvent.change(screen.getByLabelText("Reviewed FRED binding document ID"), { target: { value: "reviewed-binding" } });
    fireEvent.click(screen.getByRole("button", { name: "Import reviewed FRED observations" })); await screen.findByRole("region", { name: "FRED bond observation intake" });
    expect(importer).toHaveBeenCalledWith(expect.objectContaining({ evidenceUri: "evidence://fred-binding", documentId: "reviewed-binding", vintageDate: expect.any(String) }), expect.objectContaining({ allowDevelopmentFallback: false }));
    expect(screen.getByText(/LiveUntested/)).toBeInTheDocument(); expect(screen.getByText(/Missing observation dates/)).toBeInTheDocument();
  });
});
