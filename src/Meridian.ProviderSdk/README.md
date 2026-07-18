---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-PROVIDER-SDK
path: src/Meridian.ProviderSdk
status: active
owner_lane: Data Confidence and Validation
last_reviewed: 2026-07-15
---

# src/Meridian.ProviderSdk

## Purpose

ProviderSdk defines provider-facing abstractions for streaming, historical, symbol-search, backfill
job descriptors, dynamic provider plugin discovery, and accounting-system integrations.

## Layer responsibility

This layer is the plugin contract for provider adapters. It should expose stable interfaces without owning concrete provider implementation details.

## Key folders and files

- `Backfill/` - provider-facing backfill job, status, priority, progress, and granularity
  descriptors consumed by Infrastructure workers, Application scheduling, and UI adapters.
- Provider-facing interfaces such as market data client and accounting-system contracts.
- Shared provider metadata, attribute types, module loading, and plugin assembly discovery.

## Important workflows

Use this module when a provider abstraction must be consumed by multiple adapters or higher-level services.
`IMarketDataClient` inherits `IProviderConnectionDiagnosticsSource`, making a safe connection
snapshot a contract-level expectation for every streaming provider. The default implementation is
compatibility-preserving and conservative: enabled adapters report `Configured`, disabled adapters
report `Disabled`, `WebSocketState` is `None`, and no live connection is inferred. Adapters with a
connection supervisor should override the default event and snapshot with proven runtime state.
The retained `WebSocketConnectionDiagnostics` name is transport-compatible; polling and raw-socket
providers use `WebSocketState.None`.
`Backfill/BackfillJob.cs` owns the shared backfill job descriptors and `DataGranularity`
conversion helpers.
`PluginLoaderService` owns non-recursive provider plugin assembly scanning and registration against
`DataSourceRegistry` under the `Meridian.ProviderSdk` namespace; WPF and host composition consume that ProviderSdk-owned loader rather than
keeping reflection-based provider discovery in Application.
Provider routing capabilities are contract-level workflow gates; `FactorSchedule` is distinct from
generic `CorporateActions` so accounting workflows can degrade fixed-income, structured-credit,
amortization, and paydown evidence when a provider cannot route the required factor/coupon feed.
`AccountingSystem/IAccountingSystemProvider.cs` defines the provider-neutral GL import surface for
chart-of-accounts, journal-entry, trial-balance, and reconciliation-preview evidence. It is
read-oriented by default; write/posting support must be exposed explicitly by provider capabilities
before any service or client can offer export actions.

## Diagrams

See `DIA-ASSURANCE-LOOP` in `docs/source/data/diagram-index.yml`.

## Roadmap traceability

<!-- source-roadmap-traceability:begin module=SRC-PROVIDER-SDK -->
| Roadmap item | Title |
| --- | --- |
| `W1-DATA-001` | Provider trust gate and data confidence baseline |
| `W7-LIVE-001` | Live-readiness governance |
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module=SRC-PROVIDER-SDK -->
- No registry-backed TODOs are open for this module.
<!-- source-todos:end -->

## Validation

```bash
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "Category!=Integration" --logger "console;verbosity=normal"
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~PluginLoaderServiceTests|FullyQualifiedName~ProviderModuleLoaderTests|FullyQualifiedName~DataSourceRegistryTests" --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true /p:NodeReuse=false
```

## Change rules

Keep provider abstractions stable and implementation-neutral. Concrete adapters belong in Infrastructure.

## Related docs

- `docs/providers/`
- `docs/architecture/module-map.md`
- `docs/source/generated/source-module-index.md`
