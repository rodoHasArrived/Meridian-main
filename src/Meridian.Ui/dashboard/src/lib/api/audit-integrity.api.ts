/**
 * Client function for the read-only audit-chain integrity route.
 *
 * Thin wrapper over the shared workstation API client so errors, aborts, and
 * no-host fixture semantics stay aligned with the rest of the dashboard.
 */

import { apiGetJson, type ApiRequestOptions } from "@/lib/api";
import { WORKSTATION_API_ENDPOINTS } from "@/lib/workstation-endpoints";
import type { AccountingAuditIntegrityReportDto } from "@/types";

/** Verifies every retained accounting and ledger audit chain without appending. */
export function getAuditIntegrityReport(
  options: ApiRequestOptions = {}
): Promise<AccountingAuditIntegrityReportDto> {
  return apiGetJson<AccountingAuditIntegrityReportDto>(WORKSTATION_API_ENDPOINTS.ledgerAuditIntegrity, options);
}
