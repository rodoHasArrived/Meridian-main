import { apiGetJson, apiPostJson, type ApiRequestOptions } from "@/lib/api";

/** These operations never use development fallbacks for financial authority. */
export function getEntityOperation<T>(path: string, options: ApiRequestOptions = {}) {
  return apiGetJson<T>(path, { ...options, allowDevelopmentFallback: false });
}
export function postEntityOperation<T>(path: string, body: unknown, options: ApiRequestOptions = {}) {
  return apiPostJson<T>(path, body, { ...options, allowDevelopmentFallback: false });
}
export const entityOperationEndpoints = {
  capabilities: "/api/ledger/intercompany/capabilities",
  graph: "/api/fund-structure/graph?activeOnly=false",
  books: "/api/ledger/books",
  accountingArrangements: "/api/ledger/intercompany-accounting-arrangements",
  lifecycle: (entityId: string) => `/api/fund-structure/entities/${encodeURIComponent(entityId)}/lifecycle`,
  controls: (bookId: string) => `/api/ledger/books/${encodeURIComponent(bookId)}/controls`,
  proposals: (bookId: string) => `/api/ledger/intercompany/proposals?sourceBookId=${encodeURIComponent(bookId)}`,
  breaks: (bookId: string) => `/api/ledger/intercompany/breaks?sourceBookId=${encodeURIComponent(bookId)}`,
  counterpartCandidates: (proposalId: string, targetBookId: string) =>
    `/api/ledger/intercompany/proposals/${encodeURIComponent(proposalId)}/counterpart-candidates?${new URLSearchParams({ targetBookId })}`,
  accrualPreview: "/api/ledger/intercompany/accruals/preview",
  accrualDrafts: "/api/ledger/intercompany/accruals/drafts",
  feePreview: "/api/ledger/intercompany/fees/preview",
  feeDrafts: "/api/ledger/intercompany/fees/drafts",
  rateFixings: "/api/ledger/intercompany/rate-fixings",
  rateFixingRange: (indexReference: string, from: string, to: string) => `/api/ledger/intercompany/rate-fixings?${new URLSearchParams({ indexReference, from, to })}`,
  rateFixingDecisions: (fixingId: string) => `/api/ledger/intercompany/rate-fixings/${encodeURIComponent(fixingId)}/decisions`,
  confirmations: "/api/ledger/intercompany/confirmations",
  confirmationSummary: (bookId: string, asOf: string) => `/api/ledger/intercompany/confirmations?${new URLSearchParams({ ledgerBookId: bookId, asOf })}`,
  confirmationActions: (confirmationId: string) => `/api/ledger/intercompany/confirmations/${encodeURIComponent(confirmationId)}/actions`,
  confirmationAdjustmentDrafts: (confirmationId: string) => `/api/ledger/intercompany/confirmations/${encodeURIComponent(confirmationId)}/adjustment-drafts`,
  confirmationClosePolicies: "/api/ledger/intercompany/confirmation-close-policies",
  confirmationClosePolicyList: (bookId: string) => `/api/ledger/intercompany/confirmation-close-policies?${new URLSearchParams({ ledgerBookId: bookId })}`,
  confirmationClosePolicyDecisions: (policyId: string, bookId: string) =>
    `/api/ledger/intercompany/confirmation-close-policies/${encodeURIComponent(policyId)}/decisions?${new URLSearchParams({ ledgerBookId: bookId })}`,
  accrualSchedules: "/api/ledger/intercompany/accrual-schedules",
  accrualScheduleList: (bookId: string) => `/api/ledger/intercompany/accrual-schedules?${new URLSearchParams({ ledgerBookId: bookId })}`,
  accrualScheduleDecisions: (scheduleId: string, bookId: string) =>
    `/api/ledger/intercompany/accrual-schedules/${encodeURIComponent(scheduleId)}/decisions?${new URLSearchParams({ ledgerBookId: bookId })}`,
  accrualScheduleDue: (scheduleId: string, bookId: string, asOf: string) =>
    `/api/ledger/intercompany/accrual-schedules/${encodeURIComponent(scheduleId)}/due?${new URLSearchParams({ ledgerBookId: bookId, asOf })}`,
  accrualScheduleRun: (scheduleId: string, bookId: string) =>
    `/api/ledger/intercompany/accrual-schedules/${encodeURIComponent(scheduleId)}/run?${new URLSearchParams({ ledgerBookId: bookId })}`,
  rates: "/api/ledger/consolidation/fx/rate-sets",
  fxConfiguration: (bookId: string) => `/api/ledger/consolidation/fx/books/${encodeURIComponent(bookId)}/configuration`,
  bindings: (bookId: string, asOf: string) => `/api/ledger/intercompany/account-bindings?${new URLSearchParams({ ledgerBookId: bookId, asOf })}`
};
