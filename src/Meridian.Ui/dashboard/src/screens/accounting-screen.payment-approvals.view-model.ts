/**
 * Presentation logic for the banking payment-approval queue.
 *
 * Two rules shape every field here, because approving a payment moves money:
 *
 * 1. A payment whose currency was never retained is shown as unremediated, not
 *    given a default currency. The server refuses evidence and authorization on
 *    such an intent, so the queue must refuse to imply one too.
 * 2. Approve and reject are only offered on an intent still in Pending. A decided
 *    payment renders its decision instead of an action.
 */

import type { BankTransaction, PendingPayment } from "@/types/banking-payments.types";

export type PaymentApprovalTone = "default" | "success" | "warning" | "danger";

/**
 * `PaymentApprovalStatus` as the C# enum declares it, with explicit values:
 * Pending = 0, Approved = 1, Rejected = 2, Cancelled = 3.
 */
const PAYMENT_STATUS_LABELS: Record<number, string> = {
  0: "Pending",
  1: "Approved",
  2: "Rejected",
  3: "Cancelled"
};

const PAYMENT_STATUS_TONES: Record<number, PaymentApprovalTone> = {
  0: "warning",
  1: "success",
  2: "danger",
  3: "default"
};

const PENDING_STATUS = 0;

export interface PendingPaymentRow {
  pendingPaymentId: string;
  entityId: string;
  amount: string;
  currencyLabel: string;
  currencyMissing: boolean;
  effectiveDate: string;
  externalRef: string;
  notes: string;
  statusLabel: string;
  statusTone: PaymentApprovalTone;
  initiatedAt: string;
  decisionSummary: string | null;
  canDecide: boolean;
  ariaLabel: string;
}

export interface PaymentQueueSummary {
  pendingCount: number;
  pendingValueLabel: string;
  unremediatedCount: number;
  evidenceCount: number;
  emptyMessage: string;
}

export function paymentStatusLabel(status: number): string {
  return PAYMENT_STATUS_LABELS[status] ?? `Unknown status (${status})`;
}

export function paymentStatusTone(status: number): PaymentApprovalTone {
  return PAYMENT_STATUS_TONES[status] ?? "warning";
}

export function buildPendingPaymentRow(payment: PendingPayment): PendingPaymentRow {
  const currencyMissing = !payment.currency;
  const decided = payment.status !== PENDING_STATUS;

  return {
    pendingPaymentId: payment.pendingPaymentId,
    entityId: payment.entityId,
    amount: formatAmount(payment.amount),
    // No fallback currency: a missing code is a repair the server requires, and
    // printing "USD" here would invent the fact it is waiting for.
    currencyLabel: payment.currency ?? "Currency not retained",
    currencyMissing,
    effectiveDate: payment.effectiveDate,
    externalRef: payment.externalRef ?? "—",
    notes: payment.notes ?? "—",
    statusLabel: paymentStatusLabel(payment.status),
    statusTone: paymentStatusTone(payment.status),
    initiatedAt: payment.initiatedAt,
    decisionSummary: decided
      ? `${paymentStatusLabel(payment.status)} by ${payment.reviewedBy ?? "unattributed reviewer"}${payment.reviewNotes ? ` — ${payment.reviewNotes}` : ""}`
      : null,
    canDecide: !decided,
    ariaLabel: `Payment ${formatAmount(payment.amount)} ${payment.currency ?? "in an unretained currency"} effective ${payment.effectiveDate}, ${paymentStatusLabel(payment.status)}`
  };
}

export function buildPaymentQueueSummary(
  payments: readonly PendingPayment[],
  transactions: readonly BankTransaction[]
): PaymentQueueSummary {
  const pending = payments.filter((payment) => payment.status === PENDING_STATUS);
  const unremediated = pending.filter((payment) => !payment.currency);

  return {
    pendingCount: pending.length,
    // Summing across unretained currencies would be a made-up total, so the
    // value line reports only what carries a currency and says how many do not.
    pendingValueLabel: summarizePendingValue(pending),
    unremediatedCount: unremediated.length,
    evidenceCount: transactions.filter((transaction) => transaction.pendingPaymentId && !transaction.isVoided).length,
    emptyMessage: payments.length === 0
      ? "No payment intents are recorded."
      : "No payment intents match this view."
  };
}

function summarizePendingValue(pending: readonly PendingPayment[]): string {
  const byCurrency = new Map<string, number>();
  let withoutCurrency = 0;
  for (const payment of pending) {
    if (!payment.currency) {
      withoutCurrency += 1;
      continue;
    }
    byCurrency.set(payment.currency, (byCurrency.get(payment.currency) ?? 0) + payment.amount);
  }

  const totals = [...byCurrency.entries()]
    .sort(([left], [right]) => left.localeCompare(right))
    .map(([currency, amount]) => `${formatAmount(amount)} ${currency}`);

  if (totals.length === 0) {
    return withoutCurrency > 0 ? "Not totalable" : "—";
  }

  return withoutCurrency > 0 ? `${totals.join(" · ")} (+${withoutCurrency} uncosted)` : totals.join(" · ");
}

function formatAmount(value: number): string {
  return value.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}
