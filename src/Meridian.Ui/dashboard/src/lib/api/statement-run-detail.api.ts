/**
 * Client functions for the per-run statement reconciliation routes.
 *
 * Thin wrappers over the shared workstation API client so errors, aborts, and
 * no-host fixture semantics stay aligned with the rest of the dashboard.
 */

import { apiGetJson, apiPostJson, type ApiRequestOptions } from "@/lib/api";
import {
  reconciliationStatementRunBreaksEndpoint,
  reconciliationStatementRunReconcileEndpoint,
  reconciliationStatementRunValidationEndpoint
} from "@/lib/workstation-endpoints";
import type {
  StatementRunBreak,
  StatementRunReconcileAcknowledgement,
  StatementRunReconcileRequest,
  StatementRunValidation
} from "@/types/statement-run-detail.types";

export function getStatementRunValidation(
  runId: string,
  options: ApiRequestOptions = {}
): Promise<StatementRunValidation> {
  return apiGetJson<StatementRunValidation>(reconciliationStatementRunValidationEndpoint(runId), options);
}

export function getStatementRunBreaks(
  runId: string,
  options: ApiRequestOptions = {}
): Promise<StatementRunBreak[]> {
  return apiGetJson<StatementRunBreak[]>(reconciliationStatementRunBreaksEndpoint(runId), options);
}

export function reconcileStatementRun(
  runId: string,
  request: StatementRunReconcileRequest = {},
  options: ApiRequestOptions = {}
): Promise<StatementRunReconcileAcknowledgement> {
  return apiPostJson<StatementRunReconcileAcknowledgement>(
    reconciliationStatementRunReconcileEndpoint(runId),
    request,
    options
  );
}
