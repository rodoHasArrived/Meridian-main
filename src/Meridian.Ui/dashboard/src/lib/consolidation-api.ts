import { apiGetJson, apiPostJson, type ApiRequestOptions } from "@/lib/api";
import type { ConsolidationRequest, ConsolidationView } from "@/types/consolidation";

export function previewConsolidation(request: ConsolidationRequest, options: ApiRequestOptions = {}) {
  const query = new URLSearchParams({ ...request });
  return apiGetJson<ConsolidationView>(`/api/ledger/consolidation/preview?${query}`, options);
}

export function createConsolidationDrafts(request: ConsolidationRequest, options: ApiRequestOptions = {}) {
  return apiPostJson<ConsolidationView>("/api/ledger/consolidation/drafts", request, options);
}
