import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
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
    modelVersion: "bond-analytics-v2",
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

function floatingResult(): BondAnalyticsResult {
  // Presentation fixture: arithmetic and retained input selection are exercised by the service integration tests.
  return {
    ...result(),
    modelVersion: "bond-analytics-v3",
    context: { asOf: "2026-02-15T00:00:00Z", settlementDate: "2026-02-15" },
    yieldToMaturity: "0.03",
    accruedInterest: "0.333333333333",
    spreadDv01: "0.019123456789",
    inputs: [
      ...result().inputs,
      {
        kind: "ForwardCurveVersion",
        key: "Synthetic SOFR",
        version: 4,
        versionId: "bbbbbbbb-0000-0000-0000-000000000002",
        contentSha256: "curve-content-hash"
      }
    ],
    cashFlows: [
      {
        flowId: "observed-sofr",
        paymentDate: "2026-07-15",
        accrualStart: "2026-01-15",
        accrualEnd: "2026-07-15",
        interest: "2",
        principal: "0",
        timeInYears: "0.416666666667",
        floatingCoupon: {
          resetDate: "2026-01-15",
          isObserved: true,
          indexName: "SOFR",
          indexRate: "0.03",
          spreadBps: "100",
          couponRate: "0.04",
          source: "Retained reset schedule"
        }
      },
      {
        flowId: "forecast-sofr",
        paymentDate: "2027-01-15",
        accrualStart: "2026-07-15",
        accrualEnd: "2027-01-15",
        interest: "1.5",
        principal: "0",
        timeInYears: "0.916666666667",
        floatingCoupon: {
          resetDate: "2026-07-15",
          isObserved: false,
          indexName: "SOFR",
          indexRate: "0.02",
          spreadBps: "100",
          couponRate: "0.03",
          source: "Synthetic retained SOFR curve"
        }
      }
    ]
  };
}

function submitPrice() {
  fireEvent.change(screen.getByLabelText("Clean price / 100"), { target: { value: "100" } });
  fireEvent.submit(screen.getByRole("form", { name: "Bond analytics inputs" }));
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
        calculationVersion: "bond-analytics-v2",
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
    expect(screen.queryByRole("table", { name: "Projected bond payments" })).not.toBeInTheDocument();
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

describe("bond analytics calculation versions", () => {
  it("keeps v2 as the default and explicitly supports legacy v1 without floating measures", async () => {
    const calculate = vi.fn().mockResolvedValue({ ...result(), modelVersion: "bond-analytics-v1" });
    const user = userEvent.setup();
    render(<BondAnalyticsPanel securityId={SECURITY_ID} calculate={calculate} />);
    const selector = screen.getByLabelText("Calculation version");
    expect(selector).toHaveValue("bond-analytics-v2");
    await user.selectOptions(selector, "bond-analytics-v1");
    submitPrice();
    await screen.findByText("Calculation ready.");
    expect(calculate.mock.calls[0][1]).toMatchObject({ calculationVersion: "bond-analytics-v1" });
    expect(screen.getByText("Model: bond-analytics-v1")).toBeInTheDocument();
    expect(screen.getByText("YTM (annual fraction)")).toBeInTheDocument();
    expect(screen.queryByText(/SpreadDv01/)).not.toBeInTheDocument();
    expect(screen.queryByText("Fixing / source")).not.toBeInTheDocument();
  });

  it("requests v3 and displays source-selected fixings, projected-flow yield and distinct spread sensitivity", async () => {
    const calculate = vi.fn().mockResolvedValue(floatingResult());
    const user = userEvent.setup();
    const { container } = render(<BondAnalyticsPanel securityId={SECURITY_ID} calculate={calculate} />);
    await user.selectOptions(screen.getByLabelText("Calculation version"), "bond-analytics-v3");
    fireEvent.change(screen.getByLabelText("Calculation date (UTC)"), { target: { value: "2026-02-15" } });
    fireEvent.change(screen.getByLabelText("Settlement date"), { target: { value: "2026-02-15" } });
    fireEvent.change(screen.getByLabelText("Input basis"), { target: { value: "yield" } });
    fireEvent.change(screen.getByLabelText("Annual yield (0.05 = 5%)"), { target: { value: "0.03" } });
    fireEvent.submit(screen.getByRole("form", { name: "Bond analytics inputs" }));
    await screen.findByText("Calculation ready.");
    expect(calculate).toHaveBeenCalledWith(SECURITY_ID, {
      asOf: "2026-02-15T00:00:00Z",
      settlementDate: "2026-02-15",
      calculationVersion: "bond-analytics-v3",
      yield: "0.03",
      yieldConvention: "NominalPeriodic"
    }, expect.objectContaining({ allowDevelopmentFallback: false }));
    expect(screen.getByText("Model: bond-analytics-v3")).toBeInTheDocument();
    expect(screen.getByText("Projected-flow yield (annual fraction)")).toBeInTheDocument();
    expect(screen.getByText(/not a discount margin/)).toBeInTheDocument();
    expect(screen.getByText("DV01 (price points / 100 / bp)")).toBeInTheDocument();
    expect(screen.getByText("SpreadDv01 (price points / 100 / +1 bp)")).toBeInTheDocument();
    expect(screen.getByText("0.019123456789")).toBeInTheDocument();
    expect(screen.queryByText("Yield to worst (annual fraction)")).not.toBeInTheDocument();
    await user.click(screen.getByText("Projected contractual payments (2)"));
    const payments = within(screen.getByRole("table", { name: "Projected bond payments" }));
    expect(payments.getByText("Observed fixing · SOFR")).toBeInTheDocument();
    expect(payments.getByText("Forecast fixing · SOFR")).toBeInTheDocument();
    expect(payments.getByText("Reset 2026-01-15")).toBeInTheDocument();
    expect(payments.getByText("Reset 2026-07-15")).toBeInTheDocument();
    expect(payments.getByText("Index fraction: 0.03")).toBeInTheDocument();
    expect(payments.getByText("Index fraction: 0.02")).toBeInTheDocument();
    expect(payments.getAllByText("Spread: 100 bp")).toHaveLength(2);
    expect(payments.getByText("Coupon fraction: 0.04")).toBeInTheDocument();
    expect(payments.getByText("Retained reset schedule")).toBeInTheDocument();
    expect(payments.getByText("Synthetic retained SOFR curve")).toBeInTheDocument();
    await user.click(screen.getByText("Input provenance and operation support"));
    expect(screen.getByText("Version ID bbbbbbbb-0000-0000-0000-000000000002")).toBeInTheDocument();
    expect(screen.getByText(/Synthetic SOFR · version 4/)).toBeInTheDocument();
    expect((await axe(container)).violations).toEqual([]);
  });

  it.each(["MissingFixing", "ForwardCurveRequired", "UnsupportedCouponStructure"])(
    "displays a %s refusal without metrics, payments or a retry using a different model",
    async (code) => {
      const calculate = vi.fn().mockResolvedValue({
        ...floatingResult(),
        isBlocked: true,
        blockers: [{ code, reason: "Retained inputs do not support this calculation." }]
      });
      render(<BondAnalyticsPanel securityId={SECURITY_ID} calculate={calculate} />);
      fireEvent.change(screen.getByLabelText("Calculation version"), { target: { value: "bond-analytics-v3" } });
      submitPrice();
      expect(await screen.findByText(`${code}: Retained inputs do not support this calculation.`)).toBeInTheDocument();
      expect(screen.getByLabelText("Calculation version")).toHaveValue("bond-analytics-v3");
      expect(calculate).toHaveBeenCalledTimes(1);
      expect(calculate.mock.calls[0][1].calculationVersion).toBe("bond-analytics-v3");
      expect(screen.queryByText("Projected-flow yield (annual fraction)")).not.toBeInTheDocument();
      expect(screen.queryByText("0.019123456789")).not.toBeInTheDocument();
      expect(screen.queryByRole("table", { name: "Projected bond payments" })).not.toBeInTheDocument();
    }
  );

  it("cancels a pending request on model selection and ignores its late result", async () => {
    let resolve: (value: BondAnalyticsResult) => void;
    const calculate = vi.fn().mockReturnValue(new Promise<BondAnalyticsResult>((done) => { resolve = done; }));
    const user = userEvent.setup();
    render(<BondAnalyticsPanel securityId={SECURITY_ID} calculate={calculate} />);
    await user.selectOptions(screen.getByLabelText("Calculation version"), "bond-analytics-v3");
    submitPrice();
    await user.selectOptions(screen.getByLabelText("Calculation version"), "bond-analytics-v2");
    expect(calculate.mock.calls[0][2].signal.aborted).toBe(true);
    await act(async () => resolve(floatingResult()));
    expect(screen.queryByText("Calculation ready.")).not.toBeInTheDocument();
    expect(screen.queryByText("0.019123456789")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Calculate bond analytics" })).toBeEnabled();
  });

  it("refuses a response from a different model without displaying or retrying it", async () => {
    const calculate = vi.fn().mockResolvedValue(result());
    render(<BondAnalyticsPanel securityId={SECURITY_ID} calculate={calculate} />);
    fireEvent.change(screen.getByLabelText("Calculation version"), { target: { value: "bond-analytics-v3" } });
    submitPrice();
    expect(await screen.findByRole("alert")).toHaveTextContent("different model version");
    expect(screen.queryByText("0.0512345")).not.toBeInTheDocument();
    expect(calculate).toHaveBeenCalledTimes(1);
  });

  it("shows unavailable evidence and sensitivity without deriving a fixing or inserting zero", async () => {
    const floating = floatingResult();
    const calculate = vi.fn().mockResolvedValue({
      ...floating,
      spreadDv01: null,
      cashFlows: floating.cashFlows.map((flow) => ({ ...flow, floatingCoupon: null }))
    });
    render(<BondAnalyticsPanel securityId={SECURITY_ID} calculate={calculate} />);
    fireEvent.change(screen.getByLabelText("Calculation version"), { target: { value: "bond-analytics-v3" } });
    submitPrice();
    await screen.findByText("Calculation ready.");
    const spreadMetric = screen.getByText("SpreadDv01 (price points / 100 / +1 bp)").parentElement!;
    expect(within(spreadMetric).getByText("Unavailable")).toBeInTheDocument();
    expect(screen.getAllByText("Fixing evidence unavailable")).toHaveLength(2);
    expect(screen.queryByText("Observed fixing · SOFR")).not.toBeInTheDocument();
    expect(screen.queryByText("Forecast fixing · SOFR")).not.toBeInTheDocument();
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
