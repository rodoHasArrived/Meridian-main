import { apiGetJson, type ApiRequestOptions } from "@/lib/api";
import { UI_API_ROUTES } from "@/lib/ui-api-routes.generated";
import type { BondAnalyticsQuery, BondAnalyticsResult } from "@/types/bond-analytics.types";

export function getBondAnalytics(securityId: string, query: BondAnalyticsQuery, options: ApiRequestOptions = {}): Promise<BondAnalyticsResult> {
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(query)) {
    if (value !== undefined && value !== "") params.set(key, value);
  }
  const path = UI_API_ROUTES.ReferenceDataBondAnalytics.replace("{securityId:guid}", encodeURIComponent(securityId));
  return apiGetJson<BondAnalyticsResult>(`${path}?${params}`, { ...options, allowDevelopmentFallback: false });
}
