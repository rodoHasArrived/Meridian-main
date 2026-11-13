import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { AccountBondPlanningPanel } from "./account-bond-planning-panel";
import type { AccountBondIncomePlan, AccountBondIncomePlanningRequest, BondMonitoringSnapshot } from "@/types/bond-workflow.types";

function response(request: AccountBondIncomePlanningRequest): AccountBondIncomePlan {
  return { scope: request.scope, periodStart: request.periodStart, periodEnd: request.periodEnd, holdingsBatchId: "accepted-batch", holdingsContentHash: "hash", currencyLadders: [], baseCurrencyLadder: {} as AccountBondIncomePlan["baseCurrencyLadder"], calculations: [], blockers: ["Opening USD balance missing"], generatedAt: "2026-11-12T12:00:00Z", monitoringInputs: { runId: "run", scope: request.scope, recordedAtUtc: "2026-11-12T12:00:00Z", obligations: [{ obligationId: "coupon" }], payments: [], allocations: [], receiptRevisions: [], observations: [] } };
}
function fillBook() { fireEvent.change(screen.getByLabelText("Ledger book ID"), { target: { value: "book-a" } }); }
describe("AccountBondPlanningPanel", () => {
  it("defaults to twelve months, uses explicit scope, and preserves missing-cash blockers", async () => {
    const plan = vi.fn(async (_book: string, _account: string, request: AccountBondIncomePlanningRequest) => response(request));
    render(<AccountBondPlanningPanel accountId="account-a" plan={plan} />); fillBook();
    const start = (screen.getByLabelText("Planning start") as HTMLInputElement).value;
    const end = (screen.getByLabelText("Planning end (inclusive)") as HTMLInputElement).value;
    expect((Date.parse(end) - Date.parse(start)) / 86400000).toBeGreaterThanOrEqual(364);
    expect((Date.parse(end) - Date.parse(start)) / 86400000).toBeLessThanOrEqual(365);
    fireEvent.click(screen.getByRole("button", { name: "Build bond income plan" }));
    await screen.findByRole("region", { name: "Account bond income plan" });
    expect(plan.mock.calls[0][2]).toMatchObject({ scope: { accountId: "account-a", bookId: "book-a" }, periodStart: start, periodEnd: end, openingBalances: [], fxConversions: [] });
    expect(screen.getByText("Opening USD balance missing")).toBeInTheDocument();
  });
  it("invalidates late forecasts when account selection changes", async () => {
    let complete!: () => void;
    const plan = vi.fn((_book: string, _account: string, request: AccountBondIncomePlanningRequest) => new Promise<AccountBondIncomePlan>((resolve) => { complete = () => resolve(response(request)); }));
    render(<AccountBondPlanningPanel accountId="account-a" plan={plan} />); fillBook();
    fireEvent.click(screen.getByRole("button", { name: "Build bond income plan" })); await waitFor(() => expect(plan).toHaveBeenCalledOnce());
    fireEvent.change(screen.getByLabelText("Account ID"), { target: { value: "account-b" } });
    await act(async () => complete()); expect(screen.queryByRole("region", { name: "Account bond income plan" })).not.toBeInTheDocument();
  });
  it("refuses forecasts from another account", async () => {
    const plan = vi.fn(async (_book: string, _account: string, request: AccountBondIncomePlanningRequest) => ({ ...response(request), scope: { ...request.scope, accountId: "other" } }));
    render(<AccountBondPlanningPanel accountId="account-a" plan={plan} />); fillBook(); fireEvent.click(screen.getByRole("button", { name: "Build bond income plan" }));
    await screen.findByRole("alert"); expect(screen.getByText(/different account or book/)).toBeInTheDocument();
  });
  it("retains only server-assembled obligations and renders collection residuals", async () => {
    const plan = vi.fn(async (_book: string, _account: string, request: AccountBondIncomePlanningRequest) => response(request));
    const capture = vi.fn(async (_book: string, _account: string, inputs: BondMonitoringSnapshot["inputs"]): Promise<BondMonitoringSnapshot> => ({ snapshotId: "snapshot", engineVersion: "bond-monitoring-v1", inputs, collections: [{ obligationId: "coupon", securityId: "bond", flowType: "Coupon", dueDate: "2026-11-11", expectedAmount: "100", netCollected: "40", grossCollected: "40", withholdingTax: "0", fees: "0", remainingAmount: "60", currency: "USD", status: "PartialOverdue", ageDays: 1, revisedReceiptDate: null, includedInSpendableForecast: false, paymentVersionIds: ["receipt"] }], watch: [], issues: [] }));
    render(<AccountBondPlanningPanel accountId="account-a" plan={plan} capture={capture} />); fillBook();
    fireEvent.click(screen.getByRole("button", { name: "Build bond income plan" })); await screen.findByRole("region", { name: "Account bond income plan" });
    fireEvent.click(screen.getByRole("button", { name: "Retain expected obligations for monitoring" }));
    await screen.findByRole("region", { name: "Bond collection and watch" }); expect(capture.mock.calls[0][2].obligations).toEqual([{ obligationId: "coupon" }]);
    expect(screen.getByText("60")).toBeInTheDocument(); expect(screen.getByText(/PartialOverdue/)).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText(/Evidence handoff JSON with complete selected payments/), { target: { value: '{"payments":[]}' } });
    fireEvent.change(screen.getByLabelText("Monitoring correction rationale"), { target: { value: "Retain corrected custodian evidence" } });
    fireEvent.click(screen.getByRole("button", { name: "Retain receipt and watch evidence" }));
    await waitFor(() => expect(capture).toHaveBeenCalledTimes(2));
    expect(capture.mock.calls[1][2]).toMatchObject({ planningRunId: "run", previousSnapshotId: "snapshot", correctionRationale: "Retain corrected custodian evidence" });
    expect(capture.mock.calls[1][2].runId).not.toBe("run");
  });
});
