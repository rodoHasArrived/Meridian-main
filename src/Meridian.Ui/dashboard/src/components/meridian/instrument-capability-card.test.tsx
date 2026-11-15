import { fireEvent, render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import type { InstrumentCapabilityAssessment } from "@/types";
import { InstrumentCapabilityCard } from "./instrument-capability-card";

const assessment = (overrides: Partial<InstrumentCapabilityAssessment> = {}): InstrumentCapabilityAssessment => ({
  capability: "cash-flows", label: "Cash flows", operation: "BaseCashFlowCalculation", applicability: "Applicable",
  status: "Blocked", evaluationState: "Complete", securityVersion: 42, asOfUtc: "2026-11-14T00:00:00Z",
  context: "Selected security; no book context.", reasons: [{ code: "RateUnitUndeclared", reason: "Coupon rate unit is undeclared.", remediationPath: "/accounting/security-master?rateRepair=queue" }], ...overrides
});

describe("InstrumentCapabilityCard", () => {
  it("shows explicit status and exposes the reason, version and governed repair link", () => {
    render(<MemoryRouter><InstrumentCapabilityCard capabilities={[assessment()]} /></MemoryRouter>);
    expect(screen.getByText("Blocked")).toBeInTheDocument();
    fireEvent.click(screen.getByText("Cash flows"));
    expect(screen.getByText("Coupon rate unit is undeclared.")).toBeVisible();
    expect(screen.getByText(/Version 42/)).toBeVisible();
    expect(screen.getByRole("link", { name: "Review cash flows evidence" })).toHaveAttribute("href", "/accounting/security-master?rateRepair=queue");
  });

  it("keeps applicability distinct from a review or refusal", () => {
    render(<MemoryRouter><InstrumentCapabilityCard capabilities={[assessment({ applicability: "NotApplicable", status: "Review", reasons: [] })]} /></MemoryRouter>);
    expect(screen.getByText("Not applicable")).toBeInTheDocument();
    expect(screen.queryByText("Blocked")).not.toBeInTheDocument();
  });

  it("shows absent assessment without inventing Ready", () => {
    render(<MemoryRouter><InstrumentCapabilityCard capabilities={[]} /></MemoryRouter>);
    expect(screen.getByText(/Capability assessment is unavailable/)).toBeInTheDocument();
    expect(screen.queryByText("Ready")).not.toBeInTheDocument();
  });

  it("keeps every status visible and switches one shared detail area with keyboard Enter", async () => {
    const user = userEvent.setup();
    render(<MemoryRouter><InstrumentCapabilityCard capabilities={[
      assessment(), assessment({ capability: "terms", label: "Terms", status: "Ready", reasons: [] })
    ]} /></MemoryRouter>);
    const flows = screen.getByRole("button", { name: "Cash flows: Blocked" });
    const terms = screen.getByRole("button", { name: "Terms: Ready" });
    expect(flows).toHaveAttribute("aria-expanded", "false");
    await user.tab();
    expect(flows).toHaveFocus();
    await user.keyboard("{Enter}");
    expect(flows).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByRole("region", { name: "Cash flows capability details" })).toBeVisible();
    expect(terms).toBeVisible();
    await user.tab();
    expect(terms).toHaveFocus();
    await user.keyboard("{Enter}");
    expect(flows).toHaveAttribute("aria-expanded", "false");
    expect(terms).toHaveAttribute("aria-expanded", "true");
    expect(flows.getAttribute("aria-controls")).toBe(terms.getAttribute("aria-controls"));
    expect(screen.queryByText("Coupon rate unit is undeclared.")).not.toBeInTheDocument();
    await user.keyboard("{Enter}");
    expect(terms).toHaveAttribute("aria-expanded", "false");
  });
});
