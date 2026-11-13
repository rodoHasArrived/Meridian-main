// Security Master search, corporate action, trading parameter, and conflict endpoints.
// Re-exported through @/lib/api.
import type { CorporateAction, InstrumentPassport, StatementRunException, StatementRunSummary, ResolveConflictRequest, OperatorOverridesDto, OperatorOverridesPatchRequest, SecurityIdentityDrillIn, SecurityMasterConflict, SecurityMasterEntry, SecurityMasterTrustSnapshot, TradingParameters } from "@/types";
import { RECONCILIATION_API_ENDPOINTS, SECURITY_MASTER_API_ENDPOINTS, reconciliationRunEndpoint, reconciliationStatementExceptionsEndpoint, reconciliationStatementRunEndpoint, reconciliationStatementRunsEndpoint, securityMasterAliasUpsertEndpoint, securityMasterAmendEndpoint, securityMasterConflictsEndpoint, securityMasterConflictResolveEndpoint, securityMasterCorporateActionsEndpoint, securityMasterEntryEndpoint, securityMasterOperatorOverridesEndpoint, securityMasterTradingParametersEndpoint, workstationSecurityMasterEconomicDefinitionEndpoint, workstationSecurityMasterEntryEndpoint, workstationSecurityMasterHistoryEndpoint, workstationSecurityMasterIdentityEndpoint, workstationSecurityMasterInstrumentPassportEndpoint, workstationSecurityMasterSearchEndpoint, workstationSecurityMasterTrustSnapshotEndpoint } from "@/lib/workstation-endpoints";
import { getDevelopmentSearchFallback, getJson, patchJson, postJson, type ApiRequestOptions } from "@/lib/api/http-client";

// --- Security Master search ---

export async function searchSecurities(query: string, take = 25, activeOnly = true, options: ApiRequestOptions = {}) {
  const path = workstationSecurityMasterSearchEndpoint({ query, take, activeOnly });
  const results = await getJson<SecurityMasterEntry[]>(path, options);

  if (import.meta.env.DEV && options.allowDevelopmentFallback !== false && results.length === 0) {
    const fixtureResults = await getDevelopmentSearchFallback(query, take, activeOnly);
    if (fixtureResults && fixtureResults.length > 0) {
      return fixtureResults;
    }
  }

  return results;
}

export function getSecurityDetail(securityId: string, options: ApiRequestOptions = {}) {
  return getJson<SecurityMasterEntry>(workstationSecurityMasterEntryEndpoint(securityId), options);
}

export function getSecurityIdentity(securityId: string, options: ApiRequestOptions = {}) {
  return getJson<SecurityIdentityDrillIn>(workstationSecurityMasterIdentityEndpoint(securityId), options);
}

export function getSecurityHistory(securityId: string) {
  return getJson<unknown>(workstationSecurityMasterHistoryEndpoint(securityId));
}

export function getSecurityEconomicDefinition(securityId: string) {
  return getJson<unknown>(workstationSecurityMasterEconomicDefinitionEndpoint(securityId));
}

export function getSecurityTrustSnapshot(securityId: string) {
  return getJson<SecurityMasterTrustSnapshot>(workstationSecurityMasterTrustSnapshotEndpoint(securityId));
}

export function getSecurityInstrumentPassport(securityId: string) {
  return getJson<InstrumentPassport>(workstationSecurityMasterInstrumentPassportEndpoint(securityId));
}

export function createSecurityMasterEntry(request: Record<string, unknown>) {
  return postJson<SecurityMasterEntry>(securityMasterEntryEndpoint(), request);
}

export function amendSecurityMasterEntry(request: Record<string, unknown>) {
  return postJson<SecurityMasterEntry>(securityMasterAmendEndpoint(), request);
}

export function upsertSecurityAlias(request: Record<string, unknown>) {
  return postJson<Record<string, unknown>>(securityMasterAliasUpsertEndpoint(), request);
}

// --- Security Master corporate actions and trading parameters ---

export function getCorporateActions(securityId: string, options: ApiRequestOptions = {}) {
  return getJson<CorporateAction[]>(securityMasterCorporateActionsEndpoint(securityId), options);
}

export function getTradingParameters(securityId: string, options: ApiRequestOptions = {}) {
  return getJson<TradingParameters>(securityMasterTradingParametersEndpoint(securityId), options);
}

export function getOperatorOverrides(securityId: string) {
  return getJson<OperatorOverridesDto>(securityMasterOperatorOverridesEndpoint(securityId));
}

export function patchOperatorOverrides(securityId: string, request: OperatorOverridesPatchRequest) {
  return patchJson<OperatorOverridesDto>(securityMasterOperatorOverridesEndpoint(securityId), request);
}

// --- Security Master conflicts ---

export function getSecurityConflicts() {
  return getJson<SecurityMasterConflict[]>(securityMasterConflictsEndpoint());
}

export function resolveSecurityConflict(request: ResolveConflictRequest) {
  return postJson<SecurityMasterConflict>(
    securityMasterConflictResolveEndpoint(request.conflictId),
    request
  );
}

export function bulkResolveSecurityConflicts(request: Record<string, unknown>) {
  return postJson<unknown>(SECURITY_MASTER_API_ENDPOINTS.workstationConflictsBulkResolve, request);
}

export function runReconciliation(request: Record<string, unknown>) {
  return postJson<unknown>(RECONCILIATION_API_ENDPOINTS.runs, request);
}

export function getReconciliationRun(reconciliationRunId: string) {
  return getJson<unknown>(reconciliationRunEndpoint(reconciliationRunId));
}

export function getReconciliationStatementRuns(options: ApiRequestOptions = {}) {
  return getJson<StatementRunSummary[]>(reconciliationStatementRunsEndpoint(), options);
}

export function getReconciliationStatementRun(runId: string, options: ApiRequestOptions = {}) {
  return getJson<StatementRunSummary>(reconciliationStatementRunEndpoint(runId), options);
}

export function getReconciliationStatementExceptions(options: ApiRequestOptions = {}) {
  return getJson<StatementRunException[]>(reconciliationStatementExceptionsEndpoint(), options);
}

export const getStatementRuns = getReconciliationStatementRuns;

export const getStatementRun = getReconciliationStatementRun;

export const getStatementRunExceptions = getReconciliationStatementExceptions;
