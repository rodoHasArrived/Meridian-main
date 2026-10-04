/**
 * Client functions for the reconciliation queue readiness routes.
 *
 * Thin wrappers over the shared workstation API client so errors, aborts, and
 * no-host fixture semantics stay aligned with the rest of the dashboard.
 */

import { apiGetJson, type ApiRequestOptions } from "@/lib/api";
import {
  reconciliationBreakQueueTaxonomyEndpoint,
  reconciliationOpenCasesEndpoint,
  reconciliationQueueStatusEndpoint
} from "@/lib/workstation-endpoints";
import type {
  ReconciliationCaseSummary,
  ReconciliationQueueAccountStatus,
  ReconciliationTaxonomySnapshot
} from "@/types/reconciliation-readiness.types";

export function getReconciliationQueueStatus(
  options: ApiRequestOptions = {}
): Promise<ReconciliationQueueAccountStatus[]> {
  return apiGetJson<ReconciliationQueueAccountStatus[]>(reconciliationQueueStatusEndpoint(), options);
}

export function getReconciliationOpenCases(
  options: ApiRequestOptions = {}
): Promise<ReconciliationCaseSummary[]> {
  return apiGetJson<ReconciliationCaseSummary[]>(reconciliationOpenCasesEndpoint(), options);
}

export function getReconciliationTaxonomy(
  options: ApiRequestOptions = {}
): Promise<ReconciliationTaxonomySnapshot> {
  return apiGetJson<ReconciliationTaxonomySnapshot>(reconciliationBreakQueueTaxonomyEndpoint(), options);
}
