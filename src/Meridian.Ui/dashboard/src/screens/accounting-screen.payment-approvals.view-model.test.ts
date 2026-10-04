import { describe, expect, it } from "vitest";
import {
  buildPaymentQueueSummary,
  buildPendingPaymentRow,
  paymentStatusLabel
} from "@/screens/accounting-screen.payment-approvals.view-model";
import type { BankTransaction, PendingPayment } from "@/types/banking-payments.types";

function payment(overrides: Partial<PendingPayment> = {}): PendingPayment {
  return {
    pendingPaymentId: "11111111-1111-1111-1111-111111111111",
    entityId: "22222222-2222-2222-2222-222222222222",
    amount: 125_000,
    effectiveDate: "2026-06-01",
    externalRef: "WIRE-8891",
    notes: "Quarterly servicer remittance",
    status: 0,
    reviewedBy: null,
    reviewNotes: null,
    initiatedAt: "2026-05-29T09:00:00Z",
    reviewedAt: null,
    currency: "USD",
    ...overrides
  };
}

function transaction(overrides: Partial<BankTransaction> = {}): BankTransaction {
  return {
    bankTransactionId: "33333333-3333-3333-3333-333333333333",
    entityId: "22222222-2222-2222-2222-222222222222",
    transactionType: "Debit",
    effectiveDate: "2026-06-01",
    transactionDate: "2026-06-01",
    settlementDate: "2026-06-02",
    amount: 125_000,
    currency: "USD",
    externalRef: "WIRE-8891",
    recordedAt: "2026-06-02T10:00:00Z",
    isVoided: false,
    pendingPaymentId: "11111111-1111-1111-1111-111111111111",
    ...overrides
  };
}

describe("paymentStatusLabel", () => {
  it("mirrors the declared PaymentApprovalStatus values", () => {
    expect([0, 1, 2, 3].map(paymentStatusLabel)).toEqual(["Pending", "Approved", "Rejected", "Cancelled"]);
  });

  it("names an unrecognized ordinal rather than guessing a status", () => {
    expect(paymentStatusLabel(9)).toBe("Unknown status (9)");
  });
});

describe("buildPendingPaymentRow", () => {
  it("offers a decision only while the intent is still pending", () => {
    expect(buildPendingPaymentRow(payment()).canDecide).toBe(true);
    expect(buildPendingPaymentRow(payment({ status: 1 })).canDecide).toBe(false);
    expect(buildPendingPaymentRow(payment({ status: 2 })).canDecide).toBe(false);
  });

  it("renders the decision and its reviewer once one is recorded", () => {
    const row = buildPendingPaymentRow(payment({
      status: 2,
      reviewedBy: "controller@example.com",
      reviewNotes: "Duplicate of WIRE-8890"
    }));

    expect(row.statusTone).toBe("danger");
    expect(row.decisionSummary).toBe("Rejected by controller@example.com — Duplicate of WIRE-8890");
  });

  it("says a decision is unattributed rather than inventing a reviewer", () => {
    const row = buildPendingPaymentRow(payment({ status: 1, reviewedBy: null }));
    expect(row.decisionSummary).toBe("Approved by unattributed reviewer");
  });

  it("reports an unretained currency instead of defaulting to one", () => {
    const row = buildPendingPaymentRow(payment({ currency: null }));
    expect(row.currencyMissing).toBe(true);
    expect(row.currencyLabel).toBe("Currency not retained");
    expect(row.ariaLabel).toContain("in an unretained currency");
  });
});

describe("buildPaymentQueueSummary", () => {
  it("totals pending value per currency rather than across them", () => {
    const summary = buildPaymentQueueSummary(
      [payment(), payment({ pendingPaymentId: "b", amount: 40_000, currency: "EUR" })],
      []
    );

    expect(summary.pendingCount).toBe(2);
    expect(summary.pendingValueLabel).toBe("40,000.00 EUR · 125,000.00 USD");
  });

  it("counts intents with no currency apart from the totals they cannot join", () => {
    const summary = buildPaymentQueueSummary(
      [payment(), payment({ pendingPaymentId: "b", amount: 9_000, currency: null })],
      []
    );

    expect(summary.unremediatedCount).toBe(1);
    expect(summary.pendingValueLabel).toBe("125,000.00 USD (+1 uncosted)");
  });

  it("declines to total at all when no pending intent carries a currency", () => {
    const summary = buildPaymentQueueSummary([payment({ currency: null })], []);
    expect(summary.pendingValueLabel).toBe("Not totalable");
  });

  it("excludes decided intents and voided evidence from the counts", () => {
    const summary = buildPaymentQueueSummary(
      [payment({ status: 1 })],
      [transaction(), transaction({ bankTransactionId: "d", isVoided: true }), transaction({ bankTransactionId: "e", pendingPaymentId: null })]
    );

    expect(summary.pendingCount).toBe(0);
    expect(summary.pendingValueLabel).toBe("—");
    expect(summary.evidenceCount).toBe(1);
  });
});
