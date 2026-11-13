// Backfill, provider management, provider capability matrix, and security-master supply endpoints.
// Re-exported through @/lib/api.
import { providerConnectionScopeEndpoint, type ProviderCredentialRequestOptions } from "@/lib/provider-credential-scope";
import type { BackfillPreviewResult, BackfillExecutionHistoryResponse, BackfillProgressResponse, BackfillTriggerRequest, BackfillTriggerResult, AlpacaBrokerageConnectionRequest, BrokerageConnectionStatus, BrokerageHouseholdPortfolio, AssetOperationsDetail, MultiAssetCoverageSummary, NetSymbolPosition, ProviderConnectionRow, ProviderCredentialMutationResult, ProviderCredentialUpsertRequest, ProviderCredentialVerificationResult, ProviderReadinessSummary, ProviderRoutePreviewRequest, ProviderRoutePreviewResponse, ProviderRoutingBinding, ProviderRoutingConnection, ProviderRoutingTrustSnapshot, ManualCsvProviderIntegrationDryRunRequest, ProviderIntegrationActivationReadiness, ProviderIntegrationActivationRequest, ProviderIntegrationActivationResult, ProviderIntegrationConnectionMonitor, ProviderIntegrationDryRunResult, ProviderIntegrationManifest, ProviderIntegrationOpenApiImportRequest, ProviderIntegrationOpenApiImportResult, ProviderIntegrationPromotionReadinessPreview, ProviderIntegrationQuarantineReplayRequest, ProviderIntegrationQuarantineReplayResult, ProviderIntegrationQuarantineResolutionRequest, ProviderIntegrationQuarantineResolutionResult, ProviderIntegrationQuarantineReview, ProviderIntegrationReconciliationHandoffHistory, ProviderIntegrationReconciliationHandoffRequest, ProviderIntegrationReconciliationHandoffResult, ProviderIntegrationRestDryRunRequest, ProviderIntegrationRunDueSyncRequest, ProviderIntegrationRunDueSyncResult, ProviderIntegrationSchemaDriftCheckRequest, ProviderIntegrationSchemaDriftCheckResult, ProviderIntegrationSetupSaveRequest, ProviderIntegrationSetupSaveResult, ProviderIntegrationStagingIdentityResolutionPreview, ProviderIntegrationStagingReview, ProviderIntegrationSyncPlan, ProviderIntegrationSyncRunHistory, ProviderIntegrationTemplateCatalogEntry, PlaidLinkTokenRequest, PlaidLinkTokenResponse, PlaidPublicTokenExchangeRequest, PlaidPublicTokenExchangeResult, PlaidInstitutionSearchResult } from "@/types";
import { BACKFILL_API_ENDPOINTS, PORTFOLIO_API_ENDPOINTS, PLAID_API_ENDPOINTS, PROVIDER_API_ENDPOINTS, PROVIDER_INTEGRATION_API_ENDPOINTS, PROVIDER_ROUTING_API_ENDPOINTS, QUALITY_API_ENDPOINTS, SECURITY_MASTER_API_ENDPOINTS, WORKSTATION_API_ENDPOINTS, securityMasterCoverageDraftEndpoint, brokerageConnectionConnectEndpoint, brokerageConnectionEndpoint, brokerageConnectionStatusEndpoint, portfolioHouseholdEndpoint, portfolioSymbolExposureEndpoint, providerCredentialEndpoint, providerVerifyEndpoint, workstationProviderIntegrationConnectionMonitorEndpoint, workstationProviderIntegrationConnectionRunDueSyncEndpoint, workstationProviderIntegrationConnectionSyncPlanEndpoint, workstationProviderIntegrationConnectionSyncRunsEndpoint, workstationProviderIntegrationIdentityResolutionEndpoint, workstationProviderIntegrationPromotionReadinessEndpoint, workstationProviderIntegrationQuarantineReviewEndpoint, workstationProviderIntegrationReadinessEndpoint, workstationProviderIntegrationReconciliationHandoffHistoryEndpoint, workstationProviderIntegrationStagingReviewEndpoint, workstationProviderIntegrationTemplateEndpoint, providerRemoveEndpoint, providerTestEndpoint, qualityAnomalyAcknowledgeEndpoint, securityMasterCorporateActionSourceProposalAcceptEndpoint, workstationAssetOperationsEndpoint } from "@/lib/workstation-endpoints";
import { deleteJson, getJson, postJson, putJson, type ApiRequestOptions } from "@/lib/api/http-client";

// --- Backfill mutations ---

export function getBackfillProgress(options: ApiRequestOptions = {}) {
  return getJson<BackfillProgressResponse>(BACKFILL_API_ENDPOINTS.progress, options);
}

export function getBackfillExecutionHistory(limit = 100, options: ApiRequestOptions = {}) {
  const query = new URLSearchParams({ limit: String(Math.max(1, Math.min(limit, 1000))) });
  return getJson<BackfillExecutionHistoryResponse>(`${BACKFILL_API_ENDPOINTS.executions}?${query}`, options);
}

export function triggerBackfill(request: BackfillTriggerRequest) {
  return postJson<BackfillTriggerResult>(BACKFILL_API_ENDPOINTS.run, request);
}

export function previewBackfill(request: BackfillTriggerRequest) {
  return postJson<BackfillPreviewResult>(BACKFILL_API_ENDPOINTS.runPreview, request);
}

// --- Provider management ---

export function setupProvider(request: import("@/types").ProviderSetupRequest) {
  return postJson<import("@/types").ProviderSetupResult>(PROVIDER_API_ENDPOINTS.configure, request);
}

export function searchPlaidInstitutions(query: string, options: ApiRequestOptions = {}) {
  const params = new URLSearchParams({
    query,
    products: "transactions,auth,identity,investments",
    countryCodes: "US"
  });
  return getJson<PlaidInstitutionSearchResult>(`${PLAID_API_ENDPOINTS.institutionSearch}?${params.toString()}`, options);
}

export function createPlaidLinkToken(request: PlaidLinkTokenRequest, options: ApiRequestOptions = {}) {
  return postJson<PlaidLinkTokenResponse>(PLAID_API_ENDPOINTS.linkToken, request, options);
}

export function exchangePlaidPublicToken(request: PlaidPublicTokenExchangeRequest, options: ApiRequestOptions = {}) {
  return postJson<PlaidPublicTokenExchangeResult>(PLAID_API_ENDPOINTS.publicTokenExchange, request, options);
}

export function getProviderRoutingConnections(options: ApiRequestOptions = {}) {
  return getJson<ProviderRoutingConnection[]>(PROVIDER_ROUTING_API_ENDPOINTS.connections, options);
}

export function getProviderRoutingBindings(options: ApiRequestOptions = {}) {
  return getJson<ProviderRoutingBinding[]>(PROVIDER_ROUTING_API_ENDPOINTS.bindings, options);
}

export function getProviderRoutingTrustSnapshots(options: ApiRequestOptions = {}) {
  return getJson<ProviderRoutingTrustSnapshot[]>(PROVIDER_ROUTING_API_ENDPOINTS.trustSnapshots, options);
}

export function previewProviderRoute(request: ProviderRoutePreviewRequest, options: ApiRequestOptions = {}) {
  return postJson<ProviderRoutePreviewResponse>(PROVIDER_ROUTING_API_ENDPOINTS.preview, request, options);
}

export function getProviderIntegrationTemplates(options: ApiRequestOptions = {}) {
  return getJson<ProviderIntegrationTemplateCatalogEntry[]>(PROVIDER_INTEGRATION_API_ENDPOINTS.templates, options);
}

export function getProviderIntegrationTemplate(manifestId: string, options: ApiRequestOptions = {}) {
  return getJson<ProviderIntegrationManifest>(workstationProviderIntegrationTemplateEndpoint(manifestId), options);
}

export function importProviderIntegrationOpenApi(
  request: ProviderIntegrationOpenApiImportRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<ProviderIntegrationOpenApiImportResult>(
    PROVIDER_INTEGRATION_API_ENDPOINTS.openApiImport,
    request,
    options
  );
}

export function saveProviderIntegrationSetup(
  request: ProviderIntegrationSetupSaveRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<ProviderIntegrationSetupSaveResult>(PROVIDER_INTEGRATION_API_ENDPOINTS.setupSave, request, options);
}

export function getProviderIntegrationReadiness(
  manifestId: string,
  connectionId?: string | null,
  options: ApiRequestOptions = {}
) {
  return getJson<ProviderIntegrationActivationReadiness>(
    workstationProviderIntegrationReadinessEndpoint(manifestId, connectionId),
    options
  );
}

export function runManualCsvProviderIntegrationDryRun(
  request: ManualCsvProviderIntegrationDryRunRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<ProviderIntegrationDryRunResult>(
    PROVIDER_INTEGRATION_API_ENDPOINTS.manualCsvDryRun,
    request,
    options
  );
}

export function runRestProviderIntegrationDryRun(
  request: ProviderIntegrationRestDryRunRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<ProviderIntegrationDryRunResult>(PROVIDER_INTEGRATION_API_ENDPOINTS.restDryRun, request, options);
}

export function activateProviderIntegration(
  request: ProviderIntegrationActivationRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<ProviderIntegrationActivationResult>(PROVIDER_INTEGRATION_API_ENDPOINTS.activate, request, options);
}

export function getProviderIntegrationConnectionMonitor(
  connectionId: string,
  recentRunLimit?: number,
  options: ApiRequestOptions = {}
) {
  return getJson<ProviderIntegrationConnectionMonitor>(
    workstationProviderIntegrationConnectionMonitorEndpoint(connectionId, recentRunLimit),
    options
  );
}

export function getProviderIntegrationConnectionSyncRuns(
  connectionId: string,
  recentRunLimit?: number,
  options: ApiRequestOptions = {}
) {
  return getJson<ProviderIntegrationSyncRunHistory>(
    workstationProviderIntegrationConnectionSyncRunsEndpoint(connectionId, recentRunLimit),
    options
  );
}

export function getProviderIntegrationConnectionSyncPlan(
  connectionId: string,
  evaluatedAt?: string | null,
  options: ApiRequestOptions = {}
) {
  return getJson<ProviderIntegrationSyncPlan>(
    workstationProviderIntegrationConnectionSyncPlanEndpoint(connectionId, evaluatedAt),
    options
  );
}

export function runDueProviderIntegrationSync(
  connectionId: string,
  request: ProviderIntegrationRunDueSyncRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<ProviderIntegrationRunDueSyncResult>(
    workstationProviderIntegrationConnectionRunDueSyncEndpoint(connectionId),
    request,
    options
  );
}

export function checkProviderIntegrationSchemaDrift(
  request: ProviderIntegrationSchemaDriftCheckRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<ProviderIntegrationSchemaDriftCheckResult>(
    PROVIDER_INTEGRATION_API_ENDPOINTS.schemaDriftCheck,
    request,
    options
  );
}

export function getProviderIntegrationStagingReview(
  connectionId: string,
  recentRunLimit?: number,
  options: ApiRequestOptions = {}
) {
  return getJson<ProviderIntegrationStagingReview>(
    workstationProviderIntegrationStagingReviewEndpoint(connectionId, recentRunLimit),
    options
  );
}

export function getProviderIntegrationIdentityResolution(
  connectionId: string,
  recentRunLimit?: number,
  options: ApiRequestOptions = {}
) {
  return getJson<ProviderIntegrationStagingIdentityResolutionPreview>(
    workstationProviderIntegrationIdentityResolutionEndpoint(connectionId, recentRunLimit),
    options
  );
}

export function getProviderIntegrationPromotionReadiness(
  connectionId: string,
  recentRunLimit?: number,
  options: ApiRequestOptions = {}
) {
  return getJson<ProviderIntegrationPromotionReadinessPreview>(
    workstationProviderIntegrationPromotionReadinessEndpoint(connectionId, recentRunLimit),
    options
  );
}

export function getProviderIntegrationReconciliationHandoffHistory(
  connectionId: string,
  options: ApiRequestOptions = {}
) {
  return getJson<ProviderIntegrationReconciliationHandoffHistory>(
    workstationProviderIntegrationReconciliationHandoffHistoryEndpoint(connectionId),
    options
  );
}

export function createProviderIntegrationReconciliationHandoff(
  request: ProviderIntegrationReconciliationHandoffRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<ProviderIntegrationReconciliationHandoffResult>(
    PROVIDER_INTEGRATION_API_ENDPOINTS.reconciliationHandoff,
    request,
    options
  );
}

export function getProviderIntegrationQuarantineReview(
  connectionId: string,
  recentRunLimit?: number,
  options: ApiRequestOptions = {}
) {
  return getJson<ProviderIntegrationQuarantineReview>(
    workstationProviderIntegrationQuarantineReviewEndpoint(connectionId, recentRunLimit),
    options
  );
}

export function resolveProviderIntegrationQuarantineRecord(
  request: ProviderIntegrationQuarantineResolutionRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<ProviderIntegrationQuarantineResolutionResult>(
    PROVIDER_INTEGRATION_API_ENDPOINTS.quarantineResolve,
    request,
    options
  );
}

export function replayProviderIntegrationQuarantineRecords(
  request: ProviderIntegrationQuarantineReplayRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<ProviderIntegrationQuarantineReplayResult>(
    PROVIDER_INTEGRATION_API_ENDPOINTS.quarantineReplay,
    request,
    options
  );
}

export function removeProvider(providerId: string) {
  return postJson<{ success: boolean; message: string }>(providerRemoveEndpoint(providerId));
}

export function testProviderConnection(providerId: string) {
  return postJson<{ success: boolean; latency: string | null; message: string }>(providerTestEndpoint(providerId));
}

// --- Provider capability matrix and security-master supply surfaces ---

export function getProviderCapabilityMatrix() {
  return getJson<import("@/types").ProviderCapabilityMatrixResponse>(PROVIDER_API_ENDPOINTS.capabilityMatrix);
}

export function getProviderCatalog(options: ApiRequestOptions = {}) {
  return getJson<import("@/types").ProviderCatalogResponse>(PROVIDER_API_ENDPOINTS.catalog, options);
}

export function getProviderRateLimits(options: ApiRequestOptions = {}) {
  return getJson<import("@/types").ProviderRateLimitsResponse>(PROVIDER_API_ENDPOINTS.rateLimits, options);
}

export function getProviderConnectionHealth(options: ApiRequestOptions = {}) {
  return getJson<import("@/types").ProviderConnectionHealthResponse>(PROVIDER_API_ENDPOINTS.health, options);
}

export function getCorporateActionInbox() {
  return getJson<import("@/types").CorporateActionInboxResponse>(SECURITY_MASTER_API_ENDPOINTS.corporateActionInbox);
}

export function getSecurityMasterCoverageDraft(symbol: string) {
  return getJson<import("@/types").SecurityMasterDraftProposal>(securityMasterCoverageDraftEndpoint(symbol));
}

export function getSecurityMasterQualityReport() {
  return getJson<import("@/types").SecurityMasterQualityReport>(SECURITY_MASTER_API_ENDPOINTS.qualityReportLatest);
}

export function acceptCorporateActionInboxProposal(request: import("@/types").CorporateActionInboxAcceptRequest) {
  const endpoint = securityMasterCorporateActionSourceProposalAcceptEndpoint(request.proposalId);
  return postJson<import("@/types").CorporateActionInboxAcceptResult>(endpoint, request);
}

export function getQualityAnomalies() {
  return getJson<import("@/types").QualityAnomalyEntry[]>(QUALITY_API_ENDPOINTS.anomalies);
}

export function acknowledgeAnomaly(anomalyId: string) {
  return postJson<void>(qualityAnomalyAcknowledgeEndpoint(anomalyId));
}

export function getQualityCompleteness() {
  return getJson<Array<{ symbol: string; score: number; sampledAt: string }>>(QUALITY_API_ENDPOINTS.completeness);
}

export function getRobinhoodConnectionStatus(options: ApiRequestOptions = {}) {
  return getJson<BrokerageConnectionStatus>(brokerageConnectionStatusEndpoint("robinhood"), options);
}

export function startRobinhoodConnection(options: ApiRequestOptions = {}) {
  return postJson<BrokerageConnectionStatus>(brokerageConnectionConnectEndpoint("robinhood"), undefined, options);
}

export function revokeRobinhoodConnection(options: ApiRequestOptions = {}) {
  return deleteJson<BrokerageConnectionStatus>(brokerageConnectionEndpoint("robinhood"), options);
}

export function getPortfolioWorkspace(options: ApiRequestOptions = {}) {
  return getJson<import("@/types").PortfolioWorkspaceResponse>(WORKSTATION_API_ENDPOINTS.portfolio, options);
}

export function getPortfolioMultiAssetCoverage(options: ApiRequestOptions = {}) {
  return getJson<MultiAssetCoverageSummary>(WORKSTATION_API_ENDPOINTS.portfolioMultiAssetCoverage, options);
}

export function getAssetOperations(securityId: string, options: ApiRequestOptions = {}) {
  return getJson<AssetOperationsDetail>(workstationAssetOperationsEndpoint(securityId), options);
}

export function getAlpacaConnectionStatus(options: ApiRequestOptions = {}) {
  return getJson<BrokerageConnectionStatus>(brokerageConnectionStatusEndpoint("alpaca"), options);
}

export function getProviderConnections(options: ProviderCredentialRequestOptions = {}) {
  return getJson<ProviderConnectionRow[]>(providerConnectionScopeEndpoint(PROVIDER_API_ENDPOINTS.connections, options.connectionId), options);
}

export function getProviderReadiness(options: ApiRequestOptions = {}) {
  return getJson<ProviderReadinessSummary>(PROVIDER_API_ENDPOINTS.readiness, options);
}

export function putProviderCredentials(
  providerId: string,
  request: ProviderCredentialUpsertRequest,
  options: ProviderCredentialRequestOptions = {}
) {
  return putJson<ProviderCredentialMutationResult>(providerConnectionScopeEndpoint(providerCredentialEndpoint(providerId), options.connectionId), request, options);
}

export function verifyProviderConnection(providerId: string, options: ProviderCredentialRequestOptions = {}) {
  return postJson<ProviderCredentialVerificationResult>(providerConnectionScopeEndpoint(providerVerifyEndpoint(providerId), options.connectionId), undefined, options);
}

export function deleteProviderCredentials(providerId: string, options: ProviderCredentialRequestOptions = {}) {
  return deleteJson<ProviderCredentialMutationResult>(providerConnectionScopeEndpoint(providerCredentialEndpoint(providerId), options.connectionId), options);
}

export function connectAlpacaConnection(
  request: AlpacaBrokerageConnectionRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<BrokerageConnectionStatus>(brokerageConnectionConnectEndpoint("alpaca"), request, options);
}

export function revokeAlpacaConnection(options: ApiRequestOptions = {}) {
  return deleteJson<BrokerageConnectionStatus>(brokerageConnectionEndpoint("alpaca"), options);
}

export function getBrokerageHouseholdPortfolio(provider = "alpaca", options: ApiRequestOptions = {}) {
  return getJson<BrokerageHouseholdPortfolio>(portfolioHouseholdEndpoint(provider), options);
}

export function getPortfolioAggregate() {
  return getJson<unknown>(PORTFOLIO_API_ENDPOINTS.aggregate);
}

export function getPortfolioExposure() {
  return getJson<unknown>(PORTFOLIO_API_ENDPOINTS.exposure);
}

export function getPortfolioSymbolExposure(symbol: string) {
  return getJson<NetSymbolPosition>(portfolioSymbolExposureEndpoint(symbol));
}
