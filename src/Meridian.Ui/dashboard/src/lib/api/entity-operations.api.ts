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
  agreements: "/api/fund-structure/intercompany-agreements",
  lifecycle: (entityId: string) => `/api/fund-structure/entities/${encodeURIComponent(entityId)}/lifecycle`,
  controls: (bookId: string) => `/api/ledger/books/${encodeURIComponent(bookId)}/controls`,
  proposals: (bookId: string) => `/api/ledger/intercompany/proposals?sourceBookId=${encodeURIComponent(bookId)}`,
  breaks: (bookId: string) => `/api/ledger/intercompany/breaks?sourceBookId=${encodeURIComponent(bookId)}`,
  rates: "/api/ledger/consolidation/fx/rate-sets",
  fxConfiguration: (bookId: string) => `/api/ledger/consolidation/fx/books/${encodeURIComponent(bookId)}/configuration`,
  bindings: (bookId: string, asOf: string) => `/api/ledger/intercompany/account-bindings?${new URLSearchParams({ ledgerBookId: bookId, asOf })}`
};
