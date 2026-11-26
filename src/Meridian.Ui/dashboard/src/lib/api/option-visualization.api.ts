import { apiGetJson, apiPostJson, type ApiRequestOptions } from "@/lib/api";
import { UI_API_ROUTES } from "@/lib/ui-api-routes.generated";
import type {
  OptionScreenCriteria, OptionScreenResult, OptionAssessmentImportRequest,
  OptionAssessmentImportPreview, RetainedOptionAssessment
} from "@/types/option-visualization.types";

const authoritative = (options: ApiRequestOptions) => ({ ...options, allowDevelopmentFallback: false });
const accountRoute = (route: string, account: string) => route.replace("{accountId:guid}", encodeURIComponent(account));
const accounts = (account: string) => accountRoute(UI_API_ROUTES.OptionAssessments, account);

export function screenOptions(criteria: OptionScreenCriteria, options: ApiRequestOptions = {}) {
  return apiPostJson<OptionScreenResult>(UI_API_ROUTES.OptionsScreen, criteria, authoritative(options));
}
export function listOptionAssessments(account: string, options: ApiRequestOptions = {}) {
  return apiGetJson<RetainedOptionAssessment[]>(accounts(account), authoritative(options));
}
export function previewOptionAssessment(account: string, request: OptionAssessmentImportRequest, options: ApiRequestOptions = {}) {
  return apiPostJson<OptionAssessmentImportPreview>(accountRoute(UI_API_ROUTES.OptionAssessmentPreview, account), request, authoritative(options));
}
export function acceptOptionAssessment(account: string, request: {
  idempotencyKey: string; previewFingerprint: string; import: OptionAssessmentImportRequest
}, options: ApiRequestOptions = {}) {
  return apiPostJson<RetainedOptionAssessment>(accountRoute(UI_API_ROUTES.OptionAssessmentAccept, account), request, authoritative(options));
}
export function optionAssessmentSourceUrl(account: string, assessment: string) {
  return accountRoute(UI_API_ROUTES.OptionAssessmentSource, account).replace("{assessmentId}", encodeURIComponent(assessment));
}
