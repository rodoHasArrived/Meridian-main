# Architectural Decision Records (ADRs)

This directory contains Architectural Decision Records documenting significant technical decisions made in the Meridian project.

## What is an ADR?

An ADR is a document that captures an important architectural decision along with its context and consequences. Each ADR includes links to the actual code implementations, making it easy to verify that code matches documented decisions.

## ADR Index

| ID | Title | Status | Key Components |
|----|-------|--------|----------------|
| [ADR-001](001-provider-abstraction.md) | Provider Abstraction Pattern | Accepted | `IMarketDataClient`, `IHistoricalDataProvider` |
| [ADR-002](002-tiered-storage-architecture.md) | Tiered Storage Architecture | Accepted | `IStorageSink`, `TierMigrationService` |
| [ADR-003](003-microservices-decomposition.md) | Microservices Decomposition | Rejected | Monolith preferred |
| [ADR-004](004-async-streaming-patterns.md) | Async Streaming Patterns | Accepted | `IAsyncEnumerable<T>`, `Channel<T>` |
| [ADR-005](005-attribute-based-discovery.md) | Attribute-Based Provider Discovery | Accepted | `DataSourceAttribute`, `DataSourceRegistry` |
| [ADR-006](006-domain-events-polymorphic-payload.md) | Domain Events Polymorphic Payload | Accepted | `MarketEvent`, `IMarketEventPayload` |
| [ADR-007](007-write-ahead-log-durability.md) | Write-Ahead Log Durability | Accepted | `WriteAheadLog`, `EventPipeline` |
| [ADR-008](008-multi-format-composite-storage.md) | Multi-Format Composite Storage | Accepted | `CompositeSink`, `IStorageSink` |
| [ADR-009](009-fsharp-interop.md) | F# Type-Safe Domain with C# Interop | Accepted | `Meridian.FSharp`, `Interop.fs` |
| [ADR-010](010-httpclient-factory.md) | HttpClientFactory Lifecycle | Accepted | `HttpClientConfiguration`, `HttpClientNames` |
| [ADR-011](011-centralized-configuration-and-credentials.md) | Centralized Configuration & Credentials | Accepted | `IConfigurationProvider`, `ICredentialStore` |
| [ADR-012](012-monitoring-and-alerting-pipeline.md) | Unified Monitoring & Alerting | Accepted | `IHealthCheckProvider`, `IAlertDispatcher` |
| [ADR-013](013-bounded-channel-policy.md) | Bounded Channel Pipeline Policy | Accepted | `EventPipelinePolicy` |
| [ADR-014](014-json-source-generators.md) | JSON Source Generators | Accepted | `MarketDataJsonContext` |
| [ADR-015](015-strategy-execution-contract.md) | Strategy Execution Contract | Accepted | `IOrderGateway`, `IExecutionContext` |
<<<<<<< HEAD
| [ADR-016](016-platform-architecture-migration.md) | Platform Architecture Migration Mandate | Accepted | `Meridian.Execution`, `Meridian.Risk`, `Meridian.Strategies`, `Meridian.QuantScript` |
| [ADR-015-platform-restructuring](../../archive/docs/migrations/ADR-015-platform-restructuring.md) | Platform Restructuring (historical) | **Superseded** | Archived in migration docs; superseded by ADR-015 and ADR-016 |
=======
| [ADR-016](016-platform-architecture-migration.md) | Platform Architecture Migration Mandate | Accepted | `Meridian.Execution`, `Meridian.Strategies`, `StrategyRunStore` |
>>>>>>> b39663640d8410b70232c5008f8860a1e82d5cbe

## ADR Dependencies

ADRs build on each other. The diagram below shows key relationships:

```
ADR-001 Provider Abstraction
  ├─→ ADR-005 Attribute-Based Discovery (auto-registers providers)
  ├─→ ADR-010 HttpClientFactory (providers use named HTTP clients)
  └─→ ADR-011 Configuration & Credentials (providers consume config/creds)

ADR-002 Tiered Storage
  ├─→ ADR-007 Write-Ahead Log (crash-safe hot-tier writes)
  └─→ ADR-008 Composite Storage (fan-out to JSONL + Parquet)

ADR-003 Monolith Decision
  └─→ ADR-004 Async Streaming (in-process channels over network RPC)
      └─→ ADR-013 Bounded Channel Policy (standardized channel config)

ADR-006 Domain Events
  └─→ ADR-009 F# Interop (F# discriminated unions map to C# factories)
  └─→ ADR-014 JSON Source Generators (events serialized via source gen)

ADR-012 Monitoring & Alerting
  ├─→ ADR-011 Configuration (validation feeds health checks)
  └─→ ADR-013 Bounded Channel Policy (backpressure feeds alerts)
```

## ADR Lifecycle

1. **Proposed** - Under discussion
2. **Accepted** - Approved and implemented
3. **Deprecated** - No longer recommended
4. **Superseded** - Replaced by another ADR

## Creating a New ADR

Use the template at [_template.md](_template.md) to create new ADRs. Number sequentially (next: ADR-015).

## Verification

ADRs include implementation links that are verified during the build process. Run:

```bash
make verify-adrs
```

This ensures documented decisions remain in sync with actual code.

---

<<<<<<< HEAD
*Last Updated: 2026-04-07*
=======
*Last Updated: 2026-02-20*
>>>>>>> b39663640d8410b70232c5008f8860a1e82d5cbe
