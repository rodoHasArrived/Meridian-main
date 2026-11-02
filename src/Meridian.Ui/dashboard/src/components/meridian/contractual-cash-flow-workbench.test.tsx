import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import * as cashFlowApi from "@/lib/api/contractual-cash-flow.api";
import { ContractualCashFlowWorkbench } from "./contractual-cash-flow-workbench";
import type { ContractualCashFlowResult, ContractualCashFlowRun } from "@/types/contractual-cash-flow.types";

vi.mock("@/lib/api/contractual-cash-flow.api", async () => {
  const actual = await vi.importActual<typeof import("@/lib/api/contractual-cash-flow.api")>("@/lib/api/contractual-cash-flow.api");
  return {
    isContractualCashFlowRun: actual.isContractualCashFlowRun,
    previewContractualCashFlows: vi.fn(),
    retainContractualCashFlowRun: vi.fn(),
    getContractualCashFlowRun: vi.fn(),
    compareContractualCashFlowRuns: vi.fn()
  };
});

const api = vi.mocked(cashFlowApi);
afterEach(() => vi.resetAllMocks());

const evidence = (outputHash: string) => ({
  engineVersion: "contractual-cashflow-v1", mappingVersion: "m1", prepaymentVersion: "p1", kernelVersion: "k1",
  inputHash: "in-1", outputHash, sourceTermsHash: "terms-1", sourceVersion: "7", hasContractOverride: false, calendars: [], curves: [], warnings: []
});

const calculated: ContractualCashFlowResult = {
  events: [{
    eventId: "e1", streamId: "coupon", periodId: "p1", date: "2027-01-31", accrualStart: "2026-07-31", accrualEnd: "2027-01-31",
    openingBalance: "1000000", cashInterest: "25000.123456789012", capitalizedInterest: "0", scheduledPrincipal: "0", forecastPrepayment: "0",
    redeemedPrincipal: "0", redemptionPremium: "0", prepaymentPenalty: "0", closingBalance: "1000000", direction: "Receive"
  }],
  exerciseEligibility: [{ side: "Call", entryStartDate: "2027-01-31", streamId: "coupon", windowEnd: "2028-01-31", style: "Bermudan", dates: ["2027-01-31", "2027-07-31"], businessDayCalendarId: "NYB" }],
  exerciseCalculations: [], blockers: [], evidence: evidence("out-1"), isCalculated: true, isPostable: false
};

const blocked: ContractualCashFlowResult = { ...calculated, events: [], exerciseEligibility: [], blockers: [{ code: "CurvePinRequired", message: "Pin the SOFR curve.", streamId: "float" }], isCalculated: false };

describe("ContractualCashFlowWorkbench", () => {
  it("previews with the entered scenario, shows exact amounts and blocks retention when blockers remain", async () => {
    api.previewContractualCashFlows.mockResolvedValueOnce(blocked).mockResolvedValueOnce(calculated);
    render(<ContractualCashFlowWorkbench subjectKind="Security" subjectId="sec-1" subjectLabel="ACME 5 2030" />);

    fireEvent.change(screen.getByLabelText("Scenario valuation date"), { target: { value: "2026-10-31" } });
    fireEvent.change(screen.getByLabelText("Scenario knowledge cutoff"), { target: { value: "2026-10-31T09:30" } });
    fireEvent.change(screen.getByLabelText("Scenario rate shock"), { target: { value: "Up200" } });
    fireEvent.click(screen.getByRole("button", { name: "Preview scenario" }));

    await screen.findByLabelText("Calculation blockers");
    expect(screen.getByText(/Pin the SOFR curve/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Retain run" })).toBeDisabled();
    expect(api.previewContractualCashFlows).toHaveBeenLastCalledWith("Security", "sec-1",
      expect.objectContaining({ valuationDate: "2026-10-31", knowledgeCutoff: "2026-10-31T09:30:00Z", rateScenario: "Up200", horizon: null, exercises: null, prepayments: null }),
      expect.anything());

    fireEvent.click(screen.getByRole("button", { name: "Preview scenario" }));
    await screen.findByText("25,000.123456789012");
    expect(screen.getByRole("button", { name: "Retain run" })).toBeEnabled();
    expect(screen.getByLabelText("Include call exercise on coupon")).toBeInTheDocument();
  });

  it("retains a run under a client-generated identity bound to the previewed request", async () => {
    api.previewContractualCashFlows.mockResolvedValue(calculated);
    api.retainContractualCashFlowRun.mockImplementation(async (kind, subjectId, request) => ({
      runId: request.runId, subjectKind: kind, subjectId, recordedAt: "2026-10-31T10:00:00Z", request: request.projection, result: calculated, actor: "operator-a"
    } satisfies ContractualCashFlowRun));
    render(<ContractualCashFlowWorkbench subjectKind="Loan" subjectId="loan-1" subjectLabel="Facility A" />);

    fireEvent.click(screen.getByRole("button", { name: "Preview scenario" }));
    await screen.findByText("25,000.123456789012");
    fireEvent.click(screen.getByRole("button", { name: "Retain run" }));

    await screen.findByLabelText("Retained run");
    expect(api.retainContractualCashFlowRun).toHaveBeenCalledTimes(1);
    const [kind, subjectId, request] = api.retainContractualCashFlowRun.mock.calls[0];
    expect(kind).toBe("Loan");
    expect(subjectId).toBe("loan-1");
    expect(request.runId).toBeTruthy();
    expect(request.projection).toEqual(api.previewContractualCashFlows.mock.calls[0][2]);
    expect(screen.getByLabelText("Retained run")).toHaveTextContent(`Run ${request.runId} retained 2026-10-31T10:00:00Z by operator-a`);
    expect(screen.getByRole("button", { name: "Retain run" })).toBeDisabled();
  });

  it("sends an exercise selection with its own notice date, defaulting to the exercise date, and refuses a late notice", async () => {
    api.previewContractualCashFlows.mockResolvedValue(calculated);
    render(<ContractualCashFlowWorkbench subjectKind="Security" subjectId="sec-1" subjectLabel="ACME 5 2030" />);
    fireEvent.click(screen.getByRole("button", { name: "Preview scenario" }));
    await screen.findByLabelText("Include call exercise on coupon");

    fireEvent.click(screen.getByLabelText("Include call exercise on coupon"));
    fireEvent.change(screen.getByLabelText("Exercise date for coupon"), { target: { value: "2027-07-31" } });
    fireEvent.change(screen.getByLabelText("Exercise amount for coupon"), { target: { value: "1000000" } });
    fireEvent.change(screen.getByLabelText("Notice evidence for coupon"), { target: { value: " 3f2504e0-4f89-11d3-9a0c-0305e82c3301 " } });
    fireEvent.click(screen.getByRole("button", { name: "Preview scenario" }));
    await waitFor(() => expect(api.previewContractualCashFlows).toHaveBeenCalledTimes(2));
    expect(api.previewContractualCashFlows.mock.calls[1][2].exercises).toEqual([{
      side: "Call", entryStartDate: "2027-01-31", streamId: "coupon", exerciseDate: "2027-07-31", noticeDate: "2027-07-31",
      amount: "1000000", hypotheticalNotice: false, noticeEvidenceId: "3f2504e0-4f89-11d3-9a0c-0305e82c3301"
    }]);

    fireEvent.change(screen.getByLabelText("Notice date for coupon"), { target: { value: "2027-06-30" } });
    fireEvent.click(screen.getByRole("button", { name: "Preview scenario" }));
    await waitFor(() => expect(api.previewContractualCashFlows).toHaveBeenCalledTimes(3));
    expect(api.previewContractualCashFlows.mock.calls[2][2].exercises?.[0]).toMatchObject({ exerciseDate: "2027-07-31", noticeDate: "2027-06-30" });

    fireEvent.change(screen.getByLabelText("Notice date for coupon"), { target: { value: "2027-08-01" } });
    fireEvent.click(screen.getByRole("button", { name: "Preview scenario" }));
    expect(await screen.findByText(/cannot follow its exercise date/)).toBeInTheDocument();
    expect(api.previewContractualCashFlows).toHaveBeenCalledTimes(3);
  });

  it("loads a retained run and verifies replay by output hash", async () => {
    const retained: ContractualCashFlowRun = {
      runId: "run-9", subjectKind: "Security", subjectId: "sec-1", recordedAt: "2026-10-30T00:00:00Z", actor: "operator-a",
      request: { valuationDate: "2026-10-30", knowledgeCutoff: "2026-10-30T00:00:00Z", rateScenario: "Down100" }, result: calculated
    };
    api.getContractualCashFlowRun.mockResolvedValue(retained);
    api.previewContractualCashFlows.mockResolvedValueOnce({ ...calculated, evidence: evidence("out-2") });
    render(<ContractualCashFlowWorkbench subjectKind="Security" subjectId="sec-1" subjectLabel="ACME 5 2030" />);

    fireEvent.change(screen.getByLabelText("Retained run identity"), { target: { value: "run-9" } });
    fireEvent.click(screen.getByRole("button", { name: "Load retained run" }));
    expect(await screen.findByLabelText("Loaded retained run")).toHaveTextContent(/Run run-9 .* Down100 .* 1 events .* out-1/);
    expect(api.getContractualCashFlowRun).toHaveBeenCalledWith("Security", "sec-1", "run-9", expect.anything());

    fireEvent.click(screen.getByRole("button", { name: "Verify replay" }));
    await waitFor(() => expect(api.previewContractualCashFlows).toHaveBeenCalledWith("Security", "sec-1", retained.request, expect.anything()));
    expect(await screen.findByLabelText("Replay verification")).toHaveTextContent("Replay differs: retained output out-1, current out-2. Source terms unchanged.");
  });

  it("compares the loaded retained run with another run of the same subject", async () => {
    const retained: ContractualCashFlowRun = {
      runId: "run-9", subjectKind: "Security", subjectId: "sec-1", recordedAt: "2026-10-30T00:00:00Z", actor: "operator-a",
      request: { valuationDate: "2026-10-30", knowledgeCutoff: "2026-10-30T00:00:00Z" }, result: calculated
    };
    api.getContractualCashFlowRun.mockResolvedValue(retained);
    api.compareContractualCashFlowRuns.mockResolvedValue({
      subjectKind: "Security", subjectId: "sec-1", leftRunId: "run-9", rightRunId: "run-10", leftReplayVerified: true, rightReplayVerified: false,
      sameRequest: false, leftOutputHash: "out-1", rightOutputHash: "out-2", totalDelta: "125.5",
      cashDeltas: [{ date: "2027-01-31", streamId: "coupon", leftNetCash: "25000.123456789012", rightNetCash: "25125.623456789012", delta: "125.5" }],
      blockerDeltas: [{ side: "RightOnly", code: "CurvePinRequired", message: "Pin the SOFR curve.", streamId: "float" }]
    });
    render(<ContractualCashFlowWorkbench subjectKind="Security" subjectId="sec-1" subjectLabel="ACME 5 2030" />);

    fireEvent.change(screen.getByLabelText("Retained run identity"), { target: { value: "run-9" } });
    fireEvent.click(screen.getByRole("button", { name: "Load retained run" }));
    await screen.findByLabelText("Loaded retained run");
    fireEvent.change(screen.getByLabelText("Comparison run identity"), { target: { value: "run-10" } });
    fireEvent.click(screen.getByRole("button", { name: "Compare runs" }));

    const section = await screen.findByLabelText("Retained run comparison");
    expect(api.compareContractualCashFlowRuns).toHaveBeenCalledWith("Security", "sec-1", "run-9", "run-10", expect.anything());
    expect(section).toHaveTextContent(/run-9 \(replay verified\) vs run-10 \(REPLAY FAILED\) · different requests · total net cash delta 125.5/);
    expect(screen.getByLabelText("Blocker differences")).toHaveTextContent("Only in second: CurvePinRequired (float): Pin the SOFR curve.");
    expect(screen.getByText("125.5", { selector: "td, td *" })).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Load retained run" }));
    await screen.findByLabelText("Loaded retained run");
    expect(screen.queryByLabelText("Retained run comparison")).toBeNull();
  });
});
