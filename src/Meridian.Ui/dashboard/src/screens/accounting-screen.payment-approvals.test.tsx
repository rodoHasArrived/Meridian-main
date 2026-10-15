import { act, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import * as bankingApi from "@/lib/api/banking-payments.api";
import { BankingPaymentApprovalsPanel } from "@/screens/accounting-screen.payment-approvals";
import type { PendingPayment } from "@/types/banking-payments.types";

vi.mock("@/lib/api/banking-payments.api", () => ({
  getPendingPayments: vi.fn(),
  getBankTransactions: vi.fn(),
  approvePendingPayment: vi.fn(),
  rejectPendingPayment: vi.fn()
}));

const api = vi.mocked(bankingApi);

const pending: PendingPayment = {
  pendingPaymentId: "pay-1",
  entityId: "entity-1",
  amount: 125_000,
  effectiveDate: "2026-06-01",
  externalRef: "WIRE-8891",
  notes: "Quarterly servicer remittance",
  status: 0,
  reviewedBy: null,
  reviewNotes: null,
  initiatedAt: "2026-05-29T09:00:00Z",
  reviewedAt: null,
  currency: "USD"
};

afterEach(() => {
  vi.resetAllMocks();
});

function primeReads(payments: PendingPayment[] = [pending]) {
  api.getPendingPayments.mockResolvedValue(payments);
  api.getBankTransactions.mockResolvedValue([]);
}

describe("BankingPaymentApprovalsPanel", () => {
  it("requires a selected entity before requesting payment or bank evidence", () => {
    render(<BankingPaymentApprovalsPanel />);
    expect(screen.getByText("Select an entity to review its payment intents.")).toBeInTheDocument();
    expect(api.getPendingPayments).not.toHaveBeenCalled();
    expect(api.getBankTransactions).not.toHaveBeenCalled();
  });

  it("lists payment intents awaiting review", async () => {
    primeReads();
    render(<BankingPaymentApprovalsPanel entityId="entity-1" />);

    const table = await screen.findByLabelText("Payment intents");
    expect(within(table).getByText("WIRE-8891")).toBeInTheDocument();
    expect(within(table).getByText("Pending")).toBeInTheDocument();
    expect(within(screen.getByLabelText("Payment approval posture")).getByText("125,000.00 USD")).toBeInTheDocument();
    expect(api.getPendingPayments).toHaveBeenCalledWith("entity-1", expect.objectContaining({ signal: expect.any(AbortSignal) }));
    expect(api.getBankTransactions).toHaveBeenCalledWith("entity-1", expect.objectContaining({ signal: expect.any(AbortSignal) }));
  });

  it("approves an intent with the reviewer's notes", async () => {
    primeReads();
    api.approvePendingPayment.mockResolvedValue({ ...pending, status: 1, reviewedBy: "controller@example.com" });
    const user = userEvent.setup();
    render(<BankingPaymentApprovalsPanel entityId="entity-1" />);

    await screen.findByLabelText("Payment intents");
    await user.click(screen.getByRole("button", { name: "Approve" }));
    await user.type(screen.getByLabelText("Approval notes"), "Matches the servicer schedule");
    await user.click(screen.getByRole("button", { name: "Confirm approve" }));

    await waitFor(() => expect(api.approvePendingPayment).toHaveBeenCalledWith("pay-1", "entity-1", {
      reviewNotes: "Matches the servicer schedule",
      actionOrigin: "HumanOperator"
    }));
    expect(await screen.findByText("Payment pay-1 is now Approved.")).toBeInTheDocument();
  });

  it("refuses a rejection with no reason instead of recording an unexplained decision", async () => {
    primeReads();
    const user = userEvent.setup();
    render(<BankingPaymentApprovalsPanel entityId="entity-1" />);

    await screen.findByLabelText("Payment intents");
    await user.click(screen.getByRole("button", { name: "Reject" }));
    await user.click(screen.getByRole("button", { name: "Confirm reject" }));

    expect(await screen.findByText("A rejection needs a reason; it is retained with the decision.")).toBeInTheDocument();
    expect(api.rejectPendingPayment).not.toHaveBeenCalled();
  });

  it("warns about intents whose currency was never retained", async () => {
    primeReads([{ ...pending, currency: null }]);
    render(<BankingPaymentApprovalsPanel entityId="entity-1" />);

    expect(await screen.findByText("1 payment intent(s) have no retained currency")).toBeInTheDocument();
    expect(screen.getByText("Currency not retained")).toBeInTheDocument();
  });

  it("offers no decision on an intent that was already reviewed", async () => {
    primeReads([{ ...pending, status: 1, reviewedBy: "controller@example.com" }]);
    render(<BankingPaymentApprovalsPanel entityId="entity-1" />);

    await screen.findByLabelText("Payment intents");
    expect(screen.queryByRole("button", { name: "Approve" })).not.toBeInTheDocument();
    expect(screen.getByText("Approved by controller@example.com")).toBeInTheDocument();
  });

  it("surfaces a failed queue read rather than showing an empty queue", async () => {
    api.getPendingPayments.mockRejectedValue(new Error("banking service unavailable"));
    api.getBankTransactions.mockResolvedValue([]);
    render(<BankingPaymentApprovalsPanel entityId="entity-1" />);

    expect(await screen.findByText("banking service unavailable")).toBeInTheDocument();
  });

  it("discards pending decisions and evidence when entity scope changes", async () => {
    primeReads();
    const user = userEvent.setup();
    const { rerender } = render(<BankingPaymentApprovalsPanel entityId="entity-1" />);
    await user.click(await screen.findByRole("button", { name: "Reject" }));
    await user.type(screen.getByLabelText("Rejection reason"), "Entity one review");

    let finishRead!: (payments: PendingPayment[]) => void;
    api.getPendingPayments.mockImplementationOnce(() => new Promise(resolve => { finishRead = resolve; }));
    rerender(<BankingPaymentApprovalsPanel entityId="entity-2" />);
    expect(screen.queryByText("WIRE-8891")).not.toBeInTheDocument();
    expect(screen.queryByLabelText("Rejection reason")).not.toBeInTheDocument();
    expect(screen.queryByText("125,000.00 USD")).not.toBeInTheDocument();

    await act(async () => finishRead([{ ...pending, entityId: "entity-2", externalRef: "ENTITY-2-WIRE" }]));
    await user.click(screen.getByRole("button", { name: "Reject" }));
    expect(screen.getByLabelText("Rejection reason")).toHaveValue("");
    await user.type(screen.getByLabelText("Rejection reason"), "Entity two review");
    api.rejectPendingPayment.mockResolvedValue({ ...pending, entityId: "entity-2", status: 2 });
    await user.click(screen.getByRole("button", { name: "Confirm reject" }));
    expect(api.rejectPendingPayment).toHaveBeenCalledWith("pay-1", "entity-2", { reason: "Entity two review", actionOrigin: "HumanOperator" });
  });

  it("ignores late reads and decision completions from the previous entity", async () => {
    primeReads();
    const user = userEvent.setup();
    let finishDecision!: (payment: PendingPayment) => void;
    api.approvePendingPayment.mockImplementationOnce(() => new Promise(resolve => { finishDecision = resolve; }));
    const { rerender } = render(<BankingPaymentApprovalsPanel entityId="entity-1" />);
    await user.click(await screen.findByRole("button", { name: "Approve" }));
    await user.click(screen.getByRole("button", { name: "Confirm approve" }));
    let finishRead!: (payments: PendingPayment[]) => void;
    api.getPendingPayments.mockImplementationOnce(() => new Promise(resolve => { finishRead = resolve; }));
    await user.click(screen.getByRole("button", { name: "Refresh" }));

    api.getPendingPayments.mockResolvedValue([{ ...pending, entityId: "entity-2", externalRef: "ENTITY-2-WIRE" }]);
    rerender(<BankingPaymentApprovalsPanel entityId="entity-2" />);
    await screen.findByText("ENTITY-2-WIRE");
    const readsBeforeCompletion = api.getPendingPayments.mock.calls.length;
    await act(async () => {
      finishRead([pending]);
      finishDecision({ ...pending, status: 1 });
    });
    expect(screen.getByText("ENTITY-2-WIRE")).toBeInTheDocument();
    expect(screen.queryByText("WIRE-8891")).not.toBeInTheDocument();
    expect(screen.queryByText("Payment decision recorded")).not.toBeInTheDocument();
    expect(api.getPendingPayments).toHaveBeenCalledTimes(readsBeforeCompletion);
  });
});
