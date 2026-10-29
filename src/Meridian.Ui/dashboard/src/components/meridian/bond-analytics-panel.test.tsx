import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { axe } from "jest-axe";
import { ApiError } from "@/lib/api-errors";
import { BondAnalyticsPanel } from "./bond-analytics-panel";
import { BondMigrationPreviewPanel } from "./bond-migration-preview-panel";
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

describe("bond migration preview", () => {
  it("requires the current terms version and an explicit coupon unit", async () => {
    const preview = vi.fn();
    const { rerender } = render(
      <BondMigrationPreviewPanel securityId={SECURITY_ID} expectedVersion={null} preview={preview} />
    );
    expect(
      screen.getByRole("button", {
        name: "Preview convention migration"
      })
    ).toBeDisabled();
    rerender(<BondMigrationPreviewPanel securityId={SECURITY_ID} expectedVersion={7} preview={preview} />);
    fireEvent.submit(screen.getByRole("form", { name: "Bond migration preview inputs" }));
    expect(preview).not.toHaveBeenCalled();
    expect(screen.getByRole("alert")).toHaveTextContent("retained coupon unit");
  });

  it("shows hash-bound prior/candidate payment comparisons without issuing a write", async () => {
    const preview = vi.fn().mockImplementation(async (request) => ({
      request,
      previewSha256: "preview-hash",
      securityContentSha256: "security-hash",
      cashDifferences: [
        {
          paymentDate: "2027-01-31",
          previousPrincipal: "100",
          candidatePrincipal: "100",
          previousInterest: "12.5",
          candidateInterest: "0.125"
        }
      ],
      gaps: [],
      canStage: true
    }));
    render(<BondMigrationPreviewPanel securityId={SECURITY_ID} expectedVersion={7} preview={preview} />);
    fireEvent.change(screen.getByLabelText("Retained coupon unit"), {
      target: { value: "Percent" }
    });
    fireEvent.submit(screen.getByRole("form", { name: "Bond migration preview inputs" }));
    await waitFor(() => expect(screen.getByText("Preview ready for governed review")).toBeInTheDocument());
    expect(preview).toHaveBeenCalledTimes(1);
    expect(preview.mock.calls[0][0]).toMatchObject({
      securityId: SECURITY_ID,
      expectedVersion: 7,
      scheduleConventions: { couponRateUnit: "Percent" }
    });
    expect(
      screen.getByRole("table", {
        name: "Bond migration payment comparison"
      })
    ).toHaveTextContent("0.125");
    expect(screen.queryByRole("button", { name: /apply|publish|post/i })).not.toBeInTheDocument();
  });

  it("stages a governed draft only after explicit justification, submitting hashes rather than amounts", async () => {
    const preview = vi.fn().mockImplementation(async (request) => ({
      request,
      previewSha256: "preview-hash",
      securityContentSha256: "security-hash",
      cashDifferences: [],
      gaps: [],
      canStage: true
    }));
    const stage = vi.fn().mockResolvedValue({
      securityId: SECURITY_ID,
      revisionId: "revision-8",
      newVersion: 8,
      state: "Draft",
      changeEntry: {}
    });
    render(<BondMigrationPreviewPanel securityId={SECURITY_ID} expectedVersion={7} preview={preview} stage={stage} />);
    fireEvent.change(screen.getByLabelText("Retained coupon unit"), {
      target: { value: "Percent" }
    });
    fireEvent.submit(screen.getByRole("form", { name: "Bond migration preview inputs" }));
    const button = await screen.findByRole("button", {
      name: "Stage governed draft"
    });
    expect(button).toBeDisabled();
    fireEvent.change(screen.getByLabelText("Review justification"), {
      target: {
        value: "Contract confirmation reviewed against changed payments."
      }
    });
    fireEvent.submit(screen.getByRole("form", { name: "Stage reviewed bond migration" }));
    expect(await screen.findByText("Governed revision created")).toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Governed bond migration result" })).toHaveFocus();
    expect(stage).toHaveBeenCalledTimes(1);
    expect(stage.mock.calls[0][0]).toMatchObject({
      previewSha256: "preview-hash",
      securityContentSha256: "security-hash",
      justification: "Contract confirmation reviewed against changed payments."
    });
    expect(stage.mock.calls[0][0]).not.toHaveProperty("cashDifferences");
    expect(screen.queryByRole("button", { name: "Stage governed draft" })).not.toBeInTheDocument();
    expect(screen.getByText(/No approval or publication was requested/)).toBeInTheDocument();
  });

  it("blocks reuse of a stale preview and asks for a fresh retained version", async () => {
    const preview = vi.fn().mockImplementation(async (request) => ({
      request,
      previewSha256: "preview-hash",
      securityContentSha256: "security-hash",
      cashDifferences: [],
      gaps: [],
      canStage: true
    }));
    const stage = vi.fn().mockRejectedValue(new ApiError({ path: "/migration-submit", status: 409 }));
    render(<BondMigrationPreviewPanel securityId={SECURITY_ID} expectedVersion={7} preview={preview} stage={stage} />);
    fireEvent.change(screen.getByLabelText("Retained coupon unit"), {
      target: { value: "Percent" }
    });
    fireEvent.submit(screen.getByRole("form", { name: "Bond migration preview inputs" }));
    await screen.findByRole("button", { name: "Stage governed draft" });
    fireEvent.change(screen.getByLabelText("Review justification"), {
      target: { value: "Reviewed." }
    });
    fireEvent.submit(screen.getByRole("form", { name: "Stage reviewed bond migration" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("prepare a fresh preview");
    expect(screen.getByRole("button", { name: "Stage governed draft" })).toBeDisabled();
  });
});
