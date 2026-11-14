import { apiGetBlob, apiGetJson, apiPostJson, type ApiRequestOptions } from "@/lib/api/http-client";
import { UI_API_ROUTES } from "@/lib/ui-api-routes.generated";
import type { LedgerBook } from "@/types";
import type { AccountingPeriodOption, ActivatePostingGrant, AutomatedPostingGrant, PrepareThreeWayAdjustment, PreparedThreeWayAdjustmentDraft, ThreeWayReconciliation } from "@/types/accounting-integration";

const exact = (options: ApiRequestOptions = {}) => ({ ...options, allowDevelopmentFallback: false });
const id = encodeURIComponent;
const grantRoot = "/api/ledger/automation/grants";
const reconciliationRoot = (accountId: string) => `/api/fund-accounts/${id(accountId)}/brokerage-sync`;

export function listIntegrationBooks(options: ApiRequestOptions = {}) {
  return apiGetJson<LedgerBook[]>("/api/ledger/books", exact(options));
}
export function listIntegrationPeriods(bookId: string, options: ApiRequestOptions = {}) {
  return apiGetJson<AccountingPeriodOption[]>(`/api/ledger/periods?ledgerBookId=${id(bookId)}`, exact(options));
}
export function getIntegrationMapping(book: LedgerBook, options: ApiRequestOptions = {}) {
  return apiGetJson<{ configurationVersion: string }>(`/api/ledger/accounting-configuration?fundProfileId=${id(book.fundProfileId)}&ledgerBookId=${id(book.ledgerBookId)}`, exact(options));
}
export function activatePostingGrant(request: ActivatePostingGrant, options: ApiRequestOptions = {}) {
  return apiPostJson<AutomatedPostingGrant>(grantRoot, request, exact(options));
}
export function getPostingGrant(grantId: string, options: ApiRequestOptions = {}) {
  return apiGetJson<AutomatedPostingGrant>(`${grantRoot}/${id(grantId)}`, exact(options));
}
export function revokePostingGrant(grantId: string, expectedVersion: number, options: ApiRequestOptions = {}) {
  return apiPostJson<AutomatedPostingGrant>(`${grantRoot}/${id(grantId)}/revoke`, { expectedVersion, actionOrigin: "HumanOperator" }, exact(options));
}
export function runThreeWayReconciliation(accountId: string, ledgerBookId: string, accountingPeriodId: string,
  operationId: string, options: ApiRequestOptions = {}) {
  return apiPostJson<ThreeWayReconciliation>(`${reconciliationRoot(accountId)}/reconcile-three-way`,
    { ledgerBookId, accountingPeriodId, operationId }, exact(options));
}
export function getLatestThreeWayReconciliation(accountId: string, bookId: string, periodId: string, options: ApiRequestOptions = {}) {
  return apiGetJson<ThreeWayReconciliation | null>(`${reconciliationRoot(accountId)}/three-way-reconciliation/latest?ledgerBookId=${id(bookId)}&accountingPeriodId=${id(periodId)}`, exact(options));
}
export function prepareThreeWayAdjustment(accountId: string, request: PrepareThreeWayAdjustment, options: ApiRequestOptions = {}) {
  return apiPostJson<PreparedThreeWayAdjustmentDraft>(`${reconciliationRoot(accountId)}/three-way-reconciliation/adjustments/prepare`, request, exact(options));
}
export function downloadGeneralLedger(bookId: string, periodId: string, options: ApiRequestOptions = {}) {
  return apiGetBlob(`/api/ledger/books/${id(bookId)}/periods/${id(periodId)}/exports/general-ledger`, exact(options));
}

/** Read current session authority; a role label or reporting-release capability is insufficient. */
export async function getGeneralLedgerExportAccess(options: ApiRequestOptions = {}): Promise<boolean> {
  const caller = await apiGetJson<{ permissionNames?: unknown }>(UI_API_ROUTES.AuthApiMe, exact(options));
  if (!Array.isArray(caller.permissionNames) || caller.permissionNames.some(permission => typeof permission !== "string")) {
    throw new Error("The current session's export permissions are unavailable. Sign in again before downloading accounting records.");
  }
  return caller.permissionNames.includes("ExportData");
}
