/**
 * Client functions for the user-editable cross-provider tolerance policy
 * (`/api/quality/cross-provider/*`).
 */

import { apiGetJson, apiPostJson, apiPutJson, type ApiRequestOptions } from "@/lib/api";
import { qualityCrossProviderPolicyEndpoint, qualityCrossProviderPreviewEndpoint } from "@/lib/workstation-endpoints";
import type {
  CrossProviderTolerancePolicy,
  CrossProviderTolerancePolicyResponse,
  CrossProviderTolerancePolicySaveResponse,
  CrossProviderTolerancePreviewRequest,
  CrossProviderTolerancePreviewResponse
} from "@/types/data-quality-watch.types";

/** Saved cross-provider tolerances with their bounds, defaults, audit and preview providers. */
export function getCrossProviderTolerancePolicy(options: ApiRequestOptions = {}): Promise<CrossProviderTolerancePolicyResponse> {
  return apiGetJson<CrossProviderTolerancePolicyResponse>(qualityCrossProviderPolicyEndpoint(), { ...options, allowDevelopmentFallback: false });
}

/** Saves tolerances; loosening needs a reason. The response reports whether the host applied them live. */
export function saveCrossProviderTolerancePolicy(
  policy: CrossProviderTolerancePolicy,
  reason: string,
  options: ApiRequestOptions = {}
): Promise<CrossProviderTolerancePolicySaveResponse> {
  return apiPutJson<CrossProviderTolerancePolicySaveResponse>(qualityCrossProviderPolicyEndpoint(), { policy, reason: reason || null }, options);
}

/** Counts the days a draft policy would have flagged for one symbol; costs a few provider calls. */
export function previewCrossProviderTolerancePolicy(
  request: CrossProviderTolerancePreviewRequest,
  options: ApiRequestOptions = {}
): Promise<CrossProviderTolerancePreviewResponse> {
  return apiPostJson<CrossProviderTolerancePreviewResponse>(qualityCrossProviderPreviewEndpoint(), request, options);
}
