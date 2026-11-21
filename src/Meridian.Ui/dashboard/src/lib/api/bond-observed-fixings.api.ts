import { apiGetJson, apiPostJson, type ApiRequestOptions } from "@/lib/api";
import type { SecurityRateObservation } from "./security-rates.api";
import { UI_API_ROUTES } from "@/lib/ui-api-routes.generated";

export interface BondObservedFixingReceipt {
  receiptId: string;
  fixing: {
    securityId: string; securityVersion: number; indexName: string; resetDate: string; rateFraction: string;
    observation: SecurityRateObservation; recordedBy: string; justification: string;
  };
  recordedAt: string;
  contentSha256: string;
}

const endpoint = (securityId: string) => UI_API_ROUTES.SecurityMasterObservedBondFixings.replace("{securityId:guid}", encodeURIComponent(securityId));
export function getBondObservedFixings(securityId: string, options: ApiRequestOptions = {}) {
  return apiGetJson<BondObservedFixingReceipt[]>(endpoint(securityId), { ...options, allowDevelopmentFallback: false });
}
export function retainBondObservedFixing(securityId: string, request: {
  expectedSecurityVersion: number; resetDate: string; observation: SecurityRateObservation; justification: string;
}, options: ApiRequestOptions = {}) {
  return apiPostJson<BondObservedFixingReceipt>(endpoint(securityId), request, { ...options, allowDevelopmentFallback: false });
}
