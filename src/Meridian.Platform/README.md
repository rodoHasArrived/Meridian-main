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
policy, deployment context, runtime mode resolution, startup summary display, operational
performance controls, hosted graceful-shutdown flush behavior, shutdown sequence handling,
operational scheduling and trading-calendar utilities, cluster lease management, coordinator
election, split-brain detection, scheduled-work ownership, subscription ownership, shared operation
result semantics, diagnostic state, diagnostic bundle generation, runtime alert dispatch, health
aggregation, SLO and alert-runbook registries, runtime error ring-buffer retention, system-health
resource snapshots, default event-pipeline metrics counters, domain cutover, and shadow-projection
conformance.

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
- `Coordination/` - platform-owned lease renewal, cluster coordinator election, split-brain
  detection, scheduled-work ownership, and subscription ownership runtime services that implement
  the contracts-owned coordination ports.
- `Results/` - shared `Result`, `OperationError`, `ErrorCode`, and friendly error-formatting
  primitives used by commands, startup validation, diagnostics endpoints, and other cross-domain
  runtime flows.
- `Diagnostics/` - process-level error tracking, runtime error ring-buffer retention,
  system-health resource snapshots, diagnostic bundle generation, shutdown-sequence diagnostic
  snapshots, and shutdown lifecycle DTOs consumed by Application orchestration and shared endpoint
  projections.
- `Monitoring/CircuitBreakerStatusService.cs` - runtime circuit-breaker state dashboard,
  transition events, and health snapshot records consumed by Application composition and shared
  resilience endpoints.
- `Monitoring/BackpressureAlertService.cs` - runtime pipeline backpressure detection and alert
  publishing over the Contracts-owned pipeline statistics DTO and monitoring webhook sink contract.
- `Monitoring/Core/` - runtime alert dispatcher, health-check aggregator, SLO registry, and
  alert-to-runbook registry implementations over the Core-owned monitoring contracts.
- `Runtime/` - shared deployment context, CLI/runtime mode policy, startup summary display,
  hosted graceful-shutdown flush behavior, shutdown sequence handling, and console progress display
  helpers used by startup summaries and diagnostics/connectivity workflows.
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
- `Tracing/Metrics.cs` and `Tracing/DefaultEventMetrics.cs` - hot-path event-pipeline counters
  and the default implementation of the contracts-owned `IEventMetrics` interface.
- `Tracing/TracedEventMetrics.cs` - OpenTelemetry-compatible event-pipeline metrics decorator
  that wraps the default metrics implementation while emitting platform telemetry.
- `ApiDocumentation/ApiDocumentationService.cs` - lightweight OpenAPI, Swagger HTML, and
  markdown documentation model generation for host diagnostics and API explorer surfaces.

## Important workflows

Use this module when changing cross-domain runtime cutover controls, shadow-write behavior,
persisted-projection read switching, hosted projection-reconciliation plumbing, or shared
command/startup result semantics. Application composition may register these services and consume
Platform diagnostics, monitoring core services, trace context carriers, event metrics counters, and metrics decorators, but it should not own the platform cutover
contracts, shadow-projection schemas, cluster lease renewal, coordinator election, split-brain
detection, scheduled-work ownership, subscription ownership, runtime alert dispatch, health
aggregation, SLO/runbook registries, trace context carriers, OpenTelemetry setup, market-data
tracing helpers, default event metrics counters, OpenTelemetry-compatible event metrics decoration, trading-hours-aware operational
scheduling policy, trading-calendar implementation, deployment context, runtime mode resolution,
startup summary display, hosted graceful-shutdown flush behavior, shutdown sequence handling, colocation profile activation,
diagnostic state snapshots, diagnostic bundle generation, shutdown lifecycle DTOs, API documentation model generation, runtime circuit-breaker status dashboards,
runtime error ring buffers, or shared system-health resource snapshots, or shared error-code
taxonomy.

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
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~EventPipelineTracePropagationTests|FullyQualifiedName~EventTraceContextTests|FullyQualifiedName~DefaultEventMetricsTests|FullyQualifiedName~TracedEventMetricsTests" --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true /p:NodeReuse=false
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~DefaultEventMetricsTests|FullyQualifiedName~TracedEventMetricsTests|FullyQualifiedName~EventPipelineMetricsTests|FullyQualifiedName~DiagnosticsEndpointsTests|FullyQualifiedName~DiagnosticsFeatureRegistrationTests|FullyQualifiedName~DiagnosticBundleServiceTests" --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true /p:NodeReuse=false
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~TradingCalendarTests|FullyQualifiedName~OperationalSchedulerTests" --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true /p:NodeReuse=false
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~ApiDocumentationServiceTests" --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true /p:NodeReuse=false
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~CoLocationProfileActivatorTests" --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true /p:NodeReuse=false
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~CircuitBreakerStatusServiceTests" --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true /p:NodeReuse=false
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~ErrorRingBufferTests" --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true /p:NodeReuse=false
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~SystemHealthCheckerTests" --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true /p:NodeReuse=false
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
trading-calendar behavior, runtime mode resolution, startup summary behavior, result/error semantics, runtime diagnostics, diagnostic bundle generation,
deployment context behavior, hosted graceful-shutdown flush behavior, shutdown sequence handling, shutdown lifecycle DTOs,
API documentation model generation, runtime performance profile behavior, runtime circuit-breaker status behavior, runtime error ring-buffer
behavior, default event metrics counter behavior, cluster coordination behavior, runtime monitoring
core behavior, shadow-projection, system-health resource snapshot behavior, trace context propagation, or hosted reconciliation
workflow semantics change.

## Related docs

- `docs/source/README.md`
- `docs/source/generated/source-module-index.md`
- `docs/architecture/module-map.md`
