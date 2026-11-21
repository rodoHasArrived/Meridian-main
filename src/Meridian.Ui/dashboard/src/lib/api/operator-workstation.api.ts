/**
 * Client functions for the operator-scoped workstation seams added in Phase 3: the shared
 * cross-screen counts (A3), the operator's default scope (C1), surface visits (C3) and
 * presentation preferences.
 *
 * Thin wrappers over the shared workstation API client so errors, aborts and no-host fixture
 * semantics stay aligned with the rest of the dashboard.
 */

import { apiGetJson, apiPostJson, apiPutJson, type ApiRequestOptions } from "@/lib/api";
import { UI_API_ROUTES } from "@/lib/ui-api-routes.generated";
import { WORKSTATION_API_ENDPOINTS } from "@/lib/workstation-endpoints";
import type { WorkstationCrossScreenCounts } from "@/types/cross-screen-counts";
import type { OperatorPreferences, OperatorPreferenceValues } from "@/types/operator-preferences";
import type { OperatingScopeValues, OperatorScopePreference } from "@/types/operator-scope-preference";
import type { OperatorSurfaceVisit } from "@/types/operator-surface-visit";

export function workstationCrossScreenCountsEndpoint(fundAccountId?: string): string {
  const path = WORKSTATION_API_ENDPOINTS.crossScreenCounts;
  const id = fundAccountId?.trim();
  return id ? `${path}?${new URLSearchParams({ fundAccountId: id }).toString()}` : path;
}

/** The signed-in operator's visits to one workstation surface (C3). */
export function workstationOperatorSurfaceVisitEndpoint(surface: string): string {
  return UI_API_ROUTES.WorkstationOperatorSurfaceVisit.replace("{surface}", encodeURIComponent(surface));
}

/** Open orders, provider health and open breaks, counted once for every screen (A3). */
export function getCrossScreenCounts(fundAccountId?: string, options: ApiRequestOptions = {}) {
  return apiGetJson<WorkstationCrossScreenCounts>(workstationCrossScreenCountsEndpoint(fundAccountId), options);
}

/** The signed-in operator's default operating scope (C1). */
export function getOperatorScopePreference(options: ApiRequestOptions = {}) {
  return apiGetJson<OperatorScopePreference>(UI_API_ROUTES.WorkstationOperatorScopePreference, options);
}

/** Saves (or, with null, clears) the signed-in operator's default operating scope. */
export function saveOperatorScopePreference(defaultScope: OperatingScopeValues | null, options: ApiRequestOptions = {}) {
  return apiPutJson<OperatorScopePreference>(UI_API_ROUTES.WorkstationOperatorScopePreference, { defaultScope }, options);
}

/** The signed-in operator's presentation preferences (theme, density, time zone, descriptions, shortcuts). */
export function getOperatorPreferences(options: ApiRequestOptions = {}) {
  return apiGetJson<OperatorPreferences>(UI_API_ROUTES.WorkstationOperatorPreferences, options);
}

/** Replaces (or, with null, clears) the signed-in operator's presentation preferences. */
export function saveOperatorPreferences(preferences: OperatorPreferenceValues | null, options: ApiRequestOptions = {}) {
  return apiPutJson<OperatorPreferences>(UI_API_ROUTES.WorkstationOperatorPreferences, { preferences }, options);
}

/** The signed-in operator's last visit to a surface, without recording one (C3). */
export function getOperatorSurfaceVisit(surface: string, options: ApiRequestOptions = {}) {
  return apiGetJson<OperatorSurfaceVisit>(workstationOperatorSurfaceVisitEndpoint(surface), options);
}

/** Records the signed-in operator's visit to a surface and returns the visit it replaced (C3). */
export function recordOperatorSurfaceVisit(surface: string, options: ApiRequestOptions = {}) {
  return apiPostJson<OperatorSurfaceVisit>(workstationOperatorSurfaceVisitEndpoint(surface), undefined, options);
}
