import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { axe } from "jest-axe";
import { listSecurityCurves, listSecurityCurveVersions } from "@/lib/api/security-curves.api";
import { calculateSecurityCurveValuation, SecurityCurveValuationPanel } from "./security-curve-valuation-panel";
vi.mock("@/lib/api/security-curves.api", () => ({ listSecurityCurves: vi.fn(), listSecurityCurveVersions: vi.fn() }));
const result = { securityId: "bond", modelKey: "discounted-cash-flow", modelVersion: "v2", currency: "USD", asOf: "2026-11-01T00:00:00Z", knowledgeAsOf: "2026-11-02T00:00:00Z", presentValue: "123456.123456789012345678", price: "99.12", priceUnit: "Per100Face", observability: "ObservableInputs", inputs: [{ kind: "DiscountCurveVersion", reference: "USD OIS", id: "version-1", contentSha256: "retained-hash" }], blockers: [], diagnostics: [], isBlocked: false };
beforeEach(() => { vi.clearAllMocks(); vi.mocked(listSecurityCurves).mockResolvedValue([{ curveId: "curve-1", curveKey: "USD OIS", kind: "Discount", currency: "USD" }] as never); vi.mocked(listSecurityCurveVersions).mockResolvedValue([{ curveId: "curve-1", curveVersionId: "version-1", version: 1, asOf: "2026-11-01", recordedAt: "2026-11-01T00:00:00Z", contentSha256: "retained-hash" }] as never); });
const change = (label: string, value: string) => fireEvent.change(screen.getByLabelText(label), { target: { value } });
async function selectDiscount() {
  fireEvent.click(screen.getByRole("button", { name: "Add curve pin" }));
  await waitFor(() => expect(screen.getByLabelText("Curve 1")).toHaveTextContent("USD OIS"));
  change("Calculation role 1", "Discount"); change("Curve 1", "curve-1");
  await waitFor(() => expect(screen.getByLabelText("Curve version 1")).toHaveTextContent("v1"));
  change("Curve version 1", "version-1");
}
describe("supported exact curve valuation", () => {
  it("selects retained discount inputs, computes through the supported model and exposes exact outputs and provenance", async () => {
    const calculate = vi.fn().mockResolvedValue(result);
    const { container } = render(<SecurityCurveValuationPanel securityId="bond" calculate={calculate} />);
    await selectDiscount();
    change("Curve valuation date (UTC)", "2026-11-01");
    change("Valuation recorded cutoff (UTC, optional)", "2026-11-02T09:30");
    fireEvent.submit(screen.getByRole("form", { name: "Curve valuation inputs" }));
    await screen.findByText("Curve valuation ready.");
    expect(calculate).toHaveBeenCalledWith({ securityId: "bond", model: "DiscountedCashFlow", asOf: "2026-11-01T00:00:00Z", knownAt: "2026-11-02T09:30:00Z", curves: [{ role: "Discount", curveId: "curve-1", curveVersionId: "version-1" }] }, expect.anything());
    expect(screen.getByText("123,456.123456789012345678")).toBeInTheDocument();
    expect(screen.getByText("SHA-256 retained-hash")).toBeInTheDocument();
    expect((await axe(container)).violations).toEqual([]);
    change("Curve valuation date (UTC)", "2026-11-03");
    expect(screen.queryByText("Curve valuation ready.")).not.toBeInTheDocument();
  });
  it("keeps unsupported model blockers visible and suppresses amounts", async () => {
    render(<SecurityCurveValuationPanel securityId="bond" calculate={vi.fn().mockResolvedValue({ ...result, isBlocked: true, blockers: [{ code: "CurveTooOld", reason: "No eligible retained pillars" }] })} />);
    await selectDiscount(); fireEvent.submit(screen.getByRole("form", { name: "Curve valuation inputs" }));
    expect(await screen.findByText(/CurveTooOld:/)).toBeInTheDocument();
    expect(screen.queryByText("123,456.123456789012345678")).not.toBeInTheDocument();
  });
  it("aborts changed-security calculations and ignores late results", async () => {
    let finish: (value: typeof result) => void;
    const calculate = vi.fn().mockImplementation(() => new Promise((resolve) => { finish = resolve; }));
    const { rerender } = render(<SecurityCurveValuationPanel securityId="bond" calculate={calculate} />);
    await selectDiscount(); fireEvent.submit(screen.getByRole("form", { name: "Curve valuation inputs" }));
    rerender(<SecurityCurveValuationPanel securityId="other" calculate={calculate} />);
    await act(async () => finish(result));
    expect(calculate.mock.calls[0][1].signal.aborted).toBe(true);
    expect(screen.queryByText("Curve valuation ready.")).not.toBeInTheDocument();
  });
  it("posts exact retained pin identities and propagates service unavailability", async () => {
    const fetcher = vi.fn().mockResolvedValue(new Response('{"error":"Valuation unavailable"}', { status: 503 })); vi.stubGlobal("fetch", fetcher);
    try {
      const request = { securityId: "bond", asOf: "2026-11-01T00:00:00Z", model: "DiscountedCashFlow" as const, curves: [{ role: "Discount", curveId: "curve-1", curveVersionId: "version-1" }] };
      await expect(calculateSecurityCurveValuation(request)).rejects.toMatchObject({ status: 503 });
      expect(JSON.parse(fetcher.mock.calls[0][1].body)).toEqual(request);
    } finally { vi.unstubAllGlobals(); }
  });
});
