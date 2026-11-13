import { apiGetJson, apiPostJson, type ApiRequestOptions } from "@/lib/api";
import { UI_API_ROUTES } from "@/lib/ui-api-routes.generated";
import type { DefineSecurityCurveRequest, PublishSecurityCurveRequest, SecurityCurveDefinition, SecurityCurveVersion } from "@/types/security-curves.types";

const versionsPath = (id: string) => UI_API_ROUTES.SecurityMasterCurveVersions.replace("{curveId:guid}", encodeURIComponent(id));
export function listSecurityCurves(options: ApiRequestOptions = {}): Promise<SecurityCurveDefinition[]> {
  return apiGetJson(UI_API_ROUTES.SecurityMasterCurves, { ...options, allowDevelopmentFallback: false });
}
export function listSecurityCurveVersions(curveId: string, options: ApiRequestOptions = {}): Promise<SecurityCurveVersion[]> {
  return apiGetJson(versionsPath(curveId), { ...options, allowDevelopmentFallback: false });
}
export function defineSecurityCurve(request: DefineSecurityCurveRequest, options: ApiRequestOptions = {}): Promise<SecurityCurveDefinition> {
  return apiPostJson(UI_API_ROUTES.SecurityMasterCurves, request, { ...options, allowDevelopmentFallback: false });
}
export function publishSecurityCurve(request: PublishSecurityCurveRequest, options: ApiRequestOptions = {}): Promise<SecurityCurveVersion> {
  return apiPostJson(versionsPath(request.curveId), request, { ...options, allowDevelopmentFallback: false });
}
