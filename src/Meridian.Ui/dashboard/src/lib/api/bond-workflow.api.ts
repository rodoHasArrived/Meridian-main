import { apiGetJson, apiPostJson, type ApiRequestOptions } from "@/lib/api";
import type {
  AccountBondIncomePlan, AccountBondIncomePlanningRequest, BondIncomePreview, BondIncomePreviewRequest,
  BondMonitoringCaptureRequest, BondMonitoringSnapshot, BondWorkflowRequest, BondWorkflowResult,
  BondComparisonRequest, BondComparisonResult, BondPortfolioRiskRequest, BondPortfolioRiskResult, BondMarketObservation, BondConnectionCapability,
  BondPerformanceRequest, BondPerformanceResult, BondScreeningQuery, BondScreeningPage,
  BondCouponRecognitionRequest, BondCouponRecognitionPreview, BondCouponRecognitionPreparation,
  BondIdentifierDiscoveryRequest, BondIdentifierDiscoveryResult, BondFredIntakeRequest, BondFredIntakeResult
} from "@/types/bond-workflow.types";

const accountPath = (bookId: string, accountId: string) =>
  `/api/ledger/books/${encodeURIComponent(bookId)}/accounts/${encodeURIComponent(accountId)}`;

export function calculateBondWorkflow(request: BondWorkflowRequest, options: ApiRequestOptions & { ledgerBookId?: string } = {}) {
  const { ledgerBookId, ...requestOptions } = options;
  if (request.accountId && !ledgerBookId?.trim()) return Promise.reject(new Error("Account pricing requires its authorized ledger book."));
  const path = request.accountId && ledgerBookId
    ? `${accountPath(ledgerBookId.trim(), request.accountId)}/bond-workflows/${encodeURIComponent(request.securityId)}`
    : `/api/reference-data/bonds/${encodeURIComponent(request.securityId)}/workflow`;
  return apiPostJson<BondWorkflowResult>(path, request, { ...requestOptions, allowDevelopmentFallback: false });
}
export function planAccountBondIncome(bookId: string, accountId: string, request: AccountBondIncomePlanningRequest, options: ApiRequestOptions = {}) {
  return apiPostJson<AccountBondIncomePlan>(`${accountPath(bookId, accountId)}/bond-income/preview`, request,
    { ...options, allowDevelopmentFallback: false });
}
export function getBondMonitoring(bookId: string, accountId: string, options: ApiRequestOptions = {}) {
  return apiGetJson<BondMonitoringSnapshot[]>(`${accountPath(bookId, accountId)}/bond-monitoring`,
    { ...options, allowDevelopmentFallback: false });
}
export function captureBondMonitoring(bookId: string, accountId: string, request: BondMonitoringCaptureRequest, options: ApiRequestOptions = {}) {
  return apiPostJson<BondMonitoringSnapshot>(`${accountPath(bookId, accountId)}/bond-monitoring`, request,
    { ...options, allowDevelopmentFallback: false });
}
export function previewBondIncome(bookId: string, accountId: string, request: BondIncomePreviewRequest, options: ApiRequestOptions = {}) {
  return apiPostJson<BondIncomePreview>(`${accountPath(bookId, accountId)}/bond-accounting/preview`, request,
    { ...options, allowDevelopmentFallback: false });
}
export function saveBondComparison(bookId: string, accountId: string, request: BondComparisonRequest, options: ApiRequestOptions = {}) {
  return apiPostJson<BondComparisonResult>(`${accountPath(bookId, accountId)}/bond-comparisons`, request, { ...options, allowDevelopmentFallback: false });
}
export function loadBondComparison(bookId: string, accountId: string, runId: string, options: ApiRequestOptions = {}) {
  return apiGetJson<{ request: BondComparisonRequest; result: BondComparisonResult }>(`${accountPath(bookId, accountId)}/bond-comparisons/${encodeURIComponent(runId)}`, { ...options, allowDevelopmentFallback: false });
}
export function calculateAccountBondRisk(bookId: string, accountId: string, request: BondPortfolioRiskRequest, options: ApiRequestOptions = {}) {
  return apiPostJson<BondPortfolioRiskResult>(`${accountPath(bookId, accountId)}/bond-risk`, request, { ...options, allowDevelopmentFallback: false });
}
export function loadAccountBondRisk(bookId: string, accountId: string, runId: string, options: ApiRequestOptions = {}) {
  return apiGetJson<{ request: BondPortfolioRiskRequest; result: BondPortfolioRiskResult }>(`${accountPath(bookId, accountId)}/bond-risk/${encodeURIComponent(runId)}`, { ...options, allowDevelopmentFallback: false });
}
export function importAccountBondHoldings(bookId: string, accountId: string, request: { connectionId: string; syncRunId: string; accountId: string; batchId: string; asOfDate: string; revision: number; supersedesBatchId?: string | null }, options: ApiRequestOptions = {}) {
  return apiPostJson<{ batchId: string }>(`${accountPath(bookId, accountId)}/bond-holdings/import`, request, { ...options, allowDevelopmentFallback: false });
}
export function queryBondMarketObservations(securityId: string, options: ApiRequestOptions = {}) {
  return apiGetJson<BondMarketObservation[]>(`/api/reference-data/bonds/market-observations?securityId=${encodeURIComponent(securityId)}`, { ...options, allowDevelopmentFallback: false });
}
export function importBondMarketObservations(request: { observations: unknown[] }, options: ApiRequestOptions = {}) {
  return apiPostJson<{ observations: BondMarketObservation[]; appendedCount: number }>("/api/reference-data/bonds/market-observations", request, { ...options, allowDevelopmentFallback: false });
}
export function getBondConnectionReadiness(securityId: string, accountId?: string, options: ApiRequestOptions = {}) {
  const params = new URLSearchParams({ securityId }); if (accountId?.trim()) params.set("accountId", accountId.trim());
  return apiGetJson<BondConnectionCapability[]>(`/api/reference-data/bonds/connection-readiness?${params}`, { ...options, allowDevelopmentFallback: false });
}
export function explainBondPerformance(bookId: string, accountId: string, request: BondPerformanceRequest, options: ApiRequestOptions = {}) {
  return apiPostJson<BondPerformanceResult>(`${accountPath(bookId, accountId)}/bond-performance`, request, { ...options, allowDevelopmentFallback: false });
}
export function importBondPerformanceTransactions(bookId: string, accountId: string, request: { evidenceUri: string; documentId: string }, options: ApiRequestOptions = {}) {
  return apiPostJson<{ populationId: string }>(`${accountPath(bookId, accountId)}/bond-performance/transactions/import`, request, { ...options, allowDevelopmentFallback: false });
}
export function screenBonds(request: BondScreeningQuery, options: ApiRequestOptions = {}) {
  return apiPostJson<BondScreeningPage>("/api/reference-data/bonds/screen", request, { ...options, allowDevelopmentFallback: false });
}
export function previewBondCoupon(bookId: string, accountId: string, request: BondCouponRecognitionRequest, options: ApiRequestOptions = {}) {
  return apiPostJson<BondCouponRecognitionPreview>(`${accountPath(bookId, accountId)}/bond-accounting/coupon-preview`, request, { ...options, allowDevelopmentFallback: false });
}
export function prepareBondCoupon(bookId: string, accountId: string, request: { request: BondCouponRecognitionRequest; expectedFingerprint: string }, options: ApiRequestOptions = {}) {
  return apiPostJson<BondCouponRecognitionPreparation>(`${accountPath(bookId, accountId)}/bond-accounting/coupon-prepare`, request, { ...options, allowDevelopmentFallback: false });
}
export function discoverBondIdentifier(request: BondIdentifierDiscoveryRequest, options: ApiRequestOptions = {}) {
  return apiPostJson<BondIdentifierDiscoveryResult>("/api/reference-data/bonds/identifier-discovery", request, { ...options, allowDevelopmentFallback: false });
}
export function importBondFredObservations(request: BondFredIntakeRequest, options: ApiRequestOptions = {}) {
  return apiPostJson<BondFredIntakeResult>("/api/reference-data/bonds/fred-observations/import", request, { ...options, allowDevelopmentFallback: false });
}
