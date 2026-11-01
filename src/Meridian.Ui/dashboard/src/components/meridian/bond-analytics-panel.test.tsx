import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { axe } from "jest-axe";
import { BondAnalyticsPanel } from "./bond-analytics-panel";
import type { BondAnalyticsResult } from "@/types/bond-analytics.types";

const SECURITY_ID = "11111111-1111-1111-1111-111111111111";
const OTHER_ID = "22222222-2222-2222-2222-222222222222";
function result(securityId = SECURITY_ID): BondAnalyticsResult {
  return {
    securityId,
    currency: "USD",
    modelVersion: "bond-analytics-v1",
    context: { asOf: "2026-10-29T00:00:00Z", settlementDate: "2026-10-30" },
    yieldConvention: "NominalPeriodic",
    cleanPrice: "99.125",
    dirtyPrice: "100.125",
    accruedInterest: "1",
    yieldToMaturity: "0.0512345",
    currentYield: "0.05044",
    macaulayDuration: "3.2",
    modifiedDuration: "3.1",
    convexity: "12.5",
    dv01: "0.031",
    yieldToWorst: "0.04",
    cashFlows: [
      {
        flowId: "flow",
        paymentDate: "2027-01-31",
        accrualStart: "2026-07-31",
        accrualEnd: "2027-01-31",
        interest: "2.5",
        principal: "100",
        timeInYears: "0.25"
      }
    ],
    exerciseYields: [],
    inputs: [
      {
        kind: "SecurityTerms",
        key: SECURITY_ID,
        version: 7,
        contentSha256: "abc123"
      }
    ],
    blockers: [],
    capabilities: [
      { operation: "Analytics", supported: true },
      { operation: "Posting", supported: false, reason: "Analysis only" }
    ],
    diagnostics: [],
    isBlocked: false
  };
}

describe("bond analytics panel", () => {
  it("provides accessible labels and semantic input groups", async () => {
    const { container } = render(<BondAnalyticsPanel securityId={SECURITY_ID} />);
    expect((await axe(container)).violations).toEqual([]);
  });
  it("requires user price input and displays server-calculated values and inspectable provenance", async () => {
    const calculate = vi.fn().mockResolvedValue(result());
    const user = userEvent.setup();
    render(<BondAnalyticsPanel securityId={SECURITY_ID} calculate={calculate} />);
    expect(calculate).not.toHaveBeenCalled();
    await user.type(screen.getByLabelText("Clean price / 100"), "99.125");
    await user.click(screen.getByRole("button", { name: "Calculate bond analytics" }));
    await screen.findByText("Calculation ready.");
    expect(calculate).toHaveBeenCalledWith(
      SECURITY_ID,
      expect.objectContaining({
        cleanPrice: "99.125",
        yieldConvention: "NominalPeriodic"
      }),
      expect.objectContaining({ allowDevelopmentFallback: false })
    );
    expect(screen.getByText("0.0512345")).toBeInTheDocument();
    expect(screen.getByText("SHA-256 abc123")).toBeInTheDocument();
    expect(screen.getByText(/Imported Pricing & Risk fields above remain separate/)).toBeInTheDocument();
    expect(screen.getByRole("table", { name: "Projected bond payments" })).toBeInTheDocument();
    await user.type(screen.getByLabelText("Clean price / 100"), "1");
    expect(screen.queryByText("0.0512345")).not.toBeInTheDocument();
  });

  it("shows unsupported reasons without substituting calculated metrics", async () => {
    const blocked = {
      ...result(),
      isBlocked: true,
      blockers: [
        {
          code: "UnsupportedCoupon",
          reason: "Inflation index projection requires an authoritative provider."
        }
      ]
    };
    render(<BondAnalyticsPanel securityId={SECURITY_ID} calculate={vi.fn().mockResolvedValue(blocked)} />);
    fireEvent.change(screen.getByLabelText("Clean price / 100"), {
      target: { value: "100" }
    });
    fireEvent.submit(screen.getByRole("form", { name: "Bond analytics inputs" }));
    expect(await screen.findByText(/UnsupportedCoupon:/)).toBeInTheDocument();
    expect(screen.queryByText("0.0512345")).not.toBeInTheDocument();
  });

  it("aborts a previous security request and ignores a late response", async () => {
    let resolve: (value: BondAnalyticsResult) => void;
    const calculate = vi.fn().mockReturnValue(
      new Promise<BondAnalyticsResult>((done) => {
        resolve = done;
      })
    );
    const { rerender } = render(<BondAnalyticsPanel securityId={SECURITY_ID} calculate={calculate} />);
    fireEvent.change(screen.getByLabelText("Clean price / 100"), {
      target: { value: "100" }
    });
    fireEvent.submit(screen.getByRole("form", { name: "Bond analytics inputs" }));
    rerender(<BondAnalyticsPanel securityId={OTHER_ID} calculate={calculate} />);
    expect(calculate.mock.calls[0][2].signal.aborted).toBe(true);
    await act(async () => resolve(result()));
    expect(screen.queryByText("0.0512345")).not.toBeInTheDocument();
    expect(screen.getByLabelText("Clean price / 100")).toHaveValue("");
  });

  it("rejects cross-security responses", async () => {
    render(<BondAnalyticsPanel securityId={SECURITY_ID} calculate={vi.fn().mockResolvedValue(result(OTHER_ID))} />);
    fireEvent.change(screen.getByLabelText("Clean price / 100"), {
      target: { value: "100" }
    });
    fireEvent.submit(screen.getByRole("form", { name: "Bond analytics inputs" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("different security");
    expect(screen.queryByText("0.0512345")).not.toBeInTheDocument();
  });
});

describe("bond analytics inputs", () => {
  it("accepts a leading-dot decimal and normalizes a cutoff without seconds to a UTC instant", async () => {
    const calculate = vi.fn().mockResolvedValue(result());
    render(<BondAnalyticsPanel securityId={SECURITY_ID} calculate={calculate} />);
    fireEvent.change(screen.getByLabelText("Input basis"), { target: { value: "yield" } });
    fireEvent.change(screen.getByLabelText("Annual yield (0.05 = 5%)"), { target: { value: ".05" } });
    fireEvent.change(screen.getByLabelText("Recorded cutoff (UTC, optional)"), { target: { value: "2026-10-29T09:30" } });
    fireEvent.submit(screen.getByRole("form", { name: "Bond analytics inputs" }));
    await waitFor(() => expect(calculate).toHaveBeenCalledTimes(1));
    expect(calculate.mock.calls[0][1]).toMatchObject({ yield: ".05", knownAt: "2026-10-29T09:30:00Z" });
  });

  it("displays retained decimal strings exactly, digit-grouped, without float conversion", async () => {
    const calculate = vi.fn().mockResolvedValue({ ...result(), dirtyPrice: "1234567.123456789012" });
    render(<BondAnalyticsPanel securityId={SECURITY_ID} calculate={calculate} />);
    fireEvent.change(screen.getByLabelText("Clean price / 100"), { target: { value: "99" } });
    fireEvent.submit(screen.getByRole("form", { name: "Bond analytics inputs" }));
    expect(await screen.findByText("1,234,567.123456789012")).toBeInTheDocument();
  });
});
