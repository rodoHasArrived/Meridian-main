import { apiGetBlob, apiGetJson, apiPostJson, type ApiRequestOptions } from "@/lib/api";
import { UI_API_ROUTES } from "@/lib/ui-api-routes.generated";
import type {
  CaptureOnboardingComparisonRequest, CreateOnboardingWorkspaceRequest, OnboardingCriteria,
  OnboardingComparison, OnboardingReadinessPacket, OnboardingSourceSelection, OnboardingWorkspace
} from "@/types/accounting-onboarding";

export const onboardingWorkspacesEndpoint = UI_API_ROUTES.OnboardingWorkspaces;
const workspaceEndpoint = (id: string) => `${onboardingWorkspacesEndpoint}/${encodeURIComponent(id)}`;
const authoritative = (options: ApiRequestOptions) => ({ ...options, allowDevelopmentFallback: false });

export function listOnboardingWorkspaces(options: ApiRequestOptions = {}) {
  return apiGetJson<OnboardingWorkspace[]>(onboardingWorkspacesEndpoint, authoritative(options));
}
export function getOnboardingWorkspace(id: string, options: ApiRequestOptions = {}) {
  return apiGetJson<OnboardingWorkspace>(workspaceEndpoint(id), authoritative(options));
}
export function getOnboardingSources(id: string, options: ApiRequestOptions = {}) {
  return apiGetJson<OnboardingSourceSelection[]>(`${workspaceEndpoint(id)}/sources`, authoritative(options));
}
export function createOnboardingWorkspace(request: CreateOnboardingWorkspaceRequest, options: ApiRequestOptions = {}) {
  return apiPostJson<OnboardingWorkspace>(onboardingWorkspacesEndpoint, request, authoritative(options));
}
export function updateOnboardingCriteria(id: string, expectedVersion: number, criteria: OnboardingCriteria, options: ApiRequestOptions = {}) {
  return apiPostJson<OnboardingWorkspace>(`${workspaceEndpoint(id)}/criteria`, { expectedVersion, criteria }, authoritative(options));
}
export function captureOnboardingComparison(id: string, request: CaptureOnboardingComparisonRequest, options: ApiRequestOptions = {}) {
  return apiPostJson<OnboardingWorkspace>(`${workspaceEndpoint(id)}/comparisons`, request, authoritative(options));
}
export function assignOnboardingDifference(id: string, key: string, request: {
  expectedVersion: number; ownerId: string; notes: string; evidenceIds: string[];
}, options: ApiRequestOptions = {}) {
  return apiPostJson<OnboardingWorkspace>(`${workspaceEndpoint(id)}/differences/${encodeURIComponent(key)}/assignment`, request, authoritative(options));
}
export function reviewOnboardingWorkspace(id: string, request: {
  expectedVersion: number; dataRevision: number; decision: string; notes: string; evidenceIds: string[];
}, options: ApiRequestOptions = {}) {
  return apiPostJson<OnboardingWorkspace>(`${workspaceEndpoint(id)}/reviews`, request, authoritative(options));
}
export function freezeOnboardingPacket(id: string, expectedVersion: number, options: ApiRequestOptions = {}) {
  return apiPostJson<OnboardingReadinessPacket>(`${workspaceEndpoint(id)}/packets`, { expectedVersion }, authoritative(options));
}
export function getOnboardingPacket(id: string, packetId: string, options: ApiRequestOptions = {}) {
  return apiGetJson<OnboardingReadinessPacket>(`${workspaceEndpoint(id)}/packets/${encodeURIComponent(packetId)}`, authoritative(options));
}
export function downloadOnboardingPacket(id: string, packetId: string, options: ApiRequestOptions = {}) {
  return apiGetBlob(`${workspaceEndpoint(id)}/packets/${encodeURIComponent(packetId)}/download`, authoritative(options));
}
export function replayOnboardingComparison(id: string, comparisonId: string, options: ApiRequestOptions = {}) {
  return apiPostJson<OnboardingComparison>(`${workspaceEndpoint(id)}/comparisons/${encodeURIComponent(comparisonId)}/replay`, undefined, authoritative(options));
}
