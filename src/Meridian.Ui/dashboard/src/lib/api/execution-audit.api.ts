/**
 * Client function for the cross-object audit trail search.
 *
 * Thin wrapper over the shared workstation API client so errors, aborts, and
 * no-host fixture semantics stay aligned with the rest of the dashboard.
 */

import { apiGetJson, type ApiRequestOptions } from "@/lib/api";
import { executionAuditSearchEndpoint } from "@/lib/workstation-endpoints";
import type { AuditTrailExplorerResult, AuditTrailSearchQuery } from "@/types/execution-audit.types";

export function searchExecutionAuditTrail(
  query: AuditTrailSearchQuery = {},
  options: ApiRequestOptions = {}
): Promise<AuditTrailExplorerResult> {
  return apiGetJson<AuditTrailExplorerResult>(
    executionAuditSearchEndpoint({ ...query } as Record<string, string | number | undefined>),
    options
  );
}
