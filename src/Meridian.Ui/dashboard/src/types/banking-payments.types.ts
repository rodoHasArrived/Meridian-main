/**
 * Banking payment-approval read models.
 *
 * Mirrors `Meridian.Contracts.Banking`. `PaymentApprovalStatus` is a bare .NET
 * enum with no string converter, so it arrives as its declared ordinal — the C#
 * declaration assigns those values explicitly (`Pending = 0` … `Cancelled = 3`)
 * rather than relying on position, which is what makes the mirror below safe.
 */

/** Ordinal of `PaymentApprovalStatus`. */
export type PaymentApprovalStatusOrdinal = number;

/**
 * Origin of a governed action. This enum *is* string-converted on the wire, so
 * the request bodies below send the name. Re-exported from the shared barrel,
 * which already declares the same union for the operations-continuity surface.
 */
import type { OperationsActionOrigin } from "@/types";

export type { OperationsActionOrigin };

export interface PendingPayment {
  pendingPaymentId: string;
  /** Opaque entity identifier — a loan, account, or counterparty id. */
  entityId: string;
  amount: number;
  effectiveDate: string;
  externalRef?: string | null;
  notes?: string | null;
  status: PaymentApprovalStatusOrdinal;
  reviewedBy?: string | null;
  reviewNotes?: string | null;
  initiatedAt: string;
  reviewedAt?: string | null;
  /**
   * Null identifies a legacy intent whose currency was never retained. It must be
   * remediated before bank evidence or transfer authorization can proceed, so the
   * queue surfaces it rather than defaulting to a currency.
   */
  currency?: string | null;
  currencyRemediatedBy?: string | null;
  currencyRemediationReason?: string | null;
  currencyRemediatedAt?: string | null;
}

export interface BankTransaction {
  bankTransactionId: string;
  entityId: string;
  transactionType: string;
  effectiveDate: string;
  transactionDate: string;
  settlementDate: string;
  amount: number;
  currency: string;
  externalRef?: string | null;
  recordedAt: string;
  isVoided: boolean;
  recordedBy?: string | null;
  /** Payment intent this evidence proves; null for a generic bank transaction. */
  pendingPaymentId?: string | null;
  evidenceId?: string | null;
  canonicalInputHash?: string | null;
}

export interface ApprovePaymentRequest {
  reviewNotes?: string | null;
  reviewedBy?: string | null;
  actionOrigin?: OperationsActionOrigin;
}

export interface RejectPaymentRequest {
  reason: string;
  reviewedBy?: string | null;
  actionOrigin?: OperationsActionOrigin;
}
