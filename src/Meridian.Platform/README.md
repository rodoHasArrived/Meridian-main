---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-DESIGN-PLATFORM
path: src/Meridian.Platform
status: active
owner_lane: Runtime Host
last_reviewed: 2026-06-06
---

# src/Meridian.Platform

## Purpose

Physical bounded-context module project for Platform ownership, composition, configuration, runtime
policy, runtime mode resolution, operational performance controls, operational scheduling and
trading-calendar utilities, shared operation result semantics, diagnostic state, domain cutover,
and shadow-projection conformance.

## Layer responsibility

This module belongs to the Design Module layer. Keep changes within that ownership boundary and update the registry if the boundary changes.

## Key folders and files

- `FundOperationsPersistence/FundOperationsPersistenceContracts.cs` - domain cutover mode,
  read-mode, discrepancy, reconciliation-job, and shadow-projection writer contracts.
- `FundOperationsPersistence/CanonicalProjectionSchemas.cs` - canonical shadow-projection schemas
  for fund structure, fund accounts, direct lending, banking, and money-market domains.
- `FundOperationsPersistence/FileShadowProjectionWriter.cs` - sanitized file-backed shadow
  projection writer for platform cutover validation.
- `FundOperationsPersistence/DomainReadSwitch.cs` - read-mode selector used during
  persisted-projection cutovers.
- `FundOperationsPersistence/ProjectionReconciliationHostedService.cs` - hosted reconciliation
  loop for domain projection discrepancies.
- `Results/` - shared `Result`, `OperationError`, `ErrorCode`, and friendly error-formatting
  primitives used by commands, startup validation, diagnostics endpoints, and other cross-domain
  runtime flows.
- `Diagnostics/` - process-level error tracking, shutdown-sequence diagnostic snapshots, and
  shutdown lifecycle DTOs consumed by Application orchestration and shared endpoint projections.
- `Runtime/` - shared CLI/runtime mode policy and console progress display helpers used by
  deployment context, startup summaries, and diagnostics/connectivity workflows.
- `Performance/CoLocationProfileActivator.cs` - platform-owned runtime performance profile
  activation for the desktop diagnostics surface and latency-sensitive host startup flows.
- `Scheduling/OperationalScheduler.cs` - trading-hours-aware runtime scheduling policy for
  maintenance, backfill, reporting, health, credential refresh, and other operational work.
- `Scheduling/TradingCalendar.cs` - US-market trading-day, holiday, half-day, session-status, and
  `ITradingCalendarProvider` implementation used by runtime scheduling and calendar endpoints.
- `Tracing/EventTraceContext.cs` - cross-thread activity context capture used by the Application
  event pipeline to preserve trace parent/correlation IDs across queued market events.
- `Tracing/OpenTelemetrySetup.cs` - host OpenTelemetry setup, exporter configuration, and
  `MarketDataTracing` activity source/counter helpers for platform-level market-data telemetry.
- `Tracing/TracedEventMetrics.cs` - OpenTelemetry-compatible event-pipeline metrics decorator
  that preserves the shared contracts-owned metrics shape while emitting platform telemetry.
- `ApiDocumentation/ApiDocumentationService.cs` - lightweight OpenAPI, Swagger HTML, and
  markdown documentation model generation for host diagnostics and API explorer surfaces.

## Important workflows

Use this module when changing cross-domain runtime cutover controls, shadow-write behavior,
persisted-projection read switching, hosted projection-reconciliation plumbing, or shared
command/startup result semantics. Application composition may register these services and consume
Platform diagnostics, trace context carriers, and metrics decorators, but it should not own the platform cutover
contracts, shadow-projection schemas, trace context carriers, OpenTelemetry setup, market-data
tracing helpers, OpenTelemetry-compatible event metrics decoration, trading-hours-aware operational
scheduling policy, trading-calendar implementation, runtime mode resolution, colocation profile
activation, diagnostic state snapshots, shutdown lifecycle DTOs, API documentation model
generation, or shared error-code taxonomy.

## Diagrams

`DIA-ASSURANCE-LOOP`

## Roadmap traceability

<!-- source-roadmap-traceability:begin module=SRC-DESIGN-PLATFORM -->
| Roadmap item | Title |
| --- | --- |
| `W1-DATA-001` | Provider trust gate and data confidence baseline |
| `W5-ACCT-001` | Accounting records and operational evidence |
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module=SRC-DESIGN-PLATFORM -->
- No registry-backed TODOs are open for this module.
<!-- source-todos:end -->

## Validation

```bash
dotnet build src/Meridian.Platform/Meridian.Platform.csproj /p:EnableWindowsTargeting=true
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter FullyQualifiedName~StorageFeatureRegistrationTests --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true /p:NodeReuse=false
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~ErrorCodeMappingTests|FullyQualifiedName~DiagnosticsCommandsTests|FullyQualifiedName~StatementImportCommandsTests|FullyQualifiedName~SimulationCommandsTests|FullyQualifiedName~SecurityMasterCommandsEdgarTests" --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true /p:NodeReuse=false
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~EventTraceContextTests" --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true /p:NodeReuse=false
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~EventPipelineTracePropagationTests|FullyQualifiedName~EventTraceContextTests|FullyQualifiedName~TracedEventMetricsTests" --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true /p:NodeReuse=false
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~TracedEventMetricsTests|FullyQualifiedName~EventPipelineMetricsTests|FullyQualifiedName~DiagnosticsEndpointsTests|FullyQualifiedName~DiagnosticsFeatureRegistrationTests|FullyQualifiedName~DiagnosticBundleServiceTests" --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true /p:NodeReuse=false
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~TradingCalendarTests|FullyQualifiedName~OperationalSchedulerTests" --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true /p:NodeReuse=false
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~ApiDocumentationServiceTests" --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true /p:NodeReuse=false
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~CoLocationProfileActivatorTests" --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true /p:NodeReuse=false
```

## Optional conditional sections

Add only the sections that apply to this module:

- `### Plans and roadmap`
- `### End-user value`
- `### Benchmarks and performance`
- `### Operational evidence`
- `### Security and credentials`
- `### API and contract notes`
- `### Migration and archive notes`

## Change rules

Preserve the module boundary declared in `docs/source/data/source-modules.yml` and update the
nearest docs when platform cutover, configuration, runtime policy, operational scheduling,
trading-calendar behavior, runtime mode resolution, result/error semantics, runtime diagnostics,
shutdown lifecycle DTOs, API documentation model generation, runtime performance profile behavior,
shadow-projection, trace context propagation, or hosted reconciliation workflow semantics change.

## Related docs

- `docs/source/README.md`
- `docs/source/generated/source-module-index.md`
- `docs/architecture/module-map.md`
