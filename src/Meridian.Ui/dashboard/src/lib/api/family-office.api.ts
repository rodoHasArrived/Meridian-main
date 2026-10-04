/**
 * Client function for the family-office workstation read.
 *
 * Thin wrapper over the shared workstation API client so errors, aborts, and
 * no-host fixture semantics stay aligned with the rest of the dashboard. Only the
 * consolidated `overview` read is wrapped: the narrower balance-sheet, entities,
 * and ownership-graph endpoints have no caller yet, and an unused wrapper would
 * make the route-wiring report count them as wired.
 */

import { apiGetJson, type ApiRequestOptions } from "@/lib/api";
import { FAMILY_OFFICE_API_ENDPOINTS } from "@/lib/workstation-endpoints";
import type { FamilyOfficeOverview } from "@/types/family-office.types";

export function getFamilyOfficeOverview(options: ApiRequestOptions = {}): Promise<FamilyOfficeOverview> {
  return apiGetJson<FamilyOfficeOverview>(FAMILY_OFFICE_API_ENDPOINTS.overview, options);
}
