import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { axe } from "jest-axe";
import { SecurityRatesIngestionPanel } from "./security-rates-ingestion-panel";
import type { RateCurveVersion, SecurityRateObservation } from "@/lib/api/security-rates.api";

const observation: SecurityRateObservation = {
  provider: "fred", seriesId: "SOFR", value: "4.12345678901234567890123456", originalValue: "4.12345678901234567890123456",
  unit: "Percent", sourceUnits: "Percent", currency: "USD", observationDate: "2026-11-09", publishedAt: null,
  vintageDate: "2026-11-10", vintageEndDate: "9999-12-31", retrievedAt: "2026-11-10T12:00:00Z",
  transformation: "lin (untransformed provider level)", kind: "ObservedFixing"
};
const curve = { curveId: "curve-1", curveKey: "USD-SOFR", kind: "Forward", currency: "USD", indexName: "SOFR" };
const version: RateCurveVersion = { curveVersionId: "version-1", curveKey: "USD-SOFR", version: 1,
  contentSha256: "source-hash", points: [{ date: "2027-11-10", value: "0.0412345678901234567890123456" }] };

async function selectObservation() {
  fireEvent.change(screen.getByLabelText("Observation start"), { target: { value: "2026-11-09" } });
  fireEvent.change(screen.getByLabelText("Observation end / curve as-of"), { target: { value: "2026-11-10" } });
  fireEvent.click(screen.getByRole("button", { name: "Load typed observations" }));
  await screen.findByRole("option", { name: /SOFR · 2026-11-09/ });
  fireEvent.change(screen.getByLabelText("Select fixing or rate observation"), { target: { value: "0" } });
}

describe("SecurityRatesIngestionPanel", () => {
  it("shows exact provider units and vintage, then publishes an explicitly reviewed forecast assumption", async () => {
    const publish = vi.fn().mockResolvedValue(version);
    const { container } = render(<SecurityRatesIngestionPanel securityId="bond-1"
      loadObservations={vi.fn().mockResolvedValue([observation])} loadCurves={vi.fn().mockResolvedValue([curve])} publish={publish} />);
    await selectObservation();
    expect(screen.getByText(/4.12345678901234567890123456 \/ Percent/)).toBeInTheDocument();
    expect(screen.getByText(/Publication time unavailable/)).toBeInTheDocument();
    expect(screen.getByText(/Output role: forecast assumption/)).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Existing curve"), { target: { value: "curve-1" } });
    fireEvent.change(screen.getByLabelText("Pillar date"), { target: { value: "2027-11-10" } });
    fireEvent.change(screen.getByLabelText("Construction conventions and assumptions"), { target: { value: "Reviewed flat assumption for the selected SOFR curve" } });
    expect(screen.getByRole("button", { name: "Publish reviewed curve version" })).toBeDisabled();
    fireEvent.click(screen.getByRole("checkbox"));
    fireEvent.click(screen.getByRole("button", { name: "Publish reviewed curve version" }));
    await screen.findByText("Retained USD-SOFR version 1");
    expect(publish).toHaveBeenCalledWith(expect.objectContaining({ curveId: "curve-1", asOf: "2026-11-10",
      constructionMethod: "ObservedFixingFlatForecast", points: [{ pointDate: "2027-11-10", observation }] }), expect.anything());
    expect((await axe(container)).violations).toEqual([]);
  });

  it("blocks Treasury par yields until reviewed discount construction is selected", async () => {
    render(<SecurityRatesIngestionPanel securityId="bond-1"
      loadObservations={vi.fn().mockResolvedValue([{ ...observation, seriesId: "DGS10", kind: "TreasuryConstantMaturityYield" }])}
      loadCurves={vi.fn().mockResolvedValue([{ ...curve, kind: "Discount", indexName: null }])} publish={vi.fn()} />);
    fireEvent.change(screen.getByLabelText("Observation start"), { target: { value: "2026-11-09" } });
    fireEvent.change(screen.getByLabelText("Observation end / curve as-of"), { target: { value: "2026-11-10" } });
    fireEvent.click(screen.getByRole("button", { name: "Load typed observations" }));
    await screen.findByRole("option", { name: /DGS10 · 2026-11-09/ });
    fireEvent.change(screen.getByLabelText("Select fixing or rate observation"), { target: { value: "0" } });
    expect(screen.getByText("Treasury par yield needs construction")).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Existing curve"), { target: { value: "curve-1" } });
    fireEvent.click(screen.getByRole("checkbox"));
    expect(screen.getByRole("button", { name: "Publish reviewed curve version" })).toBeDisabled();
    fireEvent.change(screen.getByLabelText("Construction method"), { target: { value: "ReviewedDiscountFactors" } });
    expect(screen.getByLabelText("Reviewed discount factor")).toHaveValue("");
  });

  it("clears source selection when the security context changes", async () => {
    const props = { loadObservations: vi.fn().mockResolvedValue([observation]), loadCurves: vi.fn().mockResolvedValue([curve]), publish: vi.fn() };
    const { rerender } = render(<SecurityRatesIngestionPanel securityId="bond-1" {...props} />);
    await selectObservation();
    rerender(<SecurityRatesIngestionPanel securityId="bond-2" {...props} />);
    await waitFor(() => expect(screen.queryByText(/Output role: forecast assumption/)).not.toBeInTheDocument());
    expect(screen.getByLabelText("Select fixing or rate observation")).toHaveValue("");
  });
});
