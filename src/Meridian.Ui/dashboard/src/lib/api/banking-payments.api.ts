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
  BANKING_API_ENDPOINTS,
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

export function getPendingPayments(options: ApiRequestOptions = {}): Promise<PendingPayment[]> {
  return apiGetJson<PendingPayment[]>(BANKING_API_ENDPOINTS.pendingPayments, options);
}

export function getBankTransactions(entityId?: string, options: ApiRequestOptions = {}): Promise<BankTransaction[]> {
  return apiGetJson<BankTransaction[]>(bankingTransactionsEndpoint(entityId), options);
}

export function approvePendingPayment(
  pendingPaymentId: string,
  request: ApprovePaymentRequest,
  options: ApiRequestOptions = {}
): Promise<PendingPayment> {
  return apiPostJson<PendingPayment>(bankingPaymentApproveEndpoint(pendingPaymentId), request, options);
}

export function rejectPendingPayment(
  pendingPaymentId: string,
  request: RejectPaymentRequest,
  options: ApiRequestOptions = {}
): Promise<PendingPayment> {
  return apiPostJson<PendingPayment>(bankingPaymentRejectEndpoint(pendingPaymentId), request, options);
}
