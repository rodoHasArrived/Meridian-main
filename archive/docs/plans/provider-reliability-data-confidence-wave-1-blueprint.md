# Provider Reliability and Data Confidence Wave 1 Blueprint

**Owner:** Core Team  
**Audience:** Infrastructure, storage, provider, QA, and operations contributors  
**Last Updated:** 2026-04-27
**Status:** Active blueprint for the Wave 1 operator-trust gate

---

## Summary

Wave 1 closes the smallest provider-confidence gate Meridian can defend today without overstating live-readiness. The active provider set is:

- `Alpaca`
- `Robinhood`
- `Yahoo`

`Polygon`, `Interactive Brokers`, and `NYSE` are deferred from the active gate for now. `StockSharp` remains outside the Wave 1 core gate as reference and future validation inventory.

The other active Wave 1 closures remain checkpoint reliability and Parquet Level 2 flush behavior. This blueprint keeps those proof points synchronized across tests, docs, and validation automation.

---

## Scope

### In scope

- Alpaca evidence formalization as an active core provider row.
- Robinhood supported-surface evidence plus explicit bounded runtime references.
- Yahoo historical-only core provider confidence.
- Backfill checkpoint reliability and gap-detection evidence.
- Parquet sink proof already used by the trust gate.
- Synchronizing the baseline, matrix, roadmap, production status, and validation script.

### Deferred from the active gate

- `Polygon`
- `Interactive Brokers`
- `NYSE`
- `StockSharp`

### Assumptions

- Offline and CI-friendly evidence remains the mandatory baseline for this wave.
- Robinhood is the only active provider row that still requires bounded runtime evidence.
- Yahoo is core only for historical and fallback confidence, not for live runtime validation.
- Deferred providers remain part of Meridian's broader strategy, but not this wave's closure target.

---

## Current Grounded State

### Alpaca

Authoritative repo evidence already exists through:

- `AlpacaBrokerageGatewayTests`
- `AlpacaCorporateActionProviderTests`
- `AlpacaCredentialAndReconnectTests`
- `AlpacaMessageParsingTests`
- `AlpacaQuotePipelineGoldenTests`
- `AlpacaQuoteRoutingTests`
- `ExecutionGovernanceEndpointsTests.AlpacaExecutionPath_SubmitsOrderThroughStableExecutionSeam`

### Robinhood

Authoritative repo evidence exists through:

- `RobinhoodBrokerageGatewayTests`
- `RobinhoodMarketDataClientTests`
- `RobinhoodHistoricalDataProviderTests`
- `RobinhoodSymbolSearchProviderTests`
- `ExecutionGovernanceEndpointsTests.RobinhoodExecutionPath_SubmitsOrderThroughStableExecutionSeam`

Bounded runtime confidence still requires review-run evidence for `auth-session`, `quote-polling`, `order-submit-cancel`, and `throttling-reconnect`. The earlier `artifacts/provider-validation/robinhood/2026-04-09/` packet is no longer retained in the repo, so DK1 review must regenerate or attach the runtime packet for the review date.

### Yahoo

The active deterministic Yahoo evidence is:

- `YahooFinanceHistoricalDataProviderTests`
- `YahooFinanceIntradayContractTests`

Yahoo remains a historical-only and fallback provider row for Wave 1.

### Checkpoint and gap reliability

The active checkpoint proof surfaces are:

- `BackfillStatusStoreTests`
- `ParallelBackfillServiceTests`
- `GapBackfillServiceTests`
- `CheckpointEndpointTests`

### Parquet flush behavior

The active Parquet proof surfaces are:

- `ParquetStorageSinkTests`
- `ParquetConversionServiceTests`

---

## Active Gaps This Wave Must Close

- Keep the authoritative docs and validation script describing the same active provider set.
- Keep Alpaca's checked-in provider and execution-seam evidence explicit in the baseline and matrix instead of letting broader provider inventory overshadow it.
- Keep Yahoo formalized as an active historical-only core provider row in the authoritative Wave 1 docs.
- Robinhood's bounded runtime language needs to stay explicit and consistent instead of drifting into broader live-readiness claims.
- Checkpoint confidence should explicitly prove overlapping ranges, longer ranges, provider-tagged resume behavior, and endpoint-facing pending-symbol semantics.

---

## Delivery Shape

Wave 1 should be executed as five coupled tracks:

1. Alpaca evidence formalization.
2. Robinhood bounded runtime evidence alignment.
3. Yahoo historical-only core formalization.
4. Checkpoint and gap proof hardening.
5. Evidence-doc and validation-script synchronization.

---

## Interfaces and Models

### Core contracts to preserve

- `src/Meridian.ProviderSdk/IMarketDataClient.cs`
- `src/Meridian.Infrastructure/Adapters/Core/IHistoricalDataProvider.cs`
- `src/Meridian.Storage/Interfaces/IStorageSink.cs`

### Wave-1-relevant models and services

- `src/Meridian.Application/Backfill/BackfillRequest.cs`
- `src/Meridian.Application/Backfill/BackfillResult.cs`
- `src/Meridian.Application/Backfill/BackfillStatusStore.cs`

No public contracts need to change for this wave.

---

## Evidence Surfaces To Keep Authoritative

- `docs/providers/provider-confidence-baseline.md`
- `docs/status/provider-validation-matrix.md`
- `docs/status/production-status.md`
- `docs/status/ROADMAP.md`
- `scripts/dev/run-wave1-provider-validation.ps1`
- generated or attached `artifacts/provider-validation/` runtime outputs for bounded Robinhood scenarios

The docs summarize the gate result. The script reproduces the offline gate. Robinhood runtime packets are generated or attached evidence for the review run, not retained roadmap source.

---

## Test Plan

Run the narrowest suites that match the active Wave 1 gate:

```powershell
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~AlpacaBrokerageGatewayTests|FullyQualifiedName~AlpacaCorporateActionProviderTests|FullyQualifiedName~AlpacaCredentialAndReconnectTests|FullyQualifiedName~AlpacaMessageParsingTests|FullyQualifiedName~AlpacaQuotePipelineGoldenTests|FullyQualifiedName~AlpacaQuoteRoutingTests|FullyQualifiedName~AlpacaExecutionPath_SubmitsOrderThroughStableExecutionSeam"

dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~RobinhoodBrokerageGatewayTests|FullyQualifiedName~RobinhoodMarketDataClientTests|FullyQualifiedName~RobinhoodHistoricalDataProviderTests|FullyQualifiedName~RobinhoodSymbolSearchProviderTests|FullyQualifiedName~RobinhoodExecutionPath_SubmitsOrderThroughStableExecutionSeam"

dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~YahooFinanceHistoricalDataProviderTests|FullyQualifiedName~YahooFinanceIntradayContractTests"

dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~BackfillStatusStoreTests|FullyQualifiedName~ParallelBackfillServiceTests|FullyQualifiedName~GapBackfillServiceTests|FullyQualifiedName~CheckpointEndpointTests"

dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~ParquetStorageSinkTests|FullyQualifiedName~ParquetConversionServiceTests"

./scripts/dev/run-wave1-provider-validation.ps1
```

---

## Exit Signal

Wave 1 is ready to describe as closed when:

- the authoritative docs all describe the same active provider set: `Alpaca`, `Robinhood`, `Yahoo`
- Alpaca and Yahoo are closed by repo-backed evidence
- Robinhood is the only active provider row that remains explicitly bounded
- checkpoint reliability and Parquet L2 flush behavior remain closed in repo tests
- deferred providers stay clearly deferred and are not implied as active blockers
