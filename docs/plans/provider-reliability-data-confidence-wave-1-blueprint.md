# Provider Reliability and Data Confidence Wave 1 Blueprint

**Owner:** Core Team  
**Audience:** Infrastructure, storage, provider, QA, and operations contributors  
**Last Updated:** 2026-04-08  
**Status:** Active blueprint for the Wave 1 core operator-readiness gate

---

## Summary

Wave 1 closes the trust gate that blocks every downstream readiness claim in Meridian. The goal is not more provider surface area. The goal is evidence-backed confidence that the providers, replay paths, checkpointing, and persistence seams already present in the repo behave predictably enough to support backtesting, paper-trading, and later execution work without overstating what has been validated.

This blueprint turns the active Wave 1 scope into a concrete delivery plan grounded in the current provider contracts, replay suites, backfill services, and Parquet sink implementation already in the repository. It is the main plan artifact behind the provider-confidence gate in the canonical roadmap.

---

## Scope

### In scope

- Polygon replay coverage across feeds and edge cases.
- Robinhood execution-adjacent provider confidence across brokerage submit, quote polling, historical daily bars, and symbol search evidence.
- Interactive Brokers runtime and bootstrap validation against real vendor surfaces.
- NYSE shared-lifecycle and Level 2 depth coverage.
- StockSharp connector examples and validated adapter guidance.
- Backfill checkpoint reliability and gap detection across providers and date ranges.
- Parquet sink flush-path hardening and ADR-014 cleanup for L2 snapshot persistence.
- NYSE transport hardening: `IHttpClientFactory` alignment plus cancellation-safe websocket send and resubscribe flows.
- Synchronizing Wave 1 evidence across roadmap, provider-confidence baseline, validation matrix, and production-readiness docs.

### Out of scope

- Broad live-broker rollout or general live-trading readiness claims.
- New provider families or speculative connector expansion.
- Historical-only and Security Master support providers such as FRED and EDGAR; they remain inventoried but are not part of the execution-oriented Wave 1 exit gate.
- Workstation shell, cockpit, or governance product work outside the evidence surfaces needed to close this gate.
- Optional research tracks such as L3 inference, QuantScript expansion, or multi-instance scale-out.

### Assumptions

- Offline, replay, and CI-friendly evidence remain the mandatory baseline for this wave.
- Real-vendor checks are explicit runtime evidence, not something implied by the default build.
- The minimum StockSharp validated-adapter set for this wave is `Rithmic`, `IQFeed`, `CQG`, and `InteractiveBrokers`, because those package surfaces are already represented in Meridian's current StockSharp support.
- Polygon remains a replay and streaming-confidence target, not a Level 2 depth target.
- Robinhood is in scope because it already participates in quote, historical, symbol-search, and stable execution seams; FRED and EDGAR stay in inventory/status docs unless their non-execution surfaces materially change.
- Raw runtime artifacts should be archived under `artifacts/provider-validation/` with the docs carrying the human-readable summary and gate result.

---

## Architecture

### Current grounded state

Wave 1 already has the right architectural anchors in code:

- Streaming provider seams exist through [`src/Meridian.ProviderSdk/IMarketDataClient.cs`](../../src/Meridian.ProviderSdk/IMarketDataClient.cs), [`src/Meridian.ProviderSdk/IDataSource.cs`](../../src/Meridian.ProviderSdk/IDataSource.cs), and [`src/Meridian.Infrastructure/Adapters/Core/IHistoricalDataProvider.cs`](../../src/Meridian.Infrastructure/Adapters/Core/IHistoricalDataProvider.cs).
- Polygon replay and parser coverage already flows through [`src/Meridian.Infrastructure/Adapters/Polygon/PolygonMarketDataClient.cs`](../../src/Meridian.Infrastructure/Adapters/Polygon/PolygonMarketDataClient.cs) and [`tests/Meridian.Tests/Infrastructure/Providers/PolygonRecordedSessionReplayTests.cs`](../../tests/Meridian.Tests/Infrastructure/Providers/PolygonRecordedSessionReplayTests.cs).
- Robinhood execution-adjacent coverage already flows through [`src/Meridian.Infrastructure/Adapters/Robinhood/RobinhoodBrokerageGateway.cs`](../../src/Meridian.Infrastructure/Adapters/Robinhood/RobinhoodBrokerageGateway.cs), [`src/Meridian.Infrastructure/Adapters/Robinhood/RobinhoodMarketDataClient.cs`](../../src/Meridian.Infrastructure/Adapters/Robinhood/RobinhoodMarketDataClient.cs), [`tests/Meridian.Tests/Infrastructure/Providers/RobinhoodBrokerageGatewayTests.cs`](../../tests/Meridian.Tests/Infrastructure/Providers/RobinhoodBrokerageGatewayTests.cs), [`tests/Meridian.Tests/Infrastructure/Providers/RobinhoodMarketDataClientTests.cs`](../../tests/Meridian.Tests/Infrastructure/Providers/RobinhoodMarketDataClientTests.cs), and the stable-seam execution path in [`tests/Meridian.Tests/Ui/ExecutionGovernanceEndpointsTests.cs`](../../tests/Meridian.Tests/Ui/ExecutionGovernanceEndpointsTests.cs).
- Interactive Brokers bootstrap guidance already exists through [`src/Meridian.Infrastructure/Adapters/InteractiveBrokers/EnhancedIBConnectionManager.cs`](../../src/Meridian.Infrastructure/Adapters/InteractiveBrokers/EnhancedIBConnectionManager.cs), [`src/Meridian.Infrastructure/Adapters/InteractiveBrokers/EnhancedIBConnectionManager.IBApi.cs`](../../src/Meridian.Infrastructure/Adapters/InteractiveBrokers/EnhancedIBConnectionManager.IBApi.cs), [`src/Meridian.Infrastructure/Adapters/InteractiveBrokers/IBApiVersionValidator.cs`](../../src/Meridian.Infrastructure/Adapters/InteractiveBrokers/IBApiVersionValidator.cs), and [`docs/providers/interactive-brokers-setup.md`](../providers/interactive-brokers-setup.md).
- NYSE lifecycle and transport already center on [`src/Meridian.Infrastructure/Adapters/NYSE/NYSEDataSource.cs`](../../src/Meridian.Infrastructure/Adapters/NYSE/NYSEDataSource.cs), [`src/Meridian.Infrastructure/Adapters/NYSE/NyseMarketDataClient.cs`](../../src/Meridian.Infrastructure/Adapters/NYSE/NyseMarketDataClient.cs), and [`tests/Meridian.Tests/Infrastructure/Providers/NyseSharedLifecycleTests.cs`](../../tests/Meridian.Tests/Infrastructure/Providers/NyseSharedLifecycleTests.cs).
- StockSharp adapter capability and conversion seams are already in [`src/Meridian.Infrastructure/Adapters/StockSharp/StockSharpConnectorFactory.cs`](../../src/Meridian.Infrastructure/Adapters/StockSharp/StockSharpConnectorFactory.cs), [`src/Meridian.Infrastructure/Adapters/StockSharp/StockSharpConnectorCapabilities.cs`](../../src/Meridian.Infrastructure/Adapters/StockSharp/StockSharpConnectorCapabilities.cs), [`src/Meridian.Infrastructure/Adapters/StockSharp/Converters/MessageConverter.cs`](../../src/Meridian.Infrastructure/Adapters/StockSharp/Converters/MessageConverter.cs), and [`docs/providers/stocksharp-connectors.md`](../providers/stocksharp-connectors.md).
- Backfill checkpointing and gap work already exist in [`src/Meridian.Application/Backfill/BackfillStatusStore.cs`](../../src/Meridian.Application/Backfill/BackfillStatusStore.cs), [`src/Meridian.Application/Backfill/GapBackfillService.cs`](../../src/Meridian.Application/Backfill/GapBackfillService.cs), [`src/Meridian.Application/Backfill/HistoricalBackfillService.cs`](../../src/Meridian.Application/Backfill/HistoricalBackfillService.cs), and the checkpoint endpoints exercised by [`tests/Meridian.Tests/Integration/EndpointTests/CheckpointEndpointTests.cs`](../../tests/Meridian.Tests/Integration/EndpointTests/CheckpointEndpointTests.cs).
- L2 snapshot persistence already passes through [`src/Meridian.Storage/Sinks/ParquetStorageSink.cs`](../../src/Meridian.Storage/Sinks/ParquetStorageSink.cs), which has a dedicated L2 schema and already serializes `BidsJson` and `AsksJson` through `MarketDataJsonContext`.

### Current gaps this wave must close

- Polygon replay evidence is strong, but the matrix still treats reconnect and rate-limit runtime proof as partial.
- Robinhood has stable-seam execution and provider-suite coverage, but reconnect, cancellation, and rate-limit behavior are still only partially evidenced because the adapter relies on an unofficial broker API and polling-oriented quote surfaces.
- IB guidance is explicit, but the repo still needs a clearer line between simulation mode, compile-only smoke mode, and real vendor bootstrap evidence.
- NYSE has lifecycle and parser coverage, but auth failure, rate-limit, cancellation, and reconnect/resubscribe proof are not yet equally explicit in the matrix.
- StockSharp examples and capability tests exist, but the repo still needs a stronger definition of which adapters Meridian is prepared to call validated.
- Backfill checkpoints exist, but operator confidence still depends too much on assuming resume behavior across provider changes and longer windows rather than proving it.
- `ParquetStorageSinkTests` currently prove trade flush/dispose behavior, but do not yet prove equivalent L2 snapshot flush-path durability.
- `BackfillStatusStore` still uses general `JsonSerializerOptions` with `DefaultJsonTypeInfoResolver`; any Wave 1 ADR-014 cleanup that touches checkpoint/L2 persistence should converge on source-generated serialization instead of widening generic JSON use.

### Delivery shape

Wave 1 should be executed as eight tightly coupled tracks:

1. Polygon replay closure.
2. Robinhood execution-readiness closure.
3. IB runtime and bootstrap validation.
4. NYSE lifecycle-depth and transport hardening.
5. StockSharp validated-adapter closure.
6. Backfill checkpoint and gap-detection reliability.
7. Parquet L2 flush-path hardening and ADR-014 cleanup.
8. Evidence-document synchronization and exit-gate review.

---

## Interfaces and Models

### Core contracts to preserve

- [`src/Meridian.ProviderSdk/IMarketDataClient.cs`](../../src/Meridian.ProviderSdk/IMarketDataClient.cs)
- [`src/Meridian.ProviderSdk/IRealtimeDataSource.cs`](../../src/Meridian.ProviderSdk/IRealtimeDataSource.cs)
- [`src/Meridian.ProviderSdk/IHistoricalDataSource.cs`](../../src/Meridian.ProviderSdk/IHistoricalDataSource.cs)
- [`src/Meridian.Infrastructure/Adapters/Core/IHistoricalDataProvider.cs`](../../src/Meridian.Infrastructure/Adapters/Core/IHistoricalDataProvider.cs)
- [`src/Meridian.Storage/Interfaces/IStorageSink.cs`](../../src/Meridian.Storage/Interfaces/IStorageSink.cs)

### Wave-1-relevant models and services

- [`src/Meridian.Application/Backfill/BackfillRequest.cs`](../../src/Meridian.Application/Backfill/BackfillRequest.cs)
- [`src/Meridian.Application/Backfill/BackfillResult.cs`](../../src/Meridian.Application/Backfill/BackfillResult.cs)
- [`src/Meridian.Application/Backfill/BackfillStatusStore.cs`](../../src/Meridian.Application/Backfill/BackfillStatusStore.cs)
- [`src/Meridian.Contracts/Domain/Models/L2SnapshotPayload.cs`](../../src/Meridian.Contracts/Domain/Models/L2SnapshotPayload.cs)
- [`src/Meridian.Contracts/Domain/Models/DepthIntegrityEvent.cs`](../../src/Meridian.Contracts/Domain/Models/DepthIntegrityEvent.cs)

### Evidence surfaces to keep authoritative

- [`docs/providers/provider-confidence-baseline.md`](../providers/provider-confidence-baseline.md)
- [`docs/status/provider-validation-matrix.md`](../status/provider-validation-matrix.md)
- [`docs/status/production-status.md`](../status/production-status.md)
- `artifacts/provider-validation/<provider>/<yyyy-mm-dd>/`

The docs should summarize the gate result. The artifact path should carry raw logs, screenshots, session transcripts, or checklist evidence for runtime validations.

---

## Data Flow

### 1. Polygon replay closure

Use [`tests/Meridian.Tests/Infrastructure/Providers/Fixtures/Polygon/`](../../tests/Meridian.Tests/Infrastructure/Providers/Fixtures/Polygon/) and [`tests/Meridian.Tests/Infrastructure/Providers/PolygonRecordedSessionReplayTests.cs`](../../tests/Meridian.Tests/Infrastructure/Providers/PolygonRecordedSessionReplayTests.cs) as the authoritative replay seam.

Required additions:

- Extend replay fixtures to cover the remaining feed-shape and status-marker combinations Meridian wants to claim.
- Preserve the existing rule that replay tests only emit documented event types.
- Keep feed limitations explicit in docs instead of treating missing live depth or entitlement-dependent behavior as solved.

Done when:

- Polygon replay coverage is broader across feed variants and edge cases.
- The validation matrix shows exactly what is replay-proven versus runtime-bounded.

### 2. Robinhood execution-readiness closure

Use [`src/Meridian.Infrastructure/Adapters/Robinhood/RobinhoodBrokerageGateway.cs`](../../src/Meridian.Infrastructure/Adapters/Robinhood/RobinhoodBrokerageGateway.cs), [`src/Meridian.Infrastructure/Adapters/Robinhood/RobinhoodMarketDataClient.cs`](../../src/Meridian.Infrastructure/Adapters/Robinhood/RobinhoodMarketDataClient.cs), [`tests/Meridian.Tests/Infrastructure/Providers/RobinhoodBrokerageGatewayTests.cs`](../../tests/Meridian.Tests/Infrastructure/Providers/RobinhoodBrokerageGatewayTests.cs), and [`tests/Meridian.Tests/Ui/ExecutionGovernanceEndpointsTests.cs`](../../tests/Meridian.Tests/Ui/ExecutionGovernanceEndpointsTests.cs) as the baseline.

Required additions:

- Keep the stable execution seam, quote-polling path, historical daily bars, and symbol-search behavior aligned as one supported Robinhood surface instead of letting those claims drift independently.
- Capture runtime evidence for reconnect, cancellation, and throttling behavior from a real broker session because the adapter has no public websocket replay seam to substitute for that proof.
- Keep the unofficial API boundary explicit in docs and operator guidance so Wave 1 closes evidence gaps without overstating vendor-supported live readiness.

Done when:

- Robinhood's matrix row distinguishes clearly between stable-seam proof already in repo and remaining live-session proof still bounded by the unofficial broker surface.
- The provider baseline, validation matrix, and production-status docs all point at the same Robinhood evidence set.

### 3. Interactive Brokers runtime and bootstrap validation

Use [`docs/providers/interactive-brokers-setup.md`](../providers/interactive-brokers-setup.md), [`src/Meridian.Infrastructure/Adapters/InteractiveBrokers/IBApiVersionValidator.cs`](../../src/Meridian.Infrastructure/Adapters/InteractiveBrokers/IBApiVersionValidator.cs), and [`tests/Meridian.Tests/Infrastructure/Providers/IBRuntimeGuidanceTests.cs`](../../tests/Meridian.Tests/Infrastructure/Providers/IBRuntimeGuidanceTests.cs) as the baseline.

Required additions:

- Keep the three IB modes explicit: simulation/non-`IBAPI`, compile-only smoke path, and real vendor runtime.
- Validate the startup/bootstrap checklist against actual TWS or IB Gateway surfaces and capture the evidence as a runtime artifact.
- Keep version-window claims synchronized with `IBApiVersionValidator` and the setup guide.

Done when:

- The smoke path, setup guide, and runtime evidence all point at the same tested bootstrap rules.
- The validation matrix stops implying live readiness where only guidance or compile-only proof exists.

### 4. NYSE lifecycle-depth coverage and transport hardening

Use [`src/Meridian.Infrastructure/Adapters/NYSE/NYSEDataSource.cs`](../../src/Meridian.Infrastructure/Adapters/NYSE/NYSEDataSource.cs), [`tests/Meridian.Tests/Infrastructure/Providers/NyseSharedLifecycleTests.cs`](../../tests/Meridian.Tests/Infrastructure/Providers/NyseSharedLifecycleTests.cs), and [`tests/Meridian.Tests/Infrastructure/Providers/NyseTaqCollectorIntegrationTests.cs`](../../tests/Meridian.Tests/Infrastructure/Providers/NyseTaqCollectorIntegrationTests.cs).

Required additions:

- Expand shared-lifecycle tests around trade, quote, and depth coexistence, duplicate subscribe suppression, unsubscribe behavior, and reconnect resubscription.
- Add explicit auth-failure, rate-limit, and cancellation-path assertions where the matrix still shows partial or missing proof.
- Keep HTTP usage aligned with `IHttpClientFactory` and make websocket send or resubscribe work abort cleanly on disconnect and shutdown.

Done when:

- NYSE depth and shared-lifecycle behavior are represented by executable evidence rather than implied by code shape.
- Disconnect and reconnect paths do not leave orphaned send or resubscribe work behind.

### 5. StockSharp connector examples and validated adapters

Use [`docs/providers/stocksharp-connectors.md`](../providers/stocksharp-connectors.md), [`src/Meridian.Infrastructure/Adapters/StockSharp/StockSharpConnectorFactory.cs`](../../src/Meridian.Infrastructure/Adapters/StockSharp/StockSharpConnectorFactory.cs), and [`tests/Meridian.Tests/Infrastructure/Providers/StockSharpConnectorFactoryTests.cs`](../../tests/Meridian.Tests/Infrastructure/Providers/StockSharpConnectorFactoryTests.cs).

Required additions:

- Keep doc examples aligned with the named connectors and capability surfaces Meridian actually supports.
- Treat `Rithmic`, `IQFeed`, `CQG`, and `InteractiveBrokers` as the validated-adapter baseline for this wave.
- Keep crypto connectors documented as example or optional paths unless package/runtime access is explicitly available and evidenced.
- Strengthen converter edge-case coverage where supported connectors have materially different message shapes.

Done when:

- Meridian can point to a specific validated adapter set instead of a generic StockSharp umbrella claim.
- Example config and factory capabilities are synchronized.

### 6. Backfill checkpoint reliability and gap detection

Use [`src/Meridian.Application/Backfill/BackfillStatusStore.cs`](../../src/Meridian.Application/Backfill/BackfillStatusStore.cs), [`tests/Meridian.Tests/Application/Backfill/BackfillStatusStoreTests.cs`](../../tests/Meridian.Tests/Application/Backfill/BackfillStatusStoreTests.cs), [`tests/Meridian.Tests/Application/Backfill/GapBackfillServiceTests.cs`](../../tests/Meridian.Tests/Application/Backfill/GapBackfillServiceTests.cs), and [`tests/Meridian.Tests/Application/Services/DataQuality/GapAnalyzerTests.cs`](../../tests/Meridian.Tests/Application/Services/DataQuality/GapAnalyzerTests.cs).

Required additions:

- Prove resume semantics across partial-success runs, longer ranges, overlapping ranges, and representative providers.
- Tie checkpoint evidence to both the backfill application services and the checkpoint endpoints, not only to storage round-trips.
- Keep gap-detection behavior explicit for operator review so the same evidence supports both automation and diagnostics.

Done when:

- Checkpoint and gap claims are backed by representative provider and date-window evidence instead of assumptions.
- The matrix and roadmap can state exactly which ranges and restart cases were validated.

### 7. Parquet flush-path hardening and ADR-014 cleanup for L2 snapshot persistence

Use [`src/Meridian.Storage/Sinks/ParquetStorageSink.cs`](../../src/Meridian.Storage/Sinks/ParquetStorageSink.cs) and [`tests/Meridian.Tests/Storage/ParquetStorageSinkTests.cs`](../../tests/Meridian.Tests/Storage/ParquetStorageSinkTests.cs).

Required additions:

- Extend the storage sink tests beyond trade-only coverage to L2 snapshot flush, final-dispose flush, and temp-file cleanup.
- Keep `BidsJson` and `AsksJson` on the ADR-014 path and remove any remaining generic JSON usage that still participates in L2 snapshot persistence or closely coupled checkpoint evidence.
- Confirm cancellation and failure behavior do not silently drop buffered depth snapshots.

Done when:

- L2 snapshot persistence has executable flush-path evidence.
- No Wave 1 storage doc implies guarantees that only exist for trade events.

### 8. Evidence synchronization and exit-gate review

Use [`docs/providers/provider-confidence-baseline.md`](../providers/provider-confidence-baseline.md), [`docs/status/provider-validation-matrix.md`](../status/provider-validation-matrix.md), [`docs/status/ROADMAP.md`](../status/ROADMAP.md), and [`docs/status/production-status.md`](../status/production-status.md).

Required additions:

- Keep status docs synchronized in the same change set whenever provider evidence claims move.
- Archive raw runtime artifacts under `artifacts/provider-validation/`.
- Review every remaining `⚠️` or `❌` row as either closed by evidence or explicitly bounded by entitlement, vendor, or package availability.

Done when:

- Every major provider has documented replay or runtime evidence.
- Every supported validation suite passes.
- Remaining vendor-entitlement limits are documented as bounds, not hidden behind implied readiness language.

---

## Edge Cases and Risks

- Polygon status frames, delayed plans, and entitlement-dependent feeds can easily look "validated" unless the docs keep replay proof separate from live-vendor proof.
- Robinhood can look more live-validated than it really is if stable execution tests and polling-path unit coverage are allowed to stand in for reconnect or throttling evidence from a real broker session.
- IB version-window drift will cause misleading setup guidance if `IBApiVersionValidator`, smoke-build instructions, and runtime logs are not updated together.
- NYSE reconnect behavior is easy to overstate because lifecycle tests and transport shutdown behavior are related but not identical proof.
- StockSharp can look broader than it really is if named connectors in docs outrun the packages Meridian actually references.
- Backfill confidence will remain shallow if tests only prove file persistence and not cross-provider resume semantics.
- Parquet storage can appear hardened while still lacking L2-specific flush evidence if tests remain trade-only.

---

## Test Plan

Run the narrowest suites that match the touched slice:

```bash
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~PolygonRecordedSessionReplayTests|FullyQualifiedName~PolygonMessageParsingTests|FullyQualifiedName~PolygonSubscriptionTests|FullyQualifiedName~PolygonMarketDataClientTests"

dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~RobinhoodBrokerageGatewayTests|FullyQualifiedName~RobinhoodMarketDataClientTests|FullyQualifiedName~RobinhoodHistoricalDataProviderTests|FullyQualifiedName~RobinhoodSymbolSearchProviderTests|FullyQualifiedName~RobinhoodExecutionPath_SubmitsOrderThroughStableExecutionSeam"

dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~NyseSharedLifecycleTests|FullyQualifiedName~NyseMarketDataClientTests|FullyQualifiedName~NYSEMessageParsingTests|FullyQualifiedName~NyseTaqCollectorIntegrationTests"

dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~IBRuntimeGuidanceTests|FullyQualifiedName~IBOrderSampleTests|FullyQualifiedName~IBHistoricalProviderContractTests|FullyQualifiedName~IBMarketDataClientContractTests"

./scripts/dev/build-ibapi-smoke.ps1

dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~StockSharpSubscriptionTests|FullyQualifiedName~StockSharpMessageConversionTests|FullyQualifiedName~StockSharpConverterEdgeCaseTests|FullyQualifiedName~StockSharpConnectorFactoryTests"

dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~BackfillStatusStoreTests|FullyQualifiedName~GapBackfillServiceTests|FullyQualifiedName~GapAnalyzerTests|FullyQualifiedName~CheckpointEndpointTests"

dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~ParquetStorageSinkTests|FullyQualifiedName~ParquetConversionServiceTests"
```

Manual or runtime evidence should additionally capture:

- Polygon live-path notes when plan-tier or entitlement behavior matters.
- Robinhood broker-session notes for quote polling, token/auth handling, cancellation, reconnect behavior, and throttling boundaries.
- IB TWS or Gateway bootstrap logs, server-version validation output, and entitlement notes.
- NYSE auth/connectivity and depth entitlement evidence where available.
- StockSharp validated-adapter runtime notes for the selected baseline connectors.

---

## Open Questions

- Should NYSE premium depth be mandatory for the Wave 1 exit gate, or acceptable as an explicitly bounded entitlement-dependent path?
- For Robinhood, is one current broker-session artifact per major flow enough for Wave 1, or do we want separate runtime evidence for quotes, brokerage submit/cancel, and throttling?
- Do we want runtime evidence committed directly under `artifacts/provider-validation/`, or summarized there with larger logs kept outside the repo and referenced by path?
- Is one validated runtime artifact per StockSharp baseline adapter sufficient, or does this wave require both historical and streaming evidence for each validated adapter?
