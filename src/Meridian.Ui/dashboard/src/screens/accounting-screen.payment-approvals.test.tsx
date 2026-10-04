import { render, screen, waitFor, within } from "@testing-library/react";
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
    render(<BankingPaymentApprovalsPanel />);

    const table = await screen.findByLabelText("Payment intents");
    expect(within(table).getByText("WIRE-8891")).toBeInTheDocument();
    expect(within(table).getByText("Pending")).toBeInTheDocument();
    expect(within(screen.getByLabelText("Payment approval posture")).getByText("125,000.00 USD")).toBeInTheDocument();
  });

  it("approves an intent with the reviewer's notes", async () => {
    primeReads();
    api.approvePendingPayment.mockResolvedValue({ ...pending, status: 1, reviewedBy: "controller@example.com" });
    const user = userEvent.setup();
    render(<BankingPaymentApprovalsPanel />);

    await screen.findByLabelText("Payment intents");
    await user.click(screen.getByRole("button", { name: "Approve" }));
    await user.type(screen.getByLabelText("Approval notes"), "Matches the servicer schedule");
    await user.click(screen.getByRole("button", { name: "Confirm approve" }));

    await waitFor(() => expect(api.approvePendingPayment).toHaveBeenCalledWith("pay-1", {
      reviewNotes: "Matches the servicer schedule",
      actionOrigin: "HumanOperator"
    }));
    expect(await screen.findByText("Payment pay-1 is now Approved.")).toBeInTheDocument();
  });

  it("refuses a rejection with no reason instead of recording an unexplained decision", async () => {
    primeReads();
    const user = userEvent.setup();
    render(<BankingPaymentApprovalsPanel />);

    await screen.findByLabelText("Payment intents");
    await user.click(screen.getByRole("button", { name: "Reject" }));
    await user.click(screen.getByRole("button", { name: "Confirm reject" }));

    expect(await screen.findByText("A rejection needs a reason; it is retained with the decision.")).toBeInTheDocument();
    expect(api.rejectPendingPayment).not.toHaveBeenCalled();
  });

  it("warns about intents whose currency was never retained", async () => {
    primeReads([{ ...pending, currency: null }]);
    render(<BankingPaymentApprovalsPanel />);

    expect(await screen.findByText("1 payment intent(s) have no retained currency")).toBeInTheDocument();
    expect(screen.getByText("Currency not retained")).toBeInTheDocument();
  });

  it("offers no decision on an intent that was already reviewed", async () => {
    primeReads([{ ...pending, status: 1, reviewedBy: "controller@example.com" }]);
    render(<BankingPaymentApprovalsPanel />);

    await screen.findByLabelText("Payment intents");
    expect(screen.queryByRole("button", { name: "Approve" })).not.toBeInTheDocument();
    expect(screen.getByText("Approved by controller@example.com")).toBeInTheDocument();
  });

  it("surfaces a failed queue read rather than showing an empty queue", async () => {
    api.getPendingPayments.mockRejectedValue(new Error("banking service unavailable"));
    api.getBankTransactions.mockResolvedValue([]);
    render(<BankingPaymentApprovalsPanel />);

    expect(await screen.findByText("banking service unavailable")).toBeInTheDocument();
  });
});
