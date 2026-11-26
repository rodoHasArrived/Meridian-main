import { apiGetJson, apiPutJson, type ApiRequestOptions } from "@/lib/api";
import { UI_API_ROUTES } from "@/lib/ui-api-routes.generated";

export interface PricingSourceEntry {
  priority: number;
  sourceId: string;
  sourceDisplayName: string;
  maxDaysStale: number;
}
export interface PricingSourceHierarchy {
  securityId: string;
  accountId: string | null;
  entries: PricingSourceEntry[];
  asOf: string;
  updatedBy: string;
}
/** The authenticated server supplies UpdatedBy. */
export type PricingHierarchyWrite = Omit<PricingSourceHierarchy, "updatedBy">;

function endpoint(securityId: string) {
  return UI_API_ROUTES.SecurityMasterPricingHierarchy.replace("{securityId:guid}", encodeURIComponent(securityId));
}

export async function getPricingHierarchy(securityId: string, accountId: string | null, options: ApiRequestOptions = {}): Promise<PricingSourceHierarchy> {
  const query = new URLSearchParams();
  if (accountId) query.set("accountId", accountId);
  const result = await apiGetJson<PricingSourceHierarchy>(`${endpoint(securityId)}${query.size ? `?${query}` : ""}`, { ...options, allowDevelopmentFallback: false });
  if (result.securityId.toLowerCase() !== securityId.toLowerCase() || (result.accountId || null) !== accountId) {
    throw new Error("The returned pricing hierarchy belongs to another security or account.");
  }
  return result;
}

export function savePricingHierarchy(request: PricingHierarchyWrite, options: ApiRequestOptions = {}): Promise<void> {
  return apiPutJson<void>(endpoint(request.securityId), request, { ...options, allowDevelopmentFallback: false });
}
