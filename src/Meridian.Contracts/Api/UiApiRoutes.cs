namespace Meridian.Contracts.Api;

/// <summary>
/// Shared UI API routes for the web dashboard and desktop clients.
/// </summary>
public static class UiApiRoutes
{
    // Health and status endpoints (shared with StatusHttpServer)
    public const string Health = "/health";
    public const string HealthDetailed = "/health/detailed";
    public const string Ready = "/ready";
    public const string Live = "/live";
    public const string Metrics = "/metrics";
    public const string Status = "/api/status";
    public const string Errors = "/api/errors";
    public const string Backpressure = "/api/backpressure";
    public const string ProvidersLatency = "/api/providers/latency";
    public const string Connections = "/api/connections";

    // Configuration endpoints
    public const string Config = "/api/config";
    public const string ConfigEffective = "/api/config/effective";
    public const string ConfigDataSource = "/api/config/datasource";
    public const string ConfigAlpaca = "/api/config/alpaca";
    public const string ConfigStorage = "/api/config/storage";
    public const string ConfigSymbols = "/api/config/symbols";
    public const string ConfigDataSources = "/api/config/datasources";
    public const string ConfigDataSourcesDefaults = "/api/config/datasources/defaults";
    public const string ConfigDataSourcesFailover = "/api/config/datasources/failover";
    public const string ConfigDerivatives = "/api/config/derivatives";

    // Backfill endpoints
    public const string BackfillProviders = "/api/backfill/providers";
    public const string BackfillStatus = "/api/backfill/status";
    public const string BackfillRun = "/api/backfill/run";
    public const string BackfillRunPreview = "/api/backfill/run/preview";
    public const string BackfillProgress = "/api/backfill/progress";
    public const string BackfillHealth = "/api/backfill/health";
    public const string BackfillResolve = "/api/backfill/resolve/{symbol}";
    public const string BackfillGapFill = "/api/backfill/gap-fill";
    public const string BackfillPresets = "/api/backfill/presets";
    public const string BackfillExecutions = "/api/backfill/executions";
    public const string BackfillStatistics = "/api/backfill/statistics";
    public const string BackfillSchedules = "/api/backfill/schedules";
    public const string BackfillSchedulesById = "/api/backfill/schedules/{id}";
    public const string BackfillSchedulesDelete = "/api/backfill/schedules/{id}/delete";
    public const string BackfillSchedulesEnable = "/api/backfill/schedules/{id}/enable";
    public const string BackfillSchedulesDisable = "/api/backfill/schedules/{id}/disable";
    public const string BackfillSchedulesRun = "/api/backfill/schedules/{id}/run";
    public const string BackfillSchedulesHistory = "/api/backfill/schedules/{id}/history";
    public const string BackfillSchedulesTemplates = "/api/backfill/schedules/templates";

    // Backfill checkpoint/resume endpoints (P0: expose checkpoint semantics to users)
    public const string BackfillCheckpoints = "/api/backfill/checkpoints";
    public const string BackfillCheckpointsResumable = "/api/backfill/checkpoints/resumable";
    public const string BackfillCheckpointsValidation = "/api/backfill/checkpoints/validation";
    public const string BackfillCheckpointById = "/api/backfill/checkpoints/{jobId}";
    public const string BackfillCheckpointResume = "/api/backfill/checkpoints/{jobId}/resume";
    public const string BackfillCheckpointPending = "/api/backfill/checkpoints/{jobId}/pending";

    // Ingestion job endpoints (P0: unified job contract)
    public const string IngestionJobs = "/api/ingestion/jobs";
    public const string IngestionJobById = "/api/ingestion/jobs/{jobId}";
    public const string IngestionJobTransition = "/api/ingestion/jobs/{jobId}/transition";

    // Backfill provider metadata and status endpoints
    public const string BackfillProviderMetadata = "/api/backfill/providers/metadata";
    public const string BackfillProviderStatuses = "/api/backfill/providers/statuses";
    public const string BackfillFallbackChain = "/api/backfill/providers/fallback-chain";
    public const string BackfillDryRunPlan = "/api/backfill/providers/dry-run-plan";
    public const string BackfillProviderConfigAudit = "/api/backfill/providers/audit";

    // Provider endpoints
    public const string ProviderComparison = "/api/providers/comparison";
    public const string ProviderStatus = "/api/providers/status";
    public const string ProviderMetrics = "/api/providers/metrics";
    public const string ProviderConfigure = "/api/providers/configure";
    public const string ProviderConnections = "/api/providers/connections";
    public const string ProviderCredentialMutation = "/api/providers/{providerId}/credentials";
    public const string ProviderCredentialVerify = "/api/providers/{providerId}/verify";
    public const string ProviderCatalog = "/api/providers/catalog";
    public const string ProviderCatalogById = "/api/providers/catalog/{providerId}";
    public const string ProviderById = "/api/providers/{providerName}";
    public const string ProviderFailover = "/api/providers/failover";
    public const string ProviderFailoverTrigger = "/api/providers/failover/trigger";
    public const string ProviderFailoverReset = "/api/providers/failover/reset";
    public const string ProviderRateLimits = "/api/providers/rate-limits";
    public const string ProviderRateLimitHistory = "/api/providers/{providerName}/rate-limit-history";
    public const string ProviderCapabilities = "/api/providers/capabilities";
    public const string ProviderSwitch = "/api/providers/switch";
    public const string ProviderTest = "/api/providers/{providerName}/test";
    public const string ProviderFailoverThresholds = "/api/providers/failover-thresholds";
    public const string ProviderHealth = "/api/providers/health";
    public const string ProviderReadiness = "/api/providers/readiness";
    public const string ProviderRoutingConnections = "/api/provider-routing/connections";
    public const string ProviderRoutingBindings = "/api/provider-routing/bindings";
    public const string ProviderRoutingTrustSnapshots = "/api/provider-routing/trust-snapshots";
    public const string ProviderRoutingPreview = "/api/provider-routing/preview";

    // Plaid governed provider endpoints
    public const string PlaidItems = "/api/plaid/items";
    public const string PlaidAccounts = "/api/plaid/accounts";
    public const string PlaidInstitutionSearch = "/api/plaid/institutions/search";
    public const string PlaidLinkToken = "/api/plaid/link-token";
    public const string PlaidPublicTokenExchange = "/api/plaid/public-token/exchange";
    public const string PlaidItemSync = "/api/plaid/items/{itemId}/sync";
    public const string PlaidWebhook = "/api/plaid/webhook";
    public const string PlaidSandboxTransfer = "/api/plaid/transfers/sandbox";

    // Accounting system / external GL provider endpoints
    public const string AccountingSystemProviders = "/api/accounting-system/providers";
    public const string AccountingSystemImportPreview = "/api/accounting-system/import/preview";
    public const string AccountingSystemImportLatest = "/api/accounting-system/import/latest";
    public const string AccountingSystemReconciliationLatest = "/api/accounting-system/reconciliation/latest";
    public const string AccountingSystemQuickBooksOAuthStart = "/api/accounting-system/quickbooks/oauth/start";
    public const string AccountingSystemQuickBooksOAuthCallback = "/api/accounting-system/quickbooks/oauth/callback";

    /// <summary>
    /// Unified traffic-light health dashboard across all providers.
    /// Returns green/yellow/red overall status with per-provider detail.
    /// </summary>
    public const string ProvidersDashboard = "/api/providers/dashboard";

    // Interactive Brokers specific endpoints
    public const string IBStatus = "/api/providers/ib/status";
    public const string IBErrorCodes = "/api/providers/ib/error-codes";
    public const string IBLimits = "/api/providers/ib/limits";

    // Failover endpoints
    public const string FailoverConfig = "/api/failover/config";
    public const string FailoverRules = "/api/failover/rules";
    public const string FailoverForce = "/api/failover/force/{ruleId}";
    public const string FailoverHealth = "/api/failover/health";

    // Symbol management endpoints
    public const string Symbols = "/api/symbols";
    public const string SymbolsMonitored = "/api/symbols/monitored";
    public const string SymbolsArchived = "/api/symbols/archived";
    public const string SymbolStatus = "/api/symbols/{symbol}/status";
    public const string SymbolsAdd = "/api/symbols/add";
    public const string SymbolRemove = "/api/symbols/{symbol}/remove";
    public const string SymbolTrades = "/api/symbols/{symbol}/trades";
    public const string SymbolDepth = "/api/symbols/{symbol}/depth";
    public const string SymbolsStatistics = "/api/symbols/statistics";
    public const string SymbolsValidate = "/api/symbols/validate";
    public const string SymbolArchive = "/api/symbols/{symbol}/archive";
    public const string SymbolsBulkAdd = "/api/symbols/bulk-add";
    public const string SymbolsBulkRemove = "/api/symbols/bulk-remove";
    public const string SymbolsSearch = "/api/symbols/search";
    public const string SymbolsBatch = "/api/symbols/batch";
    public const string SymbolMappings = "/api/symbols/mappings";

    // Catalog search and discovery endpoints
    public const string CatalogSearch = "/api/catalog/search";
    public const string CatalogTimeline = "/api/catalog/timeline";
    public const string CatalogSymbols = "/api/catalog/symbols";
    public const string CatalogCoverage = "/api/catalog/coverage";

    // Storage endpoints
    public const string StorageProfiles = "/api/storage/profiles";
    public const string StorageStats = "/api/storage/stats";
    public const string StorageBreakdown = "/api/storage/breakdown";
    public const string StorageSymbolInfo = "/api/storage/symbol/{symbol}/info";
    public const string StorageSymbolStats = "/api/storage/symbol/{symbol}/stats";
    public const string StorageSymbolFiles = "/api/storage/symbol/{symbol}/files";
    public const string StorageSymbolPath = "/api/storage/symbol/{symbol}/path";
    public const string StorageHealth = "/api/storage/health";
    public const string StorageCleanupCandidates = "/api/storage/cleanup/candidates";
    public const string StorageCleanup = "/api/storage/cleanup";
    public const string StorageArchiveStats = "/api/storage/archive/stats";
    public const string StorageCatalog = "/api/storage/catalog";
    public const string StorageSearchFiles = "/api/storage/search/files";
    public const string StorageHealthCheck = "/api/storage/health/check";
    public const string StorageHealthOrphans = "/api/storage/health/orphans";
    public const string StorageTiersMigrate = "/api/storage/tiers/migrate";
    public const string StorageTiersStatistics = "/api/storage/tiers/statistics";
    public const string StorageTiersPlan = "/api/storage/tiers/plan";
    public const string StorageMaintenanceDefrag = "/api/storage/maintenance/defrag";
    public const string StorageConvertParquet = "/api/storage/convert-parquet";
    public const string StorageCapacityForecast = "/api/storage/capacity-forecast";

    // Historical data query endpoints
    public const string HistoricalData = "/api/historical";

    // Quality/drops endpoints
    public const string QualityDrops = "/api/quality/drops";
    public const string QualityDropsBySymbol = "/api/quality/drops/{symbol}";

    // Quality monitoring endpoints (DataQualityMonitoringService)
    public const string QualityDashboard = "/api/quality/dashboard";
    public const string QualityMetrics = "/api/quality/metrics";
    public const string QualityCompleteness = "/api/quality/completeness";
    public const string QualityCompletenessBySymbol = "/api/quality/completeness/{symbol}";
    public const string QualityCompletenessSummary = "/api/quality/completeness/summary";
    public const string QualityCompletenessLow = "/api/quality/completeness/low";
    public const string QualityGaps = "/api/quality/gaps";
    public const string QualityGapsBySymbol = "/api/quality/gaps/{symbol}";
    public const string QualityGapsTimeline = "/api/quality/gaps/timeline/{symbol}";
    public const string QualityGapsStatistics = "/api/quality/gaps/statistics";
    public const string QualityErrors = "/api/quality/errors";
    public const string QualityErrorsBySymbol = "/api/quality/errors/{symbol}";
    public const string QualityErrorsStatistics = "/api/quality/errors/statistics";
    public const string QualityErrorsTopSymbols = "/api/quality/errors/top-symbols";
    public const string QualityAnomalies = "/api/quality/anomalies";
    public const string QualityAnomaliesBySymbol = "/api/quality/anomalies/{symbol}";
    public const string QualityAnomaliesUnacknowledged = "/api/quality/anomalies/unacknowledged";
    public const string QualityAnomaliesAcknowledge = "/api/quality/anomalies/{anomalyId}/acknowledge";
    public const string QualityAnomaliesStatistics = "/api/quality/anomalies/statistics";
    public const string QualityAnomaliesStale = "/api/quality/anomalies/stale";
    public const string QualityLatency = "/api/quality/latency";
    public const string QualityLatencyBySymbol = "/api/quality/latency/{symbol}";
    public const string QualityLatencyHistogram = "/api/quality/latency/{symbol}/histogram";
    public const string QualityLatencyStatistics = "/api/quality/latency/statistics";
    public const string QualityLatencyHigh = "/api/quality/latency/high";
    public const string QualityComparison = "/api/quality/comparison/{symbol}";
    public const string QualityComparisonDiscrepancies = "/api/quality/comparison/discrepancies";
    public const string QualityComparisonStatistics = "/api/quality/comparison/statistics";
    public const string QualityReportsDaily = "/api/quality/reports/daily";
    public const string QualityReportsWeekly = "/api/quality/reports/weekly";
    public const string QualityReportsExport = "/api/quality/reports/export";
    public const string QualityHealth = "/api/quality/health";
    public const string QualityHealthBySymbol = "/api/quality/health/{symbol}";
    public const string QualityHealthUnhealthy = "/api/quality/health/unhealthy";

    // SLA monitoring endpoints
    public const string SlaStatus = "/api/sla/status";
    public const string SlaStatusBySymbol = "/api/sla/status/{symbol}";
    public const string SlaViolations = "/api/sla/violations";
    public const string SlaHealth = "/api/sla/health";
    public const string SlaMetrics = "/api/sla/metrics";

    // Storage quality endpoints
    public const string StorageQualitySummary = "/api/storage/quality/summary";
    public const string StorageQualityScores = "/api/storage/quality/scores";
    public const string StorageQualitySymbol = "/api/storage/quality/symbol/{symbol}";
    public const string StorageQualityAlerts = "/api/storage/quality/alerts";
    public const string StorageQualityAlertAcknowledge = "/api/storage/quality/alerts/{alertId}/acknowledge";
    public const string StorageQualityRankings = "/api/storage/quality/rankings/{symbol}";
    public const string StorageQualityTrends = "/api/storage/quality/trends";
    public const string StorageQualityAnomalies = "/api/storage/quality/anomalies";
    public const string StorageQualityCheck = "/api/storage/quality/check";

    // Diagnostics endpoints
    public const string DiagnosticsDryRun = "/api/diagnostics/dry-run";
    public const string DiagnosticsProviders = "/api/diagnostics/providers";
    public const string DiagnosticsStorage = "/api/diagnostics/storage";
    public const string DiagnosticsConfig = "/api/diagnostics/config";
    public const string DiagnosticsBundle = "/api/diagnostics/bundle";
    public const string DiagnosticsMetrics = "/api/diagnostics/metrics";
    public const string DiagnosticsValidate = "/api/diagnostics/validate";
    public const string DiagnosticsProviderTest = "/api/diagnostics/providers/{providerName}/test";
    public const string DiagnosticsQuickCheck = "/api/diagnostics/quick-check";
    public const string DiagnosticsShowConfig = "/api/diagnostics/show-config";
    public const string DiagnosticsErrorCodes = "/api/diagnostics/error-codes";
    public const string DiagnosticsSelftest = "/api/diagnostics/selftest";
    public const string DiagnosticsValidateCredentials = "/api/diagnostics/validate-credentials";
    public const string DiagnosticsTestConnectivity = "/api/diagnostics/test-connectivity";
    public const string DiagnosticsValidateConfig = "/api/diagnostics/validate-config";
    public const string DiagnosticsCoordination = "/api/diagnostics/coordination";

    // Admin/Maintenance endpoints
    public const string AdminMaintenanceSchedule = "/api/admin/maintenance/schedule";
    public const string AdminMaintenanceRun = "/api/admin/maintenance/run";
    public const string AdminMaintenanceRunById = "/api/admin/maintenance/run/{runId}";
    public const string AdminMaintenanceHistory = "/api/admin/maintenance/history";
    public const string AdminStorageTiers = "/api/admin/storage/tiers";
    public const string AdminStorageMigrate = "/api/admin/storage/migrate/{targetTier}";
    public const string AdminStorageUsage = "/api/admin/storage/usage";
    public const string AdminRetention = "/api/admin/retention";
    public const string AdminRetentionDelete = "/api/admin/retention/{policyId}/delete";
    public const string AdminRetentionApply = "/api/admin/retention/apply";
    public const string AdminCleanupPreview = "/api/admin/cleanup/preview";
    public const string AdminCleanupExecute = "/api/admin/cleanup/execute";
    public const string AdminStoragePermissions = "/api/admin/storage/permissions";
    public const string AdminSelftest = "/api/admin/selftest";
    public const string AdminErrorCodes = "/api/admin/error-codes";
    public const string AdminShowConfig = "/api/admin/show-config";
    public const string AdminQuickCheck = "/api/admin/quick-check";

    // Maintenance schedules
    public const string MaintenanceSchedules = "/api/maintenance/schedules";
    public const string MaintenanceSchedulesById = "/api/maintenance/schedules/{id}";
    public const string MaintenanceSchedulesDelete = "/api/maintenance/schedules/{id}/delete";
    public const string MaintenanceSchedulesEnable = "/api/maintenance/schedules/{id}/enable";
    public const string MaintenanceSchedulesDisable = "/api/maintenance/schedules/{id}/disable";
    public const string MaintenanceSchedulesRun = "/api/maintenance/schedules/{id}/run";
    public const string MaintenanceSchedulesHistory = "/api/maintenance/schedules/{id}/history";

    // Cron schedule validation
    public const string SchedulesCronValidate = "/api/schedules/cron/validate";
    public const string SchedulesCronNextRuns = "/api/schedules/cron/next-runs";

    // Analytics endpoints
    public const string AnalyticsGaps = "/api/analytics/gaps";
    public const string AnalyticsGapsRepair = "/api/analytics/gaps/repair";
    public const string AnalyticsCompare = "/api/analytics/compare";
    public const string AnalyticsLatency = "/api/analytics/latency";
    public const string AnalyticsLatencyStats = "/api/analytics/latency/stats";
    public const string AnalyticsAnomalies = "/api/analytics/anomalies";
    public const string AnalyticsQualityReport = "/api/analytics/quality-report";
    public const string AnalyticsCompleteness = "/api/analytics/completeness";
    public const string AnalyticsThroughput = "/api/analytics/throughput";
    public const string AnalyticsRateLimits = "/api/analytics/rate-limits";

    // System health endpoints
    public const string HealthSummary = "/api/health/summary";
    public const string HealthProviders = "/api/health/providers";
    public const string HealthProviderDiagnostics = "/api/health/providers/{provider}/diagnostics";
    public const string HealthStorage = "/api/health/storage";
    public const string HealthEvents = "/api/health/events";
    public const string HealthMetrics = "/api/health/metrics";
    public const string HealthProviderTest = "/api/health/providers/{provider}/test";
    public const string HealthDiagnosticsBundle = "/api/health/diagnostics/bundle";

    // Trading calendar endpoints
    public const string CalendarStatus = "/api/calendar/status";
    public const string CalendarHolidays = "/api/calendar/holidays";
    public const string CalendarTradingDays = "/api/calendar/trading-days";


    // Security Master endpoints
    public const string SecurityMasterById = "/api/security-master/{securityId:guid}";
    public const string SecurityMasterValidation = "/api/security-master/{securityId:guid}/validation";
    public const string SecurityMasterResolve = "/api/security-master/resolve";
    public const string SecurityMasterSearch = "/api/security-master/search";
    public const string SecurityMasterAssetProfiles = "/api/security-master/asset-profiles";
    public const string SecurityMasterAssetProfilePromotionCandidates = "/api/security-master/asset-profiles/promotion-candidates";
    public const string SecurityMasterAssetProfileLineage = "/api/security-master/asset-profiles/{profileId}/lineage";
    public const string SecurityMasterAssetProfileDrafts = "/api/security-master/asset-profiles/drafts";
    public const string SecurityMasterAssetProfileApprove = "/api/security-master/asset-profiles/approve";
    public const string SecurityMasterAssetProfileRollback = "/api/security-master/asset-profiles/rollback";
    public const string SecurityMasterHistory = "/api/security-master/{securityId:guid}/history";
    public const string SecurityMasterCreate = "/api/security-master";
    public const string SecurityMasterAmend = "/api/security-master/amend";
    public const string SecurityMasterDeactivate = "/api/security-master/deactivate";
    public const string SecurityMasterAliasesUpsert = "/api/security-master/aliases/upsert";
    public const string SecurityMasterTradingParameters = "/api/security-master/{securityId:guid}/trading-parameters";
    public const string SecurityMasterPreferredEquityTerms = "/api/security-master/{securityId:guid}/preferred-equity-terms";
    public const string SecurityMasterConvertibleEquityTerms = "/api/security-master/{securityId:guid}/convertible-equity-terms";
    public const string SecurityMasterCorporateActions = "/api/security-master/{securityId:guid}/corporate-actions";
    public const string SecurityMasterOperatorOverrides = "/api/security-master/{securityId:guid}/operator-overrides";
    public const string SecurityMasterConflicts = "/api/security-master/conflicts";
    public const string SecurityMasterConflictResolve = "/api/security-master/conflicts/{conflictId:guid}/resolve";
    public const string SecurityMasterImport = "/api/security-master/import";
    public const string SecurityMasterIngestStatus = "/api/security-master/ingest/status";
    public const string SecurityMasterEdgarIngest = "/api/security-master/ingest/edgar";
    public const string ReferenceDataEdgarFiler = "/api/reference-data/edgar/filers/{cik}";
    public const string ReferenceDataEdgarFacts = "/api/reference-data/edgar/facts/{cik}";
    public const string ReferenceDataEdgarSecurityData = "/api/reference-data/edgar/security-data/{cik}";
    public const string ReferenceDataBondReference = "/api/reference-data/bonds/{securityId:guid}";
    public const string ReferenceDataBondLifecycle = "/api/reference-data/bonds/{securityId:guid}/lifecycle";
    public const string ReferenceDataBondAccrualConvention = "/api/reference-data/bonds/{securityId:guid}/accrual-convention";
    public const string ReferenceDataBondIssuerLadder = "/api/reference-data/bonds/issuer-ladder";
    public const string ReferenceDataBondMaturityLadder = "/api/reference-data/bonds/maturity-ladder";
    public const string WorkstationAssetOperations = "/api/workstation/assets/{securityId:guid}/operations";
    public const string ReferenceDataOptionContract = "/api/reference-data/options/contracts/{contractSymbol}";
    public const string ReferenceDataOptionSeries = "/api/reference-data/options/series/{optionChainId}";
    public const string ReferenceDataOptionUnderlyingLinkage = "/api/reference-data/options/contracts/{contractSymbol}/underlying-linkage";
    public const string ReferenceDataOptionExpiryLadder = "/api/reference-data/options/expiry-ladder";
    public const string ReferenceDataOptionChainImport = "/api/reference-data/options/chains/import";
    public const string ReferenceDataOptionChainSnapshot = "/api/reference-data/options/chains/snapshot";

    public const string ReferenceDataEquityReference = "/api/reference-data/equities/{securityId:guid}";
    public const string ReferenceDataEquityByExchange = "/api/reference-data/equities/by-exchange";
    public const string ReferenceDataEquityByIssuer = "/api/reference-data/equities/by-issuer";

    public const string ReferenceDataFutureReference = "/api/reference-data/futures/{securityId:guid}";
    public const string ReferenceDataFuturesByRoot = "/api/reference-data/futures/by-root";
    public const string ReferenceDataFutureExpiryLadder = "/api/reference-data/futures/expiry-ladder";
    public const string ReferenceDataFutureFrontMonth = "/api/reference-data/futures/front-month";

    public const string ReferenceDataFxSpotReference = "/api/reference-data/fxspot/{securityId:guid}";
    public const string ReferenceDataFxSpotByPairCode = "/api/reference-data/fxspot/pairs/{pairCode}";
    public const string ReferenceDataFxSpotByCurrency = "/api/reference-data/fxspot/by-currency";

    public const string ReferenceDataSwapReference = "/api/reference-data/swaps/{securityId:guid}";
    public const string ReferenceDataSwapsByType = "/api/reference-data/swaps/by-type";
    public const string ReferenceDataSwapsMaturingBefore = "/api/reference-data/swaps/maturing-before";

    public const string ReferenceDataCommodityReference = "/api/reference-data/commodities/{securityId:guid}";
    public const string ReferenceDataCommoditiesByType = "/api/reference-data/commodities/by-type";
    public const string ReferenceDataCommoditiesByExchange = "/api/reference-data/commodities/by-exchange";

    public const string ReferenceDataCryptoReference = "/api/reference-data/crypto/{securityId:guid}";
    public const string ReferenceDataCryptoByNetwork = "/api/reference-data/crypto/by-network";
    public const string ReferenceDataCryptoByBaseCurrency = "/api/reference-data/crypto/by-base-currency";

    public const string ReferenceDataDepositReference = "/api/reference-data/deposits/{securityId:guid}";
    public const string ReferenceDataDepositsByInstitution = "/api/reference-data/deposits/by-institution";
    public const string ReferenceDataDepositsMaturingBefore = "/api/reference-data/deposits/maturing-before";

    public const string ReferenceDataMoneyMarketFundReference = "/api/reference-data/money-market-funds/{securityId:guid}";
    public const string ReferenceDataMoneyMarketFundsByFamily = "/api/reference-data/money-market-funds/by-family";
    public const string ReferenceDataMoneyMarketFundsBySweepEligibility = "/api/reference-data/money-market-funds/by-sweep-eligibility";

    public const string ReferenceDataCertificateOfDepositReference = "/api/reference-data/certificates-of-deposit/{securityId:guid}";
    public const string ReferenceDataCertificatesOfDepositByIssuer = "/api/reference-data/certificates-of-deposit/by-issuer";
    public const string ReferenceDataCertificatesOfDepositMaturingBefore = "/api/reference-data/certificates-of-deposit/maturing-before";

    // Messaging endpoints
    public const string MessagingConfig = "/api/messaging/config";
    public const string MessagingStatus = "/api/messaging/status";
    public const string MessagingStats = "/api/messaging/stats";
    public const string MessagingActivity = "/api/messaging/activity";
    public const string MessagingConsumers = "/api/messaging/consumers";
    public const string MessagingEndpoints = "/api/messaging/endpoints";
    public const string MessagingTest = "/api/messaging/test";
    public const string MessagingPublishing = "/api/messaging/publishing";
    public const string MessagingQueuePurge = "/api/messaging/queues/{queueName}/purge";
    public const string MessagingErrors = "/api/messaging/errors";
    public const string MessagingErrorRetry = "/api/messaging/errors/{messageId}/retry";

    // Time series alignment endpoints
    public const string AlignmentCreate = "/api/alignment/create";
    public const string AlignmentPreview = "/api/alignment/preview";

    // Sampling endpoints
    public const string SamplingCreate = "/api/sampling/create";
    public const string SamplingEstimate = "/api/sampling/estimate";
    public const string SamplingSaved = "/api/sampling/saved";
    public const string SamplingById = "/api/sampling/{sampleId}";

    // Live data endpoints
    public const string DataTrades = "/api/data/trades/{symbol}";
    public const string DataQuotes = "/api/data/quotes/{symbol}";
    public const string DataQuotesSnapshot = "/api/data/quotes-snapshot";
    public const string DataOrderbook = "/api/data/orderbook/{symbol}";
    public const string DataL3Orderbook = "/api/data/l3-orderbook/{symbol}";
    public const string DataBbo = "/api/data/bbo/{symbol}";
    public const string DataOrderflow = "/api/data/orderflow/{symbol}";
    public const string DataHealth = "/api/data/health";

    // Subscription endpoints
    public const string SubscriptionsActive = "/api/subscriptions/active";
    public const string SubscriptionsSubscribe = "/api/subscriptions/subscribe";
    public const string SubscriptionsUnsubscribe = "/api/subscriptions/unsubscribe/{symbol}";

    // Replay endpoints
    public const string ReplayFiles = "/api/replay/files";
    public const string ReplayStart = "/api/replay/start";
    public const string ReplayPause = "/api/replay/{sessionId}/pause";
    public const string ReplayResume = "/api/replay/{sessionId}/resume";
    public const string ReplayStop = "/api/replay/{sessionId}/stop";
    public const string ReplaySeek = "/api/replay/{sessionId}/seek";
    public const string ReplaySpeed = "/api/replay/{sessionId}/speed";
    public const string ReplayStatus = "/api/replay/{sessionId}/status";
    public const string ReplayPreview = "/api/replay/preview";
    public const string ReplayStats = "/api/replay/stats";

    // Export endpoints
    public const string ExportAnalysis = "/api/export/analysis";
    public const string ExportPreview = "/api/export/preview";
    public const string ExportFormats = "/api/export/formats";
    public const string ExportQualityReport = "/api/export/quality-report";
    public const string ExportOrderflow = "/api/export/orderflow";
    public const string ExportIntegrity = "/api/export/integrity";
    public const string ExportStrategyPackage = "/api/export/strategy-package";
    public const string ExportResearchPackage = "/api/export/research-package";

    // Lean integration endpoints
    public const string LeanStatus = "/api/lean/status";
    public const string LeanConfig = "/api/lean/config";
    public const string LeanVerify = "/api/lean/verify";
    public const string LeanAlgorithms = "/api/lean/algorithms";
    public const string LeanSync = "/api/lean/sync";
    public const string LeanSyncStatus = "/api/lean/sync/status";
    public const string LeanBacktestStart = "/api/lean/backtest/start";
    public const string LeanBacktestStatus = "/api/lean/backtest/{backtestId}/status";
    public const string LeanBacktestResults = "/api/lean/backtest/{backtestId}/results";
    public const string LeanBacktestStop = "/api/lean/backtest/{backtestId}/stop";
    public const string LeanBacktestHistory = "/api/lean/backtest/history";
    public const string LeanBacktestDelete = "/api/lean/backtest/{backtestId}/delete";
    public const string LeanAutoExportStatus = "/api/lean/auto-export";
    public const string LeanAutoExportConfigure = "/api/lean/auto-export/configure";
    public const string LeanResultsIngest = "/api/lean/results/ingest";
    public const string LeanSymbolMap = "/api/lean/symbol-map";

    // Options / Derivatives endpoints
    public const string OptionsChains = "/api/options/chains/{underlyingSymbol}";
    public const string OptionsExpirations = "/api/options/expirations/{underlyingSymbol}";
    public const string OptionsStrikes = "/api/options/strikes/{underlyingSymbol}/{expiration}";
    public const string OptionsQuote = "/api/options/quote";
    public const string OptionsQuotesByUnderlying = "/api/options/quotes/{underlyingSymbol}";
    public const string OptionsTrades = "/api/options/trades";
    public const string OptionsGreeks = "/api/options/greeks";
    public const string OptionsOpenInterest = "/api/options/open-interest";
    public const string OptionsSummary = "/api/options/summary";
    public const string OptionsTrackedUnderlyings = "/api/options/underlyings";
    public const string OptionsRefresh = "/api/options/refresh";

    // Index endpoints
    public const string IndicesConstituents = "/api/indices/{indexName}/constituents";

    // Canonicalization parity endpoints (Phase 2)
    public const string CanonicalizationStatus = "/api/canonicalization/status";
    public const string CanonicalizationParity = "/api/canonicalization/parity";
    public const string CanonicalizationParityByProvider = "/api/canonicalization/parity/{provider}";
    public const string CanonicalizationConfig = "/api/canonicalization/config";

    // Authentication endpoints
    public const string AuthLoginPage = "/login";
    public const string AuthApiLogin = "/api/auth/login";
    public const string AuthApiLogout = "/api/auth/logout";
    public const string AuthApiMe = "/api/auth/me";
    public const string AuthApiRoles = "/api/auth/roles";
    public const string AuthApiRoleProfiles = "/api/auth/role-profiles";
    public const string AuthApiAccounts = "/api/auth/accounts";
    public const string AuthApiAccountByUsername = "/api/auth/accounts/{username}";
    public const string AuthApiAccountPasswordReset = "/api/auth/accounts/{username}/password-reset";
    public const string AuthApiAccountDisable = "/api/auth/accounts/{username}/disable";
    public const string AuthApiSessionsRevoke = "/api/auth/sessions/revoke";
    public const string AuthApiAudit = "/api/auth/audit";
    public const string AuthApiAccessAssignments = "/api/auth/access-assignments";
    public const string AuthApiAccessAssignmentRevoke = "/api/auth/access-assignments/{assignmentId}/revoke";

    // Execution / Paper Trading Cockpit endpoints
    public const string ExecutionAccount = "/api/execution/account";
    public const string ExecutionPositions = "/api/execution/positions";
    public const string ExecutionBlotterPositions = "/api/execution/positions/blotter";
    public const string ExecutionOrders = "/api/execution/orders";
    public const string ExecutionOrderById = "/api/execution/orders/{orderId}";
    public const string ExecutionOrderSubmit = "/api/execution/orders/submit";
    public const string ExecutionOrderCancel = "/api/execution/orders/{orderId}/cancel";
    public const string ExecutionPositionActionClose = "/api/execution/positions/actions/close";
    public const string ExecutionPositionActionUpsize = "/api/execution/positions/actions/upsize";
    public const string ExecutionPortfolio = "/api/execution/portfolio";
    public const string ExecutionHealth = "/api/execution/health";
    public const string ExecutionCapabilities = "/api/execution/capabilities";
    public const string ExecutionAudit = "/api/execution/audit";
    public const string ExecutionControls = "/api/execution/controls";
    public const string ExecutionControlsCircuitBreaker = "/api/execution/controls/circuit-breaker";
    public const string ExecutionControlsDefaultPositionLimit = "/api/execution/controls/position-limits/default";
    public const string ExecutionControlsSymbolPositionLimit = "/api/execution/controls/position-limits/{symbol}";
    public const string ExecutionControlsManualOverrides = "/api/execution/controls/manual-overrides";
    public const string ExecutionControlsManualOverrideClear = "/api/execution/controls/manual-overrides/{overrideId}/clear";
    public static readonly string ExecutionManualOverrides = ExecutionControlsManualOverrides;
    public static readonly string ExecutionManualOverrideClear = ExecutionControlsManualOverrideClear;
    public const string ExecutionSessions = "/api/execution/sessions";
    public const string ExecutionSessionById = "/api/execution/sessions/{sessionId}";
    public const string ExecutionSessionCreate = "/api/execution/sessions/create";
    public const string ExecutionSessionClose = "/api/execution/sessions/{sessionId}/close";
    public const string ExecutionSessionReplay = "/api/execution/sessions/{sessionId}/replay";

    // Multi-account execution endpoints
    public const string ExecutionAccounts = "/api/execution/accounts";
    public const string ExecutionAccountById = "/api/execution/accounts/{accountId}";
    public const string ExecutionAccountPositions = "/api/execution/accounts/{accountId}/positions";
    public const string ExecutionPortfolioAggregate = "/api/execution/portfolio/aggregate";

    // Cross-strategy portfolio workstation endpoints
    public const string PortfolioAggregate = "/api/portfolio/aggregate";
    public const string PortfolioExposure = "/api/portfolio/exposure";
    public const string PortfolioSymbolExposure = "/api/portfolio/symbols/{symbol}/exposure";

    // Promotion workflow endpoints (Backtest → Paper → Live)
    public const string PromotionEvaluate = "/api/promotion/evaluate/{runId}";
    public const string PromotionApprove = "/api/promotion/approve";
    public const string PromotionReject = "/api/promotion/reject";
    public const string PromotionHistory = "/api/promotion/history";

    // Workstation root and compatibility workspace endpoints
    public const string WorkstationSession = "/api/workstation/session";
    public const string WorkstationResearch = "/api/workstation/research";
    public const string WorkstationStrategy = "/api/workstation/strategy";
    public const string WorkstationResearchBriefing = "/api/workstation/research/briefing";
    public const string WorkstationStrategyBriefing = "/api/workstation/strategy/briefing";
    public const string WorkstationTrading = "/api/workstation/trading";
    public const string WorkstationDataOperations = "/api/workstation/data-operations";
    public const string WorkstationData = "/api/workstation/data";
    public const string WorkstationDataUploadTemplates = "/api/workstation/data/uploads/templates";
    public const string WorkstationDataUploadPreview = "/api/workstation/data/uploads/preview";
    public const string WorkstationBankStatementImport = "/api/workstation/data/uploads/bank-statements/import";
    public const string WorkstationGovernance = "/api/workstation/governance";
    public const string WorkstationAccounting = "/api/workstation/accounting";
    public const string WorkstationReporting = "/api/workstation/reporting";
    public const string WorkstationPortfolio = "/api/workstation/portfolio";
    public const string WorkstationPortfolioSummary = "/api/workstation/portfolio/summary";
    public const string WorkstationPortfolioMultiAssetCoverage = "/api/workstation/portfolio/multi-asset-coverage";

    // Strategy run comparison and diff endpoints
    public const string WorkstationWorkflowSummary = "/api/workstation/workflow-summary";
    public const string WorkstationFeatureCapabilities = "/api/workstation/settings/feature-capabilities";
    public const string WorkstationFeatureCapabilityByKey = "/api/workstation/settings/feature-capabilities/{capabilityKey}";
    public const string WorkstationTradingReadiness = "/api/workstation/trading/readiness";
    public const string WorkstationOperatorInbox = "/api/workstation/operator/inbox";
    public const string WorkstationEvidenceSubjects = "/api/workstation/evidence/subjects";
    public const string WorkstationEvidenceSubjectPacket = "/api/workstation/evidence/subjects/{subjectKind}/{subjectId}/packet";
    public const string WorkstationEvidenceSubjectGraph = "/api/workstation/evidence/subjects/{subjectKind}/{subjectId}/graph";
    public const string WorkstationEvidenceSubjectValidate = "/api/workstation/evidence/subjects/{subjectKind}/{subjectId}/validate";
    public const string WorkstationEvidenceSubjectExportManifest = "/api/workstation/evidence/subjects/{subjectKind}/{subjectId}/export-manifest";
    public const string WorkstationEvidenceTemplates = "/api/workstation/evidence/templates";
    public const string OperationsContinuity = "/api/workstation/operations/continuity";
    public const string OperationsContinuityById = "/api/workstation/operations/continuity/{workflowId:guid}";
    public const string OperationsContinuityCloseReadiness = "/api/workstation/operations/continuity/{workflowId:guid}/close-readiness";
    public const string OperationsContinuityTimeline = "/api/workstation/operations/continuity/{workflowId:guid}/timeline";
    public const string OperationsContinuityBreaks = "/api/workstation/operations/continuity/{workflowId:guid}/breaks";
    public const string OperationsContinuityLedgerPreview = "/api/workstation/operations/continuity/{workflowId:guid}/ledger-preview";
    public const string OperationsContinuityChecklist = "/api/workstation/operations/continuity/{workflowId:guid}/checklist";
    public const string OperationsContinuityChecklistAcknowledge = "/api/workstation/operations/continuity/{workflowId:guid}/checklist/{taskId}/acknowledge";
    public const string OperationsContinuityCloseCalendar = "/api/workstation/operations/continuity/close-calendar";
    public const string OperationsContinuityBrokerImport = "/api/workstation/operations/continuity/{workflowId:guid}/broker/import";
    public const string OperationsContinuityBrokerNormalize = "/api/workstation/operations/continuity/{workflowId:guid}/broker/normalize";
    public const string OperationsContinuityPostureRefresh = "/api/workstation/operations/continuity/{workflowId:guid}/posture/refresh";
    public const string OperationsContinuitySecurityMasterResolve = "/api/workstation/operations/continuity/{workflowId:guid}/security-master/resolve";
    public const string OperationsContinuitySecurityMasterOverrideApprove = "/api/workstation/operations/continuity/{workflowId:guid}/security-master/overrides/{overrideId}/approve";
    public const string OperationsContinuityLedgerDraft = "/api/workstation/operations/continuity/{workflowId:guid}/ledger/draft";
    public const string OperationsContinuityLedgerValidate = "/api/workstation/operations/continuity/{workflowId:guid}/ledger/validate";
    public const string OperationsContinuityLedgerPost = "/api/workstation/operations/continuity/{workflowId:guid}/ledger/post";
    public const string OperationsContinuityReconciliationRun = "/api/workstation/operations/continuity/{workflowId:guid}/reconciliation/run";
    public const string OperationsContinuityReconciliationBreakResolve = "/api/workstation/operations/continuity/{workflowId:guid}/reconciliation/breaks/{breakId}/resolve";
    public const string OperationsContinuityApprovalPolicyMatrix = "/api/workstation/operations/continuity/approval-policy-matrix";
    public const string OperationsContinuityApprovalPolicyRules = "/api/workstation/operations/continuity/approval-policy-rules";
    public const string OperationsContinuityCloseCalendarItems = "/api/workstation/operations/continuity/close-calendar-items";
    public const string OperationsContinuityApprovalSubmit = "/api/workstation/operations/continuity/{workflowId:guid}/approval/submit";
    public const string OperationsContinuityApprovalApprove = "/api/workstation/operations/continuity/{workflowId:guid}/approval/approve";
    public const string OperationsContinuityApprovalReject = "/api/workstation/operations/continuity/{workflowId:guid}/approval/reject";
    public const string OperationsContinuityClose = "/api/workstation/operations/continuity/{workflowId:guid}/close";
    public const string OperationsContinuityReopen = "/api/workstation/operations/continuity/{workflowId:guid}/reopen";
    public const string RunsCompare = "/api/workstation/runs/compare";
    public const string RunsDiff = "/api/workstation/runs/diff";
    public const string RunsEquityCurve = "/api/workstation/runs/{runId}/equity-curve";
    public const string RunsFills = "/api/workstation/runs/{runId}/fills";
    public const string RunsAttribution = "/api/workstation/runs/{runId}/attribution";
    public const string RunHistory = "/api/workstation/runs/history";
    public const string ReconciliationRuns = "/api/workstation/reconciliation/runs";
    public const string ReconciliationRunById = "/api/workstation/reconciliation/runs/{reconciliationRunId}";
    public const string ReconciliationStatementRuns = "/api/workstation/reconciliation/statement-runs";
    public const string ReconciliationStatementRunById = "/api/workstation/reconciliation/statement-runs/{runId}";
    public const string ReconciliationStatementRunValidation = "/api/workstation/reconciliation/statement-runs/{runId}/validation";
    public const string ReconciliationStatementRunBreaks = "/api/workstation/reconciliation/statement-runs/{runId}/breaks";
    public const string ReconciliationStatementRunReconcile = "/api/workstation/reconciliation/statement-runs/{runId}/reconcile";
    public const string ReconciliationStatementExceptions = "/api/workstation/reconciliation/statement-exceptions";
    public const string ReconciliationStatementBreaks = "/api/workstation/reconciliation/statement-breaks";
    public const string ReconciliationOpenCases = "/api/workstation/reconciliation/cases";
    public const string ReconciliationQueueStatus = "/api/workstation/reconciliation/queue-status";
    public const string RunsReconciliation = "/api/workstation/runs/{runId}/reconciliation";
    public const string RunsReconciliationHistory = "/api/workstation/runs/{runId}/reconciliation/history";
    public const string RunsLedger = "/api/workstation/runs/{runId}/ledger";
    /// <summary>
    /// Canonical strategy run continuity drill-in route shared by workstation surfaces.
    /// </summary>
    public const string RunsContinuity = "/api/workstation/runs/{runId}/continuity";
    public const string RunsReviewPacket = "/api/workstation/runs/{runId}/review-packet";
    public const string RunsLedgerTrialBalance = "/api/workstation/runs/{runId}/ledger/trial-balance";
    public const string RunsLedgerJournal = "/api/workstation/runs/{runId}/ledger/journal";
    public const string LedgerBooks = "/api/ledger/books";
    public const string LedgerBookById = "/api/ledger/books/{ledgerBookId:guid}";
    public const string LedgerPeriods = "/api/ledger/periods";
    public const string LedgerPeriodClose = "/api/ledger/periods/{periodId:guid}/close";
    public const string LedgerPeriodTrialBalance = "/api/ledger/periods/{periodId:guid}/trial-balance";
    public const string LedgerPeriodTrialBalanceReport = "/api/ledger/periods/{periodId:guid}/trial-balance-report";
    public const string LedgerPeriodPnlSummary = "/api/ledger/periods/{periodId:guid}/pnl-summary";
    public const string LedgerAccountingConfiguration = "/api/ledger/accounting-configuration";
    public const string LedgerAccountingConfigurationChart = "/api/ledger/accounting-configuration/chart";
    public const string LedgerAccountingConfigurationTemplates = "/api/ledger/accounting-configuration/templates";
    public const string LedgerAccountingConfigurationPostingRules = "/api/ledger/accounting-configuration/posting-rules";
    public const string LedgerAccountingConfigurationPreview = "/api/ledger/accounting-configuration/preview";
    public const string LedgerAccountingConfigurationActivate = "/api/ledger/accounting-configuration/activate";
    public const string LedgerAccountingConfigurationAudit = "/api/ledger/accounting-configuration/audit";
    public const string LedgerManualJournalEntryWorkbench = "/api/ledger/journal-entry-workbench";
    public const string LedgerManualJournalEntryDrafts = "/api/ledger/journal-entry-workbench/drafts";
    public const string LedgerManualJournalEntryValidate = "/api/ledger/journal-entry-workbench/validate";
    public const string LedgerManualJournalEntrySubmitApproval = "/api/ledger/journal-entry-workbench/submit-approval";
    public const string LedgerReportsTrialBalance = "/api/ledger/reports/trial-balance";
    public const string LedgerReportsPnlSummary = "/api/ledger/reports/pnl-summary";
    public const string WorkstationSecurityMasterSearch = "/api/workstation/security-master/securities";
    public const string WorkstationSecurityMasterById = "/api/workstation/security-master/securities/{securityId:guid}";
    public const string WorkstationSecurityMasterHistory = "/api/workstation/security-master/securities/{securityId:guid}/history";
    public const string WorkstationSecurityMasterIdentity = "/api/workstation/security-master/securities/{securityId:guid}/identity";
    public const string WorkstationSecurityMasterInstrumentPassport = "/api/workstation/security-master/securities/{securityId:guid}/passport";
    public const string WorkstationSecurityMasterEconomicDefinition = "/api/workstation/security-master/securities/{securityId:guid}/economic-definition";
    public const string WorkstationSecurityMasterTrustSnapshot = "/api/workstation/security-master/securities/{securityId:guid}/trust-snapshot";
    public const string WorkstationSecurityMasterBulkResolveConflicts = "/api/workstation/security-master/conflicts/bulk-resolve";
    public const string ReconciliationCalibrationSummary = "/api/workstation/reconciliation/calibration-summary";
    public const string ReconciliationBreakQueue = "/api/workstation/reconciliation/break-queue";
    public const string ReconciliationCaseTaxonomy = "/api/workstation/reconciliation/break-queue/taxonomy";
    public const string ReconciliationBreakQueueById = "/api/workstation/reconciliation/break-queue/{breakId}";
    public const string ReconciliationBreakRebuiltSnapshot = "/api/workstation/reconciliation/break-queue/{breakId}/rebuilt-snapshot";
    public const string ReconciliationBreakAudit = "/api/workstation/reconciliation/break-queue/{breakId}/audit";
    public const string ReconciliationBreakReview = "/api/workstation/reconciliation/break-queue/{breakId}/review";
    public const string ReconciliationBreakResolve = "/api/workstation/reconciliation/break-queue/{breakId}/resolve";
    public const string ReconciliationBreakAssign = "/api/workstation/reconciliation/break-queue/{breakId}/assign";
    public const string ReconciliationBreakTransition = "/api/workstation/reconciliation/break-queue/{breakId}/transition";
    public const string ReconciliationBreakComments = "/api/workstation/reconciliation/break-queue/{breakId}/comments";
    public const string ReconciliationBreakComment = "/api/workstation/reconciliation/break-queue/{breakId}/comments/{commentId}";
    public const string ReconciliationBreakRootCause = "/api/workstation/reconciliation/break-queue/{breakId}/root-cause";
    public const string ReconciliationBreakResolution = "/api/workstation/reconciliation/break-queue/{breakId}/resolution";
    public const string ReconciliationBreakSignOff = "/api/workstation/reconciliation/break-queue/{breakId}/sign-off";
    public const string ReconciliationBreakReopen = "/api/workstation/reconciliation/break-queue/{breakId}/reopen";
    public const string ReconciliationBreakBulkDryRun = "/api/workstation/reconciliation/break-queue/bulk/dry-run";
    public const string ReconciliationBreakBulkExecute = "/api/workstation/reconciliation/break-queue/bulk/execute";
    public const string ReconciliationBreakBulkStatus = "/api/workstation/reconciliation/break-queue/bulk/{bulkActionId}";
    public const string ReconciliationBreakBulkResult = "/api/workstation/reconciliation/break-queue/bulk/{bulkActionId}/result";
    public const string FundReportPacks = "/api/fund-structure/report-packs";
    public const string FundReportPackById = "/api/fund-structure/report-packs/{reportId}";
    public const string FundReportPackEvidenceBundle = "/api/fund-structure/report-packs/{reportId}/evidence-bundle";
    public const string FundReportPackLedgerProvenance = "/api/fund-structure/report-packs/{reportId}/ledger-provenance";
    public const string ReportingPackWorkflows = "/api/fund-structure/reporting/packs";
    public const string ReportingPackWorkflowValidate = "/api/fund-structure/reporting/packs/{reportId}/validate";
    public const string ReportingPackWorkflowSubmit = "/api/fund-structure/reporting/packs/{reportId}/submit";
    public const string ReportingPackWorkflowApprove = "/api/fund-structure/reporting/packs/{reportId}/approve";
    public const string ReportingPackWorkflowPublish = "/api/fund-structure/reporting/packs/{reportId}/publish";
    public const string ReportingPackWorkflowRestate = "/api/fund-structure/reporting/packs/{reportId}/restatements";
    public const string ReportingPackWorkflowArchive = "/api/fund-structure/reporting/packs/{reportId}/archive";
    public const string ReportingPackWorkflowDeliveries = "/api/fund-structure/reporting/packs/{reportId}/deliveries";
    public const string ReportingPackWorkflowDeliveryFailures = "/api/fund-structure/reporting/packs/{reportId}/deliveries/failures";
    public const string ReportingRuns = "/api/fund-structure/reporting/runs";
    public const string ReportingSchedules = "/api/fund-structure/reporting/schedules";
    public const string ReportingScheduleRunDue = "/api/fund-structure/reporting/schedules/run-due";
    public const string ReportingSchedulePause = "/api/fund-structure/reporting/schedules/{scheduleId}/pause";
    public const string ReportingScheduleResume = "/api/fund-structure/reporting/schedules/{scheduleId}/resume";
    public const string ReportingScheduleRunNow = "/api/fund-structure/reporting/schedules/{scheduleId}/run";
    public const string FundStructureSetupDraftValidate = "/api/fund-structure/setup-drafts/validate";
    public const string FundStructureSetupDraftCreate = "/api/fund-structure/setup-drafts/create";
    public const string FundStructureLegalEntityProfile = "/api/fund-structure/entities/{entityId}";
    public const string FundStructureLedgerMappingAssignments = "/api/fund-structure/ledger-mapping-assignments";

    // Fund account brokerage read-side sync endpoints
    public const string FundAccountBrokerageSyncAccounts = "/api/fund-accounts/brokerage-sync/accounts";
    public const string FundAccountBrokerageSyncStatus = "/api/fund-accounts/{accountId}/brokerage-sync/status";
    public const string FundAccountBrokerageSyncRun = "/api/fund-accounts/{accountId}/brokerage-sync/run";
    public const string FundAccountBrokerageSyncPositions = "/api/fund-accounts/{accountId}/brokerage-sync/positions";
    public const string FundAccountBrokerageSyncActivity = "/api/fund-accounts/{accountId}/brokerage-sync/activity";
    public const string FundAccountBrokerageSyncReconcileLedger = "/api/fund-accounts/{accountId}/brokerage-sync/reconcile-ledger";
    public const string FundAccountBrokerageSyncReconciliationLatest = "/api/fund-accounts/{accountId}/brokerage-sync/reconciliation/latest";
    public const string FundAccountCloseReadiness = "/api/fund-accounts/{accountId}/close-readiness";

    // Portfolio cash-flow projection endpoints
    public const string PortfolioCashFlows = "/api/portfolio/{runId}/cash-flows";

    // Resilience endpoints
    public const string ResilienceCircuitBreakers = "/api/resilience/circuit-breakers";

    // Backfill cost estimation
    public const string BackfillCostEstimate = "/api/backfill/cost-estimate";

    // Retention compliance
    public const string RetentionComplianceReport = "/api/admin/retention/compliance-report";

    /// <summary>
    /// Replaces a route parameter with a URL-encoded path segment value.
    /// </summary>
    public static string WithParam(string route, string paramName, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        ArgumentException.ThrowIfNullOrWhiteSpace(paramName);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var encodedValue = Uri.EscapeDataString(value.Trim());
        var parameterName = paramName.Trim();
        var simplePlaceholder = $"{{{parameterName}}}";
        var updated = route.Replace(simplePlaceholder, encodedValue, StringComparison.Ordinal);

        var constrainedPrefix = $"{{{parameterName}:";
        var searchIndex = 0;
        while (searchIndex < updated.Length)
        {
            var startIndex = updated.IndexOf(constrainedPrefix, searchIndex, StringComparison.Ordinal);
            if (startIndex < 0)
            {
                break;
            }

            var endIndex = updated.IndexOf('}', startIndex);
            if (endIndex < 0)
            {
                break;
            }

            updated = string.Concat(
                updated.AsSpan(0, startIndex),
                encodedValue,
                updated.AsSpan(endIndex + 1));
            searchIndex = startIndex + encodedValue.Length;
        }

        return updated;
    }

    /// <summary>
    /// Appends a query string to a route.
    /// </summary>
    public static string WithQuery(string route, string queryString)
        => string.IsNullOrEmpty(queryString) ? route : $"{route}?{queryString}";

    // Credential management endpoints
    public const string Credentials = "/api/credentials";
    public const string CredentialByProvider = "/api/credentials/{provider}";
    public const string CredentialTest = "/api/credentials/{provider}/test";

    // Quant Lab endpoints (gated behind QuantLab:Enabled host configuration)
    public const string QuantRun = "/api/quant/run";
    public const string QuantParameters = "/api/quant/parameters";
    public const string QuantTemplates = "/api/quant/templates";

    // Demo mode endpoints
    public const string DemoMode = "/api/demo/mode";
    public const string DemoSymbols = "/api/demo/symbols";
    public const string DemoMarketData = "/api/demo/market-data/{symbol}";
    public const string DemoHistoricalData = "/api/demo/historical/{symbol}";

    // Symbol universe management endpoints
    public const string SymbolUpdate = "/api/symbols/{symbol}/update";
    public const string SymbolDelete = "/api/symbols/{symbol}";
    public const string SymbolCreate = "/api/symbols/create";

    // Backfill validation endpoints
    public const string BackfillValidation = "/api/backfill/validation";
    public const string BackfillValidationBySymbol = "/api/backfill/validation/{symbol}";
    public const string BackfillGapDetection = "/api/backfill/gaps";
    public const string BackfillCompleteness = "/api/backfill/completeness";

    // Provider credential verification endpoints
    public const string ProviderCredentialsValidate = "/api/providers/{provider}/validate-credentials";
    public const string ProviderConnectionTest = "/api/providers/{provider}/test-connection";

    // Covered-call strategy endpoints (slice 1: backtest UI)
    public const string CoveredCallRuns = "/api/strategies/covered-call/runs";
    public const string CoveredCallRunStatus = "/api/strategies/covered-call/runs/{runId}/status";
    public const string CoveredCallRunResult = "/api/strategies/covered-call/runs/{runId}/result";
    public const string CoveredCallRunCancel = "/api/strategies/covered-call/runs/{runId}/cancel";
    public const string CoveredCallChainPreview = "/api/strategies/covered-call/chain-preview";
}
