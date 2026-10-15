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
  it("lists payment intents awaiting review", async () => {
    primeReads();
    render(<BankingPaymentApprovalsPanel entityId="entity-1" />);

    const table = await screen.findByLabelText("Payment intents");
    expect(within(table).getByText("WIRE-8891")).toBeInTheDocument();
    expect(within(table).getByText("Pending")).toBeInTheDocument();
    expect(within(screen.getByLabelText("Payment approval posture")).getByText("125,000.00 USD")).toBeInTheDocument();
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
    }, expect.objectContaining({ signal: expect.any(AbortSignal) })));
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

  it("requires an entity before loading or deciding payments", () => {
    render(<BankingPaymentApprovalsPanel />);
    expect(screen.getByText("Select an entity to review payments")).toBeInTheDocument();
    expect(api.getPendingPayments).not.toHaveBeenCalled();
    expect(api.getBankTransactions).not.toHaveBeenCalled();
    expect(screen.getByRole("button", { name: "Refresh" })).toBeDisabled();
  });

  it("resets a pending decision, notes and queue immediately when the entity changes", async () => {
    primeReads();
    const next = deferred<PendingPayment[]>();
    const user = userEvent.setup();
    const { rerender } = render(<BankingPaymentApprovalsPanel entityId="entity-1" />);
    await user.click(await screen.findByRole("button", { name: "Reject" }));
    await user.type(screen.getByLabelText("Rejection reason"), "Entity one's decision");

    api.getPendingPayments.mockReturnValueOnce(next.promise);
    rerender(<BankingPaymentApprovalsPanel entityId="entity-2" />);
    expect(screen.queryByText("WIRE-8891")).not.toBeInTheDocument();
    expect(screen.queryByLabelText("Rejection reason")).not.toBeInTheDocument();
    expect(screen.getByText("Loading payment intents…")).toBeInTheDocument();
    expect(api.getPendingPayments).toHaveBeenLastCalledWith("entity-2", expect.any(Object));
    expect(api.getBankTransactions).toHaveBeenLastCalledWith("entity-2", expect.any(Object));

    await act(async () => next.resolve([{ ...pending, entityId: "entity-2", externalRef: "ENTITY-TWO" }]));
    await user.click(screen.getByRole("button", { name: "Reject" }));
    expect(screen.getByLabelText("Rejection reason")).toHaveValue("");
    expect(api.rejectPendingPayment).not.toHaveBeenCalled();
  });

  it("clears the queue and pending decision when entity scope is removed", async () => {
    primeReads();
    const user = userEvent.setup();
    const { rerender } = render(<BankingPaymentApprovalsPanel entityId="entity-1" />);
    await user.click(await screen.findByRole("button", { name: "Approve" }));
    rerender(<BankingPaymentApprovalsPanel />);
    expect(screen.queryByText("WIRE-8891")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Confirm approve" })).not.toBeInTheDocument();
    expect(screen.getByText("Select an entity to review payments")).toBeInTheDocument();
    expect(api.getPendingPayments).toHaveBeenCalledTimes(1);
  });

  it("ignores a late decision from the previous entity without refreshing its queue", async () => {
    primeReads();
    const result = deferred<PendingPayment>();
    api.approvePendingPayment.mockReturnValueOnce(result.promise);
    const user = userEvent.setup();
    const { rerender } = render(<BankingPaymentApprovalsPanel entityId="entity-1" />);
    await user.click(await screen.findByRole("button", { name: "Approve" }));
    await user.click(screen.getByRole("button", { name: "Confirm approve" }));
    const signal = api.approvePendingPayment.mock.calls[0][3]!.signal!;

    api.getPendingPayments.mockResolvedValue([{ ...pending, entityId: "entity-2", externalRef: "ENTITY-TWO" }]);
    rerender(<BankingPaymentApprovalsPanel entityId="entity-2" />);
    await screen.findByText("ENTITY-TWO");
    expect(signal.aborted).toBe(true);
    await act(async () => result.resolve({ ...pending, status: 1 }));
    expect(screen.queryByText("Payment decision recorded")).not.toBeInTheDocument();
    expect(api.getPendingPayments).toHaveBeenCalledTimes(2);
    expect(screen.getByRole("button", { name: "Approve" })).toBeEnabled();
  });

  it("discards late reads after an entity switch and preserves the new scope's failure", async () => {
    const previous = deferred<PendingPayment[]>();
    api.getPendingPayments.mockReturnValueOnce(previous.promise).mockRejectedValueOnce(new Error("Entity two unavailable"));
    api.getBankTransactions.mockResolvedValue([]);
    const { rerender } = render(<BankingPaymentApprovalsPanel entityId="entity-1" />);
    const signal = api.getPendingPayments.mock.calls[0][1]!.signal!;
    rerender(<BankingPaymentApprovalsPanel entityId="entity-2" />);
    await screen.findByText("Entity two unavailable");
    expect(signal.aborted).toBe(true);
    await act(async () => previous.resolve([pending]));
    expect(screen.queryByText("WIRE-8891")).not.toBeInTheDocument();
    expect(screen.getByText("Entity two unavailable")).toBeInTheDocument();
  });

});

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((resolve_) => { resolve = resolve_; });
  return { promise, resolve };
}
