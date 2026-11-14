import { apiGetJson, type ApiRequestOptions } from "@/lib/api";
import { UI_API_ROUTES } from "@/lib/ui-api-routes.generated";
import type { BondAnalyticsQuery, BondAnalyticsResult, BondRetainedObservation } from "@/types/bond-analytics.types";

export function getBondAnalytics(
  securityId: string,
  query: BondAnalyticsQuery,
  options: ApiRequestOptions = {}
): Promise<BondAnalyticsResult> {
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(query)) {
    if (value !== undefined && value !== "") params.set(key, String(value));
  }
  const path = UI_API_ROUTES.ReferenceDataBondAnalytics.replace("{securityId:guid}", encodeURIComponent(securityId));
  return apiGetJson<BondAnalyticsResult>(`${path}?${params}`, {
    ...options,
    allowDevelopmentFallback: false
  });
}

export function getBondRetainedObservations(
  securityId: string,
  query: Pick<BondAnalyticsQuery, "asOf" | "knownAt" | "priceAccountId">,
  options: ApiRequestOptions = {}
): Promise<BondRetainedObservation[]> {
  const params = new URLSearchParams({ asOf: query.asOf });
  if (query.knownAt) params.set("knownAt", query.knownAt);
  if (query.priceAccountId) params.set("accountId", query.priceAccountId);
  const path = UI_API_ROUTES.SecurityMasterRetainedPriceObservations.replace("{securityId:guid}", encodeURIComponent(securityId));
  return apiGetJson<BondRetainedObservation[]>(`${path}?${params}`, {
    ...options,
    allowDevelopmentFallback: false
  });
}
