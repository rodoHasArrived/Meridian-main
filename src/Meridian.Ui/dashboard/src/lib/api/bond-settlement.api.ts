import { apiPostJson, type ApiRequestOptions } from "@/lib/api";
import { UI_API_ROUTES } from "@/lib/ui-api-routes.generated";
import type { BondSettlementRequest, BondSettlementResult } from "@/types/bond-settlement.types";

export function prepareBondSettlement(request: BondSettlementRequest, options: ApiRequestOptions = {}) {
  return apiPostJson<BondSettlementResult>(UI_API_ROUTES.ReferenceDataBondSettlementConsideration, request, {
    ...options, allowDevelopmentFallback: false
  });
}
