/**
 * Client functions for the banking payment-approval queue.
 *
 * Thin wrappers over the shared workstation API client so errors, aborts, and
 * no-host fixture semantics stay aligned with the rest of the dashboard. Payment
 * initiation, bank evidence, and currency remediation are deliberately not
 * wrapped here: each is its own governed flow with its own operator surface, and
 * an unused wrapper would report those routes as wired.
 */

import { apiGetJson, apiPostJson, type ApiRequestOptions } from "@/lib/api";
import {
  bankingPendingPaymentsEndpoint,
  bankingPaymentApproveEndpoint,
  bankingPaymentRejectEndpoint,
  bankingTransactionsEndpoint
} from "@/lib/workstation-endpoints";
import type {
  ApprovePaymentRequest,
  BankTransaction,
  PendingPayment,
  RejectPaymentRequest
} from "@/types/banking-payments.types";

export function getPendingPayments(entityId: string, options: ApiRequestOptions = {}): Promise<PendingPayment[]> {
  return apiGetJson<PendingPayment[]>(bankingPendingPaymentsEndpoint(entityId), options);
}

export function getBankTransactions(entityId?: string, options: ApiRequestOptions = {}): Promise<BankTransaction[]> {
  return apiGetJson<BankTransaction[]>(bankingTransactionsEndpoint(entityId), options);
}

export function approvePendingPayment(
  pendingPaymentId: string,
  entityId: string,
  request: ApprovePaymentRequest,
  options: ApiRequestOptions = {}
): Promise<PendingPayment> {
  return apiPostJson<PendingPayment>(bankingPaymentApproveEndpoint(pendingPaymentId, entityId), request, options);
}

export function rejectPendingPayment(
  pendingPaymentId: string,
  entityId: string,
  request: RejectPaymentRequest,
  options: ApiRequestOptions = {}
): Promise<PendingPayment> {
  return apiPostJson<PendingPayment>(bankingPaymentRejectEndpoint(pendingPaymentId, entityId), request, options);
}
