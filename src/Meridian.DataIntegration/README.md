---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-DESIGN-DATA-INTEGRATION
path: src/Meridian.DataIntegration
status: active
owner_lane: Data Confidence and Validation
last_reviewed: 2026-06-06
---

# src/Meridian.DataIntegration

## Purpose

Physical bounded-context module project for provider, ingestion, canonicalization, validation,
source evidence, and publish-data ownership conformance.

## Layer responsibility

This module belongs to the Design Module layer. Keep changes within that ownership boundary and update the registry if the boundary changes.

## Key folders and files

- `src/Meridian.DataIntegration` - registered source module root.
- `Credentials/ProviderCredentialCatalog.cs` - canonical provider credential descriptors, field mappings, environment normalization, and credential-source projection.
- `Credentials/ICredentialStore.cs` - provider-neutral credential-store contract, credential metadata, validation result, and common credential extension helpers.
- `Credentials/IProviderCredentialStore.cs` - provider-neutral encrypted credential store contract and mutation/read result models.
- `Credentials/FileProviderCredentialStore.cs` - local encrypted provider credential vault with audit metadata, verification status, and environment fallback handling.
- `Credentials/CredentialStatus.cs` - provider credential status, test-result, cached-status, and expiration-warning records.
- `Credentials/OAuthToken.cs` - provider-neutral OAuth token, provider config, and token-refresh result records.
- `Etl/EtlAbstractions.cs`, `Etl/EtlExportService.cs`, and `Etl/EtlNormalizationService.cs` - ETL
  job-service/export-service contracts, export implementation, normalization outcome, run-result,
  export-result, partner schema registry, and partner-record normalization services used by
  Application adapters.
- `Canonicalization/` - provider event canonicalization contract, default event canonicalizer,
  Security Master id lookup seam, condition-code mapper, venue-to-MIC mapper, and
  canonicalization parity metrics used by ingestion, ETL, pipeline, and workstation status
  surfaces.
- `AccountingSystem/QuickBooks/QuickBooksFixtureAccountingProvider.cs` - read-only fixture GL evidence provider for contract and workstation validation.
- `AccountingSystem/QuickBooks/QuickBooksOnlineAccountingProvider.cs` - read-only QuickBooks Online accounting-system adapter, token refresh, connection verification, company evidence import, and DTO projection.
- `AccountingSystem/QuickBooks/QuickBooksOnlineProviderCredentialConnectionStore.cs` - QuickBooks Online connection metadata, refresh-token, and verification-state adapter backed by the provider credential vault.
- `Filters/MarketEventFilter.cs` - provider-ingestion market-event filter for symbol,
  event-type, and processing-tier matching.
- `Historical/HistoricalDataQueryService.cs` - JSONL-backed historical market-data query and
  OHLCV bar aggregation service used by CLI, diagnostics, simulation, and shared-data access
  adapters.
- `Monitoring/BadTickFilter.cs`, `Monitoring/TickSizeValidator.cs`,
  `Monitoring/TimestampMonotonicityChecker.cs`, `Monitoring/ValidationMetrics.cs`,
  `Monitoring/ProviderLatencyService.cs`, and `Monitoring/ProviderMetricsStatus.cs` - provider
  data-quality validation filters, latency histograms, provider metrics snapshots, and F#
  validation-stage counters used by ingestion, routing, diagnostics, and Application/UI adapters.
- `Monitoring/DataQuality/` - provider data-quality analyzers, freshness SLA monitor, quality
  report generator, gap/sequence/anomaly/completeness/latency trackers, and liquidity-aware
  quality thresholds used by ingestion, backfill remediation, health, and shared endpoint
  adapters.
- `Testing/DepthBufferSelfTests.cs` and `Testing/SampleDataGenerator.cs` - built-in
  depth-buffer integrity self-tests and deterministic sample market-event generation used by
  Application diagnostics adapters.

## Important workflows

Use this README to understand the module before editing source files. Update the registry when validation, roadmap links, diagrams, or ownership changes.

QuickBooks accounting-system integration lives in this module. The adapter family imports chart-of-accounts, journal-entry, and trial-balance evidence as read-only reconciliation input through `IAccountingSystemProvider`, refreshes OAuth access tokens through the server-side QuickBooks client seam, records connection verification posture, maps provider-vault credentials into the QuickBooks connection store, and leaves posting/export disabled. UI Shared registers the Data Integration providers and connection store; it does not own QuickBooks transport, credential persistence mapping, or import mapping.

Provider credential catalog, credential-store contracts, vault, status, and OAuth record ownership also lives in this module. Application and UI layers may orchestrate setup, testing, token refresh, and endpoint projection, but provider credential descriptors, encrypted local storage, validation metadata, verification metadata, expiration policy records, OAuth token records, and provider-environment normalization must stay behind the `Meridian.DataIntegration.Credentials` seam.

Market-event filtering and provider-depth-buffer self-tests live in this module because they
validate and route provider-ingested event streams before higher-level application orchestration.
Application CLI commands may invoke the self-test runner, but Data Integration owns the filter and
depth-buffer validation behavior. Sample market-event generation also lives here so development
fixtures and diagnostics use the provider/data-ingestion module instead of Application-local data
fabrication.

ETL service/export contracts, the export service implementation, result records, partner schema
registry, and partner-record normalization live in this module. The job-definition store and SFTP
publisher port contracts live in `Meridian.Contracts.Etl`, and the local JSON-backed
job-definition store implementation lives in `Meridian.Storage.Etl`. Application still owns the
current ETL job/orchestrator adapters while ingestion job orchestration and event-pipeline
dependencies remain layer-oriented prerequisites.

Canonicalization contracts, the default event canonicalizer, provider condition-code mapping, venue
normalization, Security Master id lookup seam, and parity metrics also live in this module.
Application can still compose the event-pipeline decorator and quarantine wiring, but
provider-ingestion normalization primitives should stay behind the
`Meridian.DataIntegration.Canonicalization` seam.

Historical market-data JSONL query and OHLCV aggregation behavior lives in this module. Application
commands, execution simulation, diagnostics registration, UI endpoints, and fund-structure shared
data access consume `Meridian.DataIntegration.Historical.HistoricalDataQueryService` instead of
owning file-backed historical-data query logic.

Provider data-quality validators, analyzers, freshness SLA monitoring, report generation, and
validation-stage counters live in this module. Provider latency histograms and provider metrics
snapshot contracts also live here so routing, diagnostics, browser, and desktop surfaces consume a
single provider-telemetry model. Application pipeline, backfill remediation,
Prometheus, health, daily-summary, and UI Shared endpoint adapters consume
`Meridian.DataIntegration.Monitoring` and `Meridian.DataIntegration.Monitoring.DataQuality`
instead of keeping provider trust primitives in the application layer.

## Diagrams

`DIA-ASSURANCE-LOOP`

## Roadmap traceability

<!-- source-roadmap-traceability:begin module=SRC-DESIGN-DATA-INTEGRATION -->
| Roadmap item | Title |
| --- | --- |
| `W1-DATA-001` | Provider trust gate and data confidence baseline |
| `W5-ACCT-001` | Accounting records and operational evidence |
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module=SRC-DESIGN-DATA-INTEGRATION -->
- No registry-backed TODOs are open for this module.
<!-- source-todos:end -->

## Validation

```bash
dotnet build src/Meridian.DataIntegration/Meridian.DataIntegration.csproj /p:EnableWindowsTargeting=true
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~AccountingSystemIntegrationServiceTests|FullyQualifiedName~ProviderConnectionEndpointsTests" --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true /p:NodeReuse=false
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~QuickBooksOnlineProviderCredentialConnectionStoreTests" --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true /p:NodeReuse=false
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~OAuthTokenTests|FullyQualifiedName~CredentialStatusTests|FullyQualifiedName~CredentialTestingServiceTests" --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true /p:NodeReuse=false
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~ProviderCredentialStoreTests|FullyQualifiedName~ProviderConnectionEndpointsTests|FullyQualifiedName~CredentialCompatibilityEndpointsTests|FullyQualifiedName~ProviderReadinessEndpointTests|FullyQualifiedName~ProviderRoutingEndpointsTests|FullyQualifiedName~ProviderFactoryCredentialContextTests|FullyQualifiedName~AlpacaBrokerageConnectionServiceTests|FullyQualifiedName~PlaidWorkstationServiceTests|FullyQualifiedName~BrokerageConnectionEndpointsTests" --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true /p:NodeReuse=false
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~MarketEventFilterTests|FullyQualifiedName~SelfTestCommandTests" --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true /p:NodeReuse=false
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~CredentialStoreExtensionsTests" --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true /p:NodeReuse=false
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~EtlExportServiceTests|FullyQualifiedName~EtlNormalizationServiceTests|FullyQualifiedName~EtlJobOrchestratorTests|FullyQualifiedName~EtlJobDefinitionStoreTests|FullyQualifiedName~EtlCommandsTests|FullyQualifiedName~CsvPartnerFileParserTests" --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true /p:NodeReuse=false
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~ConditionCodeMapperTests|FullyQualifiedName~VenueMicMapperTests|FullyQualifiedName~EventCanonicalizerTests|FullyQualifiedName~CanonicalizingPublisherTests|FullyQualifiedName~CanonicalizationGoldenFixtureTests" --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true /p:NodeReuse=false
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~EtlNormalizationServiceTests|FullyQualifiedName~EtlJobOrchestratorTests|FullyQualifiedName~PipelineFeatureRegistrationTests" --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true /p:NodeReuse=false
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~HistoricalDataQueryServiceTests|FullyQualifiedName~HistoricalDataQueryServiceBarsTests|FullyQualifiedName~ExecutionSimulationOrchestratorTests" --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true /p:NodeReuse=false
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~BadTickFilterTests|FullyQualifiedName~TickSizeValidatorTests|FullyQualifiedName~FSharpEventValidatorTests|FullyQualifiedName~ProviderLatencyServiceTests" --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true /p:NodeReuse=false
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~DataFreshnessSlaMonitorTests|FullyQualifiedName~DataFreshnessSlaMonitorMarketHoursTests|FullyQualifiedName~SlaStatusSnapshotTests|FullyQualifiedName~LiquidityProfileTests|FullyQualifiedName~PriceContinuityCheckerTests|FullyQualifiedName~GapAnalyzerTests|FullyQualifiedName~SequenceErrorTrackerTests|FullyQualifiedName~CompletenessScoreCalculatorTests|FullyQualifiedName~AnomalyDetectorTests|FullyQualifiedName~DataQualityMonitoringServiceTests|FullyQualifiedName~LatencyHistogramTests|FullyQualifiedName~CrossProviderComparisonServiceTests" --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true /p:NodeReuse=false
dotnet test tests/Meridian.FundStructure.Tests/Meridian.FundStructure.Tests.csproj --filter "FullyQualifiedName~GovernanceSharedDataAccessServiceTests" --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true /p:NodeReuse=false
```

### API and contract notes

The QuickBooks adapter implements `IAccountingSystemProvider`, `IAccountingSystemConnectionMetadataProvider`, and `IAccountingSystemConnectionVerifier` from `Meridian.ProviderSdk.AccountingSystem`. Accounting-system DTOs remain in `Meridian.Contracts.AccountingSystem`.

### Migration and archive notes

`QuickBooksFixtureAccountingProvider`, `QuickBooksOnlineAccountingProvider`, `IQuickBooksOnlineConnectionStore`, `IQuickBooksOnlineClient`, `QuickBooksOnlineHttpClient`, and QuickBooks evidence records moved from `src/Meridian.Infrastructure/Adapters/QuickBooks` into this module. Infrastructure no longer owns QuickBooks accounting-system transport.

`QuickBooksOnlineProviderCredentialConnectionStore` moved from `src/Meridian.Ui.Shared/Services` into this module. UI Shared keeps only endpoint and service-collection registration for the Data Integration-owned QuickBooks connection adapter.

`ProviderCredentialCatalog`, `ICredentialStore`, `IProviderCredentialStore`, `FileProviderCredentialStore`, `CredentialStatus`, and `OAuthToken` moved from Application credential/config folders into this module. Application keeps credential testing, OAuth refresh, legacy resolver/composition, and provider setup orchestration as consumers of the Data Integration credential seam.

`MarketEventFilter`, `DepthBufferSelfTests`, and `SampleDataGenerator` moved from
`src/Meridian.Application` into this module. Application keeps the `--selftest` command and
diagnostics registration as adapters that invoke the Data Integration-owned testing helpers.

`IEtlJobService`, `IEtlExportService`, `NormalizationOutcome`, `EtlRunResult`,
`EtlExportResult`, `PartnerSchemaRegistry`, `EtlNormalizationService`, and `EtlExportService`
moved from Application ETL abstractions/implementations into this module. `IEtlJobDefinitionStore`
and `ISftpFilePublisher` moved to `Meridian.Contracts.Etl`, and the local JSON-backed
`EtlJobDefinitionStore` moved to `Meridian.Storage.Etl`. Application keeps current ETL
job/orchestrator implementations until pipeline and ingestion job dependencies can move or invert
cleanly.

`IEventCanonicalizer`, `EventCanonicalizer`, `ICanonicalSecurityIdLookup`,
`ConditionCodeMapper`, `VenueMicMapper`, `ICanonicalizationMetrics`,
`DefaultCanonicalizationMetrics`, and canonicalization snapshot/parity records moved from
Application into this module. Application keeps the Security Master seed implementation and
`CanonicalizingPublisher` decorator until their remaining Security Master and event-pipeline
dependencies can move or invert cleanly.

`HistoricalDataQueryService` moved from `src/Meridian.Application/Services` into this module.
Application keeps CLI command, diagnostics, execution-simulation, and composition adapters that
consume the Data Integration historical query seam.

`BadTickFilter`, `TickSizeValidator`, `TimestampMonotonicityChecker`, and `ValidationMetrics`
moved from `src/Meridian.Application/Monitoring` into this module. Application keeps pipeline and
Prometheus adapters that consume the Data Integration-owned provider validation evidence.

`AnomalyDetector`, `CompletenessScoreCalculator`, `CrossProviderComparisonService`,
`DataFreshnessSlaMonitor`, `DataQualityMonitoringService`, `DataQualityReportGenerator`,
`GapAnalyzer`, `LatencyHistogram`, `LiquidityProfileProvider`, `PriceContinuityChecker`,
`SequenceErrorTracker`, data-quality models, and quality-analyzer contracts moved from
`src/Meridian.Application/Monitoring/DataQuality` into this module. Application and UI Shared keep
composition, remediation, health, and endpoint adapters that consume these Data Integration-owned
quality services.

`ProviderLatencyService`, provider latency summary records, `ProviderMetricsStatus`, and
`ProviderMetrics` moved from `src/Meridian.Application/Monitoring` into this module. Application
keeps connection health, degradation scoring, status endpoint handlers, and composition adapters
that consume the Data Integration-owned provider telemetry models.

## Change rules

Preserve the module boundary declared in `docs/source/data/source-modules.yml` and update the nearest docs when behavior or workflow semantics change.

## Related docs

- `docs/source/README.md`
- `docs/source/generated/source-module-index.md`
- `docs/architecture/module-map.md`
